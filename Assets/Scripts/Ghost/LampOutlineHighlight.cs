using UnityEngine;

/// <summary>
/// OPTIONAL: Alternative visual feedback using an outline/selection ring.
/// Use this INSTEAD of the color pulsing in LampFlicker if you prefer.
/// 
/// Setup:
/// 1. Create a child object under your lamp (e.g., a thin torus/ring mesh at the base)
/// 2. Give it a bright emissive material
/// 3. Add this script to the ring object
/// 4. Set the ring inactive by default
/// 5. In LampFlicker, add: [SerializeField] private GameObject outlineHighlight;
/// 6. In SetHighlight(), do: if (outlineHighlight) outlineHighlight.SetActive(enabled);
/// 
/// This script pulses and rotates the ring when active.
/// </summary>
public class LampOutlineHighlight : MonoBehaviour
{
    [Header("Pulse Settings")]
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private float minScale = 0.9f;
    [SerializeField] private float maxScale = 1.1f;
    [SerializeField] private bool rotateRing = true;
    [SerializeField] private float rotationSpeed = 30f;
    
    private Vector3 originalScale;
    private Renderer highlightRenderer;
    private MaterialPropertyBlock propertyBlock;
    
    private void Awake()
    {
        originalScale = transform.localScale;
        highlightRenderer = GetComponent<Renderer>();
        
        if (highlightRenderer != null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }
    }
    
    private void OnEnable()
    {
        // Reset scale when enabled
        if (originalScale != Vector3.zero)
            transform.localScale = originalScale;
    }
    
    private void Update()
    {
        // Pulse the scale
        float pulse = Mathf.Lerp(minScale, maxScale, 
            (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f);
        transform.localScale = originalScale * pulse;
        
        // Optional rotation for extra visibility
        if (rotateRing)
        {
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.Self);
        }
        
        // Pulse emission intensity if material supports it
        if (highlightRenderer != null && propertyBlock != null)
        {
            float intensity = Mathf.Lerp(0.5f, 2f, 
                (Mathf.Sin(Time.time * pulseSpeed * 1.5f) + 1f) * 0.5f);
            
            highlightRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_EmissionColor", Color.cyan * intensity);
            highlightRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
