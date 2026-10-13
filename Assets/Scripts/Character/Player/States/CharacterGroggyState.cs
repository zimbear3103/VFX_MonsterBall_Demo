namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 그로기 상태 — 투혼(BattleSpirit)이 0이 되어 무력화된 적. 처형(절명기)의 대상이 될 수 있다(취약).
    ///
    /// 진입: CharacterCombat.DrainTouhon이 투혼 0 도달 시, groggyOnTouhonDepleted가 켜진 캐릭터(적)에 대해
    /// EnterGroggy() → ChangeState(Groggy)로 진입시킨다(플레이어/보스는 대신 허주 디버프).
    ///
    /// 이 상태는 이동/점프/공격을 잠그고 대기한다. 스스로 빠져나오지 않는다:
    ///   - 처형되지 않으면 CharacterCombat의 투혼 리셋 타이머(touhonResetDelay) 만료 시 ExitGroggy()가 Idle로 복귀시킨다.
    ///   - 처형되면 CharacterExecutingState 시전자가 EnterExecuted()로 Executed 상태로 전환한다.
    /// (상태=잠금/타이밍, 회복 규칙=CharacterCombat 소유 — 넉백/에어본과 동일한 책임 분리.)
    /// </summary>
    public class CharacterGroggyState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Groggy;

        public override void Enter()
        {
            Movement.CanMove = false;
            Movement.CanJump = false;
            Movement.SetMoveSpeedMultiplier(0f);
            if (Combat != null) Combat.CancelCurrentAttack();
        }

        public override void Exit()
        {
            Movement.CanMove = true;
            Movement.CanJump = true;
        }
    }
}
