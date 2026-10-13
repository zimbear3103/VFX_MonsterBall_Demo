using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 씬 뷰에 배틀존 구성을 그린다(트리거 박스·웨이브별 스폰 포인트·선발대).
    /// 툴 윈도우와 커스텀 인스펙터가 같은 그림을 쓰도록 여기 한 곳에 모아 둔다.
    /// </summary>
    public static class BattleZoneGizmos
    {
        /// <summary>웨이브 순서대로 돌려 쓰는 색. 씬 뷰에서 웨이브를 색으로 구분한다.</summary>
        public static readonly Color[] WaveColors =
        {
            new Color(1.00f, 0.45f, 0.20f),
            new Color(0.30f, 0.75f, 1.00f),
            new Color(0.55f, 0.95f, 0.40f),
            new Color(0.95f, 0.40f, 0.85f),
            new Color(1.00f, 0.90f, 0.25f),
        };

        public static Color WaveColor(int index)
        {
            if (index < 0) return Color.gray;
            return WaveColors[index % WaveColors.Length];
        }

        private static GUIStyle _label;

        private static GUIStyle LabelStyle
        {
            get
            {
                if (_label == null)
                {
                    _label = new GUIStyle(EditorStyles.miniBoldLabel);
                    _label.normal.textColor = Color.white;
                    _label.alignment = TextAnchor.MiddleCenter;
                }
                return _label;
            }
        }

        /// <summary>존 전체(트리거 박스 + 모든 웨이브)를 그린다.</summary>
        public static void DrawZone(BattleZone zone, int highlightWave = -1)
        {
            if (zone == null) return;

            DrawTriggerBox(zone);

            List<EnemySpawner> waves = BattleZoneAuthoring.GetWaves(zone);
            for (int i = 0; i < waves.Count; i++)
            {
                if (waves[i] == null) continue;
                bool dim = highlightWave >= 0 && highlightWave != i;
                DrawWave(waves[i], i, dim);
            }
        }

        public static void DrawTriggerBox(BattleZone zone)
        {
            BoxCollider box = zone.GetComponent<BoxCollider>();
            if (box == null) return;

            using (new Handles.DrawingScope(zone.transform.localToWorldMatrix))
            {
                Handles.color = new Color(1f, 0.85f, 0.2f, 0.9f);
                Handles.DrawWireCube(box.center, box.size);
            }
        }

        /// <summary>한 웨이브의 스폰 포인트와 선발대를 그린다.</summary>
        public static void DrawWave(EnemySpawner spawner, int waveIndex, bool dim)
        {
            if (spawner == null) return;

            Color color = WaveColor(waveIndex);
            if (dim) color.a = 0.25f;

            SerializedObject so = new SerializedObject(spawner);
            SerializedProperty entries = so.FindProperty(BattleZoneAuthoring.P_SpawnEntries);

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty e = entries.GetArrayElementAtIndex(i);
                Transform point = e.FindPropertyRelative(BattleZoneAuthoring.P_EntryPoint).objectReferenceValue as Transform;
                if (point == null) continue;

                GameObject prefab = e.FindPropertyRelative(BattleZoneAuthoring.P_EntryPrefab).objectReferenceValue as GameObject;
                float delay = e.FindPropertyRelative(BattleZoneAuthoring.P_EntryDelay).floatValue;

                string name = prefab != null ? prefab.name : "(빈 프리팹)";
                string suffix = delay > 0f ? $"  +{delay:0.##}s" : string.Empty;
                DrawMarker(point.position, color, $"W{waveIndex + 1}  {name}{suffix}", dim);

                Handles.color = color;
                Handles.DrawDottedLine(spawner.transform.position, point.position, 3f);
            }

            SerializedProperty preplaced = so.FindProperty(BattleZoneAuthoring.P_Preplaced);
            for (int i = 0; i < preplaced.arraySize; i++)
            {
                Character c = preplaced.GetArrayElementAtIndex(i).objectReferenceValue as Character;
                if (c == null) continue;

                DrawMarker(c.transform.position, color, $"W{waveIndex + 1}  {c.name} (선발대)", dim);
            }
        }

        private static void DrawMarker(Vector3 position, Color color, string label, bool dim)
        {
            Handles.color = color;
            float size = HandleUtility.GetHandleSize(position) * 0.18f;

            Handles.DrawWireDisc(position, Vector3.up, size * 2f);
            Handles.DrawLine(position, position + Vector3.up * size * 6f);
            Handles.SphereHandleCap(0, position + Vector3.up * size * 6f, Quaternion.identity, size, EventType.Repaint);

            if (dim) return;

            GUIStyle style = LabelStyle;
            Color prev = style.normal.textColor;
            style.normal.textColor = color;
            Handles.Label(position + Vector3.up * size * 8f, label, style);
            style.normal.textColor = prev;
        }
    }
}
