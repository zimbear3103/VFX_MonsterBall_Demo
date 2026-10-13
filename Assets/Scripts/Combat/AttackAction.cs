using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 공격 1회의 완전한 정의 단위 = 애니 데이터 + Visual(VFX) + Hitbox 를 가진 프리팹의 루트.
    /// (구 AttackEffectController, 2026-08-20 리네임)
    ///
    ///   AttackAction
    ///   ├─ Visual  (VFX)
    ///   └─ Hitbox  (AttackHitbox + Trigger Collider)
    ///
    /// 보이는 공격 위치 = 실제 판정 위치가 되도록 같은 프리팹에서 관리한다.
    /// 캐릭터의 AttackRoot 아래에 미리 배치해 두고 Enable/Disable로 재사용한다.
    /// 공격의 "내용"(애니 스테이트/타이밍/배율)은 전부 여기, 공격의 "흐름"은 CharacterCombat이 담당.
    /// 새 공격 추가 = AttackAction 프리팹 추가 (CharacterCombat 수정 불필요가 목표).
    ///
    /// [히트박스 타이밍] 2026-08-25 부터 히트박스 On/Off 타이밍은 Attack Visuals 항목이 소유한다.
    /// 이펙트가 재생되는 순간(startDelay 또는 animationEventName)에 히트박스도 함께 켜져
    /// "보이는 시점 = 판정 시점"이 한 곳에서 일치한다. (구 top-level Hitbox Timing 필드는 자동 이관됨)
    ///
    /// [항목별 히트박스] 2026-09-08 부터 Attack Visuals 항목은 hitboxOverride로 별도 AttackHitbox를 지정할 수 있다.
    /// 다단 스킬에서 항목마다 판정 크기를 다르게 할 때 사용 (예: HeavyHoldSkill 슬래시=Hitbox_Slash, 폭발=Hitbox).
    /// 비우면 공용 Hitbox(References)를 쓴다.
    /// </summary>
    public class AttackAction : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("공격 식별 이름. 비워두면 GameObject 이름 사용. PlayerController 바인딩이 이 이름으로 실행할 공격을 찾는다.")]
        [SerializeField] private string attackName = "";

        [Header("Animation")]
        [Tooltip("재생할 Animator State 이름 (코드에서 CrossFade로 직접 재생)")]
        [SerializeField] private string animationStateName = "NormalAttack1";
        [Tooltip("공격 진입 CrossFade 시간(초)")]
        [SerializeField] private float crossFadeDuration = 0.05f;
        [Tooltip("Animator AttackStage Int 값 (1 = 약공격 1타)")]
        [SerializeField] private int attackStage = 1;

        [Header("Duration")]
        [Tooltip("공격 상태 최소 유지 시간(초). ※ 이 공격의 모든 시간 값(Duration/Move Cancel/Forward Move/Attack Visuals의 startDelay·hitboxDelay·hitboxDuration)은 " +
                 "Animation Speed Multiplier = 1 기준 초이며, 실제로는 모션 배속(Animation Speed Multiplier × 허주 공속 × 차징 램프)에 맞춰 같이 빨라지거나 느려진다.")]
        [SerializeField] private float minDuration = 0.15f;
        [Tooltip("공격 상태 안전 타임아웃(초, 배율 1 기준)")]
        [SerializeField] private float maxDuration = 1.2f;

        [Header("Recovery Move Cancel")]
        [Tooltip("후딜(ComboWindow 태그 상태) 진입 후 이동 입력으로 공격을 끝내고 즉시 Walk/Run으로 넘어갈 수 있는 시점(초). " +
                 "-1 = 이동 캔슬 불가(후딜을 끝까지 재생 — 자폭/잡기 등), 0 = 후딜 진입 즉시, >0 = 후딜 진입 후 n초 뒤. " +
                 "캔슬돼도 comboDropDelay 유예 안에 재입력하면 콤보가 이어진다. 이동 입력이 없으면 기존대로 후딜을 끝까지 재생한다.")]
        [SerializeField] private float moveCancelDelay = 0f;

        [Header("Damage")]
        [Tooltip("최종 데미지 = AttackPower × DamageMultiplier")]
        [SerializeField] private float damageMultiplier = 1.2f;

        [Header("Critical")]
        [Tooltip("이 공격이 치명타가 날 수 있는가. false면 절대 크리 없음(약공격 등 크리 제외에 사용)")]
        [SerializeField] private bool canCritical = true;
        [Tooltip("치명타 시 데미지 배수. 0 이하면 1.5 사용. 구버전 CriticalDamageMultiplier 대응")]
        [SerializeField] private float criticalMultiplier = 1.5f;
        [Tooltip("확정 치명타. true면 확률 무시하고 항상 크리(처형/특수 스킬용). 구버전 ForceCriticalHit 대응")]
        [SerializeField] private bool forceCritical = false;


        [Header("FP")]
        [Tooltip("이 공격 실행에 필요한 FP. 부족하면 공격이 시작되지 않는다. 0 = FP 미사용 (구버전 Weapon.FPCost 이식 — 기본공격 80/1600 = 5%)")]
        [SerializeField] private float fpCost = 0f;

        [Header("Super Armor")]
        [Tooltip("이 공격 실행 중 슈퍼아머. 피격 시 데미지는 받지만 Hit 경직/공격취소가 발생하지 않는다.")]
        [SerializeField] private bool superArmor = false;

        [Header("Hit Reaction - Common")]
        [Tooltip("가드 불가 공격. true면 CharacterDefense가 가드를 무효화(패링은 여전히 가능)")]
        [SerializeField] private bool unblockable = false;
        [Tooltip("이 공격 동안 캐릭터 방향(Facing)을 시작 시점으로 고정. 벨트스크롤 기본 true")]
        [SerializeField] private bool lockFacing = true;
        [Tooltip("다운(넉다운/누워있는) 중인 대상도 이 공격에 반응한다(OTG). true면 다운 중 전용 피격 모션을 재생하고 다운을 유지한다. false면 기존처럼 데미지만 적용되고 반응 없음.")]
        [SerializeField] private bool hitDownedTargets = false;
        [Tooltip("가드 불가 공격 시작 시 공격자 하위(비활성 포함)에서 이 이름의 오브젝트를 찾아 켠다(가드 불가 예고 마크, SpriteEffect 1회 재생). 공격 종료 시 끈다. 비우면 사용 안 함. 기본: Player 프리팹의 AttackMark")]
        [SerializeField] private string unblockableMarkName = "AttackMark";

        [Header("Hit Reaction")]
        [Tooltip("피격 반응 한 벌 — Kind(None/Push/Knockdown/Launch/Slam) + 공용 힘 + Ground Bounce 조합")]
        [SerializeField] private HitReactionSpec hitReaction = HitReactionSpec.Default;

        [Header("Hit Reaction - Body Collision")]
        [Tooltip("이 공격에 맞아 날아가는 적(넉백/넉다운/에어본)이 비행 중 다른 적과 겹치면 그 적에게도 피해를 주고 넉다운시킨다(볼링핀 연쇄). 연쇄로 맞은 적은 다시 연쇄를 일으키지 않는다.")]
        [SerializeField] private bool bodyCollision = false;
        [Tooltip("몸통 충돌 피해 배수. 연쇄 피해 = 이 공격의 최종 데미지 × 배수")]
        [SerializeField] private float bodyCollisionDamageMultiplier = 0.5f;
        [Tooltip("날아가는 몸통의 충돌 판정 반경(m)")]
        [SerializeField] private float bodyCollisionRadius = 0.8f;

        [Header("Combo Transitions")]
        [Tooltip("이 공격에서 이어질 다음 공격 분기 목록. 위에서부터 첫 번째로 조건을 만족하는 항목이 실행된다. 비어 있으면 아래 Legacy 단일 체인을 사용.")]
        [SerializeField] private List<AttackTransition> transitions = new List<AttackTransition>();

        [Header("Animator Tags")]
        [Tooltip("본 스윙 상태의 Animator Tag (Combo Window/종료 판정용)")]
        [SerializeField] private string mainStateTag = "NormalAttack";
        [Tooltip("Combo Window(후딜 Transition) 상태의 Animator Tag")]
        [SerializeField] private string comboWindowTag = "NormalAttackTransition";

        [Header("Air Combo")]
        [Tooltip("공중콤보 시동기(올려치기) 여부. 적중 시 잠시 동안 공중에서 AirCombo 그룹 공격 시작 가능(Arm)")]
        [SerializeField] private bool isAirComboLauncher = false;
        [Tooltip("공중 적 타격 시 공격자(플레이어)를 위로 띄우는 상승 속도(m/s). 0이면 자기상승 없음.")]
        [SerializeField] private float attackerLiftForce = 0f;
        [Tooltip("true면 대상이 공중(Airborne)일 때만 공격자가 뜨다. false면 지상 적을 타격해도 뜨다.")]
        [SerializeField] private bool attackerLiftOnlyVsAirborne = true;

        [Header("Air Dive")]
        [Tooltip("공중에서 이 공격을 시작하면 공격자가 빠르게 낙하한다(내려찍기 급강하). 착지해도 Land 상태로 끊기지 않고 공격 모션이 끝까지 재생된다.")]
        [SerializeField] private bool diveFall = false;
        [Tooltip("급강하 시작 하강 속도(m/s). 이후에는 기본 중력으로 계속 가속한다.")]
        [SerializeField] private float diveFallSpeed = 15f;
        [Tooltip("공격 시작 후 이 시간(초, 모션 기준) 동안 공중에 멈춰 체공한 뒤 급강하한다. 0이면 즉시 낙하. " +
                 "JumpSmash처럼 잠깐 떠 있다가 빠르게 내리꽂는 연출용. 체공 중에는 중력이 걸리지 않고 수직 속도 0으로 고정된다 (2026-09-15).")]
        [SerializeField, Min(0f)] private float diveHoverDuration = 0f;

        [Header("Legacy Combo (Deprecated - use Transitions)")]
        [Tooltip("Combo Window(NormalAttackTransition)에서 이어질 다음 공격 이름(AttackAction.AttackName). 비우면 콤보 종료(마지막 타).")]
        [SerializeField] private string nextComboAttackName = "";
        [Tooltip("이 타가 빗나가면(히트 없음) 다음 타로 진행하지 않고 첫 타(기본 공격)로 순환. 구버전 RequireHitFromComboIndex 이식 — 2·3타에 사용.")]
        [SerializeField] private bool nextComboRequiresHit = false;

        [Header("Animation Speed")]
        [Tooltip("이 공격 동안의 Animator 재생 배속. 구버전 = AttackSpeed 스탯 0.7 × 클립 AnimatorSpeed 커브의 근사값. " +
                 "이 값을 바꾸면 모션뿐 아니라 이 공격의 이펙트/히트박스/전진/Duration 타이밍(배율 1 기준 초)도 같은 비율로 따라간다 (2026-09-08).")]
        [SerializeField] private float animationSpeedMultiplier = 1f;

        [Header("Motion Speed Segments")]
        [Tooltip("구간 배속 (2026-09-15). 애니 클립 편집하듯이 '이 시간 구간만 N배'로 조정한다. 시간은 이펙트 startDelay와 같은 자(배율 1 기준 모션 초). " +
                 "가속/감속된 구간에서는 모션뿐 아니라 이펙트·히트박스·전진·Duration 타이밍이 전부 같은 비율로 따라간다. 비어 있으면 변화 없음")]
        [SerializeField] private MotionSpeedTimeline speedSegments = new MotionSpeedTimeline();

        [Header("Forward Move")]
        [Tooltip("공격 중 전진 속도(m/s, 배율 1 기준). 0 = 전진 없음. 구버전 AnimationMoveForward 커브 이식. 방향은 Facing 쪽 스플라인 접선. 모션이 빨라지면 속도도 같은 비율로 커져 총 전진 거리는 유지된다.")]
        [SerializeField] private float forwardMoveSpeed = 0f;
        [Tooltip("전진 시작 시점(초, 공격 시작 기준, 배율 1 기준)")]
        [SerializeField] private float forwardMoveStart = 0f;
        [Tooltip("전진 지속 시간(초, 배율 1 기준)")]
        [SerializeField] private float forwardMoveDuration = 0f;
        [Tooltip("지정하면 전진 창(Start/Duration)을 공격 시작이 아니라 이 애니메이션 이벤트 수신 시점 기준으로 계산한다. 홀드/차지형 공격처럼 본동작 시작 시점이 가변일 때 사용. 비우면 기존 동작(공격 시작 기준) 그대로.")]
        [SerializeField] private string forwardMoveEventName = "";

        [Header("Magnetism")]
        [Tooltip("마그네틱: 공격 시작 시 전방 가장 가까운 적 1명을 플레이어의 축에 정렬(깊이 일치)시키고 적정 타격 거리까지 끌어당긴다. 헛스윙 방지.")]
        [SerializeField] private bool useMagnetism = false;
        [Tooltip("탐색 범위: Facing 방향 벨트축 거리(m). 이 거리 안의 적만 당긴다")]
        [SerializeField] private float magnetRange = 4f;
        [Tooltip("탐색 범위: 깊이축 최대 어긋남(m). 이보다 깊이 차이가 크면 당기지 않는다")]
        [SerializeField] private float magnetDepthRange = 3f;
        [Tooltip("당김 목표 벨트축 거리(m). 적이 이보다 멀면 이 거리까지 끌어오고, 이미 가까우면 밀지 않는다")]
        [SerializeField] private float magnetStopDistance = 1.2f;
        [Tooltip("정렬에 걸리는 시간(초). 공격 시작부터 이 시간 동안 부드럽게 정렬된다 (히트박스 ON 전에 끝나도록 짧게)")]
        [SerializeField] private float magnetDuration = 0.12f;

        [Header("Hit Effect")]
        [Tooltip("피격 지점에 Hit 이펙트(피격자가 재생하는 타격 연출)를 낼지 여부. false면 이 공격은 타격 이펙트를 생략한다(무형/기 공격 등). 슬로우/쉐이크/줌/진동 등 다른 히트 연출과는 독립.")]
        [SerializeField] private bool showHitEffect = true;
        [Tooltip("판정 창(히트박스)이 열리는 순간 시전자 루트 위치에 재생할 FeedbackManager 키(FeedbackKeys.* 권장, 예: UpperSmoke). 적중 여부와 무관하게 활성 구간마다 1회. 비우면 없음. 공용 Hit 이펙트(showHitEffect)와 독립.")]
        [SerializeField] private string hitboxFeedbackKey = "";
        [Tooltip("히트박스 이펙트 위치 오프셋(m). 시전자 루트(발) 기준 — x=Facing 전방(좌향이면 자동 반전), y=위, z=깊이.")]
        [SerializeField] private Vector3 hitboxFeedbackOffset = Vector3.zero;

        [Header("Hit Slow Motion")]
        [Tooltip("이 공격에 슬로우모션(전역 timeScale 감속) 연출을 사용한다. 동시 발동 시 가중되지 않고 가장 강한(낮은) 배율 하나만 적용된다.")]
        [SerializeField] private bool useSlowMotion = false;
        [Tooltip("true = 적이 실제로 맞았을 때만 발동 (스윙당 1회). false = 공격 시작 시 무조건 발동")]
        [SerializeField] private bool slowMotionOnHitOnly = true;
        [Tooltip("발동 시점에서 슬로우모션을 추가로 미루는 시간(초, 모션 시간 — 구간 배속을 따라감). 0 = 즉시 (2026-09-15)")]
        [SerializeField, Min(0f)] private float slowMotionDelay = 0f;
        [Tooltip("얼마나 느려질지. 0.3 = 30% 속도. 1이면 효과 없음")]
        [Range(0f, 1f)]
        [SerializeField] private float slowMotionScale = 0.3f;
        [Tooltip("느려진 상태 유지 시간(초, 실시간 기준)")]
        [SerializeField] private float slowMotionDuration = 0.15f;
        [Tooltip("정상 속도 → 슬로우까지 Lerp 진입 시간(초). 0 = 즉시")]
        [SerializeField] private float slowMotionEaseIn = 0.02f;
        [Tooltip("슬로우 → 정상 속도까지 Lerp 복귀 시간(초). 0 = 즉시")]
        [SerializeField] private float slowMotionEaseOut = 0.2f;

        [Header("Hit Camera Shake")]
        [Tooltip("이 공격에 카메라 쉐이크를 사용한다 (CameraShakeService — 감쇠 Perlin 노이즈)")]
        [SerializeField] private bool useCameraShake = false;
        [Tooltip("true = 적이 실제로 맞았을 때만 발동 (스윙당 1회). false = 공격 시작 시 무조건 발동")]
        [SerializeField] private bool cameraShakeOnHitOnly = true;
        [Tooltip("발동 시점에서 쉐이크를 추가로 미루는 시간(초, 모션 시간 — 구간 배속을 따라감). 0 = 즉시 (2026-09-15)")]
        [SerializeField, Min(0f)] private float cameraShakeDelay = 0f;
        [Tooltip("축별 진폭(m). 구버전 Cinemachine Impulse Velocity 등가. 참고: 체인 잡기 적중 = (1.5,1.5,1.5)")]
        [SerializeField] private Vector3 cameraShakeVelocity = new Vector3(0.3f, 0.3f, 0.3f);
        [Tooltip("쉐이크 지속 시간(초, 실시간 기준)")]
        [SerializeField] private float cameraShakeDuration = 0.2f;
        [Tooltip("노이즈 주파수(Hz). 높을수록 잘게 떨림")]
        [SerializeField] private float cameraShakeFrequency = 25f;

        [Header("Hit Camera Zoom")]
        [Tooltip("이 공격에 카메라 줌 펀치(FOV)를 사용한다. 구버전 HitImpactDirector 줌 이관")]
        [SerializeField] private bool useCameraZoom = false;
        [Tooltip("true = 적이 실제로 맞았을 때만 발동 (스윙당 1회). false = 공격 시작 시 무조건 발동")]
        [SerializeField] private bool cameraZoomOnHitOnly = true;
        [Tooltip("발동 시점에서 줌 펀치를 추가로 미루는 시간(초, 모션 시간 — 구간 배속을 따라감). 0 = 즉시 (2026-09-15)")]
        [SerializeField, Min(0f)] private float cameraZoomDelay = 0f;
        [Tooltip("FOV 변화량(deg). 음수 = 줌인(타격 펀치), 양수 = 줌아웃. 구버전 씬 값: 일반 -2 / 강타 -4")]
        [SerializeField] private float cameraZoomFovDelta = -2f;
        [Tooltip("기본 FOV → 목표까지 진입 시간(초, 실시간)")]
        [SerializeField] private float cameraZoomEaseIn = 0.1f;
        [Tooltip("목표 FOV 유지 시간(초, 실시간)")]
        [SerializeField] private float cameraZoomHold = 0.1f;
        [Tooltip("목표 → 기본 FOV 복귀 시간(초, 실시간)")]
        [SerializeField] private float cameraZoomEaseOut = 0.1f;

        [Header("Gamepad Rumble")]
        [Tooltip("이 공격에 패드 진동을 사용한다 (Gamepad 연결 시)")]
        [SerializeField] private bool useRumble = false;
        [Tooltip("true = 적이 실제로 맞았을 때만 진동 (스윙당 1회). false = 공격 시작 시 무조건 진동")]
        [SerializeField] private bool rumbleOnHitOnly = true;
        [Tooltip("발동 시점에서 진동을 추가로 미루는 시간(초, 모션 시간 — 구간 배속을 따라감). 0 = 즉시 (2026-09-15)")]
        [SerializeField, Min(0f)] private float rumbleDelay = 0f;
        [Tooltip("저주파 모터 세기 (묵직한 진동)")]
        [Range(0f, 1f)]
        [SerializeField] private float rumbleLowFrequency = 0.5f;
        [Tooltip("고주파 모터 세기 (날카로운 진동)")]
        [Range(0f, 1f)]
        [SerializeField] private float rumbleHighFrequency = 0.5f;
        [Tooltip("진동 지속 시간(초, 실시간 기준)")]
        [SerializeField] private float rumbleDuration = 0.15f;

        [Header("Power Charge (Hold)")]
        [Tooltip("홀드 차징(기 모으기) 활성화. 버튼을 누르면 즉시 발동(연타 즉발 유지)하고, 계속 누르면 윈드업에서 모션이 정지하며 기를 모은다. 릴리즈 시 강화 공격. 구버전 파워 차징 v2 이식.")]
        [SerializeField] private bool usePowerCharge = false;
        [Tooltip("공격 시작 후 이 시간(실시간 초) 이상 계속 누르면 윈드업에서 모션 정지 = 차징 준비. 이보다 빨리 떼면(탭) 일반 공격 그대로.")]
        [SerializeField, Min(0f)] private float chargeFreezeTime = 0.15f;
        [Tooltip("공격 시작 후 이 시간(실시간 초) 이상 눌러야 차징 커밋(슬로우모션·강화). 이보다 짧으면 강화 없는 일반 공격으로 재개.")]
        [SerializeField, Min(0f)] private float chargeMinHoldTime = 0.35f;
        [Tooltip("차징 커밋 후 최대 유지 시간(실시간 초). 지나면 자동 발동.")]
        [SerializeField, Min(0.1f)] private float chargeMaxHoldTime = 1.0f;
        [Tooltip("차징 발동 시 데미지 배율 (최종 데미지 × 배율)")]
        [SerializeField, Min(1f)] private float chargeDamageMultiplier = 2.0f;
        [Tooltip("차징(기 모으기) 중 전역 시간 배율(슬로우모션). 낮을수록 극적. 릴리즈/취소 시 자동 복원.")]
        [SerializeField, Range(0.05f, 1f)] private float chargeTimeScale = 0.5f;
        [Tooltip("차징(정지) 중 자기 모션의 재생 속도 배율. 0 = 완전 정지, 0.1 = 아주 천천히(자연스러움). 슬로우모션과 곱해져 체감은 더 느림.")]
        [SerializeField, Range(0f, 0.5f)] private float chargeHoldAnimSpeed = 0.1f;
        [Tooltip("차징 발동 스윙의 이펙트(Attack Visuals) 크기 배율. 1.5 = 1.5배. 현재 크기의 몇 배로 커질지 여기서 정한다.")]
        [SerializeField, Min(1f)] private float chargeEffectScaleMultiplier = 1.5f;
        [Tooltip("릴리즈 직후 스윙을 이 배율로 짧게 가속해 정지점→타격까지의 시간을 단축한다. 1 = 가속 없음.")]
        [SerializeField, Min(1f)] private float chargeReleaseAnimSpeed = 2.5f;
        [Tooltip("릴리즈 가속 유지 시간(실시간 초). 이 시간 후 정상 속도 복귀.")]
        [SerializeField, Min(0f)] private float chargeReleaseBoostDuration = 0.3f;
        [Tooltip("정지(기 모으기) 진입 시 모션 배속을 normal→chargeHoldAnimSpeed로 서서히 떨구는 시간(실시간 초). 0 = 즉시 스냅(구버전). 값을 주면 러프(스무스) 커브로 천천히 느려진다.")]
        [SerializeField, Min(0f)] private float chargeFreezeEaseTime = 0.18f;
        [Tooltip("릴리즈 시 정지 배속 → 릴리즈/정상 배속으로 서서히 올리는 시간(실시간 초). 0 = 즉시 스냅(구버전, 빡 튐). 값을 주면 부드럽게 가속해 자연스럽게 나간다.")]
        [SerializeField, Min(0f)] private float chargeReleaseEaseTime = 0.12f;

        [Header("Power Charge Camera Zoom")]
        [Tooltip("차징(윈드업 정지) 중 카메라 줌인 사용. 정지 시작 시 서서히 줌인 → 차징 내내 유지 → 릴리즈 시 원복. 적중 펀치(Hit Camera Zoom)와 독립.")]
        [SerializeField] private bool chargeCameraZoom = true;
        [Tooltip("차징 줌 FOV 변화량(deg). 음수 = 줌인(당겨서 확대), 양수 = 줌아웃.")]
        [SerializeField] private float chargeCameraZoomFovDelta = -6f;
        [Tooltip("정지 시작 → 목표 FOV까지 부드럽게 진입하는 시간(실시간 초). 러프(스무스) 커브로 들어간다.")]
        [SerializeField, Min(0f)] private float chargeCameraZoomEaseIn = 0.18f;
        [Tooltip("릴리즈 시 목표 FOV → 기본 FOV로 복귀하는 시간(실시간 초).")]
        [SerializeField, Min(0f)] private float chargeCameraZoomEaseOut = 0.15f;

        [Header("References")]
        [SerializeField] private AttackHitbox hitbox;
        [SerializeField] private GameObject visual;
        [Tooltip("공격 자식 VFX + 히트박스 타임라인. 각 항목은 startDelay(또는 animationEventName)에 재생되며, Trigger Hitbox가 켜져 있으면 그 순간 히트박스도 함께 활성화한다. target을 비우면 히트박스 전용 항목이 된다.")]
        [SerializeField] private List<TimedVisual> attackVisuals = new List<TimedVisual>();

        // Legacy Hitbox Timing (자동 이관 소스 / 이관 전 런타임 폴백)
        // 2026-08-25: 히트박스 타이밍은 attackVisuals 항목이 소유한다. 아래 필드는 기존 자산의
        // 값을 잃지 않기 위해 유지되며, OnValidate에서 attackVisuals 항목으로 1회 자동 이관된다.
        [HideInInspector][SerializeField] private float hitboxStartDelay = 0.02f;
        [HideInInspector][SerializeField] private string hitboxOnEventName = "";
        [HideInInspector][SerializeField] private string hitboxOffEventName = "";
        [HideInInspector][SerializeField] private float hitboxActiveDuration = 0.15f;
        [HideInInspector][SerializeField] private bool _hitboxMigratedToVisuals = false;

        [System.Serializable]
        public class TimedVisual
        {
            [Tooltip("공격 자식 VFX 오브젝트. 비워두면 히트박스 전용 타임라인 항목으로 쓴다.")]
            public GameObject target;
            [Tooltip("공격 시작(Play) 기준 재생/판정 지연 시간(초). animationEventName이 지정되면 사용 안 함")]
            public float startDelay;
            [Tooltip("지정하면 타이머 대신 해당 애니메이션 이벤트 수신 시 재생/판정 시작 — 배속과 자동 동기화 (클립 이벤트 → AttackAnimationEventReceiver 경유)")]
            public string animationEventName;

            [Header("Hitbox")]
            [Tooltip("이 항목이 재생되는 순간 히트박스도 함께 켠다 (보이는 시점 = 판정 시점 일치)")]
            public bool triggerHitbox;
            [Tooltip("이 항목이 켤 히트박스. 비우면 액션 공용 Hitbox(References)를 쓴다. 다단 스킬에서 항목마다 판정 크기를 다르게 하고 싶을 때(예: 슬래시=작은 박스, 폭발=큰 박스) 별도 AttackHitbox 자식을 만들어 지정한다.")]
            public AttackHitbox hitboxOverride;
            [Tooltip("이 항목 재생 후 히트박스가 켜지기까지의 추가 지연(초). 0이면 이펙트와 동시, 예: 0.1 = 이펙트 0.1초 뒤 판정 ON")]
            public float hitboxDelay;
            [Tooltip("히트박스 유지 시간(초). hitboxOffEventName이 비어 있을 때 사용. 0이면 기본값으로 대체")]
            public float hitboxDuration;
            [Tooltip("지정하면 이 애니메이션 이벤트 수신 시 히트박스를 끈다 (없으면 hitboxDuration 경과 시 자동 종료)")]
            public string hitboxOffEventName;
            [Tooltip("이 항목이 연 히트박스에 맞은 대상에게 피격 리액션(경직/넉백/런치 상태 진입)을 줄지. 끄면 데미지·피격 연출만 적용되고 리액션 상태는 건너뛴다 (예: 다단 이펙트 중 마지막 폭발에만 리액션)")]
            public bool hitReaction = true;
            [Tooltip("체크하면 이 항목이 연 히트박스는 액션 공용 Hit Reaction 대신 아래 Reaction을 쓴다 (예: 다단 스킬에서 선행 슬래시=None 경직, 마지막 폭발=Knockdown). hitReaction이 꺼져 있으면 무시")]
            public bool overrideReaction;
            [Tooltip("overrideReaction일 때 이 항목 적중에 적용할 피격 반응")]
            public HitReactionSpec reaction = HitReactionSpec.Default;

            [Header("Power Charge")]
            [Tooltip("차징 발동 스윙에서 이 이펙트의 ParticleSystem Start Color를 아래 색으로 교체한다 (일반 스윙은 원래 색 유지)")]
            public bool chargedColorOverride;
            [Tooltip("차징 발동 시 적용할 Start Color")]
            public Color chargedStartColor = new Color(0.35f, 0.65f, 1f, 1f);

            [Header("Persist Effect")]
            [Tooltip("체크하면 이 이펙트는 공격이 끊기든 끝나든 상관없이 계속 재생된다. 부모(자식 관계)나 위치는 건드리지 않고(프리팹에 배치해 둔 그대로), 아래 지속 시간이 지나면 이펙트를 끄고 비활성 대기 상태로 복귀한다(다음 스윙 재사용). 공격 종료 시 사라지지 않으려면 AttackAction 비활성화 영향을 받지 않는 위치에 미리 배치해 둘 것.")]
            [FormerlySerializedAs("persistAfterAttack")]
            [FormerlySerializedAs("detachOnStop")]
            public bool persistEffect;
            [Tooltip("이펙트가 재생되는 순간부터 세는 총 지속 시간(초). 이 시간이 지나면 이펙트를 끈다. 종료를 시간으로 판정하므로 자체 제작 파티클(ParticleSystem 아님)에도 안전하다. 0 이하면 5초.")]
            [FormerlySerializedAs("detachReturnTimeout")]
            public float persistDuration = 3f;

            [Header("Impact Freeze")]
            [Tooltip("이 항목이 발동되는 순간 전역 시간을 잠시 멈춘(히트스탑) 뒤에 이펙트 재생·히트박스 ON을 실행한다. '멈칫 → 터짐' 연출. 정지 시간은 실시간 기준이며 타임라인의 다른 항목도 같이 멈춘다.")]
            public bool impactFreeze;
            [Tooltip("true = 차징 발동(강화) 스윙에서만 정지. false = 일반 스윙에서도 항상 정지")]
            public bool impactFreezeChargedOnly = true;
            [Tooltip("정지 유지 시간(초, 실시간)")]
            [Min(0f)] public float impactFreezeDuration = 0.12f;
            [Tooltip("정지 강도. 0 = 완전 정지, 0.1 = 10% 속도")]
            [Range(0f, 1f)] public float impactFreezeScale = 0f;

            [Header("Impact Zoom")]
            [Tooltip("이 항목이 터지는 순간(정지 해제 직후) 카메라 줌 펀치(FOV)를 발동한다. 적중 여부와 무관하게 발동. Hit Camera Zoom(적중 펀치)과 독립.")]
            public bool impactZoom;
            [Tooltip("true = 차징 발동(강화) 스윙에서만 줌. false = 일반 스윙에서도 항상 줌")]
            public bool impactZoomChargedOnly = true;
            [Tooltip("FOV 변화량(deg). 음수 = 줌인, 양수 = 줌아웃")]
            public float impactZoomFovDelta = -4f;
            [Tooltip("기본 FOV → 목표까지 진입 시간(초, 실시간)")]
            [Min(0f)] public float impactZoomEaseIn = 0.05f;
            [Tooltip("목표 FOV 유지 시간(초, 실시간)")]
            [Min(0f)] public float impactZoomHold = 0.1f;
            [Tooltip("목표 → 기본 FOV 복귀 시간(초, 실시간)")]
            [Min(0f)] public float impactZoomEaseOut = 0.2f;
        }

        private Coroutine _routine;
        private Coroutine _vfxRoutine;
        private Coroutine _hitboxCloseRoutine;
        private Coroutine _hitboxOpenRoutine;
        private readonly List<Coroutine> _impactFreezeRoutines = new List<Coroutine>();
        [System.NonSerialized] private GameObject _attacker;
        [System.NonSerialized] private float _finalDamage;
        [System.NonSerialized] private string _pendingHitboxOffEvent;

        // 항목별 히트박스(hitboxOverride) 지원 — 공용 hitbox + 오버라이드 전부를 한 번에 설정/정리한다.
        // 오버라이드 히트박스는 각자 별도 종료 타이머/Off 이벤트를 가진다 (공용 hitbox는 기존 _hitboxCloseRoutine/_pendingHitboxOffEvent 사용).
        [System.NonSerialized] private readonly List<AttackHitbox> _allHitboxes = new List<AttackHitbox>();
        [System.NonSerialized] private readonly Dictionary<AttackHitbox, Coroutine> _overrideCloseRoutines = new Dictionary<AttackHitbox, Coroutine>();
        [System.NonSerialized] private readonly Dictionary<AttackHitbox, string> _overridePendingOff = new Dictionary<AttackHitbox, string>();
        [System.NonSerialized] private GameObject _markOwner;
        [System.NonSerialized] private GameObject _unblockableMark;

        // Power Charge 런타임 상태 (흐름은 CharacterCombat이 소유, 데이터/적용은 여기)
        [System.NonSerialized] private float _chargeFxScale = 1f;
        [System.NonSerialized] private bool _chargeApplyColor;
        [System.NonSerialized] private bool _chargedUse;
        [System.NonSerialized] private Dictionary<GameObject, Vector3> _visualBaseScales;
        [System.NonSerialized] private HashSet<GameObject> _visualChargeApplied; // 현재 차징 확대 스케일이 적용돼 있는 target
        [System.NonSerialized] private Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient> _originalStartColors;

        /// <summary>홀드 차징 사용 여부. 흐름(타이머/정지/릴리즈)은 CharacterCombat.TickPowerCharge가 담당.</summary>
        public bool UsePowerCharge => usePowerCharge;
        public float ChargeFreezeTime => chargeFreezeTime;
        public float ChargeMinHoldTime => chargeMinHoldTime;
        public float ChargeMaxHoldTime => chargeMaxHoldTime;
        public float ChargeTimeScale => chargeTimeScale;
        public float ChargeHoldAnimSpeed => chargeHoldAnimSpeed;
        public float ChargeReleaseAnimSpeed => chargeReleaseAnimSpeed;
        public float ChargeReleaseBoostDuration => chargeReleaseBoostDuration;
        /// <summary>정지 진입 시 모션 배속을 서서히 떨구는 시간(초). 0 = 즉시 스냅.</summary>
        public float ChargeFreezeEaseTime => chargeFreezeEaseTime;
        /// <summary>릴리즈 시 정지 배속 → 릴리즈/정상 배속으로 서서히 올리는 시간(초). 0 = 즉시 스냅.</summary>
        public float ChargeReleaseEaseTime => chargeReleaseEaseTime;
        /// <summary>차징(윈드업 정지) 중 카메라 줌인 사용 여부.</summary>
        public bool ChargeCameraZoom => chargeCameraZoom;
        /// <summary>차징 줌 FOV 변화량(deg). 음수 = 줌인.</summary>
        public float ChargeCameraZoomFovDelta => chargeCameraZoomFovDelta;
        /// <summary>차징 줌 진입(easeIn) 시간(초).</summary>
        public float ChargeCameraZoomEaseIn => chargeCameraZoomEaseIn;
        /// <summary>차징 줌 복귀(easeOut) 시간(초).</summary>
        public float ChargeCameraZoomEaseOut => chargeCameraZoomEaseOut;

        /// <summary>차징 정지 중 VFX/히트박스 타임라인 일시정지 (CharacterCombat이 정지/해제와 함께 토글).</summary>
        public bool TimelinePaused { get; set; }

        /// <summary>
        /// VFX/히트박스 타임라인 진행 배속 (2026-09-08). startDelay/hitboxDelay/hitboxDuration은 '튜닝 기준 모션 배속'에서의 초 단위이므로
        /// 모션이 그보다 느려지거나 빨라지면(허주 공속 디버프 0.7×, 차징 정지·릴리즈 램프, 패리 프레임 정지 등) 타임라인도 같은 비율로 따라가야
        /// 이펙트/판정이 스윙 프레임에 붙어 있다. CharacterCombat이 공격 시작 시와 매 틱 (현재 Animator 배속 / 기준 배속)으로 갱신한다.
        /// 1 = 기준 그대로(실시간). 갱신자가 없는 경우(직접 링크 등)에도 1로 동작한다.
        /// </summary>
        public float TimelineSpeed { get; set; } = 1f;

        /// <summary>
        /// 구간 배속 (2026-09-15) — motionTime(배율 1 기준 모션 초 = CharacterCombat._attackTimer)에서의 배율. 구간 밖 = 1.
        /// CharacterCombat이 매 틱 Animator 배속에 곱하고, 그 결과가 TimelineSpeed로 되돌아와 이펙트/판정도 같이 빨라진다.
        /// </summary>
        public float EvaluateSpeedSegment(float motionTime)
            => speedSegments != null ? speedSegments.Evaluate(motionTime) : 1f;

        /// <summary>구간 배속 데이터 (읽기 전용 — 인스펙터/디버그용).</summary>
        public MotionSpeedTimeline SpeedSegments => speedSegments;

        /// <summary>Play~Stop 사이 (지연 연출이 공격 종료 후 대기 기준을 실시간으로 바꾸는 데 사용).</summary>
        [System.NonSerialized] private bool _playing;

        /// <summary>타임라인 기준으로 seconds(기준 배속 초)만큼 대기 — TimelineSpeed 반영, TimelinePaused 중 정지. WaitForSeconds 대체.</summary>
        private IEnumerator WaitTimeline(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                yield return null;
                if (!TimelinePaused) t += Time.deltaTime * TimelineSpeed;
            }
        }

        /// <summary>
        /// 적중 연출 지연 대기 (2026-09-15) — 공격이 살아 있는 동안은 모션 시간(TimelineSpeed·일시정지 반영), 공격이 먼저 끝나면
        /// 남은 시간은 게임 시간으로 진행해 예약이 유실되지 않는다. 코루틴은 캐릭터(시전자)에서 돌려 AttackAction 비활성화에 안 끊긴다.
        /// </summary>
        private IEnumerator WaitFeedbackDelay(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                yield return null;
                if (_playing) { if (!TimelinePaused) t += Time.deltaTime * TimelineSpeed; }
                else t += Time.deltaTime;
            }
        }

        private IEnumerator DelayedFeedback(float delay, System.Action fire)
        {
            yield return WaitFeedbackDelay(delay);
            fire?.Invoke();
        }

        /// <summary>delay가 있으면 시전자(없으면 자기 자신)에서 지연 코루틴, 없으면 즉시 발동.</summary>
        private void FireWithDelay(float delay, System.Action fire)
        {
            if (delay <= 0f) { fire?.Invoke(); return; }
            MonoBehaviour host = null;
            if (_attacker != null)
            {
                host = _attacker.GetComponentInParent<Character>();
                if (host == null) host = _attacker.GetComponent<MonoBehaviour>();
            }
            if (host == null || !host.isActiveAndEnabled) host = this;
            host.StartCoroutine(DelayedFeedback(delay, fire));
        }

        /// <summary>
        /// 차징 릴리즈 적용 — 이번 사용의 최종 데미지·이펙트 크기·색 오버라이드를 홀드 비율(0~1)만큼 강화.
        /// 히트박스가 열리기 전(윈드업 정지 해제 시점)에 호출되어야 데미지에 반영된다.
        /// </summary>
        public void SetChargeRelease(float ratio)
        {
            ratio = Mathf.Clamp01(ratio);
            _finalDamage *= Mathf.Lerp(1f, Mathf.Max(1f, chargeDamageMultiplier), ratio);
            _chargeFxScale = Mathf.Lerp(1f, Mathf.Max(1f, chargeEffectScaleMultiplier), ratio);
            _chargeApplyColor = ratio > 0.01f;
            _chargedUse = ratio > 0.01f;
        }

        /// <summary>이번 사용이 차징 발동(강화) 스윙인가. SetChargeRelease(ratio>0) 이후 true, Play 시 리셋.</summary>
        public bool IsChargedUse => _chargedUse;

        /// <summary>공격 식별 이름. 비워두면 GameObject 이름을 사용.</summary>
        public string AttackName => string.IsNullOrEmpty(attackName) ? gameObject.name : attackName;
        public string AnimationStateName => animationStateName;
        public float CrossFadeDuration => crossFadeDuration;
        public int AttackStage => attackStage;
        public float MinDuration => minDuration;
        public float MaxDuration => maxDuration;
        /// <summary>후딜 이동 캔슬 가능 시점(초, ComboWindow 진입 기준). -1 = 캔슬 불가. CharacterCombat.CanMoveCancel이 판정.</summary>
        public float MoveCancelDelay => moveCancelDelay;
        public float DamageMultiplier => damageMultiplier;
        /// <summary>이 공격이 치명타가 날 수 있는가.</summary>
        public bool CanCritical => canCritical;
        /// <summary>치명타 데미지 배수(0 이하면 1.5).</summary>
        public float CriticalMultiplier => criticalMultiplier > 0f ? criticalMultiplier : 1.5f;
        /// <summary>확정 치명타 여부.</summary>
        public bool ForceCritical => forceCritical;

        /// <summary>이 공격 실행에 필요한 FP (0 = 미사용). 게이트는 CharacterCombat.ExecuteAttack이 담당.</summary>
        public float FPCost => fpCost;
        /// <summary>이 공격 실행 중 슈퍼아머 여부. CharacterCombat.ExecuteAttack이 읽어 적용한다.</summary>
        public bool SuperArmor => superArmor;
        /// <summary>가드 불가 공격 여부 (DamageInfo.Unblockable로 전달).</summary>
        public bool Unblockable => unblockable;
        /// <summary>피격자에게 강제할 반응 한 벌(Kind + 공용 힘 + Ground Bounce). DamageInfo.Reaction으로 그대로 전달.</summary>
        public HitReactionSpec Reaction => hitReaction;
        /// <summary>날아가는 피격자의 몸통 충돌(볼링핀 연쇄) 여부.</summary>
        public bool BodyCollision => bodyCollision;
        public float BodyCollisionDamageMultiplier => bodyCollisionDamageMultiplier;
        public float BodyCollisionRadius => bodyCollisionRadius;
        public bool DiveFall => diveFall;
        public float DiveFallSpeed => diveFallSpeed;
        /// <summary>급강하 전 공중 체공 시간(초, 모션 기준). 0이면 공격 시작 즉시 낙하.</summary>
        public float DiveHoverDuration => diveHoverDuration;
        /// <summary>공격 중 Facing 고정 여부. CharacterCombat.ExecuteAttack이 Movement.FacingLocked에 반영.</summary>
        public bool LockFacing => lockFacing;
        /// <summary>다운 중인 대상도 반응시키는 공격인가(OTG). DamageInfo.HitsDowned로 전달.</summary>
        public bool HitDownedTargets => hitDownedTargets;
        /// <summary>VFX 루트(References › Visual). 잡기 등 외부 시퀀스가 자식 VFX를 컨테이너 단위로 반전할 때 참조 (2026-09-08).</summary>
        public GameObject VisualObject => visual;
        /// <summary>피격 지점 Hit 이펙트를 재생할지 여부. DamageInfo.ShowHitEffect로 전달.</summary>
        public bool ShowHitEffect => showHitEffect;
        /// <summary>히트박스가 열리는 순간 시전자 루트+오프셋에 재생할 Feedback 키(적중 무관). 빈 문자열 = 없음.</summary>
        public string HitboxFeedbackKey => hitboxFeedbackKey;
        /// <summary>HitboxFeedbackKey 재생 위치 오프셋(x=Facing 전방, y=위, z=깊이).</summary>
        public Vector3 HitboxFeedbackOffset => hitboxFeedbackOffset;
        /// <summary>Combo Window에서 이어질 다음 공격 이름. 빈 문자열 = 콤보 마지막 타.</summary>
        public string NextComboAttackName => nextComboAttackName;
        /// <summary>이 타가 빗나가면 다음 타 대신 첫 타로 순환하는가 (히트 게이트).</summary>
        public bool NextComboRequiresHit => nextComboRequiresHit;
        /// <summary>이 공격 동안의 Animator 재생 배속.</summary>
        public float AnimationSpeedMultiplier => animationSpeedMultiplier <= 0f ? 1f : animationSpeedMultiplier;
        /// <summary>마그네틱(공격 축 정렬) 사용 여부. 실행은 CharacterCombat이 담당.</summary>
        public bool UseMagnetism => useMagnetism;
        /// <summary>마그네틱 탐색 벨트축 범위(m).</summary>
        public float MagnetRange => magnetRange;
        /// <summary>마그네틱 탐색 깊이축 범위(m).</summary>
        public float MagnetDepthRange => magnetDepthRange;
        /// <summary>당김 목표 벨트축 거리(m). 당기기만 하고 밀지 않는다.</summary>
        public float MagnetStopDistance => magnetStopDistance;
        /// <summary>정렬 소요 시간(초).</summary>
        public float MagnetDuration => magnetDuration;
        public float ForwardMoveSpeed => forwardMoveSpeed;
        public float ForwardMoveStart => forwardMoveStart;
        public float ForwardMoveDuration => forwardMoveDuration;
        /// <summary>전진 창의 기준이 되는 애니메이션 이벤트 이름. 비어 있으면 공격 시작 기준.</summary>
        public string ForwardMoveEventName => forwardMoveEventName;
        /// <summary>이번 사용에서 forwardMoveEventName 이벤트를 수신했는가. Play 시 리셋.</summary>
        public bool ForwardMoveEventReceived { get; private set; }
        /// <summary>이번 사용(Play 이후)에서 히트박스(공용 또는 항목별 오버라이드)가 1회 이상 적중했는가.</summary>
        public bool HasHitThisUse
        {
            get
            {
                for (int i = 0; i < _allHitboxes.Count; i++)
                    if (_allHitboxes[i] != null && _allHitboxes[i].HasHitThisActivation) return true;
                return hitbox != null && hitbox.HasHitThisActivation;
            }
        }
        /// <summary>공중콤보 시동기(올려치기) 여부. 적중 시 CharacterCombat이 AirCombo를 Arm한다.</summary>
        public bool IsAirComboLauncher => isAirComboLauncher;
        /// <summary>공중 적 타격 시 공격자 자기상승 속도(m/s). 0 = 없음.</summary>
        public float AttackerLiftForce => attackerLiftForce;
        /// <summary>공격자 자기상승을 대상이 공중일 때로 제한할지 여부.</summary>
        public bool AttackerLiftOnlyVsAirborne => attackerLiftOnlyVsAirborne;
        /// <summary>분기형 콤보 Transition 목록 (우선순위 = 순서). 비어 있으면 Legacy 단일 체인을 사용.</summary>
        public IReadOnlyList<AttackTransition> Transitions => transitions;
        /// <summary>Transition 분기 데이터가 설정되어 있는가.</summary>
        public bool HasTransitions => transitions != null && transitions.Count > 0;
        /// <summary>본 스윙 상태 Animator Tag 해시.</summary>
        public int MainStateTagHash { get { EnsureTagHashes(); return _mainTagHash; } }
        /// <summary>Combo Window(후딜 Transition) 상태 Animator Tag 해시.</summary>
        public int ComboWindowTagHash { get { EnsureTagHashes(); return _windowTagHash; } }

        [System.NonSerialized] private int _mainTagHash;
        [System.NonSerialized] private int _windowTagHash;
        [System.NonSerialized] private bool _tagsHashed;

        private void EnsureTagHashes()
        {
            if (_tagsHashed) return;
            _mainTagHash = Animator.StringToHash(string.IsNullOrEmpty(mainStateTag) ? "NormalAttack" : mainStateTag);
            _windowTagHash = Animator.StringToHash(string.IsNullOrEmpty(comboWindowTag) ? "NormalAttackTransition" : comboWindowTag);
            _tagsHashed = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _tagsHashed = false;
            MigrateHitboxToVisuals();
            if (transitions == null) return;
            for (int i = 0; i < transitions.Count; i++)
            {
                var a = transitions[i];
                if (a == null) continue;
                for (int j = i + 1; j < transitions.Count; j++)
                {
                    var b = transitions[j];
                    if (b == null || a.input != b.input) continue;
                    bool shadowed = !a.requireHit && (a.ground == ComboGroundCondition.Any || a.ground == b.ground);
                    if (shadowed)
                        Debug.LogWarning($"[AttackAction] {name} Transitions[{j}] ({b.input}) 은 Transitions[{i}] 에 가려져 도달할 수 없습니다. 순서/조건을 확인하세요.", this);
                }
            }
        }

        /// <summary>구 top-level Hitbox Timing 필드를 attackVisuals 항목으로 1회 자동 이관 (데이터 보존).</summary>
        private void MigrateHitboxToVisuals()
        {
            if (_hitboxMigratedToVisuals) return;
            if (attackVisuals == null) attackVisuals = new List<TimedVisual>();

            bool alreadyDriven = false;
            for (int i = 0; i < attackVisuals.Count; i++)
                if (attackVisuals[i] != null && attackVisuals[i].triggerHitbox) { alreadyDriven = true; break; }

            if (!alreadyDriven)
            {
                attackVisuals.Add(new TimedVisual
                {
                    target = null,
                    startDelay = hitboxStartDelay,
                    animationEventName = hitboxOnEventName,
                    triggerHitbox = true,
                    hitboxDuration = hitboxActiveDuration,
                    hitboxOffEventName = hitboxOffEventName
                });
            }

            _hitboxMigratedToVisuals = true;
            hitboxOnEventName = "";
            hitboxOffEventName = "";
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif


        public AttackHitbox Hitbox => hitbox;

        private void Awake()
        {
            if (hitbox == null) hitbox = GetComponentInChildren<AttackHitbox>(true);
            CollectHitboxes();
        }

        /// <summary>공용 hitbox + attackVisuals 항목의 hitboxOverride를 중복 없이 모은다 (설정/정리 일괄 처리용).</summary>
        private void CollectHitboxes()
        {
            _allHitboxes.Clear();
            if (hitbox != null) _allHitboxes.Add(hitbox);
            if (attackVisuals == null) return;
            for (int i = 0; i < attackVisuals.Count; i++)
            {
                var hb = attackVisuals[i] != null ? attackVisuals[i].hitboxOverride : null;
                if (hb != null && !_allHitboxes.Contains(hb)) _allHitboxes.Add(hb);
            }
        }

        /// <summary>항목이 켤 히트박스 — hitboxOverride가 있으면 그것, 없으면 공용 hitbox.</summary>
        private AttackHitbox ResolveHitbox(TimedVisual entry)
        {
            return entry != null && entry.hitboxOverride != null ? entry.hitboxOverride : hitbox;
        }

        /// <summary>공격 시작. VFX 재생 + (비주얼 항목이 소유한) 판정 타이밍 시작.</summary>
        public void Play(GameObject attacker, float attackPower)
        {
            gameObject.SetActive(true);

            _attacker = attacker;
            _finalDamage = attackPower * damageMultiplier;
            _pendingHitboxOffEvent = null;
            _overridePendingOff.Clear();
            ForwardMoveEventReceived = false;
            TimelinePaused = false;
            TimelineSpeed = 1f; // CharacterCombat이 Play 직후·매 틱 모션 배속 비율로 갱신
            _playing = true;
            _chargeFxScale = 1f;
            _chargeApplyColor = false;
            _chargedUse = false;
            StopImpactFreezeRoutines();
            CollectHitboxes();

            // 공용 hitbox + 항목별 오버라이드 전부에 동일 설정 주입 (반응 스펙은 OpenHitbox에서 항목별로 재설정됨)
            for (int i = 0; i < _allHitboxes.Count; i++)
            {
                var hb = _allHitboxes[i];
                if (hb == null) continue;
                hb.SourceAction = this; // 적중 통지에 소속 공격 전달 (인터럽트 프레임 리프트 유실 방지)
                hb.ConfigureReaction(hitReaction, unblockable, hitDownedTargets);
                hb.ConfigureCritical(canCritical, criticalMultiplier, forceCritical);
                hb.ConfigureBodyImpact(bodyCollision ? bodyCollisionDamageMultiplier : 0f, bodyCollisionRadius);
                hb.ShowHitEffect = showHitEffect;
                hb.HitLanded = HandleHitFeedback; // 적중 게이트 연출 연결 (활성 구간당 1회 콜백)
                hb.Deactivate();
            }

            // 시작 시 발동 옵션 처리
            if (useSlowMotion && !slowMotionOnHitOnly) PlayHitSlowMotion();
            if (useRumble && !rumbleOnHitOnly) PlayHitRumble();
            if (useCameraShake && !cameraShakeOnHitOnly) PlayHitCameraShake();
            if (useCameraZoom && !cameraZoomOnHitOnly) PlayHitCameraZoom();

            if (unblockable) ShowUnblockableMark(attacker);

            // 타임라인 항목 target은 각자 startDelay/이벤트 시점에 켜진다 — 시작 전에는 반드시 꺼져 있어야 한다.
            // 프리팹에서 활성으로 남아 있거나(예: Visual 루트 아래 Slash) 이전 사용 잔류가 있으면 visual 루트 활성화와 함께
            // 공격 시작과 동시에 이펙트가 터지므로(2026-09-08 버그), 지속(persist) 중인 것만 빼고 여기서 먼저 끈다.
            ResetTimedVisualTargets();

            if (visual != null)
            {
                visual.SetActive(true);
                PlayVisualEffects(visual, attackVisuals); // 루트 즉시 재생 — 타임라인 target 하위 파티클은 제외
            }

            if (_vfxRoutine != null) StopCoroutine(_vfxRoutine);
            _vfxRoutine = null;
            if (attackVisuals != null && attackVisuals.Count > 0)
                _vfxRoutine = StartCoroutine(AttackVisualsRoutine());

            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            if (!HasHitboxDrivingVisual() && string.IsNullOrEmpty(hitboxOnEventName))
                _routine = StartCoroutine(HitboxRoutine(attacker, _finalDamage));
        }

        /// <summary>적중 시(스윙당 1회, AttackHitbox.HitLanded) 슬로우모션/진동 발동. OnHitOnly 옵션인 항목만.</summary>
        private void HandleHitFeedback()
        {
            if (useSlowMotion && slowMotionOnHitOnly) PlayHitSlowMotion();
            if (useRumble && rumbleOnHitOnly) PlayHitRumble();
            if (useCameraShake && cameraShakeOnHitOnly) PlayHitCameraShake();
            if (useCameraZoom && cameraZoomOnHitOnly) PlayHitCameraZoom();
        }

        /// <summary>카메라 쉐이크 요청 — 슬로우모션 중에도 실시간 기준으로 감쇠(unscaled). cameraShakeDelay만큼 지연 가능 (2026-09-15).</summary>
        private void PlayHitCameraShake()
        {
            FireWithDelay(cameraShakeDelay, () =>
                CameraShakeService.Shake(cameraShakeVelocity, cameraShakeDuration, cameraShakeFrequency, unscaledTime: true));
        }

        /// <summary>카메라 줌 펀치 요청 — LensCameraNode에 FOV 채널 주입 (실시간 기준).</summary>
        private void PlayHitCameraZoom()
        {
            FireWithDelay(cameraZoomDelay, () =>
                CameraZoomService.Punch(cameraZoomFovDelta, cameraZoomEaseIn, cameraZoomHold, cameraZoomEaseOut));
        }

        /// <summary>슬로우모션 요청 — FeedbackTime이 동시 요청 중 가장 강한 배율만 적용(비가중).</summary>
        private void PlayHitSlowMotion()
        {
            if (slowMotionScale >= 1f) return;
            FireWithDelay(slowMotionDelay, () =>
                Aiara.FeedbackTime.SlowMotion(slowMotionScale, slowMotionDuration, slowMotionEaseIn, slowMotionEaseOut));
        }

        /// <summary>패드 진동 요청 — FeedbackRumble이 동시 요청 중 가장 강한 세기만 적용(비가중).</summary>
        private void PlayHitRumble()
        {
            FireWithDelay(rumbleDelay, () =>
                Aiara.FeedbackRumble.Play(rumbleLowFrequency, rumbleHighFrequency, rumbleDuration));
        }

        /// <summary>
        /// 히트박스가 아닌 경로(캐리 그랩 등 직접 링크)에서 적중이 확정된 순간의
        /// 카메라 쉐이크/줌·슬로우모션·진동 연출을 강제 발동한다. 각 use* 플래그가 켜진 것만 재생한다.
        /// </summary>
        public void PlayHitFeedbacksManual()
        {
            if (useSlowMotion) PlayHitSlowMotion();
            if (useRumble) PlayHitRumble();
            if (useCameraShake) PlayHitCameraShake();
            if (useCameraZoom) PlayHitCameraZoom();
        }

        /// <summary>attackVisuals 중 히트박스를 켜는 항목이 하나라도 있는가.</summary>
        private bool HasHitboxDrivingVisual()
        {
            if (attackVisuals == null) return false;
            for (int i = 0; i < attackVisuals.Count; i++)
                if (attackVisuals[i] != null && attackVisuals[i].triggerHitbox) return true;
            return false;
        }

        private IEnumerator HitboxRoutine(GameObject attacker, float finalDamage)
        {
            if (hitbox == null) yield break;

            hitbox.Deactivate();
            if (hitboxStartDelay > 0f)
                yield return WaitTimeline(hitboxStartDelay);

            hitbox.SuppressHitReaction = false;
            hitbox.Activate(attacker, finalDamage);
            PlayHitboxFeedback();
            yield return WaitTimeline(hitboxActiveDuration);
            hitbox.Deactivate();
            _routine = null;
        }

        /// <summary>
        /// 히트박스가 열리는 순간 시전자 루트+오프셋에 hitboxFeedbackKey 재생 (적중 여부 무관, 활성 구간마다 1회).
        /// 오프셋 x는 Facing 전방(스플라인 접선, 좌향이면 반전), z는 깊이축, y는 위. 회전은 이동 연출 Feedback과 동일 규칙.
        /// </summary>
        private void PlayHitboxFeedback()
        {
            if (string.IsNullOrEmpty(hitboxFeedbackKey) || _attacker == null) return;
            var character = _attacker.GetComponentInParent<Character>();
            Transform root = character != null ? character.transform : _attacker.transform;
            var movement = character != null ? character.Movement : null;

            Vector3 pos = root.position + Vector3.up * hitboxFeedbackOffset.y;
            Quaternion rot = Quaternion.identity;
            if (movement != null)
            {
                Vector3 fwd = movement.SplineForward;
                if (fwd.sqrMagnitude < 0.0001f) fwd = root.forward;
                Vector3 faceDir = fwd.normalized * (movement.FacingRight ? 1f : -1f);
                pos += faceDir * hitboxFeedbackOffset.x;
                Vector3 depth = movement.SplineDepth;
                if (depth.sqrMagnitude > 0.0001f) pos += depth.normalized * hitboxFeedbackOffset.z;
                rot = Quaternion.LookRotation(faceDir, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);
            }
            else
            {
                pos += root.forward * hitboxFeedbackOffset.x + root.right * hitboxFeedbackOffset.z;
            }
            Aiara.FeedbackManager.PlayFeedbackAtWorld(hitboxFeedbackKey, pos, rot);
        }

        /// <summary>공격 종료/중단. 판정을 닫고 오브젝트를 비활성화한다.</summary>
        /// <summary>공격 종료/중단. 판정을 닫고 오브젝트를 비활성화한다.</summary>
        public void Stop()
        {
            _playing = false;
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            if (_vfxRoutine != null)
            {
                StopCoroutine(_vfxRoutine);
                _vfxRoutine = null;
            }
            if (_hitboxOpenRoutine != null)
            {
                StopCoroutine(_hitboxOpenRoutine);
                _hitboxOpenRoutine = null;
            }
            if (_hitboxCloseRoutine != null)
            {
                StopCoroutine(_hitboxCloseRoutine);
                _hitboxCloseRoutine = null;
            }
            StopImpactFreezeRoutines();
            _pendingHitboxOffEvent = null;
            _overridePendingOff.Clear();
            foreach (var kv in _overrideCloseRoutines)
                if (kv.Value != null) StopCoroutine(kv.Value);
            _overrideCloseRoutines.Clear();
            StopAttackVisuals();
            HideUnblockableMark();
            for (int i = 0; i < _allHitboxes.Count; i++)
                if (_allHitboxes[i] != null) _allHitboxes[i].Deactivate();
            if (hitbox != null) hitbox.Deactivate();
            gameObject.SetActive(false);
        }

        // ─────────── Unblockable Mark (가드 불가 예고 마크) ───────────

        /// <summary>
        /// 가드 불가 공격 시작 시 공격자 하위의 unblockableMarkName 오브젝트를 켠다.
        /// 오브젝트는 SpriteEffect(autoPlay) 기준이라 Off→On 토글만으로 처음부터 1회 재생된다.
        /// 공격자별로 1회 탐색 후 캐시.
        /// </summary>
        private void ShowUnblockableMark(GameObject attacker)
        {
            if (attacker == null || string.IsNullOrEmpty(unblockableMarkName)) return;

            if (_markOwner != attacker || _unblockableMark == null)
            {
                _markOwner = attacker;
                _unblockableMark = FindChildByName(attacker.transform, unblockableMarkName);
            }
            if (_unblockableMark == null) return;

            if (_unblockableMark.activeSelf) _unblockableMark.SetActive(false);
            _unblockableMark.SetActive(true);
        }

        private void HideUnblockableMark()
        {
            if (_unblockableMark != null && _unblockableMark.activeSelf)
                _unblockableMark.SetActive(false);
        }

        /// <summary>비활성 오브젝트 포함, 깊이 무관 이름 일치 자식 탐색. 자기 자신(root)은 제외.</summary>
        private static GameObject FindChildByName(Transform root, string childName)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == root) continue;
                if (all[i].name == childName) return all[i].gameObject;
            }
            return null;
        }

        private void StopImpactFreezeRoutines()
        {
            for (int i = 0; i < _impactFreezeRoutines.Count; i++)
                if (_impactFreezeRoutines[i] != null) StopCoroutine(_impactFreezeRoutines[i]);
            _impactFreezeRoutines.Clear();
        }

        private IEnumerator AttackVisualsRoutine()
        {
            int count = attackVisuals.Count;
            var playedFlags = new bool[count];
            int played = 0;
            float elapsed = 0f;
            for (int i = 0; i < count; i++)
            {
                var v = attackVisuals[i];
                bool eventDriven = v != null && !string.IsNullOrEmpty(v.animationEventName);
                bool doesNothing = v == null || (v.target == null && !v.triggerHitbox);
                if (eventDriven || doesNothing) { playedFlags[i] = true; played++; }
            }
            while (played < count)
            {
                for (int i = 0; i < count; i++)
                {
                    if (playedFlags[i]) continue;
                    if (elapsed >= attackVisuals[i].startDelay)
                    {
                        FireVisualEntry(attackVisuals[i]);
                        playedFlags[i] = true;
                        played++;
                    }
                }
                if (played >= count) break;
                yield return null;
                if (!TimelinePaused) elapsed += Time.deltaTime * TimelineSpeed; // 차징 정지 중 일시정지 + 모션 배속 비율 추종 (모션과 동기 유지)
            }
            _vfxRoutine = null;
        }

        /// <summary>
        /// 타임라인 항목 발동. Impact Freeze가 적용되는 항목이면 전역 시간을 먼저 멈추고(실시간 대기)
        /// 정지 해제 시점에 실제 발동(FireVisualEntryNow)한다. 그 외는 즉시 발동.
        /// </summary>
        private void FireVisualEntry(TimedVisual v)
        {
            if (v == null) return;
            bool freeze = v.impactFreeze && v.impactFreezeDuration > 0f && v.impactFreezeScale < 1f
                          && (!v.impactFreezeChargedOnly || _chargedUse);
            if (!freeze) { FireVisualEntryNow(v); return; }

            Aiara.FeedbackTime.Hitstop(v.impactFreezeDuration, v.impactFreezeScale);
            _impactFreezeRoutines.Add(StartCoroutine(FireAfterImpactFreeze(v)));
        }

        /// <summary>정지 시간(실시간)만큼 기다린 뒤 항목을 발동한다. 타임라인·애니는 timeScale로 함께 멈춰 있으므로 "멈칫 → 터짐"이 된다.</summary>
        private IEnumerator FireAfterImpactFreeze(TimedVisual v)
        {
            yield return new WaitForSecondsRealtime(v.impactFreezeDuration);
            FireVisualEntryNow(v);
        }

        /// <summary>항목 실제 발동 — VFX 재생(있으면) + 히트박스 활성(triggerHitbox면, hitboxDelay 뒤) + Impact Zoom 펀치.</summary>
        private void FireVisualEntryNow(TimedVisual v)
        {
            if (v == null) return;
            if (v.impactZoom && (!v.impactZoomChargedOnly || _chargedUse))
                CameraZoomService.Punch(v.impactZoomFovDelta, v.impactZoomEaseIn, v.impactZoomHold, v.impactZoomEaseOut);
            if (v.target != null) PlayTimedVisual(v);
            if (v.triggerHitbox)
            {
                if (v.hitboxDelay > 0f)
                {
                    if (_hitboxOpenRoutine != null) StopCoroutine(_hitboxOpenRoutine);
                    _hitboxOpenRoutine = StartCoroutine(OpenHitboxDelayed(v.hitboxDelay, v.hitboxDuration, v.hitboxOffEventName, v.hitReaction, v));
                }
                else
                {
                    OpenHitbox(v.hitboxDuration, v.hitboxOffEventName, v.hitReaction, v);
                }
            }
        }

        /// <summary>hitboxDelay 만큼 기다렸다가 히트박스를 연다 (이펙트 재생 후 지연 ON).</summary>
        private IEnumerator OpenHitboxDelayed(float delay, float duration, string offEvent, bool hitReaction, TimedVisual entry)
        {
            yield return WaitTimeline(delay);
            OpenHitbox(duration, offEvent, hitReaction, entry);
            _hitboxOpenRoutine = null;
        }


        /// <summary>
        /// 히트박스 활성 + 종료 예약. offEvent가 있으면 이벤트로, 없으면 duration 타이머로 종료. hitReaction=false면 이 활성 구간의 적중은 리액션 상태를 건너뛴다.
        /// entry.overrideReaction이면 이 활성 구간만 항목 전용 Reaction을 쓰고, 아니면 액션 공용 hitReaction으로 되돌린다 (이전 항목의 오버라이드가 남지 않게 매번 재설정).
        /// </summary>
        private void OpenHitbox(float duration, string offEvent, bool hitReaction = true, TimedVisual entry = null)
        {
            var hb = ResolveHitbox(entry);
            if (hb == null) return;
            hb.SuppressHitReaction = !hitReaction;
            var spec = entry != null && entry.overrideReaction ? entry.reaction : this.hitReaction;
            hb.ConfigureReaction(spec, unblockable, hitDownedTargets);
            hb.Activate(_attacker, _finalDamage);
            PlayHitboxFeedback();
            float d = duration > 0f ? duration : (hitboxActiveDuration > 0f ? hitboxActiveDuration : 0.15f);

            if (hb == hitbox)
            {
                // 공용 hitbox — 기존 단일 타이머/Off 이벤트 경로
                if (_hitboxCloseRoutine != null) StopCoroutine(_hitboxCloseRoutine);
                _hitboxCloseRoutine = null;
                if (!string.IsNullOrEmpty(offEvent))
                {
                    _pendingHitboxOffEvent = offEvent;
                }
                else
                {
                    _pendingHitboxOffEvent = null;
                    _hitboxCloseRoutine = StartCoroutine(CloseHitboxAfter(d));
                }
                return;
            }

            // 항목별 오버라이드 hitbox — 히트박스마다 독립 타이머/Off 이벤트 (공용 hitbox와 동시 활성 가능)
            if (_overrideCloseRoutines.TryGetValue(hb, out var prev) && prev != null) StopCoroutine(prev);
            _overrideCloseRoutines.Remove(hb);
            if (!string.IsNullOrEmpty(offEvent))
            {
                _overridePendingOff[hb] = offEvent;
            }
            else
            {
                _overridePendingOff.Remove(hb);
                _overrideCloseRoutines[hb] = StartCoroutine(CloseOverrideHitboxAfter(hb, d));
            }
        }

        private IEnumerator CloseOverrideHitboxAfter(AttackHitbox hb, float duration)
        {
            yield return WaitTimeline(duration);
            if (hb != null) hb.Deactivate();
            _overrideCloseRoutines.Remove(hb);
        }

        /// <summary>
        /// 클립 애니메이션 이벤트 수신. 이벤트 구동 VFX/히트박스 항목을 발동하고, 히트박스 종료 이벤트를 처리한다.
        /// </summary>
        public void HandleAnimationEvent(string eventName)
        {
            if (!gameObject.activeInHierarchy || string.IsNullOrEmpty(eventName)) return;

            if (!string.IsNullOrEmpty(forwardMoveEventName) && eventName == forwardMoveEventName)
                ForwardMoveEventReceived = true;

            if (attackVisuals != null)
            {
                for (int i = 0; i < attackVisuals.Count; i++)
                {
                    var v = attackVisuals[i];
                    if (v != null && !string.IsNullOrEmpty(v.animationEventName) && v.animationEventName == eventName)
                        FireVisualEntry(v);
                }
            }

            if (!string.IsNullOrEmpty(_pendingHitboxOffEvent) && eventName == _pendingHitboxOffEvent && hitbox != null)
            {
                hitbox.Deactivate();
                _pendingHitboxOffEvent = null;
            }

            if (_overridePendingOff.Count > 0)
            {
                AttackHitbox closeTarget = null;
                foreach (var kv in _overridePendingOff)
                    if (kv.Value == eventName && kv.Key != null) { closeTarget = kv.Key; break; }
                if (closeTarget != null)
                {
                    closeTarget.Deactivate();
                    _overridePendingOff.Remove(closeTarget);
                }
            }

            if (!HasHitboxDrivingVisual())
            {
                if (!string.IsNullOrEmpty(hitboxOnEventName) && eventName == hitboxOnEventName)
                    OpenHitboxNow();
                if (!string.IsNullOrEmpty(hitboxOffEventName) && eventName == hitboxOffEventName && hitbox != null)
                    hitbox.Deactivate();
            }
        }

        /// <summary>레거시 이벤트 구동 판정 시작. hitboxOffEventName이 없으면 hitboxActiveDuration 후 자동 종료.</summary>
        private void OpenHitboxNow()
        {
            if (hitbox == null) return;
            hitbox.SuppressHitReaction = false;
            hitbox.Activate(_attacker, _finalDamage);
            PlayHitboxFeedback();
            if (_hitboxCloseRoutine != null) StopCoroutine(_hitboxCloseRoutine);
            _hitboxCloseRoutine = null;
            if (string.IsNullOrEmpty(hitboxOffEventName))
                _hitboxCloseRoutine = StartCoroutine(CloseHitboxAfter(hitboxActiveDuration));
        }

        private IEnumerator CloseHitboxAfter(float duration)
        {
            yield return WaitTimeline(duration);
            if (hitbox != null) hitbox.Deactivate();
            _hitboxCloseRoutine = null;
        }

        private void PlayTimedVisual(TimedVisual v)
        {
            GameObject go = v.target;
            // 이전 스윙에서 분리돼 아직 월드에서 복귀 중인 이펙트라면 먼저 원위치로 회수한 뒤 재사용한다.
            var pending = go.GetComponent<DetachedVisualReturner>();
            if (pending != null && pending.IsDetached) pending.ReturnHome();
            ApplyChargeScale(go);
            ApplyChargeColor(v);
            if (go.activeSelf) go.SetActive(false);
            go.SetActive(true);
            PlayVisualEffects(go);

            // 지속 이펙트: 부모는 유지한 채 월드 위치 고정(플레이어 미추종), 지속 시간 뒤 자동 종료 + 원위치 복귀.
            if (v.persistEffect) DetachPersistentVisual(go, v.persistDuration);
        }

        /// <summary>
        /// 차징 스윙이면 이펙트를 배율만큼 확대, 일반 스윙이면 원래 크기로 복원.
        /// 기준 크기는 '직전 재생이 차징 확대 상태가 아니었을 때의 현재 스케일'로 매번 갱신한다 —
        /// 플레이 중 인스펙터에서 이펙트 크기를 고치면 그대로 다음 스윙에 반영 (2026-09-08). 차징 확대가 적용된 채로는 캐시를 유지해 원복 기준을 잃지 않는다.
        /// </summary>
        private void ApplyChargeScale(GameObject go)
        {
            if (_visualBaseScales == null) _visualBaseScales = new Dictionary<GameObject, Vector3>();
            if (_visualChargeApplied == null) _visualChargeApplied = new HashSet<GameObject>();

            bool wasCharged = _visualChargeApplied.Contains(go);
            Vector3 baseScale;
            if (!wasCharged || !_visualBaseScales.TryGetValue(go, out baseScale))
            {
                baseScale = go.transform.localScale; // 확대 안 된 상태 = 현재 값이 곧 기준(인스펙터 수정 반영)
                _visualBaseScales[go] = baseScale;
            }

            bool charge = _chargeFxScale > 1.0001f;
            go.transform.localScale = charge ? baseScale * _chargeFxScale : baseScale;
            if (charge) _visualChargeApplied.Add(go); else _visualChargeApplied.Remove(go);
        }

        /// <summary>차징 스윙 + 색 오버라이드 항목이면 ParticleSystem Start Color 교체, 아니면 원래 색 복원.</summary>
        private void ApplyChargeColor(TimedVisual v)
        {
            if (!v.chargedColorOverride) return;
            if (_originalStartColors == null) _originalStartColors = new Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient>();

            foreach (var ps in v.target.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                if (_chargeApplyColor)
                {
                    if (!_originalStartColors.ContainsKey(ps))
                        _originalStartColors[ps] = main.startColor;
                    main.startColor = v.chargedStartColor;
                }
                else if (_originalStartColors.TryGetValue(ps, out var original))
                {
                    main.startColor = original;
                }
            }
        }

        /// <summary>파티클 Clear+Play, Trail 잔상 제거 (Pool 재사용/재실행 대비).</summary>
        private static void PlayVisualEffects(GameObject root) => PlayVisualEffects(root, null);

        /// <summary>
        /// root 하위 파티클 Clear+Play. exclude(타임라인 항목 target) 하위에 있는 파티클은 건너뛴다 —
        /// visual 루트 아래에 타임라인 target이 배치된 공격(HeavyAttack/ATK_02 등)에서 루트 즉시 재생이 항목 이펙트를 미리 재생하지 않도록.
        /// </summary>
        private static void PlayVisualEffects(GameObject root, List<TimedVisual> exclude)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (IsUnderTimedTarget(ps.transform, root.transform, exclude)) continue;
                ps.Clear(true);
                ps.Play(true);
            }
            foreach (var trail in root.GetComponentsInChildren<TrailRenderer>(true))
            {
                if (IsUnderTimedTarget(trail.transform, root.transform, exclude)) continue;
                trail.Clear();
            }
        }

        /// <summary>t가 root 아래에서 어떤 타임라인 항목 target(자기 자신 포함)의 하위인가. root 자체가 target이면 제외하지 않는다.</summary>
        private static bool IsUnderTimedTarget(Transform t, Transform root, List<TimedVisual> entries)
        {
            if (entries == null) return false;
            for (int i = 0; i < entries.Count; i++)
            {
                var tg = entries[i] != null ? entries[i].target : null;
                if (tg == null || tg.transform == root) continue;
                if (t == tg.transform || t.IsChildOf(tg.transform)) return true;
            }
            return false;
        }

        /// <summary>
        /// 공격 시작 전 타임라인 항목 target을 전부 꺼서 '시작과 동시에 이펙트'가 나가지 않게 한다.
        /// 지속(persistEffect) 재생 중인 target은 건드리지 않는다 (지속 시간 뒤 스스로 꺼짐).
        /// </summary>
        private void ResetTimedVisualTargets()
        {
            if (attackVisuals == null) return;
            for (int i = 0; i < attackVisuals.Count; i++)
            {
                var v = attackVisuals[i];
                if (v == null || v.target == null || !v.target.activeSelf) continue;
                var returner = v.target.GetComponent<DetachedVisualReturner>();
                if (returner != null && returner.IsDetached) continue;
                v.target.SetActive(false);
            }
        }

        private void StopAttackVisuals()
        {
            if (attackVisuals == null) return;
            for (int i = 0; i < attackVisuals.Count; i++)
            {
                var v = attackVisuals[i];
                if (v == null || v.target == null) continue;

                // 이미 지속(persistEffect) 중인 이펙트는 건드리지 않는다 — 월드에 고정된 채 지속 시간을 마치고 스스로 꺼진 뒤 복귀한다.
                var returner = v.target.GetComponent<DetachedVisualReturner>();
                if (returner != null && returner.IsDetached) continue;

                foreach (var ps in v.target.GetComponentsInChildren<ParticleSystem>(true))
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                foreach (var trail in v.target.GetComponentsInChildren<TrailRenderer>(true))
                    trail.Clear();
                v.target.SetActive(false);
            }
        }

        /// <summary>지속 이펙트를 부모 유지 상태로 월드 위치에 고정하고, 지속 시간 뒤 끄면서 원래 로컬 위치로 복귀시킨다.</summary>
        private void DetachPersistentVisual(GameObject go, float duration)
        {
            var returner = go.GetComponent<DetachedVisualReturner>();
            if (returner == null) returner = go.AddComponent<DetachedVisualReturner>();
            returner.BeginDetach(duration);
        }
    }
}
