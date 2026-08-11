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
            public float CenterVelocityX;
            public float CenterVelocityY;
            public float LastGrowthRate;
            public bool ObservedThisUpdate;
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
        private readonly float newTrackConfidence;
        private readonly float maximumSizeRatio;
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
            float minimumIou = 0.15f,
            float maximumCenterDistance = 0.22f,
            int maximumMissedFrames = 8,
            int confirmationHits = 3,
            int confirmationWindowFrames = 5,
            int fastConfirmationHits = 2,
            float fastConfirmationConfidence = 0.85f,
            double maximumUnobservedSeconds = 0.75,
            float newTrackConfidence = 0.55f,
            float maximumSizeRatio = 2.50f)
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
            this.newTrackConfidence = Math.Max(
                0f,
                Math.Min(1f, newTrackConfidence));
            this.maximumSizeRatio = Math.Max(1f, maximumSizeRatio);
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
            foreach (TrackState state in tracks.Values)
            {
                state.ObservedThisUpdate = false;
            }

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

                    if (detection.confidence < newTrackConfidence
                        && candidate.Lifecycle == TrackLifecycle.Tentative)
                    {
                        continue;
                    }

                    double predictionSeconds = Math.Max(
                        0.0,
                        timestampSeconds - candidate.TimestampSeconds);
                    NormalizedBoundingBox predicted = PredictedBox(
                        candidate,
                        predictionSeconds);
                    float iou = IntersectionOverUnion(
                        predicted,
                        detection.boundingBox);
                    float centerDistance = CenterDistance(
                        predicted,
                        detection.boundingBox);
                    float sizeRatio = SizeRatio(
                        candidate.Detection.boundingBox,
                        detection.boundingBox);

                    if ((iou < minimumIou
                            && centerDistance > maximumCenterDistance)
                        || sizeRatio > maximumSizeRatio)
                    {
                        continue;
                    }

                    float labelPenalty = string.Equals(
                        candidate.Detection.label,
                        detection.label,
                        StringComparison.OrdinalIgnoreCase) ? 0f : 1f;
                    float lifecyclePenalty =
                        candidate.Lifecycle == TrackLifecycle.Tentative
                            ? 0.25f
                            : 0f;
                    float sizePenalty = (float)Math.Abs(
                        Math.Log(Math.Max(0.0001f, sizeRatio)));
                    float cost =
                        (1f - iou)
                        + centerDistance * 0.35f
                        + sizePenalty * 0.15f
                        + labelPenalty
                        + lifecyclePenalty;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestTrack = candidate;
                    }
                }

                if (bestTrack == null)
                {
                    if (detection.confidence < newTrackConfidence)
                    {
                        continue;
                    }

                    bestTrack = new TrackState
                    {
                        Id = nextTrackId++,
                        Detection = detection,
                        TimestampSeconds = timestampSeconds,
                        MissedFrames = 0,
                        Lifecycle = TrackLifecycle.Tentative,
                        ObservedThisUpdate = true
                    };
                    tracks.Add(bestTrack.Id, bestTrack);
                    assignedTrackIds.Add(bestTrack.Id);
                    continue;
                }

                double elapsedSeconds = Math.Max(0.0001, timestampSeconds - bestTrack.TimestampSeconds);
                NormalizedBoundingBox previousBox =
                    bestTrack.Detection.boundingBox;
                float previousArea = Math.Max(
                    0.000001f,
                    previousBox.Area);
                float growthRate = (detection.boundingBox.Area - previousArea)
                    / previousArea
                    / (float)elapsedSeconds;
                float observedVelocityX =
                    (detection.boundingBox.centerX - previousBox.centerX)
                    / (float)elapsedSeconds;
                float observedVelocityY =
                    (detection.boundingBox.centerY - previousBox.centerY)
                    / (float)elapsedSeconds;
                bestTrack.CenterVelocityX = Lerp(
                    bestTrack.CenterVelocityX,
                    observedVelocityX,
                    0.5f);
                bestTrack.CenterVelocityY = Lerp(
                    bestTrack.CenterVelocityY,
                    observedVelocityY,
                    0.5f);
                bestTrack.LastGrowthRate = growthRate;

                bestTrack.Detection = detection;
                bestTrack.TimestampSeconds = timestampSeconds;
                bestTrack.MissedFrames = 0;
                bestTrack.ObservedThisUpdate = true;
                assignedTrackIds.Add(bestTrack.Id);
            }

            foreach (TrackState state in tracks.Values)
            {
                bool observed = state.ObservedThisUpdate;
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

            var orderedStates = tracks.Values
                .OrderBy(state => state.Id)
                .ToList();
            var output = new List<TrackedDynamicObject>(
                orderedStates.Count);
            for (int i = 0; i < orderedStates.Count; i++)
            {
                TrackState state = orderedStates[i];
                double unobservedSeconds = state.ObservedThisUpdate
                    ? 0.0
                    : Math.Max(
                        0.0,
                        timestampSeconds - state.TimestampSeconds);
                output.Add(new TrackedDynamicObject(
                    state.Id,
                    state.Detection,
                    state.ObservedThisUpdate
                        ? state.LastGrowthRate
                        : 0f,
                    state.Lifecycle,
                    state.ObservedThisUpdate,
                    state.MissedFrames,
                    unobservedSeconds));
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

        private static NormalizedBoundingBox PredictedBox(
            TrackState state,
            double elapsedSeconds)
        {
            NormalizedBoundingBox box = state.Detection.boundingBox;
            return new NormalizedBoundingBox(
                box.centerX
                    + state.CenterVelocityX * (float)elapsedSeconds,
                box.centerY
                    + state.CenterVelocityY * (float)elapsedSeconds,
                box.width,
                box.height);
        }

        private static float SizeRatio(
            NormalizedBoundingBox a,
            NormalizedBoundingBox b)
        {
            float larger = Math.Max(a.Area, b.Area);
            float smaller = Math.Max(
                0.000001f,
                Math.Min(a.Area, b.Area));
            return larger / smaller;
        }

        private static float Lerp(float from, float to, float amount)
        {
            return from + (to - from) * amount;
        }
    }
}
