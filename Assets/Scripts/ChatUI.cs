using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class ChatUI : MonoBehaviour
{
    public static bool IsChatFocused { get; private set; }

    [Header("Refs")]
    public ChatController chatController;
    public NakamaConnection conn;
    public HostAuthority hostAuthority;

    [Header("UI")]
    public TMP_InputField inputField;
    public TMP_Text logText;
    public GameObject toMediumsIndicator;
    public GameObject toGhostsIndicator;

    [Header("Config")]
    public int maxLines = 30;
    public float messageLifetimeSeconds = 10f;

    private enum TargetMode
    {
        Mediums,
        Ghosts
    }

    private struct ChatLineEntry
    {
        public string text;
        public float expiresAt;
    }

    private readonly Queue<ChatLineEntry> _lines = new Queue<ChatLineEntry>();
    private TargetMode _selectedTarget = TargetMode.Ghosts;
    private bool _boundChat;
    private bool _boundInputFieldEvents;
    private bool _sanitizeSlashNextFrame;
    private string _lastResolvedRole = string.Empty;
    private bool _logDirty;

    void Awake()
    {
        ResolveRefs();
        BindInputFieldEvents();
        RefreshRoleUI();
    }

    void OnEnable()
    {
        ResolveRefs();
        BindInputFieldEvents();
        BindChat();
        RefreshRoleUI();
    }

    void OnDisable()
    {
        IsChatFocused = false;
        UnbindInputFieldEvents();
        if (chatController != null && _boundChat)
        {
            chatController.OnChatLine -= HandleChatLine;
            _boundChat = false;
        }
    }

    void Update()
    {
        ResolveRefs();
        BindChat();
        RefreshRoleIfChanged();
        HandleSlashToggle();
        SanitizeSlashIfNeeded();
        PruneExpiredLines();

        if (inputField == null) return;
        IsChatFocused = inputField.isFocused;
        if (!inputField.isFocused) return;

        HandleShiftTargetToggle();
        UpdateTargetIndicators();

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            SendCurrent();
        }
    }

    private void HandleSlashToggle()
    {
        if (inputField == null) return;
        if (!Input.GetKeyDown(KeyCode.Slash)) return;

        if (inputField.isFocused)
        {
            if (!string.IsNullOrEmpty(inputField.text) && inputField.text.EndsWith("/"))
            {
                inputField.text = inputField.text.Substring(0, inputField.text.Length - 1);
            }
            inputField.DeactivateInputField();
            IsChatFocused = false;
            return;
        }

        inputField.ActivateInputField();
        inputField.Select();
        IsChatFocused = true;
        _sanitizeSlashNextFrame = true;
        if (string.IsNullOrEmpty(inputField.text))
        {
            inputField.text = string.Empty;
        }
    }

    private void SanitizeSlashIfNeeded()
    {
        if (!_sanitizeSlashNextFrame || inputField == null) return;
        _sanitizeSlashNextFrame = false;

        if (string.IsNullOrEmpty(inputField.text)) return;
        if (inputField.text[0] == '/')
        {
            inputField.text = inputField.text.Substring(1);
            inputField.caretPosition = inputField.text.Length;
        }
    }

    private void HandleShiftTargetToggle()
    {
        if (inputField == null || !inputField.isFocused) return;
        if (GetLocalRole() != "Medium") return;

        if (!Input.GetKeyDown(KeyCode.LeftShift) && !Input.GetKeyDown(KeyCode.RightShift)) return;

        _selectedTarget = _selectedTarget == TargetMode.Mediums
            ? TargetMode.Ghosts
            : TargetMode.Mediums;
        ApplyCurrentCharacterLimit();
    }

    public void SelectTargetMediums()
    {
        _selectedTarget = TargetMode.Mediums;
        ApplyCurrentCharacterLimit();
        UpdateTargetIndicators();
    }

    public void SelectTargetGhosts()
    {
        _selectedTarget = TargetMode.Ghosts;
        ApplyCurrentCharacterLimit();
        UpdateTargetIndicators();
    }

    public void SendCurrent()
    {
        if (chatController == null || inputField == null) return;

        var text = inputField.text;
        if (string.IsNullOrWhiteSpace(text)) return;

        var role = GetLocalRole();
        var ok = false;

        if (role == "Ghost")
        {
            ok = chatController.TrySendToGhosts(text);
        }
        else
        {
            ok = _selectedTarget == TargetMode.Mediums
                ? chatController.TrySendToMediums(text)
                : chatController.TrySendToGhosts(text);
        }

        if (!ok) return;

        inputField.text = string.Empty;
        inputField.ActivateInputField();
    }

    private void HandleChatLine(string line)
    {
        if (string.IsNullOrEmpty(line) || logText == null) return;

        _lines.Enqueue(new ChatLineEntry
        {
            text = line,
            expiresAt = Time.unscaledTime + Mathf.Max(0.1f, messageLifetimeSeconds)
        });
        while (_lines.Count > Mathf.Max(1, maxLines))
        {
            _lines.Dequeue();
        }
        _logDirty = true;
        RebuildLogTextIfDirty();
    }

    private void RefreshRoleUI()
    {
        var role = GetLocalRole();
        var isMedium = role == "Medium";

        // Default target:
        // Medium can switch; Ghost is forced to Ghosts.
        if (!isMedium)
        {
            _selectedTarget = TargetMode.Ghosts;
        }

        ApplyCurrentCharacterLimit();
        UpdateTargetIndicators();
    }

    private void ResolveRefs()
    {
        if (chatController == null) chatController = FindObjectOfType<ChatController>();
        if (conn == null) conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        if (hostAuthority == null) hostAuthority = FindObjectOfType<HostAuthority>();
    }

    private void BindChat()
    {
        if (_boundChat || chatController == null) return;
        chatController.OnChatLine += HandleChatLine;
        _boundChat = true;
    }

    private void BindInputFieldEvents()
    {
        if (_boundInputFieldEvents || inputField == null) return;

        // Ensure Enter behaves as submit in a single-line chat box.
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.onSubmit.AddListener(OnInputSubmit);
        inputField.onEndEdit.AddListener(OnInputEndEdit);
        _boundInputFieldEvents = true;
    }

    private void UnbindInputFieldEvents()
    {
        if (!_boundInputFieldEvents || inputField == null) return;

        inputField.onSubmit.RemoveListener(OnInputSubmit);
        inputField.onEndEdit.RemoveListener(OnInputEndEdit);
        _boundInputFieldEvents = false;
    }

    private void OnInputSubmit(string _)
    {
        SendCurrent();
    }

    private void OnInputEndEdit(string _)
    {
        // Fallback path for platforms/builds where submit routes through end-edit.
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            SendCurrent();
        }
    }

    private string GetLocalRole()
    {
        if (chatController != null) return chatController.LocalRole;
        var selfId = conn != null ? conn.SelfUserId : string.Empty;
        var mediumId = hostAuthority != null ? hostAuthority.CurrentMediumUserId : string.Empty;
        return (!string.IsNullOrEmpty(selfId) && selfId == mediumId) ? "Medium" : "Ghost";
    }

    private void RefreshRoleIfChanged()
    {
        var role = GetLocalRole();
        if (role == _lastResolvedRole) return;
        _lastResolvedRole = role;
        RefreshRoleUI();
    }

    private void UpdateTargetIndicators()
    {
        var role = GetLocalRole();
        var isMedium = role == "Medium";

        if (!isMedium)
        {
            if (toMediumsIndicator != null) toMediumsIndicator.SetActive(false);
            if (toGhostsIndicator != null) toGhostsIndicator.SetActive(true);
            return;
        }

        if (toMediumsIndicator != null) toMediumsIndicator.SetActive(_selectedTarget == TargetMode.Mediums);
        if (toGhostsIndicator != null) toGhostsIndicator.SetActive(_selectedTarget == TargetMode.Ghosts);
    }

    private void ApplyCurrentCharacterLimit()
    {
        if (inputField == null) return;

        var limit = GetCurrentCharacterLimit();
        inputField.characterLimit = Mathf.Max(1, limit);

        if (!string.IsNullOrEmpty(inputField.text) && inputField.text.Length > inputField.characterLimit)
        {
            inputField.text = inputField.text.Substring(0, inputField.characterLimit);
            inputField.caretPosition = inputField.text.Length;
        }
    }

    private int GetCurrentCharacterLimit()
    {
        if (chatController == null) return 40;

        var role = GetLocalRole();
        if (role == "Medium" && _selectedTarget == TargetMode.Ghosts)
        {
            return Mathf.Max(1, chatController.mediumToGhostMaxLength);
        }

        return Mathf.Max(1, chatController.maxMessageLength);
    }

    private void PruneExpiredLines()
    {
        if (_lines.Count == 0) return;

        var now = Time.unscaledTime;
        var removed = false;
        while (_lines.Count > 0 && _lines.Peek().expiresAt <= now)
        {
            _lines.Dequeue();
            removed = true;
        }

        if (removed)
        {
            _logDirty = true;
            RebuildLogTextIfDirty();
        }
    }

    private void RebuildLogTextIfDirty()
    {
        if (!_logDirty || logText == null) return;
        _logDirty = false;

        if (_lines.Count == 0)
        {
            logText.text = string.Empty;
            return;
        }

        var snapshot = _lines.ToArray();
        var lines = new string[snapshot.Length];
        for (var i = 0; i < snapshot.Length; i++)
        {
            lines[i] = snapshot[i].text;
        }
        logText.text = string.Join("\n", lines);
    }
}
