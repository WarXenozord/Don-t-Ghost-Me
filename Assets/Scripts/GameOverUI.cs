using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Game Over screen showing final stats and highscore.
/// Displays floor reached, rooms completed, and current highscore.
/// </summary>
public class GameOverUI : MonoBehaviour
{
    [Header("UI References")]
    public Text gameOverTitle;
    public Text finalStatsText;
    public Text highscoreText;
    public Button retryButton;
    public Button mainMenuButton;
    public Button quitButton;

    [Header("Scene Names")]
    public string lobbyScene = "Lobby";
    public string mainMenuScene = "MainMenu";

    [Header("Style")]
    public string newHighscorePrefix = "★ NEW RECORD! ★\n";

    private FloorProgressionManager _progressionManager;
    private bool _isNewHighscore = false;

    void Start()
    {
        _progressionManager = FloorProgressionManager.Instance;

        UpdateUI();

        // Setup buttons
        if (retryButton != null)
            retryButton.onClick.AddListener(OnRetryClicked);

        if (mainMenuButton != null)
            mainMenuButton.onClick.AddListener(OnMainMenuClicked);

        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitClicked);
    }

    private void UpdateUI()
    {
        if (_progressionManager == null)
        {
            if (finalStatsText != null)
                finalStatsText.text = "Error: No progression data found";
            return;
        }

        int finalFloor = _progressionManager.CurrentFloor;
        int finalRooms = _progressionManager.CurrentRoomCount;
        int highFloor = _progressionManager.HighestFloor;
        int highRooms = _progressionManager.HighestRoomCount;

        // Check if new highscore
        _isNewHighscore = finalFloor >= highFloor;

        // Update title
        if (gameOverTitle != null)
        {
            gameOverTitle.text = _isNewHighscore ? "NEW HIGHSCORE!" : "GAME OVER";
        }

        // Update final stats
        if (finalStatsText != null)
        {
            string stats = $"YOU REACHED FLOOR {finalFloor}\n";
            stats += $"TOTAL ROOMS CLEARED: {finalRooms}\n";
            stats += $"\n";
            stats += $"ENEMIES FACED: {_progressionManager.CalculateEnemyCount(finalFloor)}\n";

            finalStatsText.text = stats;
        }

        // Update highscore
        if (highscoreText != null)
        {
            if (_isNewHighscore)
            {
                highscoreText.text = newHighscorePrefix + 
                                   $"Floor {highFloor} | {highRooms} Rooms";
            }
            else
            {
                highscoreText.text = $"HIGHSCORE:\n" +
                                   $"Floor {highFloor} | {highRooms} Rooms";
            }
        }

        Debug.Log($"[GameOver] Final floor: {finalFloor}, Highscore: {highFloor}");
    }

    private void OnRetryClicked()
    {
        Debug.Log("[GameOver] Retry clicked - returning to lobby");
        
        // Reset progression for new run
        if (_progressionManager != null)
        {
            _progressionManager.StartNewRun();
        }

        // Reset death tracker
        var deathTracker = PlayerDeathTracker.Instance;
        if (deathTracker != null)
        {
            deathTracker.ResetTracker();
        }

        // Return to lobby to start new match
        SceneManager.LoadScene(lobbyScene);
    }

    private void OnMainMenuClicked()
    {
        Debug.Log("[GameOver] Main menu clicked");
        SceneManager.LoadScene(mainMenuScene);
    }

    private void OnQuitClicked()
    {
        Debug.Log("[GameOver] Quit clicked");
        
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }

    /// <summary>
    /// Call this to show specific player's stats
    /// </summary>
    public void ShowPlayerStats(string playerName, int floorReached, bool survived)
    {
        // Optional: Show individual player performance
        // Could be used for multiplayer end screen
    }
}