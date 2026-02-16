using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Fixed-camera minimap with uvRect panning and room-by-room reveal.
///
/// Both minimapCamera and revealCamera stay at the same fixed world position
/// (map centre, high above sprites) so their UVs always align — your reveal
/// shader works untouched.
///
/// Room reveal: each frame we check which BuildingRoom contains the player's
/// XZ position. On first entry, FloorplanRenderer.RevealRoom() is called,
/// permanently enabling that room's wall/door sprites on the minimap.
/// </summary>
[RequireComponent(typeof(FloorplanRenderer))]
public class MinimapController : MonoBehaviour
{
    [Header("References")]
    public Transform         player;
    public FloorplanRenderer floorplanRenderer;
    public RawImage          minimapRawImage;

    [Header("Cameras")]
    [Tooltip("The minimap camera (renders Minimap layer into RT).")]
    public Camera minimapCamera;
    [Tooltip("Your existing reveal camera — synced to same position/orthoSize as minimapCamera.")]
    public Camera revealCamera;

    [Header("Render Texture")]
    public int renderTextureSize = 1024;

    [Header("Viewport / Zoom")]
    [Tooltip("World units visible across the minimap window. Lower = more zoomed in.")]
    public float viewportWorldSize = 30f;
    [Tooltip("Extra padding around map bounds when fitting the camera.")]
    public float mapPadding = 5f;

    [Header("Building Info")]
    [Tooltip("Must match floorHeight in ProceduralBuildingGenerator.")]
    public float floorHeight  = 3.5f;
    public float groundFloorY = 0f;

    // ?? Map bounds ?????????????????????????????????????????????????????????
    private Rect mapBoundsXZ;
    private bool boundsReady = false;

    /// <summary>World-space XZ bounds of the entire map. Valid after SetMapBounds() is called.</summary>
    public Rect  MapBoundsXZ  => mapBoundsXZ;
    public bool  BoundsReady  => boundsReady;

    /// <summary>The RenderTexture shared by the minimap camera. Assign to any RawImage to reuse it.</summary>
    public RenderTexture MinimapRT => renderTexture;

    // ?? Room data ??????????????????????????????????????????????????????????
    private List<BuildingRoom> _rooms;
    private int _lastRoomIndex  = -1;
    private int _lastFloorIndex = -1;

    // ?? RT ?????????????????????????????????????????????????????????????????
    private RenderTexture renderTexture;

    // ?? Unity lifecycle ????????????????????????????????????????????????????

    private void Awake()
    {
        if (floorplanRenderer == null)
            floorplanRenderer = GetComponent<FloorplanRenderer>();
        SetupCamera();
    }

    private void LateUpdate()
    {
        if (player == null || minimapRawImage == null) return;

        if (!boundsReady)
        {
            Debug.LogWarning("[MinimapController] SetMapBounds() not called yet. " +
                             "Make sure MinimapController is assigned in the Generator inspector.");
            return;
        }

        // ?? Pan uvRect to centre on player ?????????????????????????????????
        float fracX = Mathf.Clamp01(viewportWorldSize / mapBoundsXZ.width);
        float fracZ = Mathf.Clamp01(viewportWorldSize / mapBoundsXZ.height);

        float playerU = (player.position.x - mapBoundsXZ.xMin) / mapBoundsXZ.width;
        float playerV = (player.position.z - mapBoundsXZ.yMin) / mapBoundsXZ.height;

        float u = Mathf.Clamp01(playerU - fracX * 0.5f);
        float v = Mathf.Clamp01(playerV - fracZ * 0.5f);
        u = Mathf.Min(u, 1f - fracX);
        v = Mathf.Min(v, 1f - fracZ);

        minimapRawImage.uvRect = new Rect(u, v, fracX, fracZ);

        // ?? Player marker ??????????????????????????????????????????????????
        floorplanRenderer.UpdatePlayerMarker(player.position);

        // ?? Floor detection ????????????????????????????????????????????????
        int currentFloor = WorldYToFloorIndex(player.position.y);
        if (currentFloor != _lastFloorIndex)
        {
            _lastFloorIndex = currentFloor;
            _lastRoomIndex  = -1; // force room re-check after floor change
            floorplanRenderer.SetActiveFloor(currentFloor);
        }

        // ?? Room reveal ????????????????????????????????????????????????????
        CheckCurrentRoom(currentFloor);
    }

    // ?? Room detection ?????????????????????????????????????????????????????

    private void CheckCurrentRoom(int floorIndex)
    {
        if (_rooms == null) return;

        float px = player.position.x;
        float pz = player.position.z;

        for (int i = 0; i < _rooms.Count; i++)
        {
            var room = _rooms[i];
            if (room.floorIndex != floorIndex) continue;

            // Point-in-rect on XZ
            if (px >= room.position.x && px <= room.position.x + room.size.x &&
                pz >= room.position.z && pz <= room.position.z + room.size.z)
            {
                if (i != _lastRoomIndex)
                {
                    _lastRoomIndex = i;
                    floorplanRenderer.RevealRoom(i);
                }
                return;
            }
        }
    }

    // ?? Public API ?????????????????????????????????????????????????????????

    /// <summary>
    /// Called by ProceduralBuildingGenerator after generation.
    /// Computes map bounds, positions both cameras, creates the RT.
    /// </summary>
    public void SetMapBounds(List<BuildingRoom> rooms)
    {
        if (rooms == null || rooms.Count == 0)
        {
            Debug.LogWarning("[MinimapController] SetMapBounds called with no rooms.");
            return;
        }

        _rooms = rooms;

        // ?? Compute XZ bounds ??????????????????????????????????????????????
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

        // ?? Position both cameras at map centre ????????????????????????????
        float camY    = floorplanRenderer.minimapSpriteY + 10f;
        float centreX = (minX + maxX) * 0.5f;
        float centreZ = (minZ + maxZ) * 0.5f;

        var camPos = new Vector3(centreX, camY, centreZ);
        var camRot = Quaternion.Euler(90f, 0f, 0f);

        minimapCamera.transform.SetPositionAndRotation(camPos, camRot);
        if (revealCamera != null)
            revealCamera.transform.SetPositionAndRotation(camPos, camRot);

        // ?? Fit ortho size to cover entire map ????????????????????????????
        float orthoSize = Mathf.Max(mapBoundsXZ.width, mapBoundsXZ.height) * 0.5f;
        minimapCamera.orthographicSize = orthoSize;
        if (revealCamera != null)
            revealCamera.orthographicSize = orthoSize;

        // ?? Build RT at correct aspect ratio (no squish) ???????????????????
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

        if (renderTexture != null)
        {
            minimapCamera.targetTexture = null;
            renderTexture.Release();
            Destroy(renderTexture);
        }

        renderTexture = new RenderTexture(rtW, rtH, 16)
        {
            name         = "MinimapRT",
            // Point filtering keeps lines sharp — Bilinear blurs thin wall quads
            filterMode   = FilterMode.Point,
            antiAliasing = 1
        };
        renderTexture.Create();
        minimapCamera.targetTexture = renderTexture;

        if (minimapRawImage != null)
        {
            minimapRawImage.texture = renderTexture;
            // Match filtering on the RawImage itself so the GPU doesn't re-blur on display
            minimapRawImage.material = null; // use default UI material
            minimapRawImage.texture.filterMode = FilterMode.Point;
        }
        else
            Debug.LogError("[MinimapController] minimapRawImage not assigned.");

        Debug.Log($"[MinimapController] Bounds X[{minX:F1}?{maxX:F1}] Z[{minZ:F1}?{maxZ:F1}] | " +
                  $"Camera at ({centreX:F1},{camY:F1},{centreZ:F1}) ortho={orthoSize:F1} | RT={rtW}×{rtH}");
    }

    // ?? Camera setup ???????????????????????????????????????????????????????

    private void SetupCamera()
    {
        if (minimapCamera == null)
        {
            var go = new GameObject("MinimapCamera");
            minimapCamera = go.AddComponent<Camera>();
            // Not parented — must never inherit rotation
        }

        minimapCamera.orthographic    = true;
        minimapCamera.orthographicSize = 100f; // overwritten by SetMapBounds
        minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        int minimapMask = LayerMask.GetMask("Minimap");
        if (minimapMask == 0)
            Debug.LogWarning("[MinimapController] 'Minimap' layer not found.");

        minimapCamera.cullingMask     = minimapMask;
        minimapCamera.clearFlags      = CameraClearFlags.SolidColor;
        minimapCamera.backgroundColor = Color.clear;
        minimapCamera.depth           = 1;

        if (revealCamera != null)
            revealCamera.orthographicSize = 100f;
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
            new Vector3(mapBoundsXZ.width, 1f, mapBoundsXZ.height));
    }
#endif
}