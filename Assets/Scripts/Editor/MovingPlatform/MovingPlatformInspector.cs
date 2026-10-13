using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// MovingPlatform 인스펙터. 기본 필드 위에 자주 쓰는 배치 동작과 검증 요약을 얹고,
    /// 씬 뷰에서 도착 지점을 직접 끌어 옮길 수 있게 핸들을 붙인다.
    /// </summary>
    [CustomEditor(typeof(MovingPlatform))]
    [CanEditMultipleObjects]
    public class MovingPlatformInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (targets.Length > 1) return;

            MovingPlatform platform = (MovingPlatform)target;

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("엘리베이터 툴 열기", GUILayout.Height(24f)))
                {
                    Selection.activeGameObject = platform.gameObject;
                    MovingPlatformToolWindow.Open();
                }

                if (GUILayout.Button("계층 자동 구성", GUILayout.Height(24f)))
                    MovingPlatformAuthoring.EnsureHierarchy(platform);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("바닥에 맞추기"))
                {
                    if (!MovingPlatformAuthoring.SnapToGround(platform))
                        Debug.LogWarning("[MovingPlatform] 아래에서 바닥 콜라이더를 찾지 못했습니다.", platform);
                }

                if (GUILayout.Button("탑승 존 맞추기"))
                {
                    if (!MovingPlatformAuthoring.FitRiderZone(platform))
                        Debug.LogWarning("[MovingPlatform] 탑승 판정 존이나 발판 콜라이더를 찾지 못했습니다.", platform);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("수직으로 정렬")) MovingPlatformAuthoring.AlignVertical(platform);
                if (GUILayout.Button("수평으로 정렬")) MovingPlatformAuthoring.AlignHorizontal(platform);
                if (GUILayout.Button("시작·끝 뒤집기")) MovingPlatformAuthoring.SwapEnds(platform);
            }

            DrawTravelHeight(platform);
            DrawSummary(platform);
            DrawIssueCount(platform);
        }

        /// <summary>엘리베이터에서 제일 자주 만지는 값. 도착 지점의 Y만 떼어 편집한다.</summary>
        private static void DrawTravelHeight(MovingPlatform platform)
        {
            Vector3 delta = MovingPlatformAuthoring.EndWorld(platform) - MovingPlatformAuthoring.StartWorld(platform);

            EditorGUI.BeginChangeCheck();
            float height = EditorGUILayout.FloatField("이동 높이 (m)", delta.y);
            if (EditorGUI.EndChangeCheck())
                MovingPlatformAuthoring.SetTravelHeight(platform, height);
        }

        private static void DrawSummary(MovingPlatform platform)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("요약", EditorStyles.boldLabel);

            float distance = MovingPlatformAuthoring.TravelDistance(platform);
            float oneWay = MovingPlatformAuthoring.OneWayTime(platform);
            float cycle = MovingPlatformAuthoring.CycleTime(platform);
            MovingPlatform.Mode mode = MovingPlatformAuthoring.GetMode(platform);

            EditorGUILayout.LabelField(
                $"{MovingPlatformAuthoring.ModeLabel(mode)} · 거리 {distance:0.##}m · 편도 {oneWay:0.##}초");

            EditorGUILayout.LabelField(mode == MovingPlatform.Mode.OneWayOnTouch
                ? "밟은 뒤 도착하면 정지 (Activate/ResetToStart로만 재사용)"
                : $"한 주기 약 {cycle:0.##}초 (대기 포함)");

            if (MovingPlatformAuthoring.GetBool(platform, MovingPlatformAuthoring.P_Ease))
                EditorGUILayout.LabelField("양 끝 감속 켜짐 — 실제 시간은 표시값보다 조금 깁니다.");
        }

        private static void DrawIssueCount(MovingPlatform platform)
        {
            List<MovingPlatformValidator.Issue> issues = MovingPlatformValidator.Validate(platform);
            if (issues.Count == 0) return;

            int errors = 0;
            for (int i = 0; i < issues.Count; i++)
                if (issues[i].Type == MessageType.Error) errors++;

            EditorGUILayout.HelpBox(
                $"검증 항목 {issues.Count}건 (오류 {errors}건). 엘리베이터 툴의 '검증'에서 확인하세요.",
                errors > 0 ? MessageType.Error : MessageType.Warning);
        }

        private void OnSceneGUI()
        {
            MovingPlatform platform = (MovingPlatform)target;

            MovingPlatformGizmos.DrawPlatform(platform, selected: true);
            DrawEndPointHandle(platform);
        }

        /// <summary>도착 지점을 따로 선택하지 않고도 바로 끌어 옮긴다.</summary>
        private static void DrawEndPointHandle(MovingPlatform platform)
        {
            if (Tools.current != Tool.Move) return;

            Transform end = MovingPlatformAuthoring.EndPointOf(platform);
            if (end == null) return;

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(end.position, Quaternion.identity);
            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(end, "Move End Point");
            end.position = moved;
        }

        /// <summary>선택하지 않았을 때도 운행 경로가 보이도록.</summary>
        [DrawGizmo(GizmoType.NonSelected | GizmoType.Pickable)]
        private static void DrawUnselected(MovingPlatform platform, GizmoType type)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.5f);
            Gizmos.DrawLine(MovingPlatformAuthoring.StartWorld(platform),
                            MovingPlatformAuthoring.EndWorld(platform));
        }
    }
}
