using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// UI for changing player display name in menu scene.
/// </summary>
public class UsernameMenuUI : MonoBehaviour
{
    [Header("UI References")]
    public InputField usernameInput;
    public Text feedbackText;
    public Button saveButton;
    public Button backButton;

    [Header("Scene Navigation")]
    public string returnScene = "MainMenu"; // Scene to go back to

    [Header("Style")]
    public Color successColor = Color.green;
    public Color errorColor = Color.red;
    public float feedbackDisplayTime = 3f;

    private UsernameManager _usernameManager;
    private float _feedbackTimer;

    void Start()
    {
        _usernameManager = UsernameManager.Instance;
        if (_usernameManager == null)
        {
            // Create if doesn't exist
            var go = new GameObject("UsernameManager");
            _usernameManager = go.AddComponent<UsernameManager>();
        }

        // Setup UI
        if (usernameInput != null)
        {
            usernameInput.text = _usernameManager.LocalDisplayName;
            usernameInput.characterLimit = 16; // Max username length
        }

        if (saveButton != null)
        {
            saveButton.onClick.AddListener(OnSaveClicked);
        }

        if (backButton != null)
        {
            backButton.onClick.AddListener(OnBackClicked);
        }

        if (feedbackText != null)
        {
            feedbackText.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        // Hide feedback after delay
        if (_feedbackTimer > 0f)
        {
            _feedbackTimer -= Time.deltaTime;
            if (_feedbackTimer <= 0f && feedbackText != null)
            {
                feedbackText.gameObject.SetActive(false);
            }
        }

        // Save on Enter key
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            OnSaveClicked();
        }
    }

    private void OnSaveClicked()
    {
        if (usernameInput == null || _usernameManager == null) return;

        string newName = usernameInput.text;

        if (_usernameManager.SetLocalDisplayName(newName))
        {
            ShowFeedback("Username saved!", successColor);

            // Broadcast new name if in a match
            var broadcaster = DisplayNameBroadcaster.Instance;
            if (broadcaster != null)
            {
                broadcaster.BroadcastLocalDisplayName();
            }
        }
        else
        {
            // Get error message
            UsernameManager.IsValidUsername(newName, out string error);
            ShowFeedback(error, errorColor);
        }
    }

    private void OnBackClicked()
    {
        if (!string.IsNullOrEmpty(returnScene))
        {
            SceneManager.LoadScene(returnScene);
        }
    }

    private void ShowFeedback(string message, Color color)
    {
        if (feedbackText == null) return;

        feedbackText.text = message;
        feedbackText.color = color;
        feedbackText.gameObject.SetActive(true);
        _feedbackTimer = feedbackDisplayTime;
    }

    /// <summary>
    /// Call this from another button/UI to open username menu
    /// </summary>
    public static void OpenUsernameMenu(string usernameScene = "UsernameMenu")
    {
        SceneManager.LoadScene(usernameScene);
    }
}