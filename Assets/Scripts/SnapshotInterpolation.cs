using System.Collections.Generic;
using UnityEngine;

public class SnapshotInterpolation : MonoBehaviour
{
    public MatchTransport transport;
    public NakamaConnection conn;
    public PlayerSpawnManager spawner;

    [Header("Smoothing")]
    public float lerpPos = 12f;
    public float lerpYaw = 12f;
    public float smoothTimePos = 0.06f;
    public float snapDistance = 6f;
    public float maxExtrapolationTime = 0.12f;
    public float maxEstimatedSpeed = 12f;
    public bool correctLocalWithSnapshots = false;
    public bool enableDebugLogs = true;
    public float debugLogInterval = 1f;
    public bool verboseMotionDebug = true;

    private readonly Dictionary<string, Vector3> _targetPos = new Dictionary<string, Vector3>();
    private readonly Dictionary<string, float> _targetYaw = new Dictionary<string, float>();
    private readonly Dictionary<string, Vector3> _lastSnapPos = new Dictionary<string, Vector3>();
    private readonly Dictionary<string, float> _lastSnapAt = new Dictionary<string, float>();
    private readonly Dictionary<string, Vector3> _estimatedVel = new Dictionary<string, Vector3>();
    private readonly Dictionary<string, Vector3> _smoothVel = new Dictionary<string, Vector3>();
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
            _bound = false;
        }
    }

    void Update()
    {
        ResolveRefs();
        EnsureBound();

        foreach (var kv in _targetPos)
        {
            var userId = kv.Key;
            if (!spawner || !spawner.TryGet(userId, out var go)) continue;

            var targetPos = kv.Value;
            if (_lastSnapAt.TryGetValue(userId, out var lastAt) && _estimatedVel.TryGetValue(userId, out var estVel))
            {
                var sinceSnap = Mathf.Clamp(Time.unscaledTime - lastAt, 0f, maxExtrapolationTime);
                targetPos += estVel * sinceSnap;
            }

            var current = go.transform.position;
            var dist = Vector3.Distance(current, targetPos);
            if (dist > snapDistance)
            {
                go.transform.position = targetPos;
                _smoothVel[userId] = Vector3.zero;
            }
            else
            {
                var velRef = _smoothVel.TryGetValue(userId, out var v) ? v : Vector3.zero;
                go.transform.position = Vector3.SmoothDamp(current, targetPos, ref velRef, smoothTimePos, Mathf.Infinity, Time.deltaTime);
                _smoothVel[userId] = velRef;
            }

            if (_targetYaw.TryGetValue(userId, out var targetYaw))
            {
                var rot = Quaternion.Euler(0f, targetYaw, 0f);
                go.transform.rotation = Quaternion.Slerp(go.transform.rotation, rot, Time.deltaTime * lerpYaw);
            }
        }
    }

    private void OnSnapshot(MatchTransport.SnapshotMsg snap)
    {
        if (snap == null || snap.players == null) return;
        ResolveRefs();
        if (!spawner || conn == null) return;

        var selfId = conn.SelfUserId;
        var appliedRemotes = 0;
        var missingRefs = 0;
        string sampleId = null;
        Vector3 sampleSnapPos = Vector3.zero;
        Vector3 sampleBefore = Vector3.zero;
        Vector3 sampleAfter = Vector3.zero;
        var capturedSample = false;
        for (var i = 0; i < snap.players.Length; i++)
        {
            var ps = snap.players[i];
            if (ps == null || string.IsNullOrEmpty(ps.id)) continue;

            var pos = new Vector3(ps.x, ps.y, ps.z);
            var yaw = ps.yaw;

            if (!string.IsNullOrEmpty(selfId) && ps.id == selfId)
            {
                // Optional: local correction can fight collision and feel like teleports through walls.
                if (correctLocalWithSnapshots)
                {
                    spawner.ApplyAuthoritativePose(ps.id, pos, yaw);
                }
                continue;
            }

            if (!spawner.TryGet(ps.id, out _))
            {
                spawner.SpawnRemote(ps.id, pos, yaw);
            }
            if (!spawner.TryGet(ps.id, out var remoteGo))
            {
                missingRefs++;
                continue;
            }

            if (!capturedSample)
            {
                capturedSample = true;
                sampleId = ps.id;
                sampleSnapPos = pos;
                sampleBefore = remoteGo.transform.position;
            }

            if (_lastSnapPos.TryGetValue(ps.id, out var prevPos) && _lastSnapAt.TryGetValue(ps.id, out var prevAt))
            {
                var dt = Time.unscaledTime - prevAt;
                if (dt > 0.0001f)
                {
                    var vel = (pos - prevPos) / dt;
                    if (vel.sqrMagnitude > maxEstimatedSpeed * maxEstimatedSpeed)
                    {
                        vel = vel.normalized * maxEstimatedSpeed;
                    }
                    _estimatedVel[ps.id] = vel;
                }
            }
            _lastSnapPos[ps.id] = pos;
            _lastSnapAt[ps.id] = Time.unscaledTime;

            if (capturedSample && sampleId == ps.id)
            {
                sampleAfter = remoteGo.transform.position;
            }
            _targetPos[ps.id] = pos;
            _targetYaw[ps.id] = yaw;
            appliedRemotes++;
        }

        if (enableDebugLogs && Time.unscaledTime >= _nextSnapshotLogAt)
        {
            _nextSnapshotLogAt = Time.unscaledTime + Mathf.Max(0.1f, debugLogInterval);
            Debug.Log($"[SnapshotInterp] RECV_SNAPSHOT players={snap.players.Length} remotesApplied={appliedRemotes} missingRefs={missingRefs} targets={_targetPos.Count} self={selfId}");
        }

        if (enableDebugLogs && verboseMotionDebug && capturedSample && Time.unscaledTime >= _nextMotionLogAt)
        {
            _nextMotionLogAt = Time.unscaledTime + Mathf.Max(0.1f, debugLogInterval);
            var beforeDist = Vector3.Distance(sampleBefore, sampleSnapPos);
            var afterDist = Vector3.Distance(sampleAfter, sampleSnapPos);
            Debug.Log(
                $"[SnapshotInterp] APPLY_SAMPLE user={sampleId} snap=({sampleSnapPos.x:F2},{sampleSnapPos.y:F2},{sampleSnapPos.z:F2}) " +
                $"before=({sampleBefore.x:F2},{sampleBefore.y:F2},{sampleBefore.z:F2}) after=({sampleAfter.x:F2},{sampleAfter.y:F2},{sampleAfter.z:F2}) " +
                $"errBefore={beforeDist:F2} errAfter={afterDist:F2}"
            );
        }
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
        _bound = true;
    }
}
