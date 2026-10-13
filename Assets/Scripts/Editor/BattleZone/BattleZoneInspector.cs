using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// BattleZone 인스펙터. 기본 필드 위에 웨이브 요약·툴 열기·벽 맞춤 바로가기를 얹고,
    /// 씬 뷰에 트리거 박스와 웨이브별 스폰 포인트를 그린다.
    /// </summary>
    [CustomEditor(typeof(BattleZone))]
    [CanEditMultipleObjects]
    public class BattleZoneInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (targets.Length > 1) return;

            BattleZone zone = (BattleZone)target;

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("배틀존 툴 열기", GUILayout.Height(24f)))
                {
                    Selection.activeGameObject = zone.gameObject;
                    BattleZoneToolWindow.Open();
                }

                if (GUILayout.Button("벽 맞춤 (Z축)", GUILayout.Height(24f)))
                    BattleZoneAuthoring.FitWalls(zone, BattleZoneAuthoring.BlockAxis.Z, 2f, 4f);
            }

            DrawSummary(zone);
            DrawIssueCount(zone);
        }

        private static void DrawSummary(BattleZone zone)
        {
            List<EnemySpawner> waves = BattleZoneAuthoring.GetWaves(zone);
            if (waves.Count == 0) return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("웨이브 요약", EditorStyles.boldLabel);

            for (int i = 0; i < waves.Count; i++)
            {
                EnemySpawner wave = waves[i];

                using (new EditorGUILayout.HorizontalScope())
                {
                    Color prev = GUI.color;
                    GUI.color = BattleZoneGizmos.WaveColor(i);
                    GUILayout.Label("■", GUILayout.Width(14f));
                    GUI.color = prev;

                    if (wave == null)
                    {
                        EditorGUILayout.LabelField($"Wave {i + 1}: 비어 있음");
                        continue;
                    }

                    int entries = BattleZoneAuthoring.GetEntryCount(wave);
                    int preplaced = BattleZoneAuthoring.GetPreplacedCount(wave);
                    EditorGUILayout.LabelField($"Wave {i + 1}: 스폰 {entries} · 선발대 {preplaced}");

                    if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(46f)))
                        Selection.activeGameObject = wave.gameObject;
                }
            }
        }

        private static void DrawIssueCount(BattleZone zone)
        {
            List<BattleZoneValidator.Issue> issues = BattleZoneValidator.Validate(zone);
            if (issues.Count == 0) return;

            int errors = 0;
            for (int i = 0; i < issues.Count; i++)
                if (issues[i].Type == MessageType.Error) errors++;

            EditorGUILayout.HelpBox(
                $"검증 항목 {issues.Count}건 (오류 {errors}건). 배틀존 툴의 '검증'에서 확인하세요.",
                errors > 0 ? MessageType.Error : MessageType.Warning);
        }

        private void OnSceneGUI()
        {
            BattleZone zone = (BattleZone)target;
            BattleZoneGizmos.DrawZone(zone);
            DrawSpawnPointHandles(zone);
        }

        /// <summary>존을 고른 채로 각 스폰 포인트를 바로 끌어 옮길 수 있게 위치 핸들을 붙인다.</summary>
        private static void DrawSpawnPointHandles(BattleZone zone)
        {
            if (Tools.current != Tool.Move) return;

            List<EnemySpawner> waves = BattleZoneAuthoring.GetWaves(zone);

            for (int w = 0; w < waves.Count; w++)
            {
                if (waves[w] == null) continue;

                SerializedObject so = new SerializedObject(waves[w]);
                SerializedProperty entries = so.FindProperty(BattleZoneAuthoring.P_SpawnEntries);

                for (int i = 0; i < entries.arraySize; i++)
                {
                    Transform point = entries.GetArrayElementAtIndex(i)
                        .FindPropertyRelative(BattleZoneAuthoring.P_EntryPoint).objectReferenceValue as Transform;
                    if (point == null) continue;

                    Handles.color = BattleZoneGizmos.WaveColor(w);

                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.FreeMoveHandle(
                        point.position,
                        HandleUtility.GetHandleSize(point.position) * 0.12f,
                        Vector3.zero,
                        Handles.RectangleHandleCap);

                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(point, "Move Spawn Point");
                        point.position = moved;
                    }
                }
            }
        }

        /// <summary>존을 고르지 않았을 때도 트리거 박스가 보이도록.</summary>
        [DrawGizmo(GizmoType.NonSelected | GizmoType.Pickable)]
        private static void DrawUnselected(BattleZone zone, GizmoType type)
        {
            BoxCollider box = zone.GetComponent<BoxCollider>();
            if (box == null) return;

            Gizmos.matrix = zone.transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.35f);
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
