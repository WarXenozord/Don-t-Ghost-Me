using System.Collections.Generic;
using UnityEngine;

public class EnemySimpleAI : SoundAgroListener
{
    private class TrackedMedium
    {
        public string userId;
        public Transform targetTransform;
        public MediumController mediumController;
    }

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
    [Min(0f)] public float revisitPenalty = 1f;

    [Header("Navigation")]
    [Min(0.05f)] public float repathInterval = 0.35f;
    [Min(0f)] public float repathTargetDelta = 1f;
    [Min(0f)] public float stuckTimeout = 1.25f;
    [Min(0f)] public float progressDistance = 0.2f;
    [Range(0f, 1f)] public float doorAxisSteer = 0.45f;
    [Min(0.1f)] public float attackNoGainTimeout = 1f;
    [Min(0f)] public float attackGainEpsilon = 0.1f;
    [Min(0f)] public float attackRerouteMinPlayerDistance = 2.5f;

    [Header("Vision")]
    [Min(0f)] public float sightDistance = 14f;
    [Range(1f, 180f)] public float sightAngle = 75f;
    [Min(0f)] public float loseTargetDistance2D = 18f;
    public bool requireSameRoomForVision = true;
    public bool requireLineOfSight = false;
    public LayerMask sightBlockMask = ~0;

    [Header("Target Refresh")]
    [Min(0.05f)] public float mediumRefreshInterval = 0.5f;

    [Header("Authority")]
    public bool hostAuthoritativeMechanics = true;

    [Header("Contact")]
    [Min(0f)] public float contactDistance = 1.2f;
    [Min(0f)] public float contactRearmDistance = 1.8f;
    [Min(0f)] public float firstTouchMinTeleportDistance = 8f;
    [Min(0f)] public float firstTouchMaxTeleportDistance = 14f;
    [Min(0f)] public float firstTouchMinDistanceFromMedium = 6f;
    public AudioSource contactAudioSource;
    public AudioClip firstTouchClip;
    public AudioClip secondTouchClip;

    private CharacterController _controller;
    private ProceduralBuildingGenerator _generator;
    private readonly List<TrackedMedium> _knownMediums = new List<TrackedMedium>();
    private readonly List<PlayerSpawnManager.SpawnedPlayerInfo> _spawnedPlayersBuffer = new List<PlayerSpawnManager.SpawnedPlayerInfo>();
    private readonly List<Vector3> _routeNodes = new List<Vector3>();
    private readonly List<Vector3> _roomCenterNodes = new List<Vector3>();
    private readonly Dictionary<int, int> _nodeVisits = new Dictionary<int, int>();
    // Shared across all enemy instances: second hit should kill regardless of which enemy hit first.
    private static readonly Dictionary<string, int> SharedTouchCountByMedium = new Dictionary<string, int>();
    private static int _sharedTouchInitId = int.MinValue;
    private readonly HashSet<string> _activeTouchMediums = new HashSet<string>();

    private Vector3 _patrolTarget;
    private Vector3 _investigateTarget;
    private TrackedMedium _attackTarget;
    private float _nextPatrolResampleAt;
    private float _nextMediumRefreshAt;

    private Vector3 _routeDestination;
    private int _routeIndex;
    private float _nextRepathAt;
    private Vector3 _lastProgressPos;
    private float _lastProgressAt;
    private bool _patrolInitialized;
    private float _attackNoGainStartedAt;
    private float _attackNoGainStartDist;
    private bool _attackRerouteToDoorActive;
    private readonly List<Vector3> _attackRerouteNodes = new List<Vector3>();
    private int _attackRerouteIndex;
    private EnemyNetIdentity _netIdentity;
    private GhostSpawner _ghostSpawner;
    private bool _isAuthoritativeInstance = true;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _generator = FindObjectOfType<ProceduralBuildingGenerator>();
        _netIdentity = GetComponent<EnemyNetIdentity>();
        _ghostSpawner = GhostSpawner.Instance != null ? GhostSpawner.Instance : FindObjectOfType<GhostSpawner>();

        
    }

    void Start()
    {
        _patrolInitialized = false;

        // WebGL-safe startup guards: scene refs/children may not be ready in the same frame.
        GameObject marker = null;
        if (transform != null && transform.childCount > 0)
        {
            var child = transform.GetChild(0);
            if (child != null) marker = child.gameObject;
        }

        var floorRenderer = FindObjectOfType<FloorplanRenderer>();
        if (floorRenderer != null && marker != null && marker.activeInHierarchy)
        {
            try
            {
                floorRenderer.SetEnemyMarkers(marker);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[EnemySimpleAI] Failed to register enemy marker at Start: " + ex.Message);
            }
        }

        ResetMovementProgress();
    }

    void Update()
    {
        if (!_isAuthoritativeInstance)
        {
            // Non-authoritative clients should only be pose/state-driven by host snapshots.
            return;
        }

        if (_generator == null) _generator = FindObjectOfType<ProceduralBuildingGenerator>();
        RefreshKnownMediumsIfNeeded();

        var visibleTarget = FindVisibleMedium();
        if (visibleTarget != null)
        {
            var enteringAttack = state != EnemyState.Attack || _attackTarget != visibleTarget;
            _attackTarget = visibleTarget;
            state = EnemyState.Attack;
            if (enteringAttack) ResetAttackTracking();
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

        HandleMediumContact();
    }

    protected override void OnSoundAgroHeard(SoundAgroEvent evt, float perceivedIntensity)
    {
        if (!CanApplyGameplayEffects()) return;
        _investigateTarget = evt.worldPosition;
        if (state != EnemyState.Attack) state = EnemyState.Investigate;
    }

    public void SetAuthoritativeInstance(bool isAuthoritative)
    {
        _isAuthoritativeInstance = isAuthoritative;
    }

    public bool CanApplyGameplayEffects()
    {
        return !hostAuthoritativeMechanics || _isAuthoritativeInstance;
    }

    public void ApplyHostState(int rawState)
    {
        var clamped = Mathf.Clamp(rawState, (int)EnemyState.Patrol, (int)EnemyState.Attack);
        state = (EnemyState)clamped;
    }

    private void TickPatrol()
    {
        if (!_patrolInitialized)
        {
            PickNextPatrolTarget(force: true);
            _nextRepathAt = 0f;
            _patrolInitialized = true;
        }

        if (IsStuck())
        {
            TeleportToRandomPatrolNode();
            PickNextPatrolTarget(force: true);
            _nextRepathAt = 0f;
            ResetMovementProgress();
            return;
        }

        if (Time.time >= _nextPatrolResampleAt || Reached2D(_patrolTarget, waypointReachDistance))
        {
            PickNextPatrolTarget(force: true);
        }
        NavigateTo(_patrolTarget, patrolSpeed);
    }

    private void TickInvestigate()
    {
        var reached = NavigateTo(_investigateTarget, investigateSpeed);
        if (!reached) return;

        _attackTarget = null;
        state = EnemyState.Patrol;
        PickNextPatrolTarget(force: true);
    }

    private void TickAttack()
    {
        if (_attackTarget == null || _attackTarget.targetTransform == null)
        {
            _attackRerouteToDoorActive = false;
            state = EnemyState.Patrol;
            _patrolInitialized = false;
            PickNextPatrolTarget(force: true);
            return;
        }

        var targetPos = _attackTarget.targetTransform.position;
        var distNow = Distance2D(transform.position, targetPos);

        if (_attackRerouteToDoorActive)
        {
            if (_attackRerouteIndex >= _attackRerouteNodes.Count)
            {
                _attackRerouteToDoorActive = false;
                ResetAttackTracking();
                return;
            }

            var rerouteTarget = _attackRerouteNodes[_attackRerouteIndex];
            var reachedDoor = MoveTowards(rerouteTarget, attackSpeed);
            TrackProgress();
            if (reachedDoor || Reached2D(rerouteTarget, waypointReachDistance))
            {
                _attackRerouteIndex++;
                if (_attackRerouteIndex >= _attackRerouteNodes.Count)
                {
                    _attackRerouteToDoorActive = false;
                    ResetAttackTracking();
                }
            }
        }
        else
        {
            MoveTowards(targetPos, attackSpeed);
            TrackProgress();

            if (distNow < _attackNoGainStartDist - attackGainEpsilon)
            {
                _attackNoGainStartDist = distNow;
                _attackNoGainStartedAt = Time.time;
            }
            else if (Time.time - _attackNoGainStartedAt >= attackNoGainTimeout)
            {
                if (distNow >= attackRerouteMinPlayerDistance &&
                    _generator != null &&
                    _generator.TryGetNearestDoorHop(transform.position, out var nearDoor, out var nextRoomCenter, preferredFloor: 0))
                {
                    _attackRerouteNodes.Clear();
                    _attackRerouteNodes.Add(nearDoor);
                    _attackRerouteNodes.Add(nextRoomCenter);
                    _attackRerouteIndex = 0;
                    _attackRerouteToDoorActive = true;
                }
                _attackNoGainStartDist = distNow;
                _attackNoGainStartedAt = Time.time;
            }
        }

        if (IsStuck())
        {
            _attackTarget = null;
            _attackRerouteToDoorActive = false;
            state = EnemyState.Patrol;
            _patrolInitialized = false;
            PickNextPatrolTarget(force: true);
            ResetMovementProgress();
            return;
        }

        var stillVisible = CanSeeMedium(_attackTarget);
        var far2D = Distance2D(transform.position, targetPos) >= loseTargetDistance2D;
        if (!stillVisible && far2D)
        {
            _attackTarget = null;
            _attackRerouteToDoorActive = false;
            state = EnemyState.Patrol;
            _patrolInitialized = false;
            PickNextPatrolTarget(force: true);
        }
    }

    private bool NavigateTo(Vector3 destination, float speed)
    {
        RebuildRouteIfNeeded(destination);

        while (_routeIndex < _routeNodes.Count && Reached2D(_routeNodes[_routeIndex], waypointReachDistance))
        {
            RegisterNodeVisit(_routeNodes[_routeIndex]);
            _routeIndex++;
        }

        if (_routeIndex < _routeNodes.Count)
        {
            var stepTarget = _routeNodes[_routeIndex];
            if (MoveTowards(stepTarget, speed))
            {
                RegisterNodeVisit(stepTarget);
                _routeIndex++;
            }
            TrackProgress();

            if (IsStuck())
            {
                _routeIndex = _routeNodes.Count;
                _nextRepathAt = 0f;
            }
            return false;
        }

        var reached = MoveTowards(destination, speed);
        TrackProgress();
        return reached;
    }

    private void RebuildRouteIfNeeded(Vector3 destination)
    {
        var hasActiveRoute = _routeIndex < _routeNodes.Count;
        var needsByTime = !hasActiveRoute && Time.time >= _nextRepathAt;
        var needsByTargetShift = Distance2D(_routeDestination, destination) >= repathTargetDelta;
        var needsByExhausted = !hasActiveRoute;
        if (!needsByTime && !needsByTargetShift && !needsByExhausted) return;

        _routeDestination = destination;
        _nextRepathAt = Time.time + repathInterval;
        _routeIndex = 0;
        _routeNodes.Clear();

        if (_generator == null) return;
        _generator.TryBuildExplicitRoomDoorGraphPath(transform.position, destination, _routeNodes, preferredFloor: 0);
    }

    private bool MoveTowards(Vector3 worldTarget, float speed)
    {
        var pos = transform.position;
        var to = new Vector3(worldTarget.x - pos.x, 0f, worldTarget.z - pos.z);
        var len = to.magnitude;
        if (len <= 0.0001f) return true;

        var dir = to / len;
        if (_generator != null && _generator.TryGetDoorPassAxisAtPosition(worldTarget, out var passAxis, maxDistance: 1.5f, preferredFloor: 0))
        {
            var axis = new Vector3(passAxis.x, 0f, passAxis.z);
            if (axis.sqrMagnitude > 0.0001f)
            {
                axis.Normalize();
                if (Vector3.Dot(axis, dir) < 0f) axis = -axis;
                dir = Vector3.Lerp(dir, axis, Mathf.Clamp01(doorAxisSteer)).normalized;
            }
        }

        var desiredRot = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, turnSpeed * Time.deltaTime);

        var step = dir * speed * Time.deltaTime;
        if (step.sqrMagnitude > to.sqrMagnitude) step = to;

        var next = pos + step;

        if (_controller != null && _controller.enabled) _controller.Move(next - pos);
        else transform.position = next;

        return Distance2D(transform.position, worldTarget) <= waypointReachDistance;
    }

    private void PickNextPatrolTarget(bool force)
    {
        if (!force && Time.time < _nextPatrolResampleAt) return;

        var origin = transform.position;
        if (_generator != null)
        {
            _roomCenterNodes.Clear();
            _generator.CollectRoomCenterNodes(_roomCenterNodes, preferredFloor: 0);

            var totalWeight = 0f;
            for (var i = 0; i < _roomCenterNodes.Count; i++)
            {
                var p = _roomCenterNodes[i];
                if (Distance2D(origin, p) > patrolSampleRadius) continue;
                _nodeVisits.TryGetValue(NodeKey(p), out var visits);
                totalWeight += Mathf.Max(0.001f, 1f / (1f + revisitPenalty * visits));
            }

            if (totalWeight > 0f)
            {
                var pick = Random.value * totalWeight;
                var acc = 0f;
                for (var i = 0; i < _roomCenterNodes.Count; i++)
                {
                    var p = _roomCenterNodes[i];
                    if (Distance2D(origin, p) > patrolSampleRadius) continue;
                    _nodeVisits.TryGetValue(NodeKey(p), out var visits);
                    var w = Mathf.Max(0.001f, 1f / (1f + revisitPenalty * visits));
                    acc += w;
                    if (acc >= pick)
                    {
                        _patrolTarget = p;
                        _nextPatrolResampleAt = Time.time + patrolResampleInterval;
                        return;
                    }
                }
            }
        }

        var fallback = origin + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));
        _patrolTarget = fallback;
        _nextPatrolResampleAt = Time.time + patrolResampleInterval;
    }

    private void RefreshKnownMediumsIfNeeded()
    {
        if (Time.time < _nextMediumRefreshAt) return;
        _nextMediumRefreshAt = Time.time + mediumRefreshInterval;

        _knownMediums.Clear();
        var playerSpawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        if (playerSpawner != null)
        {
            playerSpawner.FillSpawnedPlayers(_spawnedPlayersBuffer);
            for (var i = 0; i < _spawnedPlayersBuffer.Count; i++)
            {
                var info = _spawnedPlayersBuffer[i];
                var go = info.root;
                if (go == null || !go.activeInHierarchy) continue;

                // Ignore ghost players.
                var ghost = go.GetComponentInChildren<GhostController>(true);
                if (ghost != null) continue;

                var medium = go.GetComponentInChildren<MediumController>(true);
                var targetTransform = medium != null ? medium.transform : go.transform;
                if (targetTransform == null) continue;

                _knownMediums.Add(new TrackedMedium
                {
                    userId = info.userId,
                    targetTransform = targetTransform,
                    mediumController = medium
                });
            }
        }

        // Fallback: discover enabled hierarchy medium controllers directly.
        if (_knownMediums.Count == 0)
        {
            var all = FindObjectsOfType<MediumController>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var m = all[i];
                if (m == null || !m.gameObject.activeInHierarchy) continue;
                _knownMediums.Add(new TrackedMedium
                {
                    userId = string.Empty,
                    targetTransform = m.transform,
                    mediumController = m
                });
            }
        }
    }

    private TrackedMedium FindVisibleMedium()
    {
        TrackedMedium best = null;
        var bestDist = float.MaxValue;
        for (var i = 0; i < _knownMediums.Count; i++)
        {
            var m = _knownMediums[i];
            if (m == null || !CanSeeMedium(m)) continue;

            var d = Distance2D(transform.position, m.targetTransform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = m;
            }
        }
        return best;
    }

    private bool CanSeeMedium(TrackedMedium medium)
    {
        if (medium == null || medium.targetTransform == null) return false;

        if (requireSameRoomForVision && _generator != null)
        {
            var selfRoom = _generator.GetContainingRoomIndex(transform.position, preferredFloor: 0);
            var targetRoom = _generator.GetContainingRoomIndex(medium.targetTransform.position, preferredFloor: 0);
            if (selfRoom >= 0 && targetRoom >= 0 && selfRoom != targetRoom) return false;
        }

        var origin = transform.position + Vector3.up * 1.4f;
        var targetPos = medium.targetTransform.position + Vector3.up * 1.2f;
        var toTarget = targetPos - origin;
        var planar = new Vector3(toTarget.x, 0f, toTarget.z);
        var dist = planar.magnitude;
        if (dist > sightDistance || dist <= 0.0001f) return false;

        var dir = planar / dist;
        var dot = Vector3.Dot(transform.forward, dir);
        var angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
        if (angle > sightAngle * 0.5f) return false;

        if (!requireLineOfSight) return true;
        return !Physics.Linecast(origin, targetPos, out _, sightBlockMask, QueryTriggerInteraction.Ignore);
    }

    private void TrackProgress()
    {
        var moved = Distance2D(transform.position, _lastProgressPos);
        if (moved < progressDistance) return;
        _lastProgressPos = transform.position;
        _lastProgressAt = Time.time;
    }

    private bool IsStuck()
    {
        return Time.time - _lastProgressAt >= stuckTimeout;
    }

    private void RegisterNodeVisit(Vector3 pos)
    {
        var key = NodeKey(pos);
        if (_nodeVisits.TryGetValue(key, out var count)) _nodeVisits[key] = count + 1;
        else _nodeVisits[key] = 1;
    }

    private void ResetAttackTracking()
    {
        if (_attackTarget != null)
        {
            _attackNoGainStartDist = _attackTarget.targetTransform != null
                ? Distance2D(transform.position, _attackTarget.targetTransform.position)
                : 0f;
        }
        else
        {
            _attackNoGainStartDist = 0f;
        }
        _attackNoGainStartedAt = Time.time;
        _attackRerouteToDoorActive = false;
        _attackRerouteNodes.Clear();
        _attackRerouteIndex = 0;
        ResetMovementProgress();
    }

    private void ResetMovementProgress()
    {
        _lastProgressPos = transform.position;
        _lastProgressAt = Time.time;
    }

    private void TeleportToRandomPatrolNode()
    {
        if (_generator != null)
        {
            _roomCenterNodes.Clear();
            _generator.CollectRoomCenterNodes(_roomCenterNodes, preferredFloor: 0);
            if (_roomCenterNodes.Count > 0)
            {
                var idx = Random.Range(0, _roomCenterNodes.Count);
                var target = _roomCenterNodes[idx];
                if (_controller != null && _controller.enabled)
                {
                    _controller.enabled = false;
                    transform.position = target;
                    _controller.enabled = true;
                }
                else
                {
                    transform.position = target;
                }
                RegisterNodeVisit(target);
                return;
            }
        }

        var fallback = transform.position + new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
        if (_controller != null && _controller.enabled)
        {
            _controller.enabled = false;
            transform.position = fallback;
            _controller.enabled = true;
        }
        else
        {
            transform.position = fallback;
        }
    }

    private static int NodeKey(Vector3 p)
    {
        var x = Mathf.RoundToInt(p.x * 5f);
        var y = Mathf.RoundToInt(p.y * 5f);
        var z = Mathf.RoundToInt(p.z * 5f);
        unchecked { return (x * 73856093) ^ (y * 19349663) ^ (z * 83492791); }
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

    private static string BuildTrackedMediumKey(TrackedMedium medium)
    {
        if (medium == null) return string.Empty;
        if (!string.IsNullOrEmpty(medium.userId)) return medium.userId;
        if (medium.targetTransform != null) return "tf:" + medium.targetTransform.GetInstanceID();
        return "unknown";
    }

    private void HandleMediumContact()
    {
        if (!CanApplyGameplayEffects()) return;

        var rearmDist = Mathf.Max(contactDistance, contactRearmDistance);
        for (var i = 0; i < _knownMediums.Count; i++)
        {
            var medium = _knownMediums[i];
            if (medium == null || medium.targetTransform == null) continue;

            var mediumId = BuildTrackedMediumKey(medium);
            var dist = Distance2D(transform.position, medium.targetTransform.position);

            if (dist <= contactDistance)
            {
                if (_activeTouchMediums.Contains(mediumId)) continue;
                _activeTouchMediums.Add(mediumId);
                OnMediumTouched(medium, mediumId);
            }
            else if (dist >= rearmDist)
            {
                _activeTouchMediums.Remove(mediumId);
            }
        }
    }

    private void OnMediumTouched(TrackedMedium medium, string mediumId)
    {
        RefreshSharedTouchCounterScope();
        SharedTouchCountByMedium.TryGetValue(mediumId, out var count);
        count++;
        SharedTouchCountByMedium[mediumId] = count;
        if (medium.mediumController != null)
        {
            medium.mediumController.EnterHalfLife();
        }

        if (count == 1)
        {
            TriggerSyncedTouchFx(1);
            TeleportAfterFirstTouch(medium.targetTransform.position);
            return;
        }

        if (count == 2)
        {
            TriggerSyncedTouchFx(2);
            SpawnGhostOnSecondTouch(medium);
            TeleportAfterFirstTouch(medium.targetTransform.position);
        }
    }

    private static void RefreshSharedTouchCounterScope()
    {
        var context = MatchContext.Instance;
        var initId = (context != null && context.lastInit != null) ? context.lastInit.initId : int.MinValue;
        if (_sharedTouchInitId == initId) return;

        SharedTouchCountByMedium.Clear();
        _sharedTouchInitId = initId;
    }

    private void SpawnGhostOnSecondTouch(TrackedMedium medium)
    {
        if (!CanApplyGameplayEffects() || medium == null || medium.targetTransform == null) return;

        if (_ghostSpawner == null)
        {
            _ghostSpawner = GhostSpawner.Instance != null ? GhostSpawner.Instance : FindObjectOfType<GhostSpawner>();
        }
        if (_ghostSpawner == null) return;

        var playerSpawner = PlayerSpawnManager.Instance != null ? PlayerSpawnManager.Instance : FindObjectOfType<PlayerSpawnManager>();
        if (playerSpawner == null) return;

        var victimUserId = medium.userId;
        if (string.IsNullOrEmpty(victimUserId))
        {
            var candidateGo = medium.mediumController != null ? medium.mediumController.gameObject : medium.targetTransform.gameObject;
            if (!playerSpawner.TryGetUserIdByObject(candidateGo, out victimUserId)) return;
        }

        var pos = medium.targetTransform.position;
        var yaw = medium.targetTransform.eulerAngles.y;
        _ghostSpawner.HostKillMediumAndSpawnGhost(victimUserId, pos, yaw);
    }

    public void PlaySyncedTouchFx(int fxId)
    {
        AudioClip clip = null;
        if (fxId == 1) clip = firstTouchClip;
        else if (fxId == 2) clip = secondTouchClip;
        if (clip == null) return;

        // Prefer configured source, but keep a robust fallback so FX is never silent.
        if (contactAudioSource == null)
        {
            contactAudioSource = GetComponent<AudioSource>();
        }

        if (contactAudioSource != null)
        {
            AudioSource.PlayClipAtPoint(clip, transform.position, 1f);
            return;
        }

        AudioSource.PlayClipAtPoint(clip, transform.position, 1f);
    }

    private void TriggerSyncedTouchFx(int fxId)
    {
        // Always play immediately on the authority that triggered the contact.
        PlaySyncedTouchFx(fxId);

        if (_netIdentity == null) _netIdentity = GetComponent<EnemyNetIdentity>();
        if (_netIdentity == null || string.IsNullOrEmpty(_netIdentity.spawnId))
        {
            return;
        }

        var mgr = EnemySpawnManager.Instance != null ? EnemySpawnManager.Instance : FindObjectOfType<EnemySpawnManager>();
        if (mgr == null) return;
        mgr.HostBroadcastEnemyFx(_netIdentity.spawnId, fxId);
    }

    private void TeleportAfterFirstTouch(Vector3 mediumPos)
    {
        var best = transform.position;
        var found = false;

        if (_generator != null)
        {
            _roomCenterNodes.Clear();
            _generator.CollectRoomCenterNodes(_roomCenterNodes, preferredFloor: 0);

            var eligible = new List<int>();
            var farthestIdx = -1;
            var farthestDist = float.MinValue;
            var minT = Mathf.Min(firstTouchMinTeleportDistance, firstTouchMaxTeleportDistance);
            var maxT = Mathf.Max(firstTouchMinTeleportDistance, firstTouchMaxTeleportDistance);

            for (var i = 0; i < _roomCenterNodes.Count; i++)
            {
                var node = _roomCenterNodes[i];
                var d = Distance2D(node, mediumPos);
                if (d > farthestDist)
                {
                    farthestDist = d;
                    farthestIdx = i;
                }

                if (d < firstTouchMinDistanceFromMedium) continue;
                if (d < minT || d > maxT) continue;
                eligible.Add(i);
            }

            if (eligible.Count > 0)
            {
                var picked = eligible[Random.Range(0, eligible.Count)];
                best = _roomCenterNodes[picked];
                found = true;
            }
            else if (farthestIdx >= 0)
            {
                best = _roomCenterNodes[farthestIdx];
                found = true;
            }
        }

        if (!found)
        {
            var away = (transform.position - mediumPos);
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = Random.insideUnitSphere;
            away.y = 0f;
            away.Normalize();
            best = transform.position + away * Mathf.Max(firstTouchMinTeleportDistance, firstTouchMinDistanceFromMedium);
        }

        if (_controller != null && _controller.enabled)
        {
            _controller.enabled = false;
            transform.position = best;
            _controller.enabled = true;
        }
        else
        {
            transform.position = best;
        }

        // After first-touch teleport, always reset to patrol.
        _attackTarget = null;
        _attackRerouteToDoorActive = false;
        _attackRerouteNodes.Clear();
        _attackRerouteIndex = 0;
        state = EnemyState.Patrol;
        _patrolInitialized = false;

        _routeNodes.Clear();
        _routeIndex = 0;
        _nextRepathAt = 0f;
        ResetMovementProgress();

        if (CanApplyGameplayEffects())
        {
            if (_netIdentity == null) _netIdentity = GetComponent<EnemyNetIdentity>();
            if (_netIdentity != null && !string.IsNullOrEmpty(_netIdentity.spawnId))
            {
                var mgr = EnemySpawnManager.Instance != null ? EnemySpawnManager.Instance : FindObjectOfType<EnemySpawnManager>();
                if (mgr != null)
                {
                    mgr.HostBroadcastTeleport(_netIdentity.spawnId, transform.position, transform.eulerAngles.y, reason: 1);
                }
            }
        }
    }
}
