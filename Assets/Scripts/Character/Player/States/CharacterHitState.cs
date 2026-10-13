using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 피격 반응 상태. CharacterCombat.ReceiveDamage → (HP 감소, 생존 시) StateManager.ChangeState(Hit) 로 진입한다.
    /// 책임: Hit 애니메이션 요청 · 이동/점프/대시/공격 잠금 · 경직(HitDuration) 유지 · 수평 밀림(Push) · 종료 후 다음 상태 결정.
    ///
    /// Push(HorizontalForce&gt;0) 반응은 구 CharacterKnockbackState를 통합한 것 — 서 있는 채로 밀리며(DamagedBackward),
    /// 밀림이 잦아들면 슬라이드만 멈추고 경직은 HitDuration까지 유지한다. 몸통 충돌(FlyingBodyImpact)도 여기서 Sweep한다.
    /// 이번 범위: SuperArmor/무적/다운/사망 없음. 공격 중 피격 시 진행 중 공격은 취소된다.
    /// </summary>
    public class CharacterHitState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Hit;

        private float _timer;
        private bool _push;
        private bool _pushSettled;
        // 몸통 충돌(볼링핀 연쇄) — AttackAction.bodyCollision일 때만 생성 (null = 미사용).
        private FlyingBodyImpact _bodyImpact;

        public override void Enter()
        {
            _timer = 0f;
            var r = Machine.PendingReaction;
            HitReactionSpec spec = r.Spec;

            // 이동/점프 잠금 (상태가 곧 판단 기준 — 별도 플래그를 두지 않는다).
            Movement.CanMove = false;
            Movement.CanJump = false;
            Movement.SetMoveSpeedMultiplier(0f);

            // 공격 중 피격이면 진행 중 공격을 안전 종료(Hitbox OFF · Effect 정리).
            if (Combat != null)
            {
                Combat.CancelCurrentAttack();
                // 방향성 지상 피격: 피격 방향과 스플라인 접선/피격자 facing으로 앞/뒤를 판정해
                // 앞에서 맞으면 DamagedForward, 뒤에서 맞으면 DamagedBackward를 재생한다.
                // 방향 정보가 없으면(0) 벨트스크롤 기본값 Backward를 유지한다.
                Vector3 dir = r.Direction;
                bool forward = dir.sqrMagnitude > 0.0001f && ResolveForward(dir);
                Combat.PlayHitReaction(forward);
            }

            // 수평 밀림(Push) — 구 Knockback 통합. 서 있는 채로 밀리고, 몸통 충돌 판정을 생성한다.
            // 실제 밀림 이동은 CharacterMovement.BeginKnockback이 수행(상태=타이밍, 이동=Movement).
            _push = spec.Kind == HitReactionKind.Push && spec.HorizontalForce > 0f;
            _pushSettled = false;
            if (_push)
            {
                Movement.BeginKnockback(r.Direction, spec.HorizontalForce);
                _bodyImpact = FlyingBodyImpact.TryCreate(Machine, r);
                // 밀림 이펙트(자식 오브젝트) 재생 — 미할당이면 무시. 넉다운 슬라이드는 별도 상태라 제외된다.
                Machine.PushEffect?.Play();
            }
            else
            {
                _bodyImpact = null;
            }
        }

        public override void Tick(float deltaTime)
        {
            _timer += deltaTime;

            // 밀림 중이면 몸통 충돌 판정 + 밀림이 잦아들면 슬라이드만 정지(경직은 HitDuration까지 유지).
            if (_push && !_pushSettled)
            {
                _bodyImpact?.Sweep();
                if (_timer >= 0.08f && Movement.HorizontalSpeed <= 0.6f)
                {
                    Movement.EndKnockback();
                    _pushSettled = true;
                }
            }

            // 경직 시간 동안은 어떤 명령(Jump/Dash/Attack)도 소비하지 않는다.
            if (_timer < Machine.HitDuration)
                return;

            // 종료: 지상이면 입력 기준 복귀(Idle/Move/Run), 공중이면 Fall.
            if (Movement.IsGrounded)
                Machine.ChangeState(Machine.ResolveGroundedStateByInput());
            else
                Machine.ChangeState(CharacterStateType.Fall);
        }

        public override void Exit()
        {
            if (_push)
            {
                Movement.EndKnockback();
                _bodyImpact = null;
                _push = false;
            }
            Movement.CanMove = true;
            Movement.CanJump = true;
        }

        /// <summary>
        /// 구프로젝트 CharacterDirectionalDamage.DetermineIsForward 이관 — 피격 방향과 스플라인 접선의
        /// 내적으로 공격이 온 쪽을 판정하고, 피격자 facing과 비교해 Forward/Backward를 고른다.
        /// (CharacterAirborneState/CharacterKnockdownState의 동명 메서드와 동일 규칙.)
        /// </summary>
        private bool ResolveForward(Vector3 hitDirection)
        {
            if (Movement == null) return true;
            Vector3 forward = Movement.SplineForward;
            if (hitDirection.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f)
                return true;

            float dot = Vector3.Dot(hitDirection.normalized, forward.normalized);
            bool attackFromRight = dot > 0.01f;
            return Movement.FacingRight != attackFromRight;
        }
    }
}
