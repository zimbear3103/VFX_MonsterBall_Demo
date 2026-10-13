using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 포탈(Portal)과 씬 진입점(SceneEntryPoint) 저작 헬퍼. 툴 창과 커스텀 인스펙터가 공유하는
    /// 배치·참조 세팅·그룹 스캔 로직을 모아둔 에디터 전용 유틸리티.
    ///
    /// 이 도구가 지키려는 계약은 MultiSceneLoader가 정한 것 그대로다.
    ///   - 로더는 (SceneGroup + EntryKey) 조합으로 플레이어를 배치한다.
    ///   - EntryPoint 탐색 범위는 "그 그룹이 로드한 씬들"뿐이다(MultiSceneLoader.EnumerateInCurrentScenes).
    ///     따라서 Game.unity나 그룹 밖 씬에 놓인 진입점은 영원히 발견되지 않는다.
    ///   - 씬은 이름 문자열로 로드하므로 Build Settings에 등록돼 있어야 한다.
    /// 그래서 이 파일은 "열려 있지 않은 씬"까지 훑어 키 목록을 만든다(씬 파일 텍스트 스캔).
    /// </summary>
    public static class SceneFlowAuthoring
    {
        // ─────────── 직렬화 프로퍼티 경로 (런타임 필드명과 1:1) ───────────

        public const string P_TargetGroup = "targetGroup";
        public const string P_PortalEntryKey = "entryKey";
        public const string P_PortalActive = "active";

        public const string P_EntryKey = "entryKey";
        public const string P_FaceRight = "faceRight";

        /// <summary>SceneGroup의 기본값이자 배치된 LoadPoint들이 쓰던 키.</summary>
        public const string DefaultEntryKey = "Start";

        /// <summary>Portal.prefab. 못 찾으면 트리거 박스를 직접 조립한다.</summary>
        public const string PortalPrefabGuid = "2bd4559e015f827488d70237dcd364ca";

        /// <summary>직접 조립할 때 쓰는 포탈 트리거 크기. 벨트스크롤 진행 방향을 가로막는 문 형태.</summary>
        public static readonly Vector3 DefaultPortalSize = new Vector3(1.5f, 3f, 4f);

        // ─────────── 포탈 배치 ───────────

        /// <summary>포탈을 지정한 씬에 배치한다. 프리팹이 있으면 프리팹 인스턴스로 만든다.</summary>
        public static Portal CreatePortal(Scene scene, Vector3 worldPosition, SceneGroup group, string entryKey)
        {
            string path = AssetDatabase.GUIDToAssetPath(PortalPrefabGuid);
            GameObject prefab = string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(path);

            GameObject root;
            if (prefab != null)
            {
                root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(root, "Create Portal");
            }
            else
            {
                root = new GameObject("Portal");
                Undo.RegisterCreatedObjectUndo(root, "Create Portal");

                // Portal은 [RequireComponent(typeof(Collider))]다. 추상 타입은 자동 추가가 되지 않으니
                // 콜라이더를 먼저 붙이고 그 다음에 포탈을 얹는다.
                BoxCollider box = Undo.AddComponent<BoxCollider>(root);
                box.isTrigger = true;
                box.size = DefaultPortalSize;
                box.center = new Vector3(0f, DefaultPortalSize.y * 0.5f, 0f);

                Undo.AddComponent<Portal>(root);
            }

            root.transform.position = worldPosition;
            MoveToScene(root, scene);

            Portal portal = root.GetComponent<Portal>();
            if (portal != null)
            {
                EnsureTrigger(portal);
                SetPortalTarget(portal, group, entryKey);
                root.name = PortalName(group, entryKey);
            }

            Selection.activeGameObject = root;
            return portal;
        }

        /// <summary>대상이 한눈에 보이는 이름. 이름과 실제 참조가 어긋나 생기는 착각을 줄인다.</summary>
        public static string PortalName(SceneGroup group, string entryKey)
        {
            if (group == null) return "Portal_(대상없음)";
            return string.IsNullOrEmpty(entryKey)
                ? "Portal_To_" + group.name
                : "Portal_To_" + group.name + "_" + entryKey;
        }

        public static void SetPortalTarget(Portal portal, SceneGroup group, string entryKey)
        {
            if (portal == null) return;

            Undo.RecordObject(portal, "Set Portal Target");
            SerializedObject so = new SerializedObject(portal);
            so.FindProperty(P_TargetGroup).objectReferenceValue = group;
            so.FindProperty(P_PortalEntryKey).stringValue = entryKey ?? string.Empty;
            so.ApplyModifiedProperties();
        }

        public static void SetPortalActive(Portal portal, bool value)
        {
            if (portal == null) return;

            Undo.RecordObject(portal, "Set Portal Active");
            SerializedObject so = new SerializedObject(portal);
            so.FindProperty(P_PortalActive).boolValue = value;
            so.ApplyModifiedProperties();
        }

        /// <summary>포탈 콜라이더를 트리거로 만든다. 트리거가 아니면 진입 자체가 감지되지 않는다.</summary>
        public static Collider EnsureTrigger(Portal portal)
        {
            if (portal == null) return null;

            Collider col = portal.GetComponent<Collider>();
            if (col == null)
            {
                BoxCollider box = Undo.AddComponent<BoxCollider>(portal.gameObject);
                box.isTrigger = true;
                box.size = DefaultPortalSize;
                box.center = new Vector3(0f, DefaultPortalSize.y * 0.5f, 0f);
                return box;
            }

            if (!col.isTrigger)
            {
                Undo.RecordObject(col, "Set Is Trigger");
                col.isTrigger = true;
                EditorUtility.SetDirty(col);
            }
            return col;
        }

        /// <summary>포탈이 실제로 로드할 키. 비어 있으면 그룹의 기본 키를 쓴다(로더와 같은 규칙).</summary>
        public static string ResolvedEntryKey(Portal portal)
        {
            if (portal == null) return null;
            if (!string.IsNullOrEmpty(portal.EntryKey)) return portal.EntryKey;
            return portal.TargetGroup != null ? portal.TargetGroup.DefaultEntryKey : null;
        }

        // ─────────── 진입점 배치 ───────────

        /// <summary>진입점을 지정한 씬에 배치한다.</summary>
        public static SceneEntryPoint CreateEntryPoint(Scene scene, Vector3 worldPosition,
                                                       string entryKey, bool faceRight)
        {
            string key = string.IsNullOrEmpty(entryKey) ? DefaultEntryKey : entryKey;

            GameObject go = new GameObject("EntryPoint_" + key);
            Undo.RegisterCreatedObjectUndo(go, "Create Scene Entry Point");
            go.transform.position = worldPosition;
            MoveToScene(go, scene);

            SceneEntryPoint entry = Undo.AddComponent<SceneEntryPoint>(go);
            SetEntry(entry, key, faceRight);

            Selection.activeGameObject = go;
            return entry;
        }

        public static void SetEntry(SceneEntryPoint entry, string key, bool faceRight)
        {
            if (entry == null) return;

            Undo.RecordObject(entry, "Set Entry Point");
            SerializedObject so = new SerializedObject(entry);
            so.FindProperty(P_EntryKey).stringValue = key ?? string.Empty;
            so.FindProperty(P_FaceRight).boolValue = faceRight;
            so.ApplyModifiedProperties();
        }

        public static void SetEntryKey(SceneEntryPoint entry, string key)
        {
            if (entry == null) return;
            SetEntry(entry, key, entry.FaceRight);
        }

        /// <summary>진입점을 바로 아래 바닥에 붙인다. 공중에 뜬 진입점은 스폰 직후 낙하로 이어진다.</summary>
        public static bool SnapToGround(Transform target, float probe = 30f)
        {
            if (target == null) return false;

            Vector3 origin = target.position + Vector3.up * 0.5f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probe + 0.5f,
                                 ~0, QueryTriggerInteraction.Ignore))
                return false;

            Undo.RecordObject(target, "Snap To Ground");
            target.position = hit.point;
            return true;
        }

        public static bool HasFloorUnder(Transform target, float probe = 5f)
        {
            if (target == null) return false;
            return Physics.Raycast(target.position + Vector3.up * 0.1f, Vector3.down, probe,
                                   ~0, QueryTriggerInteraction.Ignore);
        }

        // ─────────── LoadPoint 승계 ───────────

        /// <summary>
        /// 구버전 마커 Aiara.LoadPoint를 SceneEntryPoint로 승계한다.
        /// pointName → EntryKey, characterAngle → FaceRight(90°=오른쪽 규약)로 옮기고 위치는 그대로 둔다.
        /// </summary>
        public static SceneEntryPoint ConvertLoadPoint(Aiara.LoadPoint loadPoint, bool removeOld)
        {
            if (loadPoint == null) return null;

            GameObject go = loadPoint.gameObject;
            SceneEntryPoint entry = go.GetComponent<SceneEntryPoint>();
            if (entry == null) entry = Undo.AddComponent<SceneEntryPoint>(go);

            string key = string.IsNullOrWhiteSpace(loadPoint.pointName) ? DefaultEntryKey : loadPoint.pointName;
            SetEntry(entry, key, FaceRightFromAngle(loadPoint.characterAngle));

            if (removeOld) Undo.DestroyObjectImmediate(loadPoint);

            EditorSceneManager.MarkSceneDirty(go.scene);
            return entry;
        }

        /// <summary>구버전 각도 규약: 90°가 오른쪽(+X), 270°가 왼쪽.</summary>
        public static bool FaceRightFromAngle(float characterAngle)
        {
            return Mathf.Repeat(characterAngle, 360f) < 180f;
        }

        public static Aiara.LoadPoint[] LoadPointsInScene(Scene scene)
        {
            List<Aiara.LoadPoint> found = new List<Aiara.LoadPoint>();
            if (!scene.IsValid() || !scene.isLoaded) return found.ToArray();

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                found.AddRange(roots[i].GetComponentsInChildren<Aiara.LoadPoint>(true));

            return found.ToArray();
        }

        // ─────────── 씬 / 그룹 조회 ───────────

        private static void MoveToScene(GameObject go, Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            if (go.scene == scene) return;

            Undo.MoveGameObjectToScene(go, scene, "Move To Scene");
        }

        /// <summary>현재 열려 있는 씬들.</summary>
        public static List<Scene> OpenScenes()
        {
            List<Scene> scenes = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.IsValid() && s.isLoaded) scenes.Add(s);
            }
            return scenes;
        }

        // 에셋 조회는 매 리페인트마다 부르기엔 비싸다. 프로젝트가 바뀔 때까지 들고 있는다.
        private static SceneGroup[] _groupCache;
        private static readonly Dictionary<string, string> ScenePathCache = new Dictionary<string, string>();

        /// <summary>프로젝트의 모든 SceneGroup 에셋(이름순).</summary>
        public static SceneGroup[] AllGroups()
        {
            if (_groupCache != null) return _groupCache;

            string[] guids = AssetDatabase.FindAssets("t:SceneGroup");
            List<SceneGroup> groups = new List<SceneGroup>(guids.Length);

            for (int i = 0; i < guids.Length; i++)
            {
                SceneGroup g = AssetDatabase.LoadAssetAtPath<SceneGroup>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (g != null) groups.Add(g);
            }

            groups.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            _groupCache = groups.ToArray();
            return _groupCache;
        }

        /// <summary>이 씬 이름을 포함하는 그룹들. 포탈이 자기 그룹을 가리키는지 판정하는 데 쓴다.</summary>
        public static List<SceneGroup> GroupsContainingScene(string sceneName)
        {
            List<SceneGroup> result = new List<SceneGroup>();
            if (string.IsNullOrEmpty(sceneName)) return result;

            SceneGroup[] groups = AllGroups();
            for (int i = 0; i < groups.Length; i++)
            {
                foreach (string name in groups[i].EnumerateSceneNames())
                {
                    if (name != sceneName) continue;
                    result.Add(groups[i]);
                    break;
                }
            }
            return result;
        }

        /// <summary>씬 이름 → 에셋 경로. 같은 이름이 여러 개면 첫 번째.</summary>
        public static string ScenePathOf(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;
            if (ScenePathCache.TryGetValue(sceneName, out string cached)) return cached;

            string result = null;
            string[] guids = AssetDatabase.FindAssets("t:Scene " + sceneName);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (Path.GetFileNameWithoutExtension(path) != sceneName) continue;
                result = path;
                break;
            }

            ScenePathCache[sceneName] = result;
            return result;
        }

        /// <summary>씬 이름 기반 로드라 Build Settings에 켜져 있어야 런타임에 열린다.</summary>
        public static bool IsInBuildSettings(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return false;

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].enabled && scenes[i].path == scenePath) return true;
            }
            return false;
        }

        // ─────────── 그룹 스캔 (닫힌 씬 포함) ───────────

        /// <summary>스캔으로 찾은 진입점 하나. 닫힌 씬에서 온 것은 Component가 null이다.</summary>
        public struct EntryRecord
        {
            public string Key;
            public string SceneName;
            public bool Live;
            public SceneEntryPoint Component;
        }

        /// <summary>한 그룹의 진입점 현황.</summary>
        public class GroupScan
        {
            public SceneGroup Group;
            public readonly List<EntryRecord> Entries = new List<EntryRecord>();

            /// <summary>그룹이 이름으로 지목했지만 프로젝트에서 못 찾은 씬.</summary>
            public readonly List<string> MissingScenes = new List<string>();

            /// <summary>Build Settings에 없거나 꺼져 있는 씬 — 런타임에 로드가 실패한다.</summary>
            public readonly List<string> NotInBuild = new List<string>();

            /// <summary>구버전 LoadPoint만 있고 SceneEntryPoint는 없는 씬(승계 대상).</summary>
            public readonly List<string> LegacyOnlyScenes = new List<string>();

            public bool HasKey(string key)
            {
                if (string.IsNullOrEmpty(key)) return false;
                for (int i = 0; i < Entries.Count; i++)
                    if (Entries[i].Key == key) return true;
                return false;
            }

            public string[] Keys()
            {
                List<string> keys = new List<string>();
                for (int i = 0; i < Entries.Count; i++)
                    if (!keys.Contains(Entries[i].Key)) keys.Add(Entries[i].Key);
                return keys.ToArray();
            }

            /// <summary>같은 키가 둘 이상이면 로더가 먼저 찾은 것을 쓰므로 어느 쪽인지 알 수 없다.</summary>
            public bool HasDuplicate(string key)
            {
                int count = 0;
                for (int i = 0; i < Entries.Count; i++)
                    if (Entries[i].Key == key) count++;
                return count > 1;
            }
        }

        // 닫힌 씬은 파일을 다시 읽는 값이 비싸서 캐시한다. 열린 씬은 컴포넌트 목록만 캐시하고
        // 키 값은 매번 컴포넌트에서 직접 읽는다 — 인스펙터에서 키를 고친 즉시 결과에 반영되도록.
        private static readonly Dictionary<string, List<EntryRecord>> FileScans =
            new Dictionary<string, List<EntryRecord>>();
        private static readonly Dictionary<string, bool> LegacyScans = new Dictionary<string, bool>();
        private static readonly Dictionary<string, SceneEntryPoint[]> LiveScans =
            new Dictionary<string, SceneEntryPoint[]>();
        private static readonly Dictionary<System.Type, string> GuidCache = new Dictionary<System.Type, string>();

        [InitializeOnLoadMethod]
        private static void HookCacheInvalidation()
        {
            EditorApplication.hierarchyChanged += ClearLiveCache;
            EditorApplication.projectChanged += ClearAssetCache;
            EditorSceneManager.sceneOpened += (scene, mode) => ClearScanCache();
            EditorSceneManager.sceneClosed += scene => ClearScanCache();
            EditorSceneManager.sceneSaved += scene => ClearScanCache();
        }

        /// <summary>씬을 저장했거나 새로 열었을 때처럼 결과가 낡았을 만한 시점에 부른다.</summary>
        public static void ClearScanCache()
        {
            FileScans.Clear();
            LegacyScans.Clear();
            ClearLiveCache();
            ClearAssetCache();
        }

        /// <summary>SceneGroup 에셋이나 씬 파일이 추가·삭제·이동됐을 때.</summary>
        private static void ClearAssetCache()
        {
            _groupCache = null;
            ScenePathCache.Clear();
        }

        private static void ClearLiveCache() => LiveScans.Clear();

        /// <summary>
        /// 그룹의 네 씬을 훑어 진입점 키를 모은다. 열려 있는 씬은 실제 오브젝트를,
        /// 닫힌 씬은 .unity 텍스트를 읽는다(전부 열지 않고도 현황을 보기 위해).
        /// </summary>
        public static GroupScan ScanGroup(SceneGroup group)
        {
            GroupScan scan = new GroupScan { Group = group };
            if (group == null) return scan;

            foreach (string sceneName in group.EnumerateSceneNames())
            {
                string path = ScenePathOf(sceneName);
                if (string.IsNullOrEmpty(path))
                {
                    scan.MissingScenes.Add(sceneName);
                    continue;
                }

                if (!IsInBuildSettings(path)) scan.NotInBuild.Add(sceneName);

                int before = scan.Entries.Count;
                Scene open = SceneManager.GetSceneByName(sceneName);

                if (open.IsValid() && open.isLoaded) CollectLive(open, sceneName, scan.Entries);
                else scan.Entries.AddRange(CachedFileScan(path, sceneName));

                if (scan.Entries.Count != before) continue;

                // 진입점이 없는 씬이라면, 구버전 마커라도 남아 있는지 확인해 승계 대상으로 표시한다.
                if (HasLegacyLoadPoint(path, sceneName)) scan.LegacyOnlyScenes.Add(sceneName);
            }

            return scan;
        }

        private static void CollectLive(Scene scene, string sceneName, List<EntryRecord> into)
        {
            SceneEntryPoint[] found = LiveEntries(scene, sceneName);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] == null) continue;
                into.Add(new EntryRecord
                {
                    Key = found[i].EntryKey,
                    SceneName = sceneName,
                    Live = true,
                    Component = found[i]
                });
            }
        }

        /// <summary>열린 씬의 진입점 컴포넌트들. 계층이 바뀌기 전까지 캐시한다.</summary>
        public static SceneEntryPoint[] LiveEntries(Scene scene, string sceneName)
        {
            if (!scene.IsValid() || !scene.isLoaded) return new SceneEntryPoint[0];
            if (LiveScans.TryGetValue(sceneName, out SceneEntryPoint[] cached)) return cached;

            List<SceneEntryPoint> found = new List<SceneEntryPoint>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                found.AddRange(roots[i].GetComponentsInChildren<SceneEntryPoint>(true));

            SceneEntryPoint[] result = found.ToArray();
            LiveScans[sceneName] = result;
            return result;
        }

        private static List<EntryRecord> CachedFileScan(string path, string sceneName)
        {
            if (FileScans.TryGetValue(path, out List<EntryRecord> cached)) return cached;

            List<EntryRecord> records = new List<EntryRecord>();
            ScanSceneFile(path, sceneName, ScriptGuid(typeof(SceneEntryPoint)), P_EntryKey, records);
            FileScans[path] = records;
            return records;
        }

        private static bool HasLegacyLoadPoint(string path, string sceneName)
        {
            Scene open = SceneManager.GetSceneByName(sceneName);
            if (open.IsValid() && open.isLoaded) return LoadPointsInScene(open).Length > 0;

            if (LegacyScans.TryGetValue(path, out bool cached)) return cached;

            List<EntryRecord> legacy = new List<EntryRecord>();
            ScanSceneFile(path, sceneName, ScriptGuid(typeof(Aiara.LoadPoint)), "pointName", legacy);

            bool result = legacy.Count > 0;
            LegacyScans[path] = result;
            return result;
        }

        /// <summary>
        /// 닫힌 씬의 .unity YAML을 한 줄씩 흘려 읽으며 (스크립트 guid → 키 필드) 쌍을 찾는다.
        /// 씬 파일이 수십 MB일 수 있어 통째로 올리지 않는다.
        /// </summary>
        private static void ScanSceneFile(string path, string sceneName, string scriptGuid,
                                          string keyField, List<EntryRecord> into)
        {
            if (string.IsNullOrEmpty(scriptGuid) || !File.Exists(path)) return;

            string prefix = keyField + ":";
            int lookahead = 0;

            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();

                if (lookahead > 0)
                {
                    if (line.StartsWith(prefix))
                    {
                        into.Add(new EntryRecord
                        {
                            Key = line.Substring(prefix.Length).Trim(),
                            SceneName = sceneName,
                            Live = false,
                            Component = null
                        });
                        lookahead = 0;
                        continue;
                    }

                    // 다음 오브젝트 블록으로 넘어갔으면 이 컴포넌트에는 키 필드가 없는 것.
                    if (line.StartsWith("--- !u!")) lookahead = 0;
                    else lookahead--;
                    continue;
                }

                if (line.Contains(scriptGuid)) lookahead = 12;
            }
        }

        /// <summary>타입 → 그 타입을 정의한 스크립트 에셋의 GUID. 씬 텍스트 스캔의 열쇠.</summary>
        public static string ScriptGuid(System.Type type)
        {
            if (type == null) return null;
            if (GuidCache.TryGetValue(type, out string cached)) return cached;

            string result = null;
            string[] guids = AssetDatabase.FindAssets("t:MonoScript " + type.Name);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == type) { result = guids[i]; break; }
            }

            GuidCache[type] = result;
            return result;
        }

        // ─────────── 씬 뷰 보조 ───────────

        /// <summary>씬 뷰 카메라 앞쪽 지면. 씬 뷰가 없으면 원점.</summary>
        public static Vector3 SceneViewCenter()
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return Vector3.zero;

            Vector3 pivot = view.pivot;
            if (Physics.Raycast(pivot + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 40f,
                                ~0, QueryTriggerInteraction.Ignore))
                return hit.point;

            return pivot;
        }
    }
}
