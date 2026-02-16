using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    [Header("Refs")]
    public NakamaConnection conn;
    public HostAuthority hostAuthority;
    public ProceduralBuildingGenerator buildingGenerator;

    [Header("Prefabs")]
    public GameObject localPlayerPrefab;
    public GameObject proxyPlayerPrefab;

    void Awake()
    {
        if (!conn) conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        if (!hostAuthority) hostAuthority = FindObjectOfType<HostAuthority>();
        if (!buildingGenerator) buildingGenerator = FindObjectOfType<ProceduralBuildingGenerator>();
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
        if (localPlayerPrefab)
        {
            var go = Instantiate(localPlayerPrefab, pos, Quaternion.identity);
            go.name = "LocalPlayer_" + ShortId(userId);
            return;
        }

        var fallback = new GameObject("LocalPlayer_" + ShortId(userId));
        fallback.transform.position = pos;
    }

    private void SpawnRemoteProxies(MatchTransport.SpawnPoint[] spawns, string selfId)
    {
        if (spawns == null) return;

        foreach (var spawn in spawns)
        {
            if (spawn == null || string.IsNullOrEmpty(spawn.userId)) continue;
            if (spawn.userId == selfId) continue;

            if (proxyPlayerPrefab)
            {
                var go = Instantiate(proxyPlayerPrefab, spawn.position, Quaternion.identity);
                go.name = "Proxy_" + ShortId(spawn.userId);
            }
            else
            {
                var fallback = new GameObject("Proxy_" + ShortId(spawn.userId));
                fallback.transform.position = spawn.position;
            }
        }
    }

    private static string ShortId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "------";
        return id.Length <= 6 ? id : id.Substring(0, 6);
    }
}
