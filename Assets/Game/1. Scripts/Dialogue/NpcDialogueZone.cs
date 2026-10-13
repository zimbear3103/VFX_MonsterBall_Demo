using System.Collections.Generic;
using MoreMountains.Tools;
using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Yeolha.BeltScroll;

namespace Aiara.Dialogue
{
    /// <summary>
    /// NPC 대화 트리거. 플레이어가 존 안에 있을 때 대화 버튼을 누르면 대화를 연다.
    ///
    /// 원본(TDE)에서는 <c>ButtonActivated</c> 존을 상속했지만 ver2에는 그 계층이 없다.
    /// 필요한 것은 결국 넷뿐이라 여기서 직접 갖춘다 — 트리거 감지, 안내 문구, 버튼 입력, 사용 횟수.
    ///
    /// 입력은 <b>존 안에 있을 때만</b> 직접 읽는다. 공격·점프와 버튼을 나눠 쓰기 위해서다 —
    /// 존 밖에서는 같은 버튼을 다른 기능이 자유롭게 쓸 수 있고, 존 안에서는
    /// <see cref="NpcInteractionContext"/>가 "지금 이 버튼은 대화 몫"이라고 알려준다.
    ///
    /// 입력은 장치에서 직접 읽는다. 대화가 시작되면 <see cref="DialogueControlLock"/>이
    /// PlayerController를 통째로 꺼버리기 때문에, 프로젝트의 InputAction 경로로는 신호가 오지 않는다.
    ///
    /// 대화 선택은 <see cref="Conversations"/>에 적어둔 이벤트 ID를 **위에서부터 검사해 조건이 통과하는
    /// 첫 번째**를 쓴다. 조건은 여기 적지 않는다 — EventData 시트의 ActiveQuest/ClearQuest가 임포터를 거쳐
    /// 대화 첫 노드의 Conditions로 들어가 있고(<c>CurrentQuestState(...) == "active"/"success"</c>),
    /// 이 컴포넌트는 <see cref="DialogueManager.ConversationHasValidEntry"/>에 통과 여부만 물어본다.
    /// 조건이 바뀌면 시트만 고치고 다시 임포트하면 된다. (<see cref="EventZone"/>과 같은 방식)
    ///
    /// 그래서 **순서가 곧 우선순위**다. 조건이 없는 이벤트(항상 통과)를 위에 두면 아래 것은 영영 안 열리므로,
    /// 조건이 구체적인 것을 위에, 아무 때나 되는 잡담을 맨 아래에 둘 것.
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/NPC Dialogue Zone")]
    [RequireComponent(typeof(Collider))]
    public class NpcDialogueZone : MonoBehaviour
    {
        /// <summary>이동 잠금 레지스트리에 등록할 이름. 자기 이름의 잠금만 해제할 수 있다.</summary>
        public const string DialogueLockOwner = "Dialogue";

        /// <summary>
        /// 강제 시작(<see cref="ForceTalkForTesting"/>)이 곧바로 뛰어들 대사 노드 ID.
        ///
        /// 0번은 임포터가 만드는 빈 START 노드이고 실제 내용은 1번부터다
        /// (DialogueCsvImporter의 <c>FirstEntryId</c>와 같은 값 — 시트 규약이 바뀌면 같이 고쳐야 한다).
        ///
        /// 이 번호를 직접 지정해 시작하면 조건이 **평가되지 않는다**. 임포터는 시작 조건을 1번 노드에
        /// 걸어두고, Pixel Crushers는 링크를 따라갈 때만 목적지 노드의 조건을 본다
        /// (<c>ConversationModel.EvaluateLinks</c>). START→1 링크를 건너뛰므로 조건도 건너뛴다.
        /// </summary>
        protected const int ForceStartEntryId = 1;

        [MMInspectorGroup("NPC 대화", true, 100)]

        [Tooltip("이 NPC가 발생시킬 수 있는 이벤트(대화) ID 목록. EventData 시트의 ID와 같아야 한다. " +
                 "위에서부터 조건을 검사해 **처음 통과하는 것**을 실행한다 — 조건은 시트에 있고 여기 적지 않는다. " +
                 "조건이 없는 이벤트는 항상 통과하므로 맨 아래에 둘 것.")]
        public List<string> Conversations = new List<string>();

        [Tooltip("대화 상대(NPC) Transform. 비우면 이 오브젝트를 쓴다. 대화 중 카메라/화자 지정에 쓰인다.")]
        public Transform ConversantTransform;

        [MMInspectorGroup("대화 버튼", true, 105)]

        [Tooltip("패드 X(WestButton)로 말을 건다.")]
        public bool UseWestButton = true;

        [Tooltip("키보드에서 말을 걸 키. None으로 두면 키보드로는 말을 걸 수 없다.")]
        public Key TalkKey = Key.F;

        [Tooltip("존 안에 있을 때 띄울 안내 오브젝트(말풍선 등). 비워두면 안내를 띄우지 않는다.")]
        public GameObject ButtonPrompt;

        [Tooltip("안내 오브젝트에 표시할 문구. 하위에 TextMeshPro가 있으면 거기에 써넣는다.")]
        public string ButtonPromptText = "X";

        [Tooltip("존 안에 있는 동안 안내를 띄울지 여부.")]
        public bool ShowPromptWhenColliding = true;

        [MMInspectorGroup("사용 횟수", true, 106)]

        [Tooltip("횟수 제한 없이 계속 말을 걸 수 있다. 끄면 아래 횟수만큼만 열린다.")]
        public bool UnlimitedActivations = true;

        [Tooltip("열 수 있는 최대 횟수. Unlimited가 꺼져 있을 때만 쓴다.")]
        public int MaxNumberOfActivations = 1;

        [Tooltip("횟수를 다 쓰면 이 오브젝트를 비활성화한다.")]
        public bool DisableAfterUse = false;

        [MMInspectorGroup("NPC 활성화 조건", true, 101)]

        [Tooltip("퀘스트 상태에 따라 이 NPC를 켜고 끌지 여부. 끄면 항상 활성.")]
        public bool UseActivationGate = false;

        [Tooltip("활성화 여부를 판정할 퀘스트 이름")]
        public string GateQuestName;

        [Tooltip("이 상태들 중 하나일 때만 NPC가 활성화된다. 예: Unassigned|Active = 아직 안 받았거나 진행 중일 때만 등장")]
        public QuestState GateActiveStates = QuestState.Unassigned | QuestState.Active;

        [Tooltip("게이트와 함께 켜고 끌 오브젝트들 (NPC 모델, 콜라이더 등). 비워두면 존만 잠긴다.")]
        public GameObject[] ObjectsToToggleWithGate;

        [Tooltip("퀘스트 상태 변화를 감지하는 주기(초). Dialogue System의 상태 변경 통지는 " +
                 "Dialogue Manager 오브젝트에만 BroadcastMessage로 가므로 여기서는 폴링으로 잡는다.")]
        public float GatePollInterval = 0.5f;

        [MMInspectorGroup("대화 중 잠금", true, 102)]

        [Tooltip("대화 중 플레이어 조작을 잠글지 여부. 대화 자세(IsTalk)와는 별개 스위치라, " +
                 "잠그지 않는 대화에서도 대화 자세는 들어간다.")]
        public bool LockCharacterDuringDialogue = true;

        [MMInspectorGroup("대화 연출 배치", true, 104)]

        [Tooltip("대화 시작 시 플레이어 위치·방향과 카메라를 정해진 자리에 놓는 컴포넌트. " +
                 "비워두면 이 오브젝트(및 자식)에서 자동으로 찾는다. 없으면 배치 없이 그 자리에서 대화한다 " +
                 "— 방향도 잡아둔 각도가 없으니 그대로 둔다.")]
        public DialogueStaging Staging;

        [MMInspectorGroup("임시 — UI 테스트", true, 110)]

        [Tooltip("**임시 기능.** 존 안에서 공격키를 누르면 조건·게이트·사용 횟수를 전부 무시하고 " +
                 "Conversations의 첫 이벤트를 강제로 시작한다. 대화창 UI만 눈으로 확인할 때 쓴다. " +
                 "확인이 끝나면 반드시 끌 것 — 켜둔 채 두면 공격이 대화를 열어버린다.")]
        public bool ForceTalkForTesting = false;

        [Tooltip("강제 시작에 쓸 키보드 키. ver2의 약공격 기본 바인딩이 J다. " +
                 "마우스 좌클릭은 일부러 받지 않는다 — 그건 대사 넘기기(DialogueContinueInput)와 겹친다.")]
        public Key ForceTalkKey = Key.J;

        [Tooltip("강제 시작에 쓸 패드 버튼도 함께 받는다(West = X/□). " +
                 "**말 거는 버튼과 같은 버튼이다** — 둘 다 켜면 강제 시작이 먼저 걸려 조건 검사가 통째로 건너뛰어진다. " +
                 "그래서 기본은 꺼짐이고, 강제 시작은 키보드(기본 J)로 쓰는 것을 권한다.")]
        public bool ForceTalkUseWestButton = false;

        [Tooltip("Conversations 목록에서 강제로 시작할 항목 번호(0부터). 범위를 벗어나면 첫 항목을 쓴다.")]
        public int ForceTalkConversationIndex = 0;

        [MMInspectorGroup("디버그", true, 103)]

        [Tooltip("조건을 통과한 이벤트가 하나도 없어 대화가 안 열릴 때, 검사한 목록을 콘솔에 남긴다. " +
                 "목록에 없는 ID(오타)는 이 값과 상관없이 항상 경고한다.")]
        public bool DebugLog = false;

        /// <summary>게이트가 열려 있어 이 존이 반응할 수 있는 상태인가. 게이트가 닫히면 false가 된다.</summary>
        public bool Activable { get; protected set; } = true;

        protected Character _dialogueCharacter;
        protected DialogueControlLock _controlLock;
        protected bool _playerInZone;
        protected bool _conversationActive;
        protected bool _subscribedToConversationEnd;
        protected bool _gateOpen = true;
        protected bool _gateEverApplied;
        protected float _nextGatePollAt;
        protected bool _checkedConversationNames;
        protected int _activationsLeft;
        protected bool _promptShown;

        /// <summary>지금 이 존이 대화를 열 수 있는 상태인가 (게이트 열림 + 대화 중 아님 + 횟수 남음)</summary>
        public virtual bool CanTalk =>
            _gateOpen && Activable && HasActivationsLeft
            && !_conversationActive && !DialogueManager.IsConversationActive;

        /// <summary>아직 말을 걸 수 있는 횟수가 남았는가.</summary>
        public virtual bool HasActivationsLeft => UnlimitedActivations || _activationsLeft > 0;

        /// <summary>
        /// 겹친 존들 중 누가 가까운지 잴 때 쓰는 기준점.
        ///
        /// 존 오브젝트가 아니라 <b>대화 상대(NPC 본체)</b>의 위치를 쓴다 — 존은 NPC의 자식이라
        /// 위치를 옮겨 범위를 조절하는 경우가 있어서, 존 기준으로 재면 "눈앞의 NPC"와 어긋날 수 있다.
        /// </summary>
        public virtual Vector3 InteractionPoint =>
            ConversantTransform != null ? ConversantTransform.position : transform.position;

        /// <summary>존 안에 있는 플레이어의 위치. 없으면 존 자신의 위치로 대신한다.</summary>
        protected virtual Vector3 PlayerPosition =>
            _dialogueCharacter != null ? _dialogueCharacter.transform.position : transform.position;

        /// <summary>말 걸 수 있는 기본 반경(m). 컴포넌트를 처음 붙일 때 만들어지는 콜라이더 크기.</summary>
        public const float DefaultZoneRadius = 2f;

        /// <summary>
        /// 컴포넌트를 처음 붙였을 때 트리거 콜라이더가 없으면 만들어준다.
        /// 존은 콜라이더가 없으면 진입 자체를 감지하지 못해 아무 일도 안 일어난다 —
        /// 이걸 빼먹는 게 배치할 때 가장 흔한 실수라 기본값을 깔아둔다.
        /// </summary>
        protected virtual void Reset()
        {
            var zoneCollider = GetComponent<Collider>();
            if (zoneCollider == null)
            {
                var sphere = gameObject.AddComponent<SphereCollider>();
                sphere.radius = DefaultZoneRadius;
                zoneCollider = sphere;
            }

            zoneCollider.isTrigger = true;

            // 화면에 뜨는 안내 문구. 실제 입력은 패드 X / 키보드 F다.
            ButtonPromptText = "X";
        }

        protected virtual void Awake()
        {
            if (Staging == null)
            {
                Staging = GetComponentInChildren<DialogueStaging>(true);
            }

            _activationsLeft = Mathf.Max(0, MaxNumberOfActivations);

            ApplyPromptText();
            HidePrompt();

            // 예전 구성에서 넘어온 존은 강제 시작이 West로 켜져 있을 수 있다. 그러면 말을 걸 때마다
            // 조건을 무시한 강제 시작이 먼저 걸려 "조건이 안 맞는데 대화가 열리는" 증상이 된다.
            if (ForceTalkForTesting && ForceTalkUseWestButton && UseWestButton)
            {
                Debug.LogWarning($"[NpcDialogueZone] '{name}'은 말 걸기와 강제 시작이 같은 패드 버튼(West)을 " +
                                 "쓰고 있습니다. 강제 시작이 먼저 걸려 조건 검사가 건너뛰어집니다 — " +
                                 "Force Talk Use West Button을 끄거나 Force Talk For Testing을 끄세요.", this);
            }

            RefreshActivationGate();
        }

        protected virtual void OnDisable()
        {
            NpcInteractionContext.Unregister(this);
            UnsubscribeConversationEnd();

            // 대화 도중 존이 꺼지면 잠금이 영영 남는다 — 여기서 반드시 푼다.
            if (_conversationActive)
            {
                _conversationActive = false;
                UnlockCharacter();
                ReleaseStaging();
            }

            _playerInZone = false;
            HidePrompt();
        }

        protected virtual void Update()
        {
            PollActivationGate();

            if (!_playerInZone)
            {
                return;
            }

            // 임시 테스트 경로. 게이트·사용 횟수까지 무시하므로 CanTalk보다 먼저, 더 느슨한 조건으로 본다.
            // 다른 대화가 도는 중에 끼어드는 것만은 막는다.
            if (ForceTalkForTesting && !_conversationActive && !DialogueManager.IsConversationActive
                && ForceTalkPressedThisFrame())
            {
                ForceActivateZone();
                return;
            }

            if (!CanTalk)
            {
                return;
            }

            // NPC들이 붙어 서 있으면 트리거가 겹쳐 여러 존이 같은 입력에 반응한다.
            // 가장 가까운 하나만 버튼을 가져가게 해서, Update 순서와 상관없이 결과가 일정하게 만든다.
            if (!NpcInteractionContext.IsClosest(this, PlayerPosition))
            {
                return;
            }

            if (TalkButtonPressedThisFrame())
            {
                ActivateZone();
            }
        }

        #region 입력 (대화 버튼)

        /// <summary>
        /// 이번 프레임에 대화 버튼이 눌렸는가.
        /// 존 안에 있을 때만 호출되므로, 존 밖에서는 같은 버튼을 다른 기능이 자유롭게 쓸 수 있다.
        ///
        /// 장치를 직접 읽는다 — 대화가 시작되면 PlayerController가 꺼지므로(DialogueControlLock)
        /// InputAction 경로와는 애초에 통로가 다르다.
        /// </summary>
        protected virtual bool TalkButtonPressedThisFrame()
        {
            if (UseWestButton && WestButtonPressedThisFrame())
            {
                return true;
            }

            if (TalkKey != Key.None)
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null && keyboard[TalkKey].wasPressedThisFrame)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 이번 프레임에 **강제 시작 키**(공격키)가 눌렸는가. 임시 테스트 전용이다.
        ///
        /// 여기서도 장치를 직접 읽는다 — PlayerController의 공격 액션을 가로채면 실제 공격 입력까지
        /// 건드리게 되고, 대화가 시작된 뒤에는 그 컴포넌트가 꺼져 신호가 오지도 않는다.
        /// </summary>
        protected virtual bool ForceTalkPressedThisFrame()
        {
            if (ForceTalkUseWestButton && WestButtonPressedThisFrame())
            {
                return true;
            }

            if (ForceTalkKey != Key.None)
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard != null && keyboard[ForceTalkKey].wasPressedThisFrame)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 이번 프레임에 패드 West(X/□)가 눌렸는가.
        ///
        /// <see cref="Gamepad.current"/>만 보지 않고 붙어 있는 패드를 전부 훑는다 — current는
        /// "마지막으로 입력이 들어온 패드"라서, 그 판에서 패드를 아직 안 건드렸거나 키보드로만 조작하다
        /// 오면 null이거나 엉뚱한 패드를 가리킬 수 있다. 그러면 눌러도 조용히 무시된다.
        /// (대사 넘기기 <see cref="DialogueContinueInput"/>도 같은 이유로 같은 방식을 쓴다.)
        /// </summary>
        protected virtual bool WestButtonPressedThisFrame()
        {
            var gamepads = Gamepad.all;

            for (int i = 0; i < gamepads.Count; i++)
            {
                Gamepad gamepad = gamepads[i];
                if (gamepad != null && gamepad.buttonWest.wasPressedThisFrame)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region 존 진입 / 이탈

        protected virtual void OnTriggerEnter(Collider other)
        {
            // 콜라이더가 붙은 오브젝트 자신에서만 찾는다 — 캐릭터 루트(CharacterController 캡슐)가
            // Character를 들고 있다. 자식 Hurtbox까지 훑으면 자식 콜라이더가 하나씩 드나들 때마다
            // 진입/이탈이 어긋난다.
            Character character = other.gameObject.MMGetComponentNoAlloc<Character>();
            if (character == null || character.Faction != FactionType.Player)
            {
                return;
            }

            _dialogueCharacter = character;
            _playerInZone = true;

            RefreshActivationGate();

            if (_gateOpen)
            {
                NpcInteractionContext.Register(this);

                if (ShowPromptWhenColliding)
                {
                    ShowPrompt();
                }
            }

            if (DebugLog)
            {
                // "존에 들어왔는데 반응이 없다"와 "존에 아예 안 들어왔다"를 가르는 로그다.
                // 이게 안 찍히면 콜라이더·레이어·플레이어 판정 쪽을 봐야 한다.
                Debug.Log($"[NpcDialogueZone] '{name}' 존 진입 (게이트 {(_gateOpen ? "열림" : "닫힘")}). " +
                          $"Dialogue Manager: {(DialogueManager.instance != null ? "있음" : "없음")}", this);
            }
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            Character character = other.gameObject.MMGetComponentNoAlloc<Character>();
            if (character == null || character != _dialogueCharacter)
            {
                return;
            }

            _playerInZone = false;
            NpcInteractionContext.Unregister(this);
            HidePrompt();

            // 대화 중에 존을 벗어나도 잠금은 여기서 풀지 않는다 —
            // 대화가 끝날 때(OnConversationEnded) 푸는 것이 유일한 해제 경로여야
            // "대화 중인데 움직여지는" 상태가 생기지 않는다.
        }

        #endregion

        #region 안내 문구

        protected virtual void ShowPrompt()
        {
            if (ButtonPrompt == null || _promptShown)
            {
                return;
            }

            ButtonPrompt.SetActive(true);
            _promptShown = true;
        }

        protected virtual void HidePrompt()
        {
            if (ButtonPrompt == null)
            {
                _promptShown = false;
                return;
            }

            ButtonPrompt.SetActive(false);
            _promptShown = false;
        }

        /// <summary>안내 오브젝트 하위에 TextMeshPro가 있으면 문구를 써넣는다. 없으면 아무것도 하지 않는다.</summary>
        protected virtual void ApplyPromptText()
        {
            if (ButtonPrompt == null || string.IsNullOrEmpty(ButtonPromptText))
            {
                return;
            }

            var label = ButtonPrompt.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = ButtonPromptText;
            }
        }

        /// <summary>열 대화가 없어 아무 일도 일어나지 않았음을 알린다. 지금은 로그만 남긴다.</summary>
        protected virtual void PromptError()
        {
            if (DebugLog)
            {
                Debug.Log($"[NpcDialogueZone] '{name}' 지금은 열 수 있는 대화가 없습니다.", this);
            }
        }

        #endregion

        #region 대화 시작

        /// <summary>조건이 맞으면 대화를 연다. 존 안에서 대화 버튼을 눌렀을 때 불린다.</summary>
        public virtual void ActivateZone()
        {
            if (DialogueManager.instance == null)
            {
                Debug.LogWarning("[NpcDialogueZone] 씬에 Dialogue Manager가 없어 대화를 시작할 수 없습니다.", this);
                PromptError();
                return;
            }

            WarnAboutMissingConversationsOnce();

            Transform actor = _dialogueCharacter != null ? _dialogueCharacter.transform : null;
            Transform conversant = ConversantTransform != null ? ConversantTransform : this.transform;

            string title = ResolveConversationTitle(actor, conversant);

            // 사용 횟수는 실제로 대화를 열 수 있다고 확정된 뒤에 깎는다.
            if (string.IsNullOrEmpty(title))
            {
                if (DebugLog)
                {
                    Debug.Log($"[NpcDialogueZone] '{name}'에서 조건을 통과한 이벤트가 없습니다. " +
                              $"검사한 목록: {DescribeConversations()}", this);
                }

                PromptError();
                return;
            }

            ConsumeActivation();
            BeginConversation(title, -1);
        }

        /// <summary>
        /// **임시 기능** — 조건·게이트·사용 횟수를 무시하고 대화를 연다. 대화창 UI 확인용이다.
        ///
        /// <see cref="ForceStartEntryId"/>번 노드에서 바로 시작하기 때문에 시트의 시작 조건
        /// (ActiveQuest/ClearQuest)이 아예 평가되지 않는다. 퀘스트를 하나도 켜지 않은 상태에서도 열린다.
        /// </summary>
        protected virtual void ForceActivateZone()
        {
            if (DialogueManager.instance == null)
            {
                Debug.LogWarning("[NpcDialogueZone] 씬에 Dialogue Manager가 없어 대화를 시작할 수 없습니다.", this);
                return;
            }

            WarnAboutMissingConversationsOnce();

            string title = ResolveForcedConversationTitle();
            if (string.IsNullOrEmpty(title))
            {
                Debug.LogWarning($"[NpcDialogueZone] '{name}'의 Conversations가 비어 있어 강제로 열 대화가 없습니다.", this);
                return;
            }

            // 임시 기능이 켜져 있다는 사실 자체가 눈에 띄어야 한다. 끄는 걸 잊고 빌드에 들어가면
            // 공격이 대화를 여는 채로 나가기 때문에, 조용히 넘어가지 않고 매번 경고를 남긴다.
            Debug.LogWarning($"[NpcDialogueZone] (임시) 조건을 무시하고 '{title}'을 시작합니다 — " +
                             "Force Talk For Testing이 켜져 있습니다.", this);

            BeginConversation(title, ForceStartEntryId);
        }

        /// <summary>강제로 열 대화 ID. 지정한 번호가 비었거나 범위를 벗어나면 목록의 첫 유효 항목을 쓴다.</summary>
        protected virtual string ResolveForcedConversationTitle()
        {
            if (Conversations == null || Conversations.Count == 0)
            {
                return string.Empty;
            }

            if (0 <= ForceTalkConversationIndex && ForceTalkConversationIndex < Conversations.Count
                && !string.IsNullOrEmpty(Conversations[ForceTalkConversationIndex]))
            {
                return Conversations[ForceTalkConversationIndex];
            }

            for (int i = 0; i < Conversations.Count; i++)
            {
                if (!string.IsNullOrEmpty(Conversations[i]))
                {
                    return Conversations[i];
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 잠금·연출 배치를 걸고 실제로 대화를 연다. 일반 경로와 강제 경로가 공유한다.
        ///
        /// <paramref name="initialEntryId"/>가 0 이상이면 그 노드에서 바로 시작한다(조건을 건너뛴다).
        /// -1이면 평소대로 START 노드부터 링크를 따라간다.
        /// </summary>
        protected virtual void BeginConversation(string title, int initialEntryId)
        {
            Transform actor = _dialogueCharacter != null ? _dialogueCharacter.transform : null;
            Transform conversant = ConversantTransform != null ? ConversantTransform : this.transform;

            // 대화가 같은 프레임에 끝나버리는 경우(빈 대화 등)에도 잠금이 풀리도록
            // 시작 전에 구독과 잠금을 끝내둔다.
            _conversationActive = true;
            HidePrompt();
            SubscribeConversationEnd();
            LockCharacter();

            // 배치는 잠금 뒤에 한다 — 먼저 옮기면 아직 살아 있는 스틱 입력이 그 프레임에 밀어낸다.
            ApplyStaging();

            if (initialEntryId >= 0)
            {
                DialogueManager.StartConversation(title, actor, conversant, initialEntryId);
            }
            else
            {
                DialogueManager.StartConversation(title, actor, conversant);
            }

            AbortIfConversationDidNotStart(title);
        }

        /// <summary>
        /// 대화가 실제로 시작됐는지 확인하고, 안 됐으면 방금 건 잠금과 연출을 되돌린다.
        ///
        /// Pixel Crushers는 대화를 **조용히 거절할 수 있다** — Dialogue UI가 비어 있거나(가장 흔하다),
        /// 다른 대화가 이미 진행 중이거나, 대화 제목을 못 찾은 경우다. 이때는 conversationEnded가 오지 않아
        /// 해제 경로가 통째로 사라지고, 먼저 걸어둔 잠금·카메라·배치만 영영 남는다.
        /// (증상: 카메라와 플레이어 위치만 대화용으로 바뀐 채 대화창이 뜨지 않고 조작도 막힘)
        ///
        /// 대화가 시작되자마자 같은 프레임에 끝난 경우는 conversationEnded가 이미 와서
        /// <see cref="_conversationActive"/>가 false이므로 여기 걸리지 않는다.
        /// </summary>
        protected virtual void AbortIfConversationDidNotStart(string title)
        {
            if (!_conversationActive || DialogueManager.IsConversationActive)
            {
                return;
            }

            Debug.LogError($"[NpcDialogueZone] '{title}' 대화가 시작되지 않아 잠금과 연출을 되돌립니다. " +
                           "Dialogue Manager의 Dialogue UI가 비어 있지 않은지 확인하고, " +
                           "콘솔의 'Dialogue System:' 메시지에서 거절 이유를 보세요.", this);

            _conversationActive = false;
            UnsubscribeConversationEnd();
            ReleaseStaging();
            UnlockCharacter();

            if (_playerInZone && _gateOpen && ShowPromptWhenColliding && HasActivationsLeft)
            {
                ShowPrompt();
            }
        }

        /// <summary>사용 횟수를 한 번 깎고, 다 썼으면 DisableAfterUse를 처리한다.</summary>
        protected virtual void ConsumeActivation()
        {
            if (UnlimitedActivations)
            {
                return;
            }

            _activationsLeft = Mathf.Max(0, _activationsLeft - 1);

            if (_activationsLeft > 0)
            {
                return;
            }

            NpcInteractionContext.Unregister(this);
            HidePrompt();

            // 오브젝트를 끄는 것은 대화가 끝난 뒤여야 한다 — 지금 끄면 OnDisable이
            // 방금 건 잠금을 도로 풀어버린다. 여기서는 표시만 하고 OnConversationEnded에서 끈다.
        }

        /// <summary>
        /// 목록을 위에서부터 검사해 **조건이 통과하는 첫 이벤트**를 고른다. 하나도 없으면 빈 문자열.
        ///
        /// 조건 판정은 <see cref="DialogueManager.ConversationHasValidEntry"/>에 맡긴다 —
        /// 시트에서 온 Conditions(Lua)를 그대로 평가하므로, 여기서 퀘스트 상태를 따로 볼 필요가 없다.
        /// </summary>
        public virtual string ResolveConversationTitle(Transform actor, Transform conversant)
        {
            if (Conversations == null || DialogueManager.instance == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < Conversations.Count; i++)
            {
                string title = Conversations[i];
                if (string.IsNullOrEmpty(title))
                {
                    continue;
                }

                if (DialogueManager.ConversationHasValidEntry(title, actor, conversant))
                {
                    return title;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 목록에 있는 이벤트 ID가 데이터베이스에 실제로 있는지 한 번만 확인한다.
        ///
        /// 이름 오타와 "조건이 아직 안 맞음"은 증상이 똑같이 '아무 일도 안 일어남'이라 구분이 안 된다.
        /// 오타는 고쳐야 하는 버그고 조건 미충족은 정상 동작이므로, 오타 쪽만 경고로 갈라준다.
        /// </summary>
        protected virtual void WarnAboutMissingConversationsOnce()
        {
            if (_checkedConversationNames || Conversations == null)
            {
                return;
            }

            _checkedConversationNames = true;

            if (DialogueManager.masterDatabase == null)
            {
                return;
            }

            for (int i = 0; i < Conversations.Count; i++)
            {
                string title = Conversations[i];
                if (string.IsNullOrEmpty(title))
                {
                    continue;
                }

                if (DialogueManager.masterDatabase.GetConversation(title) == null)
                {
                    Debug.LogWarning($"[NpcDialogueZone] '{name}'의 목록에 데이터베이스에 없는 이벤트 ID가 있습니다: " +
                                     $"{title} (EventData 시트의 ID와 철자가 같은지 확인하세요)", this);
                }
            }
        }

        protected virtual string DescribeConversations()
        {
            if (Conversations == null || Conversations.Count == 0)
            {
                return "(비어 있음)";
            }

            return string.Join(", ", Conversations);
        }

        /// <summary>퀘스트가 지정한 상태들 중 하나인가. QuestState는 플래그라 마스크로 검사한다.</summary>
        protected static bool QuestStateMatches(string questName, QuestState requiredStates)
        {
            // QuestLog는 Lua 환경을 읽으므로 Dialogue Manager 없이는 호출할 수 없다.
            if (DialogueManager.instance == null)
            {
                return false;
            }

            return (QuestLog.GetQuestState(questName) & requiredStates) != 0;
        }

        #endregion

        #region 대화 종료 / 잠금

        protected virtual void SubscribeConversationEnd()
        {
            if (_subscribedToConversationEnd || DialogueManager.instance == null)
            {
                return;
            }

            DialogueManager.instance.conversationEnded += OnConversationEnded;
            _subscribedToConversationEnd = true;
        }

        protected virtual void UnsubscribeConversationEnd()
        {
            if (!_subscribedToConversationEnd || DialogueManager.instance == null)
            {
                _subscribedToConversationEnd = false;
                return;
            }

            DialogueManager.instance.conversationEnded -= OnConversationEnded;
            _subscribedToConversationEnd = false;
        }

        protected virtual void OnConversationEnded(Transform actor)
        {
            _conversationActive = false;
            UnsubscribeConversationEnd();
            ReleaseStaging();
            UnlockCharacter();

            // 대화 결과로 퀘스트 상태가 바뀌었을 수 있으니 게이트를 즉시 다시 본다.
            RefreshActivationGate();

            // 횟수를 다 썼으면 이제 안전하게 끌 수 있다 — 잠금은 방금 풀렸다.
            if (DisableAfterUse && !HasActivationsLeft)
            {
                gameObject.SetActive(false);
                return;
            }

            if (_playerInZone && _gateOpen && ShowPromptWhenColliding && HasActivationsLeft)
            {
                ShowPrompt();
            }
        }

        /// <summary>플레이어를 자리에 놓고 대화 카메라를 켠다. 배치 컴포넌트가 없으면 아무 일도 하지 않는다.</summary>
        protected virtual void ApplyStaging()
        {
            if (Staging == null || _dialogueCharacter == null)
            {
                return;
            }

            Staging.Apply(_dialogueCharacter);
        }

        protected virtual void ReleaseStaging()
        {
            if (Staging == null)
            {
                return;
            }

            Staging.Release();
        }

        /// <summary>
        /// 대화 자세로 들여보내고, 필요하면 조작도 잠근다.
        ///
        /// <b>대화 자세(IsTalk)는 모든 대화에 조건 없이 붙는다.</b> 잠금(<see cref="LockCharacterDuringDialogue"/>)과는
        /// 별개라, 잠그지 않는 대화에서도 대화 자세는 들어간다.
        /// 그 위에 얹히는 대화 모션과 보조 NPC는 EventNodeData의 Action 노드(PlayerTalk1/PlayerTalk2)가 정한다.
        /// </summary>
        protected virtual void LockCharacter()
        {
            // Action 노드가 걸릴 대상. 어떤 모션을 태울지는 시트가 정하므로, 존은 "누구에게"만 알려준다.
            DialogueActions.SetPlayer(_dialogueCharacter);
            DialogueTalkPresentation.Begin(_dialogueCharacter);

            if (!LockCharacterDuringDialogue)
            {
                return;
            }

            _controlLock.Lock(_dialogueCharacter, DialogueLockOwner);
        }

        /// <summary>대화 자세를 풀고 액션이 걸어둔 모션·보조 NPC까지 되돌린다.</summary>
        protected virtual void UnlockCharacter()
        {
            _controlLock.Unlock();

            DialogueActions.ClearPlayer(_dialogueCharacter);
            DialogueTalkPresentation.End(_dialogueCharacter);
        }

        #endregion

        #region 활성화 게이트

        protected virtual void PollActivationGate()
        {
            if (!UseActivationGate || _conversationActive)
            {
                return;
            }

            if (Time.time < _nextGatePollAt)
            {
                return;
            }

            _nextGatePollAt = Time.time + Mathf.Max(0.1f, GatePollInterval);
            RefreshActivationGate();
        }

        /// <summary>
        /// 퀘스트 상태를 다시 읽어 NPC 활성화 여부를 갱신한다.
        /// 퀘스트 상태를 바꾼 쪽에서 즉시 반영이 필요하면 직접 호출해도 된다.
        /// </summary>
        public virtual void RefreshActivationGate()
        {
            if (!UseActivationGate || string.IsNullOrEmpty(GateQuestName))
            {
                ApplyGate(true);
                return;
            }

            ApplyGate(QuestStateMatches(GateQuestName, GateActiveStates));
        }

        protected virtual void ApplyGate(bool open)
        {
            if (_gateEverApplied && _gateOpen == open)
            {
                return;
            }

            _gateEverApplied = true;
            _gateOpen = open;
            Activable = open;

            if (ObjectsToToggleWithGate != null)
            {
                for (int i = 0; i < ObjectsToToggleWithGate.Length; i++)
                {
                    if (ObjectsToToggleWithGate[i] != null)
                    {
                        ObjectsToToggleWithGate[i].SetActive(open);
                    }
                }
            }

            if (!open)
            {
                NpcInteractionContext.Unregister(this);
                HidePrompt();
                return;
            }

            if (_playerInZone)
            {
                NpcInteractionContext.Register(this);
                if (ShowPromptWhenColliding && HasActivationsLeft)
                {
                    ShowPrompt();
                }
            }
        }

        #endregion
    }
}
