using System.Collections;
using System.Collections.Generic;
using System.Text;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 선택지(SelectDialog) 메뉴 패널. Pixel Crushers의 응답 메뉴 자리에 그대로 끼워 쓴다
    /// (StandardDialogueUI → Conversation UI Elements → Menu Panels / Default Menu Panel).
    ///
    /// 스톡 <see cref="StandardUIMenuPanel"/>은 버튼 프리팹을 선택지 개수만큼 복제해 붙이고
    /// EventSystem/UI Button으로 고르게 한다. 여기서는 그걸 통째로 걷어내고,
    /// <see cref="DialogueChoiceItem"/>을 <b>미리 UI에 깔아둔 뒤 필요한 개수만 켜는</b> 방식으로 바꾼다:
    ///
    ///   · Choices 리스트에 물린 순서 = 선택지가 채워지는 순서
    ///   · 처음엔 0번에 포커스, 상하좌우(패드 D-Pad·왼쪽 스틱, 키보드 방향키·WASD)로 이동
    ///     (위/왼쪽 = 이전, 아래/오른쪽 = 다음)
    ///   · 포커스가 간 오브젝트는 스케일이 살짝 커진다 (DialogueChoiceItem이 처리)
    ///   · A(패드 SouthButton) / Enter·Space로 확정
    ///
    /// 부모(패널 열고 닫기, 애니메이션, 타이머)는 그대로 쓴다. 버튼 관련 필드
    /// (Buttons / Button Template / Button Template Holder)는 **비워둬도 되고, 무시된다**.
    ///
    /// 입력은 장치에서 직접 읽는다. 대화 중에는 <see cref="DialogueControlLock"/>이 InputDetectionActive를
    /// 꺼서 일반 버튼 경로가 죽어 있기 때문이다 — <see cref="DialogueContinueInput"/>과 같은 이유, 같은 통로.
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/Dialogue Choice Menu Panel")]
    public class DialogueChoiceMenuPanel : StandardUIMenuPanel
    {
        [Header("선택지")]
        [Tooltip("미리 깔아둔 선택지 오브젝트들. 물린 순서대로 앞에서부터 채워진다. " +
                 "여기 개수보다 선택지가 많으면 넘치는 건 화면에 나오지 않는다(경고를 남긴다).")]
        public List<DialogueChoiceItem> Choices = new List<DialogueChoiceItem>();

        [Tooltip("첫 선택지에서 이전으로 가면 맨 끝으로, 마지막에서 다음으로 가면 처음으로 돌아간다.")]
        public bool WrapAround = true;

        [Header("입력")]
        [Tooltip("패드 SouthButton(A)으로 선택지를 확정한다.")]
        public bool UseSouthButton = true;

        [Tooltip("키보드 방향키/WASD로 이동하고 Enter·Space로 확정한다. 위·왼쪽=이전, 아래·오른쪽=다음.")]
        public bool UseKeyboard = true;

        [Tooltip("패드 D-Pad / 왼쪽 스틱으로 이동한다. 위·왼쪽=이전, 아래·오른쪽=다음.")]
        public bool UseGamepadStick = true;

        [Tooltip("왼쪽 스틱을 이만큼 넘게 기울여야 한 칸 움직인 것으로 본다.")]
        public float StickDeadZone = 0.5f;

        [Tooltip("방향을 누른 채로 있을 때 연속 이동이 시작되기까지의 시간(초). 0 이하면 연속 이동 없음.")]
        public float RepeatDelay = 0.4f;

        [Tooltip("연속 이동 간격(초).")]
        public float RepeatInterval = 0.12f;

        [Tooltip("메뉴가 뜨고 이만큼(초)은 입력을 무시한다. " +
                 "직전 대사를 넘긴 그 A 입력이 그대로 첫 선택지를 골라버리는 것을 막는다.")]
        public float InputBlockAfterOpen = 0.15f;

        [Tooltip("선택지를 고른 뒤 이만큼(초) 진행 입력(DialogueContinueInput)을 막는다. " +
                 "고른 그 A 입력이 다음 대사까지 연달아 넘기는 것을 막는다.")]
        public float BlockContinueAfterSelect = 0.2f;

        [Header("디버그")]

        [Tooltip("선택지를 띄우고 고르는 과정을 콘솔에 남긴다. 배선이 맞는지 확인할 때 켠다.")]
        public bool LogDetails = true;

        /// <summary>지금 포커스가 가 있는 선택지 번호. 선택지가 안 떠 있으면 -1.</summary>
        public int FocusedIndex { get { return _focusedIndex; } }

        protected Response[] _responses;
        protected Transform _target;
        protected int _shownCount;
        protected int _focusedIndex = -1;

        protected float _acceptInputAt;
        protected int _heldDirection;
        protected float _nextRepeatAt;
        protected bool _inputEnabled;

        public override void Awake()
        {
            base.Awake();

            if (Choices.Count == 0)
            {
                Debug.LogWarning("[선택지] Choices가 비어 있습니다. 미리 깔아둔 선택지 오브젝트를 물려주세요.", this);
            }

            // 이미 선택지를 띄운 뒤라면 건드리지 않는다.
            //
            // Awake는 생각보다 늦게 돌 수 있다 — 패널 오브젝트가 계층상 비활성인 채로 인스턴스화되면
            // Unity는 **처음 활성화되는 순간**에야 Awake를 돌린다. 그 순간은 부모 UIPanel.Open()이고,
            // Open()은 선택지를 다 켠 다음에 불린다. 그래서 조건 없이 끄면 방금 켠 선택지를 도로 꺼버린다.
            if (_responses == null)
            {
                HideAllChoices();
            }
            else if (LogDetails)
            {
                Debug.Log("[선택지] Awake가 선택지를 띄운 뒤에 돌았습니다 — 숨김을 건너뜁니다.", this);
            }
        }

        #region Pixel Crushers 연결

        /// <summary>
        /// 부모가 선택지를 다 올리고 패널을 연 뒤, 실제로 화면에 보이는 상태인지 한 프레임 뒤에 찍어본다.
        /// (켜졌는데 안 보이는 경우 — 부모가 꺼져 있거나, 알파가 0이거나, 화면 밖이거나 — 를 가리기 위한 진단)
        /// </summary>
        protected override void ShowResponsesNow(Subtitle subtitle, Response[] responses, Transform target)
        {
            base.ShowResponsesNow(subtitle, responses, target);

            if (LogDetails && gameObject.activeInHierarchy)
            {
                StartCoroutine(ReportVisibleState());
            }
        }

        protected virtual IEnumerator ReportVisibleState()
        {
            yield return null;

            var text = new StringBuilder();
            text.Append("[선택지] 상태 — 패널 activeInHierarchy=").Append(gameObject.activeInHierarchy)
                .Append(", panelState=").Append(panelState)
                .Append(", isOpen=").Append(isOpen);

            var canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                text.Append(", CanvasGroup alpha=").Append(canvasGroup.alpha);
            }

            for (int i = 0; i < _shownCount; i++)
            {
                DialogueChoiceItem choice = Choices[i];
                if (choice == null)
                {
                    continue;
                }

                var rect = choice.transform as RectTransform;

                text.Append("\n  ").Append(i).Append(": ").Append(choice.name)
                    .Append(" activeInHierarchy=").Append(choice.gameObject.activeInHierarchy)
                    .Append(" scale=").Append(choice.transform.localScale.x.ToString("0.##"))
                    .Append(" 화면좌표=").Append(rect != null ? rect.position.ToString() : "?")
                    .Append(" 크기=").Append(rect != null ? rect.rect.size.ToString() : "?")
                    .Append(" 글자=\"").Append(choice.Label != null ? choice.Label.text : "(Label 없음)").Append('"')
                    .Append(" 글자알파=").Append(choice.Label != null ? choice.Label.color.a.ToString("0.##") : "-");
            }

            Debug.Log(text.ToString(), this);
        }

        /// <summary>
        /// 선택지를 화면에 올린다. 부모는 여기서 버튼을 복제/배치하지만, 우리는 미리 깔아둔 것을 켠다.
        /// </summary>
        protected override void SetResponseButtons(Response[] responses, Transform target)
        {
            _responses = responses;
            _target = target;
            _shownCount = 0;
            _focusedIndex = -1;

            // 부모 UIPanel이 EventSystem으로 잡으려 드는 대상. 우리가 포커스를 직접 관리하므로 비워둔다.
            firstSelected = null;

            int count = (responses != null) ? responses.Length : 0;

            if (count > Choices.Count)
            {
                Debug.LogWarning($"[선택지] 선택지가 {count}개인데 깔아둔 오브젝트는 {Choices.Count}개뿐입니다. " +
                                 "넘치는 선택지는 화면에 나오지 않습니다.", this);
                count = Choices.Count;
            }

            for (int i = 0; i < Choices.Count; i++)
            {
                DialogueChoiceItem choice = Choices[i];
                if (choice == null)
                {
                    continue;
                }

                if (i < count)
                {
                    choice.Show(UITools.GetUIFormattedText(responses[i].formattedText), responses[i].enabled);
                    _shownCount = i + 1;
                }
                else
                {
                    choice.Hide();
                }
            }

            _inputEnabled = true;
            _acceptInputAt = Time.unscaledTime + Mathf.Max(0f, InputBlockAfterOpen);
            _heldDirection = 0;

            FocusIndex(FirstSelectableIndex());

            if (LogDetails)
            {
                Debug.Log($"[선택지] {_shownCount}개 표시 (응답 {(responses != null ? responses.Length : 0)}개 / " +
                          $"깔아둔 오브젝트 {Choices.Count}개), 포커스 {_focusedIndex}", this);
            }

            NotifyContentChanged();
        }

        /// <summary>
        /// 메뉴가 닫히거나 다시 뜰 때 선택지를 전부 끈다.
        /// 부모 구현은 Buttons 배열/복제 버튼을 정리하는데, 우리는 그 둘을 안 쓰므로 통째로 대체한다.
        /// </summary>
        protected override void ClearResponseButtons()
        {
            if (LogDetails && _shownCount > 0)
            {
                Debug.Log($"[선택지] 정리 — {_shownCount}개를 끕니다 (panelState={panelState})\n" +
                          StackTraceUtility.ExtractStackTrace(), this);
            }

            _responses = null;
            _target = null;
            _shownCount = 0;
            _focusedIndex = -1;
            _inputEnabled = false;
            _heldDirection = 0;

            HideAllChoices();
        }

        /// <summary>닫히는 애니메이션이 도는 동안 한 번 더 고르는 것을 막는다.</summary>
        public override void MakeButtonsNonclickable()
        {
            base.MakeButtonsNonclickable();
            _inputEnabled = false;
        }

        #endregion

        #region 입력

        protected override void Update()
        {
            base.Update();

            if (!_inputEnabled || !isOpen || _shownCount == 0 || Time.unscaledTime < _acceptInputAt)
            {
                return;
            }

            int step = ReadStep();
            if (step != 0)
            {
                MoveFocus(step);
            }

            if (ConfirmPressed())
            {
                Confirm();
            }
        }

        /// <summary>이번 프레임에 옮겨야 할 칸 수. 이전=-1, 다음=+1, 없으면 0. 누른 채로 있으면 연속으로 나온다.</summary>
        protected virtual int ReadStep()
        {
            int direction = HeldDirection();

            if (direction == 0)
            {
                _heldDirection = 0;
                return 0;
            }

            // 방향이 바뀌거나 새로 눌린 순간은 무조건 한 칸.
            if (direction != _heldDirection)
            {
                _heldDirection = direction;
                _nextRepeatAt = Time.unscaledTime + Mathf.Max(0f, RepeatDelay);
                return direction;
            }

            if (RepeatDelay <= 0f || Time.unscaledTime < _nextRepeatAt)
            {
                return 0;
            }

            _nextRepeatAt = Time.unscaledTime + Mathf.Max(0.02f, RepeatInterval);
            return direction;
        }

        /// <summary>
        /// 지금 눌려 있는 방향. 이전=-1, 다음=+1.
        ///
        /// 선택지는 한 줄로 늘어선 목록이라 축을 구분하지 않는다 — 위/왼쪽은 이전, 아래/오른쪽은 다음.
        /// (선택지가 가로로 깔려 있든 세로로 깔려 있든 같은 키로 움직이게 하기 위함)
        /// </summary>
        protected virtual int HeldDirection()
        {
            bool previous = false;
            bool next = false;

            if (UseKeyboard)
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    previous |= keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed
                        || keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed;
                    next |= keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed
                        || keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed;
                }
            }

            if (UseGamepadStick)
            {
                Gamepad gamepad = Gamepad.current;
                if (gamepad != null)
                {
                    // 스틱은 기운 쪽이 큰 축만 본다. 대각선으로 밀었을 때 두 축이 서로를 상쇄해
                    // 아무 데도 못 가는 일을 막는다.
                    Vector2 stick = gamepad.leftStick.ReadValue();
                    bool horizontal = Mathf.Abs(stick.x) > Mathf.Abs(stick.y);
                    float axis = horizontal ? stick.x : -stick.y;

                    previous |= gamepad.dpad.up.isPressed || gamepad.dpad.left.isPressed
                        || axis < -StickDeadZone;
                    next |= gamepad.dpad.down.isPressed || gamepad.dpad.right.isPressed
                        || axis > StickDeadZone;
                }
            }

            if (previous == next)
            {
                return 0;
            }

            return previous ? -1 : 1;
        }

        protected virtual bool ConfirmPressed()
        {
            if (UseSouthButton && SouthButtonPressed())
            {
                return true;
            }

            if (!UseKeyboard)
            {
                return false;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            return keyboard.enterKey.wasPressedThisFrame
                || keyboard.numpadEnterKey.wasPressedThisFrame
                || keyboard.spaceKey.wasPressedThisFrame;
        }

        /// <summary>
        /// 패드 A. 장치를 직접 읽는다 — 대화 중에는 <see cref="DialogueControlLock"/>이 PlayerController를
        /// 꺼두기 때문에 프로젝트의 InputAction 경로로는 아무 신호도 들어오지 않는다.
        /// </summary>
        protected virtual bool SouthButtonPressed()
        {
            Gamepad gamepad = Gamepad.current;
            return gamepad != null && gamepad.buttonSouth.wasPressedThisFrame;
        }

        #endregion

        #region 포커스 / 확정

        protected virtual void MoveFocus(int step)
        {
            int next = NextSelectableIndex(_focusedIndex, step);

            if (next < 0 || next == _focusedIndex)
            {
                return;
            }

            FocusIndex(next);
        }

        protected virtual void FocusIndex(int index)
        {
            _focusedIndex = index;

            for (int i = 0; i < _shownCount; i++)
            {
                if (Choices[i] != null)
                {
                    Choices[i].SetFocused(i == index);
                }
            }

            // 타이머가 끝나 자동으로 골라질 때 지금 보고 있는 선택지가 쓰이도록 PC에 알려준다.
            if (0 <= index && index < _shownCount)
            {
                SetCurrentResponse(_responses[index]);
            }
        }

        /// <summary>고를 수 있는 첫 선택지. 하나도 없으면 0번(=아무 것도 못 고르는 상태)을 잡는다.</summary>
        protected virtual int FirstSelectableIndex()
        {
            for (int i = 0; i < _shownCount; i++)
            {
                if (IsSelectable(i))
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>from에서 step 방향으로 고를 수 있는 다음 선택지. 없으면 -1.</summary>
        protected virtual int NextSelectableIndex(int from, int step)
        {
            int index = from;

            for (int i = 0; i < _shownCount; i++)
            {
                index += step;

                if (index < 0 || index >= _shownCount)
                {
                    if (!WrapAround)
                    {
                        return -1;
                    }

                    index = (index + _shownCount) % _shownCount;
                }

                if (IsSelectable(index))
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>조건이 안 맞아 회색으로 뜬 선택지는 건너뛴다.</summary>
        protected virtual bool IsSelectable(int index)
        {
            return 0 <= index && index < _shownCount
                && Choices[index] != null && Choices[index].IsInteractable;
        }

        /// <summary>지금 포커스된 선택지를 고른다.</summary>
        public virtual void Confirm()
        {
            if (!IsSelectable(_focusedIndex) || _responses == null || _target == null)
            {
                return;
            }

            Response response = _responses[_focusedIndex];

            if (LogDetails)
            {
                Debug.Log($"[선택지] {_focusedIndex}번 확정", this);
            }

            // 더 이상 입력을 받지 않는다. 확정 후에도 패널은 닫히는 애니메이션 동안 살아 있다.
            _inputEnabled = false;

            // 고른 그 A 입력이 같은 프레임에 다음 대사까지 넘기지 않도록 진행 입력을 잠깐 막는다.
            // (DialogueContinueInput은 메뉴가 열려 있을 때만 스스로 비키는데, 여기서 메뉴가 닫힌다.)
            DialogueContinueInput.BlockInput(BlockContinueAfterSelect);

            SetCurrentResponse(response);
            _target.SendMessage("OnClick", response, SendMessageOptions.RequireReceiver);
        }

        protected virtual void SetCurrentResponse(Response response)
        {
            if (DialogueManager.instance == null || DialogueManager.instance.conversationController == null)
            {
                return;
            }

            DialogueManager.instance.conversationController.SetCurrentResponse(response);
        }

        #endregion

        protected virtual void HideAllChoices()
        {
            for (int i = 0; i < Choices.Count; i++)
            {
                if (Choices[i] != null)
                {
                    Choices[i].Hide();
                }
            }
        }
    }
}
