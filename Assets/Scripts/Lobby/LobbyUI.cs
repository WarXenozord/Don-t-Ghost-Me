using System.Collections.Generic;
using System.Text;
using Nakama;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [Header("Refs")]
    public NakamaConnection conn;
    public MatchTransport transport;
    public HostAuthority hostAuthority;
    public LobbyCameraMover lobbyCameraMover;
    public LobbyPlaceholderSpawner lobbyPlaceholderSpawner;

    [Header("Screens")]
    public GameObject lobbywindow;
    public GameObject roomwindow;

    [Header("Main Menu")]
    public Button hostBtn;
    public Button refreshBtn;
    public Transform matchListContainer;
    public Button matchRowPrefab;

    [Header("Lobby")]
    public TMP_Text lobbyMatchIdText;
    public TMP_Text playerListText;
    public Button leaveBtn;
    public Button startBtn;

    [Header("Optional")]
    public TMP_Text infoText;

    [Header("Scene Flow")]
    public string gameSceneName = "GameScene";
    [Header("Auto Refresh")]
    public bool autoRefreshOnStart = true;
    public float autoRefreshWaitTimeout = 10f;

    private const string LastMatchIdKey = "last_match_id";
    private const string StartedMatchesKey = "started_match_ids";

    private readonly Dictionary<string, IUserPresence> players = new Dictionary<string, IUserPresence>();
    private int _lastInitUiLogId = -1;
    private bool _loadingGameScene;
    private float _lobbyRosterRefreshAt;

    private void Awake()
    {
        if (!conn) conn = FindObjectOfType<NakamaConnection>();
        if (!transport) transport = FindObjectOfType<MatchTransport>();
        if (!hostAuthority) hostAuthority = FindObjectOfType<HostAuthority>();
        if (!lobbyCameraMover) lobbyCameraMover = FindObjectOfType<LobbyCameraMover>();
        if (!lobbyPlaceholderSpawner) lobbyPlaceholderSpawner = FindObjectOfType<LobbyPlaceholderSpawner>();

        if (hostBtn) hostBtn.onClick.AddListener(HostLobby);
        if (refreshBtn) refreshBtn.onClick.AddListener(RefreshLobbies);
        if (leaveBtn) leaveBtn.onClick.AddListener(LeaveLobby);
        if (startBtn) startBtn.onClick.AddListener(StartGame);

        if (conn) conn.MatchPresenceReceived += OnPresence;

        if (transport)
        {
            transport.OnInit += OnInitReceived;
            transport.OnStart += OnStartReceived;
            transport.OnLobbyPlaceholders += OnLobbyPlaceholdersReceived;
        }

        SetScreen(isInRoom: false);
        RefreshLobbyUi();
        SyncLobbyPlaceholders();
    }

    private void Start()
    {
        if (autoRefreshOnStart)
        {
            StartCoroutine(AutoRefreshWhenReady());
        }
    }

    private void Update()
    {
        if (_loadingGameScene) return;
        if (conn != null && conn.Match != null && Time.unscaledTime >= _lobbyRosterRefreshAt)
        {
            _lobbyRosterRefreshAt = Time.unscaledTime + 0.5f;
            RebuildPlayersFromCurrentMatch();
            if (conn.IsCurrentPlayerMatchCreator)
            {
                SyncLobbyPlaceholders();
            }
        }
        if (conn == null || conn.Match == null) return;

        var context = MatchContext.Instance;
        if (!context.hasInit || !context.started) return;

        LoadGameSceneIfNeeded();
    }

    private void OnDestroy()
    {
        if (conn) conn.MatchPresenceReceived -= OnPresence;

        if (transport)
        {
            transport.OnInit -= OnInitReceived;
            transport.OnStart -= OnStartReceived;
            transport.OnLobbyPlaceholders -= OnLobbyPlaceholdersReceived;
        }
    }

    public async void HostLobby()
    {
        if (!HasConnectedSocket()) return;

        var match = await conn.Socket.CreateMatchAsync();
        conn.Match = match;
        conn.MatchCreatorUserId = conn.SelfUserId;

        PlayerPrefs.SetString(LastMatchIdKey, match.Id);
        PlayerPrefs.Save();

        RebuildPlayersFromCurrentMatch();
        SyncLobbyPlaceholders();
        SetScreen(isInRoom: true);
        if (lobbyCameraMover) lobbyCameraMover.OnJoinedOrStartedMatch();
        RefreshLobbyUi("Hosting match " + ShortId(match.Id));
    }

    public async void RefreshLobbies()
    {
        await RefreshLobbiesInternal(silentStatus: false);
    }

    private async System.Threading.Tasks.Task RefreshLobbiesInternal(bool silentStatus)
    {
        if (conn == null || conn.Client == null || conn.Session == null)
        {
            RefreshLobbyUi("Not connected.");
            return;
        }

        ClearMatchList();

        const int minSize = 0;
        const int maxSize = 16;
        const int limit = 50;

        var res = await conn.Client.ListMatchesAsync(
            conn.Session,
            minSize,
            maxSize,
            limit,
            authoritative: false,
            label: null,
            query: null
        );

        var found = false;
        if (res?.Matches != null)
        {
            foreach (var m in res.Matches)
            {
                if (m == null || string.IsNullOrEmpty(m.MatchId)) continue;
                if (IsStartedMatchId(m.MatchId)) continue;
                found = true;
                AddJoinRow(ShortId(m.MatchId) + "  |  " + m.Size + " players", m.MatchId);
            }
        }

        if (!found) AddInfoRow("No matches available.");
        if (!silentStatus)
        {
            RefreshLobbyUi("Matches refreshed.");
        }
    }

    public async void JoinLobby(string matchId)
    {
        if (!HasConnectedSocket()) return;
        if (string.IsNullOrEmpty(matchId)) return;

        var match = await conn.Socket.JoinMatchAsync(matchId);
        conn.Match = match;
        var lastHostedMatchId = PlayerPrefs.GetString(LastMatchIdKey, string.Empty);
        conn.MatchCreatorUserId = match.Id == lastHostedMatchId ? conn.SelfUserId : string.Empty;

        RebuildPlayersFromCurrentMatch();
        SyncLobbyPlaceholders();
        SetScreen(isInRoom: true);
        if (lobbyCameraMover) lobbyCameraMover.OnJoinedOrStartedMatch();
        RefreshLobbyUi("Joined match " + ShortId(match.Id));
    }

    public async void LeaveLobby()
    {
        if (conn == null) return;

        if (conn.Socket != null && conn.Match != null)
        {
            await conn.Socket.LeaveMatchAsync(conn.Match.Id);
        }

        conn.Match = null;
        conn.MatchCreatorUserId = string.Empty;
        players.Clear();
        if (lobbyPlaceholderSpawner) lobbyPlaceholderSpawner.ClearAll();
        SetScreen(isInRoom: false);
        if (lobbyCameraMover) lobbyCameraMover.OnLeftMatch();
        RefreshLobbyUi("Left match.");
    }

    public void StartGame()
    {
        if (conn == null || conn.Match == null || !conn.IsCurrentPlayerMatchCreator) return;
        if (!hostAuthority)
        {
            RefreshLobbyUi("Host authority missing.");
            return;
        }

        hostAuthority.BeginMatchInitialization();
        RefreshLobbyUi("Init sent.");
    }

    private void OnPresence(IMatchPresenceEvent e)
    {
        if (conn == null || conn.Match == null || e == null) return;

        if (e.Joins != null)
        {
            foreach (var join in e.Joins)
            {
                if (join == null || string.IsNullOrEmpty(join.UserId)) continue;
                players[join.UserId] = join;
            }
        }

        if (e.Leaves != null)
        {
            foreach (var leave in e.Leaves)
            {
                if (leave == null || string.IsNullOrEmpty(leave.UserId)) continue;
                players.Remove(leave.UserId);
            }
        }

        RefreshLobbyUi();
        if (conn.IsCurrentPlayerMatchCreator)
        {
            SyncLobbyPlaceholders();
        }
    }

    private void OnInitReceived(MatchTransport.InitMsg msg)
    {
        if (msg == null || _lastInitUiLogId == msg.initId) return;
        _lastInitUiLogId = msg.initId;

        var context = MatchContext.Instance;
        context.lastInit = msg;
        context.hasInit = true;
        context.started = false;

        var hasSpawn = false;
        if (msg.spawns != null && conn != null && !string.IsNullOrEmpty(conn.SelfUserId))
        {
            foreach (var spawn in msg.spawns)
            {
                if (spawn == null) continue;
                if (spawn.userId == conn.SelfUserId)
                {
                    hasSpawn = true;
                    break;
                }
            }
        }

        RefreshLobbyUi(hasSpawn
            ? "Init received."
            : "Init received. Missing spawn for local user.");
    }

    private void OnStartReceived(MatchTransport.StartMsg msg)
    {
        if (msg == null) return;
        MatchContext.Instance.started = true;
        if (conn != null && conn.Match != null)
        {
            MarkMatchAsStarted(conn.Match.Id);
        }
        RefreshLobbyUi("Game starting...");
        LoadGameSceneIfNeeded();
    }

    private void LoadGameSceneIfNeeded()
    {
        if (_loadingGameScene) return;
        _loadingGameScene = true;
        SceneManager.LoadScene(gameSceneName);
    }

    private void RebuildPlayersFromCurrentMatch()
    {
        players.Clear();
        if (conn?.Match == null) return;

        if (conn.Match.Presences != null)
        {
            foreach (var p in conn.Match.Presences)
            {
                if (p == null || string.IsNullOrEmpty(p.UserId)) continue;
                players[p.UserId] = p;
            }
        }

        if (conn.Match.Self != null && !string.IsNullOrEmpty(conn.Match.Self.UserId))
        {
            players[conn.Match.Self.UserId] = conn.Match.Self;
        }
    }

    private void SyncLobbyPlaceholders()
    {
        if (!lobbyPlaceholderSpawner) return;
        if (conn == null || conn.Match == null)
        {
            lobbyPlaceholderSpawner.ClearAll();
            return;
        }

        if (conn.IsCurrentPlayerMatchCreator)
        {
            var ordered = BuildDeterministicLobbyOrder();
            lobbyPlaceholderSpawner.SyncPlayers(ordered);
            if (transport != null)
            {
                transport.BroadcastLobbyPlaceholders(new MatchTransport.LobbyPlaceholdersMsg
                {
                    orderedUserIds = ordered.ToArray()
                });
            }
        }
        else
        {
            // Clients only mirror host placeholder state.
            lobbyPlaceholderSpawner.ClearAll();
        }
    }

    private List<string> BuildDeterministicLobbyOrder()
    {
        var all = new List<string>(players.Keys);
        var creatorId = conn != null ? conn.MatchCreatorUserId : string.Empty;

        all.Sort((a, b) =>
        {
            var aIsCreator = !string.IsNullOrEmpty(creatorId) && a == creatorId;
            var bIsCreator = !string.IsNullOrEmpty(creatorId) && b == creatorId;
            if (aIsCreator && !bIsCreator) return -1;
            if (!aIsCreator && bIsCreator) return 1;
            return string.CompareOrdinal(a, b);
        });

        return all;
    }

    private void OnLobbyPlaceholdersReceived(MatchTransport.LobbyPlaceholdersMsg msg)
    {
        if (msg == null || lobbyPlaceholderSpawner == null) return;
        if (conn == null || conn.Match == null) return;
        if (conn.IsCurrentPlayerMatchCreator) return;

        // Only trust host as source of placeholder ordering.
        if (!string.IsNullOrEmpty(conn.MatchCreatorUserId) &&
            !string.IsNullOrEmpty(msg.senderUserId) &&
            msg.senderUserId != conn.MatchCreatorUserId)
        {
            return;
        }

        if (msg.orderedUserIds == null)
        {
            lobbyPlaceholderSpawner.ClearAll();
            return;
        }

        var ordered = new List<string>(msg.orderedUserIds.Length);
        for (var i = 0; i < msg.orderedUserIds.Length; i++)
        {
            var id = msg.orderedUserIds[i];
            if (string.IsNullOrEmpty(id)) continue;
            ordered.Add(id);
        }

        lobbyPlaceholderSpawner.SyncPlayers(ordered);
    }

    private bool HasConnectedSocket()
    {
        return conn != null && conn.Socket != null && conn.Socket.IsConnected;
    }

    private System.Collections.IEnumerator AutoRefreshWhenReady()
    {
        var waited = 0f;
        while (waited < Mathf.Max(0.1f, autoRefreshWaitTimeout))
        {
            if (conn != null &&
                conn.Client != null &&
                conn.Session != null &&
                conn.Socket != null &&
                conn.Socket.IsConnected)
            {
                break;
            }

            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        var task = RefreshLobbiesInternal(silentStatus: true);
        while (!task.IsCompleted)
        {
            yield return null;
        }
    }

    private static HashSet<string> LoadStartedMatchIds()
    {
        var set = new HashSet<string>();
        var raw = PlayerPrefs.GetString(StartedMatchesKey, string.Empty);
        if (string.IsNullOrEmpty(raw)) return set;

        var parts = raw.Split('|');
        for (var i = 0; i < parts.Length; i++)
        {
            var id = parts[i];
            if (string.IsNullOrEmpty(id)) continue;
            set.Add(id);
        }
        return set;
    }

    private static bool IsStartedMatchId(string matchId)
    {
        if (string.IsNullOrEmpty(matchId)) return false;
        var set = LoadStartedMatchIds();
        return set.Contains(matchId);
    }

    private static void MarkMatchAsStarted(string matchId)
    {
        if (string.IsNullOrEmpty(matchId)) return;

        var set = LoadStartedMatchIds();
        if (!set.Add(matchId)) return;

        var sb = new StringBuilder();
        foreach (var id in set)
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append(id);
        }

        PlayerPrefs.SetString(StartedMatchesKey, sb.ToString());
        PlayerPrefs.Save();
    }

    private void SetScreen(bool isInRoom)
    {
        if (lobbywindow) lobbywindow.SetActive(!isInRoom);
        if (roomwindow) roomwindow.SetActive(isInRoom);
    }

    private void RefreshLobbyUi(string status = null)
    {
        var hasMatch = conn != null && conn.Match != null;
        if (lobbyMatchIdText) lobbyMatchIdText.text = hasMatch ? "Match ID: " + ShortId(conn.Match.Id) : "Match ID: -";

        if (startBtn) startBtn.gameObject.SetActive(hasMatch && conn.IsCurrentPlayerMatchCreator);
        if (playerListText) playerListText.text = BuildPlayerList();

        if (infoText && !string.IsNullOrEmpty(status)) infoText.text = status;
    }

    private string BuildPlayerList()
    {
        if (players.Count == 0) return "No players";

        var sb = new System.Text.StringBuilder();
        foreach (var kv in players)
        {
            var p = kv.Value;
            var username = string.IsNullOrEmpty(p.Username) ? "Guest" : p.Username;
            sb.AppendLine(username + " (" + ShortId(p.UserId) + ")");
        }
        return sb.ToString();
    }

    private static string ShortId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "------";
        return id.Length <= 6 ? id : id.Substring(0, 6);
    }

    private void ClearMatchList()
    {
        if (!matchListContainer) return;

        for (var i = matchListContainer.childCount - 1; i >= 0; i--)
        {
            var child = matchListContainer.GetChild(i);
            if (matchRowPrefab != null && child == matchRowPrefab.transform) continue;
            Destroy(child.gameObject);
        }

        if (matchRowPrefab) matchRowPrefab.gameObject.SetActive(false);
    }

    private void AddJoinRow(string label, string matchId)
    {
        if (!matchRowPrefab || !matchListContainer) return;

        var row = Instantiate(matchRowPrefab, matchListContainer);
        row.gameObject.SetActive(true);

        var tmp = row.GetComponentInChildren<TMP_Text>();
        if (tmp) tmp.text = label;
        var txt = row.GetComponentInChildren<Text>();
        if (txt) txt.text = label;

        row.onClick.RemoveAllListeners();
        row.onClick.AddListener(() => JoinLobby(matchId));
    }

    private void AddInfoRow(string label)
    {
        if (!matchRowPrefab || !matchListContainer) return;

        var row = Instantiate(matchRowPrefab, matchListContainer);
        row.gameObject.SetActive(true);

        var tmp = row.GetComponentInChildren<TMP_Text>();
        if (tmp) tmp.text = label;
        var txt = row.GetComponentInChildren<Text>();
        if (txt) txt.text = label;

        row.onClick.RemoveAllListeners();
        row.interactable = false;
    }
}
