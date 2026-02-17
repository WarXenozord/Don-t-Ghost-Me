using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Spawns minimap geometry (wall quads, door sprites, player marker) organised by floor.
/// Supports room-by-room reveal: all sprites start inactive and are permanently enabled
/// when RevealRoom(roomIndex) is called. Walls shared between two rooms are revealed
/// when either room is entered.
/// </summary>
public class FloorplanRenderer : MonoBehaviour
{
    [Header("Wall Appearance")]
    public Color    wallColor             = Color.black;
    public Shader   wallShader;
    public Material wallMaterialOverride;

    [Header("Minimap Prefabs")]
    [Tooltip("Door arc symbol. Arc should open toward +X in the source PNG.")]
    public GameObject doorSpritePrefab;
    [Tooltip("Player marker sprite.")]
    public GameObject playerMarkerPrefab;

    [Header("Minimap Settings")]
    [Tooltip("World Y where all floorplan sprites are placed. Must be above your tallest building.")]
    public float minimapSpriteY      = 200f;
    public float wallSpriteThickness = 0.3f;
    public float doorSpriteSize      = 1.2f;
    public float playerMarkerSize    = 1.5f;
    public bool isHidden = true;

    // ?? Runtime ????????????????????????????????????????????????????????????
    private Dictionary<int, GameObject>      floorParents  = new Dictionary<int, GameObject>();
    private Dictionary<int, List<GameObject>> roomSprites  = new Dictionary<int, List<GameObject>>();
    private HashSet<int>                     revealedRooms = new HashSet<int>();
    private GameObject                       playerMarkerInstance;
    private int                              currentVisibleFloor = -1;
    private Material                         _wallMaterial;
    private List<BuildingRoom>               _rooms; // kept for geometric wall registration

    // ?? Public API ?????????????????????????????????????????????????????????

    public void Build(
        List<BuildingWall> walls,
        List<BuildingDoor> doors,
        List<BuildingRoom> rooms,
        float              floorHeight)
    {
        Clear();

        int minimapLayer = LayerMask.NameToLayer("Minimap");
        if (minimapLayer < 0)
            Debug.LogWarning("[FloorplanRenderer] No layer named 'Minimap'. " +
                             "Create it in Edit ? Project Settings ? Tags & Layers.");

        _rooms = rooms;

        // Pre-populate roomSprites so every room index has a list, even if empty
        for (int i = 0; i < rooms.Count; i++)
            roomSprites[i] = new List<GameObject>();

        // Per-floor parent objects — start hidden, revealed rooms within them start inactive
        foreach (int f in rooms.Select(r => r.floorIndex).Distinct().OrderBy(x => x))
        {
            var parent = new GameObject($"Minimap_Floor_{f}");
            parent.transform.SetParent(transform, worldPositionStays: false);
            SetLayerRecursively(parent, minimapLayer);
            floorParents[f] = parent;
            if(isHidden) parent.SetActive(false);
        }

        SpawnWallSprites(walls, floorHeight, minimapLayer);
        SpawnDoorSprites(doors, floorHeight, minimapLayer);
        SpawnPlayerMarker(minimapLayer);

        SetActiveFloor(0);
    }

    /// <summary>
    /// Permanently reveals all minimap sprites that border roomIndex.
    /// Safe to call multiple times — subsequent calls are no-ops.
    /// </summary>
    public void RevealRoom(int roomIndex)
    {
        if (revealedRooms.Contains(roomIndex)) return;
        revealedRooms.Add(roomIndex);

        if (!roomSprites.TryGetValue(roomIndex, out var sprites)) return;
        foreach (var go in sprites)
            if (go != null) go.SetActive(true);
    }

    /// <summary>Show sprites for this floor only. Revealed state is preserved.</summary>
    public void SetActiveFloor(int floorIndex)
    {
        if (currentVisibleFloor == floorIndex) return;
        currentVisibleFloor = floorIndex;

        foreach (var kv in floorParents)
            kv.Value.SetActive(kv.Key == floorIndex);
    }

    /// <summary>Move player marker every frame.</summary>
    public void UpdatePlayerMarker(Vector3 worldPos)
    {
        if (playerMarkerInstance == null) return;
        playerMarkerInstance.transform.position =
            new Vector3(worldPos.x, minimapSpriteY + 0.2f, worldPos.z);
    }

    // ?? Sprite spawning ????????????????????????????????????????????????????

    private void SpawnWallSprites(List<BuildingWall> walls, float floorHeight, int layer)
    {
        Debug.Log($"[FloorplanRenderer] SpawnWallSprites: {walls.Count} walls, floorHeight={floorHeight}, knownFloors=[{string.Join(",", floorParents.Keys)}]");

        var mat = GetWallMaterial();
        if (mat == null)
        {
            Debug.LogError("[FloorplanRenderer] GetWallMaterial() returned null — no walls will be created. " +
                           "Assign wallShader or wallMaterialOverride in the inspector.");
            return;
        }

        int skippedFloor = 0, created = 0;
        foreach (var wall in walls)
        {
            int floorIdx = WorldYToFloorIndex(wall.position.y - wall.size.y * 0.5f, floorHeight);
            if (!floorParents.TryGetValue(floorIdx, out var parent)) { skippedFloor++; continue; }

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = $"Wall_{(wall.facingX ? "Z" : "X")}";
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.position = new Vector3(wall.position.x, minimapSpriteY, wall.position.z);

            float worldX = wall.facingX ? wallSpriteThickness : wall.size.x;
            float worldZ = wall.facingX ? wall.size.z         : wallSpriteThickness;
            go.transform.localScale = new Vector3(worldX, worldZ, 1f);

            SetLayerRecursively(go, layer);
            created++;

            // Start hidden — RevealRoom will enable it
            if(isHidden) go.SetActive(false);

            // Try roomA/roomB first (set by the refactored generator).
            // If both are -1 (older generator or exterior walls), fall back to a
            // geometric search so walls always end up in at least one room's list.
            bool registered = false;
            if (wall.roomA >= 0) { RegisterSpriteToRoom(wall.roomA, go); registered = true; }
            if (wall.roomB >= 0) { RegisterSpriteToRoom(wall.roomB, go); registered = true; }

            if (!registered)
                RegisterWallGeometrically(wall, floorIdx, go);
        }
    }

    /// <summary>
    /// Fallback: find rooms that this wall borders by geometry.
    /// Used when wall.roomA / wall.roomB are both -1.
    ///
    /// A wall borders a room when:
    ///   facingX (runs along Z) ? wall X ? room left or right edge,
    ///                            wall Z range overlaps room Z range
    ///   !facingX (runs along X) ? wall Z ? room front or back edge,
    ///                             wall X range overlaps room X range
    /// </summary>
    private void RegisterWallGeometrically(BuildingWall wall, int floorIdx, GameObject go)
    {
        const float tol = 0.3f;

        float wallHalfX = wall.size.x * 0.5f;
        float wallHalfZ = wall.size.z * 0.5f;
        float wallXMin  = wall.position.x - wallHalfX;
        float wallXMax  = wall.position.x + wallHalfX;
        float wallZMin  = wall.position.z - wallHalfZ;
        float wallZMax  = wall.position.z + wallHalfZ;

        bool anyFound = false;

        for (int i = 0; i < _rooms.Count; i++)
        {
            var r = _rooms[i];
            if (r.floorIndex != floorIdx) continue;

            float rXMax = r.position.x + r.size.x;
            float rZMax = r.position.z + r.size.z;

            bool borders;
            if (wall.facingX)
            {
                // Wall normal faces X — it sits on a left or right edge of the room
                bool onEdge = Mathf.Abs(wall.position.x - r.position.x) < tol ||
                              Mathf.Abs(wall.position.x - rXMax)        < tol;
                bool zOverlap = wallZMax > r.position.z + tol &&
                                wallZMin < rZMax            - tol;
                borders = onEdge && zOverlap;
            }
            else
            {
                // Wall normal faces Z — it sits on a front or back edge of the room
                bool onEdge = Mathf.Abs(wall.position.z - r.position.z) < tol ||
                              Mathf.Abs(wall.position.z - rZMax)        < tol;
                bool xOverlap = wallXMax > r.position.x + tol &&
                                wallXMin < rXMax            - tol;
                borders = onEdge && xOverlap;
            }

            if (borders)
            {
                RegisterSpriteToRoom(i, go);
                anyFound = true;
            }
        }

        if (!anyFound)
            Debug.LogWarning($"[FloorplanRenderer] Wall at {wall.position} could not be assigned to any room — it won't be revealed.");
    }

    private void SpawnDoorSprites(List<BuildingDoor> doors, float floorHeight, int layer)
    {
        if (doorSpritePrefab == null)
        {
            Debug.LogWarning("[FloorplanRenderer] doorSpritePrefab not assigned.");
            return;
        }

        Vector2 nativeSize = GetSpriteNativeSize(doorSpritePrefab);
        if (nativeSize == Vector2.zero) return;

        foreach (var door in doors)
        {
            int floorIdx = WorldYToFloorIndex(door.position.y - door.size.y * 0.5f, floorHeight);
            if (!floorParents.TryGetValue(floorIdx, out var parent)) continue;

            var pos = new Vector3(door.position.x, minimapSpriteY + 0.05f, door.position.z);
            var go  = Instantiate(doorSpritePrefab, pos, Quaternion.identity, parent.transform);
            go.name = "Door";

            float yRot = door.wallFacingX ? 0f : 90f;
            go.transform.rotation   = Quaternion.Euler(90f, yRot + 90f, 0f);
            go.transform.localScale = new Vector3(
                doorSpriteSize / nativeSize.x,
                doorSpriteSize / nativeSize.y,
                1f);

            SetLayerRecursively(go, layer);

            // Start hidden
            if(isHidden) go.SetActive(false);

            // Door is revealed when either neighbouring room is entered
            RegisterSpriteToRoom(door.roomA, go);
            RegisterSpriteToRoom(door.roomB, go);
        }
    }

    private void SpawnPlayerMarker(int layer)
    {
        if (playerMarkerPrefab == null) return;

        playerMarkerInstance      = Instantiate(playerMarkerPrefab, transform);
        playerMarkerInstance.name = "Minimap_Player";
        playerMarkerInstance.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        Vector2 nativeSize = GetSpriteNativeSize(playerMarkerPrefab);
        if (nativeSize != Vector2.zero)
            playerMarkerInstance.transform.localScale = new Vector3(
                playerMarkerSize / nativeSize.x,
                playerMarkerSize / nativeSize.y,
                1f);

        SetLayerRecursively(playerMarkerInstance, layer);
        // Player marker is always visible — not part of reveal system
        playerMarkerInstance.SetActive(true);
    }

    // ?? Helpers ????????????????????????????????????????????????????????????

    /// <summary>Register a sprite GO to a room. -1 = exterior, still register
    /// so exterior walls appear when the interior room is revealed.</summary>
    private void RegisterSpriteToRoom(int roomIndex, GameObject go)
    {
        if (roomIndex < 0) return; // exterior side — only registered to the interior room
        if (!roomSprites.ContainsKey(roomIndex))
            roomSprites[roomIndex] = new List<GameObject>();
        // Avoid duplicates (a wall could theoretically be registered twice)
        if (!roomSprites[roomIndex].Contains(go))
            roomSprites[roomIndex].Add(go);
    }

    private Material GetWallMaterial()
    {
        if (_wallMaterial != null) return _wallMaterial;

        // 1. Inspector override — highest priority
        if (wallMaterialOverride != null)
        {
            _wallMaterial = new Material(wallMaterialOverride) { color = wallColor };
            return _wallMaterial;
        }

        // 2. Inspector shader field
        if (wallShader != null)
        {
            _wallMaterial = new Material(wallShader) { color = wallColor };
            return _wallMaterial;
        }

        // 3. Common shader names (pipeline-dependent, may be stripped in builds)
        foreach (var name in new[] { "Unlit/Color", "Universal Render Pipeline/Unlit",
                                     "HDRP/Unlit", "Sprites/Default", "UI/Default", "Standard" })
        {
            var s = Shader.Find(name);
            if (s != null)
            {
                _wallMaterial = new Material(s) { color = wallColor };
                return _wallMaterial;
            }
        }

        // 4. Guaranteed fallback: steal the shader from a Unity primitive.
        //    CreatePrimitive always succeeds and its default material is always
        //    included in the build — so this path can never fail.
        var temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _wallMaterial = new Material(temp.GetComponent<MeshRenderer>().sharedMaterial) { color = wallColor };
        DestroyImmediate(temp);
        Debug.Log("[FloorplanRenderer] Using primitive fallback material for wall quads. " +
                  "Assign wallShader or wallMaterialOverride if you want a specific look.");
        return _wallMaterial;
    }

    private static Vector2 GetSpriteNativeSize(GameObject prefab)
    {
        var sr = prefab.GetComponentInChildren<SpriteRenderer>();
        if (sr == null || sr.sprite == null)
        {
            Debug.LogWarning($"[FloorplanRenderer] '{prefab.name}' has no SpriteRenderer or Sprite.");
            return Vector2.zero;
        }
        return sr.sprite.bounds.size;
    }

    private static int WorldYToFloorIndex(float worldBottomY, float floorHeight)
        => Mathf.RoundToInt(worldBottomY / floorHeight);

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        if (layer < 0) return;
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private void Clear()
    {
        foreach (var kv in floorParents)
            if (kv.Value != null) Destroy(kv.Value);
        floorParents.Clear();
        roomSprites.Clear();
        revealedRooms.Clear();

        if (playerMarkerInstance != null) Destroy(playerMarkerInstance);
        playerMarkerInstance = null;
        currentVisibleFloor  = -1;
        _wallMaterial        = null;
        _rooms               = null;
    }
}