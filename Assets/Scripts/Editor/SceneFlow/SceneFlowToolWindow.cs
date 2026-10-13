using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 포탈·진입점 저작 창. 포탈을 놓고, 그 포탈이 보낼 진입점을 놓고, 둘이 실제로 이어졌는지 확인한다.
    ///
    /// 이 두 오브젝트는 서로 다른 씬에 살기 때문에 인스펙터만으로는 연결 상태를 볼 수 없다.
    /// (포탈은 A 그룹의 씬에, 진입점은 B 그룹의 씬에 있고, 이어 주는 것은 EntryKey 문자열뿐이다.)
    /// 그래서 이 창은 닫힌 씬의 .unity 파일까지 훑어 그룹별 진입점 현황을 만든다.
    /// </summary>
    public class SceneFlowToolWindow : EditorWindow
    {
        private Vector2 _scroll;

        private int _targetSceneIndex;
        private SceneGroup _createGroup;
        private string _createPortalKey = "";
        private string _createEntryKey = SceneFlowAuthoring.DefaultEntryKey;
        private bool _createFaceRight = true;

        private bool _showGroups;
        private bool _showLegacy = true;
        private bool _removeLegacyOnConvert = true;

        [MenuItem("Tools/Yeolha/포탈 · 진입점 툴")]
        public static void Open()
        {
            SceneFlowToolWindow window = GetWindow<SceneFlowToolWindow>();
            window.titleContent = new GUIContent("포탈 툴");
            window.minSize = new Vector2(400f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            Selection.selectionChanged += Repaint;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
        }

        private void OnFocus()
        {
            // 다른 창에서 씬을 열거나 저장했을 수 있다.
            SceneFlowAuthoring.ClearScanCache();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawCreateSection();
            EditorGUILayout.Space(8f);
            DrawOpenScenes();
            EditorGUILayout.Space(8f);
            DrawLegacySection();
            EditorGUILayout.Space(8f);
            DrawGroupSection();
            EditorGUILayout.Space(8f);
            DrawValidation();

            EditorGUILayout.EndScrollView();
        }

        // ─────────── 배치 ───────────

        private void DrawCreateSection()
        {
            EditorGUILayout.LabelField("배치", EditorStyles.boldLabel);

            List<Scene> scenes = SceneFlowAuthoring.OpenScenes();
            if (scenes.Count == 0)
            {
                EditorGUILayout.HelpBox("열린 씬이 없습니다.", MessageType.Info);
                return;
            }

            string[] names = new string[scenes.Count];
            for (int i = 0; i < scenes.Count; i++) names[i] = scenes[i].name;

            _targetSceneIndex = Mathf.Clamp(_targetSceneIndex, 0, scenes.Count - 1);
            _targetSceneIndex = EditorGUILayout.Popup("배치할 씬", _targetSceneIndex, names);
            Scene target = scenes[_targetSceneIndex];

            // 배치 씬을 잘못 고르면(예: Game.unity) 로더가 영영 찾지 못한다 — 미리 알려 준다.
            if (SceneFlowAuthoring.GroupsContainingScene(target.name).Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"'{target.name}'은 어떤 SceneGroup에도 속하지 않습니다. 여기 놓은 진입점은 로더가 찾지 못합니다.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(4f);
            DrawCreatePortal(target);
            EditorGUILayout.Space(4f);
            DrawCreateEntry(target);
        }

        private void DrawCreatePortal(Scene target)
        {
            EditorGUILayout.LabelField("포탈", EditorStyles.miniBoldLabel);

            _createGroup = (SceneGroup)EditorGUILayout.ObjectField("보낼 그룹", _createGroup, typeof(SceneGroup), false);
            _createPortalKey = EditorGUILayout.TextField("EntryKey (비우면 기본)", _createPortalKey);

            if (_createGroup != null)
            {
                SceneFlowAuthoring.GroupScan scan = SceneFlowAuthoring.ScanGroup(_createGroup);
                string[] keys = scan.Keys();
                EditorGUILayout.LabelField(
                    keys.Length == 0
                        ? "대상 그룹에서 찾은 진입점: 없음"
                        : "대상 그룹의 진입점: " + string.Join(", ", keys),
                    EditorStyles.miniLabel);
            }

            using (new EditorGUI.DisabledScope(_createGroup == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("씬 뷰 중앙에 포탈 배치", GUILayout.Height(24f)))
                        SceneFlowAuthoring.CreatePortal(target, SceneFlowAuthoring.SceneViewCenter(),
                                                        _createGroup, _createPortalKey);

                    using (new EditorGUI.DisabledScope(Selection.activeTransform == null))
                    {
                        if (GUILayout.Button("선택 위치에 배치", GUILayout.Height(24f)))
                            SceneFlowAuthoring.CreatePortal(target, Selection.activeTransform.position,
                                                            _createGroup, _createPortalKey);
                    }
                }
            }
        }

        private void DrawCreateEntry(Scene target)
        {
            EditorGUILayout.LabelField("진입점", EditorStyles.miniBoldLabel);

            _createEntryKey = EditorGUILayout.TextField("EntryKey", _createEntryKey);
            _createFaceRight = EditorGUILayout.Toggle("오른쪽을 보고 스폰", _createFaceRight);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("씬 뷰 중앙에 진입점 배치", GUILayout.Height(24f)))
                    SceneFlowAuthoring.CreateEntryPoint(target, SceneFlowAuthoring.SceneViewCenter(),
                                                        _createEntryKey, _createFaceRight);

                using (new EditorGUI.DisabledScope(Selection.activeTransform == null))
                {
                    if (GUILayout.Button("선택 위치에 배치", GUILayout.Height(24f)))
                        SceneFlowAuthoring.CreateEntryPoint(target, Selection.activeTransform.position,
                                                            _createEntryKey, _createFaceRight);
                }
            }
        }

        // ─────────── 열린 씬 현황 ───────────

        private void DrawOpenScenes()
        {
            EditorGUILayout.LabelField("열린 씬", EditorStyles.boldLabel);

            List<Scene> scenes = SceneFlowAuthoring.OpenScenes();
            for (int i = 0; i < scenes.Count; i++)
            {
                Scene scene = scenes[i];
                Portal[] portals = PortalsIn(scene);
                SceneEntryPoint[] entries = SceneFlowAuthoring.LiveEntries(scene, scene.name);

                EditorGUILayout.LabelField($"{scene.name}  (포탈 {portals.Length} · 진입점 {entries.Length})",
                                           EditorStyles.miniBoldLabel);

                for (int p = 0; p < portals.Length; p++) DrawPortalRow(portals[p]);
                for (int e = 0; e < entries.Length; e++) DrawEntryRow(entries[e]);
            }
        }

        private static void DrawPortalRow(Portal portal)
        {
            int errors = SceneFlowValidator.CountErrors(SceneFlowValidator.ValidatePortal(portal));

            using (new EditorGUILayout.HorizontalScope())
            {
                Color prev = GUI.color;
                if (errors > 0) GUI.color = new Color(1f, 0.6f, 0.5f);
                GUILayout.Label(errors > 0 ? "▲" : "→", GUILayout.Width(16f));
                GUI.color = prev;

                string targetLabel = portal.TargetGroup != null
                    ? $"{portal.TargetGroup.name} / {SceneFlowAuthoring.ResolvedEntryKey(portal)}"
                    : "(대상 없음)";

                EditorGUILayout.LabelField($"{portal.gameObject.name} → {targetLabel}");

                if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(46f)))
                    Selection.activeGameObject = portal.gameObject;
            }
        }

        private static void DrawEntryRow(SceneEntryPoint entry)
        {
            int errors = SceneFlowValidator.CountErrors(SceneFlowValidator.ValidateEntry(entry));

            using (new EditorGUILayout.HorizontalScope())
            {
                Color prev = GUI.color;
                if (errors > 0) GUI.color = new Color(1f, 0.6f, 0.5f);
                GUILayout.Label(errors > 0 ? "▲" : "●", GUILayout.Width(16f));
                GUI.color = prev;

                EditorGUILayout.LabelField(
                    $"{entry.gameObject.name} · 키 '{entry.EntryKey}' · {(entry.FaceRight ? "→" : "←")}");

                if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(46f)))
                    Selection.activeGameObject = entry.gameObject;
            }
        }

        private static Portal[] PortalsIn(Scene scene)
        {
            List<Portal> found = new List<Portal>();
            if (!scene.IsValid() || !scene.isLoaded) return found.ToArray();

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                found.AddRange(roots[i].GetComponentsInChildren<Portal>(true));

            return found.ToArray();
        }

        // ─────────── LoadPoint 승계 ───────────

        private void DrawLegacySection()
        {
            _showLegacy = EditorGUILayout.Foldout(_showLegacy, "LoadPoint 승계", true, EditorStyles.foldoutHeader);
            if (!_showLegacy) return;

            EditorGUILayout.HelpBox(
                "구버전 마커 Aiara.LoadPoint는 읽는 코드가 없어 로더가 무시합니다. " +
                "위치는 그대로 두고 pointName → EntryKey, characterAngle → 바라보는 방향으로 옮깁니다. " +
                "변환은 열린 씬에만 적용되며, 저장은 직접 하셔야 합니다.",
                MessageType.None);

            _removeLegacyOnConvert = EditorGUILayout.Toggle("변환 후 LoadPoint 제거", _removeLegacyOnConvert);

            List<Scene> scenes = SceneFlowAuthoring.OpenScenes();
            int total = 0;

            for (int i = 0; i < scenes.Count; i++)
            {
                Aiara.LoadPoint[] points = SceneFlowAuthoring.LoadPointsInScene(scenes[i]);
                if (points.Length == 0) continue;

                total += points.Length;
                Scene scene = scenes[i];

                EditorGUILayout.LabelField($"{scene.name} ({points.Length})", EditorStyles.miniBoldLabel);

                for (int p = 0; p < points.Length; p++)
                {
                    Aiara.LoadPoint point = points[p];
                    bool already = point.GetComponent<SceneEntryPoint>() != null;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            $"· '{point.pointName}' ({point.characterAngle:0}°){(already ? " — 승계됨" : "")}");

                        if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(46f)))
                            Selection.activeGameObject = point.gameObject;

                        using (new EditorGUI.DisabledScope(already))
                        {
                            if (GUILayout.Button("승계", EditorStyles.miniButton, GUILayout.Width(46f)))
                            {
                                SceneFlowAuthoring.ConvertLoadPoint(point, _removeLegacyOnConvert);
                                GUIUtility.ExitGUI();
                            }
                        }
                    }
                }

                if (GUILayout.Button($"'{scene.name}'의 LoadPoint 전부 승계"))
                {
                    ConvertAll(points);
                    GUIUtility.ExitGUI();
                }
            }

            if (total == 0)
                EditorGUILayout.HelpBox("열린 씬에 LoadPoint가 없습니다.", MessageType.Info);
            else if (GUILayout.Button("열린 씬 저장"))
                SceneFlowSceneOps.SaveOpenScenes();
        }

        private void ConvertAll(Aiara.LoadPoint[] points)
        {
            int group = Undo.GetCurrentGroup();
            for (int i = 0; i < points.Length; i++)
                SceneFlowAuthoring.ConvertLoadPoint(points[i], _removeLegacyOnConvert);

            Undo.CollapseUndoOperations(group);
            SceneFlowAuthoring.ClearScanCache();
        }

        // ─────────── 그룹 현황 ───────────

        private void DrawGroupSection()
        {
            _showGroups = EditorGUILayout.Foldout(_showGroups, "그룹별 진입점 현황", true, EditorStyles.foldoutHeader);
            if (!_showGroups)
            {
                EditorGUILayout.LabelField("펼치면 닫힌 씬 파일까지 훑어 현황을 만듭니다.", EditorStyles.miniLabel);
                return;
            }

            if (GUILayout.Button("다시 스캔")) SceneFlowAuthoring.ClearScanCache();

            SceneGroup[] groups = SceneFlowAuthoring.AllGroups();
            for (int i = 0; i < groups.Length; i++)
            {
                SceneGroup group = groups[i];
                SceneFlowAuthoring.GroupScan scan = SceneFlowAuthoring.ScanGroup(group);

                bool ok = scan.HasKey(group.DefaultEntryKey) &&
                          scan.MissingScenes.Count == 0 && scan.NotInBuild.Count == 0;

                using (new EditorGUILayout.HorizontalScope())
                {
                    Color prev = GUI.color;
                    if (!ok) GUI.color = new Color(1f, 0.75f, 0.3f);
                    GUILayout.Label(ok ? "■" : "▲", GUILayout.Width(16f));
                    GUI.color = prev;

                    string keys = scan.Entries.Count == 0 ? "진입점 없음" : string.Join(", ", scan.Keys());
                    EditorGUILayout.LabelField($"{group.name} — {keys}");

                    if (GUILayout.Button("에셋", EditorStyles.miniButton, GUILayout.Width(46f)))
                        Selection.activeObject = group;

                    if (GUILayout.Button("씬 열기", EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        SceneFlowSceneOps.OpenGroupScenes(group);
                        GUIUtility.ExitGUI();
                    }
                }

                if (scan.LegacyOnlyScenes.Count > 0)
                {
                    EditorGUILayout.LabelField(
                        "   └ 구버전 LoadPoint만 있음: " + string.Join(", ", scan.LegacyOnlyScenes),
                        EditorStyles.miniLabel);
                }

                if (scan.MissingScenes.Count > 0)
                {
                    EditorGUILayout.LabelField("   └ 씬 없음: " + string.Join(", ", scan.MissingScenes),
                                               EditorStyles.miniLabel);
                }

                if (scan.NotInBuild.Count > 0)
                {
                    EditorGUILayout.LabelField("   └ Build 미등록: " + string.Join(", ", scan.NotInBuild),
                                               EditorStyles.miniLabel);
                }
            }
        }

        // ─────────── 검증 ───────────

        private void DrawValidation()
        {
            EditorGUILayout.LabelField("검증", EditorStyles.boldLabel);

            GameObject selected = Selection.activeGameObject;
            Portal portal = selected != null ? selected.GetComponentInParent<Portal>() : null;
            SceneEntryPoint entry = selected != null ? selected.GetComponentInParent<SceneEntryPoint>() : null;

            if (portal == null && entry == null)
            {
                EditorGUILayout.HelpBox("검증할 포탈이나 진입점을 선택하세요.", MessageType.Info);
                return;
            }

            if (portal != null)
            {
                EditorGUILayout.LabelField(portal.gameObject.name, EditorStyles.miniBoldLabel);
                DrawIssues(SceneFlowValidator.ValidatePortal(portal));
            }

            if (entry != null)
            {
                EditorGUILayout.LabelField(entry.gameObject.name, EditorStyles.miniBoldLabel);
                DrawIssues(SceneFlowValidator.ValidateEntry(entry));
            }
        }

        private static void DrawIssues(List<SceneFlowValidator.Issue> issues)
        {
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("문제를 찾지 못했습니다.", MessageType.Info);
                return;
            }

            for (int i = 0; i < issues.Count; i++)
            {
                SceneFlowValidator.Issue issue = issues[i];

                EditorGUILayout.HelpBox(issue.Message, issue.Type);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    if (issue.Context != null &&
                        GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(46f)))
                    {
                        Selection.activeObject = issue.Context;
                        EditorGUIUtility.PingObject(issue.Context);
                    }

                    if (issue.Fix != null &&
                        GUILayout.Button(issue.FixLabel, EditorStyles.miniButton, GUILayout.Width(110f)))
                    {
                        issue.Fix();
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }
    }
}
