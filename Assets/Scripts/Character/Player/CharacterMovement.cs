using System;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// CharacterController 기반 벨트스크롤 이동 — "캐릭터의 이동".
    ///
    /// ※ 컴포넌트 통합(2026-08-19): MonoBehaviour가 아니라 <b>Character가 소유하는 일반 클래스</b>.
    /// transform/GetComponent 등은 Character를 통해 접근한다. 이동 수치는 Character.Stats(=Ability)에서 조회.
    ///
    /// 축 규칙: 입력 X=스플라인 진행, Y=깊이. 이동축은 매 프레임 접선에서 계산.
    /// 포함: 가감속, 깊이 배율, Facing, Gravity/Ground/Jump(다단), 공중 관성, Dash(DashAbility 내장), Teleport.
    /// </summary>
    [Serializable]
    public class CharacterMovement
    {
        // ════════════════════════ 대시 ════════════════════════
        [Serializable]
        public class DashAbility
        {
            [Tooltip("대시 지속시간 (초)")]
            [SerializeField, Range(0.05f, 1f)] private float dashDuration = 0.18f;
            [Tooltip("대시 쿨다운 (초). 종료 시점부터 계산.")]
            [SerializeField, Range(0f, 5f)] private float dashCooldown = 0.8f;
            [Tooltip("스탯이 없을 때 사용할 대시 속도 (m/s)")]
            [SerializeField] private float fallbackDashSpeed = 18f;

            [Tooltip("이동(dashDuration)이 끝난 뒤에도 대시 애니메이션이 끝날 때까지 Dash 상태를 유지한다 (2026-09-15). " +
                     "꺼져 있으면 구버전처럼 이동 종료 즉시 Idle/Move/Run으로 전이해 모션이 중간에 끊긴다. 유지 중 공격/점프/가드/재대시로 캔슬 가능.")]
            [SerializeField] private bool holdUntilAnimationEnds = true;
            [Tooltip("대시 애니메이션 종료로 간주할 normalizedTime (0~1)")]
            [SerializeField, Range(0.5f, 1f)] private float animationExitNormalizedTime = 0.95f;
            [Tooltip("이동 종료 후 애니 대기 최대 시간(초). 애니 상태를 못 찾거나 루프일 때의 안전장치")]
            [SerializeField, Range(0f, 2f)] private float maxAnimationHold = 0.6f;

            /// <summary>이동 종료 후 대시 애니메이션이 끝날 때까지 Dash 상태를 유지하는가.</summary>
            public bool HoldUntilAnimationEnds => holdUntilAnimationEnds;
            /// <summary>대시 애니메이션 종료 판정 normalizedTime.</summary>
            public float AnimationExitNormalizedTime => animationExitNormalizedTime;
            /// <summary>이동 종료 후 애니 대기 최대 시간(초).</summary>
            public float MaxAnimationHold => maxAnimationHold;

            [System.NonSerialized] private CharacterMovement _owner;
            private float _endTime;
            private float _cooldownUntil;

            internal void Initialize(CharacterMovement owner) => _owner = owner;

            /// <summary>대시 속도. 스탯이 있으면 FinalDashSpeed.</summary>
            public float DashSpeed
            {
                get
                {
                    var stats = _owner != null ? _owner.Stats : null;
                    return stats != null ? stats.FinalDashSpeed : fallbackDashSpeed;
                }
            }

            public bool IsDashing { get; private set; }
            public bool CanDash => !IsDashing && Time.time >= _cooldownUntil;
            public Vector3 DashDirection { get; private set; }

            public void Begin(Vector3 direction)
            {
                if (_owner == null || !CanDash || direction.sqrMagnitude < 0.0001f)
                    return;
                DashDirection = direction.normalized;
                IsDashing = true;
                _endTime = Time.time + dashDuration;
                _owner.BeginDashOverride(DashDirection * DashSpeed);
            }

            public void End()
            {
                if (!IsDashing) return;
                IsDashing = false;
                _cooldownUntil = Time.time + dashCooldown;
                _owner?.EndDashOverride();
            }

            internal void Tick()
            {
                if (IsDashing && Time.time >= _endTime)
                    End();
            }
        }

        [Header("References")]
        [Tooltip("스플라인 기준 좌표계. 그룹 로드 시 GameManager가 주입.")]
        [SerializeField] private SplineMovementReference splineReference;

        [Header("Move")]
        [Tooltip("최대 수평 이동 속도 폴백 (Stats 있으면 FinalMoveSpeed 우선)")]
        [SerializeField] private float moveSpeed = 6f;
        [Tooltip("지상에서 가속도 없이 즉시 목표 속도로 이동/정지 (구프로젝트처럼 누른 방향으로 바로 이동). 끄면 아래 acceleration/deceleration 램프 사용. 공중 관성은 항상 유지.")]
        [SerializeField] private bool instantGroundMovement = true;
        private float acceleration = 20f;
        private float deceleration = 20f;
        private float depthMoveMultiplier = 0.75f;
        private float airControlMultiplier = 0.5f;
        private float knockbackDecayRate = 12f; // 넉백 지수 감쇠율(구프로젝트 KnockbackInertia=12)

        public enum FlipAxis { X, Y, Z }

        [Header("Facing")]
        [SerializeField] private float rotationSpeed = 12f;
        [SerializeField, Range(0.05f, 0.9f)] private float facingThreshold = 0.2f;
        [Tooltip("좌/우 전환 시 반전할 비주얼 루트(모델). 비우면 첫 자식.")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private FlipAxis flipAxis = FlipAxis.Z;
        [SerializeField] private bool facingRight = true;

        [Header("Gravity / Jump")]
        [SerializeField] private float gravity = -30f;
        [Tooltip("점프 최고 높이 폴백 (Stats 있으면 FinalJumpHeight 우선)")]
        [SerializeField] private float jumpHeight = 1.6f;
        private float groundedVerticalVelocity = -2f;

        [Header("Spline Stability")]
        [SerializeField, Range(1f, 50f)] private float tangentSmoothSpeed = 10f;

        [Header("Dash")]
        [SerializeField] private DashAbility dash = new DashAbility();

        [Header("Character Separation")]
        [Tooltip("캐릭터 간 소프트 콜리전(XZ 밀어내기) 사용 여부. 하드 충돌은 Character가 IgnoreCollision으로 이미 끔.")]
        [SerializeField] private bool enableSeparation = true;
        [Tooltip("분리 반경 배율. CharacterController.radius x 이 값 기준으로 겹침 판정")]
        [SerializeField, Range(0.5f, 2f)] private float separationRadiusScale = 1f;
        [Tooltip("초당 최대 밀어내기 속도 (m/s). 클수록 빠르게 분리되고 뻑뻑한 느낌")]
        [SerializeField] private float separationPushSpeed = 5f;
        [Tooltip("겹쳤을 때 내가 물러나는 비율. 플레이어 0.3·적 0.7처럼 비대칭이면 플레이어가 적 무리를 비집고 다니기 쉬움")]
        [SerializeField, Range(0f, 1f)] private float separationYield = 0.5f;

        // ─────────────── 내부 상태 ───────────────
        [Header("Movement Feedback")]
        [Tooltip("이동 연출(달리기 연기·대시·착지) Feedback 사용 여부. 구버전은 플레이어만 사용하므로 기본 꺼짐")]
        [SerializeField] private bool enableMovementFeedback = false;
        [Tooltip("달리기 중 발밑 연기 재생 주기(초) — 구버전 RunCycle 이벤트(0.21/0.49s·실효 0.7배속) 근사")]
        [SerializeField] private float runStepInterval = 0.4f;
        [Tooltip("달리기 연기 Feedback 키 (구버전 Run=Dash.prefab)")]
        [SerializeField] private string runFeedbackKey = "Run";
        [SerializeField] private string dashStartFeedbackKey = "DashStart";
        [SerializeField] private string dashEndFeedbackKey = "DashEnd";
        [SerializeField] private string landingFeedbackKey = "Landing";
        [Tooltip("강공격 대시 캔슬 순간 재생할 Feedback 키")]
        [SerializeField] private string cancelMoveFeedbackKey = "CancelMove";

        [System.NonSerialized] private Character _character;
        private CharacterController _controller;
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private bool _grounded = true;
        private Vector3 _smoothedForward = Vector3.right;
        private float _moveSpeedMultiplier = 1f;
        private int _currentJumpCount;
        private bool _dashActive;
        private Vector3 _dashVelocity;
        private bool _knockbackActive;
        private Vector3 _knockbackVelocity;
        private bool _airborneActive;

        // ─────────── Traversal(사다리/난간/맨틀) 모션 채널 ───────────
        // 상태(LadderClimb/LedgeHang/Mantle)가 타이밍/전이를 소유하고, 실제 이동은 여기서 수행한다.
        // 활성 중에는 일반 입력이동·중력·공격전진을 모두 무시하고 _traversalVelocity만 적용한다.
        private bool _traversalActive;
        private Vector3 _traversalVelocity;
        // 이동발판 등 외부에서 매 프레임 밀어 넣는 델타(상태와 무관하게 항상 적용).
        private Vector3 _externalDelta;
        // 월드 오브젝트(트리거)가 세팅하는 사용 가능 사다리/난간 참조.
        [System.NonSerialized] private LadderTraversable _availableLadder;
        [System.NonSerialized] private bool _availableLadderAtTop;
        [System.NonSerialized] private ClimbableSurface _availableClimbable;



        // ─────────── 스탯 연동 ───────────
        public CharacterRuntimeStats Stats => _character != null ? _character.Stats : null;
        public float EffectiveMoveSpeed => Stats != null ? Stats.FinalMoveSpeed : moveSpeed;
        public float EffectiveJumpHeight => Stats != null ? Stats.FinalJumpHeight : jumpHeight;
        public int MaxJumpCount => Stats != null ? Stats.FinalJumpCount : 1;
        public int CurrentJumpCount => _currentJumpCount;
        public bool CanJumpNow => CanJump && (IsGrounded || _currentJumpCount < MaxJumpCount);
        public Vector3 HorizontalVelocity => _horizontalVelocity;
        public float HorizontalSpeed => _horizontalVelocity.magnitude;
        public float VerticalVelocity => _verticalVelocity;
        public bool IsGrounded => _grounded;
        public DashAbility Dash => dash;

        // ─────────── 상태/전투 계층용 확장 지점 ───────────
        public bool CanMove { get; set; } = true;
        public bool CanJump { get; set; } = true;
        public bool AutoJumpFromInput { get; set; } = true;
        /// <summary>이동 입력으로 자동 Facing(좌우 플립)할지. 적은 false로 두고 Target 기준으로 직접 설정한다.</summary>
        public bool AutoFacingFromInput { get; set; } = true;
        /// <summary>Facing 잠금. 공격/강제반응 중 방향 고정용. SetFacingRight·입력 플립을 모두 무시한다.</summary>
        public bool FacingLocked { get; set; }
        /// <summary>루트 yaw 회전 잠금 — 넉다운/에어본 중 스플라인 추종 회전을 멈춰 낙하 클립이 흔들리지 않게 한다(구프로젝트 Orientation.AbilityPermitted=false 이관).</summary>
        public bool RotationLocked { get; set; }
        /// <summary>넉백 반응 채널 활성 여부.</summary>
        public bool IsKnockbackActive => _knockbackActive;
        /// <summary>에어본 반응 채널 활성 여부.</summary>
        public bool IsAirborneActive => _airborneActive;
        public float MoveSpeedMultiplier => _moveSpeedMultiplier;
        public void SetMoveSpeedMultiplier(float multiplier) => _moveSpeedMultiplier = Mathf.Max(0f, multiplier);

        public void Jump()
        {
            if (!CanJumpNow) return;
            _currentJumpCount++;
            _verticalVelocity = Mathf.Sqrt(EffectiveJumpHeight * -2f * gravity);
        }

        // ─────────── Dash 오버라이드 ───────────
        internal void BeginDashOverride(Vector3 dashVelocity)
        {
            _dashActive = true;
            _dashVelocity = dashVelocity;
        }

        internal void EndDashOverride()
        {
            if (!_dashActive) return;
            _dashActive = false;
            _horizontalVelocity = Vector3.ClampMagnitude(_dashVelocity, EffectiveMoveSpeed);
        }

        // ─────────── 강제 반응 채널 (Knockback / Airborne) — 상태가 타이밍 소유, 실제 이동은 여기 ───────────
        /// <summary>수평 밀림 시작 — direction(수평) 방향 속도를 주고 감쇠. 종료 타이밍은 밀림을 쓰는 상태(Hit·Knockdown)가 소유.</summary>
        public void BeginKnockback(Vector3 direction, float force)
        {
            Vector3 beltDir = ProjectToBeltAxis(direction);
            _knockbackActive = true;
            _airborneActive = false;
            _knockbackVelocity = beltDir * Mathf.Max(0f, force);
            _horizontalVelocity = _knockbackVelocity;
            _verticalVelocity = groundedVerticalVelocity;
        }

        /// <summary>수평 밀림 종료 — 밀림을 쓰는 상태(CharacterHitState·CharacterKnockdownState)가 정지 시 호출.</summary>
        public void EndKnockback()
        {
            _knockbackActive = false;
            _knockbackVelocity = Vector3.zero;
            _horizontalVelocity = Vector3.zero;
        }

        /// <summary>
        /// 에어본 시작 — 위로 upForce, horizontalDirection 방향으로 horizForce. 착지는 상태가 IsGrounded로 판정.
        /// airTime > 0 이면 체공 전용 중력을 역산(g = -2·upForce/airTime)해 총 체공시간이 airTime과 일치한다
        /// (같은 Force로 Duration만 늘리면 더 오래 떠 있음). airTime = 0 이면 기본 중력 낙하(기존 동작).
        /// </summary>
        public void BeginAirborne(Vector3 horizontalDirection, float upForce, float horizForce, float airTime = 0f)
        {
            // 넉백과 동일하게 벨트 축으로만 민다 — 깊이(Z) 성분 제거 (공중콤보 대각선 밀림 방지).
            Vector3 beltDir = ProjectToBeltAxis(horizontalDirection);
            _airborneActive = true;
            _knockbackActive = false;
            _hoverActive = false;
            _horizontalVelocity = beltDir * Mathf.Max(0f, horizForce);
            _verticalVelocity = Mathf.Max(0.1f, upForce);
            _airborneGravity = (airTime > 0f && upForce > 0f) ? (-2f * upForce / airTime) : 0f;
            _grounded = false;
        }

        /// <summary>
        /// 스파이크(내려찍기) 낙하 시작 — 에어본의 반대. 위로 뜨는 대신 downSpeed로 즉시 급강하한다.
        /// 체공 중 피격된 대상 전용(CharacterAirborneState.Enter가 SpikeForce &gt; 0일 때 호출).
        /// 착지 판정은 에어본과 동일하게 상태가 IsGrounded로 수행한다.
        /// </summary>
        public void BeginSpikeFall(Vector3 horizontalDirection, float downSpeed, float horizForce = 0f)
        {
            _airborneActive = true;
            _knockbackActive = false;
            _hoverActive = false;
            _horizontalVelocity = horizForce > 0f
                ? ProjectToBeltAxis(horizontalDirection) * horizForce
                : Vector3.zero;
            _verticalVelocity = -Mathf.Max(0.1f, downSpeed);
            _airborneGravity = 0f; // 기본 중력으로 계속 가속
            _grounded = false;
        }

        /// <summary>에어본 종료 — CharacterAirborneState.Exit(착지)에서 호출.</summary>
        public void EndAirborne()
        {
            _airborneActive = false;
            _airborneGravity = 0f;
            _horizontalVelocity = Vector3.zero;
        }

        // 체공시간 역산 중력 (0 = 기본 gravity 사용). BeginAirborne이 설정, EndAirborne이 해제.
        private float _airborneGravity;

        /// <summary>
        /// 강제 반응(넉백/에어본/스파이크) 공용 수평 방향 — 벨트 축(스플라인 접선)으로만 밀리도록
        /// 깊이(Z) 성분을 제거하고 부호만 취한다. 축 성분이 0이면 바라보는 방향 반대쪽 부호로 폴백.
        /// </summary>
        private Vector3 ProjectToBeltAxis(Vector3 direction)
        {
            direction.y = 0f;
            float along = Vector3.Dot(direction, _smoothedForward);
            float sign = Mathf.Abs(along) > 0.0001f ? Mathf.Sign(along) : (facingRight ? 1f : -1f);
            return _smoothedForward.sqrMagnitude > 0.0001f ? _smoothedForward.normalized * sign
                : (direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.right);
        }

        /// <summary>
        /// Lightweight upward lift for the ATTACKER (e.g. player rising alongside a juggled enemy).
        /// Nudges vertical velocity up like a partial jump and leaves the ground; gravity brings it back.
        /// Not a forced reaction (no knockdown pose) and does not touch the horizontal attack-motion channel.
        /// CharacterCombat.NotifyAttackLanded calls this when a hit lands on an airborne target.
        /// </summary>
        public void ApplyUpwardLift(float upForce)
        {
            if (upForce <= 0f) return;
            _verticalVelocity = Mathf.Max(_verticalVelocity, upForce);
            _grounded = false;
        }

        /// <summary>
        /// ApplyUpwardLift의 반대 — 공격자 급강하(내려찍기 공격). 수직 속도를 즉시 -downSpeed로 눌러
        /// 빠르게 낙하시키고, 이후에는 기본 중력이 계속 가속한다. 강제 반응 채널(_airborneActive)을
        /// 쓰지 않는 1회성 속도 설정이라 별도 해제가 필요 없다(착지 시 자동 정리).
        /// CharacterCombat.ExecuteAttack이 공중에서 DiveFall 공격 시작 시 호출.
        /// </summary>
        public void ApplyDownwardDive(float downSpeed)
        {
            if (downSpeed <= 0f || _grounded) return;
            _hoverActive = false; // 체공 중이었다면 해제하고 낙하
            _verticalVelocity = Mathf.Min(_verticalVelocity, -downSpeed);
        }

        // ─────────── 공중 체공(Hover) ───────────
        // 급강하 공격의 '잠깐 멈췄다가 내리꽂기'(AttackAction.DiveHoverDuration)용 1회성 채널.
        // 활성 중 공중이면 수직 속도를 0으로 고정하고 중력을 건너뛴다. 수평 이동(에어컨트롤)은 그대로.
        // 착지하거나 EndHover/ApplyDownwardDive/ResetMovementVelocity 시 해제. CharacterCombat이 소유/타이밍 관리.
        private bool _hoverActive;

        /// <summary>공중 체공 중인가(중력 정지).</summary>
        public bool IsHovering => _hoverActive && !_grounded;

        /// <summary>공중 체공 시작 — 공중일 때만 성립. 수직 속도를 즉시 0으로 만든다.</summary>
        public void BeginHover()
        {
            if (_grounded) return;
            _hoverActive = true;
            _verticalVelocity = 0f;
        }

        /// <summary>공중 체공 해제 — 이후 기본 중력으로 낙하한다.</summary>
        public void EndHover() => _hoverActive = false;


        // ─────────── Traversal API (상태 · 월드 오브젝트용) ───────────
        public bool IsTraversalActive => _traversalActive;
        public LadderTraversable ActiveLadder { get; set; }
        public ClimbableSurface ActiveClimbable { get; set; }
        public LadderTraversable AvailableLadder => _availableLadder;
        public bool AvailableLadderAtTop => _availableLadderAtTop;
        public CharacterController Controller => _controller;
        public Transform Root => _character != null ? _character.transform : null;

        public void BeginTraversal()
        {
            _traversalActive = true;
            _dashActive = false; _dashVelocity = Vector3.zero;
            _knockbackActive = false; _knockbackVelocity = Vector3.zero;
            _airborneActive = false;
            _hoverActive = false;
            _attackMotionSpeed = 0f;
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
        }

        public void SetTraversalVelocity(Vector3 velocity) => _traversalVelocity = velocity;

        public void EndTraversal()
        {
            _traversalActive = false;
            _traversalVelocity = Vector3.zero;
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = groundedVerticalVelocity;
        }

        public void AddExternalDelta(Vector3 delta) => _externalDelta += delta;

        public void MoveRaw(Vector3 delta) { if (_controller != null && _controller.enabled) _controller.Move(delta); }
        /// <summary>CharacterController on/off — Ledge 매달림 등 충돌 무시 구간용. 끔 동안은 SetWorldPosition으로 직접 배치.</summary>
        public void SetControllerEnabled(bool on) { if (_controller != null) _controller.enabled = on; }
        /// <summary>트랜스폼 위치 직접 설정(컨트롤러가 꺼져 있을 때 사용). 콜리전 무시하고 정확히 배치.</summary>
        public void SetWorldPosition(Vector3 pos) { if (_character != null) _character.transform.position = pos; }


        public void SetAvailableLadder(LadderTraversable ladder, bool atTop) { _availableLadder = ladder; _availableLadderAtTop = atTop; }
        public void ClearAvailableLadder(LadderTraversable ladder) { if (_availableLadder == ladder) { _availableLadder = null; _availableLadderAtTop = false; } }

        /// <summary>트리거 안에 있는 사용 가능 벽/난간(없으면 null). ClimbableSurface 트리거가 세팅.</summary>
        public ClimbableSurface AvailableClimbable => _availableClimbable;
        /// <summary>월드 벽(ClimbableSurface 트리거)가 사용 가능 벽을 등록.</summary>
        public void SetAvailableClimbable(ClimbableSurface surf) { _availableClimbable = surf; }
        /// <summary>지정 벽이 현재 사용 가능 벽이면 해제(트리거 이탈 시).</summary>
        public void ClearAvailableClimbable(ClimbableSurface surf) { if (_availableClimbable == surf) _availableClimbable = null; }


        
public bool IsDashOverrideActive => _dashActive;
        public Vector3 SplineForward => _smoothedForward;

        // ─────────── Attack Motion (공격 전진 — 구버전 AnimationMoveForward 커브 이식) ───────────
        // 소유: 값 결정은 CharacterCombat(AttackAction 데이터), 실제 이동은 여기(CharacterMovement)가 수행.
        [System.NonSerialized] private float _attackMotionSpeed;

        /// <summary>공격 전진 속도(m/s) 설정. CharacterCombat.TickAttack이 전진 창 구간에서 매 프레임 호출. Facing 방향 스플라인 접선으로 이동. 음수 = 후퇴(구버전 회전 베기 반동 이식).</summary>
        public void SetAttackMotionSpeed(float unitsPerSecond) => _attackMotionSpeed = unitsPerSecond;

        /// <summary>공격 전진 정지. 공격 종료/취소 시 CharacterCombat이 호출.</summary>
        public void ClearAttackMotion() => _attackMotionSpeed = 0f;
        public Vector3 SplineDepth => Vector3.Cross(_smoothedForward, Vector3.up).normalized;
        public bool FacingRight => facingRight;
        /// <summary>좌/우 반전이 적용되는 비주얼 루트(스케일 미러). 루트 바깥 오브젝트를 같은 기준으로 반전할 때 참조 (2026-09-08).</summary>
        public Transform VisualRoot => visualRoot;
        /// <summary>좌/우 반전에 쓰는 비주얼 루트 스케일 축.</summary>
        public FlipAxis VisualFlipAxis => flipAxis;

        // ─────────── Scene 전환 연동 ───────────
        public void SetSplineReference(SplineMovementReference reference) => splineReference = reference;

        public void Teleport(Vector3 position, bool faceRight = true)
        {
            if (_controller == null && _character != null)
                _controller = _character.GetComponent<CharacterController>();

            bool wasEnabled = _controller != null && _controller.enabled;
            if (_controller != null) _controller.enabled = false;
            if (_character != null) _character.transform.position = position;
            if (_controller != null) _controller.enabled = wasEnabled;

            ResetMovementVelocity();
            SetFacingRight(faceRight);
        }

        public void ResetMovementVelocity()
        {
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = groundedVerticalVelocity;
            _dashActive = false;
            _dashVelocity = Vector3.zero;
            _knockbackActive = false;
            _knockbackVelocity = Vector3.zero;
            _airborneActive = false;
            _hoverActive = false;
        }

        // ─────────── 라이프사이클 (Character가 호출) ───────────
        public void Bind(Character character) => _character = character;

        // ─────────── 이동 연출 Feedback (구버전 RunEffect/DashStart/DashFinish/Landing 이벤트 이식) ───────────

        /// <summary>달리기 발밑 연기 주기(초). CharacterRunState가 사용.</summary>
        public float RunStepInterval => runStepInterval;
        /// <summary>이동 연출 Feedback 사용 여부.</summary>
        public bool MovementFeedbackEnabled => enableMovementFeedback;

        public void PlayRunStepFeedback() => PlayMovementFeedback(runFeedbackKey);
        public void PlayDashStartFeedback() => PlayMovementFeedback(dashStartFeedbackKey);
        public void PlayDashEndFeedback() => PlayMovementFeedback(dashEndFeedbackKey, new Vector3(0.5f, 0.5f, 0.5f)); // 구버전 DashFinish scale 0.5
        public void PlayLandingFeedback() => PlayMovementFeedback(landingFeedbackKey);
        public void PlayCancelMoveFeedback() => PlayMovementFeedback(cancelMoveFeedbackKey);

        private void PlayMovementFeedback(string key, Vector3? scale = null)
        {
            if (!enableMovementFeedback || string.IsNullOrEmpty(key) || _character == null) return;
            // Facing은 루트 회전이 아니라 비주얼 스케일 플립으로 처리된다(ApplyFlip).
            // 루트 rotation은 항상 스플라인 진행 방향을 보므로, 이펙트 방향은 facingRight를 직접 반영해야 한다.
            Vector3 faceDir = _smoothedForward * (facingRight ? 1f : -1f);
            if (faceDir.sqrMagnitude < 0.0001f) faceDir = _character.transform.forward;
            // 구버전 VFX 앵커 로컬 rot(0,-90,0)을 실제 페이싱 방향에 합성
            Quaternion rot = Quaternion.LookRotation(faceDir, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);
            Vector3 pos = _character.transform.position;
            if (scale.HasValue) Aiara.FeedbackManager.PlayFeedbackAtWorld(key, pos, rot, scale.Value);
            else Aiara.FeedbackManager.PlayFeedbackAtWorld(key, pos, rot);
        }

        public void OnAwake()
        {
            _controller = _character.GetComponent<CharacterController>();
            dash.Initialize(this);
            if (visualRoot == null && _character.transform.childCount > 0)
                visualRoot = _character.transform.GetChild(0);
        }

        public void OnStart()
        {
            if (splineReference != null)
            {
                SplineFrame frame = splineReference.GetFrame(_character.transform.position);
                _smoothedForward = frame.Forward;
                _character.transform.rotation = Quaternion.LookRotation(_smoothedForward, Vector3.up);
            }
            ApplyFlip();
        }

        public void OnDisableCleanup() => dash.End();

        public void OnUpdate(float dt)
        {
            if (dt <= 0f) return;
            if (_character == null || _controller == null) return;
            dash.Tick();

            // 이동발판 델타: 상태와 무관하게 항상 먼저 적용(라이더 추종).
            if (_externalDelta.sqrMagnitude > 1e-8f && _controller.enabled)
            {
                _controller.Move(_externalDelta);
                _externalDelta = Vector3.zero;
            }

            // Traversal(사다리/난간/맨틀) 채널: 일반 이동/중력/공격전진 무시, _traversalVelocity만 적용.
            if (_traversalActive)
            {
                UpdateSplineAxes();
                if (_controller.enabled)
                    _controller.Move(_traversalVelocity * dt);
                _grounded = _controller.isGrounded;
                return;
            }

            var sm = _character != null ? _character.StateManager : null;
            Vector2 input = sm != null ? sm.MoveInput : Vector2.zero;

            UpdateSplineAxes();
            UpdateHorizontalVelocity(input, dt);
            UpdateVerticalVelocity(dt);

            Vector3 motion = (_horizontalVelocity + Vector3.up * _verticalVelocity) * dt;
            _controller.Move(motion);

            if (Mathf.Abs(_attackMotionSpeed) > 0.0001f && _controller.enabled)
            {
                Vector3 attackDir = _smoothedForward * (facingRight ? 1f : -1f);
                _controller.Move(attackDir * (_attackMotionSpeed * dt));
            }
            _grounded = _controller.isGrounded;

            ApplySeparation(dt);

            UpdateFacing(input, dt);
        }

        // ─────────── 캐릭터 간 소프트 콜리전 (XZ 밀어내기) ───────────
        // 하드 충돌은 CharacterBody 레이어(자기충돌 매트릭스 해제)로 이미 꺼져 있다 — Character.OnEnable 참조.
        // 여기서는 겹친 캐릭터로부터 매 프레임 부드럽게 물러나 완전 통과처럼 보이지 않게 한다.
        // 자기 자신만 움직인다(상대는 상대의 패스에서 물러남) — separationYield로 비대칭 조절.
        private void ApplySeparation(float dt)
        {
            if (!enableSeparation || separationYield <= 0f) return;
            if (_controller == null || !_controller.enabled) return;
            if (_traversalActive) return;
            if (_character == null || _character.IsDead) return;

            var all = Character.ActiveCharacters;
            if (all == null || all.Count < 2) return;

            Vector3 myPos = _character.transform.position;
            float myRadius = _controller.radius * Mathf.Max(0.01f, separationRadiusScale);
            float myHalfHeight = _controller.height * 0.5f;
            Vector3 push = Vector3.zero;

            for (int i = 0; i < all.Count; i++)
            {
                Character other = all[i];
                if (other == null || other == _character || other.IsDead) continue;
                CharacterController otherCc = other.Movement != null ? other.Movement.Controller : null;
                if (otherCc == null || !otherCc.enabled) continue;

                Vector3 otherPos = other.transform.position;

                // 수직으로 충분히 떨어져 있으면(저글 중 공중의 적 등) 밀지 않는다.
                float otherHalfHeight = otherCc.height * 0.5f;
                if (Mathf.Abs(otherPos.y - myPos.y) > myHalfHeight + otherHalfHeight) continue;

                float otherRadius = otherCc.radius * Mathf.Max(0.01f, separationRadiusScale);
                float combined = myRadius + otherRadius;

                Vector3 diff = myPos - otherPos;
                diff.y = 0f;
                float sqrDist = diff.sqrMagnitude;
                if (sqrDist >= combined * combined) continue;

                Vector3 away;
                float overlap;
                if (sqrDist < 1e-6f)
                {
                    // 완전 동일 위치: 깊이축으로 결정적 분리(인스턴스 ID로 방향 고정)
                    float sign = _character.GetInstanceID() > other.GetInstanceID() ? 1f : -1f;
                    away = SplineDepth * sign;
                    overlap = combined;
                }
                else
                {
                    float dist = Mathf.Sqrt(sqrDist);
                    away = diff / dist;
                    overlap = combined - dist;
                }

                push += away * overlap;
            }

            if (push.sqrMagnitude < 1e-8f) return;

            // 내 몫(separationYield)만 물러나되, 프레임당 이동량은 separationPushSpeed로 캡 — 스냅 없이 부드럽게 분리
            Vector3 delta = Vector3.ClampMagnitude(push * separationYield, separationPushSpeed * dt);
            _controller.Move(delta);
        }

        private void UpdateSplineAxes()
        {
            Vector3 rawForward = splineReference != null
                ? splineReference.GetFrame(_character.transform.position).Forward
                : Vector3.right;

            _smoothedForward = Vector3.Slerp(
                _smoothedForward, rawForward,
                1f - Mathf.Exp(-tangentSmoothSpeed * Time.deltaTime));

            if (_smoothedForward.sqrMagnitude < 0.0001f)
                _smoothedForward = rawForward;
            _smoothedForward.y = 0f;
            _smoothedForward.Normalize();
        }

        private void UpdateHorizontalVelocity(Vector2 input, float dt)
        {
            if (_dashActive)
            {
                _horizontalVelocity = _dashVelocity;
                return;
            }
            if (_knockbackActive)
            {
                // 지수 감쇠(구프로젝트 TopDownController Impact falloff, rate≈KnockbackInertia). 프레임레이트 무관.
                _horizontalVelocity = Vector3.Lerp(_horizontalVelocity, Vector3.zero, 1f - Mathf.Exp(-knockbackDecayRate * dt));
                return;
            }
            if (_airborneActive)
            {
                _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, Vector3.zero, deceleration * airControlMultiplier * 0.5f * dt);
                return;
            }

            Vector3 depth = Vector3.Cross(_smoothedForward, Vector3.up).normalized;
            Vector3 direction = _smoothedForward * input.x + depth * (input.y * depthMoveMultiplier);
            direction = Vector3.ClampMagnitude(direction, 1f);
            Vector3 targetVelocity = CanMove
                ? direction * (EffectiveMoveSpeed * _moveSpeedMultiplier)
                : Vector3.zero;

            // 지상 즉시 이동(구프로젝트 감각): 가속도 램프 없이 목표 속도로 바로. 정지도 즉시.
            // 공중에서는 관성/에어컨트롤을 유지해 점프 궤적이 부자연스럽지 않게 한다.
            if (instantGroundMovement && _grounded)
            {
                _horizontalVelocity = targetVelocity;
                return;
            }

            bool hasInput = input.sqrMagnitude > 0.01f;
            float rate = hasInput ? acceleration : deceleration;
            if (!_grounded)
                rate *= airControlMultiplier;

            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, targetVelocity, rate * dt);
        }

        private void UpdateVerticalVelocity(float dt)
        {
            if (_grounded)
            {
                if (_verticalVelocity < 0f)
                    _currentJumpCount = 0;
                if (_verticalVelocity < 0f)
                    _verticalVelocity = groundedVerticalVelocity;

                var sm = _character != null ? _character.StateManager : null;
                if (AutoJumpFromInput && sm != null && sm.JumpPressed)
                    Jump();
            }
            if (_hoverActive)
            {
                if (_grounded) { _hoverActive = false; }
                else { _verticalVelocity = 0f; return; } // 체공: 중력 정지
            }
            float g = (_airborneActive && _airborneGravity < 0f) ? _airborneGravity : gravity;
            _verticalVelocity += g * dt;
        }

        private void UpdateFacing(Vector2 input, float dt)
        {
            if (!RotationLocked && _smoothedForward.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(_smoothedForward, Vector3.up);
                _character.transform.rotation = Quaternion.Slerp(
                    _character.transform.rotation, targetRotation,
                    1f - Mathf.Exp(-rotationSpeed * dt));
            }

            if (AutoFacingFromInput && !FacingLocked && Mathf.Abs(input.x) > facingThreshold)
            {
                bool right = input.x > 0f;
                if (right != facingRight)
                {
                    facingRight = right;
                    ApplyFlip();
                }
            }
        }

        public void SetFacingRight(bool right)
        {
            if (FacingLocked) return;
            if (facingRight == right) return;
            facingRight = right;
            ApplyFlip();
        }

        private void ApplyFlip()
        {
            if (visualRoot == null) return;
            float sign = facingRight ? 1f : -1f;
            Vector3 scale = visualRoot.localScale;
            switch (flipAxis)
            {
                case FlipAxis.X: scale.x = Mathf.Abs(scale.x) * sign; break;
                case FlipAxis.Y: scale.y = Mathf.Abs(scale.y) * sign; break;
                case FlipAxis.Z: scale.z = Mathf.Abs(scale.z) * sign; break;
            }
            visualRoot.localScale = scale;
#if UNITY_EDITOR
            if (_character != null && _character.StateManager != null && _character.StateManager.CurrentStateType == CharacterStateType.Grab)
                Debug.Log($"[CharacterMovement] ApplyFlip in Grab state facingRight={facingRight} visualRoot={visualRoot.name} scale={visualRoot.localScale} lossy={visualRoot.lossyScale}\n{System.Environment.StackTrace}", visualRoot);
#endif
        }
    }
}
