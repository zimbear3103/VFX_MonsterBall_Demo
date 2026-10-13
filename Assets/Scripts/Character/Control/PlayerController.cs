using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 플레이어 제어 계층 — Unity Input System과 Character 사이의 Adapter.
    ///
    /// 책임:
    ///   - InputSystem 액션 소유·해석
    ///   - 사람의 입력을 캐릭터 '명령'으로 변환해 조종 대상의 CharacterCommandManager에 전달
    ///     (SetMoveInput / SetRunHeld / RequestJump / RequestDash / RequestAttack)
    ///   - 달리기 조건 해석(더블탭/Sprint/스틱 강도)은 플레이어의 입력 감각이므로 여기서 처리
    ///   - 현재 조종 Character 관리 (Possess — CharacterControllerBase)
    ///
    /// 직접 하지 않는 것 (기획 6/38):
    ///   CharacterController.Move / AttackHitbox 활성 / Animator 공격 재생 / Damage 계산 / State 강제 수정.
    ///
    /// 실행 순서: DefaultExecutionOrder(-20)으로 CharacterStateManager(-10)보다 먼저 Update되어
    ///   PlayerController가 명령을 세팅 → StateManager가 상태 Tick → Movement(0)가 이동을 수행한다.
    ///
    /// 이 클래스가 구 Player(입력 Facade)를 대체한다. Player는 캐릭터에 붙었지만
    /// PlayerController는 캐릭터와 분리된 오브젝트로 두고 JangHyu를 Possess한다.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class PlayerController : CharacterControllerBase
    {
        /// <summary>Run 조건 판정 방식 (Player 입력 감각).</summary>
        public enum RunConditionMode
        {
            DoubleTap,      // 같은 방향 더블탭으로 달리기 시작, 입력을 놓으면 해제 (구 프로젝트 방식)
            SprintButton,   // Sprint 액션 홀드
            StickMagnitude, // 스틱 입력 강도가 runStickThreshold 이상이면 Run
            AlwaysRun,      // 이동 입력이 있으면 항상 Run
            Never           // Run 사용 안 함
        }

        [Header("Input Actions")]
        [Tooltip("프로젝트 InputActionAsset. 비워두면 코드에서 기본 바인딩을 생성한다.")]
        [SerializeField] private InputActionAsset actionsAsset;

        [Tooltip("사용할 Action Map 이름")]
        [SerializeField] private string actionMapName = "Player";
        [SerializeField] private string moveActionName = "Move";
        [SerializeField] private string jumpActionName = "Jump";
        [SerializeField] private string sprintActionName = "Sprint";
        [SerializeField] private string dashActionName = "Dash";
        [SerializeField] private string guardActionName = "Guard";
        [SerializeField] private string chainGrabActionName = "ChainGrab";
        [SerializeField] private string executionActionName = "Execution";


        [Header("Input Thresholds")]
        [Tooltip("이 크기 이상의 이동 입력만 '이동 중'으로 판정 (데드존 겸용)")]
        [SerializeField, Range(0.01f, 0.5f)] private float moveInputThreshold = 0.1f;

        [Tooltip("스틱을 빠르게 좌우로 꺾을 때 그 사이 스틱이 순간적으로 중앙(0)을 지나며 이동이 끊기는 것을 막는 유예 시간(초). 이 시간 안에 다시 입력이 들어오면 직전 방향을 유지해 Idle로 튕기지 않는다. 0 = 유예 없음(기존 동작).")]
        [SerializeField, Range(0f, 0.2f)] private float moveReleaseGrace = 0.06f;

        [Header("Run")]
        [Tooltip("Run 조건 방식. 기본 = 같은 방향 더블탭 (구 프로젝트 방식).")]
        [SerializeField] private RunConditionMode runMode = RunConditionMode.DoubleTap;

        [Tooltip("더블탭 인정 시간 (초). 첫 탭 후 이 시간 안에 같은 방향을 다시 누르면 달리기.")]
        [SerializeField, Range(0.1f, 0.8f)] private float doubleTapWindow = 0.45f;

        [Tooltip("대시 키(RT/P)를 계속 누른 채 방향 입력이 있으면 달리기 (더블탭 보조). 누르는 순간엔 대시가 먼저 나가고, 홀드가 이어지면 달리기로 연결된다.")]
        [SerializeField] private bool dashHoldRun = true;

        [Tooltip("StickMagnitude 방식일 때 Run으로 판정할 스틱 입력 강도")]
        [SerializeField, Range(0.5f, 1f)] private float runStickThreshold = 0.9f;

        [Header("Dash Chord")]
        [Tooltip("대시 키를 누른 채 이 입력 종류의 공격 버튼을 누르면 withDash 공격 요청 → ComboGroup StarterState.DashChord(대시 강공격) 매칭. 달리기 상태에서도 매칭된다.")]
        [SerializeField] private AttackInputType dashChordAttackInput = AttackInputType.Heavy;
        [Tooltip("약공격(Light) 버튼도 대시 코드 대상으로 취급. 대시 키와 약공격을 동시에(또는 대시 직후 dashChordWindow 안에) 누르면 withDash 약공격 요청 → StarterState.DashChord 그룹(Run_LightCombo) 매칭. (2026-09-14)")]
        [SerializeField] private bool dashChordLightAttack = true;
        [Tooltip("대시 키를 누른 뒤 이 시간(초) 안에 공격 버튼이 오면 대시를 취소하고 대시 강공격으로. 이 시간만큼 대시가 보류된다. 0 = 보류 없음(대시 즉시).")]
        [SerializeField, Range(0f, 0.2f)] private float dashChordWindow = 0.08f;

        [Header("Attack Bindings")]
        [Tooltip("버튼 ↔ 공격 매핑. attackName = AttackAction.AttackName (비우면 첫 번째 = 기본 공격). 새 공격 추가 = 항목 추가")]
        [SerializeField] private List<AttackBinding> attackBindings = new List<AttackBinding>
        {
            new AttackBinding
            {
                actionName = "Attack",
                attackName = "",
                inputType = AttackInputType.Light,
                fallbackBindings = new[] { "<Gamepad>/buttonWest", "<Keyboard>/j", "<Mouse>/leftButton" }
            },
            new AttackBinding
            {
                actionName = "HeavyAttack",
                attackName = "HeavyAttack",
                inputType = AttackInputType.Heavy,
                fallbackBindings = new[] { "<Gamepad>/buttonNorth", "<Keyboard>/h" }
            },
            new AttackBinding
            {
                actionName = "HandCannon",
                attackName = "HandCannon",
                inputType = AttackInputType.Skill1,
                fallbackBindings = new[] { "<Gamepad>/leftTrigger", "<Keyboard>/y" }
            }
        };

        [Header("Heavy Hold")]
        [Tooltip("강공격 버튼(Y)을 이 시간(초) 이상 누르고 있으면 HeavyHold 입력으로 분기 (ComboGroup StarterInput=HeavyHold / Transition input=HeavyHold). " +
                 "미만으로 짧게 눌렀다 떼면 '뗀 순간' 기존 강공격(Heavy)이 나간다. 대시 키 홀드 + Y(코드)는 홀드 판정 없이 즉시 발행.")]
        [SerializeField, Range(0.05f, 1f)] private float heavyHoldThreshold = 0.3f;
        [Tooltip("홀드 임계 도달 시 HeavyHold 시작 그룹/분기가 없으면(공중 등) 일반 강공격(Heavy)으로 강등")]
        [SerializeField] private bool heavyHoldFallbackToHeavy = true;

        [Header("Hand Cannon Hold")]
        [Tooltip("손대포 버튼을 이 시간(초) 이상 누르고 있으면 회전 베기(차지)로 전환. 미만으로 짧게 눌렀다 떼면 기존 손대포 단발. 구버전 StateBasedComboWeapon.BranchThreshold(0.5) 이식.")]
        [SerializeField, Range(0.1f, 2f)] private float handCannonHoldThreshold = 0.5f;
        [Tooltip("짧은 누름에 해당하는 공격 이름 — attackBindings에서 이 attackName 항목이 홀드 분기 대상이 된다")]
        [SerializeField] private string handCannonAttackName = "HandCannon";
        [Tooltip("홀드 임계 도달 시 실행할 회전 베기 공격 이름 (AttackAction.AttackName)")]
        [SerializeField] private string handCannonSpinAttackName = "HandCannonSpin";
        [Tooltip("차지 중 버튼을 떼면 발동시킬 Animator 트리거 (차지 루프 → 회전 베기 본동작)")]
        [SerializeField] private string handCannonSpinBurstTrigger = "SpinAttackBurst";
        [Tooltip("회전 베기 차지 시작 후 이 시간(초)이 지나면 버튼을 계속 눌러도 자동 발사. 0 = 무제한 홀드 (구버전 MaxHoldDuration).")]
        [SerializeField, Min(0f)] private float handCannonMaxHoldDuration = 2f;
        [Tooltip("차지 중 시간 배율 (1 = 슬로우 없음). 구버전 ChargeSlowTimeScale(0.9) 이식. 발사/취소 시 자동 복원.")]
        [SerializeField, Range(0.1f, 1f)] private float handCannonChargeSlowScale = 0.9f;

        /// <summary>입력 버튼 ↔ 공격 이름 매핑 항목.</summary>
        [System.Serializable]
        public class AttackBinding
        {
            [Tooltip("InputActionAsset의 액션 이름. 에셋에 없으면 fallbackBindings로 자체 생성")]
            public string actionName = "Attack";
            [Tooltip("실행할 공격 이름 (AttackAction.AttackName). 비우면 기본 공격(attacks[0])")]
            public string attackName = "";
            [Tooltip("이 버튼의 공격 입력 종류 (Transition의 RequiredInput 매칭용). Light = 약공격, Heavy = 강공격")]
            public AttackInputType inputType = AttackInputType.Light;
            [Tooltip("액션이 에셋에 없을 때 생성할 바인딩 경로")]
            public string[] fallbackBindings = new string[0];
            [System.NonSerialized] public InputAction action;
            [System.NonSerialized] public bool owns;
        }

        // ─────────── 입력 액션 ───────────

        private InputAction _moveAction;
        private InputAction _jumpAction;
        private InputAction _sprintAction;
        private InputAction _dashAction;
        private bool _ownsDashAction;
        private InputAction _guardAction;
        private bool _ownsGuardAction;
        private InputAction _chainGrabAction;
        private bool _ownsChainGrabAction;
        private InputAction _executionAction;
        private bool _ownsExecutionAction;

        private bool _ownsActions; // 폴백으로 직접 생성한 액션인지 (직접 생성 시 Dispose 책임)

        // 손대포 홀드 분기 내부 상태 (구버전 ChargeMode 이식: 탭=단발, 홀드=차지→해제 시 회전 베기)
        private bool _hcHeld;
        private float _hcPressTime;
        private bool _hcSpinRequested;
        private bool _hcBurstFired;
        private bool _hcSpinWasActive;   // 이번 홀드에서 회전 베기가 실제로 시작됐는가
        private float _hcSpinStartTime;  // 회전 베기 시작 시각 (unscaled) — 자동 발사 타이머 기준
        private bool _hcSpinAcceptReady; // 이전 회전 베기가 아직 재생 중일 때 그 잔상을 '이번' 회전으로 오인하지 않기 위한 가드

        // 강공격(Y) 탭/홀드 분기 내부 상태 (탭=뗀 순간 Heavy, 홀드 임계=HeavyHold)
        private bool _hvHeld;
        private float _hvPressTime;

        // 더블탭 달리기 내부 상태
        private bool _prevHasMoveInput;
        private int _lastTapDirection = -1;
        private float _lastTapTime = -999f;
        private bool _runLatched;
        // 대시+공격 동시 입력(코드) 내부 상태
        private bool _dashPending;          // 대시 키가 눌렸고 코드 확인을 위해 보류 중
        private readonly List<AttackBinding> _chordBindings = new List<AttackBinding>(); // 이번 프레임의 대시 코드 대상 바인딩(Heavy/Light)
        private float _dashPressTime = -999f;

        // 이동 입력 디바운스 상태 — 빠른 좌우 전환 중 스틱이 순간 중앙(0)을 지나도
        // 직전 방향을 짧게 유지해 Move→Idle→Move 튕김을 막는다.
        private Vector2 _heldMove;
        private bool _hasHeldMove;
        private float _lastMoveInputTime = -999f;

        private void Awake()
        {
            ResolveActions();
        }

        private void OnEnable()
        {
            ResolveActions();
            _moveAction?.Enable();
            _jumpAction?.Enable();
            _sprintAction?.Enable();
            _dashAction?.Enable();
            _guardAction?.Enable();
            _chainGrabAction?.Enable();
            _executionAction?.Enable();

            foreach (var b in attackBindings) b?.action?.Enable();
        }

        private void OnDisable()
        {
            _moveAction?.Disable();
            _jumpAction?.Disable();
            _sprintAction?.Disable();
            _dashAction?.Disable();
            _guardAction?.Disable();
            _chainGrabAction?.Disable();
            _executionAction?.Disable();

            foreach (var b in attackBindings) b?.action?.Disable();

            if (_ownsDashAction)
            {
                _dashAction?.Dispose();
                _dashAction = null;
                _ownsDashAction = false;
            }
            if (_ownsGuardAction)
            {
                _guardAction?.Dispose();
                _guardAction = null;
                _ownsGuardAction = false;
            }

            if (_ownsChainGrabAction)
            {
                _chainGrabAction?.Dispose();
                _chainGrabAction = null;
                _ownsChainGrabAction = false;
            }

            if (_ownsExecutionAction)
            {
                _executionAction?.Dispose();
                _executionAction = null;
                _ownsExecutionAction = false;

            }


            foreach (var b in attackBindings)
            {
                if (b != null && b.owns)
                {
                    b.action?.Dispose();
                    b.action = null;
                    b.owns = false;
                }
            }

            if (_ownsActions)
            {
                _moveAction?.Dispose();
                _jumpAction?.Dispose();
                _sprintAction?.Dispose();
                _moveAction = null;
                _jumpAction = null;
                _sprintAction = null;
                _ownsActions = false;
            }
        }

        private void Update()
        {
            if (Commands == null)
                return;

            // ① 이동 입력 (데드존 + '순간 0' 디바운스)
            //   빠르게 좌우로 스틱을 꺾으면 그 사이 스틱이 중앙(0)을 지나는 프레임이 생긴다.
            //   그 0을 그대로 전달하면 상태머신이 Move→Idle→Move로 튕겨 달리기/Idle 모션이
            //   깜빡이거나 갇힌다. moveReleaseGrace 동안 직전 방향을 유지해 '실제 정지'와 구분한다.
            Vector2 move = ResolveMoveInput(out bool hasMove, out bool rawHasMove);
            Commands.SetMoveInput(move);


            // ② 달리기 조건 해석 → CommandManager에 전달
            //   더블탭 판정은 디바운스 전 원시 입력(rawHasMove)으로 — 유예(moveReleaseGrace)가 짧은 놓음을 흡수해
            //   두 번째 탭이 인식되지 않던 문제 수정 (2026-09-04)
            Commands.SetRunHeld(ResolveRunHeld(move, hasMove, rawHasMove));

            // ③ 원샷 명령 (눌린 프레임에만 요청)
            if (_jumpAction != null && _jumpAction.WasPressedThisFrame())
                Commands.RequestJump();
            // 대시 키 홀드 + 공격 버튼(코드) — 코드 대상 바인딩이 있으면 그 공격 요청에 withDash를 실어 보낸다
            bool hasChord = CollectChordBindings();
            bool dashPressedNow = _dashAction != null && _dashAction.WasPressedThisFrame();
            if (!hasChord)
            {
                if (dashPressedNow) Commands.RequestDash();
            }
            else
            {
                UpdateDashChord(dashPressedNow);
            }

            var controlledCombat = ControlledCharacterComponent != null ? ControlledCharacterComponent.Combat : null;
            foreach (var b in attackBindings)
            {
                if (b == null || b.action == null) continue;
                // 홀드 차징 판정용 버튼 눌림 상태 전달 (AttackAction.UsePowerCharge 공격에서 사용)
                controlledCombat?.SetAttackInputHeld(b.inputType, b.action.IsPressed());
                // 손대포 바인딩은 탭/홀드 분기를 별도로 처리 (짧게=단발, 길게=회전 베기 차지)
                if (b.attackName == handCannonAttackName) { UpdateHandCannonHold(b); continue; }
                // 코드 대상 바인딩은 UpdateDashChord가 요청을 대신 발행
                if (_chordBindings.Contains(b)) continue;
                // 강공격 바인딩은 탭/홀드 분기 (짧게=Heavy, 길게=HeavyHold)
                if (b.inputType == AttackInputType.Heavy)
                {
                    if (b.action.WasPressedThisFrame()) BeginHeavyHold();
                    UpdateHeavyTapHold(b);
                    continue;
                }
                if (b.action.WasPressedThisFrame())
                    Commands.RequestAttack(b.attackName, b.inputType);
            }

            // 가드 홀드 (지속 입력)
            Commands.SetGuardHeld(_guardAction != null && _guardAction.IsPressed());

            // 체인 그랩(LB) 원샷
            if (_chainGrabAction != null && _chainGrabAction.WasPressedThisFrame())
                Commands.RequestChainGrab();

            // 절명기(처형) 원샷 — R3 / 키보드 Left Ctrl (구버전 ExecutionButton)
            if (_executionAction != null && _executionAction.WasPressedThisFrame())
                Commands.RequestExecution();

        }

        /// <summary>
        /// 손대포 탭/홀드 분기 (구버전 StateBasedComboWeapon.ChargeMode 이식):
        ///   짧게 눌렀다 뗌(threshold 미만) → 뗀 순간 기존 손대포 단발 (구버전 ResolvePrimary와 동일 타이밍)
        ///   threshold 도달 → 회전 베기(차지) 공격 실행 — 차지 루프 애니메이션 유지
        ///   차지 중 버튼 뗌 → SpinAttackBurst 트리거로 회전 본동작 발동 (구버전 ResolveSecondary)
        ///   FP 부족 시 → 구버전처럼 단발 발사로 강등
        /// 실제 공격 내용은 전부 AttackAction("HandCannonSpin")이 소유하고, 여기는 입력 해석만 담당한다.
        /// </summary>
        private void UpdateHandCannonHold(AttackBinding b)
        {
            var character = ControlledCharacterComponent;

            if (b.action.WasPressedThisFrame())
            {
                _hcHeld = true;
                _hcPressTime = Time.unscaledTime;
                _hcSpinRequested = false;
                _hcBurstFired = false;
                _hcSpinWasActive = false;
            }

            // 홀드 임계 도달 → 회전 베기 요청 (FP 부족이면 단발로 강등 — 구버전 _secondaryFPAvailable 분기)
            if (_hcHeld && !_hcSpinRequested && b.action.IsPressed()
                && Time.unscaledTime - _hcPressTime >= handCannonHoldThreshold)
            {
                _hcSpinRequested = true;
                // 요청 시점에 '이전' 회전 베기가 아직 재생 중이면, 그것이 끝난 뒤에만 새 회전으로 인정
                _hcSpinAcceptReady = !(character != null && character.Combat != null
                    && character.Combat.IsAttacking
                    && character.Combat.CurrentAttackName == handCannonSpinAttackName);
                if (CanAffordSpin(character))
                    Commands.RequestAttack(handCannonSpinAttackName, b.inputType);
                else
                    Commands.RequestAttack(b.attackName, b.inputType);
            }

            // 짧은 누름 → 뗀 순간 단발 발사
            if (_hcHeld && b.action.WasReleasedThisFrame())
            {
                _hcHeld = false;
                if (!_hcSpinRequested)
                    Commands.RequestAttack(b.attackName, b.inputType);
            }

            // 회전 베기 진행 상태 추적 (차지 = 회전 베기 실행 중 && Burst 미발동)
            bool spinNameActive = character != null && character.Combat != null
                && character.Combat.IsAttacking
                && character.Combat.CurrentAttackName == handCannonSpinAttackName;
            if (!spinNameActive)
                _hcSpinAcceptReady = true; // 이전 회전이 끝남 → 다음 활성부터 새 회전으로 인정
            bool spinActive = _hcSpinRequested && _hcSpinAcceptReady && spinNameActive;
            if (spinActive && !_hcSpinWasActive)
            {
                _hcSpinWasActive = true;
                _hcSpinStartTime = Time.unscaledTime; // 자동 발사 타이머 리셋 (매 회전마다 새로 시작)
                // 이전 사용에서 소비되지 않고 래치된 Burst 트리거 정리 — 차지 진입 즉시 오발사 방지
                character.StateManager?.ResetTrigger(handCannonSpinBurstTrigger);
            }
            bool charging = spinActive && !_hcBurstFired;

            // 차지 중 슬로우모션 (구버전 UseChargeSlowMotion 이식) — 매 프레임 짧게 갱신해 유지,
            // 발사/취소로 charging이 끝나면 easeOut으로 자연 복원 (FeedbackTime 단일 작성자와 호환)
            if (charging && handCannonChargeSlowScale < 1f)
                Aiara.FeedbackTime.SlowMotion(handCannonChargeSlowScale, 0.05f, 0.02f, 0.05f);

            // 발동 조건: ① 차지 중 버튼 해제 (요청 → Attack 상태 진입 사이의 프레임 어긋남도 커버)
            //           ② 최대 홀드 시간 초과 시 자동 발사 (구버전 MaxHoldDuration, 0 = 무제한)
            bool released = !b.action.IsPressed();
            bool maxHoldReached = handCannonMaxHoldDuration > 0f
                && Time.unscaledTime - _hcSpinStartTime >= handCannonMaxHoldDuration;
            if (charging && (released || maxHoldReached))
            {
                character.StateManager?.FireTrigger(handCannonSpinBurstTrigger);
                _hcBurstFired = true;
            }
        }

        /// <summary>대시 코드 대상 공격 바인딩 (dashChordAttackInput 종류, 손대포 제외). 없으면 null.</summary>
/// <summary>대시 코드 대상 공격 바인딩 수집 (dashChordAttackInput 종류 + dashChordLightAttack 시 Light, 손대포 제외). 하나라도 있으면 true.</summary>
        private bool CollectChordBindings()
        {
            _chordBindings.Clear();
            foreach (var b in attackBindings)
            {
                if (b == null || b.action == null) continue;
                if (b.attackName == handCannonAttackName) continue;
                if (b.inputType == dashChordAttackInput || (dashChordLightAttack && b.inputType == AttackInputType.Light))
                    _chordBindings.Add(b);
            }
            return _chordBindings.Count > 0;
        }

        /// <summary>
        /// 대시 키 홀드 + 공격 버튼(코드) 판정 (2026-09-04):
        ///   공격 버튼이 눌린 순간 대시 키가 눌려 있으면 withDash=true로 요청 (달리기 중 = RT 홀드 중이므로 자동 매칭).
        ///   대시 키를 막 누른 직후(dashChordWindow 안)에 공격이 오면 보류 중이던 대시를 취소하고 코드 공격만 낸다.
        ///   창이 지나도록 공격이 없으면 대시 발행. 공격 버튼 자체는 지연 없음.
        /// </summary>
/// <summary>
        /// 대시 키 + 공격 버튼(코드) 판정 (2026-09-04, Light 확장 2026-09-14):
        ///   공격 버튼이 눌린 순간 대시 키가 눌려 있거나(홀드/동시 입력) 대시가 코드 보류 중이면(막 누른 뒤 dashChordWindow 안, 탭 후 뗐어도)
        ///   withDash=true로 요청하고 보류 중이던 대시를 취소한다 (달리기 중 = RT 홀드 중이므로 자동 매칭).
        ///   창이 지나도록 공격이 없으면 대시 발행. 공격 버튼 자체는 지연 없음.
        /// </summary>
        private void UpdateDashChord(bool dashPressedNow)
        {
            float now = Time.unscaledTime;
            if (dashPressedNow) { _dashPending = true; _dashPressTime = now; }

            bool dashHeld = _dashAction != null && _dashAction.IsPressed();
            for (int i = 0; i < _chordBindings.Count; i++)
            {
                var chord = _chordBindings[i];
                if (chord.action.WasPressedThisFrame())
                {
                    bool withDash = dashHeld || _dashPending;
                    if (withDash)
                    {
                        _dashPending = false; // 코드로 소비 → 보류 중 대시 취소
                        _hvHeld = false;      // 코드 공격은 홀드 판정 없이 즉시
                        Commands.RequestAttack(chord.attackName, chord.inputType, true);
                    }
                    else if (chord.inputType == AttackInputType.Heavy)
                        BeginHeavyHold();     // 강공격 버튼 단독 → 탭/홀드 분기
                    else
                        Commands.RequestAttack(chord.attackName, chord.inputType, false);
                }
                if (chord.inputType == AttackInputType.Heavy)
                    UpdateHeavyTapHold(chord);
            }

            if (_dashPending && now - _dashPressTime >= dashChordWindow)
            {
                _dashPending = false;
                Commands.RequestDash();
            }
        }

        private void BeginHeavyHold()
        {
            _hvHeld = true;
            _hvPressTime = Time.unscaledTime;
        }

        /// <summary>
        /// 강공격(Y) 탭/홀드 분기 (2026-09-04):
        ///   짧게 눌렀다 뗌(heavyHoldThreshold 미만) → 뗀 순간 기존 강공격(Heavy) 요청 (손대포 탭과 동일 타이밍)
        ///   heavyHoldThreshold 도달 → HeavyHold 입력 요청 (ComboGroup StarterInput=HeavyHold → 홀드 강공격)
        ///   HeavyHold 시작 그룹/분기가 없으면(공중, 콤보 중 분기 없음 등) heavyHoldFallbackToHeavy 시 Heavy로 강등.
        /// 실제 공격 내용은 AttackAction/ComboGroup 데이터가 소유하고, 여기는 입력 해석만 담당한다.
        /// </summary>
        private void UpdateHeavyTapHold(AttackBinding b)
        {
            if (!_hvHeld) return;

            if (b.action.IsPressed())
            {
                if (Time.unscaledTime - _hvPressTime < heavyHoldThreshold) return;
                _hvHeld = false;
                var combat = ControlledCharacterComponent != null ? ControlledCharacterComponent.Combat : null;
                bool canHold = combat != null && (combat.IsAttacking
                    ? combat.HasAnyBranchForInput(AttackInputType.HeavyHold)
                    : combat.ResolveStarter(AttackInputType.HeavyHold, Commands.MoveInput) != null);
                if (canHold || !heavyHoldFallbackToHeavy)
                    Commands.RequestAttack("", AttackInputType.HeavyHold);
                else
                    Commands.RequestAttack(b.attackName, b.inputType);
                return;
            }

            // 임계 전 뗌 → 탭 = 기존 강공격
            _hvHeld = false;
            Commands.RequestAttack(b.attackName, b.inputType);
        }

        /// <summary>회전 베기 실행에 필요한 FP가 있는지 확인. 액션이 없으면 false.</summary>
        private bool CanAffordSpin(Character character)
        {
            if (character == null || character.Combat == null) return false;
            var spin = character.Combat.FindAttack(handCannonSpinAttackName);
            if (spin == null) return false;
            return spin.FPCost <= 0f || character.HasEnoughFP(spin.FPCost);
        }

        protected override void OnUnPossessed(CharacterCommandManager previous)
        {
            // 조종 해제 시 남은 이동 명령이 캐릭터를 계속 밀지 않도록 정리
            previous?.SetMoveInput(Vector2.zero);
            previous?.SetRunHeld(false);
            _runLatched = false;
            _prevHasMoveInput = false;
            _hasHeldMove = false;
            _heldMove = Vector2.zero;
            _dashPending = false;
        }

        /// <summary>
        /// 원시 스틱 입력에 데드존과 '순간 0' 디바운스를 적용해 정제된 이동 입력을 돌려준다.
        ///   · 데드존 이상: 그대로 사용하고 직전 방향·시각을 기록.
        ///   · 데드존 미만이라도 moveReleaseGrace 안: 직전 방향 유지(빠른 좌우 전환의 중앙 통과 흡수).
        ///   · 유예 시간 초과: 실제 정지로 보고 0 반환.
        /// 적(AI)은 이 경로를 쓰지 않으므로 영향받지 않는다(적은 자기 Commands에 직접 MoveInput 설정).
        /// </summary>
        private Vector2 ResolveMoveInput(out bool hasMove, out bool rawHasMove)
        {
            Vector2 raw = _moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero;
            rawHasMove = raw.magnitude >= moveInputThreshold;

            if (rawHasMove)
            {
                _heldMove = raw;
                _hasHeldMove = true;
                _lastMoveInputTime = Time.unscaledTime;
                hasMove = true;
                return raw;
            }

            // 데드존 미만 — 유예 시간 안이면 직전 방향 유지(Idle로 튕기지 않음)
            if (_hasHeldMove && Time.unscaledTime - _lastMoveInputTime <= moveReleaseGrace)
            {
                hasMove = true;
                return _heldMove;
            }

            // 유예 시간 경과 → 실제 정지
            _hasHeldMove = false;
            _heldMove = Vector2.zero;
            hasMove = false;
            return Vector2.zero;
        }

        /// <summary>현재 프레임의 '달리기 홀드' 신호를 계산한다. StateManager가 이동 입력과 AND한다.</summary>
        private bool ResolveRunHeld(Vector2 move, bool hasMove, bool rawHasMove)
        {
            bool sprintHeld = _sprintAction != null && _sprintAction.IsPressed();
            // 대시 키 홀드 달리기 (2026-09-04): RT를 누른 채 방향 입력 → 달리기. 대시 직후 홀드가 이어지면 Run으로 연결.
            // 단, 누른 프레임과 코드 판정 보류(dashChordWindow) 중에는 달리기로 치지 않는다 (2026-09-07):
            // 보류 동안 RunHeld=true가 되면 Move→Run 전이로 달리기 모션이 먼저 잠깐 나온 뒤 대시가 나가던 문제.
            // 이 함수는 UpdateDashChord보다 먼저 호출되므로 _dashPending 외에 WasPressedThisFrame도 함께 본다.
            // 가드 홀드 중에는 대시 키 홀드를 달리기로 치지 않는다 (2026-09-07): 가드+대시 동시 홀드 = 가드 우선.
            // 가드 자세에서 대시(탭)는 CharacterGuardState.CheckDash로 계속 가능하다.
            bool guardHeld = _guardAction != null && _guardAction.IsPressed();
            if (dashHoldRun && !guardHeld && _dashAction != null && _dashAction.IsPressed()
                && !_dashPending && !_dashAction.WasPressedThisFrame())
                sprintHeld = true;

            switch (runMode)
            {
                case RunConditionMode.DoubleTap:
                    UpdateDoubleTapRun(move, hasMove, rawHasMove);
                    return _runLatched || sprintHeld; // Sprint 홀드는 보조 수단으로 계속 허용
                case RunConditionMode.SprintButton:
                    return sprintHeld;
                case RunConditionMode.StickMagnitude:
                    return move.magnitude >= runStickThreshold;
                case RunConditionMode.AlwaysRun:
                    return hasMove;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 더블탭 달리기 (구 프로젝트 방식):
        /// 뉴트럴 → 방향 입력 시작을 '탭'으로 기록하고,
        /// doubleTapWindow 안에 같은 방향을 다시 탭하면 달리기 시작.
        /// 입력을 놓으면 달리기가 해제되고 다시 더블탭이 필요하다.
        /// </summary>
        private void UpdateDoubleTapRun(Vector2 move, bool hasMove, bool rawHasMove)
        {
            // 탭 감지는 원시 입력 기준 (유예로 유지된 가짜 '누름'은 탭 시작으로 안 침)
            if (rawHasMove && !_prevHasMoveInput)
            {
                int direction = SnapDirection(move);
                if (direction == _lastTapDirection
                    && Time.unscaledTime - _lastTapTime <= doubleTapWindow)
                {
                    _runLatched = true;
                }
                _lastTapDirection = direction;
                _lastTapTime = Time.unscaledTime;
            }

            // 래치 해제는 디바운스된 입력 기준 (빠른 좌우 전환의 순간 0으로 달리기가 풀리지 않게)
            if (!hasMove)
                _runLatched = false;

            _prevHasMoveInput = rawHasMove;
        }

        /// <summary>입력을 4방향으로 스냅 (0=우, 1=좌, 2=상, 3=하). 대각선은 우세 축 기준.</summary>
        private static int SnapDirection(Vector2 input)
        {
            if (Mathf.Abs(input.x) >= Mathf.Abs(input.y))
                return input.x >= 0f ? 0 : 1;
            return input.y >= 0f ? 2 : 3;
        }

        // ─────────── 입력 액션 해석 (구 Player.ResolveActions 이관) ───────────

        private void ResolveActions()
        {
            if (_moveAction != null && _jumpAction != null)
                return;

            if (actionsAsset != null)
            {
                var map = actionsAsset.FindActionMap(actionMapName, throwIfNotFound: false);
                if (map != null)
                {
                    _moveAction = map.FindAction(moveActionName, throwIfNotFound: false);
                    _jumpAction = map.FindAction(jumpActionName, throwIfNotFound: false);
                    _sprintAction = map.FindAction(sprintActionName, throwIfNotFound: false); // 없으면 null 허용
                    _dashAction = map.FindAction(dashActionName, throwIfNotFound: false);
                    _guardAction = map.FindAction(guardActionName, throwIfNotFound: false);
                    _chainGrabAction = map.FindAction(chainGrabActionName, throwIfNotFound: false);
                    _executionAction = map.FindAction(executionActionName, throwIfNotFound: false);

                    foreach (var b in attackBindings)
                        if (b != null && b.action == null)
                            b.action = map.FindAction(b.actionName, throwIfNotFound: false);
                }

                if (_moveAction == null || _jumpAction == null)
                {
                    Debug.LogWarning(
                        $"[PlayerController] '{actionMapName}/{moveActionName}' 또는 " +
                        $"'{actionMapName}/{jumpActionName}' 액션을 에셋에서 찾지 못해 기본 바인딩으로 폴백합니다.",
                        this);
                }
            }

            if (_moveAction == null || _jumpAction == null)
                CreateFallbackActions();

            // 에셋에 Dash 액션이 없으면 자체 생성.
            // 바인딩은 구 yeolhadiary 실제 플레이 기준: 패드 RT / 키보드 P
            if (_dashAction == null)
            {
                _dashAction = new InputAction("Dash", InputActionType.Button);
                _dashAction.AddBinding("<Gamepad>/rightTrigger"); // Xbox RT
                _dashAction.AddBinding("<Keyboard>/p");
                _ownsDashAction = true;
            }

            // 에셋에 Guard 액션이 없으면 자체 생성. 바인딩: 패드 RB / 키보드 I (가드=홀드, 저스트프레임 시작=패링)
            if (_guardAction == null)
            {
                _guardAction = new InputAction("Guard", InputActionType.Button);
                _guardAction.AddBinding("<Gamepad>/rightShoulder");
                _guardAction.AddBinding("<Keyboard>/i");
                _ownsGuardAction = true;
            }

            // 에셋에 ChainGrab 액션이 없으면 자체 생성. 바인딩: 패드 LB / 키보드 Y
            if (_chainGrabAction == null)
            {
                _chainGrabAction = new InputAction("ChainGrab", InputActionType.Button);
                _chainGrabAction.AddBinding("<Gamepad>/leftShoulder");
                _chainGrabAction.AddBinding("<Keyboard>/y");
                _ownsChainGrabAction = true;
            }

            // 에셋에 Execution 액션이 없으면 자체 생성. 바인딩: 패드 R3(rightStickPress) / 키보드 Left Ctrl (구버전 ExecutionButton)
            if (_executionAction == null)
            {
                _executionAction = new InputAction("Execution", InputActionType.Button);
                _executionAction.AddBinding("<Gamepad>/rightStickPress");
                _executionAction.AddBinding("<Keyboard>/leftCtrl");
                _ownsExecutionAction = true;
            }

            // 에셋에 Attack 액션이 없으면 자체 생성.
            foreach (var b in attackBindings)
            {
                if (b == null || b.action != null) continue;
                b.action = new InputAction(b.actionName, InputActionType.Button);
                if (b.fallbackBindings != null)
                    foreach (var path in b.fallbackBindings)
                        b.action.AddBinding(path);
                b.owns = true;
            }
        }

        /// <summary>에셋이 없거나 액션을 찾지 못했을 때 사용하는 기본 바인딩.</summary>
        private void CreateFallbackActions()
        {
            _moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            _moveAction.AddBinding("<Gamepad>/leftStick");
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            _jumpAction = new InputAction("Jump", InputActionType.Button);
            _jumpAction.AddBinding("<Gamepad>/buttonSouth"); // Xbox A
            _jumpAction.AddBinding("<Keyboard>/space");

            _sprintAction = new InputAction("Sprint", InputActionType.Button);
            _sprintAction.AddBinding("<Keyboard>/leftShift");
            _sprintAction.AddBinding("<Gamepad>/leftStickPress"); // X는 Light Attack에 양보 (2026-08-19)

            _ownsActions = true;
        }
    }
}
