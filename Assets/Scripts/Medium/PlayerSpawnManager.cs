using System.Collections.Generic;
using UnityEngine;

public class PlayerSpawnManager : MonoBehaviour
{
    public struct SpawnedPlayerInfo
    {
        public string userId;
        public GameObject root;
    }

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
            Instance.AbsorbSceneConfig(this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void AbsorbSceneConfig(PlayerSpawnManager other)
    {
        if (other == null) return;

        if (!localPlayerPrefab && other.localPlayerPrefab)
        {
            localPlayerPrefab = other.localPlayerPrefab;
        }
        if (!remoteProxyPrefab && other.remoteProxyPrefab)
        {
            remoteProxyPrefab = other.remoteProxyPrefab;
        }

        // Keep the most permissive correction settings when scene config is available.
        localTeleportThreshold = Mathf.Max(localTeleportThreshold, other.localTeleportThreshold);
        localLerp = Mathf.Max(localLerp, other.localLerp);
        boundsPadding = Mathf.Max(boundsPadding, other.boundsPadding);
    }

    public bool SpawnLocal(string userId, Vector3 pos, float yaw, int modelIndex = -1)
    {
        Debug.Log("[SceneFlow] PlayerSpawnManager.SpawnLocal called user=" + userId + " pos=" + pos);
        if (string.IsNullOrEmpty(userId)) return false;
        if (TryGet(userId, out var existing))
        {
            var existingController = existing != null ? existing.GetComponentInChildren<MediumController>(true) : null;
            var existingCamera = existing != null ? existing.GetComponentInChildren<Camera>(true) : null;
            var looksLocalReady = _localUserId == userId && existingController != null && existingController.enabled && existingCamera != null && existingCamera.enabled;
            if (looksLocalReady)
            {
                Debug.Log("[SceneFlow] SpawnLocal reusing existing local object user=" + userId + " go=" + existing.name);
                spawnedLocal = true;
                _localUserId = userId;
                var minimapExisting = FindObjectOfType<MinimapController>();
                if (minimapExisting != null)
                {
                    minimapExisting.player = existingController.transform;
                }
                return true;
            }

            // Existing object for this user is not a valid local player (often a stale remote/proxy).
            Debug.LogWarning("[SceneFlow] SpawnLocal replacing stale existing object user=" + userId + " go=" + existing.name);
            Despawn(userId);
        }

        if (!localPlayerPrefab)
        {
            Debug.LogError("[Spawn] localPlayerPrefab is not assigned.");
            Debug.LogError("[SceneFlow] SpawnLocal failed: localPlayerPrefab missing.");
            return false;
        }
        var safePos = ClampInsideMapBounds(pos);
        var rot = Quaternion.Euler(0f, yaw, 0f);
        var go = Instantiate(localPlayerPrefab, safePos, rot);
        go.name = "Local_" + ShortId(userId);
        Debug.Log("[SceneFlow] SpawnLocal instantiated go=" + go.name + " at " + safePos);
        var randomizer = go.GetComponent<CharacterModelRandomizer>();
        if (randomizer != null)
        {
            if (modelIndex >= 0 && randomizer.GetModelCount() > 0)
            {
                randomizer.SpawnModelByIndex(modelIndex % randomizer.GetModelCount());
            }
            else
            {
                randomizer.SpawnModelFromUserId(userId); // Fallback deterministic behavior.
            }
        }
        _playersById[userId] = go;
        ConfigureNameTags(go, userId);

        _localUserId = userId;
        spawnedLocal = true;
         
    
    

        var localController = go.GetComponentInChildren<MediumController>(true);
        if (localController) localController.enabled = true;
        else Debug.LogWarning("[SceneFlow] SpawnLocal warning: MediumController not found on " + go.name);

        var cameras = go.GetComponentsInChildren<Camera>(true);
        for (var i = 0; i < cameras.Length; i++)
        {
            cameras[i].enabled = true;
        }
        if (cameras == null || cameras.Length == 0)
        {
            Debug.LogWarning("[SceneFlow] SpawnLocal warning: no Camera found on " + go.name);
        }

        var listeners = go.GetComponentsInChildren<AudioListener>(true);
        for (var i = 0; i < listeners.Length; i++)
        {
            listeners[i].enabled = true;
        }

        var minimap = FindObjectOfType<MinimapController>();
        if (minimap != null)
        {
            minimap.player = localController != null ? localController.transform : go.transform;
        }

        Debug.Log("[SceneFlow] SpawnLocal completed user=" + userId + " localUserId=" + _localUserId + " spawnedLocal=" + spawnedLocal);

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
            var minimap = FindObjectOfType<MinimapController>();
            if (minimap != null && minimap.player != null && (go == null || minimap.player.IsChildOf(go.transform) || minimap.player == go.transform))
            {
                minimap.player = null;
            }
            _localUserId = null;
            spawnedLocal = false;
        }
        return true;
    }

    public bool SpawnRemote(string userId, Vector3 pos, float yaw, int modelIndex = -1)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (TryGet(userId, out _)) return true;

        var safePos = ClampInsideMapBounds(pos);
        GameObject go;
        var rot = Quaternion.Euler(0f, yaw, 0f);
        if (remoteProxyPrefab)
        {
            go = Instantiate(remoteProxyPrefab, safePos, rot);
            var randomizer = go.GetComponent<CharacterModelRandomizer>();
            if (randomizer != null)
            {
                if (modelIndex >= 0 && randomizer.GetModelCount() > 0)
                {
                    randomizer.SpawnModelByIndex(modelIndex % randomizer.GetModelCount());
                }
                else
                {
                    randomizer.SpawnModelFromUserId(userId); // Fallback deterministic behavior.
                }
            }
    
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.position = safePos;
            go.transform.rotation = rot;
        }

        go.name = "Remote_" + ShortId(userId);
        _playersById[userId] = go;
        ConfigureNameTags(go, userId);

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

    public bool RegisterSpawnedObject(string userId, GameObject go, bool isLocal)
    {
        if (string.IsNullOrEmpty(userId) || go == null) return false;

        if (_playersById.TryGetValue(userId, out var existing) && existing != null && existing != go)
        {
            Destroy(existing);
        }

        _playersById[userId] = go;
        ConfigureNameTags(go, userId);

        if (isLocal)
        {
            _localUserId = userId;
            spawnedLocal = true;
        }
        else if (_localUserId == userId)
        {
            _localUserId = null;
            spawnedLocal = false;
        }

        return true;
    }

    private static void ConfigureNameTags(GameObject root, string userId)
    {
        if (root == null || string.IsNullOrEmpty(userId)) return;
        var tags = root.GetComponentsInChildren<PlayerNameTag>(true);
        for (var i = 0; i < tags.Length; i++)
        {
            if (tags[i] == null) continue;
            tags[i].SetUserId(userId);
        }
    }

    public void FillSpawnedPlayers(List<SpawnedPlayerInfo> output)
    {
        if (output == null) return;
        output.Clear();

        foreach (var kv in _playersById)
        {
            if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
            output.Add(new SpawnedPlayerInfo
            {
                userId = kv.Key,
                root = kv.Value
            });
        }
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
