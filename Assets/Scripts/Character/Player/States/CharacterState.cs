using Yeolha.BeltScroll;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 캐릭터 상태 공통 기반 (Player / Enemy 공용, 통합 CharacterStateManager용).
    ///
    /// State는 '행동 규칙'만 결정한다:
    ///   - 이 상태에서 어떤 SpeedMultiplier를 쓰는가
    ///   - 어떤 조건에서 다른 상태로 전이하는가
    /// 실제 이동/중력/가감속은 CharacterMovement가, 실제 공격 실행은 CharacterCombat이 수행한다.
    /// State가 CharacterController.Move나 Physics API를 직접 호출하지 않는다.
    ///
    /// State는 Player 전용 Input(InputAction 등)을 직접 읽지 않는다.
    /// Player/EnemyBrain이 CharacterStateManager에 전달한 의도 값
    /// (MoveInput / HasMoveInput / RunConditionMet / JumpPressed / DashPressed / AttackPressed)만 사용한다.
    /// </summary>
    public abstract class CharacterState
    {
        protected CharacterStateManager Machine { get; private set; }
        protected CharacterMovement Movement => Machine.Movement;
        protected CharacterCombat Combat => Machine.Combat;

        public abstract CharacterStateType Type { get; }

        public void Initialize(CharacterStateManager machine) => Machine = machine;

        public virtual void Enter() { }
        public virtual void Tick(float deltaTime) { }
        public virtual void Exit() { }

        // ─────────── 공통 전이 헬퍼 ───────────

        /// <summary>지상 상태 공통: 발밑이 사라지고 하강 중이면 Fall로. 전이했으면 true.</summary>
        protected bool CheckFall()
        {
            if (!Movement.IsGrounded && Movement.VerticalVelocity <= 0f)
            {
                Machine.ChangeState(CharacterStateType.Fall);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 지상 상태 공통: 공격 입력 시 Attack로. 전이했으면 true.
        /// 공격은 다른 행동과 동일하게 하나의 CharacterState로 진입한다.
        /// 실제 공격 실행/판정은 CharacterAttackState → CharacterCombat이 담당한다.
        /// </summary>
        protected bool CheckAttack()
        {
            // 지상은 항상, 공중은 해당 입력의 공중 시작 그룹(ComboGroup)이 있을 때만 진입 (판단은 Combat 소유).
            if (Machine.AttackPressed && Combat != null
                && Combat.CanStartAttackWith(Machine.RequestedAttackInput, Machine.MoveInput))
            {
                Machine.ChangeState(CharacterStateType.Attack);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 지상 상태 공통: 절명기(처형) 입력 시, 사거리 내 그로기 적이 있으면 Executing으로. 전이했으면 true.
        /// 실제 처형 실행/확정 사망은 CharacterExecutingState → CharacterCombat이 담당한다.
        /// </summary>
        protected bool CheckExecution()
        {
            if (!Machine.ExecutionPressed || Combat == null || Movement == null || !Movement.IsGrounded)
                return false;
            var target = Combat.FindExecutionTarget();
            if (target == null) return false;
            Combat.SetExecutionTarget(target);
            Machine.ConsumeExecutionRequest();
            Machine.ChangeState(CharacterStateType.Executing);
            return true;
        }

        /// <summary>지상 상태 공통: 가드 입력 홀드 시 Guard 상태로. 전이했으면 true.</summary>
        protected bool CheckGuard()
        {
            if (Machine.GuardHeld && Movement.IsGrounded && Machine.CanGuard)
            {
                Machine.ChangeState(CharacterStateType.Guard);
                return true;
            }
            return false;
        }

        /// <summary>지상 상태 공통: 점프 입력 시 Jump로. 전이했으면 true.</summary>
        protected bool CheckJump()
        {
            if (Machine.JumpPressed && Movement.IsGrounded)
            {
                Machine.ChangeState(CharacterStateType.Jump);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 지상 상태 공통: 대시 입력 시 Dash로 (쿨다운 체크 포함). 전이했으면 true.
        /// 우선순위상 Run/Move 전이보다 먼저 호출해 Idle→Move→Dash 같은 이중 전이를 피한다.
        /// </summary>
        protected bool CheckDash()
        {
            if (Machine.DashPressed && Movement.IsGrounded
                && Machine.Dash != null && Machine.Dash.CanDash)
            {
                Machine.ChangeState(CharacterStateType.Dash);
                return true;
            }
            return false;
        }

        /// <summary>공중 상태 공통: 다단 점프 입력 시 Jump 재진입. 전이했으면 true.</summary>
        protected bool CheckAirJump()
        {
            if (Machine.JumpPressed && !Movement.IsGrounded && Movement.CanJumpNow)
            {
                Machine.ChangeState(CharacterStateType.Jump);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 지상/공중 공통: 사용 가능 사다리 트리거 안에서 올바른 상하 입력이 들어오면 LadderClimb로. 전이했으면 true.
        /// 아래 Zone은 위 입력으로, 위 Zone은 아래 입력으로 진입한다(Zone은 감지 영역일 뿐, 최종 결정은 StateManager).
        /// </summary>
        protected bool CheckLadder()
        {
            var m = Movement;
            if (m == null || m.AvailableLadder == null) return false;
            float inputY = Machine.MoveInput.y;
            bool wantUp = inputY > 0.5f;
            bool wantDown = inputY < -0.5f;
            if ((!m.AvailableLadderAtTop && wantUp) || (m.AvailableLadderAtTop && wantDown))
            {
                m.ActiveLadder = m.AvailableLadder;
                Machine.ChangeState(CharacterStateType.LadderClimb);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 공중 전용: 하강 중 전방에 잡을 수 있는 벽/난간(ClimbableSurface)이 3중 판정을 통과하면 LedgeHang으로. 전이했으면 true.
        /// </summary>
        protected bool CheckLedge()
        {
            var m = Movement;
            if (m == null || m.AvailableClimbable == null) return false;
            if (m.IsGrounded) return false; // 공중(점프 중)에서만 매달림
            m.ActiveClimbable = m.AvailableClimbable;
            Machine.ChangeState(CharacterStateType.LedgeHang);
            return true;
        }

    }
}
