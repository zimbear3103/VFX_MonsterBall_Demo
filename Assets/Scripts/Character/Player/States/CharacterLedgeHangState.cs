using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Ledge-hang state. Entered from the air (Jump/Fall) via CharacterState.CheckLedge,
    /// which validates the grab (front wall + top surface + standing clearance) and sets
    /// Movement.ActiveClimbable before ChangeState(LedgeHang).
    ///
    /// The character snaps to the surface hang point, hangs with gravity off, then:
    ///   Up / Jump  -> Mantle
    ///   Down / Back -> release to Fall
    ///
    /// Forced reactions win via ChangeState -> Exit (EndTraversal restores gravity).
    /// </summary>
    public class CharacterLedgeHangState : CharacterState
    {
        private const float SnapSpeed = 8f;
        private const float YawAlignSpeed = 10f; // root yaw slerp speed toward wall rotation
        private bool _exiting;   // HangExit 모션 재생 중
        private float _exitTimer; // HangExit 남은 시간


        public override CharacterStateType Type => CharacterStateType.LedgeHang;

        public override void Enter()
        {
            var surf = Movement != null ? Movement.ActiveClimbable : null;
            if (surf == null)
            {
                Machine.ChangeState(CharacterStateType.Fall);
                return;
            }

            Movement.BeginTraversal();
            Movement.RotationLocked = true;
            Movement.SetFacingRight(surf.FaceRight); // 벽 방향으로 flip
            Movement.FacingLocked = true;
            _exiting = false;
            _exitTimer = 0f;
            Movement.SetControllerEnabled(false);      // 충돌 무시(벽 비뺔 방지)
            Movement.SetWorldPosition(surf.HangWorld);  // hang 위치로 즉시
            Machine.SetLedgeHang(true); // Hang 트리거 → HangingIdle
        }

        public override void Tick(float deltaTime)
        {
            var surf = Movement != null ? Movement.ActiveClimbable : null;
            if (surf == null)
            {
                Machine.ChangeState(CharacterStateType.Fall);
                return;
            }

            // 벽 회전 추종: RotationLocked로 스플라인 회전이 멈춘 동안 루트 yaw를 벽에 맞춤
            if (surf.AlignCharacterYaw && Movement.Root != null)
            {
                Movement.Root.rotation = Quaternion.Slerp(
                    Movement.Root.rotation, surf.HangRotation,
                    1f - Mathf.Exp(-YawAlignSpeed * deltaTime));
            }

            if (_exiting)
            {
                // HangExit 모션 재생 중 — 제자리 고정, 끝나면 컨트롤러 복원 + ExitPoint로 이동 후 탈출
                Movement.SetWorldPosition(surf.HangWorld);
                _exitTimer -= deltaTime;
                if (_exitTimer <= 0f)
                {
                    Movement.SetControllerEnabled(true);
                    Movement.EndTraversal();
                    Movement.Teleport(surf.ExitWorld, Movement.FacingRight);
                    Movement.ActiveClimbable = null;
                    Machine.ChangeState(Machine.ResolveGroundedStateByInput());
                }
                return;
            }

            // 매달림: hang 지점 고정(컨트롤러 꺼져 있으므로 직접 배치)
            Movement.SetWorldPosition(surf.HangWorld);

            // 스페이스(점프) → HangExit 시작
            if (Machine.JumpPressed)
            {
                Machine.SetLedgeHang(false); // HangExit 트리거
                _exiting = true;
                _exitTimer = surf.ExitDuration;
            }
        }

        public override void Exit()
        {
            if (Movement == null) return;
            Movement.SetControllerEnabled(true); // safety restore
            // hang 중 컨트롤러를 꺼서 OnTriggerExit가 안 뜼므로 AvailableClimbable을 수동 해제
            Movement.ClearAvailableClimbable(Movement.AvailableClimbable);
            Movement.EndTraversal();
            Movement.RotationLocked = false;
            Movement.FacingLocked = false;
            Movement.ActiveClimbable = null;
        }
    }
}
