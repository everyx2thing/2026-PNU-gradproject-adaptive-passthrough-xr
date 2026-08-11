using System;
using TeamVR.AdaptivePassthrough;
using UnityEngine;

[DefaultExecutionOrder(520)]
[DisallowMultipleComponent]
public sealed class StaticPassthroughPolicyController : MonoBehaviour
{
    [SerializeField] private QuestRiskExperimentLogger measurementProvider;
    [SerializeField, Min(1f)] private float evaluationRateHz = 20f;
    [SerializeField] private StaticBoundaryPolicySettings policySettings =
        new StaticBoundaryPolicySettings();

    private StaticBoundaryPolicy policy;
    private double nextEvaluationAt;
    private long sequence;

    public event Action<PassthroughSourceDecision> DecisionPublished;
    public event Action<StaticPassthroughDecision> StaticDecisionPublished;

    public PassthroughSourceDecision Latest { get; private set; }
    public StaticPassthroughDecision LatestStatic { get; private set; }

    public QuestRiskExperimentLogger MeasurementProvider
    {
        get { return measurementProvider; }
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

    public void Configure(QuestRiskExperimentLogger provider)
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
        policy?.Reset();
    }

    public void RestoreDefaultThresholds()
    {
        ApplyPersonalizedThresholds(
            PersonalizationMath.DefaultStableOnThreshold,
            PersonalizationMath.DefaultRapidOnThreshold,
            PersonalizationMath.DefaultHandFullThreshold);
    }

    public PassthroughSourceDecision Evaluate(double timestampSeconds)
    {
        ResolveReference();
        if (policy == null)
        {
            RebuildPolicy();
        }

        StaticBoundaryRiskFrame frame =
            measurementProvider == null
                ? StaticBoundaryRiskFrame.Unavailable
                : measurementProvider.CurrentStaticBoundaryFrame;

        sequence++;
        LatestStatic = policy.Evaluate(
            sequence,
            timestampSeconds,
            frame);
        Latest = LatestStatic.SourceDecision;
        StaticDecisionPublished?.Invoke(LatestStatic);
        DecisionPublished?.Invoke(Latest);
        return Latest;
    }

    private void ResolveReference()
    {
        if (measurementProvider == null)
        {
            measurementProvider =
                FindAnyObjectByType<QuestRiskExperimentLogger>();
        }
    }

    private void RebuildPolicy()
    {
        if (policySettings == null)
        {
            policySettings = new StaticBoundaryPolicySettings();
        }

        policy = new StaticBoundaryPolicy(policySettings);
        sequence = 0;
        Latest = null;
        LatestStatic = null;
    }
}
