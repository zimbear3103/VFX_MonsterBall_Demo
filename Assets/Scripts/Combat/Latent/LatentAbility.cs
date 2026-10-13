using UnityEngine;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 잠재능력 데이터 베이스 (ScriptableObject) — 구버전 LatentAbility 이식.
    /// 특정 PlayerController/공격 스크립트를 직접 찾아가지 않고, 소유 Character에만 효과를 건다.
    /// 발동/해제는 CharacterLatentAbility(공용 전투 이벤트/조건 기반)가 호출한다.
    /// </summary>
    public abstract class LatentAbility : ScriptableObject
    {
        /// <summary>발동 — 소유자에게 효과 적용.</summary>
        public abstract void Activate(Character owner);
        /// <summary>해제 — 적용한 효과 원복.</summary>
        public abstract void Deactivate(Character owner);
    }
}
