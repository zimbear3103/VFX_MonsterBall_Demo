using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// SceneEntryPoint 인스펙터. 이 진입점이 어느 그룹에 속해 있고 어떤 포탈이 이 키를 부르는지 —
    /// 진입점 오브젝트 자체만 봐서는 절대 알 수 없는 것 — 를 함께 보여준다.
    /// </summary>
    [CustomEditor(typeof(SceneEntryPoint))]
    [CanEditMultipleObjects]
    public class SceneEntryPointInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (targets.Length > 1) return;

            SceneEntryPoint entry = (SceneEntryPoint)target;

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("포탈 툴 열기", GUILayout.Height(24f)))
                {
                    Selection.activeGameObject = entry.gameObject;
                    SceneFlowToolWindow.Open();
                }

                if (GUILayout.Button("바닥에 맞추기", GUILayout.Height(24f)))
                {
                    if (!SceneFlowAuthoring.SnapToGround(entry.transform))
                        Debug.LogWarning("[SceneFlow] 아래에서 바닥 콜라이더를 찾지 못했습니다.", entry);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("보는 방향 뒤집기"))
                    SceneFlowAuthoring.SetEntry(entry, entry.EntryKey, !entry.FaceRight);

                if (GUILayout.Button($"키를 '{SceneFlowAuthoring.DefaultEntryKey}'로"))
                    SceneFlowAuthoring.SetEntryKey(entry, SceneFlowAuthoring.DefaultEntryKey);
            }

            DrawOwnership(entry);
            DrawIncomingPortals(entry);
            DrawIssueCount(entry);
        }

        /// <summary>이 진입점이 로더에게 발견되려면 속한 씬이 그룹의 씬이어야 한다.</summary>
        private static void DrawOwnership(SceneEntryPoint entry)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("소속", EditorStyles.boldLabel);

            Scene scene = entry.gameObject.scene;
            EditorGUILayout.LabelField("씬: " + (string.IsNullOrEmpty(scene.name) ? "(없음)" : scene.name));

            List<SceneGroup> groups = SceneFlowAuthoring.GroupsContainingScene(scene.name);
            if (groups.Count == 0)
            {
                EditorGUILayout.LabelField("속한 SceneGroup 없음", EditorStyles.miniLabel);
                return;
            }

            for (int i = 0; i < groups.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("· " + groups[i].name, EditorStyles.miniLabel);
                    if (GUILayout.Button("그룹 선택", EditorStyles.miniButton, GUILayout.Width(70f)))
                        Selection.activeObject = groups[i];
                }
            }
        }

        /// <summary>열린 씬 안에서 이 키를 부르는 포탈. 닫힌 씬의 포탈은 여기 보이지 않는다.</summary>
        private static void DrawIncomingPortals(SceneEntryPoint entry)
        {
            List<SceneGroup> groups = SceneFlowAuthoring.GroupsContainingScene(entry.gameObject.scene.name);
            if (groups.Count == 0) return;

            Portal[] portals = Object.FindObjectsByType<Portal>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            List<Portal> incoming = new List<Portal>();
            for (int i = 0; i < portals.Length; i++)
            {
                if (portals[i].TargetGroup == null) continue;
                if (!groups.Contains(portals[i].TargetGroup)) continue;
                if (SceneFlowAuthoring.ResolvedEntryKey(portals[i]) != entry.EntryKey) continue;
                incoming.Add(portals[i]);
            }

            EditorGUILayout.LabelField($"이 키를 부르는 포탈 ({incoming.Count}, 열린 씬 기준)",
                                       EditorStyles.miniBoldLabel);

            for (int i = 0; i < incoming.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        $"· {incoming[i].gameObject.name} ({incoming[i].gameObject.scene.name})",
                        EditorStyles.miniLabel);

                    if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(46f)))
                        Selection.activeGameObject = incoming[i].gameObject;
                }
            }
        }

        private static void DrawIssueCount(SceneEntryPoint entry)
        {
            List<SceneFlowValidator.Issue> issues = SceneFlowValidator.ValidateEntry(entry);
            if (issues.Count == 0) return;

            int errors = SceneFlowValidator.CountErrors(issues);
            EditorGUILayout.HelpBox(
                $"검증 항목 {issues.Count}건 (오류 {errors}건). 포탈 툴의 '검증'에서 확인하세요.",
                errors > 0 ? MessageType.Error : MessageType.Warning);
        }

        private void OnSceneGUI()
        {
            SceneEntryPoint entry = (SceneEntryPoint)target;
            SceneFlowGizmos.DrawEntry(entry, selected: true);
            DrawFacingButton(entry);
        }

        /// <summary>화살표를 눌러 바라보는 방향을 그 자리에서 뒤집는다.</summary>
        private static void DrawFacingButton(SceneEntryPoint entry)
        {
            Vector3 pos = entry.transform.position;
            float size = HandleUtility.GetHandleSize(pos);
            Vector3 handlePos = pos + (entry.FaceRight ? Vector3.right : Vector3.left) * size * 0.6f
                                    + Vector3.up * size * 0.3f;

            Handles.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            if (Handles.Button(handlePos, Quaternion.identity, size * 0.1f, size * 0.14f,
                               Handles.SphereHandleCap))
            {
                SceneFlowAuthoring.SetEntry(entry, entry.EntryKey, !entry.FaceRight);
            }
        }

        /// <summary>선택하지 않았을 때도 진입점이 보이도록.</summary>
        [DrawGizmo(GizmoType.NonSelected | GizmoType.Pickable)]
        private static void DrawUnselected(SceneEntryPoint entry, GizmoType type)
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.45f);
            Gizmos.DrawLine(entry.transform.position, entry.transform.position + Vector3.up * 2f);
        }
    }
}
