using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TeamVR.AdaptivePassthrough;
using Unity.InferenceEngine;
using UnityEngine;

[DefaultExecutionOrder(540)]
[DisallowMultipleComponent]
public sealed class PersonalizationRuntimeController : MonoBehaviour
{
    [Serializable]
    private sealed class ActivationEvent
    {
        public float DurationSeconds;
        public float MeanHeadSpeed;
        public float MaximumHeadSpeed;
        public bool ManualCancel;
        public string Source;
    }

    [Serializable]
    private sealed class PersonalizationLogRecord
    {
        public int schemaVersion = 2;
        public string recordType;
        public string utc;
        public string trigger;
        public double timestampSeconds;
        public int sessionCount;
        public bool coldStart;
        public bool shadowMode;
        public bool applyEnabled;
        public bool thresholdsApplied;
        public bool staticThresholdsApplied;
        public bool dynamicThresholdsApplied;
        public bool manualFeatureOverride;
        public bool probabilityOverride;
        public string inferenceSource;
        public string modelStatus;
        public float fPt;
        public float rCancel;
        public float meanDurationSeconds;
        public float meanHeadSpeedMps;
        public float maximumHeadSpeedMps;
        public float normalizedSpaceArea;
        public float normalizedSessionElapsed;
        public bool hasNegativeProbability;
        public float negativeProbability;
        public float stableOnThreshold;
        public float rapidOnThreshold;
        public float handFullThreshold;
        public float dynamicOnThreshold;
        public float dynamicOffThreshold;
        public float adjustmentDelta;
        public string activationSource;
        public bool combinedPassthroughVisible;
        public float staticRisk;
        public bool staticPassthroughEnabled;
        public float dynamicRisk;
        public bool dynamicPassthroughEnabled;
        public bool dynamicForcePassthrough;
    }

    private const string SessionCountKey =
        "TeamVR.Personalization.AccumulatedSessionCount";

    [Header("Runtime Sources")]
    [SerializeField] private StaticPassthroughPolicyController staticPolicy;
    [SerializeField] private DynamicPassthroughPolicyController dynamicPolicy;
    [SerializeField] private SelectivePassthroughController presentation;
    [SerializeField] private QuestRiskExperimentLogger measurementProvider;

    [Header("InferenceEngine Model")]
    [SerializeField] private ModelAsset modelAsset;
    [SerializeField] private UnityEngine.Object sourceModelArtifact;
    [SerializeField] private string probabilityOutputName = "risk_probability";
    [SerializeField] private BackendType backend = BackendType.CPU;

    [Header("Safe Rollout")]
    [SerializeField] private bool shadowMode = true;
    [SerializeField] private bool applyPersonalization;
    [SerializeField] private bool automaticInference;
    [SerializeField, Min(0.5f)] private float inferenceIntervalSeconds = 5f;
    [SerializeField, Min(0)] private int minimumSessionCount =
        PersonalizationMath.DefaultMinimumSessionCount;
    [SerializeField] private bool bypassColdStartForTesting;
    [SerializeField, Min(0f)] private float adjustmentScale =
        PersonalizationMath.DefaultAdjustmentScale;
    [SerializeField, Range(0.65f, 1f)] private float maximumThreshold =
        PersonalizationMath.DefaultMaximumThreshold;

    [Header("Live Feature Collection")]
    [SerializeField, Range(1, 20)] private int eventWindowSize =
        PersonalizationMath.DefaultEventWindowSize;
    [SerializeField, Min(0f)] private float spaceAreaSquareMeters = 4f;
    [SerializeField, Min(1f)] private float sessionMaximumSeconds =
        PersonalizationMath.DefaultSessionMaximumSeconds;

    [Header("Interactive Test Overrides")]
    [SerializeField] private bool manualFeatureOverride;
    [SerializeField] private PersonalizationFeatureVector manualFeatures =
        new PersonalizationFeatureVector(0f, 0f, 0f, 0f, 0f, 0f, 0f);
    [SerializeField] private bool negativeProbabilityOverride;
    [SerializeField, Range(0f, 1f)] private float overriddenNegativeProbability = 0.5f;
    [SerializeField] private PersonalizedThresholds thresholdPreview =
        new PersonalizedThresholds(0.65f, 0.45f, 0.85f);

    [Header("Diagnostics Log")]
    [SerializeField] private bool enableLogging = true;
    [SerializeField] private string filePrefix = "personalization";

    private static bool sessionCountedThisProcess;

    private readonly Queue<ActivationEvent> recentEvents =
        new Queue<ActivationEvent>();
    private readonly List<ActivationEvent> featureScratch =
        new List<ActivationEvent>();
    private Worker worker;
    private Model runtimeModel;
    private StaticPassthroughPolicyController subscribedStaticPolicy;
    private DynamicPassthroughPolicyController subscribedDynamicPolicy;
    private SelectivePassthroughController subscribedPresentation;
    private StreamWriter writer;
    private double sessionStartedAt;
    private double nextInferenceAt;
    private bool activationActive;
    private bool activationManualCancel;
    private double activationStartedAt;
    private float activationHeadSpeedSum;
    private float activationMaximumHeadSpeed;
    private int activationHeadSpeedSamples;
    private ActivationEvent lastCompletedEvent;
    private bool activationIncludedStatic;
    private bool activationIncludedDynamic;
    private bool fallbackStaticVisible;
    private bool fallbackDynamicVisible;
    private bool usingPresentationSubscription;

    public event Action SnapshotUpdated;

    public PersonalizationFeatureVector LastFeatures { get; private set; }
    public PersonalizedThresholds LastThresholds { get; private set; } =
        PersonalizationMath.Defaults;
    public float LastNegativeProbability { get; private set; }
    public bool HasNegativeProbability { get; private set; }
    public bool LastRunWasColdStart { get; private set; }
    public bool LastThresholdsApplied { get; private set; }
    public bool LastStaticThresholdsApplied { get; private set; }
    public bool LastDynamicThresholdsApplied { get; private set; }
    public string LastInferenceSource { get; private set; } = "not-run";
    public string LastStatus { get; private set; } = "Waiting for first inference.";
    public string ModelStatus { get; private set; } = "Not initialized.";
    public string CurrentLogPath { get; private set; }
    public int AccumulatedSessionCount { get; private set; }

    public bool ShadowMode => shadowMode;
    public bool ApplyPersonalization => applyPersonalization;
    public bool AutomaticInference => automaticInference;
    public bool MlEnabled =>
        automaticInference && applyPersonalization && !shadowMode;
    public bool BypassColdStartForTesting => bypassColdStartForTesting;
    public bool ManualFeatureOverride => manualFeatureOverride;
    public bool NegativeProbabilityOverride => negativeProbabilityOverride;
    public float OverriddenNegativeProbability =>
        overriddenNegativeProbability;
    public float AdjustmentScale => adjustmentScale;
    public float MaximumThreshold => maximumThreshold;
    public float SpaceAreaSquareMeters => spaceAreaSquareMeters;
    public float SessionElapsedSeconds => (float)Math.Max(
        0.0,
        Time.realtimeSinceStartupAsDouble - sessionStartedAt);
    public float CurrentHeadSpeedMetersPerSecond
    {
        get
        {
            UserMotionSnapshot snapshot = measurementProvider == null
                ? null
                : measurementProvider.CurrentMotionSnapshot;
            return snapshot == null ? 0f : snapshot.FilteredSpeed;
        }
    }
    public int RecentActivationCount => recentEvents.Count;
    public bool ActivationActive => activationActive;
    public float CurrentActivationDurationSeconds => activationActive
        ? (float)Math.Max(
            0.0,
            Time.realtimeSinceStartupAsDouble - activationStartedAt)
        : 0f;
    public bool ModelReady => worker != null;
    public string SourceModelArtifactName => sourceModelArtifact == null
        ? "none"
        : sourceModelArtifact.name;
    public PersonalizationFeatureVector ManualFeatures => manualFeatures;
    public PersonalizedThresholds ThresholdPreview => thresholdPreview;
    public int MinimumSessionCount => minimumSessionCount;
    public int EventWindowSize => eventWindowSize;
    public string CurrentActivationSource => activationActive
        ? FormatActivationSource()
        : lastCompletedEvent == null
            ? "none"
            : lastCompletedEvent.Source;

    private void Awake()
    {
        ResolveReferences();
        sessionStartedAt = Time.realtimeSinceStartupAsDouble;
        thresholdPreview = PersonalizationMath.Defaults;
        LastThresholds = PersonalizationMath.Defaults;
        CountSessionOnce();
        TryInitializeModel();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeToSources();
        nextInferenceAt = Time.realtimeSinceStartupAsDouble + 0.5;
        if (enableLogging)
        {
            OpenWriter();
        }
    }

    private void OnDisable()
    {
        if (activationActive)
        {
            CompleteActivation(
                Time.realtimeSinceStartupAsDouble,
                activationManualCancel);
        }

        UnsubscribeFromSources();
        CloseWriter();
    }

    private void OnDestroy()
    {
        worker?.Dispose();
        worker = null;
        runtimeModel = null;
    }

    private void Update()
    {
        ResolveReferences();
        SubscribeToSources();
        if (activationActive)
        {
            SampleActiveHeadSpeed();
        }

        double now = Time.realtimeSinceStartupAsDouble;
        if (automaticInference && now >= nextInferenceAt)
        {
            nextInferenceAt =
                now + Mathf.Max(0.5f, inferenceIntervalSeconds);
            RunNowInternal("automatic");
        }
    }

    public void Configure(
        StaticPassthroughPolicyController policyController,
        QuestRiskExperimentLogger provider,
        ModelAsset compatibleModel,
        UnityEngine.Object sourceArtifact)
    {
        Configure(
            policyController,
            null,
            null,
            provider,
            compatibleModel,
            sourceArtifact);
    }

    public void Configure(
        StaticPassthroughPolicyController staticPolicyController,
        DynamicPassthroughPolicyController dynamicPolicyController,
        SelectivePassthroughController presentationController,
        QuestRiskExperimentLogger provider,
        ModelAsset compatibleModel,
        UnityEngine.Object sourceArtifact)
    {
        UnsubscribeFromSources();
        staticPolicy = staticPolicyController;
        dynamicPolicy = dynamicPolicyController;
        presentation = presentationController;
        measurementProvider = provider;
        modelAsset = compatibleModel;
        sourceModelArtifact = sourceArtifact;
        probabilityOutputName = "risk_probability";
        automaticInference = false;
        applyPersonalization = false;
        shadowMode = true;
        ResolveReferences();
        SubscribeToSources();
        if (Application.isPlaying)
        {
            TryInitializeModel();
        }
        else
        {
            worker?.Dispose();
            worker = null;
            runtimeModel = null;
            ModelStatus = modelAsset == null
                ? "Unity-compatible ONNX is not assigned."
                : "Neutral-as-Negative ONNX assigned for runtime.";
        }
    }

    public void RunNow()
    {
        RunNowInternal("manual");
    }

    public void SetMlEnabled(bool enabled)
    {
        automaticInference = enabled;
        applyPersonalization = enabled;
        shadowMode = !enabled;
        nextInferenceAt = Time.realtimeSinceStartupAsDouble + 0.1;

        if (enabled)
        {
            RunNowInternal("ml-enabled");
            return;
        }

        ResolveReferences();
        thresholdPreview = PersonalizationMath.Defaults;
        LastThresholds = thresholdPreview;
        RestorePolicyDefaults();
        LastInferenceSource = "ml-disabled";
        LastStatus = "ML disabled; safe default thresholds restored.";
        WriteLog("ml-disabled");
        SnapshotUpdated?.Invoke();
    }

    public void ToggleMlEnabled()
    {
        SetMlEnabled(!MlEnabled);
    }

    public void ApplyModelNow()
    {
        RunNowInternal(
            "manual-apply-now",
            ignoreColdStart: true,
            forceApply: true);
    }

    public void SetShadowMode(bool enabled)
    {
        shadowMode = enabled;
        if (enabled)
        {
            applyPersonalization = false;
        }

        LastStatus = enabled
            ? "Shadow mode: predictions are logged but not applied."
            : "Shadow mode disabled; application still requires APPLY ON.";
        SnapshotUpdated?.Invoke();
    }

    public void SetApplyPersonalization(bool enabled)
    {
        applyPersonalization = enabled;
        if (enabled)
        {
            shadowMode = false;
            RunNowInternal("apply-enabled");
        }
        else
        {
            thresholdPreview = PersonalizationMath.Defaults;
            LastThresholds = thresholdPreview;
            RestorePolicyDefaults();
            LastStatus =
                "Automatic threshold application is OFF; defaults restored.";
            WriteLog("apply-disabled");
            SnapshotUpdated?.Invoke();
        }
    }

    public void SetAutomaticInference(bool enabled)
    {
        automaticInference = enabled;
        nextInferenceAt = Time.realtimeSinceStartupAsDouble + 0.1;
        SnapshotUpdated?.Invoke();
    }

    public void SetBypassColdStartForTesting(bool enabled)
    {
        bypassColdStartForTesting = enabled;
        SnapshotUpdated?.Invoke();
    }

    public void SetManualFeatureOverride(bool enabled)
    {
        manualFeatureOverride = enabled;
        SnapshotUpdated?.Invoke();
    }

    public void SetNegativeProbabilityOverride(bool enabled)
    {
        negativeProbabilityOverride = enabled;
        SnapshotUpdated?.Invoke();
    }

    public void SetOverriddenNegativeProbability(float value)
    {
        overriddenNegativeProbability = Mathf.Clamp01(value);
        SnapshotUpdated?.Invoke();
    }

    public void SetAdjustmentScale(float value)
    {
        adjustmentScale = Mathf.Clamp(value, 0f, 1f);
        SnapshotUpdated?.Invoke();
    }

    public void SetMaximumThreshold(float value)
    {
        maximumThreshold = Mathf.Clamp(value, 0.65f, 1f);
        SnapshotUpdated?.Invoke();
    }

    public void SetSpaceAreaSquareMeters(float value)
    {
        spaceAreaSquareMeters = Mathf.Clamp(value, 1f, 30f);
        SnapshotUpdated?.Invoke();
    }

    public void SetAccumulatedSessionCount(int value)
    {
        AccumulatedSessionCount = Mathf.Clamp(value, 0, 9999);
        PlayerPrefs.SetInt(SessionCountKey, AccumulatedSessionCount);
        PlayerPrefs.Save();
        SnapshotUpdated?.Invoke();
    }

    public void SetManualFeature(int index, float value)
    {
        switch (index)
        {
            case 0:
                manualFeatures.ActivationFrequency = Mathf.Clamp01(value);
                break;
            case 1:
                manualFeatures.ManualCancelRatio = Mathf.Clamp01(value);
                break;
            case 2:
                manualFeatures.MeanPassthroughDurationSeconds =
                    Mathf.Clamp(value, 0f, 30f);
                break;
            case 3:
                manualFeatures.MeanHeadSpeedMetersPerSecond =
                    Mathf.Clamp(value, 0f, 5f);
                break;
            case 4:
                manualFeatures.MaximumHeadSpeedMetersPerSecond =
                    Mathf.Clamp(value, 0f, 8f);
                break;
            case 5:
                manualFeatures.NormalizedSpaceArea = Mathf.Clamp01(value);
                break;
            case 6:
                manualFeatures.NormalizedSessionElapsed = Mathf.Clamp01(value);
                break;
        }

        if (manualFeatures.MaximumHeadSpeedMetersPerSecond
            < manualFeatures.MeanHeadSpeedMetersPerSecond)
        {
            manualFeatures.MaximumHeadSpeedMetersPerSecond =
                manualFeatures.MeanHeadSpeedMetersPerSecond;
        }

        SnapshotUpdated?.Invoke();
    }

    public void SetThresholdPreview(
        float stableOn,
        float rapidOn,
        float handFull)
    {
        SetFullThresholdPreview(
            stableOn,
            rapidOn,
            handFull,
            thresholdPreview.DynamicOnThreshold,
            thresholdPreview.DynamicOffThreshold);
    }

    public void SetFullThresholdPreview(
        float stableOn,
        float rapidOn,
        float handFull,
        float dynamicOn,
        float dynamicOff)
    {
        thresholdPreview = new PersonalizedThresholds(
            Mathf.Clamp(stableOn, 0.05f, 0.95f),
            Mathf.Clamp(rapidOn, 0.05f, 0.95f),
            Mathf.Clamp(handFull, 0.05f, 0.95f),
            Mathf.Clamp(dynamicOn, 0.05f, 0.95f),
            Mathf.Clamp(dynamicOff, 0.05f, 0.95f));
        SnapshotUpdated?.Invoke();
    }

    public void ApplyThresholdPreview()
    {
        ResolveReferences();
        if (staticPolicy == null && dynamicPolicy == null)
        {
            LastStatus = "Cannot apply preview: policy targets are missing.";
            SnapshotUpdated?.Invoke();
            return;
        }

        ApplyThresholds(thresholdPreview);
        shadowMode = false;
        applyPersonalization = false;
        LastThresholds = thresholdPreview;
        LastInferenceSource = "manual-thresholds";
        LastStatus =
            "Manual threshold preview applied. ML auto-apply remains OFF.";
        WriteLog("manual-threshold-apply");
        SnapshotUpdated?.Invoke();
    }

    public void RestoreSafeDefaults()
    {
        ResolveReferences();
        thresholdPreview = PersonalizationMath.Defaults;
        LastThresholds = thresholdPreview;
        RestorePolicyDefaults();
        shadowMode = true;
        applyPersonalization = false;
        LastInferenceSource = "safe-defaults";
        LastStatus = "Safe default thresholds restored; Shadow mode ON.";
        WriteLog("restore-defaults");
        SnapshotUpdated?.Invoke();
    }

    public void MarkActivationUnnecessary()
    {
        if (activationActive)
        {
            activationManualCancel = true;
            LastStatus =
                "Current activation marked unnecessary for diagnostics.";
        }
        else if (lastCompletedEvent != null)
        {
            lastCompletedEvent.ManualCancel = true;
            LastStatus =
                "Most recent activation marked unnecessary for diagnostics.";
        }
        else
        {
            LastStatus = "No activation is available to mark.";
        }

        WriteLog("manual-feedback");
        SnapshotUpdated?.Invoke();
    }

    private void RunNowInternal(
        string trigger,
        bool ignoreColdStart = false,
        bool forceApply = false)
    {
        ResolveReferences();
        LastFeatures = GetCurrentFeatureVector();
        LastRunWasColdStart =
            !ignoreColdStart
            && !bypassColdStartForTesting
            && PersonalizationMath.IsColdStart(
                AccumulatedSessionCount,
                minimumSessionCount);
        HasNegativeProbability = false;
        LastNegativeProbability = 0f;
        LastThresholdsApplied = false;
        LastStaticThresholdsApplied = false;
        LastDynamicThresholdsApplied = false;

        if (LastRunWasColdStart)
        {
            LastThresholds = PersonalizationMath.Defaults;
            LastInferenceSource = "cold-start-default";
            LastStatus = string.Format(
                "Cold start {0}/{1}: defaults retained.",
                AccumulatedSessionCount,
                minimumSessionCount);
        }
        else
        {
            float probability;
            if (negativeProbabilityOverride)
            {
                probability = overriddenNegativeProbability;
                HasNegativeProbability = true;
                LastInferenceSource = "test-probability-override";
                LastStatus = "Test pNegative override evaluated.";
            }
            else if (TryRunModel(LastFeatures, out probability))
            {
                HasNegativeProbability = true;
                LastInferenceSource = "inference-engine";
                LastStatus =
                    "ONNX inference completed; Neutral is Negative risk.";
            }
            else
            {
                probability = 0f;
                LastInferenceSource = "model-unavailable-default";
                LastStatus = ModelStatus + " Defaults retained.";
            }

            LastNegativeProbability = Mathf.Clamp01(probability);
            LastThresholds = HasNegativeProbability
                ? PersonalizationMath.FromNegativeProbability(
                    LastNegativeProbability,
                    adjustmentScale,
                    maximumThreshold)
                : PersonalizationMath.Defaults;
        }

        thresholdPreview = LastThresholds;
        if (forceApply || (applyPersonalization && !shadowMode))
        {
            ApplyThresholds(LastThresholds);
            if (LastStaticThresholdsApplied
                && LastDynamicThresholdsApplied)
            {
                LastStatus += " Applied to static and dynamic policies.";
            }
            else if (LastThresholdsApplied)
            {
                LastStatus += " Partially applied to the available policy.";
            }
            else
            {
                LastStatus += " No policy target is available.";
            }
        }
        else
        {
            LastStatus += " Shadow/log only.";
        }

        WriteLog(trigger);
        SnapshotUpdated?.Invoke();
    }

    private PersonalizationFeatureVector GetCurrentFeatureVector()
    {
        if (manualFeatureOverride)
        {
            return new PersonalizationFeatureVector(
                manualFeatures.ActivationFrequency,
                manualFeatures.ManualCancelRatio,
                manualFeatures.MeanPassthroughDurationSeconds,
                manualFeatures.MeanHeadSpeedMetersPerSecond,
                manualFeatures.MaximumHeadSpeedMetersPerSecond,
                manualFeatures.NormalizedSpaceArea,
                manualFeatures.NormalizedSessionElapsed);
        }

        featureScratch.Clear();
        foreach (ActivationEvent activationEvent in recentEvents)
        {
            featureScratch.Add(activationEvent);
        }

        if (activationActive)
        {
            featureScratch.Add(BuildCurrentActivation());
        }

        int start = Mathf.Max(0, featureScratch.Count - eventWindowSize);
        int count = featureScratch.Count - start;
        int manualCancelCount = 0;
        float durationSum = 0f;
        float meanSpeedSum = 0f;
        float maximumSpeed = 0f;
        for (int i = start; i < featureScratch.Count; i++)
        {
            ActivationEvent activationEvent = featureScratch[i];
            if (activationEvent.ManualCancel)
            {
                manualCancelCount++;
            }

            durationSum += activationEvent.DurationSeconds;
            meanSpeedSum += activationEvent.MeanHeadSpeed;
            maximumSpeed = Mathf.Max(
                maximumSpeed,
                activationEvent.MaximumHeadSpeed);
        }

        return PersonalizationMath.BuildFeatureVector(
            count,
            eventWindowSize,
            manualCancelCount,
            count == 0 ? 0f : durationSum / count,
            count == 0 ? 0f : meanSpeedSum / count,
            maximumSpeed,
            spaceAreaSquareMeters,
            SessionElapsedSeconds,
            PersonalizationMath.DefaultSpaceMinimumSquareMeters,
            PersonalizationMath.DefaultSpaceMaximumSquareMeters,
            sessionMaximumSeconds);
    }

    public void RecordPresentationVisibility(
        bool visible,
        string source,
        double timestampSeconds)
    {
        double now = double.IsNaN(timestampSeconds)
            || double.IsInfinity(timestampSeconds)
                ? Time.realtimeSinceStartupAsDouble
                : timestampSeconds;
        if (visible)
        {
            if (!activationActive)
            {
                StartActivation(now);
            }

            MergeActivationSource(source);
            return;
        }

        if (activationActive)
        {
            CompleteActivation(now, activationManualCancel);
        }
    }

    private void OnPresentationVisibilityChanged(
        bool visible,
        string source,
        double timestampSeconds)
    {
        RecordPresentationVisibility(visible, source, timestampSeconds);
    }

    private void OnStaticDecision(StaticPassthroughDecision decision)
    {
        if (decision == null || subscribedPresentation != null)
        {
            return;
        }

        fallbackStaticVisible = decision.Enabled;
        double now = decision.SourceDecision == null
            ? Time.realtimeSinceStartupAsDouble
            : decision.SourceDecision.TimestampSeconds;
        RecordFallbackVisibility(now);
    }

    private void OnDynamicDecision(PassthroughSourceDecision decision)
    {
        if (decision == null || subscribedPresentation != null)
        {
            return;
        }

        fallbackDynamicVisible = decision.Enabled;
        RecordFallbackVisibility(decision.TimestampSeconds);
    }

    private void RecordFallbackVisibility(double timestampSeconds)
    {
        string source = fallbackStaticVisible
            ? fallbackDynamicVisible ? "static+dynamic" : "static"
            : fallbackDynamicVisible ? "dynamic" : "none";
        RecordPresentationVisibility(
            fallbackStaticVisible || fallbackDynamicVisible,
            source,
            timestampSeconds);
    }

    private void StartActivation(double timestampSeconds)
    {
        activationActive = true;
        activationManualCancel = false;
        activationStartedAt = timestampSeconds;
        activationHeadSpeedSum = 0f;
        activationMaximumHeadSpeed = 0f;
        activationHeadSpeedSamples = 0;
        activationIncludedStatic = false;
        activationIncludedDynamic = false;
        SampleActiveHeadSpeed();
    }

    private void MergeActivationSource(string source)
    {
        string safeSource = source ?? string.Empty;
        activationIncludedStatic |= safeSource.IndexOf(
            "static",
            StringComparison.OrdinalIgnoreCase) >= 0;
        activationIncludedDynamic |= safeSource.IndexOf(
            "dynamic",
            StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private string FormatActivationSource()
    {
        if (activationIncludedStatic && activationIncludedDynamic)
        {
            return "static+dynamic";
        }

        if (activationIncludedStatic)
        {
            return "static";
        }

        return activationIncludedDynamic ? "dynamic" : "unknown";
    }

    private void SampleActiveHeadSpeed()
    {
        float speed = Mathf.Max(0f, CurrentHeadSpeedMetersPerSecond);
        activationHeadSpeedSum += speed;
        activationMaximumHeadSpeed = Mathf.Max(
            activationMaximumHeadSpeed,
            speed);
        activationHeadSpeedSamples++;
    }

    private ActivationEvent BuildCurrentActivation()
    {
        return new ActivationEvent
        {
            DurationSeconds = CurrentActivationDurationSeconds,
            MeanHeadSpeed = activationHeadSpeedSamples == 0
                ? 0f
                : activationHeadSpeedSum / activationHeadSpeedSamples,
            MaximumHeadSpeed = activationMaximumHeadSpeed,
            ManualCancel = activationManualCancel,
            Source = FormatActivationSource()
        };
    }

    private void CompleteActivation(double timestampSeconds, bool manualCancel)
    {
        var completed = new ActivationEvent
        {
            DurationSeconds = (float)Math.Max(
                0.0,
                timestampSeconds - activationStartedAt),
            MeanHeadSpeed = activationHeadSpeedSamples == 0
                ? 0f
                : activationHeadSpeedSum / activationHeadSpeedSamples,
            MaximumHeadSpeed = activationMaximumHeadSpeed,
            ManualCancel = manualCancel,
            Source = FormatActivationSource()
        };
        recentEvents.Enqueue(completed);
        lastCompletedEvent = completed;
        while (recentEvents.Count > Mathf.Max(1, eventWindowSize))
        {
            recentEvents.Dequeue();
        }

        activationActive = false;
        activationManualCancel = false;
        activationHeadSpeedSum = 0f;
        activationMaximumHeadSpeed = 0f;
        activationHeadSpeedSamples = 0;
        activationIncludedStatic = false;
        activationIncludedDynamic = false;
    }

    private bool TryRunModel(
        PersonalizationFeatureVector features,
        out float negativeProbability)
    {
        negativeProbability = 0f;
        if (worker == null)
        {
            TryInitializeModel();
        }

        if (worker == null)
        {
            return false;
        }

        try
        {
            float[] values = features.ToArray();
            using (var input = new Tensor<float>(
                new TensorShape(1, values.Length),
                values))
            {
                worker.Schedule(input);
            }

            Tensor<float> output =
                worker.PeekOutput(probabilityOutputName) as Tensor<float>;
            if (output == null)
            {
                ModelStatus =
                    "Model output '" + probabilityOutputName
                    + "' is not a float tensor.";
                return false;
            }

            using (Tensor<float> readable = output.ReadbackAndClone())
            {
                negativeProbability = readable[0];
            }

            if (float.IsNaN(negativeProbability)
                || float.IsInfinity(negativeProbability))
            {
                ModelStatus = "Model returned a non-finite probability.";
                negativeProbability = 0f;
                return false;
            }

            ModelStatus = "Neutral-as-Negative ONNX ready.";
            return true;
        }
        catch (Exception exception)
        {
            ModelStatus = "Inference failed: " + exception.Message;
            Debug.LogWarning("[Personalization] " + ModelStatus);
            return false;
        }
    }

    private void TryInitializeModel()
    {
        worker?.Dispose();
        worker = null;
        runtimeModel = null;

        if (modelAsset == null)
        {
            ModelStatus = sourceModelArtifact == null
                ? "No compatible personalization model is assigned."
                : "RF ONNX source exists, but its Unity-compatible model "
                    + "is not assigned.";
            return;
        }

        try
        {
            runtimeModel = ModelLoader.Load(modelAsset);
            worker = new Worker(runtimeModel, backend);
            ModelStatus = "Neutral-as-Negative ONNX ready.";
        }
        catch (Exception exception)
        {
            ModelStatus = "Model initialization failed: " + exception.Message;
            Debug.LogWarning("[Personalization] " + ModelStatus);
        }
    }

    private void ApplyThresholds(PersonalizedThresholds thresholds)
    {
        LastStaticThresholdsApplied = staticPolicy != null;
        LastDynamicThresholdsApplied = dynamicPolicy != null;
        if (staticPolicy != null)
        {
            staticPolicy.ApplyPersonalizedThresholds(
                thresholds.StableOnThreshold,
                thresholds.RapidOnThreshold,
                thresholds.HandFullThreshold);
        }

        if (dynamicPolicy != null)
        {
            dynamicPolicy.ApplyPersonalizedThresholds(
                thresholds.DynamicOnThreshold,
                thresholds.DynamicOffThreshold);
        }

        LastThresholdsApplied = LastStaticThresholdsApplied
            || LastDynamicThresholdsApplied;
    }

    private void RestorePolicyDefaults()
    {
        LastStaticThresholdsApplied = staticPolicy != null;
        LastDynamicThresholdsApplied = dynamicPolicy != null;
        staticPolicy?.RestoreDefaultThresholds();
        dynamicPolicy?.RestoreDefaultThresholds();
        LastThresholdsApplied = LastStaticThresholdsApplied
            || LastDynamicThresholdsApplied;
    }

    private void ResolveReferences()
    {
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

        if (measurementProvider == null)
        {
            measurementProvider = staticPolicy == null
                ? null
                : staticPolicy.MeasurementProvider;
            if (measurementProvider == null)
            {
                measurementProvider =
                    FindAnyObjectByType<QuestRiskExperimentLogger>();
            }
        }
    }

    private void SubscribeToSources()
    {
        bool presentationSubscriptionCurrent =
            usingPresentationSubscription
            && presentation != null
            && subscribedPresentation == presentation;
        bool fallbackSubscriptionCurrent =
            !usingPresentationSubscription
            && presentation == null
            && subscribedStaticPolicy == staticPolicy
            && subscribedDynamicPolicy == dynamicPolicy;
        if (presentationSubscriptionCurrent
            || fallbackSubscriptionCurrent)
        {
            return;
        }

        UnsubscribeFromSources();
        subscribedPresentation = presentation;
        if (subscribedPresentation != null)
        {
            usingPresentationSubscription = true;
            subscribedPresentation.VisibilityChanged +=
                OnPresentationVisibilityChanged;
            RecordPresentationVisibility(
                subscribedPresentation.AnyWindowVisible,
                subscribedPresentation.VisibleSource,
                Time.realtimeSinceStartupAsDouble);
            return;
        }

        subscribedStaticPolicy = staticPolicy;
        subscribedDynamicPolicy = dynamicPolicy;
        if (subscribedStaticPolicy != null)
        {
            subscribedStaticPolicy.StaticDecisionPublished += OnStaticDecision;
            fallbackStaticVisible = subscribedStaticPolicy.LatestStatic != null
                && subscribedStaticPolicy.LatestStatic.Enabled;
        }

        if (subscribedDynamicPolicy != null)
        {
            subscribedDynamicPolicy.DecisionPublished += OnDynamicDecision;
            fallbackDynamicVisible = subscribedDynamicPolicy.Latest != null
                && subscribedDynamicPolicy.Latest.Enabled;
        }
    }

    private void UnsubscribeFromSources()
    {
        if (subscribedPresentation != null)
        {
            subscribedPresentation.VisibilityChanged -=
                OnPresentationVisibilityChanged;
            subscribedPresentation = null;
        }

        usingPresentationSubscription = false;

        if (subscribedStaticPolicy != null)
        {
            subscribedStaticPolicy.StaticDecisionPublished -= OnStaticDecision;
            subscribedStaticPolicy = null;
        }

        if (subscribedDynamicPolicy != null)
        {
            subscribedDynamicPolicy.DecisionPublished -= OnDynamicDecision;
            subscribedDynamicPolicy = null;
        }

        fallbackStaticVisible = false;
        fallbackDynamicVisible = false;
    }

    private void CountSessionOnce()
    {
        int stored = Mathf.Max(0, PlayerPrefs.GetInt(SessionCountKey, 0));
        if (Application.isPlaying && !sessionCountedThisProcess)
        {
            stored++;
            PlayerPrefs.SetInt(SessionCountKey, stored);
            PlayerPrefs.Save();
            sessionCountedThisProcess = true;
        }

        AccumulatedSessionCount = stored;
    }

    private void OpenWriter()
    {
        CloseWriter();
        try
        {
            string directory = Path.Combine(
                Application.persistentDataPath,
                "RiskLogs");
            Directory.CreateDirectory(directory);
            CurrentLogPath = Path.Combine(
                directory,
                string.Format(
                    "{0}-{1:yyyyMMdd-HHmmss}.jsonl",
                    filePrefix,
                    DateTime.Now));
            writer = new StreamWriter(
                CurrentLogPath,
                false,
                new UTF8Encoding(false));
            Debug.Log("[Personalization] Logging to " + CurrentLogPath);
        }
        catch (Exception exception)
        {
            enableLogging = false;
            CurrentLogPath = null;
            Debug.LogError(
                "[Personalization] Could not open log: "
                + exception.Message);
        }
    }

    private void WriteLog(string trigger)
    {
        if (writer == null)
        {
            return;
        }

        PassthroughSourceDecision staticDecision =
            staticPolicy == null ? null : staticPolicy.Latest;
        PassthroughSourceDecision dynamicDecision =
            dynamicPolicy == null ? null : dynamicPolicy.Latest;
        var record = new PersonalizationLogRecord
        {
            recordType = "personalizationSnapshot",
            utc = DateTime.UtcNow.ToString("O"),
            trigger = trigger,
            timestampSeconds = Time.realtimeSinceStartupAsDouble,
            sessionCount = AccumulatedSessionCount,
            coldStart = LastRunWasColdStart,
            shadowMode = shadowMode,
            applyEnabled = applyPersonalization,
            thresholdsApplied = LastThresholdsApplied,
            staticThresholdsApplied = LastStaticThresholdsApplied,
            dynamicThresholdsApplied = LastDynamicThresholdsApplied,
            manualFeatureOverride = manualFeatureOverride,
            probabilityOverride = negativeProbabilityOverride,
            inferenceSource = LastInferenceSource,
            modelStatus = ModelStatus,
            fPt = LastFeatures.ActivationFrequency,
            rCancel = LastFeatures.ManualCancelRatio,
            meanDurationSeconds =
                LastFeatures.MeanPassthroughDurationSeconds,
            meanHeadSpeedMps =
                LastFeatures.MeanHeadSpeedMetersPerSecond,
            maximumHeadSpeedMps =
                LastFeatures.MaximumHeadSpeedMetersPerSecond,
            normalizedSpaceArea = LastFeatures.NormalizedSpaceArea,
            normalizedSessionElapsed =
                LastFeatures.NormalizedSessionElapsed,
            hasNegativeProbability = HasNegativeProbability,
            negativeProbability = LastNegativeProbability,
            stableOnThreshold = LastThresholds.StableOnThreshold,
            rapidOnThreshold = LastThresholds.RapidOnThreshold,
            handFullThreshold = LastThresholds.HandFullThreshold,
            dynamicOnThreshold = LastThresholds.DynamicOnThreshold,
            dynamicOffThreshold = LastThresholds.DynamicOffThreshold,
            adjustmentDelta = LastThresholds.AdjustmentDelta,
            activationSource = CurrentActivationSource,
            combinedPassthroughVisible = presentation == null
                ? activationActive
                : presentation.AnyWindowVisible,
            staticRisk = staticDecision == null ? 0f : staticDecision.Risk,
            staticPassthroughEnabled =
                staticDecision != null && staticDecision.Enabled,
            dynamicRisk = dynamicDecision == null ? 0f : dynamicDecision.Risk,
            dynamicPassthroughEnabled =
                dynamicDecision != null && dynamicDecision.Enabled,
            dynamicForcePassthrough = dynamicPolicy != null
                && dynamicPolicy.LatestForcePassthrough
        };
        writer.WriteLine(JsonUtility.ToJson(record));
        writer.Flush();
    }

    private void CloseWriter()
    {
        if (writer == null)
        {
            return;
        }

        writer.Flush();
        writer.Dispose();
        writer = null;
    }
}
