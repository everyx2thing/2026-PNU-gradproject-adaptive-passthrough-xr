using System;
using System.Collections.Generic;
using System.IO;
using TeamVR.AdaptivePassthrough;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(710)]
[DisallowMultipleComponent]
public sealed class PersonalizationRuntimePanel : MonoBehaviour
{
    private const string PanelOpacityPreferenceKey =
        "TeamVR.AdaptivePassthrough.PanelOpacity";
    private const string RuntimeSettingsPreferenceKey =
        "TeamVR.AdaptivePassthrough.RuntimePanelSettings.v2";
    private const string LegacyRuntimeSettingsPreferenceKey =
        "TeamVR.AdaptivePassthrough.RuntimePanelSettings.v1";
    private const int RuntimeSettingsVersion = 2;

    [Serializable]
    private sealed class RuntimePanelSettings
    {
        public int Version = RuntimeSettingsVersion;
        public int SelectedPage;
        public bool StaticFeatureEnabled = true;
        public bool DynamicFeatureEnabled = true;
        public bool MlEnabled;
        public int TrackingProfile;
        public int FeedbackMode;
    }

    private sealed class SliderBinding
    {
        public Slider Slider;
        public TMP_Text ValueText;
        public Func<float> Getter;
        public string Format;
    }

    private sealed class ToggleBinding
    {
        public Button Button;
        public TMP_Text Label;
        public Func<bool> Getter;
        public string Name;
    }

    private sealed class StatusChip
    {
        public Image Background;
        public TMP_Text Text;
    }

    private sealed class MetricTile
    {
        public Image Background;
        public TMP_Text Label;
        public TMP_Text Value;
    }

    private sealed class RiskBar
    {
        public Image Fill;
        public TMP_Text Value;
    }

    [SerializeField] private PersonalizationRuntimeController personalization;
    [SerializeField] private StaticPassthroughPolicyController staticPolicy;
    [SerializeField] private DynamicPassthroughPolicyController dynamicPolicy;
    [SerializeField] private SelectivePassthroughController presentation;
    [SerializeField] private TrackingQualityController trackingQuality;
    [SerializeField] private WorldSpacePanelPlacementController panelPlacement;
    [SerializeField, Min(1f)] private float refreshRateHz = 5f;
    [SerializeField, Range(0.35f, 1f)] private float panelOpacity = 1f;

    private readonly List<SliderBinding> sliderBindings =
        new List<SliderBinding>();
    private readonly List<ToggleBinding> toggleBindings =
        new List<ToggleBinding>();
    private readonly List<GameObject> pages = new List<GameObject>();
    private TMP_Text headerStatusText;
    private StatusChip modeChip;
    private StatusChip sessionChip;
    private StatusChip modelChip;
    private StatusChip probabilityChip;
    private StatusChip appliedChip;
    private StatusChip staticStateChip;
    private StatusChip staticEmergencyChip;
    private StatusChip dynamicStateChip;
    private StatusChip inferenceChip;
    private StatusChip scenarioChip;
    private RiskBar headRiskBar;
    private RiskBar handRiskBar;
    private RiskBar combinedRiskBar;
    private RiskBar dynamicRiskBar;
    private MetricTile staticDistanceTile;
    private MetricTile staticTtcTile;
    private MetricTile staticUserTile;
    private MetricTile staticCauseTile;
    private MetricTile dynamicPeopleTile;
    private MetricTile dynamicDistanceTile;
    private MetricTile dynamicClosingTile;
    private MetricTile dynamicTtcTile;
    private MetricTile dynamicTrackTile;
    private readonly MetricTile[] featureTiles = new MetricTile[7];
    private MetricTile stableThresholdTile;
    private MetricTile rapidThresholdTile;
    private MetricTile handThresholdTile;
    private MetricTile headSpeedTile;
    private MetricTile eventTile;
    private MetricTile logTile;
    private MetricTile trackingSpatialTile;
    private MetricTile trackingSourceTile;
    private MetricTile trackingPersonTile;
    private MetricTile trackingDistanceTile;
    private MetricTile trackingInferenceTile;
    private MetricTile trackingFrameTile;
    private MetricTile trackingThresholdTile;
    private Button feedbackModeButton;
    private TMP_Text feedbackModeLabel;
    private CanvasGroup panelCanvasGroup;
    private TMP_Text featureHelpText;
    private TMP_Text thresholdHelpText;
    private string settingsNotice;
    private Color settingsNoticeColor = EnabledColor;
    private double settingsNoticeUntil;
    private double nextRefreshAt;
    private bool refreshing;
    private int currentPage;

    private static readonly Color PanelColor =
        new Color(0.025f, 0.035f, 0.055f, 0.96f);
    private static readonly Color PageColor =
        new Color(0.045f, 0.060f, 0.085f, 0.95f);
    private static readonly Color CardColor =
        new Color(0.065f, 0.085f, 0.115f, 0.98f);
    private static readonly Color TileColor =
        new Color(0.085f, 0.115f, 0.15f, 0.98f);
    private static readonly Color ButtonColor =
        new Color(0.12f, 0.26f, 0.42f, 0.98f);
    private static readonly Color EnabledColor =
        new Color(0.08f, 0.47f, 0.27f, 0.98f);
    private static readonly Color DisabledColor =
        new Color(0.48f, 0.15f, 0.16f, 0.98f);
    private static readonly Color WarningColor =
        new Color(0.72f, 0.43f, 0.08f, 0.98f);
    private static readonly Color AccentColor =
        new Color(0.08f, 0.58f, 0.86f, 0.98f);
    private static readonly Color MutedColor =
        new Color(0.22f, 0.27f, 0.34f, 0.98f);

    private void Awake()
    {
        panelOpacity = Mathf.Clamp(
            PlayerPrefs.GetFloat(
                PanelOpacityPreferenceKey,
                panelOpacity),
            0.35f,
            1f);
        ResolveReferences();
        LoadRuntimeSettings();
        ResolvePanelCanvasGroup();
        BuildUi();
        ApplyPanelOpacity(panelOpacity, false);
        Refresh();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (personalization != null)
        {
            personalization.SnapshotUpdated += Refresh;
        }
    }

    private void OnDisable()
    {
        if (personalization != null)
        {
            personalization.SnapshotUpdated -= Refresh;
        }

        PlayerPrefs.Save();
    }

    private void LateUpdate()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (now < nextRefreshAt)
        {
            return;
        }

        nextRefreshAt = now + 1.0 / Mathf.Max(1f, refreshRateHz);
        Refresh();
    }

    public void Configure(
        PersonalizationRuntimeController runtime,
        StaticPassthroughPolicyController staticController,
        DynamicPassthroughPolicyController dynamicController,
        SelectivePassthroughController presentationController,
        TrackingQualityController qualityController = null,
        WorldSpacePanelPlacementController placementController = null)
    {
        if (personalization != null)
        {
            personalization.SnapshotUpdated -= Refresh;
        }

        personalization = runtime;
        staticPolicy = staticController;
        dynamicPolicy = dynamicController;
        presentation = presentationController;
        trackingQuality = qualityController;
        panelPlacement = placementController;
        if (isActiveAndEnabled && personalization != null)
        {
            personalization.SnapshotUpdated += Refresh;
        }
    }

    public void Refresh()
    {
        if (refreshing)
        {
            return;
        }

        refreshing = true;
        ResolveReferences();
        RefreshHeader();
        RefreshLivePage();
        RefreshHelpText();

        for (int i = 0; i < sliderBindings.Count; i++)
        {
            SliderBinding binding = sliderBindings[i];
            float value = binding.Getter == null ? 0f : binding.Getter();
            binding.Slider.SetValueWithoutNotify(value);
            binding.ValueText.text = value.ToString(binding.Format);
        }

        for (int i = 0; i < toggleBindings.Count; i++)
        {
            ToggleBinding binding = toggleBindings[i];
            bool enabled = binding.Getter != null && binding.Getter();
            binding.Label.text = binding.Name + ": "
                + (enabled ? "ON" : "OFF");
            Image image = binding.Button.targetGraphic as Image;
            if (image != null)
            {
                image.color = enabled ? EnabledColor : DisabledColor;
            }
        }

        if (feedbackModeButton != null && feedbackModeLabel != null)
        {
            bool passthrough = presentation == null
                || presentation.FeedbackMode
                    == SafetyFeedbackMode.Passthrough;
            feedbackModeLabel.text = passthrough
                ? "OUTPUT: PASSTHROUGH"
                : "OUTPUT: RED BORDER + VIBRATION";
            Image image = feedbackModeButton.targetGraphic as Image;
            if (image != null)
            {
                image.color = passthrough ? AccentColor : WarningColor;
            }
        }

        refreshing = false;
    }

    private void BuildUi()
    {
        sliderBindings.Clear();
        toggleBindings.Clear();
        pages.Clear();

        Transform existing = transform.Find("GeneratedDashboard");
        if (existing != null)
        {
            if (Application.isPlaying)
            {
                Destroy(existing.gameObject);
            }
            else
            {
                DestroyImmediate(existing.gameObject);
            }
        }

        GameObject dashboard = CreateRect(
            "GeneratedDashboard",
            transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        Image dashboardImage = dashboard.AddComponent<Image>();
        dashboardImage.color = PanelColor;
        dashboardImage.raycastTarget = false;

        TMP_Text title = CreateText(
            dashboard.transform,
            "DashboardTitle",
            new Vector2(0.02f, 0.925f),
            new Vector2(0.245f, 0.99f),
            20f,
            TextAlignmentOptions.MidlineLeft);
        title.text = "ADAPTIVE LAB";
        title.fontStyle = FontStyles.Bold;
        title.color = new Color(0.72f, 0.90f, 1f);
        GameObject dragHandle = CreateRect(
            "PanelDragHandle",
            dashboard.transform,
            new Vector2(0.02f, 0.925f),
            new Vector2(0.245f, 0.99f),
            Vector2.zero,
            Vector2.zero);
        Image dragImage = dragHandle.AddComponent<Image>();
        dragImage.color = new Color(0f, 0f, 0f, 0f);
        dragImage.raycastTarget = true;
        WorldSpacePanelDragHandle drag =
            dragHandle.AddComponent<WorldSpacePanelDragHandle>();
        drag.Configure(panelPlacement);

        modeChip = CreateStatusChip(
            dashboard.transform, "Mode", 0.255f, 0.395f);
        sessionChip = CreateStatusChip(
            dashboard.transform, "Session", 0.405f, 0.535f);
        modelChip = CreateStatusChip(
            dashboard.transform, "Model", 0.545f, 0.685f);
        probabilityChip = CreateStatusChip(
            dashboard.transform, "Probability", 0.695f, 0.835f);
        appliedChip = CreateStatusChip(
            dashboard.transform, "Applied", 0.845f, 0.98f);

        headerStatusText = CreateText(
            dashboard.transform,
            "RuntimeStatus",
            new Vector2(0.02f, 0.855f),
            new Vector2(0.34f, 0.918f),
            17f,
            TextAlignmentOptions.MidlineLeft);
        headerStatusText.color = new Color(0.72f, 0.78f, 0.86f);

        CreateActionButton(
            dashboard.transform,
            "LIVE",
            new Vector2(0.35f, 0.855f),
            new Vector2(0.405f, 0.918f),
            () => SetPage(0),
            AccentColor);
        CreateActionButton(
            dashboard.transform,
            "PANEL",
            new Vector2(0.41f, 0.855f),
            new Vector2(0.465f, 0.918f),
            () => SetPage(1),
            AccentColor);
        CreateActionButton(
            dashboard.transform,
            "SAVE SETTINGS",
            new Vector2(0.47f, 0.855f),
            new Vector2(0.57f, 0.918f),
            SaveRuntimeSettings,
            ButtonColor);
        CreateActionButton(
            dashboard.transform,
            "RESET SETTINGS",
            new Vector2(0.575f, 0.855f),
            new Vector2(0.68f, 0.918f),
            ResetRuntimeSettings,
            WarningColor);

        TMP_Text opacityLabel = CreateText(
            dashboard.transform,
            "PanelOpacityLabel",
            new Vector2(0.685f, 0.855f),
            new Vector2(0.79f, 0.918f),
            14f,
            TextAlignmentOptions.MidlineRight);
        opacityLabel.text = "PANEL OPACITY";
        opacityLabel.fontStyle = FontStyles.Bold;
        opacityLabel.color = new Color(0.62f, 0.76f, 0.88f);
        Slider opacitySlider = CreateSlider(
            dashboard.transform,
            new Vector2(0.79f, 0.86f),
            new Vector2(0.925f, 0.915f),
            0.35f,
            1f);
        TMP_Text opacityValue = CreateText(
            dashboard.transform,
            "PanelOpacityValue",
            new Vector2(0.93f, 0.855f),
            new Vector2(0.98f, 0.918f),
            15f,
            TextAlignmentOptions.MidlineRight);
        opacityValue.fontStyle = FontStyles.Bold;
        opacitySlider.SetValueWithoutNotify(panelOpacity);
        opacityValue.text = panelOpacity.ToString("F2");
        opacitySlider.onValueChanged.AddListener(value =>
        {
            if (refreshing)
            {
                return;
            }

            opacityValue.text = value.ToString("F2");
            ApplyPanelOpacity(value, true);
        });
        sliderBindings.Add(new SliderBinding
        {
            Slider = opacitySlider,
            ValueText = opacityValue,
            Getter = () => panelOpacity,
            Format = "F2"
        });

        GameObject livePage = CreatePage(dashboard.transform, "LivePage");
        livePage.GetComponent<RectTransform>().anchorMax =
            new Vector2(0.99f, 0.835f);
        pages.Add(livePage);

        BuildLivePage(livePage.transform);
        GameObject panelPage = CreatePage(
            dashboard.transform,
            "PanelPlacementPage");
        pages.Add(panelPage);
        BuildPanelPlacementPage(panelPage.transform);
        currentPage = 0;
        SetPage(0);
    }

    private void BuildPanelPlacementPage(Transform parent)
    {
        GameObject card = CreateCard(
            parent,
            "PanelPlacementCard",
            new Vector2(0.04f, 0.08f),
            new Vector2(0.96f, 0.94f),
            "WORLD PANEL PLACEMENT");
        CreateInfoStrip(
            card.transform,
            "PlacementHelp",
            "WORLD-LOCKED · DRAG THE ADAPTIVE LAB TITLE · HOLD LEFT Y FOR 1 SECOND TO RECOVER",
            new Vector2(0.04f, 0.78f),
            new Vector2(0.96f, 0.90f));

        CreatePanelPlacementSlider(
            card.transform,
            "DISTANCE",
            0.60f,
            WorldSpacePanelPlacementController.MinimumDistanceMeters,
            WorldSpacePanelPlacementController.MaximumDistanceMeters,
            () => panelPlacement == null ? 1f : panelPlacement.DistanceMeters,
            value => panelPlacement?.SetDistance(value),
            "F2");
        CreatePanelPlacementSlider(
            card.transform,
            "HEIGHT",
            0.42f,
            WorldSpacePanelPlacementController.MinimumHeightMeters,
            WorldSpacePanelPlacementController.MaximumHeightMeters,
            () => panelPlacement == null ? -0.05f : panelPlacement.HeightMeters,
            value => panelPlacement?.SetHeight(value),
            "F2");
        CreatePanelPlacementSlider(
            card.transform,
            "SCALE",
            0.24f,
            WorldSpacePanelPlacementController.MinimumScale,
            WorldSpacePanelPlacementController.MaximumScale,
            () => panelPlacement == null ? 1f : panelPlacement.ScaleMultiplier,
            value => panelPlacement?.SetScaleMultiplier(value),
            "F2");

        CreateActionButton(
            card.transform,
            "FACE ME",
            new Vector2(0.05f, 0.05f),
            new Vector2(0.31f, 0.17f),
            () => panelPlacement?.FaceMe(),
            ButtonColor);
        CreateActionButton(
            card.transform,
            "BRING HERE",
            new Vector2(0.37f, 0.05f),
            new Vector2(0.63f, 0.17f),
            () => panelPlacement?.BringHere(),
            AccentColor);
        CreateActionButton(
            card.transform,
            "RESET PANEL",
            new Vector2(0.69f, 0.05f),
            new Vector2(0.95f, 0.17f),
            () => panelPlacement?.ResetPanel(),
            WarningColor);
    }

    private void CreatePanelPlacementSlider(
        Transform parent,
        string label,
        float centerY,
        float minimum,
        float maximum,
        Func<float> getter,
        Action<float> setter,
        string format)
    {
        TMP_Text labelText = CreateText(
            parent,
            label + "Label",
            new Vector2(0.06f, centerY),
            new Vector2(0.25f, centerY + 0.11f),
            20f,
            TextAlignmentOptions.MidlineLeft);
        labelText.text = label;
        labelText.fontStyle = FontStyles.Bold;
        Slider slider = CreateSlider(
            parent,
            new Vector2(0.26f, centerY + 0.02f),
            new Vector2(0.82f, centerY + 0.09f),
            minimum,
            maximum);
        TMP_Text valueText = CreateText(
            parent,
            label + "Value",
            new Vector2(0.84f, centerY),
            new Vector2(0.95f, centerY + 0.11f),
            20f,
            TextAlignmentOptions.MidlineRight);
        slider.onValueChanged.AddListener(value =>
        {
            if (!refreshing)
            {
                setter?.Invoke(value);
            }
        });
        sliderBindings.Add(new SliderBinding
        {
            Slider = slider,
            ValueText = valueText,
            Getter = getter,
            Format = format
        });
    }

    private void BuildLivePage(Transform parent)
    {
        GameObject staticCard = CreateCard(
            parent,
            "StaticSafetyCard",
            new Vector2(0.015f, 0.49f),
            new Vector2(0.493f, 0.985f),
            "STATIC SAFETY");
        staticStateChip = CreateStatusChip(
            staticCard.transform,
            "StaticState",
            new Vector2(0.57f, 0.81f),
            new Vector2(0.76f, 0.965f));
        staticEmergencyChip = CreateStatusChip(
            staticCard.transform,
            "Emergency",
            new Vector2(0.77f, 0.81f),
            new Vector2(0.97f, 0.965f));
        headRiskBar = CreateRiskBar(
            staticCard.transform,
            "HEAD",
            new Vector2(0.035f, 0.63f),
            new Vector2(0.965f, 0.78f));
        handRiskBar = CreateRiskBar(
            staticCard.transform,
            "HAND",
            new Vector2(0.035f, 0.47f),
            new Vector2(0.965f, 0.62f));
        combinedRiskBar = CreateRiskBar(
            staticCard.transform,
            "COMBINED",
            new Vector2(0.035f, 0.31f),
            new Vector2(0.965f, 0.46f));
        staticDistanceTile = CreateMetricTile(
            staticCard.transform, "StaticDistance", "DISTANCE",
            0.035f, 0.265f, 0.04f, 0.27f);
        staticTtcTile = CreateMetricTile(
            staticCard.transform, "StaticTtc", "TTC",
            0.275f, 0.495f, 0.04f, 0.27f);
        staticUserTile = CreateMetricTile(
            staticCard.transform, "StaticUser", "USER STATE",
            0.505f, 0.725f, 0.04f, 0.27f);
        staticCauseTile = CreateMetricTile(
            staticCard.transform, "StaticCause", "CAUSE",
            0.735f, 0.965f, 0.04f, 0.27f);

        GameObject dynamicCard = CreateCard(
            parent,
            "DynamicPersonCard",
            new Vector2(0.507f, 0.49f),
            new Vector2(0.985f, 0.985f),
            "DYNAMIC PERSON");
        dynamicStateChip = CreateStatusChip(
            dynamicCard.transform,
            "DynamicState",
            new Vector2(0.72f, 0.81f),
            new Vector2(0.97f, 0.965f));
        dynamicRiskBar = CreateRiskBar(
            dynamicCard.transform,
            "RISK",
            new Vector2(0.035f, 0.60f),
            new Vector2(0.965f, 0.76f));
        dynamicPeopleTile = CreateMetricTile(
            dynamicCard.transform, "DynamicPeople", "PEOPLE",
            0.035f, 0.215f, 0.31f, 0.55f);
        dynamicDistanceTile = CreateMetricTile(
            dynamicCard.transform, "DynamicDistance", "DISTANCE",
            0.225f, 0.405f, 0.31f, 0.55f);
        dynamicClosingTile = CreateMetricTile(
            dynamicCard.transform, "DynamicClosing", "CLOSING",
            0.415f, 0.595f, 0.31f, 0.55f);
        dynamicTtcTile = CreateMetricTile(
            dynamicCard.transform, "DynamicTtc", "TTC",
            0.605f, 0.785f, 0.31f, 0.55f);
        dynamicTrackTile = CreateMetricTile(
            dynamicCard.transform, "DynamicTrack", "TRACK",
            0.795f, 0.965f, 0.31f, 0.55f);
        CreateInfoStrip(
            dynamicCard.transform,
            "DynamicGuardrail",
            "CRITICAL PERSON OVERRIDE REMAINS SAFETY-LOCKED",
            new Vector2(0.035f, 0.06f),
            new Vector2(0.965f, 0.26f));

        GameObject mlCard = CreateCard(
            parent,
            "TrackingQualityCard",
            new Vector2(0.015f, 0.19f),
            new Vector2(0.985f, 0.47f),
            "TRACKING QUALITY");
        inferenceChip = CreateStatusChip(
            mlCard.transform,
            "TrackingProfile",
            new Vector2(0.77f, 0.72f),
            new Vector2(0.98f, 0.96f));
        scenarioChip = CreateStatusChip(
            mlCard.transform,
            "TrackingScenario",
            new Vector2(0.56f, 0.81f),
            new Vector2(0.76f, 0.96f));
        CreateActionButton(
            mlCard.transform, "BALANCED",
            new Vector2(0.02f, 0.54f), new Vector2(0.17f, 0.76f),
            () => trackingQuality?.SetProfile(
                TrackingQualityProfile.Balanced), ButtonColor);
        CreateActionButton(
            mlCard.transform, "ACCURACY",
            new Vector2(0.18f, 0.54f), new Vector2(0.33f, 0.76f),
            () => trackingQuality?.SetProfile(
                TrackingQualityProfile.Accuracy), ButtonColor);
        CreateActionButton(
            mlCard.transform, "PERFORMANCE",
            new Vector2(0.34f, 0.54f), new Vector2(0.51f, 0.76f),
            () => trackingQuality?.SetProfile(
                TrackingQualityProfile.Performance), ButtonColor);
        CreateActionButton(
            mlCard.transform, "OBJECT APPROACH",
            new Vector2(0.52f, 0.54f), new Vector2(0.67f, 0.76f),
            () => trackingQuality?.AddTestMarker("object_approach"),
            MutedColor);
        CreateActionButton(
            mlCard.transform, "PERSON APPROACH",
            new Vector2(0.68f, 0.54f), new Vector2(0.83f, 0.76f),
            () => trackingQuality?.AddTestMarker("person_approach"),
            MutedColor);
        CreateActionButton(
            mlCard.transform, "PERSON RECEDE",
            new Vector2(0.84f, 0.54f), new Vector2(0.98f, 0.76f),
            () => trackingQuality?.AddTestMarker("person_recede"),
            MutedColor);

        CreateActionButton(
            mlCard.transform, "3.0m",
            new Vector2(0.02f, 0.39f), new Vector2(0.15f, 0.51f),
            () => trackingQuality?.AddGroundTruthMarker(3.0f), MutedColor);
        CreateActionButton(
            mlCard.transform, "2.0m",
            new Vector2(0.155f, 0.39f), new Vector2(0.285f, 0.51f),
            () => trackingQuality?.AddGroundTruthMarker(2.0f), MutedColor);
        CreateActionButton(
            mlCard.transform, "1.5m",
            new Vector2(0.29f, 0.39f), new Vector2(0.42f, 0.51f),
            () => trackingQuality?.AddGroundTruthMarker(1.5f), MutedColor);
        CreateActionButton(
            mlCard.transform, "1.0m",
            new Vector2(0.425f, 0.39f), new Vector2(0.555f, 0.51f),
            () => trackingQuality?.AddGroundTruthMarker(1.0f), MutedColor);
        CreateActionButton(
            mlCard.transform, "0.6m",
            new Vector2(0.56f, 0.39f), new Vector2(0.69f, 0.51f),
            () => trackingQuality?.AddGroundTruthMarker(0.6f), WarningColor);
        CreateActionButton(
            mlCard.transform, "0.25m",
            new Vector2(0.695f, 0.39f), new Vector2(0.825f, 0.51f),
            () => trackingQuality?.AddGroundTruthMarker(0.25f), WarningColor);
        CreateActionButton(
            mlCard.transform, "END",
            new Vector2(0.83f, 0.39f), new Vector2(0.98f, 0.51f),
            () => trackingQuality?.EndTestScenario(), DisabledColor);

        trackingThresholdTile = CreateMetricTile(
            mlCard.transform, "TrackingThresholds", "ML RISK THRESHOLDS",
            0.02f, 0.16f, 0.04f, 0.36f);
        trackingSpatialTile = CreateMetricTile(
            mlCard.transform, "TrackingSpatial", "SPATIAL HZ / MS",
            0.17f, 0.30f, 0.04f, 0.36f);
        trackingSourceTile = CreateMetricTile(
            mlCard.transform, "TrackingSource", "SOURCE / DIST / CONF",
            0.31f, 0.45f, 0.04f, 0.36f);
        trackingPersonTile = CreateMetricTile(
            mlCard.transform, "TrackingPerson", "PERSON ID / MOTION / MISS",
            0.46f, 0.59f, 0.04f, 0.36f);
        trackingDistanceTile = CreateMetricTile(
            mlCard.transform, "TrackingDistance", "RAW / FILTERED",
            0.60f, 0.72f, 0.04f, 0.36f);
        trackingInferenceTile = CreateMetricTile(
            mlCard.transform, "TrackingInference", "INFER HZ / MS",
            0.73f, 0.85f, 0.04f, 0.36f);
        trackingFrameTile = CreateMetricTile(
            mlCard.transform, "TrackingFrame", "FPS / P95",
            0.86f, 0.98f, 0.04f, 0.36f);
        MakeCompact(trackingThresholdTile);
        MakeCompact(trackingSpatialTile);
        MakeCompact(trackingSourceTile);
        MakeCompact(trackingPersonTile);
        MakeCompact(trackingDistanceTile);
        MakeCompact(trackingInferenceTile);
        MakeCompact(trackingFrameTile);

        CreateToggleButton(
            parent,
            "STATIC",
            new Vector2(0.02f, 0.03f),
            new Vector2(0.18f, 0.17f),
            () => presentation != null && presentation.StaticFeatureEnabled,
            () => presentation?.ToggleStaticFeature());
        CreateToggleButton(
            parent,
            "DYNAMIC",
            new Vector2(0.19f, 0.03f),
            new Vector2(0.35f, 0.17f),
            () => presentation != null && presentation.DynamicFeatureEnabled,
            () => presentation?.ToggleDynamicFeature());
        feedbackModeButton = CreateActionButton(
            parent,
            "OUTPUT: PASSTHROUGH",
            new Vector2(0.36f, 0.03f),
            new Vector2(0.62f, 0.17f),
            () => presentation?.ToggleFeedbackMode(),
            AccentColor);
        feedbackModeLabel =
            feedbackModeButton.GetComponentInChildren<TMP_Text>();
        CreateToggleButton(
            parent,
            "ML",
            new Vector2(0.63f, 0.03f),
            new Vector2(0.78f, 0.17f),
            () => personalization != null && personalization.MlEnabled,
            () => personalization?.ToggleMlEnabled());
        CreateActionButton(
            parent,
            "APPLY ML NOW",
            new Vector2(0.79f, 0.03f),
            new Vector2(0.98f, 0.17f),
            () => personalization?.ApplyModelNow(),
            AccentColor);
    }

    private void BuildFeaturePage(Transform parent)
    {
        CreateToggleButton(
            parent,
            "MANUAL FEATURES",
            new Vector2(0.02f, 0.84f),
            new Vector2(0.25f, 0.98f),
            () => personalization != null
                && personalization.ManualFeatureOverride,
            () =>
            {
                if (personalization != null)
                {
                    personalization.SetManualFeatureOverride(
                        !personalization.ManualFeatureOverride);
                }
            });
        CreateToggleButton(
            parent,
            "COLD START BYPASS",
            new Vector2(0.27f, 0.84f),
            new Vector2(0.50f, 0.98f),
            () => personalization != null
                && personalization.BypassColdStartForTesting,
            () =>
            {
                if (personalization != null)
                {
                    personalization.SetBypassColdStartForTesting(
                        !personalization.BypassColdStartForTesting);
                }
            });
        CreateToggleButton(
            parent,
            "AUTO INFERENCE",
            new Vector2(0.52f, 0.84f),
            new Vector2(0.74f, 0.98f),
            () => personalization != null
                && personalization.AutomaticInference,
            () =>
            {
                if (personalization != null)
                {
                    personalization.SetAutomaticInference(
                        !personalization.AutomaticInference);
                }
            });
        CreateActionButton(
            parent,
            "RUN WITH INPUTS",
            new Vector2(0.76f, 0.84f),
            new Vector2(0.98f, 0.98f),
            () => personalization?.RunNow(),
            ButtonColor);

        CreateFeatureSlider(
            parent, "f_pt", 0, 0, 0f, 1f, "F2", 0);
        CreateFeatureSlider(
            parent, "r_cancel", 0, 1, 0f, 1f, "F2", 1);
        CreateFeatureSlider(
            parent, "t_pt_bar (s)", 0, 2, 0f, 10f, "F2", 2);
        CreateFeatureSlider(
            parent, "v_h_bar (m/s)", 0, 3, 0f, 3f, "F2", 3);
        CreateFeatureSlider(
            parent, "v_h_max (m/s)", 1, 0, 0f, 5f, "F2", 4);
        CreateFeatureSlider(
            parent, "A_space_norm", 1, 1, 0f, 1f, "F2", 5);
        CreateFeatureSlider(
            parent, "T_session_norm", 1, 2, 0f, 1f, "F2", 6);
        CreateSliderRow(
            parent,
            "Room area (m2)",
            1,
            3,
            1f,
            20f,
            "F1",
            () => personalization == null
                ? 4f
                : personalization.SpaceAreaSquareMeters,
            value => personalization?.SetSpaceAreaSquareMeters(value));

        CreateActionButton(
            parent,
            "SESSION -",
            new Vector2(0.02f, 0.01f),
            new Vector2(0.18f, 0.13f),
            () =>
            {
                if (personalization != null)
                {
                    personalization.SetAccumulatedSessionCount(
                        personalization.AccumulatedSessionCount - 1);
                }
            },
            ButtonColor);
        CreateActionButton(
            parent,
            "SESSION +",
            new Vector2(0.20f, 0.01f),
            new Vector2(0.36f, 0.13f),
            () =>
            {
                if (personalization != null)
                {
                    personalization.SetAccumulatedSessionCount(
                        personalization.AccumulatedSessionCount + 1);
                }
            },
            ButtonColor);
        featureHelpText = CreateText(
            parent,
            "FeatureHelp",
            new Vector2(0.39f, 0.00f),
            new Vector2(0.98f, 0.14f),
            16f,
            TextAlignmentOptions.MidlineLeft);
    }

    private void BuildThresholdPage(Transform parent)
    {
        CreateToggleButton(
            parent,
            "SHADOW MODE",
            new Vector2(0.02f, 0.84f),
            new Vector2(0.24f, 0.98f),
            () => personalization != null && personalization.ShadowMode,
            () =>
            {
                if (personalization != null)
                {
                    personalization.SetShadowMode(
                        !personalization.ShadowMode);
                }
            });
        CreateToggleButton(
            parent,
            "ML AUTO APPLY",
            new Vector2(0.26f, 0.84f),
            new Vector2(0.48f, 0.98f),
            () => personalization != null
                && personalization.ApplyPersonalization,
            () =>
            {
                if (personalization != null)
                {
                    personalization.SetApplyPersonalization(
                        !personalization.ApplyPersonalization);
                }
            });
        CreateToggleButton(
            parent,
            "pNegative OVERRIDE",
            new Vector2(0.50f, 0.84f),
            new Vector2(0.73f, 0.98f),
            () => personalization != null
                && personalization.NegativeProbabilityOverride,
            () =>
            {
                if (personalization != null)
                {
                    personalization.SetNegativeProbabilityOverride(
                        !personalization.NegativeProbabilityOverride);
                }
            });
        CreateActionButton(
            parent,
            "SAFE DEFAULTS",
            new Vector2(0.75f, 0.84f),
            new Vector2(0.98f, 0.98f),
            () => personalization?.RestoreSafeDefaults(),
            WarningColor);

        CreateSliderRow(
            parent,
            "Stable ON",
            0,
            0,
            0.05f,
            0.95f,
            "F2",
            () => Preview().StableOnThreshold,
            value => SetPreview(value, null, null));
        CreateSliderRow(
            parent,
            "Rapid ON",
            0,
            1,
            0.05f,
            0.95f,
            "F2",
            () => Preview().RapidOnThreshold,
            value => SetPreview(null, value, null));
        CreateSliderRow(
            parent,
            "Hand full",
            0,
            2,
            0.05f,
            0.95f,
            "F2",
            () => Preview().HandFullThreshold,
            value => SetPreview(null, null, value));
        CreateSliderRow(
            parent,
            "pNegative test",
            0,
            3,
            0f,
            1f,
            "F2",
            () => personalization == null
                ? 0.5f
                : personalization.OverriddenNegativeProbability,
            value => personalization?
                .SetOverriddenNegativeProbability(value));
        CreateSliderRow(
            parent,
            "Adjustment scale",
            1,
            0,
            0f,
            1f,
            "F2",
            () => personalization == null
                ? 0.2f
                : personalization.AdjustmentScale,
            value => personalization?.SetAdjustmentScale(value));
        CreateSliderRow(
            parent,
            "Maximum threshold",
            1,
            1,
            0.65f,
            1f,
            "F2",
            () => personalization == null
                ? 0.95f
                : personalization.MaximumThreshold,
            value => personalization?.SetMaximumThreshold(value));

        CreateActionButton(
            parent,
            "APPLY SLIDERS",
            new Vector2(0.52f, 0.27f),
            new Vector2(0.74f, 0.42f),
            () => personalization?.ApplyThresholdPreview(),
            WarningColor);
        CreateActionButton(
            parent,
            "EVALUATE ML",
            new Vector2(0.76f, 0.27f),
            new Vector2(0.98f, 0.42f),
            () => personalization?.RunNow(),
            ButtonColor);
        thresholdHelpText = CreateText(
            parent,
            "ThresholdHelp",
            new Vector2(0.52f, 0.00f),
            new Vector2(0.98f, 0.24f),
            16f,
            TextAlignmentOptions.TopLeft);
    }

    private void CreateFeatureSlider(
        Transform parent,
        string label,
        int column,
        int row,
        float minimum,
        float maximum,
        string format,
        int featureIndex)
    {
        CreateSliderRow(
            parent,
            label,
            column,
            row,
            minimum,
            maximum,
            format,
            () => ManualFeatureValue(featureIndex),
            value => personalization?.SetManualFeature(featureIndex, value));
    }

    private void CreateSliderRow(
        Transform parent,
        string label,
        int column,
        int row,
        float minimum,
        float maximum,
        string format,
        Func<float> getter,
        Action<float> setter)
    {
        float left = column == 0 ? 0.02f : 0.52f;
        float right = column == 0 ? 0.48f : 0.98f;
        float top = 0.80f - row * 0.16f;
        float bottom = top - 0.13f;
        GameObject rowObject = CreateRect(
            label + " Row",
            parent,
            new Vector2(left, bottom),
            new Vector2(right, top),
            Vector2.zero,
            Vector2.zero);

        TMP_Text nameText = CreateText(
            rowObject.transform,
            "Name",
            new Vector2(0f, 0f),
            new Vector2(0.38f, 1f),
            17f,
            TextAlignmentOptions.MidlineLeft);
        nameText.text = label;
        Slider slider = CreateSlider(
            rowObject.transform,
            new Vector2(0.39f, 0.16f),
            new Vector2(0.83f, 0.84f),
            minimum,
            maximum);
        TMP_Text valueText = CreateText(
            rowObject.transform,
            "Value",
            new Vector2(0.85f, 0f),
            new Vector2(1f, 1f),
            17f,
            TextAlignmentOptions.MidlineRight);
        float initial = getter == null ? minimum : getter();
        slider.SetValueWithoutNotify(initial);
        valueText.text = initial.ToString(format);
        slider.onValueChanged.AddListener(value =>
        {
            if (refreshing)
            {
                return;
            }

            valueText.text = value.ToString(format);
            setter?.Invoke(value);
        });
        sliderBindings.Add(new SliderBinding
        {
            Slider = slider,
            ValueText = valueText,
            Getter = getter,
            Format = format
        });
    }

    private void RefreshHeader()
    {
        if (headerStatusText == null)
        {
            return;
        }

        if (personalization == null)
        {
            SetChip(modeChip, "MODE  UNAVAILABLE", DisabledColor);
            SetChip(sessionChip, "SESSION  --", MutedColor);
            SetChip(modelChip, "MODEL  MISSING", DisabledColor);
            SetChip(probabilityChip, "pRISK  --", MutedColor);
            SetChip(appliedChip, "APPLIED  NO", MutedColor);
            headerStatusText.text = "Personalization runtime component is missing.";
            headerStatusText.color = new Color(1f, 0.55f, 0.55f);
            return;
        }

        string mode = personalization.MlEnabled ? "ML ON" : "ML OFF";
        string probability = personalization.HasNegativeProbability
            ? personalization.LastNegativeProbability.ToString("F3")
            : "N/A";
        SetChip(
            modeChip,
            "MODE  " + mode,
            personalization.MlEnabled ? EnabledColor : MutedColor);
        SetChip(
            sessionChip,
            string.Format(
                "SESSION  {0}/{1}",
                personalization.AccumulatedSessionCount,
                personalization.MinimumSessionCount),
            personalization.LastRunWasColdStart
                ? WarningColor : EnabledColor);
        SetChip(
            modelChip,
            "MODEL  " + (personalization.ModelReady ? "READY" : "SOURCE"),
            personalization.ModelReady ? EnabledColor : WarningColor);
        SetChip(
            probabilityChip,
            "pRISK  " + probability,
            personalization.HasNegativeProbability
                ? AccentColor : MutedColor);
        SetChip(
            appliedChip,
            "APPLIED  " + AppliedStatus(),
            personalization.LastThresholdsApplied
                ? EnabledColor : MutedColor);
        headerStatusText.text = string.Format(
            "STATUS  {0}   ·   SOURCE  {1}   ·   INPUT  {2}",
            personalization.ModelReady ? "MODEL READY" : "SAFE DEFAULTS",
            personalization.LastInferenceSource,
            personalization.ManualFeatureOverride ? "MANUAL" : "LIVE");
        headerStatusText.color = personalization.ModelReady
            ? new Color(0.68f, 0.90f, 0.74f)
            : new Color(1f, 0.78f, 0.38f);

        if (!string.IsNullOrEmpty(settingsNotice)
            && Time.realtimeSinceStartupAsDouble < settingsNoticeUntil)
        {
            headerStatusText.text = settingsNotice;
            headerStatusText.color = settingsNoticeColor;
        }
        else
        {
            settingsNotice = null;
        }
    }

    private void RefreshLivePage()
    {
        if (headRiskBar == null || dynamicRiskBar == null)
        {
            return;
        }

        StaticPassthroughDecision staticDetail =
            staticPolicy == null ? null : staticPolicy.LatestStatic;
        StaticBoundaryRiskFrame staticFrame =
            staticPolicy == null || staticPolicy.FrameProvider == null
                ? null
                : staticPolicy.FrameProvider.CurrentStaticBoundaryFrame;
        float distance = staticFrame == null || !staticFrame.Available
            ? -1f
            : staticFrame.Head.ClosestDistanceMeters;
        string ttc = staticFrame != null
            && staticFrame.Available
            && staticFrame.Head.HasTimeToCollision
                ? staticFrame.Head.TimeToCollisionSeconds.ToString("F2") + " s"
                : "--";
        bool staticAvailable = staticFrame != null && staticFrame.Available;
        bool staticEnabled = staticDetail != null && staticDetail.Enabled;
        bool staticVisible = presentation != null
            && presentation.StaticWindowVisible;
        bool emergency = staticDetail != null
            && (staticDetail.EmergencyTrigger || staticDetail.EmergencyHold);
        SetChip(
            staticStateChip,
            !staticAvailable
                ? "NO DATA"
                : staticEnabled
                    ? staticVisible ? "ON · VISIBLE" : "ON · HIDDEN"
                    : "OFF",
            !staticAvailable
                ? MutedColor : staticEnabled ? EnabledColor : DisabledColor);
        SetChip(
            staticEmergencyChip,
            emergency ? "EMERGENCY" : "CLEAR",
            emergency ? DisabledColor : EnabledColor);
        SetRiskBar(
            headRiskBar,
            staticDetail == null ? 0f : staticDetail.HeadRisk,
            staticAvailable);
        SetRiskBar(
            handRiskBar,
            staticDetail == null ? 0f : staticDetail.HandRisk,
            staticAvailable);
        SetRiskBar(
            combinedRiskBar,
            staticDetail == null ? 0f : staticDetail.CombinedRisk,
            staticAvailable);
        SetMetric(
            staticDistanceTile,
            distance < 0f ? "--" : distance.ToString("F2") + " m");
        SetMetric(staticTtcTile, ttc);
        SetMetric(
            staticUserTile,
            staticDetail == null
                ? "--" : staticDetail.UserState01.ToString("F2"));
        SetMetric(
            staticCauseTile,
            staticDetail == null ? "--" : staticDetail.Cause.ToString(),
            emergency ? new Color(1f, 0.58f, 0.52f) : Color.white);

        RefreshDynamicCard();
        RefreshTrackingQualityCard();
    }

    private void RefreshTrackingQualityCard()
    {
        RefreshThresholdTile();
        if (trackingQuality == null)
        {
            SetChip(inferenceChip, "QUALITY MISSING", DisabledColor);
            SetChip(scenarioChip, "NO SCENARIO", MutedColor);
            SetMetric(trackingSpatialTile, "--");
            SetMetric(trackingSourceTile, "--");
            SetMetric(trackingPersonTile, "--");
            SetMetric(trackingDistanceTile, "--");
            SetMetric(trackingInferenceTile, "--");
            SetMetric(trackingFrameTile, "--");
            return;
        }

        TrackingDiagnosticsSnapshot snapshot = trackingQuality.GetSnapshot();
        SetChip(
            inferenceChip,
            snapshot.Profile.ToString().ToUpperInvariant()
                + (snapshot.AdaptiveLevel > 0
                    ? " -" + snapshot.AdaptiveLevel
                    : string.Empty),
            snapshot.AdaptiveLevel > 0 ? WarningColor : EnabledColor);
        SetChip(
            scenarioChip,
            string.IsNullOrEmpty(trackingQuality.ActiveScenario)
                ? "NO SCENARIO"
                : trackingQuality.ActiveScenario.ToUpperInvariant(),
            string.IsNullOrEmpty(trackingQuality.ActiveScenario)
                ? MutedColor
                : AccentColor);
        SetMetric(
            trackingSpatialTile,
            snapshot.SpatialRateHz.ToString("F1") + " / "
                + snapshot.SpatialMilliseconds.ToString("F1"));
        SetMetric(
            trackingSourceTile,
            FormatSpatialOwner("H", snapshot.HeadSpatial) + "\n"
                + FormatSpatialOwner("L", snapshot.LeftHandSpatial) + "\n"
                + FormatSpatialOwner("R", snapshot.RightHandSpatial));
        SetMetric(
            trackingPersonTile,
            "#" + snapshot.PersonTrackId + "  "
                + snapshot.PersonMotion + "  "
                + snapshot.PersonMissingSeconds.ToString("F2") + "s\n"
                + (snapshot.PersonMetricReliable
                    ? "DEPTH OK"
                    : string.IsNullOrEmpty(snapshot.PersonDepthRejectedReason)
                        ? "DEPTH --"
                        : snapshot.PersonDepthRejectedReason));
        SetMetric(
            trackingDistanceTile,
            snapshot.PersonRawDistanceMeters.ToString("F2") + " / "
                + snapshot.PersonFilteredDistanceMeters.ToString("F2")
                + " m");
        SetMetric(
            trackingInferenceTile,
            (snapshot.CameraReady ? "CAM OK " : "CAM -- ")
                + "target "
                + snapshot.TargetInferenceRateHz.ToString("F1")
                + "Hz / actual "
                + (snapshot.HasInferenceMeasurement
                    ? snapshot.MeasuredInferenceRateHz.ToString("F1")
                        + "Hz"
                    : "unavailable") + " "
                + snapshot.InferenceBackend + "\n"
                + snapshot.InferenceActiveMilliseconds.ToString("F1")
                + "/"
                + snapshot.InferenceWallMilliseconds.ToString("F0")
                + "ms " + snapshot.InferenceWatchdogState + "\n"
                + "raw/person " + snapshot.RawCandidateCount + "/"
                + snapshot.PersonCandidateCount + " reject "
                + snapshot.ClassRejectedCount + "/"
                + snapshot.ConfidenceRejectedCount + "/"
                + snapshot.BoxRejectedCount);
        SetMetric(
            trackingFrameTile,
            snapshot.FramesPerSecond.ToString("F0") + " / "
                + snapshot.FrameP95Milliseconds.ToString("F1") + "ms");
    }

    private static string FormatSpatialOwner(
        string label,
        SpatialOwnerDiagnostics value)
    {
        string environment = value.EnvironmentDistanceMeters >= 0f
            ? value.EnvironmentDistanceMeters.ToString("F2")
            : "--";
        string room = value.RoomSceneDistanceMeters >= 0f
            ? value.RoomSceneDistanceMeters.ToString("F2")
            : "--";
        string rejection = value.SelfRejected
            ? " !" + value.RejectionReason
            : string.Empty;
        return label + " D/R " + environment + "/" + room
            + " " + value.SelectedSource
            + " hit" + value.ValidRayHitCount
            + " o" + (value.ConfirmedOverlap ? "1" : "0")
            + " c" + value.Confidence.ToString("F2")
            + rejection;
    }

    private void RefreshThresholdTile()
    {
        if (trackingThresholdTile == null)
        {
            return;
        }

        float stable = staticPolicy == null
            ? PersonalizationMath.DefaultStableOnThreshold
            : staticPolicy.StableOnThreshold;
        float rapid = staticPolicy == null
            ? PersonalizationMath.DefaultRapidOnThreshold
            : staticPolicy.RapidOnThreshold;
        float hand = staticPolicy == null
            ? PersonalizationMath.DefaultHandFullThreshold
            : staticPolicy.HandFullThreshold;
        float dynamicOn = dynamicPolicy == null
            ? PersonalizationMath.DefaultDynamicOnThreshold
            : dynamicPolicy.OnThreshold;
        float dynamicOff = dynamicPolicy == null
            ? PersonalizationMath.DefaultDynamicOffThreshold
            : dynamicPolicy.OffThreshold;
        float delta = personalization == null
            ? 0f
            : personalization.LastThresholds.AdjustmentDelta;
        SetMetric(
            trackingThresholdTile,
            string.Format(
                "S .65>{0:F2} R .45>{1:F2} H .85>{2:F2}\n"
                + "D .60/.45>{3:F2}/{4:F2}  d+{5:F2}",
                stable,
                rapid,
                hand,
                dynamicOn,
                dynamicOff,
                delta));
    }

    private string AppliedStatus()
    {
        if (personalization == null
            || !personalization.LastThresholdsApplied)
        {
            return "NO";
        }

        return personalization.LastStaticThresholdsApplied
            && personalization.LastDynamicThresholdsApplied
                ? "FULL"
                : "PARTIAL";
    }

    private void RefreshDynamicCard()
    {
        DynamicRiskFrame frame = dynamicPolicy == null
            || dynamicPolicy.DynamicRiskController == null
                ? null
                : dynamicPolicy.DynamicRiskController.LatestFrame;
        PassthroughSourceDecision decision =
            dynamicPolicy == null ? null : dynamicPolicy.Latest;
        if (frame == null)
        {
            SetChip(dynamicStateChip, "NO CAMERA DATA", MutedColor);
            SetRiskBar(dynamicRiskBar, 0f, false);
            SetMetric(dynamicPeopleTile, "--");
            SetMetric(dynamicDistanceTile, "--");
            SetMetric(dynamicClosingTile, "--");
            SetMetric(dynamicTtcTile, "--");
            SetMetric(dynamicTrackTile, "--");
            return;
        }

        DynamicRiskAssessment primary = null;
        for (int i = 0; i < frame.Assessments.Count; i++)
        {
            if (primary == null
                || frame.Assessments[i].Score > primary.Score)
            {
                primary = frame.Assessments[i];
            }
        }

        bool enabled = decision != null && decision.Enabled;
        SetChip(
            dynamicStateChip,
            enabled ? "ON · VISIBLE" : "OFF",
            enabled ? EnabledColor : DisabledColor);
        SetRiskBar(dynamicRiskBar, frame.MaximumRisk, true);
        SetMetric(dynamicPeopleTile, frame.ConfirmedPersonCount.ToString());
        SetMetric(
            dynamicDistanceTile,
            primary != null && primary.Location.HasMetricDistance
                ? primary.Location.FilteredDistanceMeters.ToString("F2") + " m"
                : "--");
        SetMetric(
            dynamicClosingTile,
            primary != null && primary.Motion.HasMetricMotion
                ? primary.Motion.ClosingSpeedMetersPerSecond.ToString("F2")
                    + " m/s"
                : "--");
        SetMetric(
            dynamicTtcTile,
            primary != null && primary.Motion.MetricTtcSeconds.HasValue
                ? primary.Motion.MetricTtcSeconds.Value.ToString("F2") + " s"
                : "--");
        SetMetric(
            dynamicTrackTile,
            primary == null ? "--" : primary.TrackId.ToString());
    }

    private void RefreshHelpText()
    {
        if (featureHelpText != null)
        {
            featureHelpText.text = personalization == null
                ? "SESSION RUNTIME UNAVAILABLE"
                : string.Format(
                    "SESSION {0}   ·   COLD START {1}   ·   "
                    + "MANUAL INPUTS APPLY ONLY WHEN ENABLED",
                    personalization.AccumulatedSessionCount,
                    personalization.LastRunWasColdStart ? "ACTIVE" : "CLEAR");
        }

        if (thresholdHelpText != null)
        {
            thresholdHelpText.text =
                "<b>SAFETY GUARDRAILS</b>\n"
                + "Emergency distance, hold/release timing, and the critical-person "
                + "override stay locked. ML changes only bounded risk thresholds.";
        }
    }

    private static GameObject CreateCard(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        string title)
    {
        GameObject card = CreateRect(
            name,
            parent,
            anchorMin,
            anchorMax,
            new Vector2(3f, 3f),
            new Vector2(-3f, -3f));
        Image image = card.AddComponent<Image>();
        image.color = CardColor;
        image.raycastTarget = false;

        TMP_Text titleText = CreateText(
            card.transform,
            "Title",
            new Vector2(0.025f, 0.81f),
            new Vector2(0.56f, 0.975f),
            20f,
            TextAlignmentOptions.MidlineLeft);
        titleText.text = title;
        titleText.fontStyle = FontStyles.Bold;
        titleText.color = new Color(0.72f, 0.90f, 1f);
        return card;
    }

    private static StatusChip CreateStatusChip(
        Transform parent,
        string name,
        float left,
        float right)
    {
        return CreateStatusChip(
            parent,
            name,
            new Vector2(left, 0.928f),
            new Vector2(right, 0.99f));
    }

    private static StatusChip CreateStatusChip(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        GameObject chipObject = CreateRect(
            name + " Chip",
            parent,
            anchorMin,
            anchorMax,
            new Vector2(2f, 2f),
            new Vector2(-2f, -2f));
        Image background = chipObject.AddComponent<Image>();
        background.color = MutedColor;
        background.raycastTarget = false;
        TMP_Text text = CreateText(
            chipObject.transform,
            "Value",
            Vector2.zero,
            Vector2.one,
            16f,
            TextAlignmentOptions.Center);
        text.fontStyle = FontStyles.Bold;
        return new StatusChip
        {
            Background = background,
            Text = text
        };
    }

    private static MetricTile CreateMetricTile(
        Transform parent,
        string name,
        string label,
        float left,
        float right,
        float bottom,
        float top)
    {
        GameObject tileObject = CreateRect(
            name + " Tile",
            parent,
            new Vector2(left, bottom),
            new Vector2(right, top),
            new Vector2(2f, 2f),
            new Vector2(-2f, -2f));
        Image background = tileObject.AddComponent<Image>();
        background.color = TileColor;
        background.raycastTarget = false;

        TMP_Text labelText = CreateText(
            tileObject.transform,
            "Label",
            new Vector2(0.04f, 0.53f),
            new Vector2(0.96f, 0.94f),
            13f,
            TextAlignmentOptions.Center);
        labelText.text = label;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = new Color(0.56f, 0.66f, 0.76f);

        TMP_Text valueText = CreateText(
            tileObject.transform,
            "Value",
            new Vector2(0.04f, 0.04f),
            new Vector2(0.96f, 0.59f),
            18f,
            TextAlignmentOptions.Center);
        valueText.text = "--";
        valueText.fontStyle = FontStyles.Bold;
        return new MetricTile
        {
            Background = background,
            Label = labelText,
            Value = valueText
        };
    }

    private static void MakeCompact(MetricTile tile)
    {
        if (tile == null)
        {
            return;
        }

        tile.Label.fontSize = 11f;
        tile.Value.fontSize = 15f;
    }

    private static RiskBar CreateRiskBar(
        Transform parent,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        GameObject row = CreateRect(
            label + " Risk",
            parent,
            anchorMin,
            anchorMax,
            Vector2.zero,
            Vector2.zero);
        TMP_Text labelText = CreateText(
            row.transform,
            "Label",
            new Vector2(0f, 0f),
            new Vector2(0.18f, 1f),
            14f,
            TextAlignmentOptions.MidlineLeft);
        labelText.text = label;
        labelText.fontStyle = FontStyles.Bold;
        labelText.color = new Color(0.68f, 0.76f, 0.84f);

        GameObject track = CreateRect(
            "Track",
            row.transform,
            new Vector2(0.19f, 0.31f),
            new Vector2(0.83f, 0.69f),
            Vector2.zero,
            Vector2.zero);
        Image trackImage = track.AddComponent<Image>();
        trackImage.color = new Color(0.12f, 0.15f, 0.19f, 1f);
        trackImage.raycastTarget = false;
        GameObject fill = CreateRect(
            "Fill",
            track.transform,
            Vector2.zero,
            new Vector2(0f, 1f),
            Vector2.zero,
            Vector2.zero);
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = EnabledColor;
        fillImage.raycastTarget = false;

        TMP_Text valueText = CreateText(
            row.transform,
            "Value",
            new Vector2(0.84f, 0f),
            new Vector2(1f, 1f),
            17f,
            TextAlignmentOptions.MidlineRight);
        valueText.text = "--";
        valueText.fontStyle = FontStyles.Bold;
        return new RiskBar
        {
            Fill = fillImage,
            Value = valueText
        };
    }

    private static void CreateInfoStrip(
        Transform parent,
        string name,
        string message,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        GameObject strip = CreateRect(
            name,
            parent,
            anchorMin,
            anchorMax,
            new Vector2(2f, 2f),
            new Vector2(-2f, -2f));
        Image image = strip.AddComponent<Image>();
        image.color = new Color(0.09f, 0.15f, 0.20f, 0.98f);
        image.raycastTarget = false;
        TMP_Text text = CreateText(
            strip.transform,
            "Text",
            Vector2.zero,
            Vector2.one,
            14f,
            TextAlignmentOptions.Center);
        text.text = message;
        text.color = new Color(0.56f, 0.78f, 0.92f);
        text.fontStyle = FontStyles.Bold;
    }

    private static void SetChip(
        StatusChip chip,
        string value,
        Color color)
    {
        if (chip == null)
        {
            return;
        }

        chip.Text.text = value;
        chip.Background.color = color;
    }

    private static void SetMetric(
        MetricTile tile,
        string value,
        Color? color = null)
    {
        if (tile == null)
        {
            return;
        }

        tile.Value.text = value;
        tile.Value.color = color ?? Color.white;
    }

    private static void SetRiskBar(
        RiskBar bar,
        float value,
        bool available)
    {
        if (bar == null)
        {
            return;
        }

        float normalized = Mathf.Clamp01(value);
        RectTransform fillRect = bar.Fill.rectTransform;
        fillRect.anchorMax = new Vector2(
            available ? Mathf.Max(0.015f, normalized) : 0.015f,
            1f);
        Color color = !available
            ? MutedColor
            : normalized >= 0.70f
                ? DisabledColor
                : normalized >= 0.40f ? WarningColor : EnabledColor;
        bar.Fill.color = color;
        bar.Value.text = available ? normalized.ToString("F2") : "--";
        bar.Value.color = available ? Color.white : new Color(0.6f, 0.65f, 0.7f);
    }

    private void CreateTabButton(
        Transform parent,
        string label,
        int pageIndex,
        float x)
    {
        Button button = CreateActionButton(
            parent,
            label,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            () => SetPage(pageIndex),
            ButtonColor);
        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.815f);
        rect.anchorMax = new Vector2(0.5f, 0.815f);
        rect.sizeDelta = new Vector2(310f, 52f);
        rect.anchoredPosition = new Vector2(x, 0f);
    }

    private GameObject CreatePage(Transform parent, string name)
    {
        GameObject page = CreateRect(
            name,
            parent,
            new Vector2(0.01f, 0.01f),
            new Vector2(0.99f, 0.775f),
            Vector2.zero,
            Vector2.zero);
        Image image = page.AddComponent<Image>();
        image.color = PageColor;
        image.raycastTarget = false;
        return page;
    }

    private void SetPage(int pageIndex)
    {
        currentPage = Mathf.Clamp(pageIndex, 0, pages.Count - 1);
        for (int i = 0; i < pages.Count; i++)
        {
            pages[i].SetActive(i == currentPage);
        }

        Refresh();
    }

    private void CreateToggleButton(
        Transform parent,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Func<bool> getter,
        Action action)
    {
        Button button = CreateActionButton(
            parent,
            label,
            anchorMin,
            anchorMax,
            action,
            DisabledColor);
        TMP_Text text = button.GetComponentInChildren<TMP_Text>();
        toggleBindings.Add(new ToggleBinding
        {
            Button = button,
            Label = text,
            Getter = getter,
            Name = label
        });
    }

    private static Button CreateActionButton(
        Transform parent,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Action action,
        Color color)
    {
        GameObject buttonObject = CreateRect(
            label + " Button",
            parent,
            anchorMin,
            anchorMax,
            new Vector2(3f, 3f),
            new Vector2(-3f, -3f));
        Image image = buttonObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => action?.Invoke());

        TMP_Text text = CreateText(
            buttonObject.transform,
            "Label",
            Vector2.zero,
            Vector2.one,
            17f,
            TextAlignmentOptions.Center);
        text.text = label;
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        return button;
    }

    private static Slider CreateSlider(
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        float minimum,
        float maximum)
    {
        GameObject sliderObject = CreateRect(
            "Slider",
            parent,
            anchorMin,
            anchorMax,
            Vector2.zero,
            Vector2.zero);
        Slider slider = sliderObject.AddComponent<Slider>();
        slider.minValue = minimum;
        slider.maxValue = maximum;
        slider.direction = Slider.Direction.LeftToRight;

        GameObject background = CreateRect(
            "Background",
            sliderObject.transform,
            new Vector2(0f, 0.40f),
            new Vector2(1f, 0.60f),
            Vector2.zero,
            Vector2.zero);
        Image backgroundImage = background.AddComponent<Image>();
        backgroundImage.color = new Color(0.15f, 0.18f, 0.23f, 1f);

        GameObject fillArea = CreateRect(
            "Fill Area",
            sliderObject.transform,
            new Vector2(0f, 0.40f),
            new Vector2(1f, 0.60f),
            new Vector2(8f, 0f),
            new Vector2(-8f, 0f));
        GameObject fill = CreateRect(
            "Fill",
            fillArea.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.12f, 0.62f, 0.92f, 1f);

        GameObject handleArea = CreateRect(
            "Handle Slide Area",
            sliderObject.transform,
            Vector2.zero,
            Vector2.one,
            new Vector2(12f, 0f),
            new Vector2(-12f, 0f));
        GameObject handle = CreateRect(
            "Handle",
            handleArea.transform,
            new Vector2(0f, 0.20f),
            new Vector2(0f, 0.80f),
            new Vector2(-10f, 0f),
            new Vector2(10f, 0f));
        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = Color.white;
        handleImage.raycastTarget = true;

        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.targetGraphic = handleImage;
        return slider;
    }

    private static TMP_Text CreateText(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = CreateRect(
            name,
            parent,
            anchorMin,
            anchorMax,
            new Vector2(5f, 3f),
            new Vector2(-5f, -3f));
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateRect(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        var target = new GameObject(name, typeof(RectTransform));
        target.transform.SetParent(parent, false);
        RectTransform rect = target.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        return target;
    }

    private float ManualFeatureValue(int index)
    {
        if (personalization == null)
        {
            return 0f;
        }

        PersonalizationFeatureVector features =
            personalization.ManualFeatures;
        switch (index)
        {
            case 0: return features.ActivationFrequency;
            case 1: return features.ManualCancelRatio;
            case 2: return features.MeanPassthroughDurationSeconds;
            case 3: return features.MeanHeadSpeedMetersPerSecond;
            case 4: return features.MaximumHeadSpeedMetersPerSecond;
            case 5: return features.NormalizedSpaceArea;
            case 6: return features.NormalizedSessionElapsed;
            default: return 0f;
        }
    }

    private PersonalizedThresholds Preview()
    {
        return personalization == null
            ? PersonalizationMath.Defaults
            : personalization.ThresholdPreview;
    }

    private void SetPreview(
        float? stableOn,
        float? rapidOn,
        float? handFull)
    {
        if (personalization == null)
        {
            return;
        }

        PersonalizedThresholds current = personalization.ThresholdPreview;
        personalization.SetThresholdPreview(
            stableOn ?? current.StableOnThreshold,
            rapidOn ?? current.RapidOnThreshold,
            handFull ?? current.HandFullThreshold);
    }

    private void SaveRuntimeSettings()
    {
        ResolveReferences();
        var settings = new RuntimePanelSettings
        {
            Version = RuntimeSettingsVersion,
            SelectedPage = currentPage,
            StaticFeatureEnabled = presentation == null
                || presentation.StaticFeatureEnabled,
            DynamicFeatureEnabled = presentation == null
                || presentation.DynamicFeatureEnabled,
            MlEnabled = personalization != null
                && personalization.MlEnabled,
            TrackingProfile = trackingQuality == null
                ? (int)TrackingQualityProfile.Balanced
                : (int)trackingQuality.Profile,
            FeedbackMode = presentation == null
                ? (int)SafetyFeedbackMode.Passthrough
                : (int)presentation.FeedbackMode
        };

        PlayerPrefs.DeleteKey(LegacyRuntimeSettingsPreferenceKey);
        PlayerPrefs.SetString(
            RuntimeSettingsPreferenceKey,
            JsonUtility.ToJson(settings));
        PlayerPrefs.SetFloat(
            PanelOpacityPreferenceKey,
            panelOpacity);
        PlayerPrefs.Save();
        SetSettingsNotice("SETTINGS SAVED", EnabledColor);
        Refresh();
    }

    private void LoadRuntimeSettings()
    {
        if (!PlayerPrefs.HasKey(RuntimeSettingsPreferenceKey))
        {
            return;
        }

        try
        {
            RuntimePanelSettings settings =
                JsonUtility.FromJson<RuntimePanelSettings>(
                    PlayerPrefs.GetString(RuntimeSettingsPreferenceKey));
            if (settings == null
                || settings.Version != RuntimeSettingsVersion)
            {
                Debug.LogWarning(
                    "[AdaptiveLab] Ignoring unsupported runtime settings.");
                return;
            }

            currentPage = 0;
            presentation?.SetStaticFeatureEnabled(
                settings.StaticFeatureEnabled);
            presentation?.SetDynamicFeatureEnabled(
                settings.DynamicFeatureEnabled);
            presentation?.SetFeedbackMode(
                Enum.IsDefined(
                    typeof(SafetyFeedbackMode),
                    settings.FeedbackMode)
                    ? (SafetyFeedbackMode)settings.FeedbackMode
                    : SafetyFeedbackMode.Passthrough);
            personalization?.SetMlEnabled(settings.MlEnabled);
            if (trackingQuality != null
                && Enum.IsDefined(
                    typeof(TrackingQualityProfile),
                    settings.TrackingProfile))
            {
                trackingQuality.SetProfile(
                    (TrackingQualityProfile)settings.TrackingProfile);
            }
            SetSettingsNotice("SAVED SETTINGS LOADED", AccentColor);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[AdaptiveLab] Could not load runtime settings: "
                + exception.Message);
            SetSettingsNotice("SETTINGS LOAD FAILED", DisabledColor);
        }
    }

    private void ResetRuntimeSettings()
    {
        ResolveReferences();
        PlayerPrefs.DeleteKey(RuntimeSettingsPreferenceKey);
        PlayerPrefs.DeleteKey(LegacyRuntimeSettingsPreferenceKey);
        PlayerPrefs.DeleteKey(PanelOpacityPreferenceKey);

        panelOpacity = 1f;
        currentPage = 0;
        presentation?.SetStaticFeatureEnabled(true);
        presentation?.SetDynamicFeatureEnabled(true);
        presentation?.SetFeedbackMode(SafetyFeedbackMode.Passthrough);
        personalization?.SetMlEnabled(false);
        trackingQuality?.ResetSavedProfile();
        ApplyPanelOpacity(panelOpacity, false);
        if (pages.Count > 0)
        {
            SetPage(currentPage);
        }

        PlayerPrefs.Save();
        SetSettingsNotice("SETTINGS RESET TO SAFE DEFAULTS", WarningColor);
        Refresh();
    }

    private void SetSettingsNotice(string text, Color color)
    {
        settingsNotice = text;
        settingsNoticeColor = color;
        settingsNoticeUntil = Time.realtimeSinceStartupAsDouble + 3.0;
    }

    private void ApplyPanelOpacity(float value, bool savePreference)
    {
        panelOpacity = Mathf.Clamp(value, 0.35f, 1f);
        ResolvePanelCanvasGroup();
        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = panelOpacity;
            panelCanvasGroup.interactable = true;
            panelCanvasGroup.blocksRaycasts = true;
        }

        if (savePreference)
        {
            PlayerPrefs.SetFloat(
                PanelOpacityPreferenceKey,
                panelOpacity);
        }
    }

    private void ResolvePanelCanvasGroup()
    {
        if (panelCanvasGroup != null)
        {
            return;
        }

        Transform panelRoot = transform.parent == null
            ? transform
            : transform.parent;
        panelCanvasGroup = panelRoot.GetComponent<CanvasGroup>();
        if (panelCanvasGroup == null)
        {
            panelCanvasGroup = panelRoot.gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void ResolveReferences()
    {
        if (personalization == null)
        {
            personalization =
                FindAnyObjectByType<PersonalizationRuntimeController>();
        }

        if (staticPolicy == null)
        {
            staticPolicy =
                FindAnyObjectByType<StaticPassthroughPolicyController>();
        }

        if (dynamicPolicy == null)
        {
            dynamicPolicy =
                FindAnyObjectByType<DynamicPassthroughPolicyController>();
        }

        if (presentation == null)
        {
            presentation =
                FindAnyObjectByType<SelectivePassthroughController>();
        }

        if (trackingQuality == null)
        {
            trackingQuality =
                FindAnyObjectByType<TrackingQualityController>();
        }

        if (panelPlacement == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            GameObject root = canvas == null
                ? transform.root.gameObject
                : canvas.gameObject;
            panelPlacement = root.GetComponent<
                WorldSpacePanelPlacementController>();
            if (panelPlacement == null)
            {
                panelPlacement = root.AddComponent<
                    WorldSpacePanelPlacementController>();
            }

            panelPlacement.Configure(root.transform);
        }
    }
}
