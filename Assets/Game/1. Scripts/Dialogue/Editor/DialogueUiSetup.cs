using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Aiara.Dialogue.EditorTools
{
    /// <summary>
    /// 대화창 UI를 프로젝트 소유 프리팹으로 뽑고, **PC(플레이어) 패널을 NPC 패널과 같은 창**으로 맞춘다.
    /// 메뉴: <b>Tools/Aiara/대화창 UI 셋업</b>
    ///
    /// 왜 필요한가 —
    /// Pixel Crushers는 <c>actor.IsPlayer</c>에 따라 NPC 패널과 PC 패널을 따로 고른다. 스톡 템플릿은
    /// 두 패널이 서로 다른 오브젝트라 생김새가 갈린다. 그래서 PC 패널을 지우고 **NPC 패널을 복제**해
    /// 그 자리에 넣는다. 복제본은 Instantiate가 내부 참조(Portrait Image/Portrait Name/Subtitle Text)를
    /// 자동으로 자기 자식으로 다시 걸어주기 때문에, 배선이 그대로 살아 있는 채로 생김새만 같아진다.
    /// 차이는 화면 위/아래뿐 — NPC는 상단(원래 자리), PC 복제본은 하단 앵커로 내려둔다.
    ///
    /// 프리팹은 <c>Assets/Plugins</c>가 아니라 <c>Assets/Game/2. Prefabs/UI/Dialogue</c>로 복사해서 쓴다.
    /// 플러그인 폴더를 직접 고치면 Pixel Crushers를 업데이트할 때 통째로 덮어써진다.
    ///
    /// 소스 템플릿은 <b>TMPro 버전</b>이다. 한글은 TMP SDF 폰트여야 폰트를 지정해 쓸 수 있다.
    ///
    /// **아군 패널**(2번)도 같은 방식의 NPC 복제본으로 만든다. 아군은 IsPlayer가 아니라 액터의
    /// <c>SpeakerType</c> 필드로 갈리며, 런타임에 <see cref="DialogueSpeakerPanels"/>가 2번 패널로 돌린다.
    /// 이 메뉴가 그 컴포넌트도 Dialogue Manager에 붙여준다.
    ///
    /// 이미 프리팹이 있으면 복사는 건너뛰고 패널만 다시 맞춘다 — 즉 **여러 번 눌러도 안전**하지만,
    /// PC 패널은 매번 NPC 패널 복제본으로 교체되므로 PC 패널만 따로 꾸며놨다면 그건 날아간다.
    /// **아군 패널은 예외로, 이미 있으면 건드리지 않는다** (다르게 꾸미라고 만든 창이므로).
    /// </summary>
    public static class DialogueUiSetup
    {
        private const string TemplatePath =
            "Assets/Plugins/Pixel Crushers/Dialogue System/Prefabs/Standard UI Prefabs/Templates/Basic/Basic Standard Dialogue UI TMPro.prefab";

        private const string OutputFolder = "Assets/Game/2. Prefabs/UI/Dialogue";

        /// <summary>프로젝트가 쓰는 대화창 UI 프리팹. 다른 셋업 메뉴도 같은 것을 손댄다.</summary>
        public const string OutputPath = OutputFolder + "/Yeolha Dialogue UI.prefab";

        /// <summary>대화창에 쓰는 한글 TMP 폰트. 로그창도 같은 폰트를 쓴다.</summary>
        public const string KoreanFontPath =
            "Assets/Game/4. Resources/Pont/Noto_Sans_KR/NotoSansKR-VariableFont_wght SDF.asset";

        private const string PcPanelName = "PC Subtitle Panel";

        /// <summary>아군 전용 자막 패널. Subtitle Panels 배열의 2번(0=NPC, 1=PC, 2=아군).</summary>
        private const string AllyPanelName = "Ally Subtitle Panel";

        [MenuItem("Tools/Aiara/대화창 UI 셋업", false, 20)]
        public static void SetupDialogueUi()
        {
            string prefabPath = EnsureProjectUiPrefab();
            if (prefabPath == null)
            {
                return;
            }

            if (!RebuildPanels(prefabPath))
            {
                return;
            }

            WireSceneDialogueManager(prefabPath);

            AssetDatabase.SaveAssets();
            Debug.Log($"[대화창 셋업] 완료 — {prefabPath}\n" +
                      "자막 패널 3개: 0=NPC, 1=PC(하단), 2=아군. PC/아군 패널은 NPC 패널 복제본이라 " +
                      "생김새를 고치려면 세 패널을 같이 고치세요. 아군 패널은 겹침만 피하도록 자리를 밀어둔 상태입니다.");
        }

        /// <summary>플러그인 템플릿을 프로젝트 폴더로 복사한다. 이미 있으면 그대로 쓴다.</summary>
        private static string EnsureProjectUiPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(OutputPath) != null)
            {
                return OutputPath;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath) == null)
            {
                Debug.LogError($"[대화창 셋업] 템플릿을 찾지 못했습니다: {TemplatePath}");
                return null;
            }

            EnsureFolder(OutputFolder);

            if (!AssetDatabase.CopyAsset(TemplatePath, OutputPath))
            {
                Debug.LogError($"[대화창 셋업] 프리팹 복사에 실패했습니다: {OutputPath}");
                return null;
            }

            AssetDatabase.ImportAsset(OutputPath);
            Debug.Log($"[대화창 셋업] 템플릿을 복사했습니다 → {OutputPath}");
            return OutputPath;
        }

        private static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        /// <summary>PC 패널을 NPC 패널 복제본으로 갈아끼우고, 컨티뉴 버튼을 떼고, 한글 폰트를 입힌다.</summary>
        private static bool RebuildPanels(string prefabPath)
        {
            // TMP_PRESENT가 꺼진 채로 저장하면 프리팹의 TMP 참조가 전부 지워진다.
            // 이유는 DialogueManagerSetup.TmpSupportEnabled 참고.
            if (!DialogueManagerSetup.TmpSupportEnabled)
            {
                EditorUtility.DisplayDialog("대화창 UI 셋업",
                    "TextMesh Pro 지원(TMP_PRESENT)이 꺼져 있어 중단했습니다.\n\n" +
                    "이 상태로 프리팹을 저장하면 자막·이름의 TMP 참조가 전부 지워집니다.\n\n" +
                    "메뉴 Tools/Pixel Crushers/Dialogue System/Tools/\n" +
                    "        Enable TextMesh Pro Support... 를 먼저 실행하세요.", "확인");
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                Debug.LogError($"[대화창 셋업] 프리팹을 열지 못했습니다: {prefabPath}");
                return false;
            }

            try
            {
                var ui = root.GetComponentInChildren<StandardDialogueUI>(true);
                if (ui == null)
                {
                    Debug.LogError("[대화창 셋업] 프리팹에 StandardDialogueUI가 없습니다.");
                    return false;
                }

                var elements = ui.conversationUIElements;

                StandardUISubtitlePanel npcPanel = elements.defaultNPCSubtitlePanel;
                if (npcPanel == null && elements.subtitlePanels != null && elements.subtitlePanels.Length > 0)
                {
                    npcPanel = elements.subtitlePanels[0];
                }

                if (npcPanel == null)
                {
                    Debug.LogError("[대화창 셋업] NPC 자막 패널을 찾지 못했습니다. " +
                                   "StandardDialogueUI의 Default NPC Subtitle Panel을 먼저 물려주세요.");
                    return false;
                }

                // 기존 PC 패널이 있던 자리(부모/형제 순서)를 그대로 물려받아야 캔버스 그리기 순서가 안 바뀐다.
                Transform parent = npcPanel.transform.parent;
                int siblingIndex = -1;

                // **아군 패널은 이미 있으면 그대로 둔다.** PC 패널은 NPC와 같은 창이라는 게 규칙이라
                // 매번 복제본으로 덮어도 잃을 게 없지만, 아군 패널은 애초에 "다르게 꾸미려고" 만든 창이라
                // 셋업을 다시 돌릴 때마다 꾸민 게 날아가면 쓸 수가 없다.
                StandardUISubtitlePanel allyPanel = FindPanelByName(root, AllyPanelName);

                // 지금 물려 있는 PC 패널뿐 아니라 **이름만 같고 참조가 끊긴 고아 패널**까지 같이 치운다.
                // 예전 셋업이 defaultPCSubtitlePanel이 비어 있을 때 돌면 복제본만 남기고 참조를 새로 잡아서,
                // 하이라키에 쓰이지 않는 'PC Subtitle Panel'이 계속 쌓인다.
                foreach (var panel in root.GetComponentsInChildren<StandardUISubtitlePanel>(true))
                {
                    if (panel == null || panel == npcPanel || panel == allyPanel)
                    {
                        continue;
                    }

                    if (panel != elements.defaultPCSubtitlePanel && panel.name != PcPanelName)
                    {
                        continue;
                    }

                    if (siblingIndex < 0)
                    {
                        parent = panel.transform.parent;
                        siblingIndex = panel.transform.GetSiblingIndex();
                    }

                    Object.DestroyImmediate(panel.gameObject);
                }

                // Instantiate가 복제본 안쪽을 가리키는 참조들을 자동으로 재배선한다 —
                // 그래서 Portrait Image / Portrait Name / Subtitle Text를 손으로 다시 물릴 필요가 없다.
                StandardUISubtitlePanel pcPanel = ClonePanel(npcPanel, PcPanelName, parent, siblingIndex);
                MoveToBottom(pcPanel.transform as RectTransform, npcPanel.transform as RectTransform);

                // 아군 패널이 아직 없을 때만 만든다. 처음 만들 때는 NPC 복제본이고, NPC와 같은 변에 붙어
                // 있으면 겹치므로 한 칸 안쪽으로 밀어둔다 — 생김새·최종 위치는 프리팹에서 직접 꾸미면 된다.
                if (allyPanel == null)
                {
                    allyPanel = ClonePanel(npcPanel, AllyPanelName, parent, siblingIndex);
                    StackInward(allyPanel.transform as RectTransform, npcPanel.transform as RectTransform);
                    Debug.Log("[대화창 셋업] 아군 자막 패널(2번)을 새로 만들었습니다. " +
                              "생김새는 프리팹에서 꾸미세요 — 다음부터는 이 패널을 덮어쓰지 않습니다.");
                }

                // 배열 인덱스가 곧 패널 번호다. 아군 패널은 DialogueSpeakerPanels가 이 번호로 찾아간다.
                elements.subtitlePanels = new[] { npcPanel, pcPanel, allyPanel };
                elements.defaultNPCSubtitlePanel = npcPanel;
                elements.defaultPCSubtitlePanel = pcPanel;

                DetachContinueButton(npcPanel);
                DetachContinueButton(pcPanel);
                DetachContinueButton(allyPanel);

                FitPortraitAspect(npcPanel);
                FitPortraitAspect(pcPanel);
                FitPortraitAspect(allyPanel);

                ApplyKoreanFont(root);
                EnsurePanelFade(root);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// 페이드 속도 조절기를 대화창 루트에 달아준다. 이미 있으면 그대로 둔다(맞춰둔 값이 날아가지 않게).
        /// </summary>
        private static void EnsurePanelFade(GameObject root)
        {
            if (root.GetComponent<DialoguePanelFade>() == null)
            {
                root.AddComponent<DialoguePanelFade>();
            }
        }

        /// <summary>
        /// 대화창이 나타나고 사라지는 페이드 속도를 조절할 컴포넌트만 붙인다.
        /// 메뉴: <b>Tools/Aiara/대화창 페이드 설정</b>
        ///
        /// 대화창 UI 셋업 메뉴는 PC·아군 패널을 다시 만들기 때문에, 이미 꾸며둔 창이 있으면 함부로 누르기 어렵다.
        /// 페이드만 조절하고 싶을 때 쓰라고 따로 뺀 길이다 — 프리팹의 다른 것은 아무것도 건드리지 않는다.
        /// </summary>
        [MenuItem("Tools/Aiara/대화창 페이드 설정", false, 23)]
        public static void SetupPanelFade()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("대화창 페이드 설정",
                    $"대화창 UI 프리팹이 없습니다:\n{OutputPath}\n\n" +
                    "먼저 'Tools/Aiara/대화창 UI 셋업'을 실행하세요.", "확인");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(OutputPath);
            try
            {
                bool added = root.GetComponent<DialoguePanelFade>() == null;
                EnsurePanelFade(root);

                if (added)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, OutputPath);
                    AssetDatabase.SaveAssets();
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPath);
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log("[대화창 페이드] 대화창 프리팹 루트의 Dialogue Panel Fade에서 조절하세요.\n" +
                      "· Fade Speed: 1 = 원래 속도(0.5초), 2 = 두 배 빠르게, 0.5 = 두 배 느리게\n" +
                      "· Disable Fade: 켜면 즉시 나타나고 즉시 사라집니다\n" +
                      "플레이 중에 바꿔도 바로 반영됩니다.", prefab);
        }

        private static StandardUISubtitlePanel FindPanelByName(GameObject root, string name)
        {
            foreach (var panel in root.GetComponentsInChildren<StandardUISubtitlePanel>(true))
            {
                if (panel != null && panel.name == name)
                {
                    return panel;
                }
            }

            return null;
        }

        /// <summary>
        /// NPC 패널을 복제해 새 패널을 만든다. Instantiate가 복제본 안쪽을 가리키는 참조
        /// (Portrait Image / Portrait Name / Subtitle Text)를 자동으로 재배선해준다.
        /// </summary>
        private static StandardUISubtitlePanel ClonePanel(StandardUISubtitlePanel source, string name,
            Transform parent, int siblingIndex)
        {
            GameObject clone = Object.Instantiate(source.gameObject, parent);
            clone.name = name;

            if (siblingIndex >= 0)
            {
                clone.transform.SetSiblingIndex(siblingIndex);
            }

            return clone.GetComponent<StandardUISubtitlePanel>();
        }

        /// <summary>
        /// 아군 패널을 NPC 패널과 같은 앵커에 두되 화면 안쪽으로 한 칸 밀어 겹치지 않게 한다.
        /// (상단 앵커면 아래로, 하단 앵커면 위로.) 어디까지나 겹침 방지용 기본값이고,
        /// 최종 위치·생김새는 프리팹에서 직접 잡는 것을 전제로 한다.
        /// </summary>
        private static void StackInward(RectTransform rect, RectTransform npcRect)
        {
            if (rect == null || npcRect == null)
            {
                return;
            }

            float step = Mathf.Abs(npcRect.sizeDelta.y) + 8f;

            // 하단 앵커(0)면 위로, 그 외(상단·중앙)는 아래로 민다.
            bool anchoredToBottom = Mathf.Approximately(npcRect.anchorMin.y, 0f)
                                    && Mathf.Approximately(npcRect.anchorMax.y, 0f);

            float offset = anchoredToBottom ? step : -step;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y + offset);
        }

        /// <summary>
        /// 같은 여백을 유지한 채 PC 패널을 화면 아래쪽으로 내린다. NPC는 지금 자리(상단), PC는 하단.
        ///
        /// **NPC 패널도 하단 앵커면 그대로는 겹친다.** 예전에는 복제본을 무조건 상단으로 올렸는데,
        /// 원본 NPC 패널이 이미 상단 앵커라 아무것도 안 바뀌어 두 패널이 정확히 포개졌다.
        /// 그래서 여기서는 NPC 쪽 앵커를 보고, 같은 변(하단)에 붙어 있으면 NPC 높이만큼 더 띄운다.
        /// </summary>
        private static void MoveToBottom(RectTransform rect, RectTransform npcRect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(rect.anchorMin.x, 0f);
            rect.anchorMax = new Vector2(rect.anchorMax.x, 0f);
            rect.pivot = new Vector2(rect.pivot.x, 0f);

            float margin = Mathf.Abs(rect.anchoredPosition.y);

            if (npcRect != null &&
                Mathf.Approximately(npcRect.anchorMin.y, 0f) &&
                Mathf.Approximately(npcRect.anchorMax.y, 0f))
            {
                // NPC가 세로로 늘어나는 앵커면 sizeDelta.y가 높이가 아니지만, 이 경우 겹침을 피할 만큼만
                // 띄우면 되므로 근사치로 충분하다.
                margin += Mathf.Abs(npcRect.anchoredPosition.y) + Mathf.Abs(npcRect.sizeDelta.y);
            }

            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, margin);
        }

        /// <summary>
        /// 컨티뉴 버튼을 화면에서 치운다. 참조를 null로 두면 Pixel Crushers는 버튼을 아예 띄우지 않는다
        /// (<c>Tools.SetGameObjectActive</c>가 null을 그냥 무시한다). 진행은 DialogueContinueInput이 대신 받는다.
        /// 오브젝트 자체는 지우지 않고 비활성만 한다 — 나중에 다시 쓰고 싶을 수 있으므로.
        /// </summary>
        private static void DetachContinueButton(StandardUISubtitlePanel panel)
        {
            if (panel == null || panel.continueButton == null)
            {
                return;
            }

            panel.continueButton.gameObject.SetActive(false);
            panel.continueButton = null;
        }

        /// <summary>
        /// 초상화가 슬롯에 눌려 보이지 않도록 <see cref="DialoguePortraitAspectFitter"/>를 붙인다.
        /// 슬롯 크기(64x64)는 그대로 두고 그 안에서 원본 비율대로 맞춘다.
        ///
        /// Pixel Crushers의 <c>usePortraitNativeSize</c>는 같이 못 쓴다 — 그건 원본 픽셀 크기를 그대로
        /// sizeDelta에 박아서 대화창 밖으로 나가버린다. 켜져 있으면 꺼준다.
        /// </summary>
        private static void FitPortraitAspect(StandardUISubtitlePanel panel)
        {
            if (panel == null || panel.portraitImage == null)
            {
                return;
            }

            panel.usePortraitNativeSize = false;

            if (panel.portraitImage.GetComponent<DialoguePortraitAspectFitter>() == null)
            {
                panel.portraitImage.gameObject.AddComponent<DialoguePortraitAspectFitter>();
            }
        }

        private static void ApplyKoreanFont(GameObject root)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath);
            if (font == null)
            {
                Debug.LogWarning($"[대화창 셋업] 한글 폰트를 찾지 못해 폰트는 건드리지 않았습니다: {KoreanFontPath}");
                return;
            }

            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                text.font = font;
            }
        }

        /// <summary>
        /// 씬의 Dialogue Manager를 새 UI에 맞춰 물려준다.
        ///
        /// 실제 배선 규약은 <see cref="DialogueManagerSetup"/>에 있다 — 프리팹을 다시 만들지 않고
        /// 매니저만 고치고 싶을 때 쓰는 <b>Tools/Aiara/대화 매니저 셋업 (열린 씬)</b>과 같은 코드다.
        /// </summary>
        private static void WireSceneDialogueManager(string prefabPath)
        {
            var controller = Object.FindFirstObjectByType<DialogueSystemController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[대화창 셋업] 열린 씬에 Dialogue Manager가 없어 프리팹만 만들었습니다. " +
                                 "Dialogue Manager를 올린 씬을 열고 한 번 더 실행하거나, " +
                                 "Tools/Aiara/대화 매니저 셋업 (열린 씬)을 실행하세요.");
                return;
            }

            DialogueManagerSetup.Wire(controller, prefabPath);
        }
    }
}
