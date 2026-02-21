using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// Handles player respawning after scene reload during floor progression.
/// Ensures players are spawned even though the match is still active.
/// </summary>
public class FloorReloadHandler : MonoBehaviour
{
    public static FloorReloadHandler Instance { get; private set; }

    [Header("References")]
    public PlayerSpawnManager playerSpawner;
    public HostAuthority hostAuthority;
    public NakamaConnection conn;

    [Header("Prefabs")]
    public GameObject localPlayerPrefab;
    public GameObject remoteProxyPrefab;

    [Header("Debug")]
    public bool enableDebugLogs = true;

    private bool _hasRespawnedThisLoad;
    private string _currentSceneName;

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
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// Called when a scene finishes loading
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _currentSceneName = scene.name;

        if (enableDebugLogs)
        {
            Debug.Log($"[FloorReload] Scene loaded: {scene.name}");
        }

        // Reset the respawn flag for the new scene
        _hasRespawnedThisLoad = false;

        // Give the scene a frame to initialize, then respawn players
        StartCoroutine(RespawnPlayersAfterDelay());
    }

    private IEnumerator RespawnPlayersAfterDelay()
    {
        // Wait for scene initialization
        yield return new WaitForEndOfFrame();
        yield return new WaitForSeconds(0.1f);

        ResolveRefs();

        // Check if we're in a gameplay scene with an active match
        if (conn == null || conn.Match == null)
        {
            if (enableDebugLogs)
                Debug.Log("[FloorReload] No active match, skipping respawn");
            yield break;
        }

        // Check if this is a floor progression reload (match is active)
        var context = MatchContext.Instance;
        if (context == null || !context.hasInit || !context.started)
        {
            if (enableDebugLogs)
                Debug.Log("[FloorReload] Match not started, skipping respawn");
            yield break;
        }

        // Respawn players
        RespawnAllPlayers();
    }

    /// <summary>
    /// Respawns all players in the current match
    /// </summary>
    private void RespawnAllPlayers()
    {
        if (_hasRespawnedThisLoad)
        {
            if (enableDebugLogs)
                Debug.Log("[FloorReload] Already respawned this load");
            return;
        }

        if (playerSpawner == null)
        {
            Debug.LogError("[FloorReload] PlayerSpawnManager not found!");
            return;
        }

        // Clear existing players
        playerSpawner.ClearAll();

        var context = MatchContext.Instance;
        if (context == null || context.lastInit == null)
        {
            Debug.LogError("[FloorReload] No init data found!");
            return;
        }

        var init = context.lastInit;
        var selfId = conn.SelfUserId;

        if (enableDebugLogs)
        {
            Debug.Log($"[FloorReload] Respawning players for match {conn.Match.Id}");
        }

        // Assign prefabs if needed
        if (playerSpawner.localPlayerPrefab == null && localPlayerPrefab != null)
        {
            playerSpawner.localPlayerPrefab = localPlayerPrefab;
        }
        if (playerSpawner.remoteProxyPrefab == null && remoteProxyPrefab != null)
        {
            playerSpawner.remoteProxyPrefab = remoteProxyPrefab;
        }

        // Spawn local player
        if (!string.IsNullOrEmpty(selfId) && TryGetSpawn(init.spawns, selfId, out var localSpawn))
        {
            playerSpawner.SpawnLocal(selfId, localSpawn, 0f);
            
            if (enableDebugLogs)
                Debug.Log($"[FloorReload] Spawned local player at {localSpawn}");
        }

        // Spawn remote players
        if (init.spawns != null)
        {
            foreach (var spawn in init.spawns)
            {
                if (spawn == null || string.IsNullOrEmpty(spawn.userId)) continue;
                if (spawn.userId == selfId) continue; // Skip self

                playerSpawner.SpawnRemote(spawn.userId, spawn.position, 0f);
                
                if (enableDebugLogs)
                    Debug.Log($"[FloorReload] Spawned remote player {spawn.userId} at {spawn.position}");
            }
        }

        _hasRespawnedThisLoad = true;

        if (enableDebugLogs)
        {
            Debug.Log($"[FloorReload] Respawned {init.spawns?.Length ?? 0} players");
        }

        // Refresh ghost interaction cache
        RefreshGhostInteractionCache();
    }

    /// <summary>
    /// Refreshes the ghost interaction cache so ghost can interact with newly spawned objects
    /// </summary>
    private void RefreshGhostInteractionCache()
    {
        // Wait a frame for objects to spawn, then refresh
        StartCoroutine(RefreshCacheDelayed());
    }

    private IEnumerator RefreshCacheDelayed()
    {
        yield return new WaitForEndOfFrame();
        yield return new WaitForSeconds(0.2f);

        // Find local player's ghost interaction component
        if (playerSpawner == null || conn == null) yield break;

        var selfId = conn.SelfUserId;
        if (string.IsNullOrEmpty(selfId)) yield break;

        if (playerSpawner.TryGet(selfId, out var localGo) && localGo != null)
        {
            var ghostInteraction = localGo.GetComponentInChildren<GhostInteraction>(true);
            if (ghostInteraction != null)
            {
                ghostInteraction.RefreshInteractableCache();
                
                if (enableDebugLogs)
                    Debug.Log("[FloorReload] Refreshed ghost interaction cache");
            }
        }
    }

    private bool TryGetSpawn(MatchTransport.SpawnPoint[] spawns, string userId, out Vector3 pos)
    {
        pos = Vector3.zero;
        if (spawns == null || string.IsNullOrEmpty(userId)) return false;

        foreach (var spawn in spawns)
        {
            if (spawn == null || string.IsNullOrEmpty(spawn.userId)) continue;
            if (spawn.userId != userId) continue;
            pos = spawn.position;
            return true;
        }

        return false;
    }

    private void ResolveRefs()
    {
        if (playerSpawner == null)
            playerSpawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        
        if (hostAuthority == null)
            hostAuthority = FindObjectOfType<HostAuthority>();
        
        if (conn == null)
            conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
    }

    /// <summary>
    /// Manually trigger respawn (for debugging)
    /// </summary>
    [ContextMenu("Force Respawn Players")]
    public void ForceRespawnPlayers()
    {
        _hasRespawnedThisLoad = false;
        RespawnAllPlayers();
    }
}