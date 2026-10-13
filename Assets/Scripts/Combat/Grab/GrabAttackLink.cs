using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>Grab Visual 항목이 발동되는 잡기 시퀀스 시점.</summary>
    public enum GrabVisualTrigger
    {
        /// <summary>잡기 시퀀스 진입 순간 (대상 검증 통과·정렬 직후).</summary>
        GrabStart,
        /// <summary>시전자 클립 'Grab' 애니메이션 이벤트.</summary>
        GrabEvent,
        /// <summary>칼 박힘(무기 부착) 시작 순간 — AttachTiming(이벤트/시간)에 따라 발동.</summary>
        Impale,
        /// <summary>던지기(PerformThrow) 순간 — ThrowTiming(이벤트/시간)에 따라 발동.</summary>
        Throw,
        /// <summary>Takedown(슬램) 순간 — 피격자 클립 Takedown 이벤트 또는 폴백 시간.</summary>
        Takedown,
        /// <summary>잡기 종료(GrabFinish/안전 타임아웃) 순간.</summary>
        GrabEnd,
    }

    /// <summary>
    /// 잡기 시퀀스 중 재생할 VFX 항목 — AttackAction.TimedVisual의 잡기 전용 축소판.
    /// target은 AttackAction 프리팹 자식(Visual 하위 등)에 배치한 오브젝트. 잡기 진입 시 AttackAction 스윙은 취소되지만
    /// 이 항목들은 CharacterActionGrab이 잡기 타임라인으로 별도 재생한다.
    /// </summary>
    [System.Serializable]
    public class GrabVisual
    {
        [Tooltip("재생할 VFX 오브젝트 (AttackAction 프리팹 자식). 비우면 무시")]
        public GameObject target;
        [Tooltip("발동 시점 — 잡기 시퀀스의 어느 순간에 재생할지")]
        public GrabVisualTrigger trigger = GrabVisualTrigger.GrabStart;
        [Tooltip("발동 시점 이후 추가 지연(초, 잡기 타임라인 시간 — Speed Segments 구간 배속을 따라감. 구간 배속이 없으면 실시간과 동일). 0 = 즉시. ※ Animation Speed(전체 배속)와는 무관하므로 그쪽을 바꾸면 같이 조정")]
        [Min(0f)] public float delay;

        [Header("Persist Effect")]
        [Tooltip("체크하면 잡기가 끝나도 지속 시간 동안 계속 재생(월드 위치 고정)한 뒤 스스로 꺼진다. 끄면 잡기 종료 시 함께 꺼진다")]
        public bool persistEffect;
        [Tooltip("재생 순간부터의 총 지속 시간(초). 0 이하면 5초")]
        public float persistDuration = 3f;

        [Header("Feedback Delay")]
        [Tooltip("아래 연출(슬로우모션/카메라 쉐이크/줌/진동) 전체를 이펙트 재생 시점에서 추가로 미루는 시간(초, 잡기 타임라인 시간 — 구간 배속을 따라감). 0 = 이펙트와 동시. 잡기가 먼저 끝나면 남은 시간은 게임 시간으로 진행")]
        [Min(0f)] public float feedbackDelay;

        [Header("Slow Motion")]
        [Tooltip("이 이펙트 재생 순간 슬로우모션(전역 timeScale 감속). 동시 요청 중 가장 강한 배율만 적용(비가중)")]
        public bool useSlowMotion;
        [Tooltip("얼마나 느려질지. 0.3 = 30% 속도")]
        [Range(0f, 1f)] public float slowMotionScale = 0.3f;
        [Tooltip("유지 시간(초, 실시간)")]
        [Min(0f)] public float slowMotionDuration = 0.15f;
        [Tooltip("정상 → 슬로우 진입 시간(초). 0 = 즉시")]
        [Min(0f)] public float slowMotionEaseIn = 0.02f;
        [Tooltip("슬로우 → 정상 복귀 시간(초). 0 = 즉시")]
        [Min(0f)] public float slowMotionEaseOut = 0.2f;

        [Header("Camera Shake")]
        [Tooltip("이 이펙트 재생 순간 카메라 쉐이크 (CameraShakeService — 감쇠 Perlin 노이즈)")]
        public bool useCameraShake;
        [Tooltip("축별 진폭(m). 참고: 일반 타격 (0.3,0.3,0.3) / 체인 잡기 적중 (1.5,1.5,1.5)")]
        public Vector3 cameraShakeVelocity = new Vector3(0.3f, 0.3f, 0.3f);
        [Tooltip("쉐이크 지속 시간(초, 실시간)")]
        [Min(0f)] public float cameraShakeDuration = 0.2f;
        [Tooltip("노이즈 주파수(Hz). 높을수록 잘게 떨림")]
        [Min(0f)] public float cameraShakeFrequency = 25f;

        [Header("Camera Zoom")]
        [Tooltip("이 이펙트 재생 순간 카메라 줌 펀치(FOV)")]
        public bool useCameraZoom;
        [Tooltip("FOV 변화량(deg). 음수 = 줌인, 양수 = 줌아웃. 참고: 일반 -2 / 강타 -4")]
        public float cameraZoomFovDelta = -4f;
        [Tooltip("기본 FOV → 목표까지 진입 시간(초, 실시간)")]
        [Min(0f)] public float cameraZoomEaseIn = 0.05f;
        [Tooltip("목표 FOV 유지 시간(초, 실시간)")]
        [Min(0f)] public float cameraZoomHold = 0.1f;
        [Tooltip("목표 → 기본 FOV 복귀 시간(초, 실시간)")]
        [Min(0f)] public float cameraZoomEaseOut = 0.2f;

        [Header("Rumble")]
        [Tooltip("이 이펙트 재생 순간 패드 진동. 동시 요청 중 가장 강한 세기만 적용(비가중)")]
        public bool useRumble;
        [Range(0f, 1f)] public float rumbleLowFrequency = 0.5f;
        [Range(0f, 1f)] public float rumbleHighFrequency = 0.5f;
        [Tooltip("진동 지속 시간(초, 실시간)")]
        [Min(0f)] public float rumbleDuration = 0.15f;
    }

    /// <summary>
    /// 특정 공격이 적중한 직후에만 적용되는 잡기 사거리 오버라이드 (2026-09-14).
    /// 예: NormalAttack_3_Ground 적중 후 잡기 분기 시 그 적을 더 먼 거리에서도 잡는다.
    /// </summary>
    [System.Serializable]
    public class AttackGrabRangeOverride
    {
        [Tooltip("적중을 낸 공격의 AttackAction.AttackName (비어 있으면 AttackAction 오브젝트 이름, 예: NormalAttack_3_Ground)")]
        public string attackName;
        [Tooltip("이 공격이 맞은 직후 그 적을 잡을 수 있는 최대 거리(m). 0 이하 = Last Hit Grab Range 사용")]
        [Min(0f)] public float grabRange = 2.5f;
    }

    /// <summary>잡기 시퀀스 내부 이벤트(칼 박힘/던지기)의 발동 시점을 정하는 방식.</summary>
    /// <summary>Takedown(슬램) 순간 피격자 루트를 어디에 놓을지 (2026-09-09).</summary>
    public enum TakedownAnchorMode
    {
        /// <summary>피격자 자신의 모델(시각) 위치 + 전방 Slam Forward Distance — 잡힘 클립이 몸을 옮긴 자리 기준.</summary>
        VictimVisual,
        /// <summary>시전자 모델(시각) 위치 + 전방 Slam Forward Distance — 시전자 몸이 본 변위로 멀리 가 있어도 그 앞에 놓인다.</summary>
        CasterVisual,
    }

    public enum GrabTimingMode
    {
        /// <summary>시전자 클립의 애니메이션 이벤트(Grab / HeavyGrabThrow)가 발동 시점을 정한다.</summary>
        AnimationEvent,
        /// <summary>잡기 시작 후 지정 초(잡기 타임라인 시간 — 구간 배속 반영)에 발동한다. 애니 이벤트는 무시된다.</summary>
        TimeFromGrabStart,
    }

    /// <summary>칼에 박히는 순간 피격자가 재생할 모션.</summary>
    public enum GrabVictimHitMotion
    {
        /// <summary>뒤로 맞는 경직(Hit 트리거 → DamagedBackward). 기존 기본값.</summary>
        DamagedBackward,
        /// <summary>앞으로 맞는 경직(DamagedForward 트리거).</summary>
        DamagedForward,
        /// <summary>잡힘 모션(ActionGrabbed 트리거) — GrabType 슬롯 잡힘 애니.</summary>
        ActionGrabbed,
        /// <summary>임의 Animator 상태 경로를 CrossFade로 직접 재생 (예: "DamagedBackward.DamagedBackward").</summary>
        AnimatorState,
        /// <summary>모션을 바꾸지 않는다 (현재 포즈 유지 → 정지 지연 후 그대로 정지).</summary>
        None,
    }

    /// <summary>
    /// AttackAction 프리팹에 함께 붙여 "이 공격은 잡기(ActionGrab) 진입 공격"임을 표시하는 데이터 컴포넌트.
    ///
    /// 콤보 Transition이 이 AttackAction으로 분기되면 CharacterActionGrab이 이 데이터를 읽어
    /// 대상 검증 후 잡기 시퀀스로 전환한다. 대상이 없거나 잡을 수 없으면 전환하지 않고
    /// 이 AttackAction의 일반 스윙(폴백 공격)이 그대로 재생된다 (구버전 IComboBranchGate 대응).
    ///
    /// 인스펙터 구성 (위→아래 = 잡기 진행 순서):
    ///   공통: Grab Type / Target / Damage / Positioning (Grab Start) / Feedback / Animation Speed
    ///   ActionGrab_B(칼 박힘→던지기): Impale 1 Timing → Impale 2 Victim Motion → Impale 3 Victim Position
    ///                                → Throw 1 Timing → Throw 2 Direction → Throw 3 Reaction
    ///   ActionGrab_A(슬램): Takedown Reaction / Positioning (Takedown / End) / Victim Recovery
    /// </summary>
    [RequireComponent(typeof(AttackAction))]
    [AddComponentMenu("Yeolha/Combat/Grab Attack Link")]
    public class GrabAttackLink : MonoBehaviour, IComboBranchGate
    {
        // ─────────────────────────── 공통 ───────────────────────────

        [Header("Grab Type")]
        [Tooltip("사용할 GrabType (Animator ActionGrab1~9 슬롯). 음수 = 피격자의 기본 GrabType 사용")]
        [SerializeField] private int grabType = -1;

        [Header("Target")]
        [Tooltip("잡을 대상이 없으면 이 공격으로 분기하지 않는다(콤보 Transition/시동기 게이트) — 헛스윙(폴백 스윙) 방지. " +
                 "false = 대상이 없어도 분기해 이 AttackAction의 일반 스윙을 폴백 공격으로 재생 (2026-09-08)")]
        [SerializeField] private bool requireTarget = true;
        [Tooltip("직전 콤보 적중 대상을 우선 잡는다 (구버전 LastHitHealth). false = 항상 근접 탐색")]
        [SerializeField] private bool preferLastHitTarget = true;
        [Tooltip("직전 적중을 잡기 대상으로 인정하는 시간(초)")]
        [SerializeField] private float lastHitWindow = 1.5f;
        [Tooltip("잡기 가능 최대 거리(m) (구버전 근접 그랩 탐색 반경)")]
        [SerializeField] private float grabRange = 2.2f;
        [Tooltip("직전 적중 대상에 한해 허용하는 최대 거리(m). 콤보 적중 넉백으로 grabRange 밖(약 2.4m)까지 밀려난 적도 잡는다 " +
                 "(구버전 LastHitHealth 경로는 거리 검사 없음). 0 이하 = grabRange 사용. 정렬(AlignVictim)이 대상을 앵커로 당기므로 넓혀도 안전")]
        [SerializeField, Min(0f)] private float lastHitGrabRange = 3.5f;
        [Tooltip("특정 공격이 적중한 직후(Last Hit Window 안)에만 적용할 사거리. 직전 적중 공격의 AttackName이 목록에 있으면 그 값이 Last Hit Grab Range보다 우선. " +
                 "목록에 없거나 헛스윙 후면 기존 규칙(Last Hit Grab Range / Grab Range) (2026-09-14)")]
        [SerializeField] private List<AttackGrabRangeOverride> attackRangeOverrides = new List<AttackGrabRangeOverride>();

        [Header("Damage")]
        [Tooltip("잡기 데미지 = 공격력 × 이 배율. 던지기(Throw) 또는 Takedown(슬램) 시점에 적용")]
        [SerializeField] private float damageMultiplier = 1.5f;
        [Tooltip("피격자 클립의 Takedown 이벤트가 유실됐을 때 데미지를 적용할 폴백 시간(초) — 슬램 잡기(A) 전용")]
        [SerializeField] private float takedownFallbackTime = 1.2f;

        [Header("Positioning (Grab Start)")]
        [Tooltip("[적 위치 ①] 잡기 시작 순간 피격자를 정렬할 전방 거리(m) — 시전자 CharacterActionPoints GrabPoint(GrabType별) 미설정 시 사용")]
        [SerializeField] private float alignDistance = 1.4f;
        [Tooltip("[적 위치 ①] 잡기 시작 정렬 위치의 추가 오프셋 (오른쪽 바라볼 때 기준, 스플라인 축): x=전방(+앞), y=높이, z=깊이(카메라 반대쪽 +). " +
                 "Flip 시 전방(x) 성분은 자동 반전. 칼에 박히기(Impale) 전까지의 위치 — 박힌 뒤에는 Impale 3 Victim Position이 위치를 소유")]
        [SerializeField] private Vector3 victimOffset = Vector3.zero;

        [Header("Feedback")]
        [Tooltip("시전자 클립 Grab 이벤트 시 재생할 FeedbackManager 키. 비우면 생략")]
        [SerializeField] private string grabFeedbackKey = "Grab";
        [Tooltip("Takedown(슬램) 시 재생할 FeedbackManager 키. 비우면 생략")]
        [SerializeField] private string takedownFeedbackKey = "Hit";
        [Tooltip("Takedown(슬램) 시 적이 땅에 부딪히는 랜딩 이펙트 FeedbackManager 키 — 적 시각 위치(지면)에서 재생. 비우면 생략")]
        [SerializeField] private string takedownLandingFeedbackKey = "Landing";
        [Tooltip("랜딩 이펙트 재생 시점 — Takedown 이벤트 후 지연(초). 0 = 즉시")]
        [SerializeField] private float takedownLandingDelay = 0f;
        [Tooltip("랜딩 이펙트 위치 오프셋 (적 시각 위치 기준, 오른쪽 바라볼 때 스플라인 축): x=전방, y=높이, z=깊이. Flip 시 전방(x) 자동 반전")]
        [SerializeField] private Vector3 takedownLandingOffset = Vector3.zero;

        [Header("Grab Visuals")]
        [Tooltip("잡기 시퀀스 중 재생할 VFX 타임라인. 잡기 진입 시 AttackAction의 Attack Visuals(스윙 이펙트)는 취소되므로 잡기 중 나와야 하는 이펙트는 여기 등록한다. " +
                 "target은 이 AttackAction 프리팹 자식에 배치(프리팹 모드 기준 = 왼쪽 바라볼 때 포즈, 오른쪽은 자동 반전). 대상이 없어 폴백 스윙으로 갈 때는 재생되지 않는다 (2026-09-08)")]
        [SerializeField] private List<GrabVisual> grabVisuals = new List<GrabVisual>();
        [Tooltip("Grab Visual 좌/우 반전은 개별 이펙트가 아니라 AttackAction의 Visual 컨테이너 스케일로 처리한다(캐릭터 Visual 루트와 같은 방식, 이펙트 트랜스폼은 건드리지 않음). " +
                 "false = 프리팹의 이펙트 배치가 '오른쪽 바라볼 때' 기준(다른 공격과 동일) → 왼쪽 볼 때 반전. true = '왼쪽 바라볼 때' 기준 → 오른쪽 볼 때 반전")]
        [SerializeField] private bool visualsAuthoredFacingLeft = false;

        [Header("Animation Speed")]
        [Tooltip("시전자 잡기 모션 배속 (1 = 기본). 잡기 시작~종료 동안 시전자 Animator.speed에 곱하고 종료 시 기본 속도로 복원. 피격자 잡힘 애니 시작 지연도 같은 비율로 줄어든다. " +
                 "※ 아래 '초 단위' 타이밍 필드는 이 배속과 무관(잡기 타임라인 기준)이라 이 값을 바꾸면 같이 조정해야 한다. 구간만 빠르게 하려면 Speed Segments 사용")]
        [SerializeField, Min(0.05f)] private float animationSpeed = 1f;
        [Tooltip("피격자 잡힘 모션 배속. 0 이하 = 시전자와 동일(동기 유지)")]
        [SerializeField] private float victimAnimationSpeed = 0f;
        [Tooltip("구간 배속 (2026-09-15). 애니 클립 편집하듯이 '잡기 시작 후 이 시간 구간만 N배'로 조정한다. 시간은 잡기 타임라인 초(attachTime/throwTime/GrabVisual delay와 같은 자). " +
                 "가속된 구간에서는 시전자·피격자 모션과 잡기 타임라인(박힘/던지기/랜딩/Grab Visual/연출 딜레이 등)이 전부 같은 비율로 따라간다. Animation Speed(전체 배속)에 곱해진다. 비어 있으면 변화 없음")]
        [SerializeField] private MotionSpeedTimeline speedSegments = new MotionSpeedTimeline();

        // ─────────────────── ActionGrab_B: 칼 박힘 (Impale) ───────────────────

        [Header("Impale 1 - Timing (ActionGrab_B)")]
        [Tooltip("[박힘 ON/OFF] 피격자를 무기 본 앵커에 부착해 칼에 박힌 채 따라가게 한다 (구버전 SyncHeavyGrabAttachment). 끄면 아래 Impale 항목 전부 무시")]
        [SerializeField] private bool attachVictimToWeapon = false;
        [Tooltip("[박힘 시점] AnimationEvent = 시전자 클립의 'Grab' 이벤트 순간 / TimeFromGrabStart = 잡기 시작 후 attachTime초(잡기 타임라인)에 박힘")]
        [SerializeField] private GrabTimingMode attachTiming = GrabTimingMode.AnimationEvent;
        [Tooltip("[박힘 시점] attachTiming=TimeFromGrabStart일 때, 잡기 시작 후 몇 초(잡기 타임라인 — 구간 배속 반영)에 칼에 박힐지")]
        [SerializeField, Min(0f)] private float attachTime = 0.2f;
        [Tooltip("무기 앵커 Transform 이름 — 시전자 계층에서 이름으로 탐색 (구버전 Big_Sword_GrapPoint)")]
        [SerializeField] private string weaponAnchorName = "Big_Sword_GrapPoint";
        [Tooltip("칼이 꽂히는 순간(부착 시작) 피격자 위치에서 재생할 FeedbackManager 키. 비우면 생략")]
        [SerializeField] private string attachImpaleFeedbackKey = "";

        [Header("Impale 2 - Victim Motion (ActionGrab_B)")]
        [Tooltip("[박히기 전 적 모션] 잡기 시작 순간(정렬 직후)부터 칼에 박힐 때까지 피격자가 재생할 모션. 기본 DamagedBackward = 경직 포즈로 대기 — " +
                 "가만히 서 있는(Idle) 구간 제거. None = 현재 모션 유지(직전 피격 경직이 끝났으면 Idle이 잠깐 보일 수 있음) (2026-09-08)")]
        [SerializeField] private GrabVictimHitMotion preImpaleVictimMotion = GrabVictimHitMotion.DamagedBackward;
        [Tooltip("[박힐 때 적 모션] DamagedBackward=뒤로 맞는 경직(기본) / DamagedForward=앞으로 맞는 경직 / ActionGrabbed=잡힘 모션 / " +
                 "AnimatorState=아래 상태 경로 직접 재생 / None=모션 유지(박히기 전 모션 그대로 → 정지 지연 후 굳음)")]
        [SerializeField] private GrabVictimHitMotion attachVictimMotion = GrabVictimHitMotion.DamagedBackward;
        [Tooltip("attachVictimMotion=AnimatorState일 때 재생할 피격자 Animator 상태 경로 (예: DamagedBackward.DamagedBackward)")]
        [SerializeField] private string attachVictimStateName = "DamagedBackward.DamagedBackward";
        [Tooltip("AnimatorState 재생 시 CrossFade 시간(초)")]
        [SerializeField, Min(0f)] private float attachVictimCrossFade = 0.05f;
        [Tooltip("[포즈 정지] 박힘 시작 후 피격자 애니메이터를 정지시켜 '박힌 포즈'로 굳힐 때까지의 지연(초). 음수 = 정지 안 함(모션 계속 재생) (구버전 HeavyGrabHitPoseFreezeDelay)")]
        [SerializeField] private float attachPoseFreezeDelay = 0.1f;

        [Header("Impale 3 - Victim Position (ActionGrab_B)")]
        [Tooltip("[적 위치 ②] 박힌 동안 피격자 위치 추가 오프셋 — 무기 앵커 로컬 축 기준(칼 회전을 따라 같이 돈다). 칼날 위 어디에 박힐지 조정")]
        [SerializeField] private Vector3 attachOffset = Vector3.zero;
        [Tooltip("[적 각도] 박힌 동안 피격자 회전 추가 오프셋(오일러, 도) — 무기 앵커 로컬 축 기준. 박힌 몸의 기울기 조정")]
        [SerializeField] private Vector3 attachRotationOffset = Vector3.zero;

        // ─────────────────── ActionGrab_B: 던지기 (Throw) ───────────────────

        [Header("Throw 1 - Timing (ActionGrab_B)")]
        [Tooltip("[던지기 ON/OFF] 피격자를 에어본으로 던진다 (구버전 KatanaGrab launchMode). 켜면 Takedown(슬램)은 생략. 끄면 슬램 잡기(A)")]
        [SerializeField] private bool enableThrow = false;
        [Tooltip("[던지는 시점] AnimationEvent = 시전자 클립의 'HeavyGrabThrow' 이벤트 순간 / TimeFromGrabStart = 잡기 시작 후 throwTime초(잡기 타임라인)에 던짐")]
        [SerializeField] private GrabTimingMode throwTiming = GrabTimingMode.AnimationEvent;
        [Tooltip("[던지는 시점] throwTiming=TimeFromGrabStart일 때, 잡기 시작 후 몇 초(잡기 타임라인 — 구간 배속 반영)에 던질지. attachTime보다 커야 박힌 뒤 던진다")]
        [SerializeField, Min(0f)] private float throwTime = 0.8f;

        [Header("Throw 2 - Direction (Backward Flip, A/B)")]
        [Tooltip("잡는 중 방향키(뒤/아래)로 시전자를 뒤로 돌려 던지기/슬램 방향 반전 허용 (구버전 HeavyThrowBackward 모니터). 던지기(B)뿐 아니라 슬램 잡기(A)에도 적용")]
        [SerializeField] private bool allowBackwardThrow = true;
        [Tooltip("뒤로 플립 시점 — 잡기 시작 후 몇 초 뒤에 (그때까지 누르고 있던 방향키 기준으로) 시전자를 뒤로 돌릴지. 0 이하 = 던지기는 던지는 순간, 슬램 잡기는 Takedown 순간에 플립. 플립 후에는 방향키를 더 읽지 않는다")]
        [SerializeField] private float throwFlipDelay = 0f;
        [Tooltip("뒤로 플립 시 시전자가 새 방향으로 앞으로 이동할 거리(m). 플립 이후 모션의 본 변위는 상쇄되고, 플립 지점에서 이 거리만큼 앞으로 간 자리에 몸이 고정된다. " +
                 "0 = 플립한 자리 그대로, 음수 = 뒤로. 피격자(무기 부착 중 제외)도 같은 양만큼 따라간다(동기 유지)")]
        [SerializeField] private float flipForwardDistance = 0f;
        [Tooltip("플립 전방 이동에 걸리는 시간(초). 0 = 플립 순간 즉시 이동")]
        [SerializeField, Min(0f)] private float flipForwardDuration = 0.2f;

        [Header("Throw 3 - Reaction (ActionGrab_B)")]
        [Tooltip("[날아가는 방식] 던지기 시 피격자에게 적용할 반응 한 벌 — Kind + 공용 힘 + Ground Bounce 조합. 보통 Kind=Launch. 항상 시전자가 바라보는 방향으로 날아간다")]
        [SerializeField] private HitReactionSpec throwReaction = new HitReactionSpec { Kind = HitReactionKind.Launch, HorizontalForce = 7f, VerticalForce = 5f, AirTime = 0.3f, PushDuration = 0.25f, BouncePower = 8f, BounceDecay = 3f };
        [Tooltip("던져진 적이 다른 적과 부딪힐 때 줄 몸통 충돌 피해 배율(공격력 기준). 0 = 없음")]
        [SerializeField] private float throwBodyImpactMultiplier = 0f;

        // ─────────────────── ActionGrab_A: 슬램 (Takedown) ───────────────────

        [Header("Takedown Reaction (ActionGrab_A)")]
        [Tooltip("Takedown(슬램) 순간 피격자에게 적용할 반응. Kind=None이면 기존 슬램 연출(슬라이드→그랩 종료 시 넉다운) 그대로. Kind를 지정하면 슬램 순간 피격자를 조기 해제하고 이 반응을 정식 피격 파이프라인으로 전달한다(예: Slam+Bounce = 바닥에 박히는 시점에 튕김)")]
        [SerializeField] private HitReactionSpec takedownReaction = new HitReactionSpec { Kind = HitReactionKind.None, VerticalForce = 12f, PushDuration = 0.25f, BouncePower = 8f, BounceDecay = 3f };

        [Header("Positioning (Takedown / End)")]
        [Tooltip("Takedown(슬램) 순간 피격자를 놓을 기준. VictimVisual = 피격자 잡힘 클립이 몸을 옮긴 자리(기본) + Slam Forward Distance. " +
                 "CasterVisual = 시전자 모델(시각) 위치 + Slam Forward Distance — 시전자 몸이 앞으로 크게 이동하는 슬램(ActionGrab_A)에서 적을 그 앞에 놓을 때 (2026-09-09)")]
        [SerializeField] private TakedownAnchorMode takedownAnchor = TakedownAnchorMode.VictimVisual;
        [Tooltip("Takedown(슬램) 시 적이 놓이는 위치의 전방 오프셋(m) — Takedown Anchor 기준점(피격자 몸 / 시전자 몸)에서 시전자 전방으로. 0 = 기준점 그대로(피격자 몸이면 슬램된 자리에서 안 움직임). 음수 = 뒤")]
        [SerializeField] private float slamForwardDistance = 2.4f;
        [Tooltip("잡기 종료 시 시전자 루트가 정착하는 위치의 추가 전방 오프셋(m). 기본 위치는 종료 순간 모델(시각)이 서 있는 자리로 자동 측정되므로 보통 0. 음수 = 뒤로 (2026-09-06: 고정 거리 → 시각 위치 기준)")]
        [SerializeField] private float endForwardDistance = 0f;
        [Tooltip("잡기 종료(넉다운 해제) 시 피격자 루트가 놓이는 위치의 추가 전방 오프셋(m). 기본 위치는 종료 순간 피격자 모델(시각)이 있는 자리로 자동 측정되므로 보통 0. 음수 = 뒤로. Flip 자동 반영")]
        [SerializeField] private float victimEndForwardOffset = 0f;

        [Header("Victim Recovery")]
        [Tooltip("잡기 종료 후 적이 기상(Getup)을 시작하기까지의 시간(초) (구버전 GrabRecoveryDuration)")]
        [SerializeField] private float victimRecoveryDelay = 1.5f;
        [Tooltip("기상 애니메이션 시간(초) — 경과 후 적 AI/이동 복구 (구버전 GetupMovementLockDuration)")]
        [SerializeField] private float victimGetupDuration = 0.9f;

        [Header("Safety")]
        [Tooltip("시전자 클립 GrabFinish 이벤트가 유실됐을 때 강제 종료 시간(초) (구버전 GrabSafetyTimeout)")]
        [SerializeField] private float safetyTimeout = 6f;

        // ─────────────────────────── API ───────────────────────────

        public int GrabType => grabType;
        public bool RequireTarget => requireTarget;
        public bool PreferLastHitTarget => preferLastHitTarget;

        /// <summary>
        /// 콤보 분기 게이트 — requireTarget이면 시전자의 CharacterActionGrab으로 잡기 가능 대상 유무를 검사한다.
        /// 대상 없음 = false → CharacterCombat이 이 분기를 건너뛰어 헛스윙이 나오지 않는다.
        /// CharacterActionGrab이 없는 캐릭터는 잡기를 수행할 수 없으므로 항상 false.
        /// </summary>
        public bool CanBranch(Character attacker)
        {
            if (!requireTarget) return true;
            if (attacker == null) return false;
            var grab = attacker.GetComponent<CharacterActionGrab>();
            return grab != null && grab.HasGrabbableTarget(this);
        }
        public float LastHitWindow => lastHitWindow;
        public float GrabRange => grabRange;
        /// <summary>직전 적중 대상 전용 최대 거리. 0 이하면 GrabRange (2026-09-05).</summary>
        public float LastHitGrabRange => lastHitGrabRange > 0f ? lastHitGrabRange : grabRange;
        /// <summary>공격별 잡기 사거리 오버라이드 목록 (2026-09-14).</summary>
        public IReadOnlyList<AttackGrabRangeOverride> AttackRangeOverrides => attackRangeOverrides;
        /// <summary>
        /// 직전 적중 공격 이름에 따른 '직전 적중 대상' 잡기 사거리 — 오버라이드 목록에 있으면 그 값, 없으면 LastHitGrabRange (2026-09-14).
        /// </summary>
        public float ResolveLastHitGrabRange(string lastHitAttackName)
        {
            if (!string.IsNullOrEmpty(lastHitAttackName) && attackRangeOverrides != null)
            {
                for (int i = 0; i < attackRangeOverrides.Count; i++)
                {
                    var o = attackRangeOverrides[i];
                    if (o == null || string.IsNullOrEmpty(o.attackName)) continue;
                    if (string.Equals(o.attackName, lastHitAttackName, System.StringComparison.Ordinal))
                        return o.grabRange > 0f ? o.grabRange : LastHitGrabRange;
                }
            }
            return LastHitGrabRange;
        }
        public float DamageMultiplier => damageMultiplier;
        public float TakedownFallbackTime => takedownFallbackTime;
        public float AlignDistance => alignDistance;
        public Vector3 VictimOffset => victimOffset;
        public float SlamForwardDistance => slamForwardDistance;
        /// <summary>Takedown 순간 피격자 배치 기준 (2026-09-09).</summary>
        public TakedownAnchorMode TakedownAnchor => takedownAnchor;
        public float EndForwardDistance => endForwardDistance;
        public float VictimEndForwardOffset => victimEndForwardOffset;
        public float VictimRecoveryDelay => victimRecoveryDelay;
        public float VictimGetupDuration => victimGetupDuration;
        public string GrabFeedbackKey => grabFeedbackKey;
        public string TakedownFeedbackKey => takedownFeedbackKey;
        public string TakedownLandingFeedbackKey => takedownLandingFeedbackKey;
        public float TakedownLandingDelay => takedownLandingDelay;
        public Vector3 TakedownLandingOffset => takedownLandingOffset;
        /// <summary>잡기 시퀀스 VFX 타임라인 (2026-09-08). CharacterActionGrab이 트리거 시점마다 재생.</summary>
        public IReadOnlyList<GrabVisual> GrabVisuals => grabVisuals;
        /// <summary>Grab Visual 프리팹 배치 기준이 왼쪽 바라볼 때인가 (true면 오른쪽 볼 때 컨테이너 반전).</summary>
        public bool VisualsAuthoredFacingLeft => visualsAuthoredFacingLeft;
        /// <summary>시전자 잡기 모션 배속 (1 = 기본).</summary>
        public float AnimationSpeed => Mathf.Max(0.05f, animationSpeed);
        /// <summary>피격자 잡힘 모션 배속 — 0 이하면 시전자 배속을 따른다.</summary>
        public float VictimAnimationSpeed => victimAnimationSpeed > 0f ? victimAnimationSpeed : AnimationSpeed;
        /// <summary>구간 배속 데이터 (2026-09-15). CharacterActionGrab이 잡기 타임라인 시각으로 평가해 Animator 배속·타임라인 진행에 곱한다.</summary>
        public MotionSpeedTimeline SpeedSegments => speedSegments;
        /// <summary>grabTime(잡기 타임라인 초)에서의 구간 배속 배율. 구간 밖 = 1.</summary>
        public float EvaluateSpeedSegment(float grabTime)
            => speedSegments != null ? speedSegments.Evaluate(grabTime) : 1f;

        // Impale
        public bool AttachVictimToWeapon => attachVictimToWeapon;
        /// <summary>칼 박힘 발동 방식 — AnimationEvent('Grab') 또는 잡기 시작 후 AttachTime초 (2026-09-07).</summary>
        public GrabTimingMode AttachTiming => attachTiming;
        /// <summary>AttachTiming=TimeFromGrabStart일 때 박힘 시점(잡기 시작 후 실시간 초).</summary>
        public float AttachTime => attachTime;
        public string WeaponAnchorName => weaponAnchorName;
        public string AttachImpaleFeedbackKey => attachImpaleFeedbackKey;
        /// <summary>박히기 전(잡기 시작~박힘) 피격자 모션 (2026-09-08). 부착 잡기에서만 사용.</summary>
        public GrabVictimHitMotion PreImpaleVictimMotion => preImpaleVictimMotion;
        /// <summary>박힐 때 피격자 모션 선택 (2026-09-07).</summary>
        public GrabVictimHitMotion AttachVictimMotion => attachVictimMotion;
        /// <summary>AttachVictimMotion=AnimatorState일 때 재생할 상태 경로.</summary>
        public string AttachVictimStateName => attachVictimStateName;
        public float AttachVictimCrossFade => attachVictimCrossFade;
        public float AttachPoseFreezeDelay => attachPoseFreezeDelay;
        public Vector3 AttachOffset => attachOffset;
        /// <summary>박힌 동안 피격자 회전 오프셋(오일러, 앵커 로컬) (2026-09-07).</summary>
        public Vector3 AttachRotationOffset => attachRotationOffset;

        // Throw
        public bool EnableThrow => enableThrow;
        /// <summary>던지기 발동 방식 — AnimationEvent('HeavyGrabThrow') 또는 잡기 시작 후 ThrowTime초 (2026-09-07).</summary>
        public GrabTimingMode ThrowTiming => throwTiming;
        /// <summary>ThrowTiming=TimeFromGrabStart일 때 던지는 시점(잡기 시작 후 실시간 초).</summary>
        public float ThrowTime => throwTime;
        /// <summary>던지기 반응 한 벌(Kind + 공용 힘 + Ground Bounce). PerformThrow가 DamageInfo.Reaction으로 그대로 전달.</summary>
        public HitReactionSpec ThrowReaction => throwReaction;
        public float ThrowBodyImpactMultiplier => throwBodyImpactMultiplier;
        public bool AllowBackwardThrow => allowBackwardThrow;
        /// <summary>뒤로 던지기 플립 시점(잡기 시작 후 초). 0 이하 = 던지는/슬램 순간에 플립.</summary>
        public float ThrowFlipDelay => throwFlipDelay;
        /// <summary>플립 시 새 방향으로 이동할 거리(m, 부호 있음). 0 = 제자리 (2026-09-06).</summary>
        public float FlipForwardDistance => flipForwardDistance;
        /// <summary>플립 전방 이동 시간(초). 0 = 즉시.</summary>
        public float FlipForwardDuration => flipForwardDuration;

        // Takedown
        /// <summary>Takedown(슬램) 반응. Kind=None = 기존 슬램 연출 유지, 그 외 = 슬램 순간 조기 해제 + 이 반응 적용.</summary>
        public HitReactionSpec TakedownReaction => takedownReaction;

        public float SafetyTimeout => safetyTimeout;
    }
}
