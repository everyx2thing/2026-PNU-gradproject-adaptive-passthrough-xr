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

    public event Action<PassthroughSourceDecision> DecisionPublished;

    public PassthroughSourceDecision Latest { get; private set; }

    public DynamicRiskController DynamicRiskController
    {
        get { return dynamicRiskController; }
    }

    private void Awake()
    {
        ResolveReferences();
        RebuildFilter();
    }

    private void OnEnable()
    {
        nextEvaluationAt = 0.0;
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
                timestampSeconds - frame.TimestampSeconds);
        bool cameraReady =
            detectionRunner != null
            && detectionRunner.IsCameraReady;
        bool available =
            cameraReady
            && frame != null
            && age <= Mathf.Max(0f, staleAfterSeconds);
        float risk = available ? frame.MaximumRisk : 0f;
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
    }

    private void RebuildFilter()
    {
        filter = new PassthroughDecisionFilter(
            decisionSettings ?? new PassthroughDecisionFilterSettings());
        sequence = 0;
        Latest = null;
    }
}
