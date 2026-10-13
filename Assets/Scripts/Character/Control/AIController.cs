using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 씬 전체 AI 관리 컴포넌트 — Registry / Manager (씬 1개, Managers 아래).
    ///
    ///                  AIController (Registry)
    ///                 ┌─────────┼─────────┐
    ///          EnemyCharacter  EnemyCharacter  ...   ← 개별 AI Brain(판단)은 각 EnemyCharacter
    ///
    /// 담당: EnemyCharacter 등록/해제 관리 · Faction 적대 관계 판별(IsHostile) ·
    ///       Target 후보 제공(FindHostileTarget) · 전역 AI on/off(SetAIEnabled).
    /// 비담당: 개체의 판단(AIState·Target 선택·Chase/Attack 결정 = EnemyCharacter),
    ///       이동/전투 실행(Character 시스템), 특정 Character Possess(하지 않음).
    ///
    /// 중앙 Brain이 아니다 — 모든 판단은 EnemyCharacter가 자기 것만 한다.
    /// 등록은 EnemyCharacter.OnEnable/OnDisable 기반 자동(씬 배치·Instantiate·Destroy 안전).
    /// </summary>
    [DefaultExecutionOrder(-30)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Yeolha/AI/AI Controller")]
    public class AIController : MonoBehaviour
    {
        // ─────────── Singleton + 정적 등록 창구 ───────────

        private static AIController s_instance;
        /// <summary>도메인 리로드로 static이 유실돼도 씬에서 다시 찾는다(GameManager와 동일 패턴).</summary>
        public static AIController Instance
        {
            get
            {
                if (s_instance == null)
                    s_instance = FindFirstObjectByType<AIController>();
                return s_instance;
            }
        }

        /// <summary>AIController가 아직 없을 때(초기화 순서) 등록 요청을 보관하는 대기열.</summary>
        private static readonly List<EnemyCharacter> s_pending = new List<EnemyCharacter>();

        /// <summary>Fast Play Mode(Domain Reload 비활성) 대비 static 초기화.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            s_pending.Clear();
        }

        /// <summary>AI 자동 등록 진입점. EnemyCharacter.OnEnable에서 호출.</summary>
        public static void Register(EnemyCharacter character)
        {
            if (character == null) return;
            var controller = Instance;
            if (controller != null) controller.RegisterInternal(character);
            else if (!s_pending.Contains(character)) s_pending.Add(character);
        }

        /// <summary>AI 자동 해제 진입점. EnemyCharacter.OnDisable에서 호출.</summary>
        public static void Unregister(EnemyCharacter character)
        {
            if (character == null) return;
            s_pending.Remove(character);
            if (s_instance != null) s_instance.UnregisterInternal(character);
        }

        // ─────────── 설정 / 디버그 ───────────

        [Header("AI Control")]
        [Tooltip("전역 AI 판단 on/off. 꺼두면 등록된 모든 AI가 판단을 멈춘다(테스트·컷씬용)")]
        [SerializeField] private bool aiEnabled = true;

        [Header("Combat Coordination")]
        [Tooltip("동시에 공격 권한(Aggressive)을 가질 수 있는 최대 AI 수")]
        [SerializeField] private int maxAggressiveCount = 1;
        [Tooltip("역할 재평가 주기 (초). 매 프레임이 아니라 이 간격으로만 재배정")]
        [SerializeField] private float aggressorReevaluationInterval = 0.75f;
        [Tooltip("공격을 마친 AI가 다시 공격자로 선정되지 않는 최소 시간 (초)")]
        [SerializeField] private float attackTurnDelay = 0.4f;
        [Tooltip("비공격자 근접 견제(방어 공격)의 그룹 전체 최소 간격 (초)")]
        [SerializeField] private float defensivePokeInterval = 1.2f;
        [Tooltip("플레이어 기준 이 거리 안의 AI만 역할 조정 대상(교전 중). 밖은 Guard 처리")]
        [SerializeField] private float engagementRange = 12f;

        [Header("Debug (View Only)")]
        [Tooltip("현재 등록된 AI 수")]
        [SerializeField] private int registeredCount;
        [Tooltip("등록된 AI 캐릭터 목록")]
        [SerializeField] private List<EnemyCharacter> characters = new List<EnemyCharacter>();

        // ─────────── 전투 조정 상태 ───────────
        private float _nextReevalTime;
        private float _lastDefensivePokeTime = -999f;
        private readonly Dictionary<EnemyCharacter, float> _attackEndTimes = new Dictionary<EnemyCharacter, float>();
        private readonly List<EnemyCharacter> _engaged = new List<EnemyCharacter>();

        /// <summary>전역 AI 판단 허용 여부(컴포넌트 enable 포함). EnemyCharacter.TickAI가 조회.</summary>
        public bool AIEnabled => isActiveAndEnabled && aiEnabled;

        /// <summary>현재 등록된 AI 수.</summary>
        public int RegisteredCount => characters.Count;

        /// <summary>등록된 AI 목록(읽기 전용으로 사용할 것).</summary>
        public IReadOnlyList<EnemyCharacter> Characters => characters;

        /// <summary>전역 AI 판단 on/off. 꺼도 등록은 유지된다.</summary>
        public void SetAIEnabled(bool value) => aiEnabled = value;

        // ─────────── 수명 주기 ───────────

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Debug.LogWarning("[AIController] 중복 인스턴스 발견. 파괴합니다.", this);
                Destroy(gameObject);
                return;
            }
            s_instance = this;

            // 씬 로드 직후 상태 정리: 잔여 항목 제거 후 대기열 소화.
            characters.Clear();
            if (s_pending.Count > 0)
            {
                for (int i = 0; i < s_pending.Count; i++)
                    if (s_pending[i] != null) RegisterInternal(s_pending[i]);
                s_pending.Clear();
            }
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        private void RegisterInternal(EnemyCharacter character)
        {
            if (character == null) return;
            if (characters.Contains(character)) return;
            characters.Add(character);
            character.OnAttackFinished += HandleAttackFinished;
            registeredCount = characters.Count;
            _nextReevalTime = 0f; // 새 AI 합류 → 즉시 역할 재평가
        }

        private void UnregisterInternal(EnemyCharacter character)
        {
            if (character != null)
            {
                character.OnAttackFinished -= HandleAttackFinished;
                _attackEndTimes.Remove(character);
            }
            characters.Remove(character);
            registeredCount = characters.Count;
            _nextReevalTime = 0f; // 이탈 → 남은 AI 역할 재평가
        }

        /// <summary>Destroy 안전망: OnDisable을 못 거친 파괴 항목을 주기적으로 정리.</summary>
        private void Update()
        {
            for (int i = characters.Count - 1; i >= 0; i--)
                if (characters[i] == null) characters.RemoveAt(i);
            registeredCount = characters.Count;

            if (!AIEnabled) return;
            if (Time.time >= _nextReevalTime)
            {
                ReassignCombatRoles();
                _nextReevalTime = Time.time + Mathf.Max(0.05f, aggressorReevaluationInterval);
            }
        }

        // ─────────── Faction 적대 판별 ───────────

        /// <summary>
        /// 두 캐릭터가 서로 적대인가. 현재 규칙:
        ///   Enemy ↔ Player/Ally 적대 · 같은 진영 비적대 · Neutral은 항상 비적대.
        /// </summary>
        public static bool IsHostile(Character a, Character b)
        {
            if (a == null || b == null || a == b) return false;
            return IsHostile(a.Faction, b.Faction);
        }

        /// <summary>Faction 간 적대 규칙(대칭).</summary>
        public static bool IsHostile(FactionType a, FactionType b)
        {
            if (a == FactionType.Neutral || b == FactionType.Neutral) return false;
            bool aIsPlayerSide = a == FactionType.Player || a == FactionType.Ally;
            bool bIsPlayerSide = b == FactionType.Player || b == FactionType.Ally;
            return aIsPlayerSide != bIsPlayerSide; // 한쪽만 Player진영이면 적대 (나머지 = Enemy)
        }

        // ─────────── Target 후보 제공 ───────────

        /// <summary>
        /// 요청자와 적대인 가장 가까운(XZ 평면) 생존 Character 반환. 없으면 null.
        /// 후보 = 등록된 AI 목록 + 상주 Player(GameManager). 선택/유지 판단은 요청자의 몫.
        /// </summary>
        public Character FindHostileTarget(Character requester)
        {
            if (requester == null) return null;

            Character best = null;
            float bestSqr = float.MaxValue;

            // 등록된 AI들 중 적대 후보
            for (int i = characters.Count - 1; i >= 0; i--)
            {
                var c = characters[i];
                if (c == null) { characters.RemoveAt(i); continue; }
                Consider(requester, c, ref best, ref bestSqr);
            }

            // 상주 Player (Registry에는 없으므로 별도 후보)
            var gm = GameManager.Instance;
            if (gm != null) Consider(requester, gm.Player, ref best, ref bestSqr);

            registeredCount = characters.Count;
            return best;
        }

        private static void Consider(Character requester, Character candidate,
            ref Character best, ref float bestSqr)
        {
            if (candidate == null || candidate == requester) return;
            if (!candidate.gameObject.activeInHierarchy || candidate.IsDead) return;
            if (!IsHostile(requester, candidate)) return;

            Vector3 to = candidate.transform.position - requester.transform.position;
            to.y = 0f;
            float sqr = to.sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = candidate;
            }
        }


        // ─────────── 전투 역할 조정 (Aggressive / Pressure / Guard) ───────────

        /// <summary>공격 종료 통지 수신 — 최근 공격자로 기록하고 다음 공격자 선정을 앞당긴다.</summary>
        private void HandleAttackFinished(EnemyCharacter attacker)
        {
            if (attacker == null) return;
            _attackEndTimes[attacker] = Time.time;
            float soon = Time.time + Mathf.Max(0f, attackTurnDelay);
            if (soon < _nextReevalTime) _nextReevalTime = soon;
        }

        /// <summary>근접 예외(방어 공격) 허가. 그룹 전체 최소 간격으로 동시 난타를 막는다.</summary>
        public bool TryReserveDefensiveAttack(EnemyCharacter requester)
        {
            if (Time.time - _lastDefensivePokeTime < defensivePokeInterval) return false;
            _lastDefensivePokeTime = Time.time;
            return true;
        }

        /// <summary>
        /// 플레이어 기준으로 교전 중 AI의 전투 역할을 재배정한다(상위 명령만).
        /// 거리순 정렬 → 최근 공격자 제외 → 앞에서부터 Aggressive / Pressure / Guard.
        /// 개별 이동·공격 실행은 각 EnemyCharacter가 담당한다.
        /// </summary>
        private void ReassignCombatRoles()
        {
            Character anchor = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (anchor == null) return; // 조정 기준 부재 시 역할 유지(기본 Aggressive)

            Vector3 p = anchor.transform.position;
            float engageSqr = engagementRange * engagementRange;

            _engaged.Clear();
            for (int i = 0; i < characters.Count; i++)
            {
                var c = characters[i];
                if (c == null || c.IsDead) continue;
                if (!IsHostile(c, anchor)) continue;

                // 그로기/처형/그랩 중인 적은 공격 역할 선정에서 제외한다 — 상태가 끝나면 다음 재평가(0.75s)에서
                // 자동 복귀. (상태 소유는 Character/EnemyCharacter — 여기서는 참조만, 그로기 로직 없음.)
                if (IsIncapacitated(c))
                {
                    if (c.CombatRole != CombatRole.Guard) c.SetCombatRole(CombatRole.Guard);
                    continue;
                }

                // 등장 진입(Entering) 중인 적은 전투 역할 배분에서 제외한다.
                // 진입 상태가 대열 합류 이동을 스스로 관리하며 공격/공격권을 받지 않는다.
                if (c.IsEntering)
                {
                    if (c.CombatRole != CombatRole.Guard) c.SetCombatRole(CombatRole.Guard);
                    continue;
                }

                if (PlaneDistanceSqr(c.transform.position, p) > engageSqr)
                {
                    if (c.CombatRole != CombatRole.Guard) c.SetCombatRole(CombatRole.Guard);
                    continue;
                }
                _engaged.Add(c);
            }

            _engaged.Sort((a, b) =>
                PlaneDistanceSqr(a.transform.position, p)
                .CompareTo(PlaneDistanceSqr(b.transform.position, p)));

            int aggressive = 0;
            int pressure = 0;
            int total = _engaged.Count;
            for (int i = 0; i < total; i++)
            {
                var c = _engaged[i];

                bool recentlyAttacked =
                    _attackEndTimes.TryGetValue(c, out float endTime)
                    && Time.time - endTime < attackTurnDelay;

                CombatRole role;
                if (!recentlyAttacked && aggressive < maxAggressiveCount)
                {
                    role = CombatRole.Aggressive;
                    aggressive++;
                }
                else if (pressure < maxAggressiveCount)
                {
                    role = CombatRole.Pressure;
                    pressure++;
                }
                else
                {
                    role = CombatRole.Guard;
                }

                if (c.CombatRole != role) c.SetCombatRole(role);
                c.SetFormationSlot(i, total); // 대기 위치 분산(확장 훅)
            }
        }

        /// <summary>공격 역할(Aggressive/Pressure)을 받을 수 없는 무력화 상태(그로기/취약/처형/그랩)인가.</summary>
        private static bool IsIncapacitated(EnemyCharacter c)
        {
            if (c.IsGrabbed) return true;
            var s = c.CurrentState;
            return s == CharacterStateType.Groggy
                || s == CharacterStateType.Vulnerable
                || s == CharacterStateType.Executing
                || s == CharacterStateType.Executed;
        }

        private static float PlaneDistanceSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

    }
}

