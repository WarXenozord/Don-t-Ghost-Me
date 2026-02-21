using UnityEngine;

/// <summary>
/// The ritual mark on a table. Inactive until all candles are collected.
/// When activated, ghost can interact to trigger the ritual (candles fly to circle).
/// </summary>
public class RitualMark : MonoBehaviour, IInteractable
{
    private FloorTransitionManager _floorTransition;
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
    [SerializeField] private GameObject ritualLightPrefab;

private GameObject _spawnedLight;

    // ?? IInteractable ??????????????????????????????????????????????????????
    public float EnergyCost => energyCost;
    public bool  IsBusy     => _ritualActive || !cuAtivado;

    public void Interact(Transform ghostTransform)
    {
        if (!cuAtivado || _ritualActive) return;
        TriggerRitual();
    }

    // ?? Internal ???????????????????????????????????????????????????????????

    [Header("Runtime State (Read-Only)")]
    private bool cuAtivado = false; // true once all candles collected
    private bool _ritualActive = false; // true during candle animation
    
    private Renderer _highlightRenderer;
    private MaterialPropertyBlock _highlightBlock;
    private LevelObjectiveManager _manager;
   
    
   
    
    private void Start()
    {
         if (highlightVisual != null)
        {
            _highlightRenderer = highlightVisual.GetComponent<Renderer>();
            if (_highlightRenderer != null)
                _highlightBlock = new MaterialPropertyBlock();
            highlightVisual.SetActive(false);
        }

        _manager = FindObjectOfType<LevelObjectiveManager>();
        
        // Debug what we found
        Debug.Log($"[RitualMark {GetInstanceID()}] Initialized at {transform.position}");
        Debug.Log($"  - Manager found: {_manager != null}");
        Debug.Log($"  - Highlight assigned: {highlightVisual != null}");
        Debug.Log($"  - Layer: {LayerMask.LayerToName(gameObject.layer)}");
        Debug.Log($"  - Collider: {GetComponent<Collider>() != null}");
    }
    
    private void Update()
    {
        // Only pulse highlight if active
        if (highlightVisual != null && highlightVisual.activeSelf)
            UpdateHighlightPulse();
    }

    // ?? Activation ?????????????????????????????????????????????????????????

    /// <summary>
    /// Called by LevelObjectiveManager when all candles are collected.
    /// </summary>
    public void Activate()
    {
        if (_spawnedLight != null)
            return; // already spawned
        cuAtivado = true;
        Debug.Log("[RitualMark] Spawning ritual light.");
        // Enable spooky light
        _spawnedLight = Instantiate(
            ritualLightPrefab,
            transform.position,
            Quaternion.identity,
            transform // optional: parent it to ritual mark
        );
        
        
        Debug.Log($"[RitualMark {GetInstanceID()}] ACTIVATED!");
        Debug.Log($"  - cuAtivado: {cuAtivado}");
        Debug.Log($"  - IsBusy: {IsBusy}");
        Debug.Log($"  - Position: {transform.position}");
        Debug.Log($"  - GameObject: {gameObject.name}");
    }
    public void bostaBostaBosta(){
        cuAtivado = true;
        Debug.Log("This is an altervative to Activate, but lets hope it does the same fucking thing.");
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

        // Notify objective manager
        if (_manager != null)
            _manager.OnRitualComplete();
        
        // Trigger floor transition
        if (_floorTransition == null)
            _floorTransition = FloorTransitionManager.Instance != null 
                ? FloorTransitionManager.Instance 
                : FindObjectOfType<FloorTransitionManager>();
        
        if (_floorTransition != null)
        {
            _floorTransition.TriggerFloorTransition();
        }
        else
        {
            Debug.LogError("[RitualMark] FloorTransitionManager not found! Cannot advance to next floor.");
        }
    }


    // ?? Highlight ??????????????????????????????????????????????????????????

    public void SetHighlight(bool enabled)
    {
        if (highlightVisual == null) return;

        // Only highlight if we're ready to activate
        bool shouldShow = enabled && cuAtivado && !_ritualActive;
        
        if (enabled && !cuAtivado)
        {
            Debug.Log($"[RitualMark {GetInstanceID()}] Ghost tried to highlight but cuAtivado=false. " +
                      "Candles not collected yet?");
        }
        
        if (enabled && shouldShow)
        {
            Debug.Log($"[RitualMark {GetInstanceID()}] Highlight ON!");
        }
        
        highlightVisual.SetActive(shouldShow);
    }

    private void UpdateHighlightPulse()
    {
        if (_highlightRenderer == null || _highlightBlock == null) return;

        Color baseColor = cuAtivado ? activeColor : inactiveColor;
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