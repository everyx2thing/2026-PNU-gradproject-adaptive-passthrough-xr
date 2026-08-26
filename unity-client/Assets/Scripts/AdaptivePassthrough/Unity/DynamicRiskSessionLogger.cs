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
            public long frameSequence;
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
            public float targetInferenceRateHz;
            public float measuredInferenceRateHz;
            public bool hasInferenceMeasurement;
            public int liveTrackCount;
            public string liveTrackIds;
            public bool tentativeTrack;
            public bool revealEligible;
            public bool closePassthroughActive;
            public string closeTransitionReason;
            public int closeReleaseConfirmationCount;
            public bool hasWorldVelocity;
            public float worldVelocityX;
            public float worldVelocityY;
            public float worldVelocityZ;
            public float worldPredictionAgeSeconds;
            public float inferenceMilliseconds;
            public float framesPerSecond;
            public float frameP95Milliseconds;
            public float depthSampleDispersionMeters;
            public bool bboxDepthConflict;
            public bool metricDepthReliable;
            public string depthRejectedReason;
            public bool idHandoff;
            public float missingSeconds;
            public string marker;
            public bool spatialAvailable;
            public bool spatialRawOverlap;
            public bool spatialConfirmedOverlap;
            public int spatialValidRayHitCount;
            public string inferenceBackend;
            public bool inferenceActive;
            public float inferenceActiveMilliseconds;
            public float inferenceWallMilliseconds;
            public float captureAgeMilliseconds;
            public int inferenceScheduledLayerCount;
            public bool staticWindowVisible;
            public bool dynamicWindowVisible;
            public string visibilitySource;
            public float holdRemainingSeconds;
            public bool applicationPaused;
            public string scenarioId;
            public string scenario;
            public string markerPhase;
            public float groundTruthDistanceMeters = -1f;
            public float headDepthDistanceMeters = -1f;
            public float headRoomDistanceMeters = -1f;
            public string headSelectedSource;
            public bool headOverlap;
            public int headRayHitCount;
            public bool headSelfRejected;
            public string headSelfRejectionReason;
            public float headSpatialConfidence;
            public float leftDepthDistanceMeters = -1f;
            public float leftRoomDistanceMeters = -1f;
            public string leftSelectedSource;
            public bool leftOverlap;
            public int leftRayHitCount;
            public bool leftSelfRejected;
            public string leftSelfRejectionReason;
            public float leftSpatialConfidence;
            public float rightDepthDistanceMeters = -1f;
            public float rightRoomDistanceMeters = -1f;
            public string rightSelectedSource;
            public bool rightOverlap;
            public int rightRayHitCount;
            public bool rightSelfRejected;
            public string rightSelfRejectionReason;
            public float rightSpatialConfidence;
            public bool cameraReady;
            public int rawCandidateCount;
            public int personCandidateCount;
            public int confidenceRejectedCount;
            public int boxRejectedCount;
            public int classRejectedCount;
            public string inferenceWatchdogState;
            public float inferenceSliceBudgetMilliseconds;
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
        private IPassthroughPresentationSnapshotProvider
            presentationSnapshotProvider;
        private IPassthroughVisibilityEventSource visibilityEventSource;

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
                trackingQuality.ScenarioMarkerRequested += OnScenarioMarker;
            }

            if (visibilityEventSource != null)
            {
                visibilityEventSource.VisibilityChanged +=
                    OnVisibilityChanged;
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
                trackingQuality.ScenarioMarkerRequested -= OnScenarioMarker;
            }

            if (visibilityEventSource != null)
            {
                visibilityEventSource.VisibilityChanged -=
                    OnVisibilityChanged;
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
            frameRecord.frameSequence = controller == null
                ? 0L
                : controller.LatestFrameSequence;
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
                record.frameSequence = controller == null
                    ? 0L
                    : controller.LatestFrameSequence;
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
                record.liveTrackCount = frame.LiveTrackIds.Count;
                record.liveTrackIds = string.Join(",", frame.LiveTrackIds);
                record.tentativeTrack = assessment.Lifecycle
                    == TrackLifecycle.Tentative;
                record.revealEligible = assessment.ForcePassthrough
                    || assessment.Score >= 0.50f;
                record.closePassthroughActive =
                    assessment.ClosePassthroughActive;
                record.closeTransitionReason =
                    assessment.CloseTransitionReason;
                record.closeReleaseConfirmationCount =
                    assessment.CloseReleaseConfirmationCount;
                record.hasWorldVelocity =
                    assessment.Location.HasWorldVelocity;
                record.worldVelocityX = assessment.Location.WorldVelocity.x;
                record.worldVelocityY = assessment.Location.WorldVelocity.y;
                record.worldVelocityZ = assessment.Location.WorldVelocity.z;
                record.worldPredictionAgeSeconds =
                    assessment.Location.HasWorldVelocity && controller != null
                        ? Mathf.Min(
                            0.50f,
                            (float)Math.Max(
                                0.0,
                                Time.realtimeSinceStartupAsDouble
                                    - controller
                                        .LatestFrameCaptureRealtimeSeconds))
                        : 0f;
                record.windowVisible = windowVisible;
                record.windowX = windowVisible ? windowRect.x : 0f;
                record.windowY = windowVisible ? windowRect.y : 0f;
                record.windowWidth = windowVisible ? windowRect.width : 0f;
                record.windowHeight = windowVisible ? windowRect.height : 0f;
                record.windowOpacity = windowVisible ? windowOpacity : 0f;
                record.depthSampleDispersionMeters =
                    assessment.Location.DepthSampleDispersionMeters;
                record.bboxDepthConflict = assessment.Motion.MetricConflict;
                record.metricDepthReliable =
                    assessment.Location.IsMetricReliable;
                record.depthRejectedReason =
                    assessment.Location.DepthRejectedReason;
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
                timestampSeconds = timestampSeconds,
                frameSequence = controller == null
                    ? 0L
                    : controller.LatestFrameSequence
            };
            if (controller != null && controller.LatestFrame != null)
            {
                record.liveTrackCount =
                    controller.LatestFrame.LiveTrackIds.Count;
                record.liveTrackIds = string.Join(
                    ",",
                    controller.LatestFrame.LiveTrackIds);
            }
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
                record.targetInferenceRateHz =
                    snapshot.TargetInferenceRateHz;
                record.measuredInferenceRateHz =
                    snapshot.MeasuredInferenceRateHz;
                record.hasInferenceMeasurement =
                    snapshot.HasInferenceMeasurement;
                record.inferenceMilliseconds = snapshot.InferenceMilliseconds;
                record.inferenceActiveMilliseconds =
                    snapshot.InferenceActiveMilliseconds;
                record.inferenceWallMilliseconds =
                    snapshot.InferenceWallMilliseconds;
                record.captureAgeMilliseconds =
                    snapshot.CaptureAgeMilliseconds;
                record.inferenceScheduledLayerCount =
                    snapshot.ScheduledLayerCount;
                record.inferenceBackend = snapshot.InferenceBackend;
                record.inferenceActive = snapshot.InferenceActive;
                record.applicationPaused = snapshot.ApplicationPaused;
                record.framesPerSecond = snapshot.FramesPerSecond;
                record.frameP95Milliseconds = snapshot.FrameP95Milliseconds;
                ApplySpatialOwner(record, snapshot.HeadSpatial, 0);
                ApplySpatialOwner(record, snapshot.LeftHandSpatial, 1);
                ApplySpatialOwner(record, snapshot.RightHandSpatial, 2);
                record.cameraReady = snapshot.CameraReady;
                record.rawCandidateCount = snapshot.RawCandidateCount;
                record.personCandidateCount = snapshot.PersonCandidateCount;
                record.confidenceRejectedCount =
                    snapshot.ConfidenceRejectedCount;
                record.boxRejectedCount = snapshot.BoxRejectedCount;
                record.classRejectedCount = snapshot.ClassRejectedCount;
                record.inferenceWatchdogState =
                    snapshot.InferenceWatchdogState;
                record.inferenceSliceBudgetMilliseconds =
                    snapshot.InferenceSliceBudgetMilliseconds;
            }
#if ADAPTIVE_PASSTHROUGH_QUEST_CAMERA
            if (spatialProvider != null)
            {
                SpatialObstacleMeasurement spatial =
                    spatialProvider.LatestMeasurement;
                record.spatialSampleCount = spatial.SampleCount;
                record.spatialAvailable = spatial.Available;
                record.spatialRawOverlap = spatial.RawSafetyVolumeOverlap;
                record.spatialConfirmedOverlap = spatial.SafetyVolumeOverlap;
                record.spatialValidRayHitCount = spatial.ValidRayHitCount;
                record.spatialDispersionMeters =
                    spatial.SampleDispersionMeters;
                record.spatialAgeSeconds = spatial.AgeSeconds;
            }
#endif
            if (presentationSnapshotProvider != null)
            {
                PassthroughPresentationSnapshot presentationSnapshot =
                    presentationSnapshotProvider.GetPresentationSnapshot();
                record.staticWindowVisible =
                    presentationSnapshot.StaticVisible;
                record.dynamicWindowVisible =
                    presentationSnapshot.DynamicVisible;
                record.visibilitySource =
                    presentationSnapshot.VisibilitySource;
                record.holdRemainingSeconds =
                    presentationSnapshot.HoldRemainingSeconds;
            }
            return record;
        }

        private static void ApplySpatialOwner(
            LogRecord record,
            SpatialOwnerDiagnostics value,
            int slot)
        {
            if (slot == 0)
            {
                record.headDepthDistanceMeters =
                    value.EnvironmentDistanceMeters;
                record.headRoomDistanceMeters = value.RoomSceneDistanceMeters;
                record.headSelectedSource = value.SelectedSource.ToString();
                record.headOverlap = value.ConfirmedOverlap;
                record.headRayHitCount = value.ValidRayHitCount;
                record.headSelfRejected = value.SelfRejected;
                record.headSelfRejectionReason = value.RejectionReason;
                record.headSpatialConfidence = value.Confidence;
                return;
            }

            if (slot == 1)
            {
                record.leftDepthDistanceMeters =
                    value.EnvironmentDistanceMeters;
                record.leftRoomDistanceMeters = value.RoomSceneDistanceMeters;
                record.leftSelectedSource = value.SelectedSource.ToString();
                record.leftOverlap = value.ConfirmedOverlap;
                record.leftRayHitCount = value.ValidRayHitCount;
                record.leftSelfRejected = value.SelfRejected;
                record.leftSelfRejectionReason = value.RejectionReason;
                record.leftSpatialConfidence = value.Confidence;
                return;
            }

            record.rightDepthDistanceMeters =
                value.EnvironmentDistanceMeters;
            record.rightRoomDistanceMeters = value.RoomSceneDistanceMeters;
            record.rightSelectedSource = value.SelectedSource.ToString();
            record.rightOverlap = value.ConfirmedOverlap;
            record.rightRayHitCount = value.ValidRayHitCount;
            record.rightSelfRejected = value.SelfRejected;
            record.rightSelfRejectionReason = value.RejectionReason;
            record.rightSpatialConfidence = value.Confidence;
        }

        private void OnScenarioMarker(TrackingTestMarker marker)
        {
            if (!enableLogging || writer == null)
            {
                return;
            }

            LogRecord record = NewRecord(
                "distance_marker",
                marker.TimestampSeconds);
            record.scenarioId = marker.ScenarioId;
            record.scenario = marker.Scenario;
            record.markerPhase = marker.Phase;
            record.groundTruthDistanceMeters =
                marker.GroundTruthDistanceMeters;
            WriteRecord(record);
            writer.Flush();
            pendingRecords = 0;
        }

        private void OnVisibilityChanged(
            bool visible,
            string source,
            double timestampSeconds)
        {
            if (!enableLogging || writer == null)
            {
                return;
            }

            LogRecord record = NewRecord(
                "visibility",
                timestampSeconds);
            record.windowVisible = visible;
            record.visibilitySource = source ?? "none";
            WriteRecord(record);
            writer.Flush();
            pendingRecords = 0;
        }

        private void OnApplicationPause(bool paused)
        {
            if (!enableLogging || writer == null)
            {
                return;
            }

            LogRecord record = NewRecord(
                paused ? "pause" : "resume",
                Time.realtimeSinceStartupAsDouble);
            record.applicationPaused = paused;
            WriteRecord(record);
            writer.Flush();
            pendingRecords = 0;
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
            presentationSnapshotProvider =
                presentationBehaviour
                    as IPassthroughPresentationSnapshotProvider;
            visibilityEventSource = presentationBehaviour
                as IPassthroughVisibilityEventSource;
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
                    presentationSnapshotProvider = behaviours[i]
                        as IPassthroughPresentationSnapshotProvider;
                    visibilityEventSource = behaviours[i]
                        as IPassthroughVisibilityEventSource;
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
                    string.Format(
                        "{0}-{1:yyyyMMdd-HHmmss-fff}-{2}.jsonl",
                        filePrefix,
                        DateTime.Now,
                        Guid.NewGuid().ToString("N").Substring(0, 8)));
                writer = new StreamWriter(
                    new FileStream(
                        CurrentLogPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.Read),
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
