using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 회전 노드. 스플라인 Tangent 기준으로 카메라 Yaw를 정렬한다.
    ///
    /// 벨트스크롤이므로 카메라는 스플라인을 '옆에서' 바라본다:
    ///   Spline Tangent = 스테이지 진행축
    ///   Camera View   = Tangent에 수직인 방향 (기존 프로젝트와 동일하게 -Cross(up, tangent))
    ///
    /// 책임: Spline Tangent Yaw + Base Pitch + Camera Zone Pitch/Yaw Offset.
    /// 즉시 Snap하지 않고 SmoothDampAngle로 회전을 댐핑한다.
    /// </summary>
    public class PivotCameraNode : CameraNode
    {
        [Header("Rotation")]
        [Tooltip("기본 Pitch (deg). 위에서 내려다보는 각도.")]
        [SerializeField] private float basePitch = 15f;

        [Tooltip("스플라인 좌/우 어느 측면에서 바라볼지. +1 / -1")]
        [SerializeField] private float sideSign = 1f;

        [Tooltip("추가 Yaw 보정 (deg). 씬 배치에 맞춰 미세 조정용.")]
        [SerializeField] private float yawOffset = 0f;

        [Tooltip("회전 Damping (SmoothDamp smoothTime, 초)")]
        [SerializeField, Range(0f, 2f)] private float rotationSmoothTime = 0.3f;

        [Tooltip("스플라인이 없을 때 바라볼 기본 뷰 방향")]
        [SerializeField] private Vector3 defaultViewDirection = new Vector3(0f, 0f, 1f);

        private float _yaw;
        private float _pitch;
        private float _yawVelocity;
        private float _pitchVelocity;
        private bool _snapped;

        public override void Evaluate(float dt)
        {
            // 기준 위치: 플레이어 (없으면 FollowNode 위치 = 부모)
            Vector3 referencePosition = Rig != null && Rig.PlayerTarget != null
                ? Rig.PlayerTarget.position
                : transform.position;

            // 스플라인 진행축 → 측면 뷰 방향
            Vector3 viewDirection;
            if (Rig != null && Rig.SplineReference != null && Rig.SplineReference.HasSpline)
            {
                Vector3 forward = Rig.SplineReference.GetFrame(referencePosition).Forward;
                // 기존 프로젝트(CinemachineSplineRotation)와 동일한 측면 방향 규칙
                viewDirection = -Vector3.Cross(Vector3.up, forward) * sideSign;
            }
            else
            {
                viewDirection = defaultViewDirection;
            }

            if (viewDirection.sqrMagnitude < 0.0001f)
                viewDirection = defaultViewDirection;
            viewDirection.y = 0f;
            viewDirection.Normalize();

            // Zone Offset 합성
            float zonePitch = 0f;
            float zoneYaw = 0f;
            if (Rig != null && Rig.ZoneController != null)
            {
                zonePitch = Rig.ZoneController.PitchOffset;
                zoneYaw = Rig.ZoneController.YawOffset;
            }

            float targetYaw = Mathf.Atan2(viewDirection.x, viewDirection.z) * Mathf.Rad2Deg + yawOffset + zoneYaw;
            float targetPitch = basePitch + zonePitch;

            // Damping (첫 프레임은 Snap)
            if (!_snapped)
            {
                _yaw = targetYaw;
                _pitch = targetPitch;
                _yawVelocity = 0f;
                _pitchVelocity = 0f;
                _snapped = true;
            }
            else
            {
                _yaw = Mathf.SmoothDampAngle(_yaw, targetYaw, ref _yawVelocity, rotationSmoothTime, Mathf.Infinity, dt);
                _pitch = Mathf.SmoothDampAngle(_pitch, targetPitch, ref _pitchVelocity, rotationSmoothTime, Mathf.Infinity, dt);
            }

            // Channel 기여 (회전 오프셋)
            CameraNodeState state = CameraNodeState.Default;
            state.Rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            ApplyChannels(dt, ref state);

            transform.rotation = state.Rotation * Quaternion.Euler(state.RotationOffset);
        }
    }
}
