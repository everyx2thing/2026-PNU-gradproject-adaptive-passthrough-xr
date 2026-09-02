using System;
using System.Collections.Generic;
using TeamVR.AdaptivePassthrough;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(600)]
[DisallowMultipleComponent]
public sealed class SelectivePassthroughController :
    MonoBehaviour,
    IStaticPresentationDiagnosticsProvider,
    IPersonWindowSnapshotProvider,
    IPersonPresentationDiagnosticsProvider,
    IPassthroughPresentationSnapshotProvider,
    IPassthroughVisibilityEventSource,
    IDynamicPresentationModeProvider,
    IPersonPresentationFrameProvider
{
    private sealed class WindowSlot
    {
        public GameObject GameObject;
        public MeshRenderer Renderer;
        public MaterialPropertyBlock Properties;
        public MeshRenderer CueRenderer;
        public MaterialPropertyBlock CueProperties;
        public int TrackId;
        public StaticHazardKey StaticKey;
        public bool HasPendingStaticReplacement;
        public StaticHazardKey PendingStaticKey;
        public double StaticTransitionStartedAt;
        public float StaticTransitionStartOpacity;
        public double StaticPulseStartedAt;
        public float StaticVisualOpacity;
        public bool StaticEmergencyAssignment;
        public bool HasRenderedCurrentStaticAssignment;
        public bool StaticRenderedGeometryAvailable;
        public float StaticRenderedOpacity;
    }

    private sealed class StaticPresentationEntry
    {
        public readonly StaticHazardKey Key;
        public readonly PassthroughPresentationState State =
            new PassthroughPresentationState();
        public StaticHazardDecision Decision;

        public StaticPresentationEntry(StaticHazardKey key)
        {
            Key = key;
        }
    }

    private static readonly int FeatherProperty =
        Shader.PropertyToID("_Feather");
    private static readonly int RevealStrengthProperty =
        Shader.PropertyToID("_RevealStrength");
    private static readonly int WorldBottomLeftProperty =
        Shader.PropertyToID("_WorldBottomLeft");
    private static readonly int WorldBottomRightProperty =
        Shader.PropertyToID("_WorldBottomRight");
    private static readonly int WorldTopRightProperty =
        Shader.PropertyToID("_WorldTopRight");
    private static readonly int WorldTopLeftProperty =
        Shader.PropertyToID("_WorldTopLeft");
    private static readonly int ShapeProperty = Shader.PropertyToID("_Shape");
    private static readonly int AspectProperty = Shader.PropertyToID("_Aspect");
    private static readonly int PulseProperty = Shader.PropertyToID("_Pulse");
    private static readonly int CueModeProperty = Shader.PropertyToID("_CueMode");
    private const float StaticSlotReplacementFadeSeconds = 0.15f;
    private const float EmergencyReplacementMinimumOpacity = 0.35f;

    [Header("Independent Policies")]
    [SerializeField] private StaticPassthroughPolicyController staticPolicy;
    [SerializeField] private DynamicPassthroughPolicyController dynamicPolicy;
    [SerializeField] private bool staticFeatureEnabled = true;
    [SerializeField] private StaticRiskChannelMask enabledStaticChannels =
        StaticRiskChannelMask.All;
    [SerializeField] private bool dynamicFeatureEnabled = true;
    [SerializeField] private SafetyFeedbackMode feedbackMode =
        SafetyFeedbackMode.Passthrough;
    [SerializeField] private SafetyAlertFeedbackController alertFeedback;

    private ExperimentPresentationOverride experimentPresentationOverride =
        ExperimentPresentationOverride.UseUserSettings;

    [Header("Passthrough Rendering")]
    [SerializeField] private OVRPassthroughLayer passthroughLayer;
    [SerializeField] private Shader windowShader;
    [SerializeField] private Shader cueShader;
    [SerializeField] private Camera presentationCamera;
    [SerializeField] private Rect cameraViewport =
        new Rect(0.05f, 0.18f, 0.90f, 0.72f);
    [SerializeField, Range(0.001f, 0.5f)] private float personEdgeFeather = 0.065f;
    [SerializeField, Range(1, 5)] private int maximumPersonWindows = 3;
    [SerializeField, Range(0.01f, 1f)] private float personMinimumWidth = 0.10f;
    [SerializeField, Range(0.01f, 1f)] private float personMaximumWidth = 0.42f;
    [SerializeField, Range(0.01f, 1f)] private float personMinimumHeight = 0.16f;
    [SerializeField, Range(0.01f, 1f)] private float personMaximumHeight = 0.62f;
    [SerializeField, Range(0.05f, 1f)] private float maximumPersonRevealArea = 0.55f;
    [SerializeField, Min(0.001f)] private float personPositionSmoothingSeconds = 0.10f;
    [SerializeField, Min(0.001f)] private float personSizeSmoothingSeconds = 0.10f;
    [SerializeField, Min(0.001f)] private float personFadeInSeconds = 0.125f;
    [SerializeField, Min(0f)] private float personLostHoldSeconds = 1.50f;
    [SerializeField, Min(0.001f)] private float personFadeOutSeconds = 0.30f;
    [SerializeField, Min(0.50f)] private float personGeometryFreshnessSeconds =
        PersonWindowTracker.DefaultGeometryFreshnessSeconds;
    [SerializeField, Min(0f)] private float maximumAcceptedPersonCaptureAgeSeconds =
        PersonWindowTracker.DefaultMaximumAcceptedCaptureAgeSeconds;
    [SerializeField, Range(0.001f, 0.5f)] private float wallEdgeFeather = 0.065f;
    [SerializeField, Range(1, 2)] private int maximumStaticWindows = 2;
    [SerializeField, Range(0f, 0.5f)] private float staticReplacementRiskMargin = 0.10f;
    [SerializeField, Min(0f)] private float staticReplacementConfirmSeconds = 0.30f;

    private readonly List<WindowSlot> personSlots =
        new List<WindowSlot>();
    private readonly List<DynamicRiskAssessment> personCandidates =
        new List<DynamicRiskAssessment>();
    private readonly HashSet<int> renderedPersonTrackIds =
        new HashSet<int>();
    private readonly PersonRevealEligibilityTracker personRevealEligibility =
        new PersonRevealEligibilityTracker();
    private readonly List<WindowSlot> staticSlots =
        new List<WindowSlot>();
    private readonly List<StaticPresentationEntry> staticCandidates =
        new List<StaticPresentationEntry>(4);
    private readonly StaticHazardSlotCandidate[] staticSlotCandidates =
        new StaticHazardSlotCandidate[4];
    private readonly StaticPresentationEntry[] staticPresentations =
    {
        new StaticPresentationEntry(StaticHazardKey.Head),
        new StaticPresentationEntry(StaticHazardKey.LeftHand),
        new StaticPresentationEntry(StaticHazardKey.RightHand),
        new StaticPresentationEntry(StaticHazardKey.LowObstacle)
    };
    private PersonWindowTracker personWindowTracker;
    private WindowSlot corridorSlot;
    private Mesh sharedQuad;
    private Material runtimeMaterial;
    private Material runtimeCueMaterial;
    private bool initialized;
    private bool visibilityStateInitialized;
    private bool lastPublishedVisibility;
    private string lastPublishedVisibilitySource = "none";
    private long lastObservedFrameSequence;
    private long lastObservedStaticDecisionSequence;
    private StaticHazardSlotArbiter staticSlotArbiter;
    private DynamicRiskController subscribedDynamicRiskController;
    private bool stereoFallbackStaticRequested;
    private bool stereoFallbackDynamicRequested;

    public event Action<bool, string, double> VisibilityChanged;

    public int ActivePersonWindowCount { get; private set; }
    public bool DynamicPassthroughRendered =>
        !IsExperimentOutputSuppressed
        &&
        feedbackMode == SafetyFeedbackMode.Passthrough
        && ActivePersonWindowCount > 0;
    public bool DynamicFallbackActive =>
        !IsExperimentOutputSuppressed && stereoFallbackDynamicRequested;
    public int LatestQualifiedPersonCount { get; private set; }
    public int LatestGeometryReadyPersonCount { get; private set; }
    public PersonPresentationGeometrySource LatestPersonGeometrySource
    {
        get;
        private set;
    } = PersonPresentationGeometrySource.Unavailable;
    public PersonPresentationStatus LatestPersonPresentationStatus
    {
        get;
        private set;
    } = PersonPresentationStatus.NoFrame;
    public bool StaticWindowVisible { get; private set; }
    public int ActiveStaticWindowCount { get; private set; }
    public string LastStaticReplacementReason { get; private set; } = "none";
    public StaticPassthroughDecision LatestStaticDecision =>
        staticPolicy == null ? null : staticPolicy.LatestStatic;
    public bool StaticFeatureEnabled
    {
        get { return staticFeatureEnabled; }
    }
    public bool DynamicFeatureEnabled
    {
        get { return dynamicFeatureEnabled; }
    }
    public SafetyFeedbackMode FeedbackMode => feedbackMode;
    public ExperimentPresentationOverride ExperimentPresentationOverride =>
        experimentPresentationOverride;
    public bool IsExperimentOutputSuppressed =>
        experimentPresentationOverride
            == ExperimentPresentationOverride.SuppressAll;
    public bool PassthroughOutputVisible =>
        !IsExperimentOutputSuppressed
        &&
        feedbackMode == SafetyFeedbackMode.Passthrough
        && AnyPassthroughWindowVisible;
    public bool AlertFeedbackActive =>
        !IsExperimentOutputSuppressed
        &&
        AnyWindowVisible
        && (feedbackMode == SafetyFeedbackMode.RedBorderAndHaptics
            || stereoFallbackStaticRequested
            || stereoFallbackDynamicRequested);
    private bool AnyPassthroughWindowVisible =>
        ActivePersonWindowCount > 0 || StaticWindowVisible;
    public bool AnyWindowVisible
    {
        get
        {
            return !IsExperimentOutputSuppressed
                && (AnyPassthroughWindowVisible
                || stereoFallbackStaticRequested
                || stereoFallbackDynamicRequested);
        }
    }

    public string VisibleSource
    {
        get { return GetVisibilitySource(); }
    }

    private void Awake()
    {
        ResolveReferences();
        RebuildPersonWindowTracker();
        InitializeRendering();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (personWindowTracker == null)
        {
            RebuildPersonWindowTracker();
        }
        InitializeRendering();
        RefreshPipelineResetSubscription();
    }

    private void LateUpdate()
    {
        ResolveReferences();
        if (!initialized)
        {
            InitializeRendering();
        }

        if (!initialized)
        {
            SetLayerVisible(false);
            PublishVisibilityState();
            return;
        }

        stereoFallbackStaticRequested = false;
        stereoFallbackDynamicRequested = false;
        UpdatePersonWindows();
        UpdateWallWindow();
        ApplyFeedbackOutput();
        PublishVisibilityState();
    }

    private void OnDisable()
    {
        UnsubscribePipelineReset();
        DisableAllWindows();
        SetLayerVisible(false);
        alertFeedback?.SetAlertActive(false, 0f);
        PublishVisibilityState();
    }

    private void OnDestroy()
    {
        UnsubscribePipelineReset();
        DestroyRuntimeResources();
    }
    public StaticRiskChannelMask EnabledStaticChannels =>
        enabledStaticChannels & StaticRiskChannelMask.All;
    public bool HeadFeatureEnabled => IsStaticChannelEnabled(
        StaticRiskChannelMask.Head);
    public bool HandsFeatureEnabled => IsStaticChannelEnabled(
        StaticRiskChannelMask.Hands);
    public bool LowObstacleFeatureEnabled => IsStaticChannelEnabled(
        StaticRiskChannelMask.LowObstacle);

    public void Configure(
        StaticPassthroughPolicyController staticController,
        DynamicPassthroughPolicyController dynamicController,
        OVRPassthroughLayer layer,
        Shader shader,
        SafetyAlertFeedbackController alertController = null)
    {
        staticPolicy = staticController;
        dynamicPolicy = dynamicController;
        passthroughLayer = layer;
        windowShader = shader;
        alertFeedback = alertController;
        DestroyRuntimeResources();
        RebuildPersonWindowTracker();
        ResolveReferences();
        if (Application.isPlaying)
        {
            InitializeRendering();
        }
    }

    public void SetStaticFeatureEnabled(bool enabled)
    {
        staticFeatureEnabled = enabled;
        if (!enabled)
        {
            CancelAllStaticPresentationHolds();
        }
        PublishVisibilityState();
    }

    public bool IsStaticChannelEnabled(StaticRiskChannelMask channel)
    {
        channel &= StaticRiskChannelMask.All;
        return channel != StaticRiskChannelMask.None
            && (enabledStaticChannels & channel) == channel;
    }

    public void SetStaticChannelEnabled(
        StaticRiskChannelMask channel,
        bool enabled)
    {
        channel &= StaticRiskChannelMask.All;
        enabledStaticChannels = enabled
            ? enabledStaticChannels | channel
            : enabledStaticChannels & ~channel;
        enabledStaticChannels &= StaticRiskChannelMask.All;
        staticPolicy?.SetEnabledChannels(enabledStaticChannels);
        if (!enabled)
        {
            CancelStaticPresentationHolds(channel);
        }

        PublishVisibilityState();
    }

    public void ToggleStaticChannel(StaticRiskChannelMask channel)
    {
        SetStaticChannelEnabled(
            channel,
            !IsStaticChannelEnabled(channel));
    }

    public void SetHeadFeatureEnabled(bool enabled)
    {
        SetStaticChannelEnabled(StaticRiskChannelMask.Head, enabled);
    }

    public void SetHandsFeatureEnabled(bool enabled)
    {
        SetStaticChannelEnabled(StaticRiskChannelMask.Hands, enabled);
    }

    public void SetLowObstacleFeatureEnabled(bool enabled)
    {
        SetStaticChannelEnabled(StaticRiskChannelMask.LowObstacle, enabled);
    }

    public void ToggleHeadFeature()
    {
        ToggleStaticChannel(StaticRiskChannelMask.Head);
    }

    public void ToggleHandsFeature()
    {
        ToggleStaticChannel(StaticRiskChannelMask.Hands);
    }

    public void ToggleLowObstacleFeature()
    {
        ToggleStaticChannel(StaticRiskChannelMask.LowObstacle);
    }

    public void SetDynamicFeatureEnabled(bool enabled)
    {
        dynamicFeatureEnabled = enabled;
        PublishVisibilityState();
    }

    public void ToggleStaticFeature()
    {
        SetStaticFeatureEnabled(!staticFeatureEnabled);
    }

    public void ToggleDynamicFeature()
    {
        SetDynamicFeatureEnabled(!dynamicFeatureEnabled);
    }

    public void SetFeedbackMode(SafetyFeedbackMode mode)
    {
        feedbackMode = Enum.IsDefined(typeof(SafetyFeedbackMode), mode)
            ? mode
            : SafetyFeedbackMode.Passthrough;
        if (feedbackMode == SafetyFeedbackMode.Passthrough)
        {
            alertFeedback?.SetAlertActive(false, 0f);
        }
        else
        {
            SetLayerVisible(false);
            SuppressPassthroughWindowRenderers();
        }

        PublishVisibilityState();
    }

    public void ToggleFeedbackMode()
    {
        SetFeedbackMode(
            feedbackMode == SafetyFeedbackMode.Passthrough
                ? SafetyFeedbackMode.RedBorderAndHaptics
                : SafetyFeedbackMode.Passthrough);
    }

    public void SetExperimentPresentationOverride(
        ExperimentPresentationOverride value)
    {
        experimentPresentationOverride = Enum.IsDefined(
            typeof(ExperimentPresentationOverride),
            value)
            ? value
            : ExperimentPresentationOverride.UseUserSettings;

        if (IsExperimentOutputSuppressed)
        {
            SuppressPassthroughWindowRenderers();
            SetLayerVisible(false);
            alertFeedback?.SetAlertActive(false, 0f);
        }

        PublishVisibilityState();
    }

    private void UpdatePersonWindows()
    {
        if (personWindowTracker == null)
        {
            RebuildPersonWindowTracker();
        }

        double now = Time.realtimeSinceStartupAsDouble;
        personWindowTracker.BeginFrame();
        personCandidates.Clear();
        PassthroughSourceDecision decision =
            dynamicPolicy == null ? null : dynamicPolicy.Latest;
        DynamicRiskController controller =
            dynamicPolicy == null
                ? null
                : dynamicPolicy.DynamicRiskController;
        DynamicRiskFrame frame =
            controller == null ? null : controller.LatestFrame;
        bool dynamicPresentationActive = dynamicFeatureEnabled
            && decision != null
            && decision.Enabled;
        personWindowTracker.SetPresentationPolicyActive(
            dynamicPresentationActive,
            now);

        bool hasNewFrame = controller != null
            && controller.LatestFrameSequence > 0
            && controller.LatestFrameSequence != lastObservedFrameSequence;
        if (hasNewFrame)
        {
            lastObservedFrameSequence = controller.LatestFrameSequence;
            LatestQualifiedPersonCount = 0;
            LatestGeometryReadyPersonCount = 0;
            LatestPersonGeometrySource =
                PersonPresentationGeometrySource.Unavailable;
        }

        if (dynamicFeatureEnabled
            && frame != null
            && hasNewFrame)
        {
            for (int i = 0; i < frame.Assessments.Count; i++)
            {
                DynamicRiskAssessment assessment = frame.Assessments[i];
                if (assessment != null
                    && assessment.Detection != null
                    && assessment.ObservedThisFrame)
                {
                    personCandidates.Add(assessment);
                }
            }
        }

        personCandidates.Sort(CompareRiskDescending);
        for (int i = 0; i < personCandidates.Count; i++)
        {
            DynamicRiskAssessment assessment = personCandidates[i];
            float presentationPriorityRisk = assessment.ForcePassthrough
                ? 1f
                : assessment.Score;
            bool revealEligible = personRevealEligibility.Evaluate(
                assessment,
                dynamicFeatureEnabled,
                dynamicPolicy == null
                    ? PersonRevealEligibilityTracker.DefaultOnRisk
                    : dynamicPolicy.OnThreshold,
                dynamicPolicy == null
                    ? PersonRevealEligibilityTracker.DefaultOffRisk
                    : dynamicPolicy.OffThreshold);
            if (revealEligible)
            {
                LatestQualifiedPersonCount++;
            }
            Rect rect = SelectivePassthroughMath.FocusedPersonWindowRect(
                assessment.Detection.boundingBox,
                cameraViewport,
                presentationPriorityRisk,
                personMinimumWidth,
                personMaximumWidth,
                personMinimumHeight,
                personMaximumHeight);
            double captureRealtime = now;
            double presentedRealtime = now;
            if (controller != null)
            {
                captureRealtime =
                    controller.LatestFrameCaptureRealtimeSeconds > 0.0
                        ? controller.LatestFrameCaptureRealtimeSeconds
                        : now;
                presentedRealtime =
                    controller.LatestFrameProcessedRealtimeSeconds > 0.0
                        ? controller.LatestFrameProcessedRealtimeSeconds
                        : now;
            }

            bool geometryReady = assessment.Location.HasPresentationGeometry
                && personWindowTracker.CanAcceptGeometry(
                    captureRealtime,
                    presentedRealtime);
            if (revealEligible && geometryReady)
            {
                LatestGeometryReadyPersonCount++;
                if (LatestPersonGeometrySource
                    == PersonPresentationGeometrySource.Unavailable)
                {
                    LatestPersonGeometrySource = assessment.Location
                        .PresentationGeometrySource;
                }
            }

            personWindowTracker.Observe(
                assessment.TrackId,
                rect,
                presentationPriorityRisk,
                captureRealtime,
                presentedRealtime,
                revealEligible,
                assessment.Location.PresentationGeometry);
        }

        if (hasNewFrame && frame != null)
        {
            personRevealEligibility.PruneExcept(frame.LiveTrackIds);
        }

        personWindowTracker.Update(
            now,
            Time.unscaledDeltaTime);
        IReadOnlyList<PersonWindowSnapshot> windows =
            personWindowTracker.GetSnapshots(
                Mathf.Max(1, maximumPersonWindows));
        EnsurePersonSlots(windows.Count);
        renderedPersonTrackIds.Clear();
        int activeCount = 0;
        float renderedRevealArea = 0f;
        bool revealAreaLimited = false;
        bool slotUnavailable = false;
        for (int i = 0; i < windows.Count; i++)
        {
            PersonWindowSnapshot window = windows[i];
            float windowArea = window.Rect.width * window.Rect.height;
            if (renderedRevealArea + windowArea > maximumPersonRevealArea
                && renderedRevealArea > 0f)
            {
                revealAreaLimited = true;
                continue;
            }

            if (!window.PresentationGeometry.Available)
            {
                stereoFallbackDynamicRequested = true;
                continue;
            }

            WindowSlot slot = FindOrAssignPersonSlot(window.TrackId);
            if (slot == null)
            {
                slotUnavailable = true;
                continue;
            }

            renderedPersonTrackIds.Add(window.TrackId);
            renderedRevealArea += windowArea;
            SetSlotGeometry(
                slot,
                window.PresentationGeometry,
                personEdgeFeather,
                window.Opacity,
                window.Pulse01,
                false);
            SetSlotActive(slot, true);
            activeCount++;
        }

        for (int i = 0; i < personSlots.Count; i++)
        {
            WindowSlot slot = personSlots[i];
            if (slot.TrackId == 0
                || !renderedPersonTrackIds.Contains(slot.TrackId))
            {
                SetSlotActive(slot, false);
                slot.TrackId = 0;
            }
        }

        ActivePersonWindowCount = activeCount;
        LatestPersonPresentationStatus = ResolvePersonPresentationStatus(
            dynamicFeatureEnabled,
            frame != null,
            true,
            activeCount,
            LatestQualifiedPersonCount,
            LatestGeometryReadyPersonCount,
            revealAreaLimited,
            slotUnavailable);
    }

    public static PersonPresentationStatus ResolvePersonPresentationStatus(
        bool featureEnabled,
        bool hasFrame,
        bool policyEnabled,
        int renderedWindowCount,
        int qualifiedPersonCount,
        int geometryReadyPersonCount,
        bool revealAreaLimited,
        bool slotUnavailable)
    {
        return PersonPresentationDiagnostics.ResolveStatus(
            featureEnabled,
            hasFrame,
            policyEnabled,
            renderedWindowCount,
            qualifiedPersonCount,
            geometryReadyPersonCount,
            revealAreaLimited,
            slotUnavailable);
    }

    private void ReprojectTrackedWorldPoints(DynamicRiskFrame frame)
    {
        if (frame == null
            || presentationCamera == null
            || personWindowTracker == null)
        {
            return;
        }

        for (int i = 0; i < frame.Assessments.Count; i++)
        {
            DynamicRiskAssessment assessment = frame.Assessments[i];
            if (assessment == null
                || !assessment.Location.HasWorldPoint
                || !assessment.Location.IsMetricReliable)
            {
                continue;
            }

            double captureRealtime = dynamicPolicy == null
                || dynamicPolicy.DynamicRiskController == null
                ? Time.realtimeSinceStartupAsDouble
                : dynamicPolicy.DynamicRiskController
                    .LatestFrameCaptureRealtimeSeconds;
            float predictionAge = Mathf.Min(
                0.50f,
                (float)Math.Max(
                    0.0,
                    Time.realtimeSinceStartupAsDouble - captureRealtime));
            Vector3 predictedWorldPoint = assessment.Location.WorldPoint;
            if (assessment.Location.HasWorldVelocity)
            {
                predictedWorldPoint += assessment.Location.WorldVelocity
                    * predictionAge;
            }

            Vector3 viewport = presentationCamera.WorldToViewportPoint(
                predictedWorldPoint);
            if (viewport.z <= 0f)
            {
                continue;
            }

            personWindowTracker.ReprojectCenter(
                assessment.TrackId,
                new Vector2(viewport.x, viewport.y),
                cameraViewport);
        }
    }

    public bool TryGetPersonWindow(
        int trackId,
        out Rect rect,
        out float opacity)
    {
        PersonWindowSnapshot snapshot;
        if (!IsExperimentOutputSuppressed
            && feedbackMode == SafetyFeedbackMode.Passthrough
            && personWindowTracker != null
            && personWindowTracker.TryGetSnapshot(
                trackId,
                out snapshot))
        {
            rect = snapshot.Rect;
            opacity = snapshot.Opacity;
            return true;
        }

        rect = default;
        opacity = 0f;
        return false;
    }

    public bool IsPersonRevealEligible(DynamicRiskAssessment assessment)
    {
        if (assessment != null
            && personRevealEligibility.TryGetLatestEligibility(
                assessment.TrackId,
                out bool latestEligible))
        {
            return latestEligible;
        }

        PassthroughSourceDecision decision =
            dynamicPolicy == null ? null : dynamicPolicy.Latest;
        return PersonPresentationDiagnostics.IsRevealEligible(
            assessment,
            dynamicFeatureEnabled,
            true,
            dynamicPolicy == null
                ? PersonRevealEligibilityTracker.DefaultOnRisk
                : dynamicPolicy.OnThreshold);
    }

    public bool TryGetRenderedPersonWindow(
        int trackId,
        out Rect rect,
        out float opacity)
    {
        if (!renderedPersonTrackIds.Contains(trackId))
        {
            rect = default;
            opacity = 0f;
            return false;
        }

        return TryGetPersonWindow(trackId, out rect, out opacity);
    }

    private void UpdateWallWindow()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        StaticWindowVisible = false;
        ActiveStaticWindowCount = 0;
        StaticPassthroughDecision decision =
            staticPolicy == null ? null : staticPolicy.LatestStatic;
        bool hasNewDecision = decision != null
            && decision.SourceDecision != null
            && decision.SourceDecision.Sequence > 0
            && decision.SourceDecision.Sequence
                != lastObservedStaticDecisionSequence;
        if (hasNewDecision)
        {
            lastObservedStaticDecisionSequence =
                decision.SourceDecision.Sequence;
        }

        for (int i = 0; i < staticPresentations.Length; i++)
        {
            StaticPresentationEntry entry = staticPresentations[i];
            entry.State.BeginFrame();
            entry.Decision = FindHazardDecision(decision, entry.Key);
            bool channelActive = staticFeatureEnabled
                && entry.Decision != null
                && entry.Decision.ChannelEnabled;
            bool policyActive = channelActive
                && entry.Decision.Enabled;
            entry.State.SetPresentationPolicyActive(policyActive, now);
            if (hasNewDecision && entry.Decision != null)
            {
                HazardPresentationGeometry observedGeometry =
                    entry.Decision.PresentationGeometry;
                if (observedGeometry.Available
                    && presentationCamera != null)
                {
                    observedGeometry = WallPresentationGeometrySizing
                        .ExpandForRisk(
                            observedGeometry,
                            presentationCamera.transform.position,
                            entry.Decision.Risk,
                            entry.Decision.EmergencyTrigger
                                || entry.Decision.EmergencyHold);
                }
                double capturedAt = observedGeometry.CaptureTimestampSeconds;
                double geometryTimestamp = observedGeometry.Available
                    && capturedAt > 0.0
                    ? capturedAt
                    : now;
                bool rawEligible = policyActive
                    && entry.Decision.Available
                    && (entry.Decision.EmergencyTrigger
                        || entry.Decision.Risk
                            > entry.Decision.OffThreshold);
                entry.State.Observe(
                    observedGeometry,
                    entry.Decision.Risk,
                    geometryTimestamp,
                    now,
                    rawEligible);
            }

            entry.State.Update(now, Time.unscaledDeltaTime);
        }

        AssignStaticSlots(now);
        SetSlotActive(corridorSlot, false);
        for (int i = 0; i < staticSlots.Count; i++)
        {
            WindowSlot slot = staticSlots[i];
            AdvanceStaticSlotTransition(
                slot,
                now,
                Time.unscaledDeltaTime);
            StaticPresentationEntry entry = FindStaticPresentation(
                slot.StaticKey);
            if ((entry == null || entry.State.Opacity <= 0f)
                && slot.HasPendingStaticReplacement)
            {
                CompleteStaticSlotReplacement(slot, now);
                entry = FindStaticPresentation(slot.StaticKey);
            }

            if (entry == null || entry.State.Opacity <= 0f)
            {
                SetSlotActive(slot, false);
                MarkStaticSlotNotRendered(slot);
                continue;
            }

            HazardPresentationGeometry geometry = entry.State.Geometry;
            if (!geometry.Available)
            {
                SetSlotActive(slot, false);
                MarkStaticSlotNotRendered(slot);
                stereoFallbackStaticRequested = true;
                continue;
            }

            if (!slot.HasRenderedCurrentStaticAssignment)
            {
                RestartStaticSlotAppearance(slot, now);
            }

            float visualOpacity = Mathf.Min(
                entry.State.Opacity,
                slot.StaticVisualOpacity);
            if (visualOpacity <= 0f)
            {
                SetSlotActive(slot, false);
                MarkStaticSlotNotRendered(slot);
                continue;
            }

            SetSlotGeometry(
                slot,
                geometry,
                wallEdgeFeather,
                visualOpacity,
                StaticSlotPulse01(slot, now),
                false);
            SetSlotActive(slot, true);
            slot.HasRenderedCurrentStaticAssignment = true;
            slot.StaticRenderedGeometryAvailable = true;
            slot.StaticRenderedOpacity = visualOpacity;
            StaticWindowVisible = true;
            ActiveStaticWindowCount++;
            if (entry.Key == StaticHazardKey.LowObstacle)
            {
                UpdateLowObstacleCorridor(
                    geometry,
                    visualOpacity);
            }
        }
    }

    private void AssignStaticSlots(double now)
    {
        int slotCount = Mathf.Clamp(maximumStaticWindows, 1, 2);
        EnsureStaticSlots(slotCount);
        if (staticSlotArbiter == null
            || staticSlotArbiter.SlotCount != slotCount)
        {
            staticSlotArbiter = new StaticHazardSlotArbiter(
                slotCount,
                staticReplacementRiskMargin,
                staticReplacementConfirmSeconds);
        }
        staticCandidates.Clear();
        for (int i = 0; i < staticPresentations.Length; i++)
        {
            StaticPresentationEntry entry = staticPresentations[i];
            if (entry.State.Opacity > 0f
                || entry.Decision != null && entry.Decision.Enabled)
            {
                staticCandidates.Add(entry);
            }
        }

        for (int i = 0; i < staticCandidates.Count; i++)
        {
            StaticPresentationEntry entry = staticCandidates[i];
            StaticHazardDecision hazard = entry.Decision;
            staticSlotCandidates[i] = new StaticHazardSlotCandidate(
                entry.Key,
                true,
                IsEmergency(entry),
                PresentationRisk(entry),
                hazard == null ? 0f : hazard.TtcRisk,
                hazard == null
                    ? 0f
                    : hazard.ClosingSpeedMetersPerSecond);
        }
        staticSlotArbiter.Update(
            staticSlotCandidates,
            staticCandidates.Count,
            now);
        LastStaticReplacementReason =
            staticSlotArbiter.LastReplacementReason;
        for (int i = 0; i < staticSlots.Count; i++)
        {
            StaticHazardKey next = staticSlotArbiter.GetKey(i);
            RequestStaticSlotKey(
                staticSlots[i],
                next,
                now,
                IsEmergency(FindStaticPresentation(next)));
        }
    }

    private static bool IsEmergency(StaticPresentationEntry entry)
    {
        return entry != null
            && entry.Decision != null
            && (entry.Decision.EmergencyTrigger
                || entry.Decision.EmergencyHold);
    }

    private static float PresentationRisk(StaticPresentationEntry entry)
    {
        return entry == null
            ? 0f
            : entry.Decision == null
                ? entry.State.Risk
                : entry.Decision.Risk;
    }

    private static StaticHazardDecision FindHazardDecision(
        StaticPassthroughDecision decision,
        StaticHazardKey key)
    {
        if (decision == null || decision.Hazards == null)
        {
            return null;
        }

        for (int i = 0; i < decision.Hazards.Length; i++)
        {
            StaticHazardDecision hazard = decision.Hazards[i];
            if (hazard != null && hazard.Key == key)
            {
                return hazard;
            }
        }

        return null;
    }

    private StaticPresentationEntry FindStaticPresentation(
        StaticHazardKey key)
    {
        if (key == StaticHazardKey.None)
        {
            return null;
        }

        for (int i = 0; i < staticPresentations.Length; i++)
        {
            if (staticPresentations[i].Key == key)
            {
                return staticPresentations[i];
            }
        }

        return null;
    }

    private static void RequestStaticSlotKey(
        WindowSlot slot,
        StaticHazardKey key,
        double now,
        bool emergency)
    {
        if (slot == null)
        {
            return;
        }

        if (slot.StaticKey == key)
        {
            if (slot.HasPendingStaticReplacement)
            {
                slot.HasPendingStaticReplacement = false;
                slot.PendingStaticKey = StaticHazardKey.None;
                slot.StaticTransitionStartedAt = 0.0;
            }
            return;
        }

        if (slot.HasPendingStaticReplacement
            && slot.PendingStaticKey == key)
        {
            if (emergency && key != StaticHazardKey.None)
            {
                AssignStaticSlotKey(
                    slot,
                    key,
                    now,
                    EmergencyReplacementMinimumOpacity);
            }
            return;
        }

        if (slot.StaticKey == StaticHazardKey.None)
        {
            AssignStaticSlotKey(slot, key, now, 0f);
            return;
        }

        if (emergency && key != StaticHazardKey.None)
        {
            AssignStaticSlotKey(
                slot,
                key,
                now,
                EmergencyReplacementMinimumOpacity);
            return;
        }

        slot.HasPendingStaticReplacement = true;
        slot.PendingStaticKey = key;
        slot.StaticTransitionStartedAt = now;
        slot.StaticTransitionStartOpacity = slot.StaticVisualOpacity;
    }

    private static void AdvanceStaticSlotTransition(
        WindowSlot slot,
        double now,
        float deltaTime)
    {
        if (slot == null)
        {
            return;
        }

        if (slot.HasPendingStaticReplacement)
        {
            float elapsed = Mathf.Max(
                0f,
                (float)(now - slot.StaticTransitionStartedAt));
            float progress = Mathf.Clamp01(
                elapsed / StaticSlotReplacementFadeSeconds);
            slot.StaticVisualOpacity = Mathf.Lerp(
                slot.StaticTransitionStartOpacity,
                0f,
                progress);
            if (progress >= 1f)
            {
                CompleteStaticSlotReplacement(slot, now);
            }
            return;
        }

        if (slot.StaticKey == StaticHazardKey.None)
        {
            slot.StaticVisualOpacity = 0f;
            return;
        }

        slot.StaticVisualOpacity = Mathf.MoveTowards(
            slot.StaticVisualOpacity,
            1f,
            Mathf.Max(0f, deltaTime)
                / PassthroughPresentationState.DefaultAppearSeconds);
    }

    private static void CompleteStaticSlotReplacement(
        WindowSlot slot,
        double now)
    {
        if (slot == null || !slot.HasPendingStaticReplacement)
        {
            return;
        }

        StaticHazardKey pendingKey = slot.PendingStaticKey;
        AssignStaticSlotKey(slot, pendingKey, now, 0f);
    }

    private static void AssignStaticSlotKey(
        WindowSlot slot,
        StaticHazardKey key,
        double now,
        float startingOpacity)
    {
        SetSlotActive(slot, false);
        slot.StaticKey = key;
        slot.HasPendingStaticReplacement = false;
        slot.PendingStaticKey = StaticHazardKey.None;
        slot.StaticTransitionStartedAt = 0.0;
        slot.StaticTransitionStartOpacity = 0f;
        slot.StaticVisualOpacity = key == StaticHazardKey.None
            ? 0f
            : Mathf.Clamp01(startingOpacity);
        slot.StaticPulseStartedAt = key == StaticHazardKey.None ? 0.0 : now;
        slot.StaticEmergencyAssignment = key != StaticHazardKey.None
            && startingOpacity > 0f;
        slot.HasRenderedCurrentStaticAssignment = false;
        MarkStaticSlotNotRendered(slot);
    }

    private static void RestartStaticSlotAppearance(
        WindowSlot slot,
        double now)
    {
        if (slot == null || slot.StaticKey == StaticHazardKey.None)
        {
            return;
        }

        slot.StaticVisualOpacity = slot.StaticEmergencyAssignment
            ? EmergencyReplacementMinimumOpacity
            : 0f;
        slot.StaticPulseStartedAt = now;
        slot.HasRenderedCurrentStaticAssignment = true;
    }

    private static float StaticSlotPulse01(WindowSlot slot, double now)
    {
        if (slot == null
            || slot.StaticPulseStartedAt <= 0.0
            || now < slot.StaticPulseStartedAt)
        {
            return 0f;
        }

        float age = (float)(now - slot.StaticPulseStartedAt);
        return age < PassthroughPresentationState.DefaultPulseSeconds
            ? Mathf.Clamp01(
                age / PassthroughPresentationState.DefaultPulseSeconds)
            : 0f;
    }

    private static void MarkStaticSlotNotRendered(WindowSlot slot)
    {
        if (slot == null)
        {
            return;
        }

        slot.StaticRenderedGeometryAvailable = false;
        slot.StaticRenderedOpacity = 0f;
    }

    private static bool IsStaticSlotActuallyVisible(WindowSlot slot)
    {
        return slot != null
            && slot.StaticKey != StaticHazardKey.None
            && slot.Renderer != null
            && slot.Renderer.enabled
            && slot.StaticRenderedGeometryAvailable
            && slot.StaticRenderedOpacity > 0f;
    }

    public StaticHazardKey GetStaticSlotKey(int index)
    {
        return index >= 0 && index < staticSlots.Count
            ? staticSlots[index].StaticKey
            : StaticHazardKey.None;
    }

    public string VisibleStaticHazardKeys
    {
        get
        {
            string value = string.Empty;
            for (int i = 0; i < staticSlots.Count; i++)
            {
                if (!IsStaticSlotActuallyVisible(staticSlots[i]))
                {
                    continue;
                }

                value += value.Length == 0 ? string.Empty : ",";
                value += staticSlots[i].StaticKey.ToString();
            }

            return value;
        }
    }

    public string AssignedStaticHazardKeys
    {
        get
        {
            string value = string.Empty;
            for (int i = 0; i < staticSlots.Count; i++)
            {
                if (staticSlots[i].StaticKey == StaticHazardKey.None)
                {
                    continue;
                }

                value += value.Length == 0 ? string.Empty : ",";
                value += staticSlots[i].StaticKey.ToString();
            }

            return value;
        }
    }

    private void EnsureStaticSlots(int count)
    {
        while (staticSlots.Count < count)
        {
            staticSlots.Add(
                CreateSlot(
                    "Static Hazard Passthrough Window "
                    + (staticSlots.Count + 1)));
        }
    }

    private void CancelAllStaticPresentationHolds()
    {
        for (int i = 0; i < staticPresentations.Length; i++)
        {
            staticPresentations[i].State.CancelHoldAndFade(
                Time.realtimeSinceStartupAsDouble);
        }
    }

    private void CancelStaticPresentationHolds(
        StaticRiskChannelMask channel)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        for (int i = 0; i < staticPresentations.Length; i++)
        {
            StaticPresentationEntry entry = staticPresentations[i];
            StaticRiskChannelMask entryChannel = ChannelFor(entry.Key);
            if ((entryChannel & channel) != 0)
            {
                entry.State.CancelHoldAndFade(now);
            }
        }
    }

    private static StaticRiskChannelMask ChannelFor(StaticHazardKey key)
    {
        switch (key)
        {
            case StaticHazardKey.LeftHand:
            case StaticHazardKey.RightHand:
                return StaticRiskChannelMask.Hands;
            case StaticHazardKey.LowObstacle:
                return StaticRiskChannelMask.LowObstacle;
            case StaticHazardKey.Head:
                return StaticRiskChannelMask.Head;
            default:
                return StaticRiskChannelMask.None;
        }
    }

    private void UpdateLowObstacleCorridor(
        HazardPresentationGeometry obstacle,
        float opacity)
    {
        if (corridorSlot == null
            || presentationCamera == null
            || obstacle.Kind != HazardVisualKind.LowObstaclePatch
            || !obstacle.HasFreshFloor)
        {
            SetSlotActive(corridorSlot, false);
            return;
        }

        Vector3 up = Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(
            presentationCamera.transform.forward,
            up);
        forward = forward.sqrMagnitude > 0.0001f
            ? forward.normalized
            : Vector3.forward;
        Vector3 nearCenter = presentationCamera.transform.position
            + forward * 0.30f;
        nearCenter.y = obstacle.FloorHeight + 0.015f;
        Vector3 farCenter = obstacle.Center;
        farCenter.y = obstacle.FloorHeight + 0.015f;
        HazardPresentationGeometry corridor =
            HazardPresentationGeometry.CreateFloorCorridor(
                obstacle.StableId,
                nearCenter,
                farCenter,
                up,
                0.20f,
                0.45f,
                Time.realtimeSinceStartupAsDouble,
                obstacle.Confidence,
                obstacle.Risk,
                obstacle.Source);
        SetSlotGeometry(
            corridorSlot,
            corridor,
            0.025f,
            opacity,
            0f,
            true,
            true);
        SetSlotActive(corridorSlot, true, false, true);
    }

    private void ResolveReferences()
    {
        if (staticPolicy == null)
        {
            staticPolicy =
                FindAnyObjectByType<StaticPassthroughPolicyController>();
        }
        staticPolicy?.SetEnabledChannels(enabledStaticChannels);

        if (dynamicPolicy == null)
        {
            dynamicPolicy =
                FindAnyObjectByType<DynamicPassthroughPolicyController>();
        }

        if (passthroughLayer == null)
        {
            passthroughLayer =
                FindAnyObjectByType<OVRPassthroughLayer>();
        }

        if (presentationCamera == null)
        {
            presentationCamera = Camera.main;
        }

        if (alertFeedback == null)
        {
            alertFeedback = GetComponent<SafetyAlertFeedbackController>();
            if (alertFeedback == null)
            {
                alertFeedback =
                    FindAnyObjectByType<SafetyAlertFeedbackController>();
            }
        }

        RefreshPipelineResetSubscription();
    }

    private void InitializeRendering()
    {
        if (initialized)
        {
            return;
        }

        if (windowShader == null)
        {
            windowShader =
                Shader.Find(
                    "TeamVR/AdaptivePassthrough/PassthroughWindow");
        }

        if (cueShader == null)
        {
            cueShader = Shader.Find(
                "TeamVR/AdaptivePassthrough/HazardCue");
        }

        if (windowShader == null
            || !windowShader.isSupported
            || cueShader == null
            || !cueShader.isSupported)
        {
            Debug.LogError(
                "[PassthroughWindow] Shader is missing or unsupported.");
            return;
        }

        sharedQuad = CreateQuadMesh();
        runtimeMaterial = new Material(windowShader)
        {
            name = "Runtime Passthrough Window Material",
            hideFlags = HideFlags.DontSave,
            renderQueue = 4998
        };
        runtimeCueMaterial = new Material(cueShader)
        {
            name = "Runtime Hazard Cue Material",
            hideFlags = HideFlags.DontSave,
            renderQueue = 4999
        };
        EnsureStaticSlots(Mathf.Clamp(maximumStaticWindows, 1, 2));
        corridorSlot = CreateSlot("Low Obstacle Floor Corridor");
        EnsurePersonSlots(Mathf.Max(1, maximumPersonWindows));
        DisableAllWindows();
        initialized = true;

        if (passthroughLayer != null)
        {
            passthroughLayer.textureOpacity = 1f;
            passthroughLayer.hidden = true;
        }
    }

    private void EnsurePersonSlots(int count)
    {
        while (personSlots.Count < count)
        {
            personSlots.Add(
                CreateSlot(
                    "Dynamic Person Passthrough Window "
                    + (personSlots.Count + 1)));
        }
    }

    private WindowSlot FindOrAssignPersonSlot(int trackId)
    {
        for (int i = 0; i < personSlots.Count; i++)
        {
            if (personSlots[i].TrackId == trackId)
            {
                return personSlots[i];
            }
        }

        for (int i = 0; i < personSlots.Count; i++)
        {
            if (personSlots[i].TrackId == 0)
            {
                personSlots[i].TrackId = trackId;
                return personSlots[i];
            }
        }

        if (personSlots.Count >= Mathf.Max(1, maximumPersonWindows))
        {
            return null;
        }

        WindowSlot slot = CreateSlot(
            "Dynamic Person Passthrough Window "
            + (personSlots.Count + 1));
        slot.TrackId = trackId;
        personSlots.Add(slot);
        return slot;
    }

    private WindowSlot CreateSlot(string slotName)
    {
        var slotObject = new GameObject(slotName);
        slotObject.transform.SetParent(transform, false);
        MeshFilter filter = slotObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = slotObject.AddComponent<MeshRenderer>();
        filter.sharedMesh = sharedQuad;
        renderer.sharedMaterial = runtimeMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        var cueObject = new GameObject(slotName + " Cue");
        cueObject.transform.SetParent(slotObject.transform, false);
        MeshFilter cueFilter = cueObject.AddComponent<MeshFilter>();
        MeshRenderer cueRenderer = cueObject.AddComponent<MeshRenderer>();
        cueFilter.sharedMesh = sharedQuad;
        cueRenderer.sharedMaterial = runtimeCueMaterial;
        cueRenderer.shadowCastingMode = ShadowCastingMode.Off;
        cueRenderer.receiveShadows = false;
        cueRenderer.lightProbeUsage = LightProbeUsage.Off;
        cueRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        return new WindowSlot
        {
            GameObject = slotObject,
            Renderer = renderer,
            Properties = new MaterialPropertyBlock(),
            CueRenderer = cueRenderer,
            CueProperties = new MaterialPropertyBlock()
        };
    }

    private static Mesh CreateQuadMesh()
    {
        var mesh = new Mesh
        {
            name = "Passthrough Window Clip-Space Quad",
            hideFlags = HideFlags.DontSave
        };
        mesh.vertices = new[]
        {
            new Vector3(-1f, -1f, 0f),
            new Vector3(1f, -1f, 0f),
            new Vector3(1f, 1f, 0f),
            new Vector3(-1f, 1f, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        mesh.UploadMeshData(true);
        return mesh;
    }

    private static int CompareRiskDescending(
        DynamicRiskAssessment left,
        DynamicRiskAssessment right)
    {
        int forced = right.ForcePassthrough.CompareTo(
            left.ForcePassthrough);
        if (forced != 0)
        {
            return forced;
        }

        return right.Score.CompareTo(left.Score);
    }

    private static void SetSlotGeometry(
        WindowSlot slot,
        HazardPresentationGeometry geometry,
        float feather,
        float revealStrength,
        float pulse,
        bool cueOnly,
        bool corridorCue = false)
    {
        if (slot == null
            || slot.Renderer == null
            || !geometry.Available)
        {
            return;
        }

        slot.Properties.Clear();
        ApplyGeometryProperties(
            slot.Properties,
            geometry,
            feather,
            revealStrength,
            pulse,
            corridorCue);
        slot.Renderer.SetPropertyBlock(slot.Properties);
        if (slot.CueRenderer != null)
        {
            slot.CueProperties.Clear();
            ApplyGeometryProperties(
                slot.CueProperties,
                geometry,
                feather,
                revealStrength,
                pulse,
                corridorCue);
            slot.CueRenderer.SetPropertyBlock(slot.CueProperties);
            slot.CueRenderer.enabled = corridorCue || pulse > 0f;
        }

        slot.Renderer.enabled = !cueOnly;
    }

    private static void ApplyGeometryProperties(
        MaterialPropertyBlock properties,
        HazardPresentationGeometry geometry,
        float feather,
        float revealStrength,
        float pulse,
        bool corridorCue)
    {
        properties.SetVector(WorldBottomLeftProperty, geometry.BottomLeft);
        properties.SetVector(WorldBottomRightProperty, geometry.BottomRight);
        properties.SetVector(WorldTopRightProperty, geometry.TopRight);
        properties.SetVector(WorldTopLeftProperty, geometry.TopLeft);
        properties.SetFloat(
            ShapeProperty,
            geometry.Kind == HazardVisualKind.PersonCapsule ? 1f : 0f);
        properties.SetFloat(
            AspectProperty,
            geometry.Width / Mathf.Max(0.001f, geometry.Height));
        properties.SetFloat(
            FeatherProperty,
            Mathf.Clamp(feather, 0.001f, 0.5f));
        properties.SetFloat(
            RevealStrengthProperty,
            Mathf.Clamp01(revealStrength));
        properties.SetFloat(PulseProperty, Mathf.Clamp01(pulse));
        properties.SetFloat(CueModeProperty, corridorCue ? 1f : 0f);
    }

    private static void SetSlotActive(
        WindowSlot slot,
        bool active,
        bool mask = true,
        bool cue = true)
    {
        if (slot != null && slot.Renderer != null)
        {
            slot.Renderer.enabled = active && mask;
            if (slot.CueRenderer != null)
            {
                slot.CueRenderer.enabled = active
                    && cue
                    && slot.CueRenderer.enabled;
            }
        }
    }

    private void DisableAllWindows()
    {
        for (int i = 0; i < personSlots.Count; i++)
        {
            SetSlotActive(personSlots[i], false);
            personSlots[i].TrackId = 0;
        }

        for (int i = 0; i < staticSlots.Count; i++)
        {
            AssignStaticSlotKey(
                staticSlots[i],
                StaticHazardKey.None,
                Time.realtimeSinceStartupAsDouble,
                0f);
        }
        SetSlotActive(corridorSlot, false);
        for (int i = 0; i < staticPresentations.Length; i++)
        {
            staticPresentations[i].State.Reset();
            staticPresentations[i].Decision = null;
        }
        personWindowTracker?.Reset();
        personRevealEligibility.Reset();
        ActivePersonWindowCount = 0;
        LatestQualifiedPersonCount = 0;
        LatestGeometryReadyPersonCount = 0;
        LatestPersonGeometrySource =
            PersonPresentationGeometrySource.Unavailable;
        LatestPersonPresentationStatus = PersonPresentationStatus.NoFrame;
        ActiveStaticWindowCount = 0;
        StaticWindowVisible = false;
        stereoFallbackStaticRequested = false;
        stereoFallbackDynamicRequested = false;
        lastObservedStaticDecisionSequence = 0;
        staticSlotArbiter?.Reset();
    }

    private void SetLayerVisible(bool visible)
    {
        if (passthroughLayer != null)
        {
            passthroughLayer.hidden =
                feedbackMode != SafetyFeedbackMode.Passthrough
                || !visible;
        }
    }

    private void ApplyFeedbackOutput()
    {
        if (IsExperimentOutputSuppressed)
        {
            SuppressPassthroughWindowRenderers();
            SetLayerVisible(false);
            alertFeedback?.SetAlertActive(false, 0f);
            return;
        }

        bool feedbackRequested = AnyWindowVisible;
        if (feedbackMode == SafetyFeedbackMode.RedBorderAndHaptics)
        {
            SuppressPassthroughWindowRenderers();
            SetLayerVisible(false);
            alertFeedback?.SetAlertActive(
                feedbackRequested,
                FeedbackRiskIntensity());
            return;
        }

        bool stereoFallbackRequested = stereoFallbackStaticRequested
            || stereoFallbackDynamicRequested;
        alertFeedback?.SetAlertActive(
            stereoFallbackRequested,
            FeedbackRiskIntensity());
        SetLayerVisible(AnyPassthroughWindowVisible);
    }

    private float FeedbackRiskIntensity()
    {
        float risk = 0f;
        if ((StaticWindowVisible || stereoFallbackStaticRequested)
            && staticPolicy != null
            && staticPolicy.LatestStatic != null)
        {
            risk = Mathf.Max(
                risk,
                staticPolicy.LatestStatic.CombinedRisk);
        }

        if ((ActivePersonWindowCount > 0
                || stereoFallbackDynamicRequested)
            && dynamicPolicy != null
            && dynamicPolicy.Latest != null)
        {
            risk = Mathf.Max(risk, dynamicPolicy.Latest.Risk);
        }

        return Mathf.Clamp01(Mathf.Max(0.50f, risk));
    }

    private void SuppressPassthroughWindowRenderers()
    {
        for (int i = 0; i < personSlots.Count; i++)
        {
            SetSlotActive(personSlots[i], false);
        }

        for (int i = 0; i < staticSlots.Count; i++)
        {
            SetSlotActive(staticSlots[i], false);
        }
        SetSlotActive(corridorSlot, false);
    }

    public PassthroughPresentationSnapshot GetPresentationSnapshot()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        bool staticVisible = !IsExperimentOutputSuppressed
            && (StaticWindowVisible || stereoFallbackStaticRequested);
        bool dynamicVisible = !IsExperimentOutputSuppressed
            && (ActivePersonWindowCount > 0
                || stereoFallbackDynamicRequested);
        return new PassthroughPresentationSnapshot(
            AnyWindowVisible,
            staticVisible,
            dynamicVisible,
            ActivePersonWindowCount,
            GetVisibilitySource(),
            Mathf.Max(
                GetMaximumStaticHoldRemainingSeconds(),
                personWindowTracker == null
                    ? 0f
                    : personWindowTracker.GetMaximumHoldRemainingSeconds(now)),
            now);
    }

    private float GetMaximumStaticHoldRemainingSeconds()
    {
        float maximum = 0f;
        for (int i = 0; i < staticPresentations.Length; i++)
        {
            maximum = Mathf.Max(
                maximum,
                staticPresentations[i].State.HoldRemainingSeconds);
        }

        return maximum;
    }

    private string GetVisibilitySource()
    {
        bool staticVisible = StaticWindowVisible
            || stereoFallbackStaticRequested;
        bool dynamicVisible = ActivePersonWindowCount > 0
            || stereoFallbackDynamicRequested;
        if (IsExperimentOutputSuppressed)
        {
            return staticVisible || dynamicVisible
                ? "experiment-suppressed"
                : "none";
        }

        if (staticVisible && dynamicVisible)
        {
            return "static+dynamic";
        }

        if (staticVisible)
        {
            return "static";
        }

        return dynamicVisible ? "dynamic" : "none";
    }

    private void PublishVisibilityState()
    {
        bool visible = AnyWindowVisible;
        string source = GetVisibilitySource();
        if (visibilityStateInitialized
            && visible == lastPublishedVisibility
            && string.Equals(
                source,
                lastPublishedVisibilitySource,
                StringComparison.Ordinal))
        {
            return;
        }

        visibilityStateInitialized = true;
        lastPublishedVisibility = visible;
        lastPublishedVisibilitySource = source;
        VisibilityChanged?.Invoke(
            visible,
            source,
            Time.realtimeSinceStartupAsDouble);
    }

    private void DestroyRuntimeResources()
    {
        DisableAllWindows();
        for (int i = 0; i < personSlots.Count; i++)
        {
            DestroySlot(personSlots[i]);
        }

        personSlots.Clear();
        for (int i = 0; i < staticSlots.Count; i++)
        {
            DestroySlot(staticSlots[i]);
        }
        staticSlots.Clear();
        DestroySlot(corridorSlot);
        corridorSlot = null;
        DestroyRuntimeObject(runtimeMaterial);
        DestroyRuntimeObject(runtimeCueMaterial);
        DestroyRuntimeObject(sharedQuad);
        runtimeMaterial = null;
        runtimeCueMaterial = null;
        sharedQuad = null;
        personWindowTracker?.Reset();
        personRevealEligibility.Reset();
        lastObservedFrameSequence = 0;
        initialized = false;
    }

    private void RebuildPersonWindowTracker()
    {
        personWindowTracker = new PersonWindowTracker(
            personPositionSmoothingSeconds,
            personSizeSmoothingSeconds,
            personFadeInSeconds,
            personLostHoldSeconds,
            personFadeOutSeconds,
            0.50f,
            1.50f,
            personGeometryFreshnessSeconds,
            maximumAcceptedPersonCaptureAgeSeconds);
    }

    private void RefreshPipelineResetSubscription()
    {
        DynamicRiskController controller = dynamicPolicy == null
            ? null
            : dynamicPolicy.DynamicRiskController;
        if (ReferenceEquals(controller, subscribedDynamicRiskController))
        {
            return;
        }

        UnsubscribePipelineReset();
        subscribedDynamicRiskController = controller;
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
        personWindowTracker?.Reset();
        personRevealEligibility.Reset();
        lastObservedFrameSequence = 0;
        for (int i = 0; i < personSlots.Count; i++)
        {
            SetSlotActive(personSlots[i], false);
            personSlots[i].TrackId = 0;
        }

        ActivePersonWindowCount = 0;
        LatestQualifiedPersonCount = 0;
        LatestGeometryReadyPersonCount = 0;
        LatestPersonGeometrySource =
            PersonPresentationGeometrySource.Unavailable;
        LatestPersonPresentationStatus = PersonPresentationStatus.NoFrame;
    }

    private static void DestroySlot(WindowSlot slot)
    {
        if (slot != null && slot.GameObject != null)
        {
            DestroyRuntimeObject(slot.GameObject);
        }
    }

    private static void DestroyRuntimeObject(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}
