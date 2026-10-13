using UnityEngine;
using UnityEngine.InputSystem;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 자폭 발동 트리거 (플레이어) — 구버전 CharacterSelfDestruct의 "입력 → 자폭" 분기를 이관.
    ///
    /// 설계:
    ///   - 공유 입력(LS+RS 동시 / 키보드 G)을 읽되, 투혼이 "가득 차지 않았을 때만" 자폭을 발동한다.
    ///   - 투혼이 가득 찼을 때는 CharacterLatentAbility(나찰)가 같은 입력을 처리한다.
    ///     두 컴포넌트의 투혼 게이트가 상보적(가득 vs 미가득)이라 동시에 발동하지 않으며,
    ///     CharacterLatentAbility는 전혀 수정하지 않는다(구버전의 "투혼 풀 → 나찰 / 아니면 자폭" 분기 복원).
    ///   - 실제 폭발/데미지/자기 사망은 전부 "SelfDestruct" AttackAction(+ 자식 Explosion + 공용 ReceiveDamage)이
    ///     담당한다. 이 컴포넌트는 트리거만 한다(자폭 전용 데미지 시스템 없음).
    ///
    /// 흐름:
    ///   입력 → CommandManager.RequestAttack("SelfDestruct") → (지상) CheckAttack → Attack 상태
    ///   → CharacterCombat.ExecuteStartAttack(이름) → SelfDestruct AttackAction 실행
    ///   → TimedVisual 타이밍에 Explosion 점화(범위 피해 + 넉백) + 자기 피해 → HP 0이면 공용 사망 흐름.
    /// </summary>
    [DefaultExecutionOrder(-15)]
    [RequireComponent(typeof(Character))]
    public class CharacterSelfDestruct : MonoBehaviour
    {
        [Header("Attack")]
        [Tooltip("실행할 AttackAction 이름. 이 캐릭터의 AttackAction.AttackName과 일치해야 한다(구버전 자폭 애니 액션 대응)")]
        [SerializeField] private string selfDestructAttackName = "SelfDestruct";
        [Tooltip("공격 요청 시 사용할 입력 종류. Skill2는 콤보 시동기와 겹치지 않아 안전하다(이름 지정 실행이라 선택엔 영향 없음)")]
        [SerializeField] private AttackInputType requestInput = AttackInputType.Skill2;

        [Header("Trigger (Shared With Latent)")]
        [Tooltip("패드: 좌/우 스틱 버튼 동시 누름으로 발동 (구버전 SelfDestructButton = LS+RS, 나찰과 동일 입력)")]
        [SerializeField] private bool gamepadDualStick = true;
        [Tooltip("키보드 대체 트리거 키 (나찰과 동일)")]
        [SerializeField] private Key keyboardTrigger = Key.G;

        [Header("Gate")]
        [Tooltip("투혼이 가득 차지 않았을 때만 자폭한다(가득 참 → 나찰이 처리). 구버전 분기 감각 복원. 끄면 항상 자폭")]
        [SerializeField] private bool onlyWhenTouhonNotFull = true;

        [Header("Post-Blast Recovery")]
        [Tooltip("자폭 후 생존하면 넘어졌다가 기상한다 (구 프로젝트 Falldown 이관, 기존 Knockdown 상태 재사용). HP 0이면 대신 사망")]
        [SerializeField] private bool knockdownSelfAfterBlast = true;
        [Tooltip("넘어질 때 뒤로 밀리는 힘(m/s). 0이면 제자리에서 넘어짐")]
        [SerializeField] private float knockdownPushForce = 2f;
        [Tooltip("밀리는(Slide) 시간(초). 이후 Down(1s)→Getup 순으로 기상")]
        [SerializeField] private float knockdownSlideTime = 0.15f;

        private Character _character;
        private bool _prevTrigger;
        private CharacterCinematics _cinematics;
        private CameraSequence _camera;
        private bool _cameraPlaying;
        private bool _wasRunning;

        private void Awake()
        {
            _character = GetComponent<Character>();
            _cinematics = GetComponent<CharacterCinematics>();
        }

        private void Update()
        {
            if (_character == null) return;

            bool trig = ReadTrigger();
            if (trig && !_prevTrigger && CanTrigger())
                _character.CommandManager?.RequestAttack(selfDestructAttackName, requestInput);
            _prevTrigger = trig;

            TickSelfDestructLifecycle();
        }

        /// <summary>
        /// SelfDestruct 공격 수명 추적: (1) 카메라 시퀀스 재생/종료(그랩과 동일 경로),
        /// (2) 자폭 후 생존 시 넘어졌다 기상(구 프로젝트 Falldown 이관 — 기존 Knockdown 상태 재사용).
        /// SelfDestruct AttackAction이 Attack 상태로 실제 실행 중인 동안만 running=true.
        /// </summary>
        private void TickSelfDestructLifecycle()
        {
            var sm = _character.StateManager;
            bool running = sm != null && sm.CurrentStateType == CharacterStateType.Attack
                && _character.Combat != null && _character.Combat.CurrentAttackName == selfDestructAttackName;

            // (1) 자폭 카메라 시퀀스 — 실행 중 재생, 종료/피격취소/사망 시 Stop.
            if (_cinematics != null)
            {
                if (running && !_cameraPlaying)
                {
                    bool faceRight = _character.Movement == null || _character.Movement.FacingRight;
                    _camera = _cinematics.ResolveSelfDestructCamera(faceRight);
                    _camera?.Play();
                    _cameraPlaying = true;
                }
                else if (!running && _cameraPlaying)
                {
                    _camera?.Stop();
                    _camera = null;
                    _cameraPlaying = false;
                }
            }

            // (2) 자폭 종료 시점(running → 끝)에 생존해 있으면 넘어짐(Knockdown)으로 복귀. HP 0이면 이미 Dead라 진입 안 함.
            if (knockdownSelfAfterBlast && _wasRunning && !running && !_character.IsDead && sm != null)
            {
                var st = sm.CurrentStateType;
                if (st == CharacterStateType.Idle || st == CharacterStateType.Move
                    || st == CharacterStateType.Run || st == CharacterStateType.Land)
                    EnterPostBlastKnockdown(sm);
            }
            _wasRunning = running;
        }

        /// <summary>자폭 후 생존 넘어짐 — 기존 Knockdown 상태(Slide→Down→Getup→Idle)를 반동 넉다운으로 진입시킨다.</summary>
        private void EnterPostBlastKnockdown(CharacterStateManager sm)
        {
            var mv = _character.Movement;
            Vector3 dir = Vector3.zero;
            if (mv != null && knockdownPushForce > 0f)
                dir = (mv.FacingRight ? -1f : 1f) * mv.SplineForward; // 폭발 반동으로 뒤로 밀리며 넘어짐

            sm.SetPendingReaction(new CharacterStateManager.ReactionData
            {
                Direction = dir,
                Spec = new HitReactionSpec
                {
                    Kind = HitReactionKind.Knockdown,
                    HorizontalForce = knockdownPushForce,
                    PushDuration = knockdownSlideTime,
                },
                Attacker = _character.gameObject,
            });
            sm.ChangeState(CharacterStateType.Knockdown, true);
        }

        private void OnDisable()
        {
            if (_cameraPlaying) { _camera?.Stop(); _camera = null; _cameraPlaying = false; }
        }

        /// <summary>발동 가능 조건 — 사망/투혼 상태/커밋·무력화 상태를 확인.</summary>
        private bool CanTrigger()
        {
            if (_character.IsDead) return false;

            // 나찰과 상보 게이트: 투혼이 가득 찼으면 자폭하지 않는다(나찰이 발동).
            if (onlyWhenTouhonNotFull && _character.MaxTouhon > 0f
                && _character.CurrentTouhon >= _character.MaxTouhon)
                return false;

            // 이미 공격/커밋/무력화 상태면 발동하지 않는다(지상 상태만 공격 진입을 허용하므로 대부분 자동으로 안전).
            var sm = _character.StateManager;
            if (sm != null)
            {
                switch (sm.CurrentStateType)
                {
                    case CharacterStateType.Attack:
                    case CharacterStateType.Dead:
                    case CharacterStateType.Executing:
                    case CharacterStateType.Executed:
                    case CharacterStateType.Groggy:
                    case CharacterStateType.Vulnerable:
                    case CharacterStateType.Grab:
                    case CharacterStateType.ChainGrab:
                    case CharacterStateType.Knockdown:
                    case CharacterStateType.Airborne:
                        return false;
                }
            }
            return true;
        }

        /// <summary>공유 트리거 입력 읽기 (패드 LS+RS 동시 / 키보드 키). CharacterLatentAbility와 동일 방식.</summary>
        private bool ReadTrigger()
        {
            bool sticks = false;
            if (gamepadDualStick)
            {
                var gp = Gamepad.current;
                sticks = gp != null && gp.leftStickButton.isPressed && gp.rightStickButton.isPressed;
            }
            var kb = Keyboard.current;
            bool key = kb != null && kb[keyboardTrigger].isPressed;
            return sticks || key;
        }
    }
}
