using UnityEngine;

namespace TeamVR.Experiment
{
    [DefaultExecutionOrder(700)]
    [DisallowMultipleComponent]
    public sealed class PassthroughConditionSwitcher : MonoBehaviour
    {
        [Header("Existing Adaptive Runtime")]
        [SerializeField]
        private SelectivePassthroughController selectivePassthrough;
        [SerializeField]
        private BoundaryVisibilityController boundaryVisibility;

        public ExperimentCondition CurrentCondition { get; private set; } =
            ExperimentCondition.Adaptive;

        public bool OverrideActive { get; private set; }

        public ExperimentPresentationOverride PresentationOverride =>
            selectivePassthrough == null
                ? ExperimentPresentationOverride.UseUserSettings
                : selectivePassthrough.ExperimentPresentationOverride;

        public BoundaryVisibilityOverride BoundaryOverride =>
            boundaryVisibility == null
                ? BoundaryVisibilityOverride.UseConfiguredPolicy
                : boundaryVisibility.ExperimentOverride;

        public bool RequestedBoundarySuppression =>
            boundaryVisibility != null
            && boundaryVisibility.RequestedBoundarySuppression;

        public bool ActualBoundarySuppressed =>
            boundaryVisibility != null
            && boundaryVisibility.ActualBoundarySuppressed;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDisable()
        {
            RestoreConfiguredBehavior();
        }

        public void Configure(
            SelectivePassthroughController presentation,
            BoundaryVisibilityController boundary)
        {
            selectivePassthrough = presentation;
            boundaryVisibility = boundary;
        }

        public bool ValidateReferences(out string error)
        {
            ResolveReferences();
            if (selectivePassthrough == null)
            {
                error = "Adaptive presentation controller is missing.";
                return false;
            }

            if (boundaryVisibility == null)
            {
                error = "Boundary visibility controller is missing.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool ApplyCondition(
            ExperimentCondition condition,
            out string error)
        {
            if (!ValidateReferences(out error))
            {
                return false;
            }

            CurrentCondition = condition;
            OverrideActive = true;
            if (!ExperimentRuntimeOverrideResolver.TryResolve(
                    (int)condition,
                    out ExperimentPresentationOverride presentation,
                    out BoundaryVisibilityOverride boundary))
            {
                RestoreConfiguredBehavior();
                error = "Unknown experiment condition.";
                return false;
            }

            selectivePassthrough.SetExperimentPresentationOverride(
                presentation);
            boundaryVisibility.SetExperimentOverride(boundary);

            error = string.Empty;
            return true;
        }

        public void ApplyCondition(ExperimentCondition condition)
        {
            if (!ApplyCondition(condition, out string error))
            {
                Debug.LogError(
                    "[PassthroughConditionSwitcher] " + error,
                    this);
            }
        }

        public bool IsConditionReady()
        {
            if (!OverrideActive)
            {
                return false;
            }

            return CurrentCondition != ExperimentCondition.GuardianDefault
                || !ActualBoundarySuppressed;
        }

        public string GetReadinessFailure()
        {
            if (CurrentCondition == ExperimentCondition.GuardianDefault
                && ActualBoundarySuppressed)
            {
                return "Guardian is still suppressed. Disable full "
                    + "Boundaryless or complete Roomscale setup before "
                    + "starting the Guardian condition.";
            }

            return "Experiment condition did not become ready.";
        }

        public void RestoreConfiguredBehavior()
        {
            ResolveReferences();
            selectivePassthrough?.SetExperimentPresentationOverride(
                ExperimentPresentationOverride.UseUserSettings);
            boundaryVisibility?.SetExperimentOverride(
                BoundaryVisibilityOverride.UseConfiguredPolicy);
            OverrideActive = false;
        }

        private void ResolveReferences()
        {
            if (selectivePassthrough == null)
            {
                selectivePassthrough =
                    FindAnyObjectByType<SelectivePassthroughController>();
            }

            if (boundaryVisibility == null)
            {
                boundaryVisibility =
                    FindAnyObjectByType<BoundaryVisibilityController>();
            }
        }
    }
}
