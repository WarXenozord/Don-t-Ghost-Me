using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Full-screen map viewer with pan and zoom via uvRect.
/// Reuses the MinimapController's RenderTexture — no extra camera or rendering cost.
///
/// ?? Setup ????????????????????????????????????????????????????????????????????
///  1. Create a UI Panel (full screen, Canvas child) — assign to mapPanel.
///  2. Add a RawImage inside the panel — assign to mapRawImage.
///  3. Add a close Button — assign to closeButton.
///  4. Optionally add an open Button somewhere on the HUD — assign to openButton.
///  5. Assign minimapController.
///  6. The RawImage texture is set automatically from the minimap RT on Open().
///
/// ?? Controls ?????????????????????????????????????????????????????????????????
///  • Drag       ? pan
///  • Scroll wheel ? zoom (centred on cursor position)
///  • Keyboard shortcut (default M) or openButton ? toggle
/// </summary>
public class FullMapViewer : MonoBehaviour
{
    [Header("References")]
    public MinimapController minimapController;
    public GameObject        mapPanel;
    public RawImage          mapRawImage;
    [Tooltip("The minimap corner RawImage — hidden while the full map is open.")]
    public GameObject          minimapRawImage;
    public Button            openButton;
    public Button            closeButton;

    [Header("Controls")]
    [Tooltip("Keyboard shortcut to toggle the map.")]
    public KeyCode toggleKey = KeyCode.M;

    [Header("Zoom")]
    [Tooltip("World units visible when fully zoomed in.")]
    public float minViewportSize = 10f;
    [Tooltip("World units visible when fully zoomed out (shows entire map).")]
    public float maxViewportSize = 200f;
    [Tooltip("Starting viewport size when the map is first opened.")]
    public float defaultViewportSize = 60f;
    [Tooltip("Scroll wheel zoom sensitivity.")]
    public float zoomSpeed = 0.12f;
    [Tooltip("Zoom smoothing — 1 = instant, lower = smoother.")]
    [Range(0.05f, 1f)]
    public float zoomSmoothing = 0.15f;

    [Header("Pan")]
    [Tooltip("Pan smoothing — 1 = instant, lower = smoother.")]
    [Range(0.05f, 1f)]
    public float panSmoothing = 0.2f;

    // ?? Static flag — check this in your FPS camera script to block mouse look ??
    // Usage in your camera controller: if (FullMapViewer.IsOpen) return;
    public static bool IsOpen { get; private set; } = false;

    // ?? State ??????????????????????????????????????????????????????????????
    private bool             _isOpen          = false;
    private CursorLockMode   _prevLockState;
    private bool             _prevCursorVisible;
    private float  _currentViewport;           // world units currently visible
    private float  _targetViewport;
    private Vector2 _uvCenter;                 // UV-space centre of the view (0-1)
    private Vector2 _targetUvCenter;
    private bool   _isDragging      = false;
    private Vector2 _dragStartMouse;
    private Vector2 _dragStartUvCenter;

    // Cached map dimensions in world units (from MinimapController bounds)
    private float _mapWorldW;
    private float _mapWorldH;

    // ?? Unity lifecycle ????????????????????????????????????????????????????

    private void Awake()
    {
        if (openButton  != null) openButton.onClick.AddListener(Open);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (mapPanel    != null) mapPanel.SetActive(false);
    }

    private void Update()
    {
        if (!ChatUI.IsChatFocused && Input.GetKeyDown(toggleKey))
            Toggle();

        if (!_isOpen) return;

        HandleZoom();
        HandlePan();
        ApplySmoothing();
        ApplyUvRect();
    }

    // ?? Public API ?????????????????????????????????????????????????????????

    public void Open()
    {
        if (!minimapController.BoundsReady)
        {
            Debug.LogWarning("[FullMapViewer] Map bounds not ready yet.");
            return;
        }

        // Grab RT from the minimap controller (same texture, no extra rendering)
        if (mapRawImage != null && minimapController.MinimapRT != null)
            mapRawImage.texture = minimapController.MinimapRT;

        // Cache map world size for UV?world conversion
        var bounds  = minimapController.MapBoundsXZ;
        _mapWorldW  = bounds.width;
        _mapWorldH  = bounds.height;

        // Cap defaultViewportSize to the map's larger dimension
        float mapMax = Mathf.Max(_mapWorldW, _mapWorldH);
        maxViewportSize = Mathf.Max(maxViewportSize, mapMax);

        // Start centred, at default zoom
        _currentViewport = Mathf.Clamp(defaultViewportSize, minViewportSize, maxViewportSize);
        _targetViewport  = _currentViewport;
        _uvCenter        = new Vector2(0.5f, 0.5f);
        _targetUvCenter  = _uvCenter;

        mapPanel.SetActive(true);
        if (minimapRawImage != null) minimapRawImage.gameObject.SetActive(false);

        // Save cursor state then unlock so the player can click/drag the map
        _prevLockState      = Cursor.lockState;
        _prevCursorVisible  = Cursor.visible;
        Cursor.lockState    = CursorLockMode.None;
        Cursor.visible      = true;

        _isOpen  = true;
        IsOpen   = true;

        // Pause game while browsing — remove these two lines to keep game running
        Time.timeScale = 0f;
    }

    public void Close()
    {
        mapPanel.SetActive(false);
        if (minimapRawImage != null) minimapRawImage.gameObject.SetActive(true);

        // Restore cursor to whatever the game had before
        Cursor.lockState = _prevLockState;
        Cursor.visible   = _prevCursorVisible;

        _isOpen  = false;
        IsOpen   = false;
        Time.timeScale = 1f;
    }

    public void Toggle()
    {
        if (_isOpen) Close(); else Open();
    }

    // ?? Input handling ?????????????????????????????????????????????????????

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.001f) return;

        // Where in UV space is the cursor right now?
        Vector2 cursorUv = ScreenToUv(Input.mousePosition);

        // Zoom toward the cursor: shrink viewport, then adjust center so the
        // point under the cursor stays under the cursor.
        float oldViewport = _targetViewport;
        _targetViewport   = Mathf.Clamp(
            _targetViewport * (1f - scroll * zoomSpeed / zoomSmoothing),
            minViewportSize, maxViewportSize);

        float ratio = _targetViewport / oldViewport; // < 1 = zoom in, > 1 = zoom out
        // Move center toward cursor by the amount the viewport shrank
        _targetUvCenter = Vector2.Lerp(cursorUv, _targetUvCenter, ratio);
        _targetUvCenter = ClampUvCenter(_targetUvCenter, _targetViewport);
    }

    private void HandlePan()
    {
        if (Input.GetMouseButtonDown(0) && IsPointerOverMap())
        {
            _isDragging       = true;
            _dragStartMouse   = Input.mousePosition;
            _dragStartUvCenter = _targetUvCenter;
        }

        if (Input.GetMouseButtonUp(0))
            _isDragging = false;

        if (!_isDragging) return;

        Vector2 mouseDelta = (Vector2)Input.mousePosition - _dragStartMouse;

        // Convert pixel delta to UV delta:
        //   panel pixel size ? fraction of panel ? fraction of visible UV region ? UV delta
        RectTransform rt = mapRawImage.rectTransform;
        Vector2 panelSize = rt.rect.size;

        // Fraction of the panel we moved
        Vector2 uvFrac = new Vector2(
            mouseDelta.x / panelSize.x,
            mouseDelta.y / panelSize.y);

        // Visible UV size at current zoom
        float uvW = _currentViewport / _mapWorldW;
        float uvH = _currentViewport / _mapWorldH;

        // Panning right moves the view right ? we subtract (drag left to pan left)
        _targetUvCenter = _dragStartUvCenter - new Vector2(uvFrac.x * uvW, uvFrac.y * uvH);
        _targetUvCenter = ClampUvCenter(_targetUvCenter, _targetViewport);
    }

    // ?? Smoothing & uvRect ?????????????????????????????????????????????????

    private void ApplySmoothing()
    {
        // Use unscaled time so it works when timeScale = 0
        float dt = Time.unscaledDeltaTime;
        _currentViewport = Mathf.Lerp(_currentViewport, _targetViewport, 1f - Mathf.Pow(1f - zoomSmoothing, dt * 60f));
        _uvCenter        = Vector2.Lerp(_uvCenter,       _targetUvCenter, 1f - Mathf.Pow(1f - panSmoothing,  dt * 60f));
    }

    private void ApplyUvRect()
    {
        float fracX = Mathf.Clamp01(_currentViewport / _mapWorldW);
        float fracZ = Mathf.Clamp01(_currentViewport / _mapWorldH);

        float u = Mathf.Clamp01(_uvCenter.x - fracX * 0.5f);
        float v = Mathf.Clamp01(_uvCenter.y - fracZ * 0.5f);
        u = Mathf.Min(u, 1f - fracX);
        v = Mathf.Min(v, 1f - fracZ);

        mapRawImage.uvRect = new Rect(u, v, fracX, fracZ);
    }

    // ?? Helpers ????????????????????????????????????????????????????????????

    /// <summary>Clamp uvCenter so the visible window never scrolls outside [0,1].</summary>
    private Vector2 ClampUvCenter(Vector2 center, float viewportSize)
    {
        float halfU = (viewportSize / _mapWorldW) * 0.5f;
        float halfV = (viewportSize / _mapWorldH) * 0.5f;
        return new Vector2(
            Mathf.Clamp(center.x, halfU, 1f - halfU),
            Mathf.Clamp(center.y, halfV, 1f - halfV));
    }

    /// <summary>Convert a screen position to the UV coordinate it corresponds to on the mapRawImage.</summary>
    private Vector2 ScreenToUv(Vector2 screenPos)
    {
        RectTransform rt = mapRawImage.rectTransform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rt, screenPos, null, out Vector2 local);

        // local is in [-width/2, width/2] × [-height/2, height/2]
        Vector2 normalized = new Vector2(
            local.x / rt.rect.width  + 0.5f,
            local.y / rt.rect.height + 0.5f);

        // Map normalized panel position ? UV within current uvRect
        Rect uv = mapRawImage.uvRect;
        return new Vector2(
            uv.x + normalized.x * uv.width,
            uv.y + normalized.y * uv.height);
    }

    /// <summary>Returns true if the mouse is over the map RawImage (not a button on top of it).</summary>
    private bool IsPointerOverMap()
    {
        if (mapRawImage == null) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(
            mapRawImage.rectTransform, Input.mousePosition);
    }
}
