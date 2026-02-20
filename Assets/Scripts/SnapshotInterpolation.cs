using System.Collections.Generic;
using UnityEngine;

public class SnapshotInterpolation : MonoBehaviour
{
    public MatchTransport transport;
    public NakamaConnection conn;
    public PlayerSpawnManager spawner;

    [Header("Interpolation")]
    public float interpolationBackTime = 0.10f;
    public float maxExtrapolationTime = 0.15f;
    public float snapDistance = 5f;
    public float positionLerpGain = 25f;
    public float yawLerpGain = 25f;
    public int maxSamplesPerPlayer = 12;
    public bool correctLocalWithSnapshots = false;

    [Header("Debug")]
    public bool enableDebugLogs = true;
    public float debugLogInterval = 1f;
    public bool verboseMotionDebug = false;

    private const int ANIM_IDLE = 0;
    private const int ANIM_WALK = 1;
    private const int ANIM_RUN = 2;
    private const int ANIM_JUMP = 3;

    private readonly Dictionary<string, int> _lastAnimState = new Dictionary<string, int>();
    private readonly Dictionary<string, int> _targetAnimState = new Dictionary<string, int>();

    private struct NetSample
    {
        public float recvTime;
        public Vector3 pos;
        public float yaw;
    }

    private readonly Dictionary<string, List<NetSample>> _buffersByUser = new Dictionary<string, List<NetSample>>();
    private bool _bound;
    private float _nextSnapshotLogAt;
    private float _nextMotionLogAt;

    void Awake()
    {
        ResolveRefs();
        EnsureBound();
    }

    void OnDestroy()
    {
        if (transport != null && _bound)
        {
            transport.OnSnapshot -= OnSnapshot;
            transport.OnAnim -= OnAnim;
            _bound = false;
        }
    }

    void Update()
    {
        ResolveRefs();
        EnsureBound();
        if (!spawner || conn == null) return;

        var renderTime = Time.unscaledTime - Mathf.Max(0f, interpolationBackTime);
        var selfId = conn.SelfUserId;

        foreach (var kv in _buffersByUser)
        {
            var userId = kv.Key;
            if (string.IsNullOrEmpty(userId)) continue;
            if (!string.IsNullOrEmpty(selfId) && userId == selfId) continue;

            var samples = kv.Value;
            if (samples == null || samples.Count == 0) continue;
            if (!spawner.TryGet(userId, out var go) || go == null) continue;

            var targetPos = samples[samples.Count - 1].pos;
            var targetYaw = samples[samples.Count - 1].yaw;
            ComputeTargetPose(samples, renderTime, out targetPos, out targetYaw);

            var currentPos = go.transform.position;
            var dist = Vector3.Distance(currentPos, targetPos);
            if (dist >= snapDistance)
            {
                go.transform.position = targetPos;
            }
            else
            {
                var tPos = 1f - Mathf.Exp(-positionLerpGain * Time.deltaTime);
                go.transform.position = Vector3.Lerp(currentPos, targetPos, tPos);
            }

            var currentYaw = go.transform.eulerAngles.y;
            var tYaw = 1f - Mathf.Exp(-yawLerpGain * Time.deltaTime);
            var smoothedYaw = Mathf.LerpAngle(currentYaw, targetYaw, tYaw);
            go.transform.rotation = Quaternion.Euler(0f, smoothedYaw, 0f);
            if (_targetAnimState.TryGetValue(userId, out var animState))
            {
                ApplyAnimationState(go, userId, animState);
            }
            if (enableDebugLogs && verboseMotionDebug && Time.unscaledTime >= _nextMotionLogAt)
            {
                _nextMotionLogAt = Time.unscaledTime + Mathf.Max(0.1f, debugLogInterval);
                Debug.Log($"[SnapshotInterp] APPLY user={userId} curr=({currentPos.x:F2},{currentPos.y:F2},{currentPos.z:F2}) target=({targetPos.x:F2},{targetPos.y:F2},{targetPos.z:F2}) dist={dist:F2}");
            }
        }
    }

    private void OnSnapshot(MatchTransport.SnapshotMsg snap)
    {
        if (snap == null || snap.players == null) return;
        ResolveRefs();
        if (!spawner || conn == null) return;

        var selfId = conn.SelfUserId;
        var now = Time.unscaledTime;
        var remotesBuffered = 0;
        var localApplied = false;

        for (var i = 0; i < snap.players.Length; i++)
        {
            var ps = snap.players[i];
            if (ps == null || string.IsNullOrEmpty(ps.id)) continue;

            var pos = new Vector3(ps.x, ps.y, ps.z);
            var yaw = ps.yaw;

            if (!string.IsNullOrEmpty(selfId) && ps.id == selfId)
            {
                if (correctLocalWithSnapshots)
                {
                    spawner.ApplyAuthoritativePose(ps.id, pos, yaw);
                    localApplied = true;
                }
                continue;
            }

            if (!spawner.TryGet(ps.id, out _))
            {
                spawner.SpawnRemote(ps.id, pos, yaw);
            }

            if (!_buffersByUser.TryGetValue(ps.id, out var samples))
            {
                samples = new List<NetSample>(maxSamplesPerPlayer);
                _buffersByUser[ps.id] = samples;
            }

            samples.Add(new NetSample
            {
                recvTime = now,
                pos = pos,
                yaw = yaw
            });

            while (samples.Count > Mathf.Max(2, maxSamplesPerPlayer))
            {
                samples.RemoveAt(0);
            }

            remotesBuffered++;
        }

        if (enableDebugLogs && Time.unscaledTime >= _nextSnapshotLogAt)
        {
            _nextSnapshotLogAt = Time.unscaledTime + Mathf.Max(0.1f, debugLogInterval);
            Debug.Log($"[SnapshotInterp] RECV_SNAPSHOT players={snap.players.Length} remotesBuffered={remotesBuffered} buffers={_buffersByUser.Count} localApplied={localApplied}");
        }
    }

    private void ComputeTargetPose(List<NetSample> samples, float renderTime, out Vector3 pos, out float yaw)
    {
        var newestIdx = samples.Count - 1;
        var newest = samples[newestIdx];

        if (samples.Count == 1)
        {
            pos = newest.pos;
            yaw = newest.yaw;
            return;
        }

        // Drop stale samples while we have two and render time has moved past the second one.
        while (samples.Count >= 2 && samples[1].recvTime <= renderTime)
        {
            samples.RemoveAt(0);
        }

        if (samples.Count >= 2 && samples[0].recvTime <= renderTime && renderTime <= samples[1].recvTime)
        {
            var a = samples[0];
            var b = samples[1];
            var dt = b.recvTime - a.recvTime;
            var t = dt > 0.0001f ? Mathf.Clamp01((renderTime - a.recvTime) / dt) : 1f;
            pos = Vector3.LerpUnclamped(a.pos, b.pos, t);
            yaw = Mathf.LerpAngle(a.yaw, b.yaw, t);
            return;
        }

        // Render time is newer than newest sample: short capped extrapolation.
        if (samples.Count >= 2)
        {
            var a = samples[samples.Count - 2];
            var b = samples[samples.Count - 1];
            var dt = b.recvTime - a.recvTime;
            if (dt > 0.0001f)
            {
                var vel = (b.pos - a.pos) / dt;
                var ext = Mathf.Clamp(renderTime - b.recvTime, 0f, maxExtrapolationTime);
                pos = b.pos + vel * ext;
                yaw = b.yaw;
                return;
            }
        }

        pos = newest.pos;
        yaw = newest.yaw;
    }

    private void ResolveRefs()
    {
        if (!transport) transport = MatchTransport.Instance != null ? MatchTransport.Instance : FindObjectOfType<MatchTransport>();
        if (!conn) conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        if (!spawner) spawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
    }

    private void EnsureBound()
    {
        if (!transport || _bound) return;
        transport.OnSnapshot += OnSnapshot;
        transport.OnAnim += OnAnim;
        _bound = true;
    }

    private void OnAnim(MatchTransport.AnimMsg msg)
    {
        if (msg == null) return;
        ResolveRefs();
        if (conn == null) return;

        // Clients only trust host animation transitions.
        if (!conn.IsCurrentPlayerMatchCreator &&
            !string.IsNullOrEmpty(conn.MatchCreatorUserId) &&
            msg.senderUserId != conn.MatchCreatorUserId)
        {
            return;
        }

        var actorUserId = !string.IsNullOrEmpty(msg.userId) ? msg.userId : msg.senderUserId;
        if (string.IsNullOrEmpty(actorUserId)) return;
        if (!string.IsNullOrEmpty(conn.SelfUserId) && actorUserId == conn.SelfUserId) return;

        _targetAnimState[actorUserId] = msg.state;
        if (spawner != null && spawner.TryGet(actorUserId, out var go) && go != null)
        {
            ApplyAnimationState(go, actorUserId, msg.state);
        }
    }

    private void ApplyAnimationState(GameObject playerGo, string userId, int currentAnimState)
    {
        var animator = playerGo.GetComponentInChildren<Animator>();
        if (animator == null){ 
            Debug.Log("Oh naayyy, children no animator has.");
        return;

        }

        if (!_lastAnimState.TryGetValue(userId, out var lastState) || lastState != currentAnimState)
        {
            _lastAnimState[userId] = currentAnimState;
        }
        else
        {
            return;
        }

        switch (currentAnimState)
        {
            case ANIM_IDLE:
                animator.SetBool("IsWalking", false);
                animator.SetBool("IsRunning", false);
                break;

            case ANIM_WALK:
                animator.SetBool("IsWalking", true);
                animator.SetBool("IsRunning", false);
                break;

            case ANIM_RUN:
                animator.SetBool("IsWalking", false);
                animator.SetBool("IsRunning", true);
                break;

            case ANIM_JUMP:
                animator.SetTrigger("Jump");
                break;
        }

        if (enableDebugLogs && verboseMotionDebug)
        {
            Debug.Log($"[SnapshotInterp] ANIM user={userId} state={currentAnimState} " +
                      $"(idle=0,walk=1,run=2,jump=3)");
        }
    }
}
