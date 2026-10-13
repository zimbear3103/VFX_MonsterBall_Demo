using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Yeolha.BeltScroll;

namespace Aiara.Dialogue.EditorTools
{
    /// <summary>
    /// 대화 중 감출 오브젝트를 <see cref="DialogueHiddenObjects"/>에 등록해 준다.
    /// 메뉴: <b>Tools/Aiara/대화 중 감출 오브젝트 등록</b> / <b>… 비우기</b>
    ///
    /// 쓰는 법 — 감출 오브젝트(플레이어가 들고 있는 무기 등)를 <b>하이어라키에서 고르고</b> 메뉴를 누른다.
    /// 고른 오브젝트가 딸린 캐릭터 루트를 찾아 컴포넌트를 붙이고 목록에 넣는다. 여러 개를 한꺼번에 골라도 된다.
    ///
    /// 프리팹(플레이어)에 넣으려면 <b>프리팹을 열고</b>(더블클릭 → Prefab Stage) 그 안에서 고른 뒤 실행한다.
    /// 프로젝트 창의 프리팹 애셋을 그대로 고른 경우는 막는다 — 그 상태로 붙이면 Undo도 저장도 어긋난다.
    /// </summary>
    public static class DialogueHiddenObjectsSetup
    {
        private const string RegisterMenu = "Tools/Aiara/대화 중 감출 오브젝트 등록";
        private const string ClearMenu = "Tools/Aiara/대화 중 감출 오브젝트 비우기";

        [MenuItem(RegisterMenu, false, 25)]
        public static void Register()
        {
            GameObject[] selection = Selection.gameObjects;
            if (selection == null || selection.Length == 0)
            {
                Debug.LogWarning("[DialogueHiddenObjects] 감출 오브젝트를 하이어라키에서 먼저 고르세요.");
                return;
            }

            var touched = new HashSet<DialogueHiddenObjects>();
            int added = 0;

            foreach (GameObject target in selection)
            {
                if (!IsEditable(target))
                {
                    continue;
                }

                Character owner = target.GetComponentInParent<Character>(true);
                if (owner == null)
                {
                    Debug.LogWarning($"[DialogueHiddenObjects] '{target.name}'의 위쪽에서 Character를 찾지 못했습니다. " +
                                     "캐릭터(플레이어) 안의 오브젝트를 고르세요.", target);
                    continue;
                }

                if (target == owner.gameObject)
                {
                    Debug.LogWarning($"[DialogueHiddenObjects] '{target.name}'은(는) 캐릭터 자신이라 등록하지 않습니다 — " +
                                     "대화 중 플레이어가 통째로 사라집니다.", target);
                    continue;
                }

                DialogueHiddenObjects hider = owner.GetComponentInChildren<DialogueHiddenObjects>(true);
                if (hider == null)
                {
                    hider = Undo.AddComponent<DialogueHiddenObjects>(owner.gameObject);
                }

                if (hider.Objects == null)
                {
                    hider.Objects = new List<GameObject>();
                }

                if (hider.Objects.Contains(target))
                {
                    Debug.Log($"[DialogueHiddenObjects] '{target.name}'은(는) 이미 등록돼 있습니다.", target);
                    continue;
                }

                Undo.RecordObject(hider, "대화 중 감출 오브젝트 등록");
                hider.Objects.Add(target);
                EditorUtility.SetDirty(hider);
                touched.Add(hider);
                added++;

                Debug.Log($"[DialogueHiddenObjects] '{owner.name}'에 '{target.name}'을(를) 등록했습니다.", hider);
            }

            Save(touched);

            if (added > 0)
            {
                Debug.Log($"[DialogueHiddenObjects] {added}개를 등록했습니다. " +
                          "프리팹 안에서 작업했다면 프리팹을 저장해야 남습니다.");
            }
        }

        [MenuItem(ClearMenu, false, 26)]
        public static void Clear()
        {
            GameObject[] selection = Selection.gameObjects;
            if (selection == null || selection.Length == 0)
            {
                Debug.LogWarning("[DialogueHiddenObjects] 캐릭터(또는 그 안의 오브젝트)를 먼저 고르세요.");
                return;
            }

            var touched = new HashSet<DialogueHiddenObjects>();

            foreach (GameObject target in selection)
            {
                if (!IsEditable(target))
                {
                    continue;
                }

                Character owner = target.GetComponentInParent<Character>(true);
                DialogueHiddenObjects hider = owner != null
                    ? owner.GetComponentInChildren<DialogueHiddenObjects>(true)
                    : target.GetComponentInChildren<DialogueHiddenObjects>(true);

                if (hider == null || hider.Objects == null || hider.Objects.Count == 0)
                {
                    continue;
                }

                Undo.RecordObject(hider, "대화 중 감출 오브젝트 비우기");
                Debug.Log($"[DialogueHiddenObjects] '{hider.name}'의 목록 {hider.Objects.Count}개를 비웠습니다.", hider);
                hider.Objects.Clear();
                EditorUtility.SetDirty(hider);
                touched.Add(hider);
            }

            Save(touched);
        }

        /// <summary>
        /// 프로젝트 창의 프리팹 애셋은 거른다. 씬이나 Prefab Stage 안의 오브젝트만 손댄다 —
        /// 애셋에 직접 붙이면 Undo가 걸리지 않고 저장 시점도 어긋난다.
        /// </summary>
        private static bool IsEditable(GameObject target)
        {
            if (target == null)
            {
                return false;
            }

            if (PrefabUtility.IsPartOfPrefabAsset(target))
            {
                Debug.LogWarning($"[DialogueHiddenObjects] '{target.name}'은(는) 프로젝트 창의 프리팹 애셋입니다. " +
                                 "프리팹을 열고(더블클릭) 그 안에서 고른 뒤 다시 실행하세요.", target);
                return false;
            }

            return true;
        }

        /// <summary>바뀐 것을 씬/프리팹 스테이지에 표시한다. 저장은 평소처럼 Ctrl+S.</summary>
        private static void Save(HashSet<DialogueHiddenObjects> touched)
        {
            foreach (DialogueHiddenObjects hider in touched)
            {
                if (hider == null)
                {
                    continue;
                }

                var stage = PrefabStageUtility.GetPrefabStage(hider.gameObject);
                if (stage != null)
                {
                    EditorSceneManager.MarkSceneDirty(stage.scene);
                    continue;
                }

                EditorSceneManager.MarkSceneDirty(hider.gameObject.scene);
            }
        }
    }
}
