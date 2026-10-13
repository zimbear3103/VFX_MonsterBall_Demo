using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Yeolha.BeltScroll.EditorTools
{
    /// <summary>
    /// 사다리 저작 창. 씬의 사다리를 한눈에 훑고, 새로 놓고, 세팅 실수를 검증해 고친다.
    ///
    /// 사다리는 계층·수치·애니메이터 세 축이 모두 맞아야 동작하는데 셋이 서로 다른 곳에 흩어져 있다.
    /// 특히 애니메이터 쪽(파라미터·climbingUp/Down 이벤트)은 사다리 오브젝트만 봐서는 알 수 없어
    /// 여기서 함께 검사한다.
    /// </summary>
    public class LadderToolWindow : EditorWindow
    {
        private Vector2 _scroll;
        private Animator _animatorToCheck;
        private bool _showAnimatorSection = true;

        [MenuItem("Tools/Yeolha/사다리 툴")]
        public static void Open()
        {
            LadderToolWindow window = GetWindow<LadderToolWindow>();
            window.titleContent = new GUIContent("사다리 툴");
            window.minSize = new Vector2(360f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            Selection.selectionChanged += Repaint;
            if (_animatorToCheck == null) _animatorToCheck = FindPlayerAnimator();
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
            DrawSceneLadders();
            EditorGUILayout.Space(8f);
            DrawSelectedLadder();
            EditorGUILayout.Space(8f);
            DrawAnimatorSection();

            EditorGUILayout.EndScrollView();
        }

        // ─────────── 생성 ───────────

        private void DrawCreateSection()
        {
            EditorGUILayout.LabelField("생성", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("씬 뷰 중앙에 사다리 배치", GUILayout.Height(26f)))
                    LadderAuthoring.CreateLadder(SceneViewCenter());

                using (new EditorGUI.DisabledScope(Selection.activeTransform == null))
                {
                    if (GUILayout.Button("선택 위치에 배치", GUILayout.Height(26f)))
                        LadderAuthoring.CreateLadder(Selection.activeTransform.position);
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

        private void DrawSceneLadders()
        {
            LadderTraversable[] ladders = Object.FindObjectsByType<LadderTraversable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            EditorGUILayout.LabelField($"씬의 사다리 ({ladders.Length})", EditorStyles.boldLabel);

            if (ladders.Length == 0)
            {
                EditorGUILayout.HelpBox("이 씬에 사다리가 없습니다.", MessageType.Info);
                return;
            }

            for (int i = 0; i < ladders.Length; i++)
            {
                LadderTraversable ladder = ladders[i];
                int issues = LadderValidator.Validate(ladder).Count;

                using (new EditorGUILayout.HorizontalScope())
                {
                    Color prev = GUI.color;
                    if (issues > 0) GUI.color = new Color(1f, 0.75f, 0.3f);
                    GUILayout.Label(issues > 0 ? "▲" : "■", GUILayout.Width(16f));
                    GUI.color = prev;

                    float height = LadderAuthoring.GetFloat(ladder, LadderAuthoring.P_Height);
                    EditorGUILayout.LabelField($"{ladder.gameObject.name}  ({height:0.##}m)");

                    if (issues > 0) GUILayout.Label($"{issues}건", GUILayout.Width(34f));

                    if (GUILayout.Button("선택", EditorStyles.miniButton, GUILayout.Width(46f)))
                        Selection.activeGameObject = ladder.gameObject;
                }
            }

            EditorGUILayout.Space(4f);
            if (GUILayout.Button("전체 계층 자동 구성"))
            {
                for (int i = 0; i < ladders.Length; i++) LadderAuthoring.EnsureHierarchy(ladders[i]);
            }
        }

        // ─────────── 선택 항목 검증 ───────────

        private void DrawSelectedLadder()
        {
            LadderTraversable ladder = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<LadderTraversable>()
                : null;

            EditorGUILayout.LabelField("검증", EditorStyles.boldLabel);

            if (ladder == null)
            {
                EditorGUILayout.HelpBox("검증할 사다리를 선택하세요.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(ladder.gameObject.name, EditorStyles.miniBoldLabel);

            List<LadderValidator.Issue> issues = LadderValidator.Validate(ladder);
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("문제를 찾지 못했습니다.", MessageType.Info);
                return;
            }

            DrawIssues(issues);
        }

        // ─────────── 애니메이터 계약 ───────────

        private void DrawAnimatorSection()
        {
            _showAnimatorSection = EditorGUILayout.Foldout(_showAnimatorSection, "애니메이터 계약", true,
                                                           EditorStyles.foldoutHeader);
            if (!_showAnimatorSection) return;

            EditorGUILayout.HelpBox(
                "사다리 등반은 컨트롤러 파라미터와 등반 클립의 climbingUp/Down 이벤트가 모두 있어야 동작합니다. " +
                "이 검사는 사다리마다가 아니라 플레이어 쪽 설정 한 번만 맞추면 됩니다.",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                _animatorToCheck = (Animator)EditorGUILayout.ObjectField(
                    "Animator", _animatorToCheck, typeof(Animator), true);

                if (GUILayout.Button("플레이어 찾기", GUILayout.Width(88f)))
                    _animatorToCheck = FindPlayerAnimator();
            }

            if (_animatorToCheck == null)
            {
                EditorGUILayout.HelpBox("씬에서 플레이어 Animator를 찾지 못했습니다. 직접 지정하세요.", MessageType.Info);
                return;
            }

            List<LadderValidator.Issue> issues = LadderValidator.ValidateAnimator(_animatorToCheck);
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("애니메이터 계약이 모두 충족되었습니다.", MessageType.Info);
                return;
            }

            DrawIssues(issues);
        }

        /// <summary>씬에서 플레이어 진영 Character의 Animator를 찾는다.</summary>
        private static Animator FindPlayerAnimator()
        {
            Character[] characters = Object.FindObjectsByType<Character>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i].Faction != FactionType.Player) continue;

                Animator animator = characters[i].GetComponentInChildren<Animator>(true);
                if (animator != null) return animator;
            }
            return null;
        }

        // ─────────── 공통 ───────────

        private static void DrawIssues(List<LadderValidator.Issue> issues)
        {
            for (int i = 0; i < issues.Count; i++)
            {
                LadderValidator.Issue issue = issues[i];

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
