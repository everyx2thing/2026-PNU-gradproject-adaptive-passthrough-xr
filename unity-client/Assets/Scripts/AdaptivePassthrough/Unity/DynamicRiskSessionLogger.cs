using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    [DisallowMultipleComponent]
    public sealed class DynamicRiskSessionLogger : MonoBehaviour
    {
        [Serializable]
        private sealed class LogRecord
        {
            public string recordType;
            public string utc;
            public double timestampSeconds;
            public long latestRiskSnapshotSequence;
            public int confirmedPersonCount;
            public int trackId;
            public string label;
            public float confidence;
            public float centerX;
            public float centerY;
            public float width;
            public float height;
            public string screenZone;
            public string userRelativeDirection;
            public string distanceBand;
            public string motionState;
            public float scaleRatePerSecond;
            public float ttcSecondsApprox;
            public bool hasTtc;
            public float collisionPath;
            public float dynamicRisk;
            public string riskLevel;
            public string reasons;
        }

        [SerializeField] private DynamicRiskController controller;
        [SerializeField] private MonoBehaviour snapshotSequenceProviderBehaviour;
        [SerializeField] private bool enableLogging = true;
        [SerializeField, Min(1)] private int flushEveryRecords = 10;
        [SerializeField] private string filePrefix = "dynamic-risk";

        private StreamWriter writer;
        private int pendingRecords;
        private IRiskSnapshotSequenceProvider snapshotSequenceProvider;

        public string CurrentLogPath { get; private set; }

        private void Awake()
        {
            if (controller == null)
            {
                controller = GetComponent<DynamicRiskController>();
            }

            ResolveSnapshotSequenceProvider();
        }

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.FrameProcessed += OnFrameProcessed;
            }

            if (enableLogging)
            {
                OpenWriter();
            }
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.FrameProcessed -= OnFrameProcessed;
            }

            CloseWriter();
        }

        private void OnFrameProcessed(DynamicRiskFrame frame)
        {
            if (!enableLogging || writer == null || frame == null)
            {
                return;
            }

            WriteRecord(new LogRecord
            {
                recordType = "frame",
                utc = DateTime.UtcNow.ToString("O"),
                timestampSeconds = frame.TimestampSeconds,
                latestRiskSnapshotSequence = LatestSnapshotSequence(),
                confirmedPersonCount = frame.ConfirmedPersonCount,
                dynamicRisk = frame.MaximumRisk,
                riskLevel = frame.MaximumLevel.ToString()
            });

            for (int i = 0; i < frame.Assessments.Count; i++)
            {
                DynamicRiskAssessment assessment = frame.Assessments[i];
                NormalizedBoundingBox box = assessment.Detection.boundingBox;
                var record = new LogRecord
                {
                    recordType = "assessment",
                    utc = DateTime.UtcNow.ToString("O"),
                    timestampSeconds = frame.TimestampSeconds,
                    latestRiskSnapshotSequence = LatestSnapshotSequence(),
                    confirmedPersonCount = frame.ConfirmedPersonCount,
                    trackId = assessment.TrackId,
                    label = assessment.Detection.label,
                    confidence = assessment.Detection.confidence,
                    centerX = box.centerX,
                    centerY = box.centerY,
                    width = box.width,
                    height = box.height,
                    screenZone = assessment.Location.ScreenZone.ToString(),
                    userRelativeDirection = assessment.Location.UserRelativeDirection,
                    distanceBand = assessment.Location.DistanceBand.ToString(),
                    motionState = assessment.Motion.State.ToString(),
                    scaleRatePerSecond = assessment.Motion.ScaleRatePerSecond,
                    ttcSecondsApprox = assessment.Motion.TtcSecondsApprox.GetValueOrDefault(),
                    hasTtc = assessment.Motion.TtcSecondsApprox.HasValue,
                    collisionPath = assessment.Breakdown.CollisionPath,
                    dynamicRisk = assessment.Score,
                    riskLevel = assessment.Level.ToString(),
                    reasons = string.Join(",", assessment.Reasons)
                };

                WriteRecord(record);
            }

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

        private long LatestSnapshotSequence()
        {
            return snapshotSequenceProvider == null
                ? 0L
                : snapshotSequenceProvider.LatestSnapshotSequence;
        }

        private void ResolveSnapshotSequenceProvider()
        {
            snapshotSequenceProvider =
                snapshotSequenceProviderBehaviour
                as IRiskSnapshotSequenceProvider;
            if (snapshotSequenceProvider != null)
            {
                return;
            }

            MonoBehaviour[] behaviours =
                FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IRiskSnapshotSequenceProvider provider)
                {
                    snapshotSequenceProviderBehaviour = behaviours[i];
                    snapshotSequenceProvider = provider;
                    return;
                }
            }
        }

        private void OpenWriter()
        {
            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "RiskLogs");
                Directory.CreateDirectory(directory);
                CurrentLogPath = Path.Combine(
                    directory,
                    string.Format("{0}-{1:yyyyMMdd-HHmmss}.jsonl", filePrefix, DateTime.Now));
                writer = new StreamWriter(
                    CurrentLogPath,
                    false,
                    new UTF8Encoding(false));
                Debug.Log("[DynamicRisk] Logging to " + CurrentLogPath);
            }
            catch (Exception exception)
            {
                enableLogging = false;
                Debug.LogError("[DynamicRisk] Could not open session log: " + exception.Message);
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
}
