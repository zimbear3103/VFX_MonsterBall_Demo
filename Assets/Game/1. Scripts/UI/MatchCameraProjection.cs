using UnityEngine;

namespace Aiara
{
    /// <summary>
    /// 이 카메라의 투영(projection)을 매 프레임 Source 카메라와 일치시킨다.
    /// 월드 스페이스 UI(적 체력/투혼 바)를 포스트프로세싱 없는 오버레이 카메라로 렌더할 때,
    /// 메인 카메라와 화면상 정렬이 어긋나지 않도록 사용한다. (MMCameraZoom 등 FOV 변화 대응)
    /// 위치/회전은 이 카메라를 메인 카메라의 자식으로 두어 계층으로 일치시키고,
    /// 여기서는 투영 행렬만 복사한다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Aiara/Camera/Match Camera Projection")]
    public class MatchCameraProjection : MonoBehaviour
    {
        [Tooltip("투영을 따라갈 원본 카메라 (보통 MainCamera)")]
        public Camera Source;

        protected Camera _camera;

        protected virtual void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        protected virtual void LateUpdate()
        {
            if (Source == null || _camera == null) return;

            _camera.orthographic = Source.orthographic;
            _camera.fieldOfView = Source.fieldOfView;
            _camera.orthographicSize = Source.orthographicSize;
            _camera.nearClipPlane = Source.nearClipPlane;
            _camera.farClipPlane = Source.farClipPlane;
            // 오블리크/커스텀 투영까지 정확히 일치 (가장 견고)
            _camera.projectionMatrix = Source.projectionMatrix;
        }
    }
}
