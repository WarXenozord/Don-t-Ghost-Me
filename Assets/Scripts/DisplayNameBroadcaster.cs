using UnityEngine;

/// <summary>
/// Broadcasts local player's display name to all players in match.
/// Listens for other players' display names and registers them.
/// Attach to a persistent GameObject (or MatchTransport).
/// </summary>
public class DisplayNameBroadcaster : MonoBehaviour
{
    public static DisplayNameBroadcaster Instance { get; private set; }

    [Header("References")]
    public MatchTransport transport;
    public NakamaConnection conn;
    public UsernameManager usernameManager;

    [Header("Settings")]
    [Tooltip("Broadcast display name this many seconds after joining match")]
    public float broadcastDelay = 0.5f;

    private bool _transportBound;
    private bool _hasBroadcastThisMatch;
    private string _currentMatchId;
    private float _broadcastTimer;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        ResolveRefs();
        EnsureBound();
    }

    void Update()
    {
        ResolveRefs();
        EnsureBound();

        // Check if we've switched matches
        if (conn != null && conn.Match != null)
        {
            string matchId = conn.Match.Id;
            if (_currentMatchId != matchId)
            {
                _currentMatchId = matchId;
                _hasBroadcastThisMatch = false;
                _broadcastTimer = broadcastDelay;
                Debug.Log($"[DisplayNameBroadcaster] Joined new match: {matchId}");
            }

            // Broadcast display name after delay
            if (!_hasBroadcastThisMatch)
            {
                _broadcastTimer -= Time.deltaTime;
                if (_broadcastTimer <= 0f)
                {
                    BroadcastLocalDisplayName();
                    _hasBroadcastThisMatch = true;
                }
            }
        }
        else
        {
            // Left match
            if (!string.IsNullOrEmpty(_currentMatchId))
            {
                _currentMatchId = string.Empty;
                _hasBroadcastThisMatch = false;
                
                // Clear remote names when leaving match
                if (usernameManager != null)
                {
                    usernameManager.ClearRemoteNames();
                }
            }
        }
    }

    void OnDestroy()
    {
        if (transport != null && _transportBound)
        {
            transport.OnDisplayName -= OnDisplayNameReceived;
            _transportBound = false;
        }
    }

    /// <summary>
    /// Manually trigger broadcast (call this after changing display name)
    /// </summary>
    public void BroadcastLocalDisplayName()
    {
        if (transport == null || conn == null || conn.Match == null)
        {
            Debug.LogWarning("[DisplayNameBroadcaster] Cannot broadcast: not in match");
            return;
        }

        if (usernameManager == null)
        {
            Debug.LogWarning("[DisplayNameBroadcaster] UsernameManager not found!");
            return;
        }

        string displayName = usernameManager.LocalDisplayName;
        if (string.IsNullOrEmpty(displayName))
        {
            Debug.LogWarning("[DisplayNameBroadcaster] Local display name is empty!");
            return;
        }

        var msg = new MatchTransport.DisplayNameMsg
        {
            displayName = displayName
        };

        transport.BroadcastDisplayName(msg);
        Debug.Log($"[DisplayNameBroadcaster] Broadcast display name: {displayName}");
    }

    private void OnDisplayNameReceived(MatchTransport.DisplayNameMsg msg)
    {
        if (msg == null || string.IsNullOrEmpty(msg.senderUserId))
        {
            Debug.LogWarning("[DisplayNameBroadcaster] Received invalid display name message");
            return;
        }

        // Don't register our own name
        if (conn != null && !string.IsNullOrEmpty(conn.SelfUserId) && msg.senderUserId == conn.SelfUserId)
        {
            Debug.Log("[DisplayNameBroadcaster] Ignoring own display name broadcast");
            return;
        }

        // Register the remote player's display name
        if (usernameManager != null)
        {
            usernameManager.RegisterUserDisplayName(msg.senderUserId, msg.displayName);
        }

        Debug.Log($"[DisplayNameBroadcaster] Registered {msg.senderUserId} as '{msg.displayName}'");
    }

    private void ResolveRefs()
    {
        if (transport == null)
            transport = MatchTransport.Instance != null ? MatchTransport.Instance : FindObjectOfType<MatchTransport>();
        
        if (conn == null)
            conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        
        if (usernameManager == null)
            usernameManager = UsernameManager.Instance != null ? UsernameManager.Instance : FindObjectOfType<UsernameManager>();
    }

    private void EnsureBound()
    {
        if (_transportBound || transport == null) return;
        transport.OnDisplayName += OnDisplayNameReceived;
        _transportBound = true;
        Debug.Log("[DisplayNameBroadcaster] Bound to MatchTransport");
    }
}