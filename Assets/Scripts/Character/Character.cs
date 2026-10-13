using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 캐릭터 공용 통합 컴포넌트 — Composition Root / Facade (단일 MonoBehaviour).
    ///
    /// Player·Enemy·Boss 등 "캐릭터"는 GameObject에 <b>이 Character 컴포넌트 하나만</b> 붙인다.
    /// 적은 파생 클래스 EnemyCharacter(필요 시 적별 전용 파생)를 사용한다 — Unity 메시지는
    /// protected virtual이므로 파생에서 override 시 반드시 base 호출.
    /// 상태/이동/전투/명령은 각각의 <b>일반 C# 클래스</b>로 Character가 소유하며,
    /// 인스펙터에는 Character의 접이식 필드(Ability + 4개 시스템)로 노출된다. 상속 아님.
    ///
    ///   PlayerController ─┐(Possess)
    ///                     ├─▶ Character ── StateManager / Movement / Combat / CommandManager + Ability(SO)
    ///   AIController ─────┘
    ///
    /// Character는 참조 보유 + 초기화 순서 보장 + Ability 제공 + 단일 진입점만 담당한다.
    /// 실제 이동/전투/상태 로직은 각 시스템 클래스가 그대로 소유한다(God Object 아님).
    ///
    /// 실행 순서(DefaultExecutionOrder -10): PlayerController(-20)가 명령 세팅 →
    /// Character.Update가 StateManager.Tick → Movement.Tick 순으로 구동 → CameraRig(100).
    /// </summary>
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(CharacterController))]
    public class Character : MonoBehaviour, IDamageReceiver
    {
        [Header("Faction")] // (진영 — 적대 판별은 AIController.IsHostile)
        [Tooltip("이 캐릭터의 진영. 적대 여부 판별(AIController.IsHostile)에 사용")]
        [SerializeField] private FactionType faction = FactionType.Player;

        [Header("Ability")]// (캐릭터 기본 능력치 데이터 — SO)
        [Tooltip("이 캐릭터의 기본 능력치 프로필. 예: JangHyu_Ability")]
        [SerializeField] private CharacterAbility ability;

        [Header("Systems")] // (Character가 소유하는 일반 클래스)
        [SerializeField] private CharacterStateManager stateManager = new CharacterStateManager();
        [SerializeField] private CharacterMovement movement = new CharacterMovement();
        [SerializeField] private CharacterCombat combat = new CharacterCombat();
        [SerializeField] private CharacterCommandManager commandManager = new CharacterCommandManager();
        [SerializeField] private CharacterDefense defense = new CharacterDefense();

        /// <summary>이 캐릭터의 진영. Player·Enemy·Ally·Neutral 공통 정보.</summary>
        public FactionType Faction => faction;

        public CharacterAbility Ability => ability;
        public CharacterRuntimeStats Stats { get; private set; }

        public CharacterStateManager StateManager => stateManager;
        public CharacterMovement Movement => movement;
        public CharacterCombat Combat => combat;
        public CharacterCommandManager CommandManager => commandManager;
        public CharacterDefense Defense => defense;

        private bool _initialized;

        // ─────────── 활성 캐릭터 레지스트리 (소프트 콜리전용) ───────────
        // 캐릭터 간 하드 충돌(CharacterController 캡슐 밟고 올라가기·끼임)을 없애기 위해
        // 루트(캡슐 소유 오브젝트)를 CharacterBody 레이어로 옮긴다 — 매트릭스에서
        // CharacterBody↔CharacterBody만 꺼져 있어 캐릭터끼리는 서로 통과한다.
        // ※ Physics.IgnoreCollision은 CharacterController.Move 스윕에 적용되지 않아 레이어 방식 사용.
        // Hurtbox/AttackRoot는 자식 오브젝트로 Player/Enemy 레이어를 유지하므로 타격 판정 불변.
        // 겹침 분리는 CharacterMovement의 Separation 패스가 XZ 밀어내기로 처리한다.
        public const string BodyLayerName = "CharacterBody";

        private static readonly List<Character> s_active = new List<Character>();
        private static int s_bodyLayer = -2; // -2: 미조회, -1: 레이어 없음

        /// <summary>현재 활성(Enable) 상태인 모든 캐릭터. Separation 패스가 순회한다.</summary>
        public static IReadOnlyList<Character> ActiveCharacters => s_active;

        /// <summary>CharacterBody 레이어 인덱스(없으면 -1).</summary>
        public static int BodyLayer
        {
            get
            {
                if (s_bodyLayer == -2) s_bodyLayer = LayerMask.NameToLayer(BodyLayerName);
                return s_bodyLayer;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_active.Clear(); s_bodyLayer = -2; }

        protected virtual void Awake() => Initialize();

        /// <summary>참조·런타임 스탯 구성 + 각 시스템 OnAwake. 멱등.</summary>
        public void Initialize()
        {
            if (_initialized) return;

            if (stateManager == null) stateManager = new CharacterStateManager();
            if (movement == null) movement = new CharacterMovement();
            if (combat == null) combat = new CharacterCombat();
            if (commandManager == null) commandManager = new CharacterCommandManager();
            if (defense == null) defense = new CharacterDefense();

            Stats = new CharacterRuntimeStats();
            Stats.Bind(ability);

            movement.Bind(this);
            combat.Bind(this);
            stateManager.Bind(this);
            defense.Bind(this);

            movement.OnAwake();
            combat.OnAwake();
            stateManager.OnAwake();
            defense.OnAwake();

            _initialized = true;
        }

        protected virtual void OnEnable()
        {
            if (!_initialized) Initialize();
            if (!s_active.Contains(this)) s_active.Add(this);
            // 루트(캡슐)만 CharacterBody 레이어로 — 자식(Hurtbox 등) 레이어는 건드리지 않는다.
            if (BodyLayer >= 0) gameObject.layer = BodyLayer;
            else Debug.LogWarning($"[Character] '{BodyLayerName}' 레이어가 없어 캐릭터 간 하드 충돌이 유지됩니다. TagManager에 레이어를 추가하세요.", this);
            stateManager.OnEnableHook();
        }

        protected virtual void Start()
        {
            movement.OnStart();
            stateManager.OnStart();
        }

        protected virtual void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            stateManager.OnUpdate(dt); // 상태 Tick(+Combat.TickAttack) → 명령 소비
            movement.OnUpdate(dt);     // 실제 이동 적용
            combat.TickResources(dt);  // FP 자동 회복·투혼 리셋 타이머 (공격 흐름과 별개 상시 틱)
        }

        protected virtual void LateUpdate() => stateManager.OnLateUpdate();

        protected virtual void OnDisable()
        {
            s_active.Remove(this);
            movement.OnDisableCleanup();
            combat.OnDisableCleanup();
            commandManager.OnDisableCleanup();
        }

        // ─────────── Controller가 쓰는 얇은 Forward API (구현은 각 시스템) ───────────
        public void SetMoveInput(Vector2 input) => commandManager.SetMoveInput(input);
        public void SetRunHeld(bool held) => commandManager.SetRunHeld(held);
        public void RequestJump() => commandManager.RequestJump();
        public void RequestDash() => commandManager.RequestDash();
        public void SetGuardHeld(bool held) => commandManager.SetGuardHeld(held);
        public void RequestAttack(string attackName = "", AttackInputType inputType = AttackInputType.Light) => commandManager.RequestAttack(attackName, inputType);
        /// <summary>절명기(처형) 요청 — 그로기 적 처형. 판단/실행은 CharacterCombat이 소유.</summary>
        public void RequestExecution() => commandManager.RequestExecution();


        public CharacterStateType CurrentState
            => stateManager != null ? stateManager.CurrentStateType : CharacterStateType.Idle;

        // ─────────── 런타임 체력 (§6·§19·§22·§23·§39) ───────────

        /// <summary>최대 체력(런타임). Ability.MaxHP를 씨앗으로 한 CharacterRuntimeStats에서 조회.</summary>
        public float MaxHP => Stats != null ? Stats.FinalMaxHP : (ability != null ? ability.MaxHP : 0f);

        /// <summary>현재 체력(런타임). SO가 아니라 이 Character 인스턴스가 소유.</summary>
        public float CurrentHP => Stats != null ? Stats.CurrentHP : 0f;

        public bool IsDead => CurrentHP <= 0f;

        /// <summary>HP 변경 통지(현재, 최대). UI/HitFlash 등 확장점(§23).</summary>
        public event System.Action<float, float> OnHealthChanged;

        /// <summary>런타임 HP만 감소(0~MaxHP Clamp). SO(Ability)는 불변. 호출은 CharacterCombat.ReceiveDamage 경유.</summary>
        public void ApplyDamage(float amount)
        {
            if (Stats == null || amount <= 0f) return;
            Stats.CurrentHP = Mathf.Clamp(Stats.CurrentHP - amount, 0f, Stats.FinalMaxHP);
            OnHealthChanged?.Invoke(CurrentHP, MaxHP);
        }

        /// <summary>런타임 HP 회복(0~MaxHP Clamp). 처형 보상·힐 아이템 등 공용.</summary>
        public void Heal(float amount)
        {
            if (Stats == null || amount <= 0f) return;
            Stats.CurrentHP = Mathf.Clamp(Stats.CurrentHP + amount, 0f, Stats.FinalMaxHP);
            OnHealthChanged?.Invoke(CurrentHP, MaxHP);
        }

        /// <summary>CurrentHP를 MaxHP로 복구(Respawn·재사용 대비 §8).</summary>
        public void ResetHealth()
        {
            if (Stats == null) return;
            Stats.CurrentHP = Stats.FinalMaxHP;
            OnHealthChanged?.Invoke(CurrentHP, MaxHP);
        }

        // ─────────── 런타임 자원: FP / 투혼 (구버전 CharacterFP·BattleSpiritResource 이관) ───────────
        // 저장소는 CharacterRuntimeStats, 규칙(소비 게이트·회복·허주)은 CharacterCombat이 소유.
        // Character는 HP와 동일하게 원시 증감 + 변경 통지만 담당한다.

        /// <summary>최대 FP(런타임). Ability.MaxFP 씨앗.</summary>
        public float MaxFP => Stats != null ? Stats.FinalMaxFP : (ability != null ? ability.MaxFP : 0f);
        /// <summary>현재 FP(런타임).</summary>
        public float CurrentFP => Stats != null ? Stats.CurrentFP : 0f;
        /// <summary>FP 비율(0~1). UI 바인딩용.</summary>
        public float FPRatio => MaxFP > 0f ? CurrentFP / MaxFP : 0f;

        /// <summary>최대 투혼(런타임). Ability.MaxTouhon 씨앗. 0이면 투혼 시스템 비활성.</summary>
        public float MaxTouhon => Stats != null ? Stats.FinalMaxTouhon : (ability != null ? ability.MaxTouhon : 0f);
        /// <summary>현재 투혼(런타임).</summary>
        public float CurrentTouhon => Stats != null ? Stats.CurrentTouhon : 0f;
        /// <summary>투혼 비율(0~1). UI 바인딩용.</summary>
        public float TouhonRatio => MaxTouhon > 0f ? CurrentTouhon / MaxTouhon : 0f;

        /// <summary>FP 변경 통지(현재, 최대). HUD 확장점.</summary>
        public event System.Action<float, float> OnFPChanged;
        /// <summary>투혼 변경 통지(현재, 최대). HUD 확장점.</summary>
        public event System.Action<float, float> OnTouhonChanged;

        /// <summary>FP가 충분한지 (소모 없이 확인 — UI/게이트 힌트용).</summary>
        public bool HasEnoughFP(float amount) => amount <= 0f || CurrentFP >= amount;

        /// <summary>FP 소모 시도. 충분하면 소모 후 true, 부족하면 false(변경 없음).</summary>
        public bool TryConsumeFP(float amount)
        {
            if (amount <= 0f) return true;
            if (Stats == null || Stats.CurrentFP < amount) return false;
            Stats.CurrentFP -= amount;
            OnFPChanged?.Invoke(CurrentFP, MaxFP);
            return true;
        }

        /// <summary>FP 회복(0~MaxFP Clamp). 자동 회복·아이템 등 공용.</summary>
        public void RecoverFP(float amount)
        {
            if (Stats == null || amount <= 0f) return;
            float prev = Stats.CurrentFP;
            Stats.CurrentFP = Mathf.Min(Stats.CurrentFP + amount, Stats.FinalMaxFP);
            if (!Mathf.Approximately(prev, Stats.CurrentFP))
                OnFPChanged?.Invoke(CurrentFP, MaxFP);
        }

        /// <summary>FP를 최대치로 복구(Respawn 등).</summary>
        public void ResetFP()
        {
            if (Stats == null) return;
            Stats.CurrentFP = Stats.FinalMaxFP;
            OnFPChanged?.Invoke(CurrentFP, MaxFP);
        }

        /// <summary>투혼 값 직접 설정(0~MaxTouhon Clamp). 리셋 등 규칙 처리는 CharacterCombat이 호출.</summary>
        public void SetTouhon(float value)
        {
            if (Stats == null) return;
            float prev = Stats.CurrentTouhon;
            Stats.CurrentTouhon = Mathf.Clamp(value, 0f, Stats.FinalMaxTouhon);
            if (!Mathf.Approximately(prev, Stats.CurrentTouhon))
                OnTouhonChanged?.Invoke(CurrentTouhon, MaxTouhon);
        }

        
/// <summary>IDamageReceiver 진입점. 피격 처리 책임은 CharacterCombat에 위임(§9).</summary>
        public void ReceiveDamage(DamageInfo damage) => combat?.ReceiveDamage(damage);
    }
}
