using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Yeolha.BeltScroll;

namespace Aiara.Dialogue.EditorTools
{
    /// <summary>
    /// 대화 액션이 쏘는 트리거 파라미터를 Animator Controller에 만들어 준다.
    /// 메뉴: <b>Tools/Aiara/대화 Talk 트리거 파라미터 추가</b>
    ///
    /// EventNodeData의 Action 노드(PlayerTalk1/PlayerTalk2 …)는
    /// <see cref="DialogueTalkPresentation.TalkTriggers"/>의 이름으로 트리거를 쏜다.
    /// 파라미터가 없으면 <see cref="CharacterStateManager"/>의 가드가 조용히 무시하므로
    /// "아무 일도 안 일어나는" 상태가 되는데, 원인이 눈에 띄지 않는다. 그래서 손으로 만들지 않고 이 메뉴로 만든다.
    ///
    /// <b>파라미터만 만든다.</b> 어떤 상태로 전이할지(AnyState → Talk1/Talk2 등)는 클립과 함께
    /// Animator 창에서 직접 만들어야 한다 — 그건 연출 설계라 자동화할 수 없다.
    ///
    /// 대상은 이미 <see cref="AnimParams.IsTalk"/>를 들고 있는 컨트롤러다. 대화 자세를 쓰지 않는
    /// 몬스터 컨트롤러까지 건드리지 않기 위해서다.
    /// </summary>
    public static class DialogueTalkAnimatorSetup
    {
        [MenuItem("Tools/Aiara/대화 Talk 트리거 파라미터 추가", false, 24)]
        public static void AddTalkTriggers()
        {
            string[] guids = AssetDatabase.FindAssets("t:AnimatorController");
            var report = new StringBuilder();
            int touched = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller == null || !HasParameter(controller, AnimParams.IsTalk))
                {
                    continue;
                }

                List<string> added = AddMissingTriggers(controller);
                if (added.Count == 0)
                {
                    continue;
                }

                touched++;
                report.AppendLine($"  {path}\n    + {string.Join(", ", added)}");
            }

            if (touched == 0)
            {
                Debug.Log("[DialogueTalkAnimatorSetup] 추가할 파라미터가 없습니다 " +
                          "(대화 자세를 쓰는 컨트롤러에 이미 전부 있습니다).");
                return;
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"[DialogueTalkAnimatorSetup] 컨트롤러 {touched}개에 트리거를 추가했습니다.\n{report}" +
                      "전이(AnyState → 해당 상태)는 Animator 창에서 직접 만들어 주세요.");
        }

        /// <summary>없는 대화 트리거를 채워 넣고, 실제로 추가한 이름을 돌려준다.</summary>
        private static List<string> AddMissingTriggers(AnimatorController controller)
        {
            var added = new List<string>();

            foreach (string trigger in DialogueTalkPresentation.TalkTriggers)
            {
                if (string.IsNullOrEmpty(trigger) || HasParameter(controller, trigger))
                {
                    continue;
                }

                controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
                added.Add(trigger);
            }

            return added;
        }

        private static bool HasParameter(AnimatorController controller, string name)
        {
            AnimatorControllerParameter[] parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == name)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
