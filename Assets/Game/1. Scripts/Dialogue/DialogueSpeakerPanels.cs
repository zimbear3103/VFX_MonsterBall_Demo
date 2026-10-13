using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 아군(<see cref="SpeakerKind.Ally"/>) 액터의 대사를 전용 자막 패널로 보낸다.
    /// Dialogue Manager 오브젝트에 붙여둔다.
    ///
    /// Pixel Crushers는 액터의 <c>IsPlayer</c> 하나로 NPC 패널/PC 패널만 고르기 때문에, 세 번째 창은
    /// "이 액터는 N번 패널을 쓴다"는 덮어쓰기로만 만들 수 있다. 씬에 DialogueActor 컴포넌트를 둔 액터가
    /// 아니면 <c>IStandardDialogueUI.OverrideActorPanel(Actor, ...)</c>가 그 역할을 한다.
    ///
    /// **대사 줄마다 <c>SetPanel()</c> 시퀀서 커맨드를 붙이지 않는 이유**: 자막 패널은
    /// <c>ConversationView.StartSubtitle</c>에서 <c>ShowSubtitle</c> → <c>PlaySequence</c> 순서로
    /// 정해지므로, 그 줄의 시퀀스에 넣은 SetPanel은 **그 줄에는 이미 늦다**(다음 줄부터 먹는다).
    /// 그래서 대화가 시작될 때, 첫 대사가 뜨기 전에 한 번에 걸어둔다.
    /// (<c>OnConversationStart</c> 통지는 <c>GotoState(firstState)</c>보다 먼저 간다.)
    ///
    /// 덮어쓰기는 대화창이 닫힐 때 Pixel Crushers가 지우므로(<c>ClearCaches</c>) 대화마다 다시 건다.
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/Dialogue Speaker Panels")]
    public class DialogueSpeakerPanels : MonoBehaviour
    {
        [Tooltip("아군 대사를 띄울 자막 패널 번호. StandardDialogueUI의 Subtitle Panels 배열 인덱스다. " +
                 "이 프로젝트 기준 0=NPC, 1=PC(플레이어), 2=아군.")]
        public int AllyPanelNumber = 2;

        [Tooltip("어떤 액터가 어느 패널로 갔는지 콘솔에 남긴다.")]
        public bool DebugLog = false;

        protected bool _subscribed;
        protected bool _warnedAboutMissingPanel;

        protected virtual void OnEnable()
        {
            TrySubscribe();
        }

        protected virtual void Update()
        {
            // Dialogue Manager와 같은 오브젝트에 붙으면 OnEnable 시점에 instance가 아직 없을 수 있다
            // (Awake 순서는 보장되지 않는다). 붙을 때까지만 매 프레임 다시 시도한다.
            if (!_subscribed)
            {
                TrySubscribe();
            }
        }

        protected virtual void OnDisable()
        {
            Unsubscribe();
        }

        protected virtual void TrySubscribe()
        {
            if (_subscribed || DialogueManager.instance == null)
            {
                return;
            }

            DialogueManager.instance.conversationStarted += OnConversationStarted;
            _subscribed = true;

            // 이 컴포넌트를 대화 도중에 켠 경우에도 바로 반영되게 한다.
            if (DialogueManager.IsConversationActive)
            {
                ApplyOverrides();
            }
        }

        protected virtual void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            if (DialogueManager.instance != null)
            {
                DialogueManager.instance.conversationStarted -= OnConversationStarted;
            }

            _subscribed = false;
        }

        protected virtual void OnConversationStarted(Transform actor)
        {
            ApplyOverrides();
        }

        /// <summary>데이터베이스의 아군 액터 전부에 대해 자막 패널을 아군 패널로 덮어쓴다.</summary>
        public virtual void ApplyOverrides()
        {
            var dialogueUI = DialogueManager.dialogueUI as IStandardDialogueUI;
            if (dialogueUI == null || DialogueManager.masterDatabase == null)
            {
                return;
            }

            if (!HasPanel(AllyPanelNumber))
            {
                return;
            }

            SubtitlePanelNumber panelNumber = PanelNumberUtility.IntToSubtitlePanelNumber(AllyPanelNumber);
            var actors = DialogueManager.masterDatabase.actors;

            for (int i = 0; i < actors.Count; i++)
            {
                Actor actor = actors[i];
                if (!DialogueSpeakerTypes.IsAlly(actor))
                {
                    continue;
                }

                dialogueUI.OverrideActorPanel(actor, panelNumber);

                if (DebugLog)
                {
                    Debug.Log($"[DialogueSpeakerPanels] 아군 '{actor.Name}' → 자막 패널 {AllyPanelNumber}", this);
                }
            }
        }

        /// <summary>
        /// UI에 그 번호의 패널이 실제로 있는지 본다. 없으면 덮어써도 기본 패널로 떨어져
        /// "설정은 했는데 그대로 NPC 창으로 나온다"가 되므로, 원인을 한 번 알려준다.
        /// </summary>
        protected virtual bool HasPanel(int panelNumber)
        {
            var standardUI = DialogueManager.dialogueUI as StandardDialogueUI;
            if (standardUI == null || standardUI.conversationUIElements == null)
            {
                return true;
            }

            var panels = standardUI.conversationUIElements.subtitlePanels;
            if (panels != null && panelNumber >= 0 && panelNumber < panels.Length && panels[panelNumber] != null)
            {
                return true;
            }

            if (!_warnedAboutMissingPanel)
            {
                _warnedAboutMissingPanel = true;
                Debug.LogWarning($"[DialogueSpeakerPanels] 대화창 UI에 {AllyPanelNumber}번 자막 패널이 없습니다. " +
                                 "'Tools/Aiara/대화창 UI 셋업'을 한 번 실행해 아군 패널을 만들어 주세요. " +
                                 "그전까지 아군 대사는 NPC 창으로 나옵니다.", this);
            }

            return false;
        }
    }
}
