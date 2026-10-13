using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 모든 적(AI) 캐릭터의 공통 기반 — Character 파생 + 개체별 AI Brain.
    ///
    ///   Character
    ///    └─ EnemyCharacter        ← 기본 적은 이 클래스를 그대로 사용
    ///         ├─ SoldierEnemyCharacter (예시 — 필요해질 때만 생성)
    ///         └─ ...
    ///
    /// 담당(개체별 AI 판단): AIState(Idle·Alert·Chase·Attack) 관리 + 자신의 Target 선택/유지 +
    /// 추적·공격 의도 결정 + 개체별 AI 수치 소유. 판단 결과는 명령으로만 전달한다:
    ///   EnemyCharacter(판단) → CharacterCommandManager → CharacterStateManager → Movement/Combat
    /// Movement/Combat/Animator를 직접 호출하지 않는다 — Player와 동일 실행 파이프라인.
    ///
    /// 비담당: 전체 AI 목록 관리·Faction 적대 판별·Target 후보 제공(= AIController Registry),
    /// 실행 상태 관리(= CharacterStateManager 단일 소유).
    ///
    /// 등록: OnEnable/OnDisable에서 AIController에 자동 등록/해제(씬 배치·Instantiate 공통).
    /// AIState는 'AI 판단 상태'로 CharacterStateType(실행 상태)과 절대 합치지 않는다.
    /// 파생 시 Unity 메시지 override는 반드시 base 호출 — Character 초기화·Tick이 거기서 돈다.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    [AddComponentMenu("Yeolha/Character/Enemy Character")]
    public class EnemyCharacter : Character
    {
        /// <summary>AI 판단 상태. CharacterStateType(실행 상태)에 Alert/Chase를 추가하지 않는다.</summary>
        public enum AIState
        {
            Idle,
            Alert,
            Chase,
            Attack,
            Entering   // 공통 진입(SpawnEntry) — 대열 합류 이동, 공격/공격권 없음, 필요 시 벽 통과
        }

        [Header("AI Perception")]
        [Tooltip("적대 대상을 발견하는 거리")]
        [SerializeField] private float detectionRange = 8f;
        [Tooltip("추적을 포기하는 거리. detectionRange보다 크게 — 감지 경계에서 Chase/Idle 반복 방지")]
        [SerializeField] private float loseTargetRange = 12f;

        [Header("AI Alert")]
        [Tooltip("발견 후 추적을 시작하기까지의 경계 시간 (초)")]
        [SerializeField] private float alertDuration = 0.6f;

        [Header("AI Attack")]
        [Tooltip("공격을 시작하는 거리 (attackRange < detectionRange 권장)")]
        [SerializeField] private float attackRange = 1.8f;
        [Tooltip("공격 종료 후 다음 공격까지 대기 시간 (초)")]
        [SerializeField] private float attackCooldown = 1.5f;
        [Tooltip("사용할 공격 이름 (AttackAction.AttackName). 비우면 기본 공격(attacks[0])")]
        [SerializeField] private string attackActionName = "";

        [Header("AI Target Search")]
        [Tooltip("Target 재탐색 간격 (초). 매 프레임 후보 검색을 하지 않기 위한 값")]
        [SerializeField, Range(0.1f, 2f)] private float targetSearchInterval = 0.5f;

        [Header("AI Combat Role")]
        [Tooltip("Guard 역할일 때 유지할 대기 거리 (attackRange보다 크게)")]
        [SerializeField] private float guardDistance = 4f;
        [Tooltip("Pressure 역할일 때 유지할 거리 (Guard보다 가깝게, attackRange 바깥 권장)")]
        [SerializeField] private float pressureDistance = 2.8f;
        [Tooltip("플레이어가 이 거리 안으로 들어오면 비공격자도 제한적 견제(방어 공격) 가능")]
        [SerializeField] private float tooCloseDistance = 1.3f;
        [Tooltip("여러 대기 AI가 한 점에 겹치지 않도록 하는 깊이 방향 간격")]
        [SerializeField] private float formationSpacing = 1.1f;

        [Header("AI Guard")]
        [Tooltip("전투 중 타겟의 공격에 반응해 자동으로 가드 자세를 취할지 (CharacterDefense.CanGuard도 켜져 있어야 함)")]
        [SerializeField] private bool enableAiGuard = true;
        [Tooltip("이 거리 안에서 타겟이 공격 중이면 비공격 역할의 적이 가드한다")]
        [SerializeField] private float guardReactDistance = 2.6f;
        [Tooltip("가드를 한 번 시작하면 이 시간(초) 동안 유지한다. 타겟 공격이 끝나거나 거리가 벌어져도 창이 끝날 때까지 가드를 풀지 않는다. 유지 중 조건이 다시 충족되면 시간이 갱신(연장)된다")]
        [SerializeField, Range(0f, 5f)] private float guardHoldDuration = 1f;

        [Header("AI Spawn Entry")]
        [Tooltip("공통 진입(대열 합류) 지속시간(초). 이 시간 동안 공격·공격권 없이 대열로 이동한다. 0이면 즉시 정상 AI")]
        [SerializeField] private float entryDuration = 2f;
        [Tooltip("진입 중 벽 통과 허용(임시 SpawnEntryPass 레이어). 바닥/월드는 통과하지 않는다")]
        [SerializeField] private bool ignoreWallDuringEntry = true;
        [Tooltip("진입(대열 합류) 중 이동·애니 속도 배수. 화면 밖에서 빠르게 합류시키고 싶을 때 (예 1.5, 1이면 평상시 속도)")]
        [SerializeField] private float entrySpeedMultiplier = 1.5f;

        [Header("AI Debug (View Only)")]
        [Tooltip("현재 AI 판단 상태")]
        [SerializeField] private AIState aiState = AIState.Idle;
        [Tooltip("현재 Target")]
        [SerializeField] private Character target;
        [Tooltip("Target과의 평면 거리")]
        [SerializeField] private float distanceToTarget = float.MaxValue;
        [Tooltip("감지 여부 (detectionRange 이내)")]
        [SerializeField] private bool targetDetected;
        [Tooltip("공격 가능 여부 (쿨타임 + CanStartAttack)")]
        [SerializeField] private bool canAttack;
        [Tooltip("현재 배분된 전투 역할 (AIController가 설정)")]
        [SerializeField] private CombatRole combatRole = CombatRole.Aggressive;

        public float DetectionRange => detectionRange;
        public float LoseTargetRange => loseTargetRange;
        public float AlertDuration => alertDuration;
        public float AttackRange => attackRange;
        public float AttackCooldown => attackCooldown;
        public string AttackActionName => attackActionName;

        /// <summary>현재 AI 판단 상태.</summary>
        public AIState State => aiState;
        /// <summary>현재 Target. 없으면 null.</summary>
        public Character Target => target;
        /// <summary>Target과의 평면 거리 (Target 없으면 float.MaxValue).</summary>
        public float DistanceToTarget => distanceToTarget;
        /// <summary>감지 여부 (detectionRange 이내).</summary>
        public bool TargetDetected => targetDetected;
        /// <summary>공격 가능 여부 (쿨타임 + CanStartAttack).</summary>
        public bool CanAttack => canAttack;

        /// <summary>현재 배분된 전투 역할.</summary>
        public CombatRole CombatRole => combatRole;
        /// <summary>Guard 대기 거리.</summary>
        public float GuardDistance => guardDistance;
        /// <summary>Pressure 대기 거리.</summary>
        public float PressureDistance => pressureDistance;
        /// <summary>근접 예외(방어 공격) 거리.</summary>
        public float TooCloseDistance => tooCloseDistance;

        /// <summary>공격이 끝난(하강 엣지) 순간 발화. AIController가 다음 공격자 선정에 사용.</summary>
        public event System.Action<EnemyCharacter> OnAttackFinished;

        private int _formationSlot;
        private int _formationCount = 1;

        /// <summary>AIController가 전투 역할을 배분하는 진입점(상위 명령). 실행은 이 클래스가 담당.</summary>
        public void SetCombatRole(CombatRole role) => combatRole = role;

        // ─────────── 그랩(체인) 외부 제어 ───────────
        [System.NonSerialized] private bool _grabbed;
        [System.NonSerialized] private CharacterController _grabCC;
        /// <summary>체인 그랩 등으로 외부(CharacterChainGrab)가 이 적을 완전 제어 중인가.</summary>
        public bool IsGrabbed => _grabbed;
        /// <summary>
        /// 그랩 제어 On/Off. On이면 AI·이동·CharacterController를 멈춰 그랩 주체가 위치/애니를 소유한다.
        /// 세 잡기 경로(ChainGrab/Carry/ActionGrab) 공용 단일 진입점 — On이면 무조건 Grab 상태로 전이하고
        /// (CharacterGrabState.Enter가 진행 중 공격의 히트박스/VFX를 취소), Off이고 아직 Grab 상태면
        /// 잠금을 해제한다. 던지기/테이크다운 경로는 직후 ReceiveDamage/ChangeState로 최종 상태를 덮어쓴다.
        /// </summary>
        public void SetGrabbed(bool grabbed)
        {
            _grabbed = grabbed;
            if (_grabCC == null) _grabCC = GetComponent<CharacterController>();
            if (_grabCC != null) _grabCC.enabled = !grabbed;
            if (grabbed)
            {
                CommandManager?.SetMoveInput(Vector2.zero);
                CommandManager?.SetRunHeld(false);
                StateManager?.ChangeState(CharacterStateType.Grab);
            }
            else if (StateManager != null && StateManager.CurrentStateType == CharacterStateType.Grab)
            {
                StateManager.ChangeState(StateManager.ResolveGroundedStateByInput());
            }
        }

        /// <summary>대기 위치 분산용 슬롯 배정(확장 훅). index/total로 깊이 방향 오프셋 계산.</summary>
        public void SetFormationSlot(int index, int total)
        {
            _formationSlot = index;
            _formationCount = Mathf.Max(1, total);
        }

        // ─────────── Spawn Entry / 등장 진입 (공통 대열 합류 + 임시 벽 통과) ───────────

        /// <summary>등장 진입 중 임시로 사용하는 '벽 통과' 레이어 이름(충돌 매트릭스는 EnemyEntranceController가 설정).</summary>
        public const string SpawnEntryPassLayerName = "SpawnEntryPass";

        [System.NonSerialized] private bool _entranceControlled; // 외부 등장 연출(낙하/입장)이 이동을 구동 중 → AI 판단 억제
        [System.NonSerialized] private float _entryEndTime;
        [System.NonSerialized] private bool _wallIgnoreActive;
        [System.NonSerialized] private int _normalBodyLayer = -1;
        [System.NonSerialized] private bool _layersResolved;
        [System.NonSerialized] private int _entryPassLayer = -1;
        [System.NonSerialized] private Animator _entryAnimator;
        [System.NonSerialized] private bool _entrySpeedActive;
        [System.NonSerialized] private float _entryBaseMoveMult = 1f;
        [System.NonSerialized] private float _entryBaseAnimSpeed = 1f;

        /// <summary>공통 진입(대열 합류) 상태인가(공격/공격권 금지, 필요 시 벽 통과).</summary>
        public bool IsEntering => aiState == AIState.Entering;

        /// <summary>스포너가 등장 진입 값을 주입한다(엔트리별 지속시간·벽 통과 여부).</summary>
        public void ConfigureSpawnEntry(float duration, bool ignoreWall)
        {
            if (duration >= 0f) entryDuration = duration;
            ignoreWallDuringEntry = ignoreWall;
        }

        /// <summary>외부 등장 연출(AirDrop 낙하/입장 이동)이 이동을 구동하는 동안 AI 판단을 멈춘다.</summary>
        public void SetEntranceControlled(bool value)
        {
            _entranceControlled = value;
            if (value)
            {
                CommandManager?.SetMoveInput(Vector2.zero);
                CommandManager?.SetGuardHeld(false);
            }
        }

        /// <summary>
        /// 공통 진입(대열 합류) 시작. AirDrop 착지 후 / OffScreen 즉시 호출.
        /// entryDuration 동안 공격·공격권 없이 플레이어 주변 대열로 이동하고, 필요 시 벽을 통과한다.
        /// </summary>
        public void BeginEntering()
        {
            if (IsDead) return;
            _entranceControlled = false;
            _entryEndTime = Time.time + Mathf.Max(0f, entryDuration);

            // 대열 분산 슬롯(대략) — 종료 후 정상 AI에서 AIController가 재배정한다.
            var mgr = AIController.Instance;
            if (mgr != null)
            {
                var cs = mgr.Characters;
                int idx = 0;
                for (int i = 0; i < cs.Count; i++) { if (cs[i] == this) { idx = i; break; } }
                SetFormationSlot(idx, Mathf.Max(1, cs.Count));
            }

            ChangeAIState(AIState.Entering);
            if (ignoreWallDuringEntry) EnterWallIgnore();
            ApplyEntrySpeed(true); // 진입 이동/애니 속도 배수(화면 밖에서 빠르게 합류)

            if (entryDuration <= 0f) ChangeAIState(AIState.Idle); // 즉시 종료 설정이면 곧바로 정상 AI(부수효과 복구 포함)
        }

        // 임시 벽 통과 (Enemy↔Wall만 무시, 바닥/월드는 유지) : 레이어 교체 방식.
        // CharacterController.Move는 Physics.IgnoreCollision(콜라이더쌍)을 무시하므로 레이어 충돌 매트릭스로만 가능.
        // 매트릭스(SpawnEntryPass↔Wall/CharacterBody off, Ground/Default on)는 EnemyEntranceController가 1회 설정한다.
        private void EnterWallIgnore()
        {
            if (_wallIgnoreActive) return;
            ResolveEntryLayer();
            if (_entryPassLayer < 0) return; // 레이어 미정의 → 통과 없이 진행(안전)
            _normalBodyLayer = gameObject.layer;
            gameObject.layer = _entryPassLayer;
            _wallIgnoreActive = true;
        }

        private void ExitWallIgnore()
        {
            if (!_wallIgnoreActive) return;
            gameObject.layer = _normalBodyLayer >= 0 ? _normalBodyLayer : Character.BodyLayer;
            _wallIgnoreActive = false;
        }

        private void ResolveEntryLayer()
        {
            if (_layersResolved) return;
            _entryPassLayer = LayerMask.NameToLayer(SpawnEntryPassLayerName);
            _layersResolved = true;
        }

        // 진입(대열 합류) 중 이동/애니 속도 배수 — 화면 밖에서 빠르게 합류. 이동과 애니를 같은 배수로 올려
        // 발 미끄러짐 없이 '같은 모션 N배'가 되게 한다(animator.speed는 AnimatorTick이 매 프레임 덮어쓰지 않음). 종료 시 원값 복구.
        private void ApplyEntrySpeed(bool on)
        {
            if (on)
            {
                if (_entrySpeedActive || entrySpeedMultiplier <= 0f) return;
                if (Movement != null)
                {
                    _entryBaseMoveMult = Movement.MoveSpeedMultiplier;
                    Movement.SetMoveSpeedMultiplier(_entryBaseMoveMult * entrySpeedMultiplier);
                }
                if (_entryAnimator == null) _entryAnimator = GetComponentInChildren<Animator>(true);
                if (_entryAnimator != null)
                {
                    _entryBaseAnimSpeed = _entryAnimator.speed;
                    _entryAnimator.speed = _entryBaseAnimSpeed * entrySpeedMultiplier;
                }
                _entrySpeedActive = true;
            }
            else
            {
                if (!_entrySpeedActive) return;
                if (Movement != null) Movement.SetMoveSpeedMultiplier(_entryBaseMoveMult);
                if (_entryAnimator != null) _entryAnimator.speed = _entryBaseAnimSpeed;
                _entrySpeedActive = false;
            }
        }

        /// <summary>진입 이탈 시 부수효과(임시 벽 통과 + 이동/애니 속도 배수)를 함께 복구한다.</summary>
        private void ExitEntryEffects()
        {
            ExitWallIgnore();
            ApplyEntrySpeed(false);
        }

        private float _alertEndTime;
        private float _nextAttackTime;
        private float _nextTargetSearchTime;
        private bool _wasAttacking;

        // 가드 반응 고정 유지 창(Time.time 타임스탬프). ShouldReactGuard 충족 시 (재)무장되고,
        // 이 시각까지는 타겟 공격 종료/거리 이탈과 무관하게 가드를 유지한다. 강제반응(피격)은 취소한다.
        private float _guardHoldUntil = -1f;

        // ─────────── 수명 주기 (등록/해제) ───────────

        protected override void OnEnable()
        {
            base.OnEnable(); // Character 초기화 보장 후 등록
            EnsureAnimationEventReceiver(); // Animator에 애니 이벤트 리시버 자동 부착('no receiver' 방지)
            AIController.Register(this);
        }

        protected override void OnDisable()
        {
            ExitEntryEffects();        // 풀 반환/파괴/비활성 안전망 — 벽 통과·속도 배수가 절대 남지 않게
            _entranceControlled = false;
            AIController.Unregister(this);
            // 잔여 명령 정리 — 파괴 중일 수 있으므로 null 허용
            CommandManager?.SetMoveInput(Vector2.zero);
            CommandManager?.SetRunHeld(false);
            base.OnDisable();
        }


        /// <summary>
        /// Visual(Animator)에 AnimationEventHandler가 없으면 런타임 부착.
        /// 구 애니 클립의 AnimationEvent(string) 이벤트가 수신처를 갖게 해 'has no receiver!' 에러를 막는다.
        /// (프리팹마다 수동 부착하는 대신 프레임워크가 일괄 보장.)
        /// </summary>
        private void EnsureAnimationEventReceiver()
        {
            var animator = GetComponentInChildren<Animator>(true);
            if (animator == null) return;
            if (animator.GetComponent<AnimationEventHandler>() == null)
                animator.gameObject.AddComponent<AnimationEventHandler>();
        }


        // ─────────── AI 판단 루프 ───────────

protected override void Update()
        {
            if (_grabbed) return; // 그랩 중엔 CharacterChainGrab가 위치/애니를 완전 제어(AI·이동·상태 정지)
            if (!_entranceControlled) TickAI(); // 등장 연출(낙하/입장 이동) 중엔 AI 판단을 멈추고 이동만 진행
            base.Update();     // StateManager가 같은 프레임에 명령을 소비 (Player: Controller(-20)→Character(-10)와 등가)
        }

        private void TickAI()
        {
            var commands = CommandManager;
            if (commands == null) return;

            // 안전망: 진입(Entering)이 아닌데 진입 부수효과(벽 통과·속도 배수)가 남아있으면 즉시 복구.
            if ((_wallIgnoreActive || _entrySpeedActive) && aiState != AIState.Entering) ExitEntryEffects();

            // 전역 AI 정지(테스트·컷씬): 관리자가 꺼져 있으면 판단 중단
            var manager = AIController.Instance;
            if (manager != null && !manager.AIEnabled)
            {
                commands.SetMoveInput(Vector2.zero);
                return;
            }

            if (IsDead)
            {
                ExitEntryEffects(); // 사망 시 진입 부수효과(벽 통과·속도 배수) 반드시 복구
                commands.SetMoveInput(Vector2.zero);
                return;
            }

            // 강제 반응 상태(Hit/Knockback/Airborne)에서는 AI 명령이 캐릭터 상태를 덮어쓰지 않는다 — 상태 우선(§5).
            if (IsInForcedReactionState())
            {
                // 진입 중 강제 반응(피격 등) → 진입 취소 + 부수효과(벽 통과·속도 배수) 복구 후 정상 흐름으로.
                if (aiState == AIState.Entering) ChangeAIState(AIState.Idle);
                // Movement is locked by the reaction state, so this input never moves the character; we keep
                // pointing pursuit intent at the target so that when the reaction ends the state machine
                // resolves straight to Move instead of a 1-frame Idle -> Move (which flashes the idle clip).
                commands.SetMoveInput(IsTargetValid() ? ComputeChaseInput() : Vector2.zero);
                commands.SetGuardHeld(false);
                _guardHoldUntil = -1f; // 피격/강제반응은 가드 유지 창을 취소한다
                return;
            }
            // 적의 방향은 이동 방향이 아니라 Target 기준 — 이동 입력 자동 플립을 끈다.
            if (Movement != null) Movement.AutoFacingFromInput = false;

            RefreshTarget();
            TickAttackCooldown();

            distanceToTarget = ComputeDistanceToTarget();
            targetDetected = IsTargetValid() && distanceToTarget <= detectionRange;

            // 등장 진입(대열 합류)은 일반 전투 판단·가드보다 우선. 공격·공격권·가드 금지.
            if (aiState == AIState.Entering) { TickEntering(commands); return; }

            // ─── 가드 반응(고정 시간 유지) — AI 판단보다 우선 ───
            // 조건 충족 시 유지 창을 (재)무장한다. 창이 살아있는 동안은 타겟 공격이 끝나거나
            // 거리가 벌어져도 가드를 유지한다(가드가 0.1~0.2초 만에 풀리던 문제 수정).
            // 자신의 공격 중에는 진행 중 공격이 끊기지 않도록 유지하지 않는다(방어 견제 허용).
            bool selfAttacking = Combat != null && Combat.IsAttacking;
            bool superArmorAttack = selfAttacking && Combat.AttackSuperArmorActive;
            if (ShouldReactGuard())
                _guardHoldUntil = Time.time + guardHoldDuration;

            // 가드 유지 창 동안은 가드가 공격보다 우선한다. 진행 중인 일반 공격은 즉시 취소해
            // 이펙트/히트박스를 정리하고 가드로 넘긴다(StopAttack → 다음 틱에 Attack 상태 이탈 → 가드 진입).
            // 슈퍼아머 공격(나찰 등)만 예외 — 끊지 않고 스윙을 마친 뒤 창이 남아있으면 그때 가드한다.
            if (Time.time < _guardHoldUntil && !superArmorAttack)
            {
                if (selfAttacking) Combat.StopAttack();
                commands.SetMoveInput(Vector2.zero);
                commands.SetGuardHeld(true);
                if (IsTargetValid()) FaceTarget();
                return; // 유지 창 동안은 이동/추적/공격 판단을 건너뛰고 가드에 집중
            }
            commands.SetGuardHeld(false); // 유지 창 밖 — 가드 해제. 필요 시 아래 판단에서 다시 올린다

            switch (aiState)
            {
                case AIState.Idle: TickIdle(commands); break;
                case AIState.Alert: TickAlert(commands); break;
                case AIState.Chase: TickChase(commands); break;
                case AIState.Attack: TickAttack(commands); break;
            }

            // Target을 향한 방향 유지 (이동 방향 무관). 공격/강제반응 중이면 FacingLocked/게이팅이 막는다.
            if (IsTargetValid()) FaceTarget();
        }

        private void ChangeAIState(AIState next)
        {
            if (aiState == next) return;
            // 진입 이탈 시 부수효과(임시 벽 통과 + 진입 속도 배수)를 단일 지점에서 정리(누락 방지).
            if (aiState == AIState.Entering) ExitEntryEffects();
            aiState = next;
            if (next == AIState.Alert)
                _alertEndTime = Time.time + alertDuration;
        }

        /// <summary>공격 종료(하강 엣지) 시점부터 쿨타임 계산. 실행/종료 판정은 CharacterCombat 소유.</summary>
        private void TickAttackCooldown()
        {
            bool attacking = Combat != null && Combat.IsAttacking;
            if (_wasAttacking && !attacking)
            {
                _nextAttackTime = Time.time + attackCooldown;
                OnAttackFinished?.Invoke(this); // 공격 종료 통지 — AIController 다음 공격자 선정 트리거
            }
            _wasAttacking = attacking;

            canAttack = !attacking
                && Time.time >= _nextAttackTime
                && Combat != null && Combat.CanStartAttack;
        }

        // ─────────── 상태별 판단 ───────────

        private void TickIdle(CharacterCommandManager commands)
        {
            commands.SetMoveInput(Vector2.zero);

            if (targetDetected)
                ChangeAIState(AIState.Alert);
        }

        private void TickAlert(CharacterCommandManager commands)
        {
            commands.SetMoveInput(Vector2.zero);

            if (!IsTargetValid() || distanceToTarget > loseTargetRange)
            {
                ChangeAIState(AIState.Idle);
                return;
            }

            FaceTarget();

            if (Time.time >= _alertEndTime)
                ChangeAIState(AIState.Chase);
        }

        private void TickChase(CharacterCommandManager commands)
        {
            if (!IsTargetValid() || distanceToTarget > loseTargetRange)
            {
                commands.SetMoveInput(Vector2.zero);
                ChangeAIState(AIState.Idle);
                return;
            }

            // Aggressive만 공격 거리까지 접근해 Attack으로 전이한다.
            if (combatRole == CombatRole.Aggressive)
            {
                if (distanceToTarget <= attackRange)
                {
                    commands.SetMoveInput(Vector2.zero);
                    ChangeAIState(AIState.Attack);
                    return;
                }

                if (Combat != null && Combat.IsAttacking)
                {
                    commands.SetMoveInput(Vector2.zero);
                    return;
                }

                commands.SetMoveInput(ComputeChaseInput());
                return;
            }

            // Guard / Pressure : 배정 거리 유지(대기). 공격 권한 없음.
            FaceTarget();

            // 근접 예외 — 플레이어가 너무 가까우면 비공격자도 제한적 견제(AIController 허가 시).
            // (가드 반응은 TickAI 상단에서 우선 처리되므로 여기 도달 시점엔 가드 유지 창이 없다.)
            if (distanceToTarget <= tooCloseDistance && TryDefensivePoke(commands))
                return;

            if (Combat != null && Combat.IsAttacking)
            {
                commands.SetMoveInput(Vector2.zero);
                return;
            }

            Vector3 standoff = ComputeStandoffPosition(DesiredApproachDistance());
            Vector2 move = ComputeMoveInputTo(standoff);
            commands.SetMoveInput(move.magnitude < 0.15f ? Vector2.zero : move);
        }

        private void TickAttack(CharacterCommandManager commands)
        {
            commands.SetMoveInput(Vector2.zero);

            if (!IsTargetValid())
            {
                ChangeAIState(AIState.Idle);
                return;
            }

            // 역할이 회수되면(더 이상 Aggressive 아님) 공격 상태를 빠져나온다.
            // 진행 중인 공격은 끊지 않는다 — 종료 후 Chase에서 대기 역할로 처리.
            if (combatRole != CombatRole.Aggressive && !(Combat != null && Combat.IsAttacking))
            {
                ChangeAIState(AIState.Chase);
                return;
            }

            FaceTarget();

            // 공격 진행 중이면 종료까지 대기 (종료 판정은 CharacterCombat.TickAttack)
            if (Combat != null && Combat.IsAttacking)
                return;

            if (distanceToTarget > loseTargetRange)
            {
                ChangeAIState(AIState.Idle);
                return;
            }
            if (distanceToTarget > attackRange)
            {
                ChangeAIState(AIState.Chase);
                return;
            }

            if (canAttack)
                commands.RequestAttack(SelectAttackActionName());
        }

        // ─────────── 등장 진입 판단 (공격·공격권·가드 금지) ───────────

        private void TickEntering(CharacterCommandManager commands)
        {
            commands.SetGuardHeld(false); // 진입 중 공격/공격권/가드 전부 금지

            if (Time.time >= _entryEndTime || !IsTargetValid())
            {
                commands.SetMoveInput(Vector2.zero);
                ChangeAIState(AIState.Idle); // 정상 AI(경계/추적/공격/공격권)로 편입 — ExitWallIgnore 포함
                return;
            }

            FaceTarget();

            // 대열 합류: 기존 Guard 대기 위치/이동 계산을 그대로 재사용(별도 임시 AI 없음).
            Vector3 standoff = ComputeStandoffPosition(guardDistance);
            Vector2 move = ComputeMoveInputTo(standoff);
            commands.SetMoveInput(move.magnitude < 0.15f ? Vector2.zero : move);
            // RequestAttack 호출 없음 — 공격/공격권 미획득.
        }

        /// <summary>
        /// 이번 공격에 사용할 AttackAction 이름 선택 지점 (파생 확장 훅).
        /// 기본 구현은 attackActionName 그대로 — 다중 공격/가중치 선택이 필요한 적만 override 한다.
        /// 판단만 담당하며 실행은 기존 명령 경로(RequestAttack)를 그대로 사용한다.
        /// </summary>
        protected virtual string SelectAttackActionName()
        {
            return attackActionName;
        }


        // ─────────── 역할 실행 보조 (Guard/Pressure 대기·근접 예외) ───────────

        /// <summary>현재 역할이 유지하려는 목표 거리.</summary>
        private float DesiredApproachDistance()
        {
            switch (combatRole)
            {
                case CombatRole.Guard: return guardDistance;
                case CombatRole.Pressure: return pressureDistance;
                default: return attackRange;
            }
        }

        /// <summary>Target 기준 desired 거리 + 포메이션 깊이 오프셋을 적용한 대기 목표 위치(월드).</summary>
        private Vector3 ComputeStandoffPosition(float desired)
        {
            if (target == null) return transform.position;

            Vector3 t = target.transform.position;
            Vector3 away = transform.position - t;
            away.y = 0f;
            Vector3 approachDir = away.sqrMagnitude > 0.0001f
                ? away.normalized
                : (Movement != null ? Movement.SplineForward : Vector3.right);

            Vector3 pos = t + approachDir * desired;

            if (Movement != null && _formationCount > 1)
            {
                float offset = (_formationSlot - (_formationCount - 1) * 0.5f) * formationSpacing;
                pos += Movement.SplineDepth * offset;
            }
            return pos;
        }

        /// <summary>월드 목표 지점으로의 이동 입력(스플라인 좌표계 사영). ComputeChaseInput과 동일 규칙.</summary>
        private Vector2 ComputeMoveInputTo(Vector3 worldTarget)
        {
            if (Movement == null) return Vector2.zero;

            Vector3 to = worldTarget - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return Vector2.zero;

            var input = new Vector2(
                Vector3.Dot(to, Movement.SplineForward),
                Vector3.Dot(to, Movement.SplineDepth));

            return input.sqrMagnitude > 1f ? input.normalized : input;
        }

        /// <summary>근접 예외 견제. AIController가 동시 난타를 막기 위해 허가한 경우에만 공격.</summary>
        private bool TryDefensivePoke(CharacterCommandManager commands)
        {
            if (!canAttack) return false;

            var manager = AIController.Instance;
            if (manager != null && !manager.TryReserveDefensiveAttack(this))
                return false;

            commands.RequestAttack(SelectAttackActionName());
            return true;
        }



        // ─────────── Target (후보 제공 = AIController Registry) ───────────

        /// <summary>Target 획득. 씬 전체 검색 대신 AIController Registry에서 적대 후보를 조회.</summary>
        private void RefreshTarget()
        {
            if (IsTargetValid()) return;
            if (Time.time < _nextTargetSearchTime) return;
            _nextTargetSearchTime = Time.time + targetSearchInterval;

            var manager = AIController.Instance;
            if (manager != null)
            {
                target = manager.FindHostileTarget(this);
                return;
            }

            // 관리자 부재 폴백: 상주 Player가 적대 관계면 대상으로 삼는다.
            var gm = GameManager.Instance;
            var candidate = gm != null ? gm.Player : null;
            target = (candidate != null && candidate != this && AIController.IsHostile(this, candidate))
                ? candidate : null;
        }

        private bool IsTargetValid()
        {
            return target != null
                && target != this
                && target.gameObject.activeInHierarchy
                && !target.IsDead
                && AIController.IsHostile(this, target);
        }

        /// <summary>Y를 무시한 평면 거리 (스플라인 최근접 판정과 동일하게 XZ 기준).</summary>
        private float ComputeDistanceToTarget()
        {
            if (target == null) return float.MaxValue;
            Vector3 to = target.transform.position - transform.position;
            to.y = 0f;
            return to.magnitude;
        }

        // ─────────── 이동/방향 계산 (스플라인 좌표계 재사용) ───────────

        /// <summary>
        /// Target 방향을 벨트스크롤 이동 좌표계(X=스플라인 진행, Y=깊이)로 변환.
        /// 월드 X/Z를 그대로 넣지 않고 CharacterMovement의 SplineForward/SplineDepth에 사영한다.
        /// </summary>
        private Vector2 ComputeChaseInput()
        {
            if (Movement == null || target == null) return Vector2.zero;

            Vector3 to = target.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return Vector2.zero;

            var input = new Vector2(
                Vector3.Dot(to, Movement.SplineForward),
                Vector3.Dot(to, Movement.SplineDepth));

            return input.sqrMagnitude > 1f ? input.normalized : input;
        }

        /// <summary>기존 Facing 기능(CharacterMovement.SetFacingRight) 재사용 — 새 Facing 시스템 없음.</summary>
        /// <summary>강제 반응 상태(Hit/Knockback/Airborne)인가 — 이 동안 AI 명령을 중단한다(상태 우선).</summary>
        private bool IsInForcedReactionState()
        {
            var sm = StateManager;
            if (sm == null) return false;
            var s = sm.CurrentStateType;
            return s == CharacterStateType.Hit
                || s == CharacterStateType.Airborne
                || s == CharacterStateType.Knockdown
                || s == CharacterStateType.Groggy
                || s == CharacterStateType.Vulnerable
                || s == CharacterStateType.Executing
                || s == CharacterStateType.Executed;
        }

        /// <summary>
        /// 비공격 역할이 타겟의 공격에 반응해 가드를 시작/연장할 조건인가(판정만, 부수효과 없음).
        /// 실제 가드 유지(고정 시간 창)는 TickAI 상단의 _guardHoldUntil 로직이 담당한다.
        /// </summary>
        private bool ShouldReactGuard()
        {
            if (!enableAiGuard) return false;
            // 역할 무관 반응 가드 — Aggressive 포함 모든 적이 플레이어 근접 공격에 반응해 가드한다.
            // 자기 공격 스윙 중(selfAttacking)에는 TickAI가 유지에서 제외해 진행 중 공격을 끊지 않는다.
            var def = Defense;
            if (def == null || !def.CanGuard) return false;
            if (target == null || target.Combat == null || !target.Combat.IsAttacking) return false;
            if (distanceToTarget > guardReactDistance) return false;
            return true;
        }

        private void FaceTarget()
        {
            if (target == null || Movement == null) return;
            Vector3 to = target.transform.position - transform.position;
            float along = Vector3.Dot(to, Movement.SplineForward);
            if (Mathf.Abs(along) > 0.05f)
                Movement.SetFacingRight(along > 0f);
        }

        private void OnValidate()
        {
            attackRange = Mathf.Max(0.1f, attackRange);
            if (detectionRange < attackRange) detectionRange = attackRange;
            if (loseTargetRange < detectionRange) loseTargetRange = detectionRange;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 center = transform.position;
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.9f); // detection
            Gizmos.DrawWireSphere(center, detectionRange);
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f); // attack
            Gizmos.DrawWireSphere(center, attackRange);
            Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.6f); // lose
            Gizmos.DrawWireSphere(center, loseTargetRange);
        }
#endif
    }
}