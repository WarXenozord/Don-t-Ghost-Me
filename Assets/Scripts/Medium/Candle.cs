using UnityEngine;

/// <summary>
/// Collectible candle for Floor 1 ritual. MEDIUMS pick it up (not the ghost).
/// Ghost cannot interact with candles - they're always visible to mediums.
/// Notifies LevelObjectiveManager when collected by a medium.
/// </summary>
public class Candle : MonoBehaviour
{
    [Header("Highlight - Always Visible to Mediums")]
    [SerializeField] private GameObject highlightVisual;
    [SerializeField] private Color highlightColor = new Color(1f, 0.8f, 0f); // golden glow
    [SerializeField] private float highlightPulseSpeed = 2.5f;
    [SerializeField] private float highlightIntensityMin = 1.5f;
    [SerializeField] private float highlightIntensityMax = 6f;

    [Header("Visual")]
    [SerializeField] private GameObject candleModel; // the actual candle mesh
    [SerializeField] private ParticleSystem collectEffect; // optional sparkle on pickup
    private bool _isAimedAt = false;

    // ?? Internal ???????????????????????????????????????????????????????????

    public bool _collected = false;
    private Renderer _highlightRenderer;
    private MaterialPropertyBlock _highlightBlock;

    private void Awake()
    {
        if (highlightVisual != null)
        {
            _highlightRenderer = highlightVisual.GetComponent<Renderer>();
            if (_highlightRenderer != null)
                _highlightBlock = new MaterialPropertyBlock();
            
           
            highlightVisual.SetActive(false);
        }
    }
    public void SetHighlight(bool enable)
    {
        if (highlightVisual != null && !_collected)
        {
            highlightVisual.SetActive(enable);
        }
    }
    private void Update()
    {
         if (highlightVisual == null || _collected)
        return;
        if (_isAimedAt)
            UpdateHighlightPulse();
    }
    public void SetAimed(bool aimed)
{
    _isAimedAt = aimed;
}
    // ?? Collection (Medium-only) ???????????????????????????????????????????

    /// <summary>
    /// Called by MediumController when a living player collects this candle.
    /// Ghost CANNOT call this - they don't interact with candles directly.
    /// </summary>
    public void CollectByMedium(MediumController medium)
    {
        if (_collected) return;

        _collected = true;
        Debug.Log($"[Candle] Collected by Medium: {medium.name}");
        if (highlightVisual != null)
            highlightVisual.SetActive(false);

        // Notify manager
        var manager = FindObjectOfType<LevelObjectiveManager>();
        if (manager != null)
            manager.OnCandleCollected(this);

        // Optional effect
        if (collectEffect != null)
            collectEffect.Play();
        // Hide model but keep GameObject alive (manager tracks it)
        if (candleModel != null)
            candleModel.SetActive(false);

        
    }

    /// <summary>
    /// Called by RitualMark animation to move this candle to a circle position.
    /// </summary>
    public void AnimateToPosition(Vector3 targetPos, float delay)
    {
        Debug.Log("Animating candles");
        if (candleModel != null)
            candleModel.SetActive(true); // show it again for the animation

        StartCoroutine(AnimateToPositionCoroutine(targetPos, delay));
    }

    private System.Collections.IEnumerator AnimateToPositionCoroutine(Vector3 targetPos, float delay)
    {
        yield return new WaitForSeconds(delay);

        float duration = 1.5f;
        float elapsed  = 0f;
        Vector3 start  = transform.position;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            // Ease out cubic for smooth landing
            float smooth = 1f - Mathf.Pow(1f - t, 3f);
            transform.position = Vector3.Lerp(start, targetPos, smooth);
            yield return null;
        }

        transform.position = targetPos;
    }

    // ?? Highlight ??????????????????????????????????????????????????????????

    private void UpdateHighlightPulse()
    {
        if (_highlightRenderer == null || _highlightBlock == null) return;

        float pulse     = (Mathf.Sin(Time.time * highlightPulseSpeed) + 1f) * 0.5f;
        float intensity = Mathf.Lerp(highlightIntensityMin, highlightIntensityMax, pulse);
        Color emissive  = highlightColor * intensity;

        _highlightRenderer.GetPropertyBlock(_highlightBlock);
        _highlightBlock.SetColor("_EmissionColor", emissive);
        _highlightBlock.SetColor("_BaseColor", highlightColor);
        _highlightBlock.SetColor("_Color", highlightColor);
        _highlightRenderer.SetPropertyBlock(_highlightBlock);
    }
}