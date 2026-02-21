using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Displays player's display name as floating text above their character.
/// Attach to player prefab (local or remote).
/// </summary>
public class PlayerNameTag : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The userId this name tag represents")]
    public string userId;

    [Tooltip("Text component showing the name (will be created if null)")]
    public GameObject nameCanvas;
    public TMP_Text nameText;

    [Header("Settings")]
    [Tooltip("Offset from player position (typically above head)")]
    public Vector3 offset = new Vector3(0f, 1f, 0f);

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
    public Transform _mainCameraTransform;
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


        UpdateDisplayName();
    }

    void LateUpdate()
    {
        if (_isLocalPlayer && !showForLocalPlayer) return;

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
        if (_mainCameraTransform != null && nameText != null)
{
    Transform textTransform = nameText.transform;
    Transform parent = textTransform.parent;

    // Direction from text to camera
    Vector3 lookDir = textTransform.position - _mainCameraTransform.position;
    lookDir.y = 0f; // keep upright

    Quaternion worldLook = Quaternion.LookRotation(lookDir);

    if (parent != null)
    {
        // Cancel parent (bone) rotation
        textTransform.localRotation =
            Quaternion.Inverse(parent.rotation) * worldLook;
    }
    else
    {
        textTransform.rotation = worldLook;
    }

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
            if (nameCanvas != null) nameCanvas.SetActive(false);
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
        }
    }
}