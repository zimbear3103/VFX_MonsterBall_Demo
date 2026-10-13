using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Test-only time scale controller.
/// Creates its own overlay canvas with a slider (0 ~ 2) that absolutely
/// overrides Time.timeScale every frame while enabled.
/// The value can only be changed by dragging the slider with the pointer;
/// keyboard / gamepad directional input is ignored.
/// An ON/OFF button fully releases control and restores real time.
/// Drop this component on any GameObject in the scene.
/// </summary>
public class TestTimeScaleManager : MonoBehaviour
{
    [Header("Range")]
    [SerializeField] private float minScale = 0f;
    [SerializeField] private float maxScale = 2f;
    [SerializeField] private float startScale = 1f;

    [Header("Options")]
    [Tooltip("Force the slider value onto Time.timeScale every frame, overriding any other system.")]
    [SerializeField] private bool enforceEveryFrame = true;
    [Tooltip("Also scale Time.fixedDeltaTime so physics slows down smoothly.")]
    [SerializeField] private bool scaleFixedDeltaTime = true;
    [Tooltip("Whether the controller starts active (overriding time scale) when play begins.")]
    [SerializeField] private bool startActive = true;

    private Slider slider;
    private Text valueLabel;
    private Button powerButton;
    private Text powerLabel;
    private CanvasGroup sliderGroup;
    private float baseFixedDeltaTime;
    private float currentScale;
    private bool isActive;

    private static readonly Color OnColor = new Color(0.20f, 0.60f, 0.25f, 1f);
    private static readonly Color OffColor = new Color(0.55f, 0.20f, 0.20f, 1f);

    private void Awake()
    {
        baseFixedDeltaTime = Time.fixedDeltaTime;
        currentScale = Mathf.Clamp(startScale, minScale, maxScale);
        isActive = startActive;
        BuildUI();
        ApplyActiveState();
    }

    private void LateUpdate()
    {
        if (isActive && enforceEveryFrame)
        {
            ApplyScale(currentScale);
        }
    }

    private void OnDestroy()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = baseFixedDeltaTime;
    }

    private void OnSliderChanged(float value)
    {
        currentScale = value;
        RefreshFromScale();
    }

    private void ApplyScale(float value)
    {
        Time.timeScale = value;
        if (scaleFixedDeltaTime)
        {
            Time.fixedDeltaTime = baseFixedDeltaTime * Mathf.Max(value, 0.0001f);
        }
        UpdateValueLabel();
    }

    private void ResetScale()
    {
        currentScale = 1f;
        if (slider != null)
        {
            slider.SetValueWithoutNotify(1f);
        }
        RefreshFromScale();
    }

    private void ToggleActive()
    {
        isActive = !isActive;
        ApplyActiveState();
    }

    private void RefreshFromScale()
    {
        if (isActive)
        {
            ApplyScale(currentScale);
        }
        else
        {
            UpdateValueLabel();
        }
    }

    private void UpdateValueLabel()
    {
        if (valueLabel == null)
        {
            return;
        }
        valueLabel.text = isActive
            ? string.Format("TimeScale x{0:0.00}", currentScale)
            : string.Format("TimeScale x{0:0.00} (off)", currentScale);
    }

    private void ApplyActiveState()
    {
        if (isActive)
        {
            ApplyScale(currentScale);
        }
        else
        {
            // Fully release control: restore real time.
            Time.timeScale = 1f;
            Time.fixedDeltaTime = baseFixedDeltaTime;
            UpdateValueLabel();
        }

        if (slider != null)
        {
            slider.interactable = isActive;
        }
        if (sliderGroup != null)
        {
            sliderGroup.alpha = isActive ? 1f : 0.35f;
            sliderGroup.interactable = isActive;
        }
        if (powerLabel != null)
        {
            powerLabel.text = isActive ? "ON" : "OFF";
        }
        if (powerButton != null && powerButton.targetGraphic is Image img)
        {
            img.color = isActive ? OnColor : OffColor;
        }
    }

    private void BuildUI()
    {
        // Root canvas
        GameObject canvasGO = new GameObject("TestTimeScaleCanvas");
        canvasGO.transform.SetParent(transform, false);
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGO.AddComponent<GraphicRaycaster>();

        // Panel (top-left)
        RectTransform panel = CreateRect("Panel", canvasGO.transform);
        panel.anchorMin = new Vector2(0f, 1f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(20f, -20f);
        panel.sizeDelta = new Vector2(340f, 90f);
        Image panelImg = panel.gameObject.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.6f);

        // Value label
        RectTransform labelRect = CreateRect("ValueLabel", panel);
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -8f);
        labelRect.sizeDelta = new Vector2(-20f, 28f);
        valueLabel = labelRect.gameObject.AddComponent<Text>();
        valueLabel.font = GetDefaultFont();
        valueLabel.fontSize = 20;
        valueLabel.color = Color.white;
        valueLabel.alignment = TextAnchor.MiddleLeft;
        labelRect.offsetMin = new Vector2(15f, labelRect.offsetMin.y);
        // Reserve space on the right for the two buttons.
        labelRect.offsetMax = new Vector2(-130f, labelRect.offsetMax.y);

        // Power ON/OFF toggle button (far top-right)
        powerButton = CreateButton("PowerButton", panel, new Vector2(-10f, -8f), new Vector2(56f, 26f),
            OnColor, "ON", out powerLabel);
        powerButton.onClick.AddListener(ToggleActive);

        // Reset button (left of the power button)
        Button resetButton = CreateButton("ResetButton", panel, new Vector2(-72f, -8f), new Vector2(50f, 26f),
            new Color(0.25f, 0.25f, 0.25f, 1f), "x1", out _);
        resetButton.onClick.AddListener(ResetScale);

        // Slider
        RectTransform sliderRect = CreateRect("TimeScaleSlider", panel);
        sliderRect.anchorMin = new Vector2(0f, 0f);
        sliderRect.anchorMax = new Vector2(1f, 0f);
        sliderRect.pivot = new Vector2(0.5f, 0f);
        sliderRect.anchoredPosition = new Vector2(0f, 18f);
        sliderRect.sizeDelta = new Vector2(-30f, 24f);
        slider = sliderRect.gameObject.AddComponent<PointerOnlySlider>();
        sliderGroup = sliderRect.gameObject.AddComponent<CanvasGroup>();

        // Background
        RectTransform bg = CreateRect("Background", sliderRect);
        bg.anchorMin = new Vector2(0f, 0.35f);
        bg.anchorMax = new Vector2(1f, 0.65f);
        bg.sizeDelta = Vector2.zero;
        Image bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = new Color(0.15f, 0.15f, 0.15f, 1f);

        // Fill
        RectTransform fillArea = CreateRect("Fill Area", sliderRect);
        fillArea.anchorMin = new Vector2(0f, 0.35f);
        fillArea.anchorMax = new Vector2(1f, 0.65f);
        fillArea.sizeDelta = Vector2.zero;
        RectTransform fill = CreateRect("Fill", fillArea);
        fill.sizeDelta = Vector2.zero;
        Image fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.color = new Color(0.2f, 0.7f, 1f, 1f);
        slider.fillRect = fill;

        // Handle
        RectTransform handleArea = CreateRect("Handle Slide Area", sliderRect);
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.sizeDelta = Vector2.zero;
        RectTransform handle = CreateRect("Handle", handleArea);
        handle.sizeDelta = new Vector2(18f, 0f);
        Image handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.color = Color.white;
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;

        slider.minValue = minScale;
        slider.maxValue = maxScale;
        slider.value = currentScale;
        // Remove keyboard / gamepad focus navigation entirely.
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        slider.onValueChanged.AddListener(OnSliderChanged);

        EnsureEventSystem();
    }

    private Button CreateButton(string name, Transform parent, Vector2 anchoredPosition, Vector2 size,
        Color background, string text, out Text label)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        Image img = rect.gameObject.AddComponent<Image>();
        img.color = background;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        RectTransform labelRect = CreateRect("Label", rect);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.sizeDelta = Vector2.zero;
        label = labelRect.gameObject.AddComponent<Text>();
        label.font = GetDefaultFont();
        label.fontSize = 16;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleCenter;
        label.text = text;
        return button;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static Font GetDefaultFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
        return font;
    }

    private static void EnsureEventSystem()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
#else
        if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
#endif
    }

    /// <summary>
    /// Slider that ignores keyboard / gamepad directional input so its value
    /// can only be changed by dragging with the pointer.
    /// </summary>
    private class PointerOnlySlider : Slider
    {
        public override void OnMove(AxisEventData eventData)
        {
            // Intentionally swallow directional moves (arrow keys, WASD, dpad).
        }
    }
}
