using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 캐릭터 명령 계층 — Controller(PlayerController / AIController)와 상태 머신 사이의 유일한 명령 통로.
    ///
    /// ※ 컴포넌트 통합(2026-08-19): MonoBehaviour가 아니라 <b>Character가 소유하는 일반 클래스</b>다.
    /// GameObject에는 Character 컴포넌트 하나만 붙고, 이 클래스는 Character 인스펙터의 접이식 필드로 노출된다.
    ///
    /// 역할(접수/보관/소비만): 지속 입력(MoveInput/RunHeld) + 단발 요청(Jump/Dash/Attack 원샷) +
    /// 소비(ClearOneShots) + 확장점(짧은 선입력 Buffer). State/Animator/Damage/Input 미접근.
    /// </summary>
    [System.Serializable]
    public class CharacterCommandManager
    {
        [Header("Input Buffer")]
        [Tooltip("Attack 요청을 이 시간(초)만큼 유지해 전이창에서 소비. 0 = 버퍼 없음(현행).")]
        [SerializeField, Range(0f, 0.4f)] private float attackBufferSeconds = 0f;

        // ─────────── 지속 입력 ───────────
        public Vector2 MoveInput { get; private set; }
        public bool RunHeld { get; private set; }
        /// <summary>가드 입력 홀드 여부(지속 입력). Controller가 SetGuardHeld로 설정.</summary>
        public bool GuardHeld { get; private set; }

        // ─────────── 단발 요청(원샷) ───────────
        private bool _jumpRequested;
        private bool _dashRequested;
        private bool _chainGrabRequested;
        private bool _executionRequested;

        private string _requestedAttackName; // null = 요청 없음, "" = 기본 공격, 그 외 = AttackAction.AttackName
        private AttackInputType _requestedAttackInput = AttackInputType.Light;
        private bool _requestedAttackWithDash;
        private float _attackRequestTime = -999f;

        public bool JumpRequested => _jumpRequested;
        public bool DashRequested => _dashRequested;
        /// <summary>체인 그랩(LB) 원샷 요청.</summary>
        public bool ChainGrabRequested => _chainGrabRequested;
        /// <summary>절명기(처형, R3/Ctrl) 원샷 요청.</summary>
        public bool ExecutionRequested => _executionRequested;

        public bool AttackRequested => _requestedAttackName != null;
        /// <summary>요청된 공격 이름. "" = 기본(첫 번째) 공격. 없으면 "".</summary>
        public string RequestedAttackName => _requestedAttackName ?? "";
        /// <summary>요청된 공격 입력 종류 (Light/Heavy...). Transition 매칭용.</summary>
        public AttackInputType RequestedAttackInput => _requestedAttackInput;
        /// <summary>요청된 공격이 대시 키와 동시 입력(코드)됐는가. ComboGroup StarterState.DashChord 매칭용.</summary>
        public bool RequestedAttackWithDash => _requestedAttackName != null && _requestedAttackWithDash;

        // ─── 방향 입력 축 추적 (2026-08-27 잡기 파생용) ───
        // 수평/수직 축이 임계값(0.5)을 새로 넘는 순간을 기록해 "마지막으로 누른 방향키"의 축을 판별한다.
        // 콤보 방향 조건(Left/Right/Horizontal vs Up/Down)이 동시 입력에서 모호해지지 않게 한다.
        private const float DirectionThreshold = 0.5f;
        [System.NonSerialized] private bool _horizHeld;
        [System.NonSerialized] private bool _vertHeld;
        [System.NonSerialized] private bool _preferHorizontal;

        /// <summary>수평/수직이 동시에 눌렸을 때 수평 축이 더 최근에 눌렸는가 (콤보 방향 조건 판별용).</summary>
        public bool PreferHorizontalDirection => _preferHorizontal;

        // ─────────── Controller가 호출하는 명령 API ───────────
        public void SetMoveInput(Vector2 input)
        {
            MoveInput = input;
            bool horiz = Mathf.Abs(input.x) >= DirectionThreshold;
            bool vert = Mathf.Abs(input.y) >= DirectionThreshold;
            if (horiz && !_horizHeld) _preferHorizontal = true;   // 수평 축 새로 눌림 → 수평 우선
            if (vert && !_vertHeld) _preferHorizontal = false;    // 수직 축 새로 눌림 → 수직 우선
            if (horiz && !vert) _preferHorizontal = true;         // 한 축만 눌림 → 그 축 우선
            if (vert && !horiz) _preferHorizontal = false;
            _horizHeld = horiz;
            _vertHeld = vert;
        }
        public void SetRunHeld(bool held) => RunHeld = held;
        public void SetGuardHeld(bool held) => GuardHeld = held;
        public void RequestJump() => _jumpRequested = true;
        public void RequestDash() => _dashRequested = true;
        /// <summary>체인 그랩 요청(LB). CharacterChainGrab이 소비.</summary>
        public void RequestChainGrab() => _chainGrabRequested = true;
        /// <summary>체인 그랩 요청 소비.</summary>
        public void ConsumeChainGrab() => _chainGrabRequested = false;
        /// <summary>절명기(처형) 요청. 그로기 적 처형 시전.</summary>
        public void RequestExecution() => _executionRequested = true;
        /// <summary>절명기 요청 소비.</summary>
        public void ConsumeExecution() => _executionRequested = false;

        /// <summary>공격 요청. attackName 비면(기본값) 캐릭터의 첫 번째 공격 = 기본 공격.</summary>
        public void RequestAttack(string attackName = "", AttackInputType inputType = AttackInputType.Light)
            => RequestAttack(attackName, inputType, false);

        /// <summary>공격 요청. withDash = 대시 키와 동시 입력(코드) — DashChord 시동 그룹 매칭.</summary>
        public void RequestAttack(string attackName, AttackInputType inputType, bool withDash)
        {
            _requestedAttackName = attackName ?? "";
            _requestedAttackInput = inputType;
            _requestedAttackWithDash = withDash;
            _attackRequestTime = Time.time;
        }

        // ─────────── 소비 ───────────
        /// <summary>원샷 요청 소비. StateManager Tick 직후 Character가 호출.</summary>
        public void ClearOneShots()
        {
            _jumpRequested = false;
            _dashRequested = false;
            _chainGrabRequested = false;
            _executionRequested = false;
            if (attackBufferSeconds <= 0f || Time.time - _attackRequestTime > attackBufferSeconds)
                _requestedAttackName = null;
        }

        public void ConsumeAttack()
        {
            _requestedAttackName = null;
            _attackRequestTime = -999f;
            _requestedAttackInput = AttackInputType.Light;
            _requestedAttackWithDash = false;
        }

        // ─────────── Combo Buffer (선입력 예약) ───────────
        // Attack 상태 중 들어온 공격 입력을 "다음 공격 1회"만 예약해 두는 슬롯.
        // 판단(콤보 가능 여부/타이밍)은 CharacterCombat·CharacterAttackState가 하고, 여기는 보관/소비만 한다.
        // 주의: Unity 직렬화가 string을 ""로 초기화할 수 있으므로 null-센티널 대신 bool 플래그를 쓴다.
        [System.NonSerialized] private bool _comboReserved;
        [System.NonSerialized] private string _comboReservedName = "";
        [System.NonSerialized] private AttackInputType _comboReservedInput = AttackInputType.Light;

        /// <summary>예약된 콤보 입력이 있는가.</summary>
        public bool HasComboReservation => _comboReserved;
        /// <summary>예약된 공격 이름 ("" = 기본 공격). 없으면 "".</summary>
        public string ComboReservedName => _comboReserved ? _comboReservedName : "";
        /// <summary>예약된 공격 입력 종류. Transition 매칭용 (Light/Heavy...).</summary>
        public AttackInputType ComboReservedInput => _comboReservedInput;

        /// <summary>
        /// 콤보 선입력 예약. 슬롯은 1개(큐 없음)이며 새 입력이 기존 예약을 덮어쓴다(최신 입력 우선).
        /// 예: X 연타 후 Y를 누르면 Y가 예약된다 — 연타해도 큐가 쌓이지 않고 마지막 입력만 남는다. (2026-08-27 스윙 중 선입력 허용)
        /// </summary>
        public bool TryReserveComboAttack(AttackInputType inputType, string attackName = "")
        {
            _comboReserved = true;
            _comboReservedInput = inputType;
            _comboReservedName = attackName ?? "";
            return true;
        }

        /// <summary>예약 소비. 다음 공격 실행 직후 호출.</summary>
        public void ConsumeComboReservation() { _comboReserved = false; _comboReservedName = ""; _comboReservedInput = AttackInputType.Light; }

        /// <summary>잔여 예약 정리. Attack 상태 종료 시 호출.</summary>
        public void ClearComboReservation() { _comboReserved = false; _comboReservedName = ""; _comboReservedInput = AttackInputType.Light; }

        /// <summary>Character.OnDisable에서 호출 — 잔여 명령 정리.</summary>
        public void OnDisableCleanup()
        {
            MoveInput = Vector2.zero;
            RunHeld = false;
            GuardHeld = false;
            _jumpRequested = _dashRequested = false;
            _requestedAttackName = null;
            _attackRequestTime = -999f;
            _comboReserved = false;
            _comboReservedName = "";
            _comboReservedInput = AttackInputType.Light;
            _requestedAttackInput = AttackInputType.Light;
            _requestedAttackWithDash = false;
        }
    }
}
