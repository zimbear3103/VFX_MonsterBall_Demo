using System;
using System.Collections.Generic;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using Yeolha.BeltScroll;

namespace Aiara.Dialogue
{
    /// <summary>
    /// EventNodeData의 <c>EventType = Action</c> 노드가 실어 보낸 <b>액션 ID</b>를 실제 연출로 바꾼다.
    ///
    /// 흐름: CSV(Data 열) → 임포터가 <c>AiaraAction(액션ID)</c> 시퀀서 커맨드로 변환
    ///       → <see cref="PixelCrushers.DialogueSystem.SequencerCommands.SequencerCommandAiaraAction"/>
    ///       → 여기 <see cref="Run"/>.
    ///
    /// 예) <c>PlayerTalk1</c> → 플레이어 Animator의 <c>Talk1</c> 트리거
    ///     <c>PlayerTalk2</c> → <c>Talk2</c> 트리거 + 보조 NPC 등장
    ///
    /// <b>대화 시작만으로는 어떤 모션도 나가지 않는다.</b> 예전에는 대화가 열리면 무조건 Talk 트리거가
    /// 나갔지만, 그러면 모든 대화가 같은 자세로 시작해 연출을 대사별로 나눌 수 없었다. 지금은
    /// 대화 자세(IsTalk)만 켜두고, 어느 대사에서 어떤 모션을 태울지는 시트가 정한다.
    ///
    /// 보조 NPC의 <b>활성/비활성도 액션이 정한다</b> — 보조 NPC를 데리고 나오는 액션(PlayerTalk2)이
    /// 세우고, 데리고 나오지 않는 액션이 오면 거둔다. 대화가 끝나면 <see cref="ClearPlayer"/>가 마저 정리한다.
    ///
    /// 새 액션을 추가하려면 <see cref="Actions"/>에 한 줄 넣고, 필요하면 Animator에 같은 이름의
    /// 트리거를 만들면 된다(파라미터가 없으면 조용히 무시된다).
    /// </summary>
    public static class DialogueActions
    {
        /// <summary>액션 하나가 무엇을 하는지.</summary>
        public struct TalkAction
        {
            /// <summary>플레이어 Animator에 쏠 트리거. 비우면 트리거는 쏘지 않는다.</summary>
            public string Trigger;

            /// <summary>보조 NPC를 세울지. false면 이미 나와 있던 보조 NPC를 거둔다.</summary>
            public bool ShowCompanion;
        }

        /// <summary>
        /// 액션 ID → 연출 표. 대소문자는 구분하지 않는다(시트 표기가 흔들려도 걸리도록).
        ///
        /// 지금 쓰는 것은 이 둘뿐이다. 예전에 대화 시작마다 자동으로 나가던 Talk 트리거는
        /// 대응하는 액션을 두지 않았다 — 필요해지면 여기에 한 줄 넣으면 된다.
        /// </summary>
        private static readonly Dictionary<string, TalkAction> Actions =
            new Dictionary<string, TalkAction>(StringComparer.OrdinalIgnoreCase)
            {
                { "PlayerTalk1", new TalkAction { Trigger = AnimParams.Talk1, ShowCompanion = false } },
                { "PlayerTalk2", new TalkAction { Trigger = AnimParams.Talk2, ShowCompanion = true } },
            };

        /// <summary>대화를 연 쪽(존)이 등록해 준 플레이어. 액션은 이 캐릭터에 걸린다.</summary>
        private static Character _player;

        /// <summary>이번 대화에서 실제로 연출을 걸어둔 대상. 걸어둔 것만 거둔다.</summary>
        private static Character _staged;

        private static bool _subscribed;

        /// <summary>
        /// 정적 상태를 플레이 시작마다 되돌린다. 도메인 리로드를 끈 설정에서는 지난 플레이의
        /// 등록·구독이 그대로 남아 다음 플레이에서 죽은 캐릭터를 붙들고 있게 된다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _player = null;
            _staged = null;
            _subscribed = false;
        }

        /// <summary>지금 대화의 주인공으로 등록된 캐릭터. 없으면 null.</summary>
        public static Character Player => _player;

        /// <summary>대화를 열면서 주인공을 등록한다. 존이 부른다.</summary>
        public static void SetPlayer(Character character)
        {
            _player = character;
        }

        /// <summary>
        /// 대화가 끝났다 — 등록을 지우고, 액션이 걸어둔 연출(트리거·보조 NPC)이 있으면 거둔다.
        ///
        /// 존이 연출 스위치를 껐더라도 액션은 돌 수 있으므로, 정리는 연출 스위치와 무관하게 여기서 한다.
        /// 여러 번 불러도 안전하다.
        /// </summary>
        public static void ClearPlayer(Character character = null)
        {
            if (character != null && _player != null && character != _player)
            {
                // 다른 캐릭터의 대화가 이미 등록을 가져간 뒤다. 남의 것을 지우지 않는다.
                return;
            }

            if (_staged != null)
            {
                DialogueTalkPresentation.End(_staged);
                _staged = null;
            }

            _player = null;
            Unsubscribe();
        }

        /// <summary>
        /// 액션을 실행한다. 아는 액션이면 true.
        /// </summary>
        /// <param name="actionId">EventNodeData의 Data 값 (예: PlayerTalk2)</param>
        /// <param name="speaker">시퀀서가 알려준 화자. 등록된 플레이어가 없을 때 후보로 쓴다.</param>
        /// <param name="listener">시퀀서가 알려준 청자. 같은 용도.</param>
        public static bool Run(string actionId, Transform speaker = null, Transform listener = null)
        {
            if (string.IsNullOrEmpty(actionId))
            {
                return false;
            }

            if (!Actions.TryGetValue(actionId.Trim(), out TalkAction action))
            {
                return false;
            }

            Character character = ResolvePlayer(speaker, listener);
            if (character == null)
            {
                Debug.LogWarning($"[DialogueActions] '{actionId}'을(를) 걸 플레이어를 찾지 못했습니다. " +
                                 "대화를 연 존이 플레이어를 등록했는지 확인하세요.");
                return true;
            }

            // 대화가 끝날 때 되돌릴 대상. 존이 정리해 주지만, 존 없이 시작한 대화도 있어 스스로도 구독해 둔다.
            _staged = character;
            Subscribe();

            DialogueTalkPresentation.FireTalkTrigger(character, action.Trigger);
            DialogueTalkPresentation.SetCompanion(character, action.ShowCompanion);

            return true;
        }

        /// <summary>
        /// 액션을 걸 캐릭터를 찾는다.
        ///   ① 존이 등록해 준 플레이어 → ② 시퀀서의 화자/청자 → ③ 씬에 살아 있는 플레이어 진영 캐릭터.
        /// ②③은 존을 거치지 않고 시작한 대화(테스트 등)를 위한 보험이다.
        /// </summary>
        public static Character ResolvePlayer(Transform speaker, Transform listener)
        {
            if (_player != null)
            {
                return _player;
            }

            Character fromSpeaker = FindPlayerCharacter(speaker) ?? FindPlayerCharacter(listener);
            if (fromSpeaker != null)
            {
                return fromSpeaker;
            }

            IReadOnlyList<Character> active = Character.ActiveCharacters;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i] != null && active[i].Faction == FactionType.Player)
                {
                    return active[i];
                }
            }

            return null;
        }

        private static Character FindPlayerCharacter(Transform t)
        {
            if (t == null)
            {
                return null;
            }

            Character character = t.GetComponentInParent<Character>();
            return character != null && character.Faction == FactionType.Player ? character : null;
        }

        private static void Subscribe()
        {
            if (_subscribed || DialogueManager.instance == null)
            {
                return;
            }

            DialogueManager.instance.conversationEnded += OnConversationEnded;
            _subscribed = true;
        }

        private static void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            if (DialogueManager.instance != null)
            {
                DialogueManager.instance.conversationEnded -= OnConversationEnded;
            }

            _subscribed = false;
        }

        private static void OnConversationEnded(Transform actor)
        {
            ClearPlayer();
        }
    }
}
