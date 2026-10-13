using UnityEngine;

public sealed class MonsterballDemoController : MonoBehaviour
{
    private enum DemoState
    {
        Hidden,
        Emerging,
        Ready,
        RollAttack,
        JumpRollAttack,
        Stunned,
        Recovery,
        Dying,
        Dead
    }

    [SerializeField] private DemoPlayerController m_player;
    [SerializeField] private MonsterballAnimDemoController m_animationController;
    [SerializeField, Min(0f)] private float m_deathLaunchDistance = 3f;
    [SerializeField, Min(0f)] private float m_deathLaunchHeight = 1.5f;
    [SerializeField, Min(0f), Tooltip("Player hit radius at the death explosion position. Zero disables blast hits.")]
    private float m_explosionHitRadius = 3f;
    [SerializeField, Min(0f)] private float m_wakeDistance = 5f;
    // Preserve the legacy serialized value; emergence timing now uses its own duration.
    [SerializeField, HideInInspector] private float m_unburrowPlaybackMultiplier = 1.5f;
    [SerializeField, Min(0.01f)] private float m_unburrowDuration = 0.9f;
    [SerializeField, Min(0f)] private float m_attackDistance = 6f;
    [SerializeField, Min(0f)] private float m_rollAttackDistance = 3f;
    [SerializeField, Min(0f)] private float m_jumpRollHeight = 1.5f;
    [SerializeField, Min(0f)] private float m_initialAttackDelay = 0.8f;
    [SerializeField, Min(0.01f)] private float m_rollDistance = 5f;
    [SerializeField, Min(0.01f)] private float m_rollDuration = 2.5f;
    [SerializeField, Min(0.01f)] private float m_rollCollisionRadius = 1f;
    [SerializeField] private LayerMask m_rollObstacleLayers = Physics.DefaultRaycastLayers;
    [SerializeField, Min(0f)] private float m_contactDistance = 1.2f;
    [SerializeField, Min(0f)] private float m_landingHitRadius = 1.3f;
    [SerializeField, Min(0.01f)] private float m_stunDuration = 2f;
    [SerializeField, Min(0f)] private float m_recoveryDuration = 1.2f;

    private const float m_contactRearmMargin = 0.05f;
    private const float m_rollGroundClearance = 0.05f;

    private DemoState m_state;
    private Vector3 m_deathDirection;
    private float m_deathElapsed;
    private float m_deathDuration;
    private bool m_launchOnDeath;
    private Vector3 m_startPosition;
    private Quaternion m_startRotation;
    private Vector3 m_playerStartPosition;
    private Quaternion m_playerStartRotation;
    private Vector3 m_rollStartPosition;
    private Vector3 m_jumpStartPosition;
    private Vector3 m_jumpEndPosition;
    private Vector3 m_firstRollDirection = Vector3.left;
    private Vector3 m_rollDirection = Vector3.left;
    private Vector3 m_lastRollDirection = Vector3.left;
    private float m_readyDelay;
    private float m_recoveryElapsed;
    private float m_stunElapsed;
    private float m_rollElapsed;
    private float m_rollSpeed;
    private float m_unburrowElapsed;
    private bool m_isFirstRoll;
    private readonly RaycastHit[] m_rollObstacleHits = new RaycastHit[32];
    private readonly Collider[] m_rollObstacleOverlaps = new Collider[32];
    private bool m_hasAttackedSinceEmerging;
    private bool m_initialized;

    private void Start()
    {
        if (m_player == null || m_animationController == null)
        {
            Debug.LogError("Assign Player and Animation Controller before starting the demo.", this);
            enabled = false;
            return;
        }

        m_startPosition = transform.position;
        m_startRotation = transform.rotation;
        m_playerStartPosition = m_player.transform.position;
        m_playerStartRotation = m_player.transform.rotation;

        if (!m_animationController.Initialize())
        {
            enabled = false;
            return;
        }

        m_initialized = true;
        m_player.SetAttackTarget(this);
        ResetDemo();
    }

    private void Update()
    {
        if (!m_initialized)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetDemo();
            return;
        }

        if (Input.GetKeyDown(KeyCode.G))
        {
            // Keep G as a stationary-death debug preview; J now belongs to the player attack.
            TryExplodeInPlace();
        }
        else if (Input.GetKeyDown(KeyCode.H))
        {
            // Keep H as a launched-death debug preview; K now belongs to the player attack.
            TryReceiveHit(-GetPlayerDirection());
        }

        UpdateState(Time.deltaTime);
    }

    private void OnDisable()
    {
        if (m_animationController != null)
        {
            m_animationController.StopRollWind(true);
        }
    }

    private void UpdateState(float deltaTime)
    {
        switch (m_state)
        {
            case DemoState.Hidden:
                TryWake();
                break;

            case DemoState.Ready:
                m_readyDelay = Mathf.Max(0f, m_readyDelay - deltaTime);
                Vector3 direction = !m_hasAttackedSinceEmerging
                    ? m_firstRollDirection
                    : GetPlayerDirection();
                m_animationController.FaceDirection(direction, deltaTime);

                if (m_readyDelay <= 0f)
                {
                    // The emergence commits the first roll even if the player moves out of detection range.
                    if (!m_hasAttackedSinceEmerging)
                    {
                        BeginRoll();
                    }
                    else if (IsPlayerInRange(m_attackDistance) &&
                        !IsPlayerInRange(m_contactDistance + m_contactRearmMargin))
                    {
                        if (IsPlayerInRange(m_rollAttackDistance))
                        {
                            BeginRoll();
                        }
                        else
                        {
                            BeginJumpRoll();
                        }
                    }
                }
                break;

            case DemoState.Stunned:
                m_stunElapsed += deltaTime;

                if (m_stunElapsed >= m_stunDuration)
                {
                    BeginRecovery();
                }
                break;

            case DemoState.Recovery:
                m_recoveryElapsed += deltaTime;

                if (m_recoveryElapsed >= m_recoveryDuration)
                {
                    m_animationController.CompleteRecovery();
                    m_state = DemoState.Ready;
                    m_readyDelay = 0f;
                }
                break;
        }
    }

    private bool IsPlayerInRange(float distance)
    {
        Vector3 offset = m_player.transform.position - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= distance * distance;
    }

    private void TryWake()
    {
        if (m_state != DemoState.Hidden || !IsPlayerInRange(m_wakeDistance))
        {
            return;
        }

        // Capture the trigger direction before Unburrow; do not retarget the first attack.
        m_firstRollDirection = GetPlayerDirection();
        m_animationController.SetFacing(m_firstRollDirection);
        m_state = DemoState.Emerging;
        m_unburrowElapsed = 0f;
        m_animationController.BeginEmerging();
    }

    private void LateUpdate()
    {
        if (!m_initialized)
        {
            return;
        }

        if (m_state == DemoState.Emerging)
        {
            UpdateEmerging(Time.deltaTime);
        }
        else if (m_state == DemoState.RollAttack)
        {
            UpdateRoll(Time.deltaTime);
        }
        else if (m_state == DemoState.JumpRollAttack)
        {
            UpdateJumpRoll();
        }
        else if (m_state == DemoState.Stunned)
        {
            m_animationController.SampleStun(m_stunElapsed);
        }
        else if (m_state == DemoState.Dying)
        {
            UpdateDying(Time.deltaTime);
        }
    }

    private void UpdateEmerging(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        m_unburrowElapsed += deltaTime;
        float progress = Mathf.Clamp01(m_unburrowElapsed / Mathf.Max(0.01f, m_unburrowDuration));
        m_animationController.SampleEmerging(progress);

        if (progress >= 1f)
        {
            FinishEmerging();
        }
    }

    private void FinishEmerging()
    {
        m_animationController.PlayIdle();
        m_state = DemoState.Ready;
        m_hasAttackedSinceEmerging = false;
        m_readyDelay = m_initialAttackDelay;
    }

    private Vector3 GetPlayerDirection()
    {
        Vector3 offset = m_player.transform.position - transform.position;
        offset.y = 0f;

        if (offset.sqrMagnitude <= 0.000001f)
        {
            return m_lastRollDirection;
        }

        return offset.normalized;
    }

    private void BeginRoll()
    {
        m_isFirstRoll = !m_hasAttackedSinceEmerging;
        m_state = DemoState.RollAttack;
        m_hasAttackedSinceEmerging = true;
        m_rollStartPosition = transform.position;
        m_rollElapsed = 0f;
        m_rollSpeed = m_rollDistance / Mathf.Max(0.01f, m_rollDuration);
        m_rollDirection = m_isFirstRoll ? m_firstRollDirection : GetPlayerDirection();
        m_lastRollDirection = m_rollDirection;
        m_animationController.SetFacing(m_rollDirection);
        m_animationController.PlayRoll();
    }

    private void UpdateRoll(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        float step = m_isFirstRoll
            ? deltaTime
            : Mathf.Min(deltaTime, Mathf.Max(0f, m_rollDuration - m_rollElapsed));
        m_rollElapsed += step;
        Vector3 from = transform.position;
        Vector3 nextPosition = m_rollStartPosition + m_rollDirection * (m_rollSpeed * m_rollElapsed);

        bool hitPlayer = TryGetContactPosition(from, nextPosition, out Vector3 playerContact);
        bool hitObstacle = TryGetObstacleContact(from, nextPosition,
            out Vector3 obstacleContact, out Vector3 obstacleImpact);

        // Resolve the earliest contact, so a wall blocks a player standing behind it.
        if (hitObstacle && (!hitPlayer ||
            (obstacleContact - from).sqrMagnitude <= (playerContact - from).sqrMagnitude))
        {
            transform.position = obstacleContact;
            m_animationController.PlayGroundImpact(obstacleImpact);
            BeginRecovery();
            return;
        }

        if (hitPlayer)
        {
            transform.position = playerContact;
            m_animationController.PlayRollImpact(m_player.transform.position);
            ResolvePlayerContact();
            return;
        }

        transform.position = nextPosition;

        if (!m_isFirstRoll && m_rollElapsed >= m_rollDuration)
        {
            BeginRecovery();
            return;
        }

        m_animationController.UpdateRollLoop();
    }

    private bool TryGetObstacleContact(Vector3 from, Vector3 to,
        out Vector3 contactPosition, out Vector3 impactPosition)
    {
        contactPosition = from;
        impactPosition = from;
        float radius = Mathf.Max(0.01f, m_rollCollisionRadius);
        Vector3 origin = from + Vector3.up * (radius + m_rollGroundClearance);

        // A sweep does not report colliders already overlapping its starting sphere.
        int overlapCount = Physics.OverlapSphereNonAlloc(origin, radius, m_rollObstacleOverlaps,
            m_rollObstacleLayers, QueryTriggerInteraction.Ignore);

        if (overlapCount >= m_rollObstacleOverlaps.Length)
        {
            // Stop conservatively if the fixed buffer cannot describe the crowded query.
            return true;
        }

        for (int i = 0; i < overlapCount; i++)
        {
            Collider obstacle = m_rollObstacleOverlaps[i];

            if (!IsRollObstacle(obstacle))
            {
                continue;
            }

            impactPosition = obstacle.ClosestPoint(origin);
            return true;
        }

        Vector3 travel = to - from;
        float distance = travel.magnitude;

        if (distance <= 0.000001f)
        {
            return false;
        }

        int hitCount = Physics.SphereCastNonAlloc(origin, radius, travel / distance,
            m_rollObstacleHits, distance, m_rollObstacleLayers, QueryTriggerInteraction.Ignore);

        if (hitCount >= m_rollObstacleHits.Length)
        {
            return true;
        }

        float nearestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = m_rollObstacleHits[i];

            if (!IsRollObstacle(hit.collider) || hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = hit.distance;
            impactPosition = hit.point;
        }

        if (float.IsPositiveInfinity(nearestDistance))
        {
            return false;
        }

        contactPosition = from + travel / distance * nearestDistance;
        return true;
    }

    private bool IsRollObstacle(Collider collider)
    {
        return collider != null &&
            !collider.transform.IsChildOf(transform) &&
            !collider.transform.IsChildOf(m_player.transform);
    }

    private void BeginJumpRoll()
    {
        m_state = DemoState.JumpRollAttack;
        m_hasAttackedSinceEmerging = true;
        m_jumpStartPosition = transform.position;

        Vector3 offset = m_player.transform.position - m_jumpStartPosition;
        offset.y = 0f;
        Vector3 direction = GetPlayerDirection();
        m_lastRollDirection = direction;

        // Capture the landing point once, keeping the same contact spacing as a ground roll.
        float travelDistance = Mathf.Max(0f, offset.magnitude - m_contactDistance);
        m_jumpEndPosition = m_jumpStartPosition + direction * travelDistance;
        m_animationController.SetFacing(direction);

        m_animationController.PlayJumpRoll();
    }

    private void UpdateJumpRoll()
    {
        float progress = m_animationController.GetRollProgress();
        Vector3 position = Vector3.Lerp(m_jumpStartPosition, m_jumpEndPosition, progress);

        // Use the same animation phase for travel and height so spin and flight stay synchronized.
        position.y += 4f * m_jumpRollHeight * progress * (1f - progress);
        transform.position = position;

        if (progress >= 1f)
        {
            m_animationController.PlayGroundImpact(m_jumpEndPosition);

            // Resolve the landing once against the player's current position, so a dodge can miss.
            if (IsPlayerInRange(m_landingHitRadius))
            {
                ResolvePlayerContact();
            }
            else
            {
                BeginRecovery();
            }
        }
    }

    private bool TryGetContactPosition(Vector3 from, Vector3 to, out Vector3 contactPosition)
    {
        contactPosition = from;
        Vector3 offset = from - m_player.transform.position;
        Vector3 travel = to - from;
        offset.y = 0f;
        travel.y = 0f;
        float distanceSquared = offset.sqrMagnitude - m_contactDistance * m_contactDistance;

        if (distanceSquared <= 0f)
        {
            return true;
        }

        float travelSquared = travel.sqrMagnitude;

        if (travelSquared <= 0.000001f)
        {
            return false;
        }

        float projection = Vector3.Dot(offset, travel);
        float discriminant = projection * projection - travelSquared * distanceSquared;

        if (discriminant < 0f)
        {
            return false;
        }

        float contactTime = (-projection - Mathf.Sqrt(discriminant)) / travelSquared;

        if (contactTime < 0f || contactTime > 1f)
        {
            return false;
        }

        contactPosition = Vector3.Lerp(from, to, contactTime);
        return true;
    }

    private void ResolvePlayerContact()
    {
        bool accepted = m_player.TryReceiveHit(GetPlayerDirection(), out bool parried);
        if (accepted && parried)
        {
            BeginStun();
            return;
        }

        BeginRecovery();
    }

    private void BeginStun()
    {
        m_state = DemoState.Stunned;
        m_stunElapsed = 0f;
        m_animationController.BeginStun();
    }

    private void BeginRecovery()
    {
        m_state = DemoState.Recovery;
        m_recoveryElapsed = 0f;
        m_animationController.BeginRecovery();
    }

    public bool TryExplodeInPlace()
    {
        return BeginDeath(Vector3.zero, false);
    }

    public bool TryReceiveHit(Vector3 attackDirection)
    {
        return BeginDeath(attackDirection, true);
    }

    private bool BeginDeath(Vector3 attackDirection, bool launch)
    {
        if (!m_initialized || !isActiveAndEnabled ||
            m_state == DemoState.Hidden || m_state == DemoState.Emerging ||
            m_state == DemoState.Dying || m_state == DemoState.Dead)
        {
            return false;
        }

        // Commit the state before sampling or playing effects to reject repeated debug hits.
        m_state = DemoState.Dying;
        m_deathElapsed = 0f;
        m_launchOnDeath = launch;
        m_deathDuration = m_animationController.GetDeathDuration(launch);
        attackDirection.y = 0f;
        m_deathDirection = attackDirection.sqrMagnitude > 0.000001f
            ? attackDirection.normalized
            : -GetPlayerDirection();
        m_animationController.BeginDeath(m_deathDirection, launch);
        return true;
    }

    private void UpdateDying(float deltaTime)
    {
        if (m_state != DemoState.Dying || deltaTime <= 0f)
        {
            return;
        }

        m_deathElapsed += deltaTime;
        float progress = Mathf.Clamp01(m_deathElapsed / m_deathDuration);
        Vector3 launchOffset = Vector3.zero;
        if (m_launchOnDeath)
        {
            // The launch burst happens in the air, before any return to the ground.
            float height = m_deathLaunchHeight * (2f * progress - progress * progress);
            launchOffset = m_deathDirection * (m_deathLaunchDistance * progress) + Vector3.up * height;
        }

        m_animationController.SampleDeath(progress, launchOffset);

        if (progress >= 1f)
        {
            PlayDeathExplosion();
        }
    }

    private void PlayDeathExplosion()
    {
        // Capture the moved visual/anchor position before the body is hidden.
        Vector3 explosionPosition = m_animationController.GetDeathExplosionPosition();
        // Commit Dead before presentation and hit callbacks to prevent repeated bursts.
        m_state = DemoState.Dead;
        m_animationController.PlayDeathExplosion();
        TryHitPlayerFromExplosion(explosionPosition);
    }

    private void TryHitPlayerFromExplosion(Vector3 explosionPosition)
    {
        if (m_explosionHitRadius <= 0f || m_player == null || !m_player.isActiveAndEnabled)
        {
            return;
        }

        CharacterController playerController = m_player.GetComponent<CharacterController>();

        if (playerController == null || !playerController.enabled)
        {
            return;
        }

        Vector3 offset = playerController.bounds.center - explosionPosition;

        // Use a sphere so an airborne explosion does not hit a distant player below it.
        if (offset.sqrMagnitude > m_explosionHitRadius * m_explosionHitRadius)
        {
            return;
        }

        offset.y = 0f;
        Vector3 pushDirection = offset.sqrMagnitude > 0.000001f ? offset.normalized : GetPlayerDirection();
        // Reuse the existing front guard, rear knockdown and recovery behavior.
        m_player.TryReceiveHit(pushDirection);
    }

    public void ResetDemo()
    {
        if (!m_initialized)
        {
            return;
        }

        m_state = DemoState.Hidden;
        // Clear world-space effects before moving the actor back to its starting point.
        m_animationController.ClearEffectsForReset();
        m_unburrowElapsed = 0f;
        m_hasAttackedSinceEmerging = false;
        m_deathElapsed = 0f;
        m_launchOnDeath = false;
        m_deathDuration = 0f;
        m_readyDelay = 0f;
        m_recoveryElapsed = 0f;
        m_stunElapsed = 0f;
        m_rollElapsed = 0f;
        m_rollSpeed = 0f;
        m_isFirstRoll = false;
        m_firstRollDirection = Vector3.left;
        m_rollDirection = Vector3.left;
        m_lastRollDirection = Vector3.left;
        transform.SetPositionAndRotation(m_startPosition, m_startRotation);

        m_animationController.PrepareReset();
        m_player.Teleport(m_playerStartPosition, m_playerStartRotation);
        m_animationController.ResetVisual();
    }
}
