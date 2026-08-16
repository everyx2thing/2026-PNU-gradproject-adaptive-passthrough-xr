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
            public int schemaVersion = 2;
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
            public string distanceSource;
            public bool distanceAvailable;
            public float rawDistanceMeters;
            public float filteredDistanceMeters;
            public float distanceConfidence;
            public float boundingBoxArea;
            public string motionState;
            public float scaleRatePerSecond;
            public float closingSpeedMetersPerSecond;
            public float ttcSecondsApprox;
            public bool hasTtc;
            public float metricTtcSeconds;
            public bool hasMetricTtc;
            public float collisionPath;
            public float dynamicRisk;
            public string riskLevel;
            public string reasons;
            public bool observedThisFrame;
            public bool windowVisible;
            public float windowX;
            public float windowY;
            public float windowWidth;
            public float windowHeight;
            public float windowOpacity;
            public string trackingProfile;
            public int adaptiveLevel;
            public string spatialSource;
            public float spatialDistanceMeters;
            public float spatialConfidence;
            public int spatialSampleCount;
            public float spatialDispersionMeters;
            public float spatialAgeSeconds;
            public float spatialRateHz;
            public float spatialMilliseconds;
            public float inferenceRateHz;
            public float inferenceMilliseconds;
            public float framesPerSecond;
            public float frameP95Milliseconds;
            public float depthSampleDispersionMeters;
            public bool bboxDepthConflict;
            public bool idHandoff;
            public float missingSeconds;
            public string marker;
        }

        [SerializeField] private DynamicRiskController controller;
        [SerializeField] private MonoBehaviour presentationBehaviour;
        [SerializeField] private MonoBehaviour snapshotSequenceProviderBehaviour;
        [SerializeField] private TrackingQualityController trackingQuality;
#if ADAPTIVE_PASSTHROUGH_QUEST_CAMERA
        [SerializeField] private QuestSpatialObstacleProvider spatialProvider;
#endif
        [SerializeField] private bool enableLogging = true;
        [SerializeField, Min(1)] private int flushEveryRecords = 10;
        [SerializeField] private string filePrefix = "dynamic-risk";

        private StreamWriter writer;
        private int pendingRecords;
        private IRiskSnapshotSequenceProvider snapshotSequenceProvider;
        private IPersonWindowSnapshotProvider presentation;

        public string CurrentLogPath { get; private set; }

        private void Awake()
        {
            if (controller == null)
            {
                controller = GetComponent<DynamicRiskController>();
            }

            ResolveSnapshotSequenceProvider();
            ResolvePresentation();
            ResolveQualityReferences();
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

            if (trackingQuality != null)
            {
                trackingQuality.TestMarkerRequested += OnTestMarker;
            }
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.FrameProcessed -= OnFrameProcessed;
            }

            if (trackingQuality != null)
            {
                trackingQuality.TestMarkerRequested -= OnTestMarker;
            }

            CloseWriter();
        }

        private void OnFrameProcessed(DynamicRiskFrame frame)
        {
            if (!enableLogging || writer == null || frame == null)
            {
                return;
            }

            LogRecord frameRecord = NewRecord("frame", frame.TimestampSeconds);
            frameRecord.latestRiskSnapshotSequence = LatestSnapshotSequence();
            frameRecord.confirmedPersonCount = frame.ConfirmedPersonCount;
            frameRecord.dynamicRisk = frame.MaximumRisk;
            frameRecord.riskLevel = frame.MaximumLevel.ToString();
            WriteRecord(frameRecord);

            for (int i = 0; i < frame.Assessments.Count; i++)
            {
                DynamicRiskAssessment assessment = frame.Assessments[i];
                NormalizedBoundingBox box = assessment.Detection.boundingBox;
                Rect windowRect = default;
                float windowOpacity = 0f;
                bool windowVisible =
                    presentation != null
                    && presentation.TryGetPersonWindow(
                        assessment.TrackId,
                        out windowRect,
                        out windowOpacity);
                LogRecord record = NewRecord(
                    "assessment",
                    frame.TimestampSeconds);
                record.latestRiskSnapshotSequence = LatestSnapshotSequence();
                record.confirmedPersonCount = frame.ConfirmedPersonCount;
                record.trackId = assessment.TrackId;
                record.label = assessment.Detection.label;
                record.confidence = assessment.Detection.confidence;
                record.centerX = box.centerX;
                record.centerY = box.centerY;
                record.width = box.width;
                record.height = box.height;
                record.screenZone = assessment.Location.ScreenZone.ToString();
                record.userRelativeDirection = assessment.Location.UserRelativeDirection;
                record.distanceBand = assessment.Location.DistanceBand.ToString();
                record.distanceSource = assessment.Location.DistanceSource.ToString();
                record.distanceAvailable = assessment.Location.HasMetricDistance;
                record.rawDistanceMeters = assessment.Location.RawDistanceMeters;
                record.filteredDistanceMeters = assessment.Location.FilteredDistanceMeters;
                record.distanceConfidence = assessment.Location.DistanceConfidence;
                record.boundingBoxArea = assessment.Location.BoundingBoxArea;
                record.motionState = assessment.Motion.State.ToString();
                record.scaleRatePerSecond = assessment.Motion.ScaleRatePerSecond;
                record.closingSpeedMetersPerSecond = assessment.Motion.ClosingSpeedMetersPerSecond;
                record.ttcSecondsApprox = assessment.Motion.TtcSecondsApprox.GetValueOrDefault();
                record.hasTtc = assessment.Motion.TtcSecondsApprox.HasValue;
                record.metricTtcSeconds = assessment.Motion.MetricTtcSeconds.GetValueOrDefault();
                record.hasMetricTtc = assessment.Motion.MetricTtcSeconds.HasValue;
                record.collisionPath = assessment.Breakdown.CollisionPath;
                record.dynamicRisk = assessment.Score;
                record.riskLevel = assessment.Level.ToString();
                record.reasons = string.Join(",", assessment.Reasons);
                record.observedThisFrame = assessment.ObservedThisFrame;
                record.windowVisible = windowVisible;
                record.windowX = windowVisible ? windowRect.x : 0f;
                record.windowY = windowVisible ? windowRect.y : 0f;
                record.windowWidth = windowVisible ? windowRect.width : 0f;
                record.windowHeight = windowVisible ? windowRect.height : 0f;
                record.windowOpacity = windowVisible ? windowOpacity : 0f;
                record.depthSampleDispersionMeters =
                    assessment.Location.DepthSampleDispersionMeters;
                record.bboxDepthConflict = assessment.Motion.MetricConflict;
                record.missingSeconds = assessment.MissingSeconds;
                record.idHandoff = assessment.IdHandoff;
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

        private LogRecord NewRecord(string recordType, double timestampSeconds)
        {
            var record = new LogRecord
            {
                recordType = recordType,
                utc = DateTime.UtcNow.ToString("O"),
                timestampSeconds = timestampSeconds
            };
            if (trackingQuality != null)
            {
                TrackingDiagnosticsSnapshot snapshot =
                    trackingQuality.GetSnapshot();
                record.trackingProfile = snapshot.Profile.ToString();
                record.adaptiveLevel = snapshot.AdaptiveLevel;
                record.spatialSource = snapshot.SpatialSource.ToString();
                record.spatialDistanceMeters = snapshot.SpatialDistanceMeters;
                record.spatialConfidence = snapshot.SpatialConfidence;
                record.spatialRateHz = snapshot.SpatialRateHz;
                record.spatialMilliseconds = snapshot.SpatialMilliseconds;
                record.inferenceRateHz = snapshot.InferenceRateHz;
                record.inferenceMilliseconds = snapshot.InferenceMilliseconds;
                record.framesPerSecond = snapshot.FramesPerSecond;
                record.frameP95Milliseconds = snapshot.FrameP95Milliseconds;
            }
#if ADAPTIVE_PASSTHROUGH_QUEST_CAMERA
            if (spatialProvider != null)
            {
                SpatialObstacleMeasurement spatial =
                    spatialProvider.LatestMeasurement;
                record.spatialSampleCount = spatial.SampleCount;
                record.spatialDispersionMeters =
                    spatial.SampleDispersionMeters;
                record.spatialAgeSeconds = spatial.AgeSeconds;
            }
#endif
            return record;
        }

        private void OnTestMarker(string marker, double timestampSeconds)
        {
            if (!enableLogging || writer == null)
            {
                return;
            }

            LogRecord record = NewRecord("marker", timestampSeconds);
            record.marker = marker;
            WriteRecord(record);
            writer.Flush();
            pendingRecords = 0;
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
                FindObjectsByType<MonoBehaviour>();
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

        private void ResolvePresentation()
        {
            presentation =
                presentationBehaviour as IPersonWindowSnapshotProvider;
            if (presentation != null)
            {
                return;
            }

            MonoBehaviour[] behaviours =
                FindObjectsByType<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i]
                    is IPersonWindowSnapshotProvider provider)
                {
                    presentationBehaviour = behaviours[i];
                    presentation = provider;
                    return;
                }
            }
        }

        private void ResolveQualityReferences()
        {
            if (trackingQuality == null)
            {
                trackingQuality =
                    FindAnyObjectByType<TrackingQualityController>();
            }
#if ADAPTIVE_PASSTHROUGH_QUEST_CAMERA
            if (spatialProvider == null)
            {
                spatialProvider =
                    FindAnyObjectByType<QuestSpatialObstacleProvider>();
            }
#endif
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
