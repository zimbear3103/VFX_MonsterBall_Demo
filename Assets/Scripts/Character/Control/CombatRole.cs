namespace Yeolha.BeltScroll
{
    /// <summary>
    /// AIController가 전투 참여 AI에게 배분하는 상위 전투 역할.
    /// 역할의 '실행'(이동/공격/애니)은 EnemyCharacter가 담당한다 — 여기서는 의미만 정의.
    ///
    ///   Aggressive : 현재 공격 권한. 접근 → 공격 → 종료 후 권한 반환.
    ///   Pressure   : 다음 공격 후보. Guard보다 가까이 붙어 압박하되 즉시 공격은 하지 않음.
    ///   Guard      : 순서 대기. 적절한 거리를 유지하며 경계/위치 조정.
    /// </summary>
    public enum CombatRole
    {
        Aggressive,
        Pressure,
        Guard
    }
}
