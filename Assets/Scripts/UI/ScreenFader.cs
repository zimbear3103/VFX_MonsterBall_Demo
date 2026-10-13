using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Yeolha.BeltScroll
{
    /// <summary>
    /// 씬 전환용 전체 화면 페이드 오버레이.
    /// 자체적으로 최상위 Overlay Canvas + 검은 Image를 생성하므로 씬 셋업이 필요 없다.
    /// MultiSceneLoader가 그룹 전환 시 FadeToBlack / FadeFromBlack을 yield로 사용한다.
    /// Time.timeScale의 영향을 받지 않도록 unscaled time으로 동작한다.
    /// </summary>
    [AddComponentMenu("Yeolha/UI/Screen Fader")]
    public class ScreenFader : MonoBehaviour
    {
        private static ScreenFader _instance;

        /// <summary>씬에서 찾고, 없으면 자동 생성한다.</summary>
        public static ScreenFader Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<ScreenFader>();
                    if (_instance == null)
                    {
                        var go = new GameObject("ScreenFader");
                        _instance = go.AddComponent<ScreenFader>();
                    }
                }
                return _instance;
            }
        }

        [Header("Durations")]
        [Tooltip("페이드 인(검은 화면 → 게임 화면) 시간")]
        [SerializeField] private float fadeInDuration = 0.6f;

        [Tooltip("페이드 아웃(게임 화면 → 검은 화면) 시간")]
        [SerializeField] private float fadeOutDuration = 0.35f;

        [Header("Appearance")]
        [Tooltip("페이드 색상 (기본 검정)")]
        [SerializeField] private Color fadeColor = Color.black;

        [Tooltip("오버레이 Canvas의 Sorting Order (다른 UI보다 위)")]
        [SerializeField] private int sortingOrder = 9999;

        public bool IsFading { get; private set; }

        /// <summary>현재 화면이 완전히 가려져 있는가.</summary>
        public bool IsOpaque => _canvasGroup != null && _canvasGroup.alpha >= 1f;

        private Canvas _canvas;
        private CanvasGroup _canvasGroup;
        private Image _image;
        private Coroutine _activeFade;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            BuildOverlay();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void BuildOverlay()
        {
            if (_canvas != null) return;

            var canvasGO = new GameObject("FadeCanvas");
            canvasGO.transform.SetParent(transform, false);

            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;

            _canvasGroup = canvasGO.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;

            var imageGO = new GameObject("FadeImage");
            imageGO.transform.SetParent(canvasGO.transform, false);

            _image = imageGO.AddComponent<Image>();
            _image.color = fadeColor;
            _image.raycastTarget = false;

            var rt = _image.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            canvasGO.SetActive(false);
        }

        // ─────────────────────────── Public API ───────────────────────────

        /// <summary>즉시 완전한 검은 화면으로 만든다. (초기 로드 시작 등)</summary>
        public void SetOpaque()
        {
            BuildOverlay();
            StopActiveFade();
            _canvas.gameObject.SetActive(true);
            _canvasGroup.alpha = 1f;
        }

        /// <summary>즉시 오버레이를 걷어낸다.</summary>
        public void SetClear()
        {
            BuildOverlay();
            StopActiveFade();
            _canvasGroup.alpha = 0f;
            _canvas.gameObject.SetActive(false);
        }

        /// <summary>화면을 검게 가린다. 코루틴에서 yield 가능.</summary>
        public IEnumerator FadeToBlack(float duration = -1f)
        {
            yield return RunFade(1f, duration >= 0f ? duration : fadeOutDuration);
        }

        /// <summary>검은 화면에서 게임 화면으로 페이드 인. 코루틴에서 yield 가능.</summary>
        public IEnumerator FadeFromBlack(float duration = -1f)
        {
            yield return RunFade(0f, duration >= 0f ? duration : fadeInDuration);
        }

        // ─────────────────────────── Internal ───────────────────────────

        private IEnumerator RunFade(float targetAlpha, float duration)
        {
            BuildOverlay();
            StopActiveFade();

            _canvas.gameObject.SetActive(true);
            IsFading = true;

            float startAlpha = _canvasGroup.alpha;
            if (duration <= 0f)
            {
                _canvasGroup.alpha = targetAlpha;
            }
            else
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, Mathf.Clamp01(elapsed / duration));
                    yield return null;
                }
                _canvasGroup.alpha = targetAlpha;
            }

            IsFading = false;
            if (Mathf.Approximately(targetAlpha, 0f))
                _canvas.gameObject.SetActive(false);
        }

        private void StopActiveFade()
        {
            if (_activeFade != null)
            {
                StopCoroutine(_activeFade);
                _activeFade = null;
            }
            IsFading = false;
        }
    }
}
