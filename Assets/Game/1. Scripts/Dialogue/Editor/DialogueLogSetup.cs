using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Aiara.Dialogue.EditorTools
{
    /// <summary>
    /// 대화 로그창을 대화창 UI 프리팹 안에 만들고, 기록을 모으는 <see cref="DialogueLog"/>를
    /// 씬의 Dialogue Manager에 붙인다.
    /// 메뉴: <b>Tools/Aiara/대화 로그 셋업</b>
    ///
    /// 만드는 것 —
    /// <code>
    /// Yeolha Dialogue UI
    /// └ Dialogue Log            (항상 켜져 있음, DialogueLogPanel — 여는 입력을 받아야 하므로)
    ///   └ Window                (열 때만 켜짐, 어두운 배경)
    ///     ├ Title               "대화 로그"
    ///     └ Scroll View         (ScrollRect)
    ///       └ Viewport          (RectMask2D)
    ///         └ Content         (TMP_Text + ContentSizeFitter)
    /// </code>
    ///
    /// 대화창 UI 안에 두는 이유 — 캔버스를 새로 만들면 <see cref="DialogueGameUiHider"/>가
    /// 게임 UI로 오해하고 대화 중에 꺼버린다. 대화창은 그 예외 목록에 이미 들어 있다.
    ///
    /// <b>여러 번 눌러도 안전하다.</b> 이미 있으면 참조만 다시 물리고 생김새는 건드리지 않는다 —
    /// 색·크기를 프리팹에서 고쳐놨다면 그대로 남는다.
    /// </summary>
    public static class DialogueLogSetup
    {
        private const string RootName = "Dialogue Log";
        private const string WindowName = "Window";
        private const string TitleName = "Title";
        private const string ScrollName = "Scroll View";
        private const string ViewportName = "Viewport";
        private const string ContentName = "Content";

        private const string TitleTextValue = "대화 로그";

        [MenuItem("Tools/Aiara/대화 로그 셋업", false, 27)]
        public static void SetupDialogueLog()
        {
            string prefabPath = DialogueUiSetup.OutputPath;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                EditorUtility.DisplayDialog("대화 로그 셋업",
                    $"대화창 UI 프리팹이 없습니다:\n{prefabPath}\n\n" +
                    "먼저 'Tools/Aiara/대화창 UI 셋업'을 실행하세요.", "확인");
                return;
            }

            if (!BuildPanel(prefabPath))
            {
                return;
            }

            AttachLogToManager();

            AssetDatabase.SaveAssets();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log("[대화 로그] 셋업 완료 — 대화 중 패드 North(Y/△) 또는 Tab으로 로그창을 열고 닫습니다.\n" +
                      "· 기록은 대화 하나 단위입니다(새 대화가 시작되면 비웁니다).\n" +
                      "· 열려 있는 동안에는 대사가 넘어가지 않습니다.\n" +
                      $"· 생김새는 {prefabPath}의 '{RootName}' 아래에서 고치세요.", prefab);
        }

        /// <summary>프리팹 안에 로그창을 만들거나, 이미 있으면 참조만 다시 물린다.</summary>
        private static bool BuildPanel(string prefabPath)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                Debug.LogError($"[대화 로그] 프리팹을 열지 못했습니다: {prefabPath}");
                return false;
            }

            try
            {
                var canvas = root.GetComponentInChildren<Canvas>(true);
                Transform parent = canvas != null ? canvas.transform : root.transform;

                Transform logRoot = parent.Find(RootName);
                bool created = logRoot == null;

                if (created)
                {
                    logRoot = CreateFullScreen(RootName, parent).transform;
                }

                var panel = logRoot.GetComponent<DialogueLogPanel>();
                if (panel == null)
                {
                    panel = logRoot.gameObject.AddComponent<DialogueLogPanel>();
                }

                Transform window = logRoot.Find(WindowName);
                if (window == null)
                {
                    window = BuildWindow(logRoot);
                }

                panel.Window = window.gameObject;
                panel.Scroll = window.GetComponentInChildren<ScrollRect>(true);
                panel.Body = panel.Scroll != null
                    ? panel.Scroll.content != null ? panel.Scroll.content.GetComponent<TMP_Text>() : null
                    : null;

                if (panel.Body == null || panel.Scroll == null)
                {
                    Debug.LogWarning("[대화 로그] 스크롤 뷰 배선을 찾지 못했습니다. " +
                                     $"'{RootName}/{WindowName}'을 지우고 메뉴를 다시 실행하면 새로 만듭니다.");
                }

                // 마지막에 그려야 대화창 위로 덮인다.
                logRoot.SetAsLastSibling();

                // 편집 중에는 보이지 않게 접어둔다 — 켜둔 채 저장하면 플레이 시작부터 떠 있다.
                window.gameObject.SetActive(false);

                ApplyKoreanFont(logRoot.gameObject);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

                Debug.Log(created
                    ? $"[대화 로그] '{RootName}' 창을 새로 만들었습니다."
                    : $"[대화 로그] 이미 있는 '{RootName}' 창의 배선만 다시 맞췄습니다.");

                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>어두운 배경 + 제목 + 스크롤 뷰를 만든다.</summary>
        private static Transform BuildWindow(Transform logRoot)
        {
            GameObject window = CreateFullScreen(WindowName, logRoot);

            var dim = window.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.85f);
            dim.raycastTarget = false;

            // 화면 가장자리에서 조금 띄운다.
            var windowRect = (RectTransform)window.transform;
            windowRect.offsetMin = new Vector2(80f, 60f);
            windowRect.offsetMax = new Vector2(-80f, -60f);

            GameObject title = CreateChild(TitleName, window.transform);
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(24f, -56f);
            titleRect.offsetMax = new Vector2(-24f, -16f);

            var titleText = title.AddComponent<TextMeshProUGUI>();
            titleText.text = TitleTextValue;
            titleText.fontSize = 28f;
            titleText.alignment = TextAlignmentOptions.Left;
            titleText.raycastTarget = false;

            GameObject scrollObject = CreateChild(ScrollName, window.transform);
            var scrollRectTransform = (RectTransform)scrollObject.transform;
            scrollRectTransform.anchorMin = Vector2.zero;
            scrollRectTransform.anchorMax = Vector2.one;
            scrollRectTransform.offsetMin = new Vector2(24f, 24f);
            scrollRectTransform.offsetMax = new Vector2(-24f, -64f);

            var scroll = scrollObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            GameObject viewport = CreateChild(ViewportName, scrollObject.transform);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewport.AddComponent<RectMask2D>();

            GameObject content = CreateChild(ContentName, viewport.transform);
            var contentRect = (RectTransform)content.transform;

            // 위에서 아래로 자라는 글이므로 위쪽 가장자리에 고정한다.
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            var body = content.AddComponent<TextMeshProUGUI>();
            body.text = string.Empty;
            body.fontSize = 24f;
            body.alignment = TextAlignmentOptions.TopLeft;
            body.raycastTarget = false;

            // 글이 길어진 만큼 Content가 늘어나야 스크롤이 생긴다.
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRect;
            scroll.content = contentRect;

            return window.transform;
        }

        private static GameObject CreateFullScreen(string name, Transform parent)
        {
            GameObject go = CreateChild(name, parent);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go;
        }

        private static GameObject CreateChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void ApplyKoreanFont(GameObject root)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DialogueUiSetup.KoreanFontPath);
            if (font == null)
            {
                Debug.LogWarning($"[대화 로그] 한글 폰트를 찾지 못해 폰트는 건드리지 않았습니다: " +
                                 DialogueUiSetup.KoreanFontPath);
                return;
            }

            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                text.font = font;
            }
        }

        /// <summary>기록을 모으는 컴포넌트를 씬의 Dialogue Manager에 붙인다.</summary>
        private static void AttachLogToManager()
        {
            var controller = Object.FindFirstObjectByType<DialogueSystemController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[대화 로그] 열린 씬에 Dialogue Manager가 없어 프리팹만 만들었습니다. " +
                                 "Dialogue Manager를 올린 씬을 열고 한 번 더 실행하세요.");
                return;
            }

            if (controller.GetComponent<DialogueLog>() != null)
            {
                return;
            }

            Undo.AddComponent<DialogueLog>(controller.gameObject);
            EditorUtility.SetDirty(controller.gameObject);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);

            Debug.Log("[대화 로그] Dialogue Manager에 Dialogue Log를 붙였습니다. 씬을 저장하세요.", controller);
        }
    }
}
