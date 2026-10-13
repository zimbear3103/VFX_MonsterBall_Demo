// FeedbackCue.cs
// Lightweight replacement for MoreMountains MMF_Player. A cue holds an ordered list of
// polymorphic feedback steps (SerializeReference) and plays them, each after its own delay.
// The registry facade (FeedbackManager) drives cues exactly like it used to drive MMF_Player,
// so no caller changes are required.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Aiara
{
    // Per-play context handed to each step so it can resolve world placement and follow targets.
    public class FeedbackContext
    {
        public Transform Origin;        // the cue transform, already positioned by the caller
        public Transform FollowTarget;  // optional: spawned VFX with VFXFollowTarget will track this
    }

    // Base class for all feedback steps. Concrete steps live in FeedbackSteps.cs.
    [System.Serializable]
    public abstract class FeedbackStep
    {
        [Tooltip("Uncheck to skip this step without deleting it.")]
        public bool Active = true;

        [Tooltip("Seconds to wait, from cue start, before this step runs.")]
        public float Delay = 0f;

        // Used only to estimate the cue's total duration.
        public virtual float StepDuration => 0f;

        public abstract void Execute(FeedbackContext ctx);
    }

    public class FeedbackCue : MonoBehaviour
    {
        [SerializeReference]
        public List<FeedbackStep> Steps = new List<FeedbackStep>();

        // Set by the caller (FeedbackManager) immediately before a play; spawned VFX that carry
        // a VFXFollowTarget will track this transform without being reparented.
        [System.NonSerialized] public Transform FollowTarget;

        public float TotalDuration
        {
            get
            {
                float max = 0f;
                if (Steps == null) return 0f;
                for (int i = 0; i < Steps.Count; i++)
                {
                    FeedbackStep s = Steps[i];
                    if (s == null || !s.Active) continue;
                    float end = s.Delay + s.StepDuration;
                    if (end > max) max = end;
                }
                return max;
            }
        }

        // Position/rotate/scale the cue transform, then play. Mirrors MMF_Player placement.
        public void PlayAt(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Transform t = transform;
            t.position = position;
            t.rotation = rotation;
            t.localScale = scale;
            PlayFeedbacks();
        }

        // Plays every active step. Steps with a Delay are scheduled; the rest fire immediately.
        public void PlayFeedbacks()
        {
            if (Steps == null || Steps.Count == 0) return;
            FeedbackContext ctx = new FeedbackContext { Origin = transform, FollowTarget = FollowTarget };

            for (int i = 0; i < Steps.Count; i++)
            {
                FeedbackStep s = Steps[i];
                if (s == null || !s.Active) continue;

                if (s.Delay > 0f && Application.isPlaying)
                {
                    FeedbackRuntimeHost.Instance.Run(RunDelayed(s, ctx));
                }
                else
                {
                    SafeExecute(s, ctx);
                }
            }
        }

        // Convenience overload matching MMF_Player.PlayFeedbacks(worldPosition).
        public void PlayFeedbacks(Vector3 worldPosition)
        {
            transform.position = worldPosition;
            PlayFeedbacks();
        }

        private IEnumerator RunDelayed(FeedbackStep step, FeedbackContext ctx)
        {
            yield return new WaitForSeconds(step.Delay);
            SafeExecute(step, ctx);
        }

        private void SafeExecute(FeedbackStep step, FeedbackContext ctx)
        {
            try
            {
                step.Execute(ctx);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[FeedbackCue] step '" + step.GetType().Name + "' on '" + name + "' threw: " + e.Message, this);
            }
        }
    }
}
