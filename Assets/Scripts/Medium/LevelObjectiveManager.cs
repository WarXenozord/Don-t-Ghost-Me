using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Manages Floor 1 objectives:
/// 1. Find X candles scattered in the building
/// 2. Collect all candles (by interacting)
/// 3. Interact with the Ritual Mark
/// 4. Watch candles form circle around mark
/// 5. Advance to next floor/scene
/// </summary>
public class LevelObjectiveManager : MonoBehaviour
{
    [Header("Objectives")]
    [SerializeField] private int requiredCandles = 5;
    [SerializeField] private string nextSceneName = "Floor2";

    private RitualMark ritualMark;

    [Header("UI")]
    [SerializeField] private TMP_Text objectiveText;
    [SerializeField] private TMP_Text candleCountText;
    [SerializeField] private GameObject completionPanel; // optional "Level Complete!" panel

    [Header("Scene Transition")]
    [SerializeField] private float delayBeforeTransition = 3f;

    [Header("Network Sync")]
    [SerializeField] private MatchTransport transport;
    [SerializeField] private NakamaConnection conn;
    [Header("Debug Skip")]
    [SerializeField] private bool enableDebugSkipHotkey = false;
    [SerializeField] private KeyCode debugSkipHotkey = KeyCode.H;

    // ?? Internal ???????????????????????????????????????????????????????????

    private List<Candle> _collectedCandles = new List<Candle>();
    private readonly HashSet<string> _collectedCandleIds = new HashSet<string>();
    private readonly Dictionary<string, Candle> _candlesById = new Dictionary<string, Candle>();
    private bool _ritualComplete = false;
    private bool _debugSkipTriggered;
    private float _nextRitualMarkLookupAt;

    // ?? Unity lifecycle ????????????????????????????????????????????????????

    public void SetMark(GameObject m){
        if (m == null)
        {
            ritualMark = null;
            Debug.LogWarning("[LevelObjective] SetMark called with null GameObject.");
            return;
        }

        ritualMark = m.GetComponent<RitualMark>();
        if (ritualMark == null)
        {
            Debug.LogWarning("[LevelObjective] SetMark target has no RitualMark component.");
            return;
        }

        Debug.Log($"[LevelObjective] Sent RitualMark: {ritualMark.gameObject.name} " +
                          $"(InstanceID: {ritualMark.GetInstanceID()}) at {ritualMark.transform.position}");
    }
    private void Start()
    {
        if (conn == null) conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        if (transport == null) transport = MatchTransport.Instance != null ? MatchTransport.Instance : FindObjectOfType<MatchTransport>();
        if (transport != null) transport.OnObjectiveState += OnObjectiveStateReceived;

        BuildCandleRegistry();

        if (ritualMark == null)
        {
            ritualMark = FindObjectOfType<RitualMark>();
            if (ritualMark != null)
            {
                Debug.Log($"[LevelObjective] Auto-found RitualMark: {ritualMark.gameObject.name} " +
                          $"(InstanceID: {ritualMark.GetInstanceID()}) at {ritualMark.transform.position}");
            }
            else
            {
                Debug.LogWarning("[LevelObjective] No RitualMark found in scene yet. Will retry.");
                _nextRitualMarkLookupAt = Time.unscaledTime + 0.5f;
            }
        }
        else
        {
            Debug.Log($"[LevelObjective] Using assigned RitualMark: {ritualMark.gameObject.name} " +
                      $"(InstanceID: {ritualMark.GetInstanceID()})");
        }

        // Check for multiple instances (common bug)
        var allMarks = FindObjectsOfType<RitualMark>();
        if (allMarks.Length > 1)
        {
            Debug.LogWarning($"[LevelObjective] Found {allMarks.Length} RitualMark instances! " +
                           "There should only be 1. This may cause interaction issues.");
            for (int i = 0; i < allMarks.Length; i++)
            {
                Debug.LogWarning($"  [{i}] {allMarks[i].gameObject.name} (ID: {allMarks[i].GetInstanceID()}) " +
                               $"at {allMarks[i].transform.position}");
            }
        }

        if (completionPanel != null)
            completionPanel.SetActive(false);

        UpdateUI();
    }

    private void Update()
    {
        if (ritualMark == null && Time.unscaledTime >= _nextRitualMarkLookupAt)
        {
            _nextRitualMarkLookupAt = Time.unscaledTime + 0.5f;
            ritualMark = FindObjectOfType<RitualMark>();
            if (ritualMark != null)
            {
                Debug.Log($"[LevelObjective] Late-found RitualMark: {ritualMark.gameObject.name} " +
                          $"(InstanceID: {ritualMark.GetInstanceID()}) at {ritualMark.transform.position}");
            }
        }

        if (!enableDebugSkipHotkey || _debugSkipTriggered) return;
        if (!Input.GetKeyDown(debugSkipHotkey)) return;

        _debugSkipTriggered = true;
        Debug.Log("[LevelObjective] Debug skip hotkey pressed.");
        ForceSkipToSceneLoad();
    }

    private void OnDestroy()
    {
        if (transport != null)
        {
            transport.OnObjectiveState -= OnObjectiveStateReceived;
        }
    }

    // ?? Candle Collection ??????????????????????????????????????????????????

    public void OnCandleCollected(Candle candle)
    {
        if (candle == null) return;

        var candleId = candle.GetSyncId();
        if (string.IsNullOrEmpty(candleId)) return;
        if (_collectedCandleIds.Contains(candleId)) return;

        _collectedCandleIds.Add(candleId);
        _collectedCandles.Add(candle);
        candle.ApplyRemoteCollectedVisuals();
        UpdateUI();

        Debug.Log($"[LevelObjective] Candle collected! {_collectedCandles.Count}/{requiredCandles}");

        TryActivateRitualIfReady();
        BroadcastObjectiveState(candleId);
    }

    public List<Candle> GetCollectedCandles() => _collectedCandles;

    // ?? Ritual ?????????????????????????????????????????????????????????????

   public void OnRitualComplete()
    {
        if (_ritualComplete) return;
        _ritualComplete = true;

        Debug.Log("[LevelObjective] Ritual complete! Advancing to next floor...");

        if (completionPanel != null)
            completionPanel.SetActive(true);
        BroadcastObjectiveState(string.Empty);
    }

    private void LoadNextScene()
    {
        if (!string.IsNullOrEmpty(nextSceneName))
        {
            SceneManager.LoadScene(nextSceneName);
        }
        else
        {
            Debug.LogWarning("[LevelObjective] No next scene set. Staying on current floor.");
        }
    }

    // ?? UI ?????????????????????????????????????????????????????????????????

    private void UpdateUI()
    {
        if (candleCountText != null)
        {
            candleCountText.text = $"Candles: {_collectedCandles.Count}/{requiredCandles}";
        }

        if (objectiveText != null)
        {
            if (_ritualComplete)
            {
                objectiveText.text = "Ritual complete! Ascending to next floor...";
            }
            else if (_collectedCandles.Count >= requiredCandles)
            {
                objectiveText.text = "All candles collected! Find the Ritual Mark and press E.";
            }
            else
            {
                objectiveText.text = $"Find and collect {requiredCandles - _collectedCandles.Count} more candles.";
            }
        }
    }

    private void BuildCandleRegistry()
    {
        _candlesById.Clear();
        var allCandles = FindObjectsOfType<Candle>(true);
        for (var i = 0; i < allCandles.Length; i++)
        {
            var candle = allCandles[i];
            if (candle == null) continue;
            var id = candle.GetSyncId();
            if (string.IsNullOrEmpty(id)) continue;
            _candlesById[id] = candle;
        }
    }

    private void TryActivateRitualIfReady()
    {
        if (_collectedCandleIds.Count < requiredCandles) return;

        if (ritualMark != null)
        {
            ritualMark.Activate();
            Debug.Log($"[LevelObjective] Activated RitualMark (ID: {ritualMark.GetInstanceID()}) at {ritualMark.transform.position}");
            ritualMark.bostaBostaBosta();
        }
        else
        {
            Debug.LogError("[LevelObjective] All candles collected but ritualMark is null!");
        }

        Debug.Log("[LevelObjective] All candles collected! Find the Ritual Mark!");
    }

    private void OnObjectiveStateReceived(MatchTransport.ObjectiveStateMsg msg)
    {
        if (msg == null) return;
        if (conn != null &&
            !string.IsNullOrEmpty(msg.senderUserId) &&
            !string.IsNullOrEmpty(conn.SelfUserId) &&
            msg.senderUserId == conn.SelfUserId)
        {
            return;
        }

        if (!string.IsNullOrEmpty(msg.candleId))
        {
            ApplyCollectedCandleById(msg.candleId);
        }

        if (msg.ritualComplete && !_ritualComplete)
        {
            ApplyRitualCompleteFromNetwork();
            return;
        }

        UpdateUI();
    }

    private void ApplyCollectedCandleById(string candleId)
    {
        if (string.IsNullOrEmpty(candleId) || _collectedCandleIds.Contains(candleId)) return;

        _collectedCandleIds.Add(candleId);

        if (!_candlesById.TryGetValue(candleId, out var candle) || candle == null)
        {
            BuildCandleRegistry();
            _candlesById.TryGetValue(candleId, out candle);
        }

        if (candle != null)
        {
            candle.ApplyRemoteCollectedVisuals();
            if (!_collectedCandles.Contains(candle))
            {
                _collectedCandles.Add(candle);
            }
        }

        TryActivateRitualIfReady();
        UpdateUI();
    }

    private void ApplyRitualCompleteFromNetwork()
    {
        if (_ritualComplete) return;
        _ritualComplete = true;
        UpdateUI();
        if (completionPanel != null) completionPanel.SetActive(true);
        Debug.Log("[LevelObjective] Ritual complete synced from network.");
    }

    private void BroadcastObjectiveState(string candleId)
    {
        if (transport == null || conn == null || conn.Match == null) return;
        transport.BroadcastObjectiveState(new MatchTransport.ObjectiveStateMsg
        {
            candleId = candleId,
            collectedCount = _collectedCandleIds.Count,
            ritualComplete = _ritualComplete
        });
    }

    private void ForceSkipToSceneLoad()
    {
        if (!_ritualComplete)
        {
            OnRitualComplete();
        }

        var ritualHandler = RitualCompletionHandler.Instance != null
            ? RitualCompletionHandler.Instance
            : FindObjectOfType<RitualCompletionHandler>();
        if (ritualHandler != null)
        {
            ritualHandler.BroadcastRitualCompletion();
        }
        else
        {
            Debug.LogWarning("[LevelObjective] Debug skip failed: RitualCompletionHandler not found.");
        }
    }

    // ?? Debug ??????????????????????????????????????????????????????????????

    [ContextMenu("Complete Objective (Debug)")]
    private void DebugCompleteObjective()
    {
        // Simulate collecting all candles
        var allCandles = FindObjectsOfType<Candle>();
        for (int i = 0; i < Mathf.Min(requiredCandles, allCandles.Length); i++)
        {
            OnCandleCollected(allCandles[i]);
        }
    }

    [ContextMenu("Skip to Next Scene")]
    private void DebugSkipScene()
    {
        LoadNextScene();
    }
}
