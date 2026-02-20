using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays player's display name as floating text above their character.
/// Attach to player prefab (local or remote).
/// </summary>
public class PlayerNameTag : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The userId this name tag represents")]
    public string userId;

    [Tooltip("Canvas with name text (will be created if null)")]
    public Canvas nameCanvas;

    [Tooltip("Text component showing the name (will be created if null)")]
    public Text nameText;

    [Header("Settings")]
    [Tooltip("Offset from player position (typically above head)")]
    public Vector3 offset = new Vector3(0f, 2.5f, 0f);

    [Tooltip("Name tag scale")]
    public float scale = 0.02f;

    [Tooltip("Show name tag for local player?")]
    public bool showForLocalPlayer = false;

    [Tooltip("Maximum distance to show name tag (0 = always show)")]
    public float maxDistance = 50f;

    [Header("Style")]
    public Color nameColor = Color.white;
    public int fontSize = 24;
    public FontStyle fontStyle = FontStyle.Bold;

    [Header("Background")]
    public bool showBackground = true;
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.5f);

    private UsernameManager _usernameManager;
    private Transform _mainCameraTransform;
    private bool _isLocalPlayer;
    private string _cachedDisplayName;

    void Start()
    {
        _usernameManager = UsernameManager.Instance;
        _mainCameraTransform = Camera.main != null ? Camera.main.transform : null;

        // Determine if this is the local player
        var conn = NakamaConnection.Instance;
        _isLocalPlayer = conn != null && !string.IsNullOrEmpty(conn.SelfUserId) && userId == conn.SelfUserId;

        // Hide name tag for local player if configured
        if (_isLocalPlayer && !showForLocalPlayer)
        {
            gameObject.SetActive(false);
            return;
        }

        // Create UI if not assigned
        if (nameCanvas == null || nameText == null)
        {
            CreateNameTagUI();
        }

        UpdateDisplayName();
    }

    void LateUpdate()
    {
        if (_isLocalPlayer && !showForLocalPlayer) return;
        if (nameCanvas == null) return;

        // Update name if it changed
        if (_usernameManager != null)
        {
            string currentName = _usernameManager.GetDisplayName(userId);
            if (currentName != _cachedDisplayName)
            {
                UpdateDisplayName();
            }
        }

        // Billboard effect - always face camera
        if (_mainCameraTransform != null)
        {
            nameCanvas.transform.rotation = _mainCameraTransform.rotation;
        }

        // Distance culling
        if (maxDistance > 0f && _mainCameraTransform != null)
        {
            float dist = Vector3.Distance(transform.position, _mainCameraTransform.position);
            nameCanvas.enabled = dist <= maxDistance;
        }
    }

    /// <summary>
    /// Updates the displayed name from UsernameManager
    /// </summary>
    public void UpdateDisplayName()
    {
        if (nameText == null) return;
        if (_usernameManager == null)
        {
            _usernameManager = UsernameManager.Instance;
        }

        if (_usernameManager != null && !string.IsNullOrEmpty(userId))
        {
            _cachedDisplayName = _usernameManager.GetDisplayName(userId);
            nameText.text = _cachedDisplayName;
        }
        else
        {
            nameText.text = "...";
        }
    }

    /// <summary>
    /// Creates the UI canvas and text if not already present
    /// </summary>
    private void CreateNameTagUI()
    {
        // Create canvas GameObject
        var canvasGO = new GameObject("NameTagCanvas");
        canvasGO.transform.SetParent(transform, worldPositionStays: false);
        canvasGO.transform.localPosition = offset;
        canvasGO.transform.localRotation = Quaternion.identity;
        canvasGO.transform.localScale = Vector3.one * scale;

        // Setup Canvas component
        nameCanvas = canvasGO.AddComponent<Canvas>();
        nameCanvas.renderMode = RenderMode.WorldSpace;

        var canvasScaler = canvasGO.AddComponent<CanvasScaler>();
        canvasScaler.dynamicPixelsPerUnit = 10f;

        // Canvas size
        var rectTransform = canvasGO.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(200f, 50f);

        // Background panel (optional)
        if (showBackground)
        {
            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(canvasGO.transform, worldPositionStays: false);

            var bgImage = bgGO.AddComponent<Image>();
            bgImage.color = backgroundColor;

            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero; // Stretch to fill parent
        }

        // Text GameObject
        var textGO = new GameObject("NameText");
        textGO.transform.SetParent(canvasGO.transform, worldPositionStays: false);

        nameText = textGO.AddComponent<Text>();
        nameText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        nameText.text = "Player";
        nameText.fontSize = fontSize;
        nameText.fontStyle = fontStyle;
        nameText.color = nameColor;
        nameText.alignment = TextAnchor.MiddleCenter;

        var textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero; // Stretch to fill parent

        Debug.Log($"[PlayerNameTag] Created name tag UI for {userId}");
    }

    /// <summary>
    /// Call this to manually set the userId (for dynamically spawned players)
    /// </summary>
    public void SetUserId(string newUserId)
    {
        userId = newUserId;

        // Check if local player
        var conn = NakamaConnection.Instance;
        _isLocalPlayer = conn != null && !string.IsNullOrEmpty(conn.SelfUserId) && userId == conn.SelfUserId;

        // Hide for local if configured
        if (_isLocalPlayer && !showForLocalPlayer)
        {
            if (nameCanvas != null) nameCanvas.enabled = false;
            return;
        }

        UpdateDisplayName();
    }

    void OnValidate()
    {
        // Update style in editor
        if (nameText != null)
        {
            nameText.color = nameColor;
            nameText.fontSize = fontSize;
            nameText.fontStyle = fontStyle;
        }
    }
}