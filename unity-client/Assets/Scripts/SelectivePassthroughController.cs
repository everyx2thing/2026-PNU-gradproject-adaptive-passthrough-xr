using System;
using System.Collections.Generic;
using TeamVR.AdaptivePassthrough;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(600)]
[DisallowMultipleComponent]
public sealed class SelectivePassthroughController :
    MonoBehaviour,
    IPersonWindowSnapshotProvider,
    IPassthroughPresentationSnapshotProvider,
    IPassthroughVisibilityEventSource
{
    private sealed class WindowSlot
    {
        public GameObject GameObject;
        public MeshRenderer Renderer;
        public MaterialPropertyBlock Properties;
        public MeshRenderer CueRenderer;
        public MaterialPropertyBlock CueProperties;
        public int TrackId;
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

    [Header("Independent Policies")]
    [SerializeField] private StaticPassthroughPolicyController staticPolicy;
    [SerializeField] private DynamicPassthroughPolicyController dynamicPolicy;
    [SerializeField] private bool staticFeatureEnabled = true;
    [SerializeField] private bool dynamicFeatureEnabled = true;
    [SerializeField] private SafetyFeedbackMode feedbackMode =
        SafetyFeedbackMode.Passthrough;
    [SerializeField] private SafetyAlertFeedbackController alertFeedback;

    [Header("Passthrough Rendering")]
    [SerializeField] private OVRPassthroughLayer passthroughLayer;
    [SerializeField] private Shader windowShader;
    [SerializeField] private Shader cueShader;
    [SerializeField] private Camera presentationCamera;
    [SerializeField] private Rect cameraViewport =
        new Rect(0.05f, 0.18f, 0.90f, 0.72f);
    [SerializeField, Range(0.001f, 0.5f)] private float personEdgeFeather = 0.065f;
    [SerializeField, Range(0f, 1f)] private float minimumPersonWindowRisk = 0.50f;
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
    [SerializeField, Range(0.001f, 0.5f)] private float wallEdgeFeather = 0.065f;

    private readonly List<WindowSlot> personSlots =
        new List<WindowSlot>();
    private readonly List<DynamicRiskAssessment> personCandidates =
        new List<DynamicRiskAssessment>();
    private readonly HashSet<int> renderedPersonTrackIds =
        new HashSet<int>();
    private PersonWindowTracker personWindowTracker;
    private readonly PassthroughPresentationState wallPresentation =
        new PassthroughPresentationState();
    private WindowSlot wallSlot;
    private WindowSlot corridorSlot;
    private Mesh sharedQuad;
    private Material runtimeMaterial;
    private Material runtimeCueMaterial;
    private bool initialized;
    private bool visibilityStateInitialized;
    private bool lastPublishedVisibility;
    private string lastPublishedVisibilitySource = "none";
    private long lastObservedFrameSequence;
    private DynamicRiskController subscribedDynamicRiskController;
    private bool stereoFallbackStaticRequested;
    private bool stereoFallbackDynamicRequested;

    public event Action<bool, string, double> VisibilityChanged;

    public int ActivePersonWindowCount { get; private set; }
    public bool StaticWindowVisible { get; private set; }
    public bool StaticFeatureEnabled
    {
        get { return staticFeatureEnabled; }
    }
    public bool DynamicFeatureEnabled
    {
        get { return dynamicFeatureEnabled; }
    }
    public SafetyFeedbackMode FeedbackMode => feedbackMode;
    public bool PassthroughOutputVisible =>
        feedbackMode == SafetyFeedbackMode.Passthrough
        && AnyPassthroughWindowVisible;
    public bool AlertFeedbackActive =>
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
            return AnyPassthroughWindowVisible
                || stereoFallbackStaticRequested
                || stereoFallbackDynamicRequested;
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
        PublishVisibilityState();
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
            Rect rect = SelectivePassthroughMath.FocusedPersonWindowRect(
                assessment.Detection.boundingBox,
                cameraViewport,
                assessment.Score,
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

            personWindowTracker.Observe(
                assessment.TrackId,
                rect,
                assessment.Score,
                captureRealtime,
                presentedRealtime,
                decision != null
                    && decision.Enabled
                    && (assessment.ForcePassthrough
                        || assessment.Score >= minimumPersonWindowRisk),
                assessment.Location.PresentationGeometry);
        }

        ReprojectTrackedWorldPoints(frame);

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
        for (int i = 0; i < windows.Count; i++)
        {
            PersonWindowSnapshot window = windows[i];
            float windowArea = window.Rect.width * window.Rect.height;
            if (renderedRevealArea + windowArea > maximumPersonRevealArea
                && renderedRevealArea > 0f)
            {
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
        if (feedbackMode == SafetyFeedbackMode.Passthrough
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

    private void UpdateWallWindow()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        wallPresentation.BeginFrame();
        StaticWindowVisible = false;
        StaticPassthroughDecision decision =
            staticPolicy == null ? null : staticPolicy.LatestStatic;
        bool revealEligible = staticFeatureEnabled
            && decision != null
            && decision.Enabled;
        wallPresentation.SetPresentationPolicyActive(revealEligible, now);
        if (revealEligible)
        {
            double capturedAt = decision.PresentationGeometry
                .CaptureTimestampSeconds;
            double geometryTimestamp = decision.PresentationGeometry.Available
                && capturedAt > 0.0
                ? capturedAt
                : now;
            wallPresentation.Observe(
                decision.PresentationGeometry,
                decision.CombinedRisk,
                geometryTimestamp,
                now,
                true);
        }

        wallPresentation.Update(now, Time.unscaledDeltaTime);
        HazardPresentationGeometry geometry = wallPresentation.Geometry;
        if (wallPresentation.Opacity <= 0f)
        {
            SetSlotActive(wallSlot, false);
            SetSlotActive(corridorSlot, false);
            return;
        }

        if (!geometry.Available)
        {
            SetSlotActive(wallSlot, false);
            SetSlotActive(corridorSlot, false);
            stereoFallbackStaticRequested = true;
            return;
        }

        SetSlotGeometry(
            wallSlot,
            geometry,
            wallEdgeFeather,
            wallPresentation.Opacity,
            wallPresentation.Pulse01,
            false);
        SetSlotActive(wallSlot, true);
        UpdateLowObstacleCorridor(
            geometry,
            wallPresentation.Opacity);
        StaticWindowVisible = true;
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
        wallSlot = CreateSlot("Static Wall Passthrough Window");
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

        SetSlotActive(wallSlot, false);
        SetSlotActive(corridorSlot, false);
        wallPresentation.Reset();
        personWindowTracker?.Reset();
        ActivePersonWindowCount = 0;
        StaticWindowVisible = false;
        stereoFallbackStaticRequested = false;
        stereoFallbackDynamicRequested = false;
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

        SetSlotActive(wallSlot, false);
        SetSlotActive(corridorSlot, false);
    }

    public PassthroughPresentationSnapshot GetPresentationSnapshot()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        bool staticVisible = StaticWindowVisible
            || stereoFallbackStaticRequested;
        bool dynamicVisible = ActivePersonWindowCount > 0
            || stereoFallbackDynamicRequested;
        return new PassthroughPresentationSnapshot(
            AnyWindowVisible,
            staticVisible,
            dynamicVisible,
            ActivePersonWindowCount,
            GetVisibilitySource(),
            Mathf.Max(
                wallPresentation.HoldRemainingSeconds,
                personWindowTracker == null
                    ? 0f
                    : personWindowTracker.GetMaximumHoldRemainingSeconds(now)),
            now);
    }

    private string GetVisibilitySource()
    {
        bool staticVisible = StaticWindowVisible
            || stereoFallbackStaticRequested;
        bool dynamicVisible = ActivePersonWindowCount > 0
            || stereoFallbackDynamicRequested;
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
        DestroySlot(wallSlot);
        wallSlot = null;
        DestroySlot(corridorSlot);
        corridorSlot = null;
        DestroyRuntimeObject(runtimeMaterial);
        DestroyRuntimeObject(runtimeCueMaterial);
        DestroyRuntimeObject(sharedQuad);
        runtimeMaterial = null;
        runtimeCueMaterial = null;
        sharedQuad = null;
        personWindowTracker?.Reset();
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
            1.50f);
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
        lastObservedFrameSequence = 0;
        for (int i = 0; i < personSlots.Count; i++)
        {
            SetSlotActive(personSlots[i], false);
            personSlots[i].TrackId = 0;
        }

        ActivePersonWindowCount = 0;
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
