using System.Collections.Generic;
using Nakama;
using UnityEngine;

public class HostAuthority : MonoBehaviour
{
    public NakamaConnection conn;
    public MatchTransport transport;

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

    private readonly HashSet<string> _readyUserIds = new HashSet<string>();

    // Authoritative state (host only)
    private readonly Dictionary<string, Vector3> _pos = new Dictionary<string, Vector3>();
    private readonly Dictionary<string, float> _yaw = new Dictionary<string, float>();
    private readonly Dictionary<string, MatchTransport.InputMsg> _lastInput = new Dictionary<string, MatchTransport.InputMsg>();

    void Awake()
    {
        if (!conn) conn = GetComponent<NakamaConnection>();
        if (!transport) transport = GetComponent<MatchTransport>();

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
    }

    void Update()
    {
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
        _gameplayStarted = false;
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
        _gameplayStarted = false;
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
            _pos[msg.senderUserId] = Random.insideUnitSphere * 2f + Vector3.up * 0.5f;
            _pos[msg.senderUserId] = new Vector3(_pos[msg.senderUserId].x, 0.5f, _pos[msg.senderUserId].z);
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
            _pos[selfId] = new Vector3(0f, 0.5f, 0f);
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
}
