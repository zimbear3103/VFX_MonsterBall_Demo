// FeedbackCueRegistry.cs
// Parallel key -> FeedbackCue registry, mirroring FeedbackManager's API surface but driving the
// new Feel-independent engine. During migration this runs alongside the MMF registry; callers are
// routed here only when FeedbackManager.UseCueEngine is true, so the switch is instant and reversible.
using System.Collections.Generic;
using UnityEngine;

namespace Aiara
{
    public static class FeedbackCueRegistry
    {
        private static readonly Dictionary<string, FeedbackCue> _registry = new Dictionary<string, FeedbackCue>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            _registry.Clear();
        }

        public static void Register(string key, FeedbackCue cue)
        {
            if (string.IsNullOrEmpty(key) || cue == null) return;
            _registry[key] = cue;
        }

        public static void Unregister(string key, FeedbackCue cue)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_registry.TryGetValue(key, out FeedbackCue current) && (cue == null || current == cue))
            {
                _registry.Remove(key);
            }
        }

        public static bool IsRegistered(string key)
        {
            return !string.IsNullOrEmpty(key)
                && _registry.TryGetValue(key, out FeedbackCue c)
                && c != null;
        }

        // Local placement relative to a base transform (mirrors FeedbackManager.PlayFeedback).
        public static void PlayLocal(string key, Transform baseTransform, Vector3 localPosition, Vector3 localEulerAngles, Vector3 localScale)
        {
            if (!TryGet(key, out FeedbackCue cue)) return;

            Vector3 worldPos;
            Quaternion worldRot;
            if (baseTransform != null)
            {
                worldPos = baseTransform.TransformPoint(localPosition);
                worldRot = baseTransform.rotation * Quaternion.Euler(localEulerAngles);
            }
            else
            {
                worldPos = localPosition;
                worldRot = Quaternion.Euler(localEulerAngles);
            }

            cue.FollowTarget = baseTransform;
            cue.PlayAt(worldPos, worldRot, localScale);
        }

        // Absolute world placement (mirrors FeedbackManager.PlayFeedbackAtWorld).
        public static void PlayWorld(string key, Vector3 worldPosition, Quaternion worldRotation, Vector3 scale)
        {
            if (!TryGet(key, out FeedbackCue cue)) return;
            cue.FollowTarget = null;
            cue.PlayAt(worldPosition, worldRotation, scale);
        }

        private static bool TryGet(string key, out FeedbackCue cue)
        {
            cue = null;
            if (string.IsNullOrEmpty(key)) return false;
            if (!_registry.TryGetValue(key, out cue) || cue == null)
            {
                Debug.LogWarning("[FeedbackCueRegistry] no cue registered for key '" + key + "'.");
                return false;
            }
            return true;
        }
    }
}
