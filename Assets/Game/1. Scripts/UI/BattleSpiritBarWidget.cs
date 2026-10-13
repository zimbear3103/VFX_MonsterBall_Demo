using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MoreMountains.Tools;

namespace Aiara
{
    /// <summary>
    /// BattleSpirit(투혼) HUD 한 단위: MMProgressBar + 현재수치 텍스트 + 리셋 마커선.
    /// AiaraGUIManager 가 Push 로 값만 밀어넣고, 표현(텍스트 포맷/마커 위치)은 이 위젯이 담당한다.
    /// 원본 MMProgressBar 는 참조만 하며 수정하지 않는다.
    /// </summary>
    [AddComponentMenu("TopDown Engine/GUI/Battle Spirit Bar Widget")]
    public class BattleSpiritBarWidget : MonoBehaviour
    {
        [Header("Bindings")]
        [Tooltip("이 위젯이 갱신하는 progress bar (PlayerID 매칭의 단일 소스)")]
        public MMProgressBar Bar;

        [Tooltip("현재 투혼 수치를 표시할 TMP 텍스트 (투혼은 항상 정수)")]
        public TMP_Text CurrentValueText;

        [Tooltip("리셋 예정 위치를 나타내는 세로 마커의 RectTransform. " +
                 "fill 0~1 이 폭 전체에 매핑되는 고정폭 RectTransform(바 루트)의 자식이어야 하며, " +
                 "ForegroundBar 에 가려지지 않도록 형제 중 가장 마지막(맨 위)에 배치할 것.")]
        public RectTransform Marker;

        [Tooltip("투혼이 가득 찼을 때(ratio >= FullThreshold) 활성화할 오브젝트. " +
                 "예: 버스트 버튼 안내 UI(LR_BTN_Burst_ui). 가득 차지 않으면 자동으로 꺼진다. " +
                 "비워두면 아무 것도 하지 않는다.")]
        public GameObject FullIndicator;

        [Header("Full Material Swap")]
        [Tooltip("투혼이 가득 찼을 때 머테리얼을 교체할 배경 이미지 (예: BattleSpiritBarBackground). " +
                 "비워두면 아무 것도 하지 않는다.")]
        public Image BackgroundImage;

        [Tooltip("가득 찼을 때 배경에 적용할 머테리얼 (예: AllIn1 불타는 머테리얼). " +
                 "투혼을 사용해 가득참이 풀리면 원래 머테리얼로 자동 복원된다.")]
        public Material FullMaterial;

        [Header("Full")]
        [Tooltip("이 비율 이상이면 '가득 참'으로 보고 FullIndicator 를 켠다. 투혼은 정수이므로 1.0 근처.")]
        [Range(0.5f, 1f)]
        public float FullThreshold = 0.999f;

        [Header("Guard Break")]
        [Tooltip("투혼이 0(가드 브레이크/오토가드 불가)일 때 켤 오브젝트. 예: AvatarFront/Gaurdbreak. " +
                 "투혼이 0 초과면 자동으로 꺼진다. 비워두면 아무 것도 하지 않는다.")]
        public GameObject GuardBreakIndicator;

        [Tooltip("이 비율 이하이면 투혼 0(가드 브레이크)으로 보고 GuardBreakIndicator 를 켠다.")]
        [Range(0f, 0.2f)]
        public float GuardBreakThreshold = 0.0001f;

        [Header("Text Format")]
        [Tooltip("현재 수치 표시 포맷 (투혼은 정수이므로 \"0\")")]
        public string ValueFormat = "0";

        /// <summary>이 위젯이 담당하는 PlayerID (Bar 의 PlayerID 재사용)</summary>
        public string PlayerID => Bar != null ? Bar.PlayerID : null;

        // GC 회피: 값이 실제로 바뀐 프레임에만 텍스트/마커를 갱신한다.
        protected float _lastDisplayedValue = float.NaN;
        protected float _lastMarkerRatio = float.NaN;

        // FullIndicator 는 상태가 바뀐 프레임에만 SetActive → 정지 시 GC/오버헤드 0.
        protected bool _fullShown;
        protected bool _fullInitialized;

        // GuardBreakIndicator 도 상태 전환 프레임에만 SetActive.
        protected bool _guardBreakShown;
        protected bool _guardBreakInitialized;

        // 배경 원래 머테리얼 캐시 (첫 스왑 직전에 1회 캡처 — Awake 순서 의존 없음)
        protected Material _originalBackgroundMaterial;
        protected bool _originalMaterialCached;

        protected virtual void OnEnable()
        {
            // 활성화 시엔 항상 숨김에서 시작. 가득 차 있으면 첫 Push 에서 즉시 켜진다.
            _fullInitialized = false;
            ApplyFullIndicator(false);
            _guardBreakInitialized = false;
            ApplyGuardBreakIndicator(false);
        }

        /// <summary>
        /// HUD 한 프레임 갱신. ratio(0~1 바), absoluteValue(현재 절대 수치, 텍스트용), markerRatio(리셋 비율 0~1).
        /// </summary>
        public virtual void Push(float ratio, float absoluteValue, float markerRatio)
        {
            // UpdateBar01: Health 바와 동일하게 lerp + delayed bar 경로. 값이 안 바뀐 프레임은
            // UpdateBar 진입부에서 early-return 되어 코루틴 미시작(정지 시 GC 0). 점프 시에만 코루틴 1회.
            if (Bar != null) { Bar.UpdateBar01(ratio); }
            UpdateText(absoluteValue);
            UpdateMarker(markerRatio);
            UpdateFullIndicator(ratio);
            UpdateGuardBreakIndicator(ratio);
        }

        /// <summary>
        /// 투혼이 가득 찼을 때 FullIndicator(예: LR_BTN_Burst_ui)를 켜고, 아니면 끈다.
        /// 상태가 바뀐 프레임에만 SetActive 를 호출해 불필요한 오버헤드를 피한다.
        /// </summary>
        protected virtual void UpdateFullIndicator(float ratio)
        {
            if (FullIndicator == null) { return; }

            bool isFull = ratio >= FullThreshold;
            if (_fullInitialized && isFull == _fullShown) { return; }

            ApplyFullIndicator(isFull);
        }

        protected virtual void ApplyFullIndicator(bool show)
        {
            _fullShown = show;
            _fullInitialized = true;
            if (FullIndicator != null && FullIndicator.activeSelf != show)
            {
                FullIndicator.SetActive(show);
            }
            ApplyBackgroundMaterial(show);
        }

        /// <summary>
        /// 가득 찼으면 배경 이미지를 FullMaterial(불타는 머테리얼)로, 아니면 원래 머테리얼로 되돌린다.
        /// 상태 전환 프레임에만 호출되므로(ApplyFullIndicator 경유) 매 프레임 비용 없음.
        /// </summary>
        protected virtual void ApplyBackgroundMaterial(bool full)
        {
            if (BackgroundImage == null || FullMaterial == null) { return; }

            if (!_originalMaterialCached)
            {
                _originalBackgroundMaterial = BackgroundImage.material;
                _originalMaterialCached = true;
            }

            BackgroundImage.material = full ? FullMaterial : _originalBackgroundMaterial;
        }

        /// <summary>
        /// 투혼이 0(가드 브레이크)일 때 GuardBreakIndicator 를 켜고, 아니면 끈다.
        /// FullIndicator 와 동일하게 상태가 바뀐 프레임에만 SetActive → 정지 시 오버헤드 0.
        /// </summary>
        protected virtual void UpdateGuardBreakIndicator(float ratio)
        {
            if (GuardBreakIndicator == null) { return; }

            bool isBroken = ratio <= GuardBreakThreshold;
            if (_guardBreakInitialized && isBroken == _guardBreakShown) { return; }

            ApplyGuardBreakIndicator(isBroken);
        }

        protected virtual void ApplyGuardBreakIndicator(bool show)
        {
            _guardBreakShown = show;
            _guardBreakInitialized = true;
            if (GuardBreakIndicator != null && GuardBreakIndicator.activeSelf != show)
            {
                GuardBreakIndicator.SetActive(show);
            }
        }

        protected virtual void UpdateText(float absoluteValue)
        {
            if (CurrentValueText == null) { return; }
            // 값이 바뀐 프레임에만 ToString → 정지 상태에서 GC 0 에 수렴.
            if (Mathf.Approximately(absoluteValue, _lastDisplayedValue)) { return; }
            _lastDisplayedValue = absoluteValue;
            CurrentValueText.text = absoluteValue.ToString(ValueFormat); // "0" 포맷이 정수로 반올림 표시
        }

        protected virtual void UpdateMarker(float markerRatio)
        {
            if (Marker == null) { return; }
            markerRatio = Mathf.Clamp01(markerRatio);
            // 마커 비율은 런타임에 거의 안 변하므로 대부분 여기서 early-return.
            if (Mathf.Approximately(markerRatio, _lastMarkerRatio)) { return; }
            _lastMarkerRatio = markerRatio;

            // anchorMin.x = anchorMax.x = ratio: 부모 폭에 무관하게 비율 위치에 정렬, 리스케일 자동 대응.
            Vector2 aMin = Marker.anchorMin; aMin.x = markerRatio; Marker.anchorMin = aMin;
            Vector2 aMax = Marker.anchorMax; aMax.x = markerRatio; Marker.anchorMax = aMax;
            Vector2 pos = Marker.anchoredPosition; pos.x = 0f; Marker.anchoredPosition = pos;
        }
    }
}
