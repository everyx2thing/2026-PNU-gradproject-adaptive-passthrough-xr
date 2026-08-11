using System;

namespace TeamVR.AdaptivePassthrough
{
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
            string failureReason = null)
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
