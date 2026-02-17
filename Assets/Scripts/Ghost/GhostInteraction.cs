using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Handles the ghost's ability to interact with anything implementing IInteractable
/// (lamps, chairs, doors, etc.). Detects the nearest/looked-at interactable,
/// highlights it, and fires Interact() on key press after deducting energy.
///
/// Add new interactable prop types without ever touching this script — just
/// implement IInteractable on the new component.
/// </summary>
public class GhostInteraction : MonoBehaviour
{
    [Header("Interaction Settings")]
    [SerializeField] private float   interactionRange = 5f;
    [SerializeField] private KeyCode interactKey      = KeyCode.E;

    [Header("Detection")]
    [SerializeField] private bool  useRaycast      = true;
    [SerializeField] private float raycastDistance  = 10f;
    [SerializeField] private float detectionRadius  = 8f;
    [SerializeField] private LayerMask interactableLayer;

    [Header("References")]
    [SerializeField] private Transform   playerCamera;
    [SerializeField] private GhostEnergy ghostEnergy;

    [Header("UI Feedback (Optional)")]
    [SerializeField] private UnityEngine.UI.Text interactionPrompt;

    // ?? Internal ???????????????????????????????????????????????????????????

    private IInteractable       _currentTarget;
    private List<IInteractable> _allInteractables = new List<IInteractable>();

    // ?? Unity lifecycle ????????????????????????????????????????????????????

    private void Start()
    {
        RefreshInteractableCache();

        if (ghostEnergy == null)
            ghostEnergy = GetComponent<GhostEnergy>();

        if (playerCamera == null)
        {
            var controller = GetComponent<GhostController>();
            playerCamera   = controller != null ? controller.PlayerCamera : Camera.main?.transform;
        }

        if (interactionPrompt != null)
            interactionPrompt.gameObject.SetActive(false);
    }

    /// <summary>
    /// Call this after procedural props are spawned so newly created
    /// interactables are detected.
    /// </summary>
    public void RefreshInteractableCache()
    {
        _allInteractables.Clear();
        foreach (var mono in FindObjectsOfType<MonoBehaviour>())
        {
            if (mono is IInteractable interactable)
                _allInteractables.Add(interactable);
        }
        Debug.Log($"[GhostInteraction] Cached {_allInteractables.Count} interactables.");
    }

    private void Update()
    {
        if (FullMapViewer.IsOpen) return;

        IInteractable target = useRaycast ? FindByRaycast() : FindClosest();

        // Swap highlight if target changed
        if (target != _currentTarget)
        {
            _currentTarget?.SetHighlight(false);
            target?.SetHighlight(true);
            _currentTarget = target;
        }

        UpdatePrompt();

        if (Input.GetKeyDown(interactKey) && _currentTarget != null)
            TryInteract(_currentTarget);
    }

    // ?? Detection ??????????????????????????????????????????????????????????

    private IInteractable FindByRaycast()
    {
        if (playerCamera == null) return null;

        Ray ray = new Ray(playerCamera.position, playerCamera.forward);

        bool hit = interactableLayer != 0
            ? Physics.Raycast(ray, out RaycastHit hitInfo, raycastDistance, interactableLayer)
            : Physics.Raycast(ray, out hitInfo, raycastDistance);

        if (!hit) return null;

        IInteractable found = hitInfo.collider.GetComponent<IInteractable>()
                           ?? hitInfo.collider.GetComponentInParent<IInteractable>();

        if (found == null || found.IsBusy) return null;

        float dist = Vector3.Distance(transform.position,
                                      ((MonoBehaviour)found).transform.position);
        return dist <= interactionRange ? found : null;
    }

    private IInteractable FindClosest()
    {
        IInteractable closest  = null;
        float         bestDist = interactionRange;

        foreach (var interactable in _allInteractables)
        {
            if (interactable == null || interactable.IsBusy) continue;

            var mono = interactable as MonoBehaviour;
            if (mono == null) continue;

            float dist = Vector3.Distance(transform.position, mono.transform.position);
            if (dist < bestDist)
            {
                closest = interactable;
                bestDist = dist;
            }
        }

        return closest;
    }

    // ?? Interact ???????????????????????????????????????????????????????????

    private void TryInteract(IInteractable target)
    {
        if (target.IsBusy) return;

        if (ghostEnergy.currentHealth < target.EnergyCost)
        {
            Debug.Log($"[Ghost] Not enough energy! Need {target.EnergyCost}, " +
                      $"have {ghostEnergy.currentHealth:F0}");
            return;
        }

        ghostEnergy.currentHealth = Mathf.Clamp(
            ghostEnergy.currentHealth - target.EnergyCost,
            0f, ghostEnergy.maxHealth);

        // Pass ghost transform so props with direction (throw, push) can use it
        target.Interact(transform);

        _currentTarget = null;

        Debug.Log($"[Ghost] Interacted! Energy remaining: {ghostEnergy.currentHealth:F0}");
    }

    // ?? UI ?????????????????????????????????????????????????????????????????

    private void UpdatePrompt()
    {
        if (interactionPrompt == null) return;

        bool show = _currentTarget != null && !_currentTarget.IsBusy;
        interactionPrompt.gameObject.SetActive(show);

        if (show)
        {
            string name      = (_currentTarget as MonoBehaviour)?.gameObject.name ?? "object";
            bool   canAfford = ghostEnergy.currentHealth >= _currentTarget.EnergyCost;
            string costStr   = canAfford
                ? $"{_currentTarget.EnergyCost} energy"
                : $"not enough energy ({_currentTarget.EnergyCost} needed)";
            interactionPrompt.text = $"[{interactKey}]  {name}  —  {costStr}";
        }
    }

    // ?? Gizmos ?????????????????????????????????????????????????????????????

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
        if (!useRaycast)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
        }
    }
}