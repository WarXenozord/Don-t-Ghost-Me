using System.Collections.Generic;
using UnityEngine;

public class EnemySpawnManager : MonoBehaviour
{
    public static EnemySpawnManager Instance { get; private set; }

    [Header("Refs")]
    public NakamaConnection conn;
    public MatchTransport transport;
    public GameObject enemyPrefab;

    [Header("Debug")]
    public bool enableDebugLogs = true;

    private readonly Dictionary<string, GameObject> _enemiesBySpawnId = new Dictionary<string, GameObject>();
    private bool _bound;
    private int _spawnSeq;

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
        if (transport != null && _bound)
        {
            transport.OnEnemySpawn -= OnEnemySpawnReceived;
            _bound = false;
        }
    }

    public bool HostCommandSpawnEnemy(Vector3 position, float yaw = 0f, string prefabId = "default")
    {
        ResolveRefs();
        if (conn == null || transport == null || conn.Match == null) return false;
        if (!conn.IsCurrentPlayerMatchCreator) return false;

        var spawnId = BuildSpawnId();
        var msg = new MatchTransport.EnemySpawnMsg
        {
            spawnId = spawnId,
            prefabId = string.IsNullOrEmpty(prefabId) ? "default" : prefabId,
            x = position.x,
            y = position.y,
            z = position.z,
            yaw = yaw
        };

        ApplySpawn(msg);
        transport.BroadcastEnemySpawn(msg);
        return true;
    }

    public void ClearAll()
    {
        foreach (var kv in _enemiesBySpawnId)
        {
            if (kv.Value) Destroy(kv.Value);
        }
        _enemiesBySpawnId.Clear();
    }

    public bool TryGet(string spawnId, out GameObject enemy)
    {
        enemy = null;
        if (string.IsNullOrEmpty(spawnId)) return false;
        if (!_enemiesBySpawnId.TryGetValue(spawnId, out var go)) return false;
        if (go == null)
        {
            _enemiesBySpawnId.Remove(spawnId);
            return false;
        }

        enemy = go;
        return true;
    }

    private void OnEnemySpawnReceived(MatchTransport.EnemySpawnMsg msg)
    {
        ApplySpawn(msg);
    }

    private void ApplySpawn(MatchTransport.EnemySpawnMsg msg)
    {
        if (msg == null || string.IsNullOrEmpty(msg.spawnId)) return;
        if (_enemiesBySpawnId.ContainsKey(msg.spawnId)) return;

        if (!enemyPrefab)
        {
            if (enableDebugLogs) Debug.LogWarning("[EnemySpawn] enemyPrefab is not assigned.");
            return;
        }

        var pos = new Vector3(msg.x, msg.y, msg.z);
        var generator = FindObjectOfType<ProceduralBuildingGenerator>();
        if (generator != null && generator.TryGetSafeSpawnPoint(pos, out var safePos, preferredFloor: 0, allowDoors: false))
        {
            pos = safePos;
        }

        var go = Instantiate(enemyPrefab, pos, Quaternion.Euler(0f, msg.yaw, 0f));
        go.name = "Enemy_" + ShortId(msg.spawnId);
        _enemiesBySpawnId[msg.spawnId] = go;

        if (enableDebugLogs)
        {
            Debug.Log("[EnemySpawn] SPAWN id=" + msg.spawnId + " pos=(" + pos.x.ToString("F2") + "," + pos.y.ToString("F2") + "," + pos.z.ToString("F2") + ")");
        }
    }

    private string BuildSpawnId()
    {
        _spawnSeq++;
        var matchId = conn != null && conn.Match != null ? conn.Match.Id : "no_match";
        var self = conn != null ? conn.SelfUserId : "no_user";
        return matchId + ":" + self + ":" + _spawnSeq;
    }

    private void ResolveRefs()
    {
        if (!conn) conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        if (!transport) transport = MatchTransport.Instance != null ? MatchTransport.Instance : FindObjectOfType<MatchTransport>();
    }

    private void EnsureBound()
    {
        if (!transport || _bound) return;
        transport.OnEnemySpawn += OnEnemySpawnReceived;
        _bound = true;
    }

    private static string ShortId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "------";
        return id.Length <= 6 ? id : id.Substring(0, 6);
    }
}
