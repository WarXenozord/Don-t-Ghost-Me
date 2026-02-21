using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// Handles transitioning between floors - advancing floor number,
/// reloading scene, and regenerating building with increased difficulty.
/// </summary>
public class FloorTransitionManager : MonoBehaviour
{
    public static FloorTransitionManager Instance { get; private set; }

    [Header("Scene")]
    [Tooltip("Name of the gameplay scene to reload")]
    public string gameplayScene = "Game";

    [Header("Transition")]
    [Tooltip("Delay before starting transition")]
    public float transitionDelay = 2f;

    [Tooltip("Show loading screen during transition")]
    public GameObject loadingScreen;

    [Header("Debug")]
    public bool enableDebugLogs = true;

    private FloorProgressionManager _progressionManager;
    private HostAuthority _hostAuthority;
    private NakamaConnection _conn;
    private bool _transitionInProgress = false;

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

    void Start()
    {
        ResolveRefs();

        // Hide loading screen
        if (loadingScreen != null)
        {
            loadingScreen.SetActive(false);
        }
    }

    /// <summary>
    /// Triggers transition to next floor (called when ritual completes)
    /// </summary>
    public void TriggerFloorTransition()
    {
        if (_transitionInProgress)
        {
            Debug.LogWarning("[FloorTransition] Transition already in progress");
            return;
        }

        // Only host can trigger floor transition
        if (_conn != null && !_conn.IsCurrentPlayerMatchCreator)
        {
            if (enableDebugLogs)
                Debug.Log("[FloorTransition] Non-host cannot trigger transition");
            return;
        }

        StartCoroutine(TransitionToNextFloor());
    }

    private IEnumerator TransitionToNextFloor()
    {
        _transitionInProgress = true;

        if (_progressionManager == null)
            _progressionManager = FloorProgressionManager.Instance;

        if (enableDebugLogs)
        {
            Debug.Log($"[FloorTransition] Starting transition from floor {_progressionManager.CurrentFloor}...");
        }

        // Show loading screen
        if (loadingScreen != null)
        {
            loadingScreen.SetActive(true);
        }

        // Wait for transition delay
        yield return new WaitForSeconds(transitionDelay);

        // Advance to next floor
        if (_progressionManager != null)
        {
            _progressionManager.AdvanceToNextFloor();
        }

        // Store match context before scene reload
        StoreMatchContext();

        // Reload scene
        if (enableDebugLogs)
        {
            Debug.Log($"[FloorTransition] Reloading scene: {gameplayScene}");
        }

        SceneManager.LoadScene(gameplayScene);

        _transitionInProgress = false;
    }

    /// <summary>
    /// Stores current match state so it persists across scene reload
    /// </summary>
    private void StoreMatchContext()
    {
        // The MatchContext singleton already persists across scenes (DontDestroyOnLoad)
        // Just ensure it's populated
        var context = MatchContext.Instance;
        
        if (_hostAuthority != null && context.hasInit)
        {
            // Match state is already stored in MatchContext
            if (enableDebugLogs)
            {
                Debug.Log("[FloorTransition] Match context preserved across scene reload");
            }
        }
    }

    /// <summary>
    /// Called after scene loads to regenerate building with new difficulty
    /// </summary>
    public void RegenerateFloorWithNewDifficulty()
    {
        if (_progressionManager == null)
            _progressionManager = FloorProgressionManager.Instance;

        var generator = FindObjectOfType<ProceduralBuildingGenerator>();
        if (generator == null)
        {
            Debug.LogError("[FloorTransition] ProceduralBuildingGenerator not found!");
            return;
        }

        // Get difficulty parameters from progression manager
        int roomCount = _progressionManager.CurrentRoomCount;
        int enemyCount = _progressionManager.CurrentEnemyCount;

        if (enableDebugLogs)
        {
            Debug.Log($"[FloorTransition] Regenerating floor {_progressionManager.CurrentFloor} " +
                      $"with {roomCount} rooms and {enemyCount} enemies");
        }

        // Set room count on generator
        // Note: You may need to add a public setter for this on ProceduralBuildingGenerator
        SetGeneratorRoomCount(generator, roomCount);

        // Regenerate building (it will use the seed from MatchContext)
        var context = MatchContext.Instance;
        if (context.hasInit && context.lastInit != null)
        {
            // Use seed + floor number for variation
            int floorSeed = context.lastInit.seed + _progressionManager.CurrentFloor;
            generator.GenerateBuildingFromSeed(floorSeed);
        }
        else
        {
            generator.GenerateBuilding();
        }

        // Update enemy spawn count
        UpdateEnemySpawnCount(enemyCount);
    }

    /// <summary>
    /// Sets the room count on the building generator
    /// </summary>
    private void SetGeneratorRoomCount(ProceduralBuildingGenerator generator, int roomCount)
    {
        // This assumes ProceduralBuildingGenerator has these fields
        // You may need to adjust based on your actual implementation
        
        // Option 1: If generator has public fields
        // generator.minRooms = roomCount;
        // generator.maxRooms = roomCount;
        
        // Option 2: If generator reads from a config
        // Create a runtime override system
        
        // For now, we'll assume it has a method or we can use reflection
        var minRoomsField = generator.GetType().GetField("minRooms", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var maxRoomsField = generator.GetType().GetField("maxRooms", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        if (minRoomsField != null)
            minRoomsField.SetValue(generator, roomCount);
        
        if (maxRoomsField != null)
            maxRoomsField.SetValue(generator, roomCount);

        if (enableDebugLogs)
        {
            Debug.Log($"[FloorTransition] Set generator to {roomCount} rooms");
        }
    }

    /// <summary>
    /// Updates the enemy spawn count for the host
    /// </summary>
    private void UpdateEnemySpawnCount(int enemyCount)
    {
        if (_hostAuthority == null)
            _hostAuthority = FindObjectOfType<HostAuthority>();

        if (_hostAuthority != null)
        {
            // Set the enemy count
            var startEnemyCountField = _hostAuthority.GetType().GetField("startEnemyCount", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            if (startEnemyCountField != null)
            {
                startEnemyCountField.SetValue(_hostAuthority, enemyCount);
                
                if (enableDebugLogs)
                {
                    Debug.Log($"[FloorTransition] Set enemy count to {enemyCount}");
                }
            }
        }
    }

    private void ResolveRefs()
    {
        if (_progressionManager == null)
            _progressionManager = FloorProgressionManager.Instance != null ? FloorProgressionManager.Instance : FindObjectOfType<FloorProgressionManager>();
        
        if (_hostAuthority == null)
            _hostAuthority = FindObjectOfType<HostAuthority>();
        
        if (_conn == null)
            _conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
    }

    /// <summary>
    /// Called from GameBootstrap after scene loads to regenerate floor
    /// </summary>
    public void OnSceneLoadedAfterTransition()
    {
        // Hide loading screen
        if (loadingScreen != null)
        {
            loadingScreen.SetActive(false);
        }

        // Check if we're in a run
        if (_progressionManager != null && _progressionManager.RunActive)
        {
            RegenerateFloorWithNewDifficulty();
        }
    }
}