using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Level-facing moving platform / elevator. Drop the prefab in, move the EndPoint, pick a
    /// Mode, done. The start position is the prefab's own initial position (no separate StartPoint).
    ///
    /// Riders are carried by delta (never by re-parenting): each frame the platform pushes its
    /// step to grounded riders via CharacterMovement.AddExternalDelta, so the rider's final
    /// motion is (own movement + platform delta) and every CharacterState/animation is preserved.
    /// A jumping rider stops being grounded and is released automatically.
    /// </summary>
    [AddComponentMenu("Yeolha/Traversal/Moving Platform")]
    public class MovingPlatform : MonoBehaviour
    {
        /// <summary>운행 방식. 새 타입은 여기에 추가하고 OnArrived의 switch만 채우면 된다.</summary>
        public enum Mode
        {
            /// <summary>시작 즉시 시작↔끝을 계속 왕복.</summary>
            PingPong,
            /// <summary>시작 즉시 시작→끝 반복. 끝에 닿으면 시작 지점으로 순간복귀(라이더는 따라가지 않는다).</summary>
            LoopTeleport,
            /// <summary>정지 대기. 플레이어가 올라타면 출발해 끝까지 편도 이동 후 그대로 정지.</summary>
            OneWayOnTouch,
        }

        [Header("Mode")]
        [Tooltip("운행 방식. OneWayOnTouch는 플레이어 탑승 전까지 정지해 있는다.")]
        [SerializeField] private Mode mode = Mode.PingPong;

        [Header("Path")]
        [Tooltip("도착 지점. 시작 지점은 이 오브젝트의 초기 위치.")]
        [SerializeField] private Transform endPoint;
        [Tooltip("이동 속도 (m/s)")]
        [SerializeField, Min(0.01f)] private float speed = 2f;

        [Header("Timing")]
        [Tooltip("양 끝에서 멈춰 기다리는 시간 (s). 편도 타입에는 적용되지 않는다.")]
        [SerializeField, Min(0f)] private float waitTime = 0.5f;
        [Tooltip("출발 명령 후 지연 (s). 자동 타입은 씬 시작이, 편도 타입은 탑승 시점이 기준.")]
        [SerializeField, Min(0f)] private float startDelay = 0f;
        [Tooltip("양 끝에서 감속/가속(부드러운 이동)")]
        [SerializeField] private bool ease = false;

        private Vector3 _start;
        private Vector3 _endWorld;

        private bool _running;
        private bool _towardEnd = true;
        private float _waitUntil;
        private float _startTime;
        private readonly HashSet<Character> _riders = new HashSet<Character>();

        /// <summary>현재 운행 중인가. 편도 타입이 도착해 멈추면 false.</summary>
        public bool IsRunning => _running;

        /// <summary>운행 방식. 런타임 전환용(예: 편도 발판을 이벤트로 왕복 전환).</summary>
        public Mode CurrentMode
        {
            get => mode;
            set => mode = value;
        }

        public void AddRider(Character c)
        {
            if (c == null) return;
            _riders.Add(c);

            // 편도 타입은 플레이어가 올라탄 순간이 곧 출발 신호. 적/아군은 무시한다.
            if (mode == Mode.OneWayOnTouch && IsPlayer(c)) Activate();
        }

        public void RemoveRider(Character c) { if (c != null) _riders.Remove(c); }

        /// <summary>출발시킨다. 버튼·컷씬 등 외부 트리거용. 이미 운행 중이면 무시.</summary>
        public void Activate()
        {
            if (_running) return;
            _running = true;
            _startTime = Time.time + startDelay;
        }

        /// <summary>즉시 정지. 위치는 그대로 둔다.</summary>
        public void Halt() => _running = false;

        /// <summary>시작 지점으로 되돌리고 다시 대기 상태로. 편도 발판 재사용용.</summary>
        public void ResetToStart()
        {
            _running = false;
            _towardEnd = true;
            _waitUntil = 0f;
            transform.position = _start;
        }

        private void Awake()
        {
            if (endPoint == null) { var e = transform.Find("EndPoint"); if (e != null) endPoint = e; }
            _start = transform.position;
            _endWorld = endPoint != null ? endPoint.position : _start;
        }

        private void Start()
        {
            // 자동 타입만 씬 시작과 동시에 운행. 편도 타입은 AddRider/Activate를 기다린다.
            if (mode != Mode.OneWayOnTouch) Activate();
        }

        private void Update()
        {
            if (endPoint == null) return;
            if (!_running) return;
            if (Time.time < _startTime) return;
            if (Time.time < _waitUntil) return;

            Vector3 cur = transform.position;
            Vector3 goal = _towardEnd ? _endWorld : _start;

            float spd = speed;
            if (ease)
            {
                Vector3 segStart = _towardEnd ? _start : _endWorld;
                float total = Vector3.Distance(segStart, goal);
                float remaining = Vector3.Distance(cur, goal);
                float prog = total > 0.0001f ? 1f - Mathf.Clamp01(remaining / total) : 1f;
                spd = speed * Mathf.Lerp(0.35f, 1f, Mathf.Sin(prog * Mathf.PI));
            }

            Vector3 next = Vector3.MoveTowards(cur, goal, spd * Time.deltaTime);
            Vector3 step = next - cur;
            transform.position = next;
            PushRiders(step);

            if ((next - goal).sqrMagnitude <= 1e-6f)
                OnArrived();
        }

        private void OnArrived()
        {
            switch (mode)
            {
                case Mode.PingPong:
                    _towardEnd = !_towardEnd;
                    _waitUntil = Time.time + waitTime;
                    break;

                case Mode.LoopTeleport:
                    transform.position = _start; // teleport back — not pushed to riders
                    _waitUntil = Time.time + waitTime;
                    break;

                case Mode.OneWayOnTouch:
                    _running = false; // 편도 완주 — 재출발은 ResetToStart/Activate로만.
                    break;
            }
        }

        private void PushRiders(Vector3 step)
        {
            if (step.sqrMagnitude < 1e-10f || _riders.Count == 0) return;
            foreach (var rider in _riders)
            {
                if (rider == null || rider.Movement == null) continue;
                // Only carry grounded riders; a jumping rider is released (belt-scroll X/Y/Z stable).
                if (!rider.Movement.IsGrounded) continue;
                rider.Movement.AddExternalDelta(step);
            }
        }

        // 적도 Character이므로 GameManager의 플레이어와 동일한지로 선별한다(BattleZone과 동일 규칙).
        private static bool IsPlayer(Character character)
        {
            if (character == null) return false;

            Character player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player != null) return character == player;
            return character.Faction == FactionType.Player;
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 start = Application.isPlaying ? _start : transform.position;
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(start, 0.15f);
            if (endPoint != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(endPoint.position, 0.15f);
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(start, endPoint.position);
            }
        }
    }
}
