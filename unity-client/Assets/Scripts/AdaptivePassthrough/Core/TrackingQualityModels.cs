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

    public enum SpatialObstacleSource
    {
        Unavailable,
        EnvironmentDepth,
        RoomScene
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
                2)
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
                        profile, 30f, 24, 3f, 2.5f, 4);
                case TrackingQualityProfile.Performance:
                    return new TrackingQualitySettings(
                        profile, 10f, 6, 2f, 0.75f, 1);
                default:
                    return new TrackingQualitySettings(
                        TrackingQualityProfile.Balanced,
                        20f,
                        12,
                        3f,
                        1.5f,
                        2);
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

        public SpatialProbe(
            SpatialProbeOwner owner,
            Vector3 origin,
            Vector3 direction,
            Vector3 velocity,
            float safetyRadius)
        {
            Owner = owner;
            Origin = origin;
            Direction = direction.sqrMagnitude > 0.000001f
                ? direction.normalized
                : Vector3.forward;
            Velocity = velocity;
            SafetyRadius = Mathf.Max(0f, safetyRadius);
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
            int validRayHitCount = -1)
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
        }

        public static SpatialObstacleMeasurement Unavailable(
            double timestampSeconds)
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
                false);
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
            string personDepthRejectedReason = null)
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
