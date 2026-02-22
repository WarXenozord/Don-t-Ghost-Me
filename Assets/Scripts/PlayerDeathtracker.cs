using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Tracks player deaths and triggers game over when all players are dead.
/// A player is "dead" when they become a ghost.
/// </summary>
public class PlayerDeathTracker : MonoBehaviour
{
    public static PlayerDeathTracker Instance { get; private set; }

    [Header("References")]
    public NakamaConnection conn;
    public PlayerSpawnManager playerSpawner;
    public FloorProgressionManager progressionManager;

    [Header("Game Over")]
    public string gameOverScene = "GameOver";
    public float gameOverDelay = 3f; // Delay before loading game over scene

    [Header("Debug")]
    public bool enableDebugLogs = true;

    // Track which players are currently alive (mediums)
    private readonly HashSet<string> _alivePlayers = new HashSet<string>();
    private readonly HashSet<string> _deadPlayers = new HashSet<string>();
    
    private bool _gameOverTriggered = false;
    private float _gameOverTimer = 0f;
    private float _suppressChecksUntil;
    private readonly Dictionary<string, float> _nextMissingBodyLogAt = new Dictionary<string, float>();

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
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void Update()
    {
        ResolveRefs();
        if (Time.unscaledTime < _suppressChecksUntil) return;
        CheckPlayerStates();

        // Game over countdown
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
    /// Registers a player as alive (called when player spawns as medium)
    /// </summary>
    public void RegisterPlayerAlive(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return;

        _alivePlayers.Add(userId);
        _deadPlayers.Remove(userId);

        if (enableDebugLogs)
            Debug.Log($"[DeathTracker] Player {userId} registered as ALIVE. Alive: {_alivePlayers.Count}, Dead: {_deadPlayers.Count}");
    }

    /// <summary>
    /// Registers a player as dead (called when player becomes ghost)
    /// </summary>
    public void RegisterPlayerDead(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return;

        _alivePlayers.Remove(userId);
        _deadPlayers.Add(userId);

        if (enableDebugLogs)
            Debug.Log($"[DeathTracker] Player {userId} registered as DEAD. Alive: {_alivePlayers.Count}, Dead: {_deadPlayers.Count}");

        // Check if all dead
        CheckForGameOver();
    }

    /// <summary>
    /// Checks current player states and updates alive/dead tracking
    /// </summary>
    private void CheckPlayerStates()
    {
        if (playerSpawner == null) return;

        // Get all current players in match
        var currentPlayers = GetAllPlayerIds();
        PrunePlayersNotInMatch(currentPlayers);

        foreach (var userId in currentPlayers)
        {
            if (!playerSpawner.TryGet(userId, out var playerGo) || playerGo == null)
            {
                // Missing body should still count as an alive player by default until
                // a ghost body is explicitly observed/registered for this user.
                if (!_deadPlayers.Contains(userId) && !_alivePlayers.Contains(userId))
                {
                    RegisterPlayerAlive(userId);
                }
                continue;
            }

            // Check if player is a ghost
            var ghost = playerGo.GetComponentInChildren<GhostController>(true);
            var isGhost = ghost != null;
            if (isGhost)
            {
                // Player is a ghost (dead)
                if (!_deadPlayers.Contains(userId))
                {
                    RegisterPlayerDead(userId);
                }
            }
            else
            {
                // Player is a medium (alive)
                if (!_alivePlayers.Contains(userId))
                {
                    RegisterPlayerAlive(userId);
                }
            }
        }

        CheckForGameOver();
    }

    /// <summary>
    /// Checks if all players are dead and triggers game over
    /// </summary>
    private void CheckForGameOver()
    {
        if (_gameOverTriggered) return;

        var allPlayers = GetExpectedPlayerIds();
        
        if (allPlayers.Count == 0)
        {
            // No players yet, don't trigger game over
            return;
        }

        // Authoritative condition: every expected player currently has a ghost body.
        // This avoids false positives when one side has stale alive/dead counters.
        bool allDead = AreAllExpectedPlayersGhost(allPlayers);

        if (allDead)
        {
            TriggerGameOver();
        }
    }

    /// <summary>
    /// Triggers game over sequence
    /// </summary>
    private void TriggerGameOver()
    {
        if (_gameOverTriggered) return;

        _gameOverTriggered = true;
        _gameOverTimer = gameOverDelay;

        Debug.Log($"[DeathTracker] GAME OVER! All {_deadPlayers.Count} players are dead. Loading game over scene in {gameOverDelay}s...");

        // End the run
        if (progressionManager != null)
        {
            progressionManager.EndRun();
        }
    }

    /// <summary>
    /// Loads the game over scene
    /// </summary>
    private void LoadGameOverScene()
    {
        Debug.Log($"[DeathTracker] Loading game over scene: {gameOverScene}");
        
        if (!string.IsNullOrEmpty(gameOverScene))
        {
            SceneManager.LoadScene(gameOverScene);
        }
        else
        {
            Debug.LogError("[DeathTracker] Game over scene name not set!");
        }
    }

    /// <summary>
    /// Resets the death tracker (call when starting new run)
    /// </summary>
    public void ResetTracker()
    {
        _alivePlayers.Clear();
        _deadPlayers.Clear();
        _gameOverTriggered = false;
        _gameOverTimer = 0f;
        _suppressChecksUntil = Time.unscaledTime + 2f;
        _nextMissingBodyLogAt.Clear();

        Debug.Log("[DeathTracker] Tracker reset");
    }

    /// <summary>
    /// Gets all player IDs currently in the match
    /// </summary>
    private HashSet<string> GetAllPlayerIds()
    {
        var players = new HashSet<string>();

        if (conn == null || conn.Match == null) return players;

        // Add all presences
        if (conn.Match.Presences != null)
        {
            foreach (var presence in conn.Match.Presences)
            {
                if (presence != null && !string.IsNullOrEmpty(presence.UserId))
                {
                    players.Add(presence.UserId);
                }
            }
        }

        // Add self
        if (!string.IsNullOrEmpty(conn.SelfUserId))
        {
            players.Add(conn.SelfUserId);
        }

        return players;
    }

    private HashSet<string> GetExpectedPlayerIds()
    {
        var expected = new HashSet<string>();
        var context = MatchContext.Instance;
        var spawns = context != null && context.lastInit != null ? context.lastInit.spawns : null;

        if (spawns != null && spawns.Length > 0)
        {
            for (var i = 0; i < spawns.Length; i++)
            {
                var s = spawns[i];
                if (s == null || string.IsNullOrEmpty(s.userId)) continue;
                expected.Add(s.userId);
            }
        }

        if (expected.Count == 0)
        {
            return GetAllPlayerIds();
        }

        return expected;
    }

    private bool AreAllExpectedPlayersGhost(HashSet<string> expectedPlayers)
    {
        if (expectedPlayers == null || expectedPlayers.Count == 0) return false;
        if (playerSpawner == null) return false;

        foreach (var userId in expectedPlayers)
        {
            if (string.IsNullOrEmpty(userId)) return false;
            if (!playerSpawner.TryGet(userId, out var go) || go == null)
            {
                if (enableDebugLogs && ShouldLogMissingBody(userId))
                {
                    Debug.Log($"[DeathTracker] Not all dead yet: missing body for {userId}");
                }
                return false;
            }

            var ghost = go.GetComponentInChildren<GhostController>(true);
            if (ghost == null)
            {
                if (enableDebugLogs) Debug.Log($"[DeathTracker] Not all dead yet: {userId} is still medium");
                return false;
            }
        }

        return true;
    }

    private bool ShouldLogMissingBody(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        var now = Time.unscaledTime;
        if (_nextMissingBodyLogAt.TryGetValue(userId, out var nextAt) && now < nextAt) return false;
        _nextMissingBodyLogAt[userId] = now + 2f;
        return true;
    }

    private void OnSceneLoaded(Scene _, LoadSceneMode __)
    {
        // Give GameBootstrap/spawners time to build local+remote avatars.
        _suppressChecksUntil = Time.unscaledTime + 3f;
        _nextMissingBodyLogAt.Clear();
    }

    private void PrunePlayersNotInMatch(HashSet<string> currentPlayers)
    {
        if (currentPlayers == null) return;

        var removeAlive = new List<string>();
        foreach (var userId in _alivePlayers)
        {
            if (!currentPlayers.Contains(userId))
            {
                removeAlive.Add(userId);
            }
        }
        for (var i = 0; i < removeAlive.Count; i++)
        {
            _alivePlayers.Remove(removeAlive[i]);
        }

        var removeDead = new List<string>();
        foreach (var userId in _deadPlayers)
        {
            if (!currentPlayers.Contains(userId))
            {
                removeDead.Add(userId);
            }
        }
        for (var i = 0; i < removeDead.Count; i++)
        {
            _deadPlayers.Remove(removeDead[i]);
        }
    }

    private void ResolveRefs()
    {
        if (conn == null)
            conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        
        if (playerSpawner == null)
            playerSpawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        
        if (progressionManager == null)
            progressionManager = FloorProgressionManager.Instance != null ? FloorProgressionManager.Instance : FindObjectOfType<FloorProgressionManager>();
    }

    /// <summary>
    /// Gets current alive player count (for UI)
    /// </summary>
    public int GetAlivePlayerCount()
    {
        var expected = GetExpectedPlayerIds();
        if (expected.Count == 0) return _alivePlayers.Count;

        var alive = 0;
        foreach (var userId in expected)
        {
            if (string.IsNullOrEmpty(userId)) continue;
            if (_deadPlayers.Contains(userId)) continue;
            alive++;
        }

        return alive;
    }

    /// <summary>
    /// Gets current dead player count (for UI)
    /// </summary>
    public int GetDeadPlayerCount()
    {
        return _deadPlayers.Count;
    }

    /// <summary>
    /// Debug: Force game over
    /// </summary>
    [ContextMenu("Force Game Over")]
    public void DebugForceGameOver()
    {
        TriggerGameOver();
    }
}
