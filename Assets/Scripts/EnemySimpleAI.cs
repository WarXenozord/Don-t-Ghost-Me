using System.Collections.Generic;
using UnityEngine;

public class EnemySimpleAI : SoundAgroListener
{
    public enum EnemyState
    {
        Patrol = 1,
        Investigate = 2,
        Attack = 3
    }

    [Header("State")]
    public EnemyState state = EnemyState.Patrol;

    [Header("Speeds")]
    [Min(0f)] public float patrolSpeed = 1.8f;
    [Min(0f)] public float investigateSpeed = 2.8f;
    [Min(0f)] public float attackSpeed = 4.2f;
    [Min(0f)] public float turnSpeed = 7f;

    [Header("Patrol")]
    [Min(0f)] public float waypointReachDistance = 0.5f;
    [Min(0f)] public float patrolSampleRadius = 12f;
    [Min(0f)] public float patrolResampleInterval = 8f;

    [Header("Vision")]
    [Min(0f)] public float sightDistance = 14f;
    [Range(1f, 180f)] public float sightAngle = 75f;
    [Min(0f)] public float loseTargetDistance2D = 18f;
    public bool requireLineOfSight = false;
    public LayerMask sightBlockMask = ~0;

    [Header("Target Refresh")]
    [Min(0.05f)] public float mediumRefreshInterval = 0.5f;

    private CharacterController _controller;
    private ProceduralBuildingGenerator _generator;
    private readonly List<MediumController> _knownMediums = new List<MediumController>();

    private Vector3 _patrolTarget;
    private Vector3 _investigateTarget;
    private MediumController _attackTarget;
    private float _nextPatrolResampleAt;
    private float _nextMediumRefreshAt;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _generator = FindObjectOfType<ProceduralBuildingGenerator>();
    }

    void Start()
    {
        PickNextPatrolTarget(force: true);
    }

    void Update()
    {
        RefreshKnownMediumsIfNeeded();

        var visibleTarget = FindVisibleMedium();
        if (visibleTarget != null)
        {
            _attackTarget = visibleTarget;
            state = EnemyState.Attack;
        }

        switch (state)
        {
            case EnemyState.Patrol:
                TickPatrol();
                break;
            case EnemyState.Investigate:
                TickInvestigate();
                break;
            case EnemyState.Attack:
                TickAttack();
                break;
        }
    }

    protected override void OnSoundAgroHeard(SoundAgroEvent evt, float perceivedIntensity)
    {
        _investigateTarget = evt.worldPosition;

        if (state != EnemyState.Attack)
        {
            state = EnemyState.Investigate;
        }
    }

    private void TickPatrol()
    {
        if (Time.time >= _nextPatrolResampleAt || Reached2D(_patrolTarget, waypointReachDistance))
        {
            PickNextPatrolTarget(force: true);
        }

        MoveTowards(_patrolTarget, patrolSpeed);
    }

    private void TickInvestigate()
    {
        MoveTowards(_investigateTarget, investigateSpeed);

        if (Reached2D(_investigateTarget, waypointReachDistance))
        {
            _attackTarget = null;
            state = EnemyState.Patrol;
            PickNextPatrolTarget(force: true);
        }
    }

    private void TickAttack()
    {
        if (_attackTarget == null)
        {
            state = EnemyState.Patrol;
            PickNextPatrolTarget(force: true);
            return;
        }

        var targetPos = _attackTarget.transform.position;
        MoveTowards(targetPos, attackSpeed);

        var stillVisible = CanSeeMedium(_attackTarget);
        var far2D = Distance2D(transform.position, targetPos) >= loseTargetDistance2D;
        if (!stillVisible && far2D)
        {
            _attackTarget = null;
            state = EnemyState.Patrol;
            PickNextPatrolTarget(force: true);
        }
    }

    private void MoveTowards(Vector3 worldTarget, float speed)
    {
        var pos = transform.position;
        var to = new Vector3(worldTarget.x - pos.x, 0f, worldTarget.z - pos.z);
        var len = to.magnitude;
        if (len <= 0.0001f) return;

        var dir = to / len;
        var desiredRot = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, turnSpeed * Time.deltaTime);

        var step = dir * speed * Time.deltaTime;
        if (step.sqrMagnitude > to.sqrMagnitude) step = to;

        var next = pos + step;
        if (_generator != null && _generator.TryGetSafeSpawnPoint(next, out var safeNext, preferredFloor: 0))
        {
            next = safeNext;
        }

        if (_controller != null && _controller.enabled)
        {
            _controller.Move(next - pos);
        }
        else
        {
            transform.position = next;
        }
    }

    private void PickNextPatrolTarget(bool force)
    {
        if (!force && Time.time < _nextPatrolResampleAt) return;

        var origin = transform.position;
        var candidate = origin;
        var found = false;

        for (var i = 0; i < 12; i++)
        {
            var rnd = Random.insideUnitCircle * patrolSampleRadius;
            var requested = new Vector3(origin.x + rnd.x, origin.y, origin.z + rnd.y);
            if (_generator != null && _generator.TryGetSafeSpawnPoint(requested, out var safe, preferredFloor: 0))
            {
                candidate = safe;
                found = true;
                break;
            }
        }

        if (!found)
        {
            candidate = origin + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));
        }

        _patrolTarget = candidate;
        _nextPatrolResampleAt = Time.time + patrolResampleInterval;
    }

    private void RefreshKnownMediumsIfNeeded()
    {
        if (Time.time < _nextMediumRefreshAt) return;
        _nextMediumRefreshAt = Time.time + mediumRefreshInterval;

        _knownMediums.Clear();
        var all = FindObjectsOfType<MediumController>(true);
        for (var i = 0; i < all.Length; i++)
        {
            var m = all[i];
            if (m == null) continue;
            if (!m.gameObject.activeInHierarchy) continue;
            _knownMediums.Add(m);
        }
    }

    private MediumController FindVisibleMedium()
    {
        MediumController best = null;
        var bestDist = float.MaxValue;

        for (var i = 0; i < _knownMediums.Count; i++)
        {
            var m = _knownMediums[i];
            if (m == null) continue;
            if (!CanSeeMedium(m)) continue;

            var d = Distance2D(transform.position, m.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = m;
            }
        }

        return best;
    }

    private bool CanSeeMedium(MediumController medium)
    {
        if (medium == null) return false;

        var origin = transform.position + Vector3.up * 1.4f;
        var targetPos = medium.transform.position + Vector3.up * 1.2f;
        var toTarget = targetPos - origin;
        var planar = new Vector3(toTarget.x, 0f, toTarget.z);
        var dist = planar.magnitude;
        if (dist > sightDistance || dist <= 0.0001f) return false;

        var dir = planar / dist;
        var dot = Vector3.Dot(transform.forward, dir);
        var angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
        if (angle > sightAngle * 0.5f) return false;

        if (!requireLineOfSight) return true;

        if (Physics.Linecast(origin, targetPos, out _, sightBlockMask, QueryTriggerInteraction.Ignore))
        {
            return false;
        }
        return true;
    }

    private bool Reached2D(Vector3 target, float threshold)
    {
        return Distance2D(transform.position, target) <= threshold;
    }

    private static float Distance2D(Vector3 a, Vector3 b)
    {
        var dx = a.x - b.x;
        var dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
