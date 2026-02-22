using System.Collections.Generic;
using UnityEngine;

public class GhostSpawner : MonoBehaviour
{
    public static GhostSpawner Instance { get; private set; }

    [Header("Refs")]
    public NakamaConnection conn;
    public MatchTransport transport;
    public PlayerSpawnManager playerSpawner;
    public PlayerDeathTracker deathTracker;

    [Header("Prefabs")]
    public GameObject localGhostPrefab;
    public GameObject remoteGhostPrefab;
    [Header("Global Ghost FX")]
    public AudioSource globalGhostFxSource;
    public AudioClip lowEnergyKillClip;

    [Header("Debug")]
    public bool enableDebugLogs = true;

    private readonly Dictionary<string, GameObject> _ghostsByUserId = new Dictionary<string, GameObject>();
    private bool _bound;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolveRefs();
        EnsureBound();
    }

    void Update()
    {
        ResolveRefs();
        EnsureBound();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (transport != null && _bound)
        {
            transport.OnGhostSpawn -= OnGhostSpawnReceived;
            transport.OnGhostKillRequest -= OnGhostKillRequestReceived;
            transport.OnGhostFxRequest -= OnGhostFxRequestReceived;
            transport.OnGhostFx -= OnGhostFxReceived;
            _bound = false;
        }
    }

    public bool RequestKillMediumAndSpawnGhost(string userId, Vector3 position, float yaw)
    {
        ResolveRefs();
        if (string.IsNullOrEmpty(userId)) return false;
        if (conn == null || transport == null || conn.Match == null) return false;

        if (conn.IsCurrentPlayerMatchCreator)
        {
            return HostKillMediumAndSpawnGhost(userId, position, yaw);
        }

        transport.SendGhostKillRequest(new MatchTransport.GhostKillRequestMsg
        {
            userId = userId,
            x = position.x,
            y = position.y,
            z = position.z,
            yaw = yaw
        });
        return true;
    }

    public void RequestLowEnergyKillFx(Vector3 worldPos)
    {
        ResolveRefs();
        if (transport == null || conn == null || conn.Match == null) return;

        var msg = new MatchTransport.GhostFxMsg
        {
            fxId = 1,
            x = worldPos.x,
            y = worldPos.y,
            z = worldPos.z
        };

        if (conn.IsCurrentPlayerMatchCreator)
        {
            ApplyGhostFx(msg);
            transport.BroadcastGhostFx(msg);
        }
        else
        {
            transport.SendGhostFxRequest(msg);
        }
    }

    public bool HostKillMediumAndSpawnGhost(string userId, Vector3 position, float yaw)
    {
        ResolveRefs();
        if (conn == null || transport == null || conn.Match == null) return false;
        if (!conn.IsCurrentPlayerMatchCreator) return false;
        if (string.IsNullOrEmpty(userId)) return false;

        var msg = new MatchTransport.GhostSpawnMsg
        {
            userId = userId,
            x = position.x,
            y = position.y,
            z = position.z,
            yaw = yaw
        };

        ApplyGhostSpawn(msg);
        if (deathTracker != null)
        {
            deathTracker.RegisterPlayerDead(userId);
        }
        transport.BroadcastGhostSpawn(msg);
        return true;
    }

    public void ClearAll()
    {
        foreach (var kv in _ghostsByUserId)
        {
            if (kv.Value) Destroy(kv.Value);
        }
        _ghostsByUserId.Clear();
    }

    private void OnGhostSpawnReceived(MatchTransport.GhostSpawnMsg msg)
    {
        if (msg == null || string.IsNullOrEmpty(msg.userId)) return;
        if (conn != null && !string.IsNullOrEmpty(msg.senderUserId) && msg.senderUserId == conn.SelfUserId) return;
        ApplyGhostSpawn(msg);
    }

    private void OnGhostKillRequestReceived(MatchTransport.GhostKillRequestMsg msg)
    {
        if (msg == null || string.IsNullOrEmpty(msg.userId)) return;
        if (conn == null || !conn.IsCurrentPlayerMatchCreator) return;

        HostKillMediumAndSpawnGhost(msg.userId, new Vector3(msg.x, msg.y, msg.z), msg.yaw);
    }

    private void OnGhostFxRequestReceived(MatchTransport.GhostFxMsg msg)
    {
        if (msg == null) return;
        if (conn == null || !conn.IsCurrentPlayerMatchCreator) return;

        ApplyGhostFx(msg);
        transport.BroadcastGhostFx(msg);
    }

    private void OnGhostFxReceived(MatchTransport.GhostFxMsg msg)
    {
        if (msg == null) return;
        if (conn != null && !string.IsNullOrEmpty(msg.senderUserId) && msg.senderUserId == conn.SelfUserId) return;
        ApplyGhostFx(msg);
    }

    private void ApplyGhostSpawn(MatchTransport.GhostSpawnMsg msg)
    {
        if (msg == null || string.IsNullOrEmpty(msg.userId)) return;

        if (!playerSpawner) playerSpawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        if (playerSpawner != null)
        {
            playerSpawner.Despawn(msg.userId);
        }

        if (_ghostsByUserId.TryGetValue(msg.userId, out var existing))
        {
            if (existing) Destroy(existing);
            _ghostsByUserId.Remove(msg.userId);
        }

        var isLocalGhost = conn != null && msg.userId == conn.SelfUserId;
        var prefab = isLocalGhost ? localGhostPrefab : remoteGhostPrefab;
        if (!prefab) prefab = localGhostPrefab ? localGhostPrefab : remoteGhostPrefab;
        if (!prefab)
        {
            if (enableDebugLogs) Debug.LogWarning("[GhostSpawner] Missing ghost prefab assignment.");
            return;
        }

        var pos = new Vector3(msg.x, msg.y, msg.z);
        var go = Instantiate(prefab, pos, Quaternion.Euler(0f, msg.yaw, 0f));
        go.name = "Ghost_" + ShortId(msg.userId);
        _ghostsByUserId[msg.userId] = go;

        if (playerSpawner != null)
        {
            playerSpawner.RegisterSpawnedObject(msg.userId, go, isLocalGhost);
        }

        var ghost = go.GetComponentInChildren<GhostController>(true);
        if (ghost) ghost.enabled = isLocalGhost;

        var interactions = go.GetComponentsInChildren<GhostInteraction>(true);
        for (var i = 0; i < interactions.Length; i++)
        {
            interactions[i].enabled = isLocalGhost;
        }

        var energies = go.GetComponentsInChildren<GhostEnergy>(true);
        for (var i = 0; i < energies.Length; i++)
        {
            energies[i].enabled = isLocalGhost;
        }

        var cameras = go.GetComponentsInChildren<Camera>(true);
        for (var i = 0; i < cameras.Length; i++)
        {
            cameras[i].enabled = isLocalGhost;
        }

        var listeners = go.GetComponentsInChildren<AudioListener>(true);
        for (var i = 0; i < listeners.Length; i++)
        {
            listeners[i].enabled = isLocalGhost;
        }

        // Medium -> ghost transition: keep minimap following the new local body.
        if (isLocalGhost)
        {
            var minimap = FindObjectOfType<MinimapController>();
            if (minimap != null)
            {
                minimap.player = ghost != null ? ghost.transform : go.transform;
            }
        }

        if (enableDebugLogs)
        {
            Debug.Log("[GhostSpawner] SPAWN_GHOST user=" + msg.userId + " local=" + isLocalGhost);
        }
    }

    private void ResolveRefs()
    {
        if (!conn) conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        if (!transport) transport = MatchTransport.Instance != null ? MatchTransport.Instance : FindObjectOfType<MatchTransport>();
        if (!playerSpawner) playerSpawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        if (!deathTracker) deathTracker = PlayerDeathTracker.Instance != null ? PlayerDeathTracker.Instance : FindObjectOfType<PlayerDeathTracker>();
        if (!globalGhostFxSource) globalGhostFxSource = GetComponent<AudioSource>();
    }

    private void EnsureBound()
    {
        if (!transport || _bound) return;
        transport.OnGhostSpawn += OnGhostSpawnReceived;
        transport.OnGhostKillRequest += OnGhostKillRequestReceived;
        transport.OnGhostFxRequest += OnGhostFxRequestReceived;
        transport.OnGhostFx += OnGhostFxReceived;
        _bound = true;
    }

    private void ApplyGhostFx(MatchTransport.GhostFxMsg msg)
    {
        AudioClip clip = null;
        if (msg.fxId == 1) clip = lowEnergyKillClip;
        if (clip == null) return;

        var pos = new Vector3(msg.x, msg.y, msg.z);
        if (globalGhostFxSource != null)
        {
            globalGhostFxSource.transform.position = pos;
            globalGhostFxSource.PlayOneShot(clip);
            return;
        }

        AudioSource.PlayClipAtPoint(clip, pos, 1f);
    }

    private static string ShortId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "------";
        return id.Length <= 6 ? id : id.Substring(0, 6);
    }
}
