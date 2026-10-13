namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 콤보 파생 잡기(ActionGrab) 진행 중 플레이어 잠금 상태.
    /// 실제 시퀀스(정렬→동기 애니→Takedown→종료/복구)는 CharacterActionGrab(MonoBehaviour)가 소유·구동하고,
    /// 이 상태는 이동/점프/공격 입력을 잠그기만 한다(ChainGrab 상태와 동일 패턴).
    /// 종료는 CharacterActionGrab이 ChangeState로 직접 빠져나온다(자동 종료 없음).
    /// 외부 요인(피격/넉백 등)으로 상태를 이탈하면 CharacterActionGrab의 감시 루프가 잡힌 적을 복구한다.
    /// </summary>
    public class CharacterGrabState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Grab;

        public override void Enter()
        {
            // Entering Grab always cancels the current attack (hitbox/VFX off) — matches
            // CharacterChainGrabState.Enter. Called before the movement lock because
            // CancelCurrentAttack releases FacingLocked, which we re-assert below.
            Combat?.CancelCurrentAttack();
            Movement.CanMove = false;
            Movement.CanJump = false;
            Movement.SetMoveSpeedMultiplier(0f);
            Movement.FacingLocked = true;
        }

        public override void Tick(float deltaTime)
        {
            // 아무것도 하지 않는다 — CharacterActionGrab이 시퀀스를 구동한다.
        }

        public override void Exit()
        {
            Movement.CanMove = true;
            Movement.CanJump = true;
            Movement.FacingLocked = false;
        }
    }
}
