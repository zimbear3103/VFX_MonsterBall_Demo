using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 행동 종료 시 '보이는 몸' 위치를 루트에 정착시키는 공용 컴포넌트 (2026-09-15).
    ///
    /// 이 프로젝트의 공격/점프/잡기 모션은 루트모션이 아니라 본 변위(jnt_hip 등)로 몸이 움직인다.
    /// 그래서 모션이 끝나고 다음 모션(로코모션·다음 공격·점프)으로 블렌드되면 본이 루트 자리로 돌아가면서
    /// 화면상 캐릭터가 "행동 전 원래 위치로 되돌아가는" 현상이 생겼다(강공격·점프·잡기 공통).
    /// CharacterActionGrab은 잡기 전용 정착(TickSettle)을 갖고 있었고, 이 컴포넌트는 같은 원리를 모든 상태 전이에 일반화한다:
    ///
    ///   상태가 바뀌는 순간 몸(골반 본)의 지면 XZ 위치가 루트에서 MinOffset 이상 떨어져 있으면 정착을 시작하고,
    ///   SettleDuration 동안 매 LateUpdate(Animator 평가 후) 본이 루트로 돌아간 만큼 루트를 반대로 옮겨
    ///   화면상 몸이 그 자리에 머물게 한다. 루트가 다른 이유(이동 입력·공격 전진)로 움직인 양은 기준에 더해 상쇄하지 않는다.
    ///   → 결과적으로 "모션이 보여준 만큼" 루트가 따라가 확정된다(commit).
    ///
    ///   새 행동 상태(Attack/Jump/Dash 등)로 들어간 뒤에는 ActionGrace 동안만 이어가고 멈춘다 —
    ///   새 모션 자체의 본 변위를 상쇄하면 안 되기 때문(잡기의 SettleAttackGrace와 같은 규칙).
    ///
    /// 제외: 잡기(Grab/ChainGrab)에서 나올 때와 CharacterActionGrab이 정착 중일 때(그쪽이 담당), 사망, 텔레포트급 루트 점프.
    /// 순수 시각 보정이라 Character/State/Combat 구조는 건드리지 않는다. 플레이어 프리팹에 부착.
    /// </summary>
    [AddComponentMenu("Yeolha/Character/Character Visual Settle")]
    [DisallowMultipleComponent]
    public class CharacterVisualSettle : MonoBehaviour
    {
        [Header("Settle")]
        [Tooltip("행동 종료 정착 사용 여부")]
        public bool Enabled = true;

        [Min(0f)]
        [Tooltip("상태 전이 후 본 복귀를 상쇄하는 시간(초). 복귀 CrossFade 길이보다 조금 길게 (잡기 endSettleDuration 0.35 동일 기준)")]
        public float SettleDuration = 0.35f;

        [Min(0f)]
        [Tooltip("새 행동 상태(Attack/Jump/Dash/Guard/Grab…)에 들어간 뒤 정착을 이어가는 유예(초, CrossFade 구간). 그 뒤엔 새 모션의 본 변위를 건드리지 않는다")]
        public float ActionGrace = 0.15f;

        [Min(0f)]
        [Tooltip("전이 순간 몸이 루트에서 이 거리(m) 이상 떨어져 있을 때만 정착 시작 (로코모션 흔들림 등 미세 변위 무시)")]
        public float MinOffset = 0.03f;

        [Min(0f)]
        [Tooltip("한 프레임에 루트가 이 거리(m) 이상 이동하면(텔레포트/리스폰) 정착 중단")]
        public float AbortRootJump = 2f;

        [Header("Debug")]
        [Tooltip("정착 시작/종료 로그")]
        public bool LogSettle = false;

        private Character _character;
        private CharacterController _controller;
        private CharacterActionGrab _actionGrab;
        private Transform _visualBone;
        private bool _subscribed;

        private bool _settling;
        private float _settleTimer;
        private Vector3 _settleVisRef;
        private Vector3 _settleLastRoot;
        private float _actionEnterAt = -1f;

        /// <summary>현재 정착(본 복귀 상쇄) 진행 중인가.</summary>
        public bool IsSettling => _settling;

        private void Awake()
        {
            _character = GetComponent<Character>();
            _controller = GetComponent<CharacterController>();
            _actionGrab = GetComponent<CharacterActionGrab>();
        }

        private void OnEnable() => TrySubscribe();
        private void Start() => TrySubscribe();

        private void OnDisable()
        {
            Unsubscribe();
            _settling = false;
        }

        private void TrySubscribe()
        {
            if (_subscribed) return;
            if (_character == null) _character = GetComponent<Character>();
            if (_character == null || _character.StateManager == null) return;
            _character.StateManager.StateChanged += OnStateChanged;
            if (_character.Combat != null) _character.Combat.AttackStarted += OnAttackStarted;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_character != null && _character.StateManager != null)
                _character.StateManager.StateChanged -= OnStateChanged;
            if (_character != null && _character.Combat != null)
                _character.Combat.AttackStarted -= OnAttackStarted;
            _subscribed = false;
        }

        /// <summary>
        /// 콤보 연결처럼 Attack 상태 안에서 다음 공격이 시작될 때(StateChanged 없음). 직전 타의 본 변위를 유예 동안 정착시킨다.
        /// 잡기(GrabAttackLink) 시작은 CharacterActionGrab이 위치를 소유하므로 제외.
        /// </summary>
        private void OnAttackStarted(AttackAction action)
        {
            if (!Enabled || !isActiveAndEnabled || _character == null) return;
            var sm = _character.StateManager;
            var st = sm != null ? sm.CurrentStateType : CharacterStateType.Idle;
            if (IsGrab(st) || (_actionGrab != null && _actionGrab.IsSettling)) return;
            _actionEnterAt = Time.time;
            if (_settling) return;
            TryBeginSettle("AttackStarted:" + (action != null ? action.AttackName : ""));
        }

        private static bool IsLocomotion(CharacterStateType s)
        {
            return s == CharacterStateType.Idle || s == CharacterStateType.Move || s == CharacterStateType.Run
                || s == CharacterStateType.Land || s == CharacterStateType.Fall;
        }

        private static bool IsGrab(CharacterStateType s)
        {
            return s == CharacterStateType.Grab || s == CharacterStateType.ChainGrab;
        }

        private void OnStateChanged(CharacterStateType previous, CharacterStateType next)
        {
            if (!Enabled || !isActiveAndEnabled) return;

            // 새 행동 상태 진입 시각 기록 (정착 유예 기준). 로코모션은 유예 없이 계속.
            _actionEnterAt = IsLocomotion(next) ? -1f : Time.time;

            if (next == CharacterStateType.Dead) { _settling = false; return; }
            // 잡기는 CharacterActionGrab이 자체 정착(BeginEndSettle) — 이중 보정 방지
            if (IsGrab(previous) || IsGrab(next)) return;
            // 정착 중 다시 전이(예: Idle→Attack): 유예 규칙으로 이어가되 기준은 유지 — 새로 시작하지 않는다
            if (_settling) return;

            TryBeginSettle(previous + "→" + next);
        }

        private void TryBeginSettle(string reason)
        {
            Vector3 offset = VisualGroundPosition() - transform.position;
            offset.y = 0f;
            if (offset.magnitude < MinOffset) return;

            _settling = true;
            _settleTimer = 0f;
            _settleVisRef = VisualGroundPosition();
            _settleLastRoot = transform.position;
            if (LogSettle)
                Debug.Log($"[CharacterVisualSettle] {reason} offset={offset.magnitude:F3}m 정착 시작", this);
        }

        private void LateUpdate()
        {
            if (!_settling) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (!Enabled || _character == null || _character.IsDead
                || (_actionGrab != null && _actionGrab.IsSettling))
            {
                _settling = false;
                return;
            }

            _settleTimer += dt;
            var sm = _character.StateManager;
            var st = sm != null ? sm.CurrentStateType : CharacterStateType.Idle;
            bool allowed = IsLocomotion(st)
                || (_actionEnterAt >= 0f && Time.time - _actionEnterAt <= ActionGrace);
            if (!allowed || IsGrab(st) || _settleTimer > SettleDuration)
            {
                if (LogSettle) Debug.Log($"[CharacterVisualSettle] 정착 종료 (state={st}, t={_settleTimer:F2})", this);
                _settling = false;
                return;
            }

            // 루트가 다른 이유(이동/공격 전진/넉백)로 움직인 양은 기준에 더한다 — 그 이동은 상쇄 대상이 아님.
            Vector3 rootMoved = transform.position - _settleLastRoot;
            rootMoved.y = 0f;
            if (rootMoved.magnitude > AbortRootJump) { _settling = false; return; }
            _settleVisRef += rootMoved;

            // 본이 루트로 돌아간 만큼(시각 위치 변화) 루트를 반대로 옮겨 화면상 몸 위치를 유지한다.
            Vector3 delta = VisualGroundPosition() - _settleVisRef;
            delta.y = 0f;
            if (delta.sqrMagnitude > 0.000001f)
            {
                ShiftRoot(-delta);
                Physics.SyncTransforms();
            }
            _settleLastRoot = transform.position;
        }

        /// <summary>몸(골반 본) 지면 위치 — XZ는 본, 높이는 루트 (CharacterVisualBone 규칙).</summary>
        private Vector3 VisualGroundPosition()
        {
            if (_visualBone == null) _visualBone = CharacterVisualBone.Resolve(transform);
            return CharacterVisualBone.GroundPosition(transform, _visualBone);
        }

        /// <summary>루트를 이동(속도/Facing 불변). CharacterController가 되돌리지 않도록 잠시 비활성 (CharacterActionGrab.ShiftRoot와 동일).</summary>
        private void ShiftRoot(Vector3 shift)
        {
            if (shift.sqrMagnitude < 0.0000001f) return;
            if (_controller == null) _controller = GetComponent<CharacterController>();
            bool wasEnabled = _controller != null && _controller.enabled;
            if (_controller != null) _controller.enabled = false;
            transform.position += shift;
            if (_controller != null) _controller.enabled = wasEnabled;
        }
    }
}
