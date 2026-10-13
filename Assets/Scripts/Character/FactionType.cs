namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 캐릭터 진영. Character 공통 정보(Character.Faction)로 소유하며,
    /// 적대 관계 판별은 AIController.IsHostile이 담당한다.
    /// </summary>
    public enum FactionType
    {
        Player,
        Ally,
        Enemy,
        Neutral
    }
}
