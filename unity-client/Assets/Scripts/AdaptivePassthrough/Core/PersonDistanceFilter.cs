using System;
using System.Collections.Generic;

namespace TeamVR.AdaptivePassthrough
{
    public sealed class PersonDistanceFilter
    {
        private sealed class TrackState
        {
            public readonly List<float> RawWindow = new List<float>();
            public float[] MedianScratch = Array.Empty<float>();
            public bool HasFilteredDistance;
            public float LastRawDistance;
            public float FilteredDistance;
            public float LastConfidence;
            public int LastRequestedSamples;
            public int LastValidSamples;
            public double LastUpdateSeconds;
            public double LastMetricSeconds;
            public float LastBoundingBoxArea;
            public int PendingJumpDirection;
            public int PendingJumpCount;
        }

        private readonly Dictionary<int, TrackState> states =
            new Dictionary<int, TrackState>();
        private readonly List<WeightedSample> selectionScratch =
            new List<WeightedSample>(25);
        private readonly float minimumDistanceMeters;
        private readonly float maximumDistanceMeters;
        private readonly float clusterGapMeters;
        private readonly int minimumClusterSamples;
        private readonly int medianWindowSamples;
        private readonly float smoothingTimeConstantSeconds;
        private readonly float baseJumpThresholdMeters;
        private readonly float maximumJumpSpeedMetersPerSecond;
        private readonly int jumpConfirmationSamples;
        private readonly float metricHoldSeconds;

        public PersonDistanceFilter(
            float minimumDistanceMeters = 0.20f,
            float maximumDistanceMeters = 6.0f,
            float clusterGapMeters = 0.35f,
            int minimumClusterSamples = 3,
            int medianWindowSamples = 5,
            float smoothingTimeConstantSeconds = 0.25f,
            float jumpThresholdMeters = 0.35f,
            float maximumJumpSpeedMetersPerSecond = 2.50f,
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
            baseJumpThresholdMeters = Math.Max(0.01f, jumpThresholdMeters);
            this.maximumJumpSpeedMetersPerSecond = Math.Max(
                0f,
                maximumJumpSpeedMetersPerSecond);
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
            float boundingBoxArea,
            IReadOnlyList<float> sampleWeights = null,
            float minimumConfidenceToCommit = 0f,
            float maximumAcceptedRawDistance = float.PositiveInfinity,
            float maximumAcceptedDispersion = float.PositiveInfinity)
        {
            float rawDistance;
            int selectedSampleCount;
            float dispersion;
            if (!TrySelectSupportedForegroundClusterCore(
                    samples,
                    sampleWeights,
                    minimumDistanceMeters,
                    maximumDistanceMeters,
                    clusterGapMeters,
                    minimumClusterSamples,
                    out rawDistance,
                    out selectedSampleCount,
                    out dispersion,
                    selectionScratch))
            {
                return GetHeldOrBoundingBoxFallback(
                    trackId,
                    timestampSeconds,
                    boundingBoxArea,
                    "insufficient_depth_samples");
            }

            int acceptedRequestedSamples = Math.Max(
                requestedSampleCount,
                selectedSampleCount);
            // Reliability is based on foreground support, not on how many
            // sampling rays were requested. Otherwise a one-shot 25-point
            // recovery pass makes a valid 3-5 point body cluster less reliable
            // than the same cluster in the 13-point pass.
            float sampleRatio = Math.Min(
                1f,
                selectedSampleCount / 5f);
            float consistency = (float)Math.Exp(
                -Math.Max(0f, dispersion) / clusterGapMeters);
            float rawConfidence = Clamp01(sampleRatio * consistency);
            if (rawConfidence < Math.Max(0f, minimumConfidenceToCommit)
                || rawDistance > maximumAcceptedRawDistance
                || dispersion > maximumAcceptedDispersion)
            {
                string rejectedReason = rawDistance
                        > maximumAcceptedRawDistance
                    ? "background_depth_suspected"
                    : dispersion > maximumAcceptedDispersion
                        ? "depth_dispersion"
                        : "low_depth_confidence";
                return new PersonDistanceMeasurement(
                    trackId,
                    timestampSeconds,
                    PersonDistanceSource.EnvironmentDepth,
                    true,
                    rawDistance,
                    rawDistance,
                    rawConfidence,
                    acceptedRequestedSamples,
                    selectedSampleCount,
                    0f,
                    boundingBoxArea,
                    rejectedReason,
                    dispersion,
                    false,
                    false,
                    default,
                    false,
                    rejectedReason);
            }

            TrackState state = GetOrCreate(trackId);
            string note = string.Empty;
            double jumpElapsed = state.HasFilteredDistance
                ? Math.Max(0.0, timestampSeconds - state.LastUpdateSeconds)
                : 0.0;
            float dynamicJumpThreshold = baseJumpThresholdMeters
                + maximumJumpSpeedMetersPerSecond * (float)jumpElapsed;
            bool bboxGrowing = state.LastBoundingBoxArea > 0f
                && boundingBoxArea > state.LastBoundingBoxArea * 1.08f;
            bool depthMovingAway = state.HasFilteredDistance
                && rawDistance > state.FilteredDistance + 0.05f;
            bool bboxDepthConflict = bboxGrowing && depthMovingAway;
            if (bboxDepthConflict)
            {
                return new PersonDistanceMeasurement(
                    trackId,
                    timestampSeconds,
                    PersonDistanceSource.EnvironmentDepth,
                    true,
                    rawDistance,
                    state.FilteredDistance,
                    rawConfidence * 0.45f,
                    acceptedRequestedSamples,
                    selectedSampleCount,
                    0f,
                    boundingBoxArea,
                    "bbox_depth_conflict",
                    dispersion,
                    true,
                    false,
                    default,
                    false,
                    "bbox_depth_conflict");
            }

            state.RawWindow.Add(rawDistance);
            while (state.RawWindow.Count > medianWindowSamples)
            {
                state.RawWindow.RemoveAt(0);
            }

            float candidate = MedianCopy(state);
            if (state.HasFilteredDistance
                && Math.Abs(rawDistance - state.FilteredDistance)
                    > dynamicJumpThreshold)
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
            state.LastRequestedSamples = acceptedRequestedSamples;
            state.LastValidSamples = selectedSampleCount;
            state.LastBoundingBoxArea = boundingBoxArea;

            state.LastConfidence = rawConfidence;

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
                note,
                dispersion,
                bboxDepthConflict);
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

        public static bool TrySelectSupportedForegroundCluster(
            IReadOnlyList<float> samples,
            IReadOnlyList<float> sampleWeights,
            float minimumDistanceMeters,
            float maximumDistanceMeters,
            float clusterGapMeters,
            int minimumClusterSamples,
            out float medianDistance,
            out int selectedSampleCount,
            out float dispersion)
        {
            return TrySelectSupportedForegroundClusterCore(
                samples,
                sampleWeights,
                minimumDistanceMeters,
                maximumDistanceMeters,
                clusterGapMeters,
                minimumClusterSamples,
                out medianDistance,
                out selectedSampleCount,
                out dispersion,
                new List<WeightedSample>(samples == null ? 0 : samples.Count));
        }

        private static bool TrySelectSupportedForegroundClusterCore(
            IReadOnlyList<float> samples,
            IReadOnlyList<float> sampleWeights,
            float minimumDistanceMeters,
            float maximumDistanceMeters,
            float clusterGapMeters,
            int minimumClusterSamples,
            out float medianDistance,
            out int selectedSampleCount,
            out float dispersion,
            List<WeightedSample> valid)
        {
            medianDistance = 0f;
            selectedSampleCount = 0;
            dispersion = 0f;
            if (samples == null || samples.Count == 0)
            {
                return false;
            }

            valid.Clear();
            for (int i = 0; i < samples.Count; i++)
            {
                float value = samples[i];
                if (!IsFinite(value)
                    || value < minimumDistanceMeters
                    || value > maximumDistanceMeters)
                {
                    continue;
                }

                float weight = sampleWeights != null
                    && i < sampleWeights.Count
                        ? Math.Max(0.05f, sampleWeights[i])
                        : 1f;
                valid.Add(new WeightedSample(value, weight));
            }

            valid.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            int bestStart = -1;
            int bestEnd = -1;
            float bestScore = float.MinValue;
            int start = 0;
            while (start < valid.Count)
            {
                int end = start + 1;
                float support = valid[start].Weight;
                while (end < valid.Count
                    && valid[end].Distance - valid[end - 1].Distance
                        <= clusterGapMeters)
                {
                    support += valid[end].Weight;
                    end++;
                }

                int count = end - start;
                if (count >= Math.Max(1, minimumClusterSamples))
                {
                    float centerDistance = valid[start + count / 2].Distance;
                    float foregroundPreference = 1f
                        / (1f + centerDistance * 0.04f);
                    float score = support * foregroundPreference;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestStart = start;
                        bestEnd = end;
                    }
                }

                start = end;
            }

            if (bestStart < 0)
            {
                return false;
            }

            selectedSampleCount = bestEnd - bestStart;
            int middle = bestStart + selectedSampleCount / 2;
            medianDistance = selectedSampleCount % 2 == 0
                ? (valid[middle - 1].Distance + valid[middle].Distance) * 0.5f
                : valid[middle].Distance;
            dispersion = valid[bestEnd - 1].Distance
                - valid[bestStart].Distance;
            return true;
        }

        private readonly struct WeightedSample
        {
            public readonly float Distance;
            public readonly float Weight;

            public WeightedSample(float distance, float weight)
            {
                Distance = distance;
                Weight = weight;
            }
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

        private static float MedianCopy(TrackState state)
        {
            int count = state.RawWindow.Count;
            if (state.MedianScratch.Length < count)
            {
                state.MedianScratch = new float[Math.Max(5, count)];
            }

            for (int i = 0; i < count; i++)
            {
                state.MedianScratch[i] = state.RawWindow[i];
            }

            Array.Sort(state.MedianScratch, 0, count);
            int middle = count / 2;
            return count % 2 == 0
                ? (state.MedianScratch[middle - 1]
                    + state.MedianScratch[middle]) * 0.5f
                : state.MedianScratch[middle];
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
