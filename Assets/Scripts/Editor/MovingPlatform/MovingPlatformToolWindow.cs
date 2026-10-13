using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 엘리베이터(MovingPlatform) 저작 창. 씬의 발판을 한눈에 훑고, 새로 놓고, 세팅 실수를 검증해 고친다.
    ///
    /// 엘리베이터는 경로·발판·탑승 존이 서로 다른 오브젝트에 흩어져 있어 인스펙터만 보면
    /// "움직이는데 안 태우는" 상태를 알아채기 어렵다. 여기서 세 축을 함께 본다.
    /// </summary>
    public class MovingPlatformToolWindow : EditorWindow
    {
        private Vector2 _scroll;
        private bool _showQuickSetup = true;

        [MenuItem("Tools/Yeolha/엘리베이터 툴")]
        public static void Open()
        {
            MovingPlatformToolWindow window = GetWindow<MovingPlatformToolWindow>();
            window.titleContent = new GUIContent("엘리베이터 툴");
            window.minSize = new Vector2(360f, 480f);
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

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawCreateSection();
            EditorGUILayout.Space(8f);
            DrawScenePlatforms();
            EditorGUILayout.Space(8f);
            DrawQuickSetup();
            EditorGUILayout.Space(8f);
            DrawValidation();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>선택 계층에서 올라가며 찾은 발판. 자식(EndPoint/RiderZone)을 골라도 잡힌다.</summary>
        private static MovingPlatform SelectedPlatform()
        {
            return Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<MovingPlatform>()
                : null;
        }

        // ─────────── 생성 ───────────

        private void DrawCreateSection()
        {
            EditorGUILayout.LabelField("생성", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("씬 뷰 중앙에 배치", GUILayout.Height(26f)))
                    MovingPlatformAuthoring.CreatePlatform(SceneViewCenter());

                using (new EditorGUI.DisabledScope(Selection.activeTransform == null))
                {
                    if (GUILayout.Button("선택 위치에 배치", GUILayout.Height(26f)))
                        MovingPlatformAuthoring.CreatePlatform(Selection.activeTransform.position);
                }
            }
        }

        /// <summary>씬 뷰 카메라 앞쪽 지면. 씬 뷰가 없으면 원점.</summary>
        private static Vector3 SceneViewCenter()
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return Vector3.zero;

            Vector3 pivot = view.pivot;
            if (Physics.Raycast(pivot + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 40f,
                                ~0, QueryTriggerInteraction.Ignore))
                return hit.point;

            return pivot;
        }

        // ─────────── 씬 목록 ───────────

        private void DrawScenePlatforms()
        {
            MovingPlatform[] platforms = Object.FindObjectsByType<MovingPlatform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            EditorGUILayout.LabelField($"씬의 엘리베이터 ({platforms.Length})", EditorStyles.boldLabel);

            if (platforms.Length == 0)
            {
                EditorGUILayout.HelpBox("이 씬에 움직이는 발판이 없습니다.", MessageType.Info);
                return;
            }

            for (int i = 0; i < platforms.Length; i++)
            {
                MovingPlatform platform = platforms[i];
                int issues = MovingPlatformValidator.Validate(platform).Count;

                using (new EditorGUILayout.HorizontalScope())
                {
                    Color prev = GUI.color;
                    if (issues > 0) GUI.color = new Color(1f, 0.75f, 0.3f);
                    GUILayout.Label(issues > 0 ? "▲" : "■", GUILayout.Width(16f));
                    GUI.color = prev;

                    float distance = MovingPlatformAuthoring.TravelDistance(platform);
                    string mode = MovingPlatformAuthoring.ModeLabel(MovingPlatformAuthoring.GetMode(platform));
                    EditorGUILayout.LabelField($"{platform.gameObject.name}  ({mode} · {distance:0.##}m)");

                    if (issues > 0) GUILayout.Label($"{issues}건", GUILayout.Width(34f));

                    if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(46f)))
                        Selection.activeGameObject = platform.gameObject;
                }
            }

            EditorGUILayout.Space(4f);
            if (GUILayout.Button("전체 계층 자동 구성"))
            {
                for (int i = 0; i < platforms.Length; i++)
                    MovingPlatformAuthoring.EnsureHierarchy(platforms[i]);
            }
        }

        // ─────────── 빠른 세팅 ───────────

        private void DrawQuickSetup()
        {
            _showQuickSetup = EditorGUILayout.Foldout(_showQuickSetup, "빠른 세팅", true,
                                                      EditorStyles.foldoutHeader);
            if (!_showQuickSetup) return;

            MovingPlatform platform = SelectedPlatform();
            if (platform == null)
            {
                EditorGUILayout.HelpBox("세팅할 발판을 선택하세요.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(platform.gameObject.name, EditorStyles.miniBoldLabel);

            DrawPresets(platform);
            EditorGUILayout.Space(4f);
            DrawRunFields(platform);
            EditorGUILayout.Space(4f);
            DrawPathFields(platform);
        }

        /// <summary>모드·속도·대기·경로 방향을 한 번에 맞추는 묶음 세팅.</summary>
        private static void DrawPresets(MovingPlatform platform)
        {
            EditorGUILayout.LabelField("프리셋", EditorStyles.miniBoldLabel);

            foreach (MovingPlatformAuthoring.Preset preset in
                     System.Enum.GetValues(typeof(MovingPlatformAuthoring.Preset)))
            {
                if (GUILayout.Button(MovingPlatformAuthoring.PresetLabel(preset)))
                    MovingPlatformAuthoring.ApplyPreset(platform, preset);
            }
        }

        private static void DrawRunFields(MovingPlatform platform)
        {
            EditorGUILayout.LabelField("운행", EditorStyles.miniBoldLabel);

            MovingPlatform.Mode mode = MovingPlatformAuthoring.GetMode(platform);
            EditorGUI.BeginChangeCheck();
            MovingPlatform.Mode newMode = (MovingPlatform.Mode)EditorGUILayout.EnumPopup("운행 방식", mode);
            if (EditorGUI.EndChangeCheck()) MovingPlatformAuthoring.SetMode(platform, newMode);

            DrawFloat(platform, MovingPlatformAuthoring.P_Speed, "속도 (m/s)", 0.01f, "Set Platform Speed");
            DrawFloat(platform, MovingPlatformAuthoring.P_WaitTime, "끝에서 대기 (s)", 0f, "Set Platform Wait");
            DrawFloat(platform, MovingPlatformAuthoring.P_StartDelay, "출발 지연 (s)", 0f, "Set Platform Delay");

            bool ease = MovingPlatformAuthoring.GetBool(platform, MovingPlatformAuthoring.P_Ease);
            EditorGUI.BeginChangeCheck();
            bool newEase = EditorGUILayout.Toggle("양 끝 감속", ease);
            if (EditorGUI.EndChangeCheck())
                MovingPlatformAuthoring.SetBool(platform, MovingPlatformAuthoring.P_Ease, newEase, "Set Platform Ease");

            float distance = MovingPlatformAuthoring.TravelDistance(platform);
            float oneWay = MovingPlatformAuthoring.OneWayTime(platform);
            EditorGUILayout.LabelField($"거리 {distance:0.##}m · 편도 {oneWay:0.##}초 · " +
                                       $"주기 {MovingPlatformAuthoring.CycleTime(platform):0.##}초");
        }

        private static void DrawFloat(MovingPlatform platform, string prop, string label, float min, string undo)
        {
            float value = MovingPlatformAuthoring.GetFloat(platform, prop);

            EditorGUI.BeginChangeCheck();
            float next = EditorGUILayout.FloatField(label, value);
            if (!EditorGUI.EndChangeCheck()) return;

            MovingPlatformAuthoring.SetFloat(platform, prop, Mathf.Max(min, next), undo);
        }

        private static void DrawPathFields(MovingPlatform platform)
        {
            EditorGUILayout.LabelField("경로", EditorStyles.miniBoldLabel);

            Vector3 delta = MovingPlatformAuthoring.EndWorld(platform) - MovingPlatformAuthoring.StartWorld(platform);

            EditorGUI.BeginChangeCheck();
            float height = EditorGUILayout.FloatField("이동 높이 (m)", delta.y);
            if (EditorGUI.EndChangeCheck()) MovingPlatformAuthoring.SetTravelHeight(platform, height);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("수직 정렬")) MovingPlatformAuthoring.AlignVertical(platform);
                if (GUILayout.Button("수평 정렬")) MovingPlatformAuthoring.AlignHorizontal(platform);
                if (GUILayout.Button("시작·끝 뒤집기")) MovingPlatformAuthoring.SwapEnds(platform);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("바닥에 맞추기"))
                {
                    if (!MovingPlatformAuthoring.SnapToGround(platform))
                        Debug.LogWarning("[MovingPlatform] 아래에서 바닥 콜라이더를 찾지 못했습니다.", platform);
                }

                if (GUILayout.Button("탑승 존 맞추기"))
                {
                    if (!MovingPlatformAuthoring.FitRiderZone(platform))
                        Debug.LogWarning("[MovingPlatform] 탑승 판정 존이나 발판 콜라이더를 찾지 못했습니다.", platform);
                }

                if (GUILayout.Button("도착 지점 선택"))
                {
                    Transform end = MovingPlatformAuthoring.EndPointOf(platform);
                    if (end != null) Selection.activeGameObject = end.gameObject;
                }
            }
        }

        // ─────────── 검증 ───────────

        private void DrawValidation()
        {
            EditorGUILayout.LabelField("검증", EditorStyles.boldLabel);

            MovingPlatform platform = SelectedPlatform();
            if (platform == null)
            {
                EditorGUILayout.HelpBox("검증할 발판을 선택하세요.", MessageType.Info);
                return;
            }

            List<MovingPlatformValidator.Issue> issues = MovingPlatformValidator.Validate(platform);
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("문제를 찾지 못했습니다.", MessageType.Info);
                return;
            }

            DrawIssues(issues);
        }

        private static void DrawIssues(List<MovingPlatformValidator.Issue> issues)
        {
            for (int i = 0; i < issues.Count; i++)
            {
                MovingPlatformValidator.Issue issue = issues[i];

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
