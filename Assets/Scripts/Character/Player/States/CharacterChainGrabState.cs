namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 체인 그랩(그래플 훅) 진행 중 플레이어 잠금 상태. 실제 시퀀스(발사→끌어오기→Carry→피니셔/발사)는
    /// CharacterChainGrab(MonoBehaviour)가 소유·구동하고, 이 상태는 이동/점프/공격/가드 입력을 잠그기만 한다.
    /// 종료는 CharacterChainGrab이 ChangeState로 직접 빠져나온다(자동 종료 없음).
    /// </summary>
    public class CharacterChainGrabState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.ChainGrab;

        public override void Enter()
        {
            Movement.CanMove = false;
            Movement.CanJump = false;
            Movement.SetMoveSpeedMultiplier(0f);
            Combat?.CancelCurrentAttack();
        }

        public override void Tick(float deltaTime)
        {
            // 아무것도 하지 않는다 — CharacterChainGrab이 시퀀스를 구동한다.
        }

        public override void Exit()
        {
            // 이동 배율은 다음 상태(Idle/Move/Run 등)의 Enter가 소유·설정하므로 여기서 건드리지 않는다.
            Movement.CanMove = true;
            Movement.CanJump = true;
            // 캐리 진입 시 건 좌우 플립 잠금을 해제한다(EnterCarryMovement와 대칭). 다음 상태 Enter보다 먼저 실행되므로 안전.
            Movement.FacingLocked = false;
        }
    }
}
