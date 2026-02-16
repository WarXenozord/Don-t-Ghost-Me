using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns the orthographic minimap camera and the RenderTexture it renders into.
/// Tracks the player's world position every frame:
///   • Moves the camera to follow the player on XZ
///   • Detects floor changes and tells FloorplanRenderer to switch layers
///   • Moves the player marker sprite via FloorplanRenderer
///
/// ?? Setup checklist ????????????????????????????????????????????????????????
///  1. Create a layer called "Minimap" (Edit ? Project Settings ? Tags & Layers).
///  2. Add this component to any persistent GameObject (e.g. the Generator or a Manager).
///  3. Assign:
///       • player          ? your player Transform
///       • floorplanRenderer ? the FloorplanRenderer component
///       • minimapRawImage ? a UI RawImage where the minimap should appear
///  4. Optionally assign an existing Camera to `minimapCamera`, or leave null
///     to let this script create one automatically.
///  5. The RenderTexture is created at runtime from `renderTextureSize` —
///     no manual asset creation needed.
/// </summary>
[RequireComponent(typeof(FloorplanRenderer))]
public class Minimapcontroller : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The player Transform to follow.")]
    public Transform player;

    [Tooltip("Assign the FloorplanRenderer on the same object, or leave null for auto-find.")]
    public FloorplanRenderer floorplanRenderer;

    [Tooltip("The UI RawImage that displays the minimap RenderTexture.")]
    public RawImage minimapRawImage;

    [Header("Camera")]
    [Tooltip("Leave null to auto-create. If assigned, its settings will be overwritten.")]
    public Camera minimapCamera;

    [Tooltip("Orthographic size (world units visible from centre to edge).")]
    public float orthographicSize = 30f;

    [Tooltip("How high above minimapSpriteY the camera sits.")]
    public float cameraHeightAboveSprites = 10f;

    [Tooltip("Resolution of the RenderTexture in pixels.")]
    public int renderTextureSize = 512;

    [Header("Building Info")]
    [Tooltip("Must match floorHeight in ProceduralBuildingGenerator.")]
    public float floorHeight = 3.5f;

    [Tooltip("World Y of the ground floor (usually 0).")]
    public float groundFloorY = 0f;

    // ?? Private state ??????????????????????????????????????????????????????
    private RenderTexture renderTexture;
    private int           lastFloorIndex = -1;

    // ?? Unity lifecycle ????????????????????????????????????????????????????

    private void Awake()
    {
        if (floorplanRenderer == null)
            floorplanRenderer = GetComponent<FloorplanRenderer>();

        SetupCamera();
        SetupRenderTexture();
    }

    private void LateUpdate()
    {
        if (player == null) return;

        // 1. Follow player on XZ — camera Y is fixed high above the sprites
        float camY = floorplanRenderer.minimapSpriteY + cameraHeightAboveSprites;
        minimapCamera.transform.position =
            new Vector3(player.position.x, camY, player.position.z);

        // 2. Move the player marker sprite
        floorplanRenderer.UpdatePlayerMarker(player.position);

        // 3. Detect floor change and switch the visible floor layer
        int currentFloor = WorldYToFloorIndex(player.position.y);
        if (currentFloor != lastFloorIndex)
        {
            lastFloorIndex = currentFloor;
            floorplanRenderer.SetActiveFloor(currentFloor);
        }
    }

    // ?? Setup helpers ??????????????????????????????????????????????????????

    private void SetupCamera()
    {
        if (minimapCamera == null)
        {
            var go = new GameObject("MinimapCamera");
            go.transform.SetParent(transform, worldPositionStays: false);
            minimapCamera = go.AddComponent<Camera>();
        }

        minimapCamera.orthographic     = true;
        minimapCamera.orthographicSize = orthographicSize;

        // Point straight down
        minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // Only render the Minimap layer — all other geometry is invisible to this camera
        int minimapLayer = LayerMask.GetMask("Minimap");
        minimapCamera.cullingMask = minimapLayer;

        // Black background for the minimap
        minimapCamera.backgroundColor = Color.black;
        minimapCamera.clearFlags       = CameraClearFlags.SolidColor;

        // Render after the main camera
        minimapCamera.depth = 1;
    }

    private void SetupRenderTexture()
    {
        renderTexture = new RenderTexture(renderTextureSize, renderTextureSize, 16)
        {
            name        = "MinimapRT",
            filterMode  = FilterMode.Bilinear,
            antiAliasing = 1
        };
        renderTexture.Create();

        minimapCamera.targetTexture = renderTexture;

        if (minimapRawImage != null)
            minimapRawImage.texture = renderTexture;
        else
            Debug.LogWarning("[MinimapController] minimapRawImage not assigned — " +
                             "RenderTexture created but not displayed on any UI element.");
    }

    // ?? Floor detection ????????????????????????????????????????????????????

    /// <summary>
    /// Returns the floor index a player at worldY is standing on.
    /// Clamps to 0 so being slightly below ground doesn't give -1.
    /// </summary>
    private int WorldYToFloorIndex(float worldY)
        => Mathf.Max(0, Mathf.FloorToInt((worldY - groundFloorY) / floorHeight));

    // ?? Cleanup ????????????????????????????????????????????????????????????

    private void OnDestroy()
    {
        if (renderTexture != null)
        {
            minimapCamera.targetTexture = null;
            renderTexture.Release();
            Destroy(renderTexture);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (player == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(
            new Vector3(player.position.x,
                        floorplanRenderer != null ? floorplanRenderer.minimapSpriteY : 200f,
                        player.position.z),
            new Vector3(orthographicSize * 2f, 1f, orthographicSize * 2f));
    }
#endif
}