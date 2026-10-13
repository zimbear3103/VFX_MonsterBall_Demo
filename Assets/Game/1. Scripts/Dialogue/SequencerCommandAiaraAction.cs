using Aiara.Dialogue;
using UnityEngine;

// Pixel Crushers 관례에 맞춰 이 네임스페이스에 둔다.
// Sequencer가 "SequencerCommand" + 커맨드이름 으로 클래스를 찾아 자동 등록하므로 별도 등록 절차는 없다.
namespace PixelCrushers.DialogueSystem.SequencerCommands
{
    /// <summary>
    /// 시퀀서 커맨드: AiaraAction(액션ID)
    ///
    /// EventNodeData의 EventType이 Action인 노드가 이 커맨드로 변환된다.
    /// (예: PlayerTalk2 → <c>AiaraAction(PlayerTalk2)</c>)
    ///
    /// 무엇을 할지는 <see cref="DialogueActions"/>의 표가 정한다. 이 커맨드는 ID를 넘기고
    /// 곧바로 다음 노드로 넘어간다 — 대화 모션은 대사 위에 얹히는 연출이라
    /// 대화가 여기서 기다릴 이유가 없다.
    ///
    /// 표에 없는 ID는 아직 연출이 없는 액션(장면 전환 등)이다. 로그만 남기고 지나간다.
    /// </summary>
    [AddComponentMenu("")] // 컴포넌트 메뉴에는 숨긴다.
    public class SequencerCommandAiaraAction : SequencerCommand
    {
        public void Start()
        {
            string actionId = GetParameter(0);

            if (string.IsNullOrEmpty(actionId))
            {
                Debug.LogWarning("[AiaraAction] 액션 ID가 비어 있습니다.");
            }
            else if (!DialogueActions.Run(actionId, speaker, listener))
            {
                Debug.Log($"[AiaraAction] '{actionId}' (연출이 없는 액션 — 건너뜁니다)");
            }

            Stop();
        }
    }
}
