using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages player display names (separate from Nakama userId).
/// Stores locally in PlayerPrefs and syncs across network.
/// Singleton - persists across scenes.
/// </summary>
public class UsernameManager : MonoBehaviour
{
    public static UsernameManager Instance { get; private set; }

    private const string PREF_KEY_USERNAME = "player_display_name";
    private const int MAX_USERNAME_LENGTH = 16;
    private const int MIN_USERNAME_LENGTH = 3;

    [Header("Current Display Name")]
    [SerializeField] private string _localDisplayName = "";

    // Maps userId ? displayName for all players in current match
    private readonly Dictionary<string, string> _userDisplayNames = new Dictionary<string, string>();

    public string LocalDisplayName => _localDisplayName;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadLocalUsername();
    }

    /// <summary>
    /// Loads username from PlayerPrefs. Creates default if none exists.
    /// </summary>
    private void LoadLocalUsername()
    {
        if (PlayerPrefs.HasKey(PREF_KEY_USERNAME))
        {
            _localDisplayName = PlayerPrefs.GetString(PREF_KEY_USERNAME);
        }
        else
        {
            // Generate default name
            _localDisplayName = GenerateDefaultUsername();
            SaveLocalUsername();
        }

        Debug.Log($"[UsernameManager] Loaded display name: {_localDisplayName}");
    }

    /// <summary>
    /// Saves current username to PlayerPrefs
    /// </summary>
    private void SaveLocalUsername()
    {
        PlayerPrefs.SetString(PREF_KEY_USERNAME, _localDisplayName);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Changes the local player's display name.
    /// Returns true if valid and saved, false if invalid.
    /// </summary>
    public bool SetLocalDisplayName(string newName)
    {
        if (!IsValidUsername(newName, out string error))
        {
            Debug.LogWarning($"[UsernameManager] Invalid username: {error}");
            return false;
        }

        _localDisplayName = newName.Trim();
        SaveLocalUsername();
        Debug.Log($"[UsernameManager] Display name changed to: {_localDisplayName}");
        return true;
    }

    /// <summary>
    /// Stores a remote player's display name
    /// </summary>
    public void RegisterUserDisplayName(string userId, string displayName)
    {
        if (string.IsNullOrEmpty(userId)) return;

        _userDisplayNames[userId] = displayName ?? "Unknown";
        Debug.Log($"[UsernameManager] Registered {userId} as '{displayName}'");
    }

    /// <summary>
    /// Gets display name for a user. Returns userId if unknown.
    /// </summary>
    public string GetDisplayName(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return "Unknown";

        // Check if it's the local player
        var conn = NakamaConnection.Instance;
        if (conn != null && !string.IsNullOrEmpty(conn.SelfUserId) && userId == conn.SelfUserId)
        {
            return _localDisplayName;
        }

        // Check remote players
        if (_userDisplayNames.TryGetValue(userId, out string displayName))
        {
            return displayName;
        }

        // Fallback: show short userId
        return ShortId(userId);
    }

    /// <summary>
    /// Clears all cached remote display names (call when leaving match)
    /// </summary>
    public void ClearRemoteNames()
    {
        _userDisplayNames.Clear();
        Debug.Log("[UsernameManager] Cleared remote display names");
    }

    /// <summary>
    /// Validates a username
    /// </summary>
    public static bool IsValidUsername(string username, out string error)
    {
        error = "";

        if (string.IsNullOrEmpty(username))
        {
            error = "Username cannot be empty";
            return false;
        }

        var trimmed = username.Trim();

        if (trimmed.Length < MIN_USERNAME_LENGTH)
        {
            error = $"Username must be at least {MIN_USERNAME_LENGTH} characters";
            return false;
        }

        if (trimmed.Length > MAX_USERNAME_LENGTH)
        {
            error = $"Username cannot exceed {MAX_USERNAME_LENGTH} characters";
            return false;
        }

        // Check for invalid characters
        foreach (char c in trimmed)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != ' ')
            {
                error = "Username can only contain letters, numbers, spaces, _ and -";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Generates a default username like "Player_123"
    /// </summary>
    private string GenerateDefaultUsername()
    {
        int randomNum = Random.Range(100, 10000);
        return $"Player_{randomNum}";
    }

    /// <summary>
    /// Gets short version of userId for fallback display
    /// </summary>
    private static string ShortId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "------";
        return id.Length <= 8 ? id : id.Substring(0, 8);
    }

    /// <summary>
    /// Debug: Force regenerate username
    /// </summary>
    [ContextMenu("Regenerate Username")]
    public void RegenerateUsername()
    {
        _localDisplayName = GenerateDefaultUsername();
        SaveLocalUsername();
        Debug.Log($"[UsernameManager] Regenerated username: {_localDisplayName}");
    }
}