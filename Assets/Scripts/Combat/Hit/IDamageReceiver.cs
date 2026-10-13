namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 피격 계약. 공격자(AttackHitbox)가 구체 클래스(EnemyHealth 등)를 직접 알지 않게 한다.
    /// </summary>
    public interface IDamageReceiver
    {
        void ReceiveDamage(DamageInfo damage);
    }
}
