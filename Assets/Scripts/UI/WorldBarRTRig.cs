using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// Runtime builder for the post-processing-immune world-bar RT rig (ported from legacy 3DCameras).
    /// A BarRTCamera renders ONLY the bar layer to a transparent RenderTexture with post-processing off,
    /// then a fullscreen RawImage on a PP-free Screen-Space-Overlay canvas composites it over the view.
    /// The main camera is stripped of the bar layer so bars never go through post-processing.
    /// Drop this on one scene object. Bars follow correctly because BarRTCamera copies the main
    /// camera projection every frame (MatchCameraProjection) and sits as its child.
    /// </summary>
    public class WorldBarRTRig : MonoBehaviour
    {
        [SerializeField] private string barLayerName = "Bars";
        [SerializeField] private int overlaySortingOrder = -100; // behind the player HUD
        [Range(0.25f, 1f)] [SerializeField] private float resolutionScale = 1f;

        public static int BarLayer { get; private set; } = -1;

        private void Awake()
        {
            int layer = LayerMask.NameToLayer(barLayerName);
            BarLayer = layer;
            if (layer < 0) { Debug.LogError("[WorldBarRTRig] Bar layer '" + barLayerName + "' does not exist."); return; }

            Camera main = ResolveMainCamera();
            if (main == null) { Debug.LogError("[WorldBarRTRig] Could not resolve a main camera."); return; }

            int barMask = 1 << layer;

            // Main camera must NOT render the bar layer — bars are composited PP-free via the RT.
            main.cullingMask &= ~barMask;

            // BarRTCamera: child of main, renders only the bar layer to a transparent RT, PP off.
            var camGo = new GameObject("BarRTCamera");
            camGo.transform.SetParent(main.transform, false);
            camGo.transform.localPosition = Vector3.zero;
            camGo.transform.localRotation = Quaternion.identity;

            var cam = camGo.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.cullingMask = barMask;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.depth = main.depth - 1;
            cam.targetTexture = null; // assigned by WorldUIBarRTManager

            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderType = CameraRenderType.Base;
                data.renderPostProcessing = false;
                data.renderShadows = false;
            }

            var match = camGo.AddComponent<Aiara.MatchCameraProjection>();
            match.Source = main;

            // PP-free overlay canvas + fullscreen RawImage that displays the RT.
            var canvasGo = new GameObject("WorldBarRTCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = overlaySortingOrder;

            var riGo = new GameObject("BarRTDisplay", typeof(RectTransform), typeof(RawImage));
            riGo.transform.SetParent(canvasGo.transform, false);
            var ri = riGo.GetComponent<RawImage>();
            ri.raycastTarget = false;
            var rt = (RectTransform)riGo.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            // Add the manager with its fields already set BEFORE OnEnable fires, otherwise its
            // first EnsureRT runs with null refs (RT created but never wired → white fullscreen).
            // Deactivate the camera object, add + configure, then reactivate so OnEnable sees the refs.
            camGo.SetActive(false);
            var mgr = camGo.AddComponent<Aiara.WorldUIBarRTManager>();
            mgr.BarCamera = cam;
            mgr.DisplayImage = ri;
            mgr.ResolutionScale = resolutionScale;
            camGo.SetActive(true);

            Debug.Log("[WorldBarRTRig] Built RT rig on layer '" + barLayerName + "' (index " + layer + ").");
        }

        private Camera ResolveMainCamera()
        {
            if (Camera.main != null) return Camera.main;
            Camera best = null;
            foreach (var c in Camera.allCameras)
                if (c.targetTexture == null && (best == null || c.depth > best.depth)) best = c;
            return best;
        }
    }
}
