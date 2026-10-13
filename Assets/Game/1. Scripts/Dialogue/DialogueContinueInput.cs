using System.Collections.Generic;
using MoreMountains.Tools;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 대화 중 다음 대사로 넘기는 입력을 받는다 — 패드 SouthButton(A), 키보드(Space/Enter),
    /// 그리고 화면 아무 데나 마우스 좌클릭.
    ///
    /// Dialogue Manager 오브젝트에 붙여두면 된다. 대화가 진행 중일 때만 동작한다.
    ///
    /// 왜 별도 컴포넌트인가 —
    /// Pixel Crushers는 컨티뉴 버튼을 UI 버튼 클릭이나 자체 레거시 입력(Submit)으로 받는데,
    /// 이 프로젝트는 새 Input System으로 패드를 쓰고 PC 쪽 USE_NEW_INPUT 디파인이 없다.
    /// 그래서 프로젝트 입력을 읽어 PC에 진행 신호를 넘겨주는 다리가 필요하다.
    /// 이게 있으면 화면의 컨티뉴 버튼은 필요 없다 — 패널의 Continue Button 참조를 비워두면 버튼은 뜨지 않고,
    /// 진행은 전부 이쪽으로 들어온다. (메뉴 Tools/Aiara/대화창 UI 셋업이 그렇게 맞춰준다.)
    ///
    /// 패드 입력은 <see cref="Gamepad"/>에서 직접 읽는다.
    /// 대화 중에는 <see cref="DialogueControlLock"/>이 PlayerController를 통째로 꺼두기 때문에,
    /// 프로젝트의 InputAction 경로로는 아무것도 들어오지 않는다 — 장치를 직접 보는 통로가 필요하다.
    ///
    /// Dialogue Manager의 Display Settings → Subtitle Settings → **Continue Button을 Always로**
    /// 두어야 PC가 입력을 기다린다. Never로 두면 시간이 지나면 저절로 넘어간다.
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/Dialogue Continue Input")]
    public class DialogueContinueInput : MonoBehaviour
    {
        [MMInspectorGroup("진행 입력", true, 100)]

        [Tooltip("패드 South 버튼(엑스박스 A / 듀얼센스 ✕)으로 대사를 넘긴다.")]
        public bool UseSouthButton = true;

        [Tooltip("키보드로 대사를 넘긴다. 어떤 키를 받을지는 아래 Continue Keys에서 정한다.")]
        public bool UseKeyboard = true;

        [Tooltip("대사를 넘길 키보드 키 목록. 비워두면 키보드로는 넘길 수 없다.")]
        public List<Key> ContinueKeys = new List<Key> { Key.Space, Key.Enter, Key.NumpadEnter };

        [Tooltip("화면 아무 데나 마우스 좌클릭으로 대사를 넘긴다. " +
                 "UI 위를 눌렀는지는 따지지 않는다 — 입력 장치를 직접 읽기 때문.")]
        public bool UseMouseClick = true;

        [Tooltip("대화가 시작된 직후 이만큼(초)은 입력을 무시한다. " +
                 "이벤트를 여는 데 쓴 버튼이 그대로 첫 대사를 넘겨버리는 것을 막는다.")]
        public float InputBlockAfterStart = 0.2f;

        [MMInspectorGroup("타자기", true, 101)]

        [Tooltip("타자기 효과가 글자를 찍는 중이면 첫 입력은 '끝까지 즉시 표시'로 쓰고, " +
                 "다음 입력에서 다음 대사로 넘어간다. 끄면 찍는 중이어도 곧바로 넘어간다.")]
        public bool FastForwardTypewriter = true;

        [MMInspectorGroup("디버그", true, 102)]

        [Tooltip("어떤 장치의 어떤 버튼이 대사를 넘겼는지 콘솔에 남긴다. " +
                 "패드가 안 먹을 때 '입력이 안 들어오는 것'인지 '들어오는데 안 넘어가는 것'인지 가른다.")]
        public bool DebugLog = false;

        protected float _acceptInputAt;
        protected bool _wasConversationActive;

        /// <summary>
        /// 이 시각(unscaled)까지는 진행 입력을 받지 않는다.
        ///
        /// 선택지를 고른 A 입력이 같은 프레임에 다음 대사까지 연달아 넘기는 것을 막기 위한 통로다 —
        /// <see cref="DialogueChoiceMenuPanel"/>이 확정 직전에 걸어준다. 메뉴가 열려 있는 동안은
        /// <see cref="IsResponseMenuOpen"/>이 알아서 비켜주지만, 확정하는 그 순간 메뉴가 닫히기 때문에
        /// 실행 순서에 따라 이 컴포넌트가 같은 입력을 한 번 더 볼 수 있다.
        /// </summary>
        protected static float _blockInputUntil;

        /// <summary>
        /// 유예로 인정하는 최대 길이(초).
        ///
        /// 진행 입력을 막는 것은 "방금 그 입력이 두 번 먹지 않게" 하는 한두 프레임짜리 장치다.
        /// 이보다 긴 유예가 남아 있다면 정상적으로 걸린 값이 아니라 <b>지난 플레이 세션에서 넘어온 값</b>이다
        /// (아래 <see cref="ResetStatics"/> 주석 참고). 그런 값은 버린다.
        /// </summary>
        public const float MaxBlockSeconds = 5f;

        /// <summary>진행 입력을 seconds초 동안 막는다. 이미 걸린 시간이 더 길면 그대로 둔다.</summary>
        public static void BlockInput(float seconds)
        {
            float clamped = Mathf.Clamp(seconds, 0f, MaxBlockSeconds);
            _blockInputUntil = Mathf.Max(_blockInputUntil, Time.unscaledTime + clamped);
        }

        /// <summary>
        /// 플레이를 시작할 때 static을 초기화한다.
        ///
        /// **없으면 대사가 안 넘어간다.** 이 프로젝트는 Enter Play Mode Options에서 도메인 리로드를 꺼두었다
        /// (Project Settings → Editor). 그러면 static 필드가 플레이를 멈춰도 살아남는 반면
        /// <see cref="Time.unscaledTime"/>은 0부터 다시 시작한다. <see cref="_blockInputUntil"/>은
        /// **절대 시각**이라, 이전 판에서 t=300초에 선택지를 골랐다면 다음 판에서는 300초 동안
        /// 모든 진행 입력이 조용히 무시된다 — "패드도 클릭도 안 먹는데 가끔은 되고, 재시작하면 또 달라지는"
        /// 증상이 정확히 이것이다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _blockInputUntil = 0f;
        }

        protected virtual void Update()
        {
            bool isActive = DialogueManager.instance != null && DialogueManager.IsConversationActive;

            // 대화가 막 시작된 프레임을 잡아 입력 유예를 건다.
            if (isActive && !_wasConversationActive)
            {
                _acceptInputAt = Time.unscaledTime + Mathf.Max(0f, InputBlockAfterStart);
            }

            _wasConversationActive = isActive;

            // 남은 유예가 비정상적으로 길면 지난 세션에서 넘어온 값이다. 리셋이 어떤 이유로 안 걸렸더라도
            // (빌드 경로, 다른 진입점 등) 여기서 스스로 풀려나게 한다 — 입력이 영영 막히는 것보다 낫다.
            if (_blockInputUntil - Time.unscaledTime > MaxBlockSeconds)
            {
                if (DebugLog)
                {
                    Debug.LogWarning($"[DialogueContinueInput] 지난 세션에서 넘어온 입력 유예를 버립니다 " +
                                     $"(남은 {_blockInputUntil - Time.unscaledTime:0.#}초).", this);
                }

                _blockInputUntil = 0f;
            }

            if (!isActive || Time.unscaledTime < _acceptInputAt || Time.unscaledTime < _blockInputUntil)
            {
                return;
            }

            // 대화 로그창이 열려 있으면 대사를 넘기지 않는다 — 읽는 중에 대화가 지나가버리면
            // 로그를 여는 의미가 없다. 닫는 것은 로그창 자신의 몫(North 버튼).
            if (DialogueLogPanel.IsOpen)
            {
                return;
            }

            if (ContinuePressed())
            {
                Continue();
            }
        }

        protected virtual bool ContinuePressed()
        {
            return (UseSouthButton && SouthButtonPressed())
                || (UseKeyboard && KeyboardPressed())
                || (UseMouseClick && MouseClicked());
        }

        /// <summary>
        /// 패드 South 버튼(엑스박스 A) 입력.
        ///
        /// 장치를 직접 읽는다 — 대화 중에는 PlayerController가 꺼져 있어(DialogueControlLock)
        /// InputAction 경로로는 아무 신호도 들어오지 않기 때문이다.
        ///
        /// <b><see cref="Gamepad.current"/>만 보지 않고 붙어 있는 패드를 전부 훑는다.</b>
        /// <c>current</c>는 "마지막으로 입력이 들어온 패드"라서, 그 판에서 아직 패드를 한 번도 안 건드렸거나
        /// 다른 장치(마우스·키보드)로만 조작하다 오면 null이거나 엉뚱한 패드를 가리킬 수 있다.
        /// 그러면 A를 눌러도 조용히 무시된다 — 마우스는 되는데 패드만 안 먹는 증상이 여기서 나온다.
        /// </summary>
        protected virtual bool SouthButtonPressed()
        {
            var gamepads = Gamepad.all;

            for (int i = 0; i < gamepads.Count; i++)
            {
                Gamepad gamepad = gamepads[i];
                if (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame)
                {
                    LogTrigger($"패드 South (A) — {gamepad.displayName}");
                    return true;
                }
            }

            return false;
        }

        protected virtual bool KeyboardPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || ContinueKeys == null)
            {
                return false;
            }

            for (int i = 0; i < ContinueKeys.Count; i++)
            {
                Key key = ContinueKeys[i];
                if (key == Key.None)
                {
                    continue;
                }

                if (keyboard[key].wasPressedThisFrame)
                {
                    LogTrigger($"키보드 {key}");
                    return true;
                }
            }

            return false;
        }

        protected virtual void LogTrigger(string source)
        {
            if (DebugLog)
            {
                Debug.Log($"[DialogueContinueInput] 진행 입력: {source}", this);
            }
        }

        protected virtual bool MouseClicked()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return false;
            }

            LogTrigger("마우스 좌클릭");
            return true;
        }

        /// <summary>
        /// PC에 "다음 대사" 신호를 보낸다. 컨티뉴 버튼을 누른 것과 같다.
        ///
        /// 타자기가 글자를 찍는 중이면 넘기지 않고 먼저 끝까지 표시한다 —
        /// Pixel Crushers의 컨티뉴 버튼 고속 재생(StandardUIContinueButtonFastForward)과 같은 2단 동작.
        /// 선택지 메뉴가 떠 있는 동안은 아무것도 하지 않는다. 클릭 한 번에 선택지를 건너뛰면 안 되기 때문.
        /// </summary>
        public virtual void Continue()
        {
            var standardUI = DialogueManager.dialogueUI as StandardDialogueUI;
            if (standardUI != null)
            {
                if (IsResponseMenuOpen(standardUI))
                {
                    return;
                }

                if (FastForwardTypewriter && StopTypewriter(standardUI))
                {
                    return;
                }
            }

            var dialogueUI = DialogueManager.dialogueUI as AbstractDialogueUI;
            if (dialogueUI != null)
            {
                dialogueUI.OnContinue();
                return;
            }

            // 표준 UI가 아닌 경우의 대비책.
            ConversationView view = DialogueManager.conversationView;
            if (view != null)
            {
                view.HandleContinueButtonClick();
            }
        }

        /// <summary>글자를 찍는 중이었으면 즉시 끝까지 표시하고 true를 돌려준다.</summary>
        protected virtual bool StopTypewriter(StandardDialogueUI standardUI)
        {
            StandardUISubtitlePanel panel = FocusedSubtitlePanel(standardUI);
            if (panel == null)
            {
                return false;
            }

            AbstractTypewriterEffect typewriter = panel.GetTypewriter();
            if (typewriter == null || !typewriter.isPlaying)
            {
                return false;
            }

            typewriter.Stop();
            return true;
        }

        /// <summary>지금 대사를 보여주고 있는 자막 패널. 여러 개가 열려 있으면 포커스를 가진 쪽.</summary>
        protected virtual StandardUISubtitlePanel FocusedSubtitlePanel(StandardDialogueUI standardUI)
        {
            var panels = standardUI.conversationUIElements.subtitlePanels;
            if (panels == null)
            {
                return null;
            }

            StandardUISubtitlePanel firstOpen = null;

            foreach (var panel in panels)
            {
                if (panel == null || !panel.isOpen)
                {
                    continue;
                }

                if (panel.hasFocus)
                {
                    return panel;
                }

                if (firstOpen == null)
                {
                    firstOpen = panel;
                }
            }

            return firstOpen;
        }

        protected virtual bool IsResponseMenuOpen(StandardDialogueUI standardUI)
        {
            var menuPanels = standardUI.conversationUIElements.menuPanels;
            if (menuPanels == null)
            {
                return false;
            }

            foreach (var menuPanel in menuPanels)
            {
                if (menuPanel != null && menuPanel.isOpen)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
