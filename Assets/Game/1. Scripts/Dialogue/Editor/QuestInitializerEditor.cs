using System.Collections.Generic;
using PixelCrushers.DialogueSystem;
using UnityEditor;
using UnityEngine;

namespace Aiara.Dialogue.EditorTools
{
    /// <summary>
    /// <see cref="QuestInitializer"/> 인스펙터.
    ///
    /// 왜 필요한가 —
    /// 퀘스트 ID는 <c>QUEST_001_B_5_002</c>처럼 길고 서로 비슷해서 손으로 치면 반드시 오타가 난다.
    /// 게다가 틀려도 실행 전까지는 아무 일도 안 일어나고, 실행해도 "이벤트가 안 열린다"로만 나타나
    /// 원인을 찾기 어렵다. 그래서 <b>DB에 있는 퀘스트를 드롭다운으로 고르게</b> 한다.
    ///
    /// DB에 없는 ID(시트에서 지웠거나 아직 안 만든 것)도 <b>지우지 않고 그대로 둔다</b> —
    /// 목록을 열었다는 이유만으로 남의 데이터를 날리면 안 되기 때문이다. 대신 눈에 띄게 표시하고
    /// "직접 입력" 칸으로 고칠 수 있게 해준다.
    ///
    /// 실행 중에는 각 퀘스트의 <b>현재 상태</b>를 같이 보여준다. 초기화가 실제로 먹었는지,
    /// 대화가 진행되면서 상태가 어떻게 바뀌는지 인스펙터에서 바로 확인할 수 있다.
    /// </summary>
    [CustomEditor(typeof(QuestInitializer), true)]
    public class QuestInitializerEditor : UnityEditor.Editor
    {
        private const string DatabasePath = "Assets/Data/YeolhaDialogueDatabase.asset";

        /// <summary>드롭다운 맨 앞에 두는 "직접 입력" 항목. DB에 없는 ID를 손으로 넣을 때 쓴다.</summary>
        private const string CustomEntryLabel = "— 직접 입력 —";

        private string[] _questIds;
        private GUIContent[] _dropdownLabels;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            RefreshQuestIds();

            if (_questIds.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    $"퀘스트 DB를 찾지 못했습니다: {DatabasePath}\n" +
                    "Tools/Aiara/대화 데이터 임포트를 먼저 실행하세요. 그전까지는 ID를 직접 입력해야 합니다.",
                    MessageType.Warning);
            }

            DrawQuestList(serializedObject.FindProperty("QuestsToActivate"),
                "활성화할 퀘스트 (Active)",
                "시작할 때 active로 세운다. 아직 안 받은(unassigned) 퀘스트에만 걸린다.");

            EditorGUILayout.Space();

            DrawQuestList(serializedObject.FindProperty("QuestsToComplete"),
                "완료 처리할 퀘스트 (Success)",
                "이미 깬 셈 치고 success로 세운다. 중간 이벤트부터 테스트할 때 쓴다. " +
                "같은 ID가 양쪽에 있으면 완료가 이긴다.");

            EditorGUILayout.Space();

            DrawPropertiesExcluding(serializedObject,
                "m_Script", "QuestsToActivate", "QuestsToComplete");

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>DB에서 퀘스트 ID를 모은다. 퀘스트는 Is Item = false인 Item이다.</summary>
        private void RefreshQuestIds()
        {
            if (_questIds != null)
            {
                return;
            }

            var ids = new List<string>();
            var database = AssetDatabase.LoadAssetAtPath<DialogueDatabase>(DatabasePath);

            if (database != null)
            {
                foreach (Item item in database.items)
                {
                    if (item != null && !item.IsItem)
                    {
                        ids.Add(item.Name);
                    }
                }
            }

            ids.Sort(System.StringComparer.Ordinal);
            _questIds = ids.ToArray();

            _dropdownLabels = new GUIContent[_questIds.Length + 1];
            _dropdownLabels[0] = new GUIContent(CustomEntryLabel);
            for (int i = 0; i < _questIds.Length; i++)
            {
                _dropdownLabels[i + 1] = new GUIContent(_questIds[i]);
            }
        }

        private void DrawQuestList(SerializedProperty list, string title, string help)
        {
            if (list == null)
            {
                return;
            }

            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(help, MessageType.None);

            int removeAt = -1;

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);

                EditorGUILayout.BeginHorizontal();
                DrawQuestField(element);

                if (GUILayout.Button("−", GUILayout.Width(24)))
                {
                    removeAt = i;
                }

                EditorGUILayout.EndHorizontal();

                DrawStateOrWarning(element.stringValue);
            }

            if (removeAt >= 0)
            {
                list.DeleteArrayElementAtIndex(removeAt);
            }

            if (GUILayout.Button("퀘스트 추가", GUILayout.Height(20)))
            {
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).stringValue = string.Empty;
            }
        }

        /// <summary>
        /// 한 칸을 드롭다운으로 그린다. DB에 없는 값이면 드롭다운은 "직접 입력"에 두고
        /// 옆에 텍스트 칸을 붙여 값을 <b>보존</b>한다.
        /// </summary>
        private void DrawQuestField(SerializedProperty element)
        {
            string current = element.stringValue;
            int index = System.Array.IndexOf(_questIds, current);

            // DB에 있으면 목록의 위치(+1, 0번은 "직접 입력"), 없으면 0번.
            int selected = index >= 0 ? index + 1 : 0;

            int picked = EditorGUILayout.Popup(selected, _dropdownLabels);
            if (picked != selected)
            {
                element.stringValue = picked == 0 ? string.Empty : _questIds[picked - 1];
                return;
            }

            // "직접 입력"일 때만 텍스트 칸을 준다 — 드롭다운으로 고른 값을 실수로 덧쓰지 않도록.
            if (picked == 0)
            {
                element.stringValue = EditorGUILayout.TextField(current);
            }
        }

        private void DrawStateOrWarning(string questId)
        {
            if (string.IsNullOrEmpty(questId))
            {
                return;
            }

            if (_questIds.Length > 0 && System.Array.IndexOf(_questIds, questId) < 0)
            {
                EditorGUILayout.HelpBox($"DB에 없는 퀘스트입니다: {questId}", MessageType.Warning);
                return;
            }

            // 실행 중에만 의미가 있다. 에디터에서는 전부 unassigned라 보여줄 게 없다.
            if (Application.isPlaying && DialogueManager.instance != null)
            {
                EditorGUILayout.LabelField(" ", $"현재 상태: {QuestLog.GetQuestState(questId)}",
                    EditorStyles.miniLabel);
            }
        }
    }
}
