using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 자원 바 위젯 1개 (HP/FP/투혼 공용) — 구버전 BattleSpiritBarWidget/MMProgressBar 조합의 경량 이관.
    /// 값은 외부(PlayerHUD/WorldStatusBar/BossHUD)가 Push하고, 표현(채움/텍스트/마커/가득참 색)은 위젯이 담당.
    /// 값이 바뀐 프레임에만 갱신 → 정지 시 오버헤드 0 (구버전 GC-free 원칙 유지).
    /// </summary>
    public class ResourceBarWidget : MonoBehaviour
    {
        [Header("Bindings")]
        [Tooltip("채움 이미지 (Image.type=Filled 권장. Filled가 아니면 anchorMax.x 스케일로 처리)")]
        [SerializeField] private Image fillImage;
        [Tooltip("현재 절대 수치를 표시할 텍스트 (선택 — 투혼은 정수 표시)")]
        [SerializeField] private TMP_Text valueText;
        [Tooltip("리셋 예정 위치 세로 마커 (선택 — 투혼 전용. 바 루트의 자식이어야 함)")]
        [SerializeField] private RectTransform marker;

        [Header("Colors")]
        [Tooltip("기본 채움 색")]
        [SerializeField] private Color normalColor = Color.white;
        [Tooltip("가득 찼을 때 채움 색 (구버전 Full Material Swap 대응 — 색으로 단순화)")]
        [SerializeField] private Color fullColor = Color.white;
        [Tooltip("비었을 때(허주/가드브레이크) 채움 색")]
        [SerializeField] private Color emptyColor = Color.white;
        [Tooltip("가득/빔 상태 색 전환 사용 여부")]
        [SerializeField] private bool useStateColors = false;

        [Header("Format")]
        [Tooltip("수치 표시 포맷 (투혼은 정수 \"0\")")]
        [SerializeField] private string valueFormat = "0";

        private float _lastRatio = float.NaN;
        private float _lastValue = float.NaN;
        private float _lastMarker = float.NaN;

        public Image FillImage { get => fillImage; set => fillImage = value; }
        public TMP_Text ValueText { get => valueText; set => valueText = value; }
        public RectTransform Marker { get => marker; set => marker = value; }

        public void SetColors(Color normal, Color full, Color empty)
        {
            normalColor = normal; fullColor = full; emptyColor = empty;
            useStateColors = true;
            _lastRatio = float.NaN;
        }

        /// <summary>바 갱신. current/max 기준 비율 채움 + 텍스트 갱신.</summary>
        public void Push(float current, float max)
        {
            float ratio = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            if (!Mathf.Approximately(ratio, _lastRatio))
            {
                _lastRatio = ratio;
                if (fillImage != null)
                {
                    if (fillImage.type == Image.Type.Filled) fillImage.fillAmount = ratio;
                    else
                    {
                        var rt = fillImage.rectTransform;
                        var aMax = rt.anchorMax; aMax.x = ratio; rt.anchorMax = aMax;
                    }
                    if (useStateColors)
                        fillImage.color = ratio >= 0.999f ? fullColor : (ratio <= 0.0001f ? emptyColor : normalColor);
                }
            }
            if (valueText != null && !Mathf.Approximately(current, _lastValue))
            {
                _lastValue = current;
                valueText.text = current.ToString(valueFormat);
            }
        }

        /// <summary>리셋 마커 위치 갱신 (0~1 비율 — 구버전 anchor 방식 이식).</summary>
        public void SetMarkerRatio(float markerRatio)
        {
            if (marker == null) return;
            markerRatio = Mathf.Clamp01(markerRatio);
            if (Mathf.Approximately(markerRatio, _lastMarker)) return;
            _lastMarker = markerRatio;
            var aMin = marker.anchorMin; aMin.x = markerRatio; marker.anchorMin = aMin;
            var aMax = marker.anchorMax; aMax.x = markerRatio; marker.anchorMax = aMax;
            var pos = marker.anchoredPosition; pos.x = 0f; marker.anchoredPosition = pos;
        }
    }
}
