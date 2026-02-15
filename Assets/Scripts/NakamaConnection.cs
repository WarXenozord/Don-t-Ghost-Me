using System;
using System.Threading.Tasks;
using Nakama;
using UnityEngine;

public class NakamaConnection : MonoBehaviour
{
    [Header("Nakama")]
    public string scheme = "http";        // local: http, prod: https
    public string host = "127.0.0.1";
    public int port = 7350;
    public string serverKey = "defaultkey";

    // Kept for compatibility with existing scripts/inspector setup.
    [Header("Match")]
    public bool createMatchIfNone = true;

    public IClient Client { get; private set; }
    public ISession Session { get; private set; }
    public ISocket Socket { get; private set; }
    public IMatch Match { get; set; }

    public string SelfUserId => Session?.UserId;

    async void Awake()
    {
        DontDestroyOnLoad(gameObject);
        await ConnectOnly();
    }

    public async Task ConnectOnly()
    {
        Client = new Client(scheme, host, port, serverKey, UnityWebRequestAdapter.Instance);

        var deviceId = GetOrCreateDeviceId();
        Session = await Client.AuthenticateDeviceAsync(deviceId, create: true);

        Socket = Client.NewSocket(useMainThread: true);
        Socket.ReceivedMatchState += OnReceivedMatchState;
        Socket.ReceivedMatchPresence += OnReceivedMatchPresence;

        await Socket.ConnectAsync(Session);

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
}
