using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Companion data on a carry AttackAction (placed under AttackRoot) that marks it as a chain-grab
    /// Carry combo hit. CharacterChainGrab's carry combo runner reads this to fire the player animator
    /// trigger and to decide whether the hit keeps the enemy held (stab) or releases the grab and applies
    /// the sibling AttackAction's Reaction (finisher, e.g. Launch).
    ///
    /// Mirrors the GrabAttackLink convention: capability/routing data lives on the AttackAction prefab,
    /// damage/reaction values live on the sibling AttackAction (DamageMultiplier / Reaction / Unblockable),
    /// and the combo graph (XXX / XXY) is authored with the AttackAction.transitions list.
    /// </summary>
    [RequireComponent(typeof(AttackAction))]
    [AddComponentMenu("Yeolha/Combat/Carry Attack Link")]
    public class CarryAttackLink : MonoBehaviour
    {
        [Header("Animation")]
        [Tooltip("이 캐리 타에서 플레이어 애니메이터에 쏠 트리거 이름(예: CarryAttack, CarryAttackFinish).")]
        [SerializeField] private string animatorTrigger = "CarryAttack";

        [Header("Behaviour")]
        [Tooltip("true면 이 타가 잡기를 놓고 AttackAction의 Reaction(런치 등)을 적용하는 피니셔. false면 잡은 채 데미지만 주는 스탭.")]
        [SerializeField] private bool releasesGrab = false;
        [Tooltip("true면 캐리 중 Heavy(Y) 입력이 언제든(홀딩/몇 타든) 이 타로 바로 발동한다. 캐리 그룹에서 하나만 지정.")]
        [SerializeField] private bool isHeavyFinisher = false;
        [Tooltip("스탭(비피니셔) 타에서 다음 콤보 입력을 받는 시간(초). 지나면 홀딩으로 복귀. 0이면 러너 기본값 사용.")]
        [SerializeField] private float stepWindow = 0.6f;
        [Tooltip("이 스탭(비피니셔) 캐리 공격 중 플레이어 이동을 멈추는 시간(초). 이 시간 동안 제자리 정지 후 캐리 감속 이동으로 복귀. 0 이하면 CharacterChainGrab의 carryStabLockDuration 기본값 사용. 피니셔는 이 값을 무시한다.")]
        [SerializeField] private float stabLockDuration = 0f;

        [Header("Finisher Timing")]
        [Tooltip("피니셔(releasesGrab)에서 적을 '차서 날리는' 순간(초). 0 이하면 CharacterChainGrab 기본값 사용.")]
        [SerializeField] private float finisherKickTime = 1.4f;
        [Tooltip("피니셔 애니 총 재생 시간(초) = 캐리 종료 시점. 던지기 클립 길이에 맞춘다. 0 이하면 CharacterChainGrab 기본값.")]
        [SerializeField] private float finisherDuration = 2.3f;
        [Tooltip("피니셔 중 적을 손(HoldAnchor)이 아니라 플레이어 기준 고정 오프셋에 둔다 — 손 크게 움직이는 애니에서 적이 손에 붙어(자식처럼) 보이는 것 방지.")]
        [SerializeField] private bool finisherFixedHold = true;
        [Tooltip("피니셔 고정 홀드 오프셋(m, 플레이어 루트 기준). 오른쪽 볼 때: x=정면, y=높이, z=깊이. Flip 시 x 자동 반전.")]
        [SerializeField] private Vector3 finisherHoldOffset = new Vector3(1.3f, 1.3f, 0f);

        [Header("Hit Effect")]
        [Tooltip("피격자가 재생하는 공용 기본 Hit 이펙트(CharacterCombat.PlayHitFeedback)를 이 캐리 타에서 낼지. false면 공용 타격 이펙트를 끈다(아래 지정한 프리팹만 보이게). AttackAction.showHitEffect와 동일 개념.")]
        [SerializeField] private bool showHitEffect = true;
        [Tooltip("아래 지정한 이펙트 프리팹(Hit Effect)을 이 캐리 타에서 스폰할지. false면 프리팹 참조는 유지한 채 스폰만 끈다. 공용 Hit 이펙트와 겹쳐 지저분할 때 한쪽만 끄기 위함.")]
        [SerializeField] private bool spawnHitEffect = true;
        [Tooltip("타격 시 스폰할 이펙트 프리팹. 비우면 스폰 안 함(피니셔는 CharacterChainGrab의 finisherFeedbackKey로 폴백).")]
        [SerializeField] private GameObject hitEffect;
        [Tooltip("이펙트 위치 오프셋 — 오른쪽 바라볼 때 기준(x=정면, y=높이, z=깊이). Flip 시 정면(x) 자동 반전.")]
        [SerializeField] private Vector3 hitEffectOffset = Vector3.zero;
        [Tooltip("이펙트 자동 파괴 시간(초). 0이면 파괴 안 함(수명은 프리팹이 관리).")]
        [SerializeField] private float hitEffectLifetime = 2f;

        /// <summary>플레이어 애니메이터에 쏠 트리거 이름.</summary>
        public string AnimatorTrigger => animatorTrigger;
        /// <summary>피니셔 여부 — true면 잡기 해제 + AttackAction.Reaction 적용.</summary>
        public bool ReleasesGrab => releasesGrab;
        /// <summary>Heavy(Y)로 언제든 발동하는 캐리 피니셔인가.</summary>
        public bool IsHeavyFinisher => isHeavyFinisher;
        /// <summary>다음 콤보 입력을 받는 시간(초). 0 이하면 러너 기본값.</summary>
        public float StepWindow => stepWindow;
        /// <summary>스탭 캐리 공격 중 플레이어 이동을 멈추는 시간(초). 0 이하면 러너 기본값(carryStabLockDuration).</summary>
        public float StabLockDuration => stabLockDuration;
        /// <summary>피니셔 킥(적 날림) 시점(초). 0 이하면 러너 기본값.</summary>
        public float FinisherKickTime => finisherKickTime;
        /// <summary>피니셔 애니 총 길이 = 캐리 종료(초). 0 이하면 러너 기본값.</summary>
        public float FinisherDuration => finisherDuration;
        /// <summary>피니셔 중 적을 손 대신 고정 오프셋에 둘지.</summary>
        public bool FinisherFixedHold => finisherFixedHold;
        /// <summary>피니셔 고정 홀드 오프셋(플레이어 루트 기준, 벨트-상대).</summary>
        public Vector3 FinisherHoldOffset => finisherHoldOffset;
        /// <summary>피격자 공용 기본 Hit 이펙트(PlayHitFeedback)를 이 캐리 타에서 낼지. DamageInfo.ShowHitEffect로 전달.</summary>
        public bool ShowHitEffect => showHitEffect;
        /// <summary>지정한 이펙트 프리팹(HitEffect)을 스폰할지. false면 참조 유지한 채 스폰만 생략.</summary>
        public bool SpawnHitEffect => spawnHitEffect;
        /// <summary>타격 시 스폰할 이펙트 프리팹(없으면 null).</summary>
        public GameObject HitEffect => hitEffect;
        /// <summary>이펙트 위치 오프셋(벨트-상대, Flip 반영).</summary>
        public Vector3 HitEffectOffset => hitEffectOffset;
        /// <summary>이펙트 자동 파괴 시간(초). 0 이하면 파괴 안 함.</summary>
        public float HitEffectLifetime => hitEffectLifetime;
    }
}
