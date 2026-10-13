using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 카메라와 가까워진 캐릭터(주로 적)를 반투명하게 만든다.
    /// 구 프로젝트 Aiara.CharacterCameraFade(TopDown Engine CharacterAbility) 이관 —
    /// MoreMountains 의존을 제거한 일반 MonoBehaviour.
    ///
    /// 방식(구버전 동일): 카메라와의 단순 거리로 InverseLerp(Near, Far) → 알파 계산,
    /// 대상 머티리얼 인스턴스의 _BaseColor.a에 기록. 페이드 중에만 URP 서페이스를
    /// Transparent로 전환하고 벗어나면 Opaque로 복구한다.
    /// 게임플레이(HitBox/AI/애니메이션)는 건드리지 않는다 — 렌더링 전용.
    ///
    /// 주의: 대상 셰이더(YSA Toon Lit 등)가 Material Override를 허용하고
    /// Alpha가 _BaseColor.a로 구동돼야 실제로 투명해진다.
    /// 아웃라인 슬롯(Outline ShaderGraph)은 자동으로 제외된다.
    /// </summary>
    [AddComponentMenu("Yeolha/Character/Character Camera Fade")]
    public class CharacterCameraFade : MonoBehaviour
    {
        [Serializable]
        public class FadeMaterialEntry
        {
            [Tooltip("알파를 조절할 렌더러")]
            public Renderer Renderer;
            [Tooltip("해당 렌더러에서 조절할 머티리얼 인덱스")]
            public int MaterialIndex;
        }

        [Header("Fade Targets")]
        [Tooltip("true면 FadeEntries를 무시하고 자식 렌더러를 자동 수집한다(_BaseColor와 _Surface를 가진 머티리얼만, 아웃라인 셰이더 제외).")]
        public bool AutoCollectRenderers = true;
        [Tooltip("AutoCollectRenderers가 false일 때 사용할 대상 목록")]
        public List<FadeMaterialEntry> FadeEntries;

        [Header("Fade Distance")]
        [Tooltip("이 거리 이하에서 최소 알파값에 도달 (구버전 기본 4.2, Kiu·DreadHog 2)")]
        public float NearDistance = 4.2f;
        [Tooltip("이 거리 이상에서 완전 불투명 (구버전 기본 4.5, Kiu·DreadHog 3.2)")]
        public float FarDistance = 4.5f;
        [Tooltip("가장 가까웠을 때의 최소 알파값 (구버전 0.3)")]
        [Range(0f, 1f)]
        public float MinAlpha = 0.3f;
        [Tooltip("가장 가까웠을 때 체력바 알파 (구버전 0 = 완전히 숨김)")]
        [Range(0f, 1f)]
        public float HealthBarMinAlpha = 0f;

        [Header("Camera")]
        [Tooltip("거리 측정에 사용할 카메라. 비어 있으면 CameraRig.LensNode → Camera.main 순으로 탐색")]
        public Camera TargetCamera;

        private static readonly int _baseColorID = Shader.PropertyToID("_BaseColor");
        private static readonly int _surfaceID = Shader.PropertyToID("_Surface");
        private static readonly int _srcBlendID = Shader.PropertyToID("_SrcBlend");
        private static readonly int _dstBlendID = Shader.PropertyToID("_DstBlend");
        private static readonly int _srcBlendAlphaID = Shader.PropertyToID("_SrcBlendAlpha");
        private static readonly int _dstBlendAlphaID = Shader.PropertyToID("_DstBlendAlpha");
        private static readonly int _zwriteID = Shader.PropertyToID("_ZWrite");
        private const string TransparentKeyword = "_SURFACE_TYPE_TRANSPARENT";

        private Transform _cameraTransform;
        private readonly List<Material> _runtimeMaterials = new List<Material>();
        private readonly List<Material[]> _instancedArrays = new List<Material[]>();
        private CanvasGroup _healthBarCanvasGroup;
        private EnemyStatusBarBinder _barBinder;
        private bool _healthBarResolved;
        private bool _isTransparent;
        private float _currentAlpha = 1f;
        private bool _cached;

        /// <summary>현재 본체 알파(1 = 불투명). 외부 시각 시스템 참고용.</summary>
        public float CurrentAlpha => _currentAlpha;

        private void OnEnable()
        {
            _currentAlpha = 1f;
            _isTransparent = false;
            CacheCameraTransform();
            if (!_cached) CacheRuntimeMaterials();
        }

        private void OnDisable()
        {
            if (_isTransparent)
            {
                ApplySurfaceType(false);
                ApplyAlpha(1f, 1f);
            }
        }

        private void OnDestroy()
        {
            // renderer.materials로 만든 인스턴스 정리 (구버전 누수 개선)
            for (int i = 0; i < _instancedArrays.Count; i++)
            {
                Material[] arr = _instancedArrays[i];
                if (arr == null) continue;
                for (int j = 0; j < arr.Length; j++)
                    if (arr[j] != null) Destroy(arr[j]);
            }
            _instancedArrays.Clear();
            _runtimeMaterials.Clear();
        }

        private void CacheCameraTransform()
        {
            Camera cam = TargetCamera;
            if (cam == null)
            {
                CameraRig rig = FindFirstObjectByType<CameraRig>();
                if (rig != null && rig.LensNode != null) cam = rig.LensNode.TargetCamera;
            }
            if (cam == null) cam = Camera.main;
            _cameraTransform = cam != null ? cam.transform : null;
        }

        private void CacheRuntimeMaterials()
        {
            _cached = true;
            _runtimeMaterials.Clear();

            if (AutoCollectRenderers)
            {
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;

                    Material[] shared = r.sharedMaterials;
                    bool any = false;
                    for (int i = 0; i < shared.Length; i++)
                        if (IsFadeable(shared[i])) { any = true; break; }
                    if (!any) continue;

                    Material[] instanced = r.materials; // 인스턴스 생성(1회)
                    _instancedArrays.Add(instanced);
                    for (int i = 0; i < instanced.Length; i++)
                        if (IsFadeable(instanced[i])) _runtimeMaterials.Add(instanced[i]);
                }
            }
            else if (FadeEntries != null)
            {
                var instancedByRenderer = new Dictionary<Renderer, Material[]>();
                for (int i = 0; i < FadeEntries.Count; i++)
                {
                    FadeMaterialEntry entry = FadeEntries[i];
                    if (entry == null || entry.Renderer == null) continue;
                    if (!instancedByRenderer.TryGetValue(entry.Renderer, out Material[] mats))
                    {
                        mats = entry.Renderer.materials;
                        instancedByRenderer[entry.Renderer] = mats;
                        _instancedArrays.Add(mats);
                    }
                    if (entry.MaterialIndex >= 0 && entry.MaterialIndex < mats.Length && mats[entry.MaterialIndex] != null)
                        _runtimeMaterials.Add(mats[entry.MaterialIndex]);
                }
            }
        }

        private static bool IsFadeable(Material mat)
        {
            if (mat == null) return false;
            if (mat.shader != null && mat.shader.name.Contains("Outline")) return false;
            return mat.HasProperty(_baseColorID) && mat.HasProperty(_surfaceID);
        }

        private void LateUpdate()
        {
            if (_cameraTransform == null)
            {
                CacheCameraTransform();
                if (_cameraTransform == null) return;
            }
            if (_runtimeMaterials.Count == 0) return;

            float distance = Vector3.Distance(transform.position, _cameraTransform.position);
            float t = Mathf.InverseLerp(NearDistance, FarDistance, distance);
            float alpha = Mathf.Lerp(MinAlpha, 1f, t);
            float uiAlpha = Mathf.Lerp(HealthBarMinAlpha, 1f, t);
            bool needTransparent = alpha < 1f;

            if (needTransparent != _isTransparent) ApplySurfaceType(needTransparent);
            if (!Mathf.Approximately(alpha, _currentAlpha) || needTransparent)
                ApplyAlpha(alpha, uiAlpha);
        }

        private void ApplyAlpha(float alpha, float uiAlpha)
        {
            _currentAlpha = alpha;
            for (int i = 0; i < _runtimeMaterials.Count; i++)
            {
                Material mat = _runtimeMaterials[i];
                if (mat == null) continue;
                Color c = mat.GetColor(_baseColorID);
                c.a = alpha;
                mat.SetColor(_baseColorID, c);
            }

            TryResolveHealthBar();
            if (_healthBarCanvasGroup != null) _healthBarCanvasGroup.alpha = uiAlpha;
        }

        private void TryResolveHealthBar()
        {
            if (_healthBarResolved) return;
            if (_barBinder == null) _barBinder = GetComponent<EnemyStatusBarBinder>();
            if (_barBinder == null) { _healthBarResolved = true; return; }
            Transform barRoot = _barBinder.BarRoot;
            if (barRoot == null) return; // 바가 아직 생성 전이면 다음 프레임 재시도
            _healthBarCanvasGroup = barRoot.GetComponentInChildren<CanvasGroup>(true);
            if (_healthBarCanvasGroup == null)
                _healthBarCanvasGroup = barRoot.gameObject.AddComponent<CanvasGroup>();
            _healthBarResolved = true;
        }

        private void ApplySurfaceType(bool transparent)
        {
            _isTransparent = transparent;
            for (int i = 0; i < _runtimeMaterials.Count; i++)
            {
                Material mat = _runtimeMaterials[i];
                if (mat == null) continue;

                if (transparent)
                {
                    mat.SetFloat(_surfaceID, 1f);
                    mat.SetFloat(_srcBlendID, (float)BlendMode.SrcAlpha);
                    mat.SetFloat(_dstBlendID, (float)BlendMode.OneMinusSrcAlpha);
                    if (mat.HasProperty(_srcBlendAlphaID)) mat.SetFloat(_srcBlendAlphaID, (float)BlendMode.One);
                    if (mat.HasProperty(_dstBlendAlphaID)) mat.SetFloat(_dstBlendAlphaID, (float)BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat(_zwriteID, 0f);
                    mat.EnableKeyword(TransparentKeyword);
                    mat.renderQueue = (int)RenderQueue.Transparent;
                }
                else
                {
                    mat.SetFloat(_surfaceID, 0f);
                    mat.SetFloat(_srcBlendID, (float)BlendMode.One);
                    mat.SetFloat(_dstBlendID, (float)BlendMode.Zero);
                    if (mat.HasProperty(_srcBlendAlphaID)) mat.SetFloat(_srcBlendAlphaID, (float)BlendMode.One);
                    if (mat.HasProperty(_dstBlendAlphaID)) mat.SetFloat(_dstBlendAlphaID, (float)BlendMode.Zero);
                    mat.SetFloat(_zwriteID, 1f);
                    mat.DisableKeyword(TransparentKeyword);
                    mat.renderQueue = (int)RenderQueue.Geometry;
                }
            }
        }
    }
}
