using System;
using System.Collections.Generic;
using System.Reflection;
using PixelCrushers.DialogueSystem;
using UnityEditor;
using UnityEngine;

namespace Aiara.Dialogue.EditorTools
{
    /// <summary>
    /// <see cref="DialogueChoiceMenuPanel"/> 인스펙터.
    ///
    /// 왜 필요한가 —
    /// Pixel Crushers의 <c>StandardUIMenuPanelEditor</c>는 <c>[CustomEditor(typeof(StandardUIMenuPanel), true)]</c>,
    /// 즉 **파생 클래스까지** 가로챈다. 그런데 그 인스펙터는 그릴 필드를 이름으로 하나하나 지정해서 그리기 때문에,
    /// 상속으로 추가한 필드(Choices 등)는 인스펙터에 아예 나오지 않는다.
    ///
    /// **PC 에디터를 상속하면 안 된다** — 붙어 있는 <c>[CustomEditor]</c>까지 같이 딸려와 등록이 꼬이고,
    /// 결국 PC 인스펙터가 계속 그려진다. 그래서 여기서는 <c>UnityEditor.Editor</c>를 직접 상속하고
    /// 우리 필드를 위에, 나머지 직렬화 필드를 <c>DrawPropertiesExcluding</c>로 아래에 전부 그린다.
    ///
    /// 필드 목록은 이름을 나열하지 않고 <b>리플렉션으로 훑는다</b> — 필드를 하나 추가할 때마다
    /// 여기도 같이 고쳐야 하면 똑같은 함정(인스펙터에서 사라짐)에 다시 빠지기 때문이다.
    /// </summary>
    [CustomEditor(typeof(DialogueChoiceMenuPanel), true)]
    public class DialogueChoiceMenuPanelEditor : UnityEditor.Editor
    {
        private static readonly string[] AlwaysExcluded = { "m_Script" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            List<string> ownFields = OwnFieldNames(target.GetType());

            foreach (string fieldName in ownFields)
            {
                SerializedProperty property = serializedObject.FindProperty(fieldName);
                if (property != null)
                {
                    EditorGUILayout.PropertyField(property, true);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pixel Crushers 기본 설정", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Buttons / Button Template 항목은 이 패널에서 쓰이지 않습니다. 선택지는 위의 Choices로 관리합니다.",
                MessageType.None);

            // 위에서 그린 것 말고 남은 것(=PC 기본 필드)을 전부 그린다.
            // 이름을 나열하지 않으므로 PC를 업데이트해 필드가 늘어나도 그대로 따라간다.
            var excluded = new List<string>(ownFields);
            excluded.AddRange(AlwaysExcluded);
            DrawPropertiesExcluding(serializedObject, excluded.ToArray());

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// StandardUIMenuPanel보다 아래(파생 쪽)에서 선언된 직렬화 필드 이름을 선언 순서대로 돌려준다.
        /// </summary>
        private static List<string> OwnFieldNames(Type type)
        {
            var names = new List<string>();

            for (Type current = type; current != null && current != typeof(StandardUIMenuPanel); current = current.BaseType)
            {
                FieldInfo[] fields = current.GetFields(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                // GetFields의 순서는 보장되지 않는다. MetadataToken 순이 곧 선언 순서다.
                Array.Sort(fields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));

                var level = new List<string>();

                foreach (FieldInfo field in fields)
                {
                    if (field.IsDefined(typeof(HideInInspector), false) ||
                        field.IsDefined(typeof(NonSerializedAttribute), false))
                    {
                        continue;
                    }

                    level.Add(field.Name);
                }

                // 부모 쪽이 위에 오도록 앞에 끼워 넣는다.
                names.InsertRange(0, level);
            }

            return names;
        }
    }
}
