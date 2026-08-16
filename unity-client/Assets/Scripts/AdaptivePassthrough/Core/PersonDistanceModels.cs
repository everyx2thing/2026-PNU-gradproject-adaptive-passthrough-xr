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
        public readonly string FailureReason;

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
            Vector3 worldPoint = default)
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
            HasWorldPoint = hasWorldPoint;
            WorldPoint = hasWorldPoint ? worldPoint : Vector3.zero;
            FailureReason = failureReason ?? string.Empty;
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

        public PersonDistanceMeasurement WithWorldPoint(Vector3 worldPoint)
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
                worldPoint);
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
}
