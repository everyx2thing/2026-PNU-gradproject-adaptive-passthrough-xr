using System;
using System.Collections.Generic;

namespace TeamVR.AdaptivePassthrough
{
    public sealed class HistoryMotionEstimator
    {
        private struct Sample
        {
            public double TimestampSeconds;
            public float Scale;
            public float CenterDistance;
        }

        private readonly Dictionary<int, List<Sample>> histories =
            new Dictionary<int, List<Sample>>();
        private readonly Dictionary<int, float> smoothedScaleRates =
            new Dictionary<int, float>();
        private readonly double historyWindowSeconds;
        private readonly int minimumSamples;
        private readonly double minimumObservationSeconds;
        private readonly float approachThreshold;
        private readonly float recedeThreshold;
        private readonly float smoothingFactor;

        public HistoryMotionEstimator(
            double historyWindowSeconds = 1.5,
            int minimumSamples = 4,
            double minimumObservationSeconds = 0.25,
            float approachThreshold = 0.04f,
            float recedeThreshold = -0.04f,
            float smoothingFactor = 0.45f)
        {
            this.historyWindowSeconds = historyWindowSeconds;
            this.minimumSamples = minimumSamples;
            this.minimumObservationSeconds = minimumObservationSeconds;
            this.approachThreshold = approachThreshold;
            this.recedeThreshold = recedeThreshold;
            this.smoothingFactor = Math.Max(0f, Math.Min(0.95f, smoothingFactor));
        }

        public MotionEstimate Estimate(double timestampSeconds, TrackedDynamicObject tracked)
        {
            List<Sample> history;
            if (!histories.TryGetValue(tracked.TrackId, out history))
            {
                history = new List<Sample>();
                histories.Add(tracked.TrackId, history);
            }

            NormalizedBoundingBox box = tracked.Detection.boundingBox;
            float scale = (float)Math.Sqrt(Math.Max(0.000001f, box.Area));
            float dx = box.centerX - 0.5f;
            float dy = box.centerY - 0.5f;
            float centerDistance = (float)Math.Sqrt(dx * dx + dy * dy);

            history.Add(new Sample
            {
                TimestampSeconds = timestampSeconds,
                Scale = scale,
                CenterDistance = centerDistance
            });

            double oldestAllowed = timestampSeconds - historyWindowSeconds;
            while (history.Count > 0 && history[0].TimestampSeconds < oldestAllowed)
            {
                history.RemoveAt(0);
            }

            double observationSeconds = history.Count > 1
                ? history[history.Count - 1].TimestampSeconds - history[0].TimestampSeconds
                : 0.0;

            if (history.Count < minimumSamples || observationSeconds < minimumObservationSeconds)
            {
                return new MotionEstimate(
                    DynamicMotionState.Unknown,
                    0f,
                    null,
                    0f,
                    history.Count,
                    observationSeconds,
                    Reliability(history.Count, observationSeconds));
            }

            var scaleRates = new List<float>(history.Count - 1);
            var centerApproachRates = new List<float>(history.Count - 1);
            for (int i = 1; i < history.Count; i++)
            {
                Sample previous = history[i - 1];
                Sample current = history[i];
                double elapsed = current.TimestampSeconds - previous.TimestampSeconds;
                if (elapsed <= 0.000001)
                {
                    continue;
                }

                float rate = (float)(Math.Log(current.Scale / previous.Scale) / elapsed);
                if (!float.IsNaN(rate) && !float.IsInfinity(rate))
                {
                    scaleRates.Add(rate);
                }

                float centerRate = (previous.CenterDistance - current.CenterDistance) / (float)elapsed;
                if (!float.IsNaN(centerRate) && !float.IsInfinity(centerRate))
                {
                    centerApproachRates.Add(centerRate);
                }
            }

            float medianScaleRate = Median(scaleRates);
            float previousRate;
            float scaleRate = smoothedScaleRates.TryGetValue(tracked.TrackId, out previousRate)
                ? smoothingFactor * previousRate + (1f - smoothingFactor) * medianScaleRate
                : medianScaleRate;
            smoothedScaleRates[tracked.TrackId] = scaleRate;

            DynamicMotionState state;
            if (scaleRate >= approachThreshold)
            {
                state = DynamicMotionState.Approaching;
            }
            else if (scaleRate <= recedeThreshold)
            {
                state = DynamicMotionState.Receding;
            }
            else
            {
                state = DynamicMotionState.Steady;
            }

            float? ttcSecondsApprox = state == DynamicMotionState.Approaching && scaleRate > 0f
                ? (float?)(1f / scaleRate)
                : null;

            return new MotionEstimate(
                state,
                scaleRate,
                ttcSecondsApprox,
                Median(centerApproachRates),
                history.Count,
                observationSeconds,
                Reliability(history.Count, observationSeconds));
        }

        public void Reset()
        {
            histories.Clear();
            smoothedScaleRates.Clear();
        }

        public void PruneExcept(IEnumerable<int> liveTrackIds)
        {
            var liveIds = liveTrackIds == null
                ? new HashSet<int>()
                : new HashSet<int>(liveTrackIds);
            var expiredIds = new List<int>();
            foreach (int trackId in histories.Keys)
            {
                if (!liveIds.Contains(trackId))
                {
                    expiredIds.Add(trackId);
                }
            }

            for (int i = 0; i < expiredIds.Count; i++)
            {
                histories.Remove(expiredIds[i]);
                smoothedScaleRates.Remove(expiredIds[i]);
            }
        }

        private float Reliability(int sampleCount, double observationSeconds)
        {
            float sampleReliability = Math.Min(1f, sampleCount / 6f);
            float timeReliability = (float)Math.Min(1.0, observationSeconds / 0.75);
            return sampleReliability * timeReliability;
        }

        private static float Median(List<float> values)
        {
            if (values.Count == 0)
            {
                return 0f;
            }

            values.Sort();
            int middle = values.Count / 2;
            return values.Count % 2 == 0
                ? (values[middle - 1] + values[middle]) * 0.5f
                : values[middle];
        }
    }
}
