// FeedbackSteps.cs
// Concrete feedback primitives used by FeedbackCue. Each maps to one of the MoreMountains
// feedback types the project actually used, but with no dependency on Feel.
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Yeolha.BeltScroll;

namespace Aiara
{
    // Spawns a pooled VFX prefab. Faithful replacement for MMF_InstantiateObject (FeedbackPosition):
    //   position = origin.position + PositionOffset   (PositionOffset is a WORLD offset, not rotated)
    //   rotation = origin.rotation only if ApplyRotation (else keep prefab rotation)
    //   scale    = origin.lossyScale only if ApplyScale (else keep prefab scale)
    // All facing/flip mirroring therefore comes from origin.position, exactly like the legacy path.
    [System.Serializable]
    public class SpawnVFXStep : FeedbackStep
    {
        public GameObject Prefab;
        [Tooltip("World-space position offset added to the origin (MMF_InstantiateObject.PositionOffset).")]
        public Vector3 PositionOffset = Vector3.zero;
        [Tooltip("Apply the origin's rotation to the instance (MMF AlsoApplyRotation).")]
        public bool ApplyRotation = false;
        [Tooltip("Apply the origin's scale to the instance (MMF AlsoApplyScale).")]
        public bool ApplyScale = false;
        public bool Pooled = true;
        [Tooltip("Seconds before the instance is returned to the pool. 0 = never auto-return.")]
        public float Lifetime = 2f;
        [Tooltip("If the prefab has a VFXFollowTarget, make it track the cue's FollowTarget.")]
        public bool Follow = false;

        public override float StepDuration => Lifetime;

        public override void Execute(FeedbackContext ctx)
        {
            if (Prefab == null) return;
            Transform o = ctx != null ? ctx.Origin : null;

            Vector3 pos = (o != null ? o.position : Vector3.zero) + PositionOffset;
            Quaternion rot = (ApplyRotation && o != null) ? o.rotation : Prefab.transform.rotation;
            Vector3 scl = (ApplyScale && o != null) ? o.lossyScale : Prefab.transform.localScale;

            GameObject go = VFXPool.Spawn(Prefab, pos, rot, scl, Lifetime, Pooled);
            if (Follow && go != null && ctx != null && ctx.FollowTarget != null)
            {
                VFXFollowTarget follower = go.GetComponent<VFXFollowTarget>();
                if (follower != null) follower.SetTarget(ctx.FollowTarget);
            }
        }
    }

    // Plays a one-shot sound. Replacement for MMF_Sound (2D one-shots).
    [System.Serializable]
    public class SoundStep : FeedbackStep
    {
        public AudioClip Clip;
        [Range(0f, 1f)] public float Volume = 1f;

        public override void Execute(FeedbackContext ctx)
        {
            FeedbackAudio.Play(Clip, Volume);
        }
    }

    // Camera shake through the existing CameraShakeService/ShakeCameraNode channel.
    // Replacement for MMF_CinemachineImpulse and MMF_CinemachineImpulseForSequence.
    [System.Serializable]
    public class CameraShakeStep : FeedbackStep
    {
        [Tooltip("Per-axis amplitude, equivalent to the old Cinemachine Impulse Velocity.")]
        public Vector3 Velocity = new Vector3(0.3f, 0.3f, 0f);
        public float Duration = 0.3f;
        public float Frequency = 25f;

        public override float StepDuration => Duration;

        public override void Execute(FeedbackContext ctx)
        {
            CameraShakeService.Shake(Velocity, Duration, Frequency);
        }
    }

    // Slow motion. Replacement for MMF_TimescaleModifier.
    [System.Serializable]
    public class SlowMotionStep : FeedbackStep
    {
        [Range(0f, 1f)] public float TimeScale = 0.5f;
        public float Duration = 0.2f;
        public float EaseIn = 0.02f;
        public float EaseOut = 0.2f;

        public override float StepDuration => EaseIn + Duration + EaseOut;

        public override void Execute(FeedbackContext ctx)
        {
            FeedbackTime.SlowMotion(TimeScale, Duration, EaseIn, EaseOut);
        }
    }

    // Freeze frame / hitstop. Replacement for MMF_FreezeFrame.
    [System.Serializable]
    public class HitstopStep : FeedbackStep
    {
        public float Duration = 0.05f;
        [Range(0f, 1f)] public float FreezeScale = 0f;

        public override float StepDuration => Duration;

        public override void Execute(FeedbackContext ctx)
        {
            FeedbackTime.Hitstop(Duration, FreezeScale);
        }
    }

    // Gamepad rumble. Replacement for the custom MMF_GamepadRumble.
    [System.Serializable]
    public class RumbleStep : FeedbackStep
    {
        [Range(0f, 1f)] public float LowFrequency = 0.5f;
        [Range(0f, 1f)] public float HighFrequency = 0.5f;
        public float Duration = 0.2f;

        public static bool Authorized = true;

        public override float StepDuration => Duration;

        public override void Execute(FeedbackContext ctx)
        {
            FeedbackRumble.Play(LowFrequency, HighFrequency, Duration);
        }
    }

    // Fires an Animator trigger on the follow target (or the cue origin). Replacement for MMF_Animation.
    [System.Serializable]
    public class AnimationTriggerStep : FeedbackStep
    {
        public string TriggerName;

        public override void Execute(FeedbackContext ctx)
        {
            if (string.IsNullOrEmpty(TriggerName) || ctx == null) return;
            Transform root = ctx.FollowTarget != null ? ctx.FollowTarget : ctx.Origin;
            if (root == null) return;
            Animator anim = root.GetComponentInChildren<Animator>();
            if (anim != null) anim.SetTrigger(TriggerName);
        }
    }

    // Toggles a GameObject active state. Replacement for MMF_SetActive.
    [System.Serializable]
    public class SetActiveStep : FeedbackStep
    {
        public GameObject Target;
        public bool SetActive = true;

        public override void Execute(FeedbackContext ctx)
        {
            if (Target != null) Target.SetActive(SetActive);
        }
    }

    // Punches the main camera field of view then eases it back. Replacement for MMF_CameraZoom.
    [System.Serializable]
    public class CameraZoomStep : FeedbackStep
    {
        [Tooltip("Field of view to zoom to (degrees).")]
        public float TargetFOV = 45f;
        public float ZoomInDuration = 0.05f;
        public float Hold = 0.1f;
        public float ZoomOutDuration = 0.2f;

        public override float StepDuration => ZoomInDuration + Hold + ZoomOutDuration;

        public override void Execute(FeedbackContext ctx)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            FeedbackRuntimeHost.Instance.Run(ZoomRoutine(cam, TargetFOV, ZoomInDuration, Hold, ZoomOutDuration));
        }

        private static IEnumerator ZoomRoutine(Camera cam, float target, float inDur, float hold, float outDur)
        {
            float start = cam.fieldOfView;
            float t = 0f;
            while (t < inDur && inDur > 0f)
            {
                t += Time.unscaledDeltaTime;
                cam.fieldOfView = Mathf.Lerp(start, target, t / inDur);
                yield return null;
            }
            cam.fieldOfView = target;
            yield return new WaitForSecondsRealtime(hold);
            t = 0f;
            while (t < outDur && outDur > 0f)
            {
                t += Time.unscaledDeltaTime;
                cam.fieldOfView = Mathf.Lerp(target, start, t / outDur);
                yield return null;
            }
            cam.fieldOfView = start;
        }
    }

    // Pulses a post-processing Volume's weight up then down. Generic replacement for the rare
    // MMF_LensDistortion / MMF_MotionBlur feedbacks (drive a dedicated Volume via its weight).
    [System.Serializable]
    public class PostFXPulseStep : FeedbackStep
    {
        public Volume TargetVolume;
        [Range(0f, 1f)] public float PeakWeight = 1f;
        public float RiseDuration = 0.05f;
        public float Hold = 0.05f;
        public float FallDuration = 0.2f;

        public override float StepDuration => RiseDuration + Hold + FallDuration;

        public override void Execute(FeedbackContext ctx)
        {
            if (TargetVolume == null) return;
            FeedbackRuntimeHost.Instance.Run(PulseRoutine(TargetVolume, PeakWeight, RiseDuration, Hold, FallDuration));
        }

        private static IEnumerator PulseRoutine(Volume vol, float peak, float rise, float hold, float fall)
        {
            float start = vol.weight;
            float t = 0f;
            while (t < rise && rise > 0f)
            {
                t += Time.unscaledDeltaTime;
                vol.weight = Mathf.Lerp(start, peak, t / rise);
                yield return null;
            }
            vol.weight = peak;
            yield return new WaitForSecondsRealtime(hold);
            t = 0f;
            while (t < fall && fall > 0f)
            {
                t += Time.unscaledDeltaTime;
                vol.weight = Mathf.Lerp(peak, start, t / fall);
                yield return null;
            }
            vol.weight = start;
        }
    }
}
