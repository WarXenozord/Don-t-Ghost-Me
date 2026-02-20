using System;
using UnityEngine;

public class ChatController : MonoBehaviour
{
    [Header("Refs")]
    public NakamaConnection conn;
    public MatchTransport transport;
    public HostAuthority hostAuthority;
    public PlayerSpawnManager playerSpawner;

    [Header("Costs")]
    public int ghostMessageCost = 10;
    [Header("Limits")]
    public int maxMessageLength = 40;
    public int mediumToGhostMaxLength = 10;

    public event Action<string> OnChatLine;
    public string LocalRole => GetLocalRole();

    private const string RoleMedium = "Medium";
    private const string RoleGhost = "Ghost";
    private const string TargetMediums = "Mediums";
    private const string TargetGhosts = "Ghosts";
    private const string TargetAllGhosts = "AllGhosts";

    private bool _bound;

    void Awake()
    {
        ResolveRefs();
        EnsureBound();
    }

    void Update()
    {
        ResolveRefs();
        EnsureBound();
    }

    void OnDestroy()
    {
        if (transport != null && _bound)
        {
            transport.OnChat -= OnChatReceived;
            _bound = false;
        }
    }

    public bool TrySendToMediums(string text)
    {
        var trimmed = NormalizeOutgoing(text, maxMessageLength);
        if (string.IsNullOrEmpty(trimmed)) return false;

        var localRole = GetLocalRole();
        if (localRole == RoleGhost)
        {
            Debug.Log("[Chat] blocked local ghost->medium send.");
            return false;
        }

        return SendInternal(trimmed, TargetMediums, cost: 0);
    }

    public bool TrySendToGhosts(string text)
    {
        var localRole = GetLocalRole();
        var maxLenForRoute = localRole == RoleMedium ? mediumToGhostMaxLength : maxMessageLength;
        var trimmed = NormalizeOutgoing(text, maxLenForRoute);
        if (string.IsNullOrEmpty(trimmed)) return false;
        if (localRole == RoleGhost)
        {
            return SendInternal(trimmed, TargetGhosts, cost: 0);
        }

        // Medium -> Ghosts costs stamina.
        if (!TrySpendMediumStamina(ghostMessageCost))
        {
            Debug.Log("[Chat] not enough stamina for medium->ghost message.");
            return false;
        }

        return SendInternal(trimmed, TargetGhosts, cost: ghostMessageCost);
    }

    private bool SendInternal(string text, string target, int cost)
    {
        ResolveRefs();
        if (transport == null || conn == null || conn.Match == null) return false;

        var initId = hostAuthority != null ? hostAuthority.ActiveInitId : -1;
        if (initId < 0)
        {
            Debug.Log("[Chat] blocked send: invalid initId.");
            return false;
        }

        var msg = new MatchTransport.ChatMsg
        {
            initId = initId,
            senderRole = GetLocalRole(),
            text = text,
            target = target,
            cost = Mathf.Max(0, cost)
        };

        transport.SendChat(msg);

        // Local echo: always show sender's own message in local log.
        var color = (msg.senderRole == RoleMedium && NormalizeTarget(msg.target) == TargetGhosts)
            ? "#FF4D4D"
            : "#FFFFFF";
        var localLine = "<color=" + color + ">[" + msg.senderRole + "] " + ShortId(conn.SelfUserId) + ": " + msg.text + "</color>";
        OnChatLine?.Invoke(localLine);
        return true;
    }

    private void OnChatReceived(MatchTransport.ChatMsg msg)
    {
        if (msg == null) return;
        if (conn != null &&
            !string.IsNullOrEmpty(msg.senderUserId) &&
            !string.IsNullOrEmpty(conn.SelfUserId) &&
            msg.senderUserId == conn.SelfUserId)
        {
            // Own message is already echoed locally on send.
            return;
        }
        var activeInitId = hostAuthority != null ? hostAuthority.ActiveInitId : -1;
        if (msg.initId != activeInitId)
        {
            return;
        }

        var senderRole = ResolveRole(msg.senderUserId);
        var localRole = GetLocalRole();
        var isHostViewer = conn != null && conn.IsCurrentPlayerMatchCreator;
        var target = NormalizeTarget(msg.target);

        if (senderRole == RoleGhost && target == TargetMediums)
        {
            Debug.Log("[Chat] blocked ghost->medium");
            return;
        }

        var shouldDisplay = false;
        if (target == TargetMediums)
        {
            shouldDisplay = localRole == RoleMedium || isHostViewer;
        }
        else if (target == TargetGhosts || target == TargetAllGhosts)
        {
            shouldDisplay = localRole == RoleGhost || isHostViewer;
        }

        if (!shouldDisplay) return;

        var safeText = NormalizeIncoming(msg.text);
        if (string.IsNullOrEmpty(safeText)) return;

        var line = "[" + senderRole + "] " + ShortId(msg.senderUserId) + ": " + safeText;
        OnChatLine?.Invoke(line);
    }

    private bool TrySpendMediumStamina(int amount)
    {
        if (amount <= 0) return true;

        if (playerSpawner == null)
        {
            playerSpawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        }

        var selfId = conn != null ? conn.SelfUserId : string.Empty;
        if (string.IsNullOrEmpty(selfId) || playerSpawner == null) return false;
        if (!playerSpawner.TryGet(selfId, out var localGo) || localGo == null) return false;

        var medium = localGo.GetComponentInChildren<MediumController>(true);
        if (medium == null || !medium.enabled) return false;

        return medium.TryConsumeStamina(amount);
    }

    private string GetLocalRole()
    {
        var self = conn != null ? conn.SelfUserId : string.Empty;
        if (IsUserCurrentlyGhost(self)) return RoleGhost;
        if (IsUserCurrentlyMedium(self)) return RoleMedium;

        var mediumUserId = hostAuthority != null ? hostAuthority.CurrentMediumUserId : string.Empty;
        if (!string.IsNullOrEmpty(self) && self == mediumUserId) return RoleMedium;

        // Fail-safe: if we cannot resolve from runtime objects yet, prefer Medium so
        // alive players are not silently filtered from medium channel.
        return RoleMedium;
    }

    private string ResolveRole(string userId)
    {
        if (IsUserCurrentlyGhost(userId)) return RoleGhost;
        if (IsUserCurrentlyMedium(userId)) return RoleMedium;

        var mediumUserId = hostAuthority != null ? hostAuthority.CurrentMediumUserId : string.Empty;
        if (!string.IsNullOrEmpty(userId) && userId == mediumUserId) return RoleMedium;

        // Default unknown peers to Medium until explicitly ghosted.
        return RoleMedium;
    }

    private bool IsUserCurrentlyGhost(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;

        if (playerSpawner == null)
        {
            playerSpawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        }
        if (playerSpawner == null) return false;
        if (!playerSpawner.TryGet(userId, out var go) || go == null) return false;

        var ghost = go.GetComponentInChildren<GhostController>(true);
        return ghost != null;
    }

    private bool IsUserCurrentlyMedium(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return false;

        if (playerSpawner == null)
        {
            playerSpawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        }
        if (playerSpawner == null) return false;
        if (!playerSpawner.TryGet(userId, out var go) || go == null) return false;

        var medium = go.GetComponentInChildren<MediumController>(true);
        if (medium != null) return true;

        // If avatar is explicitly ghost, don't classify as medium.
        var ghost = go.GetComponentInChildren<GhostController>(true);
        if (ghost != null) return false;

        return false;
    }

    private void ResolveRefs()
    {
        if (conn == null) conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        if (transport == null) transport = MatchTransport.Instance != null ? MatchTransport.Instance : FindObjectOfType<MatchTransport>();
        if (hostAuthority == null) hostAuthority = FindObjectOfType<HostAuthority>();
        if (playerSpawner == null) playerSpawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
    }

    private void EnsureBound()
    {
        if (_bound || transport == null) return;
        transport.OnChat += OnChatReceived;
        _bound = true;
    }

    private string NormalizeOutgoing(string text, int routeMaxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var trimmed = text.Trim();
        var maxLen = Mathf.Max(1, Mathf.Min(maxMessageLength, routeMaxLength));
        if (trimmed.Length > maxLen) trimmed = trimmed.Substring(0, maxLen);
        return trimmed;
    }

    private string NormalizeIncoming(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var trimmed = text.Trim();
        var maxLen = Mathf.Max(1, maxMessageLength);
        if (trimmed.Length > maxLen) trimmed = trimmed.Substring(0, maxLen);
        return trimmed;
    }

    private static string NormalizeTarget(string target)
    {
        if (string.IsNullOrEmpty(target)) return string.Empty;
        if (string.Equals(target, TargetAllGhosts, StringComparison.OrdinalIgnoreCase)) return TargetAllGhosts;
        if (string.Equals(target, TargetGhosts, StringComparison.OrdinalIgnoreCase)) return TargetGhosts;
        if (string.Equals(target, TargetMediums, StringComparison.OrdinalIgnoreCase)) return TargetMediums;
        return target;
    }

    private static string ShortId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "------";
        return id.Length <= 6 ? id : id.Substring(0, 6);
    }
}
