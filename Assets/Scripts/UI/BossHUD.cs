using UnityEngine;
using TMPro;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 보스 전용 화면 상단 HUD (구버전 BossUIController/BossUIBinder 이관 — 보스 미구현이라 바인딩 훅 중심).
    /// 기본은 숨김. 보스 등장 시 BossHUD.Show(character, name)로 바인딩하면 HP/투혼 바가 표시된다.
    /// 위젯 참조가 비어 있으면 런타임 기본 바를 조립한다.
    /// </summary>
    public class BossHUD : MonoBehaviour
    {
        public static BossHUD Instance { get; private set; }

        [Header("Widgets")]// (비우면 런타임 기본 바 자동 생성)
        [SerializeField] private ResourceBarWidget hpBar;
        [SerializeField] private ResourceBarWidget touhonBar;
        [SerializeField] private TMP_Text nameText;

        private Character _bound;
        private GameObject _panel;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; Unbind(); }

        /// <summary>보스 바인딩 + 표시. displayName 비우면 Ability.DisplayName 사용.</summary>
        public static void Show(Character boss, string displayName = "")
        {
            if (Instance == null || boss == null) return;
            Instance.Bind(boss, displayName);
        }

        /// <summary>보스 HUD 숨김 (사망/이탈).</summary>
        public static void Hide()
        {
            if (Instance == null) return;
            Instance.Unbind();
        }

        private void Bind(Character boss, string displayName)
        {
            Unbind();
            EnsurePanel();
            _bound = boss;
            _bound.OnHealthChanged += OnHP;
            _bound.OnTouhonChanged += OnTouhon;

            if (nameText != null)
                nameText.text = !string.IsNullOrEmpty(displayName) ? displayName
                    : (_bound.Ability != null ? _bound.Ability.DisplayName : _bound.name);

            OnHP(_bound.CurrentHP, _bound.MaxHP);
            OnTouhon(_bound.CurrentTouhon, _bound.MaxTouhon);
            if (touhonBar != null) touhonBar.gameObject.SetActive(_bound.MaxTouhon > 0f);
            if (_panel != null) _panel.SetActive(true);
        }

        private void Unbind()
        {
            if (_bound != null)
            {
                _bound.OnHealthChanged -= OnHP;
                _bound.OnTouhonChanged -= OnTouhon;
                _bound = null;
            }
            if (_panel != null) _panel.SetActive(false);
        }

        private void OnHP(float cur, float max)
        {
            if (hpBar != null) hpBar.Push(cur, max);
            if (cur <= 0f) Unbind(); // 보스 사망 시 자동 숨김
        }

        private void OnTouhon(float cur, float max) { if (touhonBar != null) touhonBar.Push(cur, max); }

        /// <summary>기본 패널 조립 — 화면 상단 중앙 이름 + HP + 투혼.</summary>
        private void EnsurePanel()
        {
            if (_panel != null) return;
            if (hpBar != null) { _panel = hpBar.transform.parent != null ? hpBar.transform.parent.gameObject : hpBar.gameObject; return; }

            var canvas = HUDBuilder.CreateOverlayCanvas(transform, "BossHUDCanvas", 11);
            _panel = canvas.gameObject;
            var root = canvas.transform;
            Vector2 top = new Vector2(0.5f, 1f);

            var nameGo = new GameObject("BossName", typeof(RectTransform), typeof(TextMeshProUGUI));
            nameGo.transform.SetParent(root, false);
            nameText = nameGo.GetComponent<TextMeshProUGUI>();
            nameText.fontSize = 28f;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.color = Color.white;
            nameText.raycastTarget = false;
            var nRt = nameText.rectTransform;
            nRt.anchorMin = top; nRt.anchorMax = top; nRt.pivot = top;
            nRt.sizeDelta = new Vector2(800f, 34f);
            nRt.anchoredPosition = new Vector2(0f, -28f);

            var back = new Color(0f, 0f, 0f, 0.55f);
            hpBar = HUDBuilder.CreateBar(root, "BossHPBar", new Vector2(720f, 20f), new Vector2(0f, -66f),
                top, top, top, back, new Color(0.75f, 0.1f, 0.15f), withText: true);
            touhonBar = HUDBuilder.CreateBar(root, "BossTouhonBar", new Vector2(720f, 8f), new Vector2(0f, -88f),
                top, top, top, back, new Color(0.95f, 0.55f, 0.15f));

            _panel.SetActive(false);
        }
    }
}
