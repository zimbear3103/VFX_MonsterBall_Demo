using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 모션 구간 배속 항목 — "이 시간 구간만 몇 배로" (2026-09-15).
    /// 시간 단위는 해당 타임라인의 기준 시간(AttackAction = 배율 1 기준 모션 초, GrabAttackLink = 잡기 타임라인 초).
    /// 애니 클립 편집기에서 특정 구간을 늘리거나 줄이듯이 인스펙터에서 바로 조정한다.
    /// </summary>
    [System.Serializable]
    public class MotionSpeedSegment
    {
        [Tooltip("구간 시작 시각(초). 기준 시간 = 이펙트 startDelay와 같은 자(배율 1 기준)")]
        [Min(0f)] public float start;
        [Tooltip("구간 끝 시각(초). start 이하면 무시")]
        [Min(0f)] public float end = 0.2f;
        [Tooltip("이 구간의 배속. 2 = 2배 빠르게, 0.5 = 절반 속도. 모션·이펙트·히트박스·전진이 전부 같은 비율로 따라간다")]
        [Min(0.01f)] public float speed = 2f;
        [Tooltip("구간 앞뒤에서 1 → speed 로 부드럽게 넘어가는 시간(초, 구간 안쪽). 0 = 즉시 스냅")]
        [Min(0f)] public float blend;

        public bool IsValid => end > start && speed > 0f;
    }

    /// <summary>
    /// 구간 배속 타임라인 — MotionSpeedSegment 리스트를 평가해 특정 시각의 배속 배율을 돌려준다.
    /// 어떤 구간에도 속하지 않으면 1. 겹치면 리스트 뒤쪽 항목이 우선.
    /// AttackAction(공격)과 GrabAttackLink(잡기)가 공용으로 사용한다.
    /// </summary>
    [System.Serializable]
    public class MotionSpeedTimeline
    {
        [Tooltip("구간 배속 목록. 비어 있으면 배속 변화 없음(1). 겹치는 구간은 아래(뒤쪽) 항목이 우선")]
        public List<MotionSpeedSegment> segments = new List<MotionSpeedSegment>();

        /// <summary>구간이 하나라도 유효한가.</summary>
        public bool HasSegments
        {
            get
            {
                if (segments == null) return false;
                for (int i = 0; i < segments.Count; i++)
                    if (segments[i] != null && segments[i].IsValid) return true;
                return false;
            }
        }

        /// <summary>time(기준 초)에서의 배속 배율. 구간 밖 = 1. blend 구간은 SmoothStep으로 1↔speed 보간.</summary>
        public float Evaluate(float time)
        {
            if (segments == null) return 1f;
            float result = 1f;
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                if (s == null || !s.IsValid) continue;
                if (time < s.start || time >= s.end) continue;

                float w = 1f;
                if (s.blend > 0f)
                {
                    float half = Mathf.Min(s.blend, (s.end - s.start) * 0.5f);
                    float inW = half > 0f ? Mathf.Clamp01((time - s.start) / half) : 1f;
                    float outW = half > 0f ? Mathf.Clamp01((s.end - time) / half) : 1f;
                    w = Mathf.SmoothStep(0f, 1f, Mathf.Min(inW, outW));
                }
                result = Mathf.Lerp(1f, s.speed, w);
            }
            return result;
        }

        /// <summary>구간이 끝나는 가장 늦은 시각(초). 미리보기 범위 계산용.</summary>
        public float MaxTime
        {
            get
            {
                float t = 0f;
                if (segments == null) return t;
                for (int i = 0; i < segments.Count; i++)
                    if (segments[i] != null && segments[i].IsValid) t = Mathf.Max(t, segments[i].end);
                return t;
            }
        }
    }
}
