using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 캐릭터 방어 공통 구조 — 가드/패링 판정을 소유하는 일반 클래스(Character가 소유하는 5번째 시스템).
    ///
    /// 설계 원칙(구버전 GuardableHealth 이식):
    ///   - 별도의 방어 전용 상태머신을 만들지 않는다. 방어 판정 진입점은 오직
    ///     Character.ReceiveDamage → CharacterCombat.ReceiveDamage → CharacterDefense.Resolve 하나다.
    ///   - Player/Enemy 공용. 실제 가드 "자세/잠금"은 CharacterStateType.Guard 상태가 담당하고,
    ///     여기서는 "들어온 데미지를 어떻게 처리할지"(감소/무효/브레이크)만 결정한다.
    ///   - 데미지 저장소(HP)는 Character 런타임. 여기서는 DamageInfo.Damage를 감소시키기만 하고
    ///     실제 HP 적용은 CharacterCombat이 한다.
    ///
    /// Phase 2에서 패링(가드 성공의 특수 케이스)이 이 클래스에 추가된다.
    /// </summary>
    [System.Serializable]
    public class CharacterDefense
    {
        [Header("Guard")]
        [Tooltip("가드 사용 가능 여부. 끄면 이 캐릭터는 가드하지 않는다")]
        [SerializeField] private bool canGuard = true;
        [Tooltip("가드 성공 시 받는 데미지 배율 (구버전 플레이어 고정 0.2 = 80% 감소)")]
        [SerializeField, Range(0f, 1f)] private float guardDamageMultiplier = 0.2f;
        [Tooltip("모든 방향 가드 허용 (구버전 플레이어 GuardAllDirections=1). 끄면 정면 공격만 방어")]
        [SerializeField] private bool guardAllDirections = false;
        [Tooltip("정면 판정 임계값 — 바라보는 방향과 (공격자→나) 방향의 dot이 이 값 이하면 정면. 0 근처 권장")]
        [SerializeField, Range(-1f, 1f)] private float guardFrontDot = -0.1f;
        [Tooltip("가드 자세 중 이동 허용 여부 (구버전 AllowMovementDuringGuard). 기본은 제자리 방어")]
        [SerializeField] private bool allowMovementWhileGuarding = false;
        [Tooltip("가드 자세 중 이동 속도 배율 (allowMovementWhileGuarding=true일 때만). 걷기 배율에 곱하지 않고 자체 배율로 사용")]
        [SerializeField, Range(0f, 1f)] private float guardMoveSpeedMultiplier = 0.5f;
        [Tooltip("가드 자세(스탠스) 애니메이션을 재생할지. false = 구버전 플레이어처럼 자세 없이 가드피격 반응만(GuardDamaged)")]
        [SerializeField] private bool playGuardStanceAnimation = false;

        [Header("Guard Gauge (Touhon)")]
        [Tooltip("가드 1회 방어 시 소모할 투혼(=구버전 BattleSpirit GuardBlock 4). 0 = 게이지 미사용(무한 가드). 투혼 시스템(MaxTouhon>0)일 때만 의미")]
        [SerializeField] private float guardTouhonDrainPerBlock = 0f;
        [Tooltip("가드 게이지(투혼) 소진 시 가드 브레이크(풀 데미지+경직) 발생 여부")]
        [SerializeField] private bool guardBreakOnGaugeEmpty = true;
        [Tooltip("가드 중 강제로 밀릴 때(가드 브레이크/가드 불가) 넉백·수평 밀림 감소율(0~1). 1=완전 저항")]
        [SerializeField, Range(0f, 1f)] private float guardKnockbackResistance = 0.5f;
        [Tooltip("가드 성공으로 밀릴 때 슬라이드+이동잠금 지속 시간(초). 이 시간 동안 밀리며 이동이 잠긴다. 0 = 가드 밀림 없음(기존 동작). 밀림 세기는 위 GuardKnockbackResistance로 감소")]
        [SerializeField, Range(0f, 1f)] private float guardKnockbackDuration = 0.2f;
        [Tooltip("가드 성공 시 최소 밀림 세기(m/s). 공격의 HorizontalForce가 0이어도 이 값만큼은 무조건 밀린다(공격 힘과 둘 중 큰 값 사용). 0이면 공격 HorizontalForce에만 의존. '가드하면 항상 일정하게 밀리게' 하려면 이 값을 올린다")]
        [SerializeField, Range(0f, 20f)] private float guardKnockbackForce = 4f;
        [Tooltip("가드 피격(GuardDamaged)/패링(Parrying) 반응 모션이 재생되는 동안 이동잠금을 모션 길이만큼 유지. 위 Duration이 더 길면 그쪽이 우선(둘 중 긴 쪽). 끄면 Duration만 사용")]
        [SerializeField] private bool lockMovementForReactionAnim = true;

        [Header("Auto Guard (AI) — Fallback")]
        [Tooltip("[하위호환] 자동 가드 확률. 이제 CharacterAbility.GuardChance가 우선이며, 어빌리티가 없을 때만 이 값을 쓴다. 튜닝은 CharacterAbility에서 한다")]
        [SerializeField, Range(0f, 1f)] private float autoGuardChance = 0f;

        [Header("Feedback")]
        [Tooltip("가드 성공 시 재생할 FeedbackManager 키. 비우면 재생 안 함")]
        [SerializeField] private string guardFeedbackKey = "Guard";
        [Tooltip("가드 브레이크 시 재생할 FeedbackManager 키. 비우면 재생 안 함")]
        [SerializeField] private string guardBreakFeedbackKey = "GuardBreak";
        [Tooltip("가드 성공 VFX를 캐릭터 앞쪽으로 당기는 높이/전방 오프셋(m)")]
        [SerializeField] private Vector3 guardFeedbackOffset = new Vector3(0f, 1.5f, 0f);

        [Header("Guard Camera Zoom")]
        [Tooltip("가드 성공 시 카메라 FOV 줌 펀치 사용 여부. 플레이어 전용(적 가드에는 적용 안 함). 패링 줌(CharacterParryPresentation)과 별개")]
        [SerializeField] private bool guardCameraZoom = true;
        [Tooltip("FOV 변화량(deg). 음수 = 줌인, 양수 = 줌아웃. 공격 타격 줌(-5~-11)보다 약하게 권장")]
        [SerializeField] private float guardZoomFovDelta = -3f;
        [Tooltip("줌인에 걸리는 시간(초, 실시간)")]
        [SerializeField] private float guardZoomEaseIn = 0.04f;
        [Tooltip("줌인 최대 상태 유지 시간(초, 실시간)")]
        [SerializeField] private float guardZoomHold = 0.05f;
        [Tooltip("줌아웃(복귀)에 걸리는 시간(초, 실시간)")]
        [SerializeField] private float guardZoomEaseOut = 0.18f;

        [Header("Parry")]
        [Tooltip("패링 사용 여부. 가드 시작(입력 누른 순간) 패링 창이 열린다")]
        [SerializeField] private bool enableParry = true;
        [Tooltip("패링 유효 시간(초). 가드 시작 후 이 시간 안에 맞으면 패링 성공 (구버전 ParryWindow)")]
        [SerializeField, Range(0f, 0.6f)] private float parryWindow = 0.3f;
        [Tooltip("퍼펙트 패링 판정 시간(초). 가드 시작 직후 이 시간 안이면 퍼펙트")]
        [SerializeField, Range(0f, 0.3f)] private float perfectParryWindow = 0.12f;
        [Tooltip("패링 실패(창 놓침) 후 다음 패링까지 쿨다운(초). 성공 시 즉시 0 (구버전 ParryCooldown 0.5)")]
        [SerializeField, Range(0f, 2f)] private float parryCooldown = 0.5f;
        [Tooltip("일반 패링 성공 시 공격자 프레임스톱(초) (구버전 ParriedFrameStop 0.1)")]
        [SerializeField] private float parriedFrameStop = 0.1f;
        [Tooltip("퍼펙트 패링 성공 시 공격자 프레임스톱(초) (구버전 PerfectParriedFrameStop 0.3)")]
        [SerializeField] private float perfectParriedFrameStop = 0.3f;
        [Tooltip("일반 패링 성공 시 공격자를 취약(Vulnerable) 상태로 만드는 시간(초). 이 동안 공격자는 이동/공격 잠금 + 무방비(가드/패링 불가). 0 = 취약 상태 없음(프레임스톱만)")]
        [SerializeField, Range(0f, 5f)] private float parryVulnerableDuration = 0.5f;
        [Tooltip("퍼펙트 패링 성공 시 공격자를 취약(Vulnerable) 상태로 만드는 시간(초). 0 = 취약 상태 없음(프레임스톱만)")]
        [SerializeField, Range(0f, 5f)] private float perfectParryVulnerableDuration = 1f;
        [Tooltip("일반 패링 성공 시 충전할 투혼 (구버전 ChargeOnParry 6)")]
        [SerializeField] private float parryTouhonCharge = 6f;
        [Tooltip("퍼펙트 패링 성공 시 충전할 투혼 (구버전 ChargeOnPerfectParry 10)")]
        [SerializeField] private float perfectParryTouhonCharge = 10f;
        [Tooltip("일반 패링 성공 FeedbackManager 키")]
        [SerializeField] private string parryFeedbackKey = "Parry";
        [Tooltip("퍼펙트 패링 성공 FeedbackManager 키")]
        [SerializeField] private string perfectParryFeedbackKey = "PerfectParry";

        [Header("Parry Knockback")]
        [Tooltip("패링 성공 시 방어자가 뒤로 밀리는 세기(m/s). 0이면 밀림 없음. 가드 밀림과 별도로 튜닝한다")]
        [SerializeField, Range(0f, 20f)] private float parryKnockbackForce = 3f;
        [Tooltip("패링 성공 밀림(슬라이드+이동잠금) 지속 시간(초). 0이면 패링 밀림 없음. 가드 자세를 유지한 채 이 시간 동안 살짝 뒤로 밀린다. 가드 밀림과 동일한 슬라이드 창을 재사용한다")]
        [SerializeField, Range(0f, 1f)] private float parryKnockbackDuration = 0.12f;

        [Header("Counter Buff (Perfect Parry)")]
        [Tooltip("퍼펙트 패링 성공 시 카운터 버프 부여. 버프 보유 중에는 패링 반응 모션(후딜) 중에도 즉시 공격으로 캔슬할 수 있고, 온몸 발광(CharacterGuardReaction)이 유지된다")]
        [SerializeField] private bool enableCounterBuff = true;
        [Tooltip("카운터 버프 유지 시간(초). 0 = 시간 제한 없음(공격/피격으로만 소멸)")]
        [SerializeField, Range(0f, 10f)] private float counterBuffDuration = 2f;
        [Tooltip("공격을 시작하면(카운터 공격) 버프를 소모한다. 끄면 시간 만료/피격으로만 소멸")]
        [SerializeField] private bool counterBuffConsumeOnAttack = true;
        [Tooltip("방어되지 않은 실제 피격(HitLanded)을 당하면 버프를 잃는다")]
        [SerializeField] private bool counterBuffLostOnHit = true;

        [System.NonSerialized] private Character _character;
        [System.NonSerialized] private Animator _animator;
        [System.NonSerialized] private bool _isGuarding;
        [System.NonSerialized] private float _parryWindowUntil = -1f;
        [System.NonSerialized] private float _perfectParryUntil = -1f;
        [System.NonSerialized] private float _parryCooldownUntil = -1f;
        // 카운터 버프(퍼펙트 패링 보상) 런타임 — Time.time 타임스탬프. 부여/소모/상실은 CounterBuffChanged로 통지, 시간 만료는 HasCounterBuff 폴링.
        [System.NonSerialized] private bool _counterBuffActive;
        [System.NonSerialized] private float _counterBuffUntil = -1f;
        [System.NonSerialized] private bool _hitLandedSubscribed;

        // 강제 100% 가드(연속 피격) 런타임 상태 — 틱 없이 Time.time 타임스탬프로만 관리
        [System.NonSerialized] private int _recentHitCount;
        [System.NonSerialized] private float _lastHitTime = -999f;
        [System.NonSerialized] private float _forceGuardUntil = -1f;

        // 가드 성공 밀림(슬라이드+이동잠금) 창 — Time.time 타임스탬프로만 관리. CharacterGuardState가 잠금/해제 타이밍 소유.
        [System.NonSerialized] private float _guardKnockbackUntil = -1f;

        // 반응 모션(GuardDamaged/Parrying) 이동잠금 — 트리거 발사 후 Animator가 실제 상태에 들어가기까지 1~2프레임 지연을
        // 메우는 유예창. 유예가 끝나면 상태 태그(Parrying/GuardDamaged)가 재생 중인 동안만 잠금을 유지한다(= 모션 길이).
        [System.NonSerialized] private float _reactionGraceUntil = -1f;
        private const float ReactionGraceDuration = 0.15f;

        // 패링 모션 교차 재생 인덱스(0=Parrying1, 1=Parrying2). 패링 성공마다 토글.
        [System.NonSerialized] private int _parryAnimIndex;

        // 어빌리티 튜닝 값 조회 (없으면 안전한 기본값/하위호환 필드로 폴백)
        private float AbilityGuardChance => _character != null && _character.Ability != null ? _character.Ability.GuardChance : autoGuardChance;
        private float AbilityParryChance => _character != null && _character.Ability != null ? _character.Ability.ParryChance : 0f;
        private int ForceGuardHitCountValue => _character != null && _character.Ability != null ? _character.Ability.ForceGuardHitCount : 0;
        private float ForceGuardDurationValue => _character != null && _character.Ability != null ? _character.Ability.ForceGuardDuration : 1f;
        private float ForceGuardComboWindowValue => _character != null && _character.Ability != null ? _character.Ability.ForceGuardComboWindow : 2f;

        /// <summary>지금 강제 100% 가드 창이 유지 중인가(연속 피격 발동). HUD/디버그용.</summary>
        public bool IsForceGuarding => Time.time < _forceGuardUntil;

        /// <summary>지금 가드 자세(Guard 상태) 중인가. CharacterGuardState가 GuardStart/GuardStop으로 설정.</summary>
        public bool IsGuarding => _isGuarding;
        /// <summary>가드 사용 가능 여부(설정). 상태 진입 판정용.</summary>
        public bool CanGuard => canGuard;
        /// <summary>가드 자세 중 이동 허용 여부(설정). CharacterGuardState가 사용.</summary>
        public bool AllowMovementWhileGuarding => allowMovementWhileGuarding;
        /// <summary>가드 자세 중 이동 속도 배율(설정). CharacterGuardState가 사용.</summary>
        public float GuardMoveSpeedMultiplier => guardMoveSpeedMultiplier;
        /// <summary>가드 자세 애니 재생 여부(설정). false면 반응(GuardDamaged)만. StateManager가 사용.</summary>
        public bool PlayGuardStanceAnimation => playGuardStanceAnimation;
        /// <summary>가드 중 넉백/밀림 저항(0~1). CharacterCombat.EnterHitReactionState가 사용.</summary>
        public float GuardKnockbackResistance => guardKnockbackResistance;
        /// <summary>가드 성공 밀림(슬라이드+이동잠금) 지속 시간(초). 0이면 밀림 없음. CharacterCombat/CharacterGuardState가 사용.</summary>
        public float GuardKnockbackDuration => guardKnockbackDuration;
        /// <summary>가드 성공 시 최소 밀림 세기(m/s). 공격 HorizontalForce가 0이어도 이 값만큼 무조건 밀린다. CharacterCombat이 사용.</summary>
        public float GuardKnockbackForce => guardKnockbackForce;
        /// <summary>가드 성공 밀림(이동잠금) 창이 유지 중인가. CharacterGuardState가 잠금/해제 타이밍에 사용.</summary>
        public bool IsGuardKnockbackActive => Time.time < _guardKnockbackUntil;

        /// <summary>
        /// 가드 자세 중 이동잠금을 유지해야 하는가 — 밀림 창(Duration) 또는 반응 모션(GuardDamaged/Parrying) 재생 중이면 true.
        /// 모션 길이 판정은 Animator 상태 태그로 한다(클립을 바꿔도 자동 반영). CharacterGuardState가 잠금/해제 타이밍에 사용.
        /// </summary>
        public bool IsReactionLockActive
        {
            get
            {
                if (IsGuardKnockbackActive) return true;
                if (!lockMovementForReactionAnim) return false;
                if (Time.time < _reactionGraceUntil) return true;
                return IsReactionAnimPlaying;
            }
        }

        /// <summary>반응 모션(태그 Parrying/GuardDamaged)이 레이어 0에서 재생 중(전이 진입 포함)인가.</summary>
        public bool IsReactionAnimPlaying
        {
            get
            {
                if (_animator == null || !_animator.isActiveAndEnabled) return false;
                var cur = _animator.GetCurrentAnimatorStateInfo(0);
                if (IsReactionTag(cur)) return true;
                if (_animator.IsInTransition(0) && IsReactionTag(_animator.GetNextAnimatorStateInfo(0))) return true;
                return false;
            }
        }

        private static bool IsReactionTag(AnimatorStateInfo st)
            => st.IsTag(AnimParams.ParryingTag) || st.IsTag(AnimParams.GuardDamagedTag);

        /// <summary>반응 모션 이동잠금 유예창 오픈 — Guard/Parry 성공 순간(트리거 발사와 동시에) 호출. 가드 자세 중일 때만 의미.</summary>
        private void BeginReactionLock()
        {
            if (!lockMovementForReactionAnim || !_isGuarding) return;
            _reactionGraceUntil = Time.time + ReactionGraceDuration;
        }

        /// <summary>가드 성공 밀림 시작 — CharacterCombat이 Movement.BeginKnockback과 함께 호출. Duration이 0 이하면 무시.</summary>
        public void BeginGuardKnockback()
        {
            if (guardKnockbackDuration <= 0f) return;
            _guardKnockbackUntil = Time.time + guardKnockbackDuration;
        }

        /// <summary>가드 성공 밀림 창 즉시 해제 — CharacterGuardState가 슬라이드 종료/상태 이탈 시 호출.</summary>
        public void ClearGuardKnockback()
        {
            _guardKnockbackUntil = -1f;
            _reactionGraceUntil = -1f;
        }

        /// <summary>패링 성공 시 방어자가 뒤로 밀리는 세기(m/s). CharacterCombat이 사용.</summary>
        public float ParryKnockbackForce => parryKnockbackForce;
        /// <summary>패링 성공 밀림 지속 시간(초). 0이면 패링 밀림 없음. CharacterCombat이 사용.</summary>
        public float ParryKnockbackDuration => parryKnockbackDuration;

        // ─────────── 카운터 버프 (퍼펙트 패링 보상) ───────────

        /// <summary>카운터 버프 보유 중인가 — 퍼펙트 패링 직후 부여. 보유 중에는 패링 후딜을 공격으로 즉시 캔슬 가능(CharacterGuardState) + 발광 유지(CharacterGuardReaction). 시간 만료는 여기서 판정한다.</summary>
        public bool HasCounterBuff
        {
            get
            {
                if (!_counterBuffActive) return false;
                if (counterBuffDuration > 0f && Time.time >= _counterBuffUntil)
                {
                    _counterBuffActive = false; // 만료 — 다음 조회에서 정리 + 통지
                    CounterBuffChanged?.Invoke(false);
                    return false;
                }
                return true;
            }
        }

        /// <summary>카운터 버프 상태 변화 통지(true=부여, false=소모/상실/만료). 연출 계층(발광 유지 등)이 구독한다. 방어 로직은 이 이벤트로 바뀌지 않는다.</summary>
        public event System.Action<bool> CounterBuffChanged;

        /// <summary>카운터 버프 부여 — 퍼펙트 패링 성공 시 OnParrySuccess가 호출. 이미 보유 중이면 시간만 갱신(연장).</summary>
        private void GrantCounterBuff()
        {
            if (!enableCounterBuff) return;
            bool wasActive = HasCounterBuff;
            _counterBuffActive = true;
            _counterBuffUntil = counterBuffDuration > 0f ? Time.time + counterBuffDuration : float.PositiveInfinity;
            if (!wasActive) CounterBuffChanged?.Invoke(true);
        }

        /// <summary>공격 시작 시 버프 소모 — CharacterCombat.ExecuteStartAttack이 호출. counterBuffConsumeOnAttack이 꺼져 있으면 유지.</summary>
        public void ConsumeCounterBuffOnAttack()
        {
            if (!counterBuffConsumeOnAttack) return;
            ClearCounterBuff();
        }

        /// <summary>카운터 버프 즉시 제거(소모/상실). 보유 중이 아니면 무동작.</summary>
        public void ClearCounterBuff()
        {
            if (!_counterBuffActive) return;
            _counterBuffActive = false;
            _counterBuffUntil = -1f;
            CounterBuffChanged?.Invoke(false);
        }

        private void OnHitLandedLoseCounterBuff(DamageInfo damage)
        {
            if (counterBuffLostOnHit) ClearCounterBuff();
        }

        /// <summary>패링 성공 밀림 시작 — 가드 밀림과 동일한 슬라이드 창(_guardKnockbackUntil)을 재사용한다. CharacterGuardState가 이동잠금/해제를 소유하므로 가드 자세 중일 때만 호출한다. Duration이 0 이하면 무시.</summary>
        public void BeginParryKnockback()
        {
            if (parryKnockbackDuration <= 0f) return;
            _guardKnockbackUntil = Time.time + parryKnockbackDuration;
        }

        /// <summary>
        /// 패링 성공 순간 발생하는 연출 전용 훅. 인자 bool = 퍼펙트 패링 여부.
        /// 방어 판정/로직은 이 이벤트로 바뀌지 않는다(발행만). 카메라 시퀀스 등 프레젠테이션 계층이 구독한다.
        /// </summary>
        public event System.Action<bool> ParrySucceeded;

        /// <summary>가드 성공 순간 발생하는 연출 전용 훅(발행만, 방어 로직 불변). 프레젠테이션 계층이 구독한다.</summary>
        public event System.Action GuardSucceeded;

        public void Bind(Character character) => _character = character;

        public void OnAwake()
        {
            _animator = _character != null ? _character.GetComponentInChildren<Animator>(true) : null;
            // 실제 피격(방어 실패) 시 카운터 버프 상실 — Combat.OnAwake가 먼저 실행되므로 여기서 구독(멱등).
            if (!_hitLandedSubscribed && _character != null && _character.Combat != null)
            {
                _character.Combat.HitLanded += OnHitLandedLoseCounterBuff;
                _hitLandedSubscribed = true;
            }
        }

        // ─────────── 가드 자세 (Guard 상태가 호출) ───────────

        /// <summary>가드 자세 시작 — CharacterGuardState.Enter에서 호출. (Phase 2: 패링 창도 여기서 연다)</summary>
public void GuardStart()
        {
            _isGuarding = true;
            ActivateParry(); // 가드 시작 = 패링 창 오픈(구버전 GuardStart(activateParry:true))
        }

        /// <summary>가드 자세 종료 — CharacterGuardState.Exit에서 호출.</summary>
        public void GuardStop()
        {
            _isGuarding = false;
        }

/// <summary>패링 창 열기 — 가드 시작(입력 누른 순간)에 호출. 쿨다운 중이면 열지 않는다(일반 가드만).</summary>
        public void ActivateParry()
        {
            if (!enableParry) return;
            if (Time.time < _parryCooldownUntil) return;
            float now = Time.time;
            _parryWindowUntil = now + parryWindow;
            _perfectParryUntil = now + perfectParryWindow;
            _parryCooldownUntil = now + parryCooldown; // 성공 시 아래에서 0으로 해제
        }

        /// <summary>지금 패링 창이 열려 있는가(디버그/HUD용).</summary>
        public bool IsParryWindowOpen => Time.time <= _parryWindowUntil;
        /// <summary>일반 패링 성공 시 공격자에게 거는 취약(Vulnerable) 시간(초). 0이면 취약 없음.</summary>
        public float ParryVulnerableDuration => parryVulnerableDuration;
        /// <summary>퍼펙트 패링 성공 시 공격자에게 거는 취약(Vulnerable) 시간(초). 0이면 취약 없음.</summary>
        public float PerfectParryVulnerableDuration => perfectParryVulnerableDuration;


        // ─────────── 방어 판정 (CharacterCombat.ReceiveDamage가 호출) ───────────

        /// <summary>
        /// 들어온 데미지를 방어 판정한다. 가드 성공이면 damage.Damage를 감소시키고 Guard를 반환한다.
        /// 판정 순서: 타이밍 패링 → 확률 패링 → 가드 불가 → (자세/강제100%/확률) 가드 → 방향 → 게이지(브레이크).
        /// 확률 값(가드/패링/강제가드)은 CharacterAbility에서 읽어 튜닝한다.
        /// </summary>
public DefenseResult Resolve(ref DamageInfo damage)
        {
            if (_character == null) return DefenseResult.None;
            if (_character.CurrentHP <= 0f) return DefenseResult.None;

            float now = Time.time;

            // ── 1. 타이밍 패링 (가드 시작 시 창 오픈 — 주로 플레이어) — 가드 불가 공격도 스킬로 받아낼 수 있다 ──
            if (enableParry && now <= _parryWindowUntil)
            {
                bool perfect = now <= _perfectParryUntil;
                _parryWindowUntil = -1f;
                _perfectParryUntil = -1f;
                _parryCooldownUntil = 0f; // 성공 → 쿨다운 즉시 해제(연속 패링 무료, 구버전)
                OnParrySuccess(damage, perfect);
                return perfect ? DefenseResult.PerfectParry : DefenseResult.Parry;
            }

            // ── 2. 확률 패링 (Ability.ParryChance — 주로 적 패시브) — 가드 불가 공격은 대상 아님 ──
            float parryChance = AbilityParryChance;
            if (parryChance > 0f && !damage.Unblockable && Random.value <= parryChance)
            {
                OnParrySuccess(damage, false);
                return DefenseResult.Parry;
            }

            if (!canGuard) return DefenseResult.None;

            // 가드 불가 공격은 방어 불가 (강제 100% 가드 중에도 그대로 관통)
            if (damage.Unblockable) return DefenseResult.None;

            // ── 연속 피격 창 유지 (강제 가드 기능이 켜진 경우에만) ──
            //     가드 여부와 무관하게 '공격받는 중'이면 콤보를 살아있는 것으로 본다.
            bool trackForceGuard = ForceGuardHitCountValue > 0;
            if (trackForceGuard)
            {
                if (now - _lastHitTime > ForceGuardComboWindowValue) _recentHitCount = 0;
                _lastHitTime = now;
            }

            // ── 3. 가드 성립 판정 — 활성 가드 자세 / 강제 100% 가드 / 확률 자동 가드 중 하나 ──
            bool forceActive = now < _forceGuardUntil;
            bool willGuard = _isGuarding || forceActive;
            if (!willGuard)
            {
                float gc = AbilityGuardChance;
                willGuard = gc > 0f && Random.value <= gc;
            }
            if (!willGuard)
            {
                if (trackForceGuard) RegisterUnguardedHit(now);
                return DefenseResult.None;
            }

            // 방향 판정 — 강제 가드는 전방위(무조건). 그 외에는 정면만(전방위 옵션 제외).
            if (!forceActive && !guardAllDirections && !IsAttackFromFront(damage.HitDirection))
            {
                if (trackForceGuard) RegisterUnguardedHit(now);
                return DefenseResult.None;
            }

            // 가드 게이지(투혼) 소모 → 소진 시 가드 브레이크. 강제 100% 가드 중에는 무한(브레이크 없음).
            if (!forceActive && guardTouhonDrainPerBlock > 0f && _character.MaxTouhon > 0f && _character.Combat != null)
            {
                bool wouldEmpty = _character.CurrentTouhon <= guardTouhonDrainPerBlock;
                _character.Combat.DrainTouhon(guardTouhonDrainPerBlock);
                if (wouldEmpty && guardBreakOnGaugeEmpty)
                {
                    PlayGuardBreakFeedback();
                    return DefenseResult.GuardBreak; // 풀 데미지 + 경직(Hit)로 진행
                }
            }

            // 가드 성공 — 데미지 감소 + 연출
            damage.Damage *= guardDamageMultiplier;
            // 강제 가드 창 중에 또 맞으면 유지 시간 초기화(연장)
            if (forceActive)
                _forceGuardUntil = now + ForceGuardDurationValue;
            PlayGuardFeedback(damage);
            PlayGuardCameraZoom();
            SetTrigger(AnimParams.GuardDamaged);
            BeginReactionLock(); // 이동잠금을 GuardDamaged 모션 길이만큼 유지
            GuardSucceeded?.Invoke();
            return DefenseResult.Guard;
        }

        /// <summary>막지 못하고 실제로 맞은 히트를 카운트 — 연속 N회 도달 시 강제 100% 가드 발동.</summary>
        private void RegisterUnguardedHit(float now)
        {
            _recentHitCount++;
            if (_recentHitCount >= ForceGuardHitCountValue)
            {
                _forceGuardUntil = now + ForceGuardDurationValue; // 이후 유지 시간 동안 100% 가드
                _recentHitCount = 0;                               // 재무장
            }
        }

        /// <summary>공격이 정면에서 오는가 — 바라보는 방향과 (공격자→나) 방향의 dot으로 판정.</summary>
        private bool IsAttackFromFront(Vector3 hitDirection)
        {
            var m = _character.Movement;
            if (m == null) return true;
            Vector3 facing = m.FacingRight ? m.SplineForward : -m.SplineForward;
            if (facing.sqrMagnitude < 0.0001f) return true;
            // hitDirection = 공격자→나. 정면 공격이면 hitDirection ≈ -facing → dot ≤ guardFrontDot.
            return Vector3.Dot(facing.normalized, hitDirection.normalized) <= guardFrontDot;
        }

/// <summary>패링 성공 처리 — 방어자 연출(Parrying 애니 + 투혼 충전 + VFX) + 공격자 반응(Parried + 프레임스톱 + 취약 상태, 일반/퍼펙트 각각 시간 설정).</summary>
        private void OnParrySuccess(DamageInfo damage, bool perfect)
        {
            // Parrying1 / Parrying2 교차 재생 — 컨트롤러 Parrying 서브머신 Entry가 ParryingStage==1이면 Parrying2, 아니면 Parrying1.
            SetInt(AnimParams.ParryingStage, _parryAnimIndex);
            _parryAnimIndex = 1 - _parryAnimIndex;
            SetTrigger(AnimParams.Parrying);
            BeginReactionLock(); // 이동잠금을 Parrying 모션 길이만큼 유지
            if (_character.Combat != null)
                _character.Combat.ChargeTouhon(perfect ? perfectParryTouhonCharge : parryTouhonCharge);
            PlayParryFeedback(perfect);
            if (perfect) GrantCounterBuff(); // 퍼펙트 패링 보상 — ParrySucceeded 발행 전에 부여해 연출 계층이 같은 프레임에 반영

            if (damage.Attacker != null)
            {
                var atkChar = damage.Attacker.GetComponentInParent<Character>();
                if (atkChar != null && atkChar.Combat != null)
                    atkChar.Combat.ReactToParried(perfect,
                        perfect ? perfectParriedFrameStop : parriedFrameStop,
                        perfect ? perfectParryVulnerableDuration : parryVulnerableDuration);
            }

            // 연출 계층 알림(카메라 시퀀스 등). 방어 로직과 무관 — 구독자 없으면 무동작.
            ParrySucceeded?.Invoke(perfect);
        }

        private void PlayParryFeedback(bool perfect)
        {
            string key = perfect ? perfectParryFeedbackKey : parryFeedbackKey;
            if (string.IsNullOrEmpty(key) || _character == null) return;
            Vector3 pos = _character.transform.position + guardFeedbackOffset;
            Aiara.FeedbackManager.PlayFeedbackAtWorld(key, pos, Quaternion.identity, FacingFeedbackScale());
        }

        /// <summary>
        /// 가드/패링 VFX 좌우 미러 — 오른쪽 기준으로 만든 이펙트를 왼쪽을 볼 때 좌우 반전한다.
        /// identity 회전(월드 정렬)으로 재생되므로, 좌우축(스플라인 진행축)의 월드 성분을 음수 스케일로
        /// 뒤집어 반사한다. 오른쪽일 때는 (1,1,1) — 기존과 완전 동일. MMF Instantiate의 Also Apply Scale 필요.
        /// </summary>
        private Vector3 FacingFeedbackScale()
        {
            var mv = _character != null ? _character.Movement : null;
            if (mv == null || mv.FacingRight) return Vector3.one;
            Vector3 f = mv.SplineForward; f.y = 0f;
            Vector3 s = Vector3.one;
            if (Mathf.Abs(f.x) >= Mathf.Abs(f.z)) s.x = -1f; else s.z = -1f;
            return s;
        }


        // ─────────── 연출 ───────────

        private void PlayGuardFeedback(DamageInfo damage)
        {
            if (string.IsNullOrEmpty(guardFeedbackKey) || _character == null) return;
            Vector3 pos = _character.transform.position + guardFeedbackOffset;
            Aiara.FeedbackManager.PlayFeedbackAtWorld(guardFeedbackKey, pos, Quaternion.identity, FacingFeedbackScale());
        }

        /// <summary>
        /// 가드 성공 카메라 줌 펀치 — 플레이어 전용. 적(EnemyCharacter)이 가드해도 카메라는 흔들지 않는다.
        /// Camera.main FOV 직접 조작은 LensCameraNode가 덮어쓰므로 반드시 CameraZoomService(리그 채널)로 요청한다. 실시간 기준.
        /// </summary>
        private void PlayGuardCameraZoom()
        {
            if (!guardCameraZoom || _character == null || _character is EnemyCharacter) return;
            CameraZoomService.Punch(guardZoomFovDelta, guardZoomEaseIn, guardZoomHold, guardZoomEaseOut);
        }

        private void PlayGuardBreakFeedback()
        {
            if (string.IsNullOrEmpty(guardBreakFeedbackKey) || _character == null) return;
            Vector3 pos = _character.transform.position + guardFeedbackOffset;
            Aiara.FeedbackManager.PlayFeedbackAtWorld(guardBreakFeedbackKey, pos, Quaternion.identity, FacingFeedbackScale());
            SetTrigger(AnimParams.GuardBreak);
        }

        private void SetInt(string paramName, int value)
        {
            if (_animator == null || string.IsNullOrEmpty(paramName)) return;
            foreach (var p in _animator.parameters)
                if (p.name == paramName && p.type == AnimatorControllerParameterType.Int)
                {
                    _animator.SetInteger(paramName, value);
                    return;
                }
        }

        private void SetTrigger(string paramName)
        {
            if (_animator == null || string.IsNullOrEmpty(paramName)) return;
            foreach (var p in _animator.parameters)
                if (p.name == paramName && p.type == AnimatorControllerParameterType.Trigger)
                {
                    _animator.SetTrigger(paramName);
                    return;
                }
        }
    }
}
