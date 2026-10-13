using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Game.unity(Persistent Runtime Scene)에 상주하며 SceneGroup 단위 씬 전환을 담당하는 로더.
    ///
    /// 구버전(Aiara.SceneSystem.MultiSceneLoader)에서 승계한 개념:
    ///   - Game Scene 유지 + Map/Light/Spline/Sub Additive Load
    ///   - SceneGroup 요청 → 전 씬 준비 → EntryPoint 배치 → 이전 그룹 Unload
    ///   - 동일 그룹 내 EntryPoint 텔레포트
    ///   - ReloadCurrentGroup (향후 Death/Respawn 용)
    ///
    /// 승계하지 않은 것: Addressables, TDE LevelManager/GameManager 연동, MMFader 페이드,
    /// TDE Freeze/CheckPoint. 페이드/세이브 연동 지점은 코멘트로만 남긴다.
    ///
    /// 외부 시스템은 SceneManager를 직접 호출하지 말고 이 API만 사용한다:
    ///   LoadGroup / UnloadCurrentGroup / ReloadCurrentGroup / TeleportToEntryPoint
    /// </summary>
    [AddComponentMenu("Yeolha/Scene System/Multi Scene Loader")]
    public class MultiSceneLoader : MonoBehaviour
    {
        private static MultiSceneLoader _instance;

        /// <summary>도메인 리로드 등으로 static이 유실돼도 씬에서 다시 찾는다.</summary>
        public static MultiSceneLoader Instance
        {
            get
            {
                if (_instance == null)
                    _instance = FindFirstObjectByType<MultiSceneLoader>();
                return _instance;
            }
        }

        [Header("Initial Group")]
        [Tooltip("Game 씬 시작 시 자동 로드할 첫 SceneGroup. 비워두면 자동 로드하지 않음.")]
        [SerializeField] private SceneGroup initialGroup;

        [Header("Persistent References (Game.unity)")]
        [Tooltip("상주 플레이어")]
        [SerializeField] private Character player;

        [Tooltip("상주 카메라 리그")]
        [SerializeField] private CameraRig cameraRig;

        [Header("Transition Fade")]
        [Tooltip("그룹 전환 시 화면 페이드 사용 여부")]
        [SerializeField] private bool useFade = true;

        [Header("Debug (Play Mode test)")]
        [Tooltip("Play 중 체크하면 현재 그룹을 Reload한다. (자동으로 다시 꺼짐)")]
        [SerializeField] private bool debugReloadRequest;

        [Tooltip("Play 중 지정하면 해당 그룹으로 전환한다. (자동으로 비워짐)")]
        [SerializeField] private SceneGroup debugLoadRequest;

        public SceneGroup CurrentGroup { get; private set; }
        public bool IsLoading { get; private set; }

        [Tooltip("0보다 크면 Play 시작 후 해당 초가 지났을 때 1회 자동 Reload (테스트 전용)")]
        [SerializeField] private float debugAutoReloadAfter;

        private float _autoReloadTimer;
        private bool _autoReloadDone;

        private void Update()
        {
            if (debugAutoReloadAfter > 0f && !_autoReloadDone && !IsLoading && CurrentGroup != null)
            {
                _autoReloadTimer += Time.deltaTime;
                if (_autoReloadTimer >= debugAutoReloadAfter)
                {
                    _autoReloadDone = true;
                    Debug.Log("[MultiSceneLoader] (Debug) 자동 Reload 실행");
                    ReloadCurrentGroup();
                }
            }
            if (debugReloadRequest)
            {
                debugReloadRequest = false;
                ReloadCurrentGroup();
            }
            if (debugLoadRequest != null)
            {
                var g = debugLoadRequest;
                debugLoadRequest = null;
                LoadGroup(g);
            }
        }

        private readonly List<string> _currentSceneNames = new List<string>();

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[MultiSceneLoader] 중복 인스턴스 발견. 파괴합니다.", this);
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

private void Start()
        {
            if (initialGroup == null)
                return;

            // 에디터에서 스테이지 씬(Map/Light/Spline/Sub 등)을 수동으로 열어둔 상태면
            // 초기 그룹 자동 로드를 건너뛴다 — 수동 씬 구성으로도 그대로 플레이 가능.
            // (스플라인 주입은 GameManager가 이미 로드된 씬 기준으로 처리한다)
            if (HasExternallyLoadedScene())
            {
                Debug.Log("[MultiSceneLoader] 이미 로드된 씬 감지 — 초기 그룹 자동 로드 생략 (수동 씬 구성 모드)", this);
                return;
            }

            // 초기 로드는 검은 화면에서 시작해 로드 완료 후 페이드 인한다.
            if (useFade)
                ScreenFader.Instance.SetOpaque();

            LoadGroup(initialGroup);
        }

        /// <summary>상주(Game) 씬 외에 이미 로드된 씬이 있는가 — 에디터 수동 멀티씬 구성 감지.</summary>
        private bool HasExternallyLoadedScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && s != gameObject.scene)
                    return true;
            }
            return false;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ─────────────────────────── Public API ───────────────────────────

        /// <summary>SceneGroup을 로드한다. entryKey를 생략하면 그룹의 DefaultEntryKey를 사용.</summary>
        public void LoadGroup(SceneGroup group, string entryKey = null)
        {
            if (IsLoading)
            {
                Debug.LogWarning("[MultiSceneLoader] 이미 로딩 중입니다. 요청 무시.", this);
                return;
            }
            if (group == null)
            {
                Debug.LogError("[MultiSceneLoader] group이 null입니다.", this);
                return;
            }
            StartCoroutine(LoadGroupCoroutine(group, entryKey));
        }

        /// <summary>현재 그룹의 씬들만 언로드한다. Game 씬과 상주 오브젝트는 유지된다.</summary>
        public void UnloadCurrentGroup()
        {
            if (IsLoading)
            {
                Debug.LogWarning("[MultiSceneLoader] 로딩 중에는 언로드할 수 없습니다.", this);
                return;
            }
            StartCoroutine(UnloadScenesCoroutine(new List<string>(_currentSceneNames), clearCurrent: true));
        }

        /// <summary>현재 그룹을 다시 로드한다. (향후 Death/Respawn/Checkpoint에서 사용 예정)</summary>
        public void ReloadCurrentGroup(string entryKey = null)
        {
            if (CurrentGroup == null)
            {
                Debug.LogWarning("[MultiSceneLoader] CurrentGroup이 null이라 재로드 불가.", this);
                return;
            }
            LoadGroup(CurrentGroup, entryKey);
        }

        /// <summary>동일 그룹 내 EntryPoint 텔레포트. 씬 재로드 없음.</summary>
        public bool TeleportToEntryPoint(string entryKey)
        {
            var entry = FindEntryPoint(entryKey);
            if (entry == null)
            {
                string id = CurrentGroup != null ? CurrentGroup.GroupId : "(null)";
                Debug.LogWarning($"[MultiSceneLoader] 그룹 '{id}' 에서 EntryKey '{entryKey}' 를 찾지 못했습니다.", this);
                return false;
            }
            TeleportPlayerTo(entry);
            return true;
        }

        // ─────────────────────────── Loading Flow ───────────────────────────

private IEnumerator LoadGroupCoroutine(SceneGroup group, string entryKey)
        {
            IsLoading = true;

            // 전환 시작: 화면을 검게 가린다. (이미 검은 상태면 즉시 통과)
            // (향후 확장 지점) 여기서 Save 트리거.
            if (useFade && !ScreenFader.Instance.IsOpaque)
                yield return ScreenFader.Instance.FadeToBlack();

            // 새 그룹이 사용할 씬 이름 집합 (이미 로드돼 있으면 재사용 → 언로드/재로드 깜빡임 방지)
            var newGroupScenes = new HashSet<string>();
            foreach (var n in group.EnumerateSceneNames())
                newGroupScenes.Add(n);

            bool isReload = group == CurrentGroup;

            // 언로드 대상: 현재 로드된 씬 중 상주(Game) 씬과 새 그룹이 재사용할 씬을 제외한 전부.
            //   → 로더가 추적하지 않은 씬(에디터 수동 로드 등)도 확실히 정리된다.
            //   상주 Game 씬 = 이 로더가 붙은 gameObject.scene (항상 유지).
            //   Reload인 경우엔 같은 그룹 씬도 새로 로드하기 위해 언로드 대상에 포함한다.
            var scenesToUnload = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                if (s == gameObject.scene) continue;                        // 상주 Game 씬 유지
                if (!isReload && newGroupScenes.Contains(s.name)) continue;  // 재사용할 씬 유지
                scenesToUnload.Add(s.name);
            }

            // Reload면 같은 그룹 씬을 먼저 언로드해 새로 로드되게 한다.
            if (isReload)
            {
                yield return UnloadScenesCoroutine(scenesToUnload, clearCurrent: false);
                scenesToUnload.Clear();
            }

            // 이미 로드된 씬 집합 (중복 로드 방지) — 언로드 예정 씬은 제외.
            var alreadyLoaded = new HashSet<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && !scenesToUnload.Contains(s.name))
                    alreadyLoaded.Add(s.name);
            }

            // 1) 새 그룹 씬 Additive 로드 (이미 로드된 건 skip)
            var loading = new List<AsyncOperation>();
            var newScenes = new List<string>();
            foreach (var sceneName in group.EnumerateSceneNames())
            {
                newScenes.Add(sceneName);
                if (alreadyLoaded.Contains(sceneName))
                    continue; // 이미 로드됨 (재사용)

                var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                if (op == null)
                {
                    Debug.LogError($"[MultiSceneLoader] 씬 '{sceneName}' 로드 실패. Build Settings 등록 여부를 확인하세요.", this);
                    continue;
                }
                loading.Add(op);
            }

            foreach (var op in loading)
                while (!op.isDone) yield return null;

            // 2) Light Scene을 Active Scene으로 → 해당 씬의 RenderSettings(Skybox/Fog/Ambient) 적용
            if (!string.IsNullOrEmpty(group.LightSceneName))
            {
                var lightScene = SceneManager.GetSceneByName(group.LightSceneName);
                if (lightScene.IsValid() && lightScene.isLoaded)
                    SceneManager.SetActiveScene(lightScene);
            }

            CurrentGroup = group;
            _currentSceneNames.Clear();
            _currentSceneNames.AddRange(newScenes);

            // 3) 스테이지 스플라인 연결은 GameManager로 이관됨.
            //    Spline 씬이 Additive 로드되면 GameManager가 SceneManager.sceneLoaded 이벤트로
            //    감지해 플레이어/카메라에 자동 주입한다. (여기서는 아무것도 하지 않는다)

            // 4) 플레이어 EntryPoint 배치
            string key = string.IsNullOrEmpty(entryKey) ? group.DefaultEntryKey : entryKey;
            var entry = FindEntryPoint(key);
            if (entry != null)
            {
                TeleportPlayerTo(entry);
            }
            else
            {
                Debug.LogWarning($"[MultiSceneLoader] 그룹 '{group.GroupId}' 에서 EntryKey '{key}' 미발견. 플레이어를 이동하지 않음.", this);
            }

            // 5) 이전 씬들 Unload (플레이어 배치 후 → 화면 공백 최소화)
            yield return UnloadScenesCoroutine(scenesToUnload, clearCurrent: false);

            Debug.Log($"[MultiSceneLoader] 그룹 '{group.GroupId}' 로드 완료. (씬 {_currentSceneNames.Count}개)");

            // 로드 완료: 검은 화면에서 게임 화면으로 페이드 인.
            if (useFade)
                yield return ScreenFader.Instance.FadeFromBlack();

            IsLoading = false;
        }

        private IEnumerator UnloadScenesCoroutine(List<string> sceneNames, bool clearCurrent)
        {
            foreach (var sceneName in sceneNames)
            {
                var scene = SceneManager.GetSceneByName(sceneName);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                var op = SceneManager.UnloadSceneAsync(scene);
                if (op != null)
                    while (!op.isDone) yield return null;
            }

            if (clearCurrent)
            {
                _currentSceneNames.Clear();
                CurrentGroup = null;
            }
        }

        // ─────────────────────────── Player / Spline ───────────────────────────

        // 스테이지 스플라인 주입은 GameManager가 담당한다. (ConnectStageSpline 제거됨)

        private SceneEntryPoint FindEntryPoint(string entryKey)
        {
            if (string.IsNullOrEmpty(entryKey)) return null;
            foreach (var ep in EnumerateInCurrentScenes<SceneEntryPoint>())
            {
                if (ep.EntryKey == entryKey) return ep;
            }
            return null;
        }

        private void TeleportPlayerTo(SceneEntryPoint entry)
        {
            if (player != null)
            {
                if (player != null && player.Movement != null) player.Movement.Teleport(entry.transform.position, entry.FaceRight);
            }
            if (cameraRig != null)
            {
                cameraRig.SnapToTarget();
            }
        }

        // 로드된 현재 그룹 씬들의 루트만 스캔 (씬 이름/FindObjectOfType 전역 탐색 회피)
        private IEnumerable<T> EnumerateInCurrentScenes<T>() where T : Component
        {
            foreach (var sceneName in _currentSceneNames)
            {
                var scene = SceneManager.GetSceneByName(sceneName);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                var roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    var found = roots[i].GetComponentsInChildren<T>(true);
                    for (int j = 0; j < found.Length; j++)
                        yield return found[j];
                }
            }
        }
    }
}
