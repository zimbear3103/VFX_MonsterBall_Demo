using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 체인 그랩 — 로드호그식 그래플 훅(구버전 CharacterChainGrab 이식).
    /// 발사 → 끌어오기(Pull) → 들쳐업기(Carry, 이동 가능) → 찌르기 콤보(X) / 피니셔(Y) / 손대포 발사(LB) → 해제.
    ///
    /// 프레임워크 준수:
    ///   - 플레이어 잠금은 CharacterStateType.ChainGrab 상태(단일 상태머신) 하나로만 한다(두 번째 상태머신 없음).
    ///   - 붙잡힌 적 제어는 EnemyCharacter.SetGrabbed(AI·이동·CharacterController 정지) — 이 컴포넌트가 위치/애니 소유.
    ///   - 데미지/넉백/에어본은 전부 공용 피격(DamageInfo → Character.ReceiveDamage). 체인이 HP·물리를 직접 만지지 않는다.
    ///   - 애니메이션은 이미 임포트된 공유 컨트롤러의 ChainGrab/Carry 파라미터를 구동한다.
    ///   - 대상별 가능 여부/위치 보정은 선택형 ChainCarryTarget 컴포넌트로 데이터화한다(없으면 기본 허용).
    ///
    /// 실행 순서(-15): PlayerController(-20)가 입력을 명령으로 넣은 뒤, Character(-10)의 상태머신이
    ///   원샷을 소비하기 전에 이 컴포넌트가 먼저 읽어 소비한다.
    /// </summary>
    [DefaultExecutionOrder(-15)]
    [RequireComponent(typeof(Character))]
    public class CharacterChainGrab : MonoBehaviour
    {
        private enum Phase { None, Windup, Extending, Stuck, Pulling, Carrying, Finisher, Recovering }

        [Header("Hook")]
        [Tooltip("최대 그랩 사거리(m) (구버전 MaxGrabRange 9)")]
        [SerializeField] private float maxGrabRange = 9f;
        [Tooltip("훅 유효 레인 폭(m) — 이 폭 안의 적만 걸린다 (구버전 HookLaneWidth 2.5)")]
        [SerializeField] private float hookLaneWidth = 2.5f;
        [Tooltip("발사 후 실제 판정까지의 준비 시간(초) — ChainGrab_Start 클립의 던지기 순간에 맞춘다")]
        [SerializeField] private float windupDuration = 0.15f;
        [Tooltip("체인이 목표/최대사거리까지 뻗는 시간(초) (구버전 장휴 튜닝 0.1)")]
        [SerializeField] private float extendDuration = 0.1f;
        [Tooltip("빗나감 시 회수 시간(초) (구버전 MissRetractDuration)")]
        [SerializeField] private float missRetractDuration = 0.15f;
        [Tooltip("그랩 쿨다운(초) (구버전 ChainGrabCooldown 3)")]
        [SerializeField] private float cooldown = 3f;

        [Header("Stuck")]
        [Tooltip("박힌 상태 최대 유지 시간(초) — 초과 시 자동 해제. 0이면 무제한 (구버전 MaxHoldDuration)")]
        [SerializeField] private float stuckMaxDuration = 5f;
        [Tooltip("박힌 적의 떨림 진폭(m) (구버전 장휴 튜닝 0.001 — 거의 안 떨리는 게 의도)")]
        [SerializeField] private float stuckTrembleAmplitude = 0.001f;
        [Tooltip("박힌 적의 떨림 주파수(Hz) (구버전 장휴 튜닝 3)")]
        [SerializeField] private float stuckTrembleFrequency = 3f;
        [Tooltip("박힌 상태에서 이동 입력이 이 크기를 넘으면 체인을 놓는다 (구버전 MoveReleaseThreshold)")]
        [SerializeField] private float stuckMoveReleaseThreshold = 0.5f;
        [Tooltip("박힘 상태에서 적 앵커를 손 정면 직선상에 둘 때 최소 사거리(m) — 손과 겹치지 않게 하한.")]
        [SerializeField] private float stuckLineReachMin = 0.8f;

        [Header("Victim Animation")]
        [Tooltip("체인 명중 후 피격 리액션이 재생될 시간(초). 이후 AirGrabCarried bool을 켜 피격 마지막 자세로 유지한다 (구버전 EnemyHitPoseFreezeDelay 0.3)")]
        [SerializeField] private float victimCarriedPoseDelay = 0.15f;
        [Tooltip("캐리 피격 리액션(AirGrabCarriedHit) 동안 CarriedHit을 유지하는 시간(초) — AnyState→AirGrabCarried가 리액션을 홀딩으로 되돌리는 것을 막는다. 피격 클립 길이에 맞춘다.")]
        [SerializeField] private float carriedHitHold = 0.3f;

        [Header("Carry")]
        [Tooltip("끌어오기 시간(초) (구버전 PullDuration 0.15)")]
        [SerializeField] private float pullDuration = 0.15f;
        [Tooltip("잡은 적을 두는 전방 거리(m)")]
        [SerializeField] private float holdDistance = 1.3f;
        [Tooltip("잡은 적을 두는 높이(m) (구버전 HoldHeightFallback 1.3)")]
        [SerializeField] private float holdHeight = 1.3f;
        [Tooltip("Carry 중 플레이어 이동 속도 배율 — 0이면 제자리 고정, 1이면 평소 속도 (구버전 CarryMoveSpeedMultiplier 0.8)")]
        [SerializeField] private float carryMoveMultiplier = 0.7f;
        [Tooltip("Carry 유지 위치 공통 오프셋(m). 대상별 추가 보정은 ChainCarryTarget.CarryOffset")]
        [SerializeField] private Vector3 carryOffset = Vector3.zero;
        [Tooltip("Carry 최대 유지 시간(초) — 초과 시 자동 해제 (구버전 MaxHoldDuration)")]
        [SerializeField] private float carryMaxDuration = 5f;

        [Header("Damage")]
        [Tooltip("찌르기 1회 데미지 = 공격력 × 이 배율 (X 연타)")]
        [SerializeField] private float stabDamageMultiplier = 0.5f;
        [Tooltip("피니셔 데미지 = 공격력 × 이 배율 (Y) — 가드 불가")]
        [SerializeField] private float finisherDamageMultiplier = 5f;
        [Tooltip("피니셔 종료 시 적을 띄우는 상승 속도(m/s) — 공용 Airborne 반응 경로로 처리")]
        [SerializeField] private float finisherLaunchUp = 7f;
        [Tooltip("손대포 발사 데미지 = 공격력 × 이 배율 (LB) — 가드 불가")]
        [SerializeField] private float launchDamageMultiplier = 2f;
        [Tooltip("손대포 발사 시 적을 날리는 수평(x)/수직(y) 속도(m/s) — 공용 Airborne 반응 경로로 처리")]
        [SerializeField] private Vector2 launchImpulse = new Vector2(14f, 5f);

        [Header("Carry Combo")]
        [Tooltip("캐리 콤보 그룹(AttackRoot 아래 ComboGroup). 이 그룹의 StarterAttack로 콤보를 시작하고, 이후 분기는 AttackAction.transitions가 정한다.")]
        [SerializeField] private ComboGroup carryComboGroup;
        [Tooltip("carryComboGroup 미지정 시 이름으로 탐색할 캐리 콤보 그룹 오브젝트 이름.")]
        [SerializeField] private string carryComboGroupName = "Carry_Combo";
        [Tooltip("피니셔 캐리 종료 시점 기본값(초) — 각 피니셔의 CarryAttackLink.finisherDuration이 0 이하일 때만 사용하는 폴백.")]
        [SerializeField] private float carryFinisherDuration = 2.3f;
        [Tooltip("피니셔 킥(적 날림) 순간 기본값(초) — 각 피니셔의 CarryAttackLink.finisherKickTime이 0 이하일 때만 사용하는 폴백.")]
        [SerializeField] private float carryFinisherKickTime = 1.4f;
        [Tooltip("스탭(비피니셔) 캐리 공격 중 플레이어 이동을 멈추는 시간(초) 기본값 — 각 스탭의 CarryAttackLink.stabLockDuration이 0 이하일 때만 쓰는 폴백. 이 시간 동안 제자리 정지 후 캐리 감속 이동으로 복귀. 스탭 애니 길이에 맞춘다.")]
        [SerializeField] private float carryStabLockDuration = 0.4f;

        [Header("Feedback")]
        [Tooltip("체인이 적에 박힐 때 FeedbackManager 키 (구버전 HitFeedbackKey)")]
        [SerializeField] private string hitFeedbackKey = "Hit";
        [Tooltip("잡기 성공(끌어오기) 시 FeedbackManager 키")]
        [SerializeField] private string grabFeedbackKey = "Grab";
        [Tooltip("피니셔 임팩트 FeedbackManager 키")]
        [SerializeField] private string finisherFeedbackKey = "HeavySmoke";
        [Tooltip("발사 시 카메라 셰이크 진폭 (구버전 장휴 튜닝 0.05)")]
        [SerializeField] private float fireShakeAmplitude = 0.05f;
        [Tooltip("체인 적중 시 카메라 셰이크 진폭 (구버전 장휴 튜닝 HitImpactShakeAmplitude 0.3)")]
        [SerializeField] private float hitShakeAmplitude = 0.3f;
        [Tooltip("빈손 회수 시 카메라 셰이크 진폭 (구버전 장휴 튜닝 0.05)")]
        [SerializeField] private float retractShakeAmplitude = 0.05f;
        [Tooltip("적을 잡아온 회수 시 카메라 셰이크 진폭 (구버전 RetractShakeAmplitudeWithEnemy 0.25)")]
        [SerializeField] private float retractShakeAmplitudeWithEnemy = 0.25f;
        [Tooltip("카메라 셰이크 지속 시간(초) (구버전 임펄스 duration 0.2)")]
        [SerializeField] private float shakeDuration = 0.2f;

        [Header("Debug (View Only)")]
        [SerializeField] private Phase phase = Phase.None;
        [SerializeField] private EnemyCharacter grabbed;
        [SerializeField] private int stabCount;

        private Character _character;
        private Animator _animator;
        private ChainGrabVisual _visual;
        private CharacterCinematics _cinematics;
        private CameraSequence _carryCamera;
        private float _phaseTimer;
        private float _nextGrabTime = -1f;
        private Vector3 _pullFrom;
        private Animator _victimAnimator;
        private ChainCarryTarget _grabbedCaps;
        private Transform _grabbedCarryAnchor;  // per-target anchor (e.g. neck) aligned to HoldAnchor; null = root
        private float _carriedHitUntil = -1f;   // CarriedHit bool 유지 종료 시각(적 피격 리액션 게이트)
        private AttackAction _carryAction;      // current carry combo hit (null = holding)
        private AttackAction _finisherAction;   // finisher hit (applies its Reaction on release)
        private bool _finisherLaunched;         // finisher: enemy already kicked/launched this sequence
        private float _finisherKickTime;        // resolved from CarryAttackLink (fallback: carryFinisherKickTime)
        private float _finisherDuration;        // resolved from CarryAttackLink (fallback: carryFinisherDuration)
        private bool _finisherFixedHold;        // finisher: hold enemy at fixed offset (not hand)
        private Vector3 _finisherHoldOffset;    // finisher fixed hold offset (belt-relative from root)
        private bool _finisherShowHitEffect = true; // finisher: play victim default hit effect (CarryAttackLink.ShowHitEffect)
        private float _carryAttackLockUntil = -1f;   // stab carry attack: stop-in-place until this time (-1 = not locked)
        private ComboGroup _resolvedCarryGroup; // cached carry combo group (resolved by name)
        private EnemyCharacter _extendTarget;   // Extending 중 예약된 대상(아직 SetGrabbed 전)
        private Vector3 _extendFrom;            // 체인 시작점(발사 순간의 루트)
        private Vector3 _extendTo;              // 체인 도달점(적 앵커 또는 최대사거리)
        private Vector3 _chainPoint;            // 현재 체인 끝 위치(비주얼 조준용)
        private Vector3 _stuckPos;              // 박힌 적의 기준 위치(떨림의 중심)
        private float _stuckReach;              // 박힘 시 손→앵커 정면(belt) 사거리 — 체인을 직선으로 유지
        private float _trembleSeed;
        private float _victimCarriedPoseAt;     // AirGrabCarried bool을 켤 절대 시각(Embed + delay)
        private bool _victimCarriedApplied;     // 이번 그랩에서 AirGrabCarried를 이미 켰는가

        private CharacterCommandManager Commands => _character != null ? _character.CommandManager : null;
        private CharacterMovement Movement => _character != null ? _character.Movement : null;
        private CharacterCombat Combat => _character != null ? _character.Combat : null;

        private void Awake()
        {
            _character = GetComponent<Character>();
            _animator = GetComponentInChildren<Animator>(true);
            _visual = GetComponent<ChainGrabVisual>();
            _cinematics = GetComponent<CharacterCinematics>();
            _trembleSeed = Random.value * 100f;
        }

        private void Update()
        {
            if (_character == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // 강제 종료 안전망(단일 Cleanup 경로): 시퀀스 진행 중인데 캐릭터가 ChainGrab 상태를 벗어났다면
            // (피격/넉백/에어본/사망/씬전환 등 외부 요인) 즉시 정리한다. 어떤 상황에서도 붙잡힌 적이
            // 공중에 고정되거나 AI/Collider가 영구 정지되지 않도록 한다.
            if (phase != Phase.None && !IsInChainState())
            {
                Cleanup(exitState: false);
                return;
            }

            if (phase == Phase.None)
            {
                TryStart();
                return;
            }

            _phaseTimer += dt;
            switch (phase)
            {
                case Phase.Windup: TickWindup(); break;
                case Phase.Extending: TickExtending(); break;
                case Phase.Stuck: TickStuck(dt); break;
                case Phase.Pulling: TickPulling(); break;
                case Phase.Carrying: TickCarrying(); break;
                case Phase.Finisher: TickFinisher(); break;
                case Phase.Recovering: TickRecovering(); break;
            }
        }

        private void LateUpdate()
        {
            // 비주얼 조준은 위치 갱신(Update)이 끝난 뒤 1프레임 지연 없이 수행한다(구버전 LateUpdate 순서 이식).
            if (_visual == null || !_visual.IsActive) return;
            // 거리 스케일은 홀드(Stuck)·당기기(Pulling)에서 적용 — 당기는 동안 체인이 손<->적 거리에 맞춰
            // 짧아져 박힌 체인이 적을 자연스럽게 끌고오는 것처럼 보인다. 뻗기/회수는 FX 애니가 길이 담당.
            if (phase == Phase.Extending || phase == Phase.Stuck || phase == Phase.Pulling || phase == Phase.Recovering)
                _visual.UpdateAim(_chainPoint, scaleByDistance: phase == Phase.Stuck || phase == Phase.Pulling);
        }

        private bool IsInChainState()
            => _character != null && _character.StateManager != null
               && _character.StateManager.CurrentStateType == CharacterStateType.ChainGrab;

        private void TryStart()
        {
            var c = Commands;
            if (c == null || !c.ChainGrabRequested) return;
            c.ConsumeChainGrab();

            if (_character.IsDead) return;
            if (Time.time < _nextGrabTime) return;
            var m = Movement;
            if (m == null || !m.IsGrounded) return;

            _character.StateManager.ChangeState(CharacterStateType.ChainGrab);
            SetTrigger(AnimParams.ChainGrab);
            phase = Phase.Windup;
            _phaseTimer = 0f;
            stabCount = 0;
        }

        private void TickWindup()
        {
            if (_phaseTimer < windupDuration) return;

            // 발사 순간: 히트스캔으로 대상 예약 → 체인 비주얼을 켜고 뻗기 시작(구버전 Extending).
            // 발사 연출은 대상 유무와 무관하게 '빗맞음'처럼 전방 최대사거리로 뻗는다(자연스러운 던지기).
            // 실제 연결은 연장이 끝난 '명중 순간'(Embed)에 대상 위치로 스냅한다(구버전 ExtendLikeMissAlways).
            _extendTarget = FindHookTarget();
            _extendFrom = _visual != null ? _visual.RootPosition : transform.position + Vector3.up * holdHeight;
            _extendTo = _extendFrom + FacingWorld() * maxGrabRange;
            _chainPoint = _extendFrom;

            _visual?.Activate();
            CameraShakeService.Shake(Vector3.one * fireShakeAmplitude, shakeDuration);

            phase = Phase.Extending;
            _phaseTimer = 0f;
        }

        private void TickExtending()
        {
            float t = extendDuration > 0f ? Mathf.Clamp01(_phaseTimer / extendDuration) : 1f;
            // 뻗는 동안엔 전방 궤적 유지(유도 없음) — 명중 순간 Embed에서 대상 위치로 스냅(구버전 이식).
            _chainPoint = Vector3.Lerp(_extendFrom, _extendTo, t);
            if (t < 1f) return;

            var victim = _extendTarget;
            _extendTarget = null;
            if (victim == null || victim.IsDead || victim.IsGrabbed)
            {
                BeginMissRetract();
                return;
            }
            Embed(victim);
        }

        /// <summary>체인이 적에 박히는 순간(구버전 Holding/stuck 진입) — 이펙트·셰이크·홀딩 애니.</summary>
        private void Embed(EnemyCharacter victim)
        {
            grabbed = victim;
            _grabbedCaps = victim.GetComponent<ChainCarryTarget>();
            _grabbedCarryAnchor = _grabbedCaps != null ? _grabbedCaps.CarryAnchor : null;
            _victimAnimator = victim.GetComponentInChildren<Animator>(true);
            victim.SetGrabbed(true);
            _stuckPos = victim.transform.position;
            _stuckReach = ComputeStuckReach(victim);
            _chainPoint = EmbedAnchor(victim);

            SetBool(AnimParams.ChainGrabHolding, true);
            // 체인에 걸리는 순간 넉백(DamagedBackward) 리액션 없이 즉시 붙잡힌 포즈(AirGrabCarried)로 고정한다.
            // 뒤로 날아가는 것처럼 보이던 문제 제거 — 적은 그 자리에서 체인에 걸린 채 유지된다.
            // AirGrabCarried placeholder = 현재 자세로 정지. 임팩트 셰이크/피드백은 아래에서 그대로 유지.
            SetVictimBool(AnimParams.AirGrabCarried, true);
            _victimCarriedApplied = true;
            _victimCarriedPoseAt = Time.time;
            FaceGrabbed();

            PlayFeedback(hitFeedbackKey, _chainPoint);
            CameraShakeService.Shake(Vector3.one * hitShakeAmplitude, shakeDuration);

            phase = Phase.Stuck;
            _phaseTimer = 0f;
        }

        /// <summary>
        /// 피격 리액션 재생 시간이 지나면 AirGrabCarried bool을 켜 잡힌 자세를 유지시킨다
        /// (구버전 RequestAirGrabbedCarriedMotion → SetChainCarried(true) 대응). Stuck/Pulling/Carrying에서 매 틱 호출.
        /// </summary>
        private void UpdateVictimCarriedPose()
        {
            if (_victimCarriedApplied || grabbed == null || _victimAnimator == null) return;
            if (Time.time < _victimCarriedPoseAt) return;
            SetVictimBool(AnimParams.AirGrabCarried, true);
            _victimCarriedApplied = true;
        }

        /// <summary>박힌 상태 — 적을 제자리에 고정 + 떨림. 입력(체인/공격)으로 끌어오기, 이동 입력으로 해제.</summary>
        private void TickStuck(float dt)
        {
            if (grabbed == null || grabbed.IsDead) { BeginMissRetract(); return; }
            UpdateVictimCarriedPose();
            // 체인이 박혀 있는 동안 적은 항상 플레이어를 마주보게 한다(carryAnchor 유무와 무관).
            MirrorGrabbedFacing();

            // 떨림(Perlin) — 위치 스냅 후 적용이라 누적되지 않는다(구버전 ApplyAirGrabShake 이식).
            float time = Time.time * stuckTrembleFrequency * 0.1f;
            float ox = (Mathf.PerlinNoise(_trembleSeed, time) - 0.5f) * 2f * stuckTrembleAmplitude;
            float oy = (Mathf.PerlinNoise(_trembleSeed + 37f, time) - 0.5f) * 2f * stuckTrembleAmplitude;
            if (_grabbedCarryAnchor != null)
            {
                // 적 앵커(목)를 손 정면 직선상(_stuckReach)에 둔다 → 체인이 손끝에서 곧게 뻗음(높이/깊이 정렬).
                Vector3 lineAnchor = HandBase() + FacingWorld() * _stuckReach;
                Vector3 trembled = lineAnchor + new Vector3(ox, Mathf.Abs(oy), 0f);
                grabbed.transform.position += trembled - _grabbedCarryAnchor.position;
                _chainPoint = lineAnchor;
            }
            else
            {
                // 앵커 미지정 폴백: 기존 동작(박힌 자리 유지 + 고정 높이 체인끝).
                grabbed.transform.position = _stuckPos + new Vector3(ox, Mathf.Abs(oy), 0f);
                _chainPoint = _stuckPos + Vector3.up * holdHeight;
            }

            var c = Commands;
            if (c != null)
            {
                // 체인 재입력 또는 공격 입력 = 끌어오기 시작.
                if (c.ChainGrabRequested || c.AttackRequested)
                {
                    if (c.ChainGrabRequested) c.ConsumeChainGrab();
                    if (c.AttackRequested) c.ConsumeAttack();
                    BeginPull();
                    return;
                }
                // 이동 입력 = 체인을 놓고 해제(구버전 ReleaseFromStuckByMovement).
                if (c.MoveInput.magnitude > stuckMoveReleaseThreshold)
                {
                    grabbed.transform.position = _stuckPos;
                    ReleaseGrabbedAndRetract();
                    return;
                }
            }

            if (stuckMaxDuration > 0f && _phaseTimer >= stuckMaxDuration)
            {
                grabbed.transform.position = _stuckPos;
                ReleaseGrabbedAndRetract();
            }
        }

        private void BeginPull()
        {
            SetBool(AnimParams.ChainGrabHolding, false);
            _pullFrom = grabbed != null ? grabbed.transform.position : _stuckPos;
            // 당기는 동안 체인 비주얼은 켜 두고 거리 비례로 줄인다(LateUpdate) — 박힌 체인이 적에 걸린 채
            // 짧아지며 끌려오는 것처럼 보이게. 회수(zip)+왜곡은 끌어오기 완료 시(RetractVisual) 재생한다.
            PlayFeedback(grabFeedbackKey, _pullFrom + Vector3.up * holdHeight);
            phase = Phase.Pulling;
            _phaseTimer = 0f;
        }

        /// <summary>박힌 적을 놓고 체인만 회수한다(캐리 없이 종료).</summary>
        private void ReleaseGrabbedAndRetract()
        {
            if (grabbed != null) grabbed.SetGrabbed(false);
            ClearGrabRefs();
            BeginMissRetract();
        }

        /// <summary>빗나감/해제 공통 — 회수 연출 시작(Recovering).</summary>
        private void BeginMissRetract()
        {
            SetTrigger(AnimParams.ChainGrabMiss);
            RetractVisual();
            phase = Phase.Recovering;
            _phaseTimer = 0f;
        }

        /// <summary>체인 비주얼 회수(종료 애니 + 왜곡) + 회수 셰이크(적을 잡아왔으면 더 강하게).</summary>
        private void RetractVisual(bool withEnemy = false, bool immediateDistortion = false)
        {
            if (_visual != null && _visual.IsActive)
            {
                if (immediateDistortion)
                {
                    // 캐리 진입 순간 왜곡(Distortion)을 즉시 재생하고, 회수 애니는 그대로 두되 왜곡 중복만 막는다.
                    _visual.PlayRetractDistortion();
                    _visual.DeactivateDeferred(playDistortion: false);
                }
                else
                {
                    _visual.DeactivateDeferred(true);
                }
                float amp = withEnemy ? retractShakeAmplitudeWithEnemy : retractShakeAmplitude;
                CameraShakeService.Shake(Vector3.one * amp, shakeDuration);
            }
        }

        private Vector3 EmbedAnchor(EnemyCharacter victim)
            => _grabbedCarryAnchor != null ? _grabbedCarryAnchor.position : victim.transform.position + Vector3.up * holdHeight;

        /// <summary>박힘 시 손에서 적 앵커까지의 정면(belt) 사거리 — Stuck에서 체인을 직선으로 유지하는 길이.</summary>
        private float ComputeStuckReach(EnemyCharacter victim)
        {
            Vector3 anchorPos = _grabbedCarryAnchor != null
                ? _grabbedCarryAnchor.position
                : victim.transform.position + Vector3.up * holdHeight;
            float reach = Vector3.Dot(anchorPos - HandBase(), FacingWorld());
            return Mathf.Max(reach, stuckLineReachMin);
        }

        private EnemyCharacter FindHookTarget()
        {
            Vector3 origin = transform.position;
            Vector3 fwd = FacingWorld();
            var cols = Physics.OverlapSphere(origin, maxGrabRange, ~0, QueryTriggerInteraction.Collide);
            EnemyCharacter best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < cols.Length; i++)
            {
                var enemy = cols[i].GetComponentInParent<EnemyCharacter>();
                if (enemy == null || enemy == _character) continue;
                if (enemy.IsDead || enemy.IsGrabbed) continue;
                if (!AIController.IsHostile(_character, enemy)) continue;

                // 대상별 capability — 컴포넌트가 있고 canChain=false면 제외 (없으면 기본 허용)
                var caps = enemy.GetComponent<ChainCarryTarget>();
                if (caps != null && !caps.CanChain) continue;

                Vector3 to = enemy.transform.position - origin;
                to.y = 0f;
                float along = Vector3.Dot(to, fwd);
                if (along <= 0f) continue;
                Vector3 lateral = to - fwd * along;
                if (lateral.magnitude > hookLaneWidth * 0.5f) continue;
                if (along < bestDist) { bestDist = along; best = enemy; }
            }
            return best;
        }

        private void TickPulling()
        {
            if (grabbed == null || grabbed.IsDead) { RetractVisual(); EndGrab(); return; }
            UpdateVictimCarriedPose();
            float t = pullDuration > 0f ? Mathf.Clamp01(_phaseTimer / pullDuration) : 1f;
            MirrorGrabbedFacing();
            grabbed.transform.position = Vector3.Lerp(_pullFrom, CarriedRootPosition(), t);
            _chainPoint = _grabbedCarryAnchor != null ? _grabbedCarryAnchor.position : grabbed.transform.position + Vector3.up * holdHeight;
            FaceGrabbed();
            if (t >= 1f)
            {
                // 끌어오기 완료 — 체인 비주얼 회수(zip 종료 애니 + 셰이크). 왜곡은 캐리 진입 순간 즉시 재생.
                RetractVisual(withEnemy: true, immediateDistortion: true);

                // 대상이 Carry 불가면 끌어오기만 하고 놓는다(무거운 적) — 체인=끌어오기 전용으로 확장 가능.
                if (_grabbedCaps != null && !_grabbedCaps.CanCarry) { EndGrab(); return; }

                SetBool(AnimParams.ChainGrabHolding, false);
                SetBool(AnimParams.ChainGrabCarrying, true);
                // Carry 진입: 플레이어 이동 허용(감속). 점프는 계속 잠금. 적은 매 프레임 HoldAnchor 추종.
                EnterCarryMovement();
                phase = Phase.Carrying;
                _phaseTimer = 0f;
            }
        }

        /// <summary>Carry 단계에서 플레이어가 감속 이동할 수 있게 한다(ChainGrab 상태는 유지).</summary>
        private void EnterCarryMovement()
        {
            var m = Movement;
            if (m == null) return;
            m.CanMove = true;
            m.CanJump = false;
            m.SetMoveSpeedMultiplier(Mathf.Max(0f, carryMoveMultiplier));
            // 캐리 중에는 좌우 플립을 잠근다 — 감속 이동 입력으로 방향이 뒤집히지 않게(캐리~피니셔 유지, 해제는 ChainGrabState.Exit).
            m.FacingLocked = true;
        }

        /// <summary>Carry 이외 단계에서 플레이어 이동을 다시 잠근다(피니셔 등).</summary>
        private void LockPlayerMovement()
        {
            var m = Movement;
            if (m == null) return;
            m.CanMove = false;
            m.CanJump = false;
            m.SetMoveSpeedMultiplier(0f);
        }

        private void TickCarrying()
        {
            if (grabbed == null || grabbed.IsDead) { EndGrab(); return; }
            UpdateVictimCarriedPose();
            UpdateCarriedHit();
            // 스탭 공격 이동 잠금: 공격이 끝나면(잠금 시간 경과) 캐리 감속 이동을 다시 허용한다.
            UpdateCarryAttackLock();

            // 적은 매 프레임 플레이어 앞(HoldAnchor)을 추종 — 플레이어가 걸으면 함께 이동.
            // 플레이어 방향에 맞춰 적을 미러링한 뒤(앵커가 올바른 쪽에 오도록) 위치를 정렬한다.
            MirrorGrabbedFacing();
            // 지정 앵커(목 등)가 HoldAnchor에 오도록 루트를 보정한다(앵커 없으면 루트가 그 자리).
            grabbed.transform.position = CarriedRootPosition();

            // 콤보는 재시작하지 않는다: 다음 입력은 항상 현재 타의 transitions로만 진행(1→2→3)해
            // 종결 타(CarryAttack3 = releasesGrab)에서 끝난다. 느린 입력으로 CarryAttack1이 재발동하는 무한 반복 방지.
            var c = Commands;
            if (c != null)
            {
                if (c.ChainGrabRequested) { c.ConsumeChainGrab(); LaunchHandCannon(); return; }
                if (c.AttackRequested)
                {
                    var input = c.RequestedAttackInput;
                    c.ConsumeAttack();
                    CarryComboInput(input);
                    return;
                }
            }

            if (_phaseTimer >= carryMaxDuration)
                Release();
        }

        // ─────────── Carry Combo (AttackAction 기반 경량 러너) ───────────
        // 데이터(애니 트리거/피니셔 여부/스텝 창)는 CarryAttackLink, 데미지·반응은 AttackAction(DamageMultiplier·Reaction),
        // 콤보 그래프(XXX/XXY)는 AttackAction.transitions가 소유한다. 여기선 흐름만 구동하고 잡힌 적에게 직접 적용한다.

        private void CarryComboInput(AttackInputType input)
        {
            AttackAction next;
            if (input == AttackInputType.Heavy || input == AttackInputType.HeavyHold)
                // Heavy(Y)는 캐리 중 언제든(홀딩/몇 타든) 지정 피니셔로 바로 발동. 없으면 transitions 폴백.
                next = ResolveCarryHeavyFinisher() ?? ResolveCarryTransition(_carryAction, input);
            else
                next = _carryAction == null ? ResolveCarryStart(input) : ResolveCarryTransition(_carryAction, input);
            if (next != null) PlayCarryAttack(next);
        }

        /// <summary>캐리 그룹에서 Heavy(Y) 피니셔로 지정된 타(IsHeavyFinisher). Heavy가 언제든 발동하도록.</summary>
        private AttackAction ResolveCarryHeavyFinisher()
        {
            var group = ResolveCarryGroup();
            if (group == null) return null;
            var attacks = group.Attacks;
            for (int i = 0; i < attacks.Length; i++)
            {
                var a = attacks[i];
                var link = a != null ? a.GetComponent<CarryAttackLink>() : null;
                if (link != null && link.IsHeavyFinisher) return a;
            }
            return null;
        }

        /// <summary>캐리 콤보 시작 타 — Light(X)로만 시작, 캐리 콤보 그룹의 StarterAttack. 이후 분기는 transitions.</summary>
        private AttackAction ResolveCarryStart(AttackInputType input)
        {
            if (input != AttackInputType.Light) return null;
            var group = ResolveCarryGroup();
            return group != null ? group.StarterAttack : null;
        }

        /// <summary>캐리 콤보 그룹 해석 — 직접 참조 우선, 없으면 이름으로 탐색(캐시).</summary>
        private ComboGroup ResolveCarryGroup()
        {
            if (carryComboGroup != null) return carryComboGroup;
            if (_resolvedCarryGroup != null) return _resolvedCarryGroup;
            if (_character != null && !string.IsNullOrEmpty(carryComboGroupName))
            {
                var groups = _character.GetComponentsInChildren<ComboGroup>(true);
                for (int i = 0; i < groups.Length; i++)
                    if (groups[i] != null && groups[i].name == carryComboGroupName) { _resolvedCarryGroup = groups[i]; return groups[i]; }
            }
            return null;
        }

        /// <summary>현재 타의 transitions에서 입력에 맞는 다음 타 — 캐리는 잡힌 적을 항상 맞히므로 requireHit/지상/방향은 무시.</summary>
        private AttackAction ResolveCarryTransition(AttackAction from, AttackInputType input)
        {
            if (from == null) return null;
            var list = from.Transitions;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t == null || t.nextAttack == null) continue;
                if (t.input != input) continue;
                return t.nextAttack;
            }
            return null;
        }

        /// <summary>캐리 타 1회 실행 — 애니 트리거 + (스탭: 잡은 채 데미지 / 피니셔: 잡기 해제 + Reaction 적용).</summary>
        private void PlayCarryAttack(AttackAction action)
        {
            _carryAction = action;
            var link = action.GetComponent<CarryAttackLink>();
            string trigger = link != null && !string.IsNullOrEmpty(link.AnimatorTrigger) ? link.AnimatorTrigger : AnimParams.CarryAttack;

            // 잔류 트리거로 인한 오발동 방지 — 이번 타의 트리거만 남긴다.
            // (예: 스탭의 CarryAttack 트리거가 잔류하면 ChainGrabCarry 전이 우선순위상 order 1(CarryAttack)이
            //  order 2(CarryAttackFinish=피니셔)를 이겨, Y를 눌러도 스탭 모션이 나가는 문제)
            ResetTrigger(AnimParams.CarryAttack);
            ResetTrigger(AnimParams.CarryAttackFinish);
            SetTrigger(trigger);
            // 캐리 공격/피니셔 중임을 표시 — AnyState→ChainGrabCarry(ChainGrabCarrying true · CarryAttacking false)가
            // 공격/피니셔 애니를 홀딩으로 되돌리는 것을 막는다(애니 끝까지 재생). 종료 시 ClearGrabRefs에서 false.
            SetBool(AnimParams.CarryAttacking, true);
            // 캐리 중 피격 리액션(AirGrabCarried → AirGrabCarriedHit → 자동 복귀). CarriedHit을 켜서
            // AnyState→AirGrabCarried(CarriedHit false)가 리액션을 즉시 홀딩으로 되돌리는 것을 막는다(CarryAttacking과 동일 원리).
            SetVictimTrigger(AnimParams.AirGrabCarriedHit);
            SetVictimBool(AnimParams.CarriedHit, true);
            _carriedHitUntil = Time.time + carriedHitHold;

            // 타격 이펙트 — CarryAttackLink에 지정한 프리팹을 잡힌 적 위치 + 오프셋에 스폰(있으면).
            Vector3 hitPos = grabbed != null ? grabbed.transform.position + Vector3.up * holdHeight : HoldAnchor();
            bool spawnedFx = SpawnCarryHitEffect(link, hitPos);

            if (link != null && link.ReleasesGrab)
            {
                // 피니셔: 애니 재생 후 carryFinisherDuration 뒤 적을 놓고 action.Reaction 적용(TickFinisher).
                _finisherAction = action;
                _finisherLaunched = false;
                _finisherKickTime = link.FinisherKickTime > 0f ? link.FinisherKickTime : carryFinisherKickTime;
                _finisherDuration = link.FinisherDuration > 0f ? link.FinisherDuration : carryFinisherDuration;
                _finisherFixedHold = link.FinisherFixedHold;
                _finisherHoldOffset = link.FinisherHoldOffset;
                _finisherShowHitEffect = link.ShowHitEffect;
                // 시퀀스 카메라 — 플레이어 CharacterCinematics의 캐리 피니셔 카메라(Facing 좌/우, 자동 미러) 재생.
                if (_cinematics != null)
                {
                    _carryCamera = _cinematics.ResolveCarryFinisherCamera(Movement != null && Movement.FacingRight);
                    _carryCamera?.Play();
                }
                // 커스텀 이펙트가 없을 때만 기존 FeedbackManager 피니셔 이펙트로 폴백.
                if (!spawnedFx) PlayFeedback(finisherFeedbackKey, HoldAnchor());
                // 적 전용 던지기 클립(FinishFly)이 오버라이드로 매핑돼 있으면, 지금의 그랩(잡은 채)으로 그 클립을 재생한다.
                // 미매핑 적은 기존대로 잡힘 포즈 유지 후 릴리즈 시 표준 에어본으로 날아간다.
                if (VictimHasFinishFlyClip()) SetVictimTrigger(AnimParams.CarryFinishFly);
                // 피니셔는 Finisher 페이즈가 이동을 소유하므로 스탭 잠금 타이머를 해제한다(TickCarrying이 더는 돌지 않음).
                _carryAttackLockUntil = -1f;
                LockPlayerMovement();
                phase = Phase.Finisher;
                _phaseTimer = 0f;
            }
            else
            {
                // 스탭: 잡은 채 데미지만(Kind=None으로 강제 — 캐리 유지). 데미지 배율은 AttackAction 소유.
                stabCount++;
                DealToGrabbed(AttackPower() * action.DamageMultiplier, HitReactionKind.None, 0f, 0f, action.Unblockable, link != null ? link.ShowHitEffect : true);
                action.PlayHitFeedbacksManual(); // 카메라 쉐이크/줌·슬로우·진동 (AttackAction에서 설정)
                // 스탭 공격 동안 플레이어를 제자리에 멈춘다 — 공격이 끝날 때까지 이동 불가(피니셔와 동일 원리).
                // 상태는 ChainGrab 유지(별도 상태 신설 없음). 잠금 시간이 지나면 TickCarrying이 EnterCarryMovement로 감속 이동을 복구한다.
                // 다음 타로 연결되면 PlayCarryAttack이 다시 호출돼 잠금이 갱신되고, 종결 타(피니셔)는 아래 분기에서 별도로 잠근다.
                float stabLock = link != null && link.StabLockDuration > 0f ? link.StabLockDuration : carryStabLockDuration;
                if (stabLock > 0f)
                {
                    LockPlayerMovement();
                    _carryAttackLockUntil = Time.time + stabLock;
                }
            }
        }

        /// <summary>CarryAttackLink에 지정한 히트 이펙트 프리팹을 basePos + 벨트-상대 오프셋에 스폰. 스폰했으면 true.</summary>
        private bool SpawnCarryHitEffect(CarryAttackLink link, Vector3 basePos)
        {
            if (link == null || link.HitEffect == null || !link.SpawnHitEffect) return false;
            Vector3 pos = basePos + CarryEffectWorldOffset(link.HitEffectOffset);
            var fx = Instantiate(link.HitEffect, pos, Quaternion.identity);
            fx.SetActive(true);
            if (link.HitEffectLifetime > 0f) Destroy(fx, link.HitEffectLifetime);
            return true;
        }

        /// <summary>피니셔 고정 홀드 — 적 앵커를 플레이어 루트 기준 고정 오프셋에 둔다(손 추종 안 함).</summary>
        private Vector3 FinisherHoldRootPosition()
        {
            Vector3 target = transform.position + CarryEffectWorldOffset(_finisherHoldOffset);
            if (grabbed == null || _grabbedCarryAnchor == null) return target;
            Vector3 gap = target - _grabbedCarryAnchor.position;
            return grabbed.transform.position + gap;
        }

        /// <summary>벨트-상대 오프셋(x=정면/Facing, y=위, z=깊이)을 월드 벡터로 변환. Flip 시 x 자동 반전.</summary>
        private Vector3 CarryEffectWorldOffset(Vector3 local)
        {
            var m = Movement;
            Vector3 fwd = FacingWorld();
            Vector3 depth = m != null ? m.SplineDepth : Vector3.forward;
            return fwd * local.x + Vector3.up * local.y + depth * local.z;
        }

        /// <summary>잡힌 적의 애니메이터가 CarryFinishFly에 전용 클립(placeholder 아님)을 오버라이드해 두었는가.</summary>
        private bool VictimHasFinishFlyClip()
        {
            var ovr = _victimAnimator != null ? _victimAnimator.runtimeAnimatorController as AnimatorOverrideController : null;
            if (ovr == null) return false;
            var mapped = ovr["CarryFinishFly_Placeholder"];
            return mapped != null && mapped.name != "CarryFinishFly_Placeholder";
        }

        private void TickFinisher()
        {
            UpdateCarriedHit();
            // 킥 순간: 적을 놓고 반응(날림)을 적용한다. 단 ClearGrabRefs/상태전환은 하지 않아
            // ChainGrabCarrying=true가 유지되고 피니셔 애니(찌르기→발차기)가 끝까지 재생된다.
            if (!_finisherLaunched && _phaseTimer >= _finisherKickTime)
            {
                var victim = grabbed;
                grabbed = null; // 위치 구동 중단(이제 물리로 날아감)
                SetVictimBool(AnimParams.AirGrabCarried, false); // 캐리/FinishFly 포즈 종료 → 에어본으로
                if (victim != null) victim.SetGrabbed(false);
                if (victim != null && !victim.IsDead && _finisherAction != null)
                {
                    Vector3 hitPoint = victim.transform.position + Vector3.up * holdHeight;
                    var info = new DamageInfo(gameObject, AttackPower() * _finisherAction.DamageMultiplier, hitPoint, FacingWorld());
                    info.Unblockable = _finisherAction.Unblockable;
                    info.Reaction = _finisherAction.Reaction;
                    info.ShowHitEffect = _finisherShowHitEffect;
                    victim.ReceiveDamage(info);
                }
                if (_finisherAction != null) _finisherAction.PlayHitFeedbacksManual(); // 킥 순간 카메라 연출
                _finisherLaunched = true;
            }

            // 킥 전: 적을 잡은 채 유지. 손 추종 대신 고정 오프셋(자식처럼 보이는 것 방지) 옵션.
            if (grabbed != null && !grabbed.IsDead)
            {
                MirrorGrabbedFacing();
                grabbed.transform.position = _finisherFixedHold ? FinisherHoldRootPosition() : CarriedRootPosition();
            }

            // 피니셔 애니 종료: 캐리 종료(ChainGrabCarrying=false) + 지상 복귀.
            if (_phaseTimer >= _finisherDuration)
            {
                if (grabbed != null) grabbed.SetGrabbed(false);
                ClearGrabRefs();
                FinishSequence(exitState: true);
            }
        }

        private void LaunchHandCannon()
        {
            // 손대포 발사감: 전방으로 강하게 날림(Airborne+수평) — 공용 피격 경로로 처리한 뒤 해제.
            ReleaseWithLaunch(AttackPower() * launchDamageMultiplier, launchImpulse.y, launchImpulse.x);
        }

        private void Release() => EndGrab();

        private float AttackPower() => _character != null && _character.Stats != null ? _character.Stats.FinalAttackPower : 10f;

        /// <summary>붙잡힌 적에게 공용 피격 경로로 데미지/반응을 전달한다(체인이 HP·물리를 직접 만지지 않음).</summary>
        private void DealToGrabbed(float damage, HitReactionKind kind, float upForce, float horizForce, bool unblockable, bool showHitEffect = true)
        {
            if (grabbed == null) return;
            Vector3 hitPoint = grabbed.transform.position + Vector3.up * holdHeight;
            var info = new DamageInfo(gameObject, damage, hitPoint, FacingWorld());
            info.Unblockable = unblockable;
            info.ShowHitEffect = showHitEffect;
            // 잡은 적은 그랩이 피격 애니(AirGrabCarriedHit)를 직접 구동하므로 표준 리액션(DamagedBackward)을 억제 — 기본 히트모션과 이중 재생 방지.
            info.SuppressHitReaction = true;
            info.Reaction = new HitReactionSpec
            {
                Kind = kind,
                VerticalForce = upForce,
                AirTime = kind == HitReactionKind.Launch ? 0.5f : 0f,
                HorizontalForce = horizForce,
                PushDuration = 0.3f,
            };
            grabbed.ReceiveDamage(info);
        }

        /// <summary>
        /// 잡은 적을 놓으면서 던지기(Throw/발사) — 먼저 SetGrabbed(false)로 적의 이동/AI를 복구한 뒤,
        /// 공용 DamageInfo(Airborne)로 날린다. 새 물리 시스템을 만들지 않고 기존 Airborne 반응을 사용한다.
        /// </summary>
        private void ReleaseWithLaunch(float damage, float upForce, float horizForce)
        {
            var victim = grabbed;
            // 적 제어 복구 먼저 — Airborne 반응이 적 자신의 CharacterMovement로 구동되게 한다.
            if (victim != null) victim.SetGrabbed(false);
            ClearGrabRefs();

            if (victim != null && !victim.IsDead)
            {
                Vector3 hitPoint = victim.transform.position + Vector3.up * holdHeight;
                var info = new DamageInfo(gameObject, damage, hitPoint, FacingWorld());
                info.Unblockable = true;
                info.Reaction = new HitReactionSpec
                {
                    Kind = HitReactionKind.Launch,
                    VerticalForce = Mathf.Max(0.1f, upForce),
                    AirTime = 0.5f,
                    HorizontalForce = horizForce,
                };
                victim.ReceiveDamage(info);
            }

            FinishSequence(exitState: true);
        }

        private void EndGrab()
        {
            if (grabbed != null) grabbed.SetGrabbed(false);
            ClearGrabRefs();
            FinishSequence(exitState: true);
        }

        /// <summary>외부 강제 종료(피격 등)용 정리 — 이동 상태는 이미 다른 상태가 소유하므로 건드리지 않는다.</summary>
        private void Cleanup(bool exitState)
        {
            if (grabbed != null) grabbed.SetGrabbed(false);
            _visual?.DeactivateImmediate();
            ClearGrabRefs();
            FinishSequence(exitState);
        }

        /// <summary>적 피격 리액션 게이트(CarriedHit)를 유지 시간이 지나면 내린다 — AirGrabCarriedHit이 exit time으로 자연 복귀.</summary>
        private void UpdateCarriedHit()
        {
            if (_carriedHitUntil >= 0f && Time.time >= _carriedHitUntil)
            {
                SetVictimBool(AnimParams.CarriedHit, false);
                _carriedHitUntil = -1f;
            }
        }

        /// <summary>스탭 캐리 공격 이동 잠금 — 잠금 시간이 지나면 캐리 감속 이동(EnterCarryMovement)을 복구한다. Carrying 페이즈에서 매 틱 호출.</summary>
        private void UpdateCarryAttackLock()
        {
            if (_carryAttackLockUntil < 0f) return;
            if (Time.time < _carryAttackLockUntil) return;
            _carryAttackLockUntil = -1f;
            EnterCarryMovement();
        }

        private void ClearGrabRefs()
        {
            // 적 애니 상태 복구 — 참조를 지우기 전에 캐리 포즈 bool을 반드시 내린다(잔존 방지, 멱등).
            SetVictimBool(AnimParams.AirGrabCarried, false);
            SetVictimBool(AnimParams.CarriedHit, false);
            _carriedHitUntil = -1f;
            _carryAttackLockUntil = -1f;
            _victimCarriedApplied = false;
            grabbed = null;
            _grabbedCaps = null;
            _grabbedCarryAnchor = null;
            _carryAction = null;
            _finisherAction = null;
            _finisherLaunched = false;
            _victimAnimator = null;
            _extendTarget = null;
            SetBool(AnimParams.ChainGrabHolding, false);
            SetBool(AnimParams.ChainGrabCarrying, false);
            SetBool(AnimParams.CarryAttacking, false);
            _carryCamera?.Stop();
            _carryCamera = null;
        }

        /// <summary>시퀀스 종료 공통 처리 — 쿨다운 시작, 그리고 아직 ChainGrab 상태면 지상 상태로 복귀(이동 복구는 상태 Exit이 담당).</summary>
        private void FinishSequence(bool exitState)
        {
            phase = Phase.None;
            _phaseTimer = 0f;
            _nextGrabTime = Time.time + cooldown;

            // 정상 종료 경로: 지상 상태로 복귀한다. 이동 잠금 해제는 ChainGrabState.Exit,
            // 이동 배율 설정은 진입할 지상 상태(Idle/Move/Run)의 Enter가 소유한다.
            // 강제 종료 경로(exitState=false)는 이미 다른 상태가 이동을 소유하므로 상태를 바꾸지 않는다.
            if (exitState && IsInChainState())
                _character.StateManager.ChangeState(_character.StateManager.ResolveGroundedStateByInput());
        }

        private void TickRecovering()
        {
            if (_phaseTimer >= missRetractDuration)
                EndGrab();
        }

        /// <summary>손 본(체인 비주얼 루트) 월드 위치 — 손의 상하 모션 포함. 비주얼 미확보 시 루트+높이 폴백.</summary>
        private Vector3 HandBase()
            => _visual != null ? _visual.RootPosition : transform.position + Vector3.up * holdHeight;

        private Vector3 HoldAnchor()
        {
            Vector3 baseAnchor = HandBase() + FacingWorld() * holdDistance + carryOffset;
            if (_grabbedCaps != null) baseAnchor += _grabbedCaps.CarryOffset;
            return baseAnchor;
        }

        /// <summary>
        /// 잡은 적을 둘 때 적 루트가 아니라 지정 앵커(목 등)가 HoldAnchor에 오도록 루트 위치를 보정한다.
        /// 구버전 CharacterGrabbed.SyncPositionToTarget의 gap 방식 이식. 앵커 미지정(=루트)이면 HoldAnchor를 그대로 반환.
        /// </summary>
        private Vector3 CarriedRootPosition()
        {
            Vector3 target = HoldAnchor();
            if (grabbed == null || _grabbedCarryAnchor == null) return target;
            Vector3 gap = target - _grabbedCarryAnchor.position;
            return grabbed.transform.position + gap;
        }

        /// <summary>잡은 적을 플레이어 기준으로 미러링 — 플레이어가 도는(flip) 방향에 맞춰 적도 돌게 한다(구버전 MirrorGrabberDirection 이식).</summary>
        private void MirrorGrabbedFacing()
        {
            var m = Movement;
            if (m == null || grabbed == null || grabbed.Movement == null) return;
            bool shouldFaceRight = !m.FacingRight;   // 적은 플레이어를 마주보도록 반대 방향
            if (grabbed.Movement.FacingRight != shouldFaceRight)
                grabbed.Movement.SetFacingRight(shouldFaceRight);
        }

        private Vector3 FacingWorld()
        {
            var m = Movement;
            if (m == null) return transform.forward;
            Vector3 f = m.FacingRight ? m.SplineForward : -m.SplineForward;
            return f.sqrMagnitude > 0.0001f ? f.normalized : transform.forward;
        }

        private void FaceGrabbed()
        {
            var m = Movement;
            if (m == null || grabbed == null) return;
            Vector3 to = grabbed.transform.position - transform.position;
            float along = Vector3.Dot(to, m.SplineForward);
            if (Mathf.Abs(along) > 0.05f) m.SetFacingRight(along > 0f);
        }

        private void PlayFeedback(string key, Vector3 pos)
        {
            if (string.IsNullOrEmpty(key)) return;
            Aiara.FeedbackManager.PlayFeedbackAtWorld(key, pos, Quaternion.identity);
        }

        private void SetTrigger(string p) => SetTriggerOn(_animator, p);
        private void SetVictimTrigger(string p) => SetTriggerOn(_victimAnimator, p);

        private void ResetTrigger(string paramName)
        {
            if (_animator == null || string.IsNullOrEmpty(paramName)) return;
            foreach (var p in _animator.parameters)
                if (p.name == paramName && p.type == AnimatorControllerParameterType.Trigger) { _animator.ResetTrigger(paramName); return; }
        }

        private void SetVictimBool(string paramName, bool value)
        {
            if (_victimAnimator == null || string.IsNullOrEmpty(paramName)) return;
            foreach (var p in _victimAnimator.parameters)
                if (p.name == paramName && p.type == AnimatorControllerParameterType.Bool)
                { _victimAnimator.SetBool(paramName, value); return; }
        }

        private void SetTriggerOn(Animator anim, string paramName)
        {
            if (anim == null || string.IsNullOrEmpty(paramName)) return;
            foreach (var p in anim.parameters)
                if (p.name == paramName && p.type == AnimatorControllerParameterType.Trigger)
                { anim.SetTrigger(paramName); return; }
        }

        private void SetBool(string paramName, bool value)
        {
            if (_animator == null || string.IsNullOrEmpty(paramName)) return;
            foreach (var p in _animator.parameters)
                if (p.name == paramName && p.type == AnimatorControllerParameterType.Bool)
                { _animator.SetBool(paramName, value); return; }
        }

        private void OnDisable()
        {
            // 씬 전환/파괴 시에도 붙잡힌 적을 반드시 복구한다.
            if (grabbed != null) grabbed.SetGrabbed(false);
            _visual?.DeactivateImmediate();
            ClearGrabRefs();
            phase = Phase.None;
        }
    }
}
