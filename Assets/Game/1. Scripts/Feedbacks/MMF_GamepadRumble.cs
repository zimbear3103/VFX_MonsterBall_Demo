using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using MoreMountains.Feedbacks;
using MoreMountains.Tools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Aiara
{
    [AddComponentMenu("")]
    [FeedbackHelp("게임패드 진동을 재생합니다.")]
    [FeedbackPath("Haptics/Gamepad Rumble")]
    public class MMF_GamepadRumble : MMF_Feedback
    {
        public static bool FeedbackTypeAuthorized = true;

#if UNITY_EDITOR
        static MMF_GamepadRumble()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Gamepad.current?.ResetHaptics();
            }
        }
#endif
        public override float FeedbackDuration { get => Duration; set => Duration = value; }

#if UNITY_EDITOR
        public override Color FeedbackColor => MMFeedbacksInspectorColors.HapticsColor;
#endif

        [MMFInspectorGroup("Rumble Settings", true, 10)]

        [Tooltip("저주파 모터 강도 (0~1), 무거운 진동")]
        [Range(0f, 1f)]
        public float LowFrequency = 0.5f;

        [Tooltip("고주파 모터 강도 (0~1), 가벼운 진동")]
        [Range(0f, 1f)]
        public float HighFrequency = 0.5f;

        [Tooltip("진동 지속 시간 (초)")]
        public float Duration = 0.2f;

        protected Coroutine _rumbleCoroutine;

        public override void OnDisable()
        {
            base.OnDisable();
            _rumbleCoroutine = null;
            Gamepad.current?.ResetHaptics();
        }

        protected override void CustomPlayFeedback(Vector3 position, float feedbacksIntensity = 1.0f)
        {
            if (!Active || !FeedbackTypeAuthorized) return;

            float intensity = Timing.ConstantIntensity ? 1f : feedbacksIntensity;

            if (_rumbleCoroutine != null)
                Owner.StopCoroutine(_rumbleCoroutine);

            _rumbleCoroutine = Owner.StartCoroutine(RumbleCoroutine(
                LowFrequency * intensity,
                HighFrequency * intensity));
        }

        protected override void CustomStopFeedback(Vector3 position, float feedbacksIntensity = 1.0f)
        {
            if (_rumbleCoroutine != null)
            {
                Owner.StopCoroutine(_rumbleCoroutine);
                _rumbleCoroutine = null;
            }
            Gamepad.current?.SetMotorSpeeds(0f, 0f);
        }

        private IEnumerator RumbleCoroutine(float low, float high)
        {
            Gamepad gamepad = Gamepad.current;
            if (gamepad == null) yield break;

            gamepad.SetMotorSpeeds(low, high);
            yield return new WaitForSeconds(Duration);
            gamepad.SetMotorSpeeds(0f, 0f);
            _rumbleCoroutine = null;
        }
    }
}
