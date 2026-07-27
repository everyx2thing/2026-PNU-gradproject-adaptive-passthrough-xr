using System;
using TeamVR.AdaptivePassthrough;
using UnityEngine;

[DefaultExecutionOrder(500)]
[DisallowMultipleComponent]
public sealed class QuestRiskSnapshotController :
    MonoBehaviour,
    IRiskSnapshotSequenceProvider
{
    [SerializeField] private QuestRiskExperimentLogger measurementProvider;
    [SerializeField] private DynamicRiskController dynamicRiskController;
    [SerializeField] private QuestPersonDetectionRunner detectionRunner;
    [SerializeField, Min(1f)] private float snapshotRateHz = 20f;
    [SerializeField] private RiskSnapshotBuilderSettings settings =
        new RiskSnapshotBuilderSettings();
    [SerializeField] private bool logAvailabilityTransitions = true;

    private readonly RiskSnapshotBuildInput buildInput =
        new RiskSnapshotBuildInput();
    private RiskSnapshotBuilder builder;
    private double nextSnapshotAt;
    private bool previousDynamicReady;
    private bool hasPublished;

    public event Action<RiskSnapshot> SnapshotPublished;

    public RiskSnapshot Latest { get; private set; }

    public long LatestSnapshotSequence
    {
        get { return Latest == null ? 0L : Latest.Sequence; }
    }

    private void Awake()
    {
        ResolveReferences();
        RebuildBuilder();
    }

    private void OnEnable()
    {
        nextSnapshotAt = 0.0;
    }

    private void LateUpdate()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (now < nextSnapshotAt)
        {
            return;
        }

        nextSnapshotAt = now + 1.0 / Mathf.Max(1f, snapshotRateHz);
        Publish(now);
    }

    public void Configure(
        QuestRiskExperimentLogger logger,
        DynamicRiskController dynamicController,
        QuestPersonDetectionRunner runner)
    {
        measurementProvider = logger;
        dynamicRiskController = dynamicController;
        detectionRunner = runner;
        ResolveReferences();
        RebuildBuilder();
    }

    public RiskSnapshot Publish(double timestampSeconds)
    {
        ResolveReferences();
        if (builder == null)
        {
            RebuildBuilder();
        }

        buildInput.TimestampSeconds = timestampSeconds;
        buildInput.CameraReady =
            detectionRunner != null && detectionRunner.IsCameraReady;
        buildInput.RoomSceneReady =
            measurementProvider != null
            && measurementProvider.SceneDataAvailable;
        buildInput.MotionReady =
            measurementProvider != null
            && measurementProvider.CurrentMotionSnapshot != null;
        buildInput.StaticMeasurement =
            measurementProvider == null
                ? StaticRiskMeasurement.Unavailable
                : measurementProvider.CurrentStaticMeasurement;
        buildInput.MotionSnapshot =
            measurementProvider == null
                ? null
                : measurementProvider.CurrentMotionSnapshot;
        buildInput.DynamicFrame =
            dynamicRiskController == null
                ? null
                : dynamicRiskController.LatestFrame;
        buildInput.IntentReady = false;
        buildInput.IntentRisk = 0f;
        buildInput.IntentConfidence = 0f;

        RiskSnapshot snapshot = builder.Build(buildInput);
        Latest = snapshot;

        if (logAvailabilityTransitions
            && hasPublished
            && previousDynamicReady != snapshot.Sources.DynamicReady)
        {
            Debug.Log(
                string.Format(
                    "[RiskSnapshot] Dynamic source {0} at seq={1}, age={2:F3}s",
                    snapshot.Sources.DynamicReady ? "READY" : "STALE/UNAVAILABLE",
                    snapshot.Sequence,
                    snapshot.Sources.DynamicSourceAgeSeconds));
        }

        previousDynamicReady = snapshot.Sources.DynamicReady;
        hasPublished = true;
        SnapshotPublished?.Invoke(snapshot);
        return snapshot;
    }

    private void ResolveReferences()
    {
        if (measurementProvider == null)
        {
            measurementProvider =
                FindFirstObjectByType<QuestRiskExperimentLogger>();
        }

        if (dynamicRiskController == null)
        {
            dynamicRiskController =
                FindFirstObjectByType<DynamicRiskController>();
        }

        if (detectionRunner == null)
        {
            detectionRunner =
                FindFirstObjectByType<QuestPersonDetectionRunner>();
        }
    }

    private void RebuildBuilder()
    {
        builder = new RiskSnapshotBuilder(
            settings ?? new RiskSnapshotBuilderSettings());
        Latest = null;
        hasPublished = false;
        previousDynamicReady = false;
    }
}
