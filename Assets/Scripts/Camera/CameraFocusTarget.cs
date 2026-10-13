using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 전투 카메라(CombatCameraController) 포커싱 대상의 개체별 오버라이드(선택 컴포넌트).
    /// 구버전 CharacterCameraFocusTarget(TDE Ability)의 Weight/Radius/CustomTargetTransform 이관.
    ///
    /// 붙이지 않으면 컨트롤러 기본값(otherEnemyWeight · defaultEnemyRadius · Character.transform)을 쓴다.
    /// 덩치 큰 적(구버전 Kiu·Crasher = Weight 2 / Radius 1.5)처럼 개체별로 구도 비중과
    /// 프레이밍 여유를 키울 때만 부착한다. 카메라 로직은 갖지 않는다(데이터 전용).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Yeolha/Camera/Camera Focus Target")]
    public class CameraFocusTarget : MonoBehaviour
    {
        [Header("Focus Weight")]
        [Tooltip("컨트롤러 기본 Weight에 곱할 배율. 1 = 기본. 구버전 덩치 적(Weight 2)은 2")]
        [SerializeField] private float weightMultiplier = 1f;

        [Tooltip("프레이밍 반경(m). 카메라 거리 계산 시 이 캐릭터 좌우로 확보할 여유. 구버전 일반 0.5 / 덩치 적 1.5")]
        [SerializeField] private float radius = 0.5f;

        [Header("Target Transform Override")]
        [Tooltip("카메라가 추적할 기준 Transform. 비우면 Character 루트 Transform")]
        [SerializeField] private Transform customTargetTransform;

        public float WeightMultiplier => weightMultiplier;
        public float Radius => radius;

        /// <summary>추적 기준 Transform. 오버라이드가 없으면 fallback(보통 Character.transform).</summary>
        public Transform ResolveTargetTransform(Transform fallback)
            => customTargetTransform != null ? customTargetTransform : fallback;
    }
}
