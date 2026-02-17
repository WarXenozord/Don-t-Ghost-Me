using UnityEngine;

/// <summary>
/// Attach to lamp prefabs. Handles flickering behavior when triggered by the ghost.
/// Requires a Light component on this GameObject or a child.
/// </summary>
[RequireComponent(typeof(Light))]
public class LampFlicker : MonoBehaviour
{
    [Header("Flicker Settings")]
    [SerializeField] private float flickerDuration = 3f;
    [SerializeField] private float flickerSpeed = 0.1f;       // how fast the flicker changes
    [SerializeField] private float minIntensity = 0.1f;       // darkest point during flicker
    [SerializeField] private float maxIntensity = 1.5f;       // brightest point during flicker
    
    [Header("Highlight Settings")]
    [SerializeField] private GameObject highlightVisual;  // separate mesh visible only to nearby ghost
    [SerializeField] private Color highlightColor = new Color(0f, 2f, 2f, 1f);  // bright cyan
    [SerializeField] private float highlightPulseSpeed = 3f;
    [SerializeField] private float highlightIntensityMin = 2f;
    [SerializeField] private float highlightIntensityMax = 8f;
    
    [Header("State")]
    public bool isFlickering = false;
    
    private Light lampLight;
    private float originalIntensity;
    private Color originalColor;
    private float flickerTimer;
    private float nextFlickerTime;
    private Renderer highlightRenderer;
    private MaterialPropertyBlock highlightPropertyBlock;
    
    private void Awake()
    {
        lampLight = GetComponent<Light>();
        if (lampLight == null)
            lampLight = GetComponentInChildren<Light>();
        
        if (lampLight != null)
        {
            originalIntensity = lampLight.intensity;
            originalColor = lampLight.color;
        }
        
        // Get highlight visual renderer if assigned
        if (highlightVisual != null)
        {
            highlightRenderer = highlightVisual.GetComponent<Renderer>();
            if (highlightRenderer != null)
            {
                highlightPropertyBlock = new MaterialPropertyBlock();
            }
            
            // Start with highlight disabled
            highlightVisual.SetActive(false);
        }
        else
        {
            Debug.LogWarning($"[LampFlicker] No highlight visual assigned on {gameObject.name}. " +
                           "Create a child mesh (ring/sphere) and assign it for per-ghost highlights.");
        }
    }
    
    private void Update()
    {
        if (isFlickering)
        {
            UpdateFlicker();
        }
        
        // Update highlight pulsing if active
        if (highlightVisual != null && highlightVisual.activeSelf && !isFlickering)
        {
            UpdateHighlightPulse();
        }
    }
    
    /// <summary>
    /// Trigger the lamp to flicker. Called by GhostInteraction when ghost activates it.
    /// </summary>
    public void StartFlicker()
    {
        if (isFlickering) return; // already flickering
        
        isFlickering = true;
        flickerTimer = flickerDuration;
        nextFlickerTime = 0f;
    }
    
    private void UpdateFlicker()
    {
        flickerTimer -= Time.deltaTime;
        
        if (flickerTimer <= 0f)
        {
            // Flicker ended - restore original state
            StopFlicker();
            return;
        }
        
        // Random flicker pattern
        nextFlickerTime -= Time.deltaTime;
        if (nextFlickerTime <= 0f)
        {
            lampLight.intensity = Random.Range(minIntensity, maxIntensity);
            nextFlickerTime = Random.Range(flickerSpeed * 0.5f, flickerSpeed * 1.5f);
        }
    }
    
    private void StopFlicker()
    {
        isFlickering = false;
        if (lampLight != null)
        {
            lampLight.intensity = originalIntensity;
            lampLight.color = originalColor;
        }
    }
    
    /// <summary>
    /// Show visual feedback that this lamp is interactable.
    /// Called by GhostInteraction when lamp is in range and targeted.
    /// Only visible to the ghost that's targeting it.
    /// </summary>
    public void SetHighlight(bool enabled)
    {
        if (highlightVisual != null)
        {
            highlightVisual.SetActive(enabled && !isFlickering);
        }
    }
    
    private void UpdateHighlightPulse()
    {
        if (highlightRenderer == null || highlightPropertyBlock == null) return;
        
        // Intense pulsing with higher frequency and amplitude
        float pulse = (Mathf.Sin(Time.time * highlightPulseSpeed) + 1f) * 0.5f;
        float intensity = Mathf.Lerp(highlightIntensityMin, highlightIntensityMax, pulse);
        
        // Apply bright emissive color
        Color emissiveColor = highlightColor * intensity;
        
        highlightRenderer.GetPropertyBlock(highlightPropertyBlock);
        highlightPropertyBlock.SetColor("_EmissionColor", emissiveColor);
        highlightPropertyBlock.SetColor("_BaseColor", highlightColor);  // for URP
        highlightPropertyBlock.SetColor("_Color", highlightColor);      // for Standard
        highlightRenderer.SetPropertyBlock(highlightPropertyBlock);
    }
}