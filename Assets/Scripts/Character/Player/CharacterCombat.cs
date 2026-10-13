using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 캐릭터 전투 흐름 오케스트레이터 — 공격 시작/진행/종료와 피격 처리만 담당(상태 아님).
    ///
    /// ※ 역할 분리(2026-08-20): 공격의 "내용"(애니 스테이트/타이밍/배율/VFX/Hitbox)은
    /// AttackAction(구 AttackEffectController) 프리팹이 전부 소유하고,
    /// 여기서는 어떤 AttackAction을 언제 실행/종료할지만 결정한다.
    /// 상태 여부는 CharacterStateManager(Attack 상태)가 판단한다.
    /// 데미지 = Character.Stats.FinalAttackPower(=Ability.AttackPower) × AttackAction.DamageMultiplier.
    /// </summary>
    [System.Serializable]
    public class CharacterCombat : IDamageReceiver
    {
        [Header("Attacks")]
        [Tooltip("이 캐릭터가 실행할 수 있는 공격 목록. 비우면 자식(AttackRoot 아래)에서 자동 수집. [0] = 기본 공격")]
        [SerializeField] private List<AttackAction> attacks = new List<AttackAction>();
        [Tooltip("콤보 그룹 목록(시작 공격 선택용). 비우면 자식에서 자동 수집")]
        [SerializeField] private List<ComboGroup> comboGroups = new List<ComboGroup>();


        [Header("Combo")]
        [Tooltip("공격 종료 후 이 시간(초) 안에 기본 공격을 입력하면 다음 콤보 타부터 이어진다. 구버전 DropComboDelay 이식.")]
        [SerializeField, Range(0f, 1f)] private float comboDropDelay = 0.3f;
        [Tooltip("공중콤보 Arm 유지 시간(초): 시동기(올려치기) 적중 후 이 시간 안에 공중이면 AirCombo 그룹 시작 가능")]
        [SerializeField, Range(0f, 3f)] private float airComboArmDuration = 1.5f;

        [Header("Mode")]
        [Tooltip("전투 모드 여부. Animator BattleMode 파라미터 반영은 StateManager가 담당.")]
        [SerializeField] private bool battleMode = true;

        [Header("FP (Resource)")]
        [Tooltip("패시브 자동 회복량 = 매초 최대 FP의 %. 0.5 = 약 200초 완충. 공격 모션 중에는 정지. 0 = 자동 회복 없음 (구버전 CharacterFP.RegainPercentPerSecond)")]
        [SerializeField, Range(0f, 10f)] private float fpRegenPercentPerSecond = 0.5f;
        [Tooltip("공격 적중 시 회복할 FP. 0 = 비활성 (구버전 FPRegainOnAttackHit)")]
        [SerializeField] private float fpRegainOnAttackHit = 0f;

        [Header("Touhon (Battle Spirit)")]// (구버전 BattleSpiritResource 이관 — MaxTouhon(Ability)=0이면 시스템 전체 비활성)
        [Tooltip("공격 적중 시 투혼 증가량 (구버전 ChargeOnAttackHit)")]
        [SerializeField] private float touhonChargeOnAttackHit = 2f;
        [Tooltip("적 처치 시 투혼 증가량 (구버전 ChargeOnKill)")]
        [SerializeField] private float touhonChargeOnKill = 0f;
        [Tooltip("피격 시 투혼 감소량 (구버전 DrainOnNormalHit)")]
        [SerializeField] private float touhonDrainOnHit = 2f;
        [Tooltip("투혼 0 도달(허주) 후 리셋까지 대기 시간(초) (구버전 SpiritResetDelay)")]
        [SerializeField] private float touhonResetDelay = 10f;
        [Tooltip("리셋 시 설정될 투혼 절대값 (구버전 ResetValue — 기본 Max 72의 절반)")]
        [SerializeField] private float touhonResetValue = 36f;

        [Header("Heoju (Touhon 0 Debuff)")]
        [Tooltip("허주 상태 이동 속도 배율 (구버전 1/1.2 ≈ 0.833)")]
        [SerializeField] private float heojuSpeedMultiplier = 0.833f;
        [Tooltip("허주 상태 데미지 출력 배율 (구버전 1/1.2 ≈ 0.833)")]
        [SerializeField] private float heojuDamageMultiplier = 0.833f;
        [Tooltip("허주 상태 공격 애니메이션 배속 배율 (구버전 0.7)")]
        [SerializeField] private float heojuAttackSpeedMultiplier = 0.7f;

        [Header("Execution (절명기 / 처형)")]// (구버전 CharacterExecution/CharacterGroggy 이관)
        [Tooltip("투혼 0 도달 시 허주 대신 그로기(처형 대상)로 만든다. 처형 가능한 적(Enemy)에 켠다. 플레이어/보스는 끄면 기존 허주 유지")]
        [SerializeField] private bool groggyOnTouhonDepleted = false;
        [Tooltip("처형 대상(그로기 적)을 찾는 반경(m) (구버전 ExecutionRange=3)")]
        [SerializeField] private float executionRange = 3f;
        [Tooltip("처형 접근 연출 시간(초) — 이 동안 시전자는 무적으로 대상 앞으로 이동 (구버전 ApproachDuration=0.7)")]
        [SerializeField] private float executionApproachDuration = 0.7f;
        [Tooltip("처형 실행(확정 처형) 연출 시간(초)")]
        [SerializeField] private float executionDuration = 1.0f;
        [Tooltip("처형 성공 시 시전자 회복량 = 최대 체력의 비율 (구버전 ExecutionHealPercent=0.05)")]
        [SerializeField, Range(0f, 1f)] private float executionHealPercent = 0.05f;

        [System.NonSerialized] private bool _groggyActive;
        [System.NonSerialized] private bool _pendingGroggy; // 공중/다운 중 투혼 파괴 → 착지 후 그로기 진입 대기 (구버전 _pendingGroggyEntry)
        [System.NonSerialized] private bool _invincible;
        [System.NonSerialized] private Character _executionTarget;
        [System.NonSerialized] private bool _executionFinishRequested;

        // 최근 적중 대상 (잡기 대상 선정용 — 구버전 LastHitHealth 이식)
        [System.NonSerialized] private Character _lastHitVictim;
        [System.NonSerialized] private float _lastHitVictimTime = -999f;
        [System.NonSerialized] private AttackAction _lastHitAction;

        /// <summary>최근 이 캐릭터의 공격에 적중한 대상. 잡기(ActionGrab) 대상 선정용.</summary>
        public Character LastHitVictim => _lastHitVictim;
        /// <summary>최근 적중 시각(Time.time). 오래된 적중을 잡기 대상에서 제외할 때 사용.</summary>
        public float LastHitVictimTime => _lastHitVictimTime;
        /// <summary>최근 적중을 낸 공격(AttackAction). 잡기 사거리의 공격별 오버라이드(GrabAttackLink.AttackRangeOverrides) 판정용 (2026-09-14).</summary>
        public AttackAction LastHitAction => _lastHitAction;

        /// <summary>
        /// 처형 연출 조기 종료 요청(플레이어 클립 ExecuteFinish 이벤트 등).
        /// CharacterExecutionPresentation이 설정하고 CharacterExecutingState가 소비한다.
        /// </summary>
        public bool ExecutionFinishRequested => _executionFinishRequested;
        public void RequestExecutionFinish() => _executionFinishRequested = true;
        public void ConsumeExecutionFinishRequest() => _executionFinishRequested = false;


        [System.NonSerialized] private bool _heojuActive;
        [System.NonSerialized] private float _touhonResetTimer;


        [Header("Hit / Death Feedback")]
        [Tooltip("피격 시 재생할 FeedbackManager 키 (구버전 Hit/HitLight/HitCritical 계열). 비우면 재생 안 함")]
        [SerializeField] private string hitFeedbackKey = "Hit";
        [Tooltip("사망 시 재생할 FeedbackManager 키 (구버전 Death/DeathSmall). 비우면 재생 안 함")]
        [SerializeField] private string deathFeedbackKey = "Death";
        [Tooltip("사망 Feedback 재생 지연(초) — 사망 애니메이션 종료 시점에 맞춤 (구버전 Destroy 이벤트 타이밍). 0=즉시")]
        [SerializeField] private float deathFeedbackDelay = 0f;
        [Tooltip("사망 Feedback(폭발) 재생 시점에 캐릭터를 제거(비활성화)할지 — 적 전용. 구버전 CharacterDeathSequence.OnDestroyEvent 이식. 플레이어는 꺼둘 것")]
        [SerializeField] private bool despawnOnDeathFeedback = false;
        [Tooltip("같은 키 재생 최소 간격(초) — 다단히트 스팸 방지 (구버전 HitVFXReplayCooldown)")]
        [SerializeField] private float hitFeedbackCooldown = 0.1f;
        [Tooltip("Hit VFX를 카메라 쪽으로 당기는 오프셋(m) — 모델에 파묻힘 방지 (구버전 HitVFXCameraOffset)")]
        [SerializeField] private float hitVFXCameraOffset = 0.3f;
        [Tooltip("경량화: 짧은 시간창 안에 첫 피격만 큰 이펙트(Hit), 나머지는 약한 이펙트(아래 키)로 대체. 적(Enemy) 전용 — 플레이어/아군은 항상 큰 이펙트. (HitEffectBudget)")]
        [SerializeField] private bool useLightHitBudget = true;
        [Tooltip("경량화용 약한 피격 키 — 시간창에서 큰 이펙트를 얻지 못한 적이 재생. 비우면 약한 이펙트 없이 생략(최경량). useLightHitBudget=true일 때만 사용")]
        [SerializeField] private string hitFeedbackKeyLight = "HitLight";
        [Tooltip("치명타 피격 시 재생할 FeedbackManager 키 (구버전 HitCritical). 설정 시 경량화 예산과 무관하게 우선 재생. 비우면 일반 Hit 키 사용")]
        [SerializeField] private string hitFeedbackKeyCritical = "HitCritical";

        [Header("Critical Hit Stop")]
        [Tooltip("이 캐릭터의 공격이 치명타로 적을 맞혔을 때 전역 히트스탑(시간 정지)을 발동한다. 값은 '공격자' 기준으로 읽히므로 플레이어에만 설정하면 모든 적·모든 공격에 동일하게 적용된다. 동시 발동 시 가중 없이 가장 강한 하나만 적용(FeedbackTime).")]
        [SerializeField] private bool criticalHitstopEnable = true;
        [Tooltip("정지 강도. 0 = 완전 정지(진짜 히트스탑), 0.2 = 20% 속도. 1이면 효과 없음")]
        [Range(0f, 1f)]
        [SerializeField] private float criticalHitstopScale = 0f;
        [Tooltip("정지 유지 시간(초, 실시간 기준)")]
        [SerializeField] private float criticalHitstopDuration = 0.08f;
        [Tooltip("정상 → 정지까지 Lerp 진입 시간(초). 0 = 즉시 스냅(기본 히트스탑). 값을 주면 슬로우모션처럼 부드럽게 진입")]
        [SerializeField] private float criticalHitstopEaseIn = 0f;
        [Tooltip("정지 → 정상까지 Lerp 복귀 시간(초). 0 = 즉시 스냅(기본 히트스탑). 값을 주면 슬로우모션처럼 부드럽게 복귀")]
        [SerializeField] private float criticalHitstopEaseOut = 0f;

        [Header("Bounce Landing Feedback")]
        [Tooltip("그라운드 바운스 바닥 충돌 시 이 캐릭터 발밑에서 재생할 FeedbackManager 키(큰 랜딩). 비우면 생략")]
        [SerializeField] private string bounceLandingFeedbackKey = "Landing";
        [Tooltip("큰 랜딩(bounceLandingFeedbackKey)을 재생할 바닥 충돌 횟수. 1 = 첫 착지만 큰 랜딩, 이후 작은 랜딩")]
        [SerializeField, Min(0)] private int bounceLandingBigCount = 1;
        [Tooltip("큰 랜딩 횟수를 넘긴 바운스 충돌마다 재생할 FeedbackManager 키(작은 랜딩). 비우면 이후 바운스는 피드백 없음")]
        [SerializeField] private string bounceLandingSmallFeedbackKey = "Landing_small";
        [Tooltip("바운스 랜딩 이펙트 위치 오프셋 (오른쪽 바라볼 때 스플라인 축 기준): x=전방, y=높이, z=깊이(카메라 반대쪽 +). Flip 시 전방(x) 자동 반전")]
        [SerializeField] private Vector3 bounceLandingOffset = Vector3.zero;


        [System.NonSerialized] private float _lastHitFeedbackTime = -999f;

        [Header("Runtime (View Only)")]
        [Tooltip("남은 HP. 실제 저장소는 Character 런타임(CharacterRuntimeStats.CurrentHP)")]
        [SerializeField] private float currentHPView;
        [Tooltip("최대 HP (Ability 기준)")]
        [SerializeField] private float maxHPView;

        /// <summary>피격 처리 직후 발생. Hit Flash/SFX/Camera Shake/HitStop 확장점(§24).</summary>
        public event System.Action<DamageInfo> HitReceived;

        /// <summary>방어되지 않은 실제 피격에서만 발생(가드/패링 제외). 히트 리액션 연출(빨강 플래시 등) 전용 훅.</summary>
        public event System.Action<DamageInfo> HitLanded;

        /// <summary>FP 부족으로 공격이 거부되었을 때 (필요량 전달). UI 경고 확장점.</summary>
        public event System.Action<float> FPInsufficient;
        /// <summary>공격(AttackAction) 실행이 시작될 때(콤보 연결 포함, 상태 전이 없이 같은 Attack 상태 안에서 이어질 때도). 시각 정착(CharacterVisualSettle) 등 연출용.</summary>
        public event System.Action<AttackAction> AttackStarted;
        /// <summary>투혼 MAX 도달 시. 잠재능력 발동 훅 (잠재능력은 미구현 — 이벤트만).</summary>
        public event System.Action TouhonMaxed;
        /// <summary>투혼 0 도달 시 (허주/그로기 훅).</summary>
        public event System.Action TouhonDepleted;
        /// <summary>허주 상태 전환 통지 (true=발동, false=해제).</summary>
        public event System.Action<bool> HeojuChanged;


        [System.NonSerialized] private Character _character;
        private Animator _animator;
        private AttackAction _currentAction;
        private float _attackTimer;
        private bool _attackActive;
        // ForwardMoveEventName 기준 전진 창 anchor (공격 시작 기준 초). 이벤트 미수신 시 -1.
        private float _forwardMoveAnchor = -1f;
        // 콤보 유예(구버전 DropComboDelay): 자연 종료 시 '끝난 타'를 잠시 기억해 재입력으로 잇는다.
        // (2026-08-27 입력 인지형: 다음 타를 미리 확정하지 않고, 유예 내 재입력의 종류(Light/Heavy)로 그 시점에 Transition 해석 — X 종료 직후 Y 파생도 연결)
        [System.NonSerialized] private AttackAction _comboGraceFrom;
        [System.NonSerialized] private float _comboGraceUntil = -1f;        // (디버그 필드 제거됨)
        // 공중콤보 Arm (구버전 CharacterAirCombo 축약 이식): 시동기 적중 후 일정 시간 동안 공중 AirCombo 시작 허용
        [System.NonSerialized] private float _airComboArmedUntil = -1f;
        /// <summary>공중콤보 시작 가능 상태(시동기 적중 후 Arm 유지 중)인가.</summary>
        public bool IsAirComboArmed => Time.time <= _airComboArmedUntil;
        // 이번 타의 본 스윙 상태(NormalAttack 태그)에 실제 도달했는가 — 이전 타 Transition 잔상을 Combo Window로 오인하는 것 방지(구버전 stale-guard 이식).
        [System.NonSerialized] private bool _mainStateSeen;
        // 이번 타의 Transition 상태에 도달했는가 — Transition을 벗어나면 공격 종료로 간주
        // (Animator 구조상 Transition exit가 SSM 기본 상태(NormalAttack1)로 재진입해 attack 태그가 안 풀리는 문제 대응).
        
        [System.NonSerialized] private bool _getupStateSeen;   // knockdown getup: whether the Getup-tagged state has been entered yet (for robust completion)
[System.NonSerialized] private bool _transitionSeen;
        // 이번 타가 ComboWindow(후딜) 상태에 처음 도달한 공격 타이머 값(초). -1 = 아직 미도달. 후딜 이동 캔슬 시점(MoveCancelDelay) 기준점.
        [System.NonSerialized] private float _windowEnterTimer = -1f;
        // 이번 공격이 공중에서 시작됐는가 (점프킥/공중콤보). 착지 처리(MinDuration 보장 + Land 전이)의 기준 — 2026-09-06.
        [System.NonSerialized] private bool _attackStartedAirborne;
        // 급강하 체공(AttackAction.DiveHoverDuration) 진행 — 체공이 끝나면 TickAttack이 ApplyDownwardDive (2026-09-15)
        [System.NonSerialized] private bool _divePending;
        [System.NonSerialized] private float _diveHoverRemaining;

        // Animator Tag 해시는 AttackAction별 필드(MainStateTag/ComboWindowTag)로 이동 (2026-08-21 분기형 콤보).


        /// <summary>현재 실행 중인 AttackAction (없으면 null). 종류 구분은 CurrentAttackName.</summary>
        public AttackAction CurrentAction => _currentAction;
        public string CurrentAttackName => _currentAction != null ? _currentAction.AttackName : "";
        /// <summary>현재(가장 최근 시작) 공격을 시작시킨 입력 종류(Light/Heavy/...). 강공격 대시 캔슬 판정에 사용.</summary>
        public AttackInputType CurrentAttackInput => _lastStartInputType;

        /// <summary>남은 HP. 저장소는 Character 런타임(CharacterRuntimeStats.CurrentHP)이고 여기서는 조회만 한다(§4).</summary>
        public float CurrentHP => _character != null ? _character.CurrentHP : 0f;
        public float MaxHP => _character != null ? _character.MaxHP : 0f;
        /// <summary>남은 HP 비율(0~1). UI/연출용.</summary>
        public float HPRatio => MaxHP > 0f ? CurrentHP / MaxHP : 0f;
        public bool IsDead => _character != null && _character.CurrentHP <= 0f;
        public bool IsAttacking => _attackActive;
        public bool IsAttackFinished => !_attackActive;

        /// <summary>현재 공격이 공중에서 시작됐는가(점프킥/공중콤보). 공격 중이 아니면 false.</summary>
        public bool CurrentAttackStartedAirborne => _attackActive && _attackStartedAirborne;
        /// <summary>현재 공격의 MinDuration이 경과했는가(차징 정지 시간 제외). 공격 중이 아니면 false.</summary>
        public bool IsPastMinDuration => _attackActive && _currentAction != null && _attackTimer >= _currentAction.MinDuration;

        /// <summary>
        /// 공중 시작 공격의 착지 처리(2026-09-06 점프킥 삑사리 수정). 공중에서 시작한 공격은 발이 닿는 순간 공격을 끝내고
        /// Land 상태로 간다(MinDuration 무관 — 상태가 착지를 결정, 애니는 StateManager가 착지 모션으로 CrossFade).
        /// 그동안 Animator Grounded 파라미터는 false로 보류해 공격 애니 상태의 자체 Grounded 전이(→Idle)가 먼저 발화하지 않게 한다.
        /// 급강하(DiveFall) 공격은 착지 자체가 본동작(JumpSmash 충격)이므로 제외 — 기존 Animator Grounded 전이 흐름 그대로.
        /// </summary>
        public bool HandlesAirborneLanding => _attackActive && _attackStartedAirborne && _currentAction != null && !_currentAction.DiveFall;
        /// <summary>Animator Grounded 파라미터를 false로 보류해야 하는가 — 공중 시작 공격(급강하 제외) 진행 중 전 구간. CharacterStateManager.AnimatorTick이 읽는다.</summary>
        public bool ShouldHoldAirborneAnim => HandlesAirborneLanding;

        private CharacterMovement Movement => _character != null ? _character.Movement : null;

        /// <summary>지금 공격 시작 가능한가: 지상 + 대시 중 아님.</summary>
        public bool CanStartAttack
        {
            get
            {
                var m = Movement;
                if (m == null) return false;
                if (!m.IsGrounded) return false;
                if (m.Dash != null && m.Dash.IsDashing) return false;
                return true;
            }
        }

        /// <summary>입력/방향까지 고려한 시작 가능 판정 — 지상은 항상, 공중은 해당 입력의 공중 시작 그룹이 있을 때만.</summary>
        public bool CanStartAttackWith(AttackInputType inputType, Vector2 moveInput)
        {
            var m = Movement;
            if (m == null) return false;
            if (m.Dash != null && m.Dash.IsDashing) return false;
            if (m.IsGrounded) return true;
            return ResolveStarter(inputType, moveInput) != null;
        }

        /// <summary>ComboGroup 시작 조건 평가 — 입력/상태(지상·달리기·공중·공중콤보)/방향 매칭, Priority 높은 그룹 우선 (구버전 TargetState/Priority 이식).</summary>
        public AttackAction ResolveStarter(AttackInputType inputType, Vector2 moveInput)
        {
            if (comboGroups == null || comboGroups.Count == 0) return null;
            var m = Movement;
            bool grounded = m == null || m.IsGrounded;
            bool running = grounded && _character != null && _character.StateManager != null
                && (_character.StateManager.CurrentStateType == CharacterStateType.Run
                    || _character.StateManager.PreviousStateType == CharacterStateType.Run);
            bool airCombo = !grounded && IsAirComboArmed;
            // 대시 키 + 공격 버튼 동시 입력(코드) — PlayerController가 판정해 CommandManager에 표시 (2026-09-04)
            bool dashChord = grounded && _character != null && _character.CommandManager != null
                && _character.CommandManager.RequestedAttackWithDash;

            ComboGroup best = null;
            for (int i = 0; i < comboGroups.Count; i++)
            {
                var g = comboGroups[i];
                if (g == null || !g.StarterEnabled || g.StarterInput != inputType) continue;
                switch (g.StarterState)
                {
                    case StarterStateCondition.Grounded: if (!grounded) continue; break;
                    case StarterStateCondition.Running: if (!running) continue; break;
                    case StarterStateCondition.Airborne: if (grounded) continue; break;
                    case StarterStateCondition.AirCombo: if (!airCombo) continue; break;
                    case StarterStateCondition.DashChord: if (!dashChord && !running) continue; break; // 달리기 중에도 허용
                }
                if (!MatchesDirection(g.StarterDirection, moveInput)) continue;
                if (!PassesBranchGate(g.StarterAttack)) continue; // 분기 게이트(예: 잡기 대상 없음) — 다음 우선순위 그룹으로
                if (best == null || g.StarterPriority > best.StarterPriority) best = g;
            }
            return best != null ? best.StarterAttack : null;
        }

        /// <summary>
        /// 방향 조건 매칭. 수평/수직 동시 입력 시 "마지막으로 누른 축"이 우선한다(2026-08-27 잡기 파생).
        /// preferHorizontal = CharacterCommandManager.PreferHorizontalDirection.
        /// </summary>
        private bool MatchesDirection(ComboDirectionCondition dir, Vector2 moveInput)
        {
            bool preferHorizontal = _character != null && _character.CommandManager != null
                && _character.CommandManager.PreferHorizontalDirection;
            bool horiz = Mathf.Abs(moveInput.x) >= 0.5f;
            bool vert = Mathf.Abs(moveInput.y) >= 0.5f;
            // Forward/Backward: 캐릭터가 보는 방향(FacingRight) 기준 — 앞 방향키 + 버튼 파생(잡기 등) (2026-09-07)
            float facingSign = (Movement == null || Movement.FacingRight) ? 1f : -1f;
            switch (dir)
            {
                case ComboDirectionCondition.Up: return moveInput.y >= 0.5f && (!horiz || !preferHorizontal);
                case ComboDirectionCondition.Down: return moveInput.y <= -0.5f && (!horiz || !preferHorizontal);
                case ComboDirectionCondition.Left: return moveInput.x <= -0.5f && (!vert || preferHorizontal);
                case ComboDirectionCondition.Right: return moveInput.x >= 0.5f && (!vert || preferHorizontal);
                case ComboDirectionCondition.Horizontal: return horiz && (!vert || preferHorizontal);
                case ComboDirectionCondition.Forward: return moveInput.x * facingSign >= 0.5f && (!vert || preferHorizontal);
                case ComboDirectionCondition.Backward: return moveInput.x * facingSign <= -0.5f && (!vert || preferHorizontal);
                default: return true;
            }
        }

        public void Bind(Character character) => _character = character;

        public void OnAwake()
        {
            _animator = _character != null ? _character.GetComponentInChildren<Animator>(true) : null;
            if ((attacks == null || attacks.Count == 0) && _character != null)
                attacks = new List<AttackAction>(_character.GetComponentsInChildren<AttackAction>(true));
            if ((comboGroups == null || comboGroups.Count == 0) && _character != null)
                comboGroups = new List<ComboGroup>(_character.GetComponentsInChildren<ComboGroup>(true));

            // 인스펙터 표시 전용 HP 미러링 (저장소는 Character 런타임).
            if (_character != null)
            {
                _character.OnHealthChanged += UpdateHPView;
                UpdateHPView(_character.CurrentHP, _character.MaxHP);
            }

            // 그로기형(투혼 파괴) 캐릭터는 스폰 시 투혼 가득 시작 — 공격 누적으로 깎여 0 도달 시 그로기.
            // (플레이어는 기존대로 0에서 충전 시작. 그로기 회복 시에는 touhonResetValue로 일부만 복구 — 구버전 ResetValue.)
            if (groggyOnTouhonDepleted && _character != null && _character.MaxTouhon > 0f)
                _character.SetTouhon(_character.MaxTouhon);
        }

        /// <summary>
        /// 이름으로 공격 실행. 비면 attacks[0] = 기본 공격. CharacterAttackState.Enter가 호출(상태 전이는 안 함).
        /// 기본 공격("")이고 콤보 유예(comboDropDelay) 안이면 1타 대신 예약된 다음 타부터 시작한다(구버전 이식).
        /// </summary>
        public void ExecuteAttack(string attackName = "")
            => ExecuteStartAttack(attackName, AttackInputType.Light, Vector2.zero);

        /// <summary>
        /// 시작 공격 실행. 이름이 비어 있으면 ComboGroup 시작 조건(입력/상태/방향/우선순위)으로 선택한다
        /// (구버전 TargetState 그룹 선택 이식). Light + 콤보 유예 중이면 예약된 다음 타부터 시작.
        /// </summary>
        public void ExecuteStartAttack(string attackName, AttackInputType inputType, Vector2 moveInput)
        {
            _lastStartInputType = inputType;
            if (!string.IsNullOrEmpty(attackName))
            {
                ClearComboGrace();
                ExecuteAttack(FindAttack(attackName));
                return;
            }
            // 콤보 유예: 직전 타 종료 후 comboDropDelay 내 재입력이면, 그 입력 종류로 직전 타의 Transition을 해석해 잇는다
            // (Light→다음 타, Heavy→파생기 등. 해당 입력 분기가 없으면 유예를 무시하고 시동기로 진행)
            if (_comboGraceFrom != null && Time.time <= _comboGraceUntil)
            {
                var graceNext = ResolveTransitionFrom(_comboGraceFrom, inputType);
                if (graceNext != null)
                {
                    ClearComboGrace();
                    ExecuteAttack(graceNext);
                    return;
                }
            }
            ClearComboGrace();
            var starter = ResolveStarter(inputType, moveInput);
            if (starter == null && inputType == AttackInputType.Light)
                starter = FindAttack(""); // 그룹 미구성 시 하위호환: attacks[0]
            ExecuteAttack(starter);
        }

        /// <summary>이름으로 AttackAction 조회. 비면 [0](기본 공격), 없으면 null.</summary>
        public AttackAction FindAttack(string attackName)
        {
            if (attacks == null || attacks.Count == 0) return null;
            if (string.IsNullOrEmpty(attackName)) return attacks[0];
            for (int i = 0; i < attacks.Count; i++)
                if (attacks[i] != null && attacks[i].AttackName == attackName) return attacks[i];
            return null;
        }

        /// <summary>AttackAction 1개 실행: 애니 재생 + VFX/Hitbox 시작. 데이터는 전부 action이 소유.</summary>
        public void ExecuteAttack(AttackAction action)
        {
            if (action == null)
            {
                Debug.LogWarning("[CharacterCombat] AttackAction 미할당/미발견 — 공격 실행 불가", _character);
                return;
            }

            // FP 게이트 (구버전 Weapon.FPCost 이식): 부족하면 공격을 시작하지 않는다.
            if (action.FPCost > 0f && _character != null && !_character.TryConsumeFP(action.FPCost))
            {
                FPInsufficient?.Invoke(action.FPCost);
                return;
            }

            
_currentAction = action;
            if (Movement != null) Movement.FacingLocked = action.LockFacing; // 공격 중 Facing 고정(벨트스크롤 기본)
            _attackActive = true;
            _attackSuperArmor = action.SuperArmor; // 공격 데이터 기반 슈퍼아머 ON (FinishAttack에서 해제)
            _attackTimer = 0f;
            _forwardMoveAnchor = -1f; // 이벤트 기준 전진 창 anchor 리셋
            _mainStateSeen = false;
            _transitionSeen = false;
            _windowEnterTimer = -1f;
            _attackStartedAirborne = Movement != null && !Movement.IsGrounded;

            // 잔류 이동 트리거 리셋 (2026-08-27): Attack→Move 전환이 쏜 ReturnMovement(및 DirRun Start) 트리거가
            // 소비되지 않은 채 남으면, 새 공격 애니 도달 직후 AnyState 전이가 발화해 공격 모션을 이동 애니로
            // 끌고 가고 Combat이 '공격 애니 이탈'로 오판해 즉시 종료된다(방향키 홀드 중 X→Y 씹힘 원인).
            ResetAnimTrigger(AnimParams.ReturnMovement);
            ResetAnimTrigger(AnimParams.DirRunForwardStart);
            ResetAnimTrigger(AnimParams.DirRunBackwardStart);
            // 잔류 점프 트리거 리셋 (2026-09-06): 점프 직후 공격 시 Jump 진입이 쏜 Jumping 트리거가 Animator 전이 중(로코모션/착지
            // CrossFade 등)이라 소비되지 못하고 armed로 남으면, 공격 CrossFade가 끝난 직후 AnyState→Jump가 발화해
            // 점프킥 모션이 점프 모션으로 덮인다(이펙트만 나오고 킥 모션은 없는 현상). 공격 애니가 정본이므로 여기서 제거.
            ResetAnimTrigger(AnimParams.Jumping);
            // 카운터 버프(퍼펙트 패링 보상): 공격 시작 = 버프 소모. 패링 후딜을 캔슬해 들어온 경우 같은 프레임에 armed로 남은
            // Parrying 트리거가 공격 CrossFade 뒤에 발화해 모션을 덮지 않도록 잔류 트리거도 제거한다 (2026-09-15).
            if (_character != null && _character.Defense != null)
                _character.Defense.ConsumeCounterBuffOnAttack();
            ResetAnimTrigger(AnimParams.Parrying);

            SetAnimInt(AnimParams.AttackStage, action.AttackStage);
            if (_animator != null && !string.IsNullOrEmpty(action.AnimationStateName))
                _animator.CrossFadeInFixedTime(action.AnimationStateName, action.CrossFadeDuration, 0);

            // 공격 배속 (구버전: AttackSpeed 스탯 0.7 × 클립 AnimatorSpeed 커브 근사)
            _pcNormalAnimSpeed = 1f;
            if (_animator != null)
            {
                float baseSpeed = _character != null && _character.Stats != null ? _character.Stats.FinalGlobalAnimatorSpeed : 1f;
                float atkSpeedMult = _character != null && _character.Stats != null ? _character.Stats.AttackSpeedMultiplier : 1f;
                _pcNormalAnimSpeed = baseSpeed * action.AnimationSpeedMultiplier * atkSpeedMult;
                _attackAnimSpeed = _pcNormalAnimSpeed; // 허주 공속 디버프 반영
                ApplyAttackAnimatorSpeed(action, 0f);  // 구간 배속(0초 시점) 포함해 실제 배속 적용
            }

            // 파워 차징 무장 (구버전 TryArmPowerChargeOnWeaponStarted 이식):
            // 공격 시작 순간 해당 입력 버튼이 눌려 있으면(홀드 의도) 차징 감시 시작. 탭이면 즉발 그대로.
            _pcArmed = false; _pcFrozen = false; _pcFxEngaged = false; _pcDone = false;
            _pcTimer = 0f; _pcBoostEndTime = -1f;
            if (action.UsePowerCharge && IsAttackInputHeld(_lastStartInputType))
            {
                _pcArmed = true;
                _pcInputType = _lastStartInputType;
            }

            // 급강하 공격(Air Dive): 공중에서 시작하면 공격자를 빠르게 낙하시킨다.
            // Attack 상태는 착지 체크가 없으므로 착지해도 Land로 끊기지 않고 이 공격이 끝까지 우선한다.
            // DiveHoverDuration > 0 이면 먼저 그 시간(모션 기준)만큼 공중에 멈춰 있다가(BeginHover) TickAttack에서 낙하한다 (2026-09-15).
            _divePending = false;
            _diveHoverRemaining = 0f;
            if (action.DiveFall && Movement != null && !Movement.IsGrounded)
            {
                if (action.DiveHoverDuration > 0f)
                {
                    _divePending = true;
                    _diveHoverRemaining = action.DiveHoverDuration;
                    Movement.BeginHover();
                }
                else
                {
                    Movement.ApplyDownwardDive(action.DiveFallSpeed);
                }
            }

            float power = _character != null && _character.Stats != null
                ? _character.Stats.FinalAttackPower * _character.Stats.DamageOutputMultiplier // 허주 등 출력 디버프 반영
                : 0f;
            action.Play(_character != null ? _character.gameObject : null, power);
            SyncTimelineSpeed(); // VFX/히트박스 타임라인을 현재 모션 배속에 맞춤 (허주 공속 디버프 등)
            AttackStarted?.Invoke(action);

            // 마그네틱: 전방 최근접 적을 내 축(깊이)에 정렬 + 적정 거리까지 당김 (TickAttack에서 진행)
            StartMagnetism(action);
        }

        // ─────────── 타임라인 배속 동기화 (2026-09-08) ───────────
        // AttackAction의 startDelay/hitboxDelay/hitboxDuration은 '튜닝 기준 배속'(전역 Animator 배속 × 공격 AnimationSpeedMultiplier)에서의
        // 초 단위다. 허주 공속 디버프(AttackSpeedMultiplier 0.7)·차징 정지/릴리즈 램프·패리 프레임 정지처럼 Animator 배속이 기준과 달라지면
        // 모션만 느려지고 타임라인은 실시간으로 진행돼 이펙트/판정이 스윙보다 먼저(윈드업 중) 터졌다
        // ("피격(투혼 0 → 허주) 후 공격 시 모션과 동시에 이펙트" 버그). 여기서 매 틱 비율을 넘겨 타임라인이 모션을 따라가게 한다.

        /// <summary>
        /// 타임라인 튜닝 기준 배속 — 전역 Animator 배속(FinalGlobalAnimatorSpeed)만. 공격별 Animation Speed Multiplier·디버프·차징 램프는 전부
        /// TimelineSpeed에 포함된다 → AttackAction의 시간 값(startDelay/hitboxDelay/MinDuration/전진 창 …)은 "배율 1일 때의 초"이며,
        /// 인스펙터에서 Animation Speed Multiplier를 바꾸면 모션과 이펙트/판정/전진이 같이 빨라진다 (2026-09-08, 기존 값은 배율만큼 일괄 보정함).
        /// </summary>
        private float TimelineBaseSpeed(AttackAction action)
        {
            float baseSpeed = _character != null && _character.Stats != null ? _character.Stats.FinalGlobalAnimatorSpeed : 1f;
            return Mathf.Max(0.01f, baseSpeed);
        }

        /// <summary>
        /// 구간 배속 적용 (2026-09-15): Animator.speed = _attackAnimSpeed(기본 배속·차징 램프) × AttackAction.SpeedSegments(motionTime).
        /// 공격 시작 직후와 TickAttack 매 틱(SyncTimelineSpeed 직전) 호출 — 그래야 TimelineSpeed가 구간 배율까지 포함해 이펙트/판정이 같이 빨라진다.
        /// </summary>
        private void ApplyAttackAnimatorSpeed(AttackAction action, float motionTime)
        {
            if (_animator == null || action == null) return;
            _animator.speed = _attackAnimSpeed * action.EvaluateSpeedSegment(motionTime);
        }

        /// <summary>현재 공격의 AttackAction.TimelineSpeed = 현재 Animator 배속 / 전역 기준 배속 (= AnimationSpeedMultiplier × 공속 배율 × 차징 램프 × 구간 배속). 공격 시작 직후와 TickAttack 매 틱 호출.</summary>
        private void SyncTimelineSpeed()
        {
            if (_currentAction == null) return;
            _currentAction.TimelineSpeed = _animator != null
                ? Mathf.Max(0f, _animator.speed) / TimelineBaseSpeed(_currentAction)
                : 1f;
        }

        // ─────────── Magnetism (공격 축 정렬 — 2026-08-28) ───────────
        // 데이터는 AttackAction(UseMagnetism 등), 대상 선정/타이밍은 여기, 실제 이동은 대상의
        // CharacterMovement.AddExternalDelta가 수행한다 (Forward Move와 동일한 소유 규칙).
        [System.NonSerialized] private Character _magnetTarget;
        [System.NonSerialized] private float _magnetElapsed;

        /// <summary>현재 마그네틱으로 끌려오는 대상 (없으면 null). 디버그/연출용.</summary>
        public Character MagnetTarget => _magnetTarget;

        /// <summary>공격 시작 시 마그네틱 대상 선정 — Facing 방향 벨트축 범위 내 최근접 적대 캐릭터 1명.</summary>
        private void StartMagnetism(AttackAction action)
        {
            _magnetTarget = null;
            _magnetElapsed = 0f;
            if (action == null || !action.UseMagnetism) return;
            var m = Movement;
            if (m == null || _character == null) return;

            Vector3 fwd = m.SplineForward * (m.FacingRight ? 1f : -1f);
            Vector3 depthAxis = m.SplineDepth;
            Vector3 myPos = _character.transform.position;

            Character best = null;
            float bestBelt = float.MaxValue;
            var all = Character.ActiveCharacters;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == _character || c.IsDead) continue;
                if (!AIController.IsHostile(_character, c)) continue;
                if (!IsMagnetizable(c)) continue;
                Vector3 to = c.transform.position - myPos;
                float belt = Vector3.Dot(to, fwd);
                if (belt <= 0f || belt > action.MagnetRange) continue;
                if (Mathf.Abs(Vector3.Dot(to, depthAxis)) > action.MagnetDepthRange) continue;
                if (belt < bestBelt) { bestBelt = belt; best = c; }
            }
            _magnetTarget = best;
        }

        /// <summary>마그네틱으로 끌 수 있는 상태인가 — 다운/체공/강제이동/등반 중인 대상은 제외.</summary>
        private static bool IsMagnetizable(Character c)
        {
            var sm = c != null ? c.StateManager : null;
            if (sm == null) return true;
            switch (sm.CurrentStateType)
            {
                case CharacterStateType.Knockdown:
                case CharacterStateType.Airborne:
                case CharacterStateType.Executed:
                case CharacterStateType.Dead:
                case CharacterStateType.LadderClimb:
                case CharacterStateType.LedgeHang:
                case CharacterStateType.Mantle:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// 마그네틱 진행 — MagnetDuration 동안 대상의 깊이를 내 깊이에 맞추고(축 정렬 = 일렬),
        /// 벨트축 거리는 MagnetStopDistance까지 당긴다(당기기만, 밀지 않음). 프레임레이트 무관 수렴.
        /// </summary>
        private void TickMagnetism(float deltaTime)
        {
            if (_magnetTarget == null || _currentAction == null || deltaTime <= 0f) return;
            float duration = Mathf.Max(0.01f, _currentAction.MagnetDuration);
            if (_magnetTarget.IsDead || !IsMagnetizable(_magnetTarget)) { _magnetTarget = null; return; }

            var m = Movement;
            var targetMove = _magnetTarget.Movement;
            if (m == null || targetMove == null || _character == null) { _magnetTarget = null; return; }

            // 남은 시간 대비 이번 틱 비율 — 마지막 틱에서 잔여 오차를 전부 소거(=duration 안에 정렬 완료)
            float remaining = duration - _magnetElapsed;
            float frac = remaining > 0f ? Mathf.Clamp01(deltaTime / remaining) : 1f;
            _magnetElapsed += deltaTime;

            Vector3 fwd = m.SplineForward * (m.FacingRight ? 1f : -1f);
            Vector3 depthAxis = m.SplineDepth;
            Vector3 to = _magnetTarget.transform.position - _character.transform.position;

            float depthErr = Vector3.Dot(to, depthAxis);                       // 깊이 어긋남 → 0으로
            float beltErr = Vector3.Dot(to, fwd) - _currentAction.MagnetStopDistance; // 초과 거리 → 0으로
            if (beltErr < 0f) beltErr = 0f;

            Vector3 correction = depthAxis * (-depthErr) + fwd * (-beltErr);
            if (correction.sqrMagnitude > 1e-8f)
                targetMove.AddExternalDelta(correction * frac);

            if (_magnetElapsed >= duration)
                _magnetTarget = null;
        }

        // ─────────── Combo (Transition 분기) ───────────

        /// <summary>
        /// 지금이 Combo Window인가 — Animator가 현재 공격의 ComboWindowTag(기본 NormalAttackTransition) 구간에 있을 때.
        /// Animator는 진행 상태를 알려줄 뿐, 어떤 Transition을 탈지/실행/종료 판단은 Combat/AttackState가 한다(§6).
        /// </summary>
        public bool IsInComboWindow
        {
            get
            {
                if (!_attackActive || _currentAction == null || _animator == null) return false;
                // 콤보 연결 직후 Animator가 아직 이전 타의 Transition 상태에 있을 수 있다.
                // 이번 타의 본 스윙 상태(MainStateTag)에 도달한 뒤에만 창을 연다 — 이전 타 잔상으로 인한 즉발 연쇄(타 스킵) 방지 (구버전 stale-guard 이식).
                if (!_mainStateSeen) return false;
                if (_attackTimer < _currentAction.MinDuration) return false;
                var current = _animator.GetCurrentAnimatorStateInfo(0);
                if (current.tagHash == _currentAction.ComboWindowTagHash) return true;
                if (_animator.IsInTransition(0)
                    && _animator.GetNextAnimatorStateInfo(0).tagHash == _currentAction.ComboWindowTagHash) return true;
                return false;
            }
        }

        /// <summary>현재 공격의 Legacy 단일 체인 다음 공격 이름 (표시용). 없으면 "".</summary>
        public string NextComboAttackName => _currentAction != null ? _currentAction.NextComboAttackName : "";

        /// <summary>
        /// 현재 공격의 Transition[] 중 입력/적중/지상 조건을 만족하는 첫 항목의 다음 공격을 반환 (우선순위 = 목록 순서).
        /// Transition이 비어 있으면 Legacy 단일 체인(NextComboAttackName + 히트 게이트)으로 폴백:
        ///   Light 입력 → 다음 타, 게이트 타(NextComboRequiresHit)가 빗나갔으면 첫 타(기본 공격)로 순환 (구버전 1→2→1→2…).
        /// 조건 불만족/다음 공격 없음 = null.
        /// </summary>
        private AttackAction ResolveTransition(AttackInputType inputType)
            => ResolveTransitionFrom(_currentAction, inputType);

        /// <summary>지정한 공격 기준으로 Transition 해석 — 콤보 유예(끝난 타)에서도 재사용 (2026-08-27).</summary>
        private AttackAction ResolveTransitionFrom(AttackAction from, AttackInputType inputType)
        {
            if (from == null) return null;

            var list = from.Transitions;
            if (list != null && list.Count > 0)
            {
                var m = Movement;
                bool grounded = m == null || m.IsGrounded;
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    if (t == null || t.nextAttack == null) continue;
                    if (t.input != inputType) continue;
                    if (t.requireHit && !from.HasHitThisUse) continue;
                    if (t.ground == ComboGroundCondition.Grounded && !grounded) continue;
                    if (t.ground == ComboGroundCondition.Airborne && grounded) continue;
                    if (!MatchesDirection(t.direction, _character != null && _character.CommandManager != null ? _character.CommandManager.MoveInput : Vector2.zero)) continue;
                    if (!PassesBranchGate(t.nextAttack)) continue; // 예: 잡기 대상 없음 → 잡기 분기 건너뜀(헛스윙 방지)
                    return t.nextAttack;
                }
                return null;
            }

            // Legacy 폴백 (Transition 미설정 데이터 호환 — §21)
            if (inputType != AttackInputType.Light) return null;
            string next = from.NextComboAttackName;
            if (string.IsNullOrEmpty(next)) return null;
            var nextAction = FindAttack(next);
            if (nextAction == null) return null;
            if (from.NextComboRequiresHit && !from.HasHitThisUse)
                return FindAttack(""); // 빗맞음 → 1타 순환 (구버전: 1→2→1→2…)
            return nextAction;
        }

        /// <summary>
        /// 분기 게이트 — AttackAction 프리팹에 IComboBranchGate 구현 컴포넌트(예: GrabAttackLink)가 있으면 CanBranch를 묻는다.
        /// false면 Transition/시동기 해석에서 그 후보를 건너뛴다 (구버전 IComboBranchGate 이관, 2026-09-08). 게이트 없음 = 통과.
        /// </summary>
        private bool PassesBranchGate(AttackAction next)
        {
            if (next == null) return true; // null 처리는 호출부 기존 로직에 맡긴다
            var gate = next.GetComponent<IComboBranchGate>();
            return gate == null || gate.CanBranch(_character);
        }

        /// <summary>이 입력으로 갈 수 있는 분기가 현재 공격에 존재하는가 (Combo Window 여부와 무관 — 예약 필터용).</summary>
        public bool HasBranchForInput(AttackInputType inputType)
            => _attackActive && ResolveTransition(inputType) != null;

        /// <summary>
        /// 이 입력과 매칭되는 Transition이 목록에 존재하는가 — 느슨한 검사(선입력 예약 필터용, 2026-08-27).
        /// requireHit/지상/방향 등 시점 의존 조건은 무시하고 input 매칭만 본다. 실제 조건 검증은 실행 시점(TryContinueCombo)에 한다.
        /// Transition이 비어 있으면 Legacy 단일 체인(Light + NextComboAttackName)으로 폴백.
        /// </summary>
        public bool HasAnyBranchForInput(AttackInputType inputType)
        {
            if (!_attackActive || _currentAction == null) return false;
            var list = _currentAction.Transitions;
            if (list != null && list.Count > 0)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    if (t != null && t.nextAttack != null && t.input == inputType) return true;
                }
                return false;
            }
            // Legacy 폴백 (§21)
            return inputType == AttackInputType.Light && !string.IsNullOrEmpty(_currentAction.NextComboAttackName);
        }

        /// <summary>지금 다음 콤보 타로 연결 가능한가: 공격 중 + 해당 입력 분기 존재 + Combo Window.</summary>
        public bool CanContinueCombo(AttackInputType inputType)
        {
            if (!_attackActive || _currentAction == null) return false;
            if (ResolveTransition(inputType) == null) return false;
            return IsInComboWindow;
        }

        /// <summary>
        /// 해당 입력의 Transition을 따라 다음 공격 실행. CanContinueCombo가 참일 때만 동작.
        /// 이전 AttackAction을 정리(Hitbox OFF)하고 다음 AttackAction을 새 공격으로 실행한다.
        /// CharacterAttackState.Tick이 호출(상태 전이는 없음 — Attack 상태 유지, 다른 ComboGroup으로도 이동 가능).
        /// </summary>
        public bool TryContinueCombo(AttackInputType inputType)
        {
            if (!CanContinueCombo(inputType)) return false;
            var next = ResolveTransition(inputType);
            // FP 부족이면 콤보를 잇지 않는다 (이전 타 유지 — 자연 종료로 흐름 유지)
            if (next != null && next.FPCost > 0f && _character != null && !_character.HasEnoughFP(next.FPCost))
            {
                FPInsufficient?.Invoke(next.FPCost);
                return false;
            }
            _currentAction.Stop();
            _lastStartInputType = inputType;
            ExecuteAttack(next);
            return true;
        }

        /// <summary>Light 입력 기준 하위호환 오버로드.</summary>
        public bool TryContinueCombo() => TryContinueCombo(AttackInputType.Light);


        // ─────────── 콤보 유예 (구버전 DropComboDelay 0.3s) ───────────

        private void CaptureComboGrace()
        {
            // 끝난 타 자체를 기억 — 어떤 입력으로 이을지는 재입력 시점에 ResolveTransitionFrom으로 해석 (입력 인지형, 2026-08-27)
            bool hasAnyNext = _currentAction != null
                && ((_currentAction.Transitions != null && _currentAction.Transitions.Count > 0)
                    || !string.IsNullOrEmpty(_currentAction.NextComboAttackName));
            if (hasAnyNext && comboDropDelay > 0f)
            {
                _comboGraceFrom = _currentAction;
                _comboGraceUntil = Time.time + comboDropDelay;
            }
            else ClearComboGrace();
        }

        private void ClearComboGrace()
        {
            _comboGraceFrom = null;
            _comboGraceUntil = -1f;
        }

        public void TickAttack(float deltaTime)
        {
            if (!_attackActive || _currentAction == null) return;
            TickPowerCharge();
            ApplyAttackAnimatorSpeed(_currentAction, _attackTimer); // 구간 배속 (2026-09-15) — 램프 결과 × 이 시점의 구간 배율
            SyncTimelineSpeed(); // 차징 램프/디버프/구간 배속으로 바뀐 Animator 배속을 타임라인에 반영
            if (_pcFrozen)
            {
                // 차징 정지 중 — 공격 타이머/전진/종료 판정 전부 일시정지 (maxDuration 조기 타임아웃 방지).
                // 전진 창(ForwardMove) 도중 정지에 들어가면 직전 틱의 전진 속도가 Movement에 남아 홀드 내내 전진하므로 반드시 0으로 끊는다.
                // 타이머가 멈춰 있어 릴리즈 후에는 남은 창만큼 정상 전진한다.
                Movement?.SetAttackMotionSpeed(0f);
                return;
            }
            // 공격 타이머는 '모션 시간'으로 진행 (2026-09-08): MinDuration/MaxDuration/전진 창/MoveCancelDelay는 모션 기준으로 튜닝된 값이므로
            // 허주 공속 디버프·차징 램프로 Animator 배속이 바뀌면 같은 비율(TimelineSpeed)로 따라간다 — VFX/히트박스 타임라인과 동일 규칙.
            float motionDelta = deltaTime * _currentAction.TimelineSpeed;
            _attackTimer += motionDelta;

            // 급강하 체공 → 낙하 (2026-09-15): 체공 시간이 끝나면(또는 체공 중 착지/외부 요인으로 체공이 풀리면) 급강하 시작.
            if (_divePending)
            {
                _diveHoverRemaining -= motionDelta;
                var mv = Movement;
                if (mv == null || mv.IsGrounded) { _divePending = false; }
                else if (_diveHoverRemaining <= 0f)
                {
                    _divePending = false;
                    mv.EndHover();
                    mv.ApplyDownwardDive(_currentAction.DiveFallSpeed);
                }
            }

            // 마그네틱 진행 (대상 축 정렬 — 히트박스 ON 전에 정렬이 끝나도록 짧은 duration 권장)
            TickMagnetism(deltaTime);

            // 공중콤보 Arm: 시동기(올려치기) 적중 시 일정 시간 동안 공중 AirCombo 시작 허용 (구버전 CharacterAirCombo.Arm 이식)
            if (_currentAction.IsAirComboLauncher && _currentAction.HasHitThisUse)
                _airComboArmedUntil = Time.time + airComboArmDuration;

            // 이번 타의 본 스윙/Transition 상태 도달 추적 (Combo Window 유효화 + 종료 판정)
            bool leftTransition = false;
            if (_animator != null)
            {
                var cur = _animator.GetCurrentAnimatorStateInfo(0);
                if (!_mainStateSeen && cur.tagHash == _currentAction.MainStateTagHash && !_animator.IsInTransition(0))
                    _mainStateSeen = true;
                if (_mainStateSeen && cur.tagHash == _currentAction.ComboWindowTagHash)
                {
                    if (!_transitionSeen) _windowEnterTimer = _attackTimer; // 후딜 진입 시각 기록 (이동 캔슬 기준점)
                    _transitionSeen = true;
                }
                leftTransition = _transitionSeen && cur.tagHash != _currentAction.ComboWindowTagHash && !_animator.IsInTransition(0);
                // Transition 상태가 exit 없이 루프/체류하는 Animator 구조 대응: 완주(0.95, 구버전 exitTime과 동일)도 종료로 간주
                if (_transitionSeen && cur.tagHash == _currentAction.ComboWindowTagHash && cur.normalizedTime >= 0.95f)
                    leftTransition = true;
            }

            // 공격 전진 창 (구버전 AnimationMoveForward 커브 이식) — 판단은 여기, 이동은 CharacterMovement
            var movement = Movement;
            if (movement != null)
            {
                // 프레임레이트 무관 구간 적분: 이번 틱 [prev, now]이 전진 창과 겹치는 비율만큼 속도 적용
                // (저프레임에서 0.05s 창이 틱 사이로 건너뛰어지는 문제 방지 — 60fps 동작 동일)
                float prevTimer = _attackTimer - motionDelta;
                // ForwardMoveEventName 지정 공격은 이벤트 수신 시점을 창의 기준(anchor)으로 삼는다
                // (홀드/차지형 공격처럼 본동작 시작이 가변인 경우 — 2026-08-28 손대포 회전 베기 이관)
                bool eventAnchored = !string.IsNullOrEmpty(_currentAction.ForwardMoveEventName);
                if (eventAnchored && _forwardMoveAnchor < 0f && _currentAction.ForwardMoveEventReceived)
                    _forwardMoveAnchor = prevTimer;
                float moveSpeed = 0f;
                if (!eventAnchored || _forwardMoveAnchor >= 0f)
                {
                    float anchor = eventAnchored ? _forwardMoveAnchor : 0f;
                    float winStart = anchor + _currentAction.ForwardMoveStart;
                    float winEnd = winStart + _currentAction.ForwardMoveDuration;
                    float overlap = Mathf.Min(_attackTimer, winEnd) - Mathf.Max(prevTimer, winStart);
                    // 전진 속도도 모션 배속을 곱한다 — 창이 실시간으로 길어진 만큼 느리게 밀어 총 전진 거리는 배속과 무관하게 동일.
                    if (Mathf.Abs(_currentAction.ForwardMoveSpeed) > 0f && overlap > 0f && motionDelta > 0f)
                        moveSpeed = _currentAction.ForwardMoveSpeed * _currentAction.TimelineSpeed * Mathf.Clamp01(overlap / motionDelta);
                }
                movement.SetAttackMotionSpeed(moveSpeed);
            }

            bool timeout = _attackTimer >= _currentAction.MaxDuration;
            bool animDone = _attackTimer >= _currentAction.MinDuration && (!IsInAttackAnimation() || leftTransition);
            if (timeout || animDone)
            {
                // 자연 종료: 다음 타가 있으면 comboDropDelay 동안 유예 예약 (구버전 DropComboDelay)
                CaptureComboGrace();
                FinishAttack();
            }
        }

        // ─────────── Power Charge (홀드=기 모으기 — 구버전 파워 차징 v2 이식, 2026-08-29) ───────────
        // 데이터(시간/배율/이펙트)는 AttackAction이 소유, 흐름(타이머/정지/릴리즈)은 여기, 버튼 상태는 PlayerController가 전달.

        [System.NonSerialized] private AttackInputType _lastStartInputType = AttackInputType.Light;
        [System.NonSerialized] private AttackInputType _pcInputType;
        [System.NonSerialized] private bool _pcArmed;      // 차징 감시 중 (공격 시작 시 버튼 홀드였음)
        [System.NonSerialized] private bool _pcFrozen;     // 윈드업 정지(기 모으기) 중
        [System.NonSerialized] private bool _pcFxEngaged;  // 최소 홀드 충족 = 차징 커밋(슬로우모션·강화 확정)
        [System.NonSerialized] private bool _pcDone;       // 이번 공격에서 차징 해소 완료
        [System.NonSerialized] private float _pcTimer;     // 공격 시작 기준 경과(실시간)
        [System.NonSerialized] private float _pcNormalAnimSpeed = 1f;
        [System.NonSerialized] private float _pcBoostEndTime = -1f; // 릴리즈 가속 종료 시각(unscaled), -1=비활성
        [System.NonSerialized] private bool _lightHeld, _heavyHeld, _skill1Held, _skill2Held;

        // 차징 카메라 줌 홀드 핸들(정지 시작~릴리즈까지 유지). CameraZoomService.BeginHold가 반환.
        [System.NonSerialized] private ICameraZoomHold _pcZoomHold;
        // Animator 배속 스무스 램프(정지 진입/릴리즈 시 즉시 스냅 대신 러프 커브로 보간) — 전부 unscaled 기준.
        [System.NonSerialized] private bool _pcSpeedRamping;
        [System.NonSerialized] private float _pcSpeedFrom;
        [System.NonSerialized] private float _pcSpeedTo;
        [System.NonSerialized] private float _pcSpeedRampDuration;
        [System.NonSerialized] private float _pcSpeedRampElapsed;
        // 구간 배속(AttackAction.SpeedSegments) 적용 전 Animator 배속 (2026-09-15). 램프/시작 배속은 전부 이 값에 쓰고,
        // ApplyAttackAnimatorSpeed가 매 틱 (이 값 × 구간 배율)을 실제 Animator.speed에 넣는다.
        [System.NonSerialized] private float _attackAnimSpeed = 1f;

        /// <summary>차징(윈드업 정지) 중인가. 연출/AI 참고용.</summary>
        public bool IsPowerCharging => _pcFrozen;

        /// <summary>
        /// 홀드 차징 진행도 0~1 (연출용, 읽기 전용). 윈드업 정지(홀드)가 시작될 때 0에서 시작해
        /// 차징 커밋(ChargeMinHoldTime)에서 1이 되고, 커밋 이후 홀드 유지 중에는 1을 유지한다.
        /// 차징 중이 아니거나 정지 전(탭 구간)에는 0.
        /// </summary>
        public float PowerChargeProgress
        {
            get
            {
                if (!_pcFrozen || _pcDone || _currentAction == null) return 0f;
                if (_pcFxEngaged) return 1f;
                float span = _currentAction.ChargeMinHoldTime - _currentAction.ChargeFreezeTime;
                if (span <= 0f) return 1f;
                return Mathf.Clamp01((_pcTimer - _currentAction.ChargeFreezeTime) / span);
            }
        }

        /// <summary>공격 버튼의 눌림 상태 전달 — PlayerController가 매 프레임 호출 (홀드 차징 판정용).</summary>
        public void SetAttackInputHeld(AttackInputType inputType, bool held)
        {
            switch (inputType)
            {
                case AttackInputType.Light: _lightHeld = held; break;
                case AttackInputType.Heavy:
                case AttackInputType.HeavyHold: _heavyHeld = held; break; // 같은 버튼(Y)
                case AttackInputType.Skill1: _skill1Held = held; break;
                case AttackInputType.Skill2: _skill2Held = held; break;
            }
        }

        /// <summary>해당 입력 종류의 버튼이 현재 눌려 있는가.</summary>
        public bool IsAttackInputHeld(AttackInputType inputType)
        {
            switch (inputType)
            {
                case AttackInputType.Light: return _lightHeld;
                case AttackInputType.Heavy:
                case AttackInputType.HeavyHold: return _heavyHeld;
                case AttackInputType.Skill1: return _skill1Held;
                case AttackInputType.Skill2: return _skill2Held;
                default: return false;
            }
        }

        /// <summary>
        /// 홀드 차징 진행 (TickAttack 첫머리에서 호출, 타이머는 unscaled — 슬로우모션 영향 없음):
        ///   탭(freezeTime 내 릴리즈) → 아무 연출 없이 일반 공격 그대로
        ///   freezeTime 도달 → 윈드업 정지(모션 chargeHoldAnimSpeed 배속) + 타임라인 일시정지
        ///   minHoldTime 도달 → 차징 커밋: 슬로우모션 시작, 이후 릴리즈(또는 max 초과) 시 완충 강화 발동
        ///   minHoldTime 미달 릴리즈 → 강화 없이 모션만 재개
        /// </summary>
        private void TickPowerCharge()
        {
            // Animator 배속 스무스 램프 진행 (정지 진입/릴리즈 보간) — armed/done 여부와 무관하게 매 프레임 갱신.
            TickAnimSpeedRamp();

            // 릴리즈 가속 종료 (타격 프레임 도달 후 정상 속도 복귀) — 즉시 스냅 대신 짧은 램프로 복귀.
            if (_pcBoostEndTime >= 0f && Time.unscaledTime >= _pcBoostEndTime)
            {
                _pcBoostEndTime = -1f;
                float backEase = _currentAction != null ? _currentAction.ChargeReleaseEaseTime : 0f;
                BeginAnimSpeedRamp(_pcNormalAnimSpeed, backEase);
            }

            if (!_pcArmed || _pcDone || _currentAction == null) return;
            var action = _currentAction;
            bool held = IsAttackInputHeld(_pcInputType);
            _pcTimer += Time.unscaledDeltaTime;

            if (!_pcFrozen)
            {
                if (!held) { _pcArmed = false; return; } // 탭 — 연출 없이 일반 공격
                if (_pcTimer >= action.ChargeFreezeTime) EngageChargeFreeze(action);
                return;
            }

            if (!_pcFxEngaged)
            {
                if (!held) { ResolveCharge(action, 0f); return; } // 최소 홀드 미달 — 강화 없이 재개
                if (_pcTimer >= action.ChargeMinHoldTime) _pcFxEngaged = true; // 차징 커밋
                return;
            }

            // 커밋됨 — 슬로우모션 유지 (매 프레임 짧게 갱신, 릴리즈 후 easeOut 자연 복원. 손대포 홀드와 동일 패턴)
            if (action.ChargeTimeScale < 1f)
                Aiara.FeedbackTime.SlowMotion(action.ChargeTimeScale, 0.05f, 0.02f, 0.1f);

            if (!held || _pcTimer >= action.ChargeMinHoldTime + action.ChargeMaxHoldTime)
                ResolveCharge(action, 1f); // 커밋 = 완충 취급 (구버전과 동일)
        }

        /// <summary>윈드업 정지 시작 — 모션을 거의 멈추고(기 모으기) VFX/히트박스 타임라인도 함께 일시정지.
        /// 배속은 즉시 떨구지 않고 ChargeFreezeEaseTime 동안 러프 커브로 서서히 느려진다. 카메라 줌인도 이때 시작.</summary>
        private void EngageChargeFreeze(AttackAction action)
        {
            _pcFrozen = true;
            action.TimelinePaused = true;
            // 모션 배속 normal → normal×ChargeHoldAnimSpeed 로 서서히 감속 (구버전은 즉시 스냅)
            BeginAnimSpeedRamp(_pcNormalAnimSpeed * action.ChargeHoldAnimSpeed, action.ChargeFreezeEaseTime);
            // 차징 카메라 줌인 시작 — 정지 시점부터 홀드, 릴리즈 시 원복
            if (action.ChargeCameraZoom && _pcZoomHold == null)
                _pcZoomHold = CameraZoomService.BeginHold(
                    action.ChargeCameraZoomFovDelta, action.ChargeCameraZoomEaseIn, action.ChargeCameraZoomEaseOut);
        }

        /// <summary>정지 해제(모션 재개). ratio > 0 이면 데미지/이펙트 강화 + 릴리즈 가속 적용.
        /// 배속은 즉시 튀지 않고 ChargeReleaseEaseTime 동안 러프 커브로 가속하며, 카메라 줌도 여기서 원복시킨다.</summary>
        private void ResolveCharge(AttackAction action, float ratio)
        {
            _pcDone = true;
            _pcArmed = false;
            _pcFrozen = false;
            _pcFxEngaged = false;
            action.TimelinePaused = false;

            if (ratio > 0f) action.SetChargeRelease(ratio); // 히트박스 열리기 전 = 데미지/이펙트 배율 반영 시점

            // 차징 줌 해제 (릴리즈 시 easeOut으로 기본 FOV 복귀)
            ReleaseChargeZoom();

            float releaseSpeed = (ratio > 0f && action.ChargeReleaseAnimSpeed > 1.001f && action.ChargeReleaseBoostDuration > 0f)
                ? action.ChargeReleaseAnimSpeed : 1f;
            float easeTime = action.ChargeReleaseEaseTime;
            // 정지 배속 → 릴리즈/정상 배속으로 서서히 가속 (구버전은 즉시 스냅 = "빡 튐")
            BeginAnimSpeedRamp(_pcNormalAnimSpeed * releaseSpeed, easeTime);
            // 부스트가 있으면 램프-인 시간까지 포함해 유지 후 정상 복귀
            _pcBoostEndTime = releaseSpeed > 1f ? Time.unscaledTime + easeTime + action.ChargeReleaseBoostDuration : -1f;
        }

        /// <summary>Animator 배속 스무스 램프 시작. duration ≤ 0 이면 즉시 목표값으로 스냅(구버전 동작).</summary>
        private void BeginAnimSpeedRamp(float target, float duration)
        {
            if (_animator == null) return;
            if (duration <= 0f)
            {
                _attackAnimSpeed = target; // 실제 Animator.speed는 ApplyAttackAnimatorSpeed(구간 배속 포함)가 매 틱 적용
                _pcSpeedRamping = false;
                return;
            }
            _pcSpeedFrom = _attackAnimSpeed;
            _pcSpeedTo = target;
            _pcSpeedRampDuration = duration;
            _pcSpeedRampElapsed = 0f;
            _pcSpeedRamping = true;
        }

        /// <summary>진행 중인 배속 램프를 unscaled 기준으로 러프(스무스) 보간(_attackAnimSpeed). 완료 시 목표값으로 확정. 실제 Animator 적용은 ApplyAttackAnimatorSpeed.</summary>
        private void TickAnimSpeedRamp()
        {
            if (!_pcSpeedRamping || _animator == null) return;
            _pcSpeedRampElapsed += Time.unscaledDeltaTime;
            float t = _pcSpeedRampDuration > 0f ? Mathf.Clamp01(_pcSpeedRampElapsed / _pcSpeedRampDuration) : 1f;
            _attackAnimSpeed = Mathf.Lerp(_pcSpeedFrom, _pcSpeedTo, Mathf.SmoothStep(0f, 1f, t));
            if (t >= 1f) { _attackAnimSpeed = _pcSpeedTo; _pcSpeedRamping = false; }
        }

        /// <summary>차징 카메라 줌 홀드를 해제(easeOut 원복)하고 핸들을 비운다. 중복 호출 안전.</summary>
        private void ReleaseChargeZoom()
        {
            if (_pcZoomHold != null)
            {
                _pcZoomHold.Release();
                _pcZoomHold = null;
            }
        }

        /// <summary>차징 상태 정리 — 공격 종료/취소/피격 공통 (FinishAttack에서 호출).</summary>
        private void CleanupPowerCharge()
        {
            if (_currentAction != null) _currentAction.TimelinePaused = false;
            ReleaseChargeZoom(); // 정지 중 취소/피격으로 끊겨도 줌이 남지 않도록 원복
            _pcArmed = false;
            _pcFrozen = false;
            _pcFxEngaged = false;
            _pcDone = false;
            _pcBoostEndTime = -1f;
            _pcSpeedRamping = false; // FinishAttack이 배속을 전역값으로 복원하므로 램프 중단
            // 슬로우모션은 FeedbackTime 짧은 duration + easeOut으로 자연 복원 (별도 해제 불필요)
        }

        private void FinishAttack()
        {
            CleanupPowerCharge();
            _attackActive = false;
            _attackStartedAirborne = false;
            if (_divePending) { _divePending = false; Movement?.EndHover(); } // 체공 중 취소/종료 → 중력 복귀
            _magnetTarget = null; // 마그네틱 중단 (공격 종료/취소 공통)
            _attackSuperArmor = false; // 공격 종료 → 공격 기반 슈퍼아머 해제
            if (_currentAction != null)
            {
                _currentAction.Stop();
                _currentAction = null;
            }
            SetAnimInt(AnimParams.AttackStage, 0);

            // 공격 배속/전진 복원
            if (_animator != null)
                _animator.speed = _character != null && _character.Stats != null ? _character.Stats.FinalGlobalAnimatorSpeed : 1f;
            var movement = Movement;
            if (movement != null) movement.ClearAttackMotion();
            if (movement != null) movement.FacingLocked = false; // 공격 종료 → Facing 잠금 해제
        }

        public void StopAttack()
        {
            if (_attackActive)
                FinishAttack();
        }

        // ─────────── 후딜 이동 캔슬 (2026-09-06) ───────────
        // 원칙: 입력 → Character State → Animation. 후딜(ComboWindow) 재생 중이라도 AttackAction.MoveCancelDelay 시점이
        // 지났고 이동 입력이 있으면 CharacterAttackState가 공격을 끝내고 Move/Run으로 전이한다(애니는 StateManager가 따라감).
        // 판정 데이터는 AttackAction, 시점 계산은 여기, 전이 결정은 CharacterAttackState — 기존 책임 분리 유지.

        /// <summary>
        /// 지금 이동 입력으로 후딜을 끊고 Move/Run으로 나갈 수 있는가.
        /// 조건: 공격 중 + 본 스윙 종료 후 ComboWindow(후딜) 도달 + MinDuration 경과 + 후딜 진입 후 MoveCancelDelay 경과 + 차징 정지 중 아님.
        /// MoveCancelDelay &lt; 0 이면 항상 false(끝까지 재생 — 자폭/잡기 등).
        /// </summary>
        public bool CanMoveCancel
        {
            get
            {
                if (!_attackActive || _currentAction == null) return false;
                float delay = _currentAction.MoveCancelDelay;
                if (delay < 0f) return false;
                if (_pcFrozen) return false;                       // 차징 정지 중엔 릴리즈/취소가 먼저
                if (!_transitionSeen || _windowEnterTimer < 0f) return false; // 본 스윙 중엔 불가 (후딜 태그 도달 후)
                if (_attackTimer < _currentAction.MinDuration) return false;
                return _attackTimer - _windowEnterTimer >= delay;
            }
        }

        /// <summary>
        /// 이동 캔슬로 공격 종료 — 자연 종료와 동일하게 콤보 유예(comboDropDelay)를 남기고 정리한다.
        /// (유예 덕에 '방향키 홀드 + 연타' 콤보는 후딜을 건너뛰어도 다음 타로 이어진다.) CharacterAttackState가 호출.
        /// </summary>
        public void EndAttackForMoveCancel()
        {
            if (!_attackActive) return;
            CaptureComboGrace();
            FinishAttack();
        }

        /// <summary>
        /// 착지로 공중 시작 공격 종료 (2026-09-06) — 접지 순간 CharacterAttackState가 Land 전이 직전 호출.
        /// 콤보 유예는 남기지 않는다(공중 공격 → 지상 콤보 연결 없음). 공격 이펙트/히트박스는 FinishAttack→AttackAction.Stop이 즉시 정리.
        /// </summary>
        public void EndAttackForLanding()
        {
            if (!_attackActive) return;
            ClearComboGrace();
            FinishAttack();
        }

        private bool IsInAttackAnimation()
        {
            if (_animator == null || _currentAction == null) return false;
            int mainTag = _currentAction.MainStateTagHash;
            int windowTag = _currentAction.ComboWindowTagHash;
            var current = _animator.GetCurrentAnimatorStateInfo(0);
            if (current.tagHash == mainTag || current.tagHash == windowTag) return true;
            if (_animator.IsInTransition(0))
            {
                var next = _animator.GetNextAnimatorStateInfo(0);
                if (next.tagHash == mainTag || next.tagHash == windowTag) return true;
            }
            return false;
        }

        private bool HasAnimParam(string paramName)
        {
            if (_animator == null || string.IsNullOrEmpty(paramName)) return false;
            foreach (var p in _animator.parameters)
                if (p.name == paramName) return true;
            return false;
        }

        private void SetAnimInt(string paramName, int value) { if (HasAnimParam(paramName)) _animator.SetInteger(paramName, value); }

        // ─────────── 받는 전투 (피격) ───────────

        [Tooltip("피격 반응 디버그 로그([Hit]) 출력 여부")]
        [SerializeField] private bool debugHitReactions = false;
        [System.NonSerialized] private bool _superArmor;
        /// <summary>슈퍼아머 — true면 피격해도 경직(Hit)/공격취소 없이 데미지만 받는다(잠재능력 나찰 등).</summary>
        public bool SuperArmorActive { get => _superArmor; set => _superArmor = value; }
        [System.NonSerialized] private bool _attackSuperArmor;
        /// <summary>현재 공격(AttackAction.SuperArmor) 기반 슈퍼아머. ExecuteAttack에서 세팅, FinishAttack에서 해제.</summary>
        public bool AttackSuperArmorActive => _attackSuperArmor;
        /// <summary>어떤 소스든 슈퍼아머가 활성인가 (잠재능력 or 현재 공격).</summary>
        private bool AnySuperArmor => _superArmor || _attackSuperArmor;

        /// <summary>진행 중 공격을 안전 종료(Hitbox OFF · AttackAction 정리 · AttackStage 리셋 · 콤보 유예 파기).</summary>
        public void CancelCurrentAttack()
        {
            ClearComboGrace(); // 피격 등 강제 취소 시 콤보 이어가기 무효 (구버전 ForceCancel과 동일)
            StopAttack();
        }

        /// <summary>
        /// Character 피격 진입점(§2·§48). Hurtbox → Character(IDamageReceiver) → 여기로 위임된다.
        /// 데미지는 이미 DamageInfo.Damage에 계산돼 들어오므로 여기서 다시 곱하지 않는다(§20).
        /// HP 저장소는 Combat이 아니라 Character 런타임(CharacterRuntimeStats.CurrentHP)이다(§4).
        /// </summary>
public void ReceiveDamage(DamageInfo damage)
        {
            if (_character == null) return;
            if (_invincible) return; // 처형 시전 중 등 완전 무적
            
if (_character.CurrentHP <= 0f) return;

            // 넉다운/에어본(다운) 중에는 일반 재타격을 무시한다 — 구프로젝트 CollapseInvulnerable 이관(넉다운 락 방지).
            // 단, "죽는 타격"(이번 데미지로 HP가 0 이하가 되는 경우)은 무시하지 않고 통과시켜 반드시 사망 처리한다.
            // (그렇지 않으면 저HP 상태로 다운되었을 때 치명타가 계속 무시되어 죽지도, 사망 UI가 뜨지도 않는다.)
            var reactionState = _character.StateManager != null ? _character.StateManager.CurrentStateType : CharacterStateType.Idle;

            // ── 그로기/처형 중 피격 (구버전 GuardableHealth wasGroggy 분기 이관) ──
            // Executed(처형 확정 연출) 중에는 피격을 완전 무시 — 처형 종료 시 KillByExecution이 사망을 처리한다.
            if (reactionState == CharacterStateType.Executed) return;
            // 그로기 중: 가드/패리 전부 무시(무방비) + 풀 데미지 + 추가 투혼 감소 없음 +
            // 피격 리액션/넉백/에어본 상태 전이 없음(그로기 유지). 죽는 타격만 Dead로 전이한다.
            if (reactionState == CharacterStateType.Groggy)
            {
                _character.ApplyDamage(damage.Damage);
                HitReceived?.Invoke(damage);
                HitLanded?.Invoke(damage);
                if (_character.CurrentHP <= 0f)
                {
                    _groggyActive = false;
                    _pendingGroggy = false;
                    _touhonResetTimer = 0f;
                    PlayDeathFeedback();
                    if (_character.StateManager != null)
                        _character.StateManager.ChangeState(CharacterStateType.Dead);
                }
                else
                {
                    PlayHitFeedback(damage);
                }
                return;
            }

            // 저글: 에어본 중이라도 아직 체공(Rising) 구간이면 다운 무시 블록을 건너뛰어
            // 아래 EnterHitReactionState(airborneNow 분기)로 재런치/체공 유지를 허용한다.
            // 착지 후 누움(Collapse)/기상(Getup) 구간은 기존대로 재런치를 막는다(넉다운 락 방지).
            bool airborneRising = reactionState == CharacterStateType.Airborne
                && _character.StateManager != null && _character.StateManager.IsAirborneRising;
            if ((reactionState == CharacterStateType.Knockdown || reactionState == CharacterStateType.Airborne) && !airborneRising)
            {
                // 다운(넉다운/에어본) 중에는 재런치/재경직(저글 락)만 막고, 데미지 자체는 정상 적용한다.
                // 이렇게 해야 다운 상태에서 맞아도 HP가 깎여 결국 사망(→ 사망 UI)에 도달한다.
                // (구 CollapseInvulnerable을 "완전 무적"이 아니라 "재런치 무적"으로 완화.)
                bool downSuperArmor = AnySuperArmor;
                if (!downSuperArmor) CancelCurrentAttack();
                _character.ApplyDamage(damage.Damage);
                DrainTouhon(touhonDrainOnHit);
                HitReceived?.Invoke(damage);
                HitLanded?.Invoke(damage);
                if (_character.CurrentHP <= 0f)
                {
                    PlayDeathFeedback();
                    if (_character.StateManager != null)
                        _character.StateManager.ChangeState(CharacterStateType.Dead);
                }
                else
                {
                    // 다운 유지(재런치/넉백/Hit 재진입 없음) — 저글 락은 계속 방지.
                    PlayHitFeedback(damage);
                    // OTG(AttackAction.hitDownedTargets): 다운 중 타격 허용 공격이면 누운 채 전용 피격 반응.
                    // 상태는 유지하고(재런치 없음) 누움 타이머만 리셋 + 다운 포즈 클립을 처음부터 재생해 '움찔'을 보여준다.
                    if (damage.HitsDowned && _character.StateManager != null
                        && _character.StateManager.NotifyDownedHit())
                        PlayDownedHitReaction();
                }
                return;
            }

            // ── 방어 판정 (가드/패링) — Vulnerable(퍼펙트 패링당함) 중에는 무방비: 가드/패링 판정을 건너뛴다 ──
            var defense = _character.Defense;
            DefenseResult defResult = defense != null && reactionState != CharacterStateType.Vulnerable
                ? defense.Resolve(ref damage) : DefenseResult.None;

            if (defResult == DefenseResult.Parry || defResult == DefenseResult.PerfectParry)
            {
                HitReceived?.Invoke(damage);
                ApplyParryKnockback(defense, damage);
                return;
            }
            if (defResult == DefenseResult.Guard)
            {
                _character.ApplyDamage(damage.Damage);
                HitReceived?.Invoke(damage);
                ApplyGuardKnockback(defense, damage);
                return;
            }

            // 방어 실패 / GuardBreak / None → 기존 피격 처리.
            bool superArmor = AnySuperArmor; // 잠재능력(나찰)·슈퍼아머 공격 — 데미지는 받되 경직/공격취소 없음
            _hitInterruptedAttack = !superArmor && _attackActive; // 공격 중 피격 — PlayHitReaction이 직접 CrossFade로 모션을 보장
            if (!superArmor) CancelCurrentAttack();
            _character.ApplyDamage(damage.Damage);
            DrainTouhon(touhonDrainOnHit);
            HitReceived?.Invoke(damage);
            HitLanded?.Invoke(damage);
            PlayHitFeedback(damage);
            if (_character.CurrentHP <= 0f)
                PlayDeathFeedback();

            var sm = _character.StateManager;
            if (sm != null)
            {
                if (_character.CurrentHP <= 0f)
                    sm.ChangeState(CharacterStateType.Dead);
                // 이번 타격으로 투혼이 파괴돼 그로기에 진입한 경우(DrainTouhon → EnterGroggy),
                // 피격 리액션이 Groggy 상태를 Hit/넉백으로 덮어쓰지 않는다 (구버전: 그로기 우선).
                // SuppressHitReaction: 잡기 등 시전자가 피격 애니를 직접 구동하는 경우 표준 리액션 상태(DamagedBackward)를 건너뛴다 — 이중 모션 방지.
                else if (!superArmor && !_groggyActive && !damage.SuppressHitReaction)
                    EnterHitReactionState(sm, damage);
            }
        }

        /// <summary>피격 애니메이션(Hit Trigger) 재생. State 기반 진입: CharacterHitState.Enter에서 호출(§26).</summary>
public void PlayHitReaction(bool forward = false)
        {
            // Force the hit reaction to win over any leftover reaction/getup animation state.
            // After a knockdown -> getup, stale reaction triggers or hold bools can otherwise
            // swallow the hit reaction (state is correct, but the Animator keeps the old clip).
            // Clear the competing reaction triggers/bools first, then trigger the hit.
            ResetAnimTrigger(AnimParams.AirborneForward);
            ResetAnimTrigger(AnimParams.AirborneBackward);
            ResetAnimTrigger(AnimParams.KnockdownForward);
            ResetAnimTrigger(AnimParams.KnockdownBackward);
            ResetAnimTrigger(AnimParams.AirborneLand);
            ResetAnimTrigger(AnimParams.Getup);
            ResetAnimTrigger(AnimParams.QuickGetup);
            ResetAnimTrigger(AnimParams.Falldown);
            ResetAnimTrigger("DamagedForward");
            ResetAnimTrigger("AirDamagedForward");
            ResetAnimTrigger("AirDamagedBackward");
            SetAnimBool(AnimParams.Stun, false);
            SetAnimBool(AnimParams.Falldowning, false);
            // forward=true(앞에서 피격) -> DamagedForward, false -> 기존 Backward 경직(AnimParams.Hit).
            SetAnimTrigger(forward ? "DamagedForward" : AnimParams.Hit);

            // 공격 중 피격 인터럽트 보장 (2026-08-27): AnyState 트리거 전이는 진행 중인 Transition
            // (공격 진입 CrossFade/콤보 연결 구간)을 끊지 못해 공격 모션이 끝까지 재생될 수 있다.
            // 공격이 피격으로 취소됐거나 Animator가 전이 중이면 Damaged 상태로 직접 CrossFade해
            // 피격 모션이 즉시 나오게 한다. (PlayAirborneChain과 동일한 직접 재생 패턴)
            if (_animator != null && (_hitInterruptedAttack || _animator.IsInTransition(0)))
            {
                ResetAnimTrigger(forward ? "DamagedForward" : AnimParams.Hit); // 직접 재생하므로 트리거 잔류로 인한 이중 전이 방지
                _animator.CrossFadeInFixedTime(forward ? "DamagedForward.DamagedForward" : "DamagedBackward.DamagedBackward", 0.03f, 0);
            }
            _hitInterruptedAttack = false;
        }

        [System.NonSerialized] private bool _hitInterruptedAttack;

        /// <summary>
        /// 다운(누워있는) 중 OTG 피격 반응 — 현재 재생 중인 다운 포즈 클립을 처음부터 다시 재생해
        /// 누운 채 움찔하는 연출을 만든다. 상태 전이 없음(다운 유지). ReceiveDamage 다운 분기가 호출.
        /// </summary>
        public void PlayDownedHitReaction()
        {
            if (_animator == null) return;
            var st = _animator.GetCurrentAnimatorStateInfo(0);
            _animator.PlayInFixedTime(st.fullPathHash, 0, 0f);
        }

/// <summary>넉다운(Airborne) 반응 애니 — 방향별 AirborneForward/Backward 트리거 + Stun bool 유지(넘어짐 포즈). CharacterAirborneState.Enter가 호출.</summary>
        public void PlayKnockdownReaction(bool forward)
        {
            SetAnimTrigger(forward ? AnimParams.AirborneForward : AnimParams.AirborneBackward);
            SetAnimBool(AnimParams.Stun, true);
        }

        /// <summary>에어본 아크 시작 상태를 방향별로 직접 CrossFade — 애니메이터가 Start→Loop→End로 자체 전이. CharacterAirborneState.Enter가 호출.</summary>
        public void PlayAirborneChain(bool forward)
        {
            // 서브 스테이트 머신 경로 — 상태명은 AirborneStart(앞/뒤 SM에 중복 존재), 방향은 SM(AirborneForward/Backward)로 구분.
            string state = forward ? "AirborneForward.AirborneStart" : "AirborneBackward.AirborneStart";
            if (_animator != null)
                _animator.CrossFadeInFixedTime(state, 0.05f, 0);
        }

        /// <summary>
        /// 다운 포즈(누움) 상태로 직접 진입 — 방향별 Airborne 서브머신의 Knockdown(누움 루프) 상태를 CrossFade하고 Stun을 유지한다.
        /// 낙하 구간(AirborneStart→Loop→End)을 건너뛰므로 이미 바닥에 있는 대상(잡기 슬램 해제)에 쓴다. 기상은 PlayGetup이 그대로 담당 (2026-09-09).
        /// blend<=0 이면 CrossFade 없이 즉시 Play — 잡힘 클립의 본 변위가 블렌드 동안 루트로 미끄러져 돌아오는 현상 방지 (2026-09-09).
        /// </summary>
        public void PlayDownedPose(bool forward, float blend = 0.08f)
        {
            string state = forward ? "AirborneForward.Knockdown" : "AirborneBackward.Knockdown";
            SetAnimBool(AnimParams.Stun, true);
            if (_animator == null) return;
            if (blend <= 0f) _animator.Play(state, 0, 0f);
            else _animator.CrossFadeInFixedTime(state, blend, 0);
        }

        /// <summary>공중 추가 피격(저글 히트) 움찔 애니 — 방향별 AirDamaged 상태를 직접 CrossFade.
        /// 구프로젝트 CharacterAirborne.HandleAirDamage 이관(트리거 대신 직접 재생: AirborneStart 등
        /// Loop 이전 구간에서도 확실히 나오게). 종료 후 애니메이터가 ExitTime으로 AirborneLoop 복귀,
        /// 착지 시 Grounded로 AirborneEnd 전이. CharacterAirborneState.Enter(IsJuggleHit)가 호출.</summary>
        public void PlayAirDamaged(bool forward)
        {
            string state = forward ? "AirborneForward.AirDamaged" : "AirborneBackward.AirDamaged";
            if (_animator != null)
                _animator.CrossFadeInFixedTime(state, 0.03f, 0);
        }

        /// <summary>기상(Getup) — Stun 해제 + Getup 트리거. Collapse 종료 시 CharacterAirborneState가 호출.</summary>
        public void PlayGetup()
        {
            SetAnimBool(AnimParams.Stun, false);
            SetAnimBool(AnimParams.Falldowning, false);
            SetAnimBool(AnimParams.Falldowning, false); // Falldown 체인: Falldowning=false → FalldownGetup(기상)
            
            _getupStateSeen = false;
SetAnimTrigger(AnimParams.Getup);          // Knockdown/Airborne 체인: Getup 트리거 → StandUp
        }

        /// <summary>넉다운 애니 상태 정리(Stun 해제) — CharacterAirborneState.Exit 안전망.</summary>
        public void ClearKnockdownAnim()
        {
            SetAnimBool(AnimParams.Stun, false);
            SetAnimBool(AnimParams.Falldowning, false);
        }

        /// <summary>지상 넉다운(넘어짐) 반응 애니 — 방향별 Knockdown 트리거 + Falldown/Stun 유지. CharacterKnockdownState.Enter가 호출. 파라미터 미존재 시 no-op(그레이스풀).</summary>
        public void PlayGroundKnockdownReaction(bool forward)
        {
            // 방향형 낙하 애니는 구프로젝트가 쓰던 Airborne Forward/Backward 낙하 클립을 직접 CrossFade한다.
            // (KnockdownForward/Backward 상태는 placeholder(BattleIdle) 클립에 물려 있어 90도 회전/오포즈 원인 → 사용 안 함)
            // 지상 넉다운 = 컨트롤러의 Falldown 서브머신 체인(FalldownLoop 낙하 → Falldown 다운 → FalldownGetup 기상).
            // Falldown 트리거로 진입(AnyState, 중첩 상태도 확실히 잡힘) + Falldowning=true로 다운 유지(기상 시 false).
            // 실제 낙하 클립(구프로젝트 이식, 적은 오버라이드 클립)이 재생된다. 단일 트리거라 전이 충돌 없음.
            SetAnimTrigger(AnimParams.Falldown);
            SetAnimBool(AnimParams.Falldowning, true);
        }

        /// <summary>기상 애니가 끝났는가(Getup 태그 + 완주). 파라미터/상태 미존재 시 true 반환 안 함 → 상태 타임아웃이 안전망.</summary>
public bool IsGetupAnimationFinished()
        {
            if (_animator == null) return true;
            var st = _animator.GetCurrentAnimatorStateInfo(0);
            bool inGetup = st.IsTag(AnimParams.GetupTag);
            if (inGetup)
            {
                _getupStateSeen = true;
                // Finished once the get-up clip has essentially played out. We intentionally do NOT
                // require normalizedTime >= 1 or !IsInTransition: the get-up state (StandUp) exit-
                // transitions to BattleMovement before reaching 1.0, so those conditions would never
                // hold simultaneously and the reaction would linger until the safety timeout.
                return st.normalizedTime >= 0.9f;
            }
            // Not currently in a Getup-tagged state.
            // If we have not yet entered it (the Getup transition is still starting), keep waiting so
            // the get-up motion is not skipped.
            if (!_getupStateSeen) return false;
            // We were in the get-up state and have since transitioned out of it (e.g. StandUp ->
            // BattleMovement): the get-up is over.
            return true;
        }

        private void SetAnimBool(string paramName, bool value)
        {
            if (HasAnimParam(paramName)) _animator.SetBool(paramName, value);
        }


        /// <summary>
        /// 생존 피격 시 강제 반응 상태 결정 — 우선순위 Airborne &gt; Knockback &gt; Hit.
        /// 공격 데이터(DamageInfo.Reaction/force)로 분기하며, 넉백/에어본 파라미터는 StateManager.PendingReaction으로
        /// 넘긴다(실제 이동은 Movement가 수행). 이미 공중(Airborne)이면 일반 타격도 체공을 유지한다(저글 확장 여지).
        /// 가드 자세 중 강제로 밀렸을 때는 Defense.GuardKnockbackResistance만큼 밀림을 감소한다.
        /// </summary>
        // 체공 중 일반 타격(비시동기)의 재상승 속도(m/s) — 저글 유지용 작은 팝업.
        private const float JuggleRepopForce = 4f;

        private void EnterHitReactionState(CharacterStateManager sm, DamageInfo damage)
        {
            var spec = damage.Reaction;
            if (debugHitReactions)
                Debug.Log($"[Hit] {(damage.Attacker != null ? damage.Attacker.name : "?")} \u2192 {_character.name} | kind={spec.Kind} horiz={spec.HorizontalForce:0.##} vert={spec.VerticalForce:0.##} bounce={spec.WantsBounce}", _character);
            bool airborneNow = sm.CurrentStateType == CharacterStateType.Airborne;
            float kbResist = (_character.Defense != null && _character.Defense.IsGuarding)
                ? Mathf.Clamp01(_character.Defense.GuardKnockbackResistance) : 0f;
            // \uac00\ub4dc \uc800\ud56d \u2014 \ubaa8\ub4e0 \ubc18\uc751\uc758 \uc218\ud3c9 \uc131\ubd84\uc5d0 \uacf5\ud1b5 \uc801\uc6a9 (\uae30\uc874 \ub109\ubc31/\uc5d0\uc5b4\ubcf8 \uc218\ud3c9 \uac10\uc1e0\uc640 \ub3d9\uc77c).
            spec.HorizontalForce *= (1f - kbResist);

            bool wantLaunch = spec.Kind == HitReactionKind.Launch && spec.VerticalForce > 0f;
            bool wantSlam = spec.Kind == HitReactionKind.Slam && spec.VerticalForce > 0f;
            bool wantKnockdown = spec.Kind == HitReactionKind.Knockdown;
            bool wantPush = spec.Kind == HitReactionKind.Push && spec.HorizontalForce > 0f;

            // 지상 대상 Slam(바운스 없음) — 기존 동작 유지: HorizontalForce만큼 밀리며 넉다운으로 격하.
            if (wantSlam && !airborneNow && !spec.WantsBounce)
            {
                spec.Kind = HitReactionKind.Knockdown;
                wantSlam = false;
                wantKnockdown = true;
            }

            // 공중 반응 경로 — Launch / Slam / 체공 유지(저글) / 바운스 조합(Push·Knockdown+Bounce = 적중 즉시 튀며 밀림).
            bool bounceCombo = spec.WantsBounce && (wantPush || wantKnockdown);
            if (wantLaunch || wantSlam || airborneNow || bounceCombo)
            {
                // 체공 중 시동기(Launch/Slam/바운스 조합)가 아닌 일반 타격 = 저글 유지 히트.
                // 작은 재상승(JuggleRepopForce)으로 체공만 연장하고, 이전 바운스는 이어받지 않는다(덮어쓰기 규칙).
                bool juggleKeep = airborneNow && !wantLaunch && !wantSlam && !bounceCombo;
                if (juggleKeep)
                {
                    spec.Kind = HitReactionKind.Launch;
                    spec.VerticalForce = JuggleRepopForce;
                    spec.AirTime = 0f;
                    spec.EnableBounce = false;
                    spec.BouncePower = 0f;
                }
                sm.SetPendingReaction(new CharacterStateManager.ReactionData
                {
                    Direction = damage.HitDirection,
                    Spec = spec,
                    Attacker = damage.Attacker,
                    BodyImpactDamage = damage.BodyImpactDamage,
                    BodyImpactRadius = damage.BodyImpactRadius,
                    IsJuggleHit = juggleKeep,
                });
                sm.ChangeState(CharacterStateType.Airborne, allowReenter: true);
                return;
            }

            if (wantKnockdown)
            {
                sm.SetPendingReaction(new CharacterStateManager.ReactionData
                {
                    Direction = damage.HitDirection,
                    Spec = spec,
                    Attacker = damage.Attacker,
                    BodyImpactDamage = damage.BodyImpactDamage,
                    BodyImpactRadius = damage.BodyImpactRadius,
                });
                sm.ChangeState(CharacterStateType.Knockdown, allowReenter: true);
                return;
            }

            // 일반 경직(Hit) + 수평 밀림(Push) 통합 — 구 Knockback 상태를 Hit로 합침.
            // Push(HorizontalForce>0)면 CharacterHitState가 BeginKnockback으로 밀고, 몸통충돌(BodyImpact)도 Hit가 Sweep한다.
            // 방향 데이터는 앞/뒤 피격 애니 선택에도 사용(구 CharacterDirectionalDamage 지상 분기 복원).
            sm.SetPendingReaction(new CharacterStateManager.ReactionData
            {
                Direction = damage.HitDirection,
                Spec = spec,
                Attacker = damage.Attacker,
                BodyImpactDamage = damage.BodyImpactDamage,
                BodyImpactRadius = damage.BodyImpactRadius,
            });
            sm.ChangeState(CharacterStateType.Hit, allowReenter: true);
        }

        /// <summary>
        /// 가드 성공 시 '줄어든 밀림' 적용 — 가드 자세(Guard 상태)를 유지한 채 수평으로 살짝 밀린다.
        /// 밀림 세기는 Defense.GuardKnockbackResistance로 감소, 이동잠금(슬라이드) 시간은 Defense.GuardKnockbackDuration.
        /// 실제 이동은 Movement 넉백 채널이 수행하고, 잠금/해제 타이밍은 CharacterGuardState가 소유한다.
        /// 가드 '자세'(Guard 상태) 중일 때만 적용한다 — 자동/강제 가드(다른 상태)는 슬라이드 종료를 소유할 상태가 없어 제외.
        /// </summary>
        private void ApplyGuardKnockback(CharacterDefense defense, DamageInfo damage)
        {
            if (defense == null || !defense.IsGuarding) return;
            if (defense.GuardKnockbackDuration <= 0f) return;

            var movement = Movement;
            if (movement == null) return;

            // 공격 자체의 밀림 세기를 GuardKnockbackResistance로 감소시키되, GuardKnockbackForce(가드 기본 밀림)를
            // 하한으로 둔다 — 공격의 HorizontalForce가 0이어도 가드는 항상 이 값만큼 밀린다(공격 힘이 더 크면 그쪽).
            float attackForce = damage.Reaction.HorizontalForce;
            float reduced = attackForce * (1f - Mathf.Clamp01(defense.GuardKnockbackResistance));
            float force = Mathf.Max(reduced, defense.GuardKnockbackForce);
            if (force <= 0f) return;

            // 밀림 방향 — 공격 방향(공격자→나). 없으면 바라보는 반대(뒤)로 민다.
            Vector3 dir = damage.HitDirection;
            if (dir.sqrMagnitude < 0.0001f)
                dir = movement.FacingRight ? -movement.SplineForward : movement.SplineForward;

            movement.BeginKnockback(dir, force);
            defense.BeginGuardKnockback();
            // 가드 자세를 유지한 채 밀릴 때도 동일한 밀림 이펙트를 재생(자식 오브젝트, 미할당이면 무시).
            _character?.StateManager?.PushEffect?.Play();
        }

        /// <summary>
        /// 패링 성공 시 '살짝 뒤로 밀림' 적용 — 가드 밀림과 동일한 슬라이드 채널/창을 재사용해 가드 자세를 유지한 채 뒤로 밀린다.
        /// 밀림 세기/시간은 Defense.ParryKnockbackForce/ParryKnockbackDuration으로 가드와 별도 튜닝.
        /// 실제 이동은 Movement 넉백 채널이 수행하고, 이동잠금/해제 타이밍은 CharacterGuardState가 소유한다(가드 밀림과 동일 규칙).
        /// 가드 자세(Guard 상태) 중일 때만 적용 — 확률 패링(적 패시브 등)은 슬라이드 종료를 소유할 상태가 없어 제외.
        /// </summary>
        private void ApplyParryKnockback(CharacterDefense defense, DamageInfo damage)
        {
            if (defense == null || !defense.IsGuarding) return;
            if (defense.ParryKnockbackDuration <= 0f) return;

            float force = defense.ParryKnockbackForce;
            if (force <= 0f) return;

            var movement = Movement;
            if (movement == null) return;

            // 밀림 방향 — 공격 방향(공격자→나)이 곧 '뒤로'. 없으면 바라보는 반대(뒤)로 민다.
            Vector3 dir = damage.HitDirection;
            if (dir.sqrMagnitude < 0.0001f)
                dir = movement.FacingRight ? -movement.SplineForward : movement.SplineForward;

            movement.BeginKnockback(dir, force);
            defense.BeginParryKnockback();
        }

/// <summary>
        /// 공격이 패링당했을 때 공격자 측 반응 — CharacterDefense.Resolve가 방어자→공격자로 호출.
        /// 퍼펙트 패링이면 진행 중 콤보를 취소하고, 공통으로 Parried 애니 + 프레임스톱(animator.speed=0)을 준다
        /// (구버전 CharacterParried.ApplyParried 이식).
        /// vulnerableDuration &gt; 0이면(방어자 CharacterDefense.Parry/PerfectParryVulnerableDuration — 일반/퍼펙트 각각)
        /// 그 시간 동안 Vulnerable 상태(이동/공격 잠금 + 무방비)로 진입시킨다.
        /// </summary>
        public void ReactToParried(bool perfect, float frameStopDuration, float vulnerableDuration = 0f)
        {
            if (_character == null) return;
            if (perfect) CancelCurrentAttack();
            SetAnimTrigger(AnimParams.Parried);
            if (frameStopDuration > 0f && _character.isActiveAndEnabled)
                _character.StartCoroutine(FrameStopRoutine(frameStopDuration));
            if (vulnerableDuration > 0f)
                EnterVulnerable(vulnerableDuration); // Vulnerable 진입 시 State.Enter가 공격을 취소한다(일반 패링 포함)
        }

        /// <summary>현재 Vulnerable 상태의 지속 시간(초). EnterVulnerable이 설정, CharacterVulnerableState가 읽는다.</summary>
        public float VulnerableDuration { get; private set; }

        /// <summary>
        /// 취약(Vulnerable) 상태 진입 — 패링(일반/퍼펙트)당한 공격자. 지상 + 다운/처형/잡기/사망 중이 아닐 때만.
        /// 상태 잠금/타이밍은 CharacterVulnerableState, 진입 규칙은 여기(Groggy와 동일한 책임 분리).
        /// </summary>
        public void EnterVulnerable(float duration)
        {
            if (_character == null || _character.CurrentHP <= 0f || duration <= 0f) return;
            var sm = _character.StateManager;
            if (sm == null) return;
            var m = Movement;
            if (m == null || !m.IsGrounded) return;
            switch (sm.CurrentStateType)
            {
                case CharacterStateType.Dead:
                case CharacterStateType.Knockdown:
                case CharacterStateType.Airborne:
                case CharacterStateType.Groggy:
                case CharacterStateType.Executing:
                case CharacterStateType.Executed:
                case CharacterStateType.Grab:
                case CharacterStateType.ChainGrab:
                    return;
            }
            VulnerableDuration = duration;
            sm.ChangeState(CharacterStateType.Vulnerable, allowReenter: true);
        }

        private System.Collections.IEnumerator FrameStopRoutine(float duration)
        {
            if (_animator == null) yield break;
            _animator.speed = 0f;
            yield return new WaitForSecondsRealtime(duration);
            if (_animator != null)
                _animator.speed = _character != null && _character.Stats != null ? _character.Stats.FinalGlobalAnimatorSpeed : 1f;
        }


        /// <summary>
        /// This character just landed a CRITICAL hit on an enemy — fire the global hit stop using
        /// THIS (attacker) character's settings. Non-stacking (FeedbackTime strongest-wins), realtime.
        /// easeIn/easeOut = 0 makes it a true snap hitstop; positive values ease it like a slow.
        /// </summary>
        public void PlayCriticalHitstop()
        {
            if (!criticalHitstopEnable) return;
            if (criticalHitstopScale >= 1f) return;
            Aiara.FeedbackTime.SlowMotion(criticalHitstopScale, criticalHitstopDuration, criticalHitstopEaseIn, criticalHitstopEaseOut);
        }

/// <summary>피격 지점에 공용 Hit Feedback 재생. 방향은 카메라 기준 좌/우 플립 (구버전 이식).</summary>
private void PlayHitFeedback(DamageInfo damage)
        {
            // Critical hit stop — runs BEFORE the VFX early-returns so a crit still freezes even if
            // this hit shows no hit VFX. Uses the ATTACKER's settings, so tuning it once on the player
            // applies to every enemy and every attack. Gated to an enemy victim (a crit landing on an
            // enemy == the player's crit; enemies cannot damage other enemies).
            if (damage.IsCritical && _character.Faction == FactionType.Enemy && damage.Attacker != null)
            {
                var attackerChar = damage.Attacker.GetComponentInParent<Character>();
                if (attackerChar != null && attackerChar.Combat != null)
                    attackerChar.Combat.PlayCriticalHitstop();
            }

            if (!damage.ShowHitEffect) return; // 공격 데이터(AttackAction.showHitEffect)가 이 타격의 Hit 이펙트를 끈 경우 생략
            if (string.IsNullOrEmpty(hitFeedbackKey)) return;
            if (Time.time - _lastHitFeedbackTime < hitFeedbackCooldown) return;
            _lastHitFeedbackTime = Time.time;

            // 경량화: 여러 적이 짧은 시간창 안에 거의 동시에 맞으면 첫 피격만 큰 이펙트(Hit)를
            // 얻고, 나머지는 약한 이펙트(hitFeedbackKeyLight)로 대체한다. 플레이어/아군은 항상 큰 이펙트.
            // 예산은 전역 시간창(Aiara.HitEffectBudget) — AoE/다단히트/군집에서 큰 VFX 동시 재생 수를 제한.
            string key = hitFeedbackKey;
            if (useLightHitBudget
                && _character.Faction == FactionType.Enemy
                && !Aiara.HitEffectBudget.TryConsumeBig())
            {
                if (string.IsNullOrEmpty(hitFeedbackKeyLight)) return; // 약한 키 미설정 → 생략(최경량)
                key = hitFeedbackKeyLight;
            }

            // 치명타는 경량화 예산과 무관하게 전용 키로 재생(연출 강조). 키 미설정 시 위에서 정해진 일반 키 유지.
            if (damage.IsCritical && !string.IsNullOrEmpty(hitFeedbackKeyCritical))
                key = hitFeedbackKeyCritical;

            
            Vector3 pos = damage.HitPoint;
            Quaternion rot = Quaternion.identity;
            var cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                float dot = Vector3.Dot(damage.HitDirection, cam.transform.right);
                rot = Quaternion.Euler(0f, dot >= 0f ? 0f : 180f, 0f);
                pos -= cam.transform.forward * hitVFXCameraOffset;
            }
            Aiara.FeedbackManager.PlayFeedbackAtWorld(key, pos, rot);
        }

        /// <summary>
        /// 그라운드 바운스 랜딩 Feedback 재생 — CharacterAirborneState.TryGroundBounce가 매 바닥 충돌마다 호출.
        /// landingIndex = 이번 에어본에서 몇 번째 바닥 충돌인가(0부터). bounceLandingBigCount 미만이면
        /// bounceLandingFeedbackKey, 그 이후는 bounceLandingSmallFeedbackKey.
        /// 위치 = 캐릭터 루트 + bounceLandingOffset(스플라인 축 기준, Facing에 따라 전방(x) 자동 반전).
        /// </summary>
        public void PlayBounceLandingFeedback(CharacterMovement movement, int landingIndex = 0)
        {
            string key = landingIndex < bounceLandingBigCount ? bounceLandingFeedbackKey : bounceLandingSmallFeedbackKey;
            if (_character == null || string.IsNullOrEmpty(key)) return;
            Vector3 pos = _character.transform.position;
            if (movement != null)
            {
                Vector3 fwd = movement.SplineForward;
                if (fwd.sqrMagnitude > 0.0001f)
                    pos += fwd.normalized * (movement.FacingRight ? 1f : -1f) * bounceLandingOffset.x;
                Vector3 depth = movement.SplineDepth;
                if (depth.sqrMagnitude > 0.0001f)
                    pos += depth.normalized * bounceLandingOffset.z;
            }
            pos += Vector3.up * bounceLandingOffset.y;
            Aiara.FeedbackManager.PlayFeedbackAtWorld(key, pos, Quaternion.identity);
        }

        /// <summary>사망 연출 Feedback 재생 (HP 0 도달 시 1회).</summary>
private void PlayDeathFeedback()
        {
            if (_character == null) return;
            if (string.IsNullOrEmpty(deathFeedbackKey) && !despawnOnDeathFeedback) return;
            if (deathFeedbackDelay > 0f)
                _character.StartCoroutine(DeathFeedbackRoutine());
            else
                PlayDeathFeedbackNow();
        }

        private System.Collections.IEnumerator DeathFeedbackRoutine()
        {
            yield return new WaitForSeconds(deathFeedbackDelay);
            PlayDeathFeedbackNow();
        }

private void PlayDeathFeedbackNow()
        {
            if (_character == null) return;
            if (!string.IsNullOrEmpty(deathFeedbackKey))
            {
                Vector3 pos = _character.transform.position;
                Aiara.FeedbackManager.PlayFeedbackAtWorld(deathFeedbackKey, pos, Quaternion.identity);
            }
            // 폭발과 동시에 시체 제거 (파괴 대신 비활성화 — 웨이브/리스폰 재사용 대비)
            if (despawnOnDeathFeedback)
                _character.gameObject.SetActive(false);
        }


        // ─────────── 자원: FP 자동 회복 / 투혼 충전·소모 / 허주 (구버전 CharacterFP·BattleSpiritResource 이관) ───────────

        /// <summary>허주(투혼 0 디버프) 활성 여부.</summary>
        public bool IsHeojuActive => _heojuActive;
        /// <summary>투혼 리셋 대기 중(0 도달 후 허주 유지 구간)인가.</summary>
        public bool IsTouhonResetting => _touhonResetTimer > 0f;
        /// <summary>리셋 예정값의 Max 대비 비율(0~1). HUD 마커용 (구버전 PreviewResetValue).</summary>
        public float TouhonResetRatio => _character != null && _character.MaxTouhon > 0f
            ? Mathf.Clamp01(touhonResetValue / _character.MaxTouhon) : 0f;

        /// <summary>
        /// 상시 자원 틱 — Character.Update가 호출(공격 흐름과 별개).
        /// FP 패시브 회복(공격 모션 중 정지 — 구버전 IsWeaponAttacking 게이트)과 투혼 리셋 타이머를 진행한다.
        /// </summary>
        public void TickResources(float deltaTime)
        {
            if (_character == null || _character.Stats == null) return;
            if (_character.CurrentHP <= 0f) return; // 사망 시 자원 정지

            if (fpRegenPercentPerSecond > 0f && !_attackActive && _character.CurrentFP < _character.MaxFP)
                _character.RecoverFP(_character.MaxFP * (fpRegenPercentPerSecond * 0.01f) * deltaTime);

            // 착지 대기 중이던 그로기 진입 소비 (구버전 ConsumePendingGroggyEntry — 공중/다운이 끝나면 진입)
            if (_pendingGroggy)
            {
                var st = _character.StateManager != null
                    ? _character.StateManager.CurrentStateType : CharacterStateType.Idle;
                if (st != CharacterStateType.Airborne && st != CharacterStateType.Knockdown
                    && st != CharacterStateType.Dead)
                    EnterGroggy();
            }

            if (_touhonResetTimer > 0f)
            {
                _touhonResetTimer -= deltaTime;
                if (_touhonResetTimer <= 0f)
                {
                    if (_pendingGroggy)
                    {
                        // 아직 착지 대기 중 — 타이머를 잠깐 유지하고 실제 그로기 진입 시 다시 카운트한다.
                        _touhonResetTimer = 0.5f;
                    }
                    else
                    {
                        _touhonResetTimer = 0f;
                        if (_groggyActive) ExitGroggy();
                        else DeactivateHeoju();
                        _character.SetTouhon(Mathf.Min(touhonResetValue, _character.MaxTouhon));
                    }
                }
            }
        }

        /// <summary>투혼 충전. MAX 도달 시 TouhonMaxed 발화(잠재능력 훅 — 자동 리셋 없음). 리셋 대기 중에는 무시.</summary>
        public void ChargeTouhon(float amount)
        {
            if (_character == null || amount <= 0f || _character.MaxTouhon <= 0f) return;
            if (_touhonResetTimer > 0f) return;
            float prev = _character.CurrentTouhon;
            _character.SetTouhon(prev + amount);
            if (prev < _character.MaxTouhon && _character.CurrentTouhon >= _character.MaxTouhon)
                TouhonMaxed?.Invoke();
        }

        /// <summary>투혼 소모. 0 도달 시 TouhonDepleted + 허주 발동 + 리셋 타이머 시작. 리셋 대기 중에는 무시.</summary>
        public void DrainTouhon(float amount)
        {
            if (_character == null || amount <= 0f || _character.MaxTouhon <= 0f) return;
            if (_touhonResetTimer > 0f) return;
            float prev = _character.CurrentTouhon;
            _character.SetTouhon(prev - amount);
            if (prev > 0f && _character.CurrentTouhon <= 0f)
            {
                TouhonDepleted?.Invoke();
                // 적(처형 대상)은 허주 대신 그로기로 무력화 → 처형 가능. 그 외는 기존 허주 디버프.
                if (groggyOnTouhonDepleted) EnterGroggy();
                else ActivateHeoju();
                if (touhonResetDelay > 0f) _touhonResetTimer = touhonResetDelay;
            }
        }

        /// <summary>
        /// 공격 적중 통지 — AttackHitbox가 호출(공격자 측). 투혼 충전 + FP 적중 회복 + 처치 충전.
        /// 구버전 ChargeAttackHit/RegainOnAttackHit/ChargeKill과 동일 트리거.
        /// </summary>
        public void NotifyAttackLanded(Character victim, AttackAction sourceAction = null)
        {
            if (_character == null) return;
            // 최근 적중 대상 기록 — 잡기(ActionGrab) 대상 선정에 사용 (구버전 StateBasedComboWeapon.LastHitHealth 이식).
            if (victim != null && victim != _character)
            {
                _lastHitVictim = victim;
                _lastHitVictimTime = Time.time;
                _lastHitAction = sourceAction != null ? sourceAction : _currentAction;
            }
            ChargeTouhon(touhonChargeOnAttackHit);
            if (fpRegainOnAttackHit > 0f) _character.RecoverFP(fpRegainOnAttackHit);
            if (victim != null && victim.CurrentHP <= 0f) ChargeTouhon(touhonChargeOnKill);

            // Air-juggle self-lift: rise alongside a launched enemy when this attack asks for it.
            // Victim has already transitioned into its reaction state inside ReceiveDamage, so an
            // Airborne victim reads as Airborne here (including the launching hit itself).
            // sourceAction(히트박스가 전달한 소속 공격)을 우선 사용 — 히트 등록과 같은 프레임에 공격이
            // 인터럽트(피격 캔슬/조기 종료)되어 _currentAction이 이미 비워진 경우에도 리프트가 유실되지 않는다.
            var liftAction = sourceAction != null ? sourceAction : _currentAction;

            // 공중콤보 Arm 보강: 시동기 적중이 공격 인터럽트와 같은 프레임에 겹쳐 TickAttack의 Arm을
            // 놓치는 경우에도, 적중 통지 시점에 직접 Arm한다 (트리거 조건은 TickAttack과 동일).
            if (liftAction != null && liftAction.IsAirComboLauncher)
                _airComboArmedUntil = Time.time + airComboArmDuration;

            if (liftAction != null && liftAction.AttackerLiftForce > 0f && _character.Movement != null)
            {
                bool victimAirborne = victim != null && victim.StateManager != null &&
                    victim.StateManager.CurrentStateType == CharacterStateType.Airborne;
                if (!liftAction.AttackerLiftOnlyVsAirborne || victimAirborne)
                    _character.Movement.ApplyUpwardLift(liftAction.AttackerLiftForce);
            }
        }

        /// <summary>허주 발동: 이동·데미지·공속 디버프 (구버전 ActivateHeoju — 배율 곱 방식 그대로).</summary>
        private void ActivateHeoju()
        {
            if (_heojuActive || _character == null) return;
            _heojuActive = true;
            var s = _character.Stats;
            if (s != null)
            {
                s.MoveSpeedExternalMultiplier *= heojuSpeedMultiplier; // 상태가 덮어쓰는 Movement 배율 대신 Stats 축 사용
                s.DamageOutputMultiplier *= heojuDamageMultiplier;
                s.AttackSpeedMultiplier *= heojuAttackSpeedMultiplier;
            }
            HeojuChanged?.Invoke(true);
        }

        /// <summary>허주 해제: 디버프 복원 (나눗셈 복원 — 다른 배율과 중첩 안전).</summary>
        private void DeactivateHeoju()
        {
            if (!_heojuActive || _character == null) return;
            _heojuActive = false;
            var s = _character.Stats;
            if (s != null)
            {
                if (heojuSpeedMultiplier > 0f) s.MoveSpeedExternalMultiplier /= heojuSpeedMultiplier;
                if (heojuDamageMultiplier > 0f) s.DamageOutputMultiplier /= heojuDamageMultiplier;
                if (heojuAttackSpeedMultiplier > 0f) s.AttackSpeedMultiplier /= heojuAttackSpeedMultiplier;
            }
            HeojuChanged?.Invoke(false);
        }

        
// ─────────── 절명기 (처형) / 그로기 (구버전 CharacterExecution·CharacterGroggy·CharacterExecuted 이관) ───────────

        /// <summary>그로기(투혼 0으로 무력화, 처형 대상) 상태인가.</summary>
        public bool IsGroggy => _groggyActive;
        /// <summary>완전 무적(처형 시전 중 등)인가. ReceiveDamage가 조기 반환한다.</summary>
        public bool IsInvincible => _invincible;
        /// <summary>무적 On/Off — 처형 상태(Executing)가 제어.</summary>
        public void SetInvincible(bool value) => _invincible = value;
        /// <summary>처형 대상(그로기 적). CheckExecution이 설정, Executing 상태가 읽는다.</summary>
        public Character ExecutionTarget => _executionTarget;
        /// <summary>처형 대상 지정.</summary>
        public void SetExecutionTarget(Character c) => _executionTarget = c;
        public float ExecutionRange => executionRange;
        public float ExecutionApproachDuration => executionApproachDuration;
        public float ExecutionDuration => executionDuration;

        /// <summary>
        /// 투혼 0 도달 시(적) 허주 대신 그로기로 진입 — 처형 대상이 된다. 상태 잠금/연출은 CharacterGroggyState.
        /// 공중(Airborne)/다운(Knockdown) 중이면 즉시 진입하지 않고 착지 후 진입한다(구버전 _pendingGroggyEntry).
        /// 그로기 지속 시간(touhonResetDelay)은 실제 진입 시점부터 카운트한다.
        /// </summary>
        public void EnterGroggy()
        {
            if (_character == null || _character.CurrentHP <= 0f) return;
            var sm = _character.StateManager;
            var cur = sm != null ? sm.CurrentStateType : CharacterStateType.Idle;
            if (cur == CharacterStateType.Airborne || cur == CharacterStateType.Knockdown)
            {
                _pendingGroggy = true;
                return;
            }
            _pendingGroggy = false;
            _groggyActive = true;
            if (touhonResetDelay > 0f) _touhonResetTimer = touhonResetDelay;
            if (sm != null) sm.ChangeState(CharacterStateType.Groggy, allowReenter: true);
        }

        /// <summary>그로기 회복(처형되지 않고 리셋 타이머 만료) — Idle 복귀.</summary>
        private void ExitGroggy()
        {
            _groggyActive = false;
            var sm = _character != null ? _character.StateManager : null;
            if (sm != null && sm.CurrentStateType == CharacterStateType.Groggy)
                sm.ChangeState(CharacterStateType.Idle);
        }

        /// <summary>사거리 내 처형 가능한(그로기 + 적대) 대상 중 가장 가까운 캐릭터. 없으면 null. CheckExecution이 호출.</summary>
        public Character FindExecutionTarget()
        {
            if (_character == null) return null;
            var hits = Physics.OverlapSphere(_character.transform.position, executionRange);
            Character best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i] != null ? hits[i].GetComponentInParent<Character>() : null;
                if (c == null || c == _character) continue;
                if (c.IsDead) continue;
                if (c.CurrentState != CharacterStateType.Groggy) continue;
                if (!AIController.IsHostile(_character, c)) continue;
                // 처형 면역 검사 (구버전 CanBeExecuted && ExecuteTypeValue >= 0 — CharacterActionPoints가 통합 판정)
                var points = c.GetComponent<CharacterActionPoints>();
                if (points != null && !points.CanBeExecuted) continue;
                float d = (c.transform.position - _character.transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = c; }
            }
            return best;
        }

        /// <summary>피격자를 처형 잠금(Executed)으로 전환 — Executing 상태의 시전자가 대상에 호출.</summary>
        public void EnterExecuted()
        {
            _groggyActive = false; // 그로기 리셋 타이머는 무의미
            _pendingGroggy = false;
            _touhonResetTimer = 0f; // 처형 중 타이머 만료로 투혼이 복구/AI가 복귀하는 상태 불일치 방지
            var sm = _character != null ? _character.StateManager : null;
            if (sm != null) sm.ChangeState(CharacterStateType.Executed, allowReenter: true);
        }

        /// <summary>처형 확정 — 대상을 즉사시키고 시전자를 회복(구버전 KillSilent + ExecutionHealPercent). Executing 상태가 종료 시 호출.</summary>
        public void ConfirmExecution(Character victim)
        {
            if (victim == null) return;
            if (executionHealPercent > 0f && _character != null)
                _character.Heal(_character.MaxHP * executionHealPercent);
            if (victim.Combat != null) victim.Combat.KillByExecution();
            _executionTarget = null;
        }

        /// <summary>처형 확정 사망 — 데미지 롤/히트박스 없이 HP를 0으로. 피격자 측에서 실행.</summary>
        public void KillByExecution()
        {
            if (_character == null) return;
            _groggyActive = false;
            _pendingGroggy = false;
            _touhonResetTimer = 0f;
            _character.ApplyDamage(_character.MaxHP); // HP 0 → 아래 Dead 전이
            PlayDeathFeedback();
            var sm = _character.StateManager;
            if (sm != null) sm.ChangeState(CharacterStateType.Dead);
        }


        
// ─────────── 전투 모드 (소유: Combat) ───────────

        /// <summary>전투 모드 여부. Animator 반영은 StateManager.SetBattleMode가 담당.</summary>
        public bool BattleMode => battleMode;

        /// <summary>전투 모드 전환: 값 저장 + Animator 파라미터 반영 요청.</summary>
        public void SetBattleMode(bool value)
        {
            battleMode = value;
            if (_character != null && _character.StateManager != null)
                _character.StateManager.SetBattleMode(value);
        }

        private void SetAnimTrigger(string paramName)
        {
            if (_animator == null || string.IsNullOrEmpty(paramName)) return;
            foreach (var p in _animator.parameters)
            {
                if (p.name == paramName && p.type == AnimatorControllerParameterType.Trigger)
                {
                    _animator.SetTrigger(paramName);
                    return;
                }
            }
        }

private void ResetAnimTrigger(string paramName)
        {
            if (HasAnimParam(paramName)) _animator.ResetTrigger(paramName);
        }


        private void UpdateHPView(float current, float max)
        {
            currentHPView = current;
            maxHPView = max;
        }

        public void OnDisableCleanup()
        {
            if (_character != null)
                _character.OnHealthChanged -= UpdateHPView;
            StopAttack();

            // 재사용(Disable→Enable/리스폰) 대비 전투 상태 초기화 — 이전 생의
            // 그로기/무적/허주/타이머가 남지 않게 한다 (구버전 ResetAbility 이관).
            _groggyActive = false;
            _pendingGroggy = false;
            _invincible = false;
            _executionTarget = null;
            _executionFinishRequested = false;
            _touhonResetTimer = 0f;
            if (_heojuActive) DeactivateHeoju();
            // 그로기형은 투혼도 재사용 대비 가득 복구 (구버전 BattleSpiritResource.ResetAbility 이관 —
            // 풀 재사용 몬스터가 이전 생의 투혼 0을 갖고 스폰되면 영영 그로기에 진입하지 못한다).
            if (groggyOnTouhonDepleted && _character != null && _character.MaxTouhon > 0f)
                _character.SetTouhon(_character.MaxTouhon);
        }
    }
}
