// FeedbackEngineToggle.cs
// Persistent migration control: selects which feedback backend FeedbackManager uses at runtime.
// Off = legacy MoreMountains MMF_Player playback. On = the new Feel-independent FeedbackCue engine.
// Kept until Feel is fully removed (Phase 3), after which the cue engine is the only path.
using UnityEngine;

namespace Aiara
{
    public class FeedbackEngineToggle : MonoBehaviour
    {
        [Tooltip("On: play feedback through the new FeedbackCue engine. Off: legacy MMF_Player.")]
        public bool useCueEngine = false;

        private void Awake()
        {
            FeedbackManager.UseCueEngine = useCueEngine;
        }

        private void OnEnable()
        {
            FeedbackManager.UseCueEngine = useCueEngine;
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                FeedbackManager.UseCueEngine = useCueEngine;
            }
        }
    }
}
