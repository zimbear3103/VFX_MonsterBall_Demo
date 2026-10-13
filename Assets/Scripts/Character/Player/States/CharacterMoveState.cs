namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 걷기 상태. WalkMultiplier를 CharacterMovement에 전달한다.
    ///
    /// 전이:
    ///   접지 해제 + 하강 → Fall
    ///   Attack 입력      → Attack
    ///   Jump 입력        → Jump
    ///   Dash 입력        → Dash
    ///   Run 조건 충족    → Run
    ///   이동 입력 없음   → Idle
    /// </summary>
    public class CharacterMoveState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Move;

        public override void Enter()
        {
            Movement.SetMoveSpeedMultiplier(Machine.WalkMultiplier);
        }

        public override void Tick(float deltaTime)
        {
            if (CheckFall()) return;
            if (CheckExecution()) return;
            if (CheckAttack()) return;
            if (CheckJump()) return;
            if (CheckDash()) return;
            if (CheckGuard()) return;
            if (CheckLadder()) return;


            if (Machine.RunConditionMet)
            {
                Machine.ChangeState(CharacterStateType.Run);
                return;
            }

            if (!Machine.HasMoveInput)
                Machine.ChangeState(CharacterStateType.Idle);
        }
    }
}
