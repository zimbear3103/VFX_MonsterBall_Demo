using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 절명기(처형) 연출 계층 — 카메라 시퀀스 + 애니 이벤트 이펙트 + 조기 종료 이벤트.
    ///
    /// 처형의 메커니즘(무적/접근/정렬/확정 처형/회복)은 CharacterExecutingState가 소유하고,
    /// 이 컴포넌트는 상태 전이 이벤트(StateChanged)에 반응해 "보이는 것"만 담당한다:
    ///   - Executing 진입: 피격자 CharacterCinematics의 처형 카메라(좌/우, 시전자 Facing 선택) 재생.
    ///   - 시전자 클립 이벤트(ExecuteA_Hit1 등): 피격자 Cinematics의 이벤트→Feedback 매핑 재생.
    ///   - ExecuteFinish 이벤트: Combat.RequestExecutionFinish() — 상태가 조기 종료를 소비.
    ///   - Executing 이탈(정상/강제 무관): 카메라 복귀 — 어떤 경로로 깨져도 카메라가 고정되지 않는다.
    /// </summary>
    [RequireComponent(typeof(Character))]
    [AddComponentMenu("Yeolha/Combat/Character Execution Presentation")]
    public class CharacterExecutionPresentation : MonoBehaviour
    {
        private Character _character;
        private AnimationEventHandler _events;
        private CameraSequence _camera;
        private CharacterCinematics _victimCinematics;
        private Character _victim;
        private CharacterActionPoints _victimPoints;

        private void Awake()
        {
            _character = GetComponent<Character>();
        }

        private void OnEnable()
        {
            if (_character.StateManager != null)
                _character.StateManager.StateChanged += OnStateChanged;

            var animator = GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                _events = animator.GetComponent<AnimationEventHandler>();
                if (_events == null)
                    _events = animator.gameObject.AddComponent<AnimationEventHandler>();
                _events.OnAnimationEvent += OnAnimEvent;
            }
        }

        private void OnDisable()
        {
            if (_character.StateManager != null)
                _character.StateManager.StateChanged -= OnStateChanged;
            if (_events != null) _events.OnAnimationEvent -= OnAnimEvent;
            StopCamera();
        }

        private void OnStateChanged(CharacterStateType previous, CharacterStateType next)
        {
            if (next == CharacterStateType.Executing)
                BeginPresentation();
            else if (previous == CharacterStateType.Executing)
                StopCamera();
        }

        private void BeginPresentation()
        {
            _victim = _character.Combat != null ? _character.Combat.ExecutionTarget : null;
            // 카메라/이펙트 데이터: 시전자(구버전 CharacterJangHyu/CameraSequence 배선) 우선, 없으면 피격자.
            _victimCinematics = GetComponent<CharacterCinematics>();
            if (_victimCinematics == null && _victim != null)
                _victimCinematics = _victim.GetComponent<CharacterCinematics>();

            // 처형 카메라 좌/우 선택 Facing = 처형 정렬(CharacterExecutingState.BeginExecuteAnimation)과 동일 기준.
            //   피격자 방향 유지 + 시전자를 ExecutorFacesSameDirection에 따라 정렬 → 그 최종 시전자 Facing으로 선택.
            //   (구버전 BeginExecutionSequence: AlignDirection 후 casterFacingRight로 시퀀스 선택과 동일 — 접근 방향이 아님.)
            _victimPoints = _victim != null ? _victim.GetComponent<CharacterActionPoints>() : null;

            bool facingRight = _character.Movement == null || _character.Movement.FacingRight;
            if (_victim != null && _victim.Movement != null)
            {
                bool sameDir = _victimPoints == null || _victimPoints.ExecutorFacesSameDirection;
                bool victimFaceRight = _victim.Movement.FacingRight;
                facingRight = sameDir ? victimFaceRight : !victimFaceRight;
            }

            // 처형 카메라도 잡기(GrabType)처럼 ExecuteType별로 분기 — 피격자 CharacterActionPoints.ExecuteType 사용.
            int executeType = _victimPoints != null ? _victimPoints.ExecuteType : 0;
            _camera = _victimCinematics != null ? _victimCinematics.ResolveExecutionCamera(executeType, facingRight) : null;
            _camera?.Play();

            // 접근 대시 잔상 (구버전 CharacterExecution.PlayApproachEffects 이관 — 대시 캔슬과 동일한 잔상).
            // 접근(Approaching) 구간(ExecutionApproachDuration) 내내 잔상을 남겨 적에게 러프하게 파고드는 느낌을 준다.
            var afterimage = _character.GetComponentInChildren<CharacterAfterimage>(true);
            if (afterimage != null && _character.Combat != null)
                afterimage.Spawn(_character.Combat.ExecutionApproachDuration);
        }

        private void StopCamera()
        {
            _camera?.Stop();
            _camera = null;
            _victim = null;
            _victimCinematics = null;
            _victimPoints = null;
        }

        private void OnAnimEvent(string eventName)
        {
            if (_character.StateManager == null
                || _character.StateManager.CurrentStateType != CharacterStateType.Executing)
                return;

            if (eventName == "ExecuteFinish")
            {
                _character.Combat?.RequestExecutionFinish();
                return;
            }

            if (_victimCinematics != null && _victim != null)
            {
                // 이펙트 기준 위치 = 피격자 CharacterActionPoints.ExecuteEffectPoint(비우면 루트) — 적별 앵커로 조절.
                Vector3 basePos = _victimPoints != null ? _victimPoints.ExecuteEffectPoint.position : _victim.transform.position;
                _victimCinematics.TryPlayEventEffect(eventName, basePos);
            }
        }
    }
}
