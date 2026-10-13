namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 피처형 상태 — 처형(절명기)을 당하는 중인 대상(피격자).
    ///
    /// 진입: 그로기 대상에 대해 시전자(CharacterExecutingState)가 Combat.EnterExecuted()로 전환한다.
    /// 이 상태에서는 이동/공격이 잠기고, 시전자의 연출 종료 시 Combat.KillByExecution()이 HP를 0으로 만들어
    /// Dead로 전이시킨다(별도 히트박스/데미지 롤 없음 — 구버전 KillSilent 이관).
    ///
    /// 안전망: 시전자가 도중에 사라져도 무한 잠금되지 않도록 일정 시간 후 그로기로 복귀한다.
    /// </summary>
    public class CharacterExecutedState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Executed;

        // 안전망 타임아웃(초) — 시전자가 도중에 사라진 경우에만 발동해야 하므로
        // 처형 총 시간(approach + executionDuration)보다 충분히 커야 정상 처형 전에 풀리지 않는다.
        private const float SafetyTimeout = 10f;
        private float _timer;

        public override void Enter()
        {
            _timer = 0f;
            Movement.CanMove = false;
            Movement.CanJump = false;
            Movement.SetMoveSpeedMultiplier(0f);
            if (Combat != null) Combat.CancelCurrentAttack();
            // 피처형 애니(Executed 트리거)는 시전자 접근이 끝나는 시점에
            // CharacterExecutingState.BeginExecuteAnimation이 재생한다(플레이어-적 동기화).
        }

        public override void Tick(float deltaTime)
        {
            _timer += deltaTime;
            // 시전자가 정상 처형하면 KillByExecution → Dead로 전이한다. 그 전에 시전자가 사라진 경우의 안전망.
            if (_timer > SafetyTimeout)
                Machine.ChangeState(CharacterStateType.Groggy);
        }

        public override void Exit()
        {
            Movement.CanMove = true;
            Movement.CanJump = true;
        }
    }
}
