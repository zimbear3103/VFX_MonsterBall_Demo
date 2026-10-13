using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 잠재능력 "나찰" (구버전 LatentAbility_Nachal 이식).
    /// 발동 시: 이동/데미지/공격속도 ×1.5 + 슈퍼아머 + 발동 순간 주변 적 AoE 버스트.
    /// 버프는 CharacterRuntimeStats 배율 축에 곱셈으로 얹고, 해제 시 나눗셈으로 원복(중첩 안전).
    /// 데미지는 전부 공용 피격(Character.ReceiveDamage) — 잠재능력이 HP를 직접 만지지 않는다.
    /// </summary>
    [CreateAssetMenu(menuName = "Yeolha/Latent Ability/Nachal", fileName = "Latent_Nachal")]
    public class LatentAbilityNachal : LatentAbility
    {
        [Header("Buff Multipliers (구버전 1.5)")]
        [SerializeField] private float speedMultiplier = 1.5f;
        [SerializeField] private float damageMultiplier = 1.5f;
        [SerializeField] private float attackSpeedMultiplier = 1.5f;
        [SerializeField] private bool superArmor = true;

        [Header("Activation Burst (구버전 LatentBurst)")]
        [Tooltip("발동 순간 폭발 반경(m) (구버전 LatentBurstRadius 5)")]
        [SerializeField] private float burstRadius = 5f;
        [Tooltip("버스트 데미지 = 대상 최대 HP × 이 비율 (구버전 1%)")]
        [SerializeField] private float burstDamagePercent = 0.01f;
        [Tooltip("버스트 최소 데미지")]
        [SerializeField] private float burstMinDamage = 1f;
        [Tooltip("버스트 FeedbackManager 키")]
        [SerializeField] private string burstFeedbackKey = "LatentBurst";

        public override void Activate(Character owner)
        {
            if (owner == null) return;
            var s = owner.Stats;
            if (s != null)
            {
                s.MoveSpeedExternalMultiplier *= speedMultiplier;
                s.DamageOutputMultiplier *= damageMultiplier;
                s.AttackSpeedMultiplier *= attackSpeedMultiplier;
            }
            if (superArmor && owner.Combat != null) owner.Combat.SuperArmorActive = true;
            DoBurst(owner);
        }

        public override void Deactivate(Character owner)
        {
            if (owner == null) return;
            var s = owner.Stats;
            if (s != null)
            {
                if (speedMultiplier > 0f) s.MoveSpeedExternalMultiplier /= speedMultiplier;
                if (damageMultiplier > 0f) s.DamageOutputMultiplier /= damageMultiplier;
                if (attackSpeedMultiplier > 0f) s.AttackSpeedMultiplier /= attackSpeedMultiplier;
            }
            if (superArmor && owner.Combat != null) owner.Combat.SuperArmorActive = false;
        }

        private void DoBurst(Character owner)
        {
            Vector3 center = owner.transform.position;
            if (!string.IsNullOrEmpty(burstFeedbackKey))
                Aiara.FeedbackManager.PlayFeedbackAtWorld(burstFeedbackKey, center + Vector3.up, Quaternion.identity);

            var cols = Physics.OverlapSphere(center, burstRadius, ~0, QueryTriggerInteraction.Collide);
            var hit = new HashSet<Character>();
            for (int i = 0; i < cols.Length; i++)
            {
                var victim = cols[i].GetComponentInParent<Character>();
                if (victim == null || victim == owner || victim.IsDead || hit.Contains(victim)) continue;
                if (!AIController.IsHostile(owner, victim)) continue;
                hit.Add(victim);

                float dmg = Mathf.Max(burstMinDamage, victim.MaxHP * burstDamagePercent);
                Vector3 dir = victim.transform.position - center;
                dir.y = 0f;
                dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
                var info = new DamageInfo(owner.gameObject, dmg, victim.transform.position, dir);
                info.Unblockable = true;
                victim.ReceiveDamage(info);
            }
        }
    }
}
