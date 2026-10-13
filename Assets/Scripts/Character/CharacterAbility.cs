using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 캐릭터 기본 능력치 프로필 (ScriptableObject).
    ///
    /// 여기서 "Ability"는 스킬 하나가 아니라 <b>해당 캐릭터 자체의 능력치 프로필</b>이다.
    /// Player·Enemy·Boss 등 모든 Character가 공용으로 사용하는 기본 수치 데이터를 담는다.
    ///
    /// 규칙:
    ///   - 이 Asset은 <b>Base Data(원본)</b>이며 런타임에 수정하지 않는다. (기획 22)
    ///   - 버프/성장/피격 등 변하는 값은 CharacterRuntimeStats(캐릭터 인스턴스별)에서 관리한다. (기획 23~24)
    ///   - 세부 기술 수치(공격별 DamageMultiplier·Hitbox·Dash Duration 등)는 여기 넣지 않는다. (기획 21)
    ///     그런 값은 AttackEffect / Attack Data / Movement 설정 등 각 책임처가 소유한다.
    ///
    /// 같은 Asset을 여러 캐릭터가 참조해도 서로 영향을 주지 않는다 —
    /// 런타임 상태는 CharacterRuntimeStats 인스턴스에 분리 저장되기 때문이다. (기획 51)
    /// </summary>
    [CreateAssetMenu(fileName = "New_Ability", menuName = "Yeolha/Character Ability", order = 0)]
    public class CharacterAbility : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("표시용 이름 (선택). 비워두면 Asset 이름을 사용한다.")]
        [SerializeField] private string displayName = "";

        [Header("Health")]
        [Tooltip("최대 체력")]
        [SerializeField] private float maxHP = 100f;
        [Tooltip("방어력 (데미지 감산 — 다음 단계 Damage Calculator에서 사용)")]
        [SerializeField] private float defense = 5f;
        [Tooltip("생명(잔기) 수. 모든 캐릭터에 필요하지 않을 수 있으나 공용 최소값으로 둔다.")]
        [SerializeField, Min(1)] private int lifeCount = 1;

        [Header("Combat")]
        [Tooltip("기본 공격력. 최종 데미지 = AttackPower × (공격별 DamageMultiplier)")]
        [SerializeField] private float attackPower = 10f;
        [Tooltip("치명타 확률 0~1 (다음 단계 Damage Calculator에서 Roll)")]
        [SerializeField, Range(0f, 1f)] private float criticalRate = 0f;

        [Header("Resource")]
        [Tooltip("최대 FP (기력 등 캐릭터 공용 자원)")]
        [SerializeField] private float maxFP = 100f;
        [Tooltip("최대 투혼 자원")]
        [SerializeField] private float maxTouhon = 0f;

        [Header("Movement")]
        [Tooltip("기본 이동 속도 = 걷기 속도 (m/s). 구 yeolhadiary WalkSpeed=3.")]
        [SerializeField] private float moveSpeed = 3f;
        [Tooltip("달리기 배율. 달리기 속도 = MoveSpeed × RunMultiplier. (3 × 2 = 6)")]
        [SerializeField] private float runMultiplier = 2f;
        [Tooltip("점프 최고 높이 (m)")]
        [SerializeField] private float jumpHeight = 1.6f;
        [Tooltip("지면을 떠난 뒤 사용 가능한 최대 점프 횟수 (1=기본, 2=2단 점프)")]
        [SerializeField, Min(1)] private int maxJumpCount = 1;
        [Tooltip("대시 속도 (m/s)")]
        [SerializeField] private float dashSpeed = 18f;

        [Header("Reaction")]
        [Tooltip("피격 경직 시간 (초). Hit 상태 유지 시간.")]
        [SerializeField, Range(0f, 1.5f)] private float hitDuration = 0.4f;
        [Tooltip("착지 경직 시간 (초). Land 상태 유지 시간.")]
        [SerializeField, Range(0f, 1f)] private float landDuration = 0.12f;

        [Header("Animation")]
        [Tooltip("Animator 전역 재생 속도 (animator.speed)")]
        [SerializeField, Min(0.01f)] private float globalAnimatorSpeed = 0.7f;
        [Tooltip("걷기 애니메이션 속도 배율 (Animator WalkSpeedMultiplier 파라미터)")]
        [SerializeField, Min(0.01f)] private float walkAnimationSpeed = 2f;

        [Header("Guard / Parry (Defense)")]
        [Tooltip("자동 가드 확률 0~1. 가드 자세가 아닐 때 들어온 공격을 이 확률로 막는다(적 패시브 가드). 0 = 사용 안 함")]
        [SerializeField, Range(0f, 1f)] private float guardChance = 0f;
        [Tooltip("패링 확률 0~1. 들어온 공격을 이 확률로 패링(무효 + 공격자 경직)한다. 0 = 사용 안 함. 가드 불가(Unblockable) 공격은 확률 패링 대상이 아니다")]
        [SerializeField, Range(0f, 1f)] private float parryChance = 0f;
        [Tooltip("연속으로 이 횟수만큼 '실제로 맞으면' 일정 시간 100% 가드로 전환한다. 0 = 사용 안 함 (예: 3)")]
        [SerializeField, Min(0)] private int forceGuardHitCount = 0;
        [Tooltip("100% 강제 가드 유지 시간(초). 이 시간 안에 또 맞으면 시간이 초기화(연장)된다")]
        [SerializeField, Min(0f)] private float forceGuardDuration = 1f;
        [Tooltip("연속 피격 판정 리셋 간격(초). 마지막 피격 후 이 시간이 지나면 연속 카운트가 0으로 초기화된다")]
        [SerializeField, Min(0f)] private float forceGuardComboWindow = 2f;

        // ─────────── 읽기 전용 접근 (원본 Base Data) ───────────

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        public float MaxHP => maxHP;
        public float Defense => defense;
        public int LifeCount => lifeCount;

        public float AttackPower => attackPower;
        public float CriticalRate => criticalRate;

        public float MaxFP => maxFP;
        public float MaxTouhon => maxTouhon;

        public float MoveSpeed => moveSpeed;
        public float RunMultiplier => runMultiplier;
        public float JumpHeight => jumpHeight;
        public int MaxJumpCount => Mathf.Max(1, maxJumpCount);
        public float DashSpeed => dashSpeed;

        public float HitDuration => hitDuration;
        public float LandDuration => landDuration;

        public float GlobalAnimatorSpeed => globalAnimatorSpeed;
        public float WalkAnimationSpeed => walkAnimationSpeed;

        // 가드/패링 (CharacterDefense가 참조하는 튜닝 값)
        public float GuardChance => guardChance;
        public float ParryChance => parryChance;
        public int ForceGuardHitCount => forceGuardHitCount;
        public float ForceGuardDuration => forceGuardDuration;
        public float ForceGuardComboWindow => forceGuardComboWindow;
    }
}
