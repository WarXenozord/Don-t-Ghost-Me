using UnityEngine;

/// <summary>
/// Randomly selects and instantiates a character model from a list of prefabs.
/// All models must share the same rig/skeleton.
/// Attach to the root player prefab (the one with Animator/Controller).
/// </summary>
public class CharacterModelRandomizer : MonoBehaviour
{
    [Header("Character Models")]
    [Tooltip("Drag all character model prefabs here. One will be randomly chosen.")]
    public GameObject[] characterModels;

    [Tooltip("If true, uses Resources.LoadAll to find models in Resources/CharacterModels folder")]
    public bool useResourcesFolder = false;

    [Tooltip("Path in Resources folder if useResourcesFolder is true")]
    public string resourcesPath = "CharacterModels";

    [Header("Spawn Settings")]
    [Tooltip("Parent for the spawned model. Leave null to use this transform.")]
    public Transform modelParent;

    [Tooltip("Local position offset for the model")]
    public Vector3 modelOffset = Vector3.zero;

    [Header("Runtime Info (Read-Only)")]
    [SerializeField] private GameObject _spawnedModel;
    [SerializeField] private int _chosenIndex = -1;

    private Animator _rootAnimator;

    void Awake()
    {
        _rootAnimator = GetComponent<Animator>();
        if (_rootAnimator == null)
        {
            Debug.LogWarning($"[CharacterModelRandomizer] No Animator on root GameObject! " +
                           "Animator should be on the root for proper animation.");
        }

        SpawnRandomModel();
    }

    /// <summary>
    /// Spawns a random character model as a child of this GameObject
    /// </summary>
    public void SpawnRandomModel()
    {
        // Clear any existing model
        if (_spawnedModel != null)
        {
            Destroy(_spawnedModel);
            _spawnedModel = null;
        }

        // Load models
        GameObject[] models = GetAvailableModels();
        
        if (models == null || models.Length == 0)
        {
            Debug.LogError("[CharacterModelRandomizer] No character models found! " +
                         "Assign models in inspector or check Resources folder.");
            return;
        }

        // Pick random model
        _chosenIndex = Random.Range(0, models.Length);
        GameObject chosenPrefab = models[_chosenIndex];

        if (chosenPrefab == null)
        {
            Debug.LogError($"[CharacterModelRandomizer] Model at index {_chosenIndex} is null!");
            return;
        }

        // Spawn model
        Transform parent = modelParent != null ? modelParent : transform;
        _spawnedModel = Instantiate(chosenPrefab, parent);
        _spawnedModel.name = $"CharacterModel_{_chosenIndex}";
        _spawnedModel.transform.localPosition = modelOffset;
        _spawnedModel.transform.localRotation = Quaternion.identity;

        // Link animator to model's SkinnedMeshRenderer
        if (_rootAnimator != null)
        {
            var skinnedRenderer = _spawnedModel.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinnedRenderer != null)
            {
                // Verify rig compatibility
                if (skinnedRenderer.rootBone == null)
                {
                    Debug.LogWarning($"[CharacterModelRandomizer] Model '{chosenPrefab.name}' " +
                                   "has no root bone set. Make sure it's rigged properly.");
                }
            }
            else
            {
                Debug.LogWarning($"[CharacterModelRandomizer] Model '{chosenPrefab.name}' " +
                               "has no SkinnedMeshRenderer. Is it a character model?");
            }
        }
        // Get Animator from new skin
           // Animator skinAnimator = _spawnedModel.GetComponent<Animator>();

            // Assign avatar to main animator
            //_rootAnimator.avatar = skinAnimator.avatar;

            // Force rebind
            //_rootAnimator.Rebind();
//_rootAnimator.Update(0f);
        Debug.Log($"[CharacterModelRandomizer] Spawned model {_chosenIndex}: {chosenPrefab.name}");
    }

    /// <summary>
    /// Spawns a specific model by index (for networking - all clients use same model)
    /// </summary>
    public void SpawnModelByIndex(int index)
    {
        if (_spawnedModel != null)
        {
            Destroy(_spawnedModel);
            _spawnedModel = null;
        }

        GameObject[] models = GetAvailableModels();
        
        if (models == null || models.Length == 0)
        {
            Debug.LogError("[CharacterModelRandomizer] No models available!");
            return;
        }

        if (index < 0 || index >= models.Length)
        {
            Debug.LogError($"[CharacterModelRandomizer] Invalid index {index}! " +
                         $"Valid range: 0-{models.Length - 1}");
            return;
        }

        _chosenIndex = index;
        GameObject chosenPrefab = models[index];

        Transform parent = modelParent != null ? modelParent : transform;
        _spawnedModel = Instantiate(chosenPrefab, parent);
        _spawnedModel.name = $"CharacterModel_{index}";
        _spawnedModel.transform.localPosition = modelOffset;
        _spawnedModel.transform.localRotation = Quaternion.identity;

        Debug.Log($"[CharacterModelRandomizer] Spawned model {index}: {chosenPrefab.name}");
    }

    /// <summary>
    /// Gets the index of the currently spawned model (for network sync)
    /// </summary>
    public int GetChosenModelIndex()
    {
        return _chosenIndex;
    }

    /// <summary>
    /// Gets total number of available models (for network sync)
    /// </summary>
    public int GetModelCount()
    {
        GameObject[] models = GetAvailableModels();
        return models != null ? models.Length : 0;
    }

    /// <summary>
    /// Spawns a model deterministically based on userId hash.
    /// All clients will compute the same model index for the same userId.
    /// Perfect for network sync without sending extra data!
    /// </summary>
    public void SpawnModelFromUserId(string userId)
    {
        GameObject[] models = GetAvailableModels();
        if (models == null || models.Length == 0)
        {
            Debug.LogError("[CharacterModelRandomizer] No models available!");
            return;
        }

        // Hash userId to get consistent index across all clients
        int hash = string.IsNullOrEmpty(userId) ? 0 : userId.GetHashCode();
        int index = Mathf.Abs(hash) % models.Length;

        SpawnModelByIndex(index);
        Debug.Log($"[CharacterModelRandomizer] User {userId} ? Model {index} (deterministic)");
    }

    private GameObject[] GetAvailableModels()
    {
        if (useResourcesFolder)
        {
            // Load from Resources folder
            GameObject[] loaded = Resources.LoadAll<GameObject>(resourcesPath);
            if (loaded.Length == 0)
            {
                Debug.LogWarning($"[CharacterModelRandomizer] No models found in Resources/{resourcesPath}");
            }
            return loaded;
        }
        else
        {
            // Use inspector-assigned array
            return characterModels;
        }
    }

    /// <summary>
    /// Force respawn with a new random model (useful for respawn/character selection)
    /// </summary>
    [ContextMenu("Respawn Random Model")]
    public void RespawnRandomModel()
    {
        SpawnRandomModel();
    }

    /// <summary>
    /// Cycle through models for testing
    /// </summary>
    [ContextMenu("Next Model (Test)")]
    public void TestNextModel()
    {
        GameObject[] models = GetAvailableModels();
        if (models == null || models.Length == 0) return;

        int nextIndex = (_chosenIndex + 1) % models.Length;
        SpawnModelByIndex(nextIndex);
    }

    private void OnDrawGizmosSelected()
    {
        // Show model spawn position
        Transform parent = modelParent != null ? modelParent : transform;
        Vector3 spawnPos = parent.position + parent.TransformDirection(modelOffset);
        
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(spawnPos, 0.2f);
        Gizmos.DrawLine(parent.position, spawnPos);
    }
}