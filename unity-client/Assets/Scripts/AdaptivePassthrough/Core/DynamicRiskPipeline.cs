using System;
using System.Collections.Generic;

namespace TeamVR.AdaptivePassthrough
{
    public sealed class DynamicRiskPipeline
    {
        private readonly SimpleObjectTracker tracker;
        private readonly HistoryMotionEstimator motionEstimator;
        private readonly MetricMotionEstimator metricMotionEstimator;
        private readonly RelativeLocationEstimator locationEstimator;
        private readonly DynamicRiskEstimator riskEstimator;
        private readonly Dictionary<int, DynamicRiskAssessment>
            lastObservedAssessments =
                new Dictionary<int, DynamicRiskAssessment>();
        private readonly string targetLabel;

        public DynamicRiskPipeline(
            DynamicRiskSettings riskSettings = null,
            string targetLabel = "person")
        {
            tracker = new SimpleObjectTracker();
            motionEstimator = new HistoryMotionEstimator();
            metricMotionEstimator = new MetricMotionEstimator();
            locationEstimator = new RelativeLocationEstimator();
            riskEstimator = new DynamicRiskEstimator(riskSettings);
            this.targetLabel = targetLabel;
        }

        public DynamicRiskFrame Process(
            double timestampSeconds,
            IReadOnlyList<DynamicObjectDetection> detections)
        {
            return Process(timestampSeconds, detections, null);
        }

        public DynamicRiskFrame Process(
            double timestampSeconds,
            IReadOnlyList<DynamicObjectDetection> detections,
            Func<TrackedDynamicObject, PersonDistanceMeasurement>
                distanceResolver)
        {
            var filtered = new List<DynamicObjectDetection>();
            if (detections != null)
            {
                for (int i = 0; i < detections.Count; i++)
                {
                    DynamicObjectDetection detection = detections[i];
                    if (detection != null
                        && string.Equals(
                            detection.label,
                            targetLabel,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        filtered.Add(detection);
                    }
                }
            }

            IReadOnlyList<TrackedDynamicObject> tracked =
                tracker.Update(timestampSeconds, filtered);
            var liveTrackIds = new List<int>(tracker.LiveTrackIds);
            motionEstimator.PruneExcept(liveTrackIds);
            metricMotionEstimator.PruneExcept(liveTrackIds);
            riskEstimator.PruneExcept(liveTrackIds);
            PruneAssessments(liveTrackIds);
            var assessments = new List<DynamicRiskAssessment>(tracked.Count);
            for (int i = 0; i < tracked.Count; i++)
            {
                TrackedDynamicObject item = tracked[i];
                if (!item.ObservedThisFrame)
                {
                    riskEstimator.MarkUnobserved(item.TrackId);
                    DynamicRiskAssessment held;
                    if (item.IsConfirmed
                        && lastObservedAssessments.TryGetValue(
                            item.TrackId,
                            out held))
                    {
                        assessments.Add(CreateLostAssessment(item, held));
                    }

                    continue;
                }

                PersonDistanceMeasurement distance = distanceResolver == null
                    ? PersonDistanceMeasurement.BoundingBoxFallback(
                        item.TrackId,
                        timestampSeconds,
                        item.Detection.boundingBox.Area)
                    : distanceResolver(item)
                        ?? PersonDistanceMeasurement.BoundingBoxFallback(
                            item.TrackId,
                            timestampSeconds,
                            item.Detection.boundingBox.Area,
                            "distance_resolver_returned_null");
                MotionEstimate boundingBoxMotion =
                    motionEstimator.Estimate(timestampSeconds, item);
                MotionEstimate motion = metricMotionEstimator.Estimate(
                    timestampSeconds,
                    item.TrackId,
                    distance,
                    boundingBoxMotion);
                RelativeLocationEstimate location =
                    locationEstimator.Estimate(item, distance);
                DynamicRiskAssessment assessment =
                    riskEstimator.Estimate(item, location, motion);
                if (!item.IsConfirmed)
                {
                    // Normal tentative detections remain hidden, but fixed
                    // ultra-close safety conditions must not wait for tracker
                    // confirmation at a 3 Hz inference rate.
                    if (assessment.ForcePassthrough)
                    {
                        assessments.Add(assessment);
                    }
                    continue;
                }
                assessments.Add(assessment);
                lastObservedAssessments[item.TrackId] = assessment;
            }

            return new DynamicRiskFrame(
                timestampSeconds,
                assessments,
                tracker.ConfirmedTrackCount,
                liveTrackIds);
        }

        public void Reset()
        {
            tracker.Reset();
            motionEstimator.Reset();
            metricMotionEstimator.Reset();
            riskEstimator.Reset();
            lastObservedAssessments.Clear();
        }

        private DynamicRiskAssessment CreateLostAssessment(
            TrackedDynamicObject tracked,
            DynamicRiskAssessment previous)
        {
            float decayProgress = (float)Math.Min(
                1.0,
                tracked.UnobservedSeconds / 0.75);
            float score = previous.Score
                * (1f - 0.40f * decayProgress);
            var reasons = new List<string>(previous.Reasons);
            if (!reasons.Contains("temporarily_lost"))
            {
                reasons.Add("temporarily_lost");
            }

            return new DynamicRiskAssessment(
                tracked.TrackId,
                tracked.Detection,
                previous.Location,
                previous.Motion,
                score,
                riskEstimator.LevelForScore(score),
                reasons.ToArray(),
                previous.Breakdown,
                TrackLifecycle.Lost,
                false,
                (float)tracked.UnobservedSeconds,
                false,
                previous.ForcePassthrough,
                previous.ClosePassthroughActive,
                "unobserved_hold",
                previous.CloseReleaseConfirmationCount);
        }

        private void PruneAssessments(IEnumerable<int> liveTrackIds)
        {
            var live = liveTrackIds == null
                ? new HashSet<int>()
                : new HashSet<int>(liveTrackIds);
            var expired = new List<int>();
            foreach (int trackId in lastObservedAssessments.Keys)
            {
                if (!live.Contains(trackId))
                {
                    expired.Add(trackId);
                }
            }

            for (int i = 0; i < expired.Count; i++)
            {
                lastObservedAssessments.Remove(expired[i]);
            }
        }
    }
}
