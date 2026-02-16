using System.Collections.Generic;
using UnityEngine;

public class PlayerSpawnManager : MonoBehaviour
{
    public static PlayerSpawnManager Instance { get; private set; }

    [Header("Prefabs")]
    public GameObject localPlayerPrefab;
    public GameObject remoteProxyPrefab;

    [Header("Local Correction")]
    public float localTeleportThreshold = 3f;
    public float localLerp = 6f;
    public float localYawSnapThreshold = 45f;
    public float boundsPadding = 0.35f;
    public bool clampSnapshotPoses = false;

    public bool spawnedLocal { get; private set; }

    private string _localUserId;
    private readonly Dictionary<string, GameObject> _playersById = new Dictionary<string, GameObject>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public bool SpawnLocal(string userId, Vector3 pos, float yaw)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (TryGet(userId, out _))
        {
            spawnedLocal = true;
            _localUserId = userId;
            return true;
        }

        if (!localPlayerPrefab)
        {
            Debug.LogError("[Spawn] localPlayerPrefab is not assigned.");
            return false;
        }

        var safePos = ClampInsideMapBounds(pos);
        var rot = Quaternion.Euler(0f, yaw, 0f);
        var go = Instantiate(localPlayerPrefab, safePos, rot);
        go.name = "Local_" + ShortId(userId);
        _playersById[userId] = go;
        _localUserId = userId;
        spawnedLocal = true;

        var localController = go.GetComponentInChildren<MediumController>(true);
        if (localController) localController.enabled = true;

        var cameras = go.GetComponentsInChildren<Camera>(true);
        for (var i = 0; i < cameras.Length; i++)
        {
            cameras[i].enabled = true;
        }

        var listeners = go.GetComponentsInChildren<AudioListener>(true);
        for (var i = 0; i < listeners.Length; i++)
        {
            listeners[i].enabled = true;
        }

        return true;
    }

    public void ClearAll()
    {
        foreach (var kv in _playersById)
        {
            if (kv.Value) Destroy(kv.Value);
        }
        _playersById.Clear();
        _localUserId = null;
        spawnedLocal = false;
    }

    public bool Despawn(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (!_playersById.TryGetValue(userId, out var go)) return false;
        _playersById.Remove(userId);
        if (go) Destroy(go);
        if (_localUserId == userId)
        {
            _localUserId = null;
            spawnedLocal = false;
        }
        return true;
    }

    public bool SpawnRemote(string userId, Vector3 pos, float yaw)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (TryGet(userId, out _)) return true;

        var safePos = ClampInsideMapBounds(pos);
        GameObject go;
        var rot = Quaternion.Euler(0f, yaw, 0f);
        if (remoteProxyPrefab)
        {
            go = Instantiate(remoteProxyPrefab, safePos, rot);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.position = safePos;
            go.transform.rotation = rot;
        }

        go.name = "Remote_" + ShortId(userId);
        _playersById[userId] = go;

        var controller = go.GetComponentInChildren<MediumController>(true);
        if (controller) controller.enabled = false;

        var cameras = go.GetComponentsInChildren<Camera>(true);
        for (var i = 0; i < cameras.Length; i++)
        {
            cameras[i].enabled = false;
        }

        var listeners = go.GetComponentsInChildren<AudioListener>(true);
        for (var i = 0; i < listeners.Length; i++)
        {
            listeners[i].enabled = false;
        }

        return true;
    }

    public bool TryGet(string userId, out GameObject go)
    {
        go = null;
        if (string.IsNullOrEmpty(userId)) return false;
        if (!_playersById.TryGetValue(userId, out var found)) return false;
        if (found == null)
        {
            _playersById.Remove(userId);
            return false;
        }

        go = found;
        return true;
    }

    public bool TryGetUserIdByObject(GameObject candidate, out string userId)
    {
        userId = null;
        if (candidate == null) return false;

        foreach (var kv in _playersById)
        {
            var root = kv.Value;
            if (root == null) continue;
            if (candidate == root || candidate.transform.IsChildOf(root.transform))
            {
                userId = kv.Key;
                return true;
            }
        }

        return false;
    }

    public void ApplyAuthoritativePose(string userId, Vector3 pos, float yaw)
    {
        if (!TryGet(userId, out var go)) return;
        var targetPos = clampSnapshotPoses ? ClampInsideMapBounds(pos) : pos;

        if (userId == _localUserId)
        {
            var dist = Vector3.Distance(go.transform.position, targetPos);
            if (dist > localTeleportThreshold)
            {
                go.transform.position = targetPos;
            }
            else
            {
                go.transform.position = Vector3.Lerp(go.transform.position, targetPos, Time.deltaTime * localLerp);
            }
            return;
        }

        go.transform.position = targetPos;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    public Vector3 ClampInsideMapBounds(Vector3 pos)
    {
        var generator = FindObjectOfType<ProceduralBuildingGenerator>();
        if (generator && generator.TryGetSafeSpawnPoint(pos, out var roomSafePos, preferredFloor: 0, margin: boundsPadding))
        {
            return roomSafePos;
        }

        if (!TryGetMapBounds(out var bounds)) return pos;

        var minX = bounds.min.x + boundsPadding;
        var maxX = bounds.max.x - boundsPadding;
        var minZ = bounds.min.z + boundsPadding;
        var maxZ = bounds.max.z - boundsPadding;

        if (minX > maxX) { minX = bounds.min.x; maxX = bounds.max.x; }
        if (minZ > maxZ) { minZ = bounds.min.z; maxZ = bounds.max.z; }

        var clamped = pos;
        clamped.x = Mathf.Clamp(clamped.x, minX, maxX);
        clamped.z = Mathf.Clamp(clamped.z, minZ, maxZ);
        clamped.y = Mathf.Max(clamped.y, bounds.min.y + 0.25f);

        return clamped;
    }

    private bool TryGetMapBounds(out Bounds bounds)
    {
        var generator = FindObjectOfType<ProceduralBuildingGenerator>();
        if (!generator)
        {
            bounds = default;
            return false;
        }

        var renderers = generator.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (!r || !r.enabled) continue;
            bounds = r.bounds;

            for (var j = i + 1; j < renderers.Length; j++)
            {
                var rr = renderers[j];
                if (!rr || !rr.enabled) continue;
                bounds.Encapsulate(rr.bounds);
            }
            return true;
        }

        var colliders = generator.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            var c = colliders[i];
            if (!c || !c.enabled) continue;
            bounds = c.bounds;

            for (var j = i + 1; j < colliders.Length; j++)
            {
                var cc = colliders[j];
                if (!cc || !cc.enabled) continue;
                bounds.Encapsulate(cc.bounds);
            }
            return true;
        }

        bounds = default;
        return false;
    }

    private static string ShortId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "------";
        return id.Length <= 6 ? id : id.Substring(0, 6);
    }
}
