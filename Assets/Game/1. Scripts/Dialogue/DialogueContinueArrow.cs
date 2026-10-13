using MoreMountains.Tools;
using PixelCrushers;
using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 대사가 <b>다 나온 뒤</b> 자막 패널 우측 하단에서 위아래로 까딱이는 화살표 —
    /// "누르면 다음으로 넘어간다"는 표시.
    /// 자막 패널마다 하나씩 붙는다. 배선은 <b>Tools/Aiara/대화 진행 화살표 셋업</b> 메뉴가 해준다.
    ///
    /// <b>왜 Pixel Crushers의 컨티뉴 버튼을 안 쓰는가</b> — 이 프로젝트는 진행 입력을
    /// <see cref="DialogueContinueInput"/>이 장치에서 직접 받고, 화면의 컨티뉴 버튼은 떼어 두었다
    /// (대화창 UI 셋업이 그렇게 맞춘다). 그래서 버튼이 아니라 <b>표시</b>만 따로 둔다 —
    /// 누를 수 있는 물건이 아니므로 클릭 판정도 잡지 않는다.
    ///
    /// 뜨는 조건은 "지금 넘길 수 있는가"와 같아야 한다. 하나라도 어긋나면 <b>넘어가지도 않는데 떠 있는</b>
    /// 표시가 되어 오히려 헷갈린다:
    ///   - 대화 중이고, 이 패널이 열려 있고, <b>지금 말하고 있는 패널</b>일 것(<see cref="RequireFocus"/>)
    ///   - 타자기가 글자를 다 찍었을 것 — 찍는 중에는 다음 대사가 아니라 '즉시 표시'가 되므로 아직 아니다
    ///   - 선택지 메뉴가 떠 있지 않을 것(그때는 고르는 것이지 넘기는 것이 아니다)
    ///   - 대화 로그창이 열려 있지 않을 것(열려 있는 동안은 진행 입력이 막힌다)
    ///
    /// 움직임은 타임스케일과 무관하게 흐른다 — 대화 중 슬로우모션·일시정지가 걸려도 표시는 제 속도로
    /// 움직인다(대화창의 다른 연출과 같은 규칙).
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/Dialogue Continue Arrow")]
    [DisallowMultipleComponent]
    public class DialogueContinueArrow : MonoBehaviour
    {
        [MMInspectorGroup("배선", true, 100)]

        [Tooltip("깜빡일 화살표 오브젝트. 비우면 아무것도 하지 않는다.")]
        public GameObject Arrow;

        [Tooltip("어느 자막 패널의 상태를 볼지. 비우면 이 오브젝트에서 찾는다.")]
        public StandardUISubtitlePanel Panel;

        [MMInspectorGroup("움직임", true, 101)]

        [Tooltip("위아래로 까딱이는 속도. 2면 1초에 한 번 왕복한다.")]
        [Range(0.2f, 8f)]
        public float BobSpeed = 2f;

        [Tooltip("위아래로 움직이는 폭(픽셀). 0이면 제자리에 가만히 떠 있는다.")]
        [Range(0f, 20f)]
        public float BobPixels = 4f;

        [MMInspectorGroup("표시 조건", true, 102)]

        [Tooltip("지금 말하고 있는 패널에서만 띄운다. 끄면 열려 있는 패널마다 화살표가 뜬다.")]
        public bool RequireFocus = true;

        /// <summary>지금 화살표가 떠 있는가.</summary>
        public bool IsShown => Arrow != null && Arrow.activeSelf;

        protected CanvasGroup _group;
        protected RectTransform _rect;
        protected Vector2 _basePosition;
        protected bool _baseCaptured;

        protected virtual void Awake()
        {
            if (Panel == null)
            {
                Panel = GetComponent<StandardUISubtitlePanel>();
            }

            CaptureArrow();
            Show(false);
        }

        protected virtual void OnDisable()
        {
            Show(false);
        }

        protected virtual void Update()
        {
            bool show = ShouldShow();
            Show(show);

            if (show)
            {
                Animate();
            }
        }

        /// <summary>지금 "다음으로 넘길 수 있는" 상태인가.</summary>
        public virtual bool ShouldShow()
        {
            if (Arrow == null || Panel == null)
            {
                return false;
            }

            if (DialogueManager.instance == null || !DialogueManager.IsConversationActive)
            {
                return false;
            }

            // 로그창이 열려 있는 동안은 진행 입력이 막힌다. 넘어가지 않는데 깜빡이면 안 된다.
            if (DialogueLogPanel.IsOpen)
            {
                return false;
            }

            if (Panel.panelState != UIPanel.PanelState.Open)
            {
                return false;
            }

            if (RequireFocus && !Panel.hasFocus)
            {
                return false;
            }

            if (Panel.subtitleText == null || string.IsNullOrEmpty(Panel.subtitleText.text))
            {
                return false;
            }

            // 글자를 찍는 중이면 아직이다 — 그때 누르면 다음 대사가 아니라 '끝까지 즉시 표시'가 된다.
            AbstractTypewriterEffect typewriter = Panel.GetTypewriter();
            if (typewriter != null && typewriter.isPlaying)
            {
                return false;
            }

            // 선택지가 떠 있으면 고르는 차례다. 넘기는 표시를 띄우면 안 된다.
            return !IsResponseMenuOpen();
        }

        protected virtual bool IsResponseMenuOpen()
        {
            var standardUI = DialogueManager.dialogueUI as StandardDialogueUI;
            if (standardUI == null || standardUI.conversationUIElements == null)
            {
                return false;
            }

            var menuPanels = standardUI.conversationUIElements.menuPanels;
            if (menuPanels == null)
            {
                return false;
            }

            for (int i = 0; i < menuPanels.Length; i++)
            {
                if (menuPanels[i] != null && menuPanels[i].isOpen)
                {
                    return true;
                }
            }

            return false;
        }

        protected virtual void Show(bool show)
        {
            if (Arrow == null)
            {
                return;
            }

            if (Arrow.activeSelf != show)
            {
                Arrow.SetActive(show);
            }

            if (!show && _rect != null && _baseCaptured)
            {
                // 흔들리던 도중에 꺼지면 다음에 켤 때 그 자리에서 시작한다. 제자리로 돌려둔다.
                _rect.anchoredPosition = _basePosition;
            }
        }

        protected virtual void Animate()
        {
            if (_rect == null || !_baseCaptured || BobPixels <= 0f)
            {
                return;
            }

            float offset = Mathf.Sin(Time.unscaledTime * BobSpeed * Mathf.PI) * BobPixels;
            _rect.anchoredPosition = _basePosition + new Vector2(0f, offset);
        }

        /// <summary>
        /// 화살표의 제자리(움직임의 기준)를 기억하고, 클릭을 가로채지 않도록 막아둔다.
        ///
        /// CanvasGroup이 붙어 있으면 알파를 1로 세워둔다 — 예전 구성(깜빡이던 시절)에서 넘어온 값이
        /// 0에 가깝게 남아 있으면 화살표가 뜨긴 뜨는데 안 보이게 된다.
        /// </summary>
        protected virtual void CaptureArrow()
        {
            if (Arrow == null)
            {
                return;
            }

            _group = Arrow.GetComponent<CanvasGroup>();
            if (_group == null)
            {
                _group = Arrow.AddComponent<CanvasGroup>();
            }

            _group.alpha = 1f;

            // 누를 수 있는 물건이 아니다. 뒤에 있는 것의 클릭을 가로채지 않게 한다.
            _group.blocksRaycasts = false;
            _group.interactable = false;

            _rect = Arrow.transform as RectTransform;
            if (_rect != null)
            {
                _basePosition = _rect.anchoredPosition;
                _baseCaptured = true;
            }
        }
    }
}
