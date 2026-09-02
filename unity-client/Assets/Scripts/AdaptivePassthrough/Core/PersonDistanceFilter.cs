using System;
using System.Collections.Generic;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    public sealed class PersonDistanceFilter
    {
        private const float SpatialNeighborX = 0.21f;
        private const float SpatialNeighborY = 0.18f;
        private const float NeighborDistanceMeters = 0.30f;
        private const float TrackingMaximumSpanMeters = 0.35f;
        private const float SafetyMaximumSpanMeters = 0.30f;
        private const float SafetyMinimumConfidence = 0.55f;

        private sealed class TrackState
        {
            public readonly List<float> RawWindow = new List<float>();
            public float[] MedianScratch = Array.Empty<float>();
            public bool HasFilteredDistance;
            public float LastRawDistance;
            public float FilteredDistance;
            public float LastRawSafetyDistance;
            public float FilteredSafetyDistance;
            public bool HasFilteredSafetyDistance;
            public float LastConfidence;
            public float LastSafetyConfidence;
            public int LastRequestedSamples;
            public int LastValidSamples;
            public int LastTrackingSupportCount;
            public int LastSafetySupportCount;
            public string LastClusterSelectionReason = string.Empty;
            public PersonDepthClusterMeasurement LastTrackingCluster;
            public PersonDepthClusterMeasurement LastSafetyCluster;
            public double LastUpdateSeconds;
            public double LastMetricSeconds;
            public double LastSafetySeconds;
            public float LastBoundingBoxArea;
            public int PendingJumpDirection;
            public int PendingJumpCount;
        }

        private readonly Dictionary<int, TrackState> states =
            new Dictionary<int, TrackState>();
        private readonly List<ClusterSample> selectionScratch =
            new List<ClusterSample>(25);
        private readonly List<ClusterCandidate> trackingCandidateScratch =
            new List<ClusterCandidate>(25);
        private readonly List<ClusterCandidate> safetyCandidateScratch =
            new List<ClusterCandidate>(25);
        private readonly List<PersonDepthSample> legacySampleScratch =
            new List<PersonDepthSample>(25);
        private bool[] visitedScratch = new bool[32];
        private int[] queueScratch = new int[32];
        private int[] memberScratch = new int[32];
        private float[] distanceScratch = new float[32];
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

        public ulong LatestTrackingSelectionMask { get; private set; }
        public ulong LatestSafetySelectionMask { get; private set; }
        public PersonDepthClusterMeasurement LatestTrackingCluster
        {
            get;
            private set;
        }
        public PersonDepthClusterMeasurement LatestSafetyCluster
        {
            get;
            private set;
        }

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
            ResetLatestClusters("not_measured");
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
            legacySampleScratch.Clear();
            if (samples != null)
            {
                for (int i = 0; i < samples.Count; i++)
                {
                    float weight = sampleWeights != null
                        && i < sampleWeights.Count
                            ? sampleWeights[i]
                            : 1f;
                    legacySampleScratch.Add(new PersonDepthSample(
                        i,
                        Vector2.zero,
                        Vector2.zero,
                        samples[i],
                        weight,
                        true,
                        true,
                        false,
                        Vector3.zero));
                }
            }

            return UpdateMetric(
                trackId,
                timestampSeconds,
                legacySampleScratch,
                requestedSampleCount,
                boundingBoxArea,
                minimumConfidenceToCommit,
                maximumAcceptedRawDistance,
                maximumAcceptedDispersion);
        }

        public PersonDistanceMeasurement UpdateMetric(
            int trackId,
            double timestampSeconds,
            IReadOnlyList<PersonDepthSample> samples,
            int requestedSampleCount,
            float boundingBoxArea,
            float minimumConfidenceToCommit = 0f,
            float maximumAcceptedRawDistance = float.PositiveInfinity,
            float maximumAcceptedDispersion = float.PositiveInfinity)
        {
            TrackState state = GetOrCreate(trackId);
            EnsureScratchCapacity(samples == null ? 0 : samples.Count);
            float previousTrackingDistance = state.HasFilteredDistance
                ? state.FilteredDistance
                : -1f;
            TrySelectConnectedClustersCore(
                samples,
                minimumDistanceMeters,
                maximumDistanceMeters,
                Math.Min(NeighborDistanceMeters, clusterGapMeters),
                Math.Max(3, minimumClusterSamples),
                previousTrackingDistance,
                out PersonDepthClusterMeasurement trackingCluster,
                out PersonDepthClusterMeasurement safetyCluster,
                selectionScratch,
                trackingCandidateScratch,
                safetyCandidateScratch,
                visitedScratch,
                queueScratch,
                memberScratch,
                distanceScratch);

            LatestTrackingCluster = trackingCluster;
            LatestSafetyCluster = safetyCluster;
            LatestTrackingSelectionMask = trackingCluster.SelectionMask;
            LatestSafetySelectionMask = safetyCluster.SelectionMask;
            if (!trackingCluster.Available && !safetyCluster.Available)
            {
                return GetHeldOrBoundingBoxFallback(
                    trackId,
                    timestampSeconds,
                    boundingBoxArea,
                    CombinedReason(trackingCluster, safetyCluster));
            }

            int acceptedRequestedSamples = Math.Max(
                requestedSampleCount,
                Math.Max(
                    trackingCluster.SupportCount,
                    safetyCluster.SupportCount));
            bool trackingAccepted = trackingCluster.Available;
            string trackingRejectedReason = trackingCluster.SelectionReason;
            if (trackingAccepted
                && trackingCluster.Confidence
                    < Math.Max(0f, minimumConfidenceToCommit))
            {
                trackingAccepted = false;
                trackingRejectedReason = "low_depth_confidence";
            }
            if (trackingAccepted
                && trackingCluster.DistanceMeters
                    > maximumAcceptedRawDistance)
            {
                trackingAccepted = false;
                trackingRejectedReason = "background_depth_suspected";
            }
            if (trackingAccepted
                && trackingCluster.SpanMeters > maximumAcceptedDispersion)
            {
                trackingAccepted = false;
                trackingRejectedReason = "depth_dispersion";
            }

            double trackingElapsed = state.HasFilteredDistance
                ? Math.Max(0.0, timestampSeconds - state.LastUpdateSeconds)
                : 0.0;
            float dynamicJumpThreshold = baseJumpThresholdMeters
                + maximumJumpSpeedMetersPerSecond * (float)trackingElapsed;
            bool bboxGrowing = state.LastBoundingBoxArea > 0f
                && boundingBoxArea > state.LastBoundingBoxArea * 1.08f;
            bool depthMovingAway = trackingCluster.Available
                && state.HasFilteredDistance
                && trackingCluster.DistanceMeters
                    > state.FilteredDistance + 0.05f;
            bool bboxDepthConflict = bboxGrowing && depthMovingAway;
            if (bboxDepthConflict)
            {
                trackingAccepted = false;
                trackingRejectedReason = "bbox_depth_conflict";
            }

            string note = string.Empty;
            if (trackingAccepted)
            {
                state.RawWindow.Add(trackingCluster.DistanceMeters);
                while (state.RawWindow.Count > medianWindowSamples)
                {
                    state.RawWindow.RemoveAt(0);
                }

                float candidate = MedianCopy(state);
                if (state.HasFilteredDistance
                    && Math.Abs(
                        trackingCluster.DistanceMeters
                            - state.FilteredDistance) > dynamicJumpThreshold)
                {
                    int direction = Math.Sign(
                        trackingCluster.DistanceMeters
                            - state.FilteredDistance);
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

                float alpha = state.HasFilteredDistance
                    ? 1f - (float)Math.Exp(
                        -trackingElapsed / smoothingTimeConstantSeconds)
                    : 1f;
                state.FilteredDistance = state.HasFilteredDistance
                    ? Lerp(
                        state.FilteredDistance,
                        candidate,
                        Clamp01(alpha))
                    : candidate;
                state.HasFilteredDistance = true;
                state.LastRawDistance = trackingCluster.DistanceMeters;
                state.LastUpdateSeconds = timestampSeconds;
                state.LastMetricSeconds = timestampSeconds;
                state.LastConfidence = trackingCluster.Confidence;
                state.LastValidSamples = trackingCluster.SupportCount;
                state.LastTrackingSupportCount =
                    trackingCluster.SupportCount;
                state.LastTrackingCluster = trackingCluster;
            }

            bool safetyAccepted = safetyCluster.Available
                && safetyCluster.Confidence >= SafetyMinimumConfidence;
            if (safetyAccepted)
            {
                double safetyElapsed = state.HasFilteredSafetyDistance
                    ? Math.Max(0.0, timestampSeconds - state.LastSafetySeconds)
                    : 0.0;
                float safetyTimeConstant = state.HasFilteredSafetyDistance
                    && safetyCluster.DistanceMeters
                        < state.FilteredSafetyDistance
                            ? 0.08f
                            : smoothingTimeConstantSeconds;
                float safetyAlpha = state.HasFilteredSafetyDistance
                    ? 1f - (float)Math.Exp(
                        -safetyElapsed
                            / Math.Max(0.001f, safetyTimeConstant))
                    : 1f;
                state.FilteredSafetyDistance =
                    state.HasFilteredSafetyDistance
                        ? Lerp(
                            state.FilteredSafetyDistance,
                            safetyCluster.DistanceMeters,
                            Clamp01(safetyAlpha))
                        : safetyCluster.DistanceMeters;
                state.HasFilteredSafetyDistance = true;
                state.LastRawSafetyDistance = safetyCluster.DistanceMeters;
                state.LastSafetyConfidence = safetyCluster.Confidence;
                state.LastSafetySupportCount = safetyCluster.SupportCount;
                state.LastSafetySeconds = timestampSeconds;
                state.LastSafetyCluster = safetyCluster;
            }

            state.LastRequestedSamples = acceptedRequestedSamples;
            state.LastClusterSelectionReason = CombinedReason(
                trackingCluster,
                safetyCluster);
            state.LastBoundingBoxArea = boundingBoxArea;

            float outputRawDistance = trackingCluster.Available
                ? trackingCluster.DistanceMeters
                : state.LastRawDistance;
            float outputFilteredDistance = state.HasFilteredDistance
                ? state.FilteredDistance
                : 0f;
            float outputConfidence = trackingCluster.Available
                ? trackingCluster.Confidence
                : 0f;
            float outputSafetyRaw = safetyAccepted
                ? safetyCluster.DistanceMeters
                : state.HasFilteredSafetyDistance
                    ? state.LastRawSafetyDistance
                    : 0f;
            float outputSafetyDistance = state.HasFilteredSafetyDistance
                ? state.FilteredSafetyDistance
                : 0f;
            float outputSafetyConfidence = safetyAccepted
                ? safetyCluster.Confidence
                : 0f;
            int outputSafetySupport = safetyAccepted
                ? safetyCluster.SupportCount
                : 0;
            string failureReason = !string.IsNullOrEmpty(note)
                ? note
                : trackingAccepted
                    ? string.Empty
                    : trackingRejectedReason;

            return new PersonDistanceMeasurement(
                trackId,
                timestampSeconds,
                PersonDistanceSource.EnvironmentDepth,
                true,
                outputRawDistance,
                outputFilteredDistance,
                outputConfidence,
                acceptedRequestedSamples,
                trackingCluster.SupportCount,
                0f,
                boundingBoxArea,
                failureReason,
                trackingCluster.SpanMeters,
                bboxDepthConflict,
                false,
                default,
                trackingAccepted,
                trackingAccepted ? string.Empty : trackingRejectedReason,
                false,
                default,
                default,
                PersonPresentationGeometrySource.Unavailable,
                0f,
                outputSafetyRaw,
                outputSafetyDistance,
                outputSafetyConfidence,
                trackingCluster.SupportCount,
                outputSafetySupport,
                state.LastClusterSelectionReason,
                trackingCluster,
                safetyCluster);
        }

        public PersonDistanceMeasurement GetHeldOrBoundingBoxFallback(
            int trackId,
            double timestampSeconds,
            float boundingBoxArea,
            string failureReason)
        {
            if (states.TryGetValue(trackId, out TrackState state))
            {
                float trackingAge = (float)Math.Max(
                    0.0,
                    timestampSeconds - state.LastMetricSeconds);
                float safetyAge = (float)Math.Max(
                    0.0,
                    timestampSeconds - state.LastSafetySeconds);
                bool hasTrackingHold = state.HasFilteredDistance
                    && trackingAge <= metricHoldSeconds;
                bool hasSafetyHold = state.HasFilteredSafetyDistance
                    && safetyAge <= metricHoldSeconds;
                if (hasTrackingHold || hasSafetyHold)
                {
                    float trackingConfidence = hasTrackingHold
                        ? DecayedConfidence(
                            state.LastConfidence,
                            trackingAge,
                            metricHoldSeconds)
                        : 0f;
                    float safetyConfidence = hasSafetyHold
                        ? DecayedConfidence(
                            state.LastSafetyConfidence,
                            safetyAge,
                            metricHoldSeconds)
                        : 0f;
                    return new PersonDistanceMeasurement(
                        trackId,
                        timestampSeconds,
                        PersonDistanceSource.EnvironmentDepth,
                        true,
                        hasTrackingHold ? state.LastRawDistance : 0f,
                        hasTrackingHold ? state.FilteredDistance : 0f,
                        trackingConfidence,
                        state.LastRequestedSamples,
                        hasTrackingHold ? state.LastValidSamples : 0,
                        Math.Min(trackingAge, safetyAge),
                        boundingBoxArea,
                        failureReason,
                        hasTrackingHold
                            ? state.LastTrackingCluster.SpanMeters
                            : 0f,
                        false,
                        false,
                        default,
                        hasTrackingHold
                            && trackingConfidence
                                >= PersonDepthReliability.MinimumConfidence,
                        hasTrackingHold
                            ? string.Empty
                            : failureReason,
                        false,
                        default,
                        default,
                        PersonPresentationGeometrySource.Unavailable,
                        0f,
                        hasSafetyHold
                            ? state.LastRawSafetyDistance
                            : 0f,
                        hasSafetyHold
                            ? state.FilteredSafetyDistance
                            : 0f,
                        safetyConfidence,
                        hasTrackingHold
                            ? state.LastTrackingSupportCount
                            : 0,
                        hasSafetyHold ? state.LastSafetySupportCount : 0,
                        state.LastClusterSelectionReason,
                        hasTrackingHold
                            ? state.LastTrackingCluster
                            : PersonDepthClusterMeasurement.Unavailable(
                                PersonDepthClusterKind.Tracking,
                                failureReason),
                        hasSafetyHold
                            ? state.LastSafetyCluster
                            : PersonDepthClusterMeasurement.Unavailable(
                                PersonDepthClusterKind.Safety,
                                failureReason));
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
            ResetLatestClusters("reset");
        }

        public static bool TrySelectConnectedClusters(
            IReadOnlyList<PersonDepthSample> samples,
            float minimumDistanceMeters,
            float maximumDistanceMeters,
            float previousTrackingDistanceMeters,
            out PersonDepthClusterMeasurement trackingCluster,
            out PersonDepthClusterMeasurement safetyCluster)
        {
            int capacity = Math.Max(1, samples == null ? 0 : samples.Count);
            return TrySelectConnectedClustersCore(
                samples,
                minimumDistanceMeters,
                maximumDistanceMeters,
                NeighborDistanceMeters,
                3,
                previousTrackingDistanceMeters,
                out trackingCluster,
                out safetyCluster,
                new List<ClusterSample>(capacity),
                new List<ClusterCandidate>(capacity),
                new List<ClusterCandidate>(capacity),
                new bool[capacity],
                new int[capacity],
                new int[capacity],
                new float[capacity]);
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
            medianDistance = 0f;
            selectedSampleCount = 0;
            dispersion = 0f;
            if (samples == null || samples.Count == 0)
            {
                return false;
            }

            var valid = new List<WeightedSample>(samples.Count);
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
                ? (valid[middle - 1].Distance
                    + valid[middle].Distance) * 0.5f
                : valid[middle].Distance;
            dispersion = valid[bestEnd - 1].Distance
                - valid[bestStart].Distance;
            return true;
        }

        public static bool TrySelectTorsoSupportedCluster(
            IReadOnlyList<PersonDepthSample> samples,
            float minimumDistanceMeters,
            float maximumDistanceMeters,
            float clusterGapMeters,
            int minimumClusterSamples,
            out float trackingDistanceMeters,
            out float safetyDistanceMeters,
            out int selectedSampleCount,
            out int torsoSupportCount,
            out int safetySupportCount,
            out float dispersion,
            out string selectionReason)
        {
            TrySelectConnectedClusters(
                samples,
                minimumDistanceMeters,
                maximumDistanceMeters,
                -1f,
                out PersonDepthClusterMeasurement tracking,
                out PersonDepthClusterMeasurement safety);
            trackingDistanceMeters = tracking.DistanceMeters;
            safetyDistanceMeters = safety.Available
                ? safety.DistanceMeters
                : tracking.DistanceMeters;
            selectedSampleCount = tracking.SupportCount;
            torsoSupportCount = tracking.SupportCount;
            safetySupportCount = safety.SupportCount;
            dispersion = tracking.SpanMeters;
            selectionReason = CombinedReason(tracking, safety);
            return tracking.Available;
        }

        private static bool TrySelectConnectedClustersCore(
            IReadOnlyList<PersonDepthSample> samples,
            float minimumDistanceMeters,
            float maximumDistanceMeters,
            float neighborDistanceMeters,
            int minimumTrackingSamples,
            float previousTrackingDistanceMeters,
            out PersonDepthClusterMeasurement trackingCluster,
            out PersonDepthClusterMeasurement safetyCluster,
            List<ClusterSample> valid,
            List<ClusterCandidate> trackingCandidates,
            List<ClusterCandidate> safetyCandidates,
            bool[] visited,
            int[] queue,
            int[] members,
            float[] distances)
        {
            valid.Clear();
            if (samples != null)
            {
                int count = Math.Min(samples.Count, 64);
                for (int i = 0; i < count; i++)
                {
                    PersonDepthSample sample = samples[i];
                    if (!IsFinite(sample.DistanceMeters)
                        || sample.DistanceMeters < minimumDistanceMeters
                        || sample.DistanceMeters > maximumDistanceMeters)
                    {
                        continue;
                    }

                    valid.Add(new ClusterSample(
                        sample.SampleIndex >= 0 ? sample.SampleIndex : i,
                        sample.BoxRelativePosition,
                        sample.DistanceMeters,
                        sample.Weight));
                }
            }

            if (valid.Count == 0)
            {
                trackingCluster = PersonDepthClusterMeasurement.Unavailable(
                    PersonDepthClusterKind.Tracking,
                    "insufficient_tracking_cluster");
                safetyCluster = PersonDepthClusterMeasurement.Unavailable(
                    PersonDepthClusterKind.Safety,
                    "insufficient_safety_cluster");
                return false;
            }

            valid.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            BuildCandidates(
                valid,
                TrackingMaximumSpanMeters,
                neighborDistanceMeters,
                Math.Max(3, minimumTrackingSamples),
                trackingCandidates,
                visited,
                queue,
                members,
                distances);
            BuildCandidates(
                valid,
                SafetyMaximumSpanMeters,
                neighborDistanceMeters,
                2,
                safetyCandidates,
                visited,
                queue,
                members,
                distances);

            trackingCluster = SelectTrackingCluster(
                trackingCandidates,
                previousTrackingDistanceMeters);
            safetyCluster = SelectSafetyCluster(safetyCandidates);
            return trackingCluster.Available || safetyCluster.Available;
        }

        private static void BuildCandidates(
            List<ClusterSample> valid,
            float maximumSpanMeters,
            float neighborDistanceMeters,
            int minimumSupport,
            List<ClusterCandidate> output,
            bool[] visited,
            int[] queue,
            int[] members,
            float[] distances)
        {
            output.Clear();
            int validCount = valid.Count;
            for (int seed = 0; seed < validCount; seed++)
            {
                Array.Clear(visited, 0, validCount);
                float minimumDistance = valid[seed].Distance;
                int head = 0;
                int tail = 1;
                int memberCount = 0;
                queue[0] = seed;
                visited[seed] = true;
                while (head < tail)
                {
                    int current = queue[head++];
                    members[memberCount++] = current;
                    ClusterSample currentSample = valid[current];
                    for (int candidate = 0;
                        candidate < validCount;
                        candidate++)
                    {
                        if (visited[candidate])
                        {
                            continue;
                        }

                        ClusterSample next = valid[candidate];
                        if (next.Distance + 0.0001f < minimumDistance
                            || next.Distance - minimumDistance
                                > maximumSpanMeters
                            || Math.Abs(
                                next.Distance - currentSample.Distance)
                                > neighborDistanceMeters
                            || Math.Abs(
                                next.BoxPosition.x
                                    - currentSample.BoxPosition.x)
                                > SpatialNeighborX
                            || Math.Abs(
                                next.BoxPosition.y
                                    - currentSample.BoxPosition.y)
                                > SpatialNeighborY)
                        {
                            continue;
                        }

                        visited[candidate] = true;
                        queue[tail++] = candidate;
                    }
                }

                if (memberCount < minimumSupport)
                {
                    continue;
                }

                ulong internalMask = 0UL;
                ulong selectionMask = 0UL;
                float maximumDistance = minimumDistance;
                int minimumSampleIndex = int.MaxValue;
                for (int i = 0; i < memberCount; i++)
                {
                    int validIndex = members[i];
                    ClusterSample sample = valid[validIndex];
                    internalMask |= 1UL << validIndex;
                    if (sample.SampleIndex >= 0 && sample.SampleIndex < 64)
                    {
                        selectionMask |= 1UL << sample.SampleIndex;
                        minimumSampleIndex = Math.Min(
                            minimumSampleIndex,
                            sample.SampleIndex);
                    }
                    distances[i] = sample.Distance;
                    maximumDistance = Math.Max(
                        maximumDistance,
                        sample.Distance);
                }

                bool duplicate = false;
                for (int i = 0; i < output.Count; i++)
                {
                    if (output[i].InternalMask == internalMask)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate)
                {
                    continue;
                }

                Array.Sort(distances, 0, memberCount);
                float median = Quantile(distances, memberCount, 0.50f);
                float safetyPercentile = Quantile(
                    distances,
                    memberCount,
                    0.20f);
                output.Add(new ClusterCandidate(
                    minimumSampleIndex == int.MaxValue
                        ? seed + 1
                        : minimumSampleIndex + 1,
                    internalMask,
                    selectionMask,
                    memberCount,
                    minimumDistance,
                    maximumDistance,
                    median,
                    safetyPercentile));
            }
        }

        private static PersonDepthClusterMeasurement SelectTrackingCluster(
            List<ClusterCandidate> candidates,
            float previousDistanceMeters)
        {
            int bestIndex = -1;
            float bestScore = float.MinValue;
            float bestDistance = float.PositiveInfinity;
            float bestTemporal = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                ClusterCandidate candidate = candidates[i];
                float support = Math.Min(1f, candidate.SupportCount / 6f);
                float compact = (float)Math.Exp(
                    -candidate.SpanMeters / TrackingMaximumSpanMeters);
                float temporal = previousDistanceMeters >= 0f
                    ? (float)Math.Exp(
                        -Math.Abs(
                            candidate.MedianDistanceMeters
                                - previousDistanceMeters) / 0.50f)
                    : 0.50f;
                float score = 0.45f * support
                    + 0.25f * compact
                    + 0.30f * temporal;
                bool better = score > bestScore + 0.02f;
                bool nearTieCloser = Math.Abs(score - bestScore) <= 0.02f
                    && candidate.MedianDistanceMeters < bestDistance;
                if (bestIndex < 0 || better || nearTieCloser)
                {
                    bestIndex = i;
                    bestScore = score;
                    bestDistance = candidate.MedianDistanceMeters;
                    bestTemporal = temporal;
                }
            }

            if (bestIndex < 0)
            {
                return PersonDepthClusterMeasurement.Unavailable(
                    PersonDepthClusterKind.Tracking,
                    "insufficient_tracking_cluster");
            }

            ClusterCandidate best = candidates[bestIndex];
            return new PersonDepthClusterMeasurement(
                PersonDepthClusterKind.Tracking,
                best.ClusterId,
                true,
                best.MedianDistanceMeters,
                bestScore,
                best.SupportCount,
                best.SpanMeters,
                best.SelectionMask,
                false,
                Vector3.zero,
                bestTemporal,
                "connected_temporal_tracking");
        }

        private static PersonDepthClusterMeasurement SelectSafetyCluster(
            List<ClusterCandidate> candidates)
        {
            int bestIndex = -1;
            float bestDistance = float.PositiveInfinity;
            float bestConfidence = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                ClusterCandidate candidate = candidates[i];
                float confidence = 0.65f
                    * Math.Min(1f, candidate.SupportCount / 3f)
                    + 0.35f * (float)Math.Exp(
                        -candidate.SpanMeters / SafetyMaximumSpanMeters);
                if (confidence < SafetyMinimumConfidence)
                {
                    continue;
                }

                bool closer = candidate.SafetyDistanceMeters
                    < bestDistance - 0.001f;
                bool sameDistanceBetter = Math.Abs(
                        candidate.SafetyDistanceMeters - bestDistance)
                        <= 0.001f
                    && confidence > bestConfidence;
                if (bestIndex < 0 || closer || sameDistanceBetter)
                {
                    bestIndex = i;
                    bestDistance = candidate.SafetyDistanceMeters;
                    bestConfidence = confidence;
                }
            }

            if (bestIndex < 0)
            {
                return PersonDepthClusterMeasurement.Unavailable(
                    PersonDepthClusterKind.Safety,
                    "insufficient_safety_cluster");
            }

            ClusterCandidate best = candidates[bestIndex];
            return new PersonDepthClusterMeasurement(
                PersonDepthClusterKind.Safety,
                best.ClusterId,
                true,
                best.SafetyDistanceMeters,
                bestConfidence,
                best.SupportCount,
                best.SpanMeters,
                best.SelectionMask,
                false,
                Vector3.zero,
                0f,
                "connected_foreground_safety");
        }

        private void EnsureScratchCapacity(int requested)
        {
            int size = Math.Max(1, Math.Min(64, requested));
            if (visitedScratch.Length >= size)
            {
                return;
            }

            int capacity = Math.Max(size, visitedScratch.Length * 2);
            visitedScratch = new bool[capacity];
            queueScratch = new int[capacity];
            memberScratch = new int[capacity];
            distanceScratch = new float[capacity];
        }

        private void ResetLatestClusters(string reason)
        {
            LatestTrackingSelectionMask = 0UL;
            LatestSafetySelectionMask = 0UL;
            LatestTrackingCluster =
                PersonDepthClusterMeasurement.Unavailable(
                    PersonDepthClusterKind.Tracking,
                    reason);
            LatestSafetyCluster =
                PersonDepthClusterMeasurement.Unavailable(
                    PersonDepthClusterKind.Safety,
                    reason);
        }

        private TrackState GetOrCreate(int trackId)
        {
            if (!states.TryGetValue(trackId, out TrackState state))
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

        private static float Quantile(
            float[] sorted,
            int count,
            float percentile)
        {
            float index = Clamp01(percentile) * Math.Max(0, count - 1);
            int lower = (int)Math.Floor(index);
            int upper = Math.Min(count - 1, lower + 1);
            return Lerp(sorted[lower], sorted[upper], index - lower);
        }

        private static float DecayedConfidence(
            float confidence,
            float age,
            float holdSeconds)
        {
            if (holdSeconds <= 0f)
            {
                return 0f;
            }
            return Clamp01(confidence)
                * Math.Max(0f, 1f - age / holdSeconds);
        }

        private static string CombinedReason(
            PersonDepthClusterMeasurement tracking,
            PersonDepthClusterMeasurement safety)
        {
            if (tracking.Available && safety.Available)
            {
                return "connected_tracking_and_safety";
            }
            if (tracking.Available)
            {
                return tracking.SelectionReason;
            }
            if (safety.Available)
            {
                return "safety_only_connected_foreground";
            }
            return !string.IsNullOrEmpty(tracking.SelectionReason)
                ? tracking.SelectionReason
                : safety.SelectionReason;
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

        private readonly struct ClusterSample
        {
            public readonly int SampleIndex;
            public readonly Vector2 BoxPosition;
            public readonly float Distance;
            public readonly float Weight;

            public ClusterSample(
                int sampleIndex,
                Vector2 boxPosition,
                float distance,
                float weight)
            {
                SampleIndex = sampleIndex;
                BoxPosition = boxPosition;
                Distance = distance;
                Weight = Math.Max(0.05f, weight);
            }
        }

        private readonly struct ClusterCandidate
        {
            public readonly int ClusterId;
            public readonly ulong InternalMask;
            public readonly ulong SelectionMask;
            public readonly int SupportCount;
            public readonly float MinimumDistanceMeters;
            public readonly float MaximumDistanceMeters;
            public readonly float MedianDistanceMeters;
            public readonly float SafetyDistanceMeters;

            public ClusterCandidate(
                int clusterId,
                ulong internalMask,
                ulong selectionMask,
                int supportCount,
                float minimumDistanceMeters,
                float maximumDistanceMeters,
                float medianDistanceMeters,
                float safetyDistanceMeters)
            {
                ClusterId = clusterId;
                InternalMask = internalMask;
                SelectionMask = selectionMask;
                SupportCount = supportCount;
                MinimumDistanceMeters = minimumDistanceMeters;
                MaximumDistanceMeters = maximumDistanceMeters;
                MedianDistanceMeters = medianDistanceMeters;
                SafetyDistanceMeters = safetyDistanceMeters;
            }

            public float SpanMeters
            {
                get
                {
                    return Math.Max(
                        0f,
                        MaximumDistanceMeters - MinimumDistanceMeters);
                }
            }
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
    }
}
