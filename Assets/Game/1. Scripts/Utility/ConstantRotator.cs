using UnityEngine;

namespace Aiara
{
    /// <summary>
    /// 오브젝트를 일정한 방향으로 계속 회전시킵니다.
    /// RotationPerSecond에 지정한 오일러 각(도)만큼 매 초 회전합니다.
    /// (예: (0, 90, 0) → 초당 Y축 90도 회전 = 4초에 한 바퀴)
    /// </summary>
    [AddComponentMenu("Aiara/Utility/Constant Rotator")]
    public class ConstantRotator : MonoBehaviour
    {
        [Tooltip("초당 회전량 (오일러 각, 도 단위). 각 축에 입력한 값만큼 1초에 회전한다.")]
        public Vector3 RotationPerSecond = new Vector3(0f, 90f, 0f);

        [Tooltip("회전 기준 공간. Self: 로컬 축 기준, World: 월드 축 기준.")]
        public Space RotationSpace = Space.Self;

        [Tooltip("Time.timeScale의 영향을 받지 않고 회전할지 여부 (히트스톱/일시정지 중에도 회전 유지).")]
        public bool UseUnscaledTime = false;

        private void Update()
        {
            float deltaTime = UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            transform.Rotate(RotationPerSecond * deltaTime, RotationSpace);
        }
    }
}
