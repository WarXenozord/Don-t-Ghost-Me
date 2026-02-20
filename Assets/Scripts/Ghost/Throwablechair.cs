using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to any chair (or other throwable prop).
/// States:
///   Fixed     - kinematic, sitting at original position/rotation
///   Flying    - rigidbody active, thrown in ghost's look direction
///   Settling  - on the floor, waiting 5 s before returning
///   Returning - smoothly lerps back to origin position/rotation
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class Throwablechair : MonoBehaviour, IInteractable
{
    private static readonly Dictionary<string, Throwablechair> Registry = new Dictionary<string, Throwablechair>();
    private static MatchTransport _transport;
    private static NakamaConnection _conn;
    private static bool _transportBound;

    [Header("Interaction")]
    [SerializeField] private float energyCost = 30f;
    [SerializeField] private string chairId;

    [Header("Throw Settings")]
    [SerializeField] private float throwForce = 12f;
    [SerializeField] private float throwUpward = 3f;
    [SerializeField] private float torqueAmount = 8f;

    [Header("Return Settings")]
    [SerializeField] private float settleTime = 5f;
    [SerializeField] private float returnSpeed = 1.5f;
    [SerializeField] private float returnSnapDistance = 0.05f;

    [Header("Highlight")]
    [SerializeField] private GameObject highlightVisual;
    [SerializeField] private float highlightPulseSpeed = 3f;
    [SerializeField] private float highlightIntensityMin = 2f;
    [SerializeField] private float highlightIntensityMax = 8f;
    [SerializeField] private Color highlightColor = new Color(1f, 0.4f, 0f);

    public float EnergyCost => energyCost;
    public bool IsBusy => _state != State.Fixed;

    private enum State { Fixed, Flying, Settling, Returning }

    private Rigidbody _rb;
    private State _state = State.Fixed;
    private float _settleTimer;

    private Vector3 _originPos;
    private Quaternion _originRot;

    private Renderer _highlightRenderer;
    private MaterialPropertyBlock _highlightBlock;

    public void Interact(Transform ghostTransform)
    {
        if (_state != State.Fixed || ghostTransform == null) return;

        SetHighlight(false);

        var baseDir = ghostTransform.forward;
        var torque = new Vector3(
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f)) * torqueAmount;

        ThrowWithParams(baseDir, throwForce, throwUpward, torque, snapToOriginStart: false);
        BroadcastThrow(baseDir, torque);
    }

    private void Awake()
    {
        ResolveTransport();
        EnsureTransportBound();

        if (string.IsNullOrEmpty(chairId))
        {
            chairId = BuildChairIdFromPosition();
        }
        Registry[chairId] = this;

        _rb = GetComponent<Rigidbody>();
        _originPos = transform.position;
        _originRot = transform.rotation;

        SetKinematic(true);

        if (highlightVisual != null)
        {
            _highlightRenderer = highlightVisual.GetComponent<Renderer>();
            if (_highlightRenderer != null)
            {
                _highlightBlock = new MaterialPropertyBlock();
            }
            highlightVisual.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (!string.IsNullOrEmpty(chairId) && Registry.TryGetValue(chairId, out var current) && current == this)
        {
            Registry.Remove(chairId);
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
        {
            UpdateHighlightPulse();
        }
    }

    private void ThrowWithParams(Vector3 direction, float force, float upward, Vector3 torqueImpulse, bool snapToOriginStart)
    {
        if (_rb == null) return;

        if (snapToOriginStart)
        {
            transform.position = _originPos;
            transform.rotation = _originRot;
        }

        _state = State.Flying;
        SetKinematic(false);

        _rb.drag = 0f;
        _rb.angularDrag = 0.05f;

        var dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        var launch = (dir + Vector3.up * (upward / Mathf.Max(0.001f, force))).normalized;

        _rb.AddForce(launch * force, ForceMode.Impulse);
        _rb.AddTorque(torqueImpulse, ForceMode.Impulse);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_state == State.Flying)
        {
            _state = State.Settling;
            _settleTimer = settleTime;
            _rb.drag = 3f;
            _rb.angularDrag = 5f;
        }
    }

    private void UpdateSettling()
    {
        _settleTimer -= Time.deltaTime;
        if (_settleTimer <= 0f)
        {
            StartReturning();
        }
    }

    private void StartReturning()
    {
        _state = State.Returning;
        SetKinematic(true);
    }

    private void UpdateReturning()
    {
        transform.position = Vector3.Lerp(transform.position, _originPos, returnSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, _originRot, returnSpeed * Time.deltaTime);

        var distSq = (transform.position - _originPos).sqrMagnitude;
        var angleDiff = Quaternion.Angle(transform.rotation, _originRot);

        if (distSq < returnSnapDistance * returnSnapDistance && angleDiff < 0.5f)
        {
            transform.position = _originPos;
            transform.rotation = _originRot;
            _rb.drag = 0f;
            _rb.angularDrag = 0.05f;
            _state = State.Fixed;
        }
    }

    public void SetHighlight(bool enabled)
    {
        if (highlightVisual == null) return;
        highlightVisual.SetActive(enabled && _state == State.Fixed);
    }

    private void UpdateHighlightPulse()
    {
        if (_highlightRenderer == null || _highlightBlock == null) return;

        var pulse = (Mathf.Sin(Time.time * highlightPulseSpeed) + 1f) * 0.5f;
        var intensity = Mathf.Lerp(highlightIntensityMin, highlightIntensityMax, pulse);
        var emissive = highlightColor * intensity;

        _highlightRenderer.GetPropertyBlock(_highlightBlock);
        _highlightBlock.SetColor("_EmissionColor", emissive);
        _highlightBlock.SetColor("_BaseColor", highlightColor);
        _highlightBlock.SetColor("_Color", highlightColor);
        _highlightRenderer.SetPropertyBlock(_highlightBlock);
    }

    private void SetKinematic(bool kinematic)
    {
        _rb.isKinematic = kinematic;
        if (kinematic)
        {
            _rb.velocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }

    private void BroadcastThrow(Vector3 direction, Vector3 torque)
    {
        ResolveTransport();
        if (_transport == null || _conn == null || _conn.Match == null) return;
        if (string.IsNullOrEmpty(chairId)) return;

        _transport.BroadcastChairThrow(new MatchTransport.ChairThrowMsg
        {
            chairId = chairId,
            startPosX = _originPos.x,
            startPosY = _originPos.y,
            startPosZ = _originPos.z,
            startYaw = _originRot.eulerAngles.y,
            dirX = direction.x,
            dirY = direction.y,
            dirZ = direction.z,
            force = throwForce,
            upward = throwUpward,
            torqueX = torque.x,
            torqueY = torque.y,
            torqueZ = torque.z
        });
    }

    private static void OnChairThrowReceived(MatchTransport.ChairThrowMsg msg)
    {
        if (msg == null || string.IsNullOrEmpty(msg.chairId)) return;
        ResolveTransport();

        if (_conn != null &&
            !string.IsNullOrEmpty(msg.senderUserId) &&
            !string.IsNullOrEmpty(_conn.SelfUserId) &&
            msg.senderUserId == _conn.SelfUserId)
        {
            return;
        }

        if (!Registry.TryGetValue(msg.chairId, out var chair) || chair == null) return;

        chair._originPos = new Vector3(msg.startPosX, msg.startPosY, msg.startPosZ);
        chair._originRot = Quaternion.Euler(0f, msg.startYaw, 0f);
        chair.SetHighlight(false);

        chair.ThrowWithParams(
            new Vector3(msg.dirX, msg.dirY, msg.dirZ),
            Mathf.Max(0.001f, msg.force),
            msg.upward,
            new Vector3(msg.torqueX, msg.torqueY, msg.torqueZ),
            snapToOriginStart: true);
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

        _transport.OnChairThrow += OnChairThrowReceived;
        _transportBound = true;
    }

    private string BuildChairIdFromPosition()
    {
        var p = transform.position;
        var x = Mathf.RoundToInt(p.x * 10f);
        var y = Mathf.RoundToInt(p.y * 10f);
        var z = Mathf.RoundToInt(p.z * 10f);
        return "chair:" + x + ":" + y + ":" + z;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.3f);
        Gizmos.DrawSphere(transform.position, 0.5f);
    }
}
