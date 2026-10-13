using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 한 번의 타격 정보. AttackHitbox → IDamageReceiver로 전달되는 최소 데이터.
    /// 향후 확장: Knockback / HitStun / HitStop / Critical / AttackType 등.
    /// </summary>
    public struct DamageInfo
    {
        /// <summary>공격자 루트 GameObject (플레이어 등).</summary>
        public GameObject Attacker;
        /// <summary>최종 데미지 (AttackPower × DamageMultiplier 등 계산 완료 값).</summary>
        public float Damage;
        /// <summary>대략적인 명중 위치 (월드).</summary>
        public Vector3 HitPoint;
        /// <summary>공격자 → 대상 수평 방향 (정규화).</summary>
        public Vector3 HitDirection;
        /// <summary>가드 불가 공격 여부(구버전 Unblockable). true면 CharacterDefense가 방어를 무효화한다.</summary>
        public bool Unblockable;

        // ─── 강제 피격 반응 (공격 데이터 AttackAction가 지정 → AttackHitbox가 채움) ───
        /// <summary>피격 반응 한 벌(Kind + 공용 힘 + Ground Bounce). CharacterCombat.ReceiveDamage가 상태 분기에 사용.</summary>
        public HitReactionSpec Reaction;
        /// <summary>다운(넉다운/에어본 누움) 중인 대상도 반응시키는 공격인가(OTG). AttackAction.hitDownedTargets → AttackHitbox가 채움.
        /// true면 다운 중 피격 시 전용 다운 피격 반응(누움 유지 + 모션)을 재생한다. false면 기존처럼 데미지만 적용.</summary>
        public bool HitsDowned;

        // ─── 몸통 충돌 (AttackAction.bodyCollision → AttackHitbox가 채움) ───
        /// <summary>날아가는 피격자가 다른 적과 겹칠 때 줄 피해(최종값). 0 = 몸통 충돌 없음.
        /// 강제 반응 상태가 FlyingBodyImpact로 비행 중 판정하며, 맞은 적은 넉다운된다.</summary>
        public float BodyImpactDamage;
        /// <summary>몸통 충돌 판정 반경(m). 0 이하면 기본값(0.8) 사용.</summary>
        public float BodyImpactRadius;

        // ─── 치명타 (공격 데이터 AttackAction가 허용 → AttackHitbox가 첫 타격에서 1회 Roll) ───
        /// <summary>이 타격이 치명타인지. 데미지는 이미 배수 적용된 값이며, 이 플래그는 연출(VFX/SFX/데미지넘버) 분기에만 사용.</summary>
        public bool IsCritical;

        // ─── 피격 이펙트 표시 (공격 데이터 AttackAction가 지정 → AttackHitbox가 채움) ───
        /// <summary>피격 지점 Hit 이펙트(피격자 CharacterCombat.PlayHitFeedback)를 재생할지. false면 이 공격은 타격 이펙트를 생략한다. 기본 true.</summary>
        public bool ShowHitEffect;

        // ─── 피격 리액션 상태 억제 (애니 제어권을 가진 시전자가 지정) ───
        /// <summary>true면 CharacterCombat.ReceiveDamage가 표준 피격 리액션 상태(EnterHitReactionState → DamagedBackward)를 건너뛴다.
        /// 데미지/이펙트/사망 처리는 그대로. 체인 캐리 스탭처럼 시전자가 피격 애니(AirGrabCarriedHit)를 직접 구동할 때 기본 히트모션과 겹치는 것을 막는다. 기본 false.</summary>
        public bool SuppressHitReaction;


        public DamageInfo(GameObject attacker, float damage, Vector3 hitPoint, Vector3 hitDirection)
        {
            Attacker = attacker;
            Damage = damage;
            HitPoint = hitPoint;
            HitDirection = hitDirection;
            Unblockable = false;
            Reaction = default; // Kind=None(일반 경직), 힘 0
            HitsDowned = false;
            BodyImpactDamage = 0f;
            BodyImpactRadius = 0f;
            IsCritical = false;
            ShowHitEffect = true;
            SuppressHitReaction = false;

        }
    }
}
