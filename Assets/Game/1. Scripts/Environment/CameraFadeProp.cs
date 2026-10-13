using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 카메라와의 거리에 따라 환경 오브젝트(부서지는 상자 등)를 페이드시킨다.
    /// 셰이더의 Float 프로퍼티(기본 "_FadeAlpha")를 알파로 구동하고, 페이드 중에만
    /// 머티리얼을 Transparent 서페이스로 전환한다.
    ///
    /// 구 프로젝트 Aiara.CameraFadeProp의 ver2 포팅 — MoreMountains 의존과
    /// 절명기/잡기(Execution/Grab) 연출 게이트를 제거하고, "카메라가 가까우면 항상 페이드"로 단순화.
    /// GUID는 구버전과 동일(dc3aaa10...)하게 유지해 기존 BreakableBox 프리팹의 컴포넌트 설정이 그대로 링크된다.
    ///
    /// 주의: 대상 셰이더가 (1) Material Override 허용, (2) Alpha 출력이 이 Float 프로퍼티로
    /// 구동되도록 연결돼 있어야 실제로 투명해진다. Alpha가 상수 1로 고정된 셰이더면 값만 바뀌고 안 보인다.
    /// </summary>
    [AddComponentMenu("Yeolha/Environment/Camera Fade Prop")]
    public class CameraFadeProp : MonoBehaviour
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
        [Tooltip("true면 FadeEntries를 무시하고 자식의 모든 Renderer를 자동 수집한다(FadeAlphaProperty를 가진 머티리얼만). 상자처럼 렌더러가 중첩 프리팹 안에 있을 때 편하다.")]
        public bool AutoCollectRenderers = true;
        [Tooltip("AutoCollectRenderers가 false일 때 사용할, 알파를 조절할 렌더러 목록")]
        public List<FadeMaterialEntry> FadeEntries;

        [Tooltip("페이드에 사용할 셰이더 Float 프로퍼티 이름. 셰이더에서 이 프로퍼티가 Alpha에 연결돼 있어야 한다.")]
        public string FadeAlphaProperty = "_FadeAlpha";

        [Header("Fade Distance")]
        [Tooltip("이 거리 이하에서 최소 알파값에 도달")]
        public float NearDistance = 4.2f;
        [Tooltip("이 거리 이상에서 완전 불투명(알파 1)")]
        public float FarDistance = 4.5f;
        [Tooltip("가장 가까웠을 때의 최소 알파값(0=완전 투명, 1=불투명)")]
        [Range(0f, 1f)]
        public float MinAlpha = 0.15f;

        [Header("Camera")]
        [Tooltip("거리 측정에 사용할 카메라. 비어 있으면 Camera.main 사용")]
        public Camera TargetCamera;

        [Header("Debug")]
        [Tooltip("진단 로그 출력")]
        public bool DebugLog = false;

        protected int _fadeAlphaID;
        protected static readonly int _surfaceID = Shader.PropertyToID("_Surface");
        protected static readonly int _srcBlendID = Shader.PropertyToID("_SrcBlend");
        protected static readonly int _dstBlendID = Shader.PropertyToID("_DstBlend");
        protected static readonly int _srcBlendAlphaID = Shader.PropertyToID("_SrcBlendAlpha");
        protected static readonly int _dstBlendAlphaID = Shader.PropertyToID("_DstBlendAlpha");
        protected static readonly int _zwriteID = Shader.PropertyToID("_ZWrite");
        protected const string TransparentKeyword = "_SURFACE_TYPE_TRANSPARENT";

        protected Transform _cameraTransform;
        protected float _currentAlpha = 1f;
        protected bool _isTransparent = false;

        protected List<Material> _runtimeMaterials;
        protected Dictionary<Renderer, Material[]> _instancedByRenderer;

        protected virtual void OnEnable()
        {
            if (string.IsNullOrEmpty(FadeAlphaProperty))
                FadeAlphaProperty = "_FadeAlpha";
            _fadeAlphaID = Shader.PropertyToID(FadeAlphaProperty);

            CacheCameraTransform();
            CacheRuntimeMaterials();

            _currentAlpha = 1f;
            _isTransparent = false;
        }

        protected virtual void CacheCameraTransform()
        {
            Camera cam = TargetCamera != null ? TargetCamera : Camera.main;
            _cameraTransform = cam != null ? cam.transform : null;
        }

        protected virtual void CacheRuntimeMaterials()
        {
            _runtimeMaterials = new List<Material>();
            _instancedByRenderer = new Dictionary<Renderer, Material[]>();

            if (AutoCollectRenderers)
            {
                CollectFromChildRenderers();
                return;
            }

            if (FadeEntries == null) return;

            for (int i = 0; i < FadeEntries.Count; i++)
            {
                FadeMaterialEntry entry = FadeEntries[i];
                if (entry == null || entry.Renderer == null)
                {
                    _runtimeMaterials.Add(null);
                    continue;
                }

                if (!_instancedByRenderer.TryGetValue(entry.Renderer, out Material[] mats))
                {
                    mats = entry.Renderer.materials; // 접근 시 인스턴스화
                    _instancedByRenderer[entry.Renderer] = mats;
                }

                if (entry.MaterialIndex < 0 || entry.MaterialIndex >= mats.Length)
                {
                    _runtimeMaterials.Add(null);
                    continue;
                }

                _runtimeMaterials.Add(mats[entry.MaterialIndex]);
            }
        }

        /// <summary>자식 계층의 모든 Renderer(파티클 제외)를 훑어 FadeAlphaProperty를 가진 머티리얼만 페이드 대상으로 등록.</summary>
        protected virtual void CollectFromChildRenderers()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || r is ParticleSystemRenderer) continue;

                Material[] mats = r.materials; // 접근 시 인스턴스화
                _instancedByRenderer[r] = mats;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null) continue;
                    if (mat.HasProperty(_fadeAlphaID))
                        _runtimeMaterials.Add(mat);
                }
            }
        }

        protected virtual void LateUpdate()
        {
            if (_cameraTransform == null)
            {
                CacheCameraTransform();
                if (_cameraTransform == null) return;
            }

            float distance = Vector3.Distance(transform.position, _cameraTransform.position);
            float t = Mathf.InverseLerp(NearDistance, FarDistance, distance);
            float alpha = Mathf.Lerp(MinAlpha, 1f, t);

            bool needTransparent = alpha < 1f;
            if (needTransparent != _isTransparent)
            {
                ApplySurfaceType(needTransparent);
                _isTransparent = needTransparent;
            }

            ApplyAlpha(alpha);
            _currentAlpha = alpha;
        }

        protected virtual void ApplyAlpha(float alpha)
        {
            if (_runtimeMaterials == null) return;

            for (int i = 0; i < _runtimeMaterials.Count; i++)
            {
                Material mat = _runtimeMaterials[i];
                if (mat == null) continue;
                mat.SetFloat(_fadeAlphaID, alpha);
            }
        }

        protected virtual void ApplySurfaceType(bool transparent)
        {
            if (_runtimeMaterials == null) return;

            for (int i = 0; i < _runtimeMaterials.Count; i++)
            {
                Material mat = _runtimeMaterials[i];
                if (mat == null) continue;

                if (transparent)
                {
                    mat.SetFloat(_surfaceID, 1f);
                    mat.SetFloat(_srcBlendID, (float)BlendMode.SrcAlpha);
                    mat.SetFloat(_dstBlendID, (float)BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat(_srcBlendAlphaID, (float)BlendMode.One);
                    mat.SetFloat(_dstBlendAlphaID, (float)BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat(_zwriteID, 0f);
                    mat.EnableKeyword(TransparentKeyword);
                    mat.renderQueue = (int)RenderQueue.Transparent;
                }
                else
                {
                    mat.SetFloat(_surfaceID, 0f);
                    mat.SetFloat(_srcBlendID, (float)BlendMode.One);
                    mat.SetFloat(_dstBlendID, (float)BlendMode.Zero);
                    mat.SetFloat(_srcBlendAlphaID, (float)BlendMode.One);
                    mat.SetFloat(_dstBlendAlphaID, (float)BlendMode.Zero);
                    mat.SetFloat(_zwriteID, 1f);
                    mat.DisableKeyword(TransparentKeyword);
                    mat.renderQueue = (int)RenderQueue.Geometry;
                }
            }
        }
    }
}
