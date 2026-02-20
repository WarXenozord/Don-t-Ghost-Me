using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Simple lobby UI that displays all players in the current match with their display names.
/// Shows who's in the match before gameplay starts.
/// </summary>
public class LobbyPlayerList : MonoBehaviour
{
    [Header("UI References")]
    public Text playerListText;
    public Text matchInfoText;
    
    [Header("Settings")]
    public float updateInterval = 0.5f; // Update list every X seconds

    [Header("Style")]
    public string localPlayerPrefix = "? ";
    public string remotePlayerPrefix = "  ";
    public string localPlayerSuffix = " (You)";

    private NakamaConnection _conn;
    private UsernameManager _usernameManager;
    private float _updateTimer;

    void Start()
    {
        _conn = NakamaConnection.Instance;
        _usernameManager = UsernameManager.Instance;
        UpdatePlayerList();
    }

    void Update()
    {
        _updateTimer += Time.deltaTime;
        if (_updateTimer >= updateInterval)
        {
            _updateTimer = 0f;
            UpdatePlayerList();
        }
    }

    private void UpdatePlayerList()
    {
        if (playerListText == null) return;

        if (_conn == null)
        {
            _conn = NakamaConnection.Instance;
        }

        if (_usernameManager == null)
        {
            _usernameManager = UsernameManager.Instance;
        }

        // Not in a match
        if (_conn == null || _conn.Match == null)
        {
            playerListText.text = "Not in a match";
            if (matchInfoText != null)
            {
                matchInfoText.text = "";
            }
            return;
        }

        // Collect all player userIds
        var playerIds = new HashSet<string>();

        // Add all presences
        if (_conn.Match.Presences != null)
        {
            foreach (var presence in _conn.Match.Presences)
            {
                if (presence != null && !string.IsNullOrEmpty(presence.UserId))
                {
                    playerIds.Add(presence.UserId);
                }
            }
        }

        // Add self if not already included
        if (!string.IsNullOrEmpty(_conn.SelfUserId))
        {
            playerIds.Add(_conn.SelfUserId);
        }

        // Build player list string
        string list = "";
        int playerCount = 0;

        // Sort: local player first, then alphabetically
        var sortedPlayers = new List<string>(playerIds);
        sortedPlayers.Sort((a, b) =>
        {
            // Local player always first
            if (a == _conn.SelfUserId) return -1;
            if (b == _conn.SelfUserId) return 1;

            // Then alphabetically by display name
            string nameA = _usernameManager != null ? _usernameManager.GetDisplayName(a) : a;
            string nameB = _usernameManager != null ? _usernameManager.GetDisplayName(b) : b;
            return string.Compare(nameA, nameB, System.StringComparison.OrdinalIgnoreCase);
        });

        foreach (var userId in sortedPlayers)
        {
            string displayName = _usernameManager != null 
                ? _usernameManager.GetDisplayName(userId) 
                : ShortId(userId);

            bool isLocal = userId == _conn.SelfUserId;

            if (isLocal)
            {
                list += localPlayerPrefix + displayName + localPlayerSuffix + "\n";
            }
            else
            {
                list += remotePlayerPrefix + displayName + "\n";
            }

            playerCount++;
        }

        if (string.IsNullOrEmpty(list))
        {
            list = "No players in match";
        }

        playerListText.text = list.TrimEnd('\n');

        // Update match info
        if (matchInfoText != null)
        {
            string matchId = _conn.Match.Id;
            string shortMatchId = matchId.Length > 8 ? matchId.Substring(0, 8) : matchId;
            matchInfoText.text = $"Match: {shortMatchId}\nPlayers: {playerCount}";
        }
    }

    private static string ShortId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "------";
        return id.Length <= 8 ? id : id.Substring(0, 8);
    }

    /// <summary>
    /// Force immediate update (call when players join/leave)
    /// </summary>
    public void ForceUpdate()
    {
        UpdatePlayerList();
    }
}