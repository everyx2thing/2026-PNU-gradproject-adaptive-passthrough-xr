using System;
using System.Collections.Generic;

namespace TeamVR.AdaptivePassthrough
{
    public sealed class PersonDistanceFilter
    {
        private sealed class TrackState
        {
            public readonly List<float> RawWindow = new List<float>();
            public bool HasFilteredDistance;
            public float LastRawDistance;
            public float FilteredDistance;
            public float LastConfidence;
            public int LastRequestedSamples;
            public int LastValidSamples;
            public double LastUpdateSeconds;
            public double LastMetricSeconds;
            public int PendingJumpDirection;
            public int PendingJumpCount;
        }

        private readonly Dictionary<int, TrackState> states =
            new Dictionary<int, TrackState>();
        private readonly float minimumDistanceMeters;
        private readonly float maximumDistanceMeters;
        private readonly float clusterGapMeters;
        private readonly int minimumClusterSamples;
        private readonly int medianWindowSamples;
        private readonly float smoothingTimeConstantSeconds;
        private readonly float jumpThresholdMeters;
        private readonly int jumpConfirmationSamples;
        private readonly float metricHoldSeconds;

        public PersonDistanceFilter(
            float minimumDistanceMeters = 0.20f,
            float maximumDistanceMeters = 6.0f,
            float clusterGapMeters = 0.35f,
            int minimumClusterSamples = 3,
            int medianWindowSamples = 5,
            float smoothingTimeConstantSeconds = 0.25f,
            float jumpThresholdMeters = 1.50f,
            int jumpConfirmationSamples = 2,
            float metricHoldSeconds = 0.50f)
        {
            this.minimumDistanceMeters = Math.Max(0f, minimumDistanceMeters);
            this.maximumDistanceMeters = Math.Max(
                this.minimumDistanceMeters,
                maximumDistanceMeters);
            this.clusterGapMeters = Math.Max(0.01f, clusterGapMeters);
            this.minimumClusterSamples = Math.Max(1, minimumClusterSamples);
            this.medianWindowSamples = Math.Max(1, medianWindowSamples);
            this.smoothingTimeConstantSeconds = Math.Max(
                0.001f,
                smoothingTimeConstantSeconds);
            this.jumpThresholdMeters = Math.Max(0.01f, jumpThresholdMeters);
            this.jumpConfirmationSamples = Math.Max(
                1,
                jumpConfirmationSamples);
            this.metricHoldSeconds = Math.Max(0f, metricHoldSeconds);
        }

        public PersonDistanceMeasurement UpdateMetric(
            int trackId,
            double timestampSeconds,
            IReadOnlyList<float> samples,
            int requestedSampleCount,
            float boundingBoxArea)
        {
            float rawDistance;
            int selectedSampleCount;
            float dispersion;
            if (!TrySelectNearestCluster(
                    samples,
                    minimumDistanceMeters,
                    maximumDistanceMeters,
                    clusterGapMeters,
                    minimumClusterSamples,
                    out rawDistance,
                    out selectedSampleCount,
                    out dispersion))
            {
                return GetHeldOrBoundingBoxFallback(
                    trackId,
                    timestampSeconds,
                    boundingBoxArea,
                    "insufficient_depth_samples");
            }

            TrackState state = GetOrCreate(trackId);
            state.RawWindow.Add(rawDistance);
            while (state.RawWindow.Count > medianWindowSamples)
            {
                state.RawWindow.RemoveAt(0);
            }

            float candidate = MedianCopy(state.RawWindow);
            string note = string.Empty;
            if (state.HasFilteredDistance
                && Math.Abs(rawDistance - state.FilteredDistance)
                    > jumpThresholdMeters)
            {
                int direction = Math.Sign(
                    rawDistance - state.FilteredDistance);
                if (direction == state.PendingJumpDirection)
                {
                    state.PendingJumpCount++;
                }
                else
                {
                    state.PendingJumpDirection = direction;
                    state.PendingJumpCount = 1;
                }

                if (state.PendingJumpCount < jumpConfirmationSamples)
                {
                    candidate = state.FilteredDistance;
                    note = "distance_jump_suppressed";
                }
                else
                {
                    state.PendingJumpDirection = 0;
                    state.PendingJumpCount = 0;
                }
            }
            else
            {
                state.PendingJumpDirection = 0;
                state.PendingJumpCount = 0;
            }

            double elapsed = state.HasFilteredDistance
                ? Math.Max(0.0, timestampSeconds - state.LastUpdateSeconds)
                : 0.0;
            float alpha = state.HasFilteredDistance
                ? 1f - (float)Math.Exp(
                    -elapsed / smoothingTimeConstantSeconds)
                : 1f;
            state.FilteredDistance = state.HasFilteredDistance
                ? Lerp(state.FilteredDistance, candidate, Clamp01(alpha))
                : candidate;
            state.HasFilteredDistance = true;
            state.LastRawDistance = rawDistance;
            state.LastUpdateSeconds = timestampSeconds;
            state.LastMetricSeconds = timestampSeconds;
            state.LastRequestedSamples = Math.Max(
                requestedSampleCount,
                selectedSampleCount);
            state.LastValidSamples = selectedSampleCount;

            float sampleRatio = state.LastRequestedSamples <= 0
                ? 0f
                : selectedSampleCount / (float)state.LastRequestedSamples;
            float consistency = (float)Math.Exp(
                -Math.Max(0f, dispersion) / clusterGapMeters);
            state.LastConfidence = Clamp01(sampleRatio * consistency);

            return new PersonDistanceMeasurement(
                trackId,
                timestampSeconds,
                PersonDistanceSource.EnvironmentDepth,
                true,
                rawDistance,
                state.FilteredDistance,
                state.LastConfidence,
                state.LastRequestedSamples,
                selectedSampleCount,
                0f,
                boundingBoxArea,
                note);
        }

        public PersonDistanceMeasurement GetHeldOrBoundingBoxFallback(
            int trackId,
            double timestampSeconds,
            float boundingBoxArea,
            string failureReason)
        {
            TrackState state;
            if (states.TryGetValue(trackId, out state)
                && state.HasFilteredDistance)
            {
                float age = (float)Math.Max(
                    0.0,
                    timestampSeconds - state.LastMetricSeconds);
                if (age <= metricHoldSeconds)
                {
                    float holdConfidence = state.LastConfidence
                        * (metricHoldSeconds <= 0f
                            ? 0f
                            : Math.Max(0f, 1f - age / metricHoldSeconds));
                    return new PersonDistanceMeasurement(
                        trackId,
                        timestampSeconds,
                        PersonDistanceSource.EnvironmentDepth,
                        true,
                        state.LastRawDistance,
                        state.FilteredDistance,
                        holdConfidence,
                        state.LastRequestedSamples,
                        state.LastValidSamples,
                        age,
                        boundingBoxArea,
                        failureReason);
                }
            }

            return PersonDistanceMeasurement.BoundingBoxFallback(
                trackId,
                timestampSeconds,
                boundingBoxArea,
                failureReason);
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

        public static bool TrySelectNearestCluster(
            IReadOnlyList<float> samples,
            float minimumDistanceMeters,
            float maximumDistanceMeters,
            float clusterGapMeters,
            int minimumClusterSamples,
            out float medianDistance,
            out int selectedSampleCount,
            out float dispersion)
        {
            medianDistance = 0f;
            selectedSampleCount = 0;
            dispersion = 0f;
            if (samples == null || samples.Count == 0)
            {
                return false;
            }

            var valid = new List<float>(samples.Count);
            for (int i = 0; i < samples.Count; i++)
            {
                float value = samples[i];
                if (IsFinite(value)
                    && value >= minimumDistanceMeters
                    && value <= maximumDistanceMeters)
                {
                    valid.Add(value);
                }
            }

            valid.Sort();
            int start = 0;
            while (start < valid.Count)
            {
                int end = start + 1;
                while (end < valid.Count
                    && valid[end] - valid[end - 1] <= clusterGapMeters)
                {
                    end++;
                }

                int count = end - start;
                if (count >= Math.Max(1, minimumClusterSamples))
                {
                    int middle = start + count / 2;
                    medianDistance = count % 2 == 0
                        ? (valid[middle - 1] + valid[middle]) * 0.5f
                        : valid[middle];
                    selectedSampleCount = count;
                    dispersion = valid[end - 1] - valid[start];
                    return true;
                }

                start = end;
            }

            return false;
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

        private static float MedianCopy(List<float> values)
        {
            var copy = new List<float>(values);
            copy.Sort();
            int middle = copy.Count / 2;
            return copy.Count % 2 == 0
                ? (copy[middle - 1] + copy[middle]) * 0.5f
                : copy[middle];
        }

        private static float Lerp(float from, float to, float amount)
        {
            return from + (to - from) * amount;
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
