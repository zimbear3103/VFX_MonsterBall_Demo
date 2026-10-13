using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 웨이브 단위 적 스포너 (구버전 MonsterSpawner의 ver2 최소 재작성).
    /// - spawnEntries: Spawn() 호출 시 엔트리별 delay 후 프리팹 Instantiate
    /// - preplacedEnemies: 씬에 미리 배치된 적을 추적(생성 없이 전멸 판정에만 편입)
    /// - 전멸 감지: Character.OnHealthChanged 구독으로 CurrentHP<=0 판정(코어 무수정)
    /// - 전원 사망 시 OnAllDefeated(C# event) + onAllDefeated(UnityEvent) 발화
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        [Serializable]
        public class SpawnEntry
        {
            [Tooltip("스폰할 적 프리팹 (Character 필수)")]
            public GameObject prefab;
            [Tooltip("스폰 위치. 비우면 스포너 자신의 위치 사용")]
            public Transform spawnPoint;
            [Tooltip("Spawn() 호출 후 이 엔트리가 실제 생성되기까지의 지연(초)")]
            public float delay;

            [Tooltip("플레이어 기준으로 스폰할지. spawnPoint가 지정돼 있으면 그쪽이 우선")]
            public bool spawnRelativeToPlayer = true;
            [Tooltip("플레이어로부터의 스폰 거리. 0이면 적 AttackRange 기반 자동(원거리=멀리, 근거리=가까이)")]
            public float spawnDistanceOverride = 0f;
            [Tooltip("스폰 방향(플레이어 스플라인 진행축 기준). Auto=번갈아. OffScreen은 이 값으로 좌/우(진행축) 방향 결정")]
            public SpawnSide spawnSide = SpawnSide.Auto;

            [Header("Entrance")]
            [Tooltip("등장 방식. Ground=기존 지상 즉시 전투, AirDrop=공중 낙하, OffScreen=화면 밖 진입")]
            public SpawnType spawnType = SpawnType.Ground;
            [Tooltip("공통 진입(대열 합류) 지속시간(초). AirDrop/OffScreen에 적용. 0이면 즉시 정상 AI")]
            public float entryDuration = 2f;
            [Tooltip("진입 중 벽 통과 허용(바닥/월드는 통과 안 함). AirDrop/OffScreen에 적용")]
            public bool ignoreWallDuringEntry = true;
            [Tooltip("[AirDrop] 플레이어 기준 공중 생성 높이")]
            public float airDropHeight = 8f;
            [Tooltip("[AirDrop] 초기 급강하 속도(0=자연 중력만으로 낙하)")]
            public float airDropDiveSpeed = 0f;
            [Tooltip("[OffScreen] 카메라 화면 경계에서 추가로 더 밖에 생성하는 여유")]
            public float offScreenMargin = 2f;
        }

        [Header("Spawn Entries")]
        [Tooltip("Spawn() 시 생성할 적 목록")]
        [SerializeField] private List<SpawnEntry> spawnEntries = new List<SpawnEntry>();

        [Header("Preplaced")]
        [Tooltip("씬에 미리 배치된 적(선발대). 생성 없이 전멸 판정에 편입")]
        [SerializeField] private List<Character> preplacedEnemies = new List<Character>();
        [Tooltip("비활성 상태의 preplaced 적을 Spawn() 시 SetActive(true)로 깨울지")]
        [SerializeField] private bool wakePreplacedOnSpawn = false;

        [Header("Options")]
        [Tooltip("true면 Spawn()은 1회만 동작")]
        [SerializeField] private bool spawnOnce = true;
        [Tooltip("스폰 직후 GameManager.Player 방향으로 좌우를 맞출지")]
        [SerializeField] private bool faceTargetOnSpawn = true;

        [Header("Player-Relative Spawn")]
        [Tooltip("자동 스폰 거리 계산 시 적 AttackRange에 더하는 여유")]
        [SerializeField] private float spawnDistanceGap = 1.5f;
        [Tooltip("자동 스폰 거리 하한")]
        [SerializeField] private float minSpawnDistance = 2.5f;
        [Tooltip("자동 스폰 거리 상한")]
        [SerializeField] private float maxSpawnDistance = 12f;
        [Tooltip("투사체 발사기를 가진 원거리 적의 최소 스폰 거리(멀리 배치)")]
        [SerializeField] private float rangedSpawnDistance = 8f;
        [Tooltip("스폰 위치 깊이 방향 랜덤 폭(겹침 방지)")]
        [SerializeField] private float spawnDepthJitter = 1.5f;

        private int _sideCounter;

        [Header("Events")]
        [Tooltip("추적 중인 적이 전원 사망했을 때")]
        public UnityEvent onAllDefeated;

        /// <summary>추적 중 적 전원 사망 시 1회 발화.</summary>
        public event Action OnAllDefeated;

        private readonly List<Character> _tracked = new List<Character>();
        private readonly HashSet<Character> _dead = new HashSet<Character>();
        private readonly Dictionary<Character, Action<float, float>> _handlers =
            new Dictionary<Character, Action<float, float>>();

        private bool _hasSpawned;
        private bool _spawnRequested;
        private bool _allDefeatedFired;
        private int _pendingSpawns;

        /// <summary>스폰이 요청되었고, 대기 중 스폰이 없고, 추적 적이 전원 사망했는지.</summary>
        public bool AllDefeated => _spawnRequested && _pendingSpawns <= 0 && AliveCount <= 0 && _tracked.Count > 0;

        /// <summary>추적 중인 총 개체 수(preplaced + 스폰됨).</summary>
        public int SpawnedCount => _tracked.Count;

        /// <summary>아직 살아 있는 추적 개체 수.</summary>
        public int AliveCount => _tracked.Count - _dead.Count;

        private void Start()
        {
            // preplaced 적은 배틀 시작 전부터 추적을 시작한다 (활성 개체만).
            // 비활성 개체는 Awake 전이라 Stats가 없어 CurrentHP가 0으로 읽힌다(= 즉시 사망 처리).
            // 그래서 여기서는 건너뛰고 Spawn()에서 깨운 직후에 추적한다.
            for (int i = 0; i < preplacedEnemies.Count; i++)
            {
                Character c = preplacedEnemies[i];
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                Track(c);
            }
        }

        /// <summary>웨이브 스폰 시작. 엔트리별 delay 후 생성, preplaced 깨우기 옵션 처리.</summary>
        public void Spawn()
        {
            if (spawnOnce && _hasSpawned) return;
            _hasSpawned = true;
            _spawnRequested = true;

            // 비활성 선발대는 Start()에서 추적을 걸지 못했으므로, 깨운 직후(= Awake 완료 후)에 편입한다.
            for (int i = 0; i < preplacedEnemies.Count; i++)
            {
                Character c = preplacedEnemies[i];
                if (c == null) continue;

                if (!c.gameObject.activeInHierarchy)
                {
                    if (!wakePreplacedOnSpawn) continue;
                    c.gameObject.SetActive(true);
                }

                Track(c);
            }

            for (int i = 0; i < spawnEntries.Count; i++)
            {
                SpawnEntry entry = spawnEntries[i];
                if (entry == null || entry.prefab == null) continue;
                _pendingSpawns++;
                StartCoroutine(SpawnEntryRoutine(entry));
            }

            // 스폰할 것이 없고(선발대 전용) 이미 전멸 상태라면 즉시 판정.
            CheckAllDefeated();
        }

        private IEnumerator SpawnEntryRoutine(SpawnEntry entry)
        {
            if (entry.delay > 0f)
                yield return new WaitForSeconds(entry.delay);

            Vector3 pos = ResolveSpawnPositionFor(entry);
            Quaternion rot = entry.spawnPoint != null ? entry.spawnPoint.rotation : transform.rotation;
            GameObject go = Instantiate(entry.prefab, pos, rot);

            Character character = go.GetComponent<Character>();
            if (character == null)
            {
                Debug.LogWarning($"[EnemySpawner] '{entry.prefab.name}' has no Character component.", this);
            }
            else
            {
                if (faceTargetOnSpawn) FaceTowardsPlayer(character);
                Track(character);
                BeginEntrance(character, entry); // 등장 방식 적용(AirDrop/OffScreen). Ground는 기존 동작 유지.
            }

            _pendingSpawns--;
            CheckAllDefeated();
        }

        private void FaceTowardsPlayer(Character character)
        {
            Character player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (player == null || character.Movement == null) return;
            bool faceRight = player.transform.position.x >= character.transform.position.x;
            character.Movement.SetFacingRight(faceRight);
        }


        // ─────────── 스폰 위치 결정 (플레이어 기준·AttackRange 기반 거리) ───────────

        /// <summary>스폰 위치 결정. 명시 spawnPoint > 플레이어 기준(원거리=멀리·근거리=가까이) > 스포너 위치.</summary>
        private Vector3 ResolveSpawnPosition(SpawnEntry entry)
        {
            if (entry.spawnPoint != null) return entry.spawnPoint.position;

            Character player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (!entry.spawnRelativeToPlayer || player == null)
                return transform.position;

            float dist = entry.spawnDistanceOverride > 0f
                ? entry.spawnDistanceOverride
                : ResolveAutoDistance(entry.prefab);

            Vector3 fwd = player.Movement != null ? player.Movement.SplineForward : Vector3.right;
            Vector3 depth = player.Movement != null ? player.Movement.SplineDepth : Vector3.forward;
            fwd.y = 0f; depth.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.right;
            if (depth.sqrMagnitude < 0.0001f) depth = Vector3.forward;

            float sign = ResolveSideSign(entry.spawnSide);
            float jitter = spawnDepthJitter > 0f ? UnityEngine.Random.Range(-spawnDepthJitter, spawnDepthJitter) : 0f;

            Vector3 pos = player.transform.position + fwd.normalized * (sign * dist) + depth.normalized * jitter;
            pos.y = player.transform.position.y;
            return pos;
        }

        /// <summary>스폰 방향 부호(스플라인 진행축 기준). Auto는 스폰마다 앞/뒤 번갈아.</summary>
        private float ResolveSideSign(SpawnSide side)
        {
            switch (side)
            {
                case SpawnSide.Front: return 1f;
                case SpawnSide.Back: return -1f;
                case SpawnSide.Random: return UnityEngine.Random.value < 0.5f ? 1f : -1f;
                default: return (_sideCounter++ % 2 == 0) ? 1f : -1f;
            }
        }

        /// <summary>프리팹의 EnemyCharacter.AttackRange 조회(없으면 0). 원거리 적일수록 커서 멀리 스폰된다.</summary>
        /// <summary>플레이어 기준 자동 스폰 거리. 근거리=AttackRange, 원거리(투사체 발사기 보유)=멀리.</summary>
        private float ResolveAutoDistance(GameObject prefab)
        {
            float attackRange = 0f;
            bool ranged = false;
            if (prefab != null)
            {
                var enemy = prefab.GetComponent<EnemyCharacter>();
                if (enemy != null) attackRange = enemy.AttackRange;
                ranged = prefab.GetComponentInChildren<ProjectileLauncher>(true) != null;
            }

            float dist = attackRange + spawnDistanceGap;
            if (ranged) dist = Mathf.Max(dist, rangedSpawnDistance);
            return Mathf.Clamp(dist, minSpawnDistance, maxSpawnDistance);
        }


        // ─────────── 등장 방식별 스폰 위치 / 진입 연동 ───────────

        /// <summary>등장 방식에 따른 스폰 위치. Ground=기존 로직, AirDrop=위 공중, OffScreen=화면 밖.</summary>
        private Vector3 ResolveSpawnPositionFor(SpawnEntry entry)
        {
            switch (entry.spawnType)
            {
                case SpawnType.AirDrop: return ResolveAirDropPosition(entry);
                case SpawnType.OffScreen: return ResolveOffScreenPosition(entry);
                default: return ResolveSpawnPosition(entry); // Ground
            }
        }

        /// <summary>AirDrop: 기존 플레이어 기준 지상 위치를 쓰되 Y만 높인다(자연 낙하로 착지).</summary>
        private Vector3 ResolveAirDropPosition(SpawnEntry entry)
        {
            Vector3 ground = ResolveSpawnPosition(entry);
            ground.y += Mathf.Max(0.5f, entry.airDropHeight);
            return ground;
        }

        /// <summary>OffScreen: 스플라인 방향으로 실제 카메라 뷰포트 밖이 될 때까지 밀어낸 뒤 margin을 더한다.</summary>
        private Vector3 ResolveOffScreenPosition(SpawnEntry entry)
        {
            Character player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            Vector3 basePos = player != null ? player.transform.position : transform.position;

            Vector3 fwd = player != null && player.Movement != null ? player.Movement.SplineForward : Vector3.right;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.right;
            fwd.Normalize();

            float sign = ResolveSideSign(entry.spawnSide);
            Vector3 dir = fwd * sign;

            Camera cam = Camera.main;
            if (cam == null)
            {
                // 폴백: 카메라가 없으면 큰 거리로 밀어냄
                Vector3 f = basePos + dir * Mathf.Max(maxSpawnDistance, 12f);
                f.y = basePos.y;
                return f;
            }

            // 화면 안이면 점점 밀어내며 뷰포트 밖(x<0 또는 x>1, 또는 카메라 뒤 z<=0)이 되는 지점을 찾는다.
            const float step = 1f;
            const float maxPush = 60f;
            float pushed = step;
            Vector3 pos = basePos + dir * pushed;
            while (pushed < maxPush)
            {
                Vector3 vp = cam.WorldToViewportPoint(pos);
                bool offscreen = vp.z <= 0f || vp.x < 0f || vp.x > 1f;
                if (offscreen) break;
                pushed += step;
                pos = basePos + dir * pushed;
            }

            pos = basePos + dir * (pushed + Mathf.Max(0f, entry.offScreenMargin));
            pos.y = basePos.y;
            return pos;
        }

        /// <summary>등장 방식 적용: AirDrop/OffScreen만 진입 연출을 붙인다. Ground는 기존처럼 즉시 정상 AI.</summary>
        private void BeginEntrance(Character character, SpawnEntry entry)
        {
            if (entry.spawnType != SpawnType.AirDrop && entry.spawnType != SpawnType.OffScreen)
                return; // Ground → 기존 동작 유지

            EnemyCharacter enemy = character as EnemyCharacter;
            if (enemy == null) enemy = character.GetComponent<EnemyCharacter>();
            if (enemy == null) return; // 비-Enemy는 등장 연출 없음

            enemy.ConfigureSpawnEntry(entry.entryDuration, entry.ignoreWallDuringEntry);
            EnemyEntranceController ctrl = EnemyEntranceController.Attach(character.gameObject);
            if (entry.spawnType == SpawnType.AirDrop) ctrl.BeginAirDrop(enemy, entry.airDropDiveSpeed);
            else ctrl.BeginOffScreen(enemy);
        }

        private void Track(Character character)
        {
            // _handlers만 보면 "이미 사망으로 편입된 개체"를 놓쳐 _tracked에 중복으로 쌓인다.
            if (character == null || _tracked.Contains(character)) return;
            _tracked.Add(character);

            // 스폰/선발대/깨우기 적 전원이 플레이어와 동일한 스플라인 좌표계를 공유하도록 주입.
            // GameManager의 씬 로드 시점 일괄 주입을 놓친 런타임 개체를 여기서 보정한다.
            if (GameManager.Instance != null)
                GameManager.Instance.InjectSpline(character);

            if (character.IsDead)
            {
                _dead.Add(character);
                return;
            }

            Action<float, float> handler = (cur, max) => OnTrackedHealthChanged(character, cur);
            _handlers[character] = handler;
            character.OnHealthChanged += handler;
        }

        private void OnTrackedHealthChanged(Character character, float current)
        {
            if (current > 0f) return;
            if (!_dead.Add(character)) return; // 중복 사망 방지

            if (_handlers.TryGetValue(character, out Action<float, float> handler))
            {
                character.OnHealthChanged -= handler;
                _handlers.Remove(character);
            }

            CheckAllDefeated();
        }

        private void CheckAllDefeated()
        {
            if (_allDefeatedFired || !AllDefeated) return;
            _allDefeatedFired = true;
            OnAllDefeated?.Invoke();
            onAllDefeated?.Invoke();
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<Character, Action<float, float>> kv in _handlers)
            {
                if (kv.Key != null) kv.Key.OnHealthChanged -= kv.Value;
            }
            _handlers.Clear();
        }
    }
}
