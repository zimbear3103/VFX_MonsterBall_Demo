using UnityEngine;
using MoreMountains.Feedbacks;
using Yeolha.BeltScroll;

namespace Aiara
{
    /// <summary>
    /// 구버전 Cinemachine Impulse 셰이크 피드백의 ver2 호환 구현.
    /// 클래스 식별자(asm/ns/name)와 Velocity 직렬화 필드를 유지해
    /// 구버전 FeedbackManager 프리팹의 셰이크 데이터가 그대로 살아난다.
    /// 실제 셰이크는 Cinemachine 대신 ver2 ShakeCameraNode 채널로 수행한다.
    /// (Cinemachine 전용 ImpulseDefinition 데이터는 복원 불가 - Duration 기본값 사용)
    /// </summary>
    [System.Serializable]
    [AddComponentMenu("")]
    [FeedbackPath("Camera/Cinemachine Impulse (Sequence Support)")]
    [FeedbackHelp("Plays a decaying camera shake on the ver2 ShakeCameraNode. Velocity keeps legacy impulse data.")]
    public class MMF_CinemachineImpulseForSequence : MMF_Feedback
    {
        public static bool FeedbackTypeAuthorized = true;
        public override bool HasRandomness => true;

#if UNITY_EDITOR
        public override Color FeedbackColor { get { return MMFeedbacksInspectorColors.CameraColor; } }
#endif

        [MMFInspectorGroup("Camera Shake (ver2)", true, 28)]
        [Tooltip("Per-axis shake amplitude (legacy Cinemachine impulse velocity)")]
        public Vector3 Velocity = new Vector3(1f, 1f, 1f);
        [Tooltip("Shake duration in seconds")]
        public float Duration = 0.25f;
        [Tooltip("Velocity to world-offset scale")]
        public float AmplitudeScale = 0.12f;
        [Tooltip("Legacy field (kept for serialization compatibility)")]
        public bool ClearImpulseOnStop = false;

        public override float FeedbackDuration { get { return Duration; } set { } }

        protected override void CustomPlayFeedback(Vector3 position, float feedbacksIntensity = 1.0f)
        {
            if (!Active || !FeedbackTypeAuthorized)
                return;
            // (diag removed)
            CameraShakeService.Shake(Velocity * (AmplitudeScale * feedbacksIntensity), Duration);
        }
    }
}
