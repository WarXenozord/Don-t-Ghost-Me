using UnityEngine;

/// <summary>
/// Handles ghost's ability to interact with lamps (make them flicker).
/// Attach to the same GameObject as GhostController.
/// </summary>
public class GhostInteraction : MonoBehaviour
{
    [Header("Interaction Settings")]
    [SerializeField] private float interactionRange = 5f;
    [SerializeField] private float energyCostPerFlicker = 20f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private LayerMask lampLayer;  // optional: put lamps on a specific layer
    
    [Header("Detection")]
    [SerializeField] private float detectionRadius = 8f;  // how far to detect lamps
    [SerializeField] private bool useRaycast = true;      // raycast from camera vs sphere check
    [SerializeField] private float raycastDistance = 10f;
    
    [Header("References")]
    [SerializeField] private Transform playerCamera;
    [SerializeField] private GhostEnergy ghostEnergy;
    
    [Header("UI Feedback (Optional)")]
    [SerializeField] private UnityEngine.UI.Text interactionPrompt; // e.g., "Press E to flicker lamp"
    
    private LampFlicker currentTargetLamp;
    private LampFlicker[] allLamps;
    
    private void Start()
    {
        // Cache all lamps in the scene
        allLamps = FindObjectsOfType<LampFlicker>();
        
        if (ghostEnergy == null)
            ghostEnergy = GetComponent<GhostEnergy>();
        
        if (playerCamera == null)
        {
            // Try to get camera from GhostController
            var controller = GetComponent<GhostController>();
            if (controller != null)
            {
                playerCamera = controller.PlayerCamera;
            }
            
            // Fallback to main camera if still null
            if (playerCamera == null)
            {
                playerCamera = Camera.main?.transform;
            }
        }
        
        if (interactionPrompt != null)
            interactionPrompt.gameObject.SetActive(false);
    }
    
    private void Update()
    {
        if (FullMapViewer.IsOpen) return; // don't interact when map is open
        
        // Find the lamp we're targeting
        LampFlicker targetLamp = FindTargetLamp();
        
        // Update highlight state
        if (targetLamp != currentTargetLamp)
        {
            // Remove highlight from previous target
            if (currentTargetLamp != null)
                currentTargetLamp.SetHighlight(false);
            
            // Add highlight to new target
            if (targetLamp != null)
                targetLamp.SetHighlight(true);
            
            currentTargetLamp = targetLamp;
        }
        
        // Show/hide UI prompt
        UpdateInteractionPrompt(targetLamp != null);
        
        // Handle interaction input
        if (Input.GetKeyDown(interactKey) && currentTargetLamp != null)
        {
            TryInteractWithLamp(currentTargetLamp);
        }
    }
    
    /// <summary>
    /// Finds the lamp the ghost is currently targeting.
    /// Uses raycast from camera if enabled, otherwise finds closest lamp in radius.
    /// </summary>
    private LampFlicker FindTargetLamp()
    {
        if (useRaycast && playerCamera != null)
        {
            return FindLampByRaycast();
        }
        else
        {
            return FindClosestLamp();
        }
    }
    
    /// <summary>
    /// Raycast from camera to find lamp directly in view.
    /// </summary>
    private LampFlicker FindLampByRaycast()
    {
        Ray ray = new Ray(playerCamera.position, playerCamera.forward);
        RaycastHit hit;
        
        if (Physics.Raycast(ray, out hit, raycastDistance, lampLayer))
        {
            LampFlicker lamp = hit.collider.GetComponent<LampFlicker>();
            if (lamp == null)
                lamp = hit.collider.GetComponentInParent<LampFlicker>();
            
            if (lamp != null)
            {
                float distance = Vector3.Distance(transform.position, lamp.transform.position);
                if (distance <= interactionRange)
                    return lamp;
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Find the closest lamp within detection radius.
    /// </summary>
    private LampFlicker FindClosestLamp()
    {
        LampFlicker closest = null;
        float closestDistance = detectionRadius;
        
        foreach (LampFlicker lamp in allLamps)
        {
            if (lamp == null || lamp.isFlickering) continue;
            
            float distance = Vector3.Distance(transform.position, lamp.transform.position);
            
            if (distance < closestDistance && distance <= interactionRange)
            {
                closest = lamp;
                closestDistance = distance;
            }
        }
        
        return closest;
    }
    
    /// <summary>
    /// Attempt to make the target lamp flicker.
    /// Checks if ghost has enough energy and deducts cost.
    /// </summary>
    private void TryInteractWithLamp(LampFlicker lamp)
    {
        if (lamp == null) return;
        
        // Check if already flickering
        if (lamp.isFlickering)
        {
            Debug.Log("This lamp is already flickering!");
            return;
        }
        
        // Check energy cost
        if (ghostEnergy.currentHealth < energyCostPerFlicker)
        {
            Debug.Log($"Not enough energy! Need {energyCostPerFlicker}, have {ghostEnergy.currentHealth}");
            // You could show a UI message here
            return;
        }
        
        // Deduct energy
        ghostEnergy.currentHealth -= energyCostPerFlicker;
        ghostEnergy.currentHealth = Mathf.Clamp(ghostEnergy.currentHealth, 0f, ghostEnergy.maxHealth);
        
        // Trigger the flicker
        lamp.StartFlicker();
        
        // Remove highlight since lamp is now flickering
        lamp.SetHighlight(false);
        currentTargetLamp = null;
        
        Debug.Log($"Lamp flickering! Energy remaining: {ghostEnergy.currentHealth}");
    }
    
    /// <summary>
    /// Show or hide the interaction prompt UI.
    /// </summary>
    private void UpdateInteractionPrompt(bool show)
    {
        if (interactionPrompt == null) return;
        
        if (show && currentTargetLamp != null && !currentTargetLamp.isFlickering)
        {
            interactionPrompt.gameObject.SetActive(true);
            interactionPrompt.text = $"Press {interactKey} to flicker lamp ({energyCostPerFlicker} energy)";
        }
        else
        {
            interactionPrompt.gameObject.SetActive(false);
        }
    }
    
    // Optional: draw gizmos to visualize interaction range in editor
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
        
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}
