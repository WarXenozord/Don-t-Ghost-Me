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

    [Header("UI")]
    public Button hostBtn;
    public Button refreshBtn;
    public Button startBtn;
    public Transform matchListContainer;
    public Button matchRowPrefab;
    public TMP_Text infoText;

    private const long OPCODE_START = 99;

    private readonly Dictionary<string, IUserPresence> players = new Dictionary<string, IUserPresence>();
    private bool isHost;

    private void Awake()
    {
        if (!conn) conn = FindObjectOfType<NakamaConnection>();

        if (hostBtn) hostBtn.onClick.AddListener(HostLobby);
        if (refreshBtn) refreshBtn.onClick.AddListener(RefreshLobbies);
        if (startBtn) startBtn.onClick.AddListener(StartGame);

        if (conn)
        {
            conn.MatchPresenceReceived += OnPresence;
            conn.MatchStateReceived += OnMatchState;
        }

        SetStartButton();
        RenderInfo("Lobby ready.");
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

        conn.Match = await conn.Socket.CreateMatchAsync();
        isHost = true;

        players.Clear();
        ClearMatchList();
        SetStartButton();

        Debug.Log("[Lobby] Hosting match " + conn.Match.Id);
        RenderInfo("Hosting: " + ShortId(conn.Match.Id));
    }

    public async void RefreshLobbies()
    {
        if (conn == null || conn.Client == null || conn.Session == null)
        {
            Debug.LogWarning("[Lobby] Cannot refresh: not connected.");
            return;
        }

        ClearMatchList();

        const int minSize = 0;
        const int maxSize = 16;
        const int limit = 20;

        var res = await conn.Client.ListMatchesAsync(
            conn.Session,
            minSize,
            maxSize,
            limit,
            false,
            null,
            null
        );

        var hasAny = false;
        if (res != null && res.Matches != null)
        {
            foreach (var m in res.Matches)
            {
                if (m == null) continue;
                hasAny = true;

                var rowLabel = ShortId(m.MatchId) + " (" + m.Size + "/?)";
                AddJoinRow(rowLabel, m.MatchId);
            }
        }

        if (!hasAny)
        {
            AddInfoRow("No open lobbies.");
            Debug.Log("[Lobby] Refresh complete: no lobbies found.");
        }
        else
        {
            Debug.Log("[Lobby] Refresh complete.");
        }

        RenderInfo("Lobby list refreshed.");
    }

    public async void JoinLobby(string matchId)
    {
        if (!HasConnectedSocket()) return;
        if (string.IsNullOrEmpty(matchId)) return;

        conn.Match = await conn.Socket.JoinMatchAsync(matchId);
        isHost = false;

        players.Clear();
        ClearMatchList();
        SetStartButton();

        Debug.Log("[Lobby] Joined match " + conn.Match.Id);
        RenderInfo("Joined: " + ShortId(conn.Match.Id));
    }

    public async void StartGame()
    {
        if (!isHost || conn == null || conn.Socket == null || conn.Match == null)
        {
            Debug.LogWarning("[Lobby] Start blocked: host only and requires active match.");
            return;
        }

        var payload = Encoding.UTF8.GetBytes("{\"start\":true}");
        await conn.Socket.SendMatchStateAsync(conn.Match.Id, OPCODE_START, payload);
        Debug.Log("[Lobby] Start sent.");
        RenderInfo("Start sent.");
    }

    private void OnPresence(IMatchPresenceEvent e)
    {
        if (e == null) return;

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

        RenderInfo();
    }

    private void OnMatchState(IMatchState state)
    {
        if (state == null) return;

        if (state.OpCode == OPCODE_START)
        {
            Debug.Log("[Lobby] START received.");
            RenderInfo("START received.");
        }
    }

    private bool HasConnectedSocket()
    {
        if (conn == null || conn.Socket == null || !conn.Socket.IsConnected)
        {
            Debug.LogWarning("[Lobby] Socket not connected.");
            return false;
        }

        return true;
    }

    private void SetStartButton()
    {
        if (startBtn) startBtn.interactable = isHost;
    }

    private void RenderInfo(string extra = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Status: " + ConnectionState());

        if (conn != null && conn.Match != null)
        {
            sb.AppendLine("Lobby: " + ShortId(conn.Match.Id));
            sb.AppendLine("Role: " + (isHost ? "HOST" : "CLIENT"));
        }
        else
        {
            sb.AppendLine("Lobby: (none)");
        }

        sb.AppendLine("Players tracked: " + players.Count);

        if (!string.IsNullOrEmpty(extra))
        {
            sb.AppendLine(extra);
        }

        if (infoText) infoText.text = sb.ToString();
    }

    private string ConnectionState()
    {
        if (conn == null) return "No NakamaConnection";
        if (conn.Socket == null) return "Socket not created";
        return conn.Socket.IsConnected ? "Connected" : "Disconnected";
    }

    private static string ShortId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "(null)";
        return id.Length > 6 ? id.Substring(0, 6) : id;
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
        if (!matchRowPrefab || !matchListContainer)
        {
            Debug.Log("[Lobby] " + label + " => " + matchId);
            return;
        }

        var row = Instantiate(matchRowPrefab, matchListContainer);
        row.gameObject.SetActive(true);

        var rowText = row.GetComponentInChildren<Text>();
        if (rowText) rowText.text = label;

        row.onClick.RemoveAllListeners();
        row.onClick.AddListener(() => JoinLobby(matchId));
    }

    private void AddInfoRow(string text)
    {
        if (!matchRowPrefab || !matchListContainer)
        {
            Debug.Log("[Lobby] " + text);
            return;
        }

        var row = Instantiate(matchRowPrefab, matchListContainer);
        row.gameObject.SetActive(true);

        var rowText = row.GetComponentInChildren<Text>();
        if (rowText) rowText.text = text;

        row.onClick.RemoveAllListeners();
        row.interactable = false;
    }
}
