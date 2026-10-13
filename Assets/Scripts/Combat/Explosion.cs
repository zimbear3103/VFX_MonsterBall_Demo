using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 공용 폭발(범위 피해) — 발생 → 범위 탐색(OverlapSphere) → 진영 필터 → DamageInfo → Character.ReceiveDamage.
    ///
    /// 이관 원칙(구버전 CharacterSelfDestruct / AreaAttackTrigger 이식):
    ///   - 폭발 전용 HP 감소 코드를 만들지 않는다. 공용 피격 흐름(IDamageReceiver)만 사용한다.
    ///   - 진영 판정은 AIController.IsHostile 하나만 쓴다(아군 오사 방지).
    ///   - 동일 대상 중복 타격을 막고, 여러 대상 동시 타격을 지원한다.
    ///
    /// 사용 방식(둘 다 지원):
    ///   1) AttackAction 자식으로 두고 TimedVisual로 특정 프레임에 활성화 → OnEnable 자동 폭발
    ///      (공격자 = 부모 Character 자동 탐색). ProjectileLauncher와 동일 패턴.
    ///   2) 코드에서 Explode(attacker) 직접 호출 (대포 탄환 충돌·자폭 등).
    /// </summary>
    public class Explosion : MonoBehaviour
    {
        [Header("Range / Damage")]
        [Tooltip("폭발 반경(m) (구버전 ExplosionRadius 5 / AreaAttack 3)")]
        [SerializeField] private float radius = 5f;
        [Tooltip("기본 데미지(고정). useAttackerPower가 켜져 있으면 공격력×배율로 대체")]
        [SerializeField] private float damage = 30f;
        [Tooltip("데미지를 공격자 공격력 기반으로 계산할지 (기본 고정 데미지 사용)")]
        [SerializeField] private bool useAttackerPower = false;
        [Tooltip("useAttackerPower일 때 공격력에 곱할 배율")]
        [SerializeField] private float attackerPowerMultiplier = 1f;
        [Tooltip("가드 불가 폭발 여부 (구버전 자폭/버스트는 Unblockable)")]
        [SerializeField] private bool unblockable = false;

        [Header("Targeting")]
        [Tooltip("탐색할 레이어 마스크. 기본 모든 레이어(이후 Character/진영으로 다시 거른다)")]
        [SerializeField] private LayerMask targetLayers = ~0;
        [Tooltip("공격자와 적대(AIController.IsHostile)인 대상만 타격. 끄면 진영 무시하고 전부")]
        [SerializeField] private bool hostileOnly = true;
        [Tooltip("공격자 자신도 타격 대상에 포함할지 (자폭 자기 데미지). 켜면 아래 Self Damage 비율로 자기 자신에게도 피해를 준다")]
        [SerializeField] private bool includeAttacker = false;
        [Tooltip("자폭 자기 피해 = 공격자 최대 HP × 이 비율 (구버전 SelfDamageRatio 0.3). 0이면 위 Damage 값을 자기 피해로 사용. HP 0 도달 시 공용 사망 처리")]
        [SerializeField] private float selfDamageRatioOfMaxHP = 0f;
        [Tooltip("동일 대상 1회만 타격 (다단 방지)")]
        [SerializeField] private bool hitOncePerTarget = true;

        [Header("Trigger")]
        [Tooltip("이 오브젝트가 활성화(OnEnable)되면 자동 폭발. AttackAction의 TimedVisual로 점화하는 패턴")]
        [SerializeField] private bool explodeOnEnable = true;
        [Tooltip("자동 폭발 지연(초). 0=즉시")]
        [SerializeField] private float explodeDelay = 0f;

        [Header("Feedback")]
        [Tooltip("폭발 연출 FeedbackManager 키 (구버전 Bomb/SelfDestructBurst 등). 비우면 재생 안 함")]
        [SerializeField] private string feedbackKey = "Bomb";
        [Tooltip("연출 재생 위치 오프셋(m)")]
        [SerializeField] private Vector3 feedbackOffset = new Vector3(0f, 1f, 0f);

        [Header("Hit Reaction")]
        [Tooltip("폭발에 맞은 적대 대상의 피격 반응(Kind + 힘 + Ground Bounce). 구버전 자폭 넉백(수직/수평) 이관 — Launch로 띄우기 등. 자기 자신에겐 적용하지 않는다")]
        [SerializeField] private HitReactionSpec reaction = HitReactionSpec.Default;

        [Header("Debug")]
        [SerializeField] private bool drawGizmo = true;

        [System.NonSerialized] private Character _attacker;
        private readonly List<Character> _hitThisExplosion = new List<Character>();

        /// <summary>공격자(진영/데미지 기준)를 미리 지정. Explode 전에 호출하거나 OnEnable 자동 폭발 전에 세팅.</summary>
        public void SetAttacker(Character attacker) => _attacker = attacker;

        private void OnEnable()
        {
            if (!explodeOnEnable) return;
            if (explodeDelay > 0f) Invoke(nameof(ExplodeSelf), explodeDelay);
            else ExplodeSelf();
        }

        private void OnDisable()
        {
            CancelInvoke(nameof(ExplodeSelf));
        }

        private void ExplodeSelf() => Explode(_attacker != null ? _attacker : GetComponentInParent<Character>());

        /// <summary>공격자 지정 폭발.</summary>
        public void Explode(Character attacker)
        {
            _attacker = attacker;
            Vector3 center = transform.position;

            PlayFeedback(center);

            _hitThisExplosion.Clear();
            var cols = Physics.OverlapSphere(center, radius, targetLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < cols.Length; i++)
            {
                var victim = cols[i].GetComponentInParent<Character>();
                if (victim == null) continue;
                if (victim.CurrentHP <= 0f) continue;                 // 사망 대상 제외
                if (attacker != null && victim == attacker) continue; // 공격자 자신은 아래에서 별도 처리(자폭 자기 피해)
                if (hitOncePerTarget && _hitThisExplosion.Contains(victim)) continue;

                // 진영 필터 — 공격자와 적대 대상만 (아군 오사 방지)
                if (hostileOnly && attacker != null && !AIController.IsHostile(attacker, victim))
                    continue;

                Vector3 dir = victim.transform.position - center;
                dir.y = 0f;
                dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;

                var info = new DamageInfo(attacker != null ? attacker.gameObject : null, ResolveDamage(attacker), victim.transform.position, dir);
                info.Unblockable = unblockable;
                info.Reaction = reaction;   // 폭발 넉백/에어본 등(구버전 자폭 KnockbackVertical/Horizontal 이관)
                victim.ReceiveDamage(info);

                if (hitOncePerTarget) _hitThisExplosion.Add(victim);
            }

            // 자기 피해(자폭): 공격자 자신에게 별도 적용 — OverlapSphere 마스크와 무관하게 확정 적용.
            // 데미지는 공용 피격 흐름(ReceiveDamage)만 사용한다(자폭 전용 HP 조작 없음).
            if (includeAttacker && attacker != null && attacker.CurrentHP > 0f)
                ApplySelfDamage(attacker, center);
        }

        /// <summary>
        /// 자폭 자기 피해 — selfDamageRatioOfMaxHP&gt;0이면 최대HP×비율(구버전 SelfDamageRatio), 아니면 기본 damage.
        /// 가드 불가 + 반응 없음(Kind=None): 자기 자신은 넉백/에어본 없이 데미지만 받는다.
        /// HP가 0이 되면 공용 피격 흐름(ReceiveDamage → Dead)이 사망을 처리한다(자폭 후 사망).
        /// </summary>
        private void ApplySelfDamage(Character attacker, Vector3 center)
        {
            float selfDamage = selfDamageRatioOfMaxHP > 0f ? attacker.MaxHP * selfDamageRatioOfMaxHP : damage;
            if (selfDamage <= 0f) return;
            Vector3 dir = attacker.transform.position - center;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
            var info = new DamageInfo(attacker.gameObject, selfDamage, attacker.transform.position, dir);
            info.Unblockable = true;   // 자기 폭발은 가드 불가
            attacker.ReceiveDamage(info);
        }

        private float ResolveDamage(Character attacker)
        {
            if (useAttackerPower && attacker != null && attacker.Stats != null)
                return attacker.Stats.FinalAttackPower * attacker.Stats.DamageOutputMultiplier * attackerPowerMultiplier;
            return damage;
        }

        private void PlayFeedback(Vector3 center)
        {
            if (string.IsNullOrEmpty(feedbackKey)) return;
            Aiara.FeedbackManager.PlayFeedbackAtWorld(feedbackKey, center + feedbackOffset, Quaternion.identity);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmo) return;
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
