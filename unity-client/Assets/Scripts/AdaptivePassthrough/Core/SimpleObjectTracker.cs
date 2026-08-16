using System;
using System.Collections.Generic;

namespace TeamVR.AdaptivePassthrough
{
    public sealed class SimpleObjectTracker
    {
        private const float InvalidMatchCost = 1000f;
        private const float UnmatchedTrackCost = 2.25f;
        private const float NewDetectionCost = 1.75f;

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
            public float WidthVelocity;
            public float HeightVelocity;
            public float LastGrowthRate;
            public bool HasWorldPoint;
            public UnityEngine.Vector3 WorldPoint;
            public UnityEngine.Vector3 WorldVelocity;
            public float WorldPointConfidence;
            public bool ObservedThisUpdate;
            public bool ReidentifiedThisUpdate;
            public readonly Queue<bool> RecentObservations = new Queue<bool>();
        }

        private readonly Dictionary<int, TrackState> tracks =
            new Dictionary<int, TrackState>();
        private readonly List<TrackState> orderedTracks = new List<TrackState>();
        private readonly List<int> expiredTrackIds = new List<int>();
        private readonly List<TrackedDynamicObject> output =
            new List<TrackedDynamicObject>();
        private bool[] assignedDetections = Array.Empty<bool>();
        private float[,] assignmentCosts = new float[0, 0];
        private float[] assignmentU = Array.Empty<float>();
        private float[] assignmentV = Array.Empty<float>();
        private float[] assignmentMinimum = Array.Empty<float>();
        private bool[] assignmentUsed = Array.Empty<bool>();
        private int[] assignmentP = Array.Empty<int>();
        private int[] assignmentWay = Array.Empty<int>();
        private int[] assignmentResult = Array.Empty<int>();
        private int assignmentCapacity;
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

        public IEnumerable<int> LiveTrackIds => tracks.Keys;

        public IEnumerable<int> ConfirmedTrackIds
        {
            get
            {
                foreach (TrackState state in tracks.Values)
                {
                    if (state.Lifecycle == TrackLifecycle.Confirmed
                        || state.Lifecycle == TrackLifecycle.Lost)
                    {
                        yield return state.Id;
                    }
                }
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
            double maximumUnobservedSeconds = 1.20,
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
            this.fastConfirmationConfidence = Clamp01(
                fastConfirmationConfidence);
            this.maximumUnobservedSeconds = Math.Max(
                0.05,
                maximumUnobservedSeconds);
            this.newTrackConfidence = Clamp01(newTrackConfidence);
            this.maximumSizeRatio = Math.Max(1f, maximumSizeRatio);
        }

        public IReadOnlyList<TrackedDynamicObject> Update(
            double timestampSeconds,
            IReadOnlyList<DynamicObjectDetection> detections)
        {
            detections = detections ?? Array.Empty<DynamicObjectDetection>();
            orderedTracks.Clear();
            foreach (TrackState state in tracks.Values)
            {
                state.ObservedThisUpdate = false;
                state.ReidentifiedThisUpdate = false;
                orderedTracks.Add(state);
            }

            orderedTracks.Sort((a, b) => a.Id.CompareTo(b.Id));
            EnsureAssignmentCapacity(orderedTracks.Count + detections.Count);
            Array.Clear(
                assignedDetections,
                0,
                MathfMin(detections.Count, assignedDetections.Length));
            if (orderedTracks.Count > 0 && detections.Count > 0)
            {
                MatchGlobally(timestampSeconds, detections, assignedDetections);
            }

            for (int detectionIndex = 0;
                detectionIndex < detections.Count;
                detectionIndex++)
            {
                if (assignedDetections[detectionIndex])
                {
                    continue;
                }

                DynamicObjectDetection detection = detections[detectionIndex];
                if (detection == null
                    || detection.confidence < newTrackConfidence)
                {
                    continue;
                }

                var state = new TrackState
                {
                    Id = nextTrackId++,
                    Detection = detection,
                    TimestampSeconds = timestampSeconds,
                    Lifecycle = TrackLifecycle.Tentative,
                    ObservedThisUpdate = true,
                    HasWorldPoint = detection.hasWorldPoint,
                    WorldPoint = detection.worldPoint,
                    WorldPointConfidence = detection.worldPointConfidence
                };
                tracks.Add(state.Id, state);
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

            expiredTrackIds.Clear();
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

            orderedTracks.Clear();
            foreach (TrackState state in tracks.Values)
            {
                orderedTracks.Add(state);
            }

            orderedTracks.Sort((a, b) => a.Id.CompareTo(b.Id));
            output.Clear();
            for (int i = 0; i < orderedTracks.Count; i++)
            {
                TrackState state = orderedTracks[i];
                double missingSeconds = state.ObservedThisUpdate
                    ? 0.0
                    : Math.Max(0.0, timestampSeconds - state.TimestampSeconds);
                DynamicObjectDetection outputDetection = state.Detection;
                if (!state.ObservedThisUpdate)
                {
                    outputDetection = new DynamicObjectDetection(
                        state.Detection.label,
                        state.Detection.confidence,
                        PredictedBox(state, missingSeconds),
                        state.Detection.classId,
                        state.HasWorldPoint,
                        state.HasWorldPoint
                            ? state.WorldPoint
                                + state.WorldVelocity * (float)missingSeconds
                            : UnityEngine.Vector3.zero,
                        state.WorldPointConfidence);
                }

                output.Add(new TrackedDynamicObject(
                    state.Id,
                    outputDetection,
                    state.ObservedThisUpdate ? state.LastGrowthRate : 0f,
                    state.Lifecycle,
                    state.ObservedThisUpdate,
                    state.MissedFrames,
                    missingSeconds,
                    state.ReidentifiedThisUpdate));
            }

            return output;
        }

        public void Reset()
        {
            tracks.Clear();
            orderedTracks.Clear();
            expiredTrackIds.Clear();
            output.Clear();
            nextTrackId = 1;
        }

        private void MatchGlobally(
            double timestampSeconds,
            IReadOnlyList<DynamicObjectDetection> detections,
            bool[] assignedDetections)
        {
            int trackCount = orderedTracks.Count;
            int detectionCount = detections.Count;
            int size = trackCount + detectionCount;
            EnsureAssignmentCapacity(size);
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    if (row < trackCount && column < detectionCount)
                    {
                        assignmentCosts[row, column] = MatchCost(
                            orderedTracks[row],
                            detections[column],
                            timestampSeconds);
                    }
                    else if (row < trackCount)
                    {
                        assignmentCosts[row, column] = UnmatchedTrackCost;
                    }
                    else if (column < detectionCount)
                    {
                        assignmentCosts[row, column] = NewDetectionCost;
                    }
                    else
                    {
                        assignmentCosts[row, column] = 0f;
                    }
                }
            }

            int[] assignment = MinimumCostAssignment(size);
            for (int trackIndex = 0; trackIndex < trackCount; trackIndex++)
            {
                int detectionIndex = assignment[trackIndex];
                if (detectionIndex < 0
                    || detectionIndex >= detectionCount
                    || assignmentCosts[trackIndex, detectionIndex]
                        >= InvalidMatchCost)
                {
                    continue;
                }

                UpdateMatchedTrack(
                    orderedTracks[trackIndex],
                    detections[detectionIndex],
                    timestampSeconds);
                assignedDetections[detectionIndex] = true;
            }
        }

        private float MatchCost(
            TrackState state,
            DynamicObjectDetection detection,
            double timestampSeconds)
        {
            if (detection == null
                || (detection.confidence < newTrackConfidence
                    && state.Lifecycle == TrackLifecycle.Tentative))
            {
                return InvalidMatchCost;
            }

            double predictionSeconds = Math.Max(
                0.0,
                timestampSeconds - state.TimestampSeconds);
            NormalizedBoundingBox predicted = PredictedBox(
                state,
                predictionSeconds);
            float iou = IntersectionOverUnion(predicted, detection.boundingBox);
            float centerDistance = CenterDistance(
                predicted,
                detection.boundingBox);
            float sizeRatio = SizeRatio(predicted, detection.boundingBox);
            float allowedCenterDistance = maximumCenterDistance
                + (float)Math.Min(0.20, predictionSeconds * 0.10);
            if ((iou < minimumIou && centerDistance > allowedCenterDistance)
                || sizeRatio > maximumSizeRatio)
            {
                return InvalidMatchCost;
            }

            float labelPenalty = string.Equals(
                state.Detection.label,
                detection.label,
                StringComparison.OrdinalIgnoreCase) ? 0f : 1f;
            float lifecyclePenalty = state.Lifecycle == TrackLifecycle.Tentative
                ? 0.20f
                : 0f;
            float sizePenalty = (float)Math.Abs(
                Math.Log(Math.Max(0.0001f, sizeRatio)));
            float confidencePenalty = 1f - Clamp01(detection.confidence);
            float worldPenalty = 0f;
            if (state.HasWorldPoint && detection.hasWorldPoint)
            {
                UnityEngine.Vector3 predictedWorld = state.WorldPoint
                    + state.WorldVelocity * (float)predictionSeconds;
                float worldDistance = UnityEngine.Vector3.Distance(
                    predictedWorld,
                    detection.worldPoint);
                if (worldDistance > 2.0f)
                {
                    return InvalidMatchCost;
                }

                worldPenalty = Math.Min(1f, worldDistance / 1.5f)
                    * Math.Min(
                        state.WorldPointConfidence,
                        detection.worldPointConfidence)
                    * 0.50f;
            }

            return (1f - iou) * 0.55f
                + centerDistance * 1.50f
                + sizePenalty * 0.20f
                + confidencePenalty * 0.15f
                + worldPenalty
                + labelPenalty
                + lifecyclePenalty;
        }

        private int[] MinimumCostAssignment(int size)
        {
            Array.Clear(assignmentU, 0, size + 1);
            Array.Clear(assignmentV, 0, size + 1);
            Array.Clear(assignmentP, 0, size + 1);
            Array.Clear(assignmentWay, 0, size + 1);
            for (int row = 1; row <= size; row++)
            {
                assignmentP[0] = row;
                int column0 = 0;
                Array.Clear(assignmentUsed, 0, size + 1);
                for (int column = 0; column <= size; column++)
                {
                    assignmentMinimum[column] = float.MaxValue;
                }

                do
                {
                    assignmentUsed[column0] = true;
                    int row0 = assignmentP[column0];
                    float delta = float.MaxValue;
                    int column1 = 0;
                    for (int column = 1; column <= size; column++)
                    {
                        if (assignmentUsed[column])
                        {
                            continue;
                        }

                        float current = assignmentCosts[
                                row0 - 1,
                                column - 1]
                            - assignmentU[row0]
                            - assignmentV[column];
                        if (current < assignmentMinimum[column])
                        {
                            assignmentMinimum[column] = current;
                            assignmentWay[column] = column0;
                        }

                        if (assignmentMinimum[column] < delta)
                        {
                            delta = assignmentMinimum[column];
                            column1 = column;
                        }
                    }

                    for (int column = 0; column <= size; column++)
                    {
                        if (assignmentUsed[column])
                        {
                            assignmentU[assignmentP[column]] += delta;
                            assignmentV[column] -= delta;
                        }
                        else
                        {
                            assignmentMinimum[column] -= delta;
                        }
                    }

                    column0 = column1;
                }
                while (assignmentP[column0] != 0);

                do
                {
                    int column1 = assignmentWay[column0];
                    assignmentP[column0] = assignmentP[column1];
                    column0 = column1;
                }
                while (column0 != 0);
            }

            for (int i = 0; i < size; i++)
            {
                assignmentResult[i] = -1;
            }

            for (int column = 1; column <= size; column++)
            {
                if (assignmentP[column] > 0)
                {
                    assignmentResult[assignmentP[column] - 1] = column - 1;
                }
            }

            return assignmentResult;
        }

        private void EnsureAssignmentCapacity(int requestedSize)
        {
            if (requestedSize <= assignmentCapacity)
            {
                return;
            }

            int capacity = Math.Max(4, assignmentCapacity);
            while (capacity < requestedSize)
            {
                capacity *= 2;
            }

            assignmentCapacity = capacity;
            assignedDetections = new bool[capacity];
            assignmentCosts = new float[capacity, capacity];
            assignmentU = new float[capacity + 1];
            assignmentV = new float[capacity + 1];
            assignmentMinimum = new float[capacity + 1];
            assignmentUsed = new bool[capacity + 1];
            assignmentP = new int[capacity + 1];
            assignmentWay = new int[capacity + 1];
            assignmentResult = new int[capacity];
        }

        private static int MathfMin(int left, int right)
        {
            return Math.Min(left, right);
        }

        private static void UpdateMatchedTrack(
            TrackState state,
            DynamicObjectDetection detection,
            double timestampSeconds)
        {
            bool reidentified = state.Lifecycle == TrackLifecycle.Lost;
            double elapsedSeconds = Math.Max(
                0.0001,
                timestampSeconds - state.TimestampSeconds);
            float elapsed = (float)elapsedSeconds;
            NormalizedBoundingBox previous = state.Detection.boundingBox;
            NormalizedBoundingBox predicted = PredictedBox(
                state,
                elapsedSeconds);
            float residualX = detection.boundingBox.centerX - predicted.centerX;
            float residualY = detection.boundingBox.centerY - predicted.centerY;
            float residualWidth = detection.boundingBox.width - predicted.width;
            float residualHeight = detection.boundingBox.height - predicted.height;
            const float alpha = 0.70f;
            const float beta = 0.18f;
            NormalizedBoundingBox filtered = new NormalizedBoundingBox(
                predicted.centerX + alpha * residualX,
                predicted.centerY + alpha * residualY,
                predicted.width + alpha * residualWidth,
                predicted.height + alpha * residualHeight);
            state.CenterVelocityX += beta * residualX / elapsed;
            state.CenterVelocityY += beta * residualY / elapsed;
            state.WidthVelocity += beta * residualWidth / elapsed;
            state.HeightVelocity += beta * residualHeight / elapsed;
            if (detection.hasWorldPoint)
            {
                UnityEngine.Vector3 worldPrediction = state.HasWorldPoint
                    ? state.WorldPoint + state.WorldVelocity * elapsed
                    : detection.worldPoint;
                UnityEngine.Vector3 worldResidual =
                    detection.worldPoint - worldPrediction;
                state.WorldPoint = worldPrediction + 0.65f * worldResidual;
                if (state.HasWorldPoint)
                {
                    state.WorldVelocity += 0.12f * worldResidual / elapsed;
                }

                state.HasWorldPoint = true;
                state.WorldPointConfidence = detection.worldPointConfidence;
            }
            float previousArea = Math.Max(0.000001f, previous.Area);
            state.LastGrowthRate = (detection.boundingBox.Area - previousArea)
                / previousArea
                / elapsed;
            state.Detection = new DynamicObjectDetection(
                detection.label,
                detection.confidence,
                filtered,
                detection.classId,
                state.HasWorldPoint,
                state.WorldPoint,
                state.WorldPointConfidence);
            state.TimestampSeconds = timestampSeconds;
            state.MissedFrames = 0;
            state.ObservedThisUpdate = true;
            state.ReidentifiedThisUpdate = reidentified;
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
            float intersection = Math.Max(0f, right - left)
                * Math.Max(0f, bottom - top);
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
            float elapsed = (float)Math.Min(1.2, Math.Max(0.0, elapsedSeconds));
            NormalizedBoundingBox box = state.Detection.boundingBox;
            return new NormalizedBoundingBox(
                box.centerX + state.CenterVelocityX * elapsed,
                box.centerY + state.CenterVelocityY * elapsed,
                box.width + state.WidthVelocity * elapsed,
                box.height + state.HeightVelocity * elapsed);
        }

        private static float SizeRatio(
            NormalizedBoundingBox a,
            NormalizedBoundingBox b)
        {
            float larger = Math.Max(a.Area, b.Area);
            float smaller = Math.Max(0.000001f, Math.Min(a.Area, b.Area));
            return larger / smaller;
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
