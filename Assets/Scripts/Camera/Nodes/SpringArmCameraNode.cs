using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 거리 노드. Pivot과 카메라 사이의 거리를 담당한다.
    ///
    /// 이번 단계 책임: Base Distance + Camera Zone Distance Override (+ Damping).
    /// 카메라를 월드 좌표로 직접 옮기지 않고 localPosition.z = -distance 로 표현한다.
    ///
    /// 향후 확장 지점:
    ///   MultiTarget Framing Distance → 채널 또는 ComputeFramingDistance 오버라이드
    ///   Camera Collision            → ResolveCollision 오버라이드 (SphereCast → 완충)
    ///   Zoom                        → ICameraChannel로 Distance 기여
    /// </summary>
    public class SpringArmCameraNode : CameraNode
    {
        [Header("Distance")]
        [Tooltip("기본 카메라 거리 (m)")]
        [SerializeField] private float baseDistance = 8f;

        [Tooltip("최소 거리")]
        [SerializeField] private float minDistance = 2f;

        [Tooltip("최대 거리")]
        [SerializeField] private float maxDistance = 20f;

        [Tooltip("거리 변화 Damping (SmoothDamp smoothTime, 초)")]
        [SerializeField, Range(0f, 2f)] private float distanceSmoothTime = 0.35f;

        private float _currentDistance;
        private float _dampVelocity;
        private bool _snapped;

        /// <summary>현재 적용 중인 거리 (외부 조회용)</summary>
        public float CurrentDistance => _currentDistance;

        public override void Evaluate(float dt)
        {
            // Zone Override 블렌드
            float desired = Rig != null && Rig.ZoneController != null
                ? Rig.ZoneController.EvaluateDistance(baseDistance)
                : baseDistance;

            // Channel 기여 (Zoom 등)
            CameraNodeState state = CameraNodeState.Default;
            state.Distance = desired;
            ApplyChannels(dt, ref state);

            float target = Mathf.Clamp(state.Distance, minDistance, maxDistance);

            if (!_snapped)
            {
                _currentDistance = target;
                _dampVelocity = 0f;
                _snapped = true;
            }
            else
            {
                _currentDistance = Mathf.SmoothDamp(
                    _currentDistance, target, ref _dampVelocity, distanceSmoothTime,
                    Mathf.Infinity, dt);
            }

            float finalDistance = ResolveCollision(_currentDistance);

            // 카메라 노드 계층의 로컬 후방(-Z)으로 거리 표현
            transform.localPosition = new Vector3(0f, 0f, -finalDistance);
        }

        /// <summary>
        /// 카메라 충돌 완충 확장 지점.
        /// 다음 단계에서 Desired Distance → SphereCast → Collision Distance → Smooth Damping을 구현한다.
        /// 현재는 입력 거리를 그대로 반환한다.
        /// </summary>
        protected virtual float ResolveCollision(float desiredDistance) => desiredDistance;
    }
}
