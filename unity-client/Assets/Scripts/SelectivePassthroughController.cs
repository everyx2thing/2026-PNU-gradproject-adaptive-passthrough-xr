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
        public int TrackId;
    }

    private static readonly int RectProperty =
        Shader.PropertyToID("_Rect");
    private static readonly int FeatherProperty =
        Shader.PropertyToID("_Feather");
    private static readonly int RevealStrengthProperty =
        Shader.PropertyToID("_RevealStrength");

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
    [SerializeField] private Camera presentationCamera;
    [SerializeField] private Rect cameraViewport =
        new Rect(0.05f, 0.18f, 0.90f, 0.72f);
    [SerializeField, Range(0.001f, 0.5f)] private float personEdgeFeather = 0.08f;
    [SerializeField, Range(0f, 1f)] private float minimumPersonWindowRisk = 0.50f;
    [SerializeField, Range(1, 5)] private int maximumPersonWindows = 3;
    [SerializeField, Range(0.01f, 1f)] private float personMinimumWidth = 0.10f;
    [SerializeField, Range(0.01f, 1f)] private float personMaximumWidth = 0.42f;
    [SerializeField, Range(0.01f, 1f)] private float personMinimumHeight = 0.16f;
    [SerializeField, Range(0.01f, 1f)] private float personMaximumHeight = 0.62f;
    [SerializeField, Range(0.05f, 1f)] private float maximumPersonRevealArea = 0.55f;
    [SerializeField, Min(0.001f)] private float personPositionSmoothingSeconds = 0.10f;
    [SerializeField, Min(0.001f)] private float personSizeSmoothingSeconds = 0.15f;
    [SerializeField, Min(0.001f)] private float personFadeInSeconds = 0.20f;
    [SerializeField, Min(0f)] private float personLostHoldSeconds = 1.50f;
    [SerializeField, Min(0.001f)] private float personFadeOutSeconds = 0.30f;
    [SerializeField, Range(0.001f, 0.5f)] private float wallEdgeFeather = 0.16f;
    [SerializeField, Range(0f, 0.49f)] private float wallViewportEdgeMargin = 0.02f;
    [SerializeField, Range(0.05f, 1f)] private float wallMinimumWidth = 0.24f;
    [SerializeField, Range(0.05f, 1f)] private float wallMaximumWidth = 0.52f;

    private readonly List<WindowSlot> personSlots =
        new List<WindowSlot>();
    private readonly List<DynamicRiskAssessment> personCandidates =
        new List<DynamicRiskAssessment>();
    private readonly HashSet<int> renderedPersonTrackIds =
        new HashSet<int>();
    private PersonWindowTracker personWindowTracker;
    private WindowSlot wallSlot;
    private Mesh sharedQuad;
    private Material runtimeMaterial;
    private bool initialized;
    private bool visibilityStateInitialized;
    private bool lastPublishedVisibility;
    private string lastPublishedVisibilitySource = "none";
    private long lastObservedFrameSequence;
    private DynamicRiskController subscribedDynamicRiskController;

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
        && AnyWindowVisible;
    public bool AlertFeedbackActive =>
        feedbackMode == SafetyFeedbackMode.RedBorderAndHaptics
        && AnyWindowVisible;
    public bool AnyWindowVisible
    {
        get
        {
            return ActivePersonWindowCount > 0
                || StaticWindowVisible;
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
        if (!enabled)
        {
            SetSlotActive(wallSlot, false);
            StaticWindowVisible = false;
        }

        PublishVisibilityState();
    }

    public void SetDynamicFeatureEnabled(bool enabled)
    {
        dynamicFeatureEnabled = enabled;
        if (!enabled)
        {
            personWindowTracker?.Reset();
            for (int i = 0; i < personSlots.Count; i++)
            {
                SetSlotActive(personSlots[i], false);
                personSlots[i].TrackId = 0;
            }

            ActivePersonWindowCount = 0;
        }

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
                assessment.ForcePassthrough
                    || assessment.Score >= minimumPersonWindowRisk);
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
        bool presentationEnabled = dynamicFeatureEnabled
            && decision != null
            && decision.Enabled;
        float renderedRevealArea = 0f;
        for (int i = 0; i < windows.Count; i++)
        {
            if (!presentationEnabled)
            {
                break;
            }

            PersonWindowSnapshot window = windows[i];
            float windowArea = window.Rect.width * window.Rect.height;
            if (renderedRevealArea + windowArea > maximumPersonRevealArea
                && renderedRevealArea > 0f)
            {
                continue;
            }
            WindowSlot slot = FindOrAssignPersonSlot(window.TrackId);
            if (slot == null)
            {
                continue;
            }

            renderedPersonTrackIds.Add(window.TrackId);
            renderedRevealArea += windowArea;
            SetSlotProperties(
                slot,
                window.Rect,
                personEdgeFeather,
                window.Opacity);
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
                || !assessment.Location.HasWorldVelocity)
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
            Vector3 viewport = presentationCamera.WorldToViewportPoint(
                assessment.Location.WorldPoint
                    + assessment.Location.WorldVelocity * predictionAge);
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
        StaticWindowVisible = false;
        StaticPassthroughDecision decision =
            staticPolicy == null ? null : staticPolicy.LatestStatic;
        if (!staticFeatureEnabled
            || decision == null
            || !decision.Enabled
            || !decision.HazardDirectionAvailable
            || presentationCamera == null)
        {
            SetSlotActive(wallSlot, false);
            return;
        }

        Vector3 direction = decision.HazardDirectionWorld;
        Vector3 viewportPoint =
            presentationCamera.WorldToViewportPoint(
                presentationCamera.transform.position
                + direction.normalized * 2f);
        if (!SelectivePassthroughMath.IsViewportDirectionVisible(
                viewportPoint,
                wallViewportEdgeMargin))
        {
            // Rear/out-of-FOV wall handling is deliberately deferred.
            SetSlotActive(wallSlot, false);
            return;
        }

        Rect rect = SelectivePassthroughMath.WallDirectionWindowRect(
            viewportPoint.x,
            decision.CombinedRisk,
            wallMinimumWidth,
            wallMaximumWidth);
        SetSlotProperties(
            wallSlot,
            rect,
            wallEdgeFeather,
            1f);
        SetSlotActive(wallSlot, true);
        StaticWindowVisible = true;
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

        if (windowShader == null || !windowShader.isSupported)
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
            renderQueue = 5000
        };
        wallSlot = CreateSlot("Static Wall Passthrough Window");
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

        return new WindowSlot
        {
            GameObject = slotObject,
            Renderer = renderer,
            Properties = new MaterialPropertyBlock()
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

    private static void SetSlotProperties(
        WindowSlot slot,
        Rect rect,
        float feather,
        float revealStrength)
    {
        if (slot == null || slot.Renderer == null)
        {
            return;
        }

        slot.Properties.Clear();
        slot.Properties.SetVector(
            RectProperty,
            new Vector4(rect.x, rect.y, rect.width, rect.height));
        slot.Properties.SetFloat(
            FeatherProperty,
            Mathf.Clamp(feather, 0.001f, 0.5f));
        slot.Properties.SetFloat(
            RevealStrengthProperty,
            Mathf.Clamp01(revealStrength));
        slot.Renderer.SetPropertyBlock(slot.Properties);
    }

    private static void SetSlotActive(WindowSlot slot, bool active)
    {
        if (slot != null && slot.Renderer != null)
        {
            slot.Renderer.enabled = active;
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
        personWindowTracker?.Reset();
        ActivePersonWindowCount = 0;
        StaticWindowVisible = false;
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

        alertFeedback?.SetAlertActive(false, 0f);
        SetLayerVisible(feedbackRequested);
    }

    private float FeedbackRiskIntensity()
    {
        float risk = 0f;
        if (StaticWindowVisible
            && staticPolicy != null
            && staticPolicy.LatestStatic != null)
        {
            risk = Mathf.Max(
                risk,
                staticPolicy.LatestStatic.CombinedRisk);
        }

        if (ActivePersonWindowCount > 0
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
    }

    public PassthroughPresentationSnapshot GetPresentationSnapshot()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        return new PassthroughPresentationSnapshot(
            AnyWindowVisible,
            StaticWindowVisible,
            ActivePersonWindowCount > 0,
            ActivePersonWindowCount,
            GetVisibilitySource(),
            personWindowTracker == null
                ? 0f
                : personWindowTracker.GetMaximumHoldRemainingSeconds(now),
            now);
    }

    private string GetVisibilitySource()
    {
        bool staticVisible = StaticWindowVisible;
        bool dynamicVisible = ActivePersonWindowCount > 0;
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
        DestroyRuntimeObject(runtimeMaterial);
        DestroyRuntimeObject(sharedQuad);
        runtimeMaterial = null;
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
