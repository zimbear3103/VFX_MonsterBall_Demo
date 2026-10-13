using UnityEngine;
using Yeolha.BeltScroll;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 대화 연출의 <b>손발</b> — 플레이어의 대화 자세·대화 애니메이션·보조 NPC를 실제로 켜고 끈다.
    /// 어떤 대사에서 무엇을 걸지는 여기서 정하지 않는다. 그건 데이터(EventNodeData의 Action 노드)가 정하고
    /// <see cref="DialogueActions"/>가 읽어 이쪽으로 넘긴다.
    ///
    ///   - <see cref="Begin"/>: 대화 자세(<see cref="AnimParams.IsTalk"/>)를 켜고,
    ///     <see cref="DialogueHiddenObjects"/>에 적힌 오브젝트(손에 든 무기 등)를 감춘다.
    ///     <b>예전과 달리 Talk 트리거는 여기서 쏘지 않는다</b> — 모든 대화가 같은 모션으로 시작하는 문제 때문에
    ///     지금은 Action 노드가 지목한 모션(PlayerTalk1 → Talk1, PlayerTalk2 → Talk2)만 나간다.
    ///     보조 NPC도 마찬가지로 액션(PlayerTalk2)이 부를 때만 나온다.
    ///   - <see cref="End"/>: IsTalk를 끄고, 액션이 쐈을 수 있는 트리거를 <b>전부</b> 지우고,
    ///     보조 NPC를 거두고, 감췄던 오브젝트를 되돌린다.
    ///     아무도 소비하지 않은 트리거는 Animator에 남아 있다가 엉뚱한 순간에 대화 클립을 재생시킨다.
    ///
    /// <b>조작 잠금과는 별개다.</b> 잠그지 않는 대화에도 연출은 걸 수 있고 그 반대도 된다.
    ///
    /// 파라미터가 없는 캐릭터에서는 <see cref="CharacterStateManager"/>의 존재 여부 가드가 알아서 무시한다.
    /// (Talk1·Talk2를 아직 만들지 않은 Animator에서도 경고 없이 조용히 지나간다.)
    /// </summary>
    public static class DialogueTalkPresentation
    {
        /// <summary>
        /// 액션이 쏠 수 있는 대화 트리거 전부. 대화가 끝날 때 <b>쏜 것만이 아니라 전부</b> 지운다 —
        /// 어느 것이 소비되지 않고 남았는지는 Animator 바깥에서 알 수 없다.
        ///
        /// 이 목록이 <b>Talk 트리거 파라미터 추가</b> 메뉴가 만들어 주는 파라미터 목록이기도 하다.
        /// 새 대화 모션을 추가하면 여기에도 같이 넣어야 한다.
        /// </summary>
        public static readonly string[] TalkTriggers =
        {
            AnimParams.Talk1,
            AnimParams.Talk2,
        };

        /// <summary>
        /// 대화 자세로 들어가고, 대화 중 감출 오브젝트를 감춘다.
        /// 실제 대화 모션·보조 NPC는 Action 노드가 부를 때 나온다.
        /// </summary>
        public static void Begin(Character character)
        {
            SetTalkPose(character, true);
            SetHiddenObjects(character, true);
        }

        /// <summary>대화 자세를 풀고 트리거·보조 NPC를 모두 거둔다. 시작하지 않았어도 부르는 것은 안전하다.</summary>
        public static void End(Character character)
        {
            if (character == null)
            {
                return;
            }

            CharacterStateManager states = character.StateManager;
            if (states != null)
            {
                states.SetBoolParam(AnimParams.IsTalk, false);

                for (int i = 0; i < TalkTriggers.Length; i++)
                {
                    states.ResetTrigger(TalkTriggers[i]);
                }
            }

            SetCompanion(character, false);
            SetHiddenObjects(character, false);
        }

        /// <summary>대화 중 유지 상태(<see cref="AnimParams.IsTalk"/>)만 켜고 끈다.</summary>
        public static void SetTalkPose(Character character, bool talking)
        {
            if (character == null)
            {
                return;
            }

            character.StateManager?.SetBoolParam(AnimParams.IsTalk, talking);
        }

        /// <summary>
        /// 대화 모션 트리거를 쏜다.
        ///
        /// 전이 조건이 IsTalk와 트리거를 함께 보는 경우가 있어 <b>IsTalk를 먼저 세운 뒤</b> 쏜다.
        /// 순서가 뒤집히면 트리거가 헛돈다.
        /// </summary>
        public static void FireTalkTrigger(Character character, string trigger)
        {
            if (character == null || string.IsNullOrEmpty(trigger))
            {
                return;
            }

            CharacterStateManager states = character.StateManager;
            if (states == null)
            {
                return;
            }

            states.SetBoolParam(AnimParams.IsTalk, true);
            states.FireTrigger(trigger);
        }

        /// <summary>보조 NPC를 세우거나 거둔다. 달려 있지 않으면 아무 일도 하지 않는다.</summary>
        public static void SetCompanion(Character character, bool show)
        {
            if (character == null)
            {
                return;
            }

            // 평소에는 꺼져 있는 오브젝트라 반드시 비활성 포함으로 찾아야 한다.
            var companion = character.GetComponentInChildren<DialogueCompanion>(true);
            if (companion == null)
            {
                return;
            }

            if (show)
            {
                // 등장 딜레이 동안 보조 캐릭터는 아직 꺼져 있어 스스로 시간을 셀 수 없다. 캐릭터가 대신 센다.
                companion.BeginTalk(character);
            }
            else
            {
                companion.EndTalk();
            }
        }

        /// <summary>
        /// 대화 중 감출 오브젝트(손에 든 무기 등)를 감추거나 되돌린다.
        /// <see cref="DialogueHiddenObjects"/>가 달려 있지 않으면 아무 일도 하지 않는다.
        /// </summary>
        public static void SetHiddenObjects(Character character, bool hide)
        {
            if (character == null)
            {
                return;
            }

            // 감출 대상 자체가 꺼져 있을 수 있으므로 비활성 포함으로 찾는다.
            var hider = character.GetComponentInChildren<DialogueHiddenObjects>(true);
            if (hider == null)
            {
                return;
            }

            if (hide)
            {
                hider.BeginTalk();
            }
            else
            {
                hider.EndTalk();
            }
        }
    }
}
