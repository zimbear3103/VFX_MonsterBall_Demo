using System.Collections.Generic;
using MoreMountains.Tools;
using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 대화 로그창 — <see cref="DialogueLog"/>가 모아둔 대사를 화면에 띄우고 닫는다.
    /// 대화창 UI 프리팹 안에 있으며, 배선은 <b>Tools/Aiara/대화 로그 셋업</b> 메뉴가 해준다.
    ///
    /// 여는 버튼은 패드 <b>North</b>(엑스박스 Y / 듀얼센스 △). 한 번 더 누르면 닫힌다.
    /// 장치를 직접 읽는 이유는 <see cref="DialogueContinueInput"/>과 같다 — 대화 중에는
    /// <see cref="DialogueControlLock"/>이 PlayerController를 꺼두어 InputAction 경로가 죽어 있다.
    ///
    /// <b>이 컴포넌트가 붙은 오브젝트는 계속 켜져 있어야 한다.</b> 껐다 켜는 것은 자식인
    /// <see cref="Window"/>다 — 꺼진 오브젝트에서는 Update가 돌지 않아 여는 입력을 받을 수 없다.
    ///
    /// 열려 있는 동안은 대사가 넘어가지 않고(<see cref="IsOpen"/>을 진행 입력이 본다),
    /// 대화창(자막·선택지) 패널도 감춘다 — 닫으면 그대로 돌아온다.
    /// 대화가 끝나면 저절로 닫힌다 — 로그창만 남아 게임 위에 떠 있으면 빠져나갈 길이 없다.
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/Dialogue Log Panel")]
    [DisallowMultipleComponent]
    public class DialogueLogPanel : MonoBehaviour
    {
        [MMInspectorGroup("배선", true, 100)]

        [Tooltip("열고 닫을 창 오브젝트. 이 컴포넌트가 붙은 오브젝트가 아니라 그 자식이어야 한다.")]
        public GameObject Window;

        [Tooltip("대사가 들어갈 텍스트. 스크롤 뷰의 Content다.")]
        public TMP_Text Body;

        [Tooltip("스크롤 뷰. 열 때 맨 아래(가장 최근 대사)로 내려준다.")]
        public ScrollRect Scroll;

        [MMInspectorGroup("여는 입력", true, 101)]

        [Tooltip("패드 North 버튼(엑스박스 Y / 듀얼센스 △)으로 로그창을 열고 닫는다.")]
        public bool UseNorthButton = true;

        [Tooltip("로그창을 열고 닫을 키보드 키. 비워두면 키보드로는 못 연다.")]
        public List<Key> ToggleKeys = new List<Key> { Key.Tab };

        [MMInspectorGroup("보이기", true, 102)]

        [Tooltip("이름 줄의 서식. {0}에 말한 사람 이름이 들어간다. 비우면 이름을 넣지 않는다.")]
        public string NameFormat = "<b><color=#FFD37A>{0}</color></b>";

        [Tooltip("대사 사이에 넣을 글. 기본은 빈 줄 하나.")]
        public string Separator = "\n\n";

        [Tooltip("기록이 하나도 없을 때 보여줄 글.")]
        public string EmptyMessage = "아직 지나간 대사가 없습니다.";

        [Tooltip("스크롤 속도(초당 화면 비율). 패드 왼쪽 스틱·십자키, 키보드 위아래로 굴린다.")]
        public float ScrollSpeed = 1.2f;

        [Tooltip("로그창이 열려 있는 동안 대화창(자막·선택지) 패널을 감춘다. 닫으면 원래대로 돌아온다.")]
        public bool HidePanelsWhileOpen = true;

        /// <summary>지금 로그창이 열려 있는가. 진행 입력이 이걸 보고 비켜준다.</summary>
        public static bool IsOpen { get; private set; }

        protected bool _open;

        /// <summary>감춰둔 대화창 패널과 감추기 전 값. 감춘 것만 되돌린다.</summary>
        protected struct HiddenPanel
        {
            public CanvasGroup Group;
            public float Alpha;
            public bool BlocksRaycasts;
        }

        protected readonly List<HiddenPanel> _hiddenPanels = new List<HiddenPanel>();

        /// <summary>
        /// 도메인 리로드를 꺼둔 프로젝트라 정적 값이 지난 플레이에서 살아남는다.
        /// 열린 채로 플레이를 멈추면 다음 판에서 대사가 영영 안 넘어간다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsOpen = false;
        }

        protected virtual void Awake()
        {
            ApplyOpen(false);
        }

        protected virtual void OnDisable()
        {
            // 대화창 UI가 통째로 꺼지는 경우. 열린 상태로 남으면 진행 입력이 계속 막힌다.
            ApplyOpen(false);
        }

        protected virtual void Update()
        {
            bool conversationActive = DialogueManager.instance != null && DialogueManager.IsConversationActive;

            if (!conversationActive)
            {
                if (_open)
                {
                    ApplyOpen(false);
                }

                return;
            }

            if (TogglePressed())
            {
                ApplyOpen(!_open);
            }

            if (_open)
            {
                ScrollByInput();
            }
        }

        /// <summary>로그창을 연다. 열면서 지금까지의 기록으로 내용을 새로 만든다.</summary>
        public virtual void Open() => ApplyOpen(true);

        public virtual void Close() => ApplyOpen(false);

        protected virtual void ApplyOpen(bool open)
        {
            _open = open;
            IsOpen = open;

            if (open)
            {
                Rebuild();
                CollectPanels();
            }
            else
            {
                RestorePanels();
            }

            if (Window != null && Window.activeSelf != open)
            {
                Window.SetActive(open);
            }

            if (open)
            {
                ScrollToBottom();
                ApplyPanelHiding();
            }
        }

        /// <summary>
        /// 감출 대화창 패널을 모아둔다. 지금 꺼져 있는 패널도 함께 담는다 —
        /// 로그창이 열린 뒤에 열리는 패널(선택지 메뉴 등)도 같이 감춰야 하기 때문이다.
        /// </summary>
        protected virtual void CollectPanels()
        {
            _hiddenPanels.Clear();

            if (!HidePanelsWhileOpen)
            {
                return;
            }

            var ui = DialogueManager.dialogueUI as StandardDialogueUI;
            if (ui == null || ui.conversationUIElements == null)
            {
                return;
            }

            var elements = ui.conversationUIElements;

            AddPanel(elements.mainPanel != null ? elements.mainPanel.gameObject : null);

            if (elements.subtitlePanels != null)
            {
                for (int i = 0; i < elements.subtitlePanels.Length; i++)
                {
                    var panel = elements.subtitlePanels[i];
                    AddPanel(panel != null ? panel.gameObject : null);
                }
            }

            if (elements.menuPanels != null)
            {
                for (int i = 0; i < elements.menuPanels.Length; i++)
                {
                    var panel = elements.menuPanels[i];
                    AddPanel(panel != null ? panel.gameObject : null);
                }
            }
        }

        /// <summary>
        /// 감출 대상 하나를 담는다. 로그창 자신(과 그 부모)은 건드리지 않는다 —
        /// 대화창 UI 루트가 mainPanel이면 로그창까지 같이 사라지기 때문이다.
        /// </summary>
        protected virtual void AddPanel(GameObject target)
        {
            if (target == null || transform.IsChildOf(target.transform))
            {
                return;
            }

            CanvasGroup group = target.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = target.AddComponent<CanvasGroup>();
            }

            for (int i = 0; i < _hiddenPanels.Count; i++)
            {
                if (_hiddenPanels[i].Group == group)
                {
                    return;
                }
            }

            _hiddenPanels.Add(new HiddenPanel
            {
                Group = group,
                Alpha = group.alpha,
                BlocksRaycasts = group.blocksRaycasts,
            });
        }

        /// <summary>
        /// 모아둔 패널을 감춘다. <b>매 프레임 다시 건다</b> — Pixel Crushers의 UIPanel은 Animator로
        /// CanvasGroup 알파를 쓰기 때문에, 한 번만 0으로 내려두면 다음 프레임에 애니메이터가 도로 올린다.
        /// (LateUpdate에서 부르므로 그 프레임 애니메이터보다 나중에 쓴다.)
        /// </summary>
        protected virtual void ApplyPanelHiding()
        {
            for (int i = 0; i < _hiddenPanels.Count; i++)
            {
                CanvasGroup group = _hiddenPanels[i].Group;
                if (group == null)
                {
                    continue;
                }

                group.alpha = 0f;

                // 감춘 선택지 버튼이 클릭되면 안 된다.
                group.blocksRaycasts = false;
            }
        }

        /// <summary>감췄던 패널을 감추기 전 값으로 되돌린다.</summary>
        protected virtual void RestorePanels()
        {
            for (int i = 0; i < _hiddenPanels.Count; i++)
            {
                HiddenPanel hidden = _hiddenPanels[i];
                if (hidden.Group == null)
                {
                    continue;
                }

                hidden.Group.alpha = hidden.Alpha;
                hidden.Group.blocksRaycasts = hidden.BlocksRaycasts;
            }

            _hiddenPanels.Clear();
        }

        /// <summary>애니메이터가 알파를 되돌려 놓지 못하도록 프레임 끝에 다시 건다.</summary>
        protected virtual void LateUpdate()
        {
            if (_open)
            {
                ApplyPanelHiding();
            }
        }

        /// <summary>기록을 글로 만들어 넣는다.</summary>
        public virtual void Rebuild()
        {
            if (Body == null)
            {
                return;
            }

            DialogueLog log = DialogueLog.Instance;

            Body.text = log == null || log.IsEmpty
                ? EmptyMessage
                : log.BuildText(NameFormat, Separator);
        }

        /// <summary>
        /// 가장 최근 대사가 보이도록 맨 아래로 내린다.
        ///
        /// 레이아웃이 아직 갱신되지 않은 상태에서 위치를 건드리면 다음 프레임에 도로 튀어 오르므로,
        /// 크기를 먼저 확정시킨 뒤 내린다.
        /// </summary>
        protected virtual void ScrollToBottom()
        {
            if (Scroll == null)
            {
                return;
            }

            if (Body != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(Body.rectTransform);
            }

            Canvas.ForceUpdateCanvases();
            Scroll.verticalNormalizedPosition = 0f;
        }

        protected virtual bool TogglePressed()
        {
            return (UseNorthButton && NorthButtonPressed()) || KeyboardPressed();
        }

        /// <summary>
        /// 패드 North 버튼. <see cref="Gamepad.current"/>만 보지 않고 붙어 있는 패드를 전부 훑는다 —
        /// current는 "마지막으로 입력이 들어온 패드"라 그 판에서 패드를 안 건드렸으면 null일 수 있다.
        /// </summary>
        protected virtual bool NorthButtonPressed()
        {
            var gamepads = Gamepad.all;

            for (int i = 0; i < gamepads.Count; i++)
            {
                Gamepad gamepad = gamepads[i];
                if (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame)
                {
                    return true;
                }
            }

            return false;
        }

        protected virtual bool KeyboardPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || ToggleKeys == null)
            {
                return false;
            }

            for (int i = 0; i < ToggleKeys.Count; i++)
            {
                Key key = ToggleKeys[i];
                if (key != Key.None && keyboard[key].wasPressedThisFrame)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 스틱·십자키·키보드 위아래로 굴린다.
        ///
        /// EventSystem의 스크롤 입력에 기대지 않는다 — 대화 중에는 포인터가 로그창 위에 있으리라는
        /// 보장이 없고, 패드로는 애초에 스크롤 이벤트가 오지 않는다. 시간은 타임스케일과 무관하게 흐른다.
        /// </summary>
        protected virtual void ScrollByInput()
        {
            if (Scroll == null)
            {
                return;
            }

            float input = 0f;

            var gamepads = Gamepad.all;
            for (int i = 0; i < gamepads.Count; i++)
            {
                Gamepad gamepad = gamepads[i];
                if (gamepad == null)
                {
                    continue;
                }

                input += gamepad.leftStick.ReadValue().y;
                input += gamepad.dpad.ReadValue().y;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.isPressed) input += 1f;
                if (keyboard.downArrowKey.isPressed) input -= 1f;
            }

            if (Mathf.Abs(input) < 0.1f)
            {
                return;
            }

            float delta = Mathf.Clamp(input, -1f, 1f) * ScrollSpeed * Time.unscaledDeltaTime;
            Scroll.verticalNormalizedPosition =
                Mathf.Clamp01(Scroll.verticalNormalizedPosition + delta);
        }
    }
}
