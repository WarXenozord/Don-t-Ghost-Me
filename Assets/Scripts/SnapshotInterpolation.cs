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
    public bool correctLocalWithSnapshots = false;

    private readonly Dictionary<string, Vector3> _targetPos = new Dictionary<string, Vector3>();
    private readonly Dictionary<string, float> _targetYaw = new Dictionary<string, float>();

    void Awake()
    {
        if (!transport) transport = FindObjectOfType<MatchTransport>();
        if (!conn) conn = FindObjectOfType<NakamaConnection>();
        if (!spawner) spawner = FindObjectOfType<PlayerSpawnManager>();

        if (transport) transport.OnSnapshot += OnSnapshot;
    }

    void OnDestroy()
    {
        if (transport) transport.OnSnapshot -= OnSnapshot;
    }

    void Update()
    {
        foreach (var kv in _targetPos)
        {
            var userId = kv.Key;
            if (!spawner || !spawner.TryGet(userId, out var go)) continue;

            var targetPos = kv.Value;
            go.transform.position = Vector3.Lerp(go.transform.position, targetPos, Time.deltaTime * lerpPos);

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
        if (!spawner) spawner = FindObjectOfType<PlayerSpawnManager>();
        if (!conn) conn = FindObjectOfType<NakamaConnection>();
        if (!spawner || conn == null) return;

        var selfId = conn.SelfUserId;
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

            _targetPos[ps.id] = pos;
            _targetYaw[ps.id] = yaw;
        }
    }
}
