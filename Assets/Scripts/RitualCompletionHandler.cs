using UnityEngine;

/// <summary>
/// Listens for ritual completion messages and triggers floor transition (host only).
/// Ensures only one player needs to complete the ritual to advance everyone.
/// </summary>
public class RitualCompletionHandler : MonoBehaviour
{
    public static RitualCompletionHandler Instance { get; private set; }

    [Header("References")]
    public MatchTransport transport;
    public NakamaConnection conn;
    public HostAuthority hostAuthority;
    public FloorTransitionManager transitionManager;

    [Header("Debug")]
    public bool enableDebugLogs = true;

    private bool _transportBound;
    private bool _ritualCompletedThisFloor;

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
    }

    void OnDestroy()
    {
        if (transport != null && _transportBound)
        {
            transport.OnRitualComplete -= OnRitualCompleteReceived;
            _transportBound = false;
        }
    }

    /// <summary>
    /// Broadcasts ritual completion to all players in match.
    /// Call this from RitualMark when ritual is triggered.
    /// </summary>
    public void BroadcastRitualCompletion()
    {
        if (transport == null || conn == null || conn.Match == null)
        {
            Debug.LogWarning("[RitualCompletion] Cannot broadcast: not in match");
            return;
        }

        if (hostAuthority == null)
            hostAuthority = FindObjectOfType<HostAuthority>();

        int initId = hostAuthority != null ? hostAuthority.ActiveInitId : -1;

        var msg = new MatchTransport.RitualCompleteMsg
        {
            initId = initId
        };

        transport.BroadcastRitualComplete(msg);

        if (enableDebugLogs)
        {
            Debug.Log("[RitualCompletion] Broadcast ritual completion");
        }

        // Also process locally immediately (don't wait for network echo)
        ProcessRitualCompletion();
    }

    /// <summary>
    /// Handles ritual completion message from network
    /// </summary>
    private void OnRitualCompleteReceived(MatchTransport.RitualCompleteMsg msg)
    {
        if (msg == null)
        {
            Debug.LogWarning("[RitualCompletion] Received null ritual complete message");
            return;
        }

        // Verify init ID matches
        if (hostAuthority != null && msg.initId != hostAuthority.ActiveInitId)
        {
            if (enableDebugLogs)
                Debug.Log($"[RitualCompletion] Ignoring ritual complete from wrong init ({msg.initId} != {hostAuthority.ActiveInitId})");
            return;
        }

        if (enableDebugLogs)
        {
            Debug.Log($"[RitualCompletion] Received ritual completion from {msg.senderUserId}");
        }

        ProcessRitualCompletion();
    }

    /// <summary>
    /// Processes the ritual completion and triggers floor transition (host only)
    /// </summary>
    private void ProcessRitualCompletion()
    {
        // Only process once per floor
        if (_ritualCompletedThisFloor)
        {
            if (enableDebugLogs)
                Debug.Log("[RitualCompletion] Ritual already completed this floor, ignoring");
            return;
        }

        _ritualCompletedThisFloor = true;

        // Only host triggers the actual floor transition
        if (conn != null && !conn.IsCurrentPlayerMatchCreator)
        {
            if (enableDebugLogs)
                Debug.Log("[RitualCompletion] Non-host received ritual completion - waiting for host to trigger transition");
            return;
        }

        // Host: Trigger floor transition
        if (transitionManager == null)
            transitionManager = FloorTransitionManager.Instance;

        if (transitionManager != null)
        {
            if (enableDebugLogs)
                Debug.Log("[RitualCompletion] Host triggering floor transition");
            
            transitionManager.TriggerFloorTransition();
        }
        else
        {
            Debug.LogError("[RitualCompletion] FloorTransitionManager not found!");
        }
    }

    /// <summary>
    /// Resets the completion flag (call when loading new floor)
    /// </summary>
    public void ResetForNewFloor()
    {
        _ritualCompletedThisFloor = false;
        if (enableDebugLogs)
            Debug.Log("[RitualCompletion] Reset for new floor");
    }

    private void ResolveRefs()
    {
        if (transport == null)
            transport = MatchTransport.Instance != null ? MatchTransport.Instance : FindObjectOfType<MatchTransport>();
        
        if (conn == null)
            conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        
        if (hostAuthority == null)
            hostAuthority = FindObjectOfType<HostAuthority>();
        
        if (transitionManager == null)
            transitionManager = FloorTransitionManager.Instance != null ? FloorTransitionManager.Instance : FindObjectOfType<FloorTransitionManager>();
    }

    private void EnsureBound()
    {
        if (_transportBound || transport == null) return;
        transport.OnRitualComplete += OnRitualCompleteReceived;
        _transportBound = true;
        if (enableDebugLogs)
            Debug.Log("[RitualCompletion] Bound to MatchTransport");
    }
}