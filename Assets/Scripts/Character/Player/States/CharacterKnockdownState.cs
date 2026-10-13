using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 지상 넉다운(밀림 → 넘어짐 → 다운 → 기상) 강제 반응 상태.
    /// 구프로젝트 CharacterAirborne의 착지 후 Collapse→Getup '역할'을 지상 버전으로 이관한 것.
    /// (구프로젝트는 넉다운을 항상 공중 팝업의 착지 꼬리로 처리했지만, Ver2는 요구에 따라
    ///  Airborne(띄우기/저글)과 분리된 '지상 넉다운'을 별도 상태로 둔다 — 수직 팝업 없이 밀리며 쓰러짐.)
    ///
    /// Push/Light Knockback(밀리지만 서 있음)과 구분되는 '쓰러지는' 반응.
    ///   진입: CharacterCombat.EnterHitReactionState가 DamageInfo.Reaction=Knockdown일 때
    ///   PendingReaction을 채우고 ChangeState(Knockdown).
    ///
    /// 3단(상태=타이밍, 이동=Movement, 애니=Combat):
    ///   1) Slide : Movement.BeginKnockback으로 수평 밀림(팝업 없음, 지상 유지). knockbackDuration 동안.
    ///   2) Down  : 넘어져 있는 구간(고정 DownDuration). Stun/Falldowning 유지로 다운 포즈.
    ///   3) Getup : 기상 애니(Getup) 재생 후 정상 복귀.
    ///
    /// 방향은 구프로젝트 DetermineIsForward(facing vs 스플라인 접선) 이관.
    /// 애니 상태가 컨트롤러에 없어도 각 단계 타임아웃으로 멈추지 않는다(그레이스풀 디그레이드).
    /// AI 이동/공격은 이 상태 내내 무시된다(EnemyCharacter.IsInForcedReactionState + Movement 넉백 채널 선점).
    /// </summary>
    public class CharacterKnockdownState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Knockdown;

        private enum Phase { Slide, Down, Getup }

        // 넘어져 있는(Down) 고정 시간(초) — 구프로젝트 CharacterAirborne.CollapseDuration=1f 이관.
        private const float DownDuration = 1f;
        // 기상 애니 완료를 못 잡을 때(상태/파라미터 미존재) 강제 탈출 안전 시간(초).
        private const float GetupTimeout = 2f;
        // 최소 밀림(Slide) 시간(초) — knockbackDuration이 더 짧아도 최소한 이만큼은 민다.
        private const float MinSlideTime = 0.12f;

        private Phase _phase;
        private float _timer;
        private float _slideTime;
        // 몸통 충돌(볼링핀 연쇄) — AttackAction.bodyCollision일 때만 생성. Slide(밀림) 구간에서만 판정.
        private FlyingBodyImpact _bodyImpact;

        public override void Enter()
        {
            _phase = Phase.Slide;
            _timer = 0f;

            var r = Machine.PendingReaction;
            _slideTime = Mathf.Max(MinSlideTime, r.Spec.PushDuration);

            Movement.CanMove = false;
            Movement.CanJump = false;
            Movement.SetMoveSpeedMultiplier(0f);
            Movement.RotationLocked = true; // 낙하 클립 동안 스플라인 추종 yaw 정지(구프로젝트 방식)

            // 이미 누운 채 진입(잡기 슬램 해제, 2026-09-09): 밀림·낙하 클립 없이 곧바로 다운 포즈부터 — 공중에 떴다 떨어지는 연출 방지.
            if (r.StartDowned)
            {
                if (Combat != null)
                {
                    Combat.CancelCurrentAttack();
                    Combat.PlayDownedPose(ResolveForward(r.Direction), 0f); // 즉시 전환 — 잡힘 포즈에서 블렌드되면 본이 루트로 미끄러짐 (2026-09-09)
                }
                Movement.EndKnockback();
                Movement.FacingLocked = true;
                _phase = Phase.Down;
                return;
            }

            if (Combat != null)
            {
                Combat.CancelCurrentAttack();
                Combat.PlayAirborneChain(ResolveForward(r.Direction)); // 넉다운 = 에어본 Start→Loop→End 클립 재생
            }

            // 수평 밀림만(팝업 없음) — 실제 이동은 Movement의 넉백 채널이 담당(감쇠).
            Movement.BeginKnockback(r.Direction, r.Spec.HorizontalForce);
            _bodyImpact = FlyingBodyImpact.TryCreate(Machine, r);

            // 쓰러짐~기상 동안 좌우 플립 금지. CancelCurrentAttack이 FacingLocked를 풀기 때문에 그 뒤에 건다.
            Movement.FacingLocked = true;
        }

        public override void Tick(float deltaTime)
        {
            _timer += deltaTime;

            switch (_phase)
            {
                case Phase.Slide:
                    _bodyImpact?.Sweep();
                    if (_timer >= _slideTime)
                    {
                        Movement.EndKnockback();   // 밀림 정지 → 그 자리에서 다운 유지
                        _phase = Phase.Down;
                        _timer = 0f;
                    }
                    break;

                case Phase.Down:
                    if (_timer >= DownDuration)
                    {
                        Combat?.PlayGetup();
                        _phase = Phase.Getup;
                        _timer = 0f;
                    }
                    break;

                case Phase.Getup:
                    bool getupDone = Combat == null || Combat.IsGetupAnimationFinished();
                    if (getupDone || _timer >= GetupTimeout)
                        Machine.ChangeState(Machine.ResolveGroundedStateByInput());
                    break;
            }
        }

        /// <summary>
        /// 다운 중 OTG 피격(AttackAction.hitDownedTargets) 통지 — 누워있는(Down) 구간이면
        /// 누움 타이머를 리셋해(연타로 계속 눕혀둘 수 있음) true를 반환. 그 외 구간은 false.
        /// </summary>
        public bool OnDownedHit()
        {
            if (_phase != Phase.Down) return false;
            _timer = 0f;
            return true;
        }

        public override void Exit()
        {
            _bodyImpact = null;
            Movement.EndKnockback();
            Combat?.ClearKnockdownAnim();
            Movement.FacingLocked = false;
            Movement.RotationLocked = false;
            Movement.CanMove = true;
            Movement.CanJump = true;
        }

        /// <summary>
        /// 구프로젝트 CharacterAirborne.DetermineIsForward 이관 — 피격 방향과 스플라인 접선의 내적으로
        /// 공격이 온 쪽을 판정하고, 피격자 facing과 비교해 Forward/Backward를 고른다.
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
