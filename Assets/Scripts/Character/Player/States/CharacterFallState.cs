namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 하강 상태. Jump 정점 이후뿐 아니라, 절벽에서 걸어 나가는 등
    /// Jump를 거치지 않고도 지상 상태에서 직접 진입할 수 있다.
    ///
    /// 전이:
    ///   다단 점프 입력 → Jump
    ///   Grounded       → Land
    /// </summary>
    public class CharacterFallState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Fall;

        public override void Enter()
        {
            // Idle(배율 0)에서 떨어진 경우에도 공중 조향이 가능하도록 배율 보장
            Movement.SetMoveSpeedMultiplier(Machine.RunConditionMet
                ? Machine.RunMultiplier
                : Machine.WalkMultiplier);
        }

        public override void Tick(float deltaTime)
        {
            // 공중 공격 (공중 시작 그룹이 있는 입력만 Attack 진입 — 구버전 점프 공격/공중콤보)
            if (CheckAttack()) return;

            // 공중 추가 점프 (JumpCount 스탯이 2 이상일 때 Fall → Jump 재진입)
            if (CheckAirJump()) return;

            // 사다리/난간 잡기(하강 중)
            if (CheckLadder()) return;
            // Ledge(난간) 매달림은 Jump 상태에서만 — Fall에서는 제외


            if (Movement.IsGrounded)
                Machine.ChangeState(CharacterStateType.Land);
        }
    }
}
