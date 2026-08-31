using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    public enum TrackingQualityProfile
    {
        Balanced = 0,
        Accuracy = 1,
        Performance = 2
    }

    public enum SpatialProbeOwner
    {
        Head,
        LeftHand,
        RightHand
    }

    public enum SpatialProbePurpose
    {
        Standard,
        LocomotionCorridor
    }

    public enum SpatialObstacleSource
    {
        Unavailable,
        EnvironmentDepth,
        RoomScene,
        Fused
    }

    [Serializable]
    public struct TrackingQualitySettings
    {
        public TrackingQualityProfile profile;
        public float spatialRateHz;
        public int spatialRayCount;
        public float personInferenceRateHz;
        public float inferenceSliceMilliseconds;
        public int maximumLayersPerFrame;

        public TrackingQualitySettings(
            TrackingQualityProfile profile,
            float spatialRateHz,
            int spatialRayCount,
            float personInferenceRateHz)
            : this(
                profile,
                spatialRateHz,
                spatialRayCount,
                personInferenceRateHz,
                1.5f,
                64)
        {
        }

        public TrackingQualitySettings(
            TrackingQualityProfile profile,
            float spatialRateHz,
            int spatialRayCount,
            float personInferenceRateHz,
            float inferenceSliceMilliseconds,
            int maximumLayersPerFrame)
        {
            this.profile = profile;
            this.spatialRateHz = Mathf.Max(1f, spatialRateHz);
            this.spatialRayCount = Mathf.Max(1, spatialRayCount);
            this.personInferenceRateHz = Mathf.Max(1f, personInferenceRateHz);
            this.inferenceSliceMilliseconds = Mathf.Max(
                0.1f,
                inferenceSliceMilliseconds);
            this.maximumLayersPerFrame = Mathf.Max(
                1,
                maximumLayersPerFrame);
        }

        public static TrackingQualitySettings For(
            TrackingQualityProfile profile)
        {
            switch (profile)
            {
                case TrackingQualityProfile.Accuracy:
                    return new TrackingQualitySettings(
                        profile, 30f, 24, 3f, 2.5f, 96);
                case TrackingQualityProfile.Performance:
                    return new TrackingQualitySettings(
                        profile, 10f, 6, 3f, 0.75f, 48);
                default:
                    return new TrackingQualitySettings(
                        TrackingQualityProfile.Balanced,
                        20f,
                        12,
                        3f,
                        1.5f,
                        64);
            }
        }
    }

    public readonly struct SpatialProbe
    {
        public readonly SpatialProbeOwner Owner;
        public readonly Vector3 Origin;
        public readonly Vector3 Direction;
        public readonly Vector3 Velocity;
        public readonly float SafetyRadius;
        public readonly SpatialProbePurpose Purpose;

        public SpatialProbe(
            SpatialProbeOwner owner,
            Vector3 origin,
            Vector3 direction,
            Vector3 velocity,
            float safetyRadius)
            : this(
                owner,
                origin,
                direction,
                velocity,
                safetyRadius,
                SpatialProbePurpose.Standard)
        {
        }

        public SpatialProbe(
            SpatialProbeOwner owner,
            Vector3 origin,
            Vector3 direction,
            Vector3 velocity,
            float safetyRadius,
            SpatialProbePurpose purpose)
        {
            Owner = owner;
            Origin = origin;
            Direction = direction.sqrMagnitude > 0.000001f
                ? direction.normalized
                : Vector3.forward;
            Velocity = velocity;
            SafetyRadius = Mathf.Max(0f, safetyRadius);
            Purpose = purpose;
        }
    }

    public readonly struct SpatialObstacleMeasurement
    {
        public readonly SpatialObstacleSource Source;
        public readonly double TimestampSeconds;
        public readonly bool Available;
        public readonly float DistanceMeters;
        public readonly Vector3 HitPoint;
        public readonly Vector3 HitNormal;
        public readonly float ClosingSpeedMetersPerSecond;
        public readonly float TimeToCollisionSeconds;
        public readonly float Confidence;
        public readonly int SampleCount;
        public readonly float SampleDispersionMeters;
        public readonly float AgeSeconds;
        public readonly bool SafetyVolumeOverlap;
        public readonly bool RawSafetyVolumeOverlap;
        public readonly int ValidRayHitCount;
        public readonly SpatialProbeOwner Owner;
        public readonly SpatialObstacleSource SelectedSource;
        public readonly float EnvironmentDistanceMeters;
        public readonly float RoomSceneDistanceMeters;
        public readonly bool SelfRejected;
        public readonly string RejectionReason;
        public readonly SpatialProbePurpose ProbePurpose;
        public readonly int SurfaceId;
        public readonly HazardPresentationGeometry PresentationGeometry;
        public readonly float PresentationNormalChangeDegrees;
        public readonly HazardPresentationGeometry SurfaceBoundsGeometry;

        public SpatialObstacleMeasurement(
            SpatialObstacleSource source,
            double timestampSeconds,
            bool available,
            float distanceMeters,
            Vector3 hitPoint,
            Vector3 hitNormal,
            float closingSpeedMetersPerSecond,
            float confidence,
            int sampleCount,
            float sampleDispersionMeters,
            float ageSeconds,
            bool safetyVolumeOverlap,
            bool rawSafetyVolumeOverlap = false,
            int validRayHitCount = -1,
            SpatialProbeOwner owner = SpatialProbeOwner.Head,
            SpatialObstacleSource selectedSource =
                SpatialObstacleSource.Unavailable,
            float environmentDistanceMeters = -1f,
            float roomSceneDistanceMeters = -1f,
            bool selfRejected = false,
            string rejectionReason = null,
            SpatialProbePurpose probePurpose = SpatialProbePurpose.Standard,
            int surfaceId = -1,
            HazardPresentationGeometry presentationGeometry = default,
            float presentationNormalChangeDegrees = 0f,
            HazardPresentationGeometry surfaceBoundsGeometry = default)
        {
            Source = source;
            TimestampSeconds = Math.Max(0.0, timestampSeconds);
            Available = available;
            DistanceMeters = Mathf.Max(0f, distanceMeters);
            HitPoint = hitPoint;
            HitNormal = hitNormal;
            ClosingSpeedMetersPerSecond = Mathf.Max(
                0f,
                closingSpeedMetersPerSecond);
            TimeToCollisionSeconds = ClosingSpeedMetersPerSecond > 0.001f
                ? DistanceMeters / ClosingSpeedMetersPerSecond
                : float.PositiveInfinity;
            Confidence = Mathf.Clamp01(confidence);
            SampleCount = Mathf.Max(0, sampleCount);
            SampleDispersionMeters = Mathf.Max(0f, sampleDispersionMeters);
            AgeSeconds = Mathf.Max(0f, ageSeconds);
            SafetyVolumeOverlap = safetyVolumeOverlap;
            RawSafetyVolumeOverlap = rawSafetyVolumeOverlap;
            ValidRayHitCount = validRayHitCount < 0
                ? SampleCount
                : Mathf.Max(0, validRayHitCount);
            Owner = owner;
            SelectedSource = selectedSource == SpatialObstacleSource.Unavailable
                ? source
                : selectedSource;
            EnvironmentDistanceMeters = environmentDistanceMeters;
            RoomSceneDistanceMeters = roomSceneDistanceMeters;
            SelfRejected = selfRejected;
            RejectionReason = rejectionReason ?? string.Empty;
            ProbePurpose = probePurpose;
            SurfaceId = surfaceId;
            PresentationGeometry = available
                && presentationGeometry.Available
                ? presentationGeometry
                : default;
            PresentationNormalChangeDegrees = Mathf.Max(
                0f,
                presentationNormalChangeDegrees);
            SurfaceBoundsGeometry = available
                && surfaceBoundsGeometry.Available
                ? surfaceBoundsGeometry
                : default;
        }

        public static SpatialObstacleMeasurement Unavailable(
            double timestampSeconds)
        {
            return Unavailable(
                timestampSeconds,
                SpatialProbeOwner.Head,
                SpatialProbePurpose.Standard);
        }

        public static SpatialObstacleMeasurement Unavailable(
            double timestampSeconds,
            SpatialProbeOwner owner,
            SpatialProbePurpose purpose)
        {
            return new SpatialObstacleMeasurement(
                SpatialObstacleSource.Unavailable,
                timestampSeconds,
                false,
                0f,
                Vector3.zero,
                Vector3.zero,
                0f,
                0f,
                0,
                0f,
                0f,
                false,
                false,
                0,
                owner,
                SpatialObstacleSource.Unavailable,
                -1f,
                -1f,
                false,
                string.Empty,
                purpose);
        }
    }

    public readonly struct SpatialOwnerDiagnostics
    {
        public readonly SpatialProbeOwner Owner;
        public readonly bool Available;
        public readonly float EnvironmentDistanceMeters;
        public readonly float RoomSceneDistanceMeters;
        public readonly SpatialObstacleSource SelectedSource;
        public readonly float SelectedDistanceMeters;
        public readonly float Confidence;
        public readonly bool RawOverlap;
        public readonly bool ConfirmedOverlap;
        public readonly int ValidRayHitCount;
        public readonly bool SelfRejected;
        public readonly string RejectionReason;
        public readonly int SurfaceId;
        public readonly Vector3 PresentationNormal;
        public readonly float PresentationNormalChangeDegrees;

        public SpatialOwnerDiagnostics(
            SpatialProbeOwner owner,
            SpatialObstacleMeasurement measurement)
        {
            Owner = owner;
            Available = measurement.Available;
            EnvironmentDistanceMeters =
                measurement.EnvironmentDistanceMeters;
            RoomSceneDistanceMeters = measurement.RoomSceneDistanceMeters;
            SelectedSource = measurement.SelectedSource;
            SelectedDistanceMeters = measurement.DistanceMeters;
            Confidence = measurement.Confidence;
            RawOverlap = measurement.RawSafetyVolumeOverlap;
            ConfirmedOverlap = measurement.SafetyVolumeOverlap;
            ValidRayHitCount = measurement.ValidRayHitCount;
            SelfRejected = measurement.SelfRejected;
            RejectionReason = measurement.RejectionReason ?? string.Empty;
            SurfaceId = measurement.SurfaceId;
            PresentationNormal = measurement.PresentationGeometry.Available
                ? measurement.PresentationGeometry.SurfaceNormal
                : measurement.HitNormal;
            PresentationNormalChangeDegrees =
                measurement.PresentationNormalChangeDegrees;
        }
    }

    public interface ISpatialObstacleProvider
    {
        bool TryMeasure(
            SpatialProbe probe,
            out SpatialObstacleMeasurement measurement);
    }

    public interface IStaticBoundaryFrameProvider
    {
        StaticBoundaryRiskFrame CurrentStaticBoundaryFrame { get; }
    }

    public static class SpatialProviderSelection
    {
        public static bool ShouldUseRoomSceneFallback(
            bool environmentDepthSupported,
            bool hasEnvironmentMeasurement,
            float environmentMeasurementAgeSeconds,
            float staleAfterSeconds)
        {
            if (!environmentDepthSupported || !hasEnvironmentMeasurement)
            {
                return true;
            }

            return environmentMeasurementAgeSeconds
                > Mathf.Max(0f, staleAfterSeconds);
        }
    }

    public readonly struct TrackingDiagnosticsSnapshot
    {
        public readonly TrackingQualityProfile Profile;
        public readonly int AdaptiveLevel;
        public readonly float SpatialRateHz;
        public readonly int SpatialRayCount;
        public readonly float SpatialMilliseconds;
        public readonly SpatialObstacleSource SpatialSource;
        public readonly float SpatialDistanceMeters;
        public readonly float SpatialConfidence;
        public readonly float InferenceRateHz;
        public readonly float TargetInferenceRateHz;
        public readonly float MeasuredInferenceRateHz;
        public readonly bool HasInferenceMeasurement;
        public readonly float InferenceMilliseconds;
        public readonly float InferenceActiveMilliseconds;
        public readonly float InferenceWallMilliseconds;
        public readonly float CaptureAgeMilliseconds;
        public readonly int ScheduledLayerCount;
        public readonly string InferenceBackend;
        public readonly bool InferenceActive;
        public readonly bool ApplicationPaused;
        public readonly int PersonTrackId;
        public readonly float PersonRawDistanceMeters;
        public readonly float PersonFilteredDistanceMeters;
        public readonly string PersonMotion;
        public readonly float PersonMissingSeconds;
        public readonly bool PersonMetricReliable;
        public readonly string PersonDepthRejectedReason;
        public readonly float FramesPerSecond;
        public readonly float FrameP95Milliseconds;
        public readonly SpatialOwnerDiagnostics HeadSpatial;
        public readonly SpatialOwnerDiagnostics LeftHandSpatial;
        public readonly SpatialOwnerDiagnostics RightHandSpatial;
        public readonly bool CameraReady;
        public readonly int RawCandidateCount;
        public readonly int PersonCandidateCount;
        public readonly int ConfidenceRejectedCount;
        public readonly int BoxRejectedCount;
        public readonly int ClassRejectedCount;
        public readonly string InferenceWatchdogState;
        public readonly float InferenceSliceBudgetMilliseconds;

        public TrackingDiagnosticsSnapshot(
            TrackingQualityProfile profile,
            int adaptiveLevel,
            float spatialRateHz,
            int spatialRayCount,
            float spatialMilliseconds,
            SpatialObstacleSource spatialSource,
            float spatialDistanceMeters,
            float spatialConfidence,
            float inferenceRateHz,
            float inferenceMilliseconds,
            int personTrackId,
            float personRawDistanceMeters,
            float personFilteredDistanceMeters,
            string personMotion,
            float personMissingSeconds,
            float framesPerSecond,
            float frameP95Milliseconds,
            float inferenceActiveMilliseconds = 0f,
            float inferenceWallMilliseconds = 0f,
            float captureAgeMilliseconds = 0f,
            int scheduledLayerCount = 0,
            string inferenceBackend = "Unavailable",
            bool inferenceActive = false,
            bool applicationPaused = false,
            bool personMetricReliable = false,
            string personDepthRejectedReason = null,
            SpatialOwnerDiagnostics headSpatial = default,
            SpatialOwnerDiagnostics leftHandSpatial = default,
            SpatialOwnerDiagnostics rightHandSpatial = default,
            bool cameraReady = false,
            int rawCandidateCount = 0,
            int personCandidateCount = 0,
            int confidenceRejectedCount = 0,
            int boxRejectedCount = 0,
            int classRejectedCount = 0,
            string inferenceWatchdogState = "idle",
            float inferenceSliceBudgetMilliseconds = 0f,
            float targetInferenceRateHz = 0f,
            float measuredInferenceRateHz = 0f,
            bool hasInferenceMeasurement = false)
        {
            Profile = profile;
            AdaptiveLevel = Mathf.Max(0, adaptiveLevel);
            SpatialRateHz = Mathf.Max(0f, spatialRateHz);
            SpatialRayCount = Mathf.Max(0, spatialRayCount);
            SpatialMilliseconds = Mathf.Max(0f, spatialMilliseconds);
            SpatialSource = spatialSource;
            SpatialDistanceMeters = Mathf.Max(0f, spatialDistanceMeters);
            SpatialConfidence = Mathf.Clamp01(spatialConfidence);
            InferenceRateHz = Mathf.Max(0f, inferenceRateHz);
            TargetInferenceRateHz = Mathf.Max(0f, targetInferenceRateHz);
            MeasuredInferenceRateHz = Mathf.Max(
                0f,
                measuredInferenceRateHz);
            HasInferenceMeasurement = hasInferenceMeasurement;
            InferenceMilliseconds = Mathf.Max(0f, inferenceMilliseconds);
            InferenceActiveMilliseconds = Mathf.Max(
                0f,
                inferenceActiveMilliseconds);
            InferenceWallMilliseconds = Mathf.Max(
                0f,
                inferenceWallMilliseconds);
            CaptureAgeMilliseconds = Mathf.Max(0f, captureAgeMilliseconds);
            ScheduledLayerCount = Mathf.Max(0, scheduledLayerCount);
            InferenceBackend = string.IsNullOrWhiteSpace(inferenceBackend)
                ? "Unavailable"
                : inferenceBackend;
            InferenceActive = inferenceActive;
            ApplicationPaused = applicationPaused;
            PersonTrackId = Mathf.Max(0, personTrackId);
            PersonRawDistanceMeters = Mathf.Max(0f, personRawDistanceMeters);
            PersonFilteredDistanceMeters = Mathf.Max(0f, personFilteredDistanceMeters);
            PersonMotion = personMotion ?? "Unavailable";
            PersonMissingSeconds = Mathf.Max(0f, personMissingSeconds);
            PersonMetricReliable = personMetricReliable;
            PersonDepthRejectedReason = personMetricReliable
                ? string.Empty
                : personDepthRejectedReason ?? string.Empty;
            FramesPerSecond = Mathf.Max(0f, framesPerSecond);
            FrameP95Milliseconds = Mathf.Max(0f, frameP95Milliseconds);
            HeadSpatial = headSpatial;
            LeftHandSpatial = leftHandSpatial;
            RightHandSpatial = rightHandSpatial;
            CameraReady = cameraReady;
            RawCandidateCount = Mathf.Max(0, rawCandidateCount);
            PersonCandidateCount = Mathf.Max(0, personCandidateCount);
            ConfidenceRejectedCount = Mathf.Max(0, confidenceRejectedCount);
            BoxRejectedCount = Mathf.Max(0, boxRejectedCount);
            ClassRejectedCount = Mathf.Max(0, classRejectedCount);
            InferenceWatchdogState = string.IsNullOrWhiteSpace(
                    inferenceWatchdogState)
                ? "idle"
                : inferenceWatchdogState;
            InferenceSliceBudgetMilliseconds = Mathf.Max(
                0f,
                inferenceSliceBudgetMilliseconds);
        }
    }

    public readonly struct TrackingTestMarker
    {
        public readonly string ScenarioId;
        public readonly string Scenario;
        public readonly string Phase;
        public readonly float GroundTruthDistanceMeters;
        public readonly double TimestampSeconds;

        public TrackingTestMarker(
            string scenarioId,
            string scenario,
            string phase,
            float groundTruthDistanceMeters,
            double timestampSeconds)
        {
            ScenarioId = scenarioId ?? string.Empty;
            Scenario = scenario ?? string.Empty;
            Phase = phase ?? string.Empty;
            GroundTruthDistanceMeters = groundTruthDistanceMeters;
            TimestampSeconds = Math.Max(0.0, timestampSeconds);
        }
    }
}
