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

    [Header("Enemy Snapshot")]
    [Min(1f)] public float enemySnapshotSendHz = 15f;
    [Min(0f)] public float enemyLerpPos = 14f;
    [Min(0f)] public float enemyLerpYaw = 14f;
    [Min(0f)] public float enemyHardSnapDistance = 1.0f;

    private readonly Dictionary<string, GameObject> _enemiesBySpawnId = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, Vector3> _targetPosBySpawnId = new Dictionary<string, Vector3>();
    private readonly Dictionary<string, float> _targetYawBySpawnId = new Dictionary<string, float>();
    private readonly Dictionary<string, int> _targetStateBySpawnId = new Dictionary<string, int>();
    private bool _bound;
    private int _spawnSeq;
    private int _enemyTick;
    private float _enemySnapTimer;
    private float _nextEnemyDebugLogAt;

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
        TickEnemySnapshots();
    }

    void OnDestroy()
    {
        if (transport != null && _bound)
        {
            transport.OnEnemySpawn -= OnEnemySpawnReceived;
            transport.OnEnemySnapshot -= OnEnemySnapshotReceived;
            transport.OnEnemyTeleport -= OnEnemyTeleportReceived;
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
        _targetPosBySpawnId.Clear();
        _targetYawBySpawnId.Clear();
        _targetStateBySpawnId.Clear();
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

    private void OnEnemySnapshotReceived(MatchTransport.EnemySnapshotMsg msg)
    {
        if (msg == null || msg.enemies == null) return;
        if (conn != null && conn.IsCurrentPlayerMatchCreator) return;

        for (var i = 0; i < msg.enemies.Length; i++)
        {
            var e = msg.enemies[i];
            if (e == null || string.IsNullOrEmpty(e.spawnId)) continue;
            _targetPosBySpawnId[e.spawnId] = new Vector3(e.x, e.y, e.z);
            _targetYawBySpawnId[e.spawnId] = e.yaw;
            _targetStateBySpawnId[e.spawnId] = e.aiState;

            // ASAP host-truth correction: do not wait for the regular update loop when error is large.
            if (TryGet(e.spawnId, out var go) && go != null)
            {
                var targetPos = _targetPosBySpawnId[e.spawnId];
                var dist = Vector3.Distance(go.transform.position, targetPos);
                if (dist >= enemyHardSnapDistance)
                {
                    go.transform.position = targetPos;
                    go.transform.rotation = Quaternion.Euler(0f, e.yaw, 0f);

                    var aiImmediate = go.GetComponent<EnemySimpleAI>();
                    if (aiImmediate != null)
                    {
                        aiImmediate.SetAuthoritativeInstance(false);
                        aiImmediate.ApplyHostState(e.aiState);
                    }
                }
            }
        }

        if (enableDebugLogs && Time.unscaledTime >= _nextEnemyDebugLogAt)
        {
            _nextEnemyDebugLogAt = Time.unscaledTime + 1f;
            Debug.Log("[EnemySpawn] RECV_SNAPSHOT count=" + msg.enemies.Length);
        }
    }

    private void OnEnemyTeleportReceived(MatchTransport.EnemyTeleportMsg msg)
    {
        if (msg == null || string.IsNullOrEmpty(msg.spawnId)) return;
        if (!TryGet(msg.spawnId, out var go) || go == null) return;

        var pos = new Vector3(msg.x, msg.y, msg.z);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, msg.yaw, 0f);

        _targetPosBySpawnId[msg.spawnId] = pos;
        _targetYawBySpawnId[msg.spawnId] = msg.yaw;
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
        var id = go.GetComponent<EnemyNetIdentity>();
        if (id == null) id = go.AddComponent<EnemyNetIdentity>();
        id.spawnId = msg.spawnId;

        var isHost = conn != null && conn.IsCurrentPlayerMatchCreator;
        var ai = go.GetComponent<EnemySimpleAI>();
        if (ai != null)
        {
            ai.SetAuthoritativeInstance(isHost);
        }

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
        transport.OnEnemySnapshot += OnEnemySnapshotReceived;
        transport.OnEnemyTeleport += OnEnemyTeleportReceived;
        _bound = true;
    }

    public bool HostBroadcastTeleport(string spawnId, Vector3 position, float yaw, int reason = 0)
    {
        ResolveRefs();
        if (conn == null || transport == null || conn.Match == null) return false;
        if (!conn.IsCurrentPlayerMatchCreator) return false;
        if (string.IsNullOrEmpty(spawnId)) return false;

        var msg = new MatchTransport.EnemyTeleportMsg
        {
            spawnId = spawnId,
            x = position.x,
            y = position.y,
            z = position.z,
            yaw = yaw,
            reason = reason
        };

        OnEnemyTeleportReceived(msg);
        transport.BroadcastEnemyTeleport(msg);
        return true;
    }

    private void TickEnemySnapshots()
    {
        if (conn == null || transport == null || conn.Match == null) return;

        var isHost = conn.IsCurrentPlayerMatchCreator;
        if (isHost)
        {
            _enemySnapTimer += Time.deltaTime;
            var period = 1f / Mathf.Max(1f, enemySnapshotSendHz);
            if (_enemySnapTimer >= period)
            {
                _enemySnapTimer = 0f;
                var snap = BuildEnemySnapshot();
                if (snap.enemies != null && snap.enemies.Length > 0)
                {
                    transport.BroadcastEnemySnapshot(snap);
                }
            }
            return;
        }

        // Clients: run visual interpolation + host state correction.
        var tPos = 1f - Mathf.Exp(-Mathf.Max(0f, enemyLerpPos) * Time.deltaTime);
        var tYaw = 1f - Mathf.Exp(-Mathf.Max(0f, enemyLerpYaw) * Time.deltaTime);

        foreach (var kv in _targetPosBySpawnId)
        {
            var spawnId = kv.Key;
            if (!TryGet(spawnId, out var go) || go == null) continue;

            var currentPos = go.transform.position;
            var targetPos = kv.Value;
            var dist = Vector3.Distance(currentPos, targetPos);
            if (dist >= enemyHardSnapDistance)
            {
                go.transform.position = targetPos;
            }
            else
            {
                go.transform.position = Vector3.Lerp(currentPos, targetPos, tPos);
            }

            if (_targetYawBySpawnId.TryGetValue(spawnId, out var yaw))
            {
                var curYaw = go.transform.eulerAngles.y;
                var y = Mathf.LerpAngle(curYaw, yaw, tYaw);
                go.transform.rotation = Quaternion.Euler(0f, y, 0f);
            }

            if (_targetStateBySpawnId.TryGetValue(spawnId, out var aiState))
            {
                var ai = go.GetComponent<EnemySimpleAI>();
                if (ai != null)
                {
                    ai.SetAuthoritativeInstance(false);
                    ai.ApplyHostState(aiState);
                }
            }

            if (enableDebugLogs && Time.unscaledTime >= _nextEnemyDebugLogAt)
            {
                _nextEnemyDebugLogAt = Time.unscaledTime + 1f;
                Debug.Log("[EnemySpawn] APPLY_SNAPSHOT id=" + spawnId + " dist=" + dist.ToString("F2"));
            }
        }
    }

    private MatchTransport.EnemySnapshotMsg BuildEnemySnapshot()
    {
        var list = new List<MatchTransport.EnemyNetState>();
        foreach (var kv in _enemiesBySpawnId)
        {
            var spawnId = kv.Key;
            var go = kv.Value;
            if (string.IsNullOrEmpty(spawnId) || go == null) continue;

            var p = go.transform.position;
            var y = go.transform.eulerAngles.y;
            var ai = go.GetComponent<EnemySimpleAI>();
            var s = ai != null ? (int)ai.state : 0;
            if (ai != null) ai.SetAuthoritativeInstance(true);

            list.Add(new MatchTransport.EnemyNetState
            {
                spawnId = spawnId,
                x = p.x,
                y = p.y,
                z = p.z,
                yaw = y,
                aiState = s
            });
        }

        return new MatchTransport.EnemySnapshotMsg
        {
            tick = ++_enemyTick,
            enemies = list.ToArray()
        };
    }

    private static string ShortId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "------";
        return id.Length <= 6 ? id : id.Substring(0, 6);
    }
}
