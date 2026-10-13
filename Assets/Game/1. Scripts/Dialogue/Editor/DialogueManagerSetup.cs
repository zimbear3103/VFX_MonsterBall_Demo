using PixelCrushers;
using PixelCrushers.DialogueSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Aiara.Dialogue.EditorTools
{
    /// <summary>
    /// 열린 씬의 Dialogue Manager를 이 프로젝트 규약대로 맞춘다.
    /// 메뉴: <b>Tools/Aiara/대화 매니저 셋업 (열린 씬)</b>
    ///
    /// 왜 따로 있는가 —
    /// 원래 이 배선은 <see cref="DialogueUiSetup"/>(대화창 UI 셋업)에만 들어 있었는데, 그 메뉴는
    /// **UI 프리팹을 통째로 다시 만든다** (PC/아군 패널을 NPC 패널 복제본으로 갈아끼운다).
    /// 매니저 설정만 고치고 싶을 때 프리팹까지 날릴 수는 없으므로 여기로 뽑아냈다.
    /// 대화창 UI 셋업은 이제 이 클래스를 불러 쓴다 — 규약이 한 군데에만 있게 된다.
    ///
    /// 여기서 맞추는 값은 전부 **씬의 프리팹 인스턴스 오버라이드**다. Pixel Crushers의
    /// Dialogue Manager 프리팹(Assets/Plugins) 자체는 건드리지 않는다 — 플러그인을 업데이트하면
    /// 덮어써지는 자리이기 때문이다. 그래서 Dialogue Manager를 올린 씬을 열고 실행해야 하고,
    /// 씬을 저장해야 남는다.
    /// </summary>
    public static class DialogueManagerSetup
    {
        private const string DatabasePath = "Assets/Data/YeolhaDialogueDatabase.asset";
        private const string UiPrefabPath = "Assets/Game/2. Prefabs/UI/Dialogue/Yeolha Dialogue UI.prefab";

        /// <summary>아군 자막 패널 번호. StandardDialogueUI의 Subtitle Panels 배열 인덱스(0=NPC, 1=PC, 2=아군).</summary>
        public const int AllyPanelNumber = 2;

        /// <summary>
        /// TextMesh Pro 지원(<c>TMP_PRESENT</c>)이 켜져 있는지.
        ///
        /// <b>대화창 UI 프리팹을 저장하기 전에 반드시 확인할 것.</b>
        /// 이 심볼이 꺼져 있으면 <c>UITextField</c>의 <c>m_textMeshProUGUI</c> 필드가 컴파일된 클래스에
        /// 아예 없다(<c>Common/Scripts/UI/UITextField.cs</c>가 통째로 <c>#if TMP_PRESENT</c>다).
        /// 그 상태에서 <c>PrefabUtility.SaveAsPrefabAsset</c>을 부르면 Unity가 프리팹을 재직렬화하면서
        /// **모르는 필드를 조용히 버린다** — 자막·이름의 TMP 참조가 전부 날아가고, 나중에 심볼을 켜도
        /// 참조가 없어 글자가 영영 안 나온다. 되돌리려면 버전 관리에서 프리팹을 복구해야 한다.
        /// </summary>
        public static bool TmpSupportEnabled
        {
            get
            {
#if TMP_PRESENT
                return true;
#else
                return false;
#endif
            }
        }

        [MenuItem("Tools/Aiara/대화 매니저 셋업 (열린 씬)", false, 22)]
        public static void SetupOpenScene()
        {
            var controller = Object.FindFirstObjectByType<DialogueSystemController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                EditorUtility.DisplayDialog("대화 매니저 셋업",
                    "열린 씬에서 Dialogue Manager를 찾지 못했습니다.\n\n" +
                    "Dialogue Manager가 올라가 있는 씬(Game 2)을 열고 다시 실행하세요.", "확인");
                return;
            }

            string report = Wire(controller, UiPrefabPath);

            string body = string.IsNullOrEmpty(report)
                ? "이미 전부 맞춰져 있습니다. 바꾼 것이 없습니다."
                : "아래 항목을 맞췄습니다. **씬을 저장해야 남습니다.**\n\n" + report;

            EditorUtility.DisplayDialog("대화 매니저 셋업", TmpSupportWarning() + body, "확인");
        }

        /// <summary>
        /// TextMesh Pro 지원(<c>TMP_PRESENT</c> 심볼)이 꺼져 있으면 경고 문구를 돌려준다.
        ///
        /// 이게 없으면 <c>UITextField</c>의 TMP 필드가 통째로 컴파일에서 빠진다
        /// (<c>Common/Scripts/UI/UITextField.cs</c>가 전부 <c>#if TMP_PRESENT</c>로 감싸여 있다).
        /// 그러면 자막을 써넣는 <c>text</c> setter가 <c>uiText</c>(=비어 있음)만 보고 **조용히 아무것도 안 한다**.
        /// 인스펙터에 TMP가 멀쩡히 물려 있어도 소용없다 — 직렬화된 값만 남고 코드가 그 필드를 모른다.
        ///
        /// 증상이 아주 헷갈린다: 대화창·초상화·레이아웃은 전부 정상인데 <b>글자만 프리팹에 저장된
        /// 자리표시자 그대로 남는다</b>. 에러도 경고도 안 난다.
        /// 이 프로젝트 대화창은 전부 TMP 기반이라 필수 심볼이다.
        /// </summary>
        private static string TmpSupportWarning()
        {
#if TMP_PRESENT
            return string.Empty;
#else
            return "⚠ TextMesh Pro 지원이 꺼져 있습니다 (TMP_PRESENT 심볼 없음).\n\n" +
                   "이 상태에서는 자막 글자가 절대 안 바뀝니다 — UITextField의 TMP 필드가\n" +
                   "컴파일에서 빠져 있어 대사를 써넣어도 조용히 무시됩니다.\n\n" +
                   "메뉴 Tools/Pixel Crushers/Dialogue System/Tools/\n" +
                   "        Enable TextMesh Pro Support... 를 먼저 실행하세요.\n" +
                   "(심볼 추가 후 스크립트가 다시 컴파일됩니다.)\n\n" +
                   "────────────────────────────\n\n";
#endif
        }

        /// <summary>
        /// Dialogue Manager를 시트 규약에 맞게 배선한다.
        /// </summary>
        /// <param name="uiPrefabPath">물려줄 대화창 UI 프리팹. null이면 UI 참조는 건드리지 않는다.</param>
        /// <returns>바꾼 항목을 줄바꿈으로 이어붙인 보고서. 바꾼 게 없으면 빈 문자열.</returns>
        public static string Wire(DialogueSystemController controller, string uiPrefabPath)
        {
            var report = new System.Text.StringBuilder();

            void Note(string line)
            {
                report.Append("• ").Append(line).Append('\n');
                Debug.Log($"[대화 매니저 셋업] {line}", controller);
            }

#if !TMP_PRESENT
            // 콘솔에도 남긴다 — 대화창 UI 셋업 쪽에서 불려올 때는 위 다이얼로그가 뜨지 않는다.
            Debug.LogError("[대화 매니저 셋업] TMP_PRESENT 심볼이 없어 자막 글자가 바뀌지 않습니다. " +
                           "Tools/Pixel Crushers/Dialogue System/Tools/Enable TextMesh Pro Support... 를 실행하세요.");
#endif

            Undo.RecordObject(controller, "대화 매니저 셋업");

            if (!string.IsNullOrEmpty(uiPrefabPath))
            {
                var uiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(uiPrefabPath);
                if (uiPrefab == null)
                {
                    Debug.LogWarning($"[대화 매니저 셋업] 대화창 UI 프리팹을 찾지 못했습니다: {uiPrefabPath} " +
                                     "(Tools/Aiara/대화창 UI 셋업을 먼저 실행하세요)");
                }
                else if (controller.displaySettings.dialogueUI != uiPrefab)
                {
                    controller.displaySettings.dialogueUI = uiPrefab;
                    Note($"Dialogue UI → {uiPrefabPath}");
                }
            }

            // 플레이어 대사도 자막으로 띄운다. PC 기본값이 false라 이걸 안 켜면 JangHyu 대사만 통째로 안 나온다.
            DisplaySettings.SubtitleSettings subtitles = controller.displaySettings.subtitleSettings;
            if (!subtitles.showPCSubtitlesDuringLine)
            {
                subtitles.showPCSubtitlesDuringLine = true;
                Note("Subtitle Settings → Show PC Subtitles During Line = true (플레이어 대사가 자막으로 나옴)");
            }

            // Always여야 PC가 진행 입력을 기다린다. Never면 시간이 지나면 저절로 넘어간다.
            if (subtitles.continueButton != DisplaySettings.SubtitleSettings.ContinueButtonMode.Always)
            {
                subtitles.continueButton = DisplaySettings.SubtitleSettings.ContinueButtonMode.Always;
                Note("Subtitle Settings → Continue Button = Always (DialogueContinueInput으로 넘김)");
            }

            // 플레이어 대사는 시트상 NPC 대사의 자식(=response)이라, 이게 true면 **선택지가 하나뿐이어도**
            // 응답 메뉴(Response Menu Panel)로 나간다. 그래서 평범한 플레이어 대사가 자막 대신 선택지로 뜬다.
            // false면 응답이 하나일 때는 자동 선택돼 PC 자막 패널로 나오고, 진짜 선택지일 때만 메뉴가 뜬다.
            if (controller.displaySettings.inputSettings.alwaysForceResponseMenu)
            {
                controller.displaySettings.inputSettings.alwaysForceResponseMenu = false;
                Note("Input Settings → Always Force Response Menu = false (평범한 대사에 선택지 창이 안 뜸)");
            }

            if (controller.initialDatabase == null)
            {
                var database = AssetDatabase.LoadAssetAtPath<DialogueDatabase>(DatabasePath);
                if (database == null)
                {
                    Debug.LogWarning($"[대화 매니저 셋업] 대화 DB를 찾지 못했습니다: {DatabasePath} " +
                                     "(Tools/Aiara/대화 데이터 임포트를 먼저 실행하세요)");
                }
                else
                {
                    controller.initialDatabase = database;
                    Note($"Initial Database → {DatabasePath}");
                }
            }

            // 진행 입력 다리. 이게 없으면 Continue Button = Always 상태에서 대사가 영영 안 넘어간다.
            if (controller.GetComponent<DialogueContinueInput>() == null)
            {
                Undo.AddComponent<DialogueContinueInput>(controller.gameObject);
                Note("DialogueContinueInput 컴포넌트를 붙였습니다 (대사 넘기기 입력)");
            }

            // 아군 대사를 아군 패널로 보낸다. 없으면 아군(Jiji)도 NPC 창으로 나온다.
            var speakerPanels = controller.GetComponent<DialogueSpeakerPanels>();
            if (speakerPanels == null)
            {
                speakerPanels = Undo.AddComponent<DialogueSpeakerPanels>(controller.gameObject);
                Note($"DialogueSpeakerPanels 컴포넌트를 붙였습니다 (아군 → {AllyPanelNumber}번 패널)");
            }

            if (speakerPanels.AllyPanelNumber != AllyPanelNumber)
            {
                speakerPanels.AllyPanelNumber = AllyPanelNumber;
            }

            EditorUtility.SetDirty(speakerPanels);

            // 대화 중 HUD 감추기. 컴포넌트만 있고 아무 데도 안 붙어 있어서 그동안 동작하지 않았다.
            if (controller.GetComponent<DialogueGameUiHider>() == null)
            {
                Undo.AddComponent<DialogueGameUiHider>(controller.gameObject);
                Note("DialogueGameUiHider 컴포넌트를 붙였습니다 (대화 중 게임 UI 숨김)");
            }

            // 대사 안의 (고유명사)·[기술명] 표기에 글자 효과를 입힌다.
            // 흔들림용 DialogueTextShaker는 이 컴포넌트가 대화 시작 때 자막 패널에 알아서 붙인다.
            if (controller.GetComponent<DialogueTextMarkup>() == null)
            {
                Undo.AddComponent<DialogueTextMarkup>(controller.gameObject);
                Note("DialogueTextMarkup 컴포넌트를 붙였습니다 (괄호=흔들림, 대괄호=강조)");
            }

            // 시작 퀘스트 지정 자리. 목록이 비어 있으면 아무것도 하지 않으므로 붙여둬도 안전하고,
            // 붙어 있어야 인스펙터에서 눈에 띈다 — 임포터가 만드는 퀘스트는 전부 unassigned로 시작해서
            // 뭐라도 켜주지 않으면 조건이 걸린 이벤트가 하나도 안 열린다.
            if (controller.GetComponent<QuestInitializer>() == null)
            {
                Undo.AddComponent<QuestInitializer>(controller.gameObject);
                Note("QuestInitializer 컴포넌트를 붙였습니다 (시작 시 활성/완료 퀘스트 지정)");
            }

            EditorUtility.SetDirty(controller);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);

            RepairEventSystems(Note);
            CloseSubtitlePanelsAtStart(uiPrefabPath, Note);

            return report.ToString();
        }

        /// <summary>
        /// 대화창 UI 프리팹의 자막·선택지 패널이 <b>시작하자마자 열려 있지 않도록</b> 맞춘다.
        ///
        /// <see cref="UIPanel.StartState.GameObjectState"/>(기본값)는 "GameObject가 켜져 있으면 열린 것으로 친다"는
        /// 뜻이다. 패널을 꾸미려면 에디터에서 켜 둘 수밖에 없는데, 그 상태로 두면
        /// <c>UIPanel.Start()</c>가 패널을 곧바로 Opening으로 올려버린다. 그러면 대화가 시작되기도 전에
        /// 패널이 <b>프리팹에 저장된 자리표시자 글자("이름"/"텍스트")를 띄운 채로</b> 화면에 떠 있고,
        /// Pixel Crushers는 자기가 내용을 써넣은 패널만 닫기 때문에 그 자리표시자가 끝까지 남는다.
        /// (증상: 대화창은 멀쩡히 나오는데 대사가 안 바뀐다)
        ///
        /// 그래서 <see cref="UIPanel.StartState.Closed"/>로 못박는다. 에디터에서는 켜 둔 채로 꾸미고,
        /// 실행할 때만 닫힌 채 시작한다. 자막이 나올 때 <c>Open()</c>이 다시 켜준다.
        /// (스톡 템플릿이 자막 패널을 아예 비활성으로 배포하는 것도 같은 이유다.)
        ///
        /// 컨테이너인 <c>Dialogue Panel</c>은 건드리지 않는다 — 그건 StandardDialogueUI가 직접 여닫는다.
        /// </summary>
        private static void CloseSubtitlePanelsAtStart(string uiPrefabPath, System.Action<string> note)
        {
            if (string.IsNullOrEmpty(uiPrefabPath)
                || AssetDatabase.LoadAssetAtPath<GameObject>(uiPrefabPath) == null)
            {
                return;
            }

            if (!TmpSupportEnabled)
            {
                // TMP_PRESENT가 꺼진 채로 저장하면 안 된다 — 아래 "왜 위험한가"를 볼 것.
                Debug.LogError("[대화 매니저 셋업] TMP_PRESENT가 꺼져 있어 대화창 UI 프리팹을 건드리지 않았습니다. " +
                               "이 상태로 저장하면 프리팹의 TMP 참조가 전부 지워집니다. " +
                               "TextMesh Pro 지원을 먼저 켜세요.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(uiPrefabPath);
            try
            {
                var fixedPanels = new System.Collections.Generic.List<string>();

                void Close(UIPanel panel)
                {
                    if (panel == null || panel.startState == UIPanel.StartState.Closed)
                    {
                        return;
                    }

                    panel.startState = UIPanel.StartState.Closed;
                    fixedPanels.Add(panel.name);
                }

                foreach (var panel in root.GetComponentsInChildren<StandardUISubtitlePanel>(true))
                {
                    Close(panel);
                }

                foreach (var panel in root.GetComponentsInChildren<StandardUIMenuPanel>(true))
                {
                    Close(panel);
                }

                if (fixedPanels.Count == 0)
                {
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, uiPrefabPath);
                note($"대화창 UI의 패널 {fixedPanels.Count}개를 Start State = Closed로 바꿨습니다 " +
                     $"({string.Join(", ", fixedPanels)}) — 시작하자마자 자리표시자 글자가 뜨던 원인입니다");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// 씬의 EventSystem을 새 Input System용 모듈로 갈아끼운다.
        ///
        /// 이 프로젝트는 Active Input Handling이 <b>Input System Package (New)</b>다.
        /// 그 상태에서 구형 <c>StandaloneInputModule</c>은 <c>UnityEngine.Input</c>을 읽으려다
        /// <c>InvalidOperationException</c>을 <b>매 프레임</b> 던지고, 그 예외가
        /// <c>EventSystem.Update()</c>를 <c>m_CurrentInputModule.Process()</c> 앞에서 끊어버린다.
        /// 결과적으로 UI 입력이 통째로 죽는다 — 선택지 버튼 클릭도, Submit도 들어오지 않는다.
        /// </summary>
        private static void RepairEventSystems(System.Action<string> note)
        {
#if ENABLE_INPUT_SYSTEM
            var eventSystems = Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var eventSystem in eventSystems)
            {
                var legacy = eventSystem.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                if (legacy == null)
                {
                    continue;
                }

                GameObject host = eventSystem.gameObject;
                Undo.DestroyObjectImmediate(legacy);

                var module = host.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                if (module == null)
                {
                    module = Undo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>(host);
                }

                // 스크립트로 붙이면 액션이 비어 있어 모듈이 아무것도 못 읽는다. 기본 액션을 물려준다.
                if (module.actionsAsset == null)
                {
                    module.AssignDefaultActions();
                }

                EditorUtility.SetDirty(module);
                EditorSceneManager.MarkSceneDirty(host.scene);

                note($"'{host.name}'의 StandaloneInputModule을 InputSystemUIInputModule로 교체했습니다 " +
                     "(구형 모듈이 매 프레임 예외를 던져 UI 입력이 죽어 있었습니다)");
            }
#endif
        }
    }
}
