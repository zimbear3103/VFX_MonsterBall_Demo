using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 카메라 노드 평가용 상태.
    ///
    /// 흐름:
    ///   노드가 Base State 생성 → Channel들이 기여(Sample) → 최종 상태 → Transform 적용.
    /// 효과 코드(Channel)가 Transform을 직접 만지지 않도록 하는 중간 데이터다.
    ///
    /// 노드 종류에 따라 필요한 필드만 사용한다.
    /// (Follow = Position 계열, Pivot/Shake = Rotation 계열, SpringArm = Distance, Lens = Fov)
    /// </summary>
    public struct CameraNodeState
    {
        /// <summary>노드의 기본 위치 (Follow: 추적 목표 위치)</summary>
        public Vector3 Position;

        /// <summary>Channel이 더하는 위치 오프셋 (Shake: 로컬 흔들림)</summary>
        public Vector3 PositionOffset;

        /// <summary>노드의 기본 회전</summary>
        public Quaternion Rotation;

        /// <summary>Channel이 더하는 회전 오프셋 (Euler, deg)</summary>
        public Vector3 RotationOffset;

        /// <summary>SpringArm 거리</summary>
        public float Distance;

        /// <summary>카메라 FOV</summary>
        public float Fov;

        public static CameraNodeState Default => new CameraNodeState
        {
            Position = Vector3.zero,
            PositionOffset = Vector3.zero,
            Rotation = Quaternion.identity,
            RotationOffset = Vector3.zero,
            Distance = 0f,
            Fov = 60f
        };
    }
}
