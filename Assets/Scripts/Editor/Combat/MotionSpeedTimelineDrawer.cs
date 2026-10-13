using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// MotionSpeedTimeline 인스펙터 — 구간 리스트(Start/End/Speed/Blend) 아래에 배속 그래프를 읽기 전용으로 그려
    /// 애니 클립 편집기처럼 "어느 구간이 얼마나 빨라지는지"를 한눈에 확인한다 (2026-09-15).
    /// 가로 = 기준 시간(초), 세로 = 배속 배율. 그래프는 미리보기 전용(편집은 리스트에서).
    /// </summary>
    [CustomPropertyDrawer(typeof(MotionSpeedTimeline))]
    public class MotionSpeedTimelineDrawer : PropertyDrawer
    {
        private const float PreviewHeight = 64f;
        private const float Spacing = 4f;
        private const int Samples = 128;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var segments = property.FindPropertyRelative("segments");
            float h = EditorGUI.GetPropertyHeight(segments, label, true);
            if (segments.isExpanded && HasValidSegment(segments))
                h += Spacing + EditorGUIUtility.singleLineHeight + PreviewHeight;
            return h;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var segments = property.FindPropertyRelative("segments");
            float listH = EditorGUI.GetPropertyHeight(segments, label, true);
            var listRect = new Rect(position.x, position.y, position.width, listH);
            EditorGUI.PropertyField(listRect, segments, label, true);

            if (!segments.isExpanded || !HasValidSegment(segments)) return;

            var timeline = Build(segments);
            float maxT = Mathf.Max(0.1f, timeline.MaxTime * 1.1f);
            float minS = 1f, maxS = 1f;
            var curve = new AnimationCurve();
            for (int i = 0; i <= Samples; i++)
            {
                float t = maxT * i / Samples;
                float s = timeline.Evaluate(t);
                minS = Mathf.Min(minS, s);
                maxS = Mathf.Max(maxS, s);
                curve.AddKey(new Keyframe(t, s, 0f, 0f));
            }
            for (int i = 0; i < curve.length; i++)
                AnimationUtility.SetKeyBroken(curve, i, false);

            float y = position.y + listH + Spacing;
            var labelRect = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(labelRect,
                $"배속 미리보기  (가로 0~{maxT:F2}s / 세로 {minS:F2}~{maxS:F2}x, 구간 밖 = 1x)",
                EditorStyles.miniLabel);

            var previewRect = new Rect(position.x, y + EditorGUIUtility.singleLineHeight, position.width, PreviewHeight);
            float pad = Mathf.Max(0.1f, (maxS - minS) * 0.15f);
            var ranges = new Rect(0f, minS - pad, maxT, (maxS - minS) + pad * 2f);
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.CurveField(previewRect, curve, new Color(0.4f, 0.9f, 1f), ranges);
        }

        private static bool HasValidSegment(SerializedProperty segments)
        {
            for (int i = 0; i < segments.arraySize; i++)
            {
                var e = segments.GetArrayElementAtIndex(i);
                float start = e.FindPropertyRelative("start").floatValue;
                float end = e.FindPropertyRelative("end").floatValue;
                float speed = e.FindPropertyRelative("speed").floatValue;
                if (end > start && speed > 0f) return true;
            }
            return false;
        }

        private static MotionSpeedTimeline Build(SerializedProperty segments)
        {
            var tl = new MotionSpeedTimeline();
            for (int i = 0; i < segments.arraySize; i++)
            {
                var e = segments.GetArrayElementAtIndex(i);
                tl.segments.Add(new MotionSpeedSegment
                {
                    start = e.FindPropertyRelative("start").floatValue,
                    end = e.FindPropertyRelative("end").floatValue,
                    speed = e.FindPropertyRelative("speed").floatValue,
                    blend = e.FindPropertyRelative("blend").floatValue,
                });
            }
            return tl;
        }
    }
}
