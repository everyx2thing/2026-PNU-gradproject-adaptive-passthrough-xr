using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PassthroughFeatureTogglePanel : MonoBehaviour
{
    [SerializeField] private SelectivePassthroughController presentation;
    [SerializeField] private Button staticToggleButton;
    [SerializeField] private Button dynamicToggleButton;
    [SerializeField] private TMP_Text staticButtonText;
    [SerializeField] private TMP_Text dynamicButtonText;
    [SerializeField] private Color enabledColor =
        new Color(0.10f, 0.48f, 0.28f, 0.95f);
    [SerializeField] private Color disabledColor =
        new Color(0.52f, 0.16f, 0.16f, 0.95f);

    private bool listenersBound;

    private void Awake()
    {
        ResolveReference();
        BindListeners();
        Refresh();
    }

    private void OnEnable()
    {
        ResolveReference();
        BindListeners();
        Refresh();
    }

    private void OnDisable()
    {
        UnbindListeners();
    }

    public void Configure(
        SelectivePassthroughController presentationController,
        Button staticButton,
        TMP_Text staticLabel,
        Button dynamicButton,
        TMP_Text dynamicLabel)
    {
        presentation = presentationController;
        staticToggleButton = staticButton;
        staticButtonText = staticLabel;
        dynamicToggleButton = dynamicButton;
        dynamicButtonText = dynamicLabel;
        ResolveReference();
        Refresh();
    }

    public void ToggleStatic()
    {
        ResolveReference();
        if (presentation == null)
        {
            return;
        }

        presentation.ToggleStaticFeature();
        Debug.Log(
            "[PassthroughTest] Static feature "
            + (presentation.StaticFeatureEnabled ? "ON" : "OFF"));
        Refresh();
    }

    public void ToggleDynamic()
    {
        ResolveReference();
        if (presentation == null)
        {
            return;
        }

        presentation.ToggleDynamicFeature();
        Debug.Log(
            "[PassthroughTest] Dynamic feature "
            + (presentation.DynamicFeatureEnabled ? "ON" : "OFF"));
        Refresh();
    }

    public void Refresh()
    {
        ResolveReference();
        bool staticEnabled =
            presentation == null || presentation.StaticFeatureEnabled;
        bool dynamicEnabled =
            presentation == null || presentation.DynamicFeatureEnabled;

        SetButtonState(
            staticToggleButton,
            staticButtonText,
            "STATIC",
            staticEnabled);
        SetButtonState(
            dynamicToggleButton,
            dynamicButtonText,
            "DYNAMIC",
            dynamicEnabled);
    }

    private void BindListeners()
    {
        if (listenersBound)
        {
            return;
        }

        if (staticToggleButton != null)
        {
            staticToggleButton.onClick.AddListener(ToggleStatic);
        }

        if (dynamicToggleButton != null)
        {
            dynamicToggleButton.onClick.AddListener(ToggleDynamic);
        }

        listenersBound = true;
    }

    private void UnbindListeners()
    {
        if (!listenersBound)
        {
            return;
        }

        if (staticToggleButton != null)
        {
            staticToggleButton.onClick.RemoveListener(ToggleStatic);
        }

        if (dynamicToggleButton != null)
        {
            dynamicToggleButton.onClick.RemoveListener(ToggleDynamic);
        }

        listenersBound = false;
    }

    private void SetButtonState(
        Button button,
        TMP_Text label,
        string featureName,
        bool enabled)
    {
        if (label != null)
        {
            label.text =
                featureName
                + " PASSTHROUGH: "
                + (enabled ? "ON" : "OFF");
        }

        if (button == null)
        {
            return;
        }

        Color color = enabled ? enabledColor : disabledColor;
        if (button.targetGraphic != null)
        {
            button.targetGraphic.color = color;
        }

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
        colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
    }

    private void ResolveReference()
    {
        if (presentation == null)
        {
            presentation =
                FindAnyObjectByType<SelectivePassthroughController>();
        }
    }
}
