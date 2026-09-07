using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

// The transform is intentionally updated only by explicit user actions.
[DefaultExecutionOrder(650)]
[DisallowMultipleComponent]
public sealed class WorldSpacePanelPlacementController : MonoBehaviour
{
    public const float MinimumDistanceMeters = 0.5f;
    public const float MaximumDistanceMeters = 3f;
    public const float MinimumHeightMeters = -0.75f;
    public const float MaximumHeightMeters = 0.75f;
    public const float MinimumScale = 0.5f;
    public const float MaximumScale = 1.5f;

    [SerializeField] private Transform panelRoot;
    [SerializeField] private Transform head;
    [SerializeField, Range(MinimumDistanceMeters, MaximumDistanceMeters)]
    private float distanceMeters = 1f;
    [SerializeField, Range(MinimumHeightMeters, MaximumHeightMeters)]
    private float heightMeters = -0.05f;
    [SerializeField, Range(MinimumScale, MaximumScale)]
    private float scaleMultiplier = 1f;
    [SerializeField] private bool toggleVisibilityWithB;

    private Vector3 baseLocalScale;
    private bool baseScaleCaptured;
    private bool initiallyPlaced;
    private float yHoldSeconds;
    private bool bWasPressed;
    private bool panelVisible = true;
    private bool visibilityComponentsResolved;
    private Canvas panelCanvas;
    private BaseRaycaster panelRaycaster;

    public float DistanceMeters => distanceMeters;
    public float HeightMeters => heightMeters;
    public float ScaleMultiplier => scaleMultiplier;
    public Transform PanelRoot => panelRoot != null ? panelRoot : transform;
    public bool PanelVisible => panelVisible;
    public bool ToggleVisibilityWithB => toggleVisibilityWithB;

    private void Awake()
    {
        ResolveReferences();
        CaptureBaseScale();
        ResolveVisibilityComponents();
    }

    private void Start()
    {
        TryInitialPlacement();
    }

    private void Update()
    {
        ResolveReferences();
        TryInitialPlacement();
        InputDevice leftController = InputDevices.GetDeviceAtXRNode(
            XRNode.LeftHand);
        bool yPressed = leftController.isValid
            && leftController.TryGetFeatureValue(
                CommonUsages.secondaryButton,
                out bool secondaryPressed)
            && secondaryPressed;
        yHoldSeconds = yPressed
            ? yHoldSeconds + Time.unscaledDeltaTime
            : 0f;
        if (yHoldSeconds >= 1f)
        {
            yHoldSeconds = 0f;
            BringHere();
        }

        InputDevice rightController = InputDevices.GetDeviceAtXRNode(
            XRNode.RightHand);
        bool bPressed = rightController.isValid
            && rightController.TryGetFeatureValue(
                CommonUsages.secondaryButton,
                out bool secondaryPressedRight)
            && secondaryPressedRight;
        if (toggleVisibilityWithB && bPressed && !bWasPressed)
        {
            TogglePanelVisibility();
        }

        bWasPressed = bPressed;
    }

    public void Configure(Transform target, Transform headTransform = null)
    {
        panelRoot = target != null ? target : transform;
        head = headTransform;
        baseScaleCaptured = false;
        CaptureBaseScale();
        visibilityComponentsResolved = false;
        ResolveVisibilityComponents();
    }

    public void ConfigureBButtonVisibilityToggle(bool enabled)
    {
        toggleVisibilityWithB = enabled;
    }

    public void TogglePanelVisibility()
    {
        SetPanelVisible(!panelVisible);
    }

    public void SetPanelVisible(bool visible)
    {
        ResolveVisibilityComponents();
        if (visible && !panelVisible)
        {
            // Opening with B includes the old Y-button recovery behavior.
            BringHere();
        }

        panelVisible = visible;
        if (panelRaycaster != null)
        {
            panelRaycaster.enabled = visible;
        }

        if (panelCanvas != null)
        {
            panelCanvas.enabled = visible;
        }
    }

    public void SetDistance(float value)
    {
        distanceMeters = Mathf.Clamp(
            value,
            MinimumDistanceMeters,
            MaximumDistanceMeters);
        BringHere();
    }

    public void SetHeight(float value)
    {
        heightMeters = Mathf.Clamp(
            value,
            MinimumHeightMeters,
            MaximumHeightMeters);
        BringHere();
    }

    public void SetScaleMultiplier(float value)
    {
        scaleMultiplier = Mathf.Clamp(value, MinimumScale, MaximumScale);
        CaptureBaseScale();
        PanelRoot.localScale = baseLocalScale * scaleMultiplier;
    }

    public void FaceMe()
    {
        ResolveReferences();
        if (head == null)
        {
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(
            PanelRoot.position - head.position,
            Vector3.up);
        if (forward.sqrMagnitude > 0.0001f)
        {
            PanelRoot.rotation = Quaternion.LookRotation(
                forward.normalized,
                Vector3.up);
        }
    }

    public void BringHere()
    {
        ResolveReferences();
        if (head == null)
        {
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(
            head.forward,
            Vector3.up);
        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = Vector3.forward;
        }

        forward.Normalize();
        PanelRoot.position = head.position
            + forward * distanceMeters
            + Vector3.up * heightMeters;
        FaceMe();
        initiallyPlaced = true;
    }

    public void ResetPanel()
    {
        distanceMeters = 1f;
        heightMeters = -0.05f;
        scaleMultiplier = 1f;
        CaptureBaseScale();
        PanelRoot.localScale = baseLocalScale;
        BringHere();
    }

    internal void ApplyDraggedWorldPosition(Vector3 worldPosition)
    {
        ResolveReferences();
        if (head == null)
        {
            PanelRoot.position = worldPosition;
            return;
        }

        float height = Mathf.Clamp(
            worldPosition.y - head.position.y,
            MinimumHeightMeters,
            MaximumHeightMeters);
        Vector3 horizontal = Vector3.ProjectOnPlane(
            worldPosition - head.position,
            Vector3.up);
        float distance = Mathf.Clamp(
            horizontal.magnitude,
            MinimumDistanceMeters,
            MaximumDistanceMeters);
        if (horizontal.sqrMagnitude <= 0.0001f)
        {
            horizontal = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        }

        horizontal.Normalize();
        distanceMeters = distance;
        heightMeters = height;
        PanelRoot.position = head.position
            + horizontal * distance
            + Vector3.up * height;
        initiallyPlaced = true;
    }

    private void TryInitialPlacement()
    {
        if (!initiallyPlaced && head != null)
        {
            BringHere();
        }
    }

    private void ResolveReferences()
    {
        if (panelRoot == null)
        {
            panelRoot = transform;
        }

        if (head == null && Camera.main != null)
        {
            head = Camera.main.transform;
        }
    }

    private void CaptureBaseScale()
    {
        if (baseScaleCaptured)
        {
            return;
        }

        baseLocalScale = PanelRoot.localScale;
        if (scaleMultiplier > 0.001f)
        {
            baseLocalScale /= scaleMultiplier;
        }

        baseScaleCaptured = true;
    }

    private void ResolveVisibilityComponents()
    {
        if (visibilityComponentsResolved)
        {
            return;
        }

        Transform root = PanelRoot;
        panelCanvas = root.GetComponent<Canvas>();
        if (panelCanvas == null)
        {
            panelCanvas = root.GetComponentInChildren<Canvas>(true);
        }

        panelRaycaster = root.GetComponent<BaseRaycaster>();
        if (panelRaycaster == null)
        {
            panelRaycaster = root.GetComponentInChildren<BaseRaycaster>(true);
        }

        if (panelCanvas != null)
        {
            panelVisible = panelCanvas.enabled;
        }

        visibilityComponentsResolved = true;
    }
}

[DisallowMultipleComponent]
public sealed class WorldSpacePanelDragHandle : MonoBehaviour,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    [SerializeField] private WorldSpacePanelPlacementController placement;
    private Vector3 pointerOffset;

    public void Configure(WorldSpacePanelPlacementController value)
    {
        placement = value;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        ResolvePlacement();
        if (placement == null)
        {
            return;
        }

        pointerOffset = placement.PanelRoot.position
            - PointerWorldPosition(eventData, placement.PanelRoot.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        ResolvePlacement();
        if (placement == null)
        {
            return;
        }

        placement.ApplyDraggedWorldPosition(
            PointerWorldPosition(eventData, placement.PanelRoot.position)
            + pointerOffset);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        placement?.FaceMe();
    }

    private void ResolvePlacement()
    {
        if (placement == null)
        {
            placement = GetComponentInParent<
                WorldSpacePanelPlacementController>();
        }
    }

    private static Vector3 PointerWorldPosition(
        PointerEventData eventData,
        Vector3 fallback)
    {
        Vector3 world = eventData.pointerCurrentRaycast.worldPosition;
        if (world.sqrMagnitude > 0.000001f)
        {
            return world;
        }

        Camera camera = eventData.pressEventCamera;
        if (camera == null)
        {
            return fallback;
        }

        Ray ray = camera.ScreenPointToRay(eventData.position);
        float distance = Vector3.Distance(camera.transform.position, fallback);
        return ray.GetPoint(distance);
    }
}
