using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Ladder climbing state. Timing/transition only; actual motion runs through
    /// CharacterMovement's traversal channel (BeginTraversal / SetTraversalVelocity).
    ///
    /// Entry is decided by CharacterState.CheckLadder (grounded/air states) which sets
    /// Movement.ActiveLadder before ChangeState(LadderClimb). The ladder never owns the
    /// state itself; CharacterStateManager stays the single source of truth.
    ///
    /// Forced reactions (Hit / Knockback / Airborne / Dead) always win: they call
    /// ChangeState, whose Exit runs EndTraversal and restores gravity/velocity.
    /// </summary>
    public class CharacterLadderClimbState : CharacterState
    {
        private const float SnapSpeed = 5f;      // horizontal centring speed (avoids hard teleport)
        private const float YawAlignSpeed = 10f; // root yaw slerp speed toward ladder rotation
        private const float ReachEpsilon = 0.05f;
        private int _lastClimbDir;
        private const float EnterSettleDuration = 0.4f; // 마운트(올라타기) 애니 보호 시간 — 이 동안 방향트리거/수직이동 보류
        private float _enterSettleTimer;
        // 이벤트 기반 한-칸 이동(climbingUp/Down 이벤트가 시작)
        private float _stepRemaining; // 남은 이동 거리
        private int _stepDir;         // +1 위 / -1 아래
        private float _stepSpeed;     // 이동 속도
        // 하차(빠져나가기) 잠금 — 탈출 애니가 끝날 때까지 이동·중력·회전·입력을 모두 정지시키고 상태를 유지한다.
        // 상태를 유지해야 RotationLocked/FacingLocked/traversal(중력·입력 무시)이 살아 있어 위치·각도가 고정된다.
        private bool _exiting;
        private bool _exitAtTop;
        private float _exitTimer;
        // (rolled back) _climbZ 미사용


 // 마지막으로 발사한 상하 방향(+1/-1/0) — 바뀬 때만 트리거 발사


        public override CharacterStateType Type => CharacterStateType.LadderClimb;

        public override void Enter()
        {
            var ladder = Movement != null ? Movement.ActiveLadder : null;
            if (ladder == null)
            {
                Machine.ChangeState(Machine.ResolveGroundedStateByInput());
                return;
            }

            Movement.BeginTraversal();
            Movement.RotationLocked = true;
            Movement.SetFacingRight(ladder.FaceRight); // 사다리 방향으로 flip (잠그기 전)
            Movement.FacingLocked = true;
            _enterSettleTimer = EnterSettleDuration;
            _stepRemaining = 0f;
            _stepDir = 0;
            _exiting = false;
            Machine.SetOnLadder(true);
            Machine.FireLadderEnter(!Movement.AvailableLadderAtTop); // 하단=ClimbBottomLadder / 상단=ClimbingLadderTop
        }

        /// <summary>climbingUp/Down 애니 이벤트가 호출 — 사다리 변수(StepDistance/StepSpeed)만큼 한 칸 이동 시작.</summary>
        /// <summary>climbingUp/Down 애니 이벤트(사이클 끝에 배치 권장)가 호출 — 한 칸 이동 + 버튼을 계속 누르면 다음 사이클 트리거.</summary>
        /// <summary>climbingUp/Down 애니 이벤트(선택) — 진행 중인 스템이 없을 때만 한 칸 이동 시작(상태 주도 사이클과 중복 방지). 이벤트 없어도 등반은 상태가 구동.</summary>
        /// <summary>climbingUp/Down 애니 이벤트가 호출 — 사다리 변수(StepDistance/StepSpeed)만큼 한 칸 이동 시작.</summary>
        public void RequestClimbStep(int dir)
        {
            var ladder = Movement != null ? Movement.ActiveLadder : null;
            if (ladder == null || dir == 0) return;
            _stepDir = dir > 0 ? 1 : -1;
            _stepRemaining = ladder.StepDistance;
            _stepSpeed = ladder.StepSpeed;
        }

        public override void Tick(float deltaTime)
        {
            var ladder = Movement != null ? Movement.ActiveLadder : null;
            if (ladder == null)
            {
                Machine.ChangeState(Machine.ResolveGroundedStateByInput());
                return;
            }

            // 하차 잠금: 탈출 애니가 끝날 때까지 아무것도 하지 않는다(점프·이동·상하 입력 전부 무시).
            // 위치는 traversal 속도 0으로, 각도는 RotationLocked로 고정된다.
            if (_exiting)
            {
                _exitTimer -= deltaTime;
                Movement.SetTraversalVelocity(Vector3.zero);
                Machine.SetLadderClimbParam(0f);
                if (_exitTimer <= 0f) FinishExit(ladder);
                return;
            }

            if (Machine.JumpPressed)
            {
                Machine.ChangeState(CharacterStateType.Jump);
                return;
            }

            _enterSettleTimer -= deltaTime;
            bool settling = _enterSettleTimer > 0f;

            Vector3 cur = Movement.Root.position;
            Vector3 center = ladder.CenterAtHeight(cur.y);
            Vector3 horizErr = new Vector3(center.x - cur.x, 0f, center.z - cur.z);
            Vector3 snapVel = deltaTime > 0f
                ? Vector3.ClampMagnitude(horizErr / deltaTime, SnapSpeed)
                : Vector3.zero;

            float inputY = Machine.MoveInput.y;
            float bottomY = ladder.BottomWorld.y;
            float topY = ladder.TopWorld.y;

            if (!settling)
            {
                if (inputY > 0.15f && cur.y >= topY - ReachEpsilon)
                {
                    BeginExit(ladder, true);
                    return;
                }
                if (inputY < -0.15f && cur.y <= bottomY + ReachEpsilon)
                {
                    BeginExit(ladder, false);
                    return;
                }
            }

            float stepVy = 0f;
            if (!settling && _stepRemaining > 0f)
            {
                float d = Mathf.Min(_stepSpeed * deltaTime, _stepRemaining);
                float nextY = cur.y + _stepDir * d;
                if (_stepDir > 0 && nextY > topY) d = Mathf.Max(0f, topY - cur.y);
                else if (_stepDir < 0 && nextY < bottomY) d = Mathf.Max(0f, cur.y - bottomY);
                stepVy = deltaTime > 0f ? (_stepDir * d) / deltaTime : 0f;
                _stepRemaining -= d;
            }

            // 홀드 중: 방향 트리거 발동. 뗄 순간부터: 선입력된 상하 트리거를 계속 무시(한 번 더 재생 방지)
            if (!settling)
            {
                if (inputY > 0.15f) Machine.FireLadderClimbDir(1);        // ClimbLadderBottomUp
                else if (inputY < -0.15f) Machine.FireLadderClimbDir(-1); // ClimbLadderBottomDown
                else Machine.ResetLadderDirTriggers();
            }

            // 사다리 회전 추종: RotationLocked로 스플라인 회전이 멈춘 동안 루트 yaw를 사다리에 맞춤
            if (ladder.AlignCharacterYaw && Movement.Root != null)
            {
                Movement.Root.rotation = Quaternion.Slerp(
                    Movement.Root.rotation, ladder.ClimbRotation,
                    1f - Mathf.Exp(-YawAlignSpeed * deltaTime));
            }

            Movement.SetTraversalVelocity(snapVel + Vector3.up * stepVy);
            Machine.SetLadderClimbParam(settling ? 0f : inputY);
        }

        /// <summary>하차 시작 — 탈출 애니 트리거만 쏘고 상태는 유지한다. 실제 이탈(EndTraversal + Teleport +
        /// 상태 전환)은 애니가 끝나는 FinishExit에서 한다. 상태를 유지하므로 이 동안 위치·각도가 고정되고
        /// 입력도 먹지 않는다. OnLadder(Climbing) bool도 켠 채로 둔다 — 여기서 꺼면 탈출 클립이 중간에 잘린다.</summary>
        private void BeginExit(LadderTraversable ladder, bool atTop)
        {
            _exiting = true;
            _exitAtTop = atTop;
            _exitTimer = ladder.ExitLockDuration;
            _stepRemaining = 0f;
            _stepDir = 0;
            Machine.ResetLadderDirTriggers();
            Machine.SetLadderClimbParam(0f);
            Movement.SetTraversalVelocity(Vector3.zero);
            Machine.FireLadderExit(atTop); // ClimbLadderTopExit / ClimbLadderBottomExit
        }

        /// <summary>하차 애니 종료 — 이제서야 실제로 사다리를 떠난다(탈출 지점으로 이동 후 지상 상태로).</summary>
        private void FinishExit(LadderTraversable ladder)
        {
            _exiting = false;
            Movement.EndTraversal();
            Movement.Teleport(_exitAtTop ? ladder.TopExitWorld : ladder.BottomExitWorld, Movement.FacingRight);
            Machine.ChangeState(Machine.ResolveGroundedStateByInput());
        }

        /// <summary>ladderExitEnd 애니 이벤트가 호출 — 탈출 클립이 실제로 끝난 시점에 잠금을 정확히 해제한다.
        /// 이벤트가 없는 클립은 LadderTraversable.ExitLockDuration 타이머로 풀린다.</summary>
        public void NotifyExitAnimationEnd()
        {
            if (!_exiting) return;
            var ladder = Movement != null ? Movement.ActiveLadder : null;
            if (ladder == null) { _exiting = false; return; }
            FinishExit(ladder);
        }

        public override void Exit()
        {
            if (Movement == null) return;
            _exiting = false;
            Movement.EndTraversal();
            Movement.RotationLocked = false;
            Movement.FacingLocked = false;
            Movement.ActiveLadder = null;
            Machine.SetOnLadder(false);
        }
    }
}
