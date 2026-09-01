using UnityEngine;

namespace TeamVR.Experiment
{
    // Toggles the team's existing passthrough policy components on/off to
    // realize condition A/B/C. It never touches their internal risk
    // calculation logic, only enabled state and the passthrough layer's
    // hidden flag.
    [DefaultExecutionOrder(700)]
    [DisallowMultipleComponent]
    public sealed class PassthroughConditionSwitcher : MonoBehaviour
    {
        [Header("Team Policy Components (Adaptive Dynamic Risk System)")]
        [SerializeField] private SelectivePassthroughController selectivePassthrough;
        [SerializeField] private StaticPassthroughPolicyController staticPolicy;
        [SerializeField] private DynamicPassthroughPolicyController dynamicPolicy;
        [SerializeField] private BoundaryVisibilityController boundaryVisibility;

        [Header("Quest OS")]
        [SerializeField] private OVRPassthroughLayer passthroughLayer;
        [SerializeField] private OVRManager ovrManager;

        public ExperimentCondition CurrentCondition { get; private set; } =
            ExperimentCondition.Adaptive;

        private void Awake()
        {
            ResolveReferences();
        }

        private void LateUpdate()
        {
            // Condition A must keep the passthrough layer hidden even though
            // Quest OS can still force-show the Guardian boundary if the
            // user gets very close to a real wall. That system-level safety
            // override cannot be suppressed by the app and is not a bug.
            if (CurrentCondition == ExperimentCondition.NoPassthrough
                && passthroughLayer != null)
            {
                passthroughLayer.hidden = true;
            }
        }

        public void ApplyCondition(ExperimentCondition condition)
        {
            ResolveReferences();
            CurrentCondition = condition;

            switch (condition)
            {
                // Round 1 - guardian must stay off.
                case ExperimentCondition.NoPassthrough:
                    SetTeamPolicyEnabled(false);
                    SetBoundaryVisibilityEnabled(false);
                    ForceBoundarySuppressed(true);
                    if (passthroughLayer != null)
                    {
                        passthroughLayer.hidden = true;
                    }
                    break;

                // Round 2 - the only round where the guardian is turned on.
                case ExperimentCondition.GuardianDefault:
                    SetTeamPolicyEnabled(false);
                    SetBoundaryVisibilityEnabled(false);
                    ForceBoundarySuppressed(false);
                    break;

                // Round 3 - guardian must stay off; the team's adaptive
                // passthrough system is the safety net instead.
                case ExperimentCondition.Adaptive:
                    SetTeamPolicyEnabled(true);
                    SetBoundaryVisibilityEnabled(false);
                    ForceBoundarySuppressed(true);
                    break;
            }
        }

        // BoundaryVisibilityController.enabled=false does not, by itself,
        // reset OVRManager's suppression flag (it only resets it if that
        // controller was the one actively suppressing), so any leftover
        // state from a previous round can bleed into this one. Force the
        // flag explicitly here so each condition always ends in a known
        // guardian state regardless of round order.
        private void ForceBoundarySuppressed(bool suppressed)
        {
            if (ovrManager != null)
            {
                ovrManager.shouldBoundaryVisibilityBeSuppressed = suppressed;
            }
        }

        private void SetTeamPolicyEnabled(bool policyEnabled)
        {
            if (staticPolicy != null)
            {
                staticPolicy.enabled = policyEnabled;
            }

            if (dynamicPolicy != null)
            {
                dynamicPolicy.enabled = policyEnabled;
            }

            if (selectivePassthrough != null)
            {
                selectivePassthrough.enabled = policyEnabled;
            }
        }

        private void SetBoundaryVisibilityEnabled(bool visibilityEnabled)
        {
            if (boundaryVisibility != null)
            {
                boundaryVisibility.enabled = visibilityEnabled;
            }
        }

        private void ResolveReferences()
        {
            if (selectivePassthrough == null)
            {
                selectivePassthrough =
                    FindAnyObjectByType<SelectivePassthroughController>();
            }

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

            if (boundaryVisibility == null)
            {
                boundaryVisibility =
                    FindAnyObjectByType<BoundaryVisibilityController>();
            }

            if (passthroughLayer == null)
            {
                passthroughLayer = FindAnyObjectByType<OVRPassthroughLayer>();
            }

            if (ovrManager == null)
            {
                ovrManager = FindAnyObjectByType<OVRManager>();
            }
        }
    }
}
