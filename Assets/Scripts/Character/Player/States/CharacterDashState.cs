using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 대시 상태.
    ///
    /// 진입 시 대시 방향을 결정해 CharacterMovement.DashAbility.Begin을 1회 호출한다.
    ///   이동 입력 있음 → 스플라인 진행/깊이 축 기준 입력 방향 (대각 정규화)
    ///   이동 입력 없음 → 현재 Facing 방향 (정지 대시 허용)
    /// 방향은 시작 순간 고정되며, 도중 스틱 반전에 영향받지 않는다.
    ///
    /// Facing은 진행축 성분이 있을 때만 갱신한다 (깊이 대시로 90도 회전 방지).
    ///
    /// 전이:
    ///   이동(dashDuration) 종료 → (DashAbility.HoldUntilAnimationEnds면) 대시 애니메이션 완주까지 대기 → Idle / Move / Run (입력 기준)
    ///   대기 구간에서는 공격/점프/가드/재대시 입력으로 즉시 캔슬 가능 (이동 입력만으로는 끊기지 않는다 — 모션 완주 우선).
    ///   2026-09-15: 구버전은 이동 종료 즉시 전이해 대시 모션이 중간에 끊겼다.
    /// </summary>
    public class CharacterDashState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Dash;

        // 애니 대기: 이동 종료 후 대시 애니 상태를 찾아 완주(normalizedTime)까지 유지
        private int _entryAnimHash;     // 진입 시 Animator 상태(대시 트리거 처리 전) — 대시 애니 식별 기준
        private int _dashAnimHash;      // 식별된 대시 애니 상태
        private bool _dashAnimSeen;
        private bool _holding;
        private float _holdTimer;
        private const float AnimDetectTimeout = 0.3f; // 이 시간 안에 대시 애니 상태를 못 찾으면 대기 포기

        public override void Enter()
        {
            _holding = false;
            _holdTimer = 0f;
            _dashAnimSeen = false;
            _dashAnimHash = 0;
            _entryAnimHash = CurrentAnimHash(out _);

            Vector3 direction = ResolveDirection();

            // 진행축 성분이 충분하면 Facing 갱신 (벨트스크롤 규칙 유지)
            float forwardComponent = Vector3.Dot(direction, Movement.SplineForward);
            if (Mathf.Abs(forwardComponent) > 0.2f)
                Movement.SetFacingRight(forwardComponent > 0f);

            Machine.Dash.Begin(direction);

            // 대시 시작 연기 (구버전 DashStart 이벤트)
            if (Machine.Dash.IsDashing)
                Movement.PlayDashStartFeedback();

            // Begin이 거부된 경우(방향 퇴화 등) 즉시 복귀
            if (!Machine.Dash.IsDashing)
                Machine.ChangeState(Machine.ResolveGroundedStateByInput());
        }

        public override void Tick(float deltaTime)
        {
            // DashAbility가 Duration 만료 시 스스로 End() → 이후 애니 완주 대기 또는 복귀 전이
            if (Machine.Dash.IsDashing)
                return;

            if (!Machine.Dash.HoldUntilAnimationEnds)
            {
                Machine.ChangeState(Machine.ResolveGroundedStateByInput());
                return;
            }

            if (!_holding)
            {
                _holding = true;
                _holdTimer = 0f;
                Movement.CanMove = false; // 대기 중 이동 입력으로 미끄러지지 않게 (Exit에서 복구)
            }
            _holdTimer += deltaTime;

            // 캔슬: 공격 / 점프 / 가드 / 재대시 (이동 입력은 모션 완주 우선이라 캔슬하지 않는다)
            if (CheckAttack()) return;
            if (CheckJump()) return;
            if (CheckGuard()) return;
            if (CheckDash()) return;
            if (CheckFall()) return;

            if (IsDashAnimationFinished() || _holdTimer >= Machine.Dash.MaxAnimationHold)
                Machine.ChangeState(Machine.ResolveGroundedStateByInput());
        }

        public override void Exit()
        {
            // 상태가 외부 요인으로 강제 전환될 때도 대시를 정리
            Machine.Dash.End();
            if (_holding) Movement.CanMove = true;
            _holding = false;
            // 대시 종료 연기 (구버전 DashFinish 이벤트, scale 0.5)
            Movement.PlayDashEndFeedback();
        }

        /// <summary>
        /// 대시 애니메이션 완주 여부. 진입 시 상태와 다른 상태에 처음 도달한 것을 대시 애니로 보고,
        /// 그 상태를 벗어났거나 normalizedTime이 AnimationExitNormalizedTime 이상이면 완주. Animator가 없으면 즉시 완주.
        /// </summary>
        private bool IsDashAnimationFinished()
        {
            Animator animator = Machine.Animator;
            if (animator == null || !animator.isActiveAndEnabled) return true;

            bool inTransition;
            int hash = CurrentAnimHash(out inTransition);
            var info = inTransition ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);

            if (!_dashAnimSeen)
            {
                if (hash != _entryAnimHash && hash != 0)
                {
                    _dashAnimSeen = true;
                    _dashAnimHash = hash;
                }
                else
                {
                    return _holdTimer >= AnimDetectTimeout; // 대시 애니 미검출 → 잠깐 기다렸다 포기
                }
            }

            if (hash != _dashAnimHash) return true;                        // 대시 애니를 이미 벗어남(Exit Time 전이 등)
            if (inTransition) return false;                                // 대시 애니로 들어가는 중
            return info.normalizedTime >= Machine.Dash.AnimationExitNormalizedTime;
        }

        private int CurrentAnimHash(out bool inTransition)
        {
            inTransition = false;
            Animator animator = Machine.Animator;
            if (animator == null || !animator.isActiveAndEnabled) return 0;
            inTransition = animator.IsInTransition(0);
            var info = inTransition ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
            return info.fullPathHash;
        }

        /// <summary>대시 방향 계산: 입력 방향(스플라인 축) 우선, 없으면 Facing 방향.</summary>
        private Vector3 ResolveDirection()
        {
            Vector2 input = Machine.MoveInput;

            if (Machine.HasMoveInput)
            {
                Vector3 direction =
                    Movement.SplineForward * input.x +
                    Movement.SplineDepth * input.y;
                if (direction.sqrMagnitude > 0.0001f)
                    return direction.normalized;
            }

            // 입력 없음 → 현재 Facing 방향으로 대시 (정지 대시)
            return Movement.SplineForward * (Movement.FacingRight ? 1f : -1f);
        }
    }
}
