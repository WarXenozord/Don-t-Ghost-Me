using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Fixed-camera minimap that pans via RawImage.uvRect.
///
/// Both this camera and your reveal camera stay at the same fixed world position
/// (centre of the map, high up) so their UVs always align — your reveal shader
/// keeps working untouched.
///
/// Player tracking works by projecting the player's world XZ position into 0–1
/// UV space and offsetting uvRect so the player stays centred in the RawImage.
///
/// ?? Setup checklist ????????????????????????????????????????????????????????
///  1. Create a layer called "Minimap" (Edit ? Project Settings ? Tags & Layers).
///  2. Add this component + FloorplanRenderer to any persistent GameObject.
///  3. Assign: player, floorplanRenderer, minimapRawImage.
///  4. Call SetMapBounds() from ProceduralBuildingGenerator after generation,
///     OR it will be called automatically if you use the BuildMinimap() hook.
///  5. viewportWorldSize controls how many world units the RawImage "window" shows.
///     Think of it as the zoom level — smaller = more zoomed in.
/// </summary>
[RequireComponent(typeof(FloorplanRenderer))]
public class MinimapController : MonoBehaviour
{
    [Header("References")]
    public Transform        player;
    public FloorplanRenderer floorplanRenderer;
    public RawImage         minimapRawImage;

    [Header("Camera")]
    [Tooltip("Leave null to auto-create.")]
    public Camera minimapCamera;
    public Camera revealCamera;
    [Tooltip("Resolution of the RenderTexture in pixels (square).")]
    public int renderTextureSize = 1024;

    [Header("Viewport / Zoom")]
    [Tooltip("How many world units are visible across the RawImage window. Lower = more zoomed in.")]
    public float viewportWorldSize = 30f;

    [Tooltip("Extra padding added around the map bounds when fitting the camera.")]
    public float mapPadding = 5f;

    [Header("Building Info")]
    [Tooltip("Must match floorHeight in ProceduralBuildingGenerator.")]
    public float floorHeight = 3.5f;
    [Tooltip("World Y of the ground floor (usually 0).")]
    public float groundFloorY = 0f;

    // ?? Map bounds (set by generator) ?????????????????????????????????????
    private Rect   mapBoundsXZ;      // world-space XZ rect of the entire map
    private bool   boundsReady = false;

    // ?? Runtime ???????????????????????????????????????????????????????????
    private RenderTexture renderTexture;
    private int           lastFloorIndex = -1;

    // ?? Unity lifecycle ????????????????????????????????????????????????????

    private void Awake()
    {
        if (floorplanRenderer == null)
            floorplanRenderer = GetComponent<FloorplanRenderer>();

        SetupCamera();
    }

    private void LateUpdate()
    {
        if (player == null || !boundsReady || minimapRawImage == null) return;

        // ?? Pan uvRect to centre on the player ????????????????????????????
        // uvRect.size = what fraction of the RT the RawImage window shows.
        // uvRect.position = bottom-left corner of that window in UV space.
        //
        // UV space: (0,0) = bottom-left of RT = (mapMinX, mapMinZ) in world
        //           (1,1) = top-right  of RT = (mapMaxX, mapMaxZ) in world
        //
        // Fraction of map covered by the viewport:
        float fracX = viewportWorldSize / mapBoundsXZ.width;
        float fracZ = viewportWorldSize / mapBoundsXZ.height;

        // Player position in UV space:
        float playerU = (player.position.x - mapBoundsXZ.xMin) / mapBoundsXZ.width;
        float playerV = (player.position.z - mapBoundsXZ.yMin) / mapBoundsXZ.height;

        // Bottom-left corner of the window (centred on player, clamped to [0,1]):
        float u = Mathf.Clamp01(playerU - fracX * 0.5f);
        float v = Mathf.Clamp01(playerV - fracZ * 0.5f);

        // Clamp the far edge too so we don't scroll past the map
        u = Mathf.Min(u, 1f - fracX);
        v = Mathf.Min(v, 1f - fracZ);

        var newRect = new Rect(u, v, fracX, fracZ);
        minimapRawImage.uvRect = newRect;

        // Uncomment to debug — remove once working:
        // Debug.Log($"[Minimap] uvRect={newRect}, playerUV=({playerU:F2},{playerV:F2}), boundsReady={boundsReady}");

        // ?? Player marker ?????????????????????????????????????????????????
        floorplanRenderer.UpdatePlayerMarker(player.position);

        // ?? Floor detection ???????????????????????????????????????????????
        int currentFloor = WorldYToFloorIndex(player.position.y);
        if (currentFloor != lastFloorIndex)
        {
            lastFloorIndex = currentFloor;
            floorplanRenderer.SetActiveFloor(currentFloor);
        }
    }

    // ?? Public API ?????????????????????????????????????????????????????????

    /// <summary>
    /// Called by ProceduralBuildingGenerator (via BuildMinimap) after rooms are generated.
    /// Computes the world XZ bounds, positions the camera to cover the whole map,
    /// and creates the RenderTexture.
    /// </summary>
    public void SetMapBounds(List<BuildingRoom> rooms)
    {
        if (rooms == null || rooms.Count == 0)
        {
            Debug.LogWarning("[MinimapController] SetMapBounds called with no rooms.");
            return;
        }

        // ?? Compute XZ bounds from all room corners ????????????????????????
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (var room in rooms)
        {
            minX = Mathf.Min(minX, room.position.x);
            maxX = Mathf.Max(maxX, room.position.x + room.size.x);
            minZ = Mathf.Min(minZ, room.position.z);
            maxZ = Mathf.Max(maxZ, room.position.z + room.size.z);
        }

        minX -= mapPadding; maxX += mapPadding;
        minZ -= mapPadding; maxZ += mapPadding;

        mapBoundsXZ = new Rect(minX, minZ, maxX - minX, maxZ - minZ);
        boundsReady  = true;

        Debug.Log($"[MinimapController] Map bounds: X[{minX:F1}?{maxX:F1}] Z[{minZ:F1}?{maxZ:F1}]");

        // ?? Position camera at map centre, high above sprites ?????????????
        float camY = floorplanRenderer.minimapSpriteY + 10f;
        float centreX = (minX + maxX) * 0.5f;
        float centreZ = (minZ + maxZ) * 0.5f;

        minimapCamera.transform.SetPositionAndRotation(
            new Vector3(centreX, camY, centreZ),
            Quaternion.Euler(90f, 0f, 0f));
        revealCamera.transform.SetPositionAndRotation(
            new Vector3(centreX, camY, centreZ),
            Quaternion.Euler(90f, 0f, 0f));
        // ?? Fit ortho size to cover the entire map ????????????????????????
        // The camera is square; we need to cover a potentially rectangular map,
        // so take the larger of the two half-extents.
        float halfW = mapBoundsXZ.width  * 0.5f;
        float halfH = mapBoundsXZ.height * 0.5f;
        minimapCamera.orthographicSize = Mathf.Max(halfW, halfH);
        revealCamera.orthographicSize = Mathf.Max(halfW, halfH);
        // ?? Build RenderTexture at aspect ratio matching map bounds ????????
        // Use a non-square RT so 1 pixel = 1 pixel in both axes (no squish).
        // We scale the shorter side down from renderTextureSize.
        int rtW, rtH;
        if (mapBoundsXZ.width >= mapBoundsXZ.height)
        {
            rtW = renderTextureSize;
            rtH = Mathf.Max(1, Mathf.RoundToInt(renderTextureSize * mapBoundsXZ.height / mapBoundsXZ.width));
        }
        else
        {
            rtH = renderTextureSize;
            rtW = Mathf.Max(1, Mathf.RoundToInt(renderTextureSize * mapBoundsXZ.width / mapBoundsXZ.height));
        }

        // Release previous RT if regenerating
        if (renderTexture != null)
        {
            minimapCamera.targetTexture = null;
            renderTexture.Release();
            Destroy(renderTexture);
        }

        renderTexture = new RenderTexture(rtW, rtH, 16)
        {
            name        = "MinimapRT",
            filterMode  = FilterMode.Bilinear,
            antiAliasing = 1
        };
        renderTexture.Create();
        minimapCamera.targetTexture = renderTexture;

        if (minimapRawImage != null)
            minimapRawImage.texture = renderTexture;

        Debug.Log($"[MinimapController] Camera fixed at ({centreX:F1}, {camY:F1}, {centreZ:F1}), " +
                  $"orthoSize={minimapCamera.orthographicSize:F1}, RT={rtW}×{rtH}");
    }

    // ?? Setup ??????????????????????????????????????????????????????????????

    private void SetupCamera()
    {
        if (minimapCamera == null)
        {
            var go = new GameObject("MinimapCamera");
            // No parent — must never inherit rotation from anything
            minimapCamera = go.AddComponent<Camera>();
        }

        minimapCamera.orthographic   = true;
        minimapCamera.orthographicSize = 100f; // temporary until SetMapBounds is called
        revealCamera.orthographicSize = 100f;
        minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        int minimapMask = LayerMask.GetMask("Minimap");
        if (minimapMask == 0)
            Debug.LogWarning("[MinimapController] 'Minimap' layer not found — " +
                             "create it in Edit ? Project Settings ? Tags & Layers.");
        minimapCamera.cullingMask    = minimapMask;
        minimapCamera.clearFlags     = CameraClearFlags.SolidColor;
        minimapCamera.backgroundColor = Color.clear; // transparent so RT background is clear
        minimapCamera.depth          = 1;

        // targetTexture is assigned in SetMapBounds once bounds are known
    }

    // ?? Helpers ????????????????????????????????????????????????????????????

    private int WorldYToFloorIndex(float worldY)
        => Mathf.Max(0, Mathf.FloorToInt((worldY - groundFloorY) / floorHeight));

    private void OnDestroy()
    {
        if (renderTexture != null)
        {
            if (minimapCamera != null) minimapCamera.targetTexture = null;
            renderTexture.Release();
            Destroy(renderTexture);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!boundsReady) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(
            new Vector3(mapBoundsXZ.center.x,
                        floorplanRenderer != null ? floorplanRenderer.minimapSpriteY : 200f,
                        mapBoundsXZ.center.y),
            new Vector3(mapBoundsXZ.width, 0f, mapBoundsXZ.height));
    }
#endif
}