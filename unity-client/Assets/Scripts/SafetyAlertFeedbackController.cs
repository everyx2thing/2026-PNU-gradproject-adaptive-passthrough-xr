using TeamVR.AdaptivePassthrough;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(650)]
[DisallowMultipleComponent]
public sealed class SafetyAlertFeedbackController : MonoBehaviour
{
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int ThicknessProperty =
        Shader.PropertyToID("_Thickness");
    private static readonly int PulseProperty = Shader.PropertyToID("_Pulse");

    [SerializeField] private Shader borderShader;
    [SerializeField] private Color borderColor = new Color(1f, 0.02f, 0.01f, 0.95f);
    [SerializeField, Range(0.01f, 0.12f)] private float borderThickness = 0.035f;
    [SerializeField, Range(0f, 1f)] private float minimumHapticAmplitude = 0.30f;
    [SerializeField, Range(0f, 1f)] private float maximumHapticAmplitude = 0.85f;
    [SerializeField, Range(0f, 1f)] private float hapticFrequency = 0.70f;

    private GameObject borderObject;
    private Mesh borderMesh;
    private Material borderMaterial;
    private MeshRenderer borderRenderer;
    private MaterialPropertyBlock propertyBlock;
    private bool alertRequested;
    private float alertIntensity;
    private bool hapticPulseOn;
    private float appliedHapticAmplitude = -1f;

    public bool AlertActive => alertRequested && isActiveAndEnabled;
    public float AlertIntensity => alertIntensity;
    public bool HapticPulseOn => hapticPulseOn;

    private void Awake()
    {
        InitializeRendering();
    }

    private void OnEnable()
    {
        InitializeRendering();
    }

    private void Update()
    {
        UpdateFeedback(Time.unscaledTime);
    }

    private void OnDisable()
    {
        SetAlertActive(false, 0f);
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            SetAlertActive(false, 0f);
        }
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused)
        {
            SetAlertActive(false, 0f);
        }
    }

    private void OnDestroy()
    {
        StopHaptics();
        DestroyRenderingResources();
    }

    private void DestroyRenderingResources()
    {
        borderRenderer = null;
        propertyBlock = null;
        DestroyRuntimeObject(borderMaterial);
        DestroyRuntimeObject(borderMesh);
        DestroyRuntimeObject(borderObject);
        borderMaterial = null;
        borderMesh = null;
        borderObject = null;
    }

    public void Configure(Shader shader)
    {
        if (borderShader == shader)
        {
            return;
        }

        borderShader = shader;
        DestroyRenderingResources();
        if (Application.isPlaying)
        {
            InitializeRendering();
        }
    }

    public void SetAlertActive(bool active, float intensity)
    {
        alertRequested = active;
        alertIntensity = active ? Mathf.Clamp01(intensity) : 0f;
        if (!active)
        {
            if (borderRenderer != null)
            {
                borderRenderer.enabled = false;
            }

            StopHaptics();
        }
    }

    public static bool IsHapticPulseOn(float timeSeconds, float intensity)
    {
        return SafetyFeedbackPulse.IsHapticPulseOn(timeSeconds, intensity);
    }

    public static float VisualPulse(float timeSeconds, float intensity)
    {
        return SafetyFeedbackPulse.VisualPulse(timeSeconds, intensity);
    }

    private void UpdateFeedback(float timeSeconds)
    {
        InitializeRendering();
        bool active = alertRequested && isActiveAndEnabled;
        if (borderRenderer != null)
        {
            borderRenderer.enabled = active;
        }

        if (!active)
        {
            StopHaptics();
            return;
        }

        if (borderRenderer != null)
        {
            propertyBlock.Clear();
            propertyBlock.SetColor(ColorProperty, borderColor);
            propertyBlock.SetFloat(
                ThicknessProperty,
                Mathf.Clamp(borderThickness, 0.01f, 0.12f));
            propertyBlock.SetFloat(
                PulseProperty,
                VisualPulse(timeSeconds, alertIntensity));
            borderRenderer.SetPropertyBlock(propertyBlock);
        }

        bool pulseOn = IsHapticPulseOn(timeSeconds, alertIntensity);
        float amplitude = pulseOn
            ? Mathf.Lerp(
                minimumHapticAmplitude,
                maximumHapticAmplitude,
                alertIntensity)
            : 0f;
        if (pulseOn != hapticPulseOn
            || Mathf.Abs(amplitude - appliedHapticAmplitude) > 0.02f)
        {
            hapticPulseOn = pulseOn;
            appliedHapticAmplitude = amplitude;
            ApplyHaptics(amplitude);
        }
    }

    private void InitializeRendering()
    {
        if (borderRenderer != null)
        {
            return;
        }

        if (borderShader == null)
        {
            borderShader = Shader.Find(
                "TeamVR/AdaptivePassthrough/SafetyAlertBorder");
        }

        if (borderShader == null || !borderShader.isSupported)
        {
            return;
        }

        borderObject = new GameObject("Runtime Safety Alert Border")
        {
            hideFlags = HideFlags.DontSave
        };
        borderObject.transform.SetParent(transform, false);
        MeshFilter filter = borderObject.AddComponent<MeshFilter>();
        borderRenderer = borderObject.AddComponent<MeshRenderer>();
        borderRenderer.shadowCastingMode = ShadowCastingMode.Off;
        borderRenderer.receiveShadows = false;
        borderRenderer.lightProbeUsage = LightProbeUsage.Off;
        borderRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        borderRenderer.sortingOrder = short.MaxValue;

        borderMesh = CreateFullscreenQuad();
        filter.sharedMesh = borderMesh;
        borderMaterial = new Material(borderShader)
        {
            name = "Runtime Safety Alert Border Material",
            hideFlags = HideFlags.DontSave,
            renderQueue = 5000
        };
        borderRenderer.sharedMaterial = borderMaterial;
        propertyBlock = new MaterialPropertyBlock();
        borderRenderer.enabled = false;
    }

    private static Mesh CreateFullscreenQuad()
    {
        var mesh = new Mesh
        {
            name = "Runtime Safety Alert Fullscreen Quad",
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

    private void ApplyHaptics(float amplitude)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        OVRInput.SetControllerVibration(
            Mathf.Clamp01(hapticFrequency),
            Mathf.Clamp01(amplitude),
            OVRInput.Controller.LTouch | OVRInput.Controller.RTouch);
    }

    private void StopHaptics()
    {
        hapticPulseOn = false;
        appliedHapticAmplitude = 0f;
        ApplyHaptics(0f);
    }

    private static void DestroyRuntimeObject(Object target)
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
