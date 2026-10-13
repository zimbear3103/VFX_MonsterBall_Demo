namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Animator 파라미터 이름 상수 — 캐릭터 공용.
    /// (2026-08-20) 인스펙터 SerializeField 문자열을 제거하고 상수로 통합.
    /// 파라미터 존재 여부는 각 시스템의 _availableParams 가드가 계속 담당한다.
    /// </summary>
    public static class AnimParams
    {
        public const string MoveSpeed = "MoveSpeed";
        public const string WalkSpeedMultiplier = "WalkSpeedMultiplier";
        public const string Grounded = "Grounded";
        public const string Walking = "Walking";
        public const string Running = "Running";

        public const string DirRunForward = "DirectionalRunningForward";
        public const string DirRunBackward = "DirectionalRunningBackward";
        public const string DirRunForwardStart = "DirectionalRunningForwardStart";
        public const string DirRunBackwardStart = "DirectionalRunningBackwardStart";

        public const string Jumping = "Jumping";
        public const string Dash = "Dash";
        public const string DashDirection = "DashDirection";

        public const string Alive = "Alive";
        public const string IsAlive = "IsAlive";
        public const string BattleMode = "BattleMode";

        public const string RelForward = "RelativeForwardSpeedNormalized";
        public const string RelLateral = "RelativeLateralSpeedNormalized";

        public const string AttackStage = "AttackStage";
        public const string Hit = "DamagedBackward";
        public const string Death = "Death";
        public const string ReturnMovement = "ReturnMovement";

        // Guard (Phase 1)
        public const string Guarding = "Guarding";
        public const string GuardStart = "StartGuard";
        public const string GuardStop = "StopGuard";
        public const string GuardDamaged = "GuardDamaged";
        public const string GuardBreak = "StartStun";

        // Forced hit reactions (Knockback / Airborne) — optional; guarded by _availableParams so missing params are no-ops.
        public const string Knockback = "DamagedBackward"; // 지면 수평 넉백(밀림) = 방향 경직 애니. 구프로젝트 CharacterDirectionalDamage 대응.
        public const string Airborne = "Airborne"; // (legacy, no-op fallback — 방향별은 아래 Forward/Backward 사용)
        public const string AirborneForward = "AirborneForward";
        public const string AirborneBackward = "AirborneBackward";
        public const string AirborneLand = "AirborneLand";
        public const string Stun = "Stun";                 // 넉다운(넘어짐) 유지 bool — 구프로젝트 CharacterAirborne
        public const string Getup = "Getup";               // 기상 트리거
        public const string QuickGetup = "QuickGetup";     // 빠른 기상 트리거
        public const string GetupTag = "Getup";            // 기상 애니 상태 태그(완료 판정용)
        public const string Falldown = "Falldown";            // 지상 넉다운 진입 트리거(넘어짐)
        public const string Falldowning = "Falldowning";      // 넉다운 유지 bool(대체 게이트)
        public const string KnockdownForward = "KnockdownForward";
        public const string KnockdownBackward = "KnockdownBackward";
        public const string KnockdownTag = "Knockdown";        // 넉다운(다운) 상태 태그

        // Parry (Phase 2)
        public const string Parrying = "Parrying";
        public const string ParryingStage = "ParryingStage"; // int: 0=Parrying1, 1=Parrying2 (패링 성공마다 번갈아 재생)
        public const string Parried = "Parried";
        public const string ParryingTag = "Parrying";        // 패링 반응 애니 상태 태그(이동잠금 = 모션 길이 판정용)
        public const string GuardDamagedTag = "GuardDamaged"; // 가드 피격 반응 애니 상태 태그(이동잠금 = 모션 길이 판정용)

        // Execution (절명기 / 처형) — 공유 컨트롤러 Execute 서브머신 (구프로젝트 CharacterExecution 대응)
        public const string Execute = "Execute";           // trigger: 시전자 처형 애니 진입
        public const string ExecuteType = "ExecuteType";   // int: 처형 타입 분기 (ExecuteA/B/C...)
        public const string Executed = "Executed";         // trigger: 피격자 피처형 애니
        public const string Groggy = "Groggy";             // bool: 그로기 루프 유지
        public const string GroggyStart = "StartGroggy";   // trigger: 그로기 진입

        // Action Grab (콤보 파생 잡기) — 구프로젝트 CharacterGrab/CharacterGrabbed 대응
        // 컨트롤러 실측: AnyState→ActionGrab(SM)은 "Grab" 트리거, →ActionGrabbed(SM)은 "Grabbed" 트리거.
        // GrabType 0=ActionGrab1, 1=ActionGrab2 … / ExecuteType 0=ExecuteA, 1=ExecuteB, 2=ExecuteC.
        public const string ActionGrab = "Grab";           // trigger: 시전자 잡기 애니 (GrabType 슬롯 분기)
        public const string ActionGrabbed = "Grabbed";     // trigger: 피격자 잡힘 애니
        public const string GrabType = "GrabType";         // int: 잡기 타입 슬롯 (0 = ActionGrab1)

        // Chain Grab (Phase 5)
        public const string ChainGrab = "ChainGrab";
        public const string ChainGrabHolding = "ChainGrabHolding";
        public const string ChainGrabMiss = "ChainGrabMiss";
        public const string ChainGrabCarrying = "ChainGrabCarrying";
        public const string CarryAttack = "CarryAttack";
        public const string CarryAttacking = "CarryAttacking";
        public const string CarryAttackFinish = "CarryAttackFinish";
        // Chain Grab victim (enemy) side — 공유 컨트롤러의 피격자 상태. 구프로젝트 CharacterGrabbed 대응.
        public const string AirGrabCarried = "AirGrabCarried";         // bool: 잡힌/캐리 포즈 유지(placeholder 클립 = 피격 마지막 자세 유지)
        public const string AirGrabCarriedHit = "AirGrabCarriedHit";   // trigger: 캐리 중 피격(찌르기) 리액션
        public const string CarriedHit = "CarriedHit";                 // bool(victim): 피격 리액션 유지 게이트(AnyState 복귀 방지)
        public const string CarryFinishFly = "CarryFinishFly";         // trigger(victim): 던지기 시 적 전용 fly 클립 재생

        // Traversal (Ladder / Ledge / Mantle) — optional; guarded by _availableParams so missing params are no-ops.
        public const string OnLadder = "Climbing";             // 기존 콘트롤러 Bool — 사다리 붙음 유지              // bool: 사다리 붙음 유지
        public const string LadderClimbSpeed = "ClimbLadderSpeed"; // float: +1 위 / -1 아래 (블렌드/재생속도)
        public const string LadderTopExit = "ExitLadderTop";    // trigger: 사다리 상단 올라서기
        public const string LedgeHangEnter = "Hang";            // 기존 트리거 — 난간 매달림 진입
        public const string LedgeHangExit = "HangExit";         // 기존 트리거 — 난간 해제            // bool: 난간 매달림 유지
        public const string Mantle = "ExitLadderTop";
        // 기존 "Climbing Ladder" 서브 상태머신 구동 파라미터 (직접 이름)
        public const string ClimbEnterBottom = "ClimbingLadderBottom";  // 하단 진입(올라가기 시작) 트리거
        public const string ClimbEnterTop = "ClimbingLadderTop";      // 상단 진입(위에서 내려가기 시작)
        public const string ClimbGoUp = "ClimbLadderBottomUp";      // 올라갈 때
        public const string ClimbGoDown = "ClimbLadderBottomDown";  // 내려갈 때
        public const string ClimbExitTop = "ClimbLadderTopExit";    // 최상단 도착 탈출
        public const string ClimbExitBottom = "ClimbLadderBottomExit"; // 최하단 도착 탈출        // 상단 진입(내려가기 시작) 트리거
        public const string LadderDirection = "ClimbLadderDirection"; // int: +1 위 / -1 아래 / 0 대기
        public const string LadderValue = "ClimbLadderValue";        // float: |상하입력| 0~1
        public const string ExitLadderBottomTrig = "ExitLadderBottom";

        // Dialogue — optional; guarded by _availableParams so missing params are no-ops.
        public const string Talk = "Talk";       // trigger: (구) 대화 시작 — 지금은 쓰지 않는다
        public const string Talk1 = "Talk1";     // trigger: 대화 연출 1
        public const string Talk2 = "Talk2";     // trigger: 대화 연출 2 (보조 NPC 동반)
        public const string IsTalk = "IsTalk";   // bool: 대화 중 유지
        public const string CompanionShow = "Show"; // bool(보조 NPC): 대화 중 등장 유지
           // 맨틀=상단 올라서기 클립 재사용(프록시)                  // trigger: 난간 올라서기(맨틀)

    }
}
