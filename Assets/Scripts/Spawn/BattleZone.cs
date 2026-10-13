using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 전투 구역 (구버전 SpawnBattleScene의 ver2 최소 재작성).
    /// 플레이어가 트리거에 진입하면 waves[0].Spawn() → 웨이브 전멸 시 다음 웨이브 →
    /// 마지막 웨이브 전멸 시 전투 종료. 벽(battleWalls) 온오프와 UnityEvent 훅 제공.
    /// 그룹 동시공격 제한·순차 MaxAlive·로테이션은 미구현(AI 설계에 흡수 예정).
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class BattleZone : MonoBehaviour
    {
        [Header("Waves")]
        [Tooltip("웨이브 순서대로 배열된 스포너 목록. n번 전멸 시 n+1번 Spawn()")]
        [SerializeField] private List<EnemySpawner> waves = new List<EnemySpawner>();

        [Header("Walls")]
        [Tooltip("전투 중 활성화할 벽 루트(선택). 시작 시 ON, 종료 시 OFF")]
        [SerializeField] private GameObject battleWalls;

        [Header("Options")]
        [Tooltip("true면 트리거는 1회만 동작")]
        [SerializeField] private bool triggerOnce = true;

        [Header("Events")]
        [Tooltip("전투 시작 시")]
        public UnityEvent onBattleStart;
        [Tooltip("마지막 웨이브 전멸(전투 종료) 시")]
        public UnityEvent onBattleCleared;

        /// <summary>전투가 진행 중인지.</summary>
        public bool BattleActive { get; private set; }

        /// <summary>전투가 종료(클리어)되었는지.</summary>
        public bool BattleCleared { get; private set; }

        private bool _triggered;
        private int _currentWave = -1;

        private void Awake()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            if (box != null) box.isTrigger = true;

            if (battleWalls != null) battleWalls.SetActive(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_triggered && triggerOnce) return;
            if (BattleActive) return;
            if (!IsPlayer(other)) return;

            _triggered = true;
            StartBattle();
        }

        private static bool IsPlayer(Collider other)
        {
            Character character = other.GetComponentInParent<Character>();
            if (character == null) return false;

            Character player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player != null) return character == player;
            return character.Faction == FactionType.Player;
        }

        /// <summary>전투 시작. 벽 ON + 첫 웨이브 Spawn.</summary>
        public void StartBattle()
        {
            if (BattleActive || BattleCleared) return;
            BattleActive = true;

            if (battleWalls != null) battleWalls.SetActive(true);
            onBattleStart?.Invoke();

            _currentWave = -1;
            AdvanceWave();
        }

        private void AdvanceWave()
        {
            _currentWave++;

            // 비어 있는(널) 웨이브는 건너뛴다.
            while (_currentWave < waves.Count && waves[_currentWave] == null)
                _currentWave++;

            if (_currentWave >= waves.Count)
            {
                EndBattle();
                return;
            }

            EnemySpawner wave = waves[_currentWave];
            wave.OnAllDefeated += OnWaveDefeated;
            wave.Spawn();
        }

        private void OnWaveDefeated()
        {
            if (_currentWave >= 0 && _currentWave < waves.Count && waves[_currentWave] != null)
                waves[_currentWave].OnAllDefeated -= OnWaveDefeated;

            AdvanceWave();
        }

        private void EndBattle()
        {
            if (!BattleActive) return;
            BattleActive = false;
            BattleCleared = true;

            if (battleWalls != null) battleWalls.SetActive(false);
            onBattleCleared?.Invoke();
        }

        private void OnDestroy()
        {
            if (_currentWave >= 0 && _currentWave < waves.Count && waves[_currentWave] != null)
                waves[_currentWave].OnAllDefeated -= OnWaveDefeated;
        }
    }
}
