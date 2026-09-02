using UnityEngine;

[DefaultExecutionOrder(650)]
[DisallowMultipleComponent]
public sealed class BoundaryVisibilityController : MonoBehaviour
{
    [Header("Priority")]
    [SerializeField] private bool preferFullBoundaryless = true;
    [SerializeField] private bool contextualFallbackEnabled = true;

    [Header("Runtime Sources")]
    [SerializeField] private OVRManager ovrManager;
    [SerializeField] private SelectivePassthroughController presentation;
    [SerializeField] private OVRPassthroughLayer passthroughLayer;

    private bool ownsContextualSuppression;
    private BoundaryVisibilityOverride experimentOverride =
        BoundaryVisibilityOverride.UseConfiguredPolicy;
    private bool configuredRequestBeforeExperiment;
    private bool configuredBoundarylessBeforeExperiment;
    private bool waitingForExperimentSuppressionRelease;

    public bool PreferFullBoundaryless
    {
        get { return preferFullBoundaryless; }
    }

    public bool ContextualFallbackEnabled
    {
        get { return contextualFallbackEnabled; }
    }

    public bool FullBoundarylessObserved { get; private set; }

    public bool OwnsContextualSuppression
    {
        get { return ownsContextualSuppression; }
    }

    public BoundaryVisibilityOverride ExperimentOverride =>
        experimentOverride;

    public bool RequestedBoundarySuppression =>
        experimentOverride == BoundaryVisibilityOverride.ForceSuppressed
        || (ovrManager != null
            && ovrManager.shouldBoundaryVisibilityBeSuppressed);

    public bool ActualBoundarySuppressed =>
        ovrManager != null && ovrManager.isBoundaryVisibilitySuppressed;

    private void Awake()
    {
        ResolveReferences();
        SynchronizeBoundaryRequest();
    }

    private void OnEnable()
    {
        OVRManager.BoundaryVisibilityChanged += OnBoundaryVisibilityChanged;
        ResolveReferences();
        SynchronizeBoundaryRequest();
    }

    private void LateUpdate()
    {
        ResolveReferences();
        SynchronizeBoundaryRequest();
    }

    private void OnDisable()
    {
        OVRManager.BoundaryVisibilityChanged -= OnBoundaryVisibilityChanged;
        if (ownsContextualSuppression && ovrManager != null)
        {
            ovrManager.shouldBoundaryVisibilityBeSuppressed = false;
        }

        ownsContextualSuppression = false;
    }

    public void Configure(
        OVRManager manager,
        SelectivePassthroughController presentationController,
        OVRPassthroughLayer layer)
    {
        ovrManager = manager;
        presentation = presentationController;
        passthroughLayer = layer;
        ResolveReferences();
    }

    public void SetExperimentOverride(BoundaryVisibilityOverride value)
    {
        BoundaryVisibilityOverride next = System.Enum.IsDefined(
            typeof(BoundaryVisibilityOverride),
            value)
            ? value
            : BoundaryVisibilityOverride.UseConfiguredPolicy;

        bool enteringExperiment =
            experimentOverride
                == BoundaryVisibilityOverride.UseConfiguredPolicy
            && next != BoundaryVisibilityOverride.UseConfiguredPolicy;
        bool leavingExperiment =
            experimentOverride
                != BoundaryVisibilityOverride.UseConfiguredPolicy
            && next == BoundaryVisibilityOverride.UseConfiguredPolicy;

        ResolveReferences();
        if (enteringExperiment && ovrManager != null)
        {
            configuredRequestBeforeExperiment =
                ovrManager.shouldBoundaryVisibilityBeSuppressed;
            configuredBoundarylessBeforeExperiment =
                FullBoundarylessObserved
                || (ovrManager.isBoundaryVisibilitySuppressed
                    && !ownsContextualSuppression);
        }

        experimentOverride = next;
        ownsContextualSuppression = false;
        if (leavingExperiment && ovrManager != null)
        {
            FullBoundarylessObserved =
                configuredBoundarylessBeforeExperiment;
            ovrManager.shouldBoundaryVisibilityBeSuppressed =
                configuredRequestBeforeExperiment;
            waitingForExperimentSuppressionRelease =
                !configuredBoundarylessBeforeExperiment
                && !configuredRequestBeforeExperiment
                && ovrManager.isBoundaryVisibilitySuppressed;
        }

        SynchronizeBoundaryRequest();
    }

    private void SynchronizeBoundaryRequest()
    {
        if (ovrManager == null)
        {
            return;
        }

        if (experimentOverride == BoundaryVisibilityOverride.ForceVisible)
        {
            ownsContextualSuppression = false;
            ovrManager.shouldBoundaryVisibilityBeSuppressed = false;
            return;
        }

        if (experimentOverride == BoundaryVisibilityOverride.ForceSuppressed)
        {
            ownsContextualSuppression = false;
            ovrManager.shouldBoundaryVisibilityBeSuppressed = true;
            return;
        }

        if (waitingForExperimentSuppressionRelease)
        {
            ovrManager.shouldBoundaryVisibilityBeSuppressed = false;
            if (!ovrManager.isBoundaryVisibilitySuppressed)
            {
                waitingForExperimentSuppressionRelease = false;
            }
            else
            {
                return;
            }
        }

        bool systemSuppressed =
            ovrManager.isBoundaryVisibilitySuppressed;
        if (!ownsContextualSuppression)
        {
            FullBoundarylessObserved =
                preferFullBoundaryless && systemSuppressed;
        }

        if (preferFullBoundaryless
            && systemSuppressed
            && !ownsContextualSuppression)
        {
            // Mirror the OS-owned full Boundaryless state. This prevents
            // OVRManager from issuing a conflicting restore request.
            FullBoundarylessObserved = true;
            ovrManager.shouldBoundaryVisibilityBeSuppressed = true;
            return;
        }

        if (!contextualFallbackEnabled)
        {
            if (!ownsContextualSuppression)
            {
                ovrManager.shouldBoundaryVisibilityBeSuppressed =
                    preferFullBoundaryless && systemSuppressed;
            }
            return;
        }

        if (IsSelectivePassthroughActive())
        {
            if (!systemSuppressed
                && !ovrManager.shouldBoundaryVisibilityBeSuppressed)
            {
                ownsContextualSuppression = true;
            }

            ovrManager.shouldBoundaryVisibilityBeSuppressed = true;
            return;
        }

        if (ownsContextualSuppression)
        {
            ovrManager.shouldBoundaryVisibilityBeSuppressed = false;
            if (!systemSuppressed)
            {
                ownsContextualSuppression = false;
            }
            return;
        }

        ovrManager.shouldBoundaryVisibilityBeSuppressed =
            preferFullBoundaryless && systemSuppressed;
    }

    private bool IsSelectivePassthroughActive()
    {
        return presentation != null
            && presentation.AnyWindowVisible
            && passthroughLayer != null
            && passthroughLayer.isActiveAndEnabled
            && !passthroughLayer.hidden;
    }

    private void OnBoundaryVisibilityChanged(
        OVRPlugin.BoundaryVisibility visibility)
    {
        if (!ownsContextualSuppression)
        {
            FullBoundarylessObserved =
                preferFullBoundaryless
                && visibility == OVRPlugin.BoundaryVisibility.Suppressed;
            if (FullBoundarylessObserved && ovrManager != null)
            {
                ovrManager.shouldBoundaryVisibilityBeSuppressed = true;
            }
        }
    }

    private void ResolveReferences()
    {
        if (ovrManager == null)
        {
            ovrManager = FindAnyObjectByType<OVRManager>();
        }

        if (presentation == null)
        {
            presentation =
                FindAnyObjectByType<SelectivePassthroughController>();
        }

        if (passthroughLayer == null)
        {
            passthroughLayer = FindAnyObjectByType<OVRPassthroughLayer>();
        }
    }
}
