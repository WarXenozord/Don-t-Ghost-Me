using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class UsernameMenuUI : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField usernameInput;
    public TMP_Text feedbackText;
    public Button startButton;
    public Button backButton;

    [Header("Menu Panels")]
    public GameObject usernamePanel;
    public GameObject mainButtonsPanel;
    public MenuStateController menuStateController;
    [Header("Scene")]
    public string lobbySceneName = "Lobby";

    [Header("Style")]
    public Color successColor = Color.green;
    public Color errorColor = Color.red;
    public float feedbackDisplayTime = 2f;

    private UsernameManager _usernameManager;
    private float _feedbackTimer;

    void Start()
    {
        _usernameManager = UsernameManager.Instance;

        if (_usernameManager == null)
        {
            var go = new GameObject("UsernameManager");
            _usernameManager = go.AddComponent<UsernameManager>();
        }

        if (usernameInput != null)
        {
            usernameInput.text = _usernameManager.LocalDisplayName;
            usernameInput.characterLimit = 16;
        }

        if (startButton != null)
            startButton.onClick.AddListener(OnStartClicked);

        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);

        if (feedbackText != null)
            feedbackText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (_feedbackTimer > 0f)
        {
            _feedbackTimer -= Time.deltaTime;
            if (_feedbackTimer <= 0f && feedbackText != null)
                feedbackText.gameObject.SetActive(false);
        }

        if (Input.GetKeyDown(KeyCode.Return) || 
            Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            OnStartClicked();
        }
    }

    private void OnStartClicked()
    {
        if (usernameInput == null || _usernameManager == null) return;

        string newName = usernameInput.text;

        if (_usernameManager.SetLocalDisplayName(newName))
        {
            ShowFeedback("Username saved!", successColor);

            // Optional: Broadcast if needed
            var broadcaster = DisplayNameBroadcaster.Instance;
            if (broadcaster != null)
                broadcaster.BroadcastLocalDisplayName();

            // Small delay before loading Lobby (optional)
            Invoke(nameof(LoadLobby), 0.6f);
        }
        else
        {
            UsernameManager.IsValidUsername(newName, out string error);
            ShowFeedback(error, errorColor);
        }
    }

    private void LoadLobby()
    {
        SceneManager.LoadScene(lobbySceneName);
    }

    private void OnBackClicked()
    {
        if (usernamePanel != null)
            usernamePanel.SetActive(false);

        if (mainButtonsPanel != null)
           menuStateController.ReturnToMain();
    }

    private void ShowFeedback(string message, Color color)
    {
        if (feedbackText == null) return;

        feedbackText.text = message;
        feedbackText.color = color;
        feedbackText.gameObject.SetActive(true);
        _feedbackTimer = feedbackDisplayTime;
    }
}