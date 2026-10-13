namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 점프 상승 상태. 진입 시 한 번만 CharacterMovement.Jump()를 실행한다.
    /// 공중 수평 이동/관성은 CharacterMovement의 기존 공중 제어 로직을 그대로 사용한다.
    ///
    /// 전이:
    ///   VerticalVelocity <= 0 → Fall (또는 이미 접지면 Land)
    /// </summary>
    public class CharacterJumpState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Jump;

        // 이륙 직후 공중 공격 시작 금지 시간(초) — 점프+공격 거의 동시 입력 시 지면 높이에서 점프킥이 시작되는 것 방지 (2026-09-06).
        // 공격 요청은 CommandManager 버퍼(attackBufferSeconds)에 남아 있으므로 이 시간이 지나면 자동으로 공중 공격이 나간다.
        private const float MinAirtimeBeforeAttack = 0.08f;

        private float _airTimer;

        public override void Enter()
        {
            _airTimer = 0f;

            // 공중 조작감을 위해 진입 시점의 지상 배율을 유지한다
            Movement.SetMoveSpeedMultiplier(Machine.RunConditionMet
                ? Machine.RunMultiplier
                : Machine.WalkMultiplier);

            Movement.Jump(); // 진입 시 1회만. Tick에서 반복 호출하지 않는다.
        }

        public override void Tick(float deltaTime)
        {
            _airTimer += deltaTime;

            // 공중 공격 (공중 시작 그룹이 있는 입력만 Attack 진입 — 구버전 점프 공격/공중콤보). 이륙 직후 유예 경과 후에만.
            if (!Movement.IsGrounded && _airTimer >= MinAirtimeBeforeAttack && CheckAttack()) return;

            // 상승 중엔 난간 잡기 금지(CheckLedge 내부 상승속도 가드), 사다리는 가능
            if (CheckLadder()) return;
            if (CheckLedge()) return;


            // 정점 통과 → 하강
            if (Movement.VerticalVelocity <= 0f)
            {
                Machine.ChangeState(Movement.IsGrounded
                    ? CharacterStateType.Land
                    : CharacterStateType.Fall);
            }
        }
    }
}
