using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 게임 전역 매니저(싱글톤). Game.unity(상주 씬)에 1개만 존재한다.
    ///
    /// 역할(현재): 스플라인 기준 좌표계 배선.
    ///   - 씬이 로드/언로드될 때마다 현재 로드된 Stage Spline 씬을 탐색해
    ///     그 SplineMovementReference를 플레이어/카메라에 주입한다.
    ///   - Stage Spline 씬이 없으면 Game 씬에 상주하는 폴백 스플라인을 사용한다.
    ///
    /// 기존에는 인스펙터에서 플레이어/카메라에 스플라인을 직접 물려주고
    /// MultiSceneLoader.ConnectStageSpline이 그룹 로드 시 갈아끼웠으나,
    /// 그 책임을 이 매니저로 이관했다. MultiSceneLoader는 씬 로드만 담당한다.
    /// </summary>
    [AddComponentMenu("Yeolha/Core/Game Manager")]
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        private static GameManager _instance;

        /// <summary>도메인 리로드로 static이 유실돼도 씬에서 다시 찾는다.</summary>
        public static GameManager Instance
        {
            get
            {
                if (_instance == null)
                    _instance = FindFirstObjectByType<GameManager>();
                return _instance;
            }
        }

        [Header("Persistent References (Game.unity)")]
        [Tooltip("상주 플레이어. 비우면 자동 탐색.")]
        [SerializeField] private Character player;

        [Tooltip("상주 카메라 리그. 비우면 자동 탐색.")]
        [SerializeField] private CameraRig cameraRig;

        [Tooltip("Stage Spline 씬을 찾지 못했을 때 사용할 폴백 스플라인. 비우면 Game 씬 안에서 자동 탐색.")]
        [SerializeField] private SplineMovementReference fallbackSpline;

        [Header("Spline Scene Matching")]
        [Tooltip("스플라인 씬 이름 접두사 (이 접두사로 시작하는 씬만 Stage Spline 씬으로 간주)")]
        [SerializeField] private string splineScenePrefix = "Stage1_Sequence";

        [Tooltip("스플라인 씬 이름 접미사")]
        [SerializeField] private string splineSceneSuffix = "_Spline";

        /// <summary>현재 플레이어/카메라에 주입된 스플라인.</summary>
        public SplineMovementReference ActiveSpline { get; private set; }

        /// <summary>상주 플레이어 Character. EnemyController 등의 Target 획득용 공개 접근자.</summary>
        public Character Player => player;


        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[GameManager] 중복 인스턴스 발견. 파괴합니다.", this);
                Destroy(gameObject);
                return;
            }
            _instance = this;

            if (player == null) player = FindFirstObjectByType<Character>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<CameraRig>();
            if (fallbackSpline == null) fallbackSpline = FindFallbackSpline();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private void Start()
        {
            // 최초 진입: 이미 로드된 씬 기준으로 1회 평가.
            RefreshSpline();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ─────────────────────────── Scene Events ───────────────────────────

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 방금 로드된 씬이 Stage Spline 씬이면 "최신 우선"으로 즉시 교체.
            // (그룹 전환 중 이전/신규 Spline 씬이 잠시 공존해도 새 씬을 택한다)
            if (IsSplineScene(scene.name))
            {
                var s = FindSplineInScene(scene);
                if (s != null)
                {
                    AssignSpline(s);
                    return;
                }
            }


            // 아직 아무 스플라인도 없으면(초기 상태) 폴백까지 포함해 평가.
            if (!ActiveSpline)
                RefreshSpline();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            // 현재 사용 중인 스플라인이 사라졌으면 재평가(남은 Stage Spline → 없으면 폴백).
            if (!ActiveSpline || ActiveSpline.gameObject.scene == scene)
                RefreshSpline();
        }

        // ─────────────────────────── Core ───────────────────────────

        /// <summary>
        /// 로드된 씬들에서 Stage Spline 씬을 찾아 스플라인을 주입한다.
        /// 없으면 폴백(Game 씬) 스플라인을 사용한다. 외부에서 강제 갱신도 가능.
        /// </summary>
        public void RefreshSpline()
        {
            SplineMovementReference spline = FindStageSpline();
            if (spline == null) spline = fallbackSpline;
            AssignSpline(spline);
        }

        private SplineMovementReference FindStageSpline()
        {
            int count = SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                if (!IsSplineScene(scene.name)) continue;

                var found = FindSplineInScene(scene);
                if (found != null) return found;
            }
            return null;
        }

        private bool IsSplineScene(string sceneName)
        {
            return !string.IsNullOrEmpty(sceneName)
                && sceneName.StartsWith(splineScenePrefix)
                && sceneName.EndsWith(splineSceneSuffix);
        }

        private SplineMovementReference FindSplineInScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var c = roots[i].GetComponentInChildren<SplineMovementReference>(true);
                if (c != null) return c;
            }
            return null;
        }

        private SplineMovementReference FindFallbackSpline()
        {
            // 자신이 속한 상주(Game) 씬 안의 SplineMovementReference를 폴백으로 사용.
            var myScene = gameObject.scene;
            if (myScene.IsValid() && myScene.isLoaded)
            {
                var s = FindSplineInScene(myScene);
                if (s != null) return s;
            }
            // 그래도 없으면 전역에서 아무거나(안전망).
            return FindFirstObjectByType<SplineMovementReference>();
        }

        private void AssignSpline(SplineMovementReference spline)
        {
            if (spline == null)
            {
                Debug.LogWarning("[GameManager] 주입할 SplineMovementReference가 없습니다. (폴백도 미지정)", this);
                return;
            }
            if (ActiveSpline == spline) return; // 변경 없음 → 스킵

            ActiveSpline = spline;

            var allCharacters = FindObjectsOfType<Character>(includeInactive: false);
            foreach (var character in allCharacters)
                InjectSpline(character);

            if (cameraRig != null) cameraRig.SetSplineReference(spline);

            Debug.Log($"[GameManager] 스플라인 주입: '{spline.gameObject.scene.name}/{spline.name}'", spline);
        }

        /// <summary>
        /// 단일 Character에 현재 ActiveSpline을 주입한다.
        /// 런타임에 스폰되거나 뒤늦게 활성화된 적(EnemySpawner)이 씬 로드 시점의 일괄 주입을
        /// 놓쳐도 스플라인 좌표계를 공유하도록 하는 공개 진입점.
        /// ActiveSpline이 아직 없으면 1회 평가(RefreshSpline)를 시도한다.
        /// </summary>
        public void InjectSpline(Character character)
        {
            if (character == null || character.Movement == null) return;
            if (ActiveSpline == null) RefreshSpline();
            if (ActiveSpline == null) return;
            character.Movement.SetSplineReference(ActiveSpline);
        }

    }
}
