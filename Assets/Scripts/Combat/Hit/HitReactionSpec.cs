using System;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 피격 반응의 베이스 종류. 하나만 선택하며, Ground Bounce 등 모디파이어와 자유 조합된다.
    /// 우선순위 하드코딩 없이 Kind가 곧 반응이다.
    /// </summary>
    public enum HitReactionKind
    {
        /// <summary>일반 경직(Hit). 힘 값은 사용하지 않는다.</summary>
        None = 0,
        /// <summary>수평으로 밀려남(서 있음, 구 Knockback).</summary>
        Push = 1,
        /// <summary>밀리며 쓰러짐(구 Knockdown).</summary>
        Knockdown = 2,
        /// <summary>공중으로 띄움(구 Airborne 시동기).</summary>
        Launch = 3,
        /// <summary>아래로 내려찍음(구 Spike). 체공 대상은 급강하, 지상 대상은 넉다운(바운스 시 즉시 튕김).</summary>
        Slam = 4,
    }

    /// <summary>
    /// 피격 반응 데이터 한 벌 — 베이스 Kind + 공용 힘 + 직교 모디파이어(Ground Bounce).
    /// AttackAction(인스펙터) → AttackHitbox.ConfigureReaction → DamageInfo.Reaction →
    /// CharacterStateManager.ReactionData.Spec → 반응 상태(Knockback/Knockdown/Airborne)까지
    /// 이 구조체 하나로 전달된다. 새 조합이 생겨도 필드/분기를 반응별로 늘리지 않는다.
    ///
    /// 조합 규칙:
    ///   Launch + Bounce  = 띄운 뒤 착지에서 튕김
    ///   Slam + Bounce    = 내리꽂고 바닥에서 튕김 (지상 대상도 즉시)
    ///   Push/Knockdown + Bounce = 적중 즉시 튀며 수평으로 밀림(돌수제비) — 수평 관성은 바운스마다 감쇠
    ///   Bounce 없음      = 기존 반응 그대로
    /// </summary>
    [Serializable]
    public struct HitReactionSpec
    {
        [Tooltip("베이스 반응. None=일반 경직 / Push=밀려남 / Knockdown=밀리며 쓰러짐 / Launch=띄우기 / Slam=내려찍기")]
        public HitReactionKind Kind;

        [Tooltip("HitDirection 방향 수평 속도(m/s). 모든 Kind의 수평 성분으로 공용 사용 (Push 밀림, Launch/Slam 수평 관성, 바운스 수평 유지)")]
        public float HorizontalForce;
        [Tooltip("지상 밀림 지속 시간(초). Push/Knockdown의 Slide 구간에 사용")]
        public float PushDuration;
        [Tooltip("수직 속도(m/s). Launch=상승 / Slam=하강 / Push·Knockdown+Bounce=적중 시 살짝 띄우는 런치 높이(0이면 BouncePower 사용). None은 미사용")]
        public float VerticalForce;
        [Tooltip("Launch 총 체공 시간(초). 0보다 크면 체공 전용 중력을 역산해 같은 힘으로도 이 시간만큼 떠 있는다. 0 = 기본 중력")]
        public float AirTime;

        [Header("Ground Bounce")]
        [Tooltip("바닥 충돌 시 다시 튀어오르는 그라운드 바운스. 어떤 Kind와도 조합 가능. 남은 Power가 클수록 높게 튀고 매 충돌마다 Decay만큼 감소, 0 이하가 되면 기존 착지 흐름으로 복귀")]
        public bool EnableBounce;
        [Tooltip("바운스 시작 세기 = 첫 바운스의 수직 속도(m/s)")]
        public float BouncePower;
        [Tooltip("바닥 충돌 1회당 Power 감소량. 0 이하 입력 시 안전 최소값 적용")]
        public float BounceDecay;

        /// <summary>바운스가 실제로 유효한가.</summary>
        public bool WantsBounce => EnableBounce && BouncePower > 0f;

        /// <summary>인스펙터 기본값 — 새 AttackAction에 채워지는 값.</summary>
        public static HitReactionSpec Default => new HitReactionSpec
        {
            Kind = HitReactionKind.None,
            HorizontalForce = 0f,
            PushDuration = 0.25f,
            VerticalForce = 8f,
            AirTime = 0.5f,
            EnableBounce = false,
            BouncePower = 8f,
            BounceDecay = 3f,
        };
    }
}
