using System;
using TeamVR.AdaptivePassthrough;
using UnityEngine;

[DefaultExecutionOrder(525)]
[DisallowMultipleComponent]
public sealed class DynamicPassthroughPolicyController : MonoBehaviour
{
    [SerializeField] private DynamicRiskController dynamicRiskController;
    [SerializeField] private QuestPersonDetectionRunner detectionRunner;
    [SerializeField, Min(1f)] private float evaluationRateHz = 20f;
    [SerializeField, Min(0f)] private float staleAfterSeconds = 0.50f;
    [SerializeField] private PassthroughDecisionFilterSettings decisionSettings =
        new PassthroughDecisionFilterSettings
        {
            mode = PassthroughDecisionMode.Hysteresis,
            onThreshold = 0.60f,
            offThreshold = 0.45f,
            minimumHoldSeconds = 1.50f,
            releaseDelaySeconds = 0.35f
        };

    private PassthroughDecisionFilter filter;
    private double nextEvaluationAt;
    private long sequence;
    private DynamicRiskController subscribedDynamicRiskController;

    public event Action<PassthroughSourceDecision> DecisionPublished;

    public PassthroughSourceDecision Latest { get; private set; }

    public DynamicRiskController DynamicRiskController
    {
        get { return dynamicRiskController; }
    }

    public float OnThreshold
    {
        get
        {
            return decisionSettings == null
                ? PersonalizationMath.DefaultDynamicOnThreshold
                : decisionSettings.onThreshold;
        }
    }

    public float OffThreshold
    {
        get
        {
            return decisionSettings == null
                ? PersonalizationMath.DefaultDynamicOffThreshold
                : decisionSettings.offThreshold;
        }
    }

    public float MinimumHoldSeconds
    {
        get
        {
            return decisionSettings == null
                ? 1.50f
                : decisionSettings.minimumHoldSeconds;
        }
    }

    public bool LatestForcePassthrough { get; private set; }

    private void Awake()
    {
        ResolveReferences();
        RebuildFilter();
    }

    private void OnEnable()
    {
        nextEvaluationAt = 0.0;
        RefreshPipelineResetSubscription();
    }

    private void OnDisable()
    {
        UnsubscribePipelineReset();
    }

    private void LateUpdate()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (now < nextEvaluationAt)
        {
            return;
        }

        nextEvaluationAt =
            now + 1.0 / Mathf.Max(1f, evaluationRateHz);
        Evaluate(now);
    }

    public void Configure(
        DynamicRiskController controller,
        QuestPersonDetectionRunner runner)
    {
        dynamicRiskController = controller;
        detectionRunner = runner;
        ResolveReferences();
        RebuildFilter();
    }

    public void ApplyPersonalizedThresholds(
        float onThreshold,
        float offThreshold)
    {
        EnsureDecisionSettings();
        float onDelta = SafeThresholdDelta(
            onThreshold,
            PersonalizationMath.DefaultDynamicOnThreshold);
        float offDelta = SafeThresholdDelta(
            offThreshold,
            PersonalizationMath.DefaultDynamicOffThreshold);
        float sharedDelta = Mathf.Clamp(
            Mathf.Min(onDelta, offDelta),
            0f,
            PersonalizationMath.MaximumAdjustmentDelta);
        decisionSettings.onThreshold =
            PersonalizationMath.DefaultDynamicOnThreshold + sharedDelta;
        decisionSettings.offThreshold =
            PersonalizationMath.DefaultDynamicOffThreshold + sharedDelta;
    }

    public void RestoreDefaultThresholds()
    {
        ApplyPersonalizedThresholds(
            PersonalizationMath.DefaultDynamicOnThreshold,
            PersonalizationMath.DefaultDynamicOffThreshold);
    }

    public PassthroughSourceDecision Evaluate(double timestampSeconds)
    {
        ResolveReferences();
        if (filter == null)
        {
            RebuildFilter();
        }

        DynamicRiskFrame frame =
            dynamicRiskController == null
                ? null
                : dynamicRiskController.LatestFrame;
        float age = frame == null
            ? float.MaxValue
            : (float)Math.Max(
                0.0,
                timestampSeconds
                    - dynamicRiskController
                        .LatestFrameProcessedRealtimeSeconds);
        bool cameraReady =
            detectionRunner != null
            && detectionRunner.IsCameraReady;
        bool available =
            cameraReady
            && frame != null
            && age <= Mathf.Max(0f, staleAfterSeconds);
        LatestForcePassthrough = available && frame.ForcePassthrough;
        float risk = available ? frame.PolicyRisk : 0f;
        PassthroughDecisionSnapshot filtered =
            filter.Evaluate(timestampSeconds, available, risk);

        sequence++;
        Latest = new PassthroughSourceDecision(
            sequence,
            timestampSeconds,
            PassthroughRiskSource.Dynamic,
            available,
            risk,
            filtered);
        DecisionPublished?.Invoke(Latest);
        return Latest;
    }

    private void ResolveReferences()
    {
        if (dynamicRiskController == null)
        {
            dynamicRiskController =
                FindAnyObjectByType<DynamicRiskController>();
        }

        if (detectionRunner == null)
        {
            detectionRunner =
                FindAnyObjectByType<QuestPersonDetectionRunner>();
        }

        RefreshPipelineResetSubscription();
    }

    private void RebuildFilter()
    {
        EnsureDecisionSettings();
        filter = new PassthroughDecisionFilter(
            decisionSettings);
        sequence = 0;
        Latest = null;
        LatestForcePassthrough = false;
    }

    public void ResetRuntimeState()
    {
        filter?.Reset();
        nextEvaluationAt = 0.0;
        sequence = 0;
        Latest = null;
        LatestForcePassthrough = false;
    }

    private void RefreshPipelineResetSubscription()
    {
        if (ReferenceEquals(
                subscribedDynamicRiskController,
                dynamicRiskController))
        {
            return;
        }

        UnsubscribePipelineReset();
        subscribedDynamicRiskController = dynamicRiskController;
        if (subscribedDynamicRiskController != null)
        {
            subscribedDynamicRiskController.PipelineReset +=
                HandlePipelineReset;
        }
    }

    private void UnsubscribePipelineReset()
    {
        if (subscribedDynamicRiskController != null)
        {
            subscribedDynamicRiskController.PipelineReset -=
                HandlePipelineReset;
            subscribedDynamicRiskController = null;
        }
    }

    private void HandlePipelineReset()
    {
        ResetRuntimeState();
    }

    private void EnsureDecisionSettings()
    {
        if (decisionSettings == null)
        {
            decisionSettings = new PassthroughDecisionFilterSettings
            {
                mode = PassthroughDecisionMode.Hysteresis,
                onThreshold = PersonalizationMath.DefaultDynamicOnThreshold,
                offThreshold = PersonalizationMath.DefaultDynamicOffThreshold,
                minimumHoldSeconds = 1.50f,
                releaseDelaySeconds = 0.35f
            };
        }
    }

    private static float SafeThresholdDelta(float value, float baseline)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0f;
        }

        return Mathf.Max(0f, value - baseline);
    }
}
