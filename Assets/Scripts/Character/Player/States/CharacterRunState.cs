namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 달리기 상태. RunMultiplier를 CharacterMovement에 전달한다.
    /// Run 조건 방식(더블탭/Sprint 버튼/스틱 강도 등)은 Player가 해석해 RunConditionMet로 전달한다.
    ///
    /// 전이:
    ///   접지 해제 + 하강 → Fall
    ///   Attack 입력      → Attack
    ///   Jump 입력        → Jump
    ///   Dash 입력        → Dash
    ///   Run 조건 해제    → Move
    ///   이동 입력 없음   → Idle
    /// </summary>
    public class CharacterRunState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Run;

        // 달리기 발밑 연기 타이머 (구버전 RunCycle RunEffectLeft/Right 이벤트 이식)
        private float _stepTimer;

        public override void Enter()
        {
            Movement.SetMoveSpeedMultiplier(Machine.RunMultiplier);
            _stepTimer = 0f;
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


            if (!Machine.HasMoveInput)
            {
                Machine.ChangeState(CharacterStateType.Idle);
                return;
            }

            if (!Machine.RunConditionMet)
            {
                Machine.ChangeState(CharacterStateType.Move);
                return;
            }

            // 달리기 연기: 일정 주기로 발밑 스모크 재생
            if (Movement.MovementFeedbackEnabled && Movement.IsGrounded)
            {
                _stepTimer += deltaTime;
                if (_stepTimer >= Movement.RunStepInterval)
                {
                    _stepTimer = 0f;
                    Movement.PlayRunStepFeedback();
                }
            }
        }
    }
}
