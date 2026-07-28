using System;
using System.IO;
using System.Text;
using TeamVR.AdaptivePassthrough;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class RiskSnapshotSessionLogger : MonoBehaviour
{
    [Serializable]
    private sealed class LogRecord
    {
        public string recordType;
        public string utc;
        public int schemaVersion;
        public long sequence;
        public double timestampSeconds;
        public bool cameraReady;
        public bool roomSceneReady;
        public bool motionReady;
        public bool dynamicReady;
        public float dynamicSourceAgeSeconds;
        public int confirmedPersonCount;
        public int primaryTrackId;
        public string userState;
        public bool rstaticAvailable;
        public float rstatic;
        public bool rstateAvailable;
        public float rstate;
        public float rdynamic;
        public bool rintentAvailable;
        public float rintent;
        public bool rtotalAvailable;
        public float rtotal;
	public float headSpeedMps;
        public string overallLevel;
        public bool passthroughEnabled;
        public string passthroughReason;
    }

    [SerializeField] private QuestRiskSnapshotController snapshotController;
    [SerializeField] private bool enableLogging = true;
    [SerializeField, Min(1f)] private float maximumLogRateHz = 10f;
    [SerializeField, Min(1)] private int flushEveryRecords = 10;
    [SerializeField] private string filePrefix = "risk-snapshot";

    private StreamWriter writer;
    private int pendingRecords;
    private double nextLogAt;

    public string CurrentLogPath { get; private set; }

    private void Awake()
    {
        ResolveReference();
    }

    private void OnEnable()
    {
        ResolveReference();
        if (enableLogging)
        {
            OpenWriter();
        }

        if (snapshotController != null)
        {
            snapshotController.SnapshotPublished += OnSnapshotPublished;
        }
    }

    private void OnDisable()
    {
        if (snapshotController != null)
        {
            snapshotController.SnapshotPublished -= OnSnapshotPublished;
        }

        CloseWriter();
    }

    public void Configure(QuestRiskSnapshotController controller)
    {
        if (isActiveAndEnabled && snapshotController != null)
        {
            snapshotController.SnapshotPublished -= OnSnapshotPublished;
        }

        snapshotController = controller;
        if (isActiveAndEnabled && snapshotController != null)
        {
            snapshotController.SnapshotPublished += OnSnapshotPublished;
        }
    }

    private void OnSnapshotPublished(RiskSnapshot snapshot)
    {
        if (!enableLogging || writer == null || snapshot == null)
        {
            return;
        }

        if (snapshot.TimestampSeconds < nextLogAt)
        {
            return;
        }

        nextLogAt =
            snapshot.TimestampSeconds
            + 1.0 / Math.Max(1f, maximumLogRateHz);
        WriteRecord(new LogRecord
        {
            recordType = "riskSnapshot",
            utc = DateTime.UtcNow.ToString("O"),
            schemaVersion = snapshot.SchemaVersion,
            sequence = snapshot.Sequence,
            timestampSeconds = snapshot.TimestampSeconds,
            cameraReady = snapshot.Sources.CameraReady,
            roomSceneReady = snapshot.Sources.RoomSceneReady,
            motionReady = snapshot.Sources.MotionReady,
            dynamicReady = snapshot.Sources.DynamicReady,
            dynamicSourceAgeSeconds =
                snapshot.Sources.DynamicSourceAgeSeconds,
            confirmedPersonCount =
                snapshot.Dynamic.ConfirmedPersonCount,
            primaryTrackId = snapshot.Dynamic.PrimaryTrackId,
            userState = snapshot.UserState.StableState.ToString(),
            rstaticAvailable = snapshot.Static.Available,
            rstatic = snapshot.Static.Risk,
            rstateAvailable = snapshot.UserState.Available,
            rstate = snapshot.UserState.Risk,
            rdynamic = snapshot.Dynamic.MaximumRisk,
            rintentAvailable = snapshot.Intent.Available,
            rintent = snapshot.Intent.Risk,
            rtotalAvailable = snapshot.Overall.Available,
            rtotal = snapshot.Overall.TotalRisk,
	    headSpeedMps = snapshot.UserState.FilteredSpeed,
            overallLevel = snapshot.OverallLevel.ToString(),
            passthroughEnabled = snapshot.Passthrough.Enabled,
            passthroughReason = snapshot.Passthrough.Reason.ToString()
        });

        if (pendingRecords >= flushEveryRecords)
        {
            writer.Flush();
            pendingRecords = 0;
        }
    }

    private void WriteRecord(LogRecord record)
    {
        writer.WriteLine(JsonUtility.ToJson(record));
        pendingRecords++;
    }

    private void ResolveReference()
    {
        if (snapshotController == null)
        {
            snapshotController =
                FindFirstObjectByType<QuestRiskSnapshotController>();
        }
    }

    private void OpenWriter()
    {
        try
        {
            string directory = Path.Combine(
                Application.persistentDataPath,
                "RiskLogs");
            Directory.CreateDirectory(directory);
            CurrentLogPath = Path.Combine(
                directory,
                string.Format(
                    "{0}-{1:yyyyMMdd-HHmmss}.jsonl",
                    filePrefix,
                    DateTime.Now));
            writer = new StreamWriter(
                CurrentLogPath,
                false,
                new UTF8Encoding(false));
            Debug.Log("[RiskSnapshot] Logging to " + CurrentLogPath);
        }
        catch (Exception exception)
        {
            enableLogging = false;
            Debug.LogError(
                "[RiskSnapshot] Could not open session log: "
                + exception.Message);
        }
    }

    private void CloseWriter()
    {
        if (writer == null)
        {
            return;
        }

        writer.Flush();
        writer.Dispose();
        writer = null;
        pendingRecords = 0;
    }
}
