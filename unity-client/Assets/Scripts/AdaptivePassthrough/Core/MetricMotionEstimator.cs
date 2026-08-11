using System;
using System.Collections.Generic;

namespace TeamVR.AdaptivePassthrough
{
    public sealed class MetricMotionEstimator
    {
        private struct Sample
        {
            public double TimestampSeconds;
            public float DistanceMeters;
        }

        private sealed class TrackState
        {
            public readonly List<Sample> Samples = new List<Sample>();
            public DynamicMotionState StableState = DynamicMotionState.Unknown;
            public bool HasSmoothedClosingSpeed;
            public float SmoothedClosingSpeed;
            public double LastUpdateSeconds;
        }

        private readonly Dictionary<int, TrackState> states =
            new Dictionary<int, TrackState>();
        private readonly double historyWindowSeconds;
        private readonly int minimumSamples;
        private readonly double minimumObservationSeconds;
        private readonly float approachEnterMetersPerSecond;
        private readonly float recedeEnterMetersPerSecond;
        private readonly float stateExitMetersPerSecond;
        private readonly float smoothingTimeConstantSeconds;

        public MetricMotionEstimator(
            double historyWindowSeconds = 0.80,
            int minimumSamples = 3,
            double minimumObservationSeconds = 0.20,
            float approachEnterMetersPerSecond = 0.15f,
            float recedeEnterMetersPerSecond = -0.15f,
            float stateExitMetersPerSecond = 0.05f,
            float smoothingTimeConstantSeconds = 0.25f)
        {
            this.historyWindowSeconds = Math.Max(0.20, historyWindowSeconds);
            this.minimumSamples = Math.Max(2, minimumSamples);
            this.minimumObservationSeconds = Math.Max(
                0.05,
                minimumObservationSeconds);
            this.approachEnterMetersPerSecond = Math.Max(
                0.01f,
                approachEnterMetersPerSecond);
            this.recedeEnterMetersPerSecond = Math.Min(
                -0.01f,
                recedeEnterMetersPerSecond);
            this.stateExitMetersPerSecond = Math.Max(
                0.001f,
                stateExitMetersPerSecond);
            this.smoothingTimeConstantSeconds = Math.Max(
                0.001f,
                smoothingTimeConstantSeconds);
        }

        public MotionEstimate Estimate(
            double timestampSeconds,
            int trackId,
            PersonDistanceMeasurement distance,
            MotionEstimate boundingBoxFallback)
        {
            MotionEstimate fallback = boundingBoxFallback
                ?? new MotionEstimate(
                    DynamicMotionState.Unknown,
                    0f,
                    null,
                    0f,
                    0,
                    0.0,
                    0f);
            if (distance == null || !distance.HasMetricDistance)
            {
                return fallback.WithMetric(
                    distance == null
                        ? PersonDistanceSource.Unavailable
                        : distance.Source,
                    false,
                    0f,
                    null,
                    fallback.State,
                    fallback.Reliability,
                    fallback.SampleCount,
                    fallback.ObservationSeconds);
            }

            TrackState state = GetOrCreate(trackId);
            if (state.Samples.Count == 0
                || timestampSeconds
                    > state.Samples[state.Samples.Count - 1].TimestampSeconds)
            {
                state.Samples.Add(new Sample
                {
                    TimestampSeconds = timestampSeconds,
                    DistanceMeters = distance.FilteredDistanceMeters
                });
            }

            double oldestAllowed = timestampSeconds - historyWindowSeconds;
            while (state.Samples.Count > 0
                && state.Samples[0].TimestampSeconds < oldestAllowed)
            {
                state.Samples.RemoveAt(0);
            }

            double observationSeconds = state.Samples.Count > 1
                ? state.Samples[state.Samples.Count - 1].TimestampSeconds
                    - state.Samples[0].TimestampSeconds
                : 0.0;
            if (state.Samples.Count < minimumSamples
                || observationSeconds < minimumObservationSeconds)
            {
                state.LastUpdateSeconds = timestampSeconds;
                return fallback.WithMetric(
                    PersonDistanceSource.EnvironmentDepth,
                    false,
                    0f,
                    null,
                    DynamicMotionState.Unknown,
                    MetricReliability(
                        state.Samples.Count,
                        observationSeconds,
                        distance.Confidence),
                    state.Samples.Count,
                    observationSeconds);
            }

            float distanceSlope = LinearRegressionSlope(state.Samples);
            float rawClosingSpeed = -distanceSlope;
            double elapsed = state.LastUpdateSeconds > 0.0
                ? Math.Max(0.0, timestampSeconds - state.LastUpdateSeconds)
                : 0.0;
            float alpha = state.HasSmoothedClosingSpeed
                ? 1f - (float)Math.Exp(
                    -elapsed / smoothingTimeConstantSeconds)
                : 1f;
            state.SmoothedClosingSpeed = state.HasSmoothedClosingSpeed
                ? Lerp(
                    state.SmoothedClosingSpeed,
                    rawClosingSpeed,
                    Clamp01(alpha))
                : rawClosingSpeed;
            state.HasSmoothedClosingSpeed = true;
            state.LastUpdateSeconds = timestampSeconds;
            state.StableState = NextState(
                state.StableState,
                state.SmoothedClosingSpeed);

            float? metricTtc = state.SmoothedClosingSpeed > 0.10f
                ? (float?)Math.Min(
                    15f,
                    distance.FilteredDistanceMeters
                        / state.SmoothedClosingSpeed)
                : null;
            float reliability = MetricReliability(
                state.Samples.Count,
                observationSeconds,
                distance.Confidence);
            return fallback.WithMetric(
                PersonDistanceSource.EnvironmentDepth,
                true,
                state.SmoothedClosingSpeed,
                metricTtc,
                state.StableState,
                reliability,
                state.Samples.Count,
                observationSeconds);
        }

        public void PruneExcept(IEnumerable<int> liveTrackIds)
        {
            var live = liveTrackIds == null
                ? new HashSet<int>()
                : new HashSet<int>(liveTrackIds);
            var expired = new List<int>();
            foreach (int trackId in states.Keys)
            {
                if (!live.Contains(trackId))
                {
                    expired.Add(trackId);
                }
            }

            for (int i = 0; i < expired.Count; i++)
            {
                states.Remove(expired[i]);
            }
        }

        public void Reset()
        {
            states.Clear();
        }

        private DynamicMotionState NextState(
            DynamicMotionState previous,
            float closingSpeed)
        {
            switch (previous)
            {
                case DynamicMotionState.Approaching:
                    if (closingSpeed >= stateExitMetersPerSecond)
                    {
                        return DynamicMotionState.Approaching;
                    }
                    break;
                case DynamicMotionState.Receding:
                    if (closingSpeed <= -stateExitMetersPerSecond)
                    {
                        return DynamicMotionState.Receding;
                    }
                    break;
            }

            if (closingSpeed >= approachEnterMetersPerSecond)
            {
                return DynamicMotionState.Approaching;
            }

            if (closingSpeed <= recedeEnterMetersPerSecond)
            {
                return DynamicMotionState.Receding;
            }

            return DynamicMotionState.Steady;
        }

        private TrackState GetOrCreate(int trackId)
        {
            TrackState state;
            if (!states.TryGetValue(trackId, out state))
            {
                state = new TrackState();
                states.Add(trackId, state);
            }

            return state;
        }

        private static float LinearRegressionSlope(List<Sample> samples)
        {
            double origin = samples[0].TimestampSeconds;
            double meanTime = 0.0;
            double meanDistance = 0.0;
            for (int i = 0; i < samples.Count; i++)
            {
                meanTime += samples[i].TimestampSeconds - origin;
                meanDistance += samples[i].DistanceMeters;
            }

            meanTime /= samples.Count;
            meanDistance /= samples.Count;
            double numerator = 0.0;
            double denominator = 0.0;
            for (int i = 0; i < samples.Count; i++)
            {
                double centeredTime =
                    samples[i].TimestampSeconds - origin - meanTime;
                numerator += centeredTime
                    * (samples[i].DistanceMeters - meanDistance);
                denominator += centeredTime * centeredTime;
            }

            return denominator <= 0.0000001
                ? 0f
                : (float)(numerator / denominator);
        }

        private static float MetricReliability(
            int sampleCount,
            double observationSeconds,
            float distanceConfidence)
        {
            float countFactor = Math.Min(1f, sampleCount / 6f);
            float timeFactor = (float)Math.Min(
                1.0,
                observationSeconds / 0.60);
            return Clamp01(
                countFactor
                * timeFactor
                * Clamp01(distanceConfidence));
        }

        private static float Lerp(float from, float to, float amount)
        {
            return from + (to - from) * amount;
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
