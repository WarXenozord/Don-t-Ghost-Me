using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Spawns 2D sprites (walls, doors, player) above the building geometry, organised
/// by floor. Each floor lives under its own parent so MinimapController can
/// toggle visibility by enabling/disabling that parent.
///
/// ?? Why the scaling works the way it does ???????????????????????????????????
/// Unity sprites have a Pixels Per Unit (PPU) setting (default = 100).
/// At localScale (1,1,1) a sprite shows at (textureWidth/PPU) × (textureHeight/PPU)
/// world units — NOT 1×1. So we can't just hand wall lengths straight to localScale.
///
/// The fix: read SpriteRenderer.sprite.bounds.size, which already bakes in PPU
/// and gives us the sprite's native world-unit size at scale 1. Then:
///
///   desiredWorldSize / nativeSpriteSize = correct localScale component
///
/// Walls are code-generated quads — no prefab, no PPU math, just direct world-unit scale.
/// Door arcs and player markers remain as sprite prefabs (they have real artistic content).
/// All minimap geometry sits at minimapSpriteY, flat in XZ, visible only to the Minimap camera.
/// </summary>
public class FloorplanRenderer : MonoBehaviour
{
    [Header("Minimap Prefabs")]
    [Tooltip("Leave null — walls are drawn as code-generated quads (no prefab needed).")]
    [HideInInspector] public GameObject wallSpritePrefab; // kept for compatibility, unused

    [Header("Wall Appearance")]
    [Tooltip("Color of minimap wall lines.")]
    public Color wallColor = Color.black;

    [Tooltip("Shader used for wall quads. Leave null to use Unlit/Color.")]
    public Shader wallShader;

    [Tooltip("Door arc/opening symbol. The arc should open toward +X in the source PNG.")]
    public GameObject doorSpritePrefab;

    [Tooltip("Player marker sprite. Instantiated once and moved every frame.")]
    public GameObject playerMarkerPrefab;

    [Header("Minimap Settings")]
    [Tooltip("World Y at which all floorplan sprites are placed. Must be above your tallest building.")]
    public float minimapSpriteY = 200f;

    [Tooltip("Desired visual thickness of wall lines in world units at the minimap sprite layer.")]
    public float wallSpriteThickness = 0.3f;

    [Tooltip("Desired size of the door arc in world units (uniform — arcs are square symbols).")]
    public float doorSpriteSize = 1.2f;

    [Tooltip("Desired size of the player marker in world units (uniform).")]
    public float playerMarkerSize = 1.5f;

    // ?? Runtime state ??????????????????????????????????????????????????????
    private Dictionary<int, GameObject> floorParents  = new Dictionary<int, GameObject>();
    private GameObject                  playerMarkerInstance;
    private int                         currentVisibleFloor = -1;

    // ?? Public API ?????????????????????????????????????????????????????????

    /// <summary>Called by ProceduralBuildingGenerator after geometry is done.</summary>
    public void Build(
        List<BuildingWall> walls,
        List<BuildingDoor> doors,
        List<BuildingRoom> rooms,
        float              floorHeight)
    {
        Clear();

        int minimapLayer = LayerMask.NameToLayer("Minimap");
        if (minimapLayer < 0)
            Debug.LogWarning("[FloorplanRenderer] No layer named 'Minimap' found. " +
                             "Create it in Edit ? Project Settings ? Tags & Layers.");

        // Build per-floor parents
        foreach (int f in rooms.Select(r => r.floorIndex).Distinct().OrderBy(x => x))
        {
            var parent = new GameObject($"Minimap_Floor_{f}");
            parent.transform.SetParent(transform, worldPositionStays: false);
            SetLayerRecursively(parent, minimapLayer);
            floorParents[f] = parent;
            parent.SetActive(false);
        }

        SpawnWallSprites(walls, floorHeight, minimapLayer);
        SpawnDoorSprites(doors, floorHeight, minimapLayer);
        SpawnPlayerMarker(minimapLayer);

        SetActiveFloor(0);
    }

    /// <summary>Show sprites for this floor, hide all others.</summary>
    public void SetActiveFloor(int floorIndex)
    {
        if (currentVisibleFloor == floorIndex) return;
        currentVisibleFloor = floorIndex;

        foreach (var kv in floorParents)
            kv.Value.SetActive(kv.Key == floorIndex);

        Debug.Log($"[Minimap] Showing floor {floorIndex}");
    }

    /// <summary>Move the player marker every frame. Called by MinimapController.LateUpdate.</summary>
    public void UpdatePlayerMarker(Vector3 worldPos)
    {
        if (playerMarkerInstance == null) return;
        playerMarkerInstance.transform.position =
            new Vector3(worldPos.x, minimapSpriteY + 0.2f, worldPos.z);
    }

    // ?? Sprite spawning ????????????????????????????????????????????????????

    // Shared wall material — created once, reused for every quad.
    private Material _wallMaterial;

    private Material GetWallMaterial()
    {
        if (_wallMaterial != null) return _wallMaterial;
        var shader = wallShader != null ? wallShader : Shader.Find("Unlit/Color");
        _wallMaterial = new Material(shader) { color = wallColor };
        return _wallMaterial;
    }

    private void SpawnWallSprites(List<BuildingWall> walls, float floorHeight, int layer)
    {
        // Walls are plain colored rectangles — no need for a prefab or any PPU math.
        // We create a Unity quad, rotate it flat (Euler(90,0,0) always), then scale
        // directly in world units:
        //   facingX = true  ? wall runs along Z: scaleX = thickness, scaleZ = length
        //   facingX = false ? wall runs along X: scaleX = length,    scaleZ = thickness
        // No axis-swap, no sprite rotation tricks, just direct world-unit sizes.

        var mat = GetWallMaterial();

        foreach (var wall in walls)
        {
            int floorIdx = WorldYToFloorIndex(wall.position.y - wall.size.y * 0.5f, floorHeight);
            if (!floorParents.TryGetValue(floorIdx, out var parent)) continue;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = $"Wall_{(wall.facingX ? "Z" : "X")}";
            go.transform.SetParent(parent.transform, worldPositionStays: false);

            // Destroy the collider — we don't want physics on minimap geometry
            Destroy(go.GetComponent<Collider>());

            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // Quad lies in XY by default — rotate 90° around X to lay it flat in XZ
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.position = new Vector3(wall.position.x, minimapSpriteY, wall.position.z);

            // After Euler(90,0,0): quad local-X ? world-X, quad local-Y ? world-Z
            float worldX = wall.facingX ? wallSpriteThickness : wall.size.x;
            float worldZ = wall.facingX ? wall.size.z         : wallSpriteThickness;
            go.transform.localScale = new Vector3(worldX, worldZ, 1f);

            SetLayerRecursively(go, layer);
        }
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

            // Lie flat; rotate Y so the arc opening aligns with the door gap direction:
            //   wallFacingX = true  ? gap runs along Z ? arc opens along X ? 0° Y rotation
            //   wallFacingX = false ? gap runs along X ? arc opens along Z ? 90° Y rotation
            float yRot = door.wallFacingX ? 0f : 90f;
            go.transform.rotation = Quaternion.Euler(90f, yRot+90f, 0f);

            // Door arc is a square symbol — scale uniformly so the PNG is never stretched
            go.transform.localScale = new Vector3(
                doorSpriteSize / nativeSize.x,
                doorSpriteSize / nativeSize.y,
                1f);

            SetLayerRecursively(go, layer);
        }
    }

    private void SpawnPlayerMarker(int layer)
    {
        if (playerMarkerPrefab == null)
        {
            Debug.LogWarning("[FloorplanRenderer] playerMarkerPrefab not assigned.");
            return;
        }

        // Player marker is NOT parented to any floor — stays visible on all floors
        playerMarkerInstance      = Instantiate(playerMarkerPrefab, transform);
        playerMarkerInstance.name = "Minimap_Player";
        playerMarkerInstance.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        Vector2 nativeSize = GetSpriteNativeSize(playerMarkerPrefab);
        if (nativeSize != Vector2.zero)
        {
            playerMarkerInstance.transform.localScale = new Vector3(
                playerMarkerSize / nativeSize.x,
                playerMarkerSize / nativeSize.y,
                1f);
        }

        SetLayerRecursively(playerMarkerInstance, layer);
    }

    // ?? Helpers ????????????????????????????????????????????????????????????

    /// <summary>
    /// Returns the sprite's native world-unit size (already bakes in Pixels Per Unit).
    /// At localScale (1,1,1) the SpriteRenderer shows exactly this many world units.
    /// Returns Vector2.zero and logs a warning if no Sprite is found.
    /// </summary>
    private static Vector2 GetSpriteNativeSize(GameObject prefab)
    {
        var sr = prefab.GetComponentInChildren<SpriteRenderer>();
        if (sr == null || sr.sprite == null)
        {
            Debug.LogWarning($"[FloorplanRenderer] '{prefab.name}' has no SpriteRenderer " +
                              "or its SpriteRenderer has no Sprite assigned. " +
                              "Skipping — assign a Sprite in the prefab's SpriteRenderer.");
            return Vector2.zero;
        }
        // sprite.bounds.size accounts for PPU and texture dimensions
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

        if (playerMarkerInstance != null) Destroy(playerMarkerInstance);
        playerMarkerInstance = null;
        currentVisibleFloor  = -1;
    }
}