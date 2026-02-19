using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Attach to lamp prefabs. Handles flickering behavior when triggered by the ghost.
/// Requires a Light component on this GameObject or a child.
/// </summary>
[RequireComponent(typeof(Light))]
public class LampFlicker : MonoBehaviour, IInteractable
{
    private static readonly Dictionary<string, LampFlicker> Registry = new Dictionary<string, LampFlicker>();
    private static MatchTransport _transport;
    private static NakamaConnection _conn;
    private static bool _transportBound;

    [Header("Interaction")]
    [SerializeField] private float energyCost = 20f;
    [SerializeField] private string lampId;

    // ?? IInteractable ??????????????????????????????????????????????????????
    public float EnergyCost => energyCost;
    public bool  IsBusy     => isFlickering;

    public void Interact(UnityEngine.Transform ghostTransform)
    {
        StartFlicker();
        BroadcastFlicker();
        SetHighlight(false);
    }
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
        ResolveTransport();
        EnsureTransportBound();

        if (string.IsNullOrEmpty(lampId))
        {
            lampId = BuildLampIdFromPosition();
        }
        Registry[lampId] = this;

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

    private void OnDestroy()
    {
        if (!string.IsNullOrEmpty(lampId))
        {
            if (Registry.TryGetValue(lampId, out var current) && current == this)
            {
                Registry.Remove(lampId);
            }
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

    private void BroadcastFlicker()
    {
        ResolveTransport();
        if (_transport == null || _conn == null || _conn.Match == null) return;
        if (string.IsNullOrEmpty(lampId)) return;

        _transport.BroadcastLampFlicker(new MatchTransport.LampFlickerMsg
        {
            lampId = lampId
        });
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

    private static void OnLampFlickerReceived(MatchTransport.LampFlickerMsg msg)
    {
        if (msg == null || string.IsNullOrEmpty(msg.lampId)) return;
        ResolveTransport();

        if (_conn != null &&
            !string.IsNullOrEmpty(msg.senderUserId) &&
            !string.IsNullOrEmpty(_conn.SelfUserId) &&
            msg.senderUserId == _conn.SelfUserId)
        {
            return;
        }

        if (!Registry.TryGetValue(msg.lampId, out var lamp) || lamp == null) return;
        lamp.StartFlicker();
        lamp.SetHighlight(false);
    }

    private static void ResolveTransport()
    {
        if (_transport == null)
        {
            _transport = MatchTransport.Instance != null ? MatchTransport.Instance : FindObjectOfType<MatchTransport>();
        }
        if (_conn == null)
        {
            _conn = NakamaConnection.Instance != null ? NakamaConnection.Instance : FindObjectOfType<NakamaConnection>();
        }
    }

    private static void EnsureTransportBound()
    {
        if (_transportBound) return;
        ResolveTransport();
        if (_transport == null) return;
        _transport.OnLampFlicker += OnLampFlickerReceived;
        _transportBound = true;
    }

    private string BuildLampIdFromPosition()
    {
        var p = transform.position;
        var x = Mathf.RoundToInt(p.x * 10f);
        var y = Mathf.RoundToInt(p.y * 10f);
        var z = Mathf.RoundToInt(p.z * 10f);
        return "lamp:" + x + ":" + y + ":" + z;
    }
}
