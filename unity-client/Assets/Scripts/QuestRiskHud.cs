using TeamVR.AdaptivePassthrough;
using TMPro;
using UnityEngine;

[DefaultExecutionOrder(700)]
[DisallowMultipleComponent]
public sealed class QuestRiskHud : MonoBehaviour
{
    [SerializeField] private QuestRiskSnapshotController snapshotController;
    [SerializeField] private TMP_Text summaryText;
    [SerializeField, Min(1f)] private float refreshRateHz = 5f;

    private double _nextRefreshAt;

    public TMP_Text SummaryText
    {
        get { return summaryText; }
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        _nextRefreshAt = 0.0;
    }

    private void LateUpdate()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (now < _nextRefreshAt)
        {
            return;
        }

        _nextRefreshAt = now + 1.0 / Mathf.Max(1f, refreshRateHz);
        Refresh();
    }

    public void Configure(
        QuestRiskSnapshotController controller,
        TMP_Text text)
    {
        snapshotController = controller;
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

        RiskSnapshot snapshot =
            snapshotController == null ? null : snapshotController.Latest;
        if (snapshot == null)
        {
            summaryText.text =
                "Risk snapshot: initializing...\n"
                + "Camera and motion inputs are being prepared.";
            return;
        }

        string dynamicAge = snapshot.Dynamic.Available
            ? snapshot.Dynamic.SourceAgeSeconds.ToString("F2") + "s"
            : "-";
        string totalRisk = snapshot.Overall.Available
            ? snapshot.Overall.TotalRisk.ToString("F2")
            : "N/A";

        summaryText.text = string.Format(
            "Camera: {0} | Room: {1} | Seq: {2}\n"
            + "People: {3} | Rdynamic: {4:F2} | Age: {5}\n"
            + "User: {6} | Rstate: {7:F2} | Rstatic: {8}\n"
            + "Rtotal: {9} | {10} | Passthrough: {11}",
            snapshot.Sources.CameraReady ? "READY" : "WAIT",
            snapshot.Sources.RoomSceneReady ? "READY" : "UNAVAILABLE",
            snapshot.Sequence,
            snapshot.Dynamic.ConfirmedPersonCount,
            snapshot.Dynamic.MaximumRisk,
            dynamicAge,
            snapshot.UserState.StableState,
            snapshot.UserState.Risk,
            snapshot.Static.Available
                ? snapshot.Static.Risk.ToString("F2")
                : "N/A",
            totalRisk,
            snapshot.OverallLevel,
            snapshot.Passthrough.Enabled ? "ON" : "OFF");
    }

    private void ResolveReferences()
    {
        if (snapshotController == null)
        {
            snapshotController =
                FindFirstObjectByType<QuestRiskSnapshotController>();
        }
    }
}
