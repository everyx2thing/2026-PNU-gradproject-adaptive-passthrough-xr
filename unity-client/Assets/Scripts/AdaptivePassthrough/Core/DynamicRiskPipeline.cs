using System;
using System.Collections.Generic;

namespace TeamVR.AdaptivePassthrough
{
    public sealed class DynamicRiskPipeline
    {
        private readonly SimpleObjectTracker tracker;
        private readonly HistoryMotionEstimator motionEstimator;
        private readonly RelativeLocationEstimator locationEstimator;
        private readonly DynamicRiskEstimator riskEstimator;
        private readonly string targetLabel;

        public DynamicRiskPipeline(
            DynamicRiskSettings riskSettings = null,
            string targetLabel = "person")
        {
            tracker = new SimpleObjectTracker();
            motionEstimator = new HistoryMotionEstimator();
            locationEstimator = new RelativeLocationEstimator();
            riskEstimator = new DynamicRiskEstimator(riskSettings);
            this.targetLabel = targetLabel;
        }

        public DynamicRiskFrame Process(
            double timestampSeconds,
            IReadOnlyList<DynamicObjectDetection> detections)
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
            motionEstimator.PruneExcept(tracker.ConfirmedTrackIds);
            var assessments = new List<DynamicRiskAssessment>(tracked.Count);
            for (int i = 0; i < tracked.Count; i++)
            {
                TrackedDynamicObject item = tracked[i];
                if (!item.IsConfirmed)
                {
                    continue;
                }

                MotionEstimate motion = motionEstimator.Estimate(timestampSeconds, item);
                RelativeLocationEstimate location = locationEstimator.Estimate(item);
                assessments.Add(riskEstimator.Estimate(item, location, motion));
            }

            return new DynamicRiskFrame(
                timestampSeconds,
                assessments,
                tracker.ConfirmedTrackCount);
        }

        public void Reset()
        {
            tracker.Reset();
            motionEstimator.Reset();
        }
    }
}
