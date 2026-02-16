using System.Collections.Generic;
using Nakama;
using UnityEngine;

public class HostAuthority : MonoBehaviour
{
    public NakamaConnection conn;
    public MatchTransport transport;
    public PlayerSpawnManager spawner;

    [Header("Host")]
    public bool isHost = false;

    [Header("Tick Rates")]
    public float inputSendHz = 20f;
    public float snapshotSendHz = 10f;
    public float moveSpeed = 3f;

    private float _inputTimer;
    private float _snapTimer;

    private int _seq;
    private int _tick;

    private bool _gameplayStarted;
    private bool _initSent;
    private bool _startSent;
    private int _activeInitId = -1;
    private int _processedInitId = -1;
    private string _processedInitMatchId = string.Empty;
    private string _runtimeMatchId = string.Empty;
    private Vector3 _goalPos;
    private MatchTransport.SpawnPoint[] _cachedInitSpawns;
    private bool _spawnPassComplete;

    private readonly HashSet<string> _readyUserIds = new HashSet<string>();
    private readonly Dictionary<string, Vector3> _spawnByUserId = new Dictionary<string, Vector3>();

    // Authoritative state (host only)
    private readonly Dictionary<string, Vector3> _pos = new Dictionary<string, Vector3>();
    private readonly Dictionary<string, float> _yaw = new Dictionary<string, float>();
    private readonly Dictionary<string, MatchTransport.InputMsg> _lastInput = new Dictionary<string, MatchTransport.InputMsg>();

    void Awake()
    {
        if (!conn) conn = GetComponent<NakamaConnection>();
        if (!transport) transport = GetComponent<MatchTransport>();
        if (!spawner) spawner = FindObjectOfType<PlayerSpawnManager>();

        transport.OnInput += HandleInputFromClient;
        transport.OnInit += OnInitReceived;
        transport.OnReady += OnReadyReceived;
        transport.OnStart += OnStartReceived;

        if (conn) conn.MatchPresenceReceived += OnPresenceChanged;
    }

    void OnDestroy()
    {
        if (transport)
        {
            transport.OnInput -= HandleInputFromClient;
            transport.OnInit -= OnInitReceived;
            transport.OnReady -= OnReadyReceived;
            transport.OnStart -= OnStartReceived;
        }

        if (conn) conn.MatchPresenceReceived -= OnPresenceChanged;
    }

    void Start()
    {
        isHost = false;
        TryApplyInitFromContext();
        TrySpawnPass();
    }

    void Update()
    {
        TryApplyInitFromContext();
        TrySpawnPass();

        if (conn?.Match == null || conn.Socket == null)
        {
            if (!string.IsNullOrEmpty(_runtimeMatchId))
            {
                _runtimeMatchId = string.Empty;
                ResetMatchRuntimeState();
            }
            return;
        }

        if (_runtimeMatchId != conn.Match.Id)
        {
            _runtimeMatchId = conn.Match.Id;
            ResetMatchRuntimeState();
        }

        isHost = conn.IsCurrentPlayerMatchCreator;

        if (!_gameplayStarted) return;

        if (!isHost)
        {
            _inputTimer += Time.deltaTime;
            if (_inputTimer >= 1f / inputSendHz)
            {
                _inputTimer = 0f;
                transport.SendInput(BuildLocalInput());
            }
        }
        else
        {
            SimulateHost(Time.deltaTime);

            _snapTimer += Time.deltaTime;
            if (_snapTimer >= 1f / snapshotSendHz)
            {
                _snapTimer = 0f;
                transport.BroadcastSnapshot(BuildSnapshot());
            }
        }
    }

    public void BeginMatchInitialization()
    {
        isHost = conn != null && conn.IsCurrentPlayerMatchCreator;
        if (!isHost || conn == null || conn.Match == null || transport == null) return;
        if (_startSent) return;

        _activeInitId = Random.Range(1, int.MaxValue);
        var initMsg = BuildInitMessage(_activeInitId);

        _initSent = true;
        _startSent = false;
        _readyUserIds.Clear();

        ApplyInitOnce(initMsg);

        if (!string.IsNullOrEmpty(conn.SelfUserId))
        {
            _readyUserIds.Add(conn.SelfUserId);
        }

        transport.BroadcastInit(initMsg);
        TrySendStartIfEveryoneReady();
    }

    public void EnableGameplayAfterBootstrap()
    {
        if (conn == null || conn.Match == null) return;
        if (!MatchContext.Instance.started) return;
        _gameplayStarted = true;
    }

    private MatchTransport.InitMsg BuildInitMessage(int initId)
    {
        var seed = Random.Range(1, int.MaxValue);
        var goalPos = GetGoalPositionPlaceholder();
        var spawns = BuildSpawnPointsPlaceholder();
        return new MatchTransport.InitMsg
        {
            initId = initId,
            seed = seed,
            spawns = spawns,
            goalPos = goalPos
        };
    }

    private MatchTransport.SpawnPoint[] BuildSpawnPointsPlaceholder()
    {
        var users = GetPresentUserIds();
        var spawns = new MatchTransport.SpawnPoint[users.Count];
        var i = 0;
        foreach (var userId in users)
        {
            var position = new Vector3(i * 2f, 0.5f, 0f);
            spawns[i++] = new MatchTransport.SpawnPoint { userId = userId, position = position };
        }
        return spawns;
    }

    private Vector3 GetGoalPositionPlaceholder()
    {
        return Vector3.zero;
    }

    private void OnInitReceived(MatchTransport.InitMsg msg)
    {
        if (msg == null) return;
        if (_processedInitId == msg.initId && _processedInitMatchId == conn?.Match?.Id) return;

        ApplyInitOnce(msg);

        if (conn != null && !conn.IsCurrentPlayerMatchCreator)
        {
            transport.SendReady(new MatchTransport.ReadyMsg { initId = msg.initId });
        }
    }

    private void ApplyInitOnce(MatchTransport.InitMsg msg)
    {
        _processedInitId = msg.initId;
        _processedInitMatchId = conn?.Match?.Id ?? string.Empty;
        _activeInitId = msg.initId;
        _gameplayStarted = false;
        _initSent = true;

        _pos.Clear();
        _yaw.Clear();
        _lastInput.Clear();
        _tick = 0;
        _spawnByUserId.Clear();
        _cachedInitSpawns = msg.spawns;
        _spawnPassComplete = false;

        if (msg.spawns != null)
        {
            for (var i = 0; i < msg.spawns.Length; i++)
            {
                var spawn = msg.spawns[i];
                if (spawn == null || string.IsNullOrEmpty(spawn.userId)) continue;
                _spawnByUserId[spawn.userId] = spawn.position;
            }
        }

        TrySpawnPass();
        StoreGoalPosition(msg.goalPos);

        var context = MatchContext.Instance;
        context.lastInit = msg;
        context.hasInit = true;
        context.started = false;
    }

    private void OnReadyReceived(MatchTransport.ReadyMsg msg)
    {
        isHost = conn != null && conn.IsCurrentPlayerMatchCreator;
        if (!isHost || !_initSent || msg == null || msg.initId != _activeInitId) return;
        if (string.IsNullOrEmpty(msg.senderUserId)) return;

        _readyUserIds.Add(msg.senderUserId);
        TrySendStartIfEveryoneReady();
    }

    private void OnStartReceived(MatchTransport.StartMsg msg)
    {
        if (msg == null || msg.initId != _activeInitId) return;
        MatchContext.Instance.started = true;
        _gameplayStarted = true;
    }

    private void ResetMatchRuntimeState()
    {
        _gameplayStarted = false;
        _initSent = false;
        _startSent = false;
        _activeInitId = -1;
        _processedInitId = -1;
        _processedInitMatchId = string.Empty;

        _readyUserIds.Clear();
        _spawnByUserId.Clear();
        _cachedInitSpawns = null;
        _spawnPassComplete = false;
        _pos.Clear();
        _yaw.Clear();
        _lastInput.Clear();
        _tick = 0;
    }

    private void OnPresenceChanged(IMatchPresenceEvent e)
    {
        isHost = conn != null && conn.IsCurrentPlayerMatchCreator;
        if (!isHost || !_initSent || _startSent) return;
        TrySendStartIfEveryoneReady();
    }

    private void TrySendStartIfEveryoneReady()
    {
        isHost = conn != null && conn.IsCurrentPlayerMatchCreator;
        if (!isHost || !_initSent || _startSent || _activeInitId < 0) return;

        var presentUserIds = GetPresentUserIds();
        if (presentUserIds.Count == 0) return;

        foreach (var userId in presentUserIds)
        {
            if (!_readyUserIds.Contains(userId)) return;
        }

        _startSent = true;
        MatchContext.Instance.started = true;
        _gameplayStarted = true;
        transport.BroadcastStart(new MatchTransport.StartMsg { initId = _activeInitId });
    }

    private List<string> GetPresentUserIds()
    {
        var unique = new HashSet<string>();

        if (conn?.Match?.Presences != null)
        {
            foreach (var p in conn.Match.Presences)
            {
                if (p == null || string.IsNullOrEmpty(p.UserId)) continue;
                unique.Add(p.UserId);
            }
        }

        if (conn?.Match?.Self != null && !string.IsNullOrEmpty(conn.Match.Self.UserId))
        {
            unique.Add(conn.Match.Self.UserId);
        }

        if (!string.IsNullOrEmpty(conn?.SelfUserId))
        {
            unique.Add(conn.SelfUserId);
        }

        return new List<string>(unique);
    }

    private MatchTransport.InputMsg BuildLocalInput()
    {
        var h = Input.GetAxisRaw("Horizontal");
        var v = Input.GetAxisRaw("Vertical");

        float yaw = 0f;
        if (Input.GetMouseButton(1))
            yaw = transform.eulerAngles.y + Input.GetAxis("Mouse X") * 3f;

        return new MatchTransport.InputMsg
        {
            seq = ++_seq,
            moveX = h,
            moveZ = v,
            yaw = yaw,
            buttons = 0
        };
    }

    private void HandleInputFromClient(MatchTransport.InputMsg msg)
    {
        if (!isHost || !_gameplayStarted) return;

        if (!_pos.ContainsKey(msg.senderUserId))
        {
            if (!_spawnByUserId.TryGetValue(msg.senderUserId, out var spawn))
            {
                spawn = Vector3.zero;
            }
            if (!spawner) spawner = FindObjectOfType<PlayerSpawnManager>();
            if (spawner) spawn = spawner.ClampInsideMapBounds(spawn);
            _pos[msg.senderUserId] = spawn;
            _yaw[msg.senderUserId] = 0f;
        }

        _lastInput[msg.senderUserId] = msg;
        if (msg.yaw != 0f) _yaw[msg.senderUserId] = msg.yaw;
    }

    private void SimulateHost(float dt)
    {
        var selfId = conn.SelfUserId;
        if (!string.IsNullOrEmpty(selfId) && !_pos.ContainsKey(selfId))
        {
            if (!_spawnByUserId.TryGetValue(selfId, out var selfSpawn))
            {
                selfSpawn = new Vector3(0f, 0.5f, 0f);
            }
            if (!spawner) spawner = FindObjectOfType<PlayerSpawnManager>();
            if (spawner) selfSpawn = spawner.ClampInsideMapBounds(selfSpawn);
            _pos[selfId] = selfSpawn;
            _yaw[selfId] = 0f;
        }

        if (string.IsNullOrEmpty(selfId)) return;

        var selfInput = BuildLocalInput();
        _lastInput[selfId] = selfInput;
        if (selfInput.yaw != 0f) _yaw[selfId] = selfInput.yaw;

        foreach (var kv in _lastInput)
        {
            var id = kv.Key;
            var inp = kv.Value;

            var dir = new Vector3(inp.moveX, 0f, inp.moveZ);
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            _pos[id] += dir * moveSpeed * dt;
        }

        _tick++;
    }

    private MatchTransport.SnapshotMsg BuildSnapshot()
    {
        var players = new MatchTransport.PlayerState[_pos.Count];
        var i = 0;
        foreach (var kv in _pos)
        {
            var id = kv.Key;
            var p = kv.Value;
            players[i++] = new MatchTransport.PlayerState
            {
                id = id,
                x = p.x,
                y = p.y,
                z = p.z,
                yaw = _yaw.TryGetValue(id, out var y) ? y : 0f,
                state = 0
            };
        }

        return new MatchTransport.SnapshotMsg { tick = _tick, players = players };
    }

    private bool TryGetLocalSpawn(out Vector3 spawn)
    {
        spawn = Vector3.zero;
        if (conn == null || string.IsNullOrEmpty(conn.SelfUserId)) return false;
        return _spawnByUserId.TryGetValue(conn.SelfUserId, out spawn);
    }

    private void SpawnLocalPlayerAtAssignedSpawn(Vector3 spawnPos)
    {
        if (!spawner) spawner = FindObjectOfType<PlayerSpawnManager>();
        if (spawner == null || string.IsNullOrEmpty(conn?.SelfUserId)) return;

        var safeSpawn = spawner.ClampInsideMapBounds(spawnPos);
        spawner.SpawnLocal(conn.SelfUserId, safeSpawn, 0f);

        _spawnByUserId[conn.SelfUserId] = safeSpawn;
        _pos[conn.SelfUserId] = safeSpawn;
        _yaw[conn.SelfUserId] = 0f;
    }

    private void SpawnProxiesForOthersAtAssignedSpawns(MatchTransport.SpawnPoint[] spawns)
    {
        if (spawns == null) return;
        if (!spawner) spawner = FindObjectOfType<PlayerSpawnManager>();

        var selfId = conn != null ? conn.SelfUserId : string.Empty;
        for (var i = 0; i < spawns.Length; i++)
        {
            var spawn = spawns[i];
            if (spawn == null || string.IsNullOrEmpty(spawn.userId)) continue;
            if (spawn.userId == selfId) continue;

            var safeSpawn = spawner ? spawner.ClampInsideMapBounds(spawn.position) : spawn.position;
            if (spawner) spawner.SpawnRemote(spawn.userId, safeSpawn, 0f);
            _spawnByUserId[spawn.userId] = safeSpawn;

            if (isHost)
            {
                _pos[spawn.userId] = safeSpawn;
                _yaw[spawn.userId] = 0f;
            }
        }
    }

    private void StoreGoalPosition(Vector3 goalPos)
    {
        _goalPos = goalPos;
    }

    private void TryApplyInitFromContext()
    {
        var context = MatchContext.Instance;
        if (!context.hasInit || context.lastInit == null) return;

        var lastInit = context.lastInit;
        var alreadyProcessedThisMatch =
            _processedInitId == lastInit.initId &&
            _processedInitMatchId == (conn?.Match?.Id ?? string.Empty);

        if (alreadyProcessedThisMatch) return;

        var startedBefore = context.started;
        ApplyInitOnce(lastInit);
        context.started = startedBefore;
        if (startedBefore) _gameplayStarted = true;
    }

    private void TrySpawnPass()
    {
        if (_spawnPassComplete) return;
        if (!conn || string.IsNullOrEmpty(conn.SelfUserId)) return;

        if (!spawner) spawner = FindObjectOfType<PlayerSpawnManager>();
        if (!spawner) return;
        if (!FindObjectOfType<ProceduralBuildingGenerator>()) return;

        if (TryGetLocalSpawn(out var localSpawn))
        {
            SpawnLocalPlayerAtAssignedSpawn(localSpawn);
        }
        else
        {
            SpawnLocalPlayerAtAssignedSpawn(Vector3.zero);
        }

        SpawnProxiesForOthersAtAssignedSpawns(_cachedInitSpawns);

        // Normalize cached spawn map through the same clamp used for instantiated players.
        if (spawner)
        {
            var keys = new List<string>(_spawnByUserId.Keys);
            for (var i = 0; i < keys.Count; i++)
            {
                var id = keys[i];
                _spawnByUserId[id] = spawner.ClampInsideMapBounds(_spawnByUserId[id]);
            }
        }
        _spawnPassComplete = true;
    }
}
