namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 정지 상태. 이동 목표 속도 = 0 (실제 감속은 CharacterMovement의 Deceleration이 수행).
    ///
    /// 전이:
    ///   접지 해제 + 하강     → Fall
    ///   Attack 입력          → Attack
    ///   Jump 입력            → Jump
    ///   Dash 입력            → Dash
    ///   이동 입력 + Run 조건 → Run
    ///   이동 입력            → Move
    /// </summary>
    public class CharacterIdleState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Idle;

        public override void Enter()
        {
            Movement.SetMoveSpeedMultiplier(0f);
        }

        public override void Tick(float deltaTime)
        {
            // 우선순위: ① Ground/Air 변화 → ② Attack → ③ Jump → ④ Dash → ⑤ Run/Move
            if (CheckFall()) return;
            if (CheckExecution()) return;
            
if (CheckAttack()) return;
            if (CheckJump()) return;
            if (CheckDash()) return;
            if (CheckGuard()) return;
            if (CheckLadder()) return;


            if (Machine.HasMoveInput)
            {
                Machine.ChangeState(Machine.RunConditionMet
                    ? CharacterStateType.Run
                    : CharacterStateType.Move);
            }
        }
    }
}
