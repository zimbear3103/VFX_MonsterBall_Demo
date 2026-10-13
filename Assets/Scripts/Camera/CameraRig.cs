using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 자체 카메라 시스템의 실행 관리자. Cinemachine을 사용하지 않는다.
    ///
    /// Transform 계층 (계산 계층과 동일):
    ///   CameraRig
    ///   └─ FollowNode   (플레이어 추적 + Damping)
    ///      └─ PivotNode  (스플라인 기준 Yaw + Pitch)
    ///         └─ SpringArm (거리)
    ///            └─ ShakeNode (셰이크 — 현재 무효과 통과)
    ///               └─ LensNode (FOV)
    ///                  └─ Camera
    ///
    /// LateUpdate에서 반드시 Root → Leaf 순서로 Evaluate한다.
    /// PlayerMovement와는 Player Transform / SplineMovementReference만 공유한다.
    /// </summary>
    [DefaultExecutionOrder(100)] // 플레이어 이동(Update) 이후 실행 보장
    public class CameraRig : MonoBehaviour
    {
        [Header("Shared References")]
        [Tooltip("추적할 플레이어 Transform")]
        [SerializeField] private Transform playerTarget;

        [Tooltip("플레이어와 공유하는 스플라인 기준 좌표계")]
        [SerializeField] private SplineMovementReference splineReference;

        [Tooltip("카메라 Zone 컨트롤러 (선택). 비워두면 씬에서 찾는다.")]
        [SerializeField] private CameraZoneController zoneController;

        [Header("Confiner")]
        [Tooltip("카메라가 벗어날 수 없는 기본 경계 볼륨(항상 적용). 비우면 구역 CameraZone이 활성일 때만 제한된다. Collider.ClosestPoint로 카메라 최종 위치를 가둔다.")]
        [SerializeField] public Collider defaultConfinerVolume;

        [Header("Nodes")]//(비워두면 자식에서 자동 검색)
        [SerializeField] private FollowCameraNode followNode;
        [SerializeField] private PivotCameraNode pivotNode;
        [SerializeField] private SpringArmCameraNode springArmNode;
        [SerializeField] private ShakeCameraNode shakeNode;
        [SerializeField] private LensCameraNode lensNode;

        // ─────────────── 노드/공유 데이터 접근 ───────────────

        public Transform PlayerTarget => playerTarget;
        public SplineMovementReference SplineReference => splineReference;
        public CameraZoneController ZoneController => zoneController;

        public FollowCameraNode FollowNode => followNode;
        public PivotCameraNode PivotNode => pivotNode;
        public SpringArmCameraNode SpringArmNode => springArmNode;
        public ShakeCameraNode ShakeNode => shakeNode;
        public LensCameraNode LensNode => lensNode;

        // 트리거로 CameraZone이 할당한 Confiner 볼륨들(LIFO). 없으면 defaultConfinerVolume 사용.
        private readonly List<Collider> _assignedConfiners = new List<Collider>();

        // OnEnable 사용 이유: 도메인 리로드 후에도 다시 호출되어
        // 노드의 Rig 참조(비직렬화)가 복구된다.
        private void OnEnable()
        {
            if (followNode == null) followNode = GetComponentInChildren<FollowCameraNode>();
            if (pivotNode == null) pivotNode = GetComponentInChildren<PivotCameraNode>();
            if (springArmNode == null) springArmNode = GetComponentInChildren<SpringArmCameraNode>();
            if (shakeNode == null) shakeNode = GetComponentInChildren<ShakeCameraNode>();
            if (lensNode == null) lensNode = GetComponentInChildren<LensCameraNode>();

            if (zoneController == null)
                zoneController = FindFirstObjectByType<CameraZoneController>();

            InjectRig(followNode);
            InjectRig(pivotNode);
            InjectRig(springArmNode);
            InjectRig(shakeNode);
            InjectRig(lensNode);
        }

        private void Start()
        {
            if (playerTarget != null && followNode != null)
                followNode.AddTarget(playerTarget, 1f);
        }

        private void InjectRig(CameraNode node)
        {
            if (node != null)
                node.Rig = this;
        }

        /// <summary>실행 순서 고정: Follow → Pivot → SpringArm → Shake → Lens.</summary>
        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            if (followNode != null) followNode.Evaluate(dt);
            if (pivotNode != null) pivotNode.Evaluate(dt);
            if (springArmNode != null) springArmNode.Evaluate(dt);
            if (shakeNode != null) shakeNode.Evaluate(dt);
            if (lensNode != null) lensNode.Evaluate(dt);

            // 노드 평가 후 최종 카메라 위치를 경계 볼륨 안으로 가둔다.
            ApplyCameraConfiner();
        }

        /// <summary>런타임에 추적 대상을 바꿀 때 사용.</summary>
        public void SetPlayerTarget(Transform target)
        {
            if (followNode != null)
            {
                if (playerTarget != null)
                    followNode.RemoveTarget(playerTarget);
                if (target != null)
                    followNode.AddTarget(target, 1f);
            }

            playerTarget = target;
        }

        /// <summary>
        /// 스테이지 스플라인 교체. 그룹 로드 시 MultiSceneLoader가 새 Spline 씬의
        /// SplineMovementReference를 주입한다.
        /// </summary>
        public void SetSplineReference(SplineMovementReference reference)
        {
            splineReference = reference;
        }

        /// <summary>Scene 전환/텔레포트 직후 카메라를 타겟 위치로 즉시 스냅. (댐핑 잔상 방지)</summary>
        public void SnapToTarget()
        {
            if (followNode != null)
                followNode.Snap();
        }

        // ─────────────── 카메라 이동 경계(Confiner) — 리그 소유 ───────────────

        /// <summary>구역 CameraZone이 트리거 진입 시 등록. Confiner 후보가 된다.</summary>
        /// <summary>CameraZone이 트리거 진입(전투 시작 등) 시 자신의 Confiner 볼륨을 리그에 할당한다.</summary>
        public void AssignConfiner(Collider volume)
        {
            if (volume != null && !_assignedConfiners.Contains(volume))
                _assignedConfiners.Add(volume);
        }

        /// <summary>구역 CameraZone이 트리거 이탈/비활성 시 등록 해제.</summary>
        /// <summary>CameraZone이 트리거 이탈/비활성 시 할당한 Confiner 볼륨을 해제한다.</summary>
        public void ReleaseConfiner(Collider volume)
        {
            _assignedConfiners.Remove(volume);
        }

        /// <summary>
        /// 현재 적용할 Confiner 볼륨. 활성 구역 중 우선순위 최고(동률이면 나중 등록)이며
        /// Confiner를 켠 존을 우선, 없으면 항상 적용되는 defaultConfinerVolume을 사용한다.
        /// </summary>
        /// <summary>
        /// 현재 적용할 Confiner 볼륨. 가장 최근 할당된(활성·유효) 존 볼륨을 우선하고,
        /// 없으면 항상 적용되는 defaultConfinerVolume을 사용한다.
        /// </summary>
        private Collider ResolveConfinerVolume()
        {
            for (int i = _assignedConfiners.Count - 1; i >= 0; i--)
            {
                Collider v = _assignedConfiners[i];
                if (v != null && v.enabled && v.gameObject.activeInHierarchy)
                    return v;
            }

            if (defaultConfinerVolume != null && defaultConfinerVolume.enabled && defaultConfinerVolume.gameObject.activeInHierarchy)
                return defaultConfinerVolume;

            return null;
        }

        /// <summary>
        /// 노드 평가가 끝난 뒤, 최종 카메라 위치를 Confiner 볼륨 안으로 가둔다.
        /// 카메라를 직접 옮기지 않고 리그 루트(FollowNode)를 같은 델타만큼 이동시켜
        /// 계층의 로컬 오프셋 누적 없이 다음 프레임에 자동 복원되게 한다.
        /// </summary>
        private void ApplyCameraConfiner()
        {
            if (followNode == null || lensNode == null || lensNode.TargetCamera == null)
                return;

            Collider volume = ResolveConfinerVolume();
            if (volume == null)
                return;

            Transform cam = lensNode.TargetCamera.transform;
            Vector3 camPos = cam.position;
            Vector3 clamped = volume.ClosestPoint(camPos);
            Vector3 delta = clamped - camPos;
            if (delta.sqrMagnitude > 1e-8f)
                followNode.transform.position += delta;
        }
    }
}
