using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// Portal 인스펙터. 기본 필드 위에 "이 포탈이 실제로 어디로 보내는가"를 얹는다.
    /// EntryKey는 직접 타이핑 대신 대상 그룹에서 실제로 찾은 키 중에서 고르게 해 오타를 없앤다.
    /// </summary>
    [CustomEditor(typeof(Portal))]
    [CanEditMultipleObjects]
    public class PortalInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (targets.Length > 1) return;

            Portal portal = (Portal)target;

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("포탈 툴 열기", GUILayout.Height(24f)))
                {
                    Selection.activeGameObject = portal.gameObject;
                    SceneFlowToolWindow.Open();
                }

                if (GUILayout.Button("트리거 맞추기", GUILayout.Height(24f)))
                    SceneFlowAuthoring.EnsureTrigger(portal);
            }

            DrawEntryKeyPicker(portal);
            DrawTargetSection(portal);
            DrawIssueCount(portal);
        }

        /// <summary>대상 그룹에서 실제로 발견된 키 목록에서 고른다. 비우면 그룹 기본 키를 쓴다.</summary>
        private static void DrawEntryKeyPicker(Portal portal)
        {
            SceneGroup group = portal.TargetGroup;
            if (group == null) return;

            SceneFlowAuthoring.GroupScan scan = SceneFlowAuthoring.ScanGroup(group);
            string[] keys = scan.Keys();

            List<string> options = new List<string> { $"(그룹 기본: {group.DefaultEntryKey})" };
            options.AddRange(keys);

            int current = 0;
            if (!string.IsNullOrEmpty(portal.EntryKey))
            {
                current = System.Array.IndexOf(keys, portal.EntryKey);
                current = current >= 0 ? current + 1 : 0;
            }

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup("EntryKey 선택", current, options.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                string key = picked == 0 ? string.Empty : keys[picked - 1];
                SceneFlowAuthoring.SetPortalTarget(portal, group, key);
            }

            if (keys.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "대상 그룹의 씬에서 SceneEntryPoint를 하나도 찾지 못했습니다. 그룹 씬을 열어 진입점을 배치하세요.",
                    MessageType.Warning);
            }
        }

        private static void DrawTargetSection(Portal portal)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("대상", EditorStyles.boldLabel);

            SceneGroup group = portal.TargetGroup;
            if (group == null)
            {
                EditorGUILayout.LabelField("대상 그룹이 지정되지 않았습니다.");
                return;
            }

            string key = SceneFlowAuthoring.ResolvedEntryKey(portal);
            EditorGUILayout.LabelField($"{group.name} / '{key}'");

            foreach (string sceneName in group.EnumerateSceneNames())
                EditorGUILayout.LabelField("· " + sceneName, EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("대상 그룹 씬 열기"))
                    SceneFlowSceneOps.OpenGroupScenes(group);

                if (GUILayout.Button("이름을 대상에 맞추기"))
                {
                    Undo.RecordObject(portal.gameObject, "Rename Portal");
                    portal.gameObject.name = SceneFlowAuthoring.PortalName(group, portal.EntryKey);
                }
            }
        }

        private static void DrawIssueCount(Portal portal)
        {
            List<SceneFlowValidator.Issue> issues = SceneFlowValidator.ValidatePortal(portal);
            if (issues.Count == 0) return;

            int errors = SceneFlowValidator.CountErrors(issues);
            EditorGUILayout.HelpBox(
                $"검증 항목 {issues.Count}건 (오류 {errors}건). 포탈 툴의 '검증'에서 확인하세요.",
                errors > 0 ? MessageType.Error : MessageType.Warning);
        }

        private void OnSceneGUI()
        {
            SceneFlowGizmos.DrawPortal((Portal)target, selected: true);
        }

        /// <summary>선택하지 않았을 때도 포탈 범위가 보이도록.</summary>
        [DrawGizmo(GizmoType.NonSelected | GizmoType.Pickable)]
        private static void DrawUnselected(Portal portal, GizmoType type)
        {
            Collider col = portal.GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = new Color(0.6f, 0.4f, 1f, 0.35f);
            Bounds b = col.bounds;
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
