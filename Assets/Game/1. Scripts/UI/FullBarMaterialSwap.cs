using UnityEngine;
using UnityEngine.UI;
using MoreMountains.Tools;

namespace Aiara
{
    /// <summary>
    /// MMProgressBar 가 가득 찼을 때(BarTarget >= FullThreshold) 지정 Image 의 머테리얼을
    /// FullMaterial 로 교체하고, 가득참이 풀리면 원래 머테리얼로 복원하는 범용 컴포넌트.
    /// HP/FP 바 등 MMProgressBar 기반 HUD 어디에나 부착 가능.
    /// BarTarget 은 push 즉시 갱신되는 목표값이라 lerp/delayed 연출과 무관하게 정확한 시점에 전환된다.
    /// 상태가 바뀐 프레임에만 머테리얼을 할당하므로 매 프레임 비용은 비교 1회뿐(GC 0).
    /// </summary>
    [AddComponentMenu("TopDown Engine/GUI/Full Bar Material Swap")]
    public class FullBarMaterialSwap : MonoBehaviour
    {
        [Header("Bindings")]
        [Tooltip("감시할 progress bar. 비워두면 자신/부모에서 자동 탐색.")]
        public MMProgressBar Bar;

        [Tooltip("가득 찼을 때 머테리얼을 교체할 이미지 (예: HealthBarFront / FPBarFront)")]
        public Image TargetImage;

        [Tooltip("가득 찼을 때 적용할 머테리얼 (예: FullBarFront). 풀리면 원래 머테리얼로 자동 복원.")]
        public Material FullMaterial;

        [Header("Full")]
        [Tooltip("이 비율 이상이면 '가득 참'으로 판정")]
        [Range(0.5f, 1f)]
        public float FullThreshold = 0.999f;

        // 원래 머테리얼 캐시 (첫 스왑 직전에 1회 캡처 — 초기화 순서 의존 없음)
        protected Material _originalMaterial;
        protected bool _originalCached;

        // 상태가 바뀐 프레임에만 할당 → 정지 시 오버헤드 0.
        protected bool _isFull;
        protected bool _initialized;

        protected virtual void Awake()
        {
            if (Bar == null)
            {
                Bar = GetComponentInParent<MMProgressBar>();
            }
        }

        protected virtual void OnEnable()
        {
            // 활성화 시엔 항상 '가득 아님'에서 시작. 가득 차 있으면 첫 Update 에서 즉시 켜진다.
            _initialized = false;
            Apply(false);
        }

        protected virtual void Update()
        {
            if (Bar == null || TargetImage == null || FullMaterial == null) { return; }

            bool full = Bar.BarTarget >= FullThreshold;
            if (_initialized && full == _isFull) { return; }

            Apply(full);
        }

        protected virtual void Apply(bool full)
        {
            _isFull = full;
            _initialized = true;
            if (TargetImage == null || FullMaterial == null) { return; }

            if (!_originalCached)
            {
                _originalMaterial = TargetImage.material;
                _originalCached = true;
            }

            TargetImage.material = full ? FullMaterial : _originalMaterial;
        }
    }
}
