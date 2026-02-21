using UnityEngine;
using System.Collections;
public class GameBootstrap : MonoBehaviour
{
    [Header("Refs")]
    public FloorProgressionManager progressionManager;
    public FloorTransitionManager transitionManager;
    public PlayerDeathTracker deathTracker;
    public NakamaConnection conn;
    public HostAuthority hostAuthority;
    public ProceduralBuildingGenerator buildingGenerator;
    public PlayerSpawnManager spawner;
    public EnemySpawnManager enemySpawner;
    public GhostSpawner ghostSpawner;

    [Header("Prefabs")]
    public GameObject localPlayerPrefab;
    public GameObject proxyPlayerPrefab;
    public GameObject enemyPrefab;
    public GameObject localGhostPrefab;
    public GameObject remoteGhostPrefab;

    void Awake()
    {
        if (!conn) conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        if (!hostAuthority) hostAuthority = FindObjectOfType<HostAuthority>();
        if (!buildingGenerator) buildingGenerator = FindObjectOfType<ProceduralBuildingGenerator>();
        if (!spawner) spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        if (!enemySpawner) enemySpawner = EnemySpawnManager.Instance != null ? EnemySpawnManager.Instance : FindObjectOfType<EnemySpawnManager>();
        if (!ghostSpawner) ghostSpawner = GhostSpawner.Instance != null ? GhostSpawner.Instance : FindObjectOfType<GhostSpawner>();
        
        if (!spawner)
        {
            var go = new GameObject("PlayerSpawnManager");
            spawner = go.AddComponent<PlayerSpawnManager>();
        }
        if (!enemySpawner)
        {
            var go = new GameObject("EnemySpawnManager");
            enemySpawner = go.AddComponent<EnemySpawnManager>();
        }
        if (!ghostSpawner)
        {
            var go = new GameObject("GhostSpawner");
            ghostSpawner = go.AddComponent<GhostSpawner>();
        }
        if (!progressionManager) progressionManager = FloorProgressionManager.Instance != null ? FloorProgressionManager.Instance : FindObjectOfType<FloorProgressionManager>();
        if (!transitionManager) transitionManager = FloorTransitionManager.Instance != null ? FloorTransitionManager.Instance : FindObjectOfType<FloorTransitionManager>();
        if (!deathTracker) deathTracker = PlayerDeathTracker.Instance != null ? PlayerDeathTracker.Instance : FindObjectOfType<PlayerDeathTracker>();
        if (!progressionManager)
        {
            var go = new GameObject("FloorProgressionManager");
            progressionManager = go.AddComponent<FloorProgressionManager>();
        }
        if (!transitionManager)
        {
            var go = new GameObject("FloorTransitionManager");
            transitionManager = go.AddComponent<FloorTransitionManager>();
        }
        if (!deathTracker)
        {
            var go = new GameObject("PlayerDeathTracker");
            deathTracker = go.AddComponent<PlayerDeathTracker>();
        }
    }

    void Start()
    {
        StartCoroutine(DelayedBootstrap());
    }
private IEnumerator DelayedBootstrap()
{
    var timeout = 6f;
    var elapsed = 0f;

    while (elapsed < timeout)
    {
        if (conn == null) conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        var context = MatchContext.Instance;
        var hasConn = conn != null && !string.IsNullOrEmpty(conn.SelfUserId);
        var hasInit = context != null && context.hasInit && context.lastInit != null;
        if (hasConn && hasInit) break;
        elapsed += Time.deltaTime;
        yield return null;
    }

    if (spawner == null)
        spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();

    if (spawner != null)
    {
        spawner.ClearAll();
        Debug.Log("[GameBootstrap] Cleared players for respawn");
    }

    // Retry bootstrap until local player exists (avoids black screen race on floor reload).
    var attempts = 0;
    while (attempts < 5)
    {
        BootstrapFromMatchContext();
        if (conn != null && spawner != null && !string.IsNullOrEmpty(conn.SelfUserId) && spawner.TryGet(conn.SelfUserId, out var localGo) && localGo != null)
        {
            yield break;
        }
        attempts++;
        yield return new WaitForSeconds(0.25f);
    }

    Debug.LogWarning("[GameBootstrap] Local player not spawned after retries.");
}
    private void BootstrapFromMatchContext()
    {
        var context = MatchContext.Instance;
        if (!context.hasInit || context.lastInit == null)
        {
            Debug.LogWarning("[GameBootstrap] Missing init payload in MatchContext.");
            return;
        }

        var init = context.lastInit;
        if (progressionManager != null)
        {
            if (!progressionManager.RunActive)
            {
                // Start new run
                progressionManager.StartNewRun();
                Debug.Log("[GameBootstrap] Started new run at Floor 1");
            }
            else
            {
                Debug.Log($"[GameBootstrap] Continuing run at Floor {progressionManager.CurrentFloor}");
            }

            // Generate building with current floor's difficulty
            int roomCount = progressionManager.CurrentRoomCount;
            int floorSeed = init.seed;
            
            if (buildingGenerator)
            {
                // Set room count (you may need to add public setters to ProceduralBuildingGenerator)
                SetBuildingGeneratorRoomCount(buildingGenerator, roomCount);
                buildingGenerator.GenerateBuildingFromSeed(floorSeed);
            }
        // Set enemy count
            if (hostAuthority)
            {
                int enemyCount = progressionManager.CurrentEnemyCount;
                SetHostAuthorityEnemyCount(hostAuthority, enemyCount);
            }
        }
        else
        {
            // Fallback: Use default generation
            if (buildingGenerator) buildingGenerator.GenerateBuildingFromSeed(init.seed);
        }
        if (!spawner) spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();

        if (spawner != null)
        {
            if (!spawner.localPlayerPrefab && localPlayerPrefab) spawner.localPlayerPrefab = localPlayerPrefab;
            if (!spawner.remoteProxyPrefab && proxyPlayerPrefab) spawner.remoteProxyPrefab = proxyPlayerPrefab;
        }
        if (enemySpawner != null && !enemySpawner.enemyPrefab && enemyPrefab) enemySpawner.enemyPrefab = enemyPrefab;
        if (enemySpawner != null) enemySpawner.ClearAll();
        if (ghostSpawner != null)
        {
            if (!ghostSpawner.localGhostPrefab && localGhostPrefab) ghostSpawner.localGhostPrefab = localGhostPrefab;
            if (!ghostSpawner.remoteGhostPrefab && remoteGhostPrefab) ghostSpawner.remoteGhostPrefab = remoteGhostPrefab;
            ghostSpawner.ClearAll();
        }

        var selfId = conn != null ? conn.SelfUserId : string.Empty;
        if (string.IsNullOrEmpty(selfId))
        {
            Debug.LogWarning("[GameBootstrap] Missing local user id.");
            return;
        }

        if (TryGetSpawn(init.spawns, selfId, out var localSpawn))
        {
            SpawnLocalPlayer(selfId, localSpawn.position, localSpawn.modelIndex);
        }
        else
        {
            Debug.LogWarning("[GameBootstrap] Missing spawn for local user.");
        }

        SpawnRemoteProxies(init.spawns, selfId);

        if (hostAuthority) hostAuthority.EnableGameplayAfterBootstrap();
    }

    private static bool TryGetSpawn(MatchTransport.SpawnPoint[] spawns, string userId, out MatchTransport.SpawnPoint foundSpawn)
    {
        foundSpawn = null;
        if (spawns == null || string.IsNullOrEmpty(userId)) return false;

        foreach (var spawn in spawns)
        {
            if (spawn == null || string.IsNullOrEmpty(spawn.userId)) continue;
            if (spawn.userId != userId) continue;
            foundSpawn = spawn;
            return true;
        }

        return false;
    }

    private void SpawnLocalPlayer(string userId, Vector3 pos, int modelIndex)
    {
        if (!spawner) return;
        spawner.SpawnLocal(userId, pos, 0f, modelIndex);
    }

    private void SpawnRemoteProxies(MatchTransport.SpawnPoint[] spawns, string selfId)
    {
        if (spawns == null) return;

        foreach (var spawn in spawns)
        {
            if (spawn == null || string.IsNullOrEmpty(spawn.userId)) continue;
            if (spawn.userId == selfId) continue;

            if (!spawner) continue;
            spawner.SpawnRemote(spawn.userId, spawn.position, 0f, spawn.modelIndex);
        }
    }

    private static string ShortId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "------";
        return id.Length <= 6 ? id : id.Substring(0, 6);
    }
     private void SetBuildingGeneratorRoomCount(ProceduralBuildingGenerator generator, int roomCount)
    {
        // Use reflection to set room count
        var minRoomsField = generator.GetType().GetField("minRooms", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var maxRoomsField = generator.GetType().GetField("maxRooms", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        if (minRoomsField != null)
            minRoomsField.SetValue(generator, roomCount);
        
        if (maxRoomsField != null)
            maxRoomsField.SetValue(generator, roomCount);

        Debug.Log($"[GameBootstrap] Set generator to {roomCount} rooms");
    }

    private void SetHostAuthorityEnemyCount(HostAuthority host, int enemyCount)
    {
        // Use reflection to set enemy count
        var startEnemyCountField = host.GetType().GetField("startEnemyCount", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        if (startEnemyCountField != null)
        {
            startEnemyCountField.SetValue(host, enemyCount);
            Debug.Log($"[GameBootstrap] Set enemy count to {enemyCount}");
        }
    }
}
