using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Host-authoritative player death counter.
/// - Scene start: alive count = total players from Init spawns.
/// - On host-confirmed death: alive count decrements once per user.
/// - Host broadcasts alive count to clients.
/// - On scene reload: counter resets and re-initializes from Init spawns.
/// </summary>
public class PlayerDeathTracker : MonoBehaviour
{
    public static PlayerDeathTracker Instance { get; private set; }

    [Header("References")]
    public NakamaConnection conn;
    public MatchTransport transport;
    public FloorProgressionManager progressionManager;

    [Header("Game Over")]
    public string gameOverScene = "GameOver";
    public float gameOverDelay = 3f;

    [Header("Debug")]
    public bool enableDebugLogs = true;

    private readonly HashSet<string> _deadPlayers = new HashSet<string>();
    private bool _countInitialized;
    private int _aliveCount;
    private int _totalCount;

    private bool _gameOverTriggered;
    private float _gameOverTimer;
    private bool _boundTransport;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        ResolveRefs();
        EnsureBound();
        ResetForNewLevel();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (transport != null && _boundTransport)
        {
            transport.OnAliveCount -= OnAliveCountReceived;
            _boundTransport = false;
        }
    }

    void Update()
    {
        ResolveRefs();
        EnsureBound();
        TryInitializeCount();

        if (_gameOverTriggered)
        {
            _gameOverTimer -= Time.deltaTime;
            if (_gameOverTimer <= 0f)
            {
                LoadGameOverScene();
            }
        }
    }

    /// <summary>
    /// Host-only call: invoked when a player truly dies (second touch -> ghost spawn).
    /// </summary>
    public void RegisterPlayerDead(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return;
        if (!IsHost()) return;

        TryInitializeCount();
        if (!_countInitialized) return;
        if (_deadPlayers.Contains(userId)) return;

        _deadPlayers.Add(userId);
        _aliveCount = Mathf.Max(0, _aliveCount - 1);

        if (enableDebugLogs)
        {
            Debug.Log("[DeathTracker] HOST_DEATH user=" + userId + " alive=" + _aliveCount + "/" + _totalCount);
        }

        BroadcastAliveCount();
        CheckForGameOver();
    }

    /// <summary>
    /// Resets internal state so next scene initializes a fresh count.
    /// </summary>
    public void ResetTracker()
    {
        ResetForNewLevel();
    }

    public int GetAlivePlayerCount()
    {
        return Mathf.Max(0, _aliveCount);
    }

    public int GetDeadPlayerCount()
    {
        var dead = Mathf.Max(0, _totalCount - _aliveCount);
        return dead;
    }

    [ContextMenu("Force Game Over")]
    public void DebugForceGameOver()
    {
        TriggerGameOver();
    }

    private void TryInitializeCount()
    {
        if (_countInitialized) return;

        var context = MatchContext.Instance;
        if (context == null || context.lastInit == null || context.lastInit.spawns == null || context.lastInit.spawns.Length == 0)
        {
            return;
        }

        var uniqueUsers = new HashSet<string>();
        var spawns = context.lastInit.spawns;
        for (var i = 0; i < spawns.Length; i++)
        {
            var s = spawns[i];
            if (s == null || string.IsNullOrEmpty(s.userId)) continue;
            uniqueUsers.Add(s.userId);
        }

        if (uniqueUsers.Count <= 0) return;

        _deadPlayers.Clear();
        _totalCount = uniqueUsers.Count;
        _aliveCount = _totalCount;
        _countInitialized = true;
        _gameOverTriggered = false;
        _gameOverTimer = 0f;

        if (enableDebugLogs)
        {
            Debug.Log("[DeathTracker] INIT_COUNT alive=" + _aliveCount + "/" + _totalCount);
        }

        if (IsHost())
        {
            BroadcastAliveCount();
        }
    }

    private void BroadcastAliveCount()
    {
        if (!IsHost()) return;
        if (transport == null || conn == null || conn.Match == null) return;

        var context = MatchContext.Instance;
        var initId = (context != null && context.lastInit != null) ? context.lastInit.initId : -1;
        transport.BroadcastAliveCount(new MatchTransport.AliveCountMsg
        {
            initId = initId,
            aliveCount = _aliveCount,
            totalCount = _totalCount
        });
    }

    private void OnAliveCountReceived(MatchTransport.AliveCountMsg msg)
    {
        if (msg == null) return;
        if (IsHost()) return;
        if (msg.totalCount <= 0) return;

        var context = MatchContext.Instance;
        var localInitId = (context != null && context.lastInit != null) ? context.lastInit.initId : -1;
        if (localInitId >= 0 && msg.initId >= 0 && msg.initId != localInitId) return;

        _totalCount = Mathf.Max(0, msg.totalCount);
        _aliveCount = Mathf.Clamp(msg.aliveCount, 0, _totalCount);
        _countInitialized = true;

        if (enableDebugLogs)
        {
            Debug.Log("[DeathTracker] SYNC_COUNT alive=" + _aliveCount + "/" + _totalCount);
        }

        CheckForGameOver();
    }

    private void CheckForGameOver()
    {
        if (_gameOverTriggered) return;
        if (!_countInitialized) return;
        if (_totalCount <= 0) return;
        if (_aliveCount > 0) return;

        TriggerGameOver();
    }

    private void TriggerGameOver()
    {
        if (_gameOverTriggered) return;

        _gameOverTriggered = true;
        _gameOverTimer = gameOverDelay;

        if (enableDebugLogs)
        {
            Debug.Log("[DeathTracker] GAME OVER! alive=0/" + _totalCount + " loading in " + gameOverDelay + "s");
        }

        if (progressionManager != null)
        {
            progressionManager.EndRun();
        }
    }

    private void LoadGameOverScene()
    {
        if (enableDebugLogs)
        {
            Debug.Log("[DeathTracker] Loading game over scene: " + gameOverScene);
        }

        if (!string.IsNullOrEmpty(gameOverScene))
        {
            SceneManager.LoadScene(gameOverScene);
        }
    }

    private void OnSceneLoaded(Scene _, LoadSceneMode __)
    {
        ResetForNewLevel();
    }

    private void ResetForNewLevel()
    {
        _deadPlayers.Clear();
        _countInitialized = false;
        _aliveCount = 0;
        _totalCount = 0;
        _gameOverTriggered = false;
        _gameOverTimer = 0f;

        if (enableDebugLogs)
        {
            Debug.Log("[DeathTracker] Reset for new level.");
        }
    }

    private bool IsHost()
    {
        return conn != null && conn.IsCurrentPlayerMatchCreator;
    }

    private void ResolveRefs()
    {
        if (conn == null)
            conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();

        if (transport == null)
            transport = MatchTransport.Instance != null ? MatchTransport.Instance : FindObjectOfType<MatchTransport>();

        if (progressionManager == null)
            progressionManager = FloorProgressionManager.Instance != null ? FloorProgressionManager.Instance : FindObjectOfType<FloorProgressionManager>();
    }

    private void EnsureBound()
    {
        if (_boundTransport || transport == null) return;
        transport.OnAliveCount += OnAliveCountReceived;
        _boundTransport = true;
    }
}
