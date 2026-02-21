using UnityEngine;
using UnityEngine.SceneManagement;

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
    public FloorProgressionManager progressionManager;
    public PlayerDeathTracker deathTracker;

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
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void Update()
    {
        ResolveRefs();
        EnsureBound();
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
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
        ResolveRefs();

        var isHost = conn != null && conn.IsCurrentPlayerMatchCreator;
        var canTransition = false;
        var nextFloor = 0;
        var nextRooms = 0;
        var nextEnemies = 0;
        var nextSeed = 0;

        if (isHost)
        {
            canTransition = HasAliveMediums();
            nextSeed = BuildNextSeed();

            if (canTransition && progressionManager != null)
            {
                if (!progressionManager.RunActive) progressionManager.StartNewRun();
                progressionManager.AdvanceToNextFloor();
                nextFloor = progressionManager.CurrentFloor;
                nextRooms = progressionManager.CurrentRoomCount;
                nextEnemies = progressionManager.CurrentEnemyCount;
            }
            else if (progressionManager != null)
            {
                nextFloor = progressionManager.CurrentFloor;
                nextRooms = progressionManager.CurrentRoomCount;
                nextEnemies = progressionManager.CurrentEnemyCount;
            }
        }

        var msg = new MatchTransport.RitualCompleteMsg
        {
            initId = initId,
            shouldTransition = canTransition,
            nextSeed = nextSeed,
            nextFloor = nextFloor,
            nextRoomCount = nextRooms,
            nextEnemyCount = nextEnemies
        };

        transport.BroadcastRitualComplete(msg);

        if (enableDebugLogs)
        {
            Debug.Log("[RitualCompletion] Broadcast ritual completion");
        }

        if (isHost)
        {
            ProcessRitualCompletion(msg);
        }
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

        // Non-authoritative ritual trigger request (usually from a non-host) should not
        // mark this floor as completed on clients; wait for host payload.
        if (!msg.shouldTransition && msg.nextFloor <= 0)
        {
            if (conn != null && conn.IsCurrentPlayerMatchCreator)
            {
                BroadcastRitualCompletion();
            }
            return;
        }

        // Host receives client ritual request and rebroadcasts authoritative transition payload.
        ProcessRitualCompletion(msg);
    }

    /// <summary>
    /// Processes the ritual completion and triggers floor transition (host only)
    /// </summary>
    private void ProcessRitualCompletion(MatchTransport.RitualCompleteMsg msg)
    {
        if (msg == null) return;

        // Only process once per floor
        if (_ritualCompletedThisFloor)
        {
            if (enableDebugLogs)
                Debug.Log("[RitualCompletion] Ritual already completed this floor, ignoring");
            return;
        }

        _ritualCompletedThisFloor = true;

        if (!msg.shouldTransition)
        {
            if (enableDebugLogs)
                Debug.Log("[RitualCompletion] Ritual complete but no alive medium. Transition skipped.");
            return;
        }

        if (transitionManager != null)
        {
            if (enableDebugLogs)
                Debug.Log("[RitualCompletion] Triggering synced floor transition");
            
            transitionManager.TriggerFloorTransitionSynced(
                msg.nextFloor,
                msg.nextRoomCount,
                msg.nextEnemyCount,
                msg.nextSeed);
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

    private void OnSceneLoaded(Scene _, LoadSceneMode __)
    {
        _ritualCompletedThisFloor = false;
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
        
        if (progressionManager == null)
            progressionManager = FloorProgressionManager.Instance != null ? FloorProgressionManager.Instance : FindObjectOfType<FloorProgressionManager>();
        
        if (deathTracker == null)
            deathTracker = PlayerDeathTracker.Instance != null ? PlayerDeathTracker.Instance : FindObjectOfType<PlayerDeathTracker>();
    }

    private void EnsureBound()
    {
        if (_transportBound || transport == null) return;
        transport.OnRitualComplete += OnRitualCompleteReceived;
        _transportBound = true;
        if (enableDebugLogs)
            Debug.Log("[RitualCompletion] Bound to MatchTransport");
    }

    private bool HasAliveMediums()
    {
        if (deathTracker == null) return true;
        return deathTracker.GetAlivePlayerCount() > 0;
    }

    private int BuildNextSeed()
    {
        var context = MatchContext.Instance;
        var baseSeed = (context != null && context.lastInit != null) ? context.lastInit.seed : Random.Range(1, int.MaxValue);
        var floor = progressionManager != null ? progressionManager.CurrentFloor + 1 : 1;
        unchecked
        {
            return Mathf.Abs(baseSeed * 1103515245 + 12345 + floor * 7919);
        }
    }
}
