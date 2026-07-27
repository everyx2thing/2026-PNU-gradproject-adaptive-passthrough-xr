using System;
using System.Collections.Generic;
using System.Linq;

namespace TeamVR.AdaptivePassthrough
{
    public sealed class SimpleObjectTracker
    {
        private sealed class TrackState
        {
            public int Id;
            public DynamicObjectDetection Detection;
            public double TimestampSeconds;
            public int MissedFrames;
            public int TotalHits;
            public int ConsecutiveHits;
            public int ConsecutiveHighConfidenceHits;
            public float ConfidenceSum;
            public TrackLifecycle Lifecycle;
            public readonly Queue<bool> RecentObservations = new Queue<bool>();
        }

        private readonly Dictionary<int, TrackState> tracks = new Dictionary<int, TrackState>();
        private readonly float minimumIou;
        private readonly float maximumCenterDistance;
        private readonly int maximumMissedFrames;
        private readonly int confirmationHits;
        private readonly int confirmationWindowFrames;
        private readonly int fastConfirmationHits;
        private readonly float fastConfirmationConfidence;
        private readonly double maximumUnobservedSeconds;
        private int nextTrackId = 1;

        public IEnumerable<int> LiveTrackIds
        {
            get { return tracks.Keys; }
        }

        public IEnumerable<int> ConfirmedTrackIds
        {
            get
            {
                return tracks.Values
                    .Where(state =>
                        state.Lifecycle == TrackLifecycle.Confirmed
                        || state.Lifecycle == TrackLifecycle.Lost)
                    .Select(state => state.Id);
            }
        }

        public int ConfirmedTrackCount
        {
            get
            {
                int count = 0;
                foreach (TrackState state in tracks.Values)
                {
                    if (state.Lifecycle == TrackLifecycle.Confirmed
                        || state.Lifecycle == TrackLifecycle.Lost)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public SimpleObjectTracker(
            float minimumIou = 0.2f,
            float maximumCenterDistance = 0.25f,
            int maximumMissedFrames = 5,
            int confirmationHits = 3,
            int confirmationWindowFrames = 5,
            int fastConfirmationHits = 2,
            float fastConfirmationConfidence = 0.85f,
            double maximumUnobservedSeconds = 0.5)
        {
            this.minimumIou = minimumIou;
            this.maximumCenterDistance = maximumCenterDistance;
            this.maximumMissedFrames = maximumMissedFrames;
            this.confirmationHits = Math.Max(1, confirmationHits);
            this.confirmationWindowFrames = Math.Max(
                this.confirmationHits,
                confirmationWindowFrames);
            this.fastConfirmationHits = Math.Max(1, fastConfirmationHits);
            this.fastConfirmationConfidence = Math.Max(
                0f,
                Math.Min(1f, fastConfirmationConfidence));
            this.maximumUnobservedSeconds = Math.Max(
                0.05,
                maximumUnobservedSeconds);
        }

        public IReadOnlyList<TrackedDynamicObject> Update(
            double timestampSeconds,
            IReadOnlyList<DynamicObjectDetection> detections)
        {
            if (detections == null)
            {
                detections = Array.Empty<DynamicObjectDetection>();
            }

            var assignedTrackIds = new HashSet<int>();
            var matched = new List<(TrackState State, float GrowthRate)>(
                detections.Count);

            for (int detectionIndex = 0; detectionIndex < detections.Count; detectionIndex++)
            {
                DynamicObjectDetection detection = detections[detectionIndex];
                TrackState bestTrack = null;
                float bestCost = float.MaxValue;

                foreach (TrackState candidate in tracks.Values)
                {
                    if (assignedTrackIds.Contains(candidate.Id))
                    {
                        continue;
                    }

                    float iou = IntersectionOverUnion(
                        candidate.Detection.boundingBox,
                        detection.boundingBox);
                    float centerDistance = CenterDistance(
                        candidate.Detection.boundingBox,
                        detection.boundingBox);

                    if (iou < minimumIou && centerDistance > maximumCenterDistance)
                    {
                        continue;
                    }

                    float labelPenalty = string.Equals(
                        candidate.Detection.label,
                        detection.label,
                        StringComparison.OrdinalIgnoreCase) ? 0f : 1f;
                    float cost = (1f - iou) + centerDistance * 0.35f + labelPenalty;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestTrack = candidate;
                    }
                }

                if (bestTrack == null)
                {
                    bestTrack = new TrackState
                    {
                        Id = nextTrackId++,
                        Detection = detection,
                        TimestampSeconds = timestampSeconds,
                        MissedFrames = 0,
                        Lifecycle = TrackLifecycle.Tentative
                    };
                    tracks.Add(bestTrack.Id, bestTrack);
                    assignedTrackIds.Add(bestTrack.Id);
                    matched.Add((bestTrack, 0f));
                    continue;
                }

                double elapsedSeconds = Math.Max(0.0001, timestampSeconds - bestTrack.TimestampSeconds);
                float previousArea = Math.Max(0.000001f, bestTrack.Detection.boundingBox.Area);
                float growthRate = (detection.boundingBox.Area - previousArea)
                    / previousArea
                    / (float)elapsedSeconds;

                bestTrack.Detection = detection;
                bestTrack.TimestampSeconds = timestampSeconds;
                bestTrack.MissedFrames = 0;
                assignedTrackIds.Add(bestTrack.Id);
                matched.Add((bestTrack, growthRate));
            }

            foreach (TrackState state in tracks.Values)
            {
                bool observed = assignedTrackIds.Contains(state.Id);
                RecordObservation(state, observed);
                if (observed)
                {
                    state.MissedFrames = 0;
                    if (state.Lifecycle == TrackLifecycle.Lost)
                    {
                        state.Lifecycle = TrackLifecycle.Confirmed;
                    }
                    else if (state.Lifecycle == TrackLifecycle.Tentative
                        && ShouldConfirm(state))
                    {
                        state.Lifecycle = TrackLifecycle.Confirmed;
                    }
                }
                else
                {
                    state.MissedFrames++;
                    if (state.Lifecycle == TrackLifecycle.Confirmed)
                    {
                        state.Lifecycle = TrackLifecycle.Lost;
                    }
                }
            }

            var expiredTrackIds = new List<int>();
            foreach (TrackState state in tracks.Values)
            {
                double unobservedSeconds = Math.Max(
                    0.0,
                    timestampSeconds - state.TimestampSeconds);
                if (state.MissedFrames > maximumMissedFrames
                    || unobservedSeconds > maximumUnobservedSeconds)
                {
                    expiredTrackIds.Add(state.Id);
                }
            }

            for (int i = 0; i < expiredTrackIds.Count; i++)
            {
                tracks.Remove(expiredTrackIds[i]);
            }

            var output = new List<TrackedDynamicObject>(matched.Count);
            for (int i = 0; i < matched.Count; i++)
            {
                TrackState state = matched[i].State;
                if (!tracks.ContainsKey(state.Id))
                {
                    continue;
                }

                output.Add(new TrackedDynamicObject(
                    state.Id,
                    state.Detection,
                    matched[i].GrowthRate,
                    state.Lifecycle,
                    true,
                    state.MissedFrames));
            }

            return output;
        }

        public void Reset()
        {
            tracks.Clear();
            nextTrackId = 1;
        }

        private void RecordObservation(TrackState state, bool observed)
        {
            state.RecentObservations.Enqueue(observed);
            while (state.RecentObservations.Count > confirmationWindowFrames)
            {
                state.RecentObservations.Dequeue();
            }

            if (!observed)
            {
                state.ConsecutiveHits = 0;
                state.ConsecutiveHighConfidenceHits = 0;
                return;
            }

            state.TotalHits++;
            state.ConsecutiveHits++;
            state.ConfidenceSum += state.Detection.confidence;
            if (state.Detection.confidence >= fastConfirmationConfidence)
            {
                state.ConsecutiveHighConfidenceHits++;
            }
            else
            {
                state.ConsecutiveHighConfidenceHits = 0;
            }
        }

        private bool ShouldConfirm(TrackState state)
        {
            int recentHits = 0;
            foreach (bool observed in state.RecentObservations)
            {
                if (observed)
                {
                    recentHits++;
                }
            }

            return recentHits >= confirmationHits
                || state.ConsecutiveHighConfidenceHits >= fastConfirmationHits;
        }

        public static float IntersectionOverUnion(
            NormalizedBoundingBox a,
            NormalizedBoundingBox b)
        {
            float left = Math.Max(a.Left, b.Left);
            float top = Math.Max(a.Top, b.Top);
            float right = Math.Min(a.Right, b.Right);
            float bottom = Math.Min(a.Bottom, b.Bottom);
            float intersection = Math.Max(0f, right - left) * Math.Max(0f, bottom - top);
            float union = a.Area + b.Area - intersection;
            return union <= 0f ? 0f : intersection / union;
        }

        private static float CenterDistance(
            NormalizedBoundingBox a,
            NormalizedBoundingBox b)
        {
            float dx = a.centerX - b.centerX;
            float dy = a.centerY - b.centerY;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
