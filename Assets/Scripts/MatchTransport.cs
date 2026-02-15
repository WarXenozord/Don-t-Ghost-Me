using System;
using System.Text;
using Nakama;
using UnityEngine;

public class MatchTransport : MonoBehaviour
{
    public const long OPCODE_INPUT = 1;
    public const long OPCODE_SNAPSHOT = 2;

    public NakamaConnection conn;

    public event Action<InputMsg> OnInput;
    public event Action<SnapshotMsg> OnSnapshot;

    void Awake()
    {
        if (!conn) conn = GetComponent<NakamaConnection>();
        conn.MatchStateReceived += HandleMatchState;
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
    public class PlayerState
    {
        public string id;
        public float x, y, z;
        public float yaw;
        public int state;
    }
}