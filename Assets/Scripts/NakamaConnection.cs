using System;
using System.Threading.Tasks;
using Nakama;
using UnityEngine;

public class NakamaConnection : MonoBehaviour
{
    [Header("Nakama")]
    public string scheme = "https";
    public string host = "nakama.juanlibonatti.com";
    public int port = 443;
    public string serverKey = "6a990ac72bd7bf85414160d6cd207e1b45f56535ddf71566af85e6b217aa850b";

    // Kept for compatibility with existing scripts/inspector setup.
    [Header("Match")]
    public bool createMatchIfNone = true;

    public IClient Client { get; private set; }
    public ISession Session { get; private set; }
    public ISocket Socket { get; private set; }
    public IMatch Match { get; set; }
    public string MatchCreatorUserId { get; set; }

    public string SelfUserId => Session?.UserId;
    public bool IsCurrentPlayerMatchCreator =>
        !string.IsNullOrEmpty(SelfUserId) &&
        !string.IsNullOrEmpty(MatchCreatorUserId) &&
        SelfUserId == MatchCreatorUserId;

    async void Awake()
    {
        DontDestroyOnLoad(gameObject);
        await ConnectOnly();
    }

    public async Task ConnectOnly()
    {
        Client = new Client(scheme, host, port, serverKey, UnityWebRequestAdapter.Instance);

        var deviceId = GetOrCreateDeviceId();
        var username = GetOrCreateGuestUsername();
        Session = await Client.AuthenticateDeviceAsync(deviceId, username: username, create: true);

        Socket = Client.NewSocket(useMainThread: true);
        Socket.ReceivedMatchState += OnReceivedMatchState;
        Socket.ReceivedMatchPresence += OnReceivedMatchPresence;

        await Socket.ConnectAsync(Session, true); // IMPORTANT for HTTPS/WSS

        Debug.Log($"[Nakama] Connected as {Session.Username} ({Session.UserId}).");
    }

    public event Action<IMatchState> MatchStateReceived;
    public event Action<IMatchPresenceEvent> MatchPresenceReceived;

    private void OnReceivedMatchState(IMatchState state) => MatchStateReceived?.Invoke(state);
    private void OnReceivedMatchPresence(IMatchPresenceEvent e) => MatchPresenceReceived?.Invoke(e);

    private string GetOrCreateDeviceId()
    {
        const string key = "device_id";
        if (PlayerPrefs.HasKey(key)) return PlayerPrefs.GetString(key);

        var id = Guid.NewGuid().ToString("N");
        PlayerPrefs.SetString(key, id);
        PlayerPrefs.Save();
        return id;
    }

    private string GetOrCreateGuestUsername()
    {
        const string key = "guest_username";
        if (PlayerPrefs.HasKey(key)) return PlayerPrefs.GetString(key);

        var suffix = UnityEngine.Random.Range(100, 1000);
        var username = "Guest" + suffix;
        PlayerPrefs.SetString(key, username);
        PlayerPrefs.Save();
        return username;
    }
}
