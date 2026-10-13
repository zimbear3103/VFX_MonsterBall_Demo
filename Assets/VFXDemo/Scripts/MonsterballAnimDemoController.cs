using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.VFX.Utility;

public sealed class MonsterballAnimDemoController : MonoBehaviour
{
    #region Inspector

    [SerializeField] private Transform m_visualRoot;
    [SerializeField] private Animator m_animator;
    [SerializeField, Min(0f)] private float m_hiddenDepth = 5f;
    [SerializeField, Min(0f)] private float m_unburrowHeight = 3f;
    [SerializeField, Tooltip("Time: 0..1. Negative values scale Hidden Depth; positive values scale Unburrow Height.")]
    private AnimationCurve m_unburrowHeightCurve = CreateUnburrowHeightCurve();
    [SerializeField, Min(0.01f)] private float m_rollPlaybackMultiplier = 0.4f;
    [SerializeField, Min(0.01f)] private float m_jumpRollPlaybackMultiplier = 0.6f;
    [SerializeField, Min(0f)] private float m_recoveryBlendDuration = 0.15f;
    [SerializeField, Min(0.01f)] private float m_stunStartDuration = 0.35f;

    [SerializeField] private GameObject m_flameRoot;
    [SerializeField] private ParticleSystem m_groundBurst;
    [SerializeField] private ParticleSystem m_rollDust;
    [SerializeField] private ParticleSystem m_rollImpact;
    [SerializeField] private VisualEffect m_rollWind;
    [SerializeField] private ParticleSystem m_deathExplosion;
    [SerializeField] private Transform m_deathExplosionAnchor;
    [SerializeField, Min(0.01f)] private float m_hitExplosionDelay = 0.45f;
    [SerializeField, Min(0.01f)] private float m_inPlaceExplosionDelay = 1.2f;
    [SerializeField, Range(0f, 1f)] private float m_groundBurstNormalizedTime = 0.08f / 0.9f;
    [SerializeField, Range(0f, 1f)] private float m_unburrowLandingNormalizedTime = 0.62f / 0.9f;

    #endregion

    #region Cached visual state

    private const float m_rollEndNormalizedTime = 1f / 3f;
    private const float m_visualTurnSpeed = 540f;
    private const float m_groggyLoopClipDuration = 1f;

    private static readonly int m_spawnStateHash = Animator.StringToHash("Base Layer.Spawn");
    private static readonly int m_idleStateHash = Animator.StringToHash("Base Layer.BattleMovement.BattleIdle");
    private static readonly int m_rollStateHash = Animator.StringToHash("Base Layer.Backup.NormalAttack.NormalAttack1");
    private static readonly int m_deathStateHash = Animator.StringToHash("Base Layer.Dead.Dead");
    private static readonly int m_groggyStartStateHash = Animator.StringToHash("Base Layer.Groggy.GroggyStart");
    private static readonly int m_groggyLoopStateHash = Animator.StringToHash("Base Layer.Groggy.GroggyLoop");

    private Renderer[] m_bodyRenderers;
    private Vector3 m_visualStartPosition;
    private Quaternion m_visualStartRotation;
    private Vector3 m_visualStartScale;
    private Transform m_deformationSystem;
    private Vector3 m_deformationStartScale;
    private Transform m_flameScaleTarget;
    private Vector3 m_flameScaleTargetStartScale;
    private Transform m_deathDebrisRoot;
    private bool m_deathDebrisStartActive;
    private Vector3 m_deathStartPosition;
    private float m_normalAnimatorSpeed;
    private VisualEffect[] m_flameEffects;
    private ParticleSystem[] m_flameParticles;
    private Vector3 m_rollImpactStartLocalPosition;
    private Vector3 m_deathExplosionStartLocalPosition;
    private bool m_groundBurstPlayed;
    private bool m_unburrowLandingPlayed;
    private bool m_flameStarted;
    private bool m_stunRecoveryActive;
    private bool m_deathAnimationActive;
    private bool m_initializationAttempted;
    private bool m_initialized;

    #endregion

    #region Initialization

    public bool Initialize()
    {
        if (m_initializationAttempted)
        {
            return m_initialized;
        }

        // Baselines belong to the original scene pose, never a later reset or death pose.
        m_initializationAttempted = true;
        if (m_visualRoot == null || m_animator == null)
        {
            Debug.LogError("Assign Visual Root and Animator before starting the demo.", this);
            return false;
        }

        m_rollImpactStartLocalPosition = m_rollImpact != null
            ? m_rollImpact.transform.localPosition
            : Vector3.zero;
        m_deathExplosionStartLocalPosition = m_deathExplosion != null
            ? m_deathExplosion.transform.localPosition
            : Vector3.zero;
        m_flameEffects = m_flameRoot != null
            ? m_flameRoot.GetComponentsInChildren<VisualEffect>(true)
            : new VisualEffect[0];
        m_flameParticles = m_flameRoot != null
            ? m_flameRoot.GetComponentsInChildren<ParticleSystem>(true)
            : new ParticleSystem[0];

        m_visualStartPosition = m_visualRoot.localPosition;
        m_visualStartRotation = m_visualRoot.localRotation;
        m_visualStartScale = m_visualRoot.localScale;
        m_deformationSystem = m_visualRoot.Find("Group/DeformationSystem");
        m_deformationStartScale = m_deformationSystem != null
            ? m_deformationSystem.localScale
            : Vector3.one;
        m_flameScaleTarget = m_deformationSystem != null
            ? m_deformationSystem.Find("Root_M/VFXHolder")
            : null;
        m_flameScaleTargetStartScale = m_flameScaleTarget != null
            ? m_flameScaleTarget.localScale
            : Vector3.one;
        m_deathDebrisRoot = m_visualRoot.Find("group1");
        m_deathDebrisStartActive = m_deathDebrisRoot != null && m_deathDebrisRoot.gameObject.activeSelf;
        m_bodyRenderers = m_visualRoot.GetComponentsInChildren<Renderer>(true);
        m_normalAnimatorSpeed = m_animator.speed;

        m_visualRoot.gameObject.SetActive(true);
        m_animator.enabled = true;
        m_animator.applyRootMotion = false;
        m_animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        m_animator.Rebind();

        if (!m_animator.HasState(0, m_spawnStateHash) ||
            !m_animator.HasState(0, m_idleStateHash) ||
            !m_animator.HasState(0, m_rollStateHash) ||
            !m_animator.HasState(0, m_deathStateHash) ||
            !m_animator.HasState(0, m_groggyStartStateHash) ||
            !m_animator.HasState(0, m_groggyLoopStateHash))
        {
            Debug.LogError("Use the MonsterBall demo override with Spawn, BattleIdle, NormalAttack1, Dead, GroggyStart and GroggyLoop states.", this);
            return false;
        }

        if (m_unburrowHeightCurve == null || m_unburrowHeightCurve.length == 0)
        {
            m_unburrowHeightCurve = CreateUnburrowHeightCurve();
        }

        if (m_rollWind != null)
        {
            // This graph uses custom events, so reinitialization must not start emission.
            m_rollWind.initialEventName = "stop";
        }

        m_initialized = true;
        return true;
    }

    #endregion

    #region Facing and emergence

    public void FaceDirection(Vector3 direction, float deltaTime)
    {
        Quaternion facing = GetRollFacing(direction);
        m_visualRoot.rotation = Quaternion.RotateTowards(
            m_visualRoot.rotation, facing, m_visualTurnSpeed * deltaTime);
    }

    public void SetFacing(Vector3 direction)
    {
        m_visualRoot.rotation = GetRollFacing(direction);
    }

    private static Quaternion GetRollFacing(Vector3 direction)
    {
        // The Guide1 spin copy rolls towards local +X, so map that axis onto the world travel direction.
        float yaw = Mathf.Atan2(-direction.z, direction.x) * Mathf.Rad2Deg;
        return Quaternion.Euler(0f, yaw, 0f);
    }

    public void BeginEmerging()
    {
        m_animator.enabled = true;
        // Sample the pose with the emergence clock, independently of Animator transitions.
        m_animator.speed = 0f;
        m_animator.Play(m_spawnStateHash, 0, 0f);
        m_animator.Update(0f);
        RestoreFlameScale();
    }

    public void SampleEmerging(float progress)
    {
        m_animator.Play(m_spawnStateHash, 0, Mathf.Min(progress, 0.9999f));
        m_animator.Update(0f);
        RestoreFlameScale();
        float curveHeight = m_unburrowHeightCurve.Evaluate(progress);
        float height = curveHeight < 0f
            ? curveHeight * m_hiddenDepth
            : curveHeight * m_unburrowHeight;

        // Move only the visual. Ground effects and gameplay XZ stay at the emergence point.
        m_visualRoot.localPosition = m_visualStartPosition + Vector3.up * height;
        TryPlayGroundBurst(progress);

        if (!m_unburrowLandingPlayed && progress >= m_unburrowLandingNormalizedTime)
        {
            m_unburrowLandingPlayed = true;
            PlayGroundImpact(transform.position);
        }
    }

    private static AnimationCurve CreateUnburrowHeightCurve()
    {
        return new AnimationCurve(
            new Keyframe(0f, -1f, 0f, 0f),
            new Keyframe(0.08f / 0.9f, -0.3f, 6f, 6f),
            // Broken tangents keep launch velocity continuous across the 5 m / 3 m scales.
            new Keyframe(0.12f / 0.9f, 0f, 6f, 10f),
            new Keyframe(0.25f / 0.9f, 1f, 0f, 0f),
            new Keyframe(0.62f / 0.9f, 0f, -4.865f, 2.625f),
            new Keyframe(0.7f / 0.9f, 0.35f / 3f, 0f, 0f),
            new Keyframe(1f, 0f, 0f, 0f));
    }

    #endregion

    #region Idle, roll and recovery

    public void PlayIdle()
    {
        m_animator.speed = m_normalAnimatorSpeed;
        m_animator.Play(m_idleStateHash, 0, 0f);
        m_animator.Update(0f);
        RestoreFlameScale();
        m_visualRoot.localPosition = m_visualStartPosition;
        TryPlayGroundBurst(1f);
    }

    public void PlayRoll()
    {
        m_animator.speed = m_normalAnimatorSpeed * m_rollPlaybackMultiplier;
        m_animator.Play(m_rollStateHash, 0, 0f);
        m_animator.Update(0f);
        RestoreFlameScale();

        if (m_rollDust != null)
        {
            m_rollDust.Play(true);
        }

        PlayRollWind();
    }

    public void PlayJumpRoll()
    {
        if (m_rollDust != null)
        {
            // Ground dust must stop emitting before the actor leaves the floor.
            m_rollDust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        PlayGroundImpact(transform.position);
        m_animator.speed = m_normalAnimatorSpeed * m_jumpRollPlaybackMultiplier;
        m_animator.Play(m_rollStateHash, 0, 0f);
        m_animator.Update(0f);
        RestoreFlameScale();
        PlayRollWind();
    }

    public float GetRollProgress()
    {
        AnimatorStateInfo stateInfo = m_animator.GetCurrentAnimatorStateInfo(0);

        // A large frame step can advance the supplied controller into its exit state.
        return stateInfo.fullPathHash == m_rollStateHash
            ? Mathf.Clamp01(stateInfo.normalizedTime / m_rollEndNormalizedTime)
            : 1f;
    }

    public void UpdateRollLoop()
    {
        AnimatorStateInfo stateInfo = m_animator.GetCurrentAnimatorStateInfo(0);

        if (stateInfo.fullPathHash != m_rollStateHash ||
            stateInfo.normalizedTime >= m_rollEndNormalizedTime)
        {
            // The first third is one complete spin; reuse it without playing the clip's exit.
            float normalizedTime = stateInfo.fullPathHash == m_rollStateHash
                ? Mathf.Repeat(stateInfo.normalizedTime, m_rollEndNormalizedTime)
                : 0f;
            m_animator.Play(m_rollStateHash, 0, normalizedTime);
            m_animator.Update(0f);
            RestoreFlameScale();
        }
    }

    public void BeginRecovery()
    {
        StopRollWind(false);
        if (m_rollDust != null)
        {
            // Let existing particles finish their lifetime after the attack.
            m_rollDust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        m_animator.SetBool("Groggy", false);
        m_animator.SetBool("Stun", false);
        m_animator.speed = m_normalAnimatorSpeed;
        m_animator.CrossFadeInFixedTime(m_idleStateHash, m_recoveryBlendDuration, 0, 0f);

        if (m_stunRecoveryActive)
        {
            // Resume the authored flame while recovering, before another attack can begin.
            StartFlame();
        }
    }

    public void CompleteRecovery()
    {
        if (!m_stunRecoveryActive)
        {
            return;
        }

        // Recovery may end before its blend completes, so settle Idle before releasing scale ownership.
        m_animator.Play(m_idleStateHash, 0, 0f);
        m_animator.Update(0f);
        RestoreDeformationScale();
        RestoreFlameScale();
        m_stunRecoveryActive = false;
        StartFlame();
    }

    #endregion

    #region Stun

    public void BeginStun()
    {
        StopRollWind(true);
        if (m_rollDust != null)
        {
            m_rollDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        StopFlameForStun();
        m_stunRecoveryActive = true;
        m_animator.enabled = true;
        m_animator.speed = 0f;
        m_animator.ResetTrigger("ReturnMovement");
        m_animator.SetBool("Alive", true);
        m_animator.SetBool("Groggy", true);
        m_animator.SetBool("Stun", false);
        SampleStun(0f);
    }

    public void SampleStun(float elapsed)
    {
        float startDuration = Mathf.Max(0.01f, m_stunStartDuration);
        elapsed = Mathf.Max(0f, elapsed);
        if (elapsed <= startDuration)
        {
            m_animator.Play(m_groggyStartStateHash, 0, Mathf.Min(elapsed / startDuration, 0.9999f));
        }
        else
        {
            float loopTime = Mathf.Repeat((elapsed - startDuration) / m_groggyLoopClipDuration, 1f);
            m_animator.Play(m_groggyLoopStateHash, 0, loopTime);
        }

        m_animator.Update(0f);
        // Imported Groggy clips bind scale 100; keep the scene baseline without resetting the pose.
        RestoreDeformationScale();
        RestoreFlameScale();
    }

    private void LateUpdate()
    {
        if (m_stunRecoveryActive)
        {
            // Idle blending can also reapply the imported deformation scale.
            RestoreDeformationScale();
        }

        if (!m_deathAnimationActive)
        {
            // Living states can write the holder's cached death pose through Animator defaults.
            RestoreFlameScale();
        }
    }

    private void RestoreDeformationScale()
    {
        if (m_deformationSystem != null)
        {
            m_deformationSystem.localScale = m_deformationStartScale;
        }
    }

    #endregion

    #region Death and visibility

    public float GetDeathDuration(bool launch)
    {
        return Mathf.Max(0.01f, launch ? m_hitExplosionDelay : m_inPlaceExplosionDelay);
    }

    public void BeginDeath(Vector3 deathDirection, bool launch)
    {
        // Release the stun scale override before sampling the authored death scale curve.
        m_stunRecoveryActive = false;
        m_deathAnimationActive = true;
        m_animator.SetBool("Groggy", false);
        m_animator.SetBool("Stun", false);
        StopRollWind(true);
        if (m_rollDust != null)
        {
            m_rollDust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        m_deathStartPosition = m_visualRoot.position;

        // The demo death clip is bound relative to Group, where the Animator lives.
        m_animator.enabled = true;
        m_animator.speed = 0f;
        m_animator.ResetTrigger("ReturnMovement");
        m_animator.SetBool("Alive", true);
        if (launch)
        {
            SetFacing(-deathDirection);
        }

        // Preserve a hit at the jump apex instead of snapping the visual to the floor.
        SampleDeath(0f, Vector3.zero);
    }

    public void SampleDeath(float progress, Vector3 launchOffset)
    {
        progress = Mathf.Clamp01(progress);
        // The death clip owns scale; the existing clock controls pose, launch and explosion.
        m_animator.Play(m_deathStateHash, 0, Mathf.Min(progress, 0.9999f));
        m_animator.Update(0f);
        m_visualRoot.position = m_deathStartPosition + launchOffset;
    }

    public Vector3 GetBodyCenter()
    {
        bool hasBounds = false;
        Bounds bounds = new Bounds();
        foreach (Renderer bodyRenderer in m_bodyRenderers)
        {
            if (bodyRenderer == null || !bodyRenderer.enabled ||
                !bodyRenderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = bodyRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(bodyRenderer.bounds);
            }
        }

        return hasBounds ? bounds.center : m_visualRoot.position;
    }

    public void SetVisualActive(bool active)
    {
        m_visualRoot.gameObject.SetActive(active);
    }

    #endregion

    #region Effects

    private void StartFlame()
    {
        if (m_flameStarted || m_flameRoot == null)
        {
            return;
        }

        m_flameStarted = true;
        m_flameRoot.SetActive(true);
        RefreshFlameBindings();

        // Reinit sends the configured initial event (loop for the supplied flame).
        foreach (VisualEffect effect in m_flameEffects)
        {
            effect.Reinit();
        }
    }

    private void RefreshFlameBindings()
    {
        foreach (VisualEffect effect in m_flameEffects)
        {
            VFXPropertyBinder binder = effect.GetComponent<VFXPropertyBinder>();
            if (binder == null || !binder.enabled)
            {
                continue;
            }

            // The flame can be inactive during reset, so refresh components before its next simulation.
            foreach (VFXBinderBase binding in effect.GetComponents<VFXBinderBase>())
            {
                if (binding.enabled && binding.IsValid(effect))
                {
                    binding.UpdateBinding(effect);
                }
            }
        }
    }

    private void RestoreFlameScale()
    {
        if (m_flameScaleTarget != null)
        {
            m_flameScaleTarget.localScale = m_flameScaleTargetStartScale;
        }
    }

    private void StopFlameForStun()
    {
        m_flameStarted = false;
        foreach (VisualEffect effect in m_flameEffects)
        {
            effect.Reinit();
            effect.Stop();
        }

        foreach (ParticleSystem particles in m_flameParticles)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (m_flameRoot != null)
        {
            m_flameRoot.SetActive(false);
        }
    }

    private void TryPlayGroundBurst(float progress)
    {
        if (m_groundBurstPlayed || progress < m_groundBurstNormalizedTime)
        {
            return;
        }

        // Latch before playing so skipped or repeated animation updates cannot duplicate the cue.
        m_groundBurstPlayed = true;
        if (m_groundBurst != null)
        {
            m_groundBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            m_groundBurst.Play(true);
        }
        StartFlame();
    }

    public void PlayGroundImpact(Vector3 position)
    {
        if (m_rollImpact == null)
        {
            return;
        }

        position.y = transform.TransformPoint(m_rollImpactStartLocalPosition).y;
        m_rollImpact.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        m_rollImpact.transform.position = position;
        m_rollImpact.Play(true);
    }

    public void PlayRollImpact(Vector3 playerPosition)
    {
        if (m_rollImpact == null)
        {
            return;
        }

        Vector3 impactPosition = (transform.position + playerPosition) * 0.5f;
        // Keep the puff at the authored floor height, between the contacting actors.
        impactPosition.y = transform.TransformPoint(m_rollImpactStartLocalPosition).y;
        m_rollImpact.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        m_rollImpact.transform.position = impactPosition;
        m_rollImpact.Play(true);
    }

    private void PlayRollWind()
    {
        if (m_rollWind == null)
        {
            return;
        }

        // The supplied graph starts continuous ribbons with its custom loop event.
        m_rollWind.SendEvent("loop");
    }

    public void StopRollWind(bool clearParticles)
    {
        if (m_rollWind == null)
        {
            return;
        }

        if (clearParticles)
        {
            // Clear world-space ribbons before a reset or deactivation can move the actor.
            m_rollWind.Reinit();
        }

        // Stop spawning while allowing existing ribbons to finish during Recovery.
        m_rollWind.SendEvent("stop");
    }

    public Vector3 GetDeathExplosionPosition()
    {
        return m_deathExplosionAnchor != null
            ? m_deathExplosionAnchor.position
            : GetBodyCenter();
    }

    public void PlayDeathExplosion()
    {
        if (m_deathExplosion != null)
        {
            m_deathExplosion.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            m_deathExplosion.transform.position = GetDeathExplosionPosition();
            m_deathExplosion.Play(true);
        }

        SetVisualActive(false);
        if (m_flameRoot != null)
        {
            m_flameRoot.SetActive(false);
        }
    }

    #endregion

    #region Reset

    public void ClearEffectsForReset()
    {
        m_stunRecoveryActive = false;
        m_animator.SetBool("Groggy", false);
        m_animator.SetBool("Stun", false);
        StopRollWind(true);
        m_groundBurstPlayed = false;
        m_unburrowLandingPlayed = false;
        m_flameStarted = false;

        if (m_deathExplosion != null)
        {
            m_deathExplosion.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (m_groundBurst != null)
        {
            m_groundBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (m_rollDust != null)
        {
            // Clear before teleporting so rate-over-distance cannot leave a reset trail.
            m_rollDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (m_rollImpact != null)
        {
            m_rollImpact.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    public void PrepareReset()
    {
        if (m_rollImpact != null)
        {
            m_rollImpact.transform.localPosition = m_rollImpactStartLocalPosition;
        }
        if (m_deathExplosion != null)
        {
            m_deathExplosion.transform.localPosition = m_deathExplosionStartLocalPosition;
        }
        // Reactivate before Rebind/Update so reset restores a valid visible pose.
        m_visualRoot.gameObject.SetActive(true);
        m_visualRoot.localRotation = m_visualStartRotation;
        m_visualRoot.localScale = m_visualStartScale;
        // Rebind must capture the authored holder scale instead of the final death pose.
        RestoreFlameScale();
    }

    public void ResetVisual()
    {
        m_stunRecoveryActive = false;
        m_deathAnimationActive = false;
        foreach (VisualEffect effect in m_flameEffects)
        {
            effect.Reinit();
            effect.Stop();
        }

        foreach (ParticleSystem particles in m_flameParticles)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (m_flameRoot != null)
        {
            m_flameRoot.SetActive(false);
        }

        PrepareReset();
        m_animator.enabled = true;
        m_animator.speed = m_normalAnimatorSpeed;
        m_animator.Rebind();
        // Clear the legacy movement request before explicitly playing demo states after reset.
        m_animator.ResetTrigger("ReturnMovement");
        m_animator.SetBool("Alive", true);
        m_animator.SetBool("Groggy", false);
        m_animator.SetBool("Stun", false);
        m_animator.SetBool("BattleMode", true);
        m_animator.SetBool("Walking", false);
        m_animator.SetBool("WaitingIdle", false);
        m_animator.SetBool("Grounded", true);
        m_animator.Play(m_idleStateHash, 0, 0f);
        m_animator.Update(0f);
        if (m_deformationSystem != null)
        {
            m_deformationSystem.localScale = m_deformationStartScale;
        }
        RestoreFlameScale();
        if (m_deathDebrisRoot != null)
        {
            m_deathDebrisRoot.gameObject.SetActive(m_deathDebrisStartActive);
        }
        m_visualRoot.localPosition = m_visualStartPosition + Vector3.down * m_hiddenDepth;
        m_animator.enabled = false;
        RefreshFlameBindings();
    }

    #endregion
}
