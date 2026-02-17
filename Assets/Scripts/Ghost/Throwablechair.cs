using UnityEngine;

/// <summary>
/// Attach to any chair (or other throwable prop).
/// States:
///   Fixed     — kinematic, sitting at original position/rotation
///   Flying    — rigidbody active, thrown in ghost's look direction
///   Settling  — on the floor, waiting 5 s before returning
///   Returning — smoothly lerps back to origin position/rotation
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class Throwablechair : MonoBehaviour, IInteractable
{
    [Header("Interaction")]
    [SerializeField] private float energyCost   = 30f;

    [Header("Throw Settings")]
    [SerializeField] private float throwForce   = 12f;
    [SerializeField] private float throwUpward  = 3f;    // upward kick so it arcs
    [SerializeField] private float torqueAmount = 8f;    // spin during flight

    [Header("Return Settings")]
    [SerializeField] private float settleTime   = 5f;    // seconds on floor before returning
    [SerializeField] private float returnSpeed  = 1.5f;  // lerp speed back to origin
    [SerializeField] private float returnSnapDistance = 0.05f; // snap when this close

    [Header("Highlight")]
    [SerializeField] private GameObject highlightVisual;
    [SerializeField] private float highlightPulseSpeed    = 3f;
    [SerializeField] private float highlightIntensityMin  = 2f;
    [SerializeField] private float highlightIntensityMax  = 8f;
    [SerializeField] private Color highlightColor         = new Color(1f, 0.4f, 0f); // orange — distinct from lamp cyan

    // ?? IInteractable ??????????????????????????????????????????????????????
    public float EnergyCost => energyCost;
    public bool  IsBusy     => _state != State.Fixed;

    public void Interact(Transform ghostTransform)
    {
        if (_state != State.Fixed) return;
        SetHighlight(false);
        Throw(ghostTransform);
    }

    // ?? Internal ???????????????????????????????????????????????????????????

    private enum State { Fixed, Flying, Settling, Returning }

    private Rigidbody   _rb;
    private State       _state       = State.Fixed;
    private float       _settleTimer = 0f;

    private Vector3     _originPos;
    private Quaternion  _originRot;

    private Renderer    _highlightRenderer;
    private MaterialPropertyBlock _highlightBlock;

    // ?? Unity lifecycle ????????????????????????????????????????????????????

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        // Store spawn transform as the "home" position
        _originPos = transform.position;
        _originRot = transform.rotation;

        // Start fully fixed
        SetKinematic(true);

        // Highlight setup
        if (highlightVisual != null)
        {
            _highlightRenderer = highlightVisual.GetComponent<Renderer>();
            if (_highlightRenderer != null)
                _highlightBlock = new MaterialPropertyBlock();

            highlightVisual.SetActive(false);
        }
    }

    private void Update()
    {
        switch (_state)
        {
            case State.Settling:
                UpdateSettling();
                break;

            case State.Returning:
                UpdateReturning();
                break;
        }

        if (highlightVisual != null && highlightVisual.activeSelf)
            UpdateHighlightPulse();
    }

    // ?? Throw ??????????????????????????????????????????????????????????????

    private void Throw(Transform ghost)
    {
        _state = State.Flying;
        SetKinematic(false);

        // Direction: ghost look direction with upward arc
        Vector3 dir = ghost.forward + Vector3.up * (throwUpward / throwForce);
        dir.Normalize();

        _rb.AddForce(dir * throwForce, ForceMode.Impulse);

        // Random spin for ragdoll feel
        _rb.AddTorque(new Vector3(
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f)) * torqueAmount, ForceMode.Impulse);
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Transition to Settling the first time we hit something after being thrown
        if (_state == State.Flying)
        {
            _state       = State.Settling;
            _settleTimer = settleTime;

            // Dampen so it doesn't keep sliding forever
            _rb.drag        = 3f;
            _rb.angularDrag = 5f;
        }
    }

    // ?? Settling ???????????????????????????????????????????????????????????

    private void UpdateSettling()
    {
        _settleTimer -= Time.deltaTime;
        if (_settleTimer <= 0f)
            StartReturning();
    }

    // ?? Returning ??????????????????????????????????????????????????????????

    private void StartReturning()
    {
        _state = State.Returning;

        // Go kinematic so physics doesn't fight the lerp
        SetKinematic(true);
    }

    private void UpdateReturning()
    {
        // Smooth spooky lerp toward origin
        transform.position = Vector3.Lerp(
            transform.position, _originPos, returnSpeed * Time.deltaTime);

        transform.rotation = Quaternion.Slerp(
            transform.rotation, _originRot, returnSpeed * Time.deltaTime);

        // Snap once close enough to avoid infinite asymptote
        float distSq = (transform.position - _originPos).sqrMagnitude;
        float angleDiff = Quaternion.Angle(transform.rotation, _originRot);

        if (distSq < returnSnapDistance * returnSnapDistance && angleDiff < 0.5f)
        {
            transform.position = _originPos;
            transform.rotation = _originRot;

            // Reset drag values for next throw
            _rb.drag        = 0f;
            _rb.angularDrag = 0.05f;

            _state = State.Fixed;
        }
    }

    // ?? Highlight (same system as LampFlicker) ?????????????????????????????

    public void SetHighlight(bool enabled)
    {
        if (highlightVisual == null) return;
        highlightVisual.SetActive(enabled && _state == State.Fixed);
    }

    private void UpdateHighlightPulse()
    {
        if (_highlightRenderer == null || _highlightBlock == null) return;

        float pulse     = (Mathf.Sin(Time.time * highlightPulseSpeed) + 1f) * 0.5f;
        float intensity = Mathf.Lerp(highlightIntensityMin, highlightIntensityMax, pulse);
        Color emissive  = highlightColor * intensity;

        _highlightRenderer.GetPropertyBlock(_highlightBlock);
        _highlightBlock.SetColor("_EmissionColor", emissive);
        _highlightBlock.SetColor("_BaseColor", highlightColor);
        _highlightBlock.SetColor("_Color",     highlightColor);
        _highlightRenderer.SetPropertyBlock(_highlightBlock);
    }

    // ?? Utility ????????????????????????????????????????????????????????????

    private void SetKinematic(bool kinematic)
    {
        _rb.isKinematic = kinematic;

        if (kinematic)
        {
            _rb.velocity        = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }

    // Show interaction range in scene view
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.3f);
        Gizmos.DrawSphere(transform.position, 0.5f);
    }
}