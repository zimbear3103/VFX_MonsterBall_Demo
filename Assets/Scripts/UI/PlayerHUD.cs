using UnityEngine;
using MoreMountains.Tools;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 플레이어 화면 고정 HUD — HP/FP/투혼 바 (구버전 AiaraGUIManager + FPBars + BattleSpiritBarWidget 이관).
    /// 값은 Character 이벤트(OnHealthChanged/OnFPChanged/OnTouhonChanged)로 push 받는다(폴링 없음).
    /// 위젯 참조가 비어 있으면 런타임에 기본 바를 조립한다(아트 교체 시 인스펙터 참조 할당으로 대체).
    /// </summary>
    public class PlayerHUD : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("표시 대상 캐릭터. 비우면 GameManager.Player 자동 바인딩")]
        [SerializeField] private Character target;

        [Header("Widgets")]// (비우면 런타임 기본 바 자동 생성)
        [SerializeField] private ResourceBarWidget hpBar;
        [SerializeField] private ResourceBarWidget fpBar;
        [SerializeField] private ResourceBarWidget touhonBar;

        [Header("Legacy Art Widgets")]// (구버전 아트 캔버스 바인딩 — 할당 시 기본 바 자동 생성 안 함)
        [Tooltip("구버전 아트 HP 바 (MMProgressBar). 할당 시 우선 사용")]
        [SerializeField] private MMProgressBar hpBarLegacy;
        [Tooltip("구버전 아트 FP 바 (MMProgressBar)")]
        [SerializeField] private MMProgressBar fpBarLegacy;
        [Tooltip("구버전 아트 투혼 위젯 (BattleSpiritBarWidget — 바+수치+마커+가득참 연출)")]
        [SerializeField] private Aiara.BattleSpiritBarWidget touhonWidgetLegacy;

        private bool HasLegacyWidgets => hpBarLegacy != null || fpBarLegacy != null || touhonWidgetLegacy != null;

        private Character _bound;
        private float _retryTimer;

        private void Start() => TryBind();

        private void Update()
        {
            // Player가 늦게 생기는 경우(멀티씬 로딩) 대비 재시도
            if (_bound == null)
            {
                _retryTimer -= Time.deltaTime;
                if (_retryTimer <= 0f)
                {
                    _retryTimer = 0.5f;
                    TryBind();
                }
            }
        }

        private void TryBind()
        {
            var t = target != null ? target
                : (GameManager.Instance != null ? GameManager.Instance.Player : null);
            if (t == null) return;

            EnsureWidgets();
            Bind(t);
        }

        /// <summary>대상 교체 바인딩 (Possess 교체 등 외부 호출 가능).</summary>
        public void Bind(Character character)
        {
            Unbind();
            _bound = character;
            if (_bound == null) return;

            _bound.OnHealthChanged += OnHP;
            _bound.OnFPChanged += OnFP;
            _bound.OnTouhonChanged += OnTouhon;

            // 초기값 즉시 반영
            OnHP(_bound.CurrentHP, _bound.MaxHP);
            OnFP(_bound.CurrentFP, _bound.MaxFP);
            OnTouhon(_bound.CurrentTouhon, _bound.MaxTouhon);
            if (touhonBar != null && _bound.Combat != null)
                touhonBar.SetMarkerRatio(_bound.Combat.TouhonResetRatio);

            // 투혼 미사용 캐릭터면 투혼 바 숨김
            if (touhonBar != null) touhonBar.gameObject.SetActive(_bound.MaxTouhon > 0f);
        }

        public void Unbind()
        {
            if (_bound == null) return;
            _bound.OnHealthChanged -= OnHP;
            _bound.OnFPChanged -= OnFP;
            _bound.OnTouhonChanged -= OnTouhon;
            _bound = null;
        }

        private void OnDestroy() => Unbind();

        private void OnHP(float cur, float max)
        {
            if (hpBar != null) hpBar.Push(cur, max);
            if (hpBarLegacy != null) hpBarLegacy.UpdateBar(cur, 0f, max); // 구버전 HealthBar 경로와 동일(lerp+delayed)
        }

        private void OnFP(float cur, float max)
        {
            if (fpBar != null) fpBar.Push(cur, max);
            if (fpBarLegacy != null) fpBarLegacy.SetBar01(max > 0f ? cur / max : 0f); // 구버전 UpdateFPBars와 동일(즉시)
        }

        private void OnTouhon(float cur, float max)
        {
            if (touhonBar != null) touhonBar.Push(cur, max);
            if (touhonWidgetLegacy != null)
            {
                float markerRatio = _bound != null && _bound.Combat != null ? _bound.Combat.TouhonResetRatio : 0f;
                touhonWidgetLegacy.Push(max > 0f ? cur / max : 0f, cur, markerRatio); // 구버전 UpdateBattleSpiritBars와 동일
            }
        }

        /// <summary>기본 HUD 조립 — 좌상단 HP(적록)/FP(청)/투혼(주황+마커+수치).</summary>
        private void EnsureWidgets()
        {
            if (HasLegacyWidgets) return; // 아트 캔버스 할당 시 기본 바 생성 안 함
            if (hpBar != null || fpBar != null || touhonBar != null) return;

            var canvas = HUDBuilder.CreateOverlayCanvas(transform, "PlayerHUDCanvas", 10);
            var root = canvas.transform;
            Vector2 anchor = new Vector2(0f, 1f); // 좌상단

            var back = new Color(0f, 0f, 0f, 0.55f);
            hpBar = HUDBuilder.CreateBar(root, "HPBar", new Vector2(320f, 22f), new Vector2(40f, -40f),
                anchor, anchor, anchor, back, new Color(0.85f, 0.2f, 0.2f), withText: true);
            fpBar = HUDBuilder.CreateBar(root, "FPBar", new Vector2(320f, 12f), new Vector2(40f, -66f),
                anchor, anchor, anchor, back, new Color(0.25f, 0.6f, 0.95f));
            touhonBar = HUDBuilder.CreateBar(root, "TouhonBar", new Vector2(320f, 12f), new Vector2(40f, -82f),
                anchor, anchor, anchor, back, new Color(0.95f, 0.55f, 0.15f), withText: true, withMarker: true);
            // 투혼: 가득참(잠재능력 가능) = 밝은 금색, 0(허주) = 보라 회색 (구버전 Full/GuardBreak Indicator 색 단순화)
            touhonBar.SetColors(new Color(0.95f, 0.55f, 0.15f), new Color(1f, 0.85f, 0.2f), new Color(0.45f, 0.35f, 0.55f));
        }
    }
}
