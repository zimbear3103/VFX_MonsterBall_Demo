using MoreMountains.Tools;
using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace Aiara.Dialogue
{
    /// <summary>
    /// 게임 시작 시 특정 퀘스트를 활성/완료 상태로 만든다.
    ///
    /// 임포터가 만드는 퀘스트는 전부 unassigned로 시작한다. 그런데 첫 이벤트(EVENT_01_001)는
    /// QUEST_01_001이 활성이어야 열리므로, 최초의 한 개를 켜주는 무언가가 필요하다.
    /// 그 역할을 하는 최소한의 컴포넌트다.
    ///
    /// <see cref="QuestsToComplete"/>는 **이미 깬 셈 치고 시작**할 퀘스트다. 시트의 ClearQuest 조건은
    /// <c>CurrentQuestState(...) == "success"</c>로 들어가므로(임포터), 중간 이벤트부터 확인하려면
    /// 앞선 퀘스트를 success로 세워둬야 그 이벤트가 열린다. 테스트용 시작 지점을 잡을 때 쓴다.
    ///
    /// 나중에 세이브/로드가 붙으면 "저장된 진행도가 없을 때만" 이 초기화가 돌아야 한다.
    /// 활성화는 지금도 unassigned인 퀘스트만 건드려서, 이미 진행 중인 퀘스트를 되돌리지 않는다.
    /// </summary>
    [AddComponentMenu("Aiara/Dialogue/Quest Initializer")]
    public class QuestInitializer : MonoBehaviour
    {
        [MMInspectorGroup("시작 퀘스트", true, 100)]

        [Tooltip("게임 시작 시 활성화할 퀘스트 ID 목록. (예: QUEST_01_001) " +
                 "아직 안 받은(unassigned) 퀘스트만 켜지므로, 이미 진행 중인 것을 처음으로 되돌리지 않는다.")]
        public string[] QuestsToActivate;

        [Tooltip("게임 시작 시 완료(success)로 만들 퀘스트 ID 목록. 이미 깬 셈 치고 시작할 때 쓴다. " +
                 "시트의 ClearQuest 조건이 success를 보므로, 중간 이벤트부터 테스트하려면 여기에 앞선 퀘스트를 넣는다. " +
                 "활성화 목록보다 나중에 처리되므로, 같은 ID를 양쪽에 넣으면 완료가 이긴다.")]
        public string[] QuestsToComplete;

        [MMInspectorGroup("디버그", true, 101)]

        [Tooltip("데이터베이스에 없는 퀘스트 ID가 있으면 경고를 남긴다. 오타를 잡는 데 쓴다.")]
        public bool WarnOnUnknownQuest = true;

        protected virtual void Start()
        {
            bool hasAnything =
                (QuestsToActivate != null && QuestsToActivate.Length > 0) ||
                (QuestsToComplete != null && QuestsToComplete.Length > 0);

            if (!hasAnything)
            {
                return;
            }

            if (DialogueManager.instance == null)
            {
                Debug.LogWarning("[QuestInitializer] 씬에 Dialogue Manager가 없어 퀘스트를 초기화하지 못했습니다.", this);
                return;
            }

            // 완료를 나중에 처리한다 — 같은 ID가 양쪽에 있으면 완료 상태로 끝나야 의도가 분명하다.
            ApplyQuestStates(QuestsToActivate, QuestState.Active);
            ApplyQuestStates(QuestsToComplete, QuestState.Success);
        }

        protected virtual void ApplyQuestStates(string[] questIds, QuestState targetState)
        {
            if (questIds == null)
            {
                return;
            }

            for (int i = 0; i < questIds.Length; i++)
            {
                string questId = questIds[i];
                if (string.IsNullOrEmpty(questId))
                {
                    continue;
                }

                if (WarnOnUnknownQuest && !QuestExists(questId))
                {
                    Debug.LogWarning($"[QuestInitializer] 데이터베이스에 없는 퀘스트입니다: {questId} " +
                                     "(시트의 퀘스트 ID와 철자가 같은지 확인하세요)", this);
                }

                QuestState state = QuestLog.GetQuestState(questId);
                if (state == targetState)
                {
                    continue;
                }

                // 활성화는 아직 안 받은 퀘스트에만 건다 — 진행 중이거나 끝난 퀘스트를 처음으로 되돌리지 않는다.
                // 완료는 그 반대로, 지금 상태와 상관없이 앞당겨 세운다(그게 이 목록을 쓰는 이유다).
                if (targetState == QuestState.Active && state != QuestState.Unassigned)
                {
                    continue;
                }

                QuestLog.SetQuestState(questId, targetState);
            }
        }

        /// <summary>퀘스트는 Is Item=false인 Item으로 들어가 있다. 이름이 맞는지만 확인한다.</summary>
        protected static bool QuestExists(string questId)
        {
            return DialogueManager.masterDatabase != null
                   && DialogueManager.masterDatabase.GetItem(questId) != null;
        }
    }
}
