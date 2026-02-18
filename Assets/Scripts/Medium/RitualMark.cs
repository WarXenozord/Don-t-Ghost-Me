using UnityEngine;

/// <summary>
/// The ritual mark on a table. Inactive until all candles are collected.
/// When activated, ghost can interact to trigger the ritual (candles fly to circle).
/// </summary>
public class RitualMark : MonoBehaviour, IInteractable
{
    [Header("Interaction")]
    [SerializeField] private float energyCost = 50f; // completing ritual costs energy

    [Header("Ritual Circle")]
    [SerializeField] private float circleRadius = 2f;
    [SerializeField] private float candleHeight = 1.2f; // height above mark surface

    [Header("Highlight")]
    [SerializeField] private GameObject highlightVisual;
    [SerializeField] private Color inactiveColor = new Color(0.3f, 0.3f, 0.3f); // dim gray
    [SerializeField] private Color activeColor   = new Color(0.5f, 0f, 1f);     // purple glow
    [SerializeField] private float highlightPulseSpeed = 2f;
    [SerializeField] private float highlightIntensityMin = 1f;
    [SerializeField] private float highlightIntensityMax = 5f;

    [Header("Visual")]
    [SerializeField] private GameObject markModel; // the ritual symbol mesh
    [SerializeField] private ParticleSystem ritualEffect; // optional particles during animation

    // ?? IInteractable ??????????????????????????????????????????????????????
    public float EnergyCost => energyCost;
    public bool  IsBusy     => _ritualActive || !_canActivate;

    public void Interact(Transform ghostTransform)
    {
        if (!_canActivate || _ritualActive) return;
        TriggerRitual();
    }

    // ?? Internal ???????????????????????????????????????????????????????????

    private bool _canActivate   = false; // true once all candles collected
    private bool _ritualActive  = false; // true during candle animation
    private Renderer _highlightRenderer;
    private MaterialPropertyBlock _highlightBlock;
    private LevelObjectiveManager _manager;
  

    private void Awake()
    {
        if (highlightVisual != null)
        {
            _highlightRenderer = highlightVisual.GetComponent<Renderer>();
            if (_highlightRenderer != null)
                _highlightBlock = new MaterialPropertyBlock();
            highlightVisual.SetActive(false);
        }

        _manager = FindObjectOfType<LevelObjectiveManager>();
    }

    private void Update()
    {
        if (highlightVisual != null && highlightVisual.activeSelf)
            UpdateHighlightPulse();
    }

    // ?? Activation ?????????????????????????????????????????????????????????

    /// <summary>
    /// Called by LevelObjectiveManager when all candles are collected.
    /// </summary>
    public void Activate()
    {
        _canActivate = true;
        Debug.Log("[RitualMark] Activated! Ghost can now trigger the ritual.");
    }

    // ?? Ritual ?????????????????????????????????????????????????????????????

    private void TriggerRitual()
    {
        _ritualActive = true;
        SetHighlight(false);

        if (ritualEffect != null)
            ritualEffect.Play();

        // Animate candles to circle positions
        AnimateCandlesToCircle();

        // Notify manager after animation completes
        float animDuration = 2.5f; // total time for all candles to arrive
        Invoke(nameof(CompleteRitual), animDuration);

        Debug.Log("[RitualMark] Ritual triggered!");
    }

    private void AnimateCandlesToCircle()
    {
        if (_manager == null) return;

        var candles = _manager.GetCollectedCandles();
        int count   = candles.Count;

        for (int i = 0; i < count; i++)
        {
            float angle    = i * (360f / count) * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * circleRadius;
            Vector3 target = transform.position + offset + Vector3.up * candleHeight;

            float delay = i * 0.15f; // stagger the animations
            candles[i].AnimateToPosition(target, delay);
        }
    }

    private void CompleteRitual()
    {
        Debug.Log("[RitualMark] Ritual complete!");

        if (_manager != null)
            _manager.OnRitualComplete();
    }

    // ?? Highlight ??????????????????????????????????????????????????????????

    public void SetHighlight(bool enabled)
    {
        if (highlightVisual == null) return;

        // Only highlight if we're ready to activate
        highlightVisual.SetActive(enabled && _canActivate && !_ritualActive);
    }

    private void UpdateHighlightPulse()
    {
        if (_highlightRenderer == null || _highlightBlock == null) return;

        Color baseColor = _canActivate ? activeColor : inactiveColor;
        float pulse     = (Mathf.Sin(Time.time * highlightPulseSpeed) + 1f) * 0.5f;
        float intensity = Mathf.Lerp(highlightIntensityMin, highlightIntensityMax, pulse);
        Color emissive  = baseColor * intensity;

        _highlightRenderer.GetPropertyBlock(_highlightBlock);
        _highlightBlock.SetColor("_EmissionColor", emissive);
        _highlightBlock.SetColor("_BaseColor", baseColor);
        _highlightBlock.SetColor("_Color", baseColor);
        _highlightRenderer.SetPropertyBlock(_highlightBlock);
    }

    // ?? Gizmos ?????????????????????????????????????????????????????????????

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0f, 1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, circleRadius);
    }
}