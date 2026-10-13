using UnityEngine;
using UnityEngine.UI;

namespace Aiara
{
    /// <summary>
    /// 월드 스페이스 적 체력/투혼 바를 포스트프로세싱 없이 화면에 합성하기 위한 RenderTexture 매니저.
    ///
    /// 원리(이 URP 버전에선 Overlay 카메라가 월드 UI를 못 그리고, Base 카메라를 위에 올리면 화면이 지워지므로):
    /// - BarCamera가 메인 카메라와 같은 시점으로 '바 레이어'만 투명 배경 RenderTexture(_rt)에 렌더(포스트프로세싱 OFF).
    /// - 그 RT를 포스트프로세싱을 받지 않는 HUD(UICamera) 위의 전체화면 RawImage(DisplayImage)로 덧그린다.
    /// 결과: 바는 PP를 안 받고, 메인 카메라와 같은 화면 위치에 정확히 겹쳐 보인다(따라다니기 유지).
    ///
    /// RT는 화면 해상도에 맞춰 생성/재생성한다. BarCamera의 위치/회전은 메인 카메라의 자식으로,
    /// 투영은 MatchCameraProjection으로 일치시킨다.
    ///
    /// 주의: BarCamera는 바 레이어만 렌더하므로 월드 지오메트리 깊이가 없다 → 바가 벽 뒤에서도 보인다(항상 위).
    /// </summary>
    [AddComponentMenu("Aiara/UI/World UI Bar RT Manager")]
    public class WorldUIBarRTManager : MonoBehaviour
    {
        [Tooltip("바 레이어만 렌더해 RT에 담을 카메라 (메인 카메라 자식, 투명 배경, PP off)")]
        public Camera BarCamera;

        [Tooltip("RT를 화면에 덧그릴 전체화면 RawImage (PP 없는 HUD 캔버스 위)")]
        public RawImage DisplayImage;

        [Tooltip("RT 해상도 스케일 (1=화면 해상도). 성능 필요 시 낮춤)")]
        [Range(0.25f, 1f)] public float ResolutionScale = 1f;

        protected RenderTexture _rt;
        protected int _w, _h;

        protected virtual void OnEnable()
        {
            // RT가 준비되기 전엔 RawImage를 숨긴다 — 텍스처 없는 RawImage는 흰색 전체화면으로
            // 화면을 다 가려버리므로. 준비되면 EnsureRT가 다시 켠다.
            if (DisplayImage != null) DisplayImage.enabled = false;
            EnsureRT();
        }

        protected virtual void LateUpdate() { EnsureRT(); }

        protected virtual void EnsureRT()
        {
            int w = Mathf.Max(2, Mathf.RoundToInt(Screen.width * ResolutionScale));
            int h = Mathf.Max(2, Mathf.RoundToInt(Screen.height * ResolutionScale));
            if (_rt != null && _w == w && _h == h) return;

            ReleaseRT();
            _rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32) { name = "WorldBarRT" };
            _rt.Create();
            _w = w; _h = h;

            // 카메라가 아직 렌더하기 전의 미정의 픽셀(흰색/노이즈)이 잠깐 보이지 않도록 투명으로 클리어.
            ClearTransparent(_rt);

            if (BarCamera != null) BarCamera.targetTexture = _rt;
            if (DisplayImage != null)
            {
                DisplayImage.texture = _rt;
                DisplayImage.enabled = true; // RT 준비 완료 → 이제 표시
            }
        }

        protected static void ClearTransparent(RenderTexture rt)
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
            RenderTexture.active = prev;
        }

        protected virtual void ReleaseRT()
        {
            if (BarCamera != null && BarCamera.targetTexture == _rt) BarCamera.targetTexture = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
        }

        protected virtual void OnDisable() { ReleaseRT(); }
    }
}
