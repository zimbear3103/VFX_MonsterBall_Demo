using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 런타임 기본 HUD 조립 헬퍼 — 아트 리소스 없이 동작하는 기본 바를 코드로 조립한다.
    /// PlayerHUD/WorldStatusBar/BossHUD가 인스펙터 참조가 비어 있을 때만 사용(아트 교체 시 참조 할당으로 대체).
    /// </summary>
    public static class HUDBuilder
    {
        /// <summary>Screen Space Overlay Canvas 생성 (1920x1080 기준 스케일).</summary>
        public static Canvas CreateOverlayCanvas(Transform parent, string name, int sortingOrder = 10)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        /// <summary>단색 Image 사각형 생성.</summary>
        public static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// 배경 + 채움(왼쪽 기준 anchorMax 스케일) 바 위젯 생성.
        /// anchoredPos는 부모 기준, pivot/anchor는 호출부가 SetAnchors로 조정.
        /// </summary>
        public static ResourceBarWidget CreateBar(Transform parent, string name, Vector2 size, Vector2 anchoredPos,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Color backColor, Color fillColor, bool withText = false, bool withMarker = false)
        {
            var back = CreateImage(parent, name, backColor);
            var backRt = back.rectTransform;
            backRt.anchorMin = anchorMin; backRt.anchorMax = anchorMax; backRt.pivot = pivot;
            backRt.sizeDelta = size; backRt.anchoredPosition = anchoredPos;

            var fill = CreateImage(back.transform, "Fill", fillColor);
            var fillRt = fill.rectTransform;
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = new Vector2(1f, 1f);
            fillRt.offsetMin = new Vector2(1f, 1f); fillRt.offsetMax = new Vector2(-1f, -1f);
            fillRt.pivot = new Vector2(0f, 0.5f);

            var widget = back.gameObject.AddComponent<ResourceBarWidget>();
            widget.FillImage = fill;

            if (withMarker)
            {
                var marker = CreateImage(back.transform, "Marker", new Color(1f, 1f, 1f, 0.9f));
                var mRt = marker.rectTransform;
                mRt.anchorMin = new Vector2(0.5f, 0f); mRt.anchorMax = new Vector2(0.5f, 1f);
                mRt.pivot = new Vector2(0.5f, 0.5f);
                mRt.sizeDelta = new Vector2(2f, 0f);
                mRt.anchoredPosition = Vector2.zero;
                widget.Marker = mRt;
            }

            if (withText)
            {
                var textGo = new GameObject("Value", typeof(RectTransform), typeof(TextMeshProUGUI));
                textGo.transform.SetParent(back.transform, false);
                var text = textGo.GetComponent<TextMeshProUGUI>();
                text.fontSize = size.y * 0.9f;
                text.alignment = TextAlignmentOptions.MidlineRight;
                text.color = Color.white;
                text.raycastTarget = false;
                var tRt = text.rectTransform;
                tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
                tRt.offsetMin = Vector2.zero; tRt.offsetMax = new Vector2(-4f, 0f);
                widget.ValueText = text;
            }

            return widget;
        }
    }
}
