using UnityEngine;

public class MatchContext : MonoBehaviour
{
    public static MatchContext Instance
    {
        get
        {
            if (_instance != null) return _instance;
            var existing = FindObjectOfType<MatchContext>();
            if (existing != null)
            {
                _instance = existing;
                return _instance;
            }

            var go = new GameObject("MatchContext");
            _instance = go.AddComponent<MatchContext>();
            return _instance;
        }
    }

    private static MatchContext _instance;

    public MatchTransport.InitMsg lastInit;
    public bool hasInit;
    public bool started;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
