using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 넉다운(공중 띄우기 → 넘어짐 → 기상) 강제 반응 상태 — 구프로젝트 CharacterAirborne 핵심 시퀀스 이관.
    /// "넉백 애니"로 보이는 큰 반응이 이것. 일반 경직(Hit)/수평 밀림(Knockback)과 분리된다.
    ///
    /// 진입: CharacterCombat.ReceiveDamage → EnterHitReactionState가 DamageInfo.Reaction=Airborne일 때
    /// StateManager.PendingReaction을 채운 뒤 ChangeState(Airborne)로 진입시킨다.
    ///
    /// 3단 시퀀스(상태=타이밍, 이동=Movement, 애니=Combat):
    ///   1) Rising  : Movement.BeginAirborne로 팝업(위+수평). 착지까지 대기.
    ///   2) Collapse: 넘어져 있는 구간(고정 시간). Stun bool 유지로 넉다운 포즈.
    ///   3) Getup   : 기상 애니(Getup 트리거) 재생 후 정상 복귀.
    ///
    /// 방향(Forward/Backward)은 구프로젝트 DetermineIsForward와 동일 — 피격자 facing vs 스플라인 접선 기준.
    /// 애니 상태가 컨트롤러에 없어도 각 단계 타임아웃으로 멈추지 않는다(그레이스풀 디그레이드).
    /// AI 이동/공격 명령은 이 상태 내내 무시된다(EnemyCharacter.IsInForcedReactionState + Movement 채널 선점).
    /// </summary>
    public class CharacterAirborneState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Airborne;

        private enum Phase { Rising, Collapse, Getup }

        // 넘어져 있는(Collapse) 고정 시간(초) — 구프로젝트 CharacterAirborne.CollapseDuration=1f 이관.
        private const float CollapseDuration = 1f;
        // 기상 애니 완료를 못 잡을 때(상태/파라미터 미존재) 강제 탈출 안전 시간(초).
        private const float GetupTimeout = 2f;
        // 팝업이 실제로 떠오르지 못한 약한 힘일 때 곧장 Collapse로 넘기는 유예(초).
        private const float LiftoffGrace = 0.12f;
        // 착지 피드백 발동 최소 체공 시간(초) — 0.2초 이상 떴을 때만 피드백 플레이.
        private const float MinAirtimeForLandingFeedback = 0.2f;

        // 바운스 후 착지 재판정 최소 체공(초) — 낮은 Power의 짧은 바운스도 놓치지 않는 값.
        private const float BounceMinAirTime = 0.03f;
        // Decay가 0 이하로 들어왔을 때의 안전 최소 감소량(무한 바운스 방지).
        private const float MinBounceDecay = 0.5f;
        // 바운스마다 수평 관성 감쇠 배율 — 튈수록 제자리에 가깝게 멈춘다.
        private const float BounceHorizontalDamping = 0.5f;

        private Phase _phase;
        private float _timer;
        private float _minAirTime;
        private bool _leftGround;
        // 그라운드 바운스 — 남은 세기가 곧 다음 바운스의 수직 속도(m/s). 매 바닥 충돌마다 Decay만큼 감소.
        private float _bouncePower;
        private float _bounceDecay;
        private float _bounceHorizontal;
        private UnityEngine.Vector3 _bounceDirection;
        // 이번 에어본 동안 바닥 충돌(바운스) 횟수 — CharacterCombat.bounceLandingBigCount 미만은 큰 랜딩, 이후는 작은 랜딩.
        private int _bounceLandingCount;
        // 몸통 충돌(볼링핀 연쇄) — AttackAction.bodyCollision일 때만 생성. 체공(Rising) 구간에서만 판정.
        private FlyingBodyImpact _bodyImpact;

        /// <summary>
        /// 체공(상승/낙하) 구간인가 — 아직 착지해 넘어지기 전. 이 동안은 재런치(저글)가 허용된다.
        /// CharacterStateManager.IsAirborneRising 경유로 CharacterCombat.ReceiveDamage가 읽는다.
        /// </summary>
        public bool IsRising => _phase == Phase.Rising;

        public override void Enter()
        {
            _phase = Phase.Rising;
            _timer = 0f;
            _leftGround = false;

            var r = Machine.PendingReaction;
            var spec = r.Spec;
            _minAirTime = Mathf.Max(0.1f, spec.AirTime);

            // 그라운드 바운스 데이터 인수 — 재피격(allowReenter 재진입) 시 새 공격의 값으로 항상 대체된다(덮어쓰기 규칙).
            _bouncePower = spec.WantsBounce ? spec.BouncePower : 0f;
            _bounceDecay = spec.BounceDecay > 0f ? spec.BounceDecay : MinBounceDecay;
            _bounceHorizontal = spec.HorizontalForce;
            _bounceDirection = r.Direction;
            _bounceLandingCount = 0;

            Movement.CanMove = false;
            Movement.CanJump = false;
            Movement.SetMoveSpeedMultiplier(0f);
            Movement.RotationLocked = true; // 체공/낙하 클립 동안 스플라인 추종 yaw 정지(구프로젝트 방식)

            if (Combat != null)
            {
                Combat.CancelCurrentAttack();
                // 저글 유지 히트(체공 중 추가타)면 에어본 아크를 처음부터 다시 트는 대신
                // 공중 피격 움찔(AirDamaged) 애니를 재생한다. 시동기/스파이크는 기존 체인.
                if (r.IsJuggleHit)
                    Combat.PlayAirDamaged(ResolveForward(r.Direction));
                else
                    Combat.PlayAirborneChain(ResolveForward(r.Direction));
            }

            // Kind별 진입 이동:
            //   Slam           : 상승 없이 급강하 — 착지 판정으로 기존 Collapse→Getup(또는 바운스)에 합류.
            //   Push/Knockdown : 바운스 조합으로만 이 상태에 들어온다 — 적중 시 "런치"(살짝 띄워 날림)만 하고
            //                    랜딩 피드백/Decay 소모 없음. 첫 실제 바닥 충돌부터 바운스(Landing) 카운트 시작.
            //                    띄움 높이 = VerticalForce(0이면 BouncePower). 수평 관성 유지(돌수제비).
            //   Launch/저글    : 위로 팝업. AirTime > 0 이면 체공 전용 중력 역산.
            if (spec.Kind == HitReactionKind.Slam && spec.VerticalForce > 0f)
            {
                Movement.BeginSpikeFall(r.Direction, spec.VerticalForce, spec.HorizontalForce);
            }
            else if (spec.Kind == HitReactionKind.Push || spec.Kind == HitReactionKind.Knockdown)
            {
                _minAirTime = BounceMinAirTime;
                float launchUp = spec.VerticalForce > 0f ? spec.VerticalForce : _bouncePower;
                if (launchUp <= 0f) launchUp = 0.1f; // 안전망: 바운스 값 없이 진입한 경우
                Movement.BeginAirborne(r.Direction, launchUp, spec.HorizontalForce, 0f);
            }
            else
            {
                Movement.BeginAirborne(r.Direction, spec.VerticalForce, spec.HorizontalForce, spec.AirTime);
            }

            _bodyImpact = FlyingBodyImpact.TryCreate(Machine, r);
        }

        public override void Tick(float deltaTime)
        {
            _timer += deltaTime;

            switch (_phase)
            {
                case Phase.Rising:
                    _bodyImpact?.Sweep();
                    if (!Movement.IsGrounded)
                        _leftGround = true;

                    // 착지: 떠올랐다가 다시 접지 + 최소 체공 경과 + 하강 중.
                    // 그라운드 바운스 세기가 남아 있으면 일반 착지(Collapse) 대신 다시 튀어오른다.
                    // 착지 판정 조건(_leftGround 리셋)이 한 번의 접촉당 1회만 통과하므로 바운스 중복 발동은 없다.
                    if (_leftGround && Movement.IsGrounded && _timer >= _minAirTime && Movement.VerticalVelocity <= 0f)
                    {
                        if (Machine != null)
                            Machine.SetHadSufficientAirtime(_timer >= MinAirtimeForLandingFeedback);
                        if (!TryGroundBounce())
                            EnterCollapse();
                    }
                    // 약한 힘으로 애초에 뜨지 못한 경우 — 유예 후 곧장 넘어짐 처리(넉다운 보장).
                    else if (!_leftGround && Movement.IsGrounded && _timer >= LiftoffGrace)
                    {
                        if (Machine != null)
                            Machine.SetHadSufficientAirtime(_timer >= MinAirtimeForLandingFeedback);
                        if (!TryGroundBounce())
                            EnterCollapse();
                    }
                    break;

                case Phase.Collapse:
                    if (_timer >= CollapseDuration)
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
        /// 다운 중 OTG 피격(AttackAction.hitDownedTargets) 통지 — 착지 후 누워있는(Collapse) 구간이면
        /// 누움 타이머를 리셋해 true를 반환. 상승(Rising)/기상(Getup) 구간은 false(반응 없음).
        /// </summary>
        public bool OnDownedHit()
        {
            if (_phase != Phase.Collapse) return false;
            _timer = 0f;
            return true;
        }

        public override void Exit()
        {
            _bodyImpact = null;
            Movement.EndAirborne();
            Combat?.ClearKnockdownAnim();
            Movement.RotationLocked = false;
            Movement.CanMove = true;
            Movement.CanJump = true;
        }

        /// <summary>
        /// 바닥 충돌 시 그라운드 바운스 시도 — 남은 Power가 0 이하이면 false(기존 Collapse→Getup 흐름으로 복귀).
        /// 남은 Power가 곧 이번 바운스의 수직 속도(m/s)라 클수록 높게, 감소할수록 낮게 튄다(횟수 저장 방식 아님).
        /// 바운스 1회당 Power를 Decay만큼 감소시키고 수평 관성도 감쇠시킨다.
        /// 착지 플래그(_leftGround)와 타이머를 리셋하므로 한 번의 접촉에서 중복 발동하지 않는다.
        /// </summary>
        private bool TryGroundBounce()
        {
            if (_bouncePower <= 0f) return false;

            // 기존 Airborne 이동 채널 재사용 — airTime 0 = 기본 중력 낙하(바운스 높이 = Power^2 / 2g).
            Movement.BeginAirborne(_bounceDirection, _bouncePower, _bounceHorizontal, 0f);

            // 바닥 충돌 순간 랜딩 이펙트 — 충돌 순번에 따라 Landing/Landing_small 분기(횟수·키·오프셋은 CharacterCombat 인스펙터).
            Combat?.PlayBounceLandingFeedback(Movement, _bounceLandingCount);
            _bounceLandingCount++;

            _bouncePower -= Mathf.Max(MinBounceDecay, _bounceDecay);
            _bounceHorizontal *= BounceHorizontalDamping;

            _leftGround = false;
            _timer = 0f;
            _minAirTime = BounceMinAirTime;
            return true;
        }

        private void EnterCollapse()
        {
            Movement.EndAirborne();
            _phase = Phase.Collapse;
            _timer = 0f;
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
