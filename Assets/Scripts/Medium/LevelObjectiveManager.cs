using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

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
    [SerializeField] private Text objectiveText;
    [SerializeField] private Text candleCountText;
    [SerializeField] private GameObject completionPanel; // optional "Level Complete!" panel

    [Header("Scene Transition")]
    [SerializeField] private float delayBeforeTransition = 3f;

    // ?? Internal ???????????????????????????????????????????????????????????

    private List<Candle> _collectedCandles = new List<Candle>();
    private bool _ritualComplete = false;

    // ?? Unity lifecycle ????????????????????????????????????????????????????

    public void SetMark(GameObject m){
        ritualMark = m.GetComponent<RitualMark>();
        Debug.Log($"[LevelObjective] Sent RitualMark: {ritualMark.gameObject.name} " +
                          $"(InstanceID: {ritualMark.GetInstanceID()}) at {ritualMark.transform.position}");
    }
    private void Start()
    {
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
                Debug.LogError("[LevelObjective] No RitualMark found in scene!");
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

    // ?? Candle Collection ??????????????????????????????????????????????????

    public void OnCandleCollected(Candle candle)
    {
        if (_collectedCandles.Contains(candle)) return;

        _collectedCandles.Add(candle);
        UpdateUI();

        Debug.Log($"[LevelObjective] Candle collected! {_collectedCandles.Count}/{requiredCandles}");

        // Activate ritual mark once all candles are collected
        if (_collectedCandles.Count >= requiredCandles)
        {
            if (ritualMark != null)
            {
                ritualMark.Activate();
                
                Debug.Log($"[LevelObjective] Activated RitualMark (ID: {ritualMark.GetInstanceID()}) " +
                          $"at {ritualMark.transform.position}");
                ritualMark.bostaBostaBosta();
            }
            else
            {
                Debug.LogError("[LevelObjective] All candles collected but ritualMark is null!");
            }

            Debug.Log("[LevelObjective] All candles collected! Find the Ritual Mark!");
        }
    }

    public List<Candle> GetCollectedCandles() => _collectedCandles;

    // ?? Ritual ?????????????????????????????????????????????????????????????

    public void OnRitualComplete()
    {
        if (_ritualComplete) return;
        _ritualComplete = true;

        UpdateUI();

        if (completionPanel != null)
            completionPanel.SetActive(true);

        // Transition to next scene
        Invoke(nameof(LoadNextScene), delayBeforeTransition);

        Debug.Log("[LevelObjective] Ritual complete! Loading next floor...");
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