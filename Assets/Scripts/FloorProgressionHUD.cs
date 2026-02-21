using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-game HUD displaying current floor, room count, and stats.
/// Shows player count and highscore.
/// </summary>
public class FloorProgressionHUD : MonoBehaviour
{
    [Header("UI References")]
    public Text floorText;
    public Text statsText;
    public Text highscoreText;
    public Text playerCountText;

    [Header("Update Rate")]
    public float updateInterval = 0.5f;

    [Header("Style")]
    public bool showHighscoreInGame = true;
    public bool showPlayerCount = true;

    private FloorProgressionManager _progressionManager;
    private PlayerDeathTracker _deathTracker;
    private NakamaConnection _conn;
    private float _updateTimer;

    void Start()
    {
        ResolveRefs();
        UpdateUI();
    }

    void Update()
    {
        _updateTimer += Time.deltaTime;
        if (_updateTimer >= updateInterval)
        {
            _updateTimer = 0f;
            UpdateUI();
        }
    }

    private void UpdateUI()
    {
        ResolveRefs();

        if (_progressionManager == null) return;

        // Floor number
        if (floorText != null)
        {
            floorText.text = $"FLOOR {_progressionManager.CurrentFloor}";
        }

        // Stats (rooms, enemies)
        if (statsText != null)
        {
            int rooms = _progressionManager.CurrentRoomCount;
            int enemies = _progressionManager.CurrentEnemyCount;
            statsText.text = $"{rooms} Rooms | {enemies} Enemies";
        }

        // Highscore
        if (highscoreText != null && showHighscoreInGame)
        {
            if (_progressionManager.HighestFloor > 0)
            {
                highscoreText.text = $"Best: Floor {_progressionManager.HighestFloor}";
            }
            else
            {
                highscoreText.text = "";
            }
        }

        // Player count
        if (playerCountText != null && showPlayerCount)
        {
            if (_deathTracker != null)
            {
                int alive = _deathTracker.GetAlivePlayerCount();
                int dead = _deathTracker.GetDeadPlayerCount();
                int total = alive + dead;

                if (total > 0)
                {
                    playerCountText.text = $"Players: {alive}/{total} Alive";
                }
                else
                {
                    playerCountText.text = "";
                }
            }
            else
            {
                playerCountText.text = "";
            }
        }
    }

    private void ResolveRefs()
    {
        if (_progressionManager == null)
            _progressionManager = FloorProgressionManager.Instance;
        
        if (_deathTracker == null)
            _deathTracker = PlayerDeathTracker.Instance;
        
        if (_conn == null)
            _conn = NakamaConnection.Instance;
    }

    /// <summary>
    /// Shows a message on the HUD (for transitions, etc.)
    /// </summary>
    public void ShowMessage(string message, float duration = 3f)
    {
        // Optional: Add a message text field to show temporary messages
        // Like "Advancing to Floor 2..." or "All candles collected!"
    }
}