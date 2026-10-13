using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Trigger Collider 기반 카메라 파라미터 Override 존.
    ///
    /// 플레이어(PlayerMovement)가 진입하면 CameraZoneController 스택에 올라가고,
    /// Priority가 가장 높은 Zone의 Override 값이 BlendTime에 걸쳐 블렌드된다.
    /// 이탈하면 하위 Zone 또는 기본 상태로 복귀한다.
    ///
    /// 기존(레거시) 전역 CameraZone과는 별개의 신규 시스템이다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CameraZone : MonoBehaviour
    {
        [Header("Zone")]
        [Tooltip("중첩 시 높은 값이 우선 적용된다.")]
        [SerializeField] private int priority = 0;

        [Tooltip("진입/이탈 블렌드 시간 (초)")]
        [SerializeField] private float blendTime = 0.6f;

        [Header("Overrides")]
        [SerializeField] private bool overrideDistance = false;
        [Tooltip("SpringArm 거리 Override")]
        [SerializeField] private float distance = 8f;

        [SerializeField] private bool overridePitchOffset = false;
        [Tooltip("Pivot Pitch에 더할 각도 (deg)")]
        [SerializeField] private float pitchOffset = 0f;

        [SerializeField] private bool overrideYawOffset = false;
        [Tooltip("Pivot Yaw에 더할 각도 (deg)")]
        [SerializeField] private float yawOffset = 0f;

        [SerializeField] private bool overrideHeightOffset = false;
        [Tooltip("Follow 위치에 더할 높이 (m)")]
        [SerializeField] private float heightOffset = 0f;

        [SerializeField] private bool overrideFollowOffset = false;
        [Tooltip("Follow 위치에 더할 월드 오프셋")]
        [SerializeField] private Vector3 followOffset = Vector3.zero;

        [Header("Confiner")]
        [Tooltip("체크 시 이 존 안에서 카메라 추적 위치를 아래 볼륨 안으로 가둔다. 카메라가 갈 수 있는 거리를 제한한다.")]
        [SerializeField] private bool overrideConfiner = false;

        [Tooltip("카메라 추적 위치를 가둘 볼륨. 비우면 이 존의 Trigger Collider가 사용된다. 특정 축 제한을 풀려면 그 축으로 큰 별도 Collider를 할당한다. (MeshCollider는 Convex 필요)")]
        [SerializeField] private Collider confinerVolume;

        [Header("References")]
        [Tooltip("Zone 컨트롤러. 비워두면 씬에서 자동으로 찾는다.")]
        [SerializeField] private CameraZoneController controller;

        [Tooltip("Confiner를 등록할 CameraRig. 비워두면 씬에서 자동으로 찾는다.")]
        [SerializeField] private CameraRig cameraRig;

        public int Priority => priority;
        public float BlendTime => blendTime;

        public bool OverrideDistance => overrideDistance;
        public float Distance => distance;

        public bool OverridePitchOffset => overridePitchOffset;
        public float PitchOffset => pitchOffset;

        public bool OverrideYawOffset => overrideYawOffset;
        public float YawOffset => yawOffset;

        public bool OverrideHeightOffset => overrideHeightOffset;
        public float HeightOffset => heightOffset;

        public bool OverrideFollowOffset => overrideFollowOffset;
        public Vector3 FollowOffset => followOffset;

        public bool OverrideConfiner => overrideConfiner;

        /// <summary>실효 Confiner 볼륨. confinerVolume이 지정돼 있으면 그것을, 아니면 이 존의 Trigger Collider를 반환.</summary>
        public Collider EffectiveConfinerVolume
            => confinerVolume != null ? confinerVolume : GetComponent<Collider>();

        private void Reset()
        {
            // Zone 콜라이더는 항상 Trigger
            Collider col = GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;
        }

        private CameraZoneController ResolveController()
        {
            if (controller == null)
                controller = FindFirstObjectByType<CameraZoneController>();
            return controller;
        }

        private CameraRig ResolveRig()
        {
            if (cameraRig == null)
                cameraRig = FindFirstObjectByType<CameraRig>();
            // defaultConfinerVolume is the scene-owned always-on boundary (CameraConfinerBounds).
            // A zone must NOT overwrite it, or the confiner never releases after the zone exits.
            return cameraRig;
        }


        private static bool IsPlayer(Collider other)
        {
            // CharacterMovement는 컴포넌트가 아니라 Character 내부 일반 클래스이므로
            // GetComponent 대상이 될 수 없다. 실제 컴포넌트인 Character로 찾고,
            // 적도 Character이므로 GameManager의 플레이어와 동일한지로 선별한다.
            Character character = other.GetComponentInParent<Character>();
            if (character == null)
                return false;

            GameManager gm = GameManager.Instance;
            return gm != null && gm.Player == character;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsPlayer(other))
                return;

            CameraZoneController zoneController = ResolveController();
            if (zoneController != null)
                zoneController.PushZone(this);

            if (overrideConfiner)
            {
                CameraRig rig = ResolveRig();
                if (rig != null)
                    rig.AssignConfiner(EffectiveConfinerVolume);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsPlayer(other))
                return;

            CameraZoneController zoneController = ResolveController();
            if (zoneController != null)
                zoneController.PopZone(this);

            CameraRig rig = ResolveRig();
            if (rig != null)
                rig.ReleaseConfiner(EffectiveConfinerVolume);
        }

        private void OnDisable()
        {
            if (controller != null)
                controller.PopZone(this);
            if (cameraRig != null)
                cameraRig.ReleaseConfiner(EffectiveConfinerVolume);
        }
    }
}
