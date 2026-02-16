using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Procedurally furnishes rooms with props and applies materials.
/// Called by ProceduralBuildingGenerator after geometry is instantiated.
///
/// ── How placement works ───────────────────────────────────────────────────
/// Floor props:
///   Items with prefersWall=true try each of the 4 room sides in random order.
///   If a wall side has enough clear space the item is placed against it.
///   Remaining items are placed at random clear positions inside the room.
///   All placed items are tracked as XZ AABBs — no overlaps.
///
/// Wall props (paintings):
///   Uses the BuildingWall list directly to find walls that border the room.
///   Places the prop at a random position along the wall at wallPropHeight.
///
/// Ceiling props (lamps):
///   Small rooms get one at centre. Large rooms get one per quadrant.
///
/// Materials:
///   Floor and ceiling materials applied directly from RoomMaterialProfile.
///   Wall materials applied after all rooms are processed using priority order.
/// </summary>
public class PropSpawner : MonoBehaviour
{
    [Header("Profiles")]
    [Tooltip("One RoomFurnishingProfile per RoomType. Missing types get no props.")]
    public RoomFurnishingProfile[] furnishingProfiles;

    [Tooltip("One RoomMaterialProfile per RoomType. First in array = highest wall priority.")]
    public RoomMaterialProfile[]   materialProfiles;

    [Header("Placement Settings")]
    [Tooltip("Minimum clearance from room walls for floor prop placement.")]
    public float wallMargin  = 0.5f;

    [Tooltip("Minimum gap between placed props.")]
    public float propPadding = 0.1f;

    [Tooltip("How many random positions to try before giving up on a prop.")]
    public int   maxPlacementAttempts = 30;

    // ── Internal state per Furnish() call ──────────────────────────────────
    private Dictionary<RoomType, RoomFurnishingProfile> _furnishLookup;
    private Dictionary<RoomType, RoomMaterialProfile>   _matLookup;
    private List<BuildingWall>                          _walls;
    private List<BuildingRoom>                          _rooms;
    private System.Random                               _rng;

    // Per-room placed item footprints (XZ AABB) for overlap checking
    private List<Rect> _placed;

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Main entry point. Called by ProceduralBuildingGenerator after InstantiateGeometry().
    /// roomGeometry[i] = (floorGO, ceilingGO) for rooms[i].
    /// buildingParents[buildingIndex] = parent Transform for that building section.
    /// </summary>
    public void Furnish(
        List<BuildingRoom>                           rooms,
        List<BuildingWall>                           walls,
        Dictionary<int, Transform>                   buildingParents,
        List<(GameObject floor, GameObject ceiling)> roomGeometry,
        int                                          seed,
        List<GameObject>                             wallGameObjects = null)
    {
        _rng    = new System.Random(seed ^ 0xBEEF);
        _rooms  = rooms;
        _walls  = walls;

        BuildLookups();

        Debug.Log($"[PropSpawner] Furnish called — {rooms.Count} rooms, {walls.Count} walls, " +
                  $"{roomGeometry.Count} room GOs, {(wallGameObjects != null ? wallGameObjects.Count : 0)} wall GOs, " +
                  $"{_furnishLookup.Count} furnish profiles, {_matLookup.Count} material profiles.");

        // Log what profile each room type maps to
        foreach (var kv in _matLookup)
            Debug.Log($"[PropSpawner] Material profile: {kv.Key} → floor={kv.Value.floorMaterial?.name ?? "null"}, " +
                      $"ceil={kv.Value.ceilingMaterial?.name ?? "null"}, wall={kv.Value.wallMaterial?.name ?? "null"}");

        ApplyWallMaterials(rooms, walls, wallGameObjects);

        for (int i = 0; i < rooms.Count; i++)
        {
            var room   = rooms[i];
            var (floorGO, ceilingGO) = roomGeometry[i];
            var parent = buildingParents.TryGetValue(room.buildingIndex, out var p) ? p : transform;

            // ── Materials ─────────────────────────────────────────────────
            if (_matLookup.TryGetValue(room.roomType, out var matProfile))
            {
                if (matProfile.floorMaterial != null && floorGO != null)
                {
                    // GetComponentInChildren so it works whether the MeshRenderer
                    // is on the root or a child of the prefab
                    var mr = floorGO.GetComponentInChildren<MeshRenderer>();
                    if (mr != null) mr.sharedMaterial = matProfile.floorMaterial;
                    else Debug.LogWarning($"[PropSpawner] Floor GO '{floorGO.name}' has no MeshRenderer.");
                }
                if (matProfile.ceilingMaterial != null && ceilingGO != null)
                {
                    var mr = ceilingGO.GetComponentInChildren<MeshRenderer>();
                    if (mr != null) mr.sharedMaterial = matProfile.ceilingMaterial;
                    else Debug.LogWarning($"[PropSpawner] Ceiling GO '{ceilingGO.name}' has no MeshRenderer.");
                }
            }
            else
            {
                // Only log once per missing type to avoid spam
                if (i == 0 || rooms[i-1].roomType != room.roomType)
                    Debug.Log($"[PropSpawner] No material profile for RoomType.{room.roomType} — using default material.");
            }

            // ── Props ─────────────────────────────────────────────────────
            if (!_furnishLookup.TryGetValue(room.roomType, out var profile)) continue;

            _placed = new List<Rect>();

            SpawnCeilingProps(room, profile, ceilingGO, parent);
            SpawnWallProps(room, i, profile, parent);
            SpawnFloorProps(room, profile, parent);
        }
    }

    // ── Ceiling props ──────────────────────────────────────────────────────

    private void SpawnCeilingProps(BuildingRoom room, RoomFurnishingProfile profile,
                                    GameObject ceilingGO, Transform parent)
    {
        if (profile.ceilingProps == null || profile.ceilingProps.Length == 0) return;

        var entry = WeightedPick(profile.ceilingProps);
        if (entry?.prefab == null) return;

        float area   = room.size.x * room.size.z;
        float ceilY  = room.position.y + room.size.y + profile.ceilingPropYOffset;

        if (area <= profile.singleLampAreaThreshold)
        {
            // One lamp at room centre
            PlaceProp(entry.prefab,
                new Vector3(room.position.x + room.size.x * 0.5f, ceilY, room.position.z + room.size.z * 0.5f),
                0f, parent);
        }
        else
        {
            // One lamp per quadrant
            Vector3[] quadrantCentres =
            {
                new Vector3(room.position.x + room.size.x * 0.25f, ceilY, room.position.z + room.size.z * 0.25f),
                new Vector3(room.position.x + room.size.x * 0.75f, ceilY, room.position.z + room.size.z * 0.25f),
                new Vector3(room.position.x + room.size.x * 0.25f, ceilY, room.position.z + room.size.z * 0.75f),
                new Vector3(room.position.x + room.size.x * 0.75f, ceilY, room.position.z + room.size.z * 0.75f),
            };
            foreach (var pos in quadrantCentres)
                PlaceProp(entry.prefab, pos, 0f, parent);
        }
    }

    // ── Wall props (paintings) ─────────────────────────────────────────────

    private void SpawnWallProps(BuildingRoom room, int roomIndex,
                                 RoomFurnishingProfile profile, Transform parent)
    {
        if (profile.wallProps == null || profile.wallProps.Length == 0) return;

        // Find walls that border this room geometrically
        var roomWalls = FindWallsBorderingRoom(room, roomIndex);
        if (roomWalls.Count == 0) return;

        Shuffle(roomWalls);

        int placed = 0;
        foreach (var wall in roomWalls)
        {
            if (placed >= profile.maxWallProps) break;

            var entry = WeightedPick(profile.wallProps);
            if (entry?.prefab == null) continue;

            // Determine wall side and inward direction
            GetWallSideInfo(wall, room, out Vector3 inwardNormal, out float wallCoord,
                            out float spanMin, out float spanMax, out bool isXWall);

            float spanLength = spanMax - spanMin;
            float minSpan    = entry.footprintX + wallMargin * 2f;
            if (spanLength < minSpan) continue;

            // Random position along the wall span, keeping margin from corners
            float t        = (float)_rng.NextDouble();
            float spanPos  = Mathf.Lerp(spanMin + wallMargin, spanMax - wallMargin, t);
            float propY    = room.position.y + profile.wallPropHeight;

            Vector3 propPos = isXWall
                ? new Vector3(spanPos, propY, wallCoord + inwardNormal.z * profile.wallPropInset)
                : new Vector3(wallCoord + inwardNormal.x * profile.wallPropInset, propY, spanPos);

            float yRot = Mathf.Atan2(-inwardNormal.x, -inwardNormal.z) * Mathf.Rad2Deg;
            PlaceProp(entry.prefab, propPos, yRot, parent);
            placed++;
        }
    }

    // ── Floor props ────────────────────────────────────────────────────────

    private void SpawnFloorProps(BuildingRoom room, RoomFurnishingProfile profile, Transform parent)
    {
        if (profile.floorProps == null || profile.floorProps.Length == 0) return;

        int count = _rng.Next(profile.minFloorProps, profile.maxFloorProps + 1);

        for (int i = 0; i < count; i++)
        {
            var entry = WeightedPick(profile.floorProps);
            if (entry?.prefab == null) continue;

            float propY = room.position.y + entry.yOffset;

            if (entry.prefersWall)
            {
                // Try each of the 4 walls in random order
                var sides = ShuffledSides();
                bool wallPlaced = false;

                foreach (int side in sides)
                {
                    if (TryPlaceAgainstWall(room, entry, side, propY, parent))
                    {
                        wallPlaced = true;
                        break;
                    }
                }

                // Fall back to random placement if no wall worked
                if (!wallPlaced)
                    TryPlaceRandom(room, entry, propY, parent);
            }
            else
            {
                TryPlaceRandom(room, entry, propY, parent);
            }
        }
    }

    // ── Wall placement ─────────────────────────────────────────────────────

    // side: 0=left(x-), 1=right(x+), 2=front(z-), 3=back(z+)
    private bool TryPlaceAgainstWall(BuildingRoom room, PropEntry entry,
                                      int side, float propY, Transform parent)
    {
        float fw = entry.footprintX;  // along wall
        float fd = entry.footprintZ;  // perpendicular (depth into room)

        float xMin = room.position.x, xMax = room.position.x + room.size.x;
        float zMin = room.position.z, zMax = room.position.z + room.size.z;

        float clearX, clearZ, yRot;
        float spanMin, spanMax;

        switch (side)
        {
            case 0: // left wall (x-)
                clearX   = xMin + wallMargin + fd * 0.5f;
                spanMin  = zMin + wallMargin + fw * 0.5f;
                spanMax  = zMax - wallMargin - fw * 0.5f;
                yRot     = 90f;  // face right (+X)
                clearZ   = RandomRange(spanMin, spanMax);
                if (spanMax <= spanMin) return false;
                return TryCommitPlacement(new Vector3(clearX, propY, clearZ), fw, fd, yRot, parent, entry.prefab);

            case 1: // right wall (x+)
                clearX   = xMax - wallMargin - fd * 0.5f;
                spanMin  = zMin + wallMargin + fw * 0.5f;
                spanMax  = zMax - wallMargin - fw * 0.5f;
                yRot     = -90f; // face left (-X)
                clearZ   = RandomRange(spanMin, spanMax);
                if (spanMax <= spanMin) return false;
                return TryCommitPlacement(new Vector3(clearX, propY, clearZ), fw, fd, yRot, parent, entry.prefab);

            case 2: // front wall (z-)
                clearZ   = zMin + wallMargin + fd * 0.5f;
                spanMin  = xMin + wallMargin + fw * 0.5f;
                spanMax  = xMax - wallMargin - fw * 0.5f;
                yRot     = 0f;   // face forward (+Z)
                clearX   = RandomRange(spanMin, spanMax);
                if (spanMax <= spanMin) return false;
                // Swap footprint dims since prop faces Z
                return TryCommitPlacement(new Vector3(clearX, propY, clearZ), fd, fw, yRot, parent, entry.prefab);

            case 3: // back wall (z+)
                clearZ   = zMax - wallMargin - fd * 0.5f;
                spanMin  = xMin + wallMargin + fw * 0.5f;
                spanMax  = xMax - wallMargin - fw * 0.5f;
                yRot     = 180f; // face backward (-Z)
                clearX   = RandomRange(spanMin, spanMax);
                if (spanMax <= spanMin) return false;
                return TryCommitPlacement(new Vector3(clearX, propY, clearZ), fd, fw, yRot, parent, entry.prefab);

            default: return false;
        }
    }

    // ── Random placement ────────────────────────────────────────────────────

    private bool TryPlaceRandom(BuildingRoom room, PropEntry entry, float propY, Transform parent)
    {
        float fw = entry.footprintX;
        float fd = entry.footprintZ;

        float xMin = room.position.x + wallMargin + fw * 0.5f;
        float xMax = room.position.x + room.size.x - wallMargin - fw * 0.5f;
        float zMin = room.position.z + wallMargin + fd * 0.5f;
        float zMax = room.position.z + room.size.z - wallMargin - fd * 0.5f;

        if (xMax <= xMin || zMax <= zMin) return false;

        float yRot = _rng.Next(4) * 90f;

        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
        {
            float x = RandomRange(xMin, xMax);
            float z = RandomRange(zMin, zMax);
            if (TryCommitPlacement(new Vector3(x, propY, z), fw, fd, yRot, parent, entry.prefab))
                return true;
        }
        return false;
    }

    // ── Commit placement ────────────────────────────────────────────────────

    private bool TryCommitPlacement(Vector3 pos, float fw, float fd,
                                     float yRot, Transform parent, GameObject prefab)
    {
        var footprint = new Rect(pos.x - fw * 0.5f - propPadding,
                                 pos.z - fd * 0.5f - propPadding,
                                 fw + propPadding * 2f,
                                 fd + propPadding * 2f);

        if (OverlapsAny(footprint)) return false;

        _placed.Add(footprint);
        PlaceProp(prefab, pos, yRot, parent);
        return true;
    }

    private bool OverlapsAny(Rect r)
    {
        foreach (var p in _placed)
            if (r.Overlaps(p)) return true;
        return false;
    }

    // ── Material application ───────────────────────────────────────────────

    private void ApplyWallMaterials(List<BuildingRoom> rooms, List<BuildingWall> walls,
                                     List<GameObject> wallGOs)
    {
        if (wallGOs == null || wallGOs.Count == 0)
        {
            Debug.LogWarning("[PropSpawner] No wall GameObjects passed — wall materials skipped. " +
                             "Make sure the generator passes wallGameObjects to Furnish().");
            return;
        }

        if (walls.Count != wallGOs.Count)
        {
            Debug.LogWarning($"[PropSpawner] walls.Count ({walls.Count}) != wallGOs.Count ({wallGOs.Count}). " +
                             "Wall materials skipped — lists must be parallel.");
            return;
        }

        // Build priority map: roomIndex → profile array index (lower = higher priority)
        var roomPriority = new Dictionary<int, int>();
        for (int i = 0; i < rooms.Count; i++)
            for (int p = 0; p < materialProfiles.Length; p++)
                if (materialProfiles[p] != null && materialProfiles[p].roomType == rooms[i].roomType)
                { roomPriority[i] = p; break; }

        int applied = 0;
        for (int wi = 0; wi < walls.Count; wi++)
        {
            var wall = walls[wi];
            var go   = wallGOs[wi];
            if (go == null) continue;

            // Pick whichever bordering room has the highest-priority profile
            int bestRoom = -1, bestPriority = int.MaxValue;

            if (wall.roomA >= 0 && roomPriority.TryGetValue(wall.roomA, out int pA) && pA < bestPriority)
            { bestRoom = wall.roomA; bestPriority = pA; }
            if (wall.roomB >= 0 && roomPriority.TryGetValue(wall.roomB, out int pB) && pB < bestPriority)
            { bestRoom = wall.roomB; }

            if (bestRoom < 0) continue;

            var mat = materialProfiles[roomPriority[bestRoom]].wallMaterial;
            if (mat == null) continue;

            var mr = go.GetComponentInChildren<MeshRenderer>();
            if (mr != null) { mr.sharedMaterial = mat; applied++; }
        }

        Debug.Log($"[PropSpawner] Wall materials applied to {applied}/{walls.Count} walls.");
    }

    // ── Wall geometry helpers ──────────────────────────────────────────────

    private List<BuildingWall> FindWallsBorderingRoom(BuildingRoom room, int roomIndex)
    {
        const float tol = 0.3f;
        var result = new List<BuildingWall>();

        float rXMax = room.position.x + room.size.x;
        float rZMax = room.position.z + room.size.z;

        foreach (var wall in _walls)
        {
            // First try roomA/roomB index match (fast)
            if (wall.roomA == roomIndex || wall.roomB == roomIndex)
            {
                result.Add(wall);
                continue;
            }

            // Geometric fallback
            float wHalfX = wall.size.x * 0.5f, wHalfZ = wall.size.z * 0.5f;
            float wXMin = wall.position.x - wHalfX, wXMax = wall.position.x + wHalfX;
            float wZMin = wall.position.z - wHalfZ, wZMax = wall.position.z + wHalfZ;

            bool borders;
            if (wall.facingX)
            {
                bool onEdge   = Mathf.Abs(wall.position.x - room.position.x) < tol ||
                                Mathf.Abs(wall.position.x - rXMax)           < tol;
                bool zOverlap = wZMax > room.position.z + tol && wZMin < rZMax - tol;
                borders = onEdge && zOverlap;
            }
            else
            {
                bool onEdge   = Mathf.Abs(wall.position.z - room.position.z) < tol ||
                                Mathf.Abs(wall.position.z - rZMax)           < tol;
                bool xOverlap = wXMax > room.position.x + tol && wXMin < rXMax - tol;
                borders = onEdge && xOverlap;
            }

            if (borders) result.Add(wall);
        }
        return result;
    }

    /// <summary>
    /// Given a wall bordering a room, returns the inward normal, the wall's fixed coordinate,
    /// the span range along the wall, and whether it runs along X.
    /// </summary>
    private void GetWallSideInfo(BuildingWall wall, BuildingRoom room,
                                  out Vector3 inwardNormal, out float wallCoord,
                                  out float spanMin, out float spanMax, out bool isXWall)
    {
        const float tol = 0.3f;
        float rXMax = room.position.x + room.size.x;
        float rZMax = room.position.z + room.size.z;

        if (wall.facingX)
        {
            // Wall runs along Z — spans Z
            isXWall  = false;
            wallCoord = wall.position.x;
            spanMin   = wall.position.z - wall.size.z * 0.5f;
            spanMax   = wall.position.z + wall.size.z * 0.5f;

            // Inward normal: is the wall on the left (-X) or right (+X) of the room?
            inwardNormal = Mathf.Abs(wall.position.x - room.position.x) < tol
                ? Vector3.right   // wall at room.xMin → inward is +X
                : Vector3.left;   // wall at room.xMax → inward is -X
        }
        else
        {
            // Wall runs along X — spans X
            isXWall   = true;
            wallCoord  = wall.position.z;
            spanMin    = wall.position.x - wall.size.x * 0.5f;
            spanMax    = wall.position.x + wall.size.x * 0.5f;

            inwardNormal = Mathf.Abs(wall.position.z - room.position.z) < tol
                ? Vector3.forward  // wall at room.zMin → inward is +Z
                : Vector3.back;    // wall at room.zMax → inward is -Z
        }
    }

    // ── Utility ────────────────────────────────────────────────────────────

    private void PlaceProp(GameObject prefab, Vector3 position, float yRotation, Transform parent)
    {
        var go = Instantiate(prefab, position, Quaternion.Euler(0f, yRotation, 0f), parent);
        go.name = prefab.name;
    }

    private PropEntry WeightedPick(PropEntry[] entries)
    {
        if (entries == null || entries.Length == 0) return null;

        float total = 0f;
        foreach (var e in entries) total += e.weight;
        float roll = (float)_rng.NextDouble() * total;

        foreach (var e in entries)
        {
            roll -= e.weight;
            if (roll <= 0f) return e;
        }
        return entries[entries.Length - 1];
    }

    private float RandomRange(float min, float max)
        => (float)(_rng.NextDouble() * (max - min) + min);

    private int[] ShuffledSides()
    {
        var sides = new[] { 0, 1, 2, 3 };
        for (int i = sides.Length - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (sides[i], sides[j]) = (sides[j], sides[i]);
        }
        return sides;
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void BuildLookups()
    {
        _furnishLookup = new Dictionary<RoomType, RoomFurnishingProfile>();
        if (furnishingProfiles != null)
            foreach (var p in furnishingProfiles)
                if (p != null) _furnishLookup[p.roomType] = p;

        _matLookup = new Dictionary<RoomType, RoomMaterialProfile>();
        if (materialProfiles != null)
            foreach (var p in materialProfiles)
                if (p != null) _matLookup[p.roomType] = p;
    }
}

// Extension to reduce verbosity
internal static class MeshRendererExt
{
    internal static void SetMaterial(this MeshRenderer mr, Material mat)
    {
        if (mr != null && mat != null) mr.sharedMaterial = mat;
    }
}