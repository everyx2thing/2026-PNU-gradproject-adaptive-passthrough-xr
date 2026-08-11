using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class DynamicRiskPipelineTests
    {
        [Test]
        public void TrackerKeepsIdForNearbyPerson()
        {
            var tracker = new SimpleObjectTracker();
            IReadOnlyList<TrackedDynamicObject> first = tracker.Update(
                0.0,
                new[] { Person(0.40f, 0.50f, 0.20f, 0.40f) });
            IReadOnlyList<TrackedDynamicObject> second = tracker.Update(
                0.1,
                new[] { Person(0.42f, 0.50f, 0.21f, 0.41f) });

            Assert.That(second[0].TrackId, Is.EqualTo(first[0].TrackId));
        }

        [Test]
        public void OneFrameFalsePositiveIsNotConfirmed()
        {
            var tracker = new SimpleObjectTracker();
            IReadOnlyList<TrackedDynamicObject> first = tracker.Update(
                0.0,
                new[] { Person(0.5f, 0.5f, 0.2f, 0.4f, 0.7f) });

            Assert.That(first.Count, Is.EqualTo(1));
            Assert.That(first[0].Lifecycle, Is.EqualTo(TrackLifecycle.Tentative));
            Assert.That(tracker.ConfirmedTrackCount, Is.EqualTo(0));
        }

        [Test]
        public void TrackerConfirmsAfterThreeOfFiveFrames()
        {
            var tracker = new SimpleObjectTracker();
            tracker.Update(0.0, new[] { Person(0.5f, 0.5f, 0.2f, 0.4f, 0.7f) });
            tracker.Update(0.1, Array.Empty<DynamicObjectDetection>());
            tracker.Update(0.2, new[] { Person(0.5f, 0.5f, 0.2f, 0.4f, 0.7f) });
            tracker.Update(0.3, Array.Empty<DynamicObjectDetection>());
            IReadOnlyList<TrackedDynamicObject> confirmed = tracker.Update(
                0.4,
                new[] { Person(0.5f, 0.5f, 0.2f, 0.4f, 0.7f) });

            Assert.That(confirmed.Count, Is.EqualTo(1));
            Assert.That(confirmed[0].Lifecycle, Is.EqualTo(TrackLifecycle.Confirmed));
            Assert.That(tracker.ConfirmedTrackCount, Is.EqualTo(1));
        }

        [Test]
        public void HighConfidenceTrackUsesFastConfirmation()
        {
            var tracker = new SimpleObjectTracker();
            tracker.Update(0.0, new[] { Person(0.5f, 0.5f, 0.2f, 0.4f, 0.9f) });
            IReadOnlyList<TrackedDynamicObject> second = tracker.Update(
                0.1,
                new[] { Person(0.5f, 0.5f, 0.2f, 0.4f, 0.9f) });

            Assert.That(second[0].IsConfirmed, Is.True);
            Assert.That(tracker.ConfirmedTrackCount, Is.EqualTo(1));
        }

        [Test]
        public void ConfirmedCountSurvivesBriefMissAndThenExpires()
        {
            var tracker = new SimpleObjectTracker();
            tracker.Update(0.0, new[] { Person(0.5f, 0.5f, 0.2f, 0.4f, 0.9f) });
            tracker.Update(0.1, new[] { Person(0.5f, 0.5f, 0.2f, 0.4f, 0.9f) });
            tracker.Update(0.4, Array.Empty<DynamicObjectDetection>());
            Assert.That(tracker.ConfirmedTrackCount, Is.EqualTo(1));

            tracker.Update(0.9, Array.Empty<DynamicObjectDetection>());
            Assert.That(tracker.ConfirmedTrackCount, Is.EqualTo(0));
        }

        [Test]
        public void LowConfidenceCannotCreateButCanContinueConfirmedTrack()
        {
            var tracker = new SimpleObjectTracker();
            IReadOnlyList<TrackedDynamicObject> lowOnly = tracker.Update(
                0.0,
                new[] { Person(0.5f, 0.5f, 0.2f, 0.4f, 0.40f) });
            Assert.That(lowOnly, Is.Empty);

            tracker.Update(
                0.1,
                new[] { Person(0.5f, 0.5f, 0.2f, 0.4f, 0.90f) });
            IReadOnlyList<TrackedDynamicObject> confirmed = tracker.Update(
                0.2,
                new[] { Person(0.51f, 0.5f, 0.2f, 0.4f, 0.90f) });
            int trackId = confirmed[0].TrackId;
            IReadOnlyList<TrackedDynamicObject> continued = tracker.Update(
                0.3,
                new[] { Person(0.53f, 0.5f, 0.2f, 0.4f, 0.40f) });

            Assert.That(continued.Count, Is.EqualTo(1));
            Assert.That(continued[0].TrackId, Is.EqualTo(trackId));
            Assert.That(continued[0].IsConfirmed, Is.True);
        }

        [Test]
        public void DynamicRiskPipelineIgnoresTentativeDetections()
        {
            var pipeline = new DynamicRiskPipeline();
            DynamicRiskFrame first = pipeline.Process(
                0.0,
                new[] { Person(0.5f, 0.5f, 0.4f, 0.6f, 0.7f) });
            DynamicRiskFrame second = pipeline.Process(
                0.1,
                new[] { Person(0.5f, 0.5f, 0.4f, 0.6f, 0.7f) });
            DynamicRiskFrame third = pipeline.Process(
                0.2,
                new[] { Person(0.5f, 0.5f, 0.4f, 0.6f, 0.7f) });

            Assert.That(first.Assessments, Is.Empty);
            Assert.That(second.Assessments, Is.Empty);
            Assert.That(third.Assessments.Count, Is.EqualTo(1));
            Assert.That(third.ConfirmedPersonCount, Is.EqualTo(1));
        }

        [Test]
        public void LocationUsesReportThresholds()
        {
            var estimator = new RelativeLocationEstimator();
            var farLeft = new TrackedDynamicObject(
                1,
                Person(0.39f, 0.5f, 0.1f, 0.2f),
                0f);
            var nearCenter = new TrackedDynamicObject(
                2,
                Person(0.5f, 0.5f, 0.3f, 0.3f),
                0f);

            RelativeLocationEstimate left = estimator.Estimate(farLeft);
            RelativeLocationEstimate center = estimator.Estimate(nearCenter);

            Assert.That(left.ScreenZone, Is.EqualTo(HorizontalZone.Left));
            Assert.That(left.DistanceBand, Is.EqualTo(DistanceBand.Far));
            Assert.That(left.BearingDegrees, Is.EqualTo(-16.5f).Within(0.001f));
            Assert.That(center.ScreenZone, Is.EqualTo(HorizontalZone.Center));
            Assert.That(center.DistanceBand, Is.EqualTo(DistanceBand.Near));
        }

        [Test]
        public void MotionHistoryRecognizesApproachAndTtcProxy()
        {
            var estimator = new HistoryMotionEstimator();
            MotionEstimate estimate = null;
            for (int i = 0; i < 5; i++)
            {
                double timestamp = i * 0.1;
                float scale = 0.1f * (float)Math.Exp(0.2 * timestamp);
                estimate = estimator.Estimate(
                    timestamp,
                    new TrackedDynamicObject(
                        1,
                        Person(0.5f, 0.5f, scale, scale),
                        0f));
            }

            Assert.That(estimate, Is.Not.Null);
            Assert.That(estimate.State, Is.EqualTo(DynamicMotionState.Approaching));
            Assert.That(estimate.ScaleRatePerSecond, Is.EqualTo(0.2f).Within(0.005f));
            Assert.That(estimate.TtcSecondsApprox, Is.EqualTo(5f).Within(0.15f));
        }

        [Test]
        public void MotionHistoryIsReleasedWhenTrackExpires()
        {
            var estimator = new HistoryMotionEstimator();
            for (int i = 0; i < 4; i++)
            {
                estimator.Estimate(
                    i * 0.1,
                    new TrackedDynamicObject(
                        1,
                        Person(0.5f, 0.5f, 0.1f + i * 0.01f, 0.2f),
                        0f));
            }

            estimator.PruneExcept(new[] { 2 });
            MotionEstimate restarted = estimator.Estimate(
                1.0,
                new TrackedDynamicObject(
                    1,
                    Person(0.5f, 0.5f, 0.2f, 0.2f),
                    0f));

            Assert.That(restarted.SampleCount, Is.EqualTo(1));
            Assert.That(restarted.State, Is.EqualTo(DynamicMotionState.Unknown));
        }

        [Test]
        public void RiskMatchesMidtermReportFormula()
        {
            DynamicObjectDetection detection = Person(0.5f, 0.5f, 0.4f, 0.4f, 1f);
            var tracked = new TrackedDynamicObject(1, detection, 0f);
            var location = new RelativeLocationEstimator().Estimate(tracked);
            var motion = new MotionEstimate(
                DynamicMotionState.Approaching,
                0.15f,
                2f,
                0f,
                8,
                1.0,
                1f);

            DynamicRiskAssessment risk =
                new DynamicRiskEstimator().Estimate(tracked, location, motion);

            Assert.That(risk.Score, Is.EqualTo(0.9825f).Within(0.0001f));
            Assert.That(risk.Level, Is.EqualTo(DynamicRiskLevel.Danger));
        }

        [Test]
        public void RecedingMultiplierReducesNearPersonToSafe()
        {
            DynamicObjectDetection detection = Person(0.5f, 0.5f, 0.4f, 0.4f, 1f);
            var tracked = new TrackedDynamicObject(1, detection, 0f);
            RelativeLocationEstimate location =
                new RelativeLocationEstimator().Estimate(tracked);
            var motion = new MotionEstimate(
                DynamicMotionState.Receding,
                -0.2f,
                null,
                0f,
                8,
                1.0,
                1f);

            DynamicRiskAssessment risk =
                new DynamicRiskEstimator().Estimate(tracked, location, motion);

            Assert.That(risk.Score, Is.EqualTo(0.237875f).Within(0.0001f));
            Assert.That(risk.Level, Is.EqualTo(DynamicRiskLevel.Safe));
        }

        [Test]
        public void MetricDistanceMakesCloserPersonMoreRisky()
        {
            DynamicObjectDetection detection =
                Person(0.5f, 0.5f, 0.2f, 0.4f, 1f);
            var tracked = new TrackedDynamicObject(1, detection, 0f);
            var estimator = new RelativeLocationEstimator();
            var riskEstimator = new DynamicRiskEstimator();
            var steady = new MotionEstimate(
                DynamicMotionState.Steady,
                0f,
                null,
                0f,
                6,
                0.6,
                1f,
                PersonDistanceSource.EnvironmentDepth,
                true,
                0f,
                null);
            RelativeLocationEstimate close = estimator.Estimate(
                tracked,
                MetricDistance(1, 0.5f));
            RelativeLocationEstimate far = estimator.Estimate(
                tracked,
                MetricDistance(1, 3f));

            float closeRisk =
                riskEstimator.Estimate(tracked, close, steady).Score;
            float farRisk =
                riskEstimator.Estimate(tracked, far, steady).Score;

            Assert.That(closeRisk, Is.GreaterThan(farRisk));
        }

        [Test]
        public void OverallFusionNormalizesConfiguredWeights()
        {
            OverallRiskResult result = OverallRiskFusion.Calculate(
                0.2f,
                0.5f,
                0.9f,
                0f);

            Assert.That(result.TotalRisk, Is.EqualTo(0.54f).Within(0.0001f));
        }

        private static DynamicObjectDetection Person(
            float centerX,
            float centerY,
            float width,
            float height,
            float confidence = 0.9f)
        {
            return new DynamicObjectDetection(
                "person",
                confidence,
                new NormalizedBoundingBox(centerX, centerY, width, height));
        }

        private static PersonDistanceMeasurement MetricDistance(
            int trackId,
            float meters)
        {
            return new PersonDistanceMeasurement(
                trackId,
                0.0,
                PersonDistanceSource.EnvironmentDepth,
                true,
                meters,
                meters,
                1f,
                7,
                7,
                0f,
                0.08f);
        }
    }
}
