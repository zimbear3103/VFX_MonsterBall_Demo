namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 적의 '등장 방식'만 결정하는 값 — 전투 AI와 분리된다.
    /// 모든 등장 방식은 최종적으로 동일 흐름을 사용한다:
    ///   등장/활성화 → SpawnEntry(EnemyCharacter.AIState.Entering: 대열 합류) → 기존 AIController 제어.
    ///
    ///   Ground     : (기존 동작) 플레이어 기준 지상 위치에 생성 → 곧바로 정상 AI.
    ///   AirDrop    : 플레이어 위 공중 생성 → 낙하 → 착지 → SpawnEntry.
    ///   OffScreen  : 카메라 화면 밖 생성 → 걸어 들어옴(진입 중 벽 통과) → SpawnEntry.
    ///
    /// 씬에 미리 배치된 적(선발대)은 기존 EnemySpawner.preplacedEnemies + wakePreplacedOnSpawn을 사용한다.
    /// SpawnEntry(Entering)는 EnemyCharacter가 소유하는 공통 진입 상태이며, 등장 연출은
    /// EnemyEntranceController가 담당한다(스포너는 '어떤/어디서/언제/어떻게 등장'까지만 책임).
    /// </summary>
    public enum SpawnType
    {
        Ground,
        AirDrop,
        OffScreen
    }
}
