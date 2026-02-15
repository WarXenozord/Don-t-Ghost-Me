using System.Collections.Generic;
using UnityEngine;

public class SnapshotInterpolation : MonoBehaviour
{
    public MatchTransport transport;
    public GameObject playerProxyPrefab;

    [Header("Smoothing")]
    public float lerpPos = 12f;
    public float lerpYaw = 12f;

    private readonly Dictionary<string, GameObject> _proxies = new();
    private readonly Dictionary<string, Vector3> _targetPos = new();
    private readonly Dictionary<string, float> _targetYaw = new();

    void Awake()
    {
        if (!transport) transport = GetComponent<MatchTransport>();
        transport.OnSnapshot += OnSnapshot;
    }

    void Update()
    {
        foreach (var kv in _proxies)
        {
            var id = kv.Key;
            var go = kv.Value;

            if (_targetPos.TryGetValue(id, out var tp))
                go.transform.position = Vector3.Lerp(go.transform.position, tp, Time.deltaTime * lerpPos);

            if (_targetYaw.TryGetValue(id, out var ty))
            {
                var rot = Quaternion.Euler(0f, ty, 0f);
                go.transform.rotation = Quaternion.Slerp(go.transform.rotation, rot, Time.deltaTime * lerpYaw);
            }
        }
    }

    private void OnSnapshot(MatchTransport.SnapshotMsg snap)
    {
        if (snap.players == null) return;

        foreach (var ps in snap.players)
        {
            if (!_proxies.ContainsKey(ps.id))
            {
                var go = Instantiate(playerProxyPrefab);
                go.name = $"PlayerProxy_{ps.id.Substring(0, 6)}";
                _proxies[ps.id] = go;
            }

            _targetPos[ps.id] = new Vector3(ps.x, ps.y, ps.z);
            _targetYaw[ps.id] = ps.yaw;
        }
    }
}