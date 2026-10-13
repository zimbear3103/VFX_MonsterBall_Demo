using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 콤보 파생 잡기(ActionGrab) 시퀀스 — 구 프로젝트 CharacterGrab + CharacterGrabbed 이관(ver2 재작성).
    ///
    /// 진입: 콤보 Transition이 GrabAttackLink가 붙은 AttackAction으로 분기되면 이 컴포넌트가 감지해
    /// 대상 검증 후 잡기 시퀀스로 전환한다.
    /// 대상 없음: GrabAttackLink.RequireTarget(기본 true)이면 CharacterCombat이 Transition 해석 단계에서
    /// IComboBranchGate(CanBranch→HasGrabbableTarget)로 분기 자체를 건너뛰어 헛스윙이 나오지 않는다 (2026-09-08).
    /// RequireTarget=false면 분기는 되고 해당 AttackAction의 일반 스윙이 폴백 공격으로 재생된다.
    ///
    /// 프레임워크 준수 (CharacterChainGrab과 동일 패턴):
    ///   - 플레이어 잠금은 CharacterStateType.Grab 상태 하나로만 한다.
    ///   - 붙잡힌 적 제어는 EnemyCharacter.SetGrabbed(AI·이동·CharacterController 정지) — 이 컴포넌트가 위치/애니 소유.
    ///   - 잡기 중 피격자는 완전 무적(SetInvincible) — 다른 공격의 넉백/에어본/경직이 연출을 깨지 않는다.
    ///   - 위치 정렬은 CharacterActionPoints 앵커(시전자 GrabPoint ↔ 피격자 GrabbedPoint) 기준.
    ///   - 동기 애니: 시전자 ActionGrab(GrabType 슬롯) / 피격자 ActionGrabbed(지연 0.25s — 구버전 블렌드 동기화).
    ///   - 타이밍은 애니 이벤트(시전자 Grab/HeavyGrabThrow/GrabFinish, 피격자 Takedown)가 소유하고, 전 이벤트에 타임아웃 폴백.
    ///     칼 박힘/던지기는 GrabAttackLink.AttachTiming/ThrowTiming=TimeFromGrabStart로 '잡기 시작 후 N초'로도 발동 가능 (2026-09-07).
    ///   - ActionGrab_B 튜닝 항목 위치(GrabAttackLink): 적 위치=Positioning(Grab Start)·Impale 3 / 박힐 때 모션=Impale 2 /
    ///     박히는 시점=Impale 1 / 던지는 시점·방향·반응=Throw 1~3.
    ///   - 카메라는 피격자 프리팹의 CharacterCinematics(GrabType별 좌/우 쌍)를 재생.
    ///
    /// 예외 복구: 시퀀스 중 플레이어가 Grab 상태를 벗어나면(피격/사망/씬 전환) 감시 루프가
    /// 피격자를 즉시 복구한다. OnDisable에서도 동일 정리.
    /// </summary>
    [DefaultExecutionOrder(-15)]
    [RequireComponent(typeof(Character))]
    public class CharacterActionGrab : MonoBehaviour
    {
        private enum Phase { None, Grabbing } // 기상 회복은 CharacterKnockdownState가 소유 (구 Recovering 페이즈 제거)

        [Header("Victim Animation")]
        [Tooltip("잡힘(ActionGrabbed) 애니 시작 지연(초) — 시전자의 콤보→잡기 블렌드와 동기화 (구버전 GrabbedAnimStartDelay)")]
        [SerializeField] private float victimAnimStartDelay = 0.25f;
        [Tooltip("Takedown(슬램) 슬라이드 시간(초) (구버전 TakedownSlideDuration)")]
        [SerializeField] private float takedownSlideDuration = 0.15f;

        [Header("End Position")]
        [Tooltip("잡기 종료 후 시전자 루트를 모델(시각) 위치에 정착시키는 시간(초). 복귀 블렌드 동안 본이 루트로 돌아가는 만큼 루트를 반대로 옮겨 " +
                 "화면상 몸 위치를 고정한다 — 종료 후 시작 위치/지정 좌표로 튀는 순간이동 제거. 0 = 종료 순간 1회 스냅")]
        [SerializeField] private float endSettleDuration = 0.35f;

        [Header("Debug (View Only)")]
        [SerializeField] private Phase phase = Phase.None;
        [SerializeField] private EnemyCharacter victim;

        private Character _character;
        private CharacterActionPoints _points;
        private AnimationEventHandler _playerEvents;
        private AnimationEventHandler _victimEvents;
        private GrabAttackLink _link;
        private CharacterActionPoints _victimPoints;
        private CharacterCinematics _victimCinematics;
        private CameraSequence _camera;
        private AttackAction _processedAction;

        private int _grabType;
        private float _phaseTimer;
        private float _victimAnimAt = -1f;
        private bool _victimAnimDone;
        private bool _takedownDone;
        private bool _victimDied;
        private bool _endRequested;
        // 던지기 (구버전 KatanaGrab launchMode)
        private bool _victimReleasedEarly; // HeavyGrabThrow로 조기 해제됨 — GrabFinish까지 시전자만 유지
        private bool _throwBackward;       // 잡는 중 방향키 모니터 결과 (구버전 HeavyThrowBackward)
        private bool _throwFlipDone;       // 뒤로 던지기 플립 완료 — 이후 방향키 모니터 중단, PerformThrow에서 재플립 금지
        // 무기 부착 — 칼에 박힌 채 이동 (구버전 SyncHeavyGrabAttachment)
        private Transform _weaponAnchor;          // 무기 본 앵커 (구버전 Big_Sword_GrapPoint)
        private bool _attachActive;               // Grab 이벤트 이후 부착 추적 중
        private bool _attachOffsetCaptured;       // 첫 LateUpdate에서 상대 오프셋 확보 (스냅 방지)
        private Quaternion _attachRotOffset;
        private Vector3 _attachRootOffset;
        private Animator _victimAnimator;
        private Quaternion _victimPreAttachRotation;
        private bool _victimRotationCaptured;
        private bool _victimPoseFrozen;           // 피격자 애니메이터 정지(히트 포즈) 중
        private float _poseFreezeAt = -1f;
        // 모션 배속 (GrabAttackLink.AnimationSpeed) — 시전자/피격자 Animator.speed를 잡기 동안만 덮고 종료 시 복원
        private Animator _casterAnimator;
        private Animator _victimSpeedAnimator;
        private bool _speedApplied;
        // 랜딩 이펙트 예약 (Takedown + takedownLandingDelay)
        private bool _landingPending;
        private float _landingAt;
        private Vector3 _landingFallbackPos;
        // Takedown 슬라이드
        private bool _sliding;
        private float _slideTimer;
        private Vector3 _slideFrom;
        private Vector3 _slideTo;
        // 플립 후 전방 이동 상쇄 (GrabAttackLink.FreezeTravelAfterFlip) — 플립 순간 시각 지면 위치를 기준으로 전방축 변위를 매 프레임 되돌린다.
        private bool _travelFreeze;
        private Vector3 _travelRef;      // 플립 순간 시각 지면 위치 (출발점)
        private float _travelTimer;      // 플립 후 경과 (FlipForwardDuration 진행용)
        // 종료 정착 (endSettleDuration) — 그랩 종료 후 복귀 블렌드 동안 시각 위치 고정.
        private bool _settling;
        private float _settleTimer;
        private Vector3 _settleVisRef;
        private Vector3 _settleLastRoot;
        private float _settleAttackAt = -1f;               // 정착 중 Attack 진입 시각(_settleTimer 기준), 미진입 -1
        private const float SettleAttackGrace = 0.15f;      // Attack 진입 후 정착을 이어가는 유예(공격 CrossFade 구간)
        private CharacterController _controller;
        // Grab Visuals (GrabAttackLink.GrabVisuals) — 잡기 타임라인 VFX. 지연 항목은 예약 후 TickGrabbing에서 발화.
        private struct PendingGrabVisual { public GrabVisual visual; public float fireAt; }
        private readonly List<PendingGrabVisual> _pendingGrabVisuals = new List<PendingGrabVisual>();
        // feedbackDelay 예약 (슬로우/쉐이크/줌/진동) — 잡기 타임라인 시각 기준. 잡기가 먼저 끝나면 남은 시간은 게임 시간 코루틴으로 넘긴다 (2026-09-15).
        private readonly List<PendingGrabVisual> _pendingGrabFeedbacks = new List<PendingGrabVisual>();
        // 구간 배속 (GrabAttackLink.SpeedSegments, 2026-09-15) — 이번 틱의 배율. _phaseTimer(잡기 타임라인)가 dt × 이 값으로 진행되고
        // 시전자/피격자 Animator.speed에도 곱해져 모션과 타임라인(박힘/던지기/랜딩/Grab Visual/연출 딜레이)이 함께 빨라진다.
        private float _grabSpeedMult = 1f;
        private GameObject _grabVisualHost; // 잡기 동안 다시 켜 둔 AttackAction 루트 (Stop()이 꺼 버리므로)
        // Grab Visual 좌/우 반전 — ActionGrab AttackAction은 Player 루트 직속(Visual 미러 바깥)이라 스케일 반전을 못 받는다.
        // 개별 이펙트 트랜스폼은 절대 건드리지 않고(플레이 중 인스펙터 수정·PlayModeChangeRecorder 저장과 충돌), AttackAction의 Visual 컨테이너
        // 스케일 부호만 캐릭터 Visual 루트와 같은 축으로 뒤집는다. 잡기 종료 시 부호 복원 (2026-09-08).
        private Transform _grabMirrorRoot;

        private CharacterMovement Movement => _character != null ? _character.Movement : null;
        private CharacterCombat Combat => _character != null ? _character.Combat : null;

        public bool IsSettling => _settling;

        private void Awake()
        {
            _character = GetComponent<Character>();
            _points = GetComponent<CharacterActionPoints>();
            _controller = GetComponent<CharacterController>();
        }

        private void OnEnable()
        {
            var animator = GetComponentInChildren<Animator>(true);
            _casterAnimator = animator;
            if (animator != null)
            {
                _playerEvents = animator.GetComponent<AnimationEventHandler>();
                if (_playerEvents == null)
                    _playerEvents = animator.gameObject.AddComponent<AnimationEventHandler>();
                _playerEvents.OnAnimationEvent += OnPlayerAnimEvent;
            }
        }

        private void OnDisable()
        {
            if (_playerEvents != null) _playerEvents.OnAnimationEvent -= OnPlayerAnimEvent;
            Cleanup(exitState: false);
        }

        private void Update()
        {
            if (_character == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // 종료 정착(TickSettle)·플립 후 이동 상쇄(ApplyTravelFreeze)는 LateUpdate에서 — Animator 평가 후 같은 프레임에 보정해야
            // 본 복귀(ReturnMovement 전이 duration 0 = 즉시 스냅)가 한 프레임 먼저 렌더되는 '팍 갔다 돌아오는' 현상이 없다 (2026-09-07).

            // 강제 종료 안전망: 시퀀스 진행 중인데 플레이어가 Grab 상태를 벗어났다면(피격/사망 등) 즉시 정리.
            if (phase == Phase.Grabbing && !IsInGrabState())
            {
                Cleanup(exitState: false);
                return;
            }

            switch (phase)
            {
                case Phase.None: TryDetectGrabAttack(); break;
                case Phase.Grabbing: TickGrabbing(dt); break;
            }
        }

        private bool IsInGrabState()
            => _character.StateManager != null
               && _character.StateManager.CurrentStateType == CharacterStateType.Grab;

        // ─────────── 진입 감지 (콤보 Transition → GrabAttackLink) ───────────

        private void TryDetectGrabAttack()
        {
            var combat = Combat;
            if (combat == null || !combat.IsAttacking) { _processedAction = null; return; }
            var action = combat.CurrentAction;
            if (action == null || action == _processedAction) return;
            _processedAction = action;

            var link = action.GetComponent<GrabAttackLink>();
            if (link == null) return;

            var target = ResolveVictim(link);
            if (target == null) return; // 대상 없음 → 이 AttackAction의 일반 스윙이 폴백 공격으로 재생된다.

            Begin(link, target);
        }

        /// <summary>
        /// 지금 이 잡기 데이터로 잡을 수 있는 대상이 있는가 — GrabAttackLink.CanBranch(콤보 분기 게이트)가 호출.
        /// 진입 감지(TryDetectGrabAttack)와 같은 ResolveVictim을 쓰므로 게이트 통과 = 실제 잡기 진입과 일치한다.
        /// 잡기 진행 중이거나 비활성이면 false.
        /// </summary>
        public bool HasGrabbableTarget(GrabAttackLink link)
        {
            if (link == null || _character == null || !isActiveAndEnabled) return false;
            if (phase != Phase.None) return false;
            return ResolveVictim(link) != null;
        }

        /// <summary>
        /// 잡기 대상 선정 — 직전 콤보 적중 대상 우선(구버전 LastHitHealth), 없으면 전방 근접 탐색.
        /// 직전 적중 대상은 LastHitGrabRange(넓음)로 검사 — X·X·X 적중 넉백으로 GrabRange 밖까지 밀려난 적도 잡힌다 (2026-09-05).
        /// </summary>
        private EnemyCharacter ResolveVictim(GrabAttackLink link)
        {
            var combat = Combat;

            if (link.PreferLastHitTarget && combat != null)
            {
                var last = combat.LastHitVictim as EnemyCharacter;
                // 직전 적중 공격별 사거리 오버라이드(예: NormalAttack_3_Ground 적중 후) → 없으면 LastHitGrabRange (2026-09-14).
                var lastAction = combat.LastHitAction;
                float lastRange = link.ResolveLastHitGrabRange(lastAction != null ? lastAction.AttackName : null);
                if (last != null
                    && Time.time - combat.LastHitVictimTime <= link.LastHitWindow
                    && IsGrabbable(last, link, lastRange))
                    return last;
            }

            // 근접 탐색: 전방 우선, 가장 가까운 적 (구버전 ProximityGrabWeapon 대응).
            Vector3 origin = transform.position;
            Vector3 fwd = FacingWorld();
            var cols = Physics.OverlapSphere(origin, link.GrabRange, ~0, QueryTriggerInteraction.Collide);
            EnemyCharacter best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < cols.Length; i++)
            {
                var e = cols[i].GetComponentInParent<EnemyCharacter>();
                if (e == null || !IsGrabbable(e, link, link.GrabRange)) continue;
                Vector3 to = e.transform.position - origin;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist > link.GrabRange) continue;
                bool front = Vector3.Dot(to, fwd) >= 0f;
                float score = dist + (front ? 0f : 100f); // 정면 우선, 후방은 최후순위
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        private bool IsGrabbable(EnemyCharacter e, GrabAttackLink link, float maxRange)
        {
            if (e == null || e == _character) return false;
            if (e.IsDead || e.IsGrabbed) return false;
            if (!AIController.IsHostile(_character, e)) return false;
            Vector3 to = e.transform.position - transform.position;
            to.y = 0f;
            if (to.magnitude > maxRange) return false;

            // 실행 상태 검사 — 공중/다운/처형 중에는 잡을 수 없다.
            var sm = e.StateManager;
            if (sm != null)
            {
                var s = sm.CurrentStateType;
                if (s == CharacterStateType.Airborne || s == CharacterStateType.Knockdown
                    || s == CharacterStateType.Executing || s == CharacterStateType.Executed
                    || s == CharacterStateType.Dead || s == CharacterStateType.Grab
                    || s == CharacterStateType.ChainGrab)
                    return false;
            }

            // 대상별 잡기 가능 데이터 (컴포넌트 없으면 기본 허용 — 구버전 CanBeGrabbed && GrabType >= 0)
            var pts = e.GetComponent<CharacterActionPoints>();
            if (pts != null && !pts.CanBeGrabbed) return false;
            return true;
        }

        // ─────────── 시퀀스 ───────────

        private void Begin(GrabAttackLink link, EnemyCharacter target)
        {
            _link = link;
            victim = target;
            _victimPoints = target.GetComponent<CharacterActionPoints>();
            // 카메라/이펙트 데이터: 시전자(구버전 CharacterJangHyu/CameraSequence 배선) 우선, 없으면 피격자.
            _victimCinematics = GetComponent<CharacterCinematics>();
            if (_victimCinematics == null)
                _victimCinematics = target.GetComponent<CharacterCinematics>();
            _victimEvents = target.GetComponentInChildren<AnimationEventHandler>(true);
            if (_victimEvents != null) _victimEvents.OnAnimationEvent += OnVictimAnimEvent;

            _grabType = link.GrabType >= 0 ? link.GrabType
                : (_victimPoints != null ? _victimPoints.VictimGrabType : 0);

            // 진입 입력이 '뒤 방향키'였는지 — 재정렬(대상 방향으로 SetFacingRight) 전의 Facing 기준으로 샘플링.
            // 뒤+강공격(ActionGrab_B 등)으로 들어오면 잡기 시작부터 뒤로 던지기 상태로 취급해
            // 키를 떼도 ThrowFlipDelay 시점에 플립된다 (2026-09-09).
            bool entryBackward = IsBackKeyHeld();

            // 방향: 시전자는 대상을 향하고, 대상은 시전자를 마주본다 (FacingLocked 걸리기 전에 설정).
            var m = Movement;
            bool faceRight = m != null && m.FacingRight;
            if (m != null)
            {
                Vector3 to = target.transform.position - transform.position;
                float along = Vector3.Dot(to, m.SplineForward);
                if (Mathf.Abs(along) > 0.05f)
                {
                    faceRight = along > 0f;
                    m.SetFacingRight(faceRight);
                }
            }
            if (target.Movement != null)
                target.Movement.SetFacingRight(!faceRight);

            // 플레이어: 진행 중 스윙 정리 후 Grab 상태로 (이동/점프/Facing 잠금은 상태가 소유).
            Combat?.CancelCurrentAttack();
            // Grab Visuals 호스트: CancelCurrentAttack → AttackAction.Stop()이 프리팹 루트를 꺼 버리므로 (히트박스/코루틴은 이미 정리됨)
            // 잡기 동안만 루트를 다시 켜 자식 VFX를 재생할 수 있게 한다. 종료 시 StopGrabVisuals가 다시 끈다 (2026-09-08).
            // 잡기 타임라인 시각은 여기서 0으로 — 아래 FireGrabVisuals(GrabStart)/피격자 애니 지연 예약이 _phaseTimer 기준이므로
            // (2026-09-15 실시간→타임라인 전환) 반드시 예약보다 먼저 리셋해야 이전 잡기의 시각이 남아 예약이 영영 안 터지는 일이 없다.
            _phaseTimer = 0f;
            _grabSpeedMult = link.EvaluateSpeedSegment(0f);
            BeginGrabVisuals(link);
            _character.StateManager.ChangeState(CharacterStateType.Grab);

            // 피격자: 완전 제어 + 무적 (일반 피격이 연출을 깨지 않게).
            target.SetGrabbed(true);
            if (target.Combat != null)
            {
                target.Combat.CancelCurrentAttack();
                target.Combat.SetInvincible(true);
            }

            // 위치 정렬: 시전자 GrabPoint(GrabType별) ↔ 피격자 GrabbedPoint (구버전 AlignToGrabber).
            AlignVictim(target);

            // 동기 애니: 시전자 즉시, 피격자는 지연(구버전 TriggerGrabbedDelayed).
            _character.StateManager.PlayActionGrabAnimation(_grabType);
            FireGrabVisuals(GrabVisualTrigger.GrabStart);
            // 모션 배속 (GrabAttackLink) — 시전자/피격자 동기 유지를 위해 피격자 애니 시작 지연도 같은 비율로 축소.
            ApplyAnimationSpeed(link, target);
            _victimAnimAt = Mathf.Max(0f, victimAnimStartDelay) / link.AnimationSpeed; // 잡기 타임라인 시각(잡기 시작 = 0)
            _victimAnimDone = false;
            // 부착(칼 박힘) 잡기: 지연 ActionGrabbed는 어차피 박힘 시점에 취소되므로 예약하지 않고,
            // 잡기 시작 즉시 '박히기 전 모션'(PreImpaleVictimMotion)을 재생해 박힐 때까지 Idle로 서 있는 구간을 없앤다 (2026-09-08).
            if (link.AttachVictimToWeapon)
            {
                _victimAnimDone = true;
                PlayVictimMotion(link.PreImpaleVictimMotion);
            }

            // 카메라: 피격자 프리팹의 GrabType별 좌/우 시퀀스.
            _camera = _victimCinematics != null ? _victimCinematics.ResolveGrabCamera(_grabType, faceRight) : null;
            _camera?.Play();

            // 무기 부착(칼 박힘) 준비 — 앵커는 시전자 계층에서 이름으로 탐색 (구버전 ResolveHeavyGrabAnchor).
            _weaponAnchor = null;
            _attachActive = false;
            _attachOffsetCaptured = false;
            _victimRotationCaptured = false;
            _victimPoseFrozen = false;
            _poseFreezeAt = -1f;
            _victimAnimator = null;
            if (link.AttachVictimToWeapon && !string.IsNullOrEmpty(link.WeaponAnchorName))
            {
                var transforms = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                    if (transforms[i] != null && transforms[i].name == link.WeaponAnchorName)
                    { _weaponAnchor = transforms[i]; break; }
                if (_weaponAnchor == null)
                    Debug.LogWarning($"[CharacterActionGrab] 무기 앵커 '{link.WeaponAnchorName}' 미발견 — 부착 없이 진행", this);
                _victimAnimator = target.GetComponentInChildren<Animator>(true);
            }

            _phaseTimer = 0f;
            _takedownDone = false;
            _victimDied = false;
            _endRequested = false;
            _sliding = false;
            _victimReleasedEarly = false;
            _throwBackward = entryBackward; // 뒤 방향키 진입 = 뒤 방향키가 이미 눌린 상태로 시작
            _throwFlipDone = false;
            _travelFreeze = false;
            _settling = false; // 직전 그랩의 정착이 남아 있으면 중단
            phase = Phase.Grabbing;
#if UNITY_EDITOR
            Debug.Log($"[CharacterActionGrab] Begin link={link.gameObject.name} throw={link.EnableThrow} back={link.AllowBackwardThrow} flipDelay={link.ThrowFlipDelay} mv={(_character.CommandManager != null ? _character.CommandManager.MoveInput : Vector2.zero)} facingRight={(m != null && m.FacingRight)}", this);
            LogTiming("Begin");
#endif
        }

        /// <summary>
        /// 피격자 루트를 시전자 GrabPoint 앵커에 정렬(피격자 GrabbedPoint 오프셋 유지).
        /// GrabPoint 앵커가 없으면 시전자 전방 alignDistance 지점(Facing 기준) 폴백.
        /// GrabPoint/GrabbedPoint는 오른쪽 바라볼 때 기준으로 배치하면 되고, Flip 시
        /// 스플라인 전방축 성분이 자동 반전된다(루트가 회전하지 않아 자식 앵커가 안 뒤집히는 문제 보정).
        /// </summary>
        private void AlignVictim(EnemyCharacter target)
        {
            Transform grabPoint = _points != null ? _points.ResolveGrabPoint(_grabType) : null;
            Vector3 anchorPos = (grabPoint != null && grabPoint != transform)
                ? MirrorByFacing(grabPoint.position)
                : transform.position + FacingWorld() * (_link != null ? _link.AlignDistance : 1.4f);

            // 추가 오프셋 (오른쪽 기준, 스플라인 축) — Flip이면 전방 성분 자동 반전.
            if (_link != null && _link.VictimOffset != Vector3.zero)
            {
                var m = Movement;
                Vector3 off = _link.VictimOffset;
                Vector3 fwdAxis = m != null ? m.SplineForward : transform.forward;
                Vector3 depthAxis = m != null ? m.SplineDepth : Vector3.Cross(fwdAxis, Vector3.up).normalized;
                float sign = (m == null || m.FacingRight) ? 1f : -1f;
                anchorPos += fwdAxis * (off.x * sign) + Vector3.up * off.y + depthAxis * off.z;
            }

            Transform victimAnchor = _victimPoints != null ? _victimPoints.GrabbedPoint : target.transform;
            Vector3 anchorOffset = target.transform.position - victimAnchor.position;
            anchorOffset = MirrorVectorByFacing(anchorOffset); // 피격자 앵커 오프셋도 같은 기준으로 반전
            Vector3 pos = anchorPos + anchorOffset;
            pos.y = target.transform.position.y + (_link != null ? _link.VictimOffset.y : 0f); // 높이는 유지 + 오프셋 y (지면 침투 방지)
            target.transform.position = pos;
            Physics.SyncTransforms(); // CharacterController가 이전 위치로 되돌리는 실측 버그 방지 (구버전 교훈)
        }

        private void TickGrabbing(float dt)
        {
            // 구간 배속 (2026-09-15): 이 시각의 배율로 잡기 타임라인을 진행하고 시전자/피격자 모션 배속에도 곱한다.
            _grabSpeedMult = _link != null ? _link.EvaluateSpeedSegment(_phaseTimer) : 1f;
            dt *= _grabSpeedMult;
            _phaseTimer += dt;
            ApplySegmentAnimatorSpeed();

            // 던지기로 조기 해제된 경우 시전자 애니(GrabFinish)만 기다린다 (구버전 _grabTargetReleasedEarly).
            if (victim == null && !_victimReleasedEarly) { EndGrab(); return; }

            if (victim != null)
            {
                // 피격자 잡힘 애니 지연 시작.
                if (!_victimAnimDone && _victimAnimAt >= 0f && _phaseTimer >= _victimAnimAt)
                {
                    _victimAnimDone = true;
                    if (victim.StateManager != null)
                        victim.StateManager.PlayActionGrabbedAnimation();
                }

                // 방향 모니터 — 잡는 중 방향키(뒤/아래=뒤로, 앞/위=앞으로, 중립=유지) (구버전 UpdateHeavyThrowDirectionMonitor).
                // 던지기(ActionGrab_B)뿐 아니라 슬램 잡기(ActionGrab_A)도 동일하게 뒤로 돌릴 수 있다 (2026-09-06).
                if (_link != null && _link.AllowBackwardThrow && !_throwFlipDone)
                {
                    UpdateThrowDirectionMonitor();
                    // 지정 시점 플립 — 잡기 시작 후 throwFlipDelay초가 지났고 뒤 방향키가 눌려 있으면 즉시 뒤로 돌린다.
                    // (delay 이전에 누르고 있었으면 정확히 delay 시점에, 이후에 눌렀으면 누른 순간 플립 — 던지기/슬램 전까지.)
                    // 플립 후에는 방향키를 더 읽지 않고 HeavyGrabThrow/Takedown은 재플립 없이 현재 방향으로 나간다.
                    if (_link.ThrowFlipDelay > 0f && _phaseTimer >= _link.ThrowFlipDelay && _throwBackward)
                    {
                        PerformBackwardThrowFlip();
                        _throwFlipDone = true;
                    }
                }

                // 시간 기반 타이밍 (GrabAttackLink.AttachTiming / ThrowTiming = TimeFromGrabStart, 2026-09-07).
                // 애니 이벤트 대신 잡기 시작 후 지정 초(실시간)에 발동 — 인스펙터에서 바로 조정. 이벤트 모드면 여기선 아무것도 안 한다.
                if (_link != null)
                {
                    if (!_attachActive && _link.AttachVictimToWeapon
                        && _link.AttachTiming == GrabTimingMode.TimeFromGrabStart
                        && _phaseTimer >= _link.AttachTime)
                        BeginWeaponAttachment();

                    if (victim != null && !_takedownDone && _link.EnableThrow
                        && _link.ThrowTiming == GrabTimingMode.TimeFromGrabStart
                        && _phaseTimer >= _link.ThrowTime)
                        PerformThrow();
                }
            }

            // 플립 후 전방 이동 상쇄(_travelFreeze)는 LateUpdate에서 적용 — 피격자 해제 후에도 GrabFinish까지 유지.

            if (victim != null)
            {
                // Takedown 슬라이드 (ease-out — 구버전 BeginTakedownSlide).
                if (_sliding)
                {
                    _slideTimer += dt;
                    float t = takedownSlideDuration > 0f ? Mathf.Clamp01(_slideTimer / takedownSlideDuration) : 1f;
                    float eased = 1f - (1f - t) * (1f - t);
                    victim.transform.position = Vector3.Lerp(_slideFrom, _slideTo, eased);
                    if (t >= 1f) _sliding = false;
                }

                // Takedown 이벤트 유실 폴백 (던지기 잡기는 슬램 없음 — 구버전 launchMode 스킵).
                if (!_takedownDone && _link != null && !_link.EnableThrow && _phaseTimer >= _link.TakedownFallbackTime)
                    HandleTakedown();
            }

            // 예약된 랜딩 이펙트 발화.
            if (_landingPending && _phaseTimer >= _landingAt)
                FireLandingFeedback();

            // 지연 예약된 Grab Visual / 연출(feedbackDelay) 발화.
            TickPendingGrabVisuals();
            TickPendingGrabFeedbacks();

            // 종료: GrabFinish 이벤트(_endRequested) 또는 안전 타임아웃.
            if (_endRequested || (_link != null && _phaseTimer >= _link.SafetyTimeout))
                EndGrab();
        }

        // ─────────── 애니메이션 이벤트 ───────────

        private void OnPlayerAnimEvent(string eventName)
        {
            if (phase != Phase.Grabbing || _link == null) return;
            switch (eventName)
            {
                case "Grab":
                    if (!string.IsNullOrEmpty(_link.GrabFeedbackKey) && victim != null)
                        Aiara.FeedbackManager.PlayFeedbackAtWorld(
                            _link.GrabFeedbackKey, victim.transform.position + Vector3.up, Quaternion.identity);
                    FireGrabVisuals(GrabVisualTrigger.GrabEvent);
                    // 박힘 시점이 시간 기반(TimeFromGrabStart)이면 이벤트는 무시 — TickGrabbing이 발동.
                    if (_link.AttachTiming == GrabTimingMode.AnimationEvent)
                        BeginWeaponAttachment();
                    break;
                case "GrabFinish":
                    _endRequested = true;
                    break;
                case "HeavyGrabThrow":
                    // 던지는 시점이 시간 기반이면 이벤트는 무시 — TickGrabbing이 발동.
                    if (_link.ThrowTiming == GrabTimingMode.AnimationEvent)
                        PerformThrow();
                    break;
                default:
                    // 그 외 이벤트는 피격자 Cinematics 이펙트 매핑에 위임 (데이터 주도).
                    if (_victimCinematics != null && victim != null)
                        _victimCinematics.TryPlayEventEffect(eventName, victim.transform.position);
                    break;
            }
        }

        private void OnVictimAnimEvent(string eventName)
        {
            if (phase != Phase.Grabbing) return;
            if (eventName == "Takedown" && (_link == null || !_link.EnableThrow)) // 던지기 잡기는 슬램 생략 (구버전 launchMode)
                HandleTakedown();
        }

        /// <summary>현재 Facing 기준 뒤 방향키(또는 아래키)가 눌려 있는가 — 방향 모니터와 같은 판정(임계 0.3).</summary>
        private bool IsBackKeyHeld()
        {
            if (_character == null || _character.CommandManager == null) return false;
            Vector2 mv = _character.CommandManager.MoveInput;
            const float threshold = 0.3f;
            bool facingRight = Movement == null || Movement.FacingRight;
            bool backKey = facingRight ? mv.x < -threshold : mv.x > threshold;
            bool fwdKey = facingRight ? mv.x > threshold : mv.x < -threshold;
            bool backward = (mv.y < -threshold) || backKey;
            bool forward = (mv.y > threshold) || fwdKey;
            return backward && !forward;
        }

        /// <summary>잡는 중 방향키로 앞/뒤 던지기 결정 — 뒤/아래=뒤로, 앞/위=앞으로, 중립·동시=직전 유지 (구버전 UpdateHeavyThrowDirectionMonitor).</summary>
        private void UpdateThrowDirectionMonitor()
        {
            if (_character.CommandManager == null) return;
            Vector2 mv = _character.CommandManager.MoveInput;
            const float threshold = 0.3f;
            bool facingRight = Movement != null && Movement.FacingRight;
            bool backKey = facingRight ? mv.x < -threshold : mv.x > threshold;
            bool fwdKey = facingRight ? mv.x > threshold : mv.x < -threshold;
            bool backward = (mv.y < -threshold) || backKey;
            bool forward = (mv.y > threshold) || fwdKey;
            if (backward && !forward) _throwBackward = true;
            else if (forward && !backward) _throwBackward = false;
#if UNITY_EDITOR
            if (_phaseTimer - Time.deltaTime < 0.25f && _phaseTimer >= 0.25f || _phaseTimer - Time.deltaTime < 0.5f && _phaseTimer >= 0.5f || _phaseTimer - Time.deltaTime < 1f && _phaseTimer >= 1f)
                Debug.Log($"[CharacterActionGrab] DirMonitor t={_phaseTimer:F2} link={_link.gameObject.name} mv={mv} facingRight={facingRight} back={backward} fwd={forward} throwBackward={_throwBackward} flipDelay={_link.ThrowFlipDelay}", this);
#endif
            // 중립/동시 입력 = 직전 값 유지
        }

        /// <summary>
        /// 뒤로 던지기 플립 — 시전자 방향 반전 + 루트 보정 + 카메라 전환 (구버전 PerformBackwardThrowFlip 완전 이식).
        /// 연출 이동이 본(bone)으로 이뤄지므로 flip만 하면 시각 포즈가 루트 기준으로 미러돼
        /// 화면상 순간이동처럼 보인다 → 루트를 시각 위치의 거울점으로 옮겨 화면 위치를 유지한다.
        /// 호출 시점: throwFlipDelay > 0이면 잡기 시작 후 그 시점(TickGrabbing), 아니면 HeavyGrabThrow 이벤트(PerformThrow).
        /// </summary>
        private void PerformBackwardThrowFlip()
        {
            if (Movement == null) return;
#if UNITY_EDITOR
            Debug.Log($"[CharacterActionGrab] BackwardThrowFlip t={_phaseTimer:F2}s link={(_link != null ? _link.gameObject.name : "null")} facingRight={Movement.FacingRight}→{!Movement.FacingRight}", this);
#endif
            Vector3 fwd0 = FacingWorld(); // flip 전 전방
            Vector3 selfVis = SelfVisualGroundPosition();
            float d = Vector3.Dot(selfVis - transform.position, fwd0);
            Vector3 newRoot = transform.position + fwd0 * (2f * d);

            bool wasLocked = Movement.FacingLocked;
            Movement.FacingLocked = false;
            Movement.Teleport(newRoot, !Movement.FacingRight);
            Movement.FacingLocked = wasLocked;
            RefreshActiveGrabVisualFacing(); // 재생 중인(비지속) Grab Visual도 새 방향으로 반전

            // 피격자도 시전자 몸(시각 위치)을 기준으로 전방축 거울점으로 옮기고 Facing을 반전한다 (2026-09-06).
            // 무기 부착 중이면 앵커 행렬이 미러되므로 LateUpdate 부착 추적이 알아서 따라간다 — 여기선 건드리지 않는다.
            if (victim != null && !_attachActive)
                MirrorVictimAcross(selfVis, fwd0);
            Physics.SyncTransforms();

            // 플립 이후 모션의 남은 본 변위가 새 방향으로 재생되면 플립한 자리에서 멀리 끌려간다 (ActionGrab_A처럼 모델이 크게 움직이는 모션).
            // 플립 순간 시각 위치를 출발점으로, 몸을 '출발점 + 새 전방 × FlipForwardDistance'에 고정한다 (FlipForwardDuration 동안 이동) (2026-09-06).
            _travelFreeze = true;
            _travelRef = SelfVisualGroundPosition();
            _travelTimer = 0f;

            // 진행 중인 잡기 카메라가 있으면 새 방향의 좌/우 시퀀스로 자연 전환(진입 블렌드 사용).
            if (_camera != null && _victimCinematics != null)
            {
                _camera.Stop();
                _camera = _victimCinematics.ResolveGrabCamera(_grabType, Movement.FacingRight);
                _camera?.Play();
            }
        }

        /// <summary>
        /// 피격자를 시전자 몸(pivot, 지면 시각 위치) 기준 전방축(fwd)의 거울점으로 이동 + Facing 반전.
        /// 잡힘 모션이 본 변위라 루트가 아닌 '보이는 몸' 위치가 대칭이 되도록, 시각 오프셋(루트→힙)을 반전해 루트를 역산한다.
        /// </summary>
        private void MirrorVictimAcross(Vector3 pivot, Vector3 fwd)
        {
            if (victim == null) return;
            Vector3 root = victim.transform.position;
            Vector3 vis = VictimVisualGroundPosition();
            Vector3 visOffset = vis - root;

            Vector3 rel = vis - pivot;
            Vector3 mirroredVis = vis - 2f * Vector3.Dot(rel, fwd) * fwd;
            Vector3 mirroredOffset = visOffset - 2f * Vector3.Dot(visOffset, fwd) * fwd;
            Vector3 newRoot = mirroredVis - mirroredOffset;
            newRoot.y = root.y;

            var vm = victim.Movement;
            if (vm != null)
            {
                bool wasLocked = vm.FacingLocked;
                vm.FacingLocked = false;
                vm.Teleport(newRoot, !vm.FacingRight);
                vm.FacingLocked = wasLocked;
            }
            else victim.transform.position = newRoot;

            // 진행 중 슬램 슬라이드가 있으면 목표도 새 전방 기준으로 재계산.
            if (_sliding && _link != null)
            {
                _slideFrom = victim.transform.position;
                _slideTo = transform.position + FacingWorld() * _link.SlamForwardDistance;
                _slideTo.y = _slideFrom.y;
            }
#if UNITY_EDITOR
            Debug.Log($"[CharacterActionGrab] MirrorVictim root {root}→{newRoot} vis {vis}→{mirroredVis}", this);
#endif
        }

        // ─────────── 루트 위치 보정 (플립 후 이동 상쇄 / 종료 정착) ───────────

        /// <summary>
        /// 플립 후 위치 고정 — 시전자 시각 지면 위치를 '플립 지점(_travelRef) + 새 전방 × FlipForwardDistance'에 맞춘다
        /// (FlipForwardDuration 동안 ease-out으로 이동, 이후 고정). 전방축 성분만 보정하며 모션의 본 변위는 그만큼 상쇄된다.
        /// 피격자(무기 부착 중이 아닐 때)와 진행 중 슬라이드 목표도 같은 양만큼 옮겨 동기를 유지한다.
        /// LateUpdate(Animator 평가 후)에서 호출 — 같은 프레임에 보정돼 렌더에 지연 스냅이 없다.
        /// </summary>
        private void ApplyTravelFreeze(float dt)
        {
            Vector3 axis = FacingWorld(); // 플립 이후의 새 전방
            axis.y = 0f;
            if (axis.sqrMagnitude < 0.0001f) return;
            axis.Normalize();

            _travelTimer += dt;
            float dist = _link != null ? _link.FlipForwardDistance : 0f;
            float dur = _link != null ? _link.FlipForwardDuration : 0f;
            float t = dur > 0f ? Mathf.Clamp01(_travelTimer / dur) : 1f;
            float eased = 1f - (1f - t) * (1f - t);
            Vector3 target = _travelRef + axis * (dist * eased);

            Vector3 delta = SelfVisualGroundPosition() - target;
            delta.y = 0f;
            float excess = Vector3.Dot(delta, axis);
            if (Mathf.Abs(excess) < 0.0005f) return;

            Vector3 shift = -axis * excess;
            ShiftRoot(shift);
            if (victim != null && !_attachActive)
                victim.transform.position += shift;
            if (_sliding) { _slideFrom += shift; _slideTo += shift; }
            if (_landingPending) _landingFallbackPos += shift;
            Physics.SyncTransforms();
        }

        /// <summary>
        /// 종료 정착 시작 — 현재 시각 지면 위치(+ EndForwardDistance)를 목표로 삼고, 복귀 블렌드 동안
        /// 본이 루트로 돌아가는 만큼 루트를 반대로 옮겨 화면상 몸이 그 자리에 머물게 한다.
        /// endSettleDuration이 0이면 루트만 시각 위치로 1회 스냅.
        /// </summary>
        private void BeginEndSettle()
        {
            Vector3 target = SelfVisualGroundPosition();
            if (_link != null && Mathf.Abs(_link.EndForwardDistance) > 0.001f)
                target += FacingWorld() * _link.EndForwardDistance;

            if (endSettleDuration <= 0f)
            {
                Vector3 snap = target;
                snap.y = transform.position.y;
                ShiftRoot(snap - transform.position);
                Physics.SyncTransforms();
                return;
            }

            _settling = true;
            _settleTimer = 0f;
            _settleAttackAt = -1f;
            _settleVisRef = target;
            _settleLastRoot = transform.position;
            // 추가 오프셋만큼은 즉시 반영 (블렌드 상쇄는 시각 위치 기준으로만).
            Vector3 extra = target - SelfVisualGroundPosition();
            if (extra.sqrMagnitude > 0.000001f)
            {
                ShiftRoot(extra);
                _settleLastRoot = transform.position;
                Physics.SyncTransforms();
            }
        }

        /// <summary>
        /// 정착 진행 — 루트가 다른 이유(이동 입력 등)로 움직인 양은 기준에 더하고, 그 외(본 복귀) 변위만 상쇄한다.
        /// 지상 이동 상태를 벗어나거나(피격/점프/새 그랩) 시간이 끝나면 중단.
        /// LateUpdate(Animator 평가 후)에서 호출 — ReturnMovement 전이가 즉시(duration 0)라 본이 한 번에 루트로 돌아가도
        /// 같은 프레임에 루트가 따라가므로 화면상 스냅이 없다.
        /// </summary>
        private void TickSettle(float dt)
        {
            _settleTimer += dt;
            var sm = _character.StateManager;
            var st = sm != null ? sm.CurrentStateType : CharacterStateType.Idle;
            bool grounded = st == CharacterStateType.Idle || st == CharacterStateType.Move || st == CharacterStateType.Run;
            // 종료 직후 버퍼된 공격이 바로 소비되면(잡기 중 약공격 연타) 지상 상태를 한 프레임도 못 거치고 Attack으로 넘어가
            // 정착이 시작도 못 하고 끊겨 본 복귀가 상쇄되지 않았다 → 몸이 잡기 전 루트 자리로 되돌아감 (2026-09-14).
            // Attack이면 진입 후 짧은 유예(공격 CrossFade 구간)만큼은 정착을 이어가고, 그 뒤엔 공격 모션의 본 변위를 건드리지 않도록 멈춘다.
            if (st == CharacterStateType.Attack)
            {
                if (_settleAttackAt < 0f) _settleAttackAt = _settleTimer;
                grounded = _settleTimer - _settleAttackAt <= SettleAttackGrace;
            }
            if (!grounded || phase != Phase.None || _settleTimer > endSettleDuration)
            {
                _settling = false;
                return;
            }

            Vector3 rootMoved = transform.position - _settleLastRoot;
            rootMoved.y = 0f;
            _settleVisRef += rootMoved;

            Vector3 delta = SelfVisualGroundPosition() - _settleVisRef;
            delta.y = 0f;
            if (delta.sqrMagnitude > 0.000001f)
            {
                ShiftRoot(-delta);
                Physics.SyncTransforms();
            }
            _settleLastRoot = transform.position;
        }

        /// <summary>시전자 루트를 이동(속도/Facing은 건드리지 않음). CharacterController가 되돌리지 않도록 잠시 비활성.</summary>
        private void ShiftRoot(Vector3 shift)
        {
            if (shift.sqrMagnitude < 0.0000001f) return;
            if (_controller == null) _controller = GetComponent<CharacterController>();
            bool wasEnabled = _controller != null && _controller.enabled;
            if (_controller != null) _controller.enabled = false;
            transform.position += shift;
            if (_controller != null) _controller.enabled = wasEnabled;
        }

        // ─────────── 모션 배속 (GrabAttackLink.AnimationSpeed) ───────────

        private static float BaseAnimSpeed(Character c)
            => c != null && c.Stats != null ? c.Stats.FinalGlobalAnimatorSpeed : 1f;

        private void ApplyAnimationSpeed(GrabAttackLink link, EnemyCharacter target)
        {
            _speedApplied = false;
            if (link == null) return;
            if (_casterAnimator == null) _casterAnimator = GetComponentInChildren<Animator>(true);
            _victimSpeedAnimator = target != null ? target.GetComponentInChildren<Animator>(true) : null;
            _grabSpeedMult = link.EvaluateSpeedSegment(0f);
            if (_casterAnimator != null) _casterAnimator.speed = BaseAnimSpeed(_character) * link.AnimationSpeed * _grabSpeedMult;
            if (_victimSpeedAnimator != null) _victimSpeedAnimator.speed = BaseAnimSpeed(target) * link.VictimAnimationSpeed * _grabSpeedMult;
            _speedApplied = true;
        }

        /// <summary>
        /// 구간 배속 매 틱 적용 (2026-09-15) — 시전자 = 전역 × AnimationSpeed × 구간배율, 피격자 = 전역 × VictimAnimationSpeed × 구간배율.
        /// 피격자 포즈 정지(speed=0) 중이거나 이미 해제(복원)된 피격자는 건드리지 않는다.
        /// </summary>
        private void ApplySegmentAnimatorSpeed()
        {
            if (!_speedApplied || _link == null) return;
            if (_casterAnimator != null) _casterAnimator.speed = BaseAnimSpeed(_character) * _link.AnimationSpeed * _grabSpeedMult;
            if (_victimSpeedAnimator != null && !_victimPoseFrozen && victim != null)
                _victimSpeedAnimator.speed = BaseAnimSpeed(victim) * _link.VictimAnimationSpeed * _grabSpeedMult;
        }

        /// <summary>피격자 Animator.speed만 기본값으로 복원 — 피격자 해제(던지기/넉다운/복구) 시점에 호출.</summary>
        private void RestoreVictimAnimationSpeed(Character victimRef)
        {
            if (_victimSpeedAnimator != null) _victimSpeedAnimator.speed = BaseAnimSpeed(victimRef);
            _victimSpeedAnimator = null;
        }

        /// <summary>시전자/피격자 Animator.speed를 기본값으로 복원. 모든 종료 경로(FinishSequence)에서 호출.</summary>
        private void RestoreAnimationSpeed(Character victimRef)
        {
            RestoreVictimAnimationSpeed(victimRef);
            if (!_speedApplied) return;
            _speedApplied = false;
            if (_casterAnimator != null) _casterAnimator.speed = BaseAnimSpeed(_character);
        }

        // ─────────── 무기 부착 (칼 박힘 — 구버전 SyncHeavyGrabAttachment) ───────────

        /// <summary>
        /// 시전자 Grab 이벤트(칼이 꽂히는 순간)부터 피격자 부착 추적을 시작한다 (구버전 BeginHeavyGrabAttachment).
        /// 피격자는 히트 리액션 포즈를 재생하고 지연 후 애니메이터를 정지해 '칼에 박힌' 모습을 유지한다.
        /// </summary>
        private void BeginWeaponAttachment()
        {
            if (_link == null || !_link.AttachVictimToWeapon || _weaponAnchor == null || victim == null) return;
            _attachActive = true;
            _attachOffsetCaptured = false;
            FireGrabVisuals(GrabVisualTrigger.Impale);

            // 부착 해제 시 복원할 루트 회전 (칼 각도를 따라 기울어지므로 반드시 복원).
            _victimPreAttachRotation = victim.transform.rotation;
            _victimRotationCaptured = true;

            // 꼽히는 순간 이펙트 — 데이터 지정 키 (비우면 생략).
            if (!string.IsNullOrEmpty(_link.AttachImpaleFeedbackKey))
                Aiara.FeedbackManager.PlayFeedbackAtWorld(
                    _link.AttachImpaleFeedbackKey, victim.transform.position + Vector3.up, Quaternion.identity);

            // 박힐 때 피격자 모션 — GrabAttackLink.AttachVictimMotion 선택 (2026-09-07). 이후 지연 후 정지 (구버전 FreezeHeavyGrabHitPose).
            _victimAnimDone = true; // 예약된 ActionGrabbed 시작 취소 (선택이 ActionGrabbed면 아래서 즉시 재생)
            PlayVictimMotion(_link.AttachVictimMotion);
            if (_link.AttachPoseFreezeDelay >= 0f)
                _poseFreezeAt = _phaseTimer + _link.AttachPoseFreezeDelay; // 잡기 타임라인 시각
#if UNITY_EDITOR
            LogTiming("Impale");
#endif
        }

#if UNITY_EDITOR
        /// <summary>타이밍 진단 — 잡기 시작 후 경과, 시전자 Animator 배속/상태 진행도, timeScale. 던지기 시점이 매번 다를 때 원인 분리용.</summary>
        private void LogTiming(string tag)
        {
            float spd = _casterAnimator != null ? _casterAnimator.speed : -1f;
            float nt = -1f;
            if (_casterAnimator != null)
            {
                var st = _casterAnimator.GetCurrentAnimatorStateInfo(0);
                nt = st.normalizedTime;
            }
            float atk = _character != null && _character.Stats != null ? _character.Stats.AttackSpeedMultiplier : -1f;
            Debug.Log($"[CharacterActionGrab] {tag} t={_phaseTimer:F3}s realtime={Time.unscaledTime:F2} animSpeed={spd:F3} (global={BaseAnimSpeed(_character):F2} atkMult={atk:F2} link={(_link != null ? _link.AnimationSpeed : 1f):F2}) stateNT={nt:F3} timeScale={Time.timeScale:F2}", this);
        }
#endif

        /// <summary>피격자 모션 재생 — 데이터(GrabVictimHitMotion) 선택. 박히기 전(Begin)과 박히는 순간(BeginWeaponAttachment)에서 호출.</summary>
        private void PlayVictimMotion(GrabVictimHitMotion motion)
        {
            if (victim == null || _link == null) return;
            switch (motion)
            {
                case GrabVictimHitMotion.DamagedBackward:
                    if (victim.Combat != null) victim.Combat.PlayHitReaction(forward: false);
                    break;
                case GrabVictimHitMotion.DamagedForward:
                    if (victim.Combat != null) victim.Combat.PlayHitReaction(forward: true);
                    break;
                case GrabVictimHitMotion.ActionGrabbed:
                    if (victim.StateManager != null) victim.StateManager.PlayActionGrabbedAnimation();
                    break;
                case GrabVictimHitMotion.AnimatorState:
                    if (victim.StateManager != null && !string.IsNullOrEmpty(_link.AttachVictimStateName))
                        victim.StateManager.CrossFadeState(_link.AttachVictimStateName, _link.AttachVictimCrossFade);
                    else if (victim.Combat != null) victim.Combat.PlayHitReaction(); // 상태명 비었으면 기본 경직
                    break;
                case GrabVictimHitMotion.None:
                    break;
            }
        }

        /// <summary>
        /// 애니메이션으로 움직이는 무기 본을 LateUpdate에서 추적 — 잡기 연출 내내 칼에서 이탈하지 않는다.
        /// AnimationEvent는 Animator 평가 도중 호출되므로, 애니 계산이 끝난 첫 LateUpdate에서는
        /// 상대 오프셋만 저장하고 이동하지 않는다 (첫 프레임 스냅 방지 — 구버전 주석 그대로).
        /// </summary>
        private void LateUpdate()
        {
            // 루트 위치 보정은 Animator 평가 후(같은 프레임)에 — Update에서 하면 본 스냅이 한 프레임 먼저 렌더된다.
            float dt = Time.deltaTime;
            if (dt > 0f && _character != null)
            {
                if (_settling) TickSettle(dt);
                if (phase == Phase.Grabbing && _travelFreeze) ApplyTravelFreeze(dt);
            }

            if (phase != Phase.Grabbing || !_attachActive || _weaponAnchor == null || victim == null) return;

            // Flip(visualRoot 음수 스케일 = 미러)이면 Transform.rotation은 반사를 모르는 '회전 체인'이라 좌/우에서 축이 어긋난다
            // (왼쪽 기준으로 맞춘 오프셋이 오른쪽에서 이상해지던 원인). 위치는 앵커 전체 행렬(TransformPoint/InverseTransformPoint)로,
            // 회전은 미러가 반영된 앵커 축(TransformVector)으로 만든 '유효 회전'(AnchorEffectiveRotation)으로 다뤄 양쪽이 대칭이 되게 한다 (2026-09-08).
            Quaternion effRot = AnchorEffectiveRotation();
            if (!_attachOffsetCaptured)
            {
                _attachRotOffset = Quaternion.Inverse(effRot) * victim.transform.rotation;
                _attachRootOffset = _weaponAnchor.InverseTransformPoint(victim.transform.position); // 앵커 로컬(스케일 포함) — 재적용 시 TransformPoint로 상쇄
                _attachOffsetCaptured = true;
#if UNITY_EDITOR
                Debug.Log($"[CharacterActionGrab] AttachCapture mirrored={AnchorIsMirrored()} facingRight={(Movement != null && Movement.FacingRight)} effRot={effRot.eulerAngles} anchorRot={_weaponAnchor.rotation.eulerAngles} victimRot={victim.transform.rotation.eulerAngles} rootOffset={_attachRootOffset}", this);
#endif
                return;
            }

            // attachOffset: 앵커 로컬 축 기준 추가 오프셋(m) — 칼 회전을 따라 같이 돌고, Flip 시 자동 미러링.
            victim.transform.position = _weaponAnchor.TransformPoint(_attachRootOffset) + AttachOffsetWorld();
            // attachRotationOffset: 앵커 로컬 축 기준 추가 회전 — 박힌 몸의 기울기 조정 (2026-09-07).
            // 미러(왼쪽)면 y/z 각을 반전해야 오른쪽의 정확한 거울상이 된다 (반사 R을 회전 E'=R·X로 근사했으므로 X·Euler·X 보정).
            Vector3 rotOff = _link != null ? _link.AttachRotationOffset : Vector3.zero;
            if (rotOff != Vector3.zero && AnchorIsMirrored()) { rotOff.y = -rotOff.y; rotOff.z = -rotOff.z; }
            victim.transform.rotation = rotOff == Vector3.zero
                ? effRot * _attachRotOffset
                : effRot * Quaternion.Euler(rotOff) * _attachRotOffset;

            // 지연 후 히트 포즈 정지 (구버전 HeavyGrabHitPoseFreezeDelay).
            if (!_victimPoseFrozen && _poseFreezeAt >= 0f && _phaseTimer >= _poseFreezeAt && _victimAnimator != null)
            {
                _victimAnimator.speed = 0f;
                _victimPoseFrozen = true;
            }
        }

        /// <summary>
        /// AttachOffset(앵커 로컬)을 월드 벡터로 — 앵커 전체 행렬로 변환해 Flip(음수 스케일) 미러를 반영하되,
        /// 스케일 크기는 축별로 정규화해 순수 방향/반사만 남긴다.
        /// </summary>
        /// <summary>
        /// 무기 앵커의 '유효 회전' — 미러(음수 스케일)가 반영된 월드 전방/상향 축(TransformVector)으로 만든 정규 회전.
        /// 반사는 쿼터니언으로 표현할 수 없으므로 forward/up을 고정하고 right는 외적으로 유도한다(좌/우 대칭 보장).
        /// 오프셋 캡처와 재적용 모두 이 회전을 쓰면 일관된다.
        /// </summary>
        private Quaternion AnchorEffectiveRotation()
        {
            if (_weaponAnchor == null) return Quaternion.identity;
            Vector3 f = _weaponAnchor.TransformVector(Vector3.forward);
            Vector3 u = _weaponAnchor.TransformVector(Vector3.up);
            if (f.sqrMagnitude < 1e-8f || u.sqrMagnitude < 1e-8f) return _weaponAnchor.rotation;
            return Quaternion.LookRotation(f.normalized, u.normalized);
        }

        /// <summary>무기 앵커 월드 행렬이 반사(음수 스케일 Flip)인가 — 축 3개의 손방향(det) 검사.</summary>
        private bool AnchorIsMirrored()
        {
            if (_weaponAnchor == null) return false;
            Vector3 r = _weaponAnchor.TransformVector(Vector3.right);
            Vector3 u = _weaponAnchor.TransformVector(Vector3.up);
            Vector3 f = _weaponAnchor.TransformVector(Vector3.forward);
            return Vector3.Dot(Vector3.Cross(r, u), f) < 0f;
        }

        private Vector3 AttachOffsetWorld()
        {
            Vector3 local = _link != null ? _link.AttachOffset : Vector3.zero;
            if (local == Vector3.zero || _weaponAnchor == null) return Vector3.zero;
            Vector3 ls = _weaponAnchor.lossyScale;
            const float eps = 0.0001f;
            local.x /= Mathf.Max(Mathf.Abs(ls.x), eps);
            local.y /= Mathf.Max(Mathf.Abs(ls.y), eps);
            local.z /= Mathf.Max(Mathf.Abs(ls.z), eps);
            return _weaponAnchor.TransformVector(local);
        }

        /// <summary>부착 종료 — 피격자 애니메이터 속도/루트 회전 복원. 모든 해제 경로에서 호출된다.</summary>
        private void ReleaseWeaponAttachment(EnemyCharacter v)
        {
            if (_victimPoseFrozen && _victimAnimator != null) _victimAnimator.speed = 1f;
            _victimPoseFrozen = false;
            _poseFreezeAt = -1f;
            if (_attachActive && _victimRotationCaptured && v != null)
                v.transform.rotation = _victimPreAttachRotation;
            _attachActive = false;
            _attachOffsetCaptured = false;
            _victimRotationCaptured = false;
            _victimAnimator = null;
        }

        /// <summary>
        /// HeavyGrabThrow 이벤트 — 피격자를 조기 해제하고 에어본으로 던진다 (구버전 ReleaseHeavyGrabEarly + ReleaseHeavyGrabThrown).
        /// 데미지는 이 시점에 적용되며 이후 Takedown/기상 시퀀스는 에어본 파이프라인(Airborne→Collapse→Getup)이 소유한다.
        /// </summary>
        private void PerformThrow()
        {
            if (phase != Phase.Grabbing || victim == null || _link == null || !_link.EnableThrow) return;
            _takedownDone = true;
            _sliding = false;
            FireGrabVisuals(GrabVisualTrigger.Throw);

            // 뒤로 던지기 플립 — throwFlipDelay가 0 이하면 여기(던지는 순간)서, 아니면 TickGrabbing이 지정 시점에 이미 처리.
            if (!_throwFlipDone && _throwBackward && _link.AllowBackwardThrow)
                PerformBackwardThrowFlip();
            _throwFlipDone = true;
            Vector3 dir = FacingWorld(); // 반전 이후 기준 — 항상 시전자가 바라보는 방향으로 던진다
#if UNITY_EDITOR
            Debug.Log($"[CharacterActionGrab] PerformThrow t={_phaseTimer:F2}s facingRight={(Movement != null ? Movement.FacingRight : true)} throwBackward={_throwBackward} dir={dir} victimRel={(victim.transform.position - transform.position)}", this);
            LogTiming("Throw");
#endif

            float power = _character.Stats != null
                ? _character.Stats.FinalAttackPower * _character.Stats.DamageOutputMultiplier : 10f;
            float damage = power * _link.DamageMultiplier;

            var v = victim;
            // 부착/포즈 정지 해제 + 루트 회전 복원 — 에어본은 반드시 직립 상태에서 시작.
            ReleaseWeaponAttachment(v);

            // 깊이(z) 정렬 — 부착 중 칼 궤적을 따라 깊이로 벗어난 위치를 시전자 깊이축으로 되돌려
            // 항상 플레이어 축 기준 정면으로 날아가게 한다 (발사 방향은 원래 깊이 0이지만 위치 오프셋이 남는 문제).
            var mv = Movement;
            if (mv != null)
            {
                Vector3 depth = mv.SplineDepth;
                if (depth.sqrMagnitude > 0.0001f)
                {
                    depth.Normalize();
                    Vector3 rel = v.transform.position - transform.position;
                    v.transform.position -= depth * Vector3.Dot(rel, depth);
                    Physics.SyncTransforms();
                }
            }
            // 제어/무적 해제 후 정식 피격 파이프라인으로 에어본 (구버전 Impact + EnterAirborneFromGrabThrow 대응).
            if (v.Combat != null) v.Combat.SetInvincible(false);
            RestoreVictimAnimationSpeed(v);
            v.SetGrabbed(false);
            if (_victimEvents != null)
            {
                _victimEvents.OnAnimationEvent -= OnVictimAnimEvent;
                _victimEvents = null;
            }

            var info = new DamageInfo(gameObject, damage, v.transform.position + Vector3.up, dir)
            {
                Unblockable = true,
                Reaction = _link.ThrowReaction,
                BodyImpactDamage = _link.ThrowBodyImpactMultiplier > 0f ? power * _link.ThrowBodyImpactMultiplier : 0f,
            };
            // 이펙트는 ReceiveDamage 정식 파이프라인(hitFeedbackKey)이 재생 — 수동 재생 시 중복(2회) 발생하므로 금지.
            v.ReceiveDamage(info);
            Combat?.NotifyAttackLanded(v);

            victim = null;
            _victimReleasedEarly = true; // 시전자는 GrabFinish까지 애니 유지, 피격자 복구 시퀀스는 생략
        }

        /// <summary>슬램 순간 — 데미지 적용 + 피격자를 슬램 위치로 슬라이드 + 넉다운 애니 (구버전 HandleTakedown).</summary>
        private void HandleTakedown()
        {
            if (_takedownDone || victim == null || _link == null) return;
            _takedownDone = true;
            FireGrabVisuals(GrabVisualTrigger.Takedown);

            // 뒤로 슬램 플립 — throwFlipDelay가 0 이하면 여기(바닥에 박히는 순간)서, 아니면 TickGrabbing이 지정 시점에 이미 처리.
            // 이후 슬램 슬라이드/Takedown 반응/종료 전진은 모두 FacingWorld() 기준이라 플립된 방향으로 나간다 (2026-09-06).
            if (!_throwFlipDone && _throwBackward && _link.AllowBackwardThrow)
                PerformBackwardThrowFlip();
            _throwFlipDone = true;

            // 데미지: 무적 상태의 피격자에게 직접 적용(연출 확정 피해 — 구버전 접촉 데미지 대응).
            float power = _character.Stats != null
                ? _character.Stats.FinalAttackPower * _character.Stats.DamageOutputMultiplier : 10f;
            float damage = power * _link.DamageMultiplier;
            // Takedown 반응(Kind != None): 데미지를 직접 적용하지 않고 조기 해제 + 정식 피격 파이프라인으로
            // 한 번에 전달한다(중복 적용 방지). 사망 처리도 ReceiveDamage가 소유.
            bool takedownReact = _link.TakedownReaction.Kind != HitReactionKind.None;
            if (!takedownReact)
            {
                victim.ApplyDamage(damage);
                Combat?.NotifyAttackLanded(victim);
                if (victim.CurrentHP <= 0f) _victimDied = true;
            }

            if (!string.IsNullOrEmpty(_link.TakedownFeedbackKey))
                Aiara.FeedbackManager.PlayFeedbackAtWorld(
                    _link.TakedownFeedbackKey, VictimVisualGroundPosition() + Vector3.up * 0.5f, Quaternion.identity);

            // 랜딩 이펙트 예약 — Takedown 이벤트 + 지연(takedownLandingDelay) 후 적 시각 위치 + 오프셋에서 재생.
            if (!string.IsNullOrEmpty(_link.TakedownLandingFeedbackKey))
            {
                _landingPending = true;
                _landingAt = _phaseTimer + Mathf.Max(0f, _link.TakedownLandingDelay); // 잡기 타임라인 시각
                _landingFallbackPos = VictimVisualGroundPosition();
                if (_link.TakedownLandingDelay <= 0f) FireLandingFeedback();
            }

            // Takedown 반응 — 슬라이드/그랩 종료 넉다운 대신, 몸이 바닥에 박히는 이 시점에
            // 조기 해제하고 설정된 반응(예: Slam+Bounce)을 적용한다 (PerformThrow의 조기 해제 패턴 재사용).
            if (takedownReact)
            {
                ReleaseVictimWithTakedownReaction(damage);
                return;
            }

            // 슬램 위치로 슬라이드 (시전자 전방 — 구버전 ComputeSlamTarget 축약).
            // TakedownAnchor=CasterVisual이면 시전자 루트가 아니라 모델(시각) 위치 기준 (2026-09-09).
            // 2026-09-09: VictimVisual + SlamForwardDistance=0 이면 슬라이드 생략 — '모델 자리로 루트 스냅'을 여기서 한 번,
            // ReleaseVictimToKnockdown에서 또 한 번 하면 본 변위가 이중 적용되어 적이 두 번 밀렸다. 루트 정착은 클립이
            // 바뀌는 종료 시점(ReleaseVictimToKnockdown) 한 곳에서만 수행한다.
            bool needSlide = _link.TakedownAnchor == TakedownAnchorMode.CasterVisual
                             || Mathf.Abs(_link.SlamForwardDistance) > 0.001f;
            if (!needSlide) { _sliding = false; return; }
            _slideFrom = victim.transform.position;
            _slideTo = ResolveTakedownGround();
            _slideTo.y = _slideFrom.y;
            _slideTimer = 0f;
            _sliding = true;
        }

        /// <summary>
        /// Takedown(슬램) 순간 반응 적용 — 피격자를 조기 해제하고 시각 위치(바닥에 박힌 지점)로 루트를 스냅한 뒤
        /// GrabAttackLink.TakedownReaction을 정식 피격 파이프라인으로 전달한다 (예: Slam+Bounce = 즉시 튕김).
        /// 그랩이 끝나기 전, 몸이 바닥에 박히는 시점에 반응이 시작된다.
        /// 시전자는 PerformThrow와 동일하게 GrabFinish까지 애니를 유지하고, 피격자 복구 시퀀스는 생략된다.
        /// </summary>
        private void ReleaseVictimWithTakedownReaction(float damage)
        {
            if (victim == null || _link == null) return;
            var v = victim;
            _sliding = false;

            // 부착/포즈 정지 해제 + 루트 회전 복원 — 에어본은 직립 상태에서 시작 (PerformThrow와 동일).
            ReleaseWeaponAttachment(v);

            // 잡힘 모션의 본 변위 보정 — 몸이 실제로 박힌 시각 지면 위치로 루트를 옮겨 스냅백 제거.
            // TakedownAnchor=CasterVisual이면 시전자 모델 위치 + SlamForwardDistance 지점에 놓는다 (2026-09-09).
            Vector3 ground = ResolveTakedownGround();
            v.transform.position = new Vector3(ground.x, v.transform.position.y, ground.z);
            Physics.SyncTransforms();

            // 제어/무적 해제 후 정식 피격 파이프라인으로 전달 (데미지·사망 처리 포함).
            if (v.Combat != null) v.Combat.SetInvincible(false);
            RestoreVictimAnimationSpeed(v);
            v.SetGrabbed(false);
            if (_victimEvents != null)
            {
                _victimEvents.OnAnimationEvent -= OnVictimAnimEvent;
                _victimEvents = null;
            }

            var info = new DamageInfo(gameObject, damage, ground + Vector3.up * 0.5f, FacingWorld())
            {
                Unblockable = true,
                Reaction = _link.TakedownReaction,
            };
            v.ReceiveDamage(info);
            Combat?.NotifyAttackLanded(v);

            victim = null;
            _victimReleasedEarly = true; // 시전자는 GrabFinish까지 애니 유지, 피격자 복구 시퀀스는 생략
        }

        // ─────────── 종료 / 복구 ───────────

        /// <summary>정상 종료 — 플레이어 복귀 + 피격자 기상 시퀀스 시작 (구버전 EndGrab + GrabRecovering).</summary>
        private void EndGrab()
        {
            _camera?.Stop();
            _camera = null;
            FireGrabVisuals(GrabVisualTrigger.GrabEnd);

            // 플레이어: 상태 복귀(Exit이 잠금 해제) 후 '종료 순간 모델이 서 있는 자리'에 루트를 정착시킨다
            // (고정 거리 스냅 → 시각 위치 기준, 2026-09-06). 정착은 복귀 블렌드 동안 이어진다(TickSettle).
            _travelFreeze = false;
            if (IsInGrabState())
            {
                var sm = _character.StateManager;
                sm.FireTrigger(AnimParams.ReturnMovement);
                sm.ChangeState(sm.ResolveGroundedStateByInput());
                if (Movement != null) BeginEndSettle();
            }

            // 피격자: 사망이면 즉시 복구+사망 처리, 생존이면 넉다운 상태로 해제 —
            // 넉다운 파이프라인(넘어짐→다운→기상)이 애니를 직접 재생(CrossFade)하므로
            // 잡힘 모션에 갇히는 문제가 없다 (기존 Recovering 타이머 방식 대체).
            if (victim == null) { FinishSequence(); return; }
            if (_victimDied)
            {
                RestoreVictim(dead: true);
                FinishSequence();
                return;
            }

            ReleaseVictimToKnockdown();
            FinishSequence();
        }

        /// <summary>
        /// 피격자 제어/무적 해제 후 그 자리에서 넉다운 상태로 전환 (밀림 없음).
        /// 이후 다운→기상→AI 복귀는 CharacterKnockdownState가 소유한다.
        /// </summary>
        private void ReleaseVictimToKnockdown()
        {
            if (victim == null) return;
            var v = victim;
            ReleaseWeaponAttachment(v);
            if (v.Combat != null) v.Combat.SetInvincible(false);

            // 종료 위치 보정 — 잡힘 모션의 본 변위로 벗어난 '모델이 실제 있는 자리'로 루트를 옮겨 스냅백 제거
            // (고정 거리 → 시각 위치 측정, 2026-09-06). VictimEndForwardOffset은 추가 미세 조정. CC 재활성화 전에 이동.
            Vector3 ground = VictimVisualGroundPosition();
            if (_link != null && Mathf.Abs(_link.VictimEndForwardOffset) > 0.001f)
                ground += FacingWorld() * _link.VictimEndForwardOffset;
            {
                Vector3 pos = new Vector3(ground.x, v.transform.position.y, ground.z);
                v.transform.position = pos;
                Physics.SyncTransforms();
            }

            RestoreVictimAnimationSpeed(v);
            v.SetGrabbed(false);
            if (_victimEvents != null)
            {
                _victimEvents.OnAnimationEvent -= OnVictimAnimEvent;
                _victimEvents = null;
            }

            var sm = v.StateManager;
            if (sm != null)
            {
                sm.SetPendingReaction(new CharacterStateManager.ReactionData
                {
                    Direction = FacingWorld(), // 시전자 기준 전방 — 넘어짐 방향 판정용 (밀림은 0)
                    Spec = new HitReactionSpec { Kind = HitReactionKind.Knockdown },
                    Attacker = gameObject,
                    StartDowned = true, // 슬램으로 이미 바닥에 있음 — 낙하 클립 없이 다운 포즈부터 (2026-09-09)
                });
                sm.ChangeState(CharacterStateType.Knockdown, allowReenter: true);

                // 포즈 전환 후 보정 — Knockdown(StartDowned)이 다운 포즈를 즉시 Play하면 힙 본이 잡힘 클립 변위에서
                // 다운 포즈 위치로 순간 이동한다. 새 포즈를 바로 평가(Update(0))해 힙이 실제로 놓인 자리를 재고,
                // 전환 전 힙 자리(ground)와의 차이만큼 루트를 되돌려 '보이는 몸'이 그 자리에 그대로 눕게 한다 (2026-09-09).
                var anim = v.GetComponentInChildren<Animator>();
                if (anim != null && anim.isActiveAndEnabled)
                {
                    anim.Update(0f);
                    Vector3 after = VictimVisualGroundPositionOf(v);
                    Vector3 fix = ground - after; fix.y = 0f;
                    if (fix.sqrMagnitude > 1e-6f)
                    {
                        v.transform.position += fix;
                        Physics.SyncTransforms();
                    }
                }
            }
            victim = null;
        }

        /// <summary>피격자 제어/무적 해제 + 상태 복귀. 모든 종료 경로가 반드시 여기를 거친다.</summary>
        private void RestoreVictim(bool dead)
        {
            if (victim == null) return;
            ReleaseWeaponAttachment(victim);
            if (victim.Combat != null) victim.Combat.SetInvincible(false);
            RestoreVictimAnimationSpeed(victim);
            victim.SetGrabbed(false);
            if (victim.StateManager != null)
                victim.StateManager.ChangeState(dead || victim.IsDead
                    ? CharacterStateType.Dead : CharacterStateType.Idle);
            if (_victimEvents != null)
            {
                _victimEvents.OnAnimationEvent -= OnVictimAnimEvent;
                _victimEvents = null;
            }
            victim = null;
        }

        private void FinishSequence()
        {
            ReleaseWeaponAttachment(victim); // 안전망 — 어떤 경로로 끝나도 부착/포즈 정지 잔류 금지
            RestoreAnimationSpeed(victim);   // 모션 배속 복원 — 시전자/피격자 Animator.speed 잔류 금지
            _weaponAnchor = null;
            // 잡기가 지연보다 먼저 끝나도 예약된 랜딩 이펙트는 발화(유실 방지).
            if (_landingPending) FireLandingFeedback();
            StopGrabVisuals(); // 비지속 VFX 종료 + AttackAction 루트 원상 복구(비활성)
            if (_victimEvents != null)
            {
                _victimEvents.OnAnimationEvent -= OnVictimAnimEvent;
                _victimEvents = null;
            }
            victim = null;
            _link = null;
            _victimPoints = null;
            _victimCinematics = null;
            _sliding = false;
            _travelFreeze = false;
            phase = Phase.None;
        }

        /// <summary>외부 강제 종료(피격/사망/비활성) — 피격자 즉시 복구. 상태는 이미 다른 소유자가 있으므로 건드리지 않는다.</summary>
        private void Cleanup(bool exitState)
        {
            _camera?.Stop();
            _camera = null;
            if (victim != null)
            {
                // 생존 피격자는 즉시 넉다운으로 해제(잠금 유출 방지) — 다운→기상은 넉다운 상태가 소유.
                bool dead = victim.IsDead || _victimDied;
                if (dead) RestoreVictim(dead: true);
                else ReleaseVictimToKnockdown();
            }
            FinishSequence();
        }

        // ─────────── Grab Visuals (GrabAttackLink.GrabVisuals, 2026-09-08) ───────────

        /// <summary>
        /// 잡기 진입 시 VFX 호스트 준비 — AttackAction.Stop()이 꺼 버린 프리팹 루트를 잡기 동안만 다시 켠다.
        /// 히트박스/코루틴은 Stop()에서 이미 정리됐고 Play()를 다시 부르지 않으므로 판정은 살아나지 않는다.
        /// </summary>
        private void BeginGrabVisuals(GrabAttackLink link)
        {
            _pendingGrabVisuals.Clear();
            _pendingGrabFeedbacks.Clear();
            _grabVisualHost = null;
            var list = link != null ? link.GrabVisuals : null;
            if (list == null || list.Count == 0) return;

            _grabVisualHost = link.gameObject;
            if (!_grabVisualHost.activeSelf) _grabVisualHost.SetActive(true);

            // 반전 컨테이너 = AttackAction Visual(없으면 프리팹 루트). 이펙트 target은 이 아래에 있어야 반전을 받는다.
            var action = link.GetComponent<AttackAction>();
            var visObj = action != null ? action.VisualObject : null;
            _grabMirrorRoot = visObj != null ? visObj.transform : link.transform;
            ApplyGrabVisualFacing();

            // 시작 전 target 전부 OFF — 프리팹에 켜진 채 남아 있으면 진입과 동시에 터진다 (지속 중인 것 제외).
            for (int i = 0; i < list.Count; i++)
            {
                var v = list[i];
                if (v == null || v.target == null || !v.target.activeSelf) continue;
                var r = v.target.GetComponent<DetachedVisualReturner>();
                if (r != null && r.IsDetached) continue;
                v.target.SetActive(false);
            }
        }

        /// <summary>트리거 시점 도달 — 해당 항목을 즉시 재생하거나 delay 뒤 발화하도록 예약. GrabEnd는 예약 없이 즉시(종료 직후 정리되므로).</summary>
        private void FireGrabVisuals(GrabVisualTrigger trigger)
        {
            if (_link == null || _grabVisualHost == null) return;
            var list = _link.GrabVisuals;
            for (int i = 0; i < list.Count; i++)
            {
                var v = list[i];
                if (v == null || v.target == null || v.trigger != trigger) continue;
                if (v.delay > 0f && trigger != GrabVisualTrigger.GrabEnd)
                    _pendingGrabVisuals.Add(new PendingGrabVisual { visual = v, fireAt = _phaseTimer + v.delay }); // 잡기 타임라인 시각
                else
                    PlayGrabVisual(v, forcePersist: trigger == GrabVisualTrigger.GrabEnd);
            }
        }

        private void TickPendingGrabVisuals()
        {
            for (int i = _pendingGrabVisuals.Count - 1; i >= 0; i--)
            {
                if (_phaseTimer < _pendingGrabVisuals[i].fireAt) continue;
                var v = _pendingGrabVisuals[i].visual;
                _pendingGrabVisuals.RemoveAt(i);
                PlayGrabVisual(v, forcePersist: false);
            }
        }

        /// <summary>AttackAction.PlayTimedVisual과 동일 규칙 — 회수→Off→On→파티클 Clear+Play, 지속이면 월드 고정 타이머.</summary>
        private void PlayGrabVisual(GrabVisual v, bool forcePersist)
        {
            var go = v.target;
            if (go == null) return;
            var pending = go.GetComponent<DetachedVisualReturner>();
            if (pending != null && pending.IsDetached) pending.ReturnHome();

            if (go.activeSelf) go.SetActive(false);
            go.SetActive(true);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { ps.Clear(true); ps.Play(true); }
            foreach (var trail in go.GetComponentsInChildren<TrailRenderer>(true)) trail.Clear();

            if (v.persistEffect || forcePersist)
            {
                var returner = go.GetComponent<DetachedVisualReturner>();
                if (returner == null) returner = go.AddComponent<DetachedVisualReturner>();
                returner.BeginDetach(v.persistDuration); // 방금 적용한 반전 포즈가 홈으로 기록됨
            }

            // 항목별 연출 — feedbackDelay만큼 미룬 뒤 발동 (잡기 타임라인 시간, 구간 배속 반영). 잡기가 먼저 끝나면 남은 시간은 게임 시간으로 진행 (StopGrabVisuals).
            if (v.feedbackDelay > 0f) _pendingGrabFeedbacks.Add(new PendingGrabVisual { visual = v, fireAt = _phaseTimer + v.feedbackDelay });
            else PlayGrabVisualFeedback(v);
        }

        private void TickPendingGrabFeedbacks()
        {
            for (int i = _pendingGrabFeedbacks.Count - 1; i >= 0; i--)
            {
                if (_phaseTimer < _pendingGrabFeedbacks[i].fireAt) continue;
                var v = _pendingGrabFeedbacks[i].visual;
                _pendingGrabFeedbacks.RemoveAt(i);
                PlayGrabVisualFeedback(v);
            }
        }

        /// <summary>잡기 종료 시 남은 feedbackDelay 예약을 게임 시간 코루틴으로 넘긴다 — 예약 유실 방지(기존 실시간 동작 유지).</summary>
        private void FlushPendingGrabFeedbacks()
        {
            for (int i = 0; i < _pendingGrabFeedbacks.Count; i++)
            {
                float remain = Mathf.Max(0f, _pendingGrabFeedbacks[i].fireAt - _phaseTimer);
                if (remain <= 0f) PlayGrabVisualFeedback(_pendingGrabFeedbacks[i].visual);
                else StartCoroutine(PlayGrabVisualFeedbackDelayed(_pendingGrabFeedbacks[i].visual, remain));
            }
            _pendingGrabFeedbacks.Clear();
        }

        private System.Collections.IEnumerator PlayGrabVisualFeedbackDelayed(GrabVisual v, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            PlayGrabVisualFeedback(v);
        }

        /// <summary>AttackAction Hit 연출과 같은 서비스 사용 (비가중: 동시 요청 중 가장 강한 것만).</summary>
        private static void PlayGrabVisualFeedback(GrabVisual v)
        {
            if (v.useSlowMotion && v.slowMotionScale < 1f)
                Aiara.FeedbackTime.SlowMotion(v.slowMotionScale, v.slowMotionDuration, v.slowMotionEaseIn, v.slowMotionEaseOut);
            if (v.useCameraShake)
                CameraShakeService.Shake(v.cameraShakeVelocity, v.cameraShakeDuration, v.cameraShakeFrequency, unscaledTime: true);
            if (v.useCameraZoom)
                CameraZoomService.Punch(v.cameraZoomFovDelta, v.cameraZoomEaseIn, v.cameraZoomHold, v.cameraZoomEaseOut);
            if (v.useRumble)
                Aiara.FeedbackRumble.Play(v.rumbleLowFrequency, v.rumbleHighFrequency, v.rumbleDuration);
        }

        /// <summary>
        /// Grab Visual 좌/우 반전 — AttackAction Visual 컨테이너의 스케일 부호를 캐릭터 Visual 루트와 같은 축으로 맞춘다.
        /// 기준: GrabAttackLink.VisualsAuthoredFacingLeft(false = 오른쪽 기준 배치 → 왼쪽 볼 때 반전). 절대값 대입이라 몇 번 불러도 같은 결과.
        /// 개별 이펙트 트랜스폼은 건드리지 않는다.
        /// </summary>
        private void ApplyGrabVisualFacing()
        {
            if (_grabMirrorRoot == null || _link == null) return;
            var m = Movement;
            bool right = m == null || m.FacingRight;
            bool mirror = _link.VisualsAuthoredFacingLeft ? right : !right;
            SetMirrorRootSign(mirror ? -1f : 1f);
        }

        private void SetMirrorRootSign(float sign)
        {
            if (_grabMirrorRoot == null) return;
            var m = Movement;
            Vector3 s = _grabMirrorRoot.localScale;
            var axis = m != null ? m.VisualFlipAxis : CharacterMovement.FlipAxis.Z;
            switch (axis)
            {
                case CharacterMovement.FlipAxis.X: s.x = Mathf.Abs(s.x) * sign; break;
                case CharacterMovement.FlipAxis.Y: s.y = Mathf.Abs(s.y) * sign; break;
                default: s.z = Mathf.Abs(s.z) * sign; break;
            }
            _grabMirrorRoot.localScale = s;
        }

        /// <summary>잡기 중 플립 — 컨테이너 부호를 새 방향으로. 지속(persist) 중인 이펙트는 DetachedVisualReturner가 부모 부호 변화를 스스로 보정한다.</summary>
        private void RefreshActiveGrabVisualFacing() => ApplyGrabVisualFacing();

        /// <summary>잡기 종료 — 비지속 VFX OFF + 예약 취소. 지속 중인 항목이 남아 있으면 끝날 때까지 기다렸다가 호스트를 끈다.</summary>
        private void StopGrabVisuals()
        {
            _pendingGrabVisuals.Clear();
            FlushPendingGrabFeedbacks();
            SetMirrorRootSign(1f); // 컨테이너 부호 복원 — 프리팹/레코더에 반전 상태가 남지 않게
            _grabMirrorRoot = null;
            var host = _grabVisualHost;
            _grabVisualHost = null;
            if (host == null) return;

            var link = host.GetComponent<GrabAttackLink>();
            var list = link != null ? link.GrabVisuals : null;
            bool anyDetached = false;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var v = list[i];
                    if (v == null || v.target == null) continue;
                    var r = v.target.GetComponent<DetachedVisualReturner>();
                    if (r != null && r.IsDetached) { anyDetached = true; continue; }
                    foreach (var ps in v.target.GetComponentsInChildren<ParticleSystem>(true))
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    foreach (var trail in v.target.GetComponentsInChildren<TrailRenderer>(true)) trail.Clear();
                    v.target.SetActive(false);
                }
            }

            // AttackAction이 이미 다음 스윙으로 다시 Play 중이면 건드리지 않는다.
            var action = host.GetComponent<AttackAction>();
            if (action != null && Combat != null && Combat.CurrentAction == action && Combat.IsAttacking) return;

            if (anyDetached) StartCoroutine(DeactivateHostWhenPersistDone(host, list));
            else host.SetActive(false);
        }

        private System.Collections.IEnumerator DeactivateHostWhenPersistDone(GameObject host, IReadOnlyList<GrabVisual> list)
        {
            while (host != null && host.activeSelf)
            {
                bool any = false;
                for (int i = 0; i < list.Count; i++)
                {
                    var v = list[i];
                    if (v == null || v.target == null) continue;
                    var r = v.target.GetComponent<DetachedVisualReturner>();
                    if (r != null && r.IsDetached) { any = true; break; }
                }
                if (!any) break;
                yield return null;
            }
            if (host == null || !host.activeSelf) yield break;
            // 그 사이 다음 스윙으로 재사용 중이면 그대로 둔다.
            var action = host.GetComponent<AttackAction>();
            if (action != null && Combat != null && Combat.CurrentAction == action && Combat.IsAttacking) yield break;
            host.SetActive(false);
        }

        /// <summary>예약된 랜딩 이펙트 재생 — 적 시각 위치(지면) + 오프셋(오른쪽 기준 스플라인 축, Flip 시 전방 반전).</summary>
        private void FireLandingFeedback()
        {
            _landingPending = false;
            if (_link == null || string.IsNullOrEmpty(_link.TakedownLandingFeedbackKey)) return;

            Vector3 pos = victim != null ? VictimVisualGroundPosition() : _landingFallbackPos;
            Vector3 off = _link.TakedownLandingOffset;
            if (off != Vector3.zero)
            {
                var m = Movement;
                Vector3 fwdAxis = m != null ? m.SplineForward : transform.forward;
                Vector3 depthAxis = m != null ? m.SplineDepth : Vector3.Cross(fwdAxis, Vector3.up).normalized;
                float sign = (m == null || m.FacingRight) ? 1f : -1f;
                pos += fwdAxis * (off.x * sign) + Vector3.up * off.y + depthAxis * off.z;
            }
            Aiara.FeedbackManager.PlayFeedbackAtWorld(_link.TakedownLandingFeedbackKey, pos, Quaternion.identity);
        }

        /// <summary>
        /// Takedown(슬램) 순간 피격자 루트가 놓일 지면 위치 (2026-09-09).
        /// VictimVisual: 피격자 모델 위치(잡힘 클립 기준, 기존 동작). CasterVisual: 시전자 모델 위치 + 전방 SlamForwardDistance.
        /// TakedownReaction=None 슬라이드 경로에서 VictimVisual이면 구버전대로 시전자 루트 + SlamForwardDistance.
        /// </summary>
        private Vector3 ResolveTakedownGround()
        {
            float offset = _link != null ? _link.SlamForwardDistance : 0f;
            bool casterAnchor = _link != null && _link.TakedownAnchor == TakedownAnchorMode.CasterVisual;
            // 기준점(시전자 몸 / 피격자 몸) + 전방 SlamForwardDistance. 시전자 루트 기준(구버전)은 폐기 —
            // 시전자 몸이 본 변위로 루트에서 3m 가까이 떨어지는 슬램에서 적이 엉뚱한 자리(루트 뒤)로 미끄러졌다 (2026-09-09).
            Vector3 anchor = casterAnchor ? SelfVisualGroundPosition() : VictimVisualGroundPosition();
            return anchor + FacingWorld() * offset;
        }

        // 시각 본 캐시 — CharacterVisualBone.Resolve 결과 (2026-09-09). 피격자는 대상이 바뀔 때마다 다시 찾는다.
        private Transform _selfVisualBone;
        private Transform _victimVisualBone;
        private EnemyCharacter _victimVisualBoneOwner;

        /// <summary>
        /// 시전자 자신의 시각(모델) 지면 위치 — 잡기 연출의 본 변위로 루트와 모델이 떨어진 정도를 측정
        /// (CharacterVisualBone: Humanoid Hips → 힙/골반 이름 본(jnt_hip) → 스킨 루트본 폴백, 높이는 루트 유지).
        /// 플립 거울점 보정·플립 후 이동 상쇄·종료 정착에 사용.
        /// 2026-09-09: 활성 메시 rootBone이 jnt_root(연출 중 고정)라 몸=루트로 오판 → 종료 정착이 본 복귀를 상쇄 못 해 위치가 튀던 문제 수정.
        /// </summary>
        private Vector3 SelfVisualGroundPosition()
        {
            if (_selfVisualBone == null) _selfVisualBone = CharacterVisualBone.Resolve(transform);
            return CharacterVisualBone.GroundPosition(transform, _selfVisualBone);
        }

        /// <summary>피격자의 시각(모델) 지면 위치 — 같은 근사. 피드백 위치·슬램 스냅·종료 위치에 사용.</summary>
        /// <summary>지정 대상의 시각(모델) 지면 위치 — 종료 보정처럼 victim 필드 해제 전후에 같은 대상을 재야 할 때 사용.</summary>
        private Vector3 VictimVisualGroundPositionOf(EnemyCharacter target)
        {
            if (target == null) return transform.position;
            if (_victimVisualBone == null || _victimVisualBoneOwner != target)
            {
                _victimVisualBone = CharacterVisualBone.Resolve(target.transform);
                _victimVisualBoneOwner = target;
            }
            return CharacterVisualBone.GroundPosition(target.transform, _victimVisualBone);
        }

        private Vector3 VictimVisualGroundPosition()
        {
            if (victim == null) return transform.position;
            if (_victimVisualBone == null || _victimVisualBoneOwner != victim)
            {
                _victimVisualBone = CharacterVisualBone.Resolve(victim.transform);
                _victimVisualBoneOwner = victim;
            }
            return CharacterVisualBone.GroundPosition(victim.transform, _victimVisualBone);
        }

        /// <summary>월드 위치를 시전자 루트 기준으로 Facing 반전 — 오른쪽이면 그대로, 왼쪽이면 스플라인 전방축 성분 반사.</summary>
        private Vector3 MirrorByFacing(Vector3 worldPos)
            => transform.position + MirrorVectorByFacing(worldPos - transform.position);

        /// <summary>월드 벡터의 스플라인 전방축 성분을 Facing에 따라 반사.</summary>
        private Vector3 MirrorVectorByFacing(Vector3 v)
        {
            var m = Movement;
            if (m == null || m.FacingRight) return v;
            Vector3 fwd = m.SplineForward;
            if (fwd.sqrMagnitude < 0.0001f) return v;
            fwd.Normalize();
            return v - 2f * Vector3.Dot(v, fwd) * fwd;
        }

        private Vector3 FacingWorld()
        {
            var m = Movement;
            if (m == null) return transform.forward;
            Vector3 f = m.FacingRight ? m.SplineForward : -m.SplineForward;
            return f.sqrMagnitude > 0.0001f ? f.normalized : transform.forward;
        }
    }
}
