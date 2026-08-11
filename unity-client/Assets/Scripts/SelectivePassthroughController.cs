using System;
using System.Collections.Generic;
using TeamVR.AdaptivePassthrough;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(600)]
[DisallowMultipleComponent]
public sealed class SelectivePassthroughController :
    MonoBehaviour,
    IPersonWindowSnapshotProvider
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
    [SerializeField, Range(0.01f, 1f)] private float personMaximumWidth = 0.32f;
    [SerializeField, Range(0.01f, 1f)] private float personMinimumHeight = 0.16f;
    [SerializeField, Range(0.01f, 1f)] private float personMaximumHeight = 0.48f;
    [SerializeField, Range(0.05f, 1f)] private float maximumPersonRevealArea = 0.45f;
    [SerializeField, Min(0.001f)] private float personPositionSmoothingSeconds = 0.15f;
    [SerializeField, Min(0.001f)] private float personSizeSmoothingSeconds = 0.25f;
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
    public bool AnyWindowVisible
    {
        get
        {
            return ActivePersonWindowCount > 0
                || StaticWindowVisible;
        }
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
            return;
        }

        UpdatePersonWindows();
        UpdateWallWindow();
        SetLayerVisible(AnyWindowVisible);
    }

    private void OnDisable()
    {
        DisableAllWindows();
        SetLayerVisible(false);
    }

    private void OnDestroy()
    {
        DestroyRuntimeResources();
    }

    public void Configure(
        StaticPassthroughPolicyController staticController,
        DynamicPassthroughPolicyController dynamicController,
        OVRPassthroughLayer layer,
        Shader shader)
    {
        staticPolicy = staticController;
        dynamicPolicy = dynamicController;
        passthroughLayer = layer;
        windowShader = shader;
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
    }

    public void ToggleStaticFeature()
    {
        SetStaticFeatureEnabled(!staticFeatureEnabled);
    }

    public void ToggleDynamicFeature()
    {
        SetDynamicFeatureEnabled(!dynamicFeatureEnabled);
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

        if (dynamicFeatureEnabled
            && decision != null
            && decision.Enabled
            && frame != null)
        {
            for (int i = 0; i < frame.Assessments.Count; i++)
            {
                DynamicRiskAssessment assessment = frame.Assessments[i];
                if (assessment != null
                    && assessment.Detection != null
                    && assessment.ObservedThisFrame
                    && assessment.Score >= minimumPersonWindowRisk)
                {
                    personCandidates.Add(assessment);
                }
            }
        }

        personCandidates.Sort(CompareRiskDescending);
        int selectedCount = Mathf.Min(
            Mathf.Max(1, maximumPersonWindows),
            personCandidates.Count);
        float revealArea = 0f;
        for (int i = 0; i < selectedCount; i++)
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
            float area = rect.width * rect.height;
            if (revealArea + area > maximumPersonRevealArea
                && revealArea > 0f)
            {
                continue;
            }

            revealArea += area;
            personWindowTracker.Observe(
                assessment.TrackId,
                rect,
                assessment.Score,
                now);
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
        for (int i = 0; i < windows.Count; i++)
        {
            PersonWindowSnapshot window = windows[i];
            WindowSlot slot = FindOrAssignPersonSlot(window.TrackId);
            if (slot == null)
            {
                continue;
            }

            renderedPersonTrackIds.Add(window.TrackId);
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

    public bool TryGetPersonWindow(
        int trackId,
        out Rect rect,
        out float opacity)
    {
        PersonWindowSnapshot snapshot;
        if (personWindowTracker != null
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
            passthroughLayer.hidden = !visible;
        }
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
        initialized = false;
    }

    private void RebuildPersonWindowTracker()
    {
        personWindowTracker = new PersonWindowTracker(
            personPositionSmoothingSeconds,
            personSizeSmoothingSeconds,
            personFadeInSeconds,
            personLostHoldSeconds,
            personFadeOutSeconds);
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
