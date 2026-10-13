using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 카메라 추적 기준점 — 캐릭터 루트가 아닌 '보이는 몸'(힙 본의 XZ, 높이는 루트)을 따라가는
    /// 자식 Transform(anchor)을 매 프레임 갱신한다.
    ///
    /// 잡기/처형/뒤로던지기처럼 연출 이동이 본(bone) 변위로 이뤄지는 동안 루트는 제자리에 남는다.
    /// CameraRig.playerTarget이 루트를 가리키면 카메라는 멈춰 있고 모델만 화면 밖으로 밀려나므로,
    /// Start에서 rig의 추적 대상을 이 앵커로 교체한다(CombatCameraController도 rig.PlayerTarget을 쓰므로 함께 반영).
    /// 앵커는 루트의 직계 자식이라 FollowCameraNode의 Facing 조회(GetComponentInParent&lt;Character&gt;)와
    /// PivotCameraNode의 스플라인 기준점도 그대로 동작한다.
    ///
    /// 실행 순서 50: CharacterActionGrab LateUpdate(-15, 루트 보정) 이후 · CombatCameraController(90) · CameraRig(100) 이전.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Yeolha/Camera/Character Camera Anchor")]
    public class CharacterCameraAnchor : MonoBehaviour
    {
        [Header("Anchor")]
        [Tooltip("카메라가 추적할 앵커 Transform. 비우면 런타임에 자식 'CameraAnchor'를 생성한다")]
        [SerializeField] private Transform anchor;

        [Tooltip("시각 위치 기준 본. 비우면 CharacterVisualBone(Humanoid Hips → 힙/골반 이름 본 → SkinnedMeshRenderer rootBone) 순으로 찾는다")]
        [SerializeField] private Transform visualBone;

        [Header("Filter")]
        [Tooltip("루트-본 XZ 변위가 이 값(m) 이하이면 무시하고, 넘는 만큼만 앵커에 반영한다(걷기/대기 힙 흔들림 필터, 연속 감쇠)")]
        [SerializeField, Min(0f)] private float deadZone = 0.1f;

        [Tooltip("앵커 변위 상한(m). 본이 비정상적으로 멀리 튀는 프레임 방어. 0 = 무제한")]
        [SerializeField, Min(0f)] private float maxOffset = 6f;

        [Header("Camera Rig")]
        [Tooltip("Start 시 CameraRig.playerTarget이 이 캐릭터 루트(또는 비어 있음)이면 앵커로 교체한다")]
        [SerializeField] private bool rebindCameraRig = true;

        private CameraRig _rig;
        private bool _boneResolved;
        private Transform _visualRoot;

        /// <summary>카메라 추적 앵커 Transform.</summary>
        public Transform Anchor => anchor;

        /// <summary>본 계층의 루트(Animator/스킨 메시가 있는 Transform). 이 아래의 Transform은 이미 모션을 따라 움직인다.</summary>
        public Transform VisualRoot
        {
            get
            {
                if (_visualRoot == null)
                {
                    var animator = GetComponentInChildren<Animator>(true);
                    _visualRoot = animator != null ? animator.transform : null;
                    if (_visualRoot == null)
                    {
                        var smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
                        if (smr != null) _visualRoot = smr.transform.parent != null ? smr.transform.parent : smr.transform;
                    }
                }
                return _visualRoot;
            }
        }

        /// <summary>현재 프레임의 루트→시각 XZ 변위(데드존 적용 후).</summary>
        public Vector3 CurrentOffset { get; private set; }

        private void Awake()
        {
            EnsureAnchor();
        }

        private void Start()
        {
            if (rebindCameraRig)
                TryRebindRig();
            UpdateAnchor();
        }

        private void OnDisable()
        {
            // 리그가 앵커를 보고 있으면 루트로 되돌려 파괴/비활성 시 추적 유실 방지.
            if (_rig != null && anchor != null && _rig.PlayerTarget == anchor)
                _rig.SetPlayerTarget(transform);
        }

        private void LateUpdate()
        {
            UpdateAnchor();
        }

        private void EnsureAnchor()
        {
            if (anchor == null)
            {
                Transform existing = transform.Find("CameraAnchor");
                if (existing == null)
                {
                    var go = new GameObject("CameraAnchor");
                    existing = go.transform;
                    existing.SetParent(transform, false);
                }
                anchor = existing;
            }

            // 앵커는 반드시 루트의 직계 자식 — 본 아래에 두면 본 스케일/미러 영향을 받고 의도가 흐려진다.
            if (anchor.parent != transform)
                anchor.SetParent(transform, true);
        }

        private void TryRebindRig()
        {
            _rig = FindFirstObjectByType<CameraRig>();
            if (_rig == null || anchor == null) return;

            Transform current = _rig.PlayerTarget;
            // 루트뿐 아니라 이 캐릭터 안의 아무 Transform(본 등)을 가리키고 있어도 앵커로 통일한다.
            bool isInSelf = current != null && current != anchor && (current == transform || current.IsChildOf(transform));
            bool isUnset = current == null && GameManager.Instance != null && GameManager.Instance.Player != null
                           && GameManager.Instance.Player.transform == transform;
            if (isInSelf || isUnset)
                _rig.SetPlayerTarget(anchor);
        }

        private Transform ResolveBone()
        {
            if (visualBone != null) return visualBone;
            if (_boneResolved) return null;
            _boneResolved = true;

            // CharacterVisualBone: Humanoid Hips → 힙/골반 이름 본(jnt_hip) → 스킨 루트본 폴백 (2026-09-09).
            // 이전엔 첫 SkinnedMeshRenderer.rootBone(=jnt_root, 연출 중 고정)을 잡아 잡기 중 카메라가 몸을 따라가지 못했다.
            visualBone = CharacterVisualBone.Resolve(transform);
            return visualBone;
        }

        private void UpdateAnchor()
        {
            if (anchor == null) return;

            Transform bone = ResolveBone();
            Vector3 root = transform.position;
            Vector3 offset = Vector3.zero;

            if (bone != null)
            {
                offset = bone.position - root;
                offset.y = 0f;

                float mag = offset.magnitude;
                if (mag <= deadZone || mag < 0.0001f)
                    offset = Vector3.zero;
                else
                    offset *= (mag - deadZone) / mag;

                if (maxOffset > 0f && offset.sqrMagnitude > maxOffset * maxOffset)
                    offset = offset.normalized * maxOffset;
            }

            CurrentOffset = offset;
            anchor.position = root + offset;
        }
    }
}
