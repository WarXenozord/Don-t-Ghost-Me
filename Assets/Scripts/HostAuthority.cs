using System.Collections.Generic;
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

    // Authoritative state (host only)
    private readonly Dictionary<string, Vector3> _pos = new();
    private readonly Dictionary<string, float> _yaw = new();
    private readonly Dictionary<string, MatchTransport.InputMsg> _lastInput = new();

    void Awake()
    {
        if (!conn) conn = GetComponent<NakamaConnection>();
        if (!transport) transport = GetComponent<MatchTransport>();

        transport.OnInput += HandleInputFromClient;
    }

    void Start()
    {
        // Simple host rule for MVP:
        // If this instance was configured to create the match, it is the host.
        // (Set this in inspector: one client createMatchIfNone=true, other=false)
        isHost = conn.createMatchIfNone;
        Debug.Log(isHost ? "[HostAuthority] I am HOST" : "[HostAuthority] I am CLIENT");
    }

    void Update()
    {
        if (conn?.Match == null || conn.Socket == null) return;

        if (!isHost)
        {
            // Send input at fixed rate
            _inputTimer += Time.deltaTime;
            if (_inputTimer >= 1f / inputSendHz)
            {
                _inputTimer = 0f;
                transport.SendInput(BuildLocalInput());
            }
        }
        else
        {
            // Host simulates world and broadcasts snapshots
            SimulateHost(Time.deltaTime);

            _snapTimer += Time.deltaTime;
            if (_snapTimer >= 1f / snapshotSendHz)
            {
                _snapTimer = 0f;
                transport.BroadcastSnapshot(BuildSnapshot());
            }
        }
    }

    private MatchTransport.InputMsg BuildLocalInput()
    {
        var h = Input.GetAxisRaw("Horizontal");
        var v = Input.GetAxisRaw("Vertical");

        // Mouse yaw for testing (optional)
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
        if (!isHost) return;

        // Initialize player if first time seen
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
        // Ensure host itself exists in state too
        var selfId = conn.SelfUserId;
        if (!string.IsNullOrEmpty(selfId) && !_pos.ContainsKey(selfId))
        {
            _pos[selfId] = new Vector3(0f, 0.5f, 0f);
            _yaw[selfId] = 0f;
        }

        // Host also uses local input for its own movement
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
        int i = 0;
        foreach (var kv in _pos)
        {
            var id = kv.Key;
            var p = kv.Value;
            players[i++] = new MatchTransport.PlayerState
            {
                id = id,
                x = p.x, y = p.y, z = p.z,
                yaw = _yaw.TryGetValue(id, out var y) ? y : 0f,
                state = 0
            };
        }

        return new MatchTransport.SnapshotMsg { tick = _tick, players = players };
    }
}