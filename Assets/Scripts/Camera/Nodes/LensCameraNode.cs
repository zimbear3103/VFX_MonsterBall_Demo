using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 렌즈 노드. 실제 Unity Camera 바로 위에 위치하며 FOV를 관리한다.
    ///
    /// 이번 단계 책임: 기본 FOV + Channel 기여(FOV Punch 등 향후).
    /// 저체력 Vignette / Post Process 연결은 향후 이 노드에서 확장한다.
    /// </summary>
    public class LensCameraNode : CameraNode
    {
        [Header("Lens")]
        [Tooltip("제어할 Unity Camera. 비워두면 자식에서 찾는다.")]
        [SerializeField] private Camera targetCamera;

        [Tooltip("기본 FOV (deg)")]
        [SerializeField, Range(1f, 179f)] private float baseFov = 55f;

        /// <summary>제어 중인 카메라 (외부 조회용)</summary>
        public Camera TargetCamera => targetCamera;

        private void Awake()
        {
            if (targetCamera == null)
                targetCamera = GetComponentInChildren<Camera>();
        }

        public override void Evaluate(float dt)
        {
            CameraNodeState state = CameraNodeState.Default;
            state.Fov = baseFov;

            ApplyChannels(dt, ref state);

            if (targetCamera != null)
                targetCamera.fieldOfView = state.Fov;
        }
    }
}
