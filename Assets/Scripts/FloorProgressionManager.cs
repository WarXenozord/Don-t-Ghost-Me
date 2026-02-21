
using UnityEngine;
using System.Collections;
/// <summary>
/// Manages floor progression, difficulty scaling, and highscore tracking.
/// Each completed floor increases room count and enemy count.
/// Endless progression until all players die.
/// </summary>
public class FloorProgressionManager : MonoBehaviour
{
    public static FloorProgressionManager Instance { get; private set; }

    [Header("Base Difficulty")]
    [Tooltip("Starting number of rooms on floor 1")]
    public int baseRoomCount = 8;

    [Tooltip("Starting number of enemies on floor 1")]
    public int baseEnemyCount = 1;

    [Header("Progression")]
    [Tooltip("Rooms added per floor")]
    public int roomsPerFloor = 5;

    [Tooltip("Enemies added per floor")]
    public int enemiesPerFloor = 1;

    [Header("Current Run")]
    [SerializeField] private int _currentFloor = 1;
    [SerializeField] private int _currentRoomCount;
    [SerializeField] private int _currentEnemyCount;
    [SerializeField] private bool _runActive = false;

    [Header("Highscore")]
    [SerializeField] private int _highestFloor = 0;
    [SerializeField] private int _highestRoomCount = 0;

    private const string PREF_HIGHEST_FLOOR = "highscore_highest_floor";
    private const string PREF_HIGHEST_ROOMS = "highscore_highest_rooms";

    public int CurrentFloor => _currentFloor;
    public int CurrentRoomCount => _currentRoomCount;
    public int CurrentEnemyCount => _currentEnemyCount;
    public int HighestFloor => _highestFloor;
    public int HighestRoomCount => _highestRoomCount;
    public bool RunActive => _runActive;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadHighscore();
    }

    /// <summary>
    /// Starts a new run at floor 1
    /// </summary>
    public void StartNewRun()
    {
        _currentFloor = 1;
        _currentRoomCount = CalculateRoomCount(1);
        _currentEnemyCount = CalculateEnemyCount(1);
        _runActive = true;

        Debug.Log($"[FloorProgression] Started new run - Floor 1, {_currentRoomCount} rooms, {_currentEnemyCount} enemies");
    }

    /// <summary>
    /// Advances to the next floor
    /// </summary>
    public void AdvanceToNextFloor()
    {
        Debug.Log("[FloorProgression] Attempting to advance from floor ...");   
        if (!_runActive)
        {
            Debug.LogWarning("[FloorProgression] Cannot advance - run not active");
            return;
        }

        _currentFloor++;
        _currentRoomCount = CalculateRoomCount(_currentFloor);
        _currentEnemyCount = CalculateEnemyCount(_currentFloor);

        Debug.Log($"[FloorProgression] Advanced to floor {_currentFloor} - {_currentRoomCount} rooms, {_currentEnemyCount} enemies");

        // Check if new highscore
        if (_currentFloor > _highestFloor)
        {
            _highestFloor = _currentFloor;
            _highestRoomCount = _currentRoomCount;
            SaveHighscore();
            Debug.Log($"[FloorProgression] NEW HIGHSCORE! Floor {_highestFloor}");
        }
    }

    /// <summary>
    /// Ends the current run (all players dead)
    /// </summary>
    public void EndRun()
    {
        if (!_runActive) return;

        Debug.Log($"[FloorProgression] Run ended - Reached floor {_currentFloor}");

        // Check highscore one last time
        if (_currentFloor > _highestFloor)
        {
            _highestFloor = _currentFloor;
            _highestRoomCount = _currentRoomCount;
            SaveHighscore();
        }

        _runActive = false;
    }

    /// <summary>
    /// Calculates room count for a given floor
    /// </summary>
    public int CalculateRoomCount(int floor)
    {
        return baseRoomCount + (roomsPerFloor * (floor - 1));
    }

    /// <summary>
    /// Calculates enemy count for a given floor
    /// </summary>
    public int CalculateEnemyCount(int floor)
    {
        return baseEnemyCount + (enemiesPerFloor * (floor - 1));
    }

    /// <summary>
    /// Gets floor statistics as a formatted string
    /// </summary>
    public string GetFloorStatsString()
    {
        return $"Floor {_currentFloor} | {_currentRoomCount} Rooms | {_currentEnemyCount} Enemies";
    }

    /// <summary>
    /// Gets highscore as a formatted string
    /// </summary>
    public string GetHighscoreString()
    {
        if (_highestFloor == 0)
            return "No highscore yet";
        
        return $"Highest Floor: {_highestFloor} ({_highestRoomCount} rooms)";
    }

    // ── Highscore Persistence ──────────────────────────────────────────────

    private void LoadHighscore()
    {
        _highestFloor = PlayerPrefs.GetInt(PREF_HIGHEST_FLOOR, 0);
        _highestRoomCount = PlayerPrefs.GetInt(PREF_HIGHEST_ROOMS, 0);

        if (_highestFloor > 0)
        {
            Debug.Log($"[FloorProgression] Loaded highscore: Floor {_highestFloor} ({_highestRoomCount} rooms)");
        }
    }

    private void SaveHighscore()
    {
        PlayerPrefs.SetInt(PREF_HIGHEST_FLOOR, _highestFloor);
        PlayerPrefs.SetInt(PREF_HIGHEST_ROOMS, _highestRoomCount);
        PlayerPrefs.Save();

        Debug.Log($"[FloorProgression] Saved highscore: Floor {_highestFloor}");
    }

    /// <summary>
    /// Resets highscore (debug only)
    /// </summary>
    [ContextMenu("Reset Highscore")]
    public void ResetHighscore()
    {
        _highestFloor = 0;
        _highestRoomCount = 0;
        SaveHighscore();
        Debug.Log("[FloorProgression] Highscore reset");
    }

    /// <summary>
    /// Debug: Skip to specific floor
    /// </summary>
    [ContextMenu("Skip to Floor 5")]
    public void DebugSkipToFloor5()
    {
        _currentFloor = 5;
        _currentRoomCount = CalculateRoomCount(5);
        _currentEnemyCount = CalculateEnemyCount(5);
        Debug.Log($"[FloorProgression] DEBUG: Skipped to floor 5");
    }
}