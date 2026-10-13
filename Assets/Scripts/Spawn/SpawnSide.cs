namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 플레이어 기준 스폰 방향(벨트스크롤 스플라인 진행축 기준).
    ///   Auto   : 스폰마다 진행/후방을 번갈아 배치(양쪽 분산)
    ///   Front  : 스플라인 진행 방향(앞)
    ///   Back   : 진행 반대(뒤)
    ///   Random : 매 스폰 무작위
    /// </summary>
    public enum SpawnSide
    {
        Auto,
        Front,
        Back,
        Random
    }
}
