using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    [Header("Refs")]
    public NakamaConnection conn;
    public HostAuthority hostAuthority;
    public ProceduralBuildingGenerator buildingGenerator;
    public PlayerSpawnManager spawner;

    [Header("Prefabs")]
    public GameObject localPlayerPrefab;
    public GameObject proxyPlayerPrefab;

    void Awake()
    {
        if (!conn) conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        if (!hostAuthority) hostAuthority = FindObjectOfType<HostAuthority>();
        if (!buildingGenerator) buildingGenerator = FindObjectOfType<ProceduralBuildingGenerator>();
        if (!spawner) spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        if (!spawner)
        {
            var go = new GameObject("PlayerSpawnManager");
            spawner = go.AddComponent<PlayerSpawnManager>();
        }
    }

    void Start()
    {
        BootstrapFromMatchContext();
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

        if (buildingGenerator) buildingGenerator.GenerateBuildingFromSeed(init.seed);
        if (!spawner) spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();

        if (spawner != null)
        {
            if (!spawner.localPlayerPrefab && localPlayerPrefab) spawner.localPlayerPrefab = localPlayerPrefab;
            if (!spawner.remoteProxyPrefab && proxyPlayerPrefab) spawner.remoteProxyPrefab = proxyPlayerPrefab;
        }

        var selfId = conn != null ? conn.SelfUserId : string.Empty;
        if (string.IsNullOrEmpty(selfId))
        {
            Debug.LogWarning("[GameBootstrap] Missing local user id.");
            return;
        }

        if (TryGetSpawn(init.spawns, selfId, out var localSpawn))
        {
            SpawnLocalPlayer(selfId, localSpawn);
        }
        else
        {
            Debug.LogWarning("[GameBootstrap] Missing spawn for local user.");
        }

        SpawnRemoteProxies(init.spawns, selfId);

        if (hostAuthority) hostAuthority.EnableGameplayAfterBootstrap();
    }

    private static bool TryGetSpawn(MatchTransport.SpawnPoint[] spawns, string userId, out Vector3 pos)
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

    private void SpawnLocalPlayer(string userId, Vector3 pos)
    {
        if (!spawner) return;
        spawner.SpawnLocal(userId, pos, 0f);
    }

    private void SpawnRemoteProxies(MatchTransport.SpawnPoint[] spawns, string selfId)
    {
        if (spawns == null) return;

        foreach (var spawn in spawns)
        {
            if (spawn == null || string.IsNullOrEmpty(spawn.userId)) continue;
            if (spawn.userId == selfId) continue;

            if (!spawner) continue;
            spawner.SpawnRemote(spawn.userId, spawn.position, 0f);
        }
    }

    private static string ShortId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "------";
        return id.Length <= 6 ? id : id.Substring(0, 6);
    }
}
