using TeamVR.AdaptivePassthrough;
using TMPro;
using UnityEngine;

[DefaultExecutionOrder(700)]
[DisallowMultipleComponent]
public sealed class IndependentPassthroughHud : MonoBehaviour
{
    [SerializeField] private StaticPassthroughPolicyController staticPolicy;
    [SerializeField] private DynamicPassthroughPolicyController dynamicPolicy;
    [SerializeField] private SelectivePassthroughController presentation;
    [SerializeField] private TMP_Text summaryText;
    [SerializeField, Min(1f)] private float refreshRateHz = 5f;

    private double nextRefreshAt;

    private void Awake()
    {
        ResolveReferences();
    }

    private void LateUpdate()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (now < nextRefreshAt)
        {
            return;
        }

        nextRefreshAt = now + 1.0 / Mathf.Max(1f, refreshRateHz);
        Refresh();
    }

    public void Configure(
        StaticPassthroughPolicyController staticController,
        DynamicPassthroughPolicyController dynamicController,
        SelectivePassthroughController presentationController,
        TMP_Text text)
    {
        staticPolicy = staticController;
        dynamicPolicy = dynamicController;
        presentation = presentationController;
        summaryText = text;
        Refresh();
    }

    public void Refresh()
    {
        ResolveReferences();
        if (summaryText == null)
        {
            return;
        }

        PassthroughSourceDecision staticDecision =
            staticPolicy == null ? null : staticPolicy.Latest;
        StaticPassthroughDecision staticDetail =
            staticPolicy == null ? null : staticPolicy.LatestStatic;
        PassthroughSourceDecision dynamicDecision =
            dynamicPolicy == null ? null : dynamicPolicy.Latest;
        string staticRisk =
            staticDecision == null || !staticDecision.Available
                ? "N/A"
                : staticDecision.Risk.ToString("F2");
        string dynamicRisk =
            dynamicDecision == null || !dynamicDecision.Available
                ? "N/A"
                : dynamicDecision.Risk.ToString("F2");
        bool staticOn =
            staticDecision != null && staticDecision.Enabled;
        bool dynamicOn =
            dynamicDecision != null && dynamicDecision.Enabled;
        bool staticControlOn =
            presentation == null || presentation.StaticFeatureEnabled;
        bool dynamicControlOn =
            presentation == null || presentation.DynamicFeatureEnabled;
        string staticDetailSummary = staticDetail == null
            ? "Head --  Hand --  User --  Threshold --"
            : string.Format(
                "Head {0:F2}  Hand {1:F2}  User {2:F2}  "
                + "ON/OFF {3:F2}/{4:F2}  {5}/{6}",
                staticDetail.HeadRisk,
                staticDetail.HandRisk,
                staticDetail.UserState01,
                staticDetail.EffectiveOnThreshold,
                staticDetail.EffectiveOffThreshold,
                staticDetail.WarningLevel,
                staticDetail.Cause);
        DynamicRiskFrame dynamicFrame =
            dynamicPolicy == null
                || dynamicPolicy.DynamicRiskController == null
                    ? null
                    : dynamicPolicy.DynamicRiskController.LatestFrame;
        DynamicRiskAssessment primary = PrimaryAssessment(dynamicFrame);
        string personSummary;
        if (primary == null)
        {
            personSummary = "Person: none | Depth: --";
        }
        else
        {
            string distance = primary.Location.HasMetricDistance
                ? primary.Location.FilteredDistanceMeters.ToString("F2")
                    + " m [DEPTH]"
                : "-- [BBOX]";
            string closing = primary.Motion.HasMetricMotion
                ? primary.Motion.ClosingSpeedMetersPerSecond.ToString("F2")
                    + " m/s"
                : "--";
            string ttc = primary.Motion.HasMetricMotion
                    && primary.Motion.MetricTtcSeconds.HasValue
                ? primary.Motion.MetricTtcSeconds.Value.ToString("F2")
                    + " s"
                : "--";
            personSummary = string.Format(
                "Person: Track {0} | Depth {1} | Closing {2} | TTC {3}",
                primary.TrackId,
                distance,
                closing,
                ttc);
        }

        summaryText.text =
            "Independent Passthrough\n"
            + string.Format(
                "Static [{0}]: Policy {1}  Risk {2}  Wall {3}\n",
                staticControlOn ? "ENABLED" : "TEST OFF",
                staticOn ? "ON" : "OFF",
                staticRisk,
                presentation != null && presentation.StaticWindowVisible
                    ? "VISIBLE"
                    : "HIDDEN")
            + "  " + staticDetailSummary + "\n"
            + string.Format(
                "Dynamic [{0}]: Policy {1}  Risk {2}  People {3}\n",
                dynamicControlOn ? "ENABLED" : "TEST OFF",
                dynamicOn ? "ON" : "OFF",
                dynamicRisk,
                dynamicFrame == null
                    ? 0
                    : dynamicFrame.ConfirmedPersonCount)
            + personSummary + "\n"
            + "Passthrough Layer: "
            + (presentation != null && presentation.AnyWindowVisible
                ? "ACTIVE"
                : "INACTIVE");
    }

    private static DynamicRiskAssessment PrimaryAssessment(
        DynamicRiskFrame frame)
    {
        if (frame == null || frame.Assessments.Count == 0)
        {
            return null;
        }

        DynamicRiskAssessment primary = frame.Assessments[0];
        for (int i = 1; i < frame.Assessments.Count; i++)
        {
            if (frame.Assessments[i].Score > primary.Score)
            {
                primary = frame.Assessments[i];
            }
        }

        return primary;
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

        if (presentation == null)
        {
            presentation =
                FindAnyObjectByType<SelectivePassthroughController>();
        }
    }
}
