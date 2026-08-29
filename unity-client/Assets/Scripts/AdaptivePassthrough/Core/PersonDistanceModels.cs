using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    public readonly struct PersonObservation
    {
        public readonly DynamicObjectDetection Detection;
        public readonly Pose CapturePose;
        public readonly double CaptureTimestampSeconds;
        public readonly float RawClusterDistanceMeters;
        public readonly float ClusterDispersionMeters;
        public readonly int ClusterSampleCount;
        public readonly bool HasWorldPoint;
        public readonly Vector3 WorldPoint;

        public PersonObservation(
            DynamicObjectDetection detection,
            Pose capturePose,
            double captureTimestampSeconds,
            float rawClusterDistanceMeters,
            float clusterDispersionMeters,
            int clusterSampleCount,
            bool hasWorldPoint = false,
            Vector3 worldPoint = default)
        {
            Detection = detection;
            CapturePose = capturePose;
            CaptureTimestampSeconds = Math.Max(0.0, captureTimestampSeconds);
            RawClusterDistanceMeters = Mathf.Max(
                0f,
                rawClusterDistanceMeters);
            ClusterDispersionMeters = Mathf.Max(0f, clusterDispersionMeters);
            ClusterSampleCount = Mathf.Max(0, clusterSampleCount);
            HasWorldPoint = hasWorldPoint;
            WorldPoint = hasWorldPoint ? worldPoint : Vector3.zero;
        }
    }

    public enum PersonDistanceSource
    {
        Unavailable,
        EnvironmentDepth,
        BoundingBoxProxy
    }

    public sealed class PersonDistanceMeasurement
    {
        public readonly int TrackId;
        public readonly double TimestampSeconds;
        public readonly PersonDistanceSource Source;
        public readonly bool Available;
        public readonly float RawDistanceMeters;
        public readonly float FilteredDistanceMeters;
        public readonly float Confidence;
        public readonly int RequestedSampleCount;
        public readonly int ValidSampleCount;
        public readonly float SourceAgeSeconds;
        public readonly float BoundingBoxArea;
        public readonly float SampleDispersionMeters;
        public readonly bool BoundingBoxDepthConflict;
        public readonly bool HasWorldPoint;
        public readonly Vector3 WorldPoint;
        public readonly bool HasWorldVelocity;
        public readonly Vector3 WorldVelocity;
        public readonly string FailureReason;
        public readonly bool IsMetricReliable;
        public readonly string DepthRejectedReason;
        public readonly bool HasPresentationGeometry;
        public readonly HazardPresentationGeometry PresentationGeometry;

        public PersonDistanceMeasurement(
            int trackId,
            double timestampSeconds,
            PersonDistanceSource source,
            bool available,
            float rawDistanceMeters,
            float filteredDistanceMeters,
            float confidence,
            int requestedSampleCount,
            int validSampleCount,
            float sourceAgeSeconds,
            float boundingBoxArea,
            string failureReason = null,
            float sampleDispersionMeters = 0f,
            bool boundingBoxDepthConflict = false,
            bool hasWorldPoint = false,
            Vector3 worldPoint = default,
            bool? isMetricReliable = null,
            string depthRejectedReason = null,
            bool hasWorldVelocity = false,
            Vector3 worldVelocity = default,
            HazardPresentationGeometry presentationGeometry = default)
        {
            TrackId = Math.Max(0, trackId);
            TimestampSeconds = Math.Max(0.0, timestampSeconds);
            Source = source;
            Available = available;
            RawDistanceMeters = NonNegativeFinite(rawDistanceMeters);
            FilteredDistanceMeters = NonNegativeFinite(filteredDistanceMeters);
            Confidence = Clamp01(confidence);
            RequestedSampleCount = Math.Max(0, requestedSampleCount);
            ValidSampleCount = Math.Max(0, validSampleCount);
            SourceAgeSeconds = NonNegativeFinite(sourceAgeSeconds);
            BoundingBoxArea = Clamp01(boundingBoxArea);
            SampleDispersionMeters = NonNegativeFinite(
                sampleDispersionMeters);
            BoundingBoxDepthConflict = boundingBoxDepthConflict;
            FailureReason = failureReason ?? string.Empty;
            bool defaultReliable = Available
                && Source == PersonDistanceSource.EnvironmentDepth
                && FilteredDistanceMeters >= 0.20f
                && Confidence >= 0.45f
                && ValidSampleCount >= 3
                && SampleDispersionMeters <= 0.35f
                && !BoundingBoxDepthConflict;
            IsMetricReliable = isMetricReliable ?? defaultReliable;
            DepthRejectedReason = IsMetricReliable
                ? string.Empty
                : depthRejectedReason ?? string.Empty;
            HasWorldPoint = hasWorldPoint && IsMetricReliable;
            WorldPoint = HasWorldPoint ? worldPoint : Vector3.zero;
            HasWorldVelocity = HasWorldPoint && hasWorldVelocity;
            WorldVelocity = HasWorldVelocity
                ? worldVelocity
                : Vector3.zero;
            HasPresentationGeometry = IsMetricReliable
                && presentationGeometry.Available;
            PresentationGeometry = HasPresentationGeometry
                ? presentationGeometry
                : default;
        }

        public bool HasMetricDistance
        {
            get
            {
                return Available
                    && Source == PersonDistanceSource.EnvironmentDepth
                    && FilteredDistanceMeters >= 0.20f;
            }
        }

        public bool HasReliableMetricDistance
        {
            get { return HasMetricDistance && IsMetricReliable; }
        }

        public PersonDistanceMeasurement WithWorldPoint(Vector3 worldPoint)
        {
            return WithWorldPoint(worldPoint, Vector3.zero, false);
        }

        public PersonDistanceMeasurement WithWorldPoint(
            Vector3 worldPoint,
            Vector3 worldVelocity)
        {
            return WithWorldPoint(worldPoint, worldVelocity, true);
        }

        public PersonDistanceMeasurement WithWorldPoint(
            Vector3 worldPoint,
            Vector3 worldVelocity,
            bool hasWorldVelocity)
        {
            return new PersonDistanceMeasurement(
                TrackId,
                TimestampSeconds,
                Source,
                Available,
                RawDistanceMeters,
                FilteredDistanceMeters,
                Confidence,
                RequestedSampleCount,
                ValidSampleCount,
                SourceAgeSeconds,
                BoundingBoxArea,
                FailureReason,
                SampleDispersionMeters,
                BoundingBoxDepthConflict,
                true,
                worldPoint,
                IsMetricReliable,
                DepthRejectedReason,
                hasWorldVelocity,
                worldVelocity,
                PresentationGeometry);
        }

        public PersonDistanceMeasurement WithPresentationGeometry(
            HazardPresentationGeometry geometry)
        {
            return new PersonDistanceMeasurement(
                TrackId,
                TimestampSeconds,
                Source,
                Available,
                RawDistanceMeters,
                FilteredDistanceMeters,
                Confidence,
                RequestedSampleCount,
                ValidSampleCount,
                SourceAgeSeconds,
                BoundingBoxArea,
                FailureReason,
                SampleDispersionMeters,
                BoundingBoxDepthConflict,
                HasWorldPoint,
                WorldPoint,
                IsMetricReliable,
                DepthRejectedReason,
                HasWorldVelocity,
                WorldVelocity,
                geometry);
        }

        public PersonDistanceMeasurement WithMetricReliability(
            bool reliable,
            string rejectedReason = null)
        {
            return new PersonDistanceMeasurement(
                TrackId,
                TimestampSeconds,
                Source,
                Available,
                RawDistanceMeters,
                FilteredDistanceMeters,
                Confidence,
                RequestedSampleCount,
                ValidSampleCount,
                SourceAgeSeconds,
                BoundingBoxArea,
                FailureReason,
                SampleDispersionMeters,
                BoundingBoxDepthConflict,
                reliable && HasWorldPoint,
                WorldPoint,
                reliable,
                reliable ? string.Empty : rejectedReason,
                reliable && HasWorldVelocity,
                WorldVelocity,
                reliable ? PresentationGeometry : default);
        }

        public static PersonDistanceMeasurement BoundingBoxFallback(
            int trackId,
            double timestampSeconds,
            float boundingBoxArea,
            string reason = "depth_unavailable")
        {
            return new PersonDistanceMeasurement(
                trackId,
                timestampSeconds,
                PersonDistanceSource.BoundingBoxProxy,
                true,
                0f,
                0f,
                0f,
                0,
                0,
                0f,
                boundingBoxArea,
                reason);
        }

        public static PersonDistanceMeasurement Unavailable(
            int trackId,
            double timestampSeconds,
            float boundingBoxArea,
            string reason)
        {
            return new PersonDistanceMeasurement(
                trackId,
                timestampSeconds,
                PersonDistanceSource.Unavailable,
                false,
                0f,
                0f,
                0f,
                0,
                0,
                0f,
                boundingBoxArea,
                reason);
        }

        private static float NonNegativeFinite(float value)
        {
            return IsFinite(value) ? Math.Max(0f, value) : 0f;
        }

        private static float Clamp01(float value)
        {
            return IsFinite(value)
                ? Math.Max(0f, Math.Min(1f, value))
                : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public static class PersonDepthReliability
    {
        public const float MinimumConfidence = 0.45f;
        public const int MinimumSamples = 3;
        public const float MaximumDispersionMeters = 0.35f;
        public const float BackgroundDistanceMeters = 2.0f;
        public const float LargeBoxHeight = 0.75f;
        public const float LargeBoxArea = 0.35f;

        public static bool IsBackgroundSuspected(
            NormalizedBoundingBox box,
            float distanceMeters)
        {
            return distanceMeters > BackgroundDistanceMeters
                && IsLargeBox(box);
        }

        public static bool IsLargeBox(NormalizedBoundingBox box)
        {
            return box.height >= LargeBoxHeight
                || box.Area >= LargeBoxArea;
        }

        public static bool IsReliable(
            PersonDistanceMeasurement measurement,
            NormalizedBoundingBox box,
            out string rejectedReason)
        {
            if (measurement == null || !measurement.HasMetricDistance)
            {
                rejectedReason = measurement == null
                    ? "depth_unavailable"
                    : measurement.FailureReason;
                return false;
            }

            if (IsBackgroundSuspected(
                    box,
                    measurement.FilteredDistanceMeters))
            {
                rejectedReason = "background_depth_suspected";
                return false;
            }

            if (measurement.BoundingBoxDepthConflict)
            {
                rejectedReason = "bbox_depth_conflict";
                return false;
            }

            if (measurement.Confidence < MinimumConfidence)
            {
                rejectedReason = "low_depth_confidence";
                return false;
            }

            if (measurement.ValidSampleCount < MinimumSamples)
            {
                rejectedReason = "insufficient_depth_samples";
                return false;
            }

            if (measurement.SampleDispersionMeters
                > MaximumDispersionMeters)
            {
                rejectedReason = "depth_dispersion";
                return false;
            }

            rejectedReason = string.Empty;
            return true;
        }
    }
}
