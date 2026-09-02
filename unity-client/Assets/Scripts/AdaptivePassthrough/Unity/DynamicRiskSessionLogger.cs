using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    [DefaultExecutionOrder(700)]
    [DisallowMultipleComponent]
    public sealed class DynamicRiskSessionLogger : MonoBehaviour
    {
        [Serializable]
        private sealed class PersonDepthSampleLog
        {
            public int index;
            public float boxX;
            public float boxY;
            public float viewportX;
            public float viewportY;
            public bool hit;
            public float distanceMeters;
            public bool hasWorldPoint;
            public float worldX;
            public float worldY;
            public float worldZ;
            public bool torsoCore;
            public bool upperBody;
            public string decision;
        }

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
            public float trackingDistanceMeters;
            public float distanceConfidence;
            public float rawSafetyDistanceMeters;
            public float safetyDistanceMeters;
            public float safetyDistanceConfidence;
            public int torsoSupportCount;
            public int safetySupportCount;
            public string clusterSelectionReason;
            public int trackingClusterId;
            public float trackingClusterConfidence;
            public int trackingClusterSupportCount;
            public float trackingClusterSpanMeters;
            public string trackingClusterSelectionMask;
            public float trackingClusterTemporalConsistency;
            public int safetyClusterId;
            public float safetyClusterConfidence;
            public int safetyClusterSupportCount;
            public float safetyClusterSpanMeters;
            public string safetyClusterSelectionMask;
            public bool expandedDepthPattern;
            public int requestedDepthSampleCount;
            public int hitDepthSampleCount;
            public bool hasTrackingWorldCenter;
            public float trackingWorldCenterX;
            public float trackingWorldCenterY;
            public float trackingWorldCenterZ;
            public PersonDepthSampleLog[] personDepthSamples;
            public float boundingBoxArea;
            public string motionState;
            public float scaleRatePerSecond;
            public float closingSpeedMetersPerSecond;
            public float ttcSecondsApprox;
            public bool hasTtc;
            public float metricTtcSeconds;
            public bool hasMetricTtc;
            public float collisionPath;
            public string riskDistanceSource;
            public float riskDistanceMeters;
            public float riskClusterConfidence;
            public float closeRiskFloor;
            public float motionAttenuation;
            public float dynamicRisk;
            public float dynamicPolicyRisk;
            public bool forcePassthrough;
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
            public bool presentationGeometryAvailable;
            public string presentationGeometrySource;
            public float presentationDistanceMeters;
            public string personPresentationStatus;
            public int qualifiedPersonCount;
            public int geometryReadyPersonCount;
            public int renderedPersonWindowCount;
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
            public bool dynamicPassthroughRendered;
            public bool dynamicFallbackActive;
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
            public string staticEnabledChannels;
            public int staticWindowCount;
            public string staticHazardKeys;
            public string staticReplacementReason;
            public bool staticDiagnosticsAvailable;
            public float headStaticRisk;
            public bool headStaticAvailable;
            public bool headChannelEnabled;
            public bool headPolicyContributing;
            public float headStaticDistanceMeters = -1f;
            public float headClosingSpeedMetersPerSecond;
            public float headDistanceRisk;
            public float headSpeedRisk;
            public float headTtcRisk;
            public float leftHandStaticRisk;
            public bool leftHandStaticAvailable;
            public bool leftHandChannelEnabled;
            public bool leftHandPolicyContributing;
            public float leftHandStaticDistanceMeters = -1f;
            public float leftHandClosingSpeedMetersPerSecond;
            public float leftHandDistanceRisk;
            public float leftHandSpeedRisk;
            public float leftHandTtcRisk;
            public float rightHandStaticRisk;
            public bool rightHandStaticAvailable;
            public bool rightHandChannelEnabled;
            public bool rightHandPolicyContributing;
            public float rightHandStaticDistanceMeters = -1f;
            public float rightHandClosingSpeedMetersPerSecond;
            public float rightHandDistanceRisk;
            public float rightHandSpeedRisk;
            public float rightHandTtcRisk;
            public float lowObstacleStaticRisk;
            public bool lowObstacleStaticAvailable;
            public bool lowObstacleChannelEnabled;
            public bool lowObstaclePolicyContributing;
            public float lowObstacleStaticDistanceMeters = -1f;
            public float lowObstacleClosingSpeedMetersPerSecond;
            public float lowObstacleDistanceRisk;
            public float lowObstacleSpeedRisk;
            public float lowObstacleTtcRisk;
            public int headSurfaceId;
            public float headPresentationNormalChangeDegrees;
            public int leftSurfaceId;
            public float leftPresentationNormalChangeDegrees;
            public int rightSurfaceId;
            public float rightPresentationNormalChangeDegrees;
            public int lowObstacleSurfaceId;
            public string lowObstacleSelectedSource;
            public float lowObstaclePresentationNormalChangeDegrees;
            public long staticPresentationStableId;
            public string staticPresentationSource;
            public float staticPresentationNormalX;
            public float staticPresentationNormalY;
            public float staticPresentationNormalZ;
            public bool primaryPolicyGeometryAvailable;
            public long primaryPolicyGeometryStableId;
            public string primaryPolicyGeometrySource;
            public float primaryPolicyGeometryNormalX;
            public float primaryPolicyGeometryNormalY;
            public float primaryPolicyGeometryNormalZ;
        }

        private sealed class PendingFrameRecord
        {
            public LogRecord Record;
            public DynamicRiskAssessment Assessment;
        }

        [SerializeField] private DynamicRiskController controller;
        [SerializeField] private MonoBehaviour presentationBehaviour;
        [SerializeField] private MonoBehaviour snapshotSequenceProviderBehaviour;
        [SerializeField] private TrackingQualityController trackingQuality;
        [SerializeField] private MonoBehaviour staticPolicyBehaviour;
#if ADAPTIVE_PASSTHROUGH_QUEST_CAMERA
        [SerializeField] private QuestSpatialObstacleProvider spatialProvider;
        [SerializeField] private QuestPersonDepthProvider personDepthProvider;
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
        private IStaticRiskDiagnosticsProvider staticPolicy;
        private IDynamicPresentationModeProvider selectivePresentation;
        private IPersonPresentationFrameProvider presentationFrameProvider;
        private readonly List<PendingFrameRecord> pendingFrameRecords =
            new List<PendingFrameRecord>();
        private bool hasDynamicPresentationMode;
        private bool lastDynamicPassthroughRendered;
        private bool lastDynamicFallbackActive;

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
            hasDynamicPresentationMode = false;
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
            FlushPendingFrameRecords();
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
            frameRecord.dynamicPolicyRisk = frame.PolicyRisk;
            frameRecord.forcePassthrough = frame.ForcePassthrough;
            frameRecord.riskLevel = frame.MaximumLevel.ToString();
            pendingFrameRecords.Add(new PendingFrameRecord
            {
                Record = frameRecord
            });

            for (int i = 0; i < frame.Assessments.Count; i++)
            {
                DynamicRiskAssessment assessment = frame.Assessments[i];
                NormalizedBoundingBox box = assessment.Detection.boundingBox;
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
                record.trackingDistanceMeters =
                    assessment.Location.FilteredDistanceMeters;
                record.distanceConfidence = assessment.Location.DistanceConfidence;
                record.rawSafetyDistanceMeters =
                    assessment.Location.RawSafetyDistanceMeters;
                record.safetyDistanceMeters =
                    assessment.Location.SafetyDistanceMeters;
                record.safetyDistanceConfidence =
                    assessment.Location.SafetyDistanceConfidence;
                record.torsoSupportCount =
                    assessment.Location.TorsoSupportCount;
                record.safetySupportCount =
                    assessment.Location.SafetySupportCount;
                record.clusterSelectionReason =
                    assessment.Location.ClusterSelectionReason;
                PersonDepthClusterMeasurement trackingCluster =
                    assessment.Location.TrackingCluster;
                PersonDepthClusterMeasurement safetyCluster =
                    assessment.Location.SafetyCluster;
                record.trackingClusterId = trackingCluster.ClusterId;
                record.trackingClusterConfidence =
                    trackingCluster.Confidence;
                record.trackingClusterSupportCount =
                    trackingCluster.SupportCount;
                record.trackingClusterSpanMeters = trackingCluster.SpanMeters;
                record.trackingClusterSelectionMask =
                    trackingCluster.SelectionMask.ToString("X16");
                record.trackingClusterTemporalConsistency =
                    trackingCluster.TemporalConsistency;
                record.safetyClusterId = safetyCluster.ClusterId;
                record.safetyClusterConfidence = safetyCluster.Confidence;
                record.safetyClusterSupportCount =
                    safetyCluster.SupportCount;
                record.safetyClusterSpanMeters = safetyCluster.SpanMeters;
                record.safetyClusterSelectionMask =
                    safetyCluster.SelectionMask.ToString("X16");
#if ADAPTIVE_PASSTHROUGH_QUEST_CAMERA
                ApplyPersonDepthSamples(record, assessment.TrackId);
#endif
                record.boundingBoxArea = assessment.Location.BoundingBoxArea;
                record.motionState = assessment.Motion.State.ToString();
                record.scaleRatePerSecond = assessment.Motion.ScaleRatePerSecond;
                record.closingSpeedMetersPerSecond = assessment.Motion.ClosingSpeedMetersPerSecond;
                record.ttcSecondsApprox = assessment.Motion.TtcSecondsApprox.GetValueOrDefault();
                record.hasTtc = assessment.Motion.TtcSecondsApprox.HasValue;
                record.metricTtcSeconds = assessment.Motion.MetricTtcSeconds.GetValueOrDefault();
                record.hasMetricTtc = assessment.Motion.MetricTtcSeconds.HasValue;
                record.collisionPath = assessment.Breakdown.CollisionPath;
                record.riskDistanceSource = assessment.Breakdown
                    .RiskDistanceSource.ToString();
                record.riskDistanceMeters = assessment.Breakdown
                    .RiskDistanceMeters;
                record.riskClusterConfidence = assessment.Breakdown
                    .ClusterConfidence;
                record.closeRiskFloor = assessment.Breakdown.CloseRiskFloor;
                record.motionAttenuation = assessment.Breakdown
                    .MotionAttenuation;
                record.dynamicRisk = assessment.Score;
                record.dynamicPolicyRisk = assessment.ForcePassthrough
                    ? 1f
                    : assessment.Score;
                record.forcePassthrough = assessment.ForcePassthrough;
                record.riskLevel = assessment.Level.ToString();
                record.reasons = string.Join(",", assessment.Reasons);
                record.observedThisFrame = assessment.ObservedThisFrame;
                record.liveTrackCount = frame.LiveTrackIds.Count;
                record.liveTrackIds = string.Join(",", frame.LiveTrackIds);
                record.tentativeTrack = assessment.Lifecycle
                    == TrackLifecycle.Tentative;
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
                record.depthSampleDispersionMeters =
                    assessment.Location.DepthSampleDispersionMeters;
                record.bboxDepthConflict = assessment.Motion.MetricConflict;
                record.metricDepthReliable =
                    assessment.Location.IsMetricReliable;
                record.depthRejectedReason =
                    assessment.Location.DepthRejectedReason;
                record.presentationGeometryAvailable =
                    assessment.Location.HasPresentationGeometry;
                record.presentationGeometrySource = assessment.Location
                    .PresentationGeometrySource.ToString();
                record.presentationDistanceMeters = assessment.Location
                    .PresentationDistanceMeters;
                record.missingSeconds = assessment.MissingSeconds;
                record.idHandoff = assessment.IdHandoff;
                pendingFrameRecords.Add(new PendingFrameRecord
                {
                    Record = record,
                    Assessment = assessment
                });
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
                SpatialObstacleMeasurement low =
                    spatialProvider.LatestLocomotionMeasurement;
                record.lowObstacleSurfaceId = low.SurfaceId;
                record.lowObstacleSelectedSource =
                    low.SelectedSource.ToString();
                record.lowObstaclePresentationNormalChangeDegrees =
                    low.PresentationNormalChangeDegrees;
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
            if (selectivePresentation != null)
            {
                record.dynamicPassthroughRendered =
                    selectivePresentation.DynamicPassthroughRendered;
                record.dynamicFallbackActive =
                    selectivePresentation.DynamicFallbackActive;
            }
            ApplyStaticDiagnostics(record);
            return record;
        }

        private void LateUpdate()
        {
            if (!enableLogging || writer == null)
            {
                return;
            }

            FlushPendingFrameRecords();
            if (selectivePresentation == null)
            {
                return;
            }

            bool passthroughRendered =
                selectivePresentation.DynamicPassthroughRendered;
            bool fallbackActive =
                selectivePresentation.DynamicFallbackActive;
            if (hasDynamicPresentationMode
                && passthroughRendered == lastDynamicPassthroughRendered
                && fallbackActive == lastDynamicFallbackActive)
            {
                return;
            }

            hasDynamicPresentationMode = true;
            lastDynamicPassthroughRendered = passthroughRendered;
            lastDynamicFallbackActive = fallbackActive;
            LogRecord record = NewRecord(
                "dynamic_presentation",
                Time.realtimeSinceStartupAsDouble);
            record.dynamicPassthroughRendered = passthroughRendered;
            record.dynamicFallbackActive = fallbackActive;
            WriteRecord(record);
            writer.Flush();
            pendingRecords = 0;
        }

        private void FlushPendingFrameRecords()
        {
            if (writer == null || pendingFrameRecords.Count == 0)
            {
                return;
            }

            for (int i = 0; i < pendingFrameRecords.Count; i++)
            {
                PendingFrameRecord pending = pendingFrameRecords[i];
                RefreshPresentationFields(
                    pending.Record,
                    pending.Assessment);
                WriteRecord(pending.Record);
            }
            pendingFrameRecords.Clear();

            if (pendingRecords >= flushEveryRecords)
            {
                writer.Flush();
                pendingRecords = 0;
            }
        }

        private void RefreshPresentationFields(
            LogRecord record,
            DynamicRiskAssessment assessment)
        {
            if (record == null)
            {
                return;
            }

            if (presentationSnapshotProvider != null)
            {
                PassthroughPresentationSnapshot snapshot =
                    presentationSnapshotProvider.GetPresentationSnapshot();
                record.staticWindowVisible = snapshot.StaticVisible;
                record.dynamicWindowVisible = snapshot.DynamicVisible;
                record.visibilitySource = snapshot.VisibilitySource;
                record.holdRemainingSeconds = snapshot.HoldRemainingSeconds;
            }
            if (selectivePresentation != null)
            {
                record.dynamicPassthroughRendered =
                    selectivePresentation.DynamicPassthroughRendered;
                record.dynamicFallbackActive =
                    selectivePresentation.DynamicFallbackActive;
            }
            if (assessment == null)
            {
                return;
            }

            record.revealEligible = presentationFrameProvider != null
                ? presentationFrameProvider.IsPersonRevealEligible(assessment)
                : assessment.ForcePassthrough;
            Rect windowRect = default;
            float windowOpacity = 0f;
            bool windowVisible = presentationFrameProvider != null
                && presentationFrameProvider.TryGetRenderedPersonWindow(
                    assessment.TrackId,
                    out windowRect,
                    out windowOpacity);
            record.windowVisible = windowVisible;
            record.windowX = windowVisible ? windowRect.x : 0f;
            record.windowY = windowVisible ? windowRect.y : 0f;
            record.windowWidth = windowVisible ? windowRect.width : 0f;
            record.windowHeight = windowVisible ? windowRect.height : 0f;
            record.windowOpacity = windowVisible ? windowOpacity : 0f;
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
            IPersonPresentationDiagnosticsProvider diagnostics =
                presentationBehaviour
                    as IPersonPresentationDiagnosticsProvider;
            if (diagnostics != null)
            {
                record.personPresentationStatus = diagnostics
                    .LatestPersonPresentationStatus.ToString();
                record.qualifiedPersonCount = diagnostics
                    .LatestQualifiedPersonCount;
                record.geometryReadyPersonCount = diagnostics
                    .LatestGeometryReadyPersonCount;
                record.renderedPersonWindowCount = diagnostics
                    .ActivePersonWindowCount;
            }
        }

#if ADAPTIVE_PASSTHROUGH_QUEST_CAMERA
        private void ApplyPersonDepthSamples(LogRecord record, int trackId)
        {
            if (record == null || personDepthProvider == null)
            {
                return;
            }

            if (!personDepthProvider.TryGetSamplingSnapshot(
                    trackId,
                    out PersonDepthSamplingSnapshot snapshot)
                || snapshot == null)
            {
                return;
            }

            record.expandedDepthPattern = snapshot.ExpandedPattern;
            record.requestedDepthSampleCount =
                snapshot.RequestedSampleCount;
            record.hitDepthSampleCount = snapshot.HitSampleCount;
            record.hasTrackingWorldCenter =
                snapshot.HasTrackingWorldCenter;
            record.trackingWorldCenterX = snapshot.TrackingWorldCenter.x;
            record.trackingWorldCenterY = snapshot.TrackingWorldCenter.y;
            record.trackingWorldCenterZ = snapshot.TrackingWorldCenter.z;
            var samples = new PersonDepthSampleLog[snapshot.Samples.Length];
            for (int i = 0; i < snapshot.Samples.Length; i++)
            {
                PersonDepthSampleDiagnostic source = snapshot.Samples[i];
                samples[i] = new PersonDepthSampleLog
                {
                    index = source.SampleIndex,
                    boxX = source.BoxRelativePosition.x,
                    boxY = source.BoxRelativePosition.y,
                    viewportX = source.CameraViewportPosition.x,
                    viewportY = source.CameraViewportPosition.y,
                    hit = source.HasHit,
                    distanceMeters = source.DistanceMeters,
                    hasWorldPoint = source.HasWorldPoint,
                    worldX = source.WorldPoint.x,
                    worldY = source.WorldPoint.y,
                    worldZ = source.WorldPoint.z,
                    torsoCore = source.IsTorsoCore,
                    upperBody = source.IsUpperBody,
                    decision = source.Decision.ToString()
                };
            }

            record.personDepthSamples = samples;
        }
#endif

        private void ApplyStaticDiagnostics(LogRecord record)
        {
            if (record == null)
            {
                return;
            }

            IStaticPresentationDiagnosticsProvider selective =
                presentationBehaviour as IStaticPresentationDiagnosticsProvider;
            if (selective != null)
            {
                record.staticEnabledChannels =
                    selective.EnabledStaticChannels.ToString();
                record.staticWindowCount = selective.ActiveStaticWindowCount;
                record.staticHazardKeys = selective.VisibleStaticHazardKeys;
                record.staticReplacementReason =
                    selective.LastStaticReplacementReason;
            }

            StaticBoundaryRiskFrame frame = staticPolicy == null
                ? null
                : staticPolicy.CurrentStaticBoundaryFrame;
            if (frame != null)
            {
                record.staticDiagnosticsAvailable = frame.Available;
                record.headStaticRisk = frame.Head.Risk;
                record.headStaticAvailable = frame.Head.Available;
                if (frame.Head.Available)
                {
                    record.headStaticDistanceMeters =
                        frame.Head.ClosestDistanceMeters;
                }
                record.headClosingSpeedMetersPerSecond =
                    frame.Head.TowardBoundarySpeed;
                record.headDistanceRisk = frame.Head.DistanceRisk;
                record.headSpeedRisk = frame.Head.SpeedRisk;
                record.headTtcRisk = frame.Head.TtcRisk;
                record.leftHandStaticRisk = frame.LeftHand.Risk;
                record.leftHandStaticAvailable = frame.LeftHand.Available;
                if (frame.LeftHand.Available)
                {
                    record.leftHandStaticDistanceMeters =
                        frame.LeftHand.DistanceMeters;
                }
                record.leftHandClosingSpeedMetersPerSecond =
                    frame.LeftHand.TowardBoundarySpeed;
                record.leftHandDistanceRisk = frame.LeftHand.DistanceRisk;
                record.leftHandSpeedRisk = frame.LeftHand.SpeedRisk;
                record.leftHandTtcRisk = frame.LeftHand.TtcRisk;
                record.rightHandStaticRisk = frame.RightHand.Risk;
                record.rightHandStaticAvailable = frame.RightHand.Available;
                if (frame.RightHand.Available)
                {
                    record.rightHandStaticDistanceMeters =
                        frame.RightHand.DistanceMeters;
                }
                record.rightHandClosingSpeedMetersPerSecond =
                    frame.RightHand.TowardBoundarySpeed;
                record.rightHandDistanceRisk = frame.RightHand.DistanceRisk;
                record.rightHandSpeedRisk = frame.RightHand.SpeedRisk;
                record.rightHandTtcRisk = frame.RightHand.TtcRisk;
                record.lowObstacleStaticRisk = frame.LowObstacle.Risk;
                record.lowObstacleStaticAvailable = frame.LowObstacle.Available;
                if (frame.LowObstacle.Available)
                {
                    record.lowObstacleStaticDistanceMeters =
                        frame.LowObstacle.ClosestDistanceMeters;
                }
                record.lowObstacleClosingSpeedMetersPerSecond =
                    frame.LowObstacle.TowardBoundarySpeed;
                record.lowObstacleDistanceRisk =
                    frame.LowObstacle.DistanceRisk;
                record.lowObstacleSpeedRisk = frame.LowObstacle.SpeedRisk;
                record.lowObstacleTtcRisk = frame.LowObstacle.TtcRisk;
            }

            StaticPassthroughDecision decision = staticPolicy == null
                ? null
                : staticPolicy.CurrentStaticDecision;
            ApplyHazardContribution(record, decision, StaticHazardKey.Head);
            ApplyHazardContribution(
                record,
                decision,
                StaticHazardKey.LeftHand);
            ApplyHazardContribution(
                record,
                decision,
                StaticHazardKey.RightHand);
            ApplyHazardContribution(
                record,
                decision,
                StaticHazardKey.LowObstacle);
            HazardPresentationGeometry geometry = decision == null
                ? default
                : decision.PresentationGeometry;
            if (geometry.Available)
            {
                record.staticPresentationStableId = geometry.StableId;
                record.staticPresentationSource = geometry.Source.ToString();
                record.staticPresentationNormalX = geometry.SurfaceNormal.x;
                record.staticPresentationNormalY = geometry.SurfaceNormal.y;
                record.staticPresentationNormalZ = geometry.SurfaceNormal.z;
                record.primaryPolicyGeometryAvailable = true;
                record.primaryPolicyGeometryStableId = geometry.StableId;
                record.primaryPolicyGeometrySource = geometry.Source.ToString();
                record.primaryPolicyGeometryNormalX = geometry.SurfaceNormal.x;
                record.primaryPolicyGeometryNormalY = geometry.SurfaceNormal.y;
                record.primaryPolicyGeometryNormalZ = geometry.SurfaceNormal.z;
            }
        }

        private static void ApplyHazardContribution(
            LogRecord record,
            StaticPassthroughDecision decision,
            StaticHazardKey key)
        {
            if (decision == null || decision.Hazards == null)
            {
                return;
            }

            for (int i = 0; i < decision.Hazards.Length; i++)
            {
                StaticHazardDecision hazard = decision.Hazards[i];
                if (hazard == null || hazard.Key != key)
                {
                    continue;
                }

                switch (key)
                {
                    case StaticHazardKey.Head:
                        record.headChannelEnabled = hazard.ChannelEnabled;
                        record.headPolicyContributing = hazard.Enabled;
                        break;
                    case StaticHazardKey.LeftHand:
                        record.leftHandChannelEnabled = hazard.ChannelEnabled;
                        record.leftHandPolicyContributing = hazard.Enabled;
                        break;
                    case StaticHazardKey.RightHand:
                        record.rightHandChannelEnabled = hazard.ChannelEnabled;
                        record.rightHandPolicyContributing = hazard.Enabled;
                        break;
                    case StaticHazardKey.LowObstacle:
                        record.lowObstacleChannelEnabled =
                            hazard.ChannelEnabled;
                        record.lowObstaclePolicyContributing = hazard.Enabled;
                        break;
                }
                return;
            }
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
                record.headSurfaceId = value.SurfaceId;
                record.headPresentationNormalChangeDegrees =
                    value.PresentationNormalChangeDegrees;
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
                record.leftSurfaceId = value.SurfaceId;
                record.leftPresentationNormalChangeDegrees =
                    value.PresentationNormalChangeDegrees;
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
            record.rightSurfaceId = value.SurfaceId;
            record.rightPresentationNormalChangeDegrees =
                value.PresentationNormalChangeDegrees;
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
            pendingFrameRecords.Add(new PendingFrameRecord
            {
                Record = record
            });
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

            FlushPendingFrameRecords();
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
            pendingFrameRecords.Add(new PendingFrameRecord
            {
                Record = record
            });
        }

        private void OnTestMarker(string marker, double timestampSeconds)
        {
            if (!enableLogging || writer == null)
            {
                return;
            }

            LogRecord record = NewRecord("marker", timestampSeconds);
            record.marker = marker;
            pendingFrameRecords.Add(new PendingFrameRecord
            {
                Record = record
            });
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
            selectivePresentation = presentationBehaviour
                as IDynamicPresentationModeProvider;
            presentationFrameProvider = presentationBehaviour
                as IPersonPresentationFrameProvider;
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
                    selectivePresentation = behaviours[i]
                        as IDynamicPresentationModeProvider;
                    presentationFrameProvider = behaviours[i]
                        as IPersonPresentationFrameProvider;
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
            if (staticPolicy == null)
            {
                staticPolicy = staticPolicyBehaviour
                    as IStaticRiskDiagnosticsProvider;
            }
            if (staticPolicy == null)
            {
                MonoBehaviour[] behaviours =
                    FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    IStaticRiskDiagnosticsProvider candidate = behaviours[i]
                        as IStaticRiskDiagnosticsProvider;
                    if (candidate == null)
                    {
                        continue;
                    }

                    staticPolicyBehaviour = behaviours[i];
                    staticPolicy = candidate;
                    break;
                }
            }
#if ADAPTIVE_PASSTHROUGH_QUEST_CAMERA
            if (spatialProvider == null)
            {
                spatialProvider =
                    FindAnyObjectByType<QuestSpatialObstacleProvider>();
            }
            if (personDepthProvider == null)
            {
                personDepthProvider =
                    FindAnyObjectByType<QuestPersonDepthProvider>();
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
