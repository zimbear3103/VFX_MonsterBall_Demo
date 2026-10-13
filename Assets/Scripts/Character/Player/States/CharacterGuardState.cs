namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 가드 자세 상태. 가드 입력(홀드) 유지 중 진입/유지되며, 실제 데미지 감소 판정은
    /// CharacterCombat.ReceiveDamage → CharacterDefense.Resolve가 수행한다(이 상태는 자세/잠금만).
    ///
    /// 진입: 지상 + 가드 입력 홀드(CheckGuard). 종료: 홀드 해제 → 입력 기준 지상 상태 복귀.
    /// 공격 입력 시에는 가드를 풀고 Attack로(CheckAttack), 낙하 시 Fall로 전이한다.
    ///
    /// 가드 성공 밀림: CharacterCombat.ApplyGuardKnockback이 Movement.BeginKnockback + Defense.BeginGuardKnockback을
    /// 호출하면, 이 상태가 GuardKnockbackDuration 동안 이동을 잠그고(슬라이드 유지) 창이 끝나면 EndKnockback으로 해제한다.
    /// (상태=타이밍, 이동=Movement 넉백 채널 — Hit/Knockdown과 동일한 소유 규칙. 가드 자세는 유지된다.)
    /// </summary>
    public class CharacterGuardState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Guard;

        // 가드 성공 밀림(이동잠금)을 이 상태가 적용 중인가. 창 종료 시 넉백 채널 해제 + 가드 이동 복원 판단에 사용.
        private bool _knockbackLock;

        public override void Enter()
        {
            Machine.Defense?.GuardStart();
            _knockbackLock = false;
            ApplyGuardMovement();
        }

        public override void Tick(float deltaTime)
        {
            var defense = Machine.Defense;

            // 가드 성공 밀림 또는 반응 모션(GuardDamaged/Parrying) 재생 중: 가드 버튼을 떼도 이동잠금이 끝날 때까지 Guard 상태를 유지한다.
            // 잠금 길이 = max(밀림 Duration, 반응 모션 길이) — 모션 길이는 Defense가 Animator 태그로 판정.
            // (버튼 해제 즉시 Exit → EndKnockback으로 밀림/잠금이 취소되던 문제 수정. 낙하만 예외로 허용.)
            if (defense != null && defense.IsReactionLockActive)
            {
                // 카운터 버프(퍼펙트 패링 보상) 보유 중: 패링/가드피격 반응 모션(후딜)을 기다리지 않고 공격 입력으로 즉시 캔슬한다 (2026-09-15).
                // Exit가 넉백 채널/반응 잠금을 해제하고, Combat.ExecuteAttack이 버프를 소모 + 잔류 Parrying 트리거를 제거한다.
                if (defense.HasCounterBuff && CheckAttack()) return;
                if (!_knockbackLock)
                {
                    _knockbackLock = true;
                    Movement.CanMove = false;
                    Movement.SetMoveSpeedMultiplier(0f);
                }
                CheckFall();
                return;
            }

            // 밀림 창 종료 → 넉백 채널 해제 + 가드 이동 복원. 이후 아래 일반 전이 판정을 이어서 진행.
            if (_knockbackLock)
            {
                _knockbackLock = false;
                Movement.EndKnockback();
                ApplyGuardMovement();
            }

            // 낙하(발판 이탈) 우선 처리
            if (CheckFall()) return;

            // 가드 홀드 해제 → 지상 상태 복귀
            if (!Machine.GuardHeld)
            {
                Machine.ChangeState(Machine.ResolveGroundedStateByInput());
                return;
            }

            // 가드 중 공격 입력 → 가드 풀고 공격
            if (CheckAttack()) return;
            // 가드 중 대시 입력 → 회피
            if (CheckDash()) return;
        }

        public override void Exit()
        {
            // 슬라이드 도중 가드가 풀리거나 다른 상태로 전이돼도 넉백 채널을 반드시 해제(잔류 이동잠금 방지).
            var defense = Machine.Defense;
            if (_knockbackLock || (defense != null && defense.IsReactionLockActive))
            {
                Movement.EndKnockback();
                defense?.ClearGuardKnockback();
            }
            _knockbackLock = false;

            defense?.GuardStop();
            Movement.CanMove = true;
            Movement.CanJump = true;
            Movement.FacingLocked = false;
        }

        /// <summary>가드 자세 기본 이동 설정 — allowMovementWhileGuarding에 따라 감속 이동/제자리 고정. (Enter + 밀림 종료 복원 공용)</summary>
        private void ApplyGuardMovement()
        {
            var defense = Machine.Defense;
            bool allowMove = defense != null && defense.AllowMovementWhileGuarding;
            Movement.CanMove = allowMove;
            Movement.CanJump = false;
            if (allowMove)
            {
                // 가드 이동: 감속 배율 + Facing 고정(후진 시 뒷걸음 모션 유지 — RelForward가 음수가 되도록)
                Movement.SetMoveSpeedMultiplier(defense.GuardMoveSpeedMultiplier);
                Movement.FacingLocked = true;
            }
            else Movement.SetMoveSpeedMultiplier(0f);
        }
    }
}
