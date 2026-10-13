using System.IO;
using PixelCrushers.DialogueSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Aiara.Dialogue.EditorTools
{
    /// <summary>
    /// 자막 패널마다 우측 하단에 진행 화살표를 만들어 붙인다.
    /// 메뉴: <b>Tools/Aiara/대화 진행 화살표 셋업</b>
    ///
    /// <code>
    /// NPC/PC/아군 Subtitle Panel   (DialogueContinueArrow)
    /// └ Continue Arrow             (평소에는 꺼져 있고, 대사가 다 나오면 깜빡인다)
    /// </code>
    ///
    /// 화살표 그림은 프로젝트에 없어서 <b>여기서 만들어 저장한다</b>(<see cref="ArrowSpritePath"/>) —
    /// 아래를 가리키는 검은 삼각형이다. <b>이미 있으면 다시 만들지 않는다</b>: 전용 아트로 갈아끼운 것을
    /// 덮어쓰지 않기 위해서다. 다시 만들고 싶으면 그 png를 지우고 메뉴를 다시 실행하면 된다.
    /// 색만 바꿀 거라면 각 화살표 Image의 Color를 쓰는 편이 빠르다(검은 그림이라 더 밝게는 못 간다).
    ///
    /// 여러 번 눌러도 안전하다 — 이미 있으면 배선만 다시 맞추고 자리·생김새는 그대로 둔다.
    /// </summary>
    public static class DialogueContinueArrowSetup
    {
        private const string ArrowObjectName = "Continue Arrow";

        private const string ArrowFolder = "Assets/Game/4. Resources/UI/Dialogue";
        private const string ArrowSpritePath = ArrowFolder + "/ContinueArrow.png";

        /// <summary>만들어 둘 삼각형 그림 크기(픽셀).</summary>
        private const int ArrowWidth = 32;
        private const int ArrowHeight = 24;

        [MenuItem("Tools/Aiara/대화 진행 화살표 셋업", false, 30)]
        public static void SetupContinueArrow()
        {
            string prefabPath = DialogueUiSetup.OutputPath;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                EditorUtility.DisplayDialog("대화 진행 화살표 셋업",
                    $"대화창 UI 프리팹이 없습니다:\n{prefabPath}\n\n" +
                    "먼저 'Tools/Aiara/대화창 UI 셋업'을 실행하세요.", "확인");
                return;
            }

            Sprite arrowSprite = EnsureArrowSprite();

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                Debug.LogError($"[진행 화살표] 프리팹을 열지 못했습니다: {prefabPath}");
                return;
            }

            int created = 0;
            int rewired = 0;

            try
            {
                var panels = root.GetComponentsInChildren<StandardUISubtitlePanel>(true);
                if (panels.Length == 0)
                {
                    Debug.LogError("[진행 화살표] 프리팹에서 자막 패널을 찾지 못했습니다.");
                    return;
                }

                foreach (var panel in panels)
                {
                    Transform arrow = panel.transform.Find(ArrowObjectName);
                    bool isNew = arrow == null;

                    if (isNew)
                    {
                        arrow = BuildArrow(panel.transform, arrowSprite).transform;
                        created++;
                    }
                    else
                    {
                        rewired++;
                    }

                    var indicator = panel.GetComponent<DialogueContinueArrow>();
                    if (indicator == null)
                    {
                        indicator = panel.gameObject.AddComponent<DialogueContinueArrow>();
                    }

                    indicator.Panel = panel;
                    indicator.Arrow = arrow.gameObject;

                    // 조건이 맞을 때만 켜진다. 켠 채로 저장하면 대사를 찍는 중에도 떠 있다.
                    arrow.gameObject.SetActive(false);
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log($"[진행 화살표] 셋업 완료 — 새로 만든 패널 {created}개, 배선만 맞춘 패널 {rewired}개.\n" +
                      "대사가 다 나오고 '지금 넘길 수 있을 때'만 위아래로 까딱입니다 " +
                      "(타자기 진행 중·선택지 메뉴·대화 로그창이 열린 동안에는 뜨지 않습니다).\n" +
                      $"자리와 크기는 각 자막 패널의 '{ArrowObjectName}'에서, 색은 그 Image의 Color에서 바꾸세요.", prefab);
        }

        /// <summary>패널 우측 하단에 화살표 이미지를 만든다.</summary>
        private static GameObject BuildArrow(Transform panel, Sprite sprite)
        {
            var arrow = new GameObject(ArrowObjectName, typeof(RectTransform));
            arrow.transform.SetParent(panel, false);

            var rect = (RectTransform)arrow.transform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(ArrowWidth, ArrowHeight);
            rect.anchoredPosition = new Vector2(-16f, 14f);

            var image = arrow.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;

            // 누를 수 있는 버튼이 아니라 표시다.
            arrow.AddComponent<CanvasGroup>().blocksRaycasts = false;

            return arrow;
        }

        /// <summary>화살표 스프라이트를 확인하고, 없으면 만들어 저장한다.</summary>
        private static Sprite EnsureArrowSprite()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(ArrowSpritePath);
            if (existing != null)
            {
                return existing;
            }

            EnsureFolder(ArrowFolder);

            var texture = new Texture2D(ArrowWidth, ArrowHeight, TextureFormat.RGBA32, false);
            var pixels = new Color32[ArrowWidth * ArrowHeight];

            for (int y = 0; y < ArrowHeight; y++)
            {
                // 위가 넓고 아래로 갈수록 좁아지는 삼각형. y=0이 아래(꼭짓점)다.
                float t = (float)y / (ArrowHeight - 1);
                float half = Mathf.Lerp(0.5f, ArrowWidth * 0.5f, t);
                float center = ArrowWidth * 0.5f;

                for (int x = 0; x < ArrowWidth; x++)
                {
                    // 가장자리 한 픽셀은 반투명하게 둬서 계단을 덜 보이게 한다.
                    float distance = Mathf.Abs(x + 0.5f - center);
                    float alpha = Mathf.Clamp01(half - distance);

                    pixels[y * ArrowWidth + x] = new Color32(0, 0, 0, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            File.WriteAllBytes(ArrowSpritePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(ArrowSpritePath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(ArrowSpritePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            Debug.Log($"[진행 화살표] 검은 삼각형 그림을 만들었습니다: {ArrowSpritePath}");
            return AssetDatabase.LoadAssetAtPath<Sprite>(ArrowSpritePath);
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
    }
}
