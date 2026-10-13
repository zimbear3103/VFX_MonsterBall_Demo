// FeedbackCueBridge.cs
// TEMPORARY migration bridge. Reads an existing MoreMountains MMF_Player and builds an
// equivalent list of FeedbackCue steps at runtime, so the new engine can be validated against
// the real, already-authored feedback data without rebuilding 60 keys by hand.
// This is the only Cue file that depends on Feel; it is deleted in Phase 3 before Feel removal,
// once the data has been baked onto FeedbackCue components.
using System.Collections.Generic;
using UnityEngine;
using MoreMountains.Feedbacks;

namespace Aiara
{
    public static class FeedbackCueBridge
    {
        private static readonly HashSet<string> _warnedTypes = new HashSet<string>();

        // Builds cue steps mirroring the MMF_Player's feedback list.
        public static List<FeedbackStep> BuildSteps(MMF_Player player)
        {
            List<FeedbackStep> steps = new List<FeedbackStep>();
            if (player == null || player.FeedbacksList == null) return steps;

            for (int i = 0; i < player.FeedbacksList.Count; i++)
            {
                MMF_Feedback fb = player.FeedbacksList[i];
                if (fb == null) continue;

                FeedbackStep step = Map(fb);
                if (step == null) continue;

                step.Active = fb.Active;
                step.Delay = fb.Timing != null ? fb.Timing.InitialDelay : 0f;
                steps.Add(step);
            }
            return steps;
        }

        private static FeedbackStep Map(MMF_Feedback fb)
        {
            if (fb is MMF_InstantiateObject inst)
            {
                bool follow = inst.GameObjectToInstantiate != null
                    && inst.GameObjectToInstantiate.GetComponent<VFXFollowTarget>() != null;
                return new SpawnVFXStep
                {
                    Prefab = inst.GameObjectToInstantiate,
                    PositionOffset = inst.PositionOffset,
                    ApplyRotation = inst.AlsoApplyRotation,
                    ApplyScale = inst.AlsoApplyScale,
                    Pooled = inst.CreateObjectPool,
                    Lifetime = 3f,
                    Follow = follow
                };
            }

            if (fb is MMF_TimescaleModifier ts)
            {
                return new SlowMotionStep
                {
                    TimeScale = ts.TimeScale,
                    Duration = ts.TimeScaleDuration
                };
            }

            if (fb is MMF_FreezeFrame fr)
            {
                return new HitstopStep
                {
                    Duration = fr.FreezeFrameDuration,
                    FreezeScale = 0f
                };
            }

            if (fb is MMF_CameraZoom cz)
            {
                return new CameraZoomStep
                {
                    TargetFOV = cz.ZoomFieldOfView,
                    ZoomInDuration = cz.ZoomTransitionDuration,
                    Hold = cz.ZoomDuration,
                    ZoomOutDuration = cz.ZoomTransitionDuration
                };
            }

            if (fb is MMF_Sound snd)
            {
                return new SoundStep
                {
                    Clip = snd.Sfx,
                    Volume = snd.MaxVolume
                };
            }

            if (fb is MMF_GamepadRumble rumble)
            {
                return new RumbleStep
                {
                    LowFrequency = rumble.LowFrequency,
                    HighFrequency = rumble.HighFrequency,
                    Duration = rumble.Duration
                };
            }

            if (fb is MMF_CinemachineImpulseForSequence imp)
            {
                return new CameraShakeStep
                {
                    Velocity = imp.Velocity * imp.AmplitudeScale,
                    Duration = imp.Duration,
                    Frequency = 25f
                };
            }

            // Unmapped type: log once so we know what (rare) feedback still needs coverage.
            string typeName = fb.GetType().Name;
            if (_warnedTypes.Add(typeName))
            {
                Debug.LogWarning("[FeedbackCueBridge] No mapping for feedback type '" + typeName + "' (skipped).");
            }
            return null;
        }
    }
}
