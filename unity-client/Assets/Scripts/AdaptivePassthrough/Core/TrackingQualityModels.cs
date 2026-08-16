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

        public TrackingQualitySettings(
            TrackingQualityProfile profile,
            float spatialRateHz,
            int spatialRayCount,
            float personInferenceRateHz)
        {
            this.profile = profile;
            this.spatialRateHz = Mathf.Max(1f, spatialRateHz);
            this.spatialRayCount = Mathf.Max(1, spatialRayCount);
            this.personInferenceRateHz = Mathf.Max(1f, personInferenceRateHz);
        }

        public static TrackingQualitySettings For(
            TrackingQualityProfile profile)
        {
            switch (profile)
            {
                case TrackingQualityProfile.Accuracy:
                    return new TrackingQualitySettings(profile, 30f, 24, 8f);
                case TrackingQualityProfile.Performance:
                    return new TrackingQualitySettings(profile, 10f, 6, 3f);
                default:
                    return new TrackingQualitySettings(
                        TrackingQualityProfile.Balanced,
                        20f,
                        12,
                        5f);
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
            bool safetyVolumeOverlap)
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
        public readonly int PersonTrackId;
        public readonly float PersonRawDistanceMeters;
        public readonly float PersonFilteredDistanceMeters;
        public readonly string PersonMotion;
        public readonly float PersonMissingSeconds;
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
            float frameP95Milliseconds)
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
            PersonTrackId = Mathf.Max(0, personTrackId);
            PersonRawDistanceMeters = Mathf.Max(0f, personRawDistanceMeters);
            PersonFilteredDistanceMeters = Mathf.Max(0f, personFilteredDistanceMeters);
            PersonMotion = personMotion ?? "Unavailable";
            PersonMissingSeconds = Mathf.Max(0f, personMissingSeconds);
            FramesPerSecond = Mathf.Max(0f, framesPerSecond);
            FrameP95Milliseconds = Mathf.Max(0f, frameP95Milliseconds);
        }
    }
}
