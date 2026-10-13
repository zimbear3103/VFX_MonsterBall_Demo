using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 캐릭터 통합 상태 매니저 — 상태 머신 + Animator 브리지 + 의도 조회.
    ///
    /// ※ 컴포넌트 통합(2026-08-19): MonoBehaviour가 아니라 <b>Character가 소유하는 일반 클래스</b>.
    /// transform/Animator/다른 시스템 접근은 Character를 통해 한다.
    /// 상태의 단일 진실의 원천은 CurrentStateType. 명령(의도)의 단일 소유는 CharacterCommandManager.
    ///
    /// 라이프사이클은 Character가 순서대로 호출: OnAwake → OnEnableHook → OnStart → OnUpdate/OnLateUpdate.
    /// </summary>
    [Serializable]
    public class CharacterStateManager
    {
        [Header("Animator")]
        [SerializeField] private Animator animator;
        [SerializeField] private Transform directionReference;

        [Header("Debug")]
        [SerializeField] private bool logStateChanges = true;
        [SerializeField] private CharacterStateType currentStateDisplay;

        [System.NonSerialized] private Character _character;

        // ─────────── 외부 조회 (모두 Character 경유) ───────────
        public CharacterMovement Movement => _character != null ? _character.Movement : null;
        public CharacterCombat Combat => _character != null ? _character.Combat : null;
        public CharacterDefense Defense => _character != null ? _character.Defense : null;
        public CharacterRuntimeStats Stats => _character != null ? _character.Stats : null;
        public CharacterMovement.DashAbility Dash => Movement != null ? Movement.Dash : null;
        /// <summary>구동 중인 Animator(읽기 전용). 상태가 애니 진행도를 참고할 때 사용 (예: 대시 모션 완주 대기). 없으면 null.</summary>
        public Animator Animator => animator;
        private CharacterCommandManager Commands => _character != null ? _character.CommandManager : null;

        /// <summary>소유 Character의 Transform. 처형 접근 이동 등 위치 기반 상태가 사용.</summary>
        public Transform Root => _character != null ? _character.transform : null;

        [System.NonSerialized] private CharacterAfterimage _afterimage;
        [System.NonSerialized] private bool _afterimageResolved;
        /// <summary>캐릭터 잔상(Afterimage) 컴포넌트(있으면). 강공격 대시 캔슬이 사용. 없으면 null(잔상 생략).</summary>
        public CharacterAfterimage Afterimage
        {
            get
            {
                if (!_afterimageResolved)
                {
                    _afterimageResolved = true;
                    _afterimage = _character != null ? _character.GetComponentInChildren<CharacterAfterimage>(true) : null;
                }
                return _afterimage;
            }
        }

        [System.NonSerialized] private CharacterPushEffect _pushEffect;
        [System.NonSerialized] private bool _pushEffectResolved;
        /// <summary>밀림(Push) 이펙트 컴포넌트(있으면). Hit 밀림/가드 밀림 시 Play 호출. 없으면 null(이펙트 생략).</summary>
        public CharacterPushEffect PushEffect
        {
            get
            {
                if (!_pushEffectResolved)
                {
                    _pushEffectResolved = true;
                    _pushEffect = _character != null ? _character.GetComponentInChildren<CharacterPushEffect>(true) : null;
                }
                return _pushEffect;
            }
        }
        
public CharacterStateType CurrentStateType { get; private set; }
        /// <summary>직전 상태. 시작 공격 선택(Running 조건) 등 진입 직전 맥락 판단용.</summary>
        public CharacterStateType PreviousStateType { get; private set; }
        public event Action<CharacterStateType, CharacterStateType> StateChanged;

        // ─── 강제 반응 데이터 (CharacterCombat이 Knockback/Airborne 진입 직전 채움) ───
        public struct ReactionData
        {
            public UnityEngine.Vector3 Direction;
            /// <summary>피격 반응 한 벌(Kind + 공용 힘 + Ground Bounce). CharacterCombat.EnterHitReactionState가
            /// 가드 저항 등 보정을 적용해 채우고, 반응 상태(Knockback/Knockdown/Airborne) Enter가 읽는다.</summary>
            public HitReactionSpec Spec;
            /// <summary>원 공격자 루트. 몸통 충돌(FlyingBodyImpact)의 적대 판정/데미지 출처에 사용.</summary>
            public UnityEngine.GameObject Attacker;
            /// <summary>비행 중 몸통 충돌로 다른 적에게 줄 피해(최종값). 0 = 몸통 충돌 없음 (AttackAction.bodyCollision).</summary>
            public float BodyImpactDamage;
            /// <summary>몸통 충돌 판정 반경(m). 0 이하면 FlyingBodyImpact 기본값 사용.</summary>
            public float BodyImpactRadius;
            /// <summary>체공 중 추가타(저글 유지 히트)인가 — true면 Airborne 재진입 시 AirborneStart 대신
            /// AirDamaged(공중 피격 움찔) 애니를 재생한다. CharacterCombat.EnterHitReactionState가 설정.</summary>
            public bool IsJuggleHit;
            /// <summary>이미 바닥에 누운 상태에서 넉다운으로 넘어오는가(잡기 슬램 해제 등, 2026-09-09) — true면
            /// CharacterKnockdownState가 Slide(밀림)와 낙하 클립(AirborneStart→Loop→End)을 건너뛰고 곧바로 다운 포즈(Knockdown 상태)부터 시작한다.
            /// 슬램으로 이미 바닥에 박힌 적이 다시 공중에 떴다가 떨어지는 모습을 막는다.</summary>
            public bool StartDowned;
        }
        private ReactionData _pendingReaction;
        /// <summary>가장 최근에 지정된 강제 반응(넉백/에어본) 파라미터. Knockback/Airborne 상태 Enter가 읽는다.</summary>
        public ReactionData PendingReaction => _pendingReaction;
        /// <summary>강제 반응 파라미터 설정 — CharacterCombat.ReceiveDamage가 ChangeState 직전에 호출.</summary>
        public void SetPendingReaction(ReactionData data) => _pendingReaction = data;

        /// <summary>
        /// 다운 중 OTG 피격 통지 — 실제로 누워있는(Down/Collapse) 구간이면 누움 타이머를 리셋하고 true.
        /// 상승/기상 등 누워있지 않은 구간이면 false(반응 없음). CharacterCombat.ReceiveDamage 다운 분기가 호출.
        /// </summary>
        /// <summary>
        /// 에어본 상태이면서 아직 체공(Rising) 중인가 — 착지·넘어짐 전. true면 재런치(저글)가 허용된다.
        /// CharacterCombat.ReceiveDamage의 다운 무시 블록이 이 값으로 저글 히트를 통과시킨다.
        /// </summary>
        public bool IsAirborneRising => _current is CharacterAirborneState air && air.IsRising;

        public bool NotifyDownedHit()
        {
            if (_current is CharacterKnockdownState k) return k.OnDownedHit();
            if (_current is CharacterAirborneState a) return a.OnDownedHit();
            return false;
        }

        // ─────────── 의도 위임 (하위호환) ───────────
        public void SetMoveInput(Vector2 input) { if (Commands != null) Commands.SetMoveInput(input); }
        public void SetRunHeld(bool held) { if (Commands != null) Commands.SetRunHeld(held); }
        public void RequestJump() { if (Commands != null) Commands.RequestJump(); }
        public void RequestDash() { if (Commands != null) Commands.RequestDash(); }
        public void RequestAttack(string attackName = "", AttackInputType inputType = AttackInputType.Light) { if (Commands != null) Commands.RequestAttack(attackName, inputType); }

        // ─────────── State가 읽는 값 ───────────
        private Vector2 RawMoveInput => Commands != null ? Commands.MoveInput : Vector2.zero;
        private bool RawRunHeld => Commands != null && Commands.RunHeld;

        public Vector2 MoveInput => DebugMoveOverride ?? RawMoveInput;
        public Vector2 Move => MoveInput;
        public bool HasMoveInput => MoveInput.sqrMagnitude > 0.0001f; // 데드존은 PlayerController가 적용
        public bool RunConditionMet => HasMoveInput && (RawRunHeld || DebugSprintHeld);
        public bool JumpPressed => Commands != null && Commands.JumpRequested;
        public bool DashPressed => Commands != null && Commands.DashRequested;
        public bool AttackPressed => Commands != null && Commands.AttackRequested;
        /// <summary>가드 입력 홀드 여부. CharacterGuardState 진입/유지 판정용.</summary>
        public bool GuardHeld => Commands != null && Commands.GuardHeld;
        /// <summary>절명기(처형) 입력 요청 여부. CheckExecution 진입 판정용.</summary>
        public bool ExecutionPressed => Commands != null && Commands.ExecutionRequested;
        /// <summary>절명기 요청 소비 — 처형 진입 직후 호출.</summary>
        public void ConsumeExecutionRequest() { if (Commands != null) Commands.ConsumeExecution(); }

        /// <summary>이 캐릭터가 가드 가능한가(설정). CheckGuard 진입 판정용.</summary>
        public bool CanGuard => Defense != null && Defense.CanGuard;
        /// <summary>요청된 공격 이름 ("" = 기본 공격). CharacterAttackState가 사용.</summary>
        public string RequestedAttackName => Commands != null ? Commands.RequestedAttackName : "";
        /// <summary>요청된 공격 입력 종류 (Light/Heavy). Transition 매칭용.</summary>
        public AttackInputType RequestedAttackInput => Commands != null ? Commands.RequestedAttackInput : AttackInputType.Light;
        /// <summary>공격 요청 소비(버퍼 포함). 공격 실행 직후 호출.</summary>

        // ─────────── 착지 피드백 컨텍스트 ───────────
        /// <summary>직전 에어본 상태에서 충분한 체공시간(0.2초 이상)이 있었는지. CharacterLandState가 착지 피드백 조건 판정용 읽음.</summary>
        public bool HadSufficientAirtime => _hadSufficientAirtime;
        /// <summary>충분한 체공시간 플래그 설정. CharacterAirborneState가 착지 판정 시 호출.</summary>
        public void SetHadSufficientAirtime(bool value) => _hadSufficientAirtime = value;
        /// <summary>착지 피드백 컨텍스트 리셋. Land 상태 진입/종료 시 호출해 다음 에어본을 대비.</summary>
        public void ResetLandingContext() => _hadSufficientAirtime = false;
        public void ConsumeAttackRequest() { if (Commands != null) Commands.ConsumeAttack(); }
        // ─────────── Combo Buffer 위임 (CharacterAttackState가 사용) ───────────
        /// <summary>예약된 콤보 선입력이 있는가.</summary>
        public bool HasComboReservation => Commands != null && Commands.HasComboReservation;
        /// <summary>예약된 공격 입력 종류 (Transition 매칭용).</summary>
        public AttackInputType ReservedAttackInput => Commands != null ? Commands.ComboReservedInput : AttackInputType.Light;
        /// <summary>예약된 공격 이름 ("" = 기본). 종료 프레임 예약 구제(2026-08-27)에 사용.</summary>
        public string ReservedAttackName => Commands != null ? Commands.ComboReservedName : "";
        /// <summary>공격 중 들어온 입력을 다음 공격 1회로 예약 (단계당 1회만).</summary>
        public void TryReserveComboAttack(AttackInputType inputType, string attackName = "") { if (Commands != null) Commands.TryReserveComboAttack(inputType, attackName); }
        /// <summary>콤보 예약 소비. 다음 타 실행 직후 호출.</summary>
        public void ConsumeComboReservation() { if (Commands != null) Commands.ConsumeComboReservation(); }
        /// <summary>잔여 콤보 예약 정리. Attack 상태 종료 시 호출.</summary>
        public void ClearComboReservation() { if (Commands != null) Commands.ClearComboReservation(); }


        // ─────────── 디버그/자동화 테스트용 ───────────
        public bool DebugSprintHeld { get; set; }
        public Vector2? DebugMoveOverride { get; set; }
        public void DebugQueueJump() { if (Commands != null) Commands.RequestJump(); }
        public void DebugQueueDash() { if (Commands != null) Commands.RequestDash(); }
        public void DebugQueueAttack() { if (Commands != null) Commands.RequestAttack(); }

        // ─────────── 상태 설정값 접근 ───────────
        public float WalkMultiplier => 1f;
        public float RunMultiplier => Stats != null ? Stats.FinalRunMultiplier : 2f;
        public float LandDuration => Stats != null ? Stats.FinalLandDuration : 0.12f;
        public float HitDuration => Stats != null ? Stats.FinalHitDuration : 0.4f;

        private readonly Dictionary<CharacterStateType, CharacterState> _states
            = new Dictionary<CharacterStateType, CharacterState>();
        private CharacterState _current;

        // 착지 피드백용 최소 체공 시간 플래그
        private bool _hadSufficientAirtime;

        public void Bind(Character character) => _character = character;

        public void OnAwake()
        {
            AnimatorAwake();

            _states.Clear();
            RegisterState(new CharacterIdleState());
            RegisterState(new CharacterMoveState());
            RegisterState(new CharacterRunState());
            RegisterState(new CharacterJumpState());
            RegisterState(new CharacterFallState());
            RegisterState(new CharacterLandState());
            RegisterState(new CharacterDashState());
            RegisterState(new CharacterAttackState());
            RegisterState(new CharacterHitState());
            RegisterState(new CharacterGuardState());
            RegisterState(new CharacterChainGrabState());
            RegisterState(new CharacterGrabState());
            RegisterState(new CharacterKnockdownState());
            RegisterState(new CharacterAirborneState());
            RegisterState(new CharacterDeadState());
            RegisterState(new CharacterGroggyState());
            RegisterState(new CharacterVulnerableState());
            RegisterState(new CharacterExecutingState());
            RegisterState(new CharacterExecutedState());
            RegisterState(new CharacterLadderClimbState());
            RegisterState(new CharacterLedgeHangState());
            RegisterState(new CharacterMantleState());


        }

        private void RegisterState(CharacterState state)
        {
            state.Initialize(this);
            _states[state.Type] = state;
        }

        public void OnEnableHook()
        {
            if (Movement != null)
                Movement.AutoJumpFromInput = false;
        }

        public void OnStart()
        {
            _current = _states[CharacterStateType.Idle];
            CurrentStateType = CharacterStateType.Idle;
            currentStateDisplay = CurrentStateType;
            _current.Enter();
            AnimatorStart();
        }

        public void OnUpdate(float dt)
        {
            if (dt <= 0f || _current == null) return;
            _current.Tick(dt);
            currentStateDisplay = CurrentStateType;
            if (Commands != null) Commands.ClearOneShots();
        }

        public void OnLateUpdate()
        {
            AnimatorTick();
        }

        public void ChangeState(CharacterStateType type) => ChangeState(type, false);

        /// <summary>
        /// 상태 전이. allowReenter=true면 같은 상태라도 Exit→Enter를 다시 실행한다
        /// (연속 피격 시 Hit 재진입: 경직 타이머·피격 애니 리셋용).
        /// </summary>
        public void ChangeState(CharacterStateType type, bool allowReenter)
        {
            if (_current != null && CurrentStateType == type && !allowReenter)
                return;

            CharacterStateType previous = CurrentStateType;
            _current?.Exit();
            _current = _states[type];
            PreviousStateType = CurrentStateType;
            CurrentStateType = type;
            _current.Enter();

            if (logStateChanges)
                Debug.Log($"[CharacterState] {previous} → {type}", _character);

            AnimatorOnStateChanged(previous, type);
            StateChanged?.Invoke(previous, type);
        }

        public CharacterStateType ResolveGroundedStateByInput()
        {
            // 가드 홀드 우선 (2026-09-07): 대시/착지 종료 시 가드 키를 누르고 있으면 Run/Move를 거치지 않고 바로 Guard로.
            // (Dash→Run→Guard 1프레임 경유로 달리기 모션이 잠깐 섞이던 문제 방지)
            if (GuardHeld && CanGuard && Movement != null && Movement.IsGrounded)
                return CharacterStateType.Guard;
            if (!HasMoveInput)
                return CharacterStateType.Idle;
            return RunConditionMet ? CharacterStateType.Run : CharacterStateType.Move;
        }

        // ════════════════════════ Animator ════════════════════════
        private readonly HashSet<string> _availableParams = new HashSet<string>();
        private bool _wasDirRunForward;
        private bool _wasDirRunBackward;
        // 방향달리기 애니 유지 유예: 상태가 아주 짧게 Run에서 빠져도(연타 사이 등) 이 시간 동안은
        // Forward Loop를 유지하고 Start 트리거를 재발사하지 않는다 → 뛰기 클립이 매 입력마다
        // 프레임 0부터 재시작(앞부분만 잘려 반복)되는 것을 막는다.
        private float _forwardRunHoldUntil;
        [Header("Directional Run")]
        [Tooltip("방향달리기 애니 유지 유예(초). 연타 사이 짧은 틈에 뛰기 모션이 프레임 0부터 재시작되는 것을 막는다. 낮출수록 정지 후 제자리 뛰기 잔상이 짧아지고, 너무 낮으면 연타 시 재시작 방지가 약해진다. 0 = 유예 없음.")]
        [SerializeField, Range(0f, 0.3f)] private float forwardRunHoldGrace = 0.05f;
        private bool _recoverBlendActive;
        private int _traversalLayerIndex = -1;

        // ─────────── Locomotion Sync (2026-09-06) ───────────
        // 원칙: 입력 → Character State → Animation. Move/Run 상태인데 Animator가 로코모션 상태가 아니면
        // (이전 행동의 후딜/복귀/대시/잡기 애니가 남아 있으면) 로코모션 상태로 직접 CrossFade한다.
        // AnyState Trigger(ReturnMovement/DirRunForwardStart) 대신 CrossFade를 쓰는 이유: Trigger는 진행 중인
        // Transition을 끊지 못하고 armed로 남아 엉뚱한 시점에 발화했다(잔류 트리거 버그 계열). CrossFade는 항상 즉시 끊는다.
        // 보호 상태(Hit/Knockdown/Dead/Executing/Grab/Groggy/Vulnerable…)는 게임 State가 끝나기 전엔 Move/Run이 될 수 없으므로
        // 이 규칙의 영향을 받지 않는다. Idle 복귀는 자연 마무리(Exit Time) 유지 — 여기서 건드리지 않는다.
        [Header("Locomotion Sync")]
        [Tooltip("Move/Run 상태인데 Animator가 아직 이전 행동(후딜·복귀·대시 등) 애니에 있을 때 로코모션으로 섞어 들어가는 CrossFade 시간(초). 낮을수록 즉시 전환, 높을수록 부드럽지만 잔여 모션이 길게 보인다.")]
        [SerializeField, Range(0f, 0.3f)] private float locomotionRecoverFade = 0.1f;
        [Tooltip("Move 상태에서 재생 중이어야 하는 Animator 상태 경로(레이어 이름 제외). 이 목록 밖의 상태에 있으면 walkStatePath/normalWalkStatePath로 CrossFade한다.")]
        [SerializeField] private List<string> walkStatePaths = new List<string> { "BattleMovement.BattleWalk", "BattleMovement.PreBattleWalk", "NormalMovement.NormalWalk" };
        [Tooltip("Move 진입 시 CrossFade할 전투 걷기 상태 경로 (BattleMode = true)")]
        [SerializeField] private string walkStatePath = "BattleMovement.BattleWalk";
        [Tooltip("Move 진입 시 CrossFade할 비전투 걷기 상태 경로 (BattleMode = false)")]
        [SerializeField] private string normalWalkStatePath = "NormalMovement.NormalWalk";
        [Tooltip("Run 상태에서 재생 중이어야 하는 Animator 상태 경로. 이 목록 밖이면 runStartStatePath(첫 진입)/runLoopStatePath(유예 중 재진입)로 CrossFade한다.")]
        [SerializeField] private List<string> runStatePaths = new List<string> { "DirectionalRunning.DirectionalRunningForward_Start", "DirectionalRunning.DirectionalRunningForward_Loop" };
        [Tooltip("Run 첫 진입 시 CrossFade할 상태 경로. 기본 Loop — 구 트리거 경로(AnyState→Loop)와 동일. " +
                 "Start 상태는 DirRunForward=false 이탈 전이가 없어 짧게 달리다 멈추면 Start 클립이 끝날 때까지 달리기 모션이 남으므로 사용하지 않는다.")]
        [SerializeField] private string runStartStatePath = "DirectionalRunning.DirectionalRunningForward_Loop";
        [Tooltip("forwardRunHoldGrace 안에 Run으로 재진입할 때 Start를 건너뛰고 들어갈 Loop 상태 경로")]
        [SerializeField] private string runLoopStatePath = "DirectionalRunning.DirectionalRunningForward_Loop";
        [Tooltip("로코모션 동기화로 CrossFade가 실제 발생할 때 [Animation] 로그 출력")]
        [SerializeField] private bool logLocomotionSync = false;
        [Tooltip("공중 공격(점프킥 등)이 착지로 끝나 Attack→Land가 될 때 CrossFade할 착지 상태 경로. 비우면 Animator의 Grounded 전이에 맡긴다.")]
        [SerializeField] private string landingStatePath = "Jump.JumpLanding";
        [Tooltip("Attack→Land 착지 CrossFade 시간(초)")]
        [SerializeField, Range(0f, 0.2f)] private float landingFade = 0.05f;

        private readonly HashSet<int> _walkStateHashes = new HashSet<int>();
        private readonly HashSet<int> _runStateHashes = new HashSet<int>();
        private int _walkStateHash, _normalWalkStateHash, _runStartStateHash, _runLoopStateHash, _landingStateHash;
        private bool _battleModeParam = true;


        private void AnimatorAwake()
        {
            if (animator == null) animator = _character.GetComponentInChildren<Animator>();

            if (directionReference == null)
            {
                var visual = _character.transform.Find("Visual");
                directionReference = visual != null ? visual
                    : (animator != null ? animator.transform : _character.transform);
            }

            _availableParams.Clear();
            if (animator != null)
            {
                foreach (var p in animator.parameters)
                    _availableParams.Add(p.name);
            }

            BuildLocomotionHashes();
        }

        /// <summary>로코모션 동기화용 상태 fullPath 해시 준비 (레이어 0 이름 + 경로).</summary>
        private void BuildLocomotionHashes()
        {
            _walkStateHashes.Clear();
            _runStateHashes.Clear();
            if (animator == null || animator.layerCount == 0) return;
            string prefix = animator.GetLayerName(0) + ".";
            int Hash(string path) => string.IsNullOrEmpty(path) ? 0 : Animator.StringToHash(prefix + path);
            foreach (var p in walkStatePaths) { int h = Hash(p); if (h != 0) _walkStateHashes.Add(h); }
            foreach (var p in runStatePaths) { int h = Hash(p); if (h != 0) _runStateHashes.Add(h); }
            _walkStateHash = Hash(walkStatePath);
            _normalWalkStateHash = Hash(normalWalkStatePath);
            _runStartStateHash = Hash(runStartStatePath);
            _runLoopStateHash = Hash(runLoopStatePath);
            _landingStateHash = Hash(landingStatePath);
            // 목표 상태는 항상 허용 집합에도 포함 (인스펙터에서 목록/목표를 따로 바꿔도 자기 자신으로 재진입하지 않게)
            if (_walkStateHash != 0) _walkStateHashes.Add(_walkStateHash);
            if (_normalWalkStateHash != 0) _walkStateHashes.Add(_normalWalkStateHash);
            if (_runStartStateHash != 0) _runStateHashes.Add(_runStartStateHash);
            if (_runLoopStateHash != 0) _runStateHashes.Add(_runLoopStateHash);
        }

        /// <summary>
        /// Move/Run 상태와 Animator 로코모션 상태를 일치시킨다 — 상태가 정본, 애니가 따라온다.
        /// Move: walkStatePaths 밖이면 걷기 상태로 CrossFade. Run: runStatePaths 밖이면 달리기 Start(첫 진입)/Loop(유예 재진입)로 CrossFade.
        /// 전이 중이면 목적지 상태로 판정한다(이미 로코모션으로 가는 중이면 건드리지 않음).
        /// 호출: 상태 전이 직후(AnimatorOnStateChanged) + 매 LateUpdate(AnimatorTick) 가드 —
        /// 다른 시스템의 CrossFade/Trigger가 덮어써도 다음 프레임에 현재 State에 맞게 복구된다(State에 맞지 않는 애니 요청은 결과적으로 무시).
        /// </summary>
        private void SyncLocomotionAnimation()
        {
            var movement = Movement;
            if (animator == null || movement == null || !animator.isActiveAndEnabled) return;
            bool isMove = CurrentStateType == CharacterStateType.Move;
            bool isRun = CurrentStateType == CharacterStateType.Run;
            if (!isMove && !isRun) return;
            if (!movement.IsGrounded) return;
            if (animator.speed <= 0f) return; // 히트스톱/정지 중엔 양보
            if (_traversalLayerIndex >= 0 && animator.GetLayerWeight(_traversalLayerIndex) > 0.5f) return; // 사다리 오버라이드 중

            // Move인데 방금 Run에서 빠진 짧은 틈(forwardRunHoldGrace)이면 달리기 Loop 유지 — 연타 재시작 방지 유예 존중.
            if (isMove && _wasDirRunForward && Time.unscaledTime < _forwardRunHoldUntil) return;

            bool inTransition = animator.IsInTransition(0);
            var info = inTransition ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
            var allowed = isRun ? _runStateHashes : _walkStateHashes;
            if (allowed.Count == 0 || allowed.Contains(info.fullPathHash)) return;

            int target;
            if (isRun)
                target = _wasDirRunForward && _runLoopStateHash != 0 ? _runLoopStateHash : _runStartStateHash;
            else
                target = _battleModeParam || _normalWalkStateHash == 0 ? _walkStateHash : _normalWalkStateHash;
            if (target == 0 || !animator.HasState(0, target)) return;

            // 복귀 트리거 잔류 정리 — CrossFade 뒤에 armed 트리거가 발화해 로코모션을 자기 자신으로 재시작시키는 것 방지.
            if (_availableParams.Contains(AnimParams.ReturnMovement)) animator.ResetTrigger(AnimParams.ReturnMovement);
            ResetDirRunTriggers();

            animator.CrossFadeInFixedTime(target, locomotionRecoverFade, 0);
            if (isRun)
            {
                SetAnimBool(AnimParams.DirRunForward, true);
                SetAnimBool(AnimParams.DirRunBackward, false);
                _wasDirRunForward = true;
                _wasDirRunBackward = false;
                _forwardRunHoldUntil = Time.unscaledTime + forwardRunHoldGrace;
            }
            if (logLocomotionSync || logStateChanges)
                Debug.Log($"[Animation] {CurrentStateType}: locomotion sync → {(isRun ? (target == _runLoopStateHash ? runLoopStatePath : runStartStatePath) : (target == _walkStateHash ? walkStatePath : normalWalkStatePath))} (was {(inTransition ? "→" : "")}{info.shortNameHash})", _character);
        }

        private void AnimatorStart()
        {
            SetAnimBool(AnimParams.Alive, true);
            SetAnimBool(AnimParams.IsAlive, true);
            _battleModeParam = Combat == null || Combat.BattleMode;
            SetAnimBool(AnimParams.BattleMode, _battleModeParam);
            if (animator != null)
                animator.speed = Stats != null ? Stats.FinalGlobalAnimatorSpeed : 1f;
            ApplyGroundedAnimState(CurrentStateType);
        }

        private void AnimatorTick()
        {
            var movement = Movement;
            if (animator == null || movement == null)
                return;

            float speed = movement.HorizontalSpeed;
            // Locomotion blend is driven by intended speed whenever we are in a move state with input but
            // still ramping up — regardless of how we got here. This covers reaction -> Idle -> Move on
            // enemies (AI re-engages a frame late, so the old fromReaction/_recoverBlendActive gate missed it
            // and the character slid in its idle pose). Presentation only; gameplay state is already correct.
            float blendSpeed = ResolveRecoveryBlendSpeed(speed);
            SetAnimFloat(AnimParams.MoveSpeed, blendSpeed);
            // 공중 시작 공격(점프킥 등) 중엔 Grounded를 false로 보류 (2026-09-06):
            // 공격 애니 상태의 유일한 출구가 Grounded→Idle 전이라 착지 시 애니메이터가 먼저 Idle로 새던 문제.
            // 착지는 CharacterAttackState가 Land 전이로 결정하고 AnimatorOnStateChanged가 착지 모션으로 CrossFade한다. 급강하는 제외(Combat 판단).
            bool holdAirborne = CurrentStateType == CharacterStateType.Attack && Combat != null && Combat.ShouldHoldAirborneAnim;
            SetAnimBool(AnimParams.Grounded, movement.IsGrounded && !holdAirborne);
            SetAnimFloat(AnimParams.WalkSpeedMultiplier, Stats != null ? Stats.FinalWalkAnimationSpeed : 1f);

            if (speed > 0.05f && directionReference != null)
            {
                Vector3 local = directionReference.InverseTransformVector(movement.HorizontalVelocity);
                Vector3 dir = local.normalized;
                SetAnimFloat(AnimParams.RelForward, dir.z);
                SetAnimFloat(AnimParams.RelLateral, dir.x);
            }
            else
            {
                SetAnimFloat(AnimParams.RelForward, 0f);
                SetAnimFloat(AnimParams.RelLateral, 0f);
            }

            // 상태→애니 가드: Move/Run인데 로코모션 애니가 아니면 즉시 복구 (전이 직후 1회 + 매 프레임 가드).
            SyncLocomotionAnimation();
            DriveDirectionalRun();
        }

private float ResolveRecoveryBlendSpeed(float actualSpeed)
        {
            var movement = Movement;
            bool locomotion = CurrentStateType == CharacterStateType.Move || CurrentStateType == CharacterStateType.Run;
            if (movement == null || !locomotion || !HasMoveInput)
            {
                _recoverBlendActive = false;
                return actualSpeed;
            }
            float mult = CurrentStateType == CharacterStateType.Run ? RunMultiplier : WalkMultiplier;
            float intended = movement.EffectiveMoveSpeed * mult;
            if (actualSpeed >= intended * 0.98f)
            {
                _recoverBlendActive = false;
                return actualSpeed;
            }
            return Mathf.Max(actualSpeed, intended);
        }


        private void DriveDirectionalRun()
        {
            // Run 여부는 게임 상태가 정본 (2026-09-06: speed > 0.05 게이트 제거 — 행동 직후 첫 프레임 속도 0으로
            // 달리기 애니가 한 프레임 늦게 시작되던 원인). 실제 진입 CrossFade는 SyncLocomotionAnimation이 담당하고
            // 여기서는 Loop 유지/이탈(DirRunForward Bool)과 유예만 관리한다. Start 트리거 경로는 더 이상 쓰지 않는다.
            bool isRun = CurrentStateType == CharacterStateType.Run;

            // 플레이어 자유 이동은 Facing이 항상 입력 방향을 따라가므로 늘 '정방향 질주'만 재생한다.
            // 뒷걸음(Backward) 서브그래프는 사용하지 않는다 — 속도/스케일 기반 방향 판정이 방향 전환 시
            // 꼿꼿한 뒷걸음 클립으로 오판·고착되던 원인이라, Backward는 항상 꺼 두고 트리거도 남기지 않는다.
            // (Backward Start는 Base Layer AnyState 전이라 armed 잔여가 곧 오전이로 이어진다.)
            if (!isRun)
            {
                // 짧은 틈(연타 사이 등)에는 방향달리기 애니를 바로 끊지 않고 유지한다.
                // Start 트리거를 재발사하지 않아 Loop가 이어지므로 뛰기 모션이 프레임 0부터
                // 재시작되지 않는다. 유예가 지나면 완전히 해제.
                if (_wasDirRunForward && Time.unscaledTime < _forwardRunHoldUntil)
                {
                    SetAnimBool(AnimParams.DirRunForward, true);
                    SetAnimBool(AnimParams.DirRunBackward, false);
                    return;
                }
                SetAnimBool(AnimParams.DirRunForward, false);
                SetAnimBool(AnimParams.DirRunBackward, false);
                ResetDirRunTriggers();
                _wasDirRunForward = false;
                _wasDirRunBackward = false;
                return;
            }

            // 달리기 진입 CrossFade는 SyncLocomotionAnimation이 수행했다(Start/Loop 직접 진입). 트리거는 잔류 방지를 위해 항상 해제.
            ResetDirRunTriggers();

            SetAnimBool(AnimParams.DirRunForward, true);
            SetAnimBool(AnimParams.DirRunBackward, false);

            _wasDirRunForward = true;
            _wasDirRunBackward = false;
            _forwardRunHoldUntil = Time.unscaledTime + forwardRunHoldGrace;
        }

        /// <summary>방향달리기 Start 트리거(Base Layer AnyState 구동)를 모두 해제 — armed 잔여로 인한 오전이/고착 방지.</summary>
        private void ResetDirRunTriggers()
        {
            if (animator == null) return;
            if (_availableParams.Contains(AnimParams.DirRunForwardStart)) animator.ResetTrigger(AnimParams.DirRunForwardStart);
            if (_availableParams.Contains(AnimParams.DirRunBackwardStart)) animator.ResetTrigger(AnimParams.DirRunBackwardStart);
        }

        /// <summary>Animator BattleMode 파라미터 반영만 담당. 값의 소유는 CharacterCombat.</summary>
        public void SetBattleMode(bool value)
        {
            _battleModeParam = value;
            SetAnimBool(AnimParams.BattleMode, value);
        }

        // ─────────── Traversal Animator 브리지 (State가 호출; 파라미터 없으면 _availableParams 가드로 무시) ───────────
        /// <summary>사다리 붙음 상태 bool. LadderClimb Enter/Exit에서 호출.</summary>
        // [롤백 2026-08-26] 사다리 애니메이션 구동 보류 — 이동/등반은 유지, 애니 파라미터는 건들지 않음.
        /// <summary>사다리 붙음 상태 bool(기존 컸트롤러 "Climbing") — 서브머신 내부가 이걸로 등반 경로 분기. LadderClimb Enter/Exit에서 호출.</summary>
        public void SetOnLadder(bool on)
        {
            SetAnimBool(AnimParams.OnLadder, on); // OnLadder = "Climbing"(Bool)
            if (!on) SetAnimFloat(AnimParams.LadderClimbSpeed, 0f);
        }

        /// <summary>TraversalLadder 오버라이드 레이어 가중치 설정(사다리 중에만 1). 이름으로 인덱스 자동 해석.</summary>
        private void SetTraversalLayerWeight(float w)
        {
            if (animator == null) return;
            if (_traversalLayerIndex < 0)
            {
                for (int i = 0; i < animator.layerCount; i++)
                    if (animator.GetLayerName(i) == "TraversalLadder") { _traversalLayerIndex = i; break; }
            }
            if (_traversalLayerIndex >= 0) animator.SetLayerWeight(_traversalLayerIndex, w);
        }
        /// <summary>사다리 상하 이동 파라미터(+1 위 / -1 아래). 블렌드/재생속도 동기화용.</summary>
        public void SetLadderClimbParam(float signedSpeed) => SetAnimFloat(AnimParams.LadderClimbSpeed, signedSpeed);

        /// <summary>사다리 진입 트리거 — 하단/상단 진입에 따라 기존 서브 상태머신 진입 트리거를 발사.</summary>
        /// <summary>사다리 진입 — 하단진입(올라가기 시작)=ClimbBottomLadder, 상단진입(내려가기 시작)=ClimbingLadderTop.</summary>
        /// <summary>사다리 진입 — 하단진입(올라가기)=ClimbBottomLadder, 상단진입(내려가기)=ClimbingLadderTop.</summary>
        public void FireLadderEnter(bool atBottom)
        {
            string trig = atBottom ? AnimParams.ClimbEnterBottom : AnimParams.ClimbEnterTop;
            UnityEngine.Debug.Log($"[Ladder] FireLadderEnter atBottom={atBottom} -> SetTrigger('{trig}') paramExists={_availableParams.Contains(trig)}");
            SetAnimTrigger(trig);
        }

        /// <summary>사다리 이동 방향 트리거 — 올라갈 때=ClimbLadderBottomUp, 내려갈 때=ClimbLadderBottomDown. 방향이 바뀔 때만 호출.</summary>
        public void FireLadderClimbDir(int dir)
        {
            if (dir > 0) SetAnimTrigger(AnimParams.ClimbGoUp);
            else if (dir < 0) SetAnimTrigger(AnimParams.ClimbGoDown);
        }

        /// <summary>사다리 상하 방향 트리거 버퍼 초기화 — 버튼을 뗄 순간 선입력된 트리거를 무시해 한 번 더 재생되지 않게 한다.</summary>
        public void ResetLadderDirTriggers()
        {
            if (animator == null) return;
            if (_availableParams.Contains(AnimParams.ClimbGoUp)) animator.ResetTrigger(AnimParams.ClimbGoUp);
            if (_availableParams.Contains(AnimParams.ClimbGoDown)) animator.ResetTrigger(AnimParams.ClimbGoDown);
        }

        /// <summary>사다리 이탈 트리거 — 상단/하단 이탈.</summary>
        /// <summary>사다리 탈출 — 최상단 도착=ClimbLadderTopExit, 최하단 도착=ClimbLadderBottomExit.</summary>
        public void FireLadderExit(bool atTop) => SetAnimTrigger(atTop ? AnimParams.ClimbExitTop : AnimParams.ClimbExitBottom);

        /// <summary>사다리 등반 애니 이벤트(climbingUp/Down) 수신 — 현재 상태가 LadderClimb면 한 칸 이동 요청. dir +1 위 / -1 아래.</summary>
        public void NotifyClimbStep(int dir)
        {
            if (_current is CharacterLadderClimbState ladder) ladder.RequestClimbStep(dir);
        }

        /// <summary>사다리 탈출 애니 종료 이벤트(ladderExitEnd) 수신 — 하차 잠금을 그 시점에 정확히 해제.</summary>
        public void NotifyLadderExitEnd()
        {
            if (_current is CharacterLadderClimbState ladder) ladder.NotifyExitAnimationEnd();
        }

        /// <summary>사다리 상단 올라서기 트리거.</summary>
        public void SetLadderTopExit() => SetAnimTrigger(AnimParams.LadderTopExit);
        /// <summary>난간 매달림 상태 bool. LedgeHang Enter/Exit에서 호출.</summary>
        public void SetLedgeHang(bool on) { if (on) SetAnimTrigger(AnimParams.LedgeHangEnter); else SetAnimTrigger(AnimParams.LedgeHangExit); }
        /// <summary>맨틀(난간 올라서기) 트리거. Mantle Enter에서 호출.</summary>
        public void TriggerMantle() => SetAnimTrigger(AnimParams.Mantle);

        // ─────────── 잡기/처형 Animator 브리지 (State·CharacterActionGrab이 호출) ───────────

        /// <summary>처형 시전 애니 — ExecuteType Int + Execute 트리거 (공유 컨트롤러 Execute 서브머신 진입).</summary>
        public void PlayExecuteAnimation(int executeType)
        {
            SetAnimInt(AnimParams.ExecuteType, executeType);
            SetAnimTrigger(AnimParams.Execute);
        }

        /// <summary>피처형 애니 — Executed 트리거. CharacterExecutedState.Enter가 호출.</summary>
        public void PlayExecutedAnimation() => SetAnimTrigger(AnimParams.Executed);

        /// <summary>잡기(ActionGrab) 시전 애니 — GrabType Int + ActionGrab 트리거.</summary>
        public void PlayActionGrabAnimation(int grabType)
        {
            SetAnimInt(AnimParams.GrabType, grabType);
            SetAnimTrigger(AnimParams.ActionGrab);
        }

        /// <summary>잡힘(피격자) 애니 — ActionGrabbed 트리거. CharacterActionGrab이 지연 호출.</summary>
        public void PlayActionGrabbedAnimation() => SetAnimTrigger(AnimParams.ActionGrabbed);

        /// <summary>임의 Animator State 직접 재생(CrossFade). 처형 접근 대시 등 연출 상태 진입용.</summary>
        public void CrossFadeState(string statePath, float fadeDuration)
        {
            if (animator != null && !string.IsNullOrEmpty(statePath))
                animator.CrossFadeInFixedTime(statePath, fadeDuration, 0);
        }

        /// <summary>공용 트리거 발사(파라미터 존재 가드 포함). 연출 시퀀스가 사용.</summary>
        public void FireTrigger(string triggerName) => SetAnimTrigger(triggerName);
        /// <summary>공용 Bool 설정(파라미터 존재 가드 포함). 연출 시퀀스가 사용.</summary>
        public void SetBoolParam(string boolName, bool value) => SetAnimBool(boolName, value);
        /// <summary>공용 트리거 해제(파라미터 존재 가드 포함). 소비되지 않고 래치된 트리거 정리용.</summary>
        public void ResetTrigger(string triggerName)
        { if (animator != null && _availableParams.Contains(triggerName)) animator.ResetTrigger(triggerName); }

        private void AnimatorOnStateChanged(CharacterStateType previous, CharacterStateType next)
        {
            // ─ 우선순위 상태(Dead)는 Animator AnyState 트리거(dur 0)로 즉시 반영 ─
            if (next == CharacterStateType.Dead)
            {
                SetAnimBool(AnimParams.Alive, false);
                SetAnimBool(AnimParams.IsAlive, false);
                SetAnimTrigger(AnimParams.Death);
            }
            // ─ 부활(Dead 이탈): Alive 복원 + 잔여 Death 트리거 제거 ─
            // 컨트롤러의 Dead 서브머신은 Alive==true가 되어야 Revive를 거쳐 로코모션으로 복귀한다.
            // 이 복원이 없으면 부활 후에도 사망 루프 모션이 계속 재생된다. (2026-08-27)
            else if (previous == CharacterStateType.Dead)
            {
                SetAnimBool(AnimParams.Alive, true);
                SetAnimBool(AnimParams.IsAlive, true);
                if (animator != null && _availableParams.Contains(AnimParams.Death))
                    animator.ResetTrigger(AnimParams.Death);
            }
            // ─ Move/Run 진입 시 로코모션 애니 동기화 (2026-09-06) ─
            // 이전 상태가 무엇이든(Attack/Hit/Dash/Grab/Executing/Idle 경유 포함) Move/Run이 되면 Animator가 로코모션이
            // 아닐 때 직접 CrossFade한다. 구 방식(previous 화이트리스트 + ReturnMovement AnyState Trigger, Move 한정)은
            // Idle 경유·Dash·Grab·Run 복귀를 놓치고 진행 중 Transition을 못 끊어 잔류 트리거를 남겼다.
            // (Idle 복귀는 자연 마무리 유지 — 끝까지 재생.) 실제 CrossFade는 함수 끝(ApplyGroundedAnimState 뒤)에서.

            // Recovery blend window: after a forced-stop reaction the physical speed restarts from 0,
            // so the locomotion blend tree would momentarily show its idle pose. Drive the blend param
            // by intended speed until actual speed catches up. Presentation only; gameplay state already correct.
            bool fromReaction = previous == CharacterStateType.Hit || previous == CharacterStateType.Attack
                || previous == CharacterStateType.Airborne || previous == CharacterStateType.Knockdown
                || previous == CharacterStateType.Vulnerable;
            if (fromReaction && (next == CharacterStateType.Move || next == CharacterStateType.Run))
            {
                _recoverBlendActive = true;
                if (logStateChanges) Debug.Log($"[Animation] {previous} -> {next} (recovery-blend)", _character);
            }
            else if (next != CharacterStateType.Move && next != CharacterStateType.Run)
            {
                _recoverBlendActive = false;
            }

            // Guard 자세: 스탠스 애니는 설정 시에만(구버전 플레이어는 반응만). 반응(GuardDamaged)은 Defense가 담당.
            var guardDef = Defense;
            if (guardDef != null && guardDef.PlayGuardStanceAnimation)
            {
                SetAnimBool(AnimParams.Guarding, next == CharacterStateType.Guard);
                if (next == CharacterStateType.Guard) SetAnimTrigger(AnimParams.GuardStart);
                if (previous == CharacterStateType.Guard && next != CharacterStateType.Guard) SetAnimTrigger(AnimParams.GuardStop);
            }
            // ─ 반응 상태 진입 시 잔류 이동 트리거 리셋 (2026-08-28) ─
            // Attack→Move 등이 쏜 ReturnMovement(및 DirRun Start) 트리거는 Animator가 전이 중이면
            // 소비되지 못하고 armed로 남는다(ExecuteAttack의 2026-08-27 리셋과 동일한 문제).
            // 이 잔류 트리거가 남은 채 반응 애니(AirborneStart CrossFade 등)에 진입하면 다음 프레임에
            // AnyState→BattleMovement[ReturnMovement] 전이가 발동해 상태는 Knockdown/Knockback인데
            // 모션만 Idle로 끌려가는 버그가 된다(약공 4타 직후 넉다운 피격 시 idle 모션). 진입 직전에 제거한다.
            bool toReaction = next == CharacterStateType.Hit
                || next == CharacterStateType.Knockdown || next == CharacterStateType.Airborne
                || next == CharacterStateType.Vulnerable;
            if (toReaction && animator != null)
            {
                if (_availableParams.Contains(AnimParams.ReturnMovement)) animator.ResetTrigger(AnimParams.ReturnMovement);
                if (_availableParams.Contains(AnimParams.DirRunForwardStart)) animator.ResetTrigger(AnimParams.DirRunForwardStart);
                if (_availableParams.Contains(AnimParams.DirRunBackwardStart)) animator.ResetTrigger(AnimParams.DirRunBackwardStart);
            }
            // ─ 그로기(투혼 0 무력화): 공유 컨트롤러의 Groggy Bool + StartGroggy 트리거로 루프 진입/해제 ─
            if (next == CharacterStateType.Groggy)
            {
                SetAnimBool(AnimParams.Groggy, true);
                SetAnimTrigger(AnimParams.GroggyStart);
            }
            else if (previous == CharacterStateType.Groggy)
            {
                SetAnimBool(AnimParams.Groggy, false);
                if (next != CharacterStateType.Executed && next != CharacterStateType.Dead)
                    SetAnimTrigger(AnimParams.ReturnMovement);
            }
            // ─ 취약(퍼펙트 패링당함): Parried 애니 유지 → 타이머 만료로 Idle 복귀 시 ReturnMovement로 빠져나온다 ─
            // (Move/Run 복귀와 피격/사망 전이는 각각 위/해당 반응 애니가 처리하므로 Idle만 여기서.)
            else if (previous == CharacterStateType.Vulnerable && next == CharacterStateType.Idle)
            {
                SetAnimTrigger(AnimParams.ReturnMovement);
            }
            // Airborne(넉다운) 애니는 방향별(AirborneForward/Backward) + Stun bool을 CharacterAirborneState가 Combat 경유로 구동한다.
            if (next == CharacterStateType.Jump)
                SetAnimTrigger(AnimParams.Jumping);
            // 공중 공격이 착지로 끝나 Land가 되면 착지 애니로 직접 CrossFade (2026-09-06):
            // 공격 상태의 Grounded 전이는 BattleMovement(Idle)로 바로 가서 착지 모션이 안 나온다. 상태가 정본 — Land면 착지 애니.
            if (next == CharacterStateType.Land && previous == CharacterStateType.Attack
                && animator != null && _landingStateHash != 0 && animator.HasState(0, _landingStateHash))
            {
                animator.CrossFadeInFixedTime(_landingStateHash, landingFade, 0);
            }
            if (next == CharacterStateType.Dash)
            {
                SetAnimInt(AnimParams.DashDirection, 1);
                SetAnimTrigger(AnimParams.Dash);
            }
            ApplyGroundedAnimState(next);

            // 상태가 정본: Move/Run이 되었으면 Animator를 즉시 로코모션으로 (Idle은 자연 마무리).
            if (next == CharacterStateType.Move || next == CharacterStateType.Run)
                SyncLocomotionAnimation();
        }

        private void ApplyGroundedAnimState(CharacterStateType state)
        {
            SetAnimBool(AnimParams.Walking, state == CharacterStateType.Move);
            SetAnimBool(AnimParams.Running, false);
        }

        private void SetAnimFloat(string name, float value)
        { if (animator != null && _availableParams.Contains(name)) animator.SetFloat(name, value); }
        private void SetAnimBool(string name, bool value)
        { if (animator != null && _availableParams.Contains(name)) animator.SetBool(name, value); }
        private void SetAnimTrigger(string name)
        { if (animator != null && _availableParams.Contains(name)) animator.SetTrigger(name); }
        private void SetAnimInt(string name, int value)
        { if (animator != null && _availableParams.Contains(name)) animator.SetInteger(name, value); }
    }
}
