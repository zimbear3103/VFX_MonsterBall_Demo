using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// SpringArmCameraNode에 주입되는 전투 프레이밍 거리 채널.
    /// CombatCameraController가 매 프레임 계산한 희망 거리(구버전 CameraMultiTargetManager.ComputeDesiredDistance +
    /// CameraDistanceController clamp 등가)를 SpringArm의 Zone/Base 거리 위에 덮어쓴다.
    /// 보간은 SpringArm의 distanceSmoothTime이 담당한다(구버전 GlobalDistanceTransitionDuration 0.4 대응).
    ///
    /// 우선순위는 낮게(먼저 샘플) 등록해 이후의 줌 채널이 이 값 위에 델타를 얹을 수 있게 한다.
    /// </summary>
    public class CombatFramingDistanceChannel : ICameraChannel
    {
        /// <summary>true면 이번 프레임 프레이밍 거리를 적용한다(적이 한 명도 없으면 false → 기본 거리 통과).</summary>
        public bool Active;

        /// <summary>프레이밍에 필요한 원시 거리(m). Clamp 전.</summary>
        public float DesiredDistance;

        /// <summary>하한(m). 0 이하면 SpringArm의 Zone/Base 거리를 하한으로 쓴다.</summary>
        public float MinDistance;

        /// <summary>상한(m).</summary>
        public float MaxDistance = 12f;

        /// <summary>
        /// Main Target 근접 줌인 비율 0~1. 1이면 하한(floor)이 floor×ZoomScale까지 내려간다.
        /// 프레이밍에 필요한 DesiredDistance가 더 크면(다른 적이 넓게 퍼짐) 줌은 자연히 무시된다.
        /// </summary>
        public float ZoomBlend;

        /// <summary>완전 줌인 시 하한 배율(0~1).</summary>
        public float ZoomScale = 1f;

        /// <summary>마지막으로 적용한 거리(디버그 표시용).</summary>
        public float LastAppliedDistance { get; private set; }

        private bool _finished;
        public bool IsFinished => _finished;

        /// <summary>컨트롤러 비활성 시 채널을 노드에서 자동 제거되게 한다.</summary>
        public void Finish() => _finished = true;

        public void Sample(float dt, ref CameraNodeState state)
        {
            if (_finished || !Active)
            {
                LastAppliedDistance = state.Distance;
                return;
            }

            float floor = MinDistance > 0f ? MinDistance : state.Distance;
            if (ZoomBlend > 0f)
                floor = Mathf.Lerp(floor, floor * Mathf.Clamp01(ZoomScale), Mathf.Clamp01(ZoomBlend));
            float ceil = Mathf.Max(floor, MaxDistance);
            float d = Mathf.Clamp(DesiredDistance, floor, ceil);
            state.Distance = d;
            LastAppliedDistance = d;
        }
    }

    /// <summary>
    /// FollowCameraNode에 주입되는 전투 구도 보정 채널.
    ///  - CombatOffset: 전투 참여 적이 있을 때 추적 중심에 더할 오프셋(Blend 0~1로 페이드).
    ///  - Chase Clamp: 포커스(구버전 AirFocus) 중 추적 중심이 플레이어에서 MaxChaseDistance 이상 벗어나지 않게 제한
    ///    (구버전 MaxAirFocusChaseDistance 등가, 수평 성분만).
    /// </summary>
    public class CombatFollowOffsetChannel : ICameraChannel
    {
        /// <summary>전투 오프셋(m). FollowNode의 followOffset 위에 더해진다.</summary>
        public Vector3 CombatOffset;

        /// <summary>오프셋 적용 비율 0~1(적 Weight 합에서 유도 → 자연 페이드).</summary>
        public float Blend;

        /// <summary>추적 중심-플레이어 최대 수평 거리(m). 0 이하면 제한 없음.</summary>
        public float MaxChaseDistance;

        /// <summary>플레이어 기준 위치(Chase Clamp 기준점). null이면 Clamp 생략.</summary>
        public Transform Anchor;

        private bool _finished;
        public bool IsFinished => _finished;
        public void Finish() => _finished = true;

        public void Sample(float dt, ref CameraNodeState state)
        {
            if (_finished) return;

            if (Blend > 0f)
                state.PositionOffset += CombatOffset * Mathf.Clamp01(Blend);

            if (MaxChaseDistance > 0f && Anchor != null)
            {
                Vector3 center = state.Position + state.PositionOffset;
                Vector3 d = center - Anchor.position;
                d.y = 0f;
                float len = d.magnitude;
                if (len > MaxChaseDistance)
                {
                    Vector3 clampedD = d * (MaxChaseDistance / len);
                    state.PositionOffset += clampedD - d;
                }
            }
        }
    }
}
