using UnityEngine;
using UnityEngine.InputSystem;

namespace Aiara
{
    /// <summary>
    /// 키보드 B 키로 게임패드 진동을 켜고/끄는 토글.
    /// 끄면 MMF_GamepadRumble.FeedbackTypeAuthorized 를 false 로 만들어
    /// 이후 모든 럼블 피드백을 차단하고, 진행 중인 진동도 즉시 정지한다.
    /// 에디터/개발용으로 항상 동작한다.
    /// </summary>
    [AddComponentMenu("Aiara/Gamepad Rumble Toggle")]
    public class GamepadRumbleToggle : MonoBehaviour
    {
        [Tooltip("진동을 켜고/끄는 키")]
        public Key ToggleKey = Key.B;

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard[ToggleKey].wasPressedThisFrame)
            {
                SetRumbleEnabled(!MMF_GamepadRumble.FeedbackTypeAuthorized);
            }
        }

        /// <summary>진동 허용 여부를 설정. 끌 때는 진행 중인 진동도 즉시 정지.</summary>
        public static void SetRumbleEnabled(bool enabled)
        {
            MMF_GamepadRumble.FeedbackTypeAuthorized = enabled;

            if (!enabled)
            {
                Gamepad.current?.SetMotorSpeeds(0f, 0f);
                Gamepad.current?.ResetHaptics();
            }

            Debug.Log($"[GamepadRumbleToggle] 게임패드 진동 {(enabled ? "ON" : "OFF")}");
        }

        private void OnDisable()
        {
            // 컴포넌트가 꺼져도 다음 실행에서 다시 진동이 나오도록 허용 상태 복구
            MMF_GamepadRumble.FeedbackTypeAuthorized = true;
        }
    }
}
