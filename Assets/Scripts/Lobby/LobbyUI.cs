using System.Collections.Generic;
using System.Text;
using Nakama;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [Header("Refs")]
    public NakamaConnection conn;

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

    private const long OPCODE_START = 99;
    private const string LastMatchIdKey = "last_match_id";

    private readonly Dictionary<string, IUserPresence> players = new Dictionary<string, IUserPresence>();

    private void Awake()
    {
        if (!conn) conn = FindObjectOfType<NakamaConnection>();

        if (hostBtn) hostBtn.onClick.AddListener(HostLobby);
        if (refreshBtn) refreshBtn.onClick.AddListener(RefreshLobbies);
        if (leaveBtn) leaveBtn.onClick.AddListener(LeaveLobby);
        if (startBtn) startBtn.onClick.AddListener(StartGame);

        if (conn)
        {
            conn.MatchPresenceReceived += OnPresence;
            conn.MatchStateReceived += OnMatchState;
        }

        SetScreen(isInRoom: false);
        RefreshLobbyUi();
    }

    private void OnDestroy()
    {
        if (!conn) return;
        conn.MatchPresenceReceived -= OnPresence;
        conn.MatchStateReceived -= OnMatchState;
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
        SetScreen(isInRoom: true);
        RefreshLobbyUi("Hosting match " + ShortId(match.Id));
    }

    public async void RefreshLobbies()
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
                found = true;
                AddJoinRow(ShortId(m.MatchId) + "  |  " + m.Size + " players", m.MatchId);
            }
        }

        if (!found) AddInfoRow("No matches available.");
        RefreshLobbyUi("Matches refreshed.");
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
        SetScreen(isInRoom: true);
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
        SetScreen(isInRoom: false);
        RefreshLobbyUi("Left match.");
    }

    public async void StartGame()
    {
        if (conn == null || conn.Socket == null || conn.Match == null || !conn.IsCurrentPlayerMatchCreator) return;

        var payload = Encoding.UTF8.GetBytes("{\"start\":true}");
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_START, payload);
        RefreshLobbyUi("Start sent.");
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
    }

    private void OnMatchState(IMatchState state)
    {
        if (state == null || state.OpCode != OPCODE_START) return;
        RefreshLobbyUi("Game starting...");
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

    private bool HasConnectedSocket()
    {
        return conn != null && conn.Socket != null && conn.Socket.IsConnected;
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

        var sb = new StringBuilder();
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
