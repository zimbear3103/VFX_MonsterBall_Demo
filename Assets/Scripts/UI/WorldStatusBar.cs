using UnityEngine;
using UnityEngine.UI;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 적 머리 위 월드 바 — HP(+투혼 사용 캐릭터면 투혼) 표시 (구버전 MMHealthBar/MMBattleSpiritBar 이관).
    /// Character에 부착하면 런타임에 World Space Canvas를 조립하고 이벤트로만 갱신한다.
    /// 사망 시 숨김(구버전 HideBarAtZero), 카메라 빌보드.
    /// </summary>
    public class WorldStatusBar : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("캐릭터 기준 바 높이 오프셋(m)")]
        [SerializeField] private float heightOffset = 2.3f;
        [Tooltip("바 폭(m)")]
        [SerializeField] private float barWidth = 1.4f;

        private Character _character;
        private Transform _root;
        private ResourceBarWidget _hpBar;
        private ResourceBarWidget _touhonBar;
        private Camera _camera;
        // 시각(모델) 앵커 — 잡기/다운처럼 본 변위로 모델이 루트에서 떨어질 때 바가 몸을 따라가게 한다.
        // XZ만 앵커를 쓰고 높이는 루트 기준 유지(눕는 모션에 바가 오르내리지 않게).
        private Transform _visualAnchor;

        /// <summary>바 높이 오프셋 설정 (EnemyBarManager 등 외부 부착 시 사용).</summary>
        public void SetHeightOffset(float height) => heightOffset = height;

        
private void Awake()
        {
            _character = GetComponentInParent<Character>();
        }

        private void Start()
        {
            if (_character == null) { enabled = false; return; }
            ResolveVisualAnchor();
            BuildBar();
            _character.OnHealthChanged += OnHP;
            _character.OnTouhonChanged += OnTouhon;
            OnHP(_character.CurrentHP, _character.MaxHP);
            if (_touhonBar != null) OnTouhon(_character.CurrentTouhon, _character.MaxTouhon);
        }

        private void OnDestroy()
        {
            if (_character == null) return;
            _character.OnHealthChanged -= OnHP;
            _character.OnTouhonChanged -= OnTouhon;
        }

        /// <summary>힙 본 → 스킨 루트본 순으로 시각 앵커 탐색. 없으면 루트만 추종.</summary>
        private void ResolveVisualAnchor()
        {
            var animator = _character.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
                _visualAnchor = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (_visualAnchor == null)
            {
                var smr = _character.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null) _visualAnchor = smr.rootBone != null ? smr.rootBone : smr.transform;
            }
        }

        private void LateUpdate()
        {
            if (_root == null) return;
            Vector3 basePos = _character.transform.position;
            if (_visualAnchor != null)
            {
                basePos.x = _visualAnchor.position.x;
                basePos.z = _visualAnchor.position.z;
            }
            _root.position = basePos + Vector3.up * heightOffset;
            if (_camera == null) _camera = Camera.main;
            if (_camera != null)
                _root.rotation = Quaternion.LookRotation(_camera.transform.forward, _camera.transform.up);
        }

        private void OnHP(float cur, float max)
        {
            if (_hpBar != null) _hpBar.Push(cur, max);
            // 사망 시 바 숨김 (구버전 HideBarAtZero — 부활 시 재표시)
            if (_root != null) _root.gameObject.SetActive(cur > 0f);
        }

        private void OnTouhon(float cur, float max)
        {
            if (_touhonBar != null) _touhonBar.Push(cur, max);
        }

        private void BuildBar()
        {
            var go = new GameObject("WorldStatusBar", typeof(RectTransform), typeof(Canvas));
            // 캐릭터 회전(Facing 플립)에 영향받지 않도록 씬 루트에 두고 위치만 추종한다.
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(100f, 14f);
            rt.localScale = Vector3.one * (barWidth / 100f);
            _root = go.transform;

            var back = new Color(0f, 0f, 0f, 0.6f);
            Vector2 mid = new Vector2(0.5f, 0.5f);
            _hpBar = HUDBuilder.CreateBar(_root, "HP", new Vector2(100f, 8f), new Vector2(0f, 3f),
                mid, mid, mid, back, new Color(0.85f, 0.25f, 0.2f));
            bool useTouhon = _character.MaxTouhon > 0f;
            if (useTouhon)
                _touhonBar = HUDBuilder.CreateBar(_root, "Touhon", new Vector2(100f, 4f), new Vector2(0f, -4f),
                    mid, mid, mid, back, new Color(0.95f, 0.55f, 0.15f));
        }
    }
}
