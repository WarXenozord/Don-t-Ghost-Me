using System;
using System.Text;
using Nakama;
using UnityEngine;

public class MatchTransport : MonoBehaviour
{
    public static MatchTransport Instance { get; private set; }

    public const long OPCODE_INPUT = 1;
    public const long OPCODE_SNAPSHOT = 2;
    public const long OPCODE_INIT = 10;
    public const long OPCODE_READY = 11;
    public const long OPCODE_START = 12;

    public NakamaConnection conn;

    public event Action<InputMsg> OnInput;
    public event Action<SnapshotMsg> OnSnapshot;
    public event Action<InitMsg> OnInit;
    public event Action<ReadyMsg> OnReady;
    public event Action<StartMsg> OnStart;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (!conn) conn = GetComponent<NakamaConnection>();
        if (!conn) conn = FindObjectOfType<NakamaConnection>();
        if (conn == null) return;
        conn.MatchStateReceived += HandleMatchState;
    }

    void OnDestroy()
    {
        if (conn != null)
        {
            conn.MatchStateReceived -= HandleMatchState;
        }
    }

    private void HandleMatchState(IMatchState state)
    {
        var json = Encoding.UTF8.GetString(state.State);
        if (state.OpCode == OPCODE_INPUT)
        {
            var msg = JsonUtility.FromJson<InputMsg>(json);
            msg.senderUserId = state.UserPresence.UserId;
            OnInput?.Invoke(msg);
        }
        else if (state.OpCode == OPCODE_SNAPSHOT)
        {
            var msg = JsonUtility.FromJson<SnapshotMsg>(json);
            OnSnapshot?.Invoke(msg);
        }
        else if (state.OpCode == OPCODE_INIT)
        {
            var msg = JsonUtility.FromJson<InitMsg>(json);
            OnInit?.Invoke(msg);
        }
        else if (state.OpCode == OPCODE_READY)
        {
            var msg = JsonUtility.FromJson<ReadyMsg>(json);
            msg.senderUserId = state.UserPresence.UserId;
            OnReady?.Invoke(msg);
        }
        else if (state.OpCode == OPCODE_START)
        {
            var msg = JsonUtility.FromJson<StartMsg>(json);
            OnStart?.Invoke(msg);
        }
    }

    public async void SendInput(InputMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_INPUT, bytes);
    }

    public async void BroadcastSnapshot(SnapshotMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_SNAPSHOT, bytes);
    }

    public async void BroadcastInit(InitMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_INIT, bytes);
    }

    public async void SendReady(ReadyMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_READY, bytes);
    }

    public async void BroadcastStart(StartMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_START, bytes);
    }

    [Serializable]
    public class InputMsg
    {
        public int seq;
        public float moveX;
        public float moveZ;
        public float yaw;
        public int buttons;
        [NonSerialized] public string senderUserId;
    }

    [Serializable]
    public class SnapshotMsg
    {
        public int tick;
        public PlayerState[] players;
    }

    [Serializable]
    public class InitMsg
    {
        public int initId;
        public int seed;
        public SpawnPoint[] spawns;
        public Vector3 goalPos;
    }

    [Serializable]
    public class SpawnPoint
    {
        public string userId;
        public Vector3 position;
    }

    [Serializable]
    public class ReadyMsg
    {
        public int initId;
        [NonSerialized] public string senderUserId;
    }

    [Serializable]
    public class StartMsg
    {
        public int initId;
    }

    [Serializable]
    public class PlayerState
    {
        public string id;
        public float x, y, z;
        public float yaw;
        public int state;
    }
}
