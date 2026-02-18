using UnityEngine;

public class LobbyCameraMover : MonoBehaviour
{
    [Header("Targets")]
    public Transform cameraTransform;
    public Transform lobbyViewPoint;
    public Transform roomViewPoint;

    [Header("Movement")]
    [Min(0.01f)] public float smoothTime = 0.2f;
    [Min(0.01f)] public float maxSpeed = 60f;
    [Min(0.01f)] public float rotationLerpSpeed = 12f;
    [Min(0f)] public float stopDistance = 0.02f;
    [Min(0f)] public float stopAngle = 0.2f;

    private Transform _target;
    private Vector3 _velocity;

    void Awake()
    {
        if (!cameraTransform) cameraTransform = transform;
        _target = lobbyViewPoint ? lobbyViewPoint : cameraTransform;
        SnapToTarget(_target);
    }

    void Update()
    {
        if (!cameraTransform || !_target) return;

        var targetPos = _target.position;
        var nextPos = Vector3.SmoothDamp(
            cameraTransform.position,
            targetPos,
            ref _velocity,
            smoothTime,
            maxSpeed,
            Time.deltaTime
        );
        cameraTransform.position = nextPos;

        var targetRot = _target.rotation;
        cameraTransform.rotation = Quaternion.Slerp(
            cameraTransform.rotation,
            targetRot,
            1f - Mathf.Exp(-rotationLerpSpeed * Time.deltaTime)
        );

        var dist = Vector3.Distance(cameraTransform.position, targetPos);
        var ang = Quaternion.Angle(cameraTransform.rotation, targetRot);
        if (dist <= stopDistance && ang <= stopAngle)
        {
            SnapToTarget(_target);
        }
    }

    public void OnJoinedOrStartedMatch()
    {
        if (roomViewPoint) _target = roomViewPoint;
    }

    public void OnLeftMatch()
    {
        if (lobbyViewPoint) _target = lobbyViewPoint;
    }

    public void SnapToLobby()
    {
        if (!lobbyViewPoint) return;
        _target = lobbyViewPoint;
        SnapToTarget(_target);
    }

    public void SnapToRoom()
    {
        if (!roomViewPoint) return;
        _target = roomViewPoint;
        SnapToTarget(_target);
    }

    private void SnapToTarget(Transform target)
    {
        if (!cameraTransform || !target) return;
        cameraTransform.position = target.position;
        cameraTransform.rotation = target.rotation;
        _velocity = Vector3.zero;
    }
}
