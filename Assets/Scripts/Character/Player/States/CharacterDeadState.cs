namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 사망 상태(최소 구현). CharacterCombat.ReceiveDamage에서 HP 0 도달 시 진입한다.
    /// 책임: 진행 중 공격 취소 · 모든 조작 잠금 · 상태 유지(종단 상태).
    /// Death 애니메이션 반영은 StateManager.AnimatorOnStateChanged가 담당
    /// (Alive=false + Death Trigger → AnyState 즉시 전환).
    /// 리스폰/사망 연출/Hurtbox 정리는 다음 단계 — 여기서는 상태 종단만 보장한다.
    /// </summary>
    public class CharacterDeadState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Dead;

        public override void Enter()
        {
            Movement.CanMove = false;
            Movement.CanJump = false;
            Movement.SetMoveSpeedMultiplier(0f);

            if (Combat != null)
                Combat.CancelCurrentAttack();

            // 사망 중 좌우 플립 금지. CancelCurrentAttack이 FacingLocked를 풀기 때문에 반드시 그 뒤에 건다.
            Movement.FacingLocked = true;
        }

        // Tick 없음 — 어떤 입력/조건으로도 스스로 벗어나지 않는 종단 상태.
        // 부활은 외부(리스폰 시스템)가 ResetHealth 후 ChangeState(Idle)로 처리한다.

        public override void Exit()
        {
            Movement.FacingLocked = false;
            Movement.CanMove = true;
            Movement.CanJump = true;
        }
    }
}
