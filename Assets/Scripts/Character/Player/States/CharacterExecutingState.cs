using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 처형(절명기) 시전 상태 — 그로기 적을 확정 처형한다(공격자).
    ///
    /// 진입: 지상 중립 상태(Idle/Move/Run)에서 CheckExecution()이 사거리 내 그로기 적을 찾으면
    /// Combat.ExecutionTarget에 지정하고 Executing으로 전이한다.
    ///
    /// 흐름(구버전 CharacterExecution 이관):
    ///   Enter        : 무적 On, 진행 공격 취소, 이동/점프 잠금, 접근 목표 = 피격자 ExecutePoint(CharacterActionPoints),
    ///                  접근 대시 애니(Dash 트리거), 대상을 Executed로 잠금.
    ///   Tick(접근)   : ExecutionApproachDuration 동안 목표 지점으로 SmoothStep 이동(목표 매 프레임 재샘플).
    ///   접근 완료    : 시전자/피격자 Facing 정렬(ExecutorFacesSameDirection) + Execute 애니(ExecuteType 분기) 재생.
    ///   Tick(실행)   : ExecuteFinish 애니 이벤트(Combat.ExecutionFinishRequested) 또는 ExecutionDuration 경과 시
    ///                  Combat.ConfirmExecution(대상) — 확정 처형 + 시전자 회복.
    ///   Exit         : 무적 Off, 잠금 해제, 잔여 이동 애니 복귀(ReturnMovement).
    ///
    /// 히트박스/데미지 롤 없음(연출 후 확정 처형). 실제 사망은 대상 Combat.KillByExecution가 수행한다.
    /// 카메라/이펙트는 CharacterExecutionPresentation(StateChanged 구독)이 담당한다.
    /// </summary>
    public class CharacterExecutingState : CharacterState
    {
        public override CharacterStateType Type => CharacterStateType.Executing;

        private const float ApproachDashFade = 0.05f;

        private Character _target;
        private CharacterActionPoints _targetPoints;
        private float _timer;
        private float _approachDur;
        private float _maxDur;
        private Vector3 _startPos;
        private bool _faceRight;
        private bool _executed;
        private bool _executeAnimStarted;

        public override void Enter()
        {
            _timer = 0f;
            _executed = false;
            _executeAnimStarted = false;
            _target = Combat != null ? Combat.ExecutionTarget : null;
            _targetPoints = _target != null ? _target.GetComponent<CharacterActionPoints>() : null;
            _approachDur = Combat != null ? Combat.ExecutionApproachDuration : 0.7f;
            _maxDur = _approachDur + (Combat != null ? Combat.ExecutionDuration : 1f);

            if (Combat != null)
            {
                Combat.CancelCurrentAttack();
                Combat.SetInvincible(true);
                Combat.ConsumeExecutionFinishRequest();
            }

            Transform root = Machine.Root;
            _startPos = root != null ? root.position : Vector3.zero;
            _faceRight = Movement != null && Movement.FacingRight;

            if (_target != null && root != null)
            {
                // 대상을 향해 바라본다(잠그기 전에 설정).
                if (Movement != null)
                {
                    Vector3 to = _target.transform.position - _startPos;
                    float along = Vector3.Dot(to, Movement.SplineForward);
                    if (Mathf.Abs(along) > 0.01f)
                    {
                        _faceRight = along > 0f;
                        Movement.SetFacingRight(_faceRight);
                    }
                }

                // 접근 대시 연출 (구버전 DashForward CrossFade + 잔상 대응 — 잔상은 Feedback 후속).
                Machine.FireTrigger(AnimParams.Dash);

                // 대상 처형 잠금(Executed) — 피처형 애니는 CharacterExecutedState.Enter가 재생.
                if (_target.Combat != null) _target.Combat.EnterExecuted();
            }

            if (Movement != null)
            {
                Movement.CanMove = false;
                Movement.CanJump = false;
                Movement.SetMoveSpeedMultiplier(0f);
                Movement.FacingLocked = true;
            }
        }

        /// <summary>접근 목표 — 피격자 ExecutePoint(매 프레임 재샘플, 구버전 동일). 앵커 없으면 대상 앞 1m.</summary>
        private Vector3 ResolveApproachTarget()
        {
            if (_target == null) return _startPos;
            Vector3 pos;
            if (_targetPoints != null && _targetPoints.ExecutePoint != _target.transform)
            {
                pos = _targetPoints.ExecutePoint.position;
            }
            else
            {
                Vector3 tp = _target.transform.position;
                Vector3 toSelf = _startPos - tp;
                toSelf.y = 0f;
                Vector3 dir = toSelf.sqrMagnitude > 0.0001f ? toSelf.normalized : Vector3.right;
                pos = tp + dir * 1.0f;
            }
            pos.y = _startPos.y;
            return pos;
        }

        public override void Tick(float deltaTime)
        {
            _timer += deltaTime;

            // 대상 소실/이미 사망 → 연출 중단하고 복귀(무적은 Exit에서 해제).
            if (_target == null || _target.IsDead || !_target.gameObject.activeInHierarchy)
            {
                if (!_executed) Finish();
                return;
            }

            if (_timer <= _approachDur)
            {
                if (Movement != null)
                {
                    float t = _approachDur > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_timer / _approachDur)) : 1f;
                    Vector3 pos = Vector3.Lerp(_startPos, ResolveApproachTarget(), t);
                    Movement.Teleport(pos, _faceRight);
                }
                return;
            }

            // 접근 완료 — Facing 정렬 + 처형 애니 시작 (1회).
            if (!_executeAnimStarted)
            {
                _executeAnimStarted = true;
                BeginExecuteAnimation();
            }

            // 종료: ExecuteFinish 애니 이벤트(조기) 또는 시간 만료.
            bool finishRequested = Combat != null && Combat.ExecutionFinishRequested;
            if (!_executed && (finishRequested || _timer >= _maxDur))
            {
                _executed = true;
                if (Combat != null)
                {
                    Combat.ConsumeExecutionFinishRequest();
                    Combat.ConfirmExecution(_target);
                }
                Finish();
            }
        }

        /// <summary>접근 종료 시점 — 최종 정렬 + Facing 규칙 적용 + Execute/ExecuteType 애니 재생 (구버전 BeginExecutionSequence).</summary>
        private void BeginExecuteAnimation()
        {
            // Facing 규칙 (구버전 CharacterExecution.AlignDirection 복원):
            //   피격자(적)의 방향은 그대로 두고(재플립 없음), 시전자를 피격자 방향 기준으로 정렬한다.
            //   ExecutorFacesSameDirection = 피격자와 같은 방향(구 ExecutionFacing.Same) / false = 마주보기(구 Opposite).
            //   구버전 기본값은 Same 이며, 적별 방향값은 CharacterActionPoints 데이터로 존재한다.
            if (_target != null && _target.Movement != null && Movement != null)
            {
                bool sameDir = _targetPoints == null || _targetPoints.ExecutorFacesSameDirection;
                bool victimFaceRight = _target.Movement.FacingRight;
                _faceRight = sameDir ? victimFaceRight : !victimFaceRight;

                Movement.FacingLocked = false;
                Movement.SetFacingRight(_faceRight);
                Movement.FacingLocked = true;
            }

            // 최종 위치 정렬(시전자) — 정렬된 _faceRight로 ExecutePoint에 스냅.
            if (Movement != null)
                Movement.Teleport(ResolveApproachTarget(), _faceRight);
            Physics.SyncTransforms();

            int executeType = _targetPoints != null ? _targetPoints.ExecuteType : 0;
            Machine.PlayExecuteAnimation(executeType);

            // 피격자 피처형 애니 — 시전자 처형 애니와 같은 프레임에 시작(동기화).
            // 전용 클립이 없는 적(녹귀 등)은 파라미터 가드로 무시되고 KillByExecution이 사망을 보장한다.
            if (_target != null && _target.StateManager != null)
                _target.StateManager.PlayExecutedAnimation();
        }

        private void Finish()
        {
            if (Combat != null) Combat.SetInvincible(false);
            Machine.FireTrigger(AnimParams.ReturnMovement);
            Machine.ChangeState(Machine.ResolveGroundedStateByInput());
        }

        public override void Exit()
        {
            if (Movement != null)
            {
                Movement.CanMove = true;
                Movement.CanJump = true;
                Movement.FacingLocked = false;
            }
            if (Combat != null)
            {
                Combat.SetInvincible(false);
                Combat.ConsumeExecutionFinishRequest();
            }
            _target = null;
            _targetPoints = null;
        }
    }
}
