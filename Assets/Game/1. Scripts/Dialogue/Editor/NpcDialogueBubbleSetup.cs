using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Aiara.Dialogue.EditorTools
{
    /// <summary>
    /// NPC 머리 위 말풍선을 만들어 붙인다.
    /// 메뉴: <b>Tools/Aiara/NPC 말풍선 붙이기</b> / <b>… 떼기</b>
    ///
    /// 쓰는 법 — 대화 존이 붙은 NPC를 하이어라키에서 고르고 메뉴를 누른다. 여러 개를 한꺼번에 골라도 된다.
    /// 만들어지는 것:
    /// <code>
    /// NPC
    /// └ Dialogue Zone          (대화 존 — 여기에 붙인다)
    ///   └ Dialogue Bubble      (월드 공간 Canvas, 평소에는 꺼져 있음)
    ///     └ QuestBubble        (말풍선 그림)
    ///       └ QuestBubbleButton(말풍선 안의 버튼 아이콘)
    /// </code>
    /// 그리고 존 오브젝트에 <see cref="NpcDialogueBubble"/>을 붙여 위 오브젝트를 물려준다.
    ///
    /// <b>NPC 본체가 아니라 존에 붙이는 이유</b> — 본체는 애니메이션과 방향 전환으로 돌아가는 물건이라,
    /// 그 밑에 두면 말풍선 자리가 같이 흔들린다. 존은 NPC 밑에 가만히 있는 빈 오브젝트라 흔들림이 없다.
    ///
    /// 처음 놓는 자리는 <see cref="NpcDialogueBubble.DefaultOffset"/>이다. 그대로 두면 그 자리가 유지되고,
    /// 씬에서 옮기면 옮긴 자리를 쓴다 — 런타임에 다시 옮기지 않는다.
    ///
    /// 여러 번 눌러도 안전하다 — 이미 있으면 배선만 다시 맞추고 생김새는 그대로 둔다.
    /// </summary>
    public static class NpcDialogueBubbleSetup
    {
        private const string BubbleName = "Dialogue Bubble";
        private const string BackgroundName = "QuestBubble";
        private const string ButtonName = "QuestBubbleButton";

        private const string ArtFolder =
            "Assets/Plugins/Pixel Crushers/Dialogue System/Prefabs/Art/Textures/Basic/";

        /// <summary>말풍선 그림. 없으면 유니티 기본 UI 스프라이트로 대신한다.</summary>
        private const string BubbleSpritePath = ArtFolder + "QuestBubble.png";

        /// <summary>말풍선 안에 얹는 버튼 아이콘.</summary>
        private const string ButtonSpritePath = ArtFolder + "QuestBubbleButtonX.png";

        /// <summary>
        /// 월드 공간 UI의 픽셀 → 미터 배율. 말풍선 원본이 224x134px이라 이 값이면 화면에서 1.1m쯤 된다 —
        /// 사람 키에 얹기 좋은 크기다. 크기는 말풍선 오브젝트의 Scale에서 바로 조절하면 된다.
        /// </summary>
        private const float WorldScale = 0.005f;

        [MenuItem("Tools/Aiara/NPC 말풍선 붙이기", false, 28)]
        public static void AttachBubbles()
        {
            GameObject[] selection = Selection.gameObjects;
            if (selection == null || selection.Length == 0)
            {
                Debug.LogWarning("[NPC 말풍선] 대화 존이 붙은 NPC를 하이어라키에서 먼저 고르세요.");
                return;
            }

            int touched = 0;

            foreach (GameObject target in selection)
            {
                if (!IsEditable(target))
                {
                    continue;
                }

                NpcDialogueZone zone = target.GetComponentInChildren<NpcDialogueZone>(true)
                                       ?? target.GetComponentInParent<NpcDialogueZone>();

                if (zone == null)
                {
                    Debug.LogWarning($"[NPC 말풍선] '{target.name}'에서 NpcDialogueZone을 찾지 못했습니다. " +
                                     "대화 존이 붙은 NPC를 고르세요.", target);
                    continue;
                }

                // 말풍선은 존 오브젝트에 붙인다. NPC 본체는 애니메이션·방향 전환으로 돌아가기 때문에
                // 그 밑에 두면 말풍선 자리가 같이 흔들린다.
                GameObject host = zone.gameObject;

                // 예전 버전이 본체에 붙여둔 말풍선이 남아 있으면 두 개가 된다. 먼저 떼라고 알려준다.
                NpcDialogueBubble stray = FindStrayBubble(zone, host);
                if (stray != null)
                {
                    Debug.LogWarning($"[NPC 말풍선] '{stray.name}'에 이전에 붙인 말풍선이 있습니다. " +
                                     "'Tools/Aiara/NPC 말풍선 떼기'로 먼저 지운 뒤 다시 붙이세요.", stray);
                    continue;
                }

                var bubble = host.GetComponent<NpcDialogueBubble>();
                if (bubble == null)
                {
                    bubble = Undo.AddComponent<NpcDialogueBubble>(host);
                }

                Transform existing = host.transform.Find(BubbleName);
                bool created = existing == null;

                if (created)
                {
                    existing = BuildBubble(host.transform).transform;
                    Undo.RegisterCreatedObjectUndo(existing.gameObject, "NPC 말풍선 붙이기");
                }

                Undo.RecordObject(bubble, "NPC 말풍선 붙이기");
                bubble.Zone = zone;
                bubble.Bubble = existing.gameObject;

                if (created)
                {
                    // 처음 놓아주는 자리. 이후에는 씬에서 옮긴 자리를 그대로 지킨다
                    // (Anchor를 지정하지 않는 한 런타임에 옮기지 않는다).
                    bubble.Offset = NpcDialogueBubble.DefaultOffset;
                    existing.localPosition = bubble.Offset;
                }

                // 평소에는 꺼져 있어야 한다 — 켠 채로 저장하면 조건과 무관하게 처음부터 떠 있다.
                existing.gameObject.SetActive(false);

                EditorUtility.SetDirty(bubble);
                MarkDirty(host);
                touched++;

                Debug.Log(created
                    ? $"[NPC 말풍선] '{host.name}'에 말풍선을 만들었습니다 (자리 {bubble.Offset})."
                    : $"[NPC 말풍선] '{host.name}'의 말풍선 배선만 다시 맞췄습니다.", bubble);
            }

            if (touched > 0)
            {
                Debug.Log($"[NPC 말풍선] {touched}개 처리했습니다. 프리팹 안에서 작업했다면 프리팹을 저장하세요.\n" +
                          "조건이 맞는 대화가 있을 때만 뜹니다 — 게이트(퀘스트 상태), 남은 사용 횟수, " +
                          "시트의 대화 시작 조건을 존이 그대로 판정합니다.");
            }
        }

        [MenuItem("Tools/Aiara/NPC 말풍선 떼기", false, 29)]
        public static void DetachBubbles()
        {
            GameObject[] selection = Selection.gameObjects;
            if (selection == null || selection.Length == 0)
            {
                Debug.LogWarning("[NPC 말풍선] 말풍선을 뗄 NPC를 먼저 고르세요.");
                return;
            }

            foreach (GameObject target in selection)
            {
                if (!IsEditable(target))
                {
                    continue;
                }

                foreach (var bubble in CollectBubbles(target))
                {
                    if (bubble.Bubble != null)
                    {
                        Undo.DestroyObjectImmediate(bubble.Bubble);
                    }

                    GameObject host = bubble.gameObject;
                    Undo.DestroyObjectImmediate(bubble);
                    MarkDirty(host);

                    Debug.Log($"[NPC 말풍선] '{host.name}'에서 말풍선을 뗐습니다.", host);
                }
            }
        }

        /// <summary>존이 아닌 다른 오브젝트(예전에 쓰던 NPC 본체)에 붙어 있는 말풍선을 찾는다.</summary>
        private static NpcDialogueBubble FindStrayBubble(NpcDialogueZone zone, GameObject host)
        {
            Transform npcRoot = zone.ConversantTransform != null ? zone.ConversantTransform : zone.transform;

            foreach (var bubble in npcRoot.GetComponentsInChildren<NpcDialogueBubble>(true))
            {
                if (bubble != null && bubble.gameObject != host)
                {
                    return bubble;
                }
            }

            return null;
        }

        private static List<NpcDialogueBubble> CollectBubbles(GameObject target)
        {
            var list = new List<NpcDialogueBubble>(target.GetComponentsInChildren<NpcDialogueBubble>(true));

            var fromParent = target.GetComponentInParent<NpcDialogueBubble>();
            if (fromParent != null && !list.Contains(fromParent))
            {
                list.Add(fromParent);
            }

            return list;
        }

        /// <summary>월드 공간 캔버스 + 말풍선 그림 + 그 안의 버튼 아이콘을 만든다.</summary>
        private static GameObject BuildBubble(Transform parent)
        {
            var root = new GameObject(BubbleName, typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(parent, false);

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rect = (RectTransform)root.transform;
            rect.localScale = new Vector3(WorldScale, WorldScale, WorldScale);

            GameObject background = CreateChild(BackgroundName, root.transform);
            var image = background.AddComponent<Image>();
            image.sprite = LoadSprite(BubbleSpritePath, "말풍선");
            image.raycastTarget = false;

            // 원본 비율 그대로 쓴다 — 늘려 놓으면 꼬리가 찌그러진다.
            var backgroundRect = (RectTransform)background.transform;
            SetNativeSize(image, backgroundRect, new Vector2(224f, 134f));
            backgroundRect.anchoredPosition = Vector2.zero;

            // 캔버스는 말풍선 크기에 맞춘다. 화면에서의 크기는 위 Scale이 정한다.
            rect.sizeDelta = backgroundRect.sizeDelta;

            GameObject button = CreateChild(ButtonName, background.transform);
            var buttonImage = button.AddComponent<Image>();
            buttonImage.sprite = LoadSprite(ButtonSpritePath, "버튼 아이콘");
            buttonImage.raycastTarget = false;

            var buttonRect = (RectTransform)button.transform;
            SetNativeSize(buttonImage, buttonRect, new Vector2(53f, 56f));

            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(64f, 6f);

            return root;
        }

        /// <summary>스프라이트를 불러온다. 없으면 경고를 남기고 기본 UI 그림으로 대신한다.</summary>
        private static Sprite LoadSprite(string path, string what)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null)
            {
                return sprite;
            }

            Debug.LogWarning($"[NPC 말풍선] {what} 스프라이트를 찾지 못해 기본 UI 그림으로 대신합니다: {path}");
            return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        }

        /// <summary>
        /// 스프라이트 원본 픽셀 크기로 맞춘다. 스프라이트를 못 불러온 경우를 대비해
        /// 원본 크기를 기본값으로 받아둔다(<see cref="Image.SetNativeSize"/>는 대체 그림 크기를 쓴다).
        /// </summary>
        private static void SetNativeSize(Image image, RectTransform rect, Vector2 fallback)
        {
            if (image.sprite != null)
            {
                image.SetNativeSize();
                return;
            }

            rect.sizeDelta = fallback;
        }

        private static GameObject CreateChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>프로젝트 창의 프리팹 애셋은 거른다 — 붙여도 Undo가 걸리지 않고 저장 시점이 어긋난다.</summary>
        private static bool IsEditable(GameObject target)
        {
            if (target == null)
            {
                return false;
            }

            if (PrefabUtility.IsPartOfPrefabAsset(target))
            {
                Debug.LogWarning($"[NPC 말풍선] '{target.name}'은(는) 프로젝트 창의 프리팹 애셋입니다. " +
                                 "프리팹을 열고(더블클릭) 그 안에서 고른 뒤 다시 실행하세요.", target);
                return false;
            }

            return true;
        }

        private static void MarkDirty(GameObject go)
        {
            var stage = PrefabStageUtility.GetPrefabStage(go);
            if (stage != null)
            {
                EditorSceneManager.MarkSceneDirty(stage.scene);
                return;
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
