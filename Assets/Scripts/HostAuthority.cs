using System.Collections.Generic;
using Nakama;
using UnityEngine;

public class HostAuthority : MonoBehaviour
{
    public NakamaConnection conn;
    public MatchTransport transport;
    public PlayerSpawnManager spawner;
    public EnemySpawnManager enemySpawner;

    [Header("Host")]
    public bool isHost = false;

    [Header("Tick Rates")]
    public float inputSendHz = 20f;
    public float snapshotSendHz = 10f;

    [Header("Host Remote Visual Smoothing")]
    public float hostRemoteVisualLerp = 16f;
    public float hostRemoteMaxExtrapolation = 0.12f;

    [Header("Enemy Spawn (Host)")]
    [Min(0)] public int startEnemyCount = 0;
    [Min(0f)] public float enemyMinDistanceFromPlayers = 8f;

    [Header("Debug")]
    public bool enableDebugLogs = true;
    public float debugLogInterval = 1f;

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
    private string _mediumUserId = string.Empty;
    private Vector3 _goalPos;
    private MatchTransport.SpawnPoint[] _cachedInitSpawns;
    private bool _spawnPassComplete;

    private readonly HashSet<string> _readyUserIds = new HashSet<string>();
    private readonly Dictionary<string, Vector3> _spawnByUserId = new Dictionary<string, Vector3>();

    // Authoritative state (host only)
    private readonly Dictionary<string, Vector3> _pos = new Dictionary<string, Vector3>();
    private readonly Dictionary<string, float> _yaw = new Dictionary<string, float>();
    private readonly Dictionary<string, MatchTransport.InputMsg> _lastInput = new Dictionary<string, MatchTransport.InputMsg>();
    private readonly Dictionary<string, float> _lastInputRecvAt = new Dictionary<string, float>();
    private readonly Dictionary<string, Vector3> _hostVisualPos = new Dictionary<string, Vector3>();
    private readonly Dictionary<string, float> _hostVisualYaw = new Dictionary<string, float>();
    private bool _transportBound;
    private bool _presenceBound;
    private readonly Dictionary<string, float> _nextInputLogAt = new Dictionary<string, float>();
    private float _nextSnapshotLogAt;
    private float _nextSnapshotPayloadLogAt;
    private bool _initialEnemiesSpawned;

    public string CurrentMediumUserId => _mediumUserId;
    public int ActiveInitId => _activeInitId;

    void Awake()
    {
        ResolveRefs();
        if (!spawner) spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        if (!enemySpawner) enemySpawner = EnemySpawnManager.Instance != null ? EnemySpawnManager.Instance : FindObjectOfType<EnemySpawnManager>();
        EnsureBindings();
    }

    void OnDestroy()
    {
        if (transport && _transportBound)
        {
            transport.OnInput -= HandleInputFromClient;
            transport.OnSnapshot -= OnSnapshotReceived;
            transport.OnInit -= OnInitReceived;
            transport.OnReady -= OnReadyReceived;
            transport.OnStart -= OnStartReceived;
            _transportBound = false;
        }

        if (conn && _presenceBound)
        {
            conn.MatchPresenceReceived -= OnPresenceChanged;
            _presenceBound = false;
        }
    }

    void Start()
    {
        isHost = false;
        TryApplyInitFromContext();
        TrySpawnPass();
    }

    void Update()
    {
        ResolveRefs();
        EnsureBindings();
        TryApplyInitFromContext();
        TrySpawnPass();

        // Fallback for scene/focus timing: if START was already recorded in context,
        // make sure this authority instance enters gameplay even if it missed the event callback.
        if (!_gameplayStarted && conn != null && conn.Match != null && MatchContext.Instance.started)
        {
            _gameplayStarted = true;
        }

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
            if (!_initialEnemiesSpawned)
            {
                TrySpawnInitialEnemies();
            }

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

        LogDebug($"BEGIN_INIT | host={isHost} initId={_activeInitId} match={conn.Match.Id} players={GetPresentUserIds().Count}");
    }

    public void EnableGameplayAfterBootstrap()
    {
        if (conn == null || conn.Match == null) return;
        if (!MatchContext.Instance.started) return;
        _gameplayStarted = true;
    }

    public bool HostSpawnEnemyCommand(Vector3 position, float yaw = 0f, string prefabId = "default")
    {
        if (conn == null || !conn.IsCurrentPlayerMatchCreator) return false;
        if (!enemySpawner) enemySpawner = EnemySpawnManager.Instance != null ? EnemySpawnManager.Instance : FindObjectOfType<EnemySpawnManager>();
        if (!enemySpawner) return false;
        return enemySpawner.HostCommandSpawnEnemy(position, yaw, prefabId);
    }

    private MatchTransport.InitMsg BuildInitMessage(int initId)
    {
        var seed = Random.Range(1, int.MaxValue);
        var goalPos = GetGoalPositionPlaceholder();
        var spawns = BuildSpawnPointsPlaceholder();
        var mediumUserId = DetermineMediumUserId();
        return new MatchTransport.InitMsg
        {
            initId = initId,
            seed = seed,
            spawns = spawns,
            goalPos = goalPos,
            mediumUserId = mediumUserId
        };
    }

    private string DetermineMediumUserId()
    {
        var creator = conn != null ? conn.MatchCreatorUserId : string.Empty;
        if (!string.IsNullOrEmpty(creator)) return creator;

        var self = conn != null ? conn.SelfUserId : string.Empty;
        if (!string.IsNullOrEmpty(self)) return self;

        var users = GetPresentUserIds();
        return users.Count > 0 ? users[0] : string.Empty;
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
        LogDebug($"RECV_INIT | initId={msg.initId} spawns={(msg.spawns == null ? 0 : msg.spawns.Length)} self={conn?.SelfUserId}");

        if (conn != null && !conn.IsCurrentPlayerMatchCreator)
        {
            transport.SendReady(new MatchTransport.ReadyMsg { initId = msg.initId });
            LogDebug($"SEND_READY | initId={msg.initId} self={conn.SelfUserId}");
        }
    }

    private void ApplyInitOnce(MatchTransport.InitMsg msg)
    {
        _processedInitId = msg.initId;
        _processedInitMatchId = conn?.Match?.Id ?? string.Empty;
        _activeInitId = msg.initId;
        _mediumUserId = msg.mediumUserId;
        _gameplayStarted = false;
        _initSent = true;

        _pos.Clear();
        _yaw.Clear();
        _lastInput.Clear();
        _lastInputRecvAt.Clear();
        _hostVisualPos.Clear();
        _hostVisualYaw.Clear();
        _tick = 0;
        _spawnByUserId.Clear();
        _cachedInitSpawns = msg.spawns;
        _spawnPassComplete = false;
        _initialEnemiesSpawned = false;

        if (string.IsNullOrEmpty(_mediumUserId))
        {
            _mediumUserId = DetermineMediumUserId();
        }

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
        LogDebug($"RECV_READY | from={msg.senderUserId} initId={msg.initId} ready={_readyUserIds.Count}");
        TrySendStartIfEveryoneReady();
    }

    private void OnStartReceived(MatchTransport.StartMsg msg)
    {
        if (msg == null || msg.initId != _activeInitId) return;
        MatchContext.Instance.started = true;
        _gameplayStarted = true;
        LogDebug($"RECV_START | initId={msg.initId} gameplayStarted={_gameplayStarted}");
    }

    private void ResetMatchRuntimeState()
    {
        _gameplayStarted = false;
        _initSent = false;
        _startSent = false;
        _activeInitId = -1;
        _processedInitId = -1;
        _processedInitMatchId = string.Empty;
        _mediumUserId = string.Empty;

        _readyUserIds.Clear();
        _spawnByUserId.Clear();
        _cachedInitSpawns = null;
        _spawnPassComplete = false;
        _pos.Clear();
        _yaw.Clear();
        _lastInput.Clear();
        _lastInputRecvAt.Clear();
        _hostVisualPos.Clear();
        _hostVisualYaw.Clear();
        _tick = 0;
        _initialEnemiesSpawned = false;

        if (!spawner) spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        if (spawner) spawner.ClearAll();
    }

    private void OnPresenceChanged(IMatchPresenceEvent e)
    {
        if (e != null && e.Joins != null)
        {
            foreach (var join in e.Joins)
            {
                if (join == null || string.IsNullOrEmpty(join.UserId)) continue;
                if (_spawnByUserId.ContainsKey(join.UserId)) continue;

                var spawn = ResolveSpawnForUser(join.UserId);
                _spawnByUserId[join.UserId] = spawn;

                if (join.UserId != conn?.SelfUserId && spawner != null && !spawner.TryGet(join.UserId, out _))
                {
                    spawner.SpawnRemote(join.UserId, spawn, 0f);
                }
            }
        }

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
        LogDebug($"SEND_START | initId={_activeInitId} ready={_readyUserIds.Count} present={presentUserIds.Count}");
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
        var yaw = GetLocalYaw();
        var vel = GetLocalNetworkVelocity();
        var pos = GetLocalNetworkPosition();

        return new MatchTransport.InputMsg
        {
            seq = ++_seq,
            yaw = yaw,
            posX = pos.x,
            posY = pos.y,
            posZ = pos.z,
            velX = vel.x,
            velY = vel.y,
            velZ = vel.z,
            buttons = 0
        };
    }

    private void HandleInputFromClient(MatchTransport.InputMsg msg)
    {
        if (!isHost || !_gameplayStarted)
        {
            if (msg != null && !string.IsNullOrEmpty(msg.senderUserId))
            {
                LogInputDebug(msg.senderUserId, $"DROP_INPUT | isHost={isHost} gameplayStarted={_gameplayStarted}");
            }
            return;
        }
        if (msg == null || string.IsNullOrEmpty(msg.senderUserId)) return;

        if (!_pos.ContainsKey(msg.senderUserId))
        {
            var spawn = ResolveSpawnForUser(msg.senderUserId);
            _spawnByUserId[msg.senderUserId] = spawn;
            _pos[msg.senderUserId] = spawn;
            _yaw[msg.senderUserId] = 0f;
            _hostVisualPos[msg.senderUserId] = spawn;
            _hostVisualYaw[msg.senderUserId] = 0f;

            if (spawner != null && msg.senderUserId != conn?.SelfUserId && !spawner.TryGet(msg.senderUserId, out _))
            {
                spawner.SpawnRemote(msg.senderUserId, spawn, 0f);
            }

            LogInputDebug(msg.senderUserId, $"FIRST_INPUT | spawn=({spawn.x:F2},{spawn.y:F2},{spawn.z:F2})");
        }

        _lastInput[msg.senderUserId] = msg;
        _lastInputRecvAt[msg.senderUserId] = Time.unscaledTime;
        _pos[msg.senderUserId] = new Vector3(msg.posX, msg.posY, msg.posZ);
        _yaw[msg.senderUserId] = msg.yaw;
        if (!_hostVisualPos.ContainsKey(msg.senderUserId))
        {
            _hostVisualPos[msg.senderUserId] = _pos[msg.senderUserId];
            _hostVisualYaw[msg.senderUserId] = msg.yaw;
        }
        if (_pos.TryGetValue(msg.senderUserId, out var p))
        {
            LogInputDebug(
                msg.senderUserId,
                $"RECV_INPUT | pos=({msg.posX:F2},{msg.posY:F2},{msg.posZ:F2}) vel=({msg.velX:F2},{msg.velY:F2},{msg.velZ:F2}) yaw={msg.yaw:F1} authPos=({p.x:F2},{p.y:F2},{p.z:F2})"
            );
        }
    }

    private void SimulateHost(float dt)
    {
        var selfId = conn.SelfUserId;
        if (!string.IsNullOrEmpty(selfId) && !_pos.ContainsKey(selfId))
        {
            var selfSpawn = ResolveSpawnForUser(selfId);
            _pos[selfId] = selfSpawn;
            _yaw[selfId] = 0f;
        }

        if (string.IsNullOrEmpty(selfId)) return;

        var selfInput = BuildLocalInput();
        _lastInput[selfId] = selfInput;
        _yaw[selfId] = selfInput.yaw;
        _pos[selfId] = new Vector3(selfInput.posX, selfInput.posY, selfInput.posZ);

        // Host-side visual update for remotes, so host doesn't depend on receiving its own snapshots.
        if (!spawner) spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        if (spawner != null)
        {
            foreach (var kv in _pos)
            {
                var id = kv.Key;
                if (id == selfId) continue;

                var targetPos = kv.Value;
                if (_lastInput.TryGetValue(id, out var lastRemoteInput) && _lastInputRecvAt.TryGetValue(id, out var recvAt))
                {
                    var age = Mathf.Clamp(Time.unscaledTime - recvAt, 0f, hostRemoteMaxExtrapolation);
                    var vel = new Vector3(lastRemoteInput.velX, lastRemoteInput.velY, lastRemoteInput.velZ);
                    targetPos += vel * age;
                }

                var targetYaw = _yaw.TryGetValue(id, out var yy) ? yy : 0f;
                var currentVisualPos = _hostVisualPos.TryGetValue(id, out var vp) ? vp : targetPos;
                var currentVisualYaw = _hostVisualYaw.TryGetValue(id, out var vy) ? vy : targetYaw;

                var t = 1f - Mathf.Exp(-Mathf.Max(0.1f, hostRemoteVisualLerp) * Time.deltaTime);
                var nextVisualPos = Vector3.Lerp(currentVisualPos, targetPos, t);
                var nextVisualYaw = Mathf.LerpAngle(currentVisualYaw, targetYaw, t);

                _hostVisualPos[id] = nextVisualPos;
                _hostVisualYaw[id] = nextVisualYaw;
                spawner.ApplyAuthoritativePose(id, nextVisualPos, nextVisualYaw);
            }
        }

        if (enableDebugLogs && Time.unscaledTime >= _nextSnapshotLogAt)
        {
            _nextSnapshotLogAt = Time.unscaledTime + Mathf.Max(0.1f, debugLogInterval);
            LogDebug($"HOST_SIM | trackedPlayers={_pos.Count} inputs={_lastInput.Count} tick={_tick}");
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

        if (enableDebugLogs && Time.unscaledTime >= _nextSnapshotPayloadLogAt)
        {
            _nextSnapshotPayloadLogAt = Time.unscaledTime + Mathf.Max(0.1f, debugLogInterval);
            MatchTransport.PlayerState sample = null;
            for (var s = 0; s < players.Length; s++)
            {
                if (players[s] == null) continue;
                if (players[s].id == conn?.SelfUserId) continue;
                sample = players[s];
                break;
            }

            if (sample != null)
            {
                LogDebug($"SNAPSHOT_PAYLOAD | count={players.Length} sampleUser={sample.id} samplePos=({sample.x:F2},{sample.y:F2},{sample.z:F2}) sampleYaw={sample.yaw:F1}");
            }
            else
            {
                LogDebug($"SNAPSHOT_PAYLOAD | count={players.Length} sampleUser=selfOnly");
            }
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
        if (!spawner) spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
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
        if (!spawner) spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();

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
                _hostVisualPos[spawn.userId] = safeSpawn;
                _hostVisualYaw[spawn.userId] = 0f;
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

        if (!spawner) spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
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

    private Vector3 ResolveSpawnForUser(string userId)
    {
        if (!string.IsNullOrEmpty(userId) && _spawnByUserId.TryGetValue(userId, out var known))
        {
            return known;
        }

        var baseSpawn = new Vector3(0f, 0.5f, 0f);
        var hash = string.IsNullOrEmpty(userId) ? 0 : Mathf.Abs(userId.GetHashCode());
        var ring = (hash % 5) + 1;
        var angle = (hash % 360) * Mathf.Deg2Rad;
        var candidate = new Vector3(Mathf.Cos(angle) * ring * 1.5f, 0.5f, Mathf.Sin(angle) * ring * 1.5f);

        if (!spawner) spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        if (spawner != null)
        {
            return spawner.ClampInsideMapBounds(candidate);
        }

        return baseSpawn;
    }

    private void ResolveRefs()
    {
        if (!conn) conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : GetComponent<NakamaConnection>();
        if (!conn) conn = FindObjectOfType<NakamaConnection>();
        if (!transport) transport = MatchTransport.Instance != null ? MatchTransport.Instance : GetComponent<MatchTransport>();
        if (!transport) transport = FindObjectOfType<MatchTransport>();
        if (!enemySpawner) enemySpawner = EnemySpawnManager.Instance != null ? EnemySpawnManager.Instance : FindObjectOfType<EnemySpawnManager>();
    }

    private void EnsureBindings()
    {
        if (transport && !_transportBound)
        {
            transport.OnInput += HandleInputFromClient;
            transport.OnSnapshot += OnSnapshotReceived;
            transport.OnInit += OnInitReceived;
            transport.OnReady += OnReadyReceived;
            transport.OnStart += OnStartReceived;
            _transportBound = true;
        }

        if (conn && !_presenceBound)
        {
            conn.MatchPresenceReceived += OnPresenceChanged;
            _presenceBound = true;
        }
    }

    private void LogDebug(string message)
    {
        if (!enableDebugLogs) return;
        Debug.Log("[HostAuthority] " + message);
    }

    private void LogInputDebug(string userId, string message)
    {
        if (!enableDebugLogs) return;
        var now = Time.unscaledTime;
        if (_nextInputLogAt.TryGetValue(userId, out var nextAt) && now < nextAt) return;
        _nextInputLogAt[userId] = now + Mathf.Max(0.1f, debugLogInterval);
        Debug.Log("[HostAuthority] " + message + " | user=" + userId);
    }

    private float GetLocalYaw()
    {
        if (TryGetLocalControlledTransform(out var controlled))
        {
            return controlled.eulerAngles.y;
        }

        return transform.eulerAngles.y;
    }

    private Vector3 GetLocalNetworkVelocity()
    {
        var selfId = conn != null ? conn.SelfUserId : string.Empty;
        if (!string.IsNullOrEmpty(selfId))
        {
            if (!spawner) spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
            if (spawner != null && spawner.TryGet(selfId, out var localGo) && localGo != null)
            {
                var controller = localGo.GetComponentInChildren<MediumController>(true);
                if (controller != null) return controller.NetworkVelocity;
                var ghost = localGo.GetComponentInChildren<GhostController>(true);
                if (ghost != null) return ghost.NetworkVelocity;
            }
        }

        return Vector3.zero;
    }

    private Vector3 GetLocalNetworkPosition()
    {
        if (TryGetLocalControlledTransform(out var controlled))
        {
            return controlled.position;
        }

        return transform.position;
    }

    private bool TryGetLocalControlledTransform(out Transform controlledTransform)
    {
        controlledTransform = null;
        var selfId = conn != null ? conn.SelfUserId : string.Empty;
        if (string.IsNullOrEmpty(selfId)) return false;

        if (!spawner) spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        if (spawner == null || !spawner.TryGet(selfId, out var localGo) || localGo == null) return false;

        var medium = localGo.GetComponentInChildren<MediumController>(true);
        if (medium != null && medium.enabled)
        {
            controlledTransform = medium.transform;
            return true;
        }

        var ghost = localGo.GetComponentInChildren<GhostController>(true);
        if (ghost != null && ghost.enabled)
        {
            controlledTransform = ghost.transform;
            return true;
        }

        controlledTransform = localGo.transform;
        return true;
    }

    private void OnSnapshotReceived(MatchTransport.SnapshotMsg snap)
    {
        if (conn != null && !conn.IsCurrentPlayerMatchCreator) return;
        if (snap == null || snap.players == null) return;
        if (!spawner) spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        if (spawner == null) return;

        var selfId = conn != null ? conn.SelfUserId : string.Empty;
        for (var i = 0; i < snap.players.Length; i++)
        {
            var ps = snap.players[i];
            if (ps == null || string.IsNullOrEmpty(ps.id)) continue;
            if (!string.IsNullOrEmpty(selfId) && ps.id == selfId) continue;

            var pos = new Vector3(ps.x, ps.y, ps.z);
            if (!spawner.TryGet(ps.id, out _))
            {
                spawner.SpawnRemote(ps.id, pos, ps.yaw);
            }
            spawner.ApplyAuthoritativePose(ps.id, pos, ps.yaw);
        }
    }

    private void TrySpawnInitialEnemies()
    {
        if (startEnemyCount <= 0)
        {
            _initialEnemiesSpawned = true;
            return;
        }

        if (!enemySpawner) enemySpawner = EnemySpawnManager.Instance != null ? EnemySpawnManager.Instance : FindObjectOfType<EnemySpawnManager>();
        if (!enemySpawner) return;

        var generator = FindObjectOfType<ProceduralBuildingGenerator>();
        if (!generator) return;

        var roomCenters = new List<Vector3>();
        generator.CollectRoomCenterNodes(roomCenters, preferredFloor: 0);
        if (roomCenters.Count == 0) return;

        var players = new List<Vector3>();
        foreach (var kv in _spawnByUserId)
        {
            players.Add(kv.Value);
        }

        if (players.Count == 0 && !string.IsNullOrEmpty(conn?.SelfUserId))
        {
            if (!spawner) spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
            if (spawner != null && spawner.TryGet(conn.SelfUserId, out var selfGo) && selfGo != null)
            {
                players.Add(selfGo.transform.position);
            }
        }

        if (players.Count == 0)
        {
            _initialEnemiesSpawned = true;
            return;
        }

        var used = new HashSet<int>();
        var spawned = 0;
        for (var n = 0; n < startEnemyCount; n++)
        {
            var chosen = ChooseSpawnNodeIndex(roomCenters, players, used, enemyMinDistanceFromPlayers);
            if (chosen < 0) break;

            used.Add(chosen);
            var ok = enemySpawner.HostCommandSpawnEnemy(roomCenters[chosen], 0f, "default");
            if (ok) spawned++;
        }

        _initialEnemiesSpawned = true;
        LogDebug($"ENEMY_START_SPAWN | requested={startEnemyCount} spawned={spawned} minDist={enemyMinDistanceFromPlayers:F1}");
    }

    private static int ChooseSpawnNodeIndex(List<Vector3> nodes, List<Vector3> players, HashSet<int> used, float minDistance)
    {
        var eligible = new List<int>();
        var bestFallback = -1;
        var bestFallbackDist = float.MinValue;

        for (var i = 0; i < nodes.Count; i++)
        {
            if (used.Contains(i)) continue;
            var node = nodes[i];

            var minDist = float.MaxValue;
            for (var p = 0; p < players.Count; p++)
            {
                var d = Vector3.Distance(node, players[p]);
                if (d < minDist) minDist = d;
            }

            if (minDist >= minDistance)
            {
                eligible.Add(i);
            }

            if (minDist > bestFallbackDist)
            {
                bestFallbackDist = minDist;
                bestFallback = i;
            }
        }

        if (eligible.Count > 0)
        {
            return eligible[Random.Range(0, eligible.Count)];
        }

        return bestFallback;
    }
}
