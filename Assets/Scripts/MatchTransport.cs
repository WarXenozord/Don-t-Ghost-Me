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
    public const long OPCODE_ENEMY_SPAWN = 20;
    public const long OPCODE_ENEMY_SNAPSHOT = 21;
    public const long OPCODE_ENEMY_TELEPORT = 22;
    public const long OPCODE_ENEMY_FX = 23;
    public const long OPCODE_GHOST_SPAWN = 30;
    public const long OPCODE_LAMP_FLICKER = 31;
    public const long OPCODE_OBJECTIVE_STATE = 32;
    public const long OPCODE_CHAT = 33;
    public const long OPCODE_ANIM = 34;
    public const long OPCODE_CHAIR_THROW = 35;
    public const long OPCODE_DISPLAY_NAME = 38;
    public const long OPCODE_CHAIR_STATE = 36;
    public const long OPCODE_LOBBY_JOIN_REQUEST = 39;
    public const long OPCODE_LOBBY_PLACEHOLDER_SPAWN = 40;

    public NakamaConnection conn;

    public event Action<InputMsg> OnInput;
    public event Action<SnapshotMsg> OnSnapshot;
    public event Action<InitMsg> OnInit;
    public event Action<ReadyMsg> OnReady;
    public event Action<StartMsg> OnStart;
    public event Action<EnemySpawnMsg> OnEnemySpawn;
    public event Action<EnemySnapshotMsg> OnEnemySnapshot;
    public event Action<EnemyTeleportMsg> OnEnemyTeleport;
    public event Action<EnemyFxMsg> OnEnemyFx;
    public event Action<GhostSpawnMsg> OnGhostSpawn;
    public event Action<LampFlickerMsg> OnLampFlicker;
    public event Action<ObjectiveStateMsg> OnObjectiveState;
    public event Action<ChatMsg> OnChat;
    public event Action<AnimMsg> OnAnim;
    public event Action<ChairThrowMsg> OnChairThrow;
    public event Action<DisplayNameMsg> OnDisplayName;
    public event Action<ChairStateMsg> OnChairState;
    public event Action<LobbyJoinRequestMsg> OnLobbyJoinRequest;
    public event Action<LobbyPlaceholderSpawnMsg> OnLobbyPlaceholderSpawn;
    private bool _isBound;
    private NakamaConnection _boundConn;

    [Header("Debug")]
    public bool enableDebugLogs = true;
    public float debugLogInterval = 1f;
    private float _nextRecvSnapshotLogAt;
    private float _nextSendSnapshotLogAt;
    private float _nextRecvEnemySnapshotLogAt;
    private float _nextSendEnemySnapshotLogAt;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        ResolveConn();
        EnsureBound();
    }

    void Update()
    {
        ResolveConn();
        EnsureBound();
    }

    void OnDestroy()
    {
        if (_boundConn != null && _isBound)
        {
            _boundConn.MatchStateReceived -= HandleMatchState;
            _isBound = false;
            _boundConn = null;
        }
    }

    private void ResolveConn()
    {
        if (!conn) conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : GetComponent<NakamaConnection>();
        if (!conn) conn = FindObjectOfType<NakamaConnection>();
    }

    private void EnsureBound()
    {
        if (conn == null) return;
        if (_isBound && _boundConn == conn) return;

        if (_isBound && _boundConn != null)
        {
            _boundConn.MatchStateReceived -= HandleMatchState;
            _isBound = false;
        }

        conn.MatchStateReceived += HandleMatchState;
        _boundConn = conn;
        _isBound = true;
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
            if (enableDebugLogs && Time.unscaledTime >= _nextRecvSnapshotLogAt)
            {
                _nextRecvSnapshotLogAt = Time.unscaledTime + Mathf.Max(0.1f, debugLogInterval);
                Debug.Log("[MatchTransport] RECV_SNAPSHOT players=" + (msg.players == null ? 0 : msg.players.Length));
            }
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
        else if (state.OpCode == OPCODE_ENEMY_SPAWN)
        {
            var msg = JsonUtility.FromJson<EnemySpawnMsg>(json);
            msg.senderUserId = state.UserPresence.UserId;
            OnEnemySpawn?.Invoke(msg);
        }
        else if (state.OpCode == OPCODE_ENEMY_SNAPSHOT)
        {
            var msg = JsonUtility.FromJson<EnemySnapshotMsg>(json);
            OnEnemySnapshot?.Invoke(msg);
            if (enableDebugLogs && Time.unscaledTime >= _nextRecvEnemySnapshotLogAt)
            {
                _nextRecvEnemySnapshotLogAt = Time.unscaledTime + Mathf.Max(0.1f, debugLogInterval);
                Debug.Log("[MatchTransport] RECV_ENEMY_SNAPSHOT enemies=" + (msg.enemies == null ? 0 : msg.enemies.Length));
            }
        }
        else if (state.OpCode == OPCODE_ENEMY_TELEPORT)
        {
            var msg = JsonUtility.FromJson<EnemyTeleportMsg>(json);
            msg.senderUserId = state.UserPresence.UserId;
            OnEnemyTeleport?.Invoke(msg);
        }
        else if (state.OpCode == OPCODE_ENEMY_FX)
        {
            var msg = JsonUtility.FromJson<EnemyFxMsg>(json);
            msg.senderUserId = state.UserPresence.UserId;
            OnEnemyFx?.Invoke(msg);
            if (enableDebugLogs)
            {
                Debug.Log("[MatchTransport] RECV_ENEMY_FX id=" + msg.spawnId + " fx=" + msg.fxId);
            }
        }
        else if (state.OpCode == OPCODE_GHOST_SPAWN)
        {
            var msg = JsonUtility.FromJson<GhostSpawnMsg>(json);
            msg.senderUserId = state.UserPresence.UserId;
            OnGhostSpawn?.Invoke(msg);
            if (enableDebugLogs)
            {
                Debug.Log("[MatchTransport] RECV_GHOST_SPAWN user=" + msg.userId);
            }
        }
        else if (state.OpCode == OPCODE_LAMP_FLICKER)
        {
            var msg = JsonUtility.FromJson<LampFlickerMsg>(json);
            msg.senderUserId = state.UserPresence.UserId;
            OnLampFlicker?.Invoke(msg);
            if (enableDebugLogs)
            {
                Debug.Log("[MatchTransport] RECV_LAMP_FLICKER lampId=" + msg.lampId);
            }
        }
        else if (state.OpCode == OPCODE_OBJECTIVE_STATE)
        {
            var msg = JsonUtility.FromJson<ObjectiveStateMsg>(json);
            msg.senderUserId = state.UserPresence.UserId;
            OnObjectiveState?.Invoke(msg);
            if (enableDebugLogs)
            {
                Debug.Log("[MatchTransport] RECV_OBJECTIVE_STATE candles=" + msg.collectedCount + " ritual=" + msg.ritualComplete);
            }
        }
        else if (state.OpCode == OPCODE_CHAT)
        {
            var msg = JsonUtility.FromJson<ChatMsg>(json);
            msg.senderUserId = state.UserPresence.UserId;
            OnChat?.Invoke(msg);
            if (enableDebugLogs)
            {
                Debug.Log("[MatchTransport] RECV_CHAT from=" + msg.senderUserId + " target=" + msg.target);
            }
        }
        else if (state.OpCode == OPCODE_ANIM)
        {
            var msg = JsonUtility.FromJson<AnimMsg>(json);
            msg.senderUserId = state.UserPresence.UserId;
            OnAnim?.Invoke(msg);
        }
        else if (state.OpCode == OPCODE_CHAIR_THROW)
        {
            var msg = JsonUtility.FromJson<ChairThrowMsg>(json);
            msg.senderUserId = state.UserPresence.UserId;
            OnChairThrow?.Invoke(msg);
        }
        else if (state.OpCode == OPCODE_DISPLAY_NAME)
        {
            var msg = JsonUtility.FromJson<DisplayNameMsg>(json);
            msg.senderUserId = state.UserPresence.UserId;
            OnDisplayName?.Invoke(msg);
            if (enableDebugLogs)
            {
                Debug.Log("[MatchTransport] RECV_DISPLAY_NAME from=" + msg.senderUserId + " name=" + msg.displayName);
            }
        }
        else if (state.OpCode == OPCODE_CHAIR_STATE)
        {
            var msg = JsonUtility.FromJson<ChairStateMsg>(json);
            msg.senderUserId = state.UserPresence.UserId;
            OnChairState?.Invoke(msg);
        }
        else if (state.OpCode == OPCODE_LOBBY_JOIN_REQUEST)
        {
            var msg = JsonUtility.FromJson<LobbyJoinRequestMsg>(json);
            msg.senderUserId = state.UserPresence.UserId;
            OnLobbyJoinRequest?.Invoke(msg);
        }
        else if (state.OpCode == OPCODE_LOBBY_PLACEHOLDER_SPAWN)
        {
            var msg = JsonUtility.FromJson<LobbyPlaceholderSpawnMsg>(json);
            msg.senderUserId = state.UserPresence.UserId;
            OnLobbyPlaceholderSpawn?.Invoke(msg);
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
        if (enableDebugLogs && Time.unscaledTime >= _nextSendSnapshotLogAt)
        {
            _nextSendSnapshotLogAt = Time.unscaledTime + Mathf.Max(0.1f, debugLogInterval);
            Debug.Log("[MatchTransport] SEND_SNAPSHOT players=" + (msg.players == null ? 0 : msg.players.Length));
        }
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

    public async void BroadcastEnemySpawn(EnemySpawnMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_ENEMY_SPAWN, bytes);
    }

    public async void BroadcastEnemySnapshot(EnemySnapshotMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_ENEMY_SNAPSHOT, bytes);
        if (enableDebugLogs && Time.unscaledTime >= _nextSendEnemySnapshotLogAt)
        {
            _nextSendEnemySnapshotLogAt = Time.unscaledTime + Mathf.Max(0.1f, debugLogInterval);
            Debug.Log("[MatchTransport] SEND_ENEMY_SNAPSHOT enemies=" + (msg.enemies == null ? 0 : msg.enemies.Length));
        }
    }

    public async void BroadcastEnemyTeleport(EnemyTeleportMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_ENEMY_TELEPORT, bytes);
    }

    public async void BroadcastEnemyFx(EnemyFxMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_ENEMY_FX, bytes);
        if (enableDebugLogs)
        {
            Debug.Log("[MatchTransport] SEND_ENEMY_FX id=" + msg.spawnId + " fx=" + msg.fxId);
        }
    }

    public async void BroadcastGhostSpawn(GhostSpawnMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_GHOST_SPAWN, bytes);
        if (enableDebugLogs)
        {
            Debug.Log("[MatchTransport] SEND_GHOST_SPAWN user=" + msg.userId);
        }
    }

    public async void BroadcastLampFlicker(LampFlickerMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_LAMP_FLICKER, bytes);
        if (enableDebugLogs)
        {
            Debug.Log("[MatchTransport] SEND_LAMP_FLICKER lampId=" + msg.lampId);
        }
    }

    public async void BroadcastObjectiveState(ObjectiveStateMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_OBJECTIVE_STATE, bytes);
        if (enableDebugLogs)
        {
            Debug.Log("[MatchTransport] SEND_OBJECTIVE_STATE candles=" + msg.collectedCount + " ritual=" + msg.ritualComplete);
        }
    }

    public async void SendChat(ChatMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_CHAT, bytes);
        if (enableDebugLogs)
        {
            Debug.Log("[MatchTransport] SEND_CHAT target=" + msg.target);
        }
    }

    public async void SendAnim(AnimMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_ANIM, bytes);
    }

    public async void BroadcastAnim(AnimMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_ANIM, bytes);
    }

    public async void BroadcastChairThrow(ChairThrowMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_CHAIR_THROW, bytes);
    }
    public async void BroadcastDisplayName(DisplayNameMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_DISPLAY_NAME, bytes);
        if (enableDebugLogs)
        {
            Debug.Log("[MatchTransport] SEND_DISPLAY_NAME name=" + msg.displayName);
        }
    }

    public async void BroadcastChairState(ChairStateMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_CHAIR_STATE, bytes);
    }

    public async void SendLobbyJoinRequest(LobbyJoinRequestMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_LOBBY_JOIN_REQUEST, bytes);
    }

    public async void BroadcastLobbyPlaceholderSpawn(LobbyPlaceholderSpawnMsg msg)
    {
        if (conn?.Socket == null || conn.Match == null) return;
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_LOBBY_PLACEHOLDER_SPAWN, bytes);
    }

    [Serializable]
    public class InputMsg
    {
        public int seq;
        public float yaw;
        public float posX;
        public float posY;
        public float posZ;
        public float velX;
        public float velY;
        public float velZ;
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
        public string mediumUserId;
    }

    [Serializable]
    public class SpawnPoint
    {
        public string userId;
        public Vector3 position;
        public int slotIndex;
        public int modelIndex;
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
    public class EnemySpawnMsg
    {
        public string spawnId;
        public string prefabId;
        public float x, y, z;
        public float yaw;
        [NonSerialized] public string senderUserId;
    }

    [Serializable]
    public class EnemySnapshotMsg
    {
        public int tick;
        public EnemyNetState[] enemies;
    }

    [Serializable]
    public class EnemyNetState
    {
        public string spawnId;
        public float x, y, z;
        public float yaw;
        public int aiState;
    }

    [Serializable]
    public class EnemyTeleportMsg
    {
        public string spawnId;
        public float x, y, z;
        public float yaw;
        public int reason;
        [NonSerialized] public string senderUserId;
    }

    [Serializable]
    public class EnemyFxMsg
    {
        public string spawnId;
        public int fxId;
        [NonSerialized] public string senderUserId;
    }

    [Serializable]
    public class GhostSpawnMsg
    {
        public string userId;
        public float x, y, z;
        public float yaw;
        [NonSerialized] public string senderUserId;
    }

    [Serializable]
    public class LampFlickerMsg
    {
        public string lampId;
        [NonSerialized] public string senderUserId;
    }

    [Serializable]
    public class ObjectiveStateMsg
    {
        public string candleId;
        public int collectedCount;
        public bool ritualComplete;
        [NonSerialized] public string senderUserId;
    }

    [Serializable]
    public class ChatMsg
    {
        public int initId;
        public string senderRole;
        public string text;
        public string target;
        public int cost;
        [NonSerialized] public string senderUserId;
    }

    [Serializable]
    public class PlayerState
    {
        public string id;
        public float x, y, z;
        public float yaw;
    }

    [Serializable]
    public class AnimMsg
    {
        public string userId;
        public int state;
        public int tick;
        [NonSerialized] public string senderUserId;
    }

    [Serializable]
    public class ChairThrowMsg
    {
        public string chairId;
        public float startPosX;
        public float startPosY;
        public float startPosZ;
        public float startYaw;
        public float startRotX;
        public float startRotY;
        public float startRotZ;
        public float startRotW;
        public float dirX;
        public float dirY;
        public float dirZ;
        public float force;
        public float upward;
        public float torqueX;
        public float torqueY;
        public float torqueZ;
        [NonSerialized] public string senderUserId;
    }
     [Serializable]
    public class DisplayNameMsg
    {
        public string displayName;
        [NonSerialized] public string senderUserId;
        }
    [Serializable]
    public class ChairStateMsg
    {
        public string chairId;
        public int state;
        public float posX;
        public float posY;
        public float posZ;
        public float rotX;
        public float rotY;
        public float rotZ;
        public float rotW;
        public float velX;
        public float velY;
        public float velZ;
        public float angVelX;
        public float angVelY;
        public float angVelZ;
        [NonSerialized] public string senderUserId;
    }

    [Serializable]
    public class LobbyJoinRequestMsg
    {
        public string userId;
        [NonSerialized] public string senderUserId;
    }

    [Serializable]
    public class LobbyPlaceholderSpawnMsg
    {
        public string userId;
        public int slotIndex;
        [NonSerialized] public string senderUserId;
    }
}

