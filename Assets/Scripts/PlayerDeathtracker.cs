using System.Collections.Generic;
using Nakama;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Host-authoritative death counter.
/// Expected player set is built from:
/// 1) Match init spawns
/// 2) Presence join/leave events
/// 3) Spawned player objects (fallback robustness)
/// </summary>
public class PlayerDeathTracker : MonoBehaviour
{
    public static PlayerDeathTracker Instance { get; private set; }

    [Header("References")]
    public NakamaConnection conn;
    public MatchTransport transport;
    public PlayerSpawnManager playerSpawner;
    public FloorProgressionManager progressionManager;

    [Header("Game Over")]
    public string gameOverScene = "GameOver";
    public float gameOverDelay = 3f;

    [Header("Debug")]
    public bool enableDebugLogs = true;
    public float hostRecountInterval = 0.5f;

    private readonly HashSet<string> _expectedUsers = new HashSet<string>();
    private readonly HashSet<string> _deadPlayers = new HashSet<string>();
    private readonly List<PlayerSpawnManager.SpawnedPlayerInfo> _spawnedBuffer = new List<PlayerSpawnManager.SpawnedPlayerInfo>();

    private bool _countInitialized;
    private int _aliveCount;
    private int _totalCount;

    private bool _gameOverTriggered;
    private float _gameOverTimer;
    private float _nextHostRecountAt;

    private bool _boundTransport;
    private bool _boundPresence;

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

        if (conn != null && _boundPresence)
        {
            conn.MatchPresenceReceived -= OnPresenceChanged;
            _boundPresence = false;
        }
    }

    void Update()
    {
        ResolveRefs();
        EnsureBound();
        TryInitializeCount();

        if (IsHost() && _countInitialized && Time.unscaledTime >= _nextHostRecountAt)
        {
            _nextHostRecountAt = Time.unscaledTime + Mathf.Max(0.1f, hostRecountInterval);
            RefreshExpectedUsersAndCountsFromAllSources(broadcastIfChanged: true);
        }

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
    /// Host-only call: invoked when player truly dies (second touch).
    /// </summary>
    public void RegisterPlayerDead(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return;
        if (!IsHost()) return;

        TryInitializeCount();
        if (!_countInitialized) return;
        if (_deadPlayers.Contains(userId)) return;

        _expectedUsers.Add(userId);
        _deadPlayers.Add(userId);
        RecomputeCountsAndMaybeBroadcast(true);

        if (enableDebugLogs)
        {
            Debug.Log("[DeathTracker] HOST_DEATH user=" + userId + " alive=" + _aliveCount + "/" + _totalCount);
        }
    }

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
        return Mathf.Max(0, _totalCount - _aliveCount);
    }

    [ContextMenu("Force Game Over")]
    public void DebugForceGameOver()
    {
        TriggerGameOver();
    }

    private void OnPresenceChanged(IMatchPresenceEvent e)
    {
        if (!IsHost()) return;
        if (e == null) return;

        var changed = false;
        if (e.Joins != null)
        {
            foreach (var join in e.Joins)
            {
                if (join == null || string.IsNullOrEmpty(join.UserId)) continue;
                changed |= _expectedUsers.Add(join.UserId);
            }
        }

        if (e.Leaves != null)
        {
            foreach (var leave in e.Leaves)
            {
                if (leave == null || string.IsNullOrEmpty(leave.UserId)) continue;
                if (_expectedUsers.Remove(leave.UserId)) changed = true;
                _deadPlayers.Remove(leave.UserId);
            }
        }

        if (!changed) return;
        if (!_countInitialized) TryInitializeCount();
        else RecomputeCountsAndMaybeBroadcast(true);

        if (enableDebugLogs)
        {
            Debug.Log("[DeathTracker] PRESENCE_UPDATE expected=" + _expectedUsers.Count + " alive=" + _aliveCount);
        }
    }

    private void TryInitializeCount()
    {
        if (_countInitialized) return;

        RefreshExpectedUsersFromAllSources();
        if (_expectedUsers.Count <= 0) return;

        _countInitialized = true;
        _gameOverTriggered = false;
        _gameOverTimer = 0f;
        RecomputeCountsAndMaybeBroadcast(IsHost());

        if (enableDebugLogs)
        {
            Debug.Log("[DeathTracker] INIT_COUNT alive=" + _aliveCount + "/" + _totalCount);
        }
    }

    private void RefreshExpectedUsersAndCountsFromAllSources(bool broadcastIfChanged)
    {
        var oldTotal = _totalCount;
        var oldAlive = _aliveCount;

        RefreshExpectedUsersFromAllSources();
        RecomputeCountsAndMaybeBroadcast(false);

        if (broadcastIfChanged && IsHost() && (oldTotal != _totalCount || oldAlive != _aliveCount))
        {
            BroadcastAliveCount();
            if (enableDebugLogs)
            {
                Debug.Log("[DeathTracker] HOST_RECOUNT alive=" + _aliveCount + "/" + _totalCount);
            }
        }
    }

    private void RefreshExpectedUsersFromAllSources()
    {
        var rebuilt = new HashSet<string>();

        // 1) Init spawns (lobby start or scene reload init)
        var context = MatchContext.Instance;
        var spawns = context != null && context.lastInit != null ? context.lastInit.spawns : null;
        if (spawns != null)
        {
            for (var i = 0; i < spawns.Length; i++)
            {
                var s = spawns[i];
                if (s == null || string.IsNullOrEmpty(s.userId)) continue;
                rebuilt.Add(s.userId);
            }
        }

        // 2) Current match presences
        if (conn != null && conn.Match != null)
        {
            var presences = conn.Match.Presences;
            if (presences != null)
            {
                foreach (var p in presences)
                {
                    if (p == null || string.IsNullOrEmpty(p.UserId)) continue;
                    rebuilt.Add(p.UserId);
                }
            }

            if (conn.Match.Self != null && !string.IsNullOrEmpty(conn.Match.Self.UserId))
            {
                rebuilt.Add(conn.Match.Self.UserId);
            }
        }

        if (conn != null && !string.IsNullOrEmpty(conn.SelfUserId))
        {
            rebuilt.Add(conn.SelfUserId);
        }

        // 3) Spawned player objects fallback
        if (playerSpawner != null)
        {
            playerSpawner.FillSpawnedPlayers(_spawnedBuffer);
            for (var i = 0; i < _spawnedBuffer.Count; i++)
            {
                var info = _spawnedBuffer[i];
                if (string.IsNullOrEmpty(info.userId)) continue;
                rebuilt.Add(info.userId);
            }
        }

        _expectedUsers.Clear();
        foreach (var userId in rebuilt)
        {
            _expectedUsers.Add(userId);
        }

        // Keep dead set valid for current expected users only.
        _deadPlayers.RemoveWhere(id => string.IsNullOrEmpty(id) || !_expectedUsers.Contains(id));
    }

    private void RecomputeCountsAndMaybeBroadcast(bool broadcast)
    {
        _totalCount = Mathf.Max(0, _expectedUsers.Count);
        var deadInExpected = 0;
        foreach (var userId in _deadPlayers)
        {
            if (_expectedUsers.Contains(userId)) deadInExpected++;
        }

        _aliveCount = Mathf.Clamp(_totalCount - deadInExpected, 0, _totalCount);

        if (broadcast && IsHost())
        {
            BroadcastAliveCount();
        }

        CheckForGameOver();
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
        _expectedUsers.Clear();
        _deadPlayers.Clear();
        _countInitialized = false;
        _aliveCount = 0;
        _totalCount = 0;
        _gameOverTriggered = false;
        _gameOverTimer = 0f;
        _nextHostRecountAt = 0f;

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

        if (playerSpawner == null)
            playerSpawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();

        if (progressionManager == null)
            progressionManager = FloorProgressionManager.Instance != null ? FloorProgressionManager.Instance : FindObjectOfType<FloorProgressionManager>();
    }

    private void EnsureBound()
    {
        if (!_boundTransport && transport != null)
        {
            transport.OnAliveCount += OnAliveCountReceived;
            _boundTransport = true;
        }

        if (!_boundPresence && conn != null)
        {
            conn.MatchPresenceReceived += OnPresenceChanged;
            _boundPresence = true;
        }
    }
}
