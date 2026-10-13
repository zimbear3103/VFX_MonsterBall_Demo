using UnityEngine;
using UnityEngine.VFX;

[RequireComponent(typeof(CharacterController))]
public sealed class DemoPlayerController : MonoBehaviour
{
    private enum HitPhase
    {
        Guard,
        Falling,
        OnGround,
        GettingUp
    }

    [SerializeField, Min(0f)] private float m_moveSpeed = 4f;
    [SerializeField, Min(0f)] private float m_gravity = 20f;
    [SerializeField, Min(0f)] private float m_turnSpeed = 540f;
    [SerializeField] private Camera m_movementCamera;
    [SerializeField] private Transform m_visualPivot;
    [SerializeField] private Animator m_animator;
    [SerializeField, Min(0.01f)] private float m_hitRecoveryDuration = 0.5f;
    [SerializeField, Min(0f)] private float m_knockbackDistance = 0.8f;
    [SerializeField, Min(0.01f)] private float m_knockbackDuration = 0.18f;
    [SerializeField, Min(0f)] private float m_hitBlendDuration = 0.05f;
    [SerializeField, Range(0f, 180f)] private float m_guardHalfAngle = 90f;
    [SerializeField, Min(0f)] private float m_knockdownHoldDuration = 0.35f;
    [SerializeField, Min(0.01f)] private float m_fallPlaybackMultiplier = 2.5f;
    [SerializeField, Min(0.01f)] private float m_getUpPlaybackMultiplier = 3f;
    [SerializeField] private Transform m_hitHip;
    [SerializeField] private VisualEffect m_hitFlame;
    [SerializeField, Min(0.01f)] private float m_hitFlameDuration = 3f;

    [SerializeField, Min(0f)] private float m_attackRange = 2.5f;
    [SerializeField, Range(0f, 180f)] private float m_attackHalfAngle = 70f;

    private const float m_attackEndNormalizedTime = 0.95f;
    private const float m_attackTimeout = 2f;
    private static readonly int m_normalAttackTriggerHash = Animator.StringToHash("NormalAttack1");
    private static readonly int m_heavyAttackTriggerHash = Animator.StringToHash("NormalAttack4");
    private static readonly int m_normalAttackStateHash = Animator.StringToHash("Base Layer.Attack.Attack1");
    private static readonly int m_heavyAttackStateHash = Animator.StringToHash("Base Layer.Attack.Attack4");
    private static readonly int m_normalAttackTransitionHash = Animator.StringToHash("Base Layer.Attack.Attack1_Transition");
    private static readonly int m_heavyAttackTransitionHash = Animator.StringToHash("Base Layer.Attack.Attack4_Transition");

    private const float m_walkBlendSpeed = 1.85f;
    private const float m_groundPoseNormalizedTime = 0.9f;
    private const float m_getUpEndNormalizedTime = 0.95f;
    private static readonly int m_moveSpeedHash = Animator.StringToHash("MoveSpeed");
    private static readonly int m_walkingHash = Animator.StringToHash("Walking");
    private static readonly int m_groundedHash = Animator.StringToHash("Grounded");
    private static readonly int m_hitStateHash = Animator.StringToHash("Base Layer.Guard.GuardDamaged");
    private static readonly int m_idleStateHash = Animator.StringToHash("Base Layer.NormalMovement.NormalIdle");
    private static readonly int m_fallStateHash = Animator.StringToHash("Base Layer.Dead.Dead");
    private static readonly int m_getUpStateHash = Animator.StringToHash("Base Layer.Dead.Revive");

    private CharacterController m_characterController;
    private float m_verticalSpeed;
    private Quaternion m_initialVisualRotation = Quaternion.identity;
    private Vector3 m_knockbackDirection;
    private float m_hitElapsed;
    private bool m_isHitReacting;
    private bool m_hasHitState;
    private bool m_hasKnockdownStates;
    private HitPhase m_hitPhase;
    private bool m_hasHeldGroundPose;
    private float m_groundHoldElapsed;
    private float m_normalAnimatorSpeed = 1f;
    private Vector3 m_initialModelPosition;
    private Vector3 m_hitHipAnchor;
    private float m_hitFlameRemaining;

    private AnimationEventHandler m_animationEvents;
    private MonsterballDemoController m_attackTarget;
    private bool m_hasAttackStates;
    private bool m_isAttacking;
    private bool m_isHeavyAttack;
    private bool m_attackHitResolved;
    private bool m_hasEnteredAttack;
    private bool m_restoreAttackIdle;
    private float m_attackElapsed;

    private void Awake()
    {
        m_characterController = GetComponent<CharacterController>();
        if (m_movementCamera == null)
        {
            m_movementCamera = Camera.main;
        }

        if (m_visualPivot != null)
        {
            m_initialVisualRotation = m_visualPivot.localRotation;
        }

        if (m_animator != null)
        {
            m_animator.applyRootMotion = false;
            m_normalAnimatorSpeed = m_animator.speed;
            m_initialModelPosition = m_animator.transform.localPosition;
            m_animationEvents = m_animator.GetComponent<AnimationEventHandler>();

            if (m_animationEvents == null)
            {
                // Clip events are delivered on the Animator object, not the player root.
                m_animationEvents = m_animator.gameObject.AddComponent<AnimationEventHandler>();
            }

            if (m_hitHip == null)
            {
                m_hitHip = m_animator.transform.Find("grp_joints/jnt_root/jnt_hip");
            }
        }

        ClearHitFlame();
    }

    private void OnEnable()
    {
        if (m_restoreAttackIdle && m_animator != null && m_animator.isActiveAndEnabled)
        {
            // An inactive hierarchy cannot evaluate the reset pose until it is enabled again.
            FinishAttack(true);
        }

        if (m_animationEvents != null)
        {
            m_animationEvents.OnAnimationEvent += OnAttackAnimationEvent;
        }
    }

    private void Start()
    {
        if (m_animator == null || m_animator.runtimeAnimatorController == null)
        {
            return;
        }

        m_animator.SetBool("Alive", true);
        m_animator.SetBool("BattleMode", false);
        m_animator.SetBool("Running", false);
        m_animator.SetBool("Guarding", false);
        m_animator.SetBool(m_walkingHash, false);
        m_animator.SetFloat("MovementSpeed", 1f);
        m_animator.SetFloat("WalkSpeedMultiplier", 1f);
        m_animator.SetFloat(m_moveSpeedHash, 0f);
        m_hasAttackStates = m_animator.HasState(0, m_normalAttackStateHash) &&
            m_animator.HasState(0, m_heavyAttackStateHash) &&
            m_animator.HasState(0, m_idleStateHash);

        if (!m_hasAttackStates)
        {
            Debug.LogWarning("Player attacks require Attack.Attack1, Attack.Attack4 and NormalMovement.NormalIdle in CharacterAnimator.", this);
        }

        m_hasHitState = m_animator.HasState(0, m_hitStateHash) &&
            m_animator.HasState(0, m_idleStateHash);

        m_hasKnockdownStates = m_animator.HasState(0, m_fallStateHash) &&
            m_animator.HasState(0, m_getUpStateHash) &&
            m_animator.HasState(0, m_idleStateHash) &&
            m_hitHip != null && m_animator.transform.parent != null;

        if (!m_hasHitState || !m_hasKnockdownStates)
        {
            Debug.LogWarning("Use CharacterAnimator with Guard.GuardDamaged, Dead.Dead, Dead.Revive and NormalMovement.NormalIdle, plus the JangHyu hip bone. Missing setup falls back to knockback without its animation.", this);
        }
    }

    private void Update()
    {
        // MonsterballDemoController owns R and cancels the attack through Teleport.
        if (Input.GetKeyDown(KeyCode.R))
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.J))
        {
            TryAttack(false);
        }
        else if (Input.GetKeyDown(KeyCode.K))
        {
            TryAttack(true);
        }

        UpdateMovement(ReadMovementDirection(), Time.deltaTime);
        UpdateHitFlame(Time.deltaTime);
    }

    private void OnDisable()
    {
        if (m_animationEvents != null)
        {
            m_animationEvents.OnAnimationEvent -= OnAttackAnimationEvent;
        }

        m_restoreAttackIdle |= m_isAttacking;
        CancelAttack();

        if (m_restoreAttackIdle && m_animator != null && m_animator.isActiveAndEnabled)
        {
            FinishAttack(true);
        }

        ClearHitFlame();
    }

    private void LateUpdate()
    {
        UpdateAttackAnimation(Time.deltaTime);
        UpdateHitAnimation(Time.deltaTime);
    }

    private void UpdateMovement(Vector3 movementDirection, float deltaTime)
    {
        if (!m_characterController.enabled || deltaTime <= 0f)
        {
            return;
        }

        bool wasHitReacting = m_isHitReacting;
        bool wasAttacking = m_isAttacking;
        Vector3 planarDisplacement = wasHitReacting
            ? AdvanceHitReaction(deltaTime)
            : wasAttacking ? Vector3.zero : movementDirection * (m_moveSpeed * deltaTime);

        if (m_characterController.isGrounded && m_verticalSpeed < 0f)
        {
            m_verticalSpeed = -2f;
        }

        m_verticalSpeed -= m_gravity * deltaTime;
        Vector3 displacement = planarDisplacement + Vector3.up * (m_verticalSpeed * deltaTime);
        m_characterController.Move(displacement);

        Vector3 planarVelocity = m_characterController.velocity;
        planarVelocity.y = 0f;

        // Reactions and attacks keep their facing and must not trigger walk animation.
        UpdateVisual(wasHitReacting || wasAttacking ? Vector3.zero : planarVelocity, deltaTime);
    }

    public void SetAttackTarget(MonsterballDemoController target)
    {
        m_attackTarget = target;
    }

    public bool TryAttack(bool heavy)
    {
        if (!isActiveAndEnabled || m_characterController == null ||
            !m_characterController.enabled || m_animator == null || !m_animator.enabled ||
            !m_hasAttackStates || m_isHitReacting || m_isAttacking)
        {
            return false;
        }

        m_isAttacking = true;
        m_isHeavyAttack = heavy;
        m_attackHitResolved = false;
        m_hasEnteredAttack = false;
        m_attackElapsed = 0f;
        m_animator.speed = m_normalAnimatorSpeed;
        m_animator.SetBool(m_walkingHash, false);
        m_animator.SetFloat(m_moveSpeedHash, 0f);
        m_animator.SetBool("Guarding", false);
        m_animator.ResetTrigger("ReturnMovement");
        m_animator.ResetTrigger(m_normalAttackTriggerHash);
        m_animator.ResetTrigger(m_heavyAttackTriggerHash);
        m_animator.SetTrigger(heavy ? m_heavyAttackTriggerHash : m_normalAttackTriggerHash);
        return true;
    }

    private void OnAttackAnimationEvent(string eventName)
    {
        string hitEvent = m_isHeavyAttack ? "Attack2" : "NormalAttack2";

        if (!m_isAttacking || m_attackHitResolved || eventName != hitEvent)
        {
            return;
        }

        // Consume contact even on a miss, so a repeated marker cannot turn a whiff into a hit.
        m_attackHitResolved = true;

        if (m_attackTarget == null || !m_attackTarget.isActiveAndEnabled)
        {
            return;
        }

        Vector3 offset = m_attackTarget.transform.position - transform.position;
        offset.y = 0f;
        Vector3 facing = m_visualPivot != null ? m_visualPivot.forward : transform.forward;
        facing.y = 0f;

        if (offset.sqrMagnitude > m_attackRange * m_attackRange ||
            facing.sqrMagnitude <= 0.000001f || Vector3.Angle(facing, offset) > m_attackHalfAngle)
        {
            return;
        }

        if (m_isHeavyAttack)
        {
            m_attackTarget.TryReceiveHit(offset.sqrMagnitude > 0.000001f ? offset.normalized : facing.normalized);
        }
        else
        {
            m_attackTarget.TryExplodeInPlace();
        }
    }

    private void UpdateAttackAnimation(float deltaTime)
    {
        if (!m_isAttacking || deltaTime <= 0f)
        {
            return;
        }

        m_attackElapsed += deltaTime;
        if (m_attackElapsed >= m_attackTimeout)
        {
            FinishAttack(true);
            return;
        }

        int attackHash = m_isHeavyAttack ? m_heavyAttackStateHash : m_normalAttackStateHash;
        AnimatorStateInfo state = m_animator.GetCurrentAnimatorStateInfo(0);
        bool isInTransition = m_animator.IsInTransition(0);
        AnimatorStateInfo nextState = isInTransition ? m_animator.GetNextAnimatorStateInfo(0) : default;
        bool enteringAttack = isInTransition && nextState.fullPathHash == attackHash;

        // The shared controller's two attack recovery motions currently point at a death clip.
        bool inheritedDeathTransition = state.fullPathHash == m_normalAttackTransitionHash ||
            state.fullPathHash == m_heavyAttackTransitionHash ||
            (isInTransition && (nextState.fullPathHash == m_normalAttackTransitionHash ||
                nextState.fullPathHash == m_heavyAttackTransitionHash));

        if (inheritedDeathTransition)
        {
            FinishAttack(true);
            return;
        }

        if (state.fullPathHash == attackHash || enteringAttack)
        {
            m_hasEnteredAttack = true;

            // Clip speed is curve-driven, so completion follows normalized time, not a fixed delay.
            if (state.fullPathHash == attackHash && state.normalizedTime >= m_attackEndNormalizedTime)
            {
                FinishAttack(state.normalizedTime >= 1f);
            }
        }
        else if (m_hasEnteredAttack)
        {
            FinishAttack(true);
        }
    }

    private void FinishAttack(bool restorePoseImmediately)
    {
        CancelAttack();
        m_restoreAttackIdle = false;
        m_animator.speed = m_normalAnimatorSpeed;

        if (restorePoseImmediately)
        {
            // Replace an inherited death pose before this frame is rendered, including large time steps.
            m_animator.Play(m_idleStateHash, 0, 0f);
            m_animator.Update(0f);
        }
        else
        {
            m_animator.CrossFadeInFixedTime(m_idleStateHash, m_hitBlendDuration, 0, 0f);
        }
    }

    private void CancelAttack()
    {
        m_isAttacking = false;
        m_attackHitResolved = false;
        m_hasEnteredAttack = false;
        m_attackElapsed = 0f;

        if (m_animator != null && m_animator.runtimeAnimatorController != null)
        {
            m_animator.ResetTrigger(m_normalAttackTriggerHash);
            m_animator.ResetTrigger(m_heavyAttackTriggerHash);
        }
    }

    public bool TryReceiveHit(Vector3 pushDirection)
    {
        return TryReceiveHit(pushDirection, out _);
    }

    public bool TryReceiveHit(Vector3 pushDirection, out bool parried)
    {
        parried = false;
        if (!isActiveAndEnabled || m_characterController == null ||
            !m_characterController.enabled || m_isHitReacting)
        {
            return false;
        }

        CancelAttack();
        pushDirection.y = 0f;

        if (pushDirection.sqrMagnitude <= 0.000001f)
        {
            pushDirection = m_visualPivot != null ? -m_visualPivot.forward : Vector3.back;
            pushDirection.y = 0f;
        }

        m_knockbackDirection = pushDirection.sqrMagnitude > 0.000001f
            ? pushDirection.normalized
            : Vector3.back;
        // Decide from the current facing before playing either reaction; never turn a rear hit into a block.
        Vector3 facing = m_visualPivot != null ? m_visualPivot.forward : transform.forward;
        facing.y = 0f;
        bool isGuarded = facing.sqrMagnitude > 0.000001f &&
            Vector3.Angle(facing, -m_knockbackDirection) <= m_guardHalfAngle;

        m_hitElapsed = 0f;
        m_isHitReacting = true;
        m_hitPhase = !isGuarded && m_hasKnockdownStates ? HitPhase.Falling : HitPhase.Guard;
        m_hasHeldGroundPose = false;
        m_groundHoldElapsed = 0f;

        if (m_hitPhase == HitPhase.Falling)
        {
            m_hitHipAnchor = m_animator.transform.parent.InverseTransformPoint(m_hitHip.position);
        }

        if (m_animator != null && m_animator.runtimeAnimatorController != null)
        {
            m_animator.SetBool(m_walkingHash, false);
            m_animator.SetFloat(m_moveSpeedHash, 0f);
            m_animator.SetBool("Guarding", false);
            m_animator.ResetTrigger("ReturnMovement");

            m_animator.speed = m_normalAnimatorSpeed;

            if (m_hitPhase == HitPhase.Falling)
            {
                // Reuse the supplied fall/get-up motions while Alive stays true.
                m_animator.speed = m_normalAnimatorSpeed * m_fallPlaybackMultiplier;
                m_animator.CrossFadeInFixedTime(m_fallStateHash, m_hitBlendDuration, 0, 0f);
            }
            else if (isGuarded && m_hasHitState)
            {
                m_animator.CrossFadeInFixedTime(m_hitStateHash, m_hitBlendDuration, 0, 0f);
                // Only an accepted, visible guard reaction counts as the temporary parry.
                parried = m_animator.isActiveAndEnabled;
            }
        }

        if (!isGuarded)
        {
            PlayHitFlame();
        }

        return true;
    }

    private Vector3 AdvanceHitReaction(float deltaTime)
    {
        float duration = Mathf.Max(0.01f, m_knockbackDuration);
        float previousProgress = Mathf.Clamp01(m_hitElapsed / duration);
        m_hitElapsed += deltaTime;
        float nextProgress = Mathf.Clamp01(m_hitElapsed / duration);

        // Integrate the easing curve so knockback distance stays consistent at different frame rates.
        float previousDistance = 1f - (1f - previousProgress) * (1f - previousProgress);
        float nextDistance = 1f - (1f - nextProgress) * (1f - nextProgress);
        Vector3 displacement = m_knockbackDirection * (m_knockbackDistance * (nextDistance - previousDistance));

        if (m_hitPhase == HitPhase.Guard &&
            m_hitElapsed >= Mathf.Max(m_hitRecoveryDuration, duration))
        {
            FinishHitReaction();
        }

        return displacement;
    }

    private void UpdateHitAnimation(float deltaTime)
    {
        if (!m_isHitReacting || m_hitPhase == HitPhase.Guard || deltaTime <= 0f)
        {
            return;
        }

        AnimatorStateInfo state = m_animator.GetCurrentAnimatorStateInfo(0);

        if (m_hitPhase == HitPhase.Falling && state.fullPathHash == m_fallStateHash)
        {
            if (!m_hasHeldGroundPose && state.normalizedTime >= m_groundPoseNormalizedTime)
            {
                // Hold the supplied fall clip near its end, where the body has settled on the ground.
                m_hitPhase = HitPhase.OnGround;
                m_groundHoldElapsed = 0f;
                m_animator.Play(m_fallStateHash, 0, m_groundPoseNormalizedTime);
                m_animator.Update(0f);
                m_animator.speed = 0f;
            }
            else if (state.normalizedTime >= 1f)
            {
                m_hitPhase = HitPhase.GettingUp;
                m_animator.speed = m_normalAnimatorSpeed * m_getUpPlaybackMultiplier;
                m_animator.CrossFadeInFixedTime(m_getUpStateHash, m_hitBlendDuration, 0, 0f);
            }
        }
        else if (m_hitPhase == HitPhase.OnGround)
        {
            m_groundHoldElapsed += deltaTime;

            if (m_groundHoldElapsed >= m_knockdownHoldDuration)
            {
                m_hasHeldGroundPose = true;
                m_hitPhase = HitPhase.Falling;
                m_animator.speed = m_normalAnimatorSpeed * m_fallPlaybackMultiplier;
            }
        }
        else if (m_hitPhase == HitPhase.GettingUp &&
            ((state.fullPathHash == m_getUpStateHash && state.normalizedTime >= m_getUpEndNormalizedTime) ||
             (state.fullPathHash == m_idleStateHash && !m_animator.IsInTransition(0))))
        {
            FinishHitReaction();
        }

        if (m_isHitReacting)
        {
            // These two clips disagree on hip XZ position; keep the visual aligned with CharacterController.
            Transform model = m_animator.transform;
            Vector3 offset = m_hitHipAnchor - model.parent.InverseTransformPoint(m_hitHip.position);
            offset.y = 0f;
            model.localPosition += offset;
        }
    }

    private void FinishHitReaction()
    {
        m_isHitReacting = false;
        RestoreHitVisual();

        if (m_animator != null && m_animator.runtimeAnimatorController != null &&
            m_animator.HasState(0, m_idleStateHash))
        {
            m_animator.CrossFadeInFixedTime(m_idleStateHash, m_hitBlendDuration, 0, 0f);
        }
    }

    private void PlayHitFlame()
    {
        if (m_hitFlame == null || m_hitFlame.visualEffectAsset == null)
        {
            return;
        }

        m_hitFlame.gameObject.SetActive(true);
        m_hitFlame.enabled = true;

        // Refresh the duration without clearing an already burning effect.
        if (m_hitFlameRemaining <= 0f)
        {
            m_hitFlame.SendEvent("loop");
        }

        m_hitFlameRemaining = Mathf.Max(0.01f, m_hitFlameDuration);
    }

    private void UpdateHitFlame(float deltaTime)
    {
        if (m_hitFlameRemaining <= 0f || deltaTime <= 0f)
        {
            return;
        }

        m_hitFlameRemaining = Mathf.Max(0f, m_hitFlameRemaining - deltaTime);

        if (m_hitFlameRemaining <= 0f && m_hitFlame != null)
        {
            // Stop emission and let the existing particles finish their lifetime.
            m_hitFlame.SendEvent("stop");
        }
    }

    private void ClearHitFlame()
    {
        m_hitFlameRemaining = 0f;

        if (m_hitFlame == null)
        {
            return;
        }

        // Reinit must not emit flame before a hit or after a reset.
        m_hitFlame.initialEventName = "stop";
        m_hitFlame.Reinit();
        m_hitFlame.SendEvent("stop");
    }

    private void RestoreHitVisual()
    {
        if (m_animator != null)
        {
            m_animator.speed = m_normalAnimatorSpeed;
            m_animator.transform.localPosition = m_initialModelPosition;
        }
    }

    public void Teleport(Vector3 position, Quaternion rotation)
    {
        CancelAttack();
        m_restoreAttackIdle = false;
        ClearHitFlame();
        bool wasEnabled = m_characterController.enabled;
        m_characterController.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        m_verticalSpeed = 0f;
        m_hitElapsed = 0f;
        m_isHitReacting = false;
        m_knockbackDirection = Vector3.zero;
        m_hitPhase = HitPhase.Guard;
        m_hasHeldGroundPose = false;
        m_groundHoldElapsed = 0f;
        m_hitHipAnchor = Vector3.zero;
        RestoreHitVisual();

        if (m_visualPivot != null)
        {
            m_visualPivot.localRotation = m_initialVisualRotation;
        }

        m_characterController.enabled = wasEnabled;

        if (m_animator != null && m_animator.runtimeAnimatorController != null)
        {
            m_animator.SetBool(m_walkingHash, false);
            m_animator.SetFloat(m_moveSpeedHash, 0f);
            m_animator.SetBool("Guarding", false);
            m_animator.ResetTrigger("ReturnMovement");

            if (m_animator.HasState(0, m_idleStateHash))
            {
                m_animator.Play(m_idleStateHash, 0, 0f);
                m_animator.Update(0f);
            }
        }
    }

    private void UpdateVisual(Vector3 planarVelocity, float deltaTime)
    {
        if (m_animator != null && m_animator.runtimeAnimatorController != null)
        {
            float actualMoveSpeed = planarVelocity.magnitude;
            bool isMoving = actualMoveSpeed > 0.1f;

            m_animator.SetBool(m_walkingHash, isMoving);
            m_animator.SetBool(m_groundedHash, m_characterController.isGrounded);

            // The supplied NormalWalk blend tree switches to BoostForward above 1.85.
            float blendSpeed = isMoving ? m_walkBlendSpeed : 0f;
            m_animator.SetFloat(m_moveSpeedHash, blendSpeed, 0.1f, deltaTime);
        }

        if (m_visualPivot == null || planarVelocity.sqrMagnitude <= 0.000001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(planarVelocity, Vector3.up);
        m_visualPivot.rotation = Quaternion.RotateTowards(
            m_visualPivot.rotation,
            targetRotation,
            m_turnSpeed * deltaTime);
    }

    private Vector3 ReadMovementDirection()
    {
        bool moveLeft = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
        bool moveRight = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
        bool moveForward = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        bool moveBackward = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);

        float horizontalInput = (moveRight ? 1f : 0f) - (moveLeft ? 1f : 0f);
        float forwardInput = (moveForward ? 1f : 0f) - (moveBackward ? 1f : 0f);
        if (m_movementCamera == null)
        {
            m_movementCamera = Camera.main;
        }

        Vector3 cameraForward = m_movementCamera != null
            ? m_movementCamera.transform.forward
            : Vector3.forward;
        return GetMovementDirection(horizontalInput, forwardInput, cameraForward);
    }

    private static Vector3 GetMovementDirection(float horizontalInput, float forwardInput, Vector3 cameraForward)
    {
        cameraForward.y = 0f;
        cameraForward = cameraForward.sqrMagnitude > 0.000001f
            ? cameraForward.normalized
            : Vector3.forward;
        Vector3 cameraRight = Vector3.Cross(Vector3.up, cameraForward);
        return (cameraRight * horizontalInput + cameraForward * forwardInput).normalized;
    }
}
