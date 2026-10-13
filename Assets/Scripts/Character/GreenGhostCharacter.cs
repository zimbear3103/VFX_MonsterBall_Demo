using System.Collections.Generic;
using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 녹귀(GreenGhost) 계열 적 — EnemyCharacter 파생.
    ///
    /// 구버전(yeolhadiary) 녹귀 AI의 "WeaponDeciding: Attack1 60% / Attack2 40% 랜덤 선택"을
    /// ver2 구조로 번역한 클래스. 고유 행동은 가중치 기반 공격 선택뿐이므로
    /// SelectAttackActionName() 훅 하나만 override 하고, 감지/추적/공격/피격/사망은
    /// 전부 EnemyCharacter·Character 공용 시스템을 그대로 사용한다.
    ///
    /// 녹귀-적/녹귀-백 차이는 데이터(Ability·AttackAction·가중치)로만 표현한다 — 파생 클래스 추가 없음.
    /// </summary>

    public class GreenGhostCharacter : EnemyCharacter
    {
        /// <summary>가중치 공격 항목 1개 (AttackAction 이름 + 선택 가중치).</summary>
        [System.Serializable]
        public class WeightedAttack
        {
            [Tooltip("AttackAction.AttackName (AttackRoot 아래 프리팹 이름)")]
            public string attackName = "";
            [Tooltip("선택 가중치. 구버전 녹귀 = Attack1:6, Attack2:4")]
            [Min(0f)] public float weight = 1f;
        }

        [Header("Nokgwi Attack Selection")]
        [Tooltip("가중치 랜덤으로 선택할 공격 목록. 비어 있으면 기본 attackActionName 사용")]
        [SerializeField] private List<WeightedAttack> weightedAttacks = new List<WeightedAttack>();

        /// <summary>가중치 랜덤 선택. 목록이 비었거나 가중치 합이 0이면 기본 공격 이름으로 폴백.</summary>
        protected override string SelectAttackActionName()
        {
            if (weightedAttacks == null || weightedAttacks.Count == 0)
                return base.SelectAttackActionName();

            float total = 0f;
            for (int i = 0; i < weightedAttacks.Count; i++)
            {
                var e = weightedAttacks[i];
                if (e != null && !string.IsNullOrEmpty(e.attackName))
                    total += e.weight;
            }
            if (total <= 0f)
                return base.SelectAttackActionName();

            float roll = Random.value * total;
            for (int i = 0; i < weightedAttacks.Count; i++)
            {
                var e = weightedAttacks[i];
                if (e == null || string.IsNullOrEmpty(e.attackName)) continue;
                roll -= e.weight;
                if (roll <= 0f)
                    return e.attackName;
            }
            return base.SelectAttackActionName();
        }
    }
}
