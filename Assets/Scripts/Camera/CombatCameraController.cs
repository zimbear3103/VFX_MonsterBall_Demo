using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 전투 카메라 컨트롤러 — 플레이어 + 전투 참여 적들의 가중 구도를 CameraRig 노드에 주입한다.
    /// 구버전 CameraMultiTargetManager(CinemachineTargetGroup) + CameraDistanceController + CharacterCameraFocusTarget 이관.
    /// Cinemachine 미사용: FollowCameraNode의 멀티타겟 API(AddTarget/Weight)와 SpringArm 채널만 사용한다.
    ///
    /// 구버전에서 그대로 승계한 동작
    ///   - Weight: Player 3 / Enemy 1 (개체 오버라이드는 CameraFocusTarget), Weight는 SmoothDamp(0.4s)로 0↔W 페이드.
    ///   - 활성 세트: 카메라 로컬 X(벨트축) 기준 |enemyX − playerX| 오름차순으로, 그룹 좌우 폭이
    ///     2·(MaxDistance·tan(hFov/2) − Padding) 이하일 때만 점진 포함. 이미 포함된 적은 ×FilterHysteresis(1.15).
    ///   - 거리: (활성 타겟 X 폭/2 + Padding) / tan(hFov/2) → [Min, Max] clamp → SpringArm Damping.
    ///     2026-09-15: 폭을 추적 중심(가중 무게중심) 기준 최대 외곽으로 재고, 세로(카메라 로컬 Y) 폭이 요구하는
    ///     거리 (halfHeight + VPadding)/tan(vFov/2)도 함께 계산해 큰 쪽을 쓴다 — 올려치기/점프 내려찍기 높이차 대응.
    ///   - 이탈/사망: 즉시 제거 대신 Weight 0으로 페이드 후 제거(PendingRemoval).
    ///   - Focus(구 AirFocus): 지정 대상 Weight ×2, 활성 세트를 플레이어+대상으로 제한, 추적거리 상한, 종료 유예.
    ///
    /// ver2에서 추가한 동작
    ///   - 전투 참여 판정: AIController 등록 적 중 살아있고 Entering이 아니며 AIState != Idle(또는 플레이어에게 최근 적중)만 포함.
    ///   - Main Target: 플레이어 공격에 적중한 적(CharacterCombat.LastHitVictim 폴링 — AttackAction/Enemy 수정 없음)을
    ///     mainTargetWeight로 승격. 유효한 Main은 mainTargetHoldTime 동안 다른 적 적중에도 유지(광역기 튐 방지).
    ///     사망/비활성/영향거리 이탈/timeout 시 해제(옵션: AIController Aggressive 역할 적으로 폴백).
    ///     플레이어 처형(Executing) 중엔 ExecutionTarget을 Main으로 고정.
    ///   - Main Target Zoom: 플레이어-Main 수평 거리가 zoomInStart→zoomInFull로 줄면 프레이밍 하한을
    ///     ×zoomInDistanceScale까지 낮춰 줌인. 다른 활성 적이 넓게 퍼져 DesiredDistance가 더 크면 프레이밍이 우선.
    ///
    /// 실행 순서 90 = CameraRig(100) 직전. 카메라 시퀀스(CameraSequenceRunner 200) 재생 중에는
    /// 시퀀스가 최종 pose를 덮어쓰므로 별도 양보 처리가 필요 없다.
    /// </summary>
    [DefaultExecutionOrder(90)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Yeolha/Camera/Combat Camera Controller")]
    public class CombatCameraController : MonoBehaviour
    {
        private const float WeightEpsilon = 0.001f;

        private enum FocusKind { Player, Enemy }

        private class Entry
        {
            public Character Character;
            public Transform Transform;
            public FocusKind Kind;
            public float BaseWeightMultiplier = 1f;
            public float Radius;
            public float CurrentWeight;
            public float TargetWeight;
            public float WeightVelocity;
            public bool PendingRemoval;
            public bool Active;      // 이번 프레임 활성 세트 포함 여부
            public bool WasActive;   // 직전 프레임 활성(hysteresis)
            public float LastHitByPlayerTime = -999f;
            public float LocalX;     // 카메라 로컬 X (벨트축) 캐시
        }

        // ─────────────── Inspector ───────────────

        [Header("References")]
        [Tooltip("대상 CameraRig. 비우면 자신/부모/씬에서 찾는다")]
        [SerializeField] private CameraRig rig;

        [Header("Weights")]
        [Tooltip("플레이어 Weight (구버전 3)")]
        [SerializeField] private float playerWeight = 3f;

        [Tooltip("Main Target(플레이어가 현재 싸우는 적) Weight (구버전 AirFocus 배율 2 승계)")]
        [SerializeField] private float mainTargetWeight = 2f;

        [Tooltip("그 외 전투 참여 적 Weight (구버전 1)")]
        [SerializeField] private float otherEnemyWeight = 1f;

        [Tooltip("Weight가 0↔목표값으로 수렴하는 SmoothDamp 시간(초). 클수록 중심이 천천히 이동 (구버전 0.4)")]
        [SerializeField, Range(0f, 2f)] private float weightTransitionDuration = 0.4f;

        [Header("Participants")]
        [Tooltip("AI 판단 상태가 Idle인(전투 미참여) 적은 제외한다. false면 등록된 모든 적 포함(구버전 방식)")]
        [SerializeField] private bool engagedOnly = true;

        [Tooltip("플레이어 기준 벨트축 거리(m)가 이 값을 넘는 적은 카메라 영향에서 제외. 0 = 화면 폭 기준만 사용")]
        [SerializeField] private float otherEnemyInfluenceDistance = 12f;

        [Tooltip("필터 hysteresis 배수. 1.15면 이미 포함 중인 적은 경계의 115%까지 벗어나야 제외 (구버전 1.15)")]
        [SerializeField] private float filterHysteresis = 1.15f;

        [Tooltip("CameraFocusTarget이 없는 플레이어의 프레이밍 반경(m)")]
        [SerializeField] private float defaultPlayerRadius = 0.5f;

        [Tooltip("CameraFocusTarget이 없는 적의 프레이밍 반경(m)")]
        [SerializeField] private float defaultEnemyRadius = 0.5f;

        [Header("Main Target")]
        [Tooltip("Main Target이 유효한 동안 다른 적을 때려도 이 시간(초) 안에는 Main을 바꾸지 않는다 (광역기 튐 방지). Main을 마지막으로 때린 시각 기준")]
        [SerializeField] private float mainTargetHoldTime = 1f;

        [Tooltip("Main Target을 이 시간(초) 동안 때리지 않으면 해제. 0 = 해제 안 함")]
        [SerializeField] private float mainTargetTimeout = 6f;

        [Tooltip("Main이 해제/무효화되면 AIController가 Aggressive 역할을 준 적(현재 공격권자)을 새 Main으로 삼는다")]
        [SerializeField] private bool fallbackToAggressiveRole = true;

        [Tooltip("플레이어가 Idle 상태의 적을 때리면(선제 공격) 그 적을 즉시 전투 참여로 간주")]
        [SerializeField] private bool hitCountsAsEngaged = true;

        [Header("Framing Distance")]
        [Tooltip("활성 타겟 좌우 폭 기준으로 카메라 거리를 자동 계산해 SpringArm에 주입한다")]
        [SerializeField] private bool useFramingDistance = true;

        [Tooltip("타겟 외곽 너머 좌우 여유 공간(m) (구버전 Padding 1.5)")]
        [SerializeField] private float framingPadding = 1.5f;

        [Tooltip("전투 카메라 최소 거리(m). 0 = SpringArm의 Zone/Base 거리를 하한으로 사용")]
        [SerializeField] private float minCombatDistance = 0f;

        [Tooltip("전투 카메라 최대 거리(m). 적이 많아도 이 이상 멀어지지 않는다 (구버전 8, ver2 base 8 기준 12 권장)")]
        [SerializeField] private float maxCombatDistance = 12f;

        [Tooltip("타겟 상하(카메라 로컬 Y) 폭도 프레이밍 거리에 반영한다. 올려치기/점프 내려찍기처럼 플레이어와 적의 높이 차가 커질 때 " +
                 "좌우 폭만 보면 둘 다 화면 밖으로 나가므로, 세로 폭이 요구하는 거리가 더 크면 그만큼 줌아웃한다 (2026-09-15)")]
        [SerializeField] private bool useVerticalFraming = true;

        [Tooltip("타겟 상하 외곽 너머 여유 공간(m). 세로 프레이밍 전용 (가로는 Framing Padding)")]
        [SerializeField] private float verticalFramingPadding = 1.2f;

        [Tooltip("프레이밍 폭을 '추적 중심(가중 무게중심)' 기준으로 잰다. 플레이어 Weight가 커서 중심이 플레이어 쪽으로 치우치면 " +
                 "반대편 적이 (폭/2)보다 멀리 있어 잘리던 문제 보정. 끄면 구버전처럼 외곽 폭/2만 사용")]
        [SerializeField] private bool frameFromWeightedCenter = true;

        [Header("Main Target Zoom")]
        [Tooltip("플레이어와 Main Target이 가까우면 프레이밍 하한(Zone/Base 거리)을 낮춰 줌인한다. 다른 적이 넓게 퍼져 있으면 프레이밍 거리가 우선한다")]
        [SerializeField] private bool useMainTargetZoom = true;

        [Tooltip("플레이어-Main Target 수평 거리(m)가 이 값 이하부터 줌인 시작")]
        [SerializeField] private float zoomInStartDistance = 3f;

        [Tooltip("플레이어-Main Target 수평 거리(m)가 이 값 이하면 완전 줌인")]
        [SerializeField] private float zoomInFullDistance = 1.2f;

        [Tooltip("완전 줌인 시 하한 거리 배율. 0.75면 Base 8m → 6m (SpringArm minDistance 이하로는 내려가지 않음)")]
        [SerializeField, Range(0.3f, 1f)] private float zoomInDistanceScale = 0.75f;

        [Tooltip("줌 비율 전환 SmoothDamp 시간(초). Main 교체/해제 시 튐 방지")]
        [SerializeField, Range(0f, 1f)] private float zoomBlendSmoothTime = 0.25f;

        [Header("Combat Offset")]
        [Tooltip("전투 참여 적이 있을 때 추적 중심에 더할 오프셋(m). 적 Weight 합에 비례해 페이드 인")]
        [SerializeField] private Vector3 combatFollowOffset = Vector3.zero;

        [Header("Focus (Air Focus)")]
        [Tooltip("BeginFocus 대상에 곱할 Weight 배율 (구버전 2)")]
        [SerializeField] private float focusWeightMultiplier = 2f;

        [Tooltip("포커스 중 Weight 전환 시간(초) (구버전 0.1)")]
        [SerializeField, Range(0f, 2f)] private float focusWeightTransitionDuration = 0.1f;

        [Tooltip("EndFocus 호출 후 실제 해제까지 유예(초) (구버전 1.0)")]
        [SerializeField] private float focusHoldDuration = 1f;

        [Tooltip("포커스 중 추적 중심이 플레이어에서 벗어날 수 있는 최대 수평 거리(m). 0 = 무제한 (구버전 8)")]
        [SerializeField] private float maxFocusChaseDistance = 8f;

        [Tooltip("포커스 대상이 플레이어에서 이 거리(m) 이상 멀어지면 스턱으로 보고 즉시 해제 (구버전 25)")]
        [SerializeField] private float focusStuckDistance = 25f;

        [Header("Combat Camera Debug (View Only)")]
        [Tooltip("현재 Main Target")]
        [SerializeField] private Character debugMainTarget;
        [Tooltip("현재 활성(카메라 영향) 적 수")]
        [SerializeField] private int debugActiveEnemyCount;
        [Tooltip("등록된 적 수(페이드아웃 중 포함)")]
        [SerializeField] private int debugRegisteredEnemyCount;
        [Tooltip("마지막으로 SpringArm에 적용한 프레이밍 거리")]
        [SerializeField] private float debugFramingDistance;
        [Tooltip("포커스 활성 여부")]
        [SerializeField] private bool debugFocusActive;
        [Tooltip("현재 Main Target 근접 줌인 비율(0~1)")]
        [SerializeField] private float debugZoomBlend;

        // ─────────────── Runtime ───────────────

        private static CombatCameraController s_instance;

        /// <summary>씬의 컨트롤러(lazy-find). 없으면 null — 호출부는 null 체크만 한다.</summary>
        public static CombatCameraController Instance
        {
            get
            {
                if (s_instance == null)
                    s_instance = FindFirstObjectByType<CombatCameraController>();
                return s_instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_instance = null;

        private readonly Dictionary<Character, Entry> _entries = new Dictionary<Character, Entry>();
        private readonly List<Entry> _enemyBuffer = new List<Entry>();
        private readonly List<Character> _removalBuffer = new List<Character>();
        private static readonly System.Comparison<Entry> s_beltOffsetComparer =
            (a, b) => Mathf.Abs(a.LocalX).CompareTo(Mathf.Abs(b.LocalX));

        private Entry _playerEntry;
        private Character _player;

        // Main Target
        private Character _mainTarget;
        private float _mainLastHitTime = -999f;
        private float _lastProcessedHitTime = -999f;

        // Focus
        private bool _focusActive;
        private bool _focusSolo;
        private bool _focusRestrictSet = true;
        private Character _focusTarget;
        private bool _focusEndRequested;
        private float _focusEndRequestTime;

        private CombatFramingDistanceChannel _distanceChannel;
        private CombatFollowOffsetChannel _followChannel;

        // Main Target Zoom
        private float _zoomBlend;
        private float _zoomBlendVelocity;

        /// <summary>현재 Main Target(플레이어가 싸우는 적). 없으면 null.</summary>
        public Character MainTarget => _mainTarget;

        /// <summary>포커스(구 AirFocus) 활성 여부 — 외부 중복 진입 방지용.</summary>
        public bool IsFocusActive => _focusActive;

        // ─────────────── Lifecycle ───────────────

        private void OnEnable()
        {
            if (s_instance == null) s_instance = this;
            ResolveRig();
            EnsureChannels();
        }

        private void OnDisable()
        {
            ReleaseChannels();
            ClearAllTargets();
            if (s_instance == this) s_instance = null;
        }

        private void ResolveRig()
        {
            if (rig != null) return;
            rig = GetComponent<CameraRig>();
            if (rig == null) rig = GetComponentInParent<CameraRig>();
            if (rig == null) rig = FindFirstObjectByType<CameraRig>();
        }

        private void EnsureChannels()
        {
            if (rig == null) return;

            if (_distanceChannel == null || _distanceChannel.IsFinished)
            {
                _distanceChannel = new CombatFramingDistanceChannel();
                if (rig.SpringArmNode != null) rig.SpringArmNode.AddChannel(_distanceChannel, -100);
            }
            if (_followChannel == null || _followChannel.IsFinished)
            {
                _followChannel = new CombatFollowOffsetChannel();
                if (rig.FollowNode != null) rig.FollowNode.AddChannel(_followChannel, -100);
            }
        }

        private void ReleaseChannels()
        {
            if (_distanceChannel != null)
            {
                _distanceChannel.Finish();
                if (rig != null && rig.SpringArmNode != null) rig.SpringArmNode.RemoveChannel(_distanceChannel);
                _distanceChannel = null;
            }
            if (_followChannel != null)
            {
                _followChannel.Finish();
                if (rig != null && rig.FollowNode != null) rig.FollowNode.RemoveChannel(_followChannel);
                _followChannel = null;
            }
        }

        /// <summary>비활성 시 FollowNode에서 적 타겟을 모두 제거하고 플레이어만 남긴다(리그 기본 동작 복귀).</summary>
        private void ClearAllTargets()
        {
            if (rig != null && rig.FollowNode != null)
            {
                foreach (var kv in _entries)
                {
                    if (kv.Value.Kind == FocusKind.Enemy && kv.Value.Transform != null)
                        rig.FollowNode.RemoveTarget(kv.Value.Transform);
                }
                if (rig.PlayerTarget != null)
                    rig.FollowNode.AddTarget(rig.PlayerTarget, 1f);
            }
            _entries.Clear();
            _playerEntry = null;
            _mainTarget = null;
            _focusActive = false;
            _focusTarget = null;
            _focusEndRequested = false;
        }

        // ─────────────── Update ───────────────

        private void LateUpdate()
        {
            if (rig == null) ResolveRig();
            if (rig == null || rig.FollowNode == null) return;
            EnsureChannels();

            float dt = Time.deltaTime;

            SyncPlayer();
            SyncEnemies();
            UpdateFocusWatchdog();
            UpdateMainTarget();
            ComputeActiveSet();
            UpdateWeights(dt);
            PushToFollowNode();
            CleanupCompletedRemovals();
            UpdateFramingDistance();
            UpdateFollowChannel();
            UpdateDebugView();
        }

        // ─────────────── Player ───────────────

        private void SyncPlayer()
        {
            Character player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            Transform playerTr = rig.PlayerTarget;
            if (playerTr == null && player != null) playerTr = player.transform;

            if (_player != player)
            {
                if (_playerEntry != null)
                {
                    if (_playerEntry.Character != null) _entries.Remove(_playerEntry.Character);
                    _playerEntry = null;
                }
                _player = player;
                _mainTarget = null;
            }

            if (playerTr == null) return;

            if (_playerEntry == null)
            {
                _playerEntry = new Entry
                {
                    Character = player,
                    Kind = FocusKind.Player,
                    CurrentWeight = playerWeight, // 플레이어는 페이드 인 없이 즉시(Follow 공백 방지)
                    TargetWeight = playerWeight,
                };
                if (player != null) _entries[player] = _playerEntry;
            }

            // 플레이어 Transform/반경은 매 프레임 갱신(CameraRig.SetPlayerTarget · 리스폰 대응)
            var focus = player != null ? player.GetComponent<CameraFocusTarget>() : null;
            _playerEntry.Transform = focus != null ? focus.ResolveTargetTransform(playerTr) : playerTr;
            _playerEntry.Radius = focus != null ? focus.Radius : defaultPlayerRadius;
            _playerEntry.BaseWeightMultiplier = focus != null ? focus.WeightMultiplier : 1f;
            _playerEntry.PendingRemoval = false;
        }

        // ─────────────── Enemies ───────────────

        private bool IsCombatParticipant(EnemyCharacter c, Entry existing)
        {
            if (c == null || !c.isActiveAndEnabled) return false;
            if (c.IsDead || c.CurrentState == CharacterStateType.Dead) return false;
            if (c.IsEntering) return false;
            if (_player != null && !AIController.IsHostile(_player, c)) return false;

            if (!engagedOnly) return true;
            if (c.State != EnemyCharacter.AIState.Idle) return true;

            // 선제 공격: Idle 적이라도 플레이어에게 최근 맞았으면 전투 참여
            if (hitCountsAsEngaged && existing != null)
            {
                float window = mainTargetTimeout > 0f ? mainTargetTimeout : mainTargetHoldTime;
                if (Time.time - existing.LastHitByPlayerTime <= window) return true;
            }
            return false;
        }

        private void SyncEnemies()
        {
            // 1. 등록된 AI 순회 — 참여자는 등록/복원, 비참여자는 페이드아웃
            var ai = AIController.Instance;
            if (ai != null)
            {
                var list = ai.Characters;
                for (int i = 0; i < list.Count; i++)
                {
                    EnemyCharacter c = list[i];
                    if (c == null) continue;
                    _entries.TryGetValue(c, out Entry e);
                    if (IsCombatParticipant(c, e))
                        RegisterEnemy(c, e);
                    else if (e != null && !e.PendingRemoval)
                        e.PendingRemoval = true;
                }
            }

            // 2. 파괴/비활성/미등록 상태의 엔트리 정리
            _removalBuffer.Clear();
            foreach (var kv in _entries)
            {
                Entry e = kv.Value;
                if (e.Kind == FocusKind.Player) continue;
                if (kv.Key == null || e.Transform == null)
                {
                    _removalBuffer.Add(kv.Key);
                    continue;
                }
                if (!e.PendingRemoval && !IsCombatParticipant(kv.Key as EnemyCharacter, e))
                    e.PendingRemoval = true;
            }
            for (int i = 0; i < _removalBuffer.Count; i++)
            {
                Entry e = _entries[_removalBuffer[i]];
                if (e.Transform != null) rig.FollowNode.RemoveTarget(e.Transform);
                _entries.Remove(_removalBuffer[i]);
            }
            _removalBuffer.Clear();
        }

        private Entry RegisterEnemy(Character c, Entry existing)
        {
            var focus = c.GetComponent<CameraFocusTarget>();
            if (existing == null)
            {
                existing = new Entry
                {
                    Character = c,
                    Kind = FocusKind.Enemy,
                    CurrentWeight = 0f,
                    TargetWeight = 0f,
                };
                _entries[c] = existing;
            }
            // 복원 경로(페이드아웃 도중 재참여) 포함 — 데이터 갱신
            existing.Transform = focus != null ? focus.ResolveTargetTransform(c.transform) : c.transform;
            existing.Radius = focus != null ? focus.Radius : defaultEnemyRadius;
            existing.BaseWeightMultiplier = focus != null ? focus.WeightMultiplier : 1f;
            existing.PendingRemoval = false;
            return existing;
        }

        // ─────────────── Main Target ───────────────

        private bool IsValidMainTarget(Character c)
        {
            if (c == null) return false;
            if (!_entries.TryGetValue(c, out Entry e)) return false;
            if (e.Kind != FocusKind.Enemy || e.PendingRemoval) return false;
            if (c.IsDead || !c.isActiveAndEnabled) return false;

            // 영향 거리 이탈(hysteresis 적용)
            if (otherEnemyInfluenceDistance > 0f && _playerEntry != null && _playerEntry.Transform != null)
            {
                float limit = otherEnemyInfluenceDistance * Mathf.Max(1f, filterHysteresis);
                if (Mathf.Abs(BeltOffset(e.Transform.position)) > limit) return false;
            }
            return true;
        }

        private void UpdateMainTarget()
        {
            float now = Time.time;
            CharacterCombat combat = _player != null ? _player.Combat : null;

            // 1. 강제 타겟: 플레이어 처형 중엔 ExecutionTarget 고정
            if (combat != null && _player.CurrentState == CharacterStateType.Executing && combat.ExecutionTarget != null)
            {
                Character exec = combat.ExecutionTarget;
                if (_entries.TryGetValue(exec, out Entry ee)) ee.LastHitByPlayerTime = now;
                else if (exec is EnemyCharacter ec && ec.isActiveAndEnabled) RegisterEnemy(ec, null).LastHitByPlayerTime = now;
                if (_mainTarget != exec) { _mainTarget = exec; }
                _mainLastHitTime = now;
                return;
            }

            // 2. 플레이어 적중 이벤트 폴링 (CharacterCombat.LastHitVictim — 잡기 대상 선정과 같은 소스)
            if (combat != null)
            {
                Character victim = combat.LastHitVictim;
                float hitTime = combat.LastHitVictimTime;
                if (victim != null && hitTime > _lastProcessedHitTime)
                {
                    _lastProcessedHitTime = hitTime;
                    OnPlayerHit(victim, hitTime);
                }
            }

            // 3. 유효성/timeout 검사 → 해제 또는 폴백
            if (_mainTarget != null)
            {
                bool expired = mainTargetTimeout > 0f && now - _mainLastHitTime > mainTargetTimeout;
                if (expired || !IsValidMainTarget(_mainTarget))
                    _mainTarget = null;
            }

            if (_mainTarget == null && fallbackToAggressiveRole)
            {
                Character fallback = FindAggressiveEnemy();
                if (fallback != null)
                {
                    _mainTarget = fallback;
                    _mainLastHitTime = now; // 폴백 시점부터 timeout 재계산
                }
            }
        }

        private void OnPlayerHit(Character victim, float hitTime)
        {
            if (victim == _player) return;
            var enemy = victim as EnemyCharacter;
            if (enemy == null) return;

            _entries.TryGetValue(victim, out Entry e);
            if (e == null)
            {
                // Idle 적 선제 공격 등 — 참여자로 즉시 등록
                if (!enemy.isActiveAndEnabled || enemy.IsDead) return;
                e = RegisterEnemy(enemy, null);
            }
            e.LastHitByPlayerTime = hitTime;

            if (_mainTarget == victim)
            {
                _mainLastHitTime = hitTime;
                return;
            }

            bool mainValid = IsValidMainTarget(_mainTarget);
            bool holdExpired = hitTime - _mainLastHitTime >= mainTargetHoldTime;
            if (!mainValid || holdExpired)
            {
                _mainTarget = victim;
                _mainLastHitTime = hitTime;
            }
            // else: 유지 시간 내 다른 적 적중 — Main 유지(광역기 튐 방지)
        }

        private Character FindAggressiveEnemy()
        {
            Character best = null;
            float bestOffset = float.MaxValue;
            foreach (var kv in _entries)
            {
                Entry e = kv.Value;
                if (e.Kind != FocusKind.Enemy || e.PendingRemoval || e.Transform == null) continue;
                var ec = kv.Key as EnemyCharacter;
                if (ec == null || ec.CombatRole != CombatRole.Aggressive) continue;
                if (!IsValidMainTarget(ec)) continue;
                float off = Mathf.Abs(BeltOffset(e.Transform.position));
                if (off < bestOffset) { bestOffset = off; best = ec; }
            }
            return best;
        }

        /// <summary>
        /// 외부(잡기/특수 연출 등)에서 Main Target을 강제 지정. null이면 해제.
        /// 지정한 대상은 hold 규칙에 따라 유지되며 무효화 시 일반 규칙으로 복귀한다.
        /// </summary>
        public void SetMainTarget(Character target)
        {
            if (target == null) { _mainTarget = null; return; }
            var enemy = target as EnemyCharacter;
            if (enemy == null || !enemy.isActiveAndEnabled || enemy.IsDead) return;
            if (!_entries.TryGetValue(enemy, out Entry e)) e = RegisterEnemy(enemy, null);
            e.LastHitByPlayerTime = Time.time;
            _mainTarget = enemy;
            _mainLastHitTime = Time.time;
        }

        // ─────────────── Focus (구 AirFocus) ───────────────

        /// <summary>
        /// 공중 콤보 등 특정 대상에 카메라를 집중. 활성 세트를 플레이어+대상으로 제한하고 대상 Weight를
        /// focusWeightMultiplier배로 올린다. solo=true면 대상만 담는다. restrictActiveSet=false면 다른 적도 유지(가중치만 상승).
        /// </summary>
        public void BeginFocus(Character target, bool solo = false, bool restrictActiveSet = true)
        {
            if (target == null) return;
            _focusActive = true;
            _focusSolo = solo;
            _focusRestrictSet = restrictActiveSet;
            _focusTarget = target;
            _focusEndRequested = false;
            if (target != _player && !_entries.ContainsKey(target) && target is EnemyCharacter ec && ec.isActiveAndEnabled)
                RegisterEnemy(ec, null);
        }

        /// <summary>포커스 해제 요청 — focusHoldDuration 유예 후 일반 구도로 복귀.</summary>
        public void EndFocus()
        {
            if (!_focusActive) return;
            _focusEndRequested = true;
            _focusEndRequestTime = Time.time;
        }

        private void ForceEndFocus()
        {
            _focusActive = false;
            _focusSolo = false;
            _focusRestrictSet = true;
            _focusTarget = null;
            _focusEndRequested = false;
        }

        private void UpdateFocusWatchdog()
        {
            if (!_focusActive) return;

            if (_focusEndRequested && Time.time - _focusEndRequestTime >= focusHoldDuration)
            {
                ForceEndFocus();
                return;
            }

            // 스턱 복구: 대상 소실/비활성/사망/과도한 이탈
            bool stuck = _focusTarget == null || !_focusTarget.isActiveAndEnabled || _focusTarget.IsDead;
            if (!stuck && focusStuckDistance > 0f && _playerEntry != null && _playerEntry.Transform != null)
                stuck = Vector3.Distance(_focusTarget.transform.position, _playerEntry.Transform.position) > focusStuckDistance;
            if (stuck) ForceEndFocus();
        }

        // ─────────────── Active Set (구버전 ComputeActiveSet) ───────────────

        private Transform CameraTransform
        {
            get
            {
                if (rig.LensNode != null && rig.LensNode.TargetCamera != null) return rig.LensNode.TargetCamera.transform;
                return Camera.main != null ? Camera.main.transform : null;
            }
        }

        private float _beltPlayerX;
        private Quaternion _invCamRot = Quaternion.identity;

        /// <summary>플레이어 기준 카메라 로컬 X(벨트축) 오프셋.</summary>
        private float BeltOffset(Vector3 worldPos) => (_invCamRot * worldPos).x - _beltPlayerX;

        private float HorizontalFovRad()
        {
            Camera cam = rig.LensNode != null ? rig.LensNode.TargetCamera : Camera.main;
            float vFov = cam != null ? cam.fieldOfView : 55f;
            float aspect = cam != null ? cam.aspect : 16f / 9f;
            return 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f * Mathf.Deg2Rad) * aspect);
        }

        private void ComputeActiveSet()
        {
            foreach (var kv in _entries)
            {
                kv.Value.WasActive = kv.Value.Active;
                kv.Value.Active = false;
            }
            if (_playerEntry == null || _playerEntry.Transform == null) return;

            Transform camTr = CameraTransform;
            _invCamRot = camTr != null ? Quaternion.Inverse(camTr.rotation) : Quaternion.identity;
            _beltPlayerX = (_invCamRot * _playerEntry.Transform.position).x;
            _playerEntry.LocalX = 0f;

            // 포커스: 플레이어 + 대상만
            if (_focusActive && _focusRestrictSet)
            {
                bool any = false;
                foreach (var kv in _entries)
                {
                    Entry e = kv.Value;
                    if (e.PendingRemoval || e.Transform == null) continue;
                    bool isFocus = kv.Key == _focusTarget;
                    bool isPlayer = e.Kind == FocusKind.Player;
                    if (_focusSolo ? isFocus : (isPlayer || isFocus))
                    {
                        e.LocalX = BeltOffset(e.Transform.position);
                        e.Active = true;
                        any = true;
                    }
                }
                if (any) return;
                // 폴백: 매칭 없음 → 일반 로직
            }

            _playerEntry.Active = true;

            // 화면 폭 cap (구버전: 2·(MaxDistance·tan(hFov/2) − Padding))
            float maxDist = useFramingDistance ? maxCombatDistance
                : (rig.SpringArmNode != null ? rig.SpringArmNode.CurrentDistance : maxCombatDistance);
            float maxHalfExtent = maxDist * Mathf.Tan(HorizontalFovRad() * 0.5f) - framingPadding;
            if (maxHalfExtent < 0f) maxHalfExtent = 0f;
            float hyst = Mathf.Max(1f, filterHysteresis);
            float capStrict = 2f * maxHalfExtent;
            float capHyst = capStrict * hyst;

            _enemyBuffer.Clear();
            foreach (var kv in _entries)
            {
                Entry e = kv.Value;
                if (e.Kind == FocusKind.Player || e.PendingRemoval || e.Transform == null) continue;
                e.LocalX = BeltOffset(e.Transform.position);

                // 영향 거리 필터 (hysteresis)
                if (otherEnemyInfluenceDistance > 0f)
                {
                    float limit = e.WasActive ? otherEnemyInfluenceDistance * hyst : otherEnemyInfluenceDistance;
                    if (Mathf.Abs(e.LocalX) > limit) continue;
                }
                _enemyBuffer.Add(e);
            }
            _enemyBuffer.Sort(s_beltOffsetComparer);

            float minX = 0f, maxX = 0f;
            for (int i = 0; i < _enemyBuffer.Count; i++)
            {
                Entry e = _enemyBuffer[i];
                float tMin = Mathf.Min(minX, e.LocalX);
                float tMax = Mathf.Max(maxX, e.LocalX);
                float cap = e.WasActive ? capHyst : capStrict;
                // 뒤쪽 적이 기존 범위 안이면 폭 불변 → 통과. early-break 금지(hysteresis로 뒤 항목이 통과할 수 있음)
                if (tMax - tMin <= cap)
                {
                    e.Active = true;
                    minX = tMin;
                    maxX = tMax;
                }
            }
        }

        // ─────────────── Weights ───────────────

        private float ResolveBaseWeight(Character c, Entry e)
        {
            if (e.Kind == FocusKind.Player) return playerWeight * e.BaseWeightMultiplier;
            float w = (c == _mainTarget ? mainTargetWeight : otherEnemyWeight) * e.BaseWeightMultiplier;
            if (_focusActive && c == _focusTarget) w *= focusWeightMultiplier;
            return w;
        }

        private void UpdateWeights(float dt)
        {
            float duration = _focusActive ? focusWeightTransitionDuration : weightTransitionDuration;
            foreach (var kv in _entries)
            {
                Entry e = kv.Value;
                if (e.PendingRemoval) e.TargetWeight = 0f;
                else if (e.Active) e.TargetWeight = ResolveBaseWeight(kv.Key, e);
                else e.TargetWeight = 0f;

                if (duration <= 0f || dt <= 0f)
                {
                    e.CurrentWeight = e.TargetWeight;
                    e.WeightVelocity = 0f;
                }
                else
                {
                    e.CurrentWeight = Mathf.SmoothDamp(
                        e.CurrentWeight, e.TargetWeight, ref e.WeightVelocity, duration, Mathf.Infinity, dt);
                }
            }
        }

        private void PushToFollowNode()
        {
            FollowCameraNode follow = rig.FollowNode;
            foreach (var kv in _entries)
            {
                Entry e = kv.Value;
                if (e.Transform == null) continue;
                if (e.Kind == FocusKind.Player)
                {
                    // 플레이어는 항상 유지(리스트 공백 시 FollowNode가 PlayerTarget을 Weight 1로 재등록하는 것 방지)
                    follow.AddTarget(e.Transform, Mathf.Max(e.CurrentWeight, WeightEpsilon));
                    continue;
                }
                if (e.CurrentWeight > WeightEpsilon) follow.AddTarget(e.Transform, e.CurrentWeight);
                else follow.RemoveTarget(e.Transform);
            }
        }

        private void CleanupCompletedRemovals()
        {
            _removalBuffer.Clear();
            foreach (var kv in _entries)
            {
                Entry e = kv.Value;
                if (e.Kind == FocusKind.Player) continue;
                if (e.PendingRemoval && e.CurrentWeight <= WeightEpsilon) _removalBuffer.Add(kv.Key);
            }
            for (int i = 0; i < _removalBuffer.Count; i++)
            {
                Entry e = _entries[_removalBuffer[i]];
                if (e.Transform != null) rig.FollowNode.RemoveTarget(e.Transform);
                if (_mainTarget == _removalBuffer[i]) _mainTarget = null;
                _entries.Remove(_removalBuffer[i]);
            }
            _removalBuffer.Clear();
        }

        // ─────────────── Framing Distance (구버전 ComputeDesiredDistance) ───────────────

        private float VerticalFovRad()
        {
            Camera cam = rig.LensNode != null ? rig.LensNode.TargetCamera : Camera.main;
            float vFov = cam != null ? cam.fieldOfView : 55f;
            return vFov * Mathf.Deg2Rad;
        }

        private void UpdateFramingDistance()
        {
            if (_distanceChannel == null) return;

            // 1패스: 외곽(min/max) + 가중 중심 (FollowNode.WeightedCenter와 같은 Weight → 실제 추적 중심과 일치)
            bool anyEnemy = false;
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            float wSum = 0f, cx = 0f, cy = 0f;
            foreach (var kv in _entries)
            {
                Entry e = kv.Value;
                if (e.Transform == null || e.CurrentWeight <= WeightEpsilon) continue;
                if (e.Kind == FocusKind.Enemy) anyEnemy = true;
                Vector3 local = _invCamRot * e.Transform.position;
                float x = local.x, y = local.y;
                if (x - e.Radius < minX) minX = x - e.Radius;
                if (x + e.Radius > maxX) maxX = x + e.Radius;
                if (y - e.Radius < minY) minY = y - e.Radius;
                if (y + e.Radius > maxY) maxY = y + e.Radius;
                cx += x * e.CurrentWeight;
                cy += y * e.CurrentWeight;
                wSum += e.CurrentWeight;
            }

            if (!useFramingDistance || !anyEnemy || float.IsInfinity(minX))
            {
                _distanceChannel.Active = false;
                UpdateMainTargetZoom(0f);
                debugFramingDistance = _distanceChannel.LastAppliedDistance;
                return;
            }

            float hFov = HorizontalFovRad();
            if (hFov <= 0.0001f) { _distanceChannel.Active = false; UpdateMainTargetZoom(0f); return; }

            // 반폭: 외곽 폭/2 (구버전) 또는 추적 중심에서 가장 먼 외곽까지 (중심 치우침 보정)
            float halfWidth, halfHeight;
            if (frameFromWeightedCenter && wSum > 0f)
            {
                cx /= wSum; cy /= wSum;
                halfWidth = Mathf.Max(cx - minX, maxX - cx);
                halfHeight = Mathf.Max(cy - minY, maxY - cy);
            }
            else
            {
                halfWidth = (maxX - minX) * 0.5f;
                halfHeight = (maxY - minY) * 0.5f;
            }
            halfWidth += framingPadding;
            halfHeight += verticalFramingPadding;

            float desired = halfWidth / Mathf.Tan(hFov * 0.5f);
            if (useVerticalFraming)
            {
                float vFov = VerticalFovRad();
                if (vFov > 0.0001f)
                    desired = Mathf.Max(desired, halfHeight / Mathf.Tan(vFov * 0.5f));
            }

            _distanceChannel.Active = true;
            _distanceChannel.DesiredDistance = desired;
            _distanceChannel.MinDistance = minCombatDistance;
            _distanceChannel.MaxDistance = maxCombatDistance;
            UpdateMainTargetZoom(ComputeMainTargetZoomGoal());
        }

        // ─────────────── Main Target Zoom ───────────────

        /// <summary>플레이어-Main Target 수평 거리 → 목표 줌 비율(0~1). Main 없음/조건 미충족이면 0.</summary>
        private float ComputeMainTargetZoomGoal()
        {
            if (!useMainTargetZoom || _mainTarget == null) return 0f;
            if (_playerEntry == null || _playerEntry.Transform == null) return 0f;
            if (!_entries.TryGetValue(_mainTarget, out Entry e) || e.PendingRemoval || e.Transform == null) return 0f;
            if (e.CurrentWeight <= WeightEpsilon) return 0f;

            Vector3 d = e.Transform.position - _playerEntry.Transform.position;
            d.y = 0f;
            float dist = d.magnitude;

            float start = Mathf.Max(zoomInStartDistance, 0f);
            float full = Mathf.Clamp(zoomInFullDistance, 0f, start);
            if (start <= 0f) return 0f;
            if (dist >= start) return 0f;
            if (dist <= full || start - full <= 0.0001f) return 1f;

            float t = 1f - (dist - full) / (start - full);
            return Mathf.SmoothStep(0f, 1f, t);
        }

        private void UpdateMainTargetZoom(float goal)
        {
            float dt = Time.deltaTime;
            if (zoomBlendSmoothTime <= 0f || dt <= 0f)
            {
                _zoomBlend = goal;
                _zoomBlendVelocity = 0f;
            }
            else
            {
                _zoomBlend = Mathf.SmoothDamp(_zoomBlend, goal, ref _zoomBlendVelocity, zoomBlendSmoothTime, Mathf.Infinity, dt);
            }
            if (_zoomBlend < 0.001f) _zoomBlend = 0f;

            _distanceChannel.ZoomBlend = _zoomBlend;
            _distanceChannel.ZoomScale = zoomInDistanceScale;
        }

        private void UpdateFollowChannel()
        {
            if (_followChannel == null) return;

            float enemyWeightSum = 0f;
            foreach (var kv in _entries)
            {
                if (kv.Value.Kind == FocusKind.Enemy) enemyWeightSum += kv.Value.CurrentWeight;
            }
            _followChannel.CombatOffset = combatFollowOffset;
            _followChannel.Blend = otherEnemyWeight > 0f ? Mathf.Clamp01(enemyWeightSum / otherEnemyWeight) : (enemyWeightSum > 0f ? 1f : 0f);
            _followChannel.MaxChaseDistance = _focusActive ? maxFocusChaseDistance : 0f;
            _followChannel.Anchor = _playerEntry != null ? _playerEntry.Transform : null;
        }

        private void UpdateDebugView()
        {
            debugMainTarget = _mainTarget;
            debugFocusActive = _focusActive;
            debugZoomBlend = _zoomBlend;
            int active = 0, registered = 0;
            foreach (var kv in _entries)
            {
                if (kv.Value.Kind != FocusKind.Enemy) continue;
                registered++;
                if (kv.Value.Active && !kv.Value.PendingRemoval) active++;
            }
            debugActiveEnemyCount = active;
            debugRegisteredEnemyCount = registered;
            if (_distanceChannel != null) debugFramingDistance = _distanceChannel.LastAppliedDistance;
        }
    }
}
