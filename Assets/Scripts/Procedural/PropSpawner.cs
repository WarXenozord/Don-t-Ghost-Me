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

    [Tooltip("Defines what small items spawn on furniture (tables, shelves, etc.). Applied to all rooms.")]
    public FurnitureItemProfile furnitureItemProfile;

    [Tooltip("Assign GhostInteraction so it re-caches interactables after props are spawned.")]
    public GhostInteraction ghostInteraction;

    [Header("Placement Settings")]
    [Tooltip("Must match the generator's Wall Thickness value so paintings sit flush on the interior surface.")]
    public float wallThickness = 0.2f;

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
    private List<BuildingDoor>                          _doors;
    private System.Random                               _rng;

    // Per-room placed item tracking (for overlap checking)
    // Changed from Rect to actual placed GameObject references for 3D collision checking
    private List<GameObject> _placedObjects;

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Main entry point. Called by ProceduralBuildingGenerator after InstantiateGeometry().
    /// roomGeometry[i] = (floorGO, ceilingGO) for rooms[i].
    /// buildingParents[buildingIndex] = parent Transform for that building section.
    /// </summary>
    public void Furnish(
        List<BuildingRoom>                           rooms,
        List<BuildingWall>                           walls,
        List<BuildingDoor>                           doors,
        Dictionary<int, Transform>                   buildingParents,
        List<(GameObject floor, GameObject ceiling)> roomGeometry,
        int                                          seed,
        List<GameObject>                             wallGameObjects = null)
    {
        _rng    = new System.Random(seed ^ 0xBEEF);
        _rooms  = rooms;
        _walls  = walls;
        _doors  = doors;

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

            _placedObjects = new List<GameObject>();

            // Pre-block door clearance zones so no floor prop can land in a doorway.
            // Creates invisible blocker GameObjects that Physics.OverlapBox will detect.
            AddDoorClearanceBlockers(room, i, parent);

            SpawnCeilingProps(room, profile, ceilingGO, parent);
            SpawnWallProps(room, i, profile, parent);
            SpawnFloorProps(room, profile, parent);
            
            // Spawn small items on furniture surfaces (tables, shelves, etc.)
            SpawnFurnitureItems(parent);
        }

        // Notify GhostInteraction so newly spawned interactable props are detected
        if (ghostInteraction != null)
            ghostInteraction.RefreshInteractableCache();
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

            // Collect door blocked intervals on this wall along its span axis
            var blockedIntervals = GetDoorIntervalsOnWall(wall, isXWall);

            // Build list of clear sub-spans (wall span minus door openings minus margins)
            float propHalf  = entry.footprintX * 0.5f;
            var clearSpans  = SubtractIntervals(
                spanMin + wallMargin + propHalf,
                spanMax - wallMargin - propHalf,
                blockedIntervals, propHalf + wallMargin);

            if (clearSpans.Count == 0) continue; // no room for a prop on this wall

            // Pick a random clear sub-span, then a random position inside it
            var (cMin, cMax) = clearSpans[_rng.Next(clearSpans.Count)];
            float spanPos = (float)(_rng.NextDouble() * (cMax - cMin) + cMin);
            float propY   = room.position.y + profile.wallPropHeight;

            // Offset from the interior wall FACE (not centre):
            // wall centre → interior face = wallThickness * 0.5f
            // then nudge slightly off the face = wallPropInset
            float surfaceOffset = wallThickness * 0.5f + profile.wallPropInset;

            Vector3 propPos = isXWall
                ? new Vector3(spanPos, propY, wallCoord + inwardNormal.z * surfaceOffset)
                : new Vector3(wallCoord + inwardNormal.x * surfaceOffset, propY, spanPos);

            float yRot = Mathf.Atan2(-inwardNormal.x, -inwardNormal.z) * Mathf.Rad2Deg;
            PlaceProp(entry.prefab, propPos, yRot, parent);
            placed++;
        }
    }

    // ── Floor props ────────────────────────────────────────────────────────

    private void SpawnFloorProps(BuildingRoom room, RoomFurnishingProfile profile, Transform parent)
    {
        if (profile.floorProps == null || profile.floorProps.Length == 0) return;

        // Track how many of each prop type we've spawned
        var spawnedCounts = new Dictionary<PropEntry, int>();
        foreach (var entry in profile.floorProps)
            spawnedCounts[entry] = 0;

        // Phase 1: Spawn minimum required props for each type
        foreach (var entry in profile.floorProps)
        {
            if (entry?.prefab == null) continue;

            for (int i = 0; i < entry.minCount; i++)
            {
                float propY = room.position.y + entry.yOffset;

                bool placed = entry.prefersWall
                    ? TryPlaceAgainstWallAny(room, entry, propY, parent)
                    : TryPlaceRandom(room, entry, propY, parent);

                if (placed)
                    spawnedCounts[entry]++;
                else
                    Debug.LogWarning($"[PropSpawner] Failed to place required {entry.prefab.name} (min={entry.minCount})");
            }
        }

        // Phase 2: Fill remaining space with weighted random props up to max
        int attempts = 0;
        int maxAttempts = profile.maxFloorProps * 3; // safety limit

        while (attempts < maxAttempts)
        {
            attempts++;

            // Pick a random prop respecting weights
            var entry = WeightedPick(profile.floorProps);
            if (entry?.prefab == null) continue;

            // Check if we've hit max for this prop type
            if (spawnedCounts[entry] >= entry.maxCount)
                continue;

            // Check if we've hit total room max
            int totalSpawned = spawnedCounts.Values.Sum();
            if (totalSpawned >= profile.maxFloorProps)
                break;

            float propY = room.position.y + entry.yOffset;

            bool placed = entry.prefersWall
                ? TryPlaceAgainstWallAny(room, entry, propY, parent)
                : TryPlaceRandom(room, entry, propY, parent);

            if (placed)
                spawnedCounts[entry]++;
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
                return TryCommitPlacement(new Vector3(clearX, propY, clearZ), entry, yRot, parent);

            case 1: // right wall (x+)
                clearX   = xMax - wallMargin - fd * 0.5f;
                spanMin  = zMin + wallMargin + fw * 0.5f;
                spanMax  = zMax - wallMargin - fw * 0.5f;
                yRot     = -90f; // face left (-X)
                clearZ   = RandomRange(spanMin, spanMax);
                if (spanMax <= spanMin) return false;
                return TryCommitPlacement(new Vector3(clearX, propY, clearZ), entry, yRot, parent);

            case 2: // front wall (z-)
                clearZ   = zMin + wallMargin + fd * 0.5f;
                spanMin  = xMin + wallMargin + fw * 0.5f;
                spanMax  = xMax - wallMargin - fw * 0.5f;
                yRot     = 0f;   // face forward (+Z)
                clearX   = RandomRange(spanMin, spanMax);
                if (spanMax <= spanMin) return false;
                return TryCommitPlacement(new Vector3(clearX, propY, clearZ), entry, yRot, parent);

            case 3: // back wall (z+)
                clearZ   = zMax - wallMargin - fd * 0.5f;
                spanMin  = xMin + wallMargin + fw * 0.5f;
                spanMax  = xMax - wallMargin - fw * 0.5f;
                yRot     = 180f; // face backward (-Z)
                clearX   = RandomRange(spanMin, spanMax);
                if (spanMax <= spanMin) return false;
                return TryCommitPlacement(new Vector3(clearX, propY, clearZ), entry, yRot, parent);

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
            if (TryCommitPlacement(new Vector3(x, propY, z), entry, yRot, parent))
                return true;
        }
        return false;
    }

    // ── Commit placement ────────────────────────────────────────────────────

    private bool TryCommitPlacement(Vector3 pos, PropEntry entry,
                                     float yRot, Transform parent)
    {
        // Get actual bounds from prefab collider or use manual footprint
        Vector3 boundsSize = entry.useColliderBounds
            ? GetColliderBounds(entry.prefab,yRot)
            : new Vector3(entry.footprintX, 2f, entry.footprintZ);

        // Add padding to prevent props from touching
        Vector3 checkSize = boundsSize + Vector3.one * propPadding * 2f;

        // Check for overlap using Physics - much more accurate than Rect
        Collider[] hits = Physics.OverlapBox(
            pos,
            checkSize * 0.5f,
            Quaternion.Euler(0f, yRot, 0f),
            ~0, // check all layers
            QueryTriggerInteraction.Collide); // include door blockers

        // Filter out hits that aren't props/blockers (like walls, floors, ceilings)
        foreach (var hit in hits)
        {
            if (_placedObjects.Contains(hit.gameObject))
                return false; // overlaps existing prop or door blocker
        }

        // Clear - place the prop
        var go = PlaceProp(entry.prefab, pos, yRot, parent);
        _placedObjects.Add(go);
        return true;
    }

    /// <summary>
    /// Gets actual bounds from prefab's collider.
    /// Returns (1,2,1) if no collider found.
    /// </summary>
    private Vector3 GetColliderBounds(GameObject prefab, float yRot)
{
    var temp = Instantiate(prefab);
    temp.transform.rotation = Quaternion.Euler(0f, yRot, 0f);

    var renderers = temp.GetComponentsInChildren<Renderer>();
    if (renderers.Length == 0)
    {
        DestroyImmediate(temp);
        return new Vector3(1f, 2f, 1f);
    }

    Bounds bounds = renderers[0].bounds;
    foreach (var r in renderers)
        bounds.Encapsulate(r.bounds);

    Vector3 size = bounds.size;

    DestroyImmediate(temp);
    return size;
}

    // Helper: Try all 4 walls, return true if any succeeds
    private bool TryPlaceAgainstWallAny(BuildingRoom room, PropEntry entry, float propY, Transform parent)
    {
        var sides = ShuffledSides();
        foreach (int side in sides)
        {
            if (TryPlaceAgainstWall(room, entry, side, propY, parent))
                return true;
        }
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

        // Each wall cube has 6 submeshes (one per face). We assign materials to the
        // inward-facing submeshes based on roomA/roomB.
        // For exterior walls (roomB = -1), determine interior face by wall position vs room bounds.
        int applied = 0;
        for (int wi = 0; wi < walls.Count; wi++)
        {
            var wall = walls[wi];
            var go   = wallGOs[wi];
            if (go == null) continue;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) continue;

            var mats = mr.sharedMaterials;
            if (mats.Length < 6) continue;

            bool changed = false;

            // Determine which submesh is interior-facing for each room
            int submeshA = -1, submeshB = -1;
            
            if (wall.facingX)
            {
                // Wall runs along Z, faces along X. Submesh 4 = -X, submesh 5 = +X
                if (wall.roomA >= 0 && wall.roomB < 0 && wall.roomA < rooms.Count)
                {
                    // Exterior wall: check if it's on room's left (xMin) or right (xMax) edge
                    var room = rooms[wall.roomA];
                    if (Mathf.Abs(wall.position.x - room.position.x) < 0.1f)
                        submeshA = 5; // wall at xMin → room interior faces +X
                    else
                        submeshA = 4; // wall at xMax → room interior faces -X
                }
                else
                {
                    submeshA = 4; // shared wall: roomA on -X side
                    submeshB = 5; // roomB on +X side
                }
            }
            else
            {
                // Wall runs along X, faces along Z. Submesh 0 = -Z, submesh 1 = +Z
                if (wall.roomA >= 0 && wall.roomB < 0 && wall.roomA < rooms.Count)
                {
                    // Exterior wall: check if it's on room's front (zMin) or back (zMax) edge
                    var room = rooms[wall.roomA];
                    if (Mathf.Abs(wall.position.z - room.position.z) < 0.1f)
                        submeshA = 1; // wall at zMin → room interior faces +Z
                    else
                        submeshA = 0; // wall at zMax → room interior faces -Z
                }
                else
                {
                    submeshA = 0; // shared wall: roomA on -Z side
                    submeshB = 1; // roomB on +Z side
                }
            }

            // Apply roomA material
            if (wall.roomA >= 0 && submeshA >= 0 && roomPriority.TryGetValue(wall.roomA, out int profA))
            {
                var matA = materialProfiles[profA].wallMaterial;
                if (matA != null)
                {
                    mats[submeshA] = matA;
                    changed = true;
                }
            }

            // Apply roomB material (shared walls only)
            if (wall.roomB >= 0 && submeshB >= 0 && roomPriority.TryGetValue(wall.roomB, out int profB))
            {
                var matB = materialProfiles[profB].wallMaterial;
                if (matB != null)
                {
                    mats[submeshB] = matB;
                    changed = true;
                }
            }

            if (changed)
            {
                mr.sharedMaterials = mats;
                applied++;
            }
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

    // ── Furniture surface items ────────────────────────────────────────────

    /// <summary>
    /// Spawns small items (books, cups, decorations) on furniture spawn points.
    /// Called after floor props are placed so furniture exists.
    /// </summary>
    private void SpawnFurnitureItems(Transform parent)
    {
        if (furnitureItemProfile == null || furnitureItemProfile.items == null)
            return;

        // Find all furniture with spawn points in this room
        var furniture = parent.GetComponentsInChildren<FurnitureSpawnPoints>();
        if (furniture.Length == 0) return;

        foreach (var piece in furniture)
        {
            if (piece.TotalPoints == 0) continue;

            // Determine how many items to spawn on this furniture piece
            int itemCount = _rng.Next(
                furnitureItemProfile.minItems, 
                furnitureItemProfile.maxItems + 1);

            // Cap by available spawn points
            itemCount = Mathf.Min(itemCount, piece.AvailableCount);

            for (int i = 0; i < itemCount; i++)
            {
                var spawnPoint = piece.GetRandomAvailablePoint();
                if (spawnPoint == null) break; // no more available points

                // Pick random item from profile
                var entry = WeightedPick(furnitureItemProfile.items);
                if (entry?.prefab == null) continue;

                // Check fill probability
                if (_rng.NextDouble() > furnitureItemProfile.fillProbability)
                    continue;

                // Spawn at the spawn point
                float yRot = piece.inheritRotation 
                    ? piece.transform.eulerAngles.y 
                    : 0f;

                var itemParent = piece.parentItems ? piece.transform : parent;
                var item = PlaceProp(entry.prefab, spawnPoint.position, yRot, itemParent);

                // Mark spawn point as occupied
                piece.MarkOccupied(spawnPoint);
            }
        }
    }

    // ── Door clearance for floor props ─────────────────────────────────────

    /// <summary>
    /// Creates invisible blocker GameObjects with colliders around doorways.
    /// Physics.OverlapBox will detect these and prevent props from blocking doors.
    /// Much larger clearance than before to account for prop rotation and actual size.
    /// </summary>
    private void AddDoorClearanceBlockers(BuildingRoom room, int roomIndex, Transform parent,
                                           float doorClearance = 1.5f)
    {
        if (_doors == null) return;

        foreach (var door in _doors)
        {
            // Only care about doors that connect to this room
            if (door.roomA != roomIndex && door.roomB != roomIndex) continue;

            // Create blocker dimensions - much more generous than before
            Vector3 blockerSize;
            if (door.wallFacingX)
            {
                // Door opening along Z
                float width = door.size.z + wallMargin * 2f;
                float depth = door.size.x + doorClearance * 2f; // both sides of door
                blockerSize = new Vector3(depth, 3f, width);
            }
            else
            {
                // Door opening along X
                float width = door.size.x + wallMargin * 2f;
                float depth = door.size.z + doorClearance * 2f;
                blockerSize = new Vector3(width, 3f, depth);
            }

            // Create invisible blocker
            var blocker = new GameObject("DoorBlocker");
            blocker.transform.SetParent(parent, worldPositionStays: false);
            blocker.transform.position = new Vector3(
                door.position.x,
                room.position.y + 0.5f, // center vertically in room
                door.position.z);

            var collider = blocker.AddComponent<BoxCollider>();
            collider.size = blockerSize;
            collider.isTrigger = true; // trigger so it doesn't affect physics
            blocker.layer = LayerMask.NameToLayer("Ignore Raycast"); // invisible to raycasts

            _placedObjects.Add(blocker);
        }
    }

    // ── Door-aware span helpers ────────────────────────────────────────────

    /// <summary>
    /// Returns the blocked intervals (min, max) along a wall's span axis
    /// caused by doors on that wall. Adds a margin on each side of the door.
    /// </summary>
    private List<(float min, float max)> GetDoorIntervalsOnWall(BuildingWall wall, bool isXWall)
    {
        var result = new List<(float, float)>();
        if (_doors == null) return result;

        const float tol = 0.3f;

        foreach (var door in _doors)
        {
            // Check if this door sits on this wall by comparing position
            if (isXWall) // wall runs along X, doors block X span
            {
                if (Mathf.Abs(door.position.z - wall.position.z) > tol) continue;
                float half = door.size.x * 0.5f;
                result.Add((door.position.x - half, door.position.x + half));
            }
            else // wall runs along Z, doors block Z span
            {
                if (Mathf.Abs(door.position.x - wall.position.x) > tol) continue;
                float half = door.size.z * 0.5f;
                result.Add((door.position.z - half, door.position.z + half));
            }
        }

        return result;
    }

    /// <summary>
    /// Subtracts blocked intervals from [rangeMin, rangeMax] and returns
    /// sub-spans that are wide enough to fit a prop (>= minWidth).
    /// </summary>
    private List<(float min, float max)> SubtractIntervals(
        float rangeMin, float rangeMax,
        List<(float min, float max)> blocked, float minWidth)
    {
        var result  = new List<(float, float)>();
        if (rangeMax <= rangeMin) return result;

        // Sort blocked intervals by start
        var sorted = new List<(float min, float max)>(blocked);
        sorted.Sort((a, b) => a.min.CompareTo(b.min));

        float cur = rangeMin;
        foreach (var (bMin, bMax) in sorted)
        {
            float clearEnd = Mathf.Min(bMin, rangeMax);
            if (clearEnd - cur >= minWidth)
                result.Add((cur, clearEnd));
            cur = Mathf.Max(cur, bMax);
        }

        // Remainder after last blocked interval
        if (rangeMax - cur >= minWidth)
            result.Add((cur, rangeMax));

        return result;
    }

    // ── Utility ────────────────────────────────────────────────────────────

    private GameObject PlaceProp(GameObject prefab, Vector3 position, float yRotation, Transform parent)
    {
        var go = Instantiate(prefab, position, Quaternion.Euler(0f, yRotation, 0f), parent);
        go.name = prefab.name;
        return go;
    }

    private PropEntry WeightedPick(PropEntry[] entries)
    {
        if (entries == null || entries.Length == 0) return null;
        if (entries.Length == 1) return entries[0];

        // If all weights are zero, treat as equal probability
        float total = 0f;
        foreach (var e in entries) total += e.weight;
        if (total <= 0f)
        {
            // Equal weight fallback — just pick randomly
            return entries[_rng.Next(entries.Length)];
        }

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