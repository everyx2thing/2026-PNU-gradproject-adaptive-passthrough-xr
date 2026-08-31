using System;
using TeamVR.AdaptivePassthrough;
using UnityEngine;

[DefaultExecutionOrder(520)]
[DisallowMultipleComponent]
public sealed class StaticPassthroughPolicyController : MonoBehaviour,
    IStaticRiskDiagnosticsProvider
{
    [SerializeField] private MonoBehaviour measurementProvider;
    [SerializeField, Min(1f)] private float evaluationRateHz = 20f;
    [SerializeField] private StaticBoundaryPolicySettings policySettings =
        new StaticBoundaryPolicySettings();
    [SerializeField] private StaticRiskChannelMask enabledChannels =
        StaticRiskChannelMask.All;

    private StaticBoundaryPolicy policy;
    private IStaticBoundaryFrameProvider frameProvider;
    private double nextEvaluationAt;
    private long sequence;

    public event Action<PassthroughSourceDecision> DecisionPublished;
    public event Action<StaticPassthroughDecision> StaticDecisionPublished;

    public PassthroughSourceDecision Latest { get; private set; }
    public StaticPassthroughDecision LatestStatic { get; private set; }

    public StaticBoundaryRiskFrame CurrentStaticBoundaryFrame
    {
        get
        {
            return frameProvider == null
                ? null
                : frameProvider.CurrentStaticBoundaryFrame;
        }
    }

    public StaticPassthroughDecision CurrentStaticDecision
    {
        get { return LatestStatic; }
    }

    public QuestRiskExperimentLogger MeasurementProvider
    {
        get { return measurementProvider as QuestRiskExperimentLogger; }
    }

    public IStaticBoundaryFrameProvider FrameProvider
    {
        get { return frameProvider; }
    }

    public float StableOnThreshold
    {
        get
        {
            return policySettings == null
                ? PersonalizationMath.DefaultStableOnThreshold
                : policySettings.stableOnThreshold;
        }
    }

    public float RapidOnThreshold
    {
        get
        {
            return policySettings == null
                ? PersonalizationMath.DefaultRapidOnThreshold
                : policySettings.rapidOnThreshold;
        }
    }

    public float HandFullThreshold
    {
        get
        {
            return policySettings == null
                ? PersonalizationMath.DefaultHandFullThreshold
                : policySettings.handFullThreshold;
        }
    }

    public float HysteresisWidth
    {
        get
        {
            return policySettings == null
                ? 0.08f
                : policySettings.hysteresisWidth;
        }
    }

    public float MinimumHoldSeconds
    {
        get
        {
            return policySettings == null
                ? 1.50f
                : policySettings.minimumHoldSeconds;
        }
    }

    public float ReleaseDelaySeconds
    {
        get
        {
            return policySettings == null
                ? 0.35f
                : policySettings.releaseDelaySeconds;
        }
    }

    public float EmergencyDistanceMeters
    {
        get
        {
            return policySettings == null
                ? 0.25f
                : policySettings.emergencyDistance;
        }
    }

    public StaticRiskChannelMask EnabledChannels
    {
        get { return enabledChannels & StaticRiskChannelMask.All; }
    }

    private void Awake()
    {
        ResolveReference();
        RebuildPolicy();
    }

    private void OnEnable()
    {
        nextEvaluationAt = 0.0;
        policy?.Reset();
    }

    private void OnDisable()
    {
        policy?.Reset();
        Latest = null;
        LatestStatic = null;
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

    public void Configure(MonoBehaviour provider)
    {
        measurementProvider = provider;
        ResolveReference();
        RebuildPolicy();
    }

    public void ApplyPersonalizedThresholds(
        float stableOn,
        float rapidOn,
        float handFull)
    {
        if (policySettings == null)
        {
            policySettings = new StaticBoundaryPolicySettings();
        }

        policySettings.stableOnThreshold = Mathf.Clamp01(stableOn);
        policySettings.rapidOnThreshold = Mathf.Clamp01(rapidOn);
        policySettings.handFullThreshold = Mathf.Clamp01(handFull);
        // The policy reads this mutable settings instance. Resetting here used
        // to drop an active emergency/minimum-hold state whenever ML applied.
    }

    public void RestoreDefaultThresholds()
    {
        ApplyPersonalizedThresholds(
            PersonalizationMath.DefaultStableOnThreshold,
            PersonalizationMath.DefaultRapidOnThreshold,
            PersonalizationMath.DefaultHandFullThreshold);
    }

    public void SetEnabledChannels(StaticRiskChannelMask channels)
    {
        enabledChannels = channels & StaticRiskChannelMask.All;
        policy?.SetEnabledChannels(enabledChannels);
    }

    public void SetChannelEnabled(
        StaticRiskChannelMask channel,
        bool enabled)
    {
        channel &= StaticRiskChannelMask.All;
        SetEnabledChannels(
            enabled
                ? enabledChannels | channel
                : enabledChannels & ~channel);
    }

    public bool IsChannelEnabled(StaticRiskChannelMask channel)
    {
        channel &= StaticRiskChannelMask.All;
        return channel != StaticRiskChannelMask.None
            && (enabledChannels & channel) == channel;
    }

    public PassthroughSourceDecision Evaluate(double timestampSeconds)
    {
        ResolveReference();
        if (policy == null)
        {
            RebuildPolicy();
        }

        StaticBoundaryRiskFrame frame =
            frameProvider == null
                ? StaticBoundaryRiskFrame.Unavailable
                : frameProvider.CurrentStaticBoundaryFrame;

        sequence++;
        LatestStatic = policy.Evaluate(
            sequence,
            timestampSeconds,
            frame,
            enabledChannels);
        Latest = LatestStatic.SourceDecision;
        StaticDecisionPublished?.Invoke(LatestStatic);
        DecisionPublished?.Invoke(Latest);
        return Latest;
    }

    private void ResolveReference()
    {
        frameProvider = measurementProvider as IStaticBoundaryFrameProvider;
        if (frameProvider == null)
        {
            measurementProvider =
                FindAnyObjectByType<QuestRiskExperimentLogger>();
            frameProvider = measurementProvider
                as IStaticBoundaryFrameProvider;
        }
    }

    private void RebuildPolicy()
    {
        if (policySettings == null)
        {
            policySettings = new StaticBoundaryPolicySettings();
        }

        policy = new StaticBoundaryPolicy(policySettings);
        policy.SetEnabledChannels(enabledChannels);
        sequence = 0;
        Latest = null;
        LatestStatic = null;
    }
}
