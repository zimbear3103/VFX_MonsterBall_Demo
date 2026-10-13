namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 공격 상태. 공격도 다른 행동과 동일하게 CharacterStateManager가 관리하는 하나의 상태다.
    ///
    /// 책임:
    ///   - 진입 시 이동 제한 규칙 적용 (지상: CanMove=false / 공중: 점프 궤적 보존 위해 CanMove 유지, CanJump=false)
    ///   - CharacterCombat에 공격 실행 요청 (ExecuteLightAttack1)
    ///   - 공격 종료 확인 (Combat.IsAttackFinished)
    ///   - 종료 시 다음 상태 결정 (입력 기준 Idle/Move/Run)
    ///   - 후딜 이동 캔슬: ComboWindow 진입 + AttackAction.MoveCancelDelay 경과 + 이동 입력이면 공격을 끝내고 Move/Run으로 (2026-09-06)
    ///   - 공중 시작 공격 착지: 접지 순간 공격 종료(이펙트 정리) 후 Land로 — 착지 모션 즉시 (급강하 제외, 2026-09-06)
    ///
    /// 실제 공격 애니메이션/이펙트/히트박스 판정은 CharacterCombat이 담당한다.
    /// 지상/공중 모두에서 진입한다. 공중 공격(점프 공격/공중콤보)은 진입 시점의 점프 궤적을 유지한다.
    ///
    /// 흐름:
    ///   Idle/Move/Run → Attack → CharacterCombat.ExecuteLightAttack1()
    ///   → Attack Animation / Effect / Hitbox → 종료 → Idle/Move/Run
    /// </summary>
    public class CharacterAttackState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Attack;

        // 강공격 대시 캔슬 시 잔상(Afterimage) 지속시간(초). 대시 길이(약 0.18)를 덮도록 약간 길게.
        private const float HeavyCancelAfterimageDuration = 0.2f;

        public override void Enter()
        {
            // 공격 중 이동/점프 잠금 (상태가 곧 판단 기준 — 별도 CombatLocked 플래그를 쓰지 않는다).
            // 단, 공중 공격은 점프 궤적(수평 관성/에어컨트롤)을 그대로 보존해야 하므로 지상에서만 이동을 잠근다.
            // 공중에서 CanMove를 끄면 목표 수평속도가 0으로 감쇠되어 점프의 전진 관성이 사라지고
            // 공격하는 순간 '그 자리에서 뚝 떨어지는' 현상이 생긴다. 공격을 하든 말든 점프 궤적은 동일해야 한다.
            // (급강하는 이 잠금이 아니라 개별 공격의 DiveFall이 담당한다 — 예: JumpSmash.)
            Movement.CanMove = !Movement.IsGrounded;
            Movement.CanJump = false;
            // 새 공격 시작 — 이전에 남았을 수 있는 콤보 예약을 정리하고 시작한다.
            Machine.ClearComboReservation();


            if (Combat != null)
            {
                // 요청된 공격 이름으로 실행 ("" = attacks[0] 기본 공격). 실행 후 요청 소비(버퍼 포함).
                // 이름 지정 시 직접 실행, 비면 ComboGroup 시작 조건(입력/상태/방향)으로 선택 (구버전 TargetState 이식)
                Combat.ExecuteStartAttack(Machine.RequestedAttackName, Machine.RequestedAttackInput, Machine.MoveInput);
                Machine.ConsumeAttackRequest();
            }
            else
            {
                // Combat이 없으면 공격할 수 없으므로 즉시 복귀
                Machine.ChangeState(Machine.ResolveGroundedStateByInput());
            }
        }

        public override void Tick(float deltaTime)
        {
            if (Combat == null)
            {
                Machine.ChangeState(Movement.IsGrounded
                    ? Machine.ResolveGroundedStateByInput()
                    : CharacterStateType.Fall);
                return;
            }

            // 스윙 중 선입력 허용 (2026-08-27, 구버전 '선입력 없음' 규칙 폐기):
            // 공격 중 들어온 입력은 Combo Window 밖(스윙 중)이어도 예약해 두고, 창이 열리는 순간 실행한다.
            // 예약 필터는 느슨한 검사(HasAnyBranchForInput: input 매칭만) — requireHit/지상/방향 등
            // 시점 의존 조건은 실행 시점(TryContinueCombo)에 검증한다. 히트 판정 전에 미리 눌러도 정상 연결.
            // 예약 슬롯은 1개이며 최신 입력이 덮어쓴다(X 연타 후 Y → Y 예약).
            if (Machine.AttackPressed)
            {
                if (Combat.HasAnyBranchForInput(Machine.RequestedAttackInput))
                    Machine.TryReserveComboAttack(Machine.RequestedAttackInput, Machine.RequestedAttackName);
                Machine.ConsumeAttackRequest();
            }

            // 공격 실행 진행 (판정창/타임아웃/애니 종료 감지)
            Combat.TickAttack(deltaTime);

            // 공중 시작 공격이 착지하면 지상 공격 규칙으로 이동 잠금 (2026-09-14):
            // Enter에서 공중 관성 보존을 위해 CanMove를 켜 둔 상태가 착지 후에도 남아, 급강하(JumpSmash)의
            // 착지 내려찍기 재생 중 방향 입력으로 캐릭터가 미끄러지던 문제. 착지 순간 한 번만 잠근다(Exit에서 복구).
            if (Movement.CanMove && Movement.IsGrounded)
                Movement.CanMove = false;

            // 공중 시작 공격 착지 (2026-09-06 점프킥 삑사리 수정): 발이 닿는 순간 공격을 끝내고(이펙트/히트박스 즉시 정리) Land로 간다 —
            // MinDuration 무관. Animator Grounded는 공격 중 StateManager가 보류하므로 애니는 여기서 결정한 Land(착지 CrossFade)만 따른다.
            // Idle 직행 대신 착지 애니/피드백을 거친다. 급강하(JumpSmash)는 HandlesAirborneLanding=false라 기존 흐름 유지.
            if (Combat.HandlesAirborneLanding && Movement.IsGrounded)
            {
                Combat.EndAttackForLanding();
                Machine.ChangeState(CharacterStateType.Land);
                return;
            }

            // 올려치기 점프 캔슬 (2026-08-27): 공중콤보 시동기(올려치기)가 적중한 뒤에는
            // 남은 모션을 기다리지 않고 점프로 즉시 캔슬해 띄운 적을 따라 올라갈 수 있다.
            // (Exit에서 CanJump가 복구된 뒤 JumpState.Enter가 Movement.Jump()를 실행하므로 잠금과 충돌 없음)
            if (Machine.JumpPressed && Movement.IsGrounded
                && Combat.CurrentAction != null
                && Combat.CurrentAction.IsAirComboLauncher
                && Combat.CurrentAction.HasHitThisUse)
            {
                Machine.ChangeState(CharacterStateType.Jump);
                return;
            }

            // 강공격 대시 캔슬 (구버전 CharacterDash3DTrigger 이관): 강공격류 실행 중 대시 입력(키보드 P / 패드 RT)이
            // 들어오면 남은 모션을 즉시 캔슬하고 Dash로 전환한다. 캔슬 순간 잔상을 남긴다(구버전 characterTrail.Spawn).
            // 지상 + 대시 가능(쿨다운) 시에만 성립. Exit에서 Combat.StopAttack + 이동 잠금 복구가 수행된다.
            if (Machine.DashPressed && Movement.IsGrounded
                && Combat.CurrentAction != null
                && (Combat.CurrentAttackInput == AttackInputType.Heavy
                    || Combat.CurrentAttackInput == AttackInputType.HeavyHold)
                && Machine.Dash != null && Machine.Dash.CanDash)
            {
                if (Machine.Afterimage != null)
                    Machine.Afterimage.Spawn(HeavyCancelAfterimageDuration);
                Movement.PlayCancelMoveFeedback();
                Machine.ChangeState(CharacterStateType.Dash);
                return;
            }

            // Combo Window + 예약 입력이 있으면 다음 타로 연결 (LightAttack1→2→3).
            // 실행 여부 판단은 Combat(CanContinueCombo)이, 예약 보관은 CommandManager가 담당.
            if (Machine.HasComboReservation && Combat.TryContinueCombo(Machine.ReservedAttackInput))
            {
                Machine.ConsumeComboReservation();
                return;
            }

            // 후딜 이동 캔슬 (2026-09-06): 본 스윙이 끝나고 후딜(ComboWindow) 진입 후 AttackAction.MoveCancelDelay가 지났으면
            // 이동 입력만으로 공격을 끝내고 즉시 Move/Run으로 나간다 — 입력 → State → Animation 순서.
            // 콤보 예약(위)이 우선이며, 캔슬돼도 Combat이 콤보 유예를 남겨 유예 안 재입력은 다음 타로 이어진다.
            // 이동 입력이 없으면 기존대로 후딜을 끝까지 재생하고 Idle로 간다. 공중 공격은 착지/Fall 흐름 그대로.
            if (Movement.IsGrounded && Machine.HasMoveInput && Combat.CanMoveCancel)
            {
                Combat.EndAttackForMoveCancel();
                ResolveNextStateAfterAttack();
                return;
            }

            if (Combat.IsAttackFinished)
                ResolveNextStateAfterAttack();
        }

        /// <summary>
        /// 공격 종료 후 다음 상태 결정 — 자연 종료/타임아웃/이동 캔슬 공통.
        /// 남은 콤보 예약이 있으면 먼저 시작 경로로 구제하고, 아니면 현재 입력 기준 Idle/Move/Run(공중이면 Fall)으로 전이한다.
        /// 애니메이션은 여기서 건드리지 않는다 — CharacterStateManager가 새 상태에 맞춰 로코모션을 동기화한다.
        /// </summary>
        private void ResolveNextStateAfterAttack()
        {
            // 종료 프레임 예약 구제 (2026-08-27): 공격이 끝나는 프레임에 눌린 입력은 예약으로 소비된 뒤
            // 창이 다시 열리지 않아 연결에 실패하고, Exit에서 폐기되어 완전히 증발했다(Y가 아무것도 안 나오는 씹힘).
            // 남은 예약을 버리지 않고 시작 경로로 넘긴다 — 유예(입력 인지형) 안이면 파생/다음 타, 아니면 시동기로 실행.
            if (Machine.HasComboReservation)
            {
                var input = Machine.ReservedAttackInput;
                var name = Machine.ReservedAttackName;
                Machine.ClearComboReservation();
                Combat.ExecuteStartAttack(name, input, Machine.MoveInput);
                if (!Combat.IsAttackFinished)
                    return; // 연결 성공 — Attack 상태 유지
            }
            Machine.ChangeState(Movement.IsGrounded
                ? Machine.ResolveGroundedStateByInput()
                : CharacterStateType.Fall);
        }

        public override void Exit()
        {
            // 외부 요인으로 강제 전환되는 경우에도 공격 실행을 정리하고 이동 잠금을 해제
            if (Combat != null)
                Combat.StopAttack();

            // 남은 콤보 예약 정리 — Attack 종료 후 의도치 않은 Attack1 자동 실행 방지
            Machine.ClearComboReservation();

            Movement.CanMove = true;
            Movement.CanJump = true;
        }
    }
}
