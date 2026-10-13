using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// LadderTraversable 인스펙터. 기본 필드 위에 자주 쓰는 배치 동작과 검증 요약을 얹고,
    /// 씬 뷰에서 높이·탈출 지점·붙는 위치를 직접 끌어 조정할 수 있게 핸들을 붙인다.
    /// </summary>
    [CustomEditor(typeof(LadderTraversable))]
    [CanEditMultipleObjects]
    public class LadderInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (targets.Length > 1) return;

            LadderTraversable ladder = (LadderTraversable)target;

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("사다리 툴 열기", GUILayout.Height(24f)))
                {
                    Selection.activeGameObject = ladder.gameObject;
                    LadderToolWindow.Open();
                }

                if (GUILayout.Button("계층 자동 구성", GUILayout.Height(24f)))
                    LadderAuthoring.EnsureHierarchy(ladder);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("바닥에 맞추기"))
                {
                    if (!LadderAuthoring.SnapToGround(ladder))
                        Debug.LogWarning("[Ladder] 아래에서 바닥 콜라이더를 찾지 못했습니다.", ladder);
                }

                if (GUILayout.Button("Visual 높이 맞추기"))
                {
                    if (!LadderAuthoring.FitHeightToVisual(ladder))
                        Debug.LogWarning("[Ladder] Visual 자식이나 렌더러를 찾지 못했습니다.", ladder);
                }
            }

            DrawSummary(ladder);
            DrawIssueCount(ladder);
        }

        private static void DrawSummary(LadderTraversable ladder)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("요약", EditorStyles.boldLabel);

            float height = LadderAuthoring.GetFloat(ladder, LadderAuthoring.P_Height);
            float stepDistance = LadderAuthoring.GetFloat(ladder, LadderAuthoring.P_StepDistance);
            float duration = LadderAuthoring.StepDuration(ladder);
            int steps = stepDistance > 0.0001f ? Mathf.CeilToInt(height / stepDistance) : 0;

            EditorGUILayout.LabelField($"높이 {height:0.##}m · 한 칸 {stepDistance:0.##}m · 총 {steps}칸");
            EditorGUILayout.LabelField($"한 칸당 {duration:0.###}초 · 완주 약 {duration * steps:0.##}초");
            EditorGUILayout.LabelField(
                LadderAuthoring.GetBool(ladder, LadderAuthoring.P_FaceRight) ? "바라보는 방향: 오른쪽" : "바라보는 방향: 왼쪽");
        }

        private static void DrawIssueCount(LadderTraversable ladder)
        {
            List<LadderValidator.Issue> issues = LadderValidator.Validate(ladder);
            if (issues.Count == 0) return;

            int errors = 0;
            for (int i = 0; i < issues.Count; i++)
                if (issues[i].Type == MessageType.Error) errors++;

            EditorGUILayout.HelpBox(
                $"검증 항목 {issues.Count}건 (오류 {errors}건). 사다리 툴의 '검증'에서 확인하세요.",
                errors > 0 ? MessageType.Error : MessageType.Warning);
        }

        private void OnSceneGUI()
        {
            LadderTraversable ladder = (LadderTraversable)target;

            LadderGizmos.DrawLadder(ladder, selected: true);
            DrawHeightHandle(ladder);
        }

        /// <summary>사다리 꼭대기를 끌어 Height를 직접 조정한다.</summary>
        private static void DrawHeightHandle(LadderTraversable ladder)
        {
            if (Tools.current != Tool.Move) return;

            Vector3 top = ladder.TopWorld;
            Handles.color = Color.cyan;

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.Slider(top, Vector3.up, HandleUtility.GetHandleSize(top) * 0.16f,
                                           Handles.ArrowHandleCap, 0.05f);

            if (!EditorGUI.EndChangeCheck()) return;

            float scaleY = Mathf.Abs(ladder.transform.lossyScale.y);
            if (scaleY < 0.0001f) return;

            float newHeight = Mathf.Max(0.5f, (moved.y - ladder.BottomWorld.y) / scaleY);
            LadderAuthoring.SetFloat(ladder, LadderAuthoring.P_Height, newHeight, "Change Ladder Height");
        }

        /// <summary>선택하지 않았을 때도 사다리 축이 보이도록.</summary>
        [DrawGizmo(GizmoType.NonSelected | GizmoType.Pickable)]
        private static void DrawUnselected(LadderTraversable ladder, GizmoType type)
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.5f);
            Gizmos.DrawLine(ladder.BottomWorld, ladder.TopWorld);
        }
    }
}
