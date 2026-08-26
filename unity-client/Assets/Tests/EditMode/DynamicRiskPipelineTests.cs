using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

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
            Assert.That(tracker.ConfirmedTrackCount, Is.EqualTo(1));

            tracker.Update(1.61, Array.Empty<DynamicObjectDetection>());
            Assert.That(tracker.ConfirmedTrackCount, Is.EqualTo(0));
        }

        [Test]
        public void GlobalMatchingKeepsIdsWhileTwoPeopleCross()
        {
            var tracker = new SimpleObjectTracker();
            tracker.Update(0.0, new[]
            {
                Person(0.30f, 0.5f, 0.18f, 0.40f),
                Person(0.70f, 0.5f, 0.18f, 0.40f)
            });
            IReadOnlyList<TrackedDynamicObject> confirmed = tracker.Update(
                0.1,
                new[]
                {
                    Person(0.35f, 0.5f, 0.18f, 0.40f),
                    Person(0.65f, 0.5f, 0.18f, 0.40f)
                });
            int leftId = confirmed[0].TrackId;
            int rightId = confirmed[1].TrackId;
            tracker.Update(0.2, new[]
            {
                Person(0.43f, 0.5f, 0.18f, 0.40f),
                Person(0.57f, 0.5f, 0.18f, 0.40f)
            });
            tracker.Update(0.3, new[]
            {
                Person(0.51f, 0.5f, 0.18f, 0.40f),
                Person(0.49f, 0.5f, 0.18f, 0.40f)
            });
            IReadOnlyList<TrackedDynamicObject> crossed = tracker.Update(
                0.4,
                new[]
                {
                    Person(0.42f, 0.5f, 0.18f, 0.40f),
                    Person(0.58f, 0.5f, 0.18f, 0.40f)
                });

            Assert.That(crossed[0].TrackId, Is.EqualTo(leftId));
            Assert.That(crossed[1].TrackId, Is.EqualTo(rightId));
            Assert.That(
                crossed[0].Detection.boundingBox.centerX,
                Is.GreaterThan(crossed[1].Detection.boundingBox.centerX));
        }

        [Test]
        public void TrackReidentifiesAfterPointSevenFiveSecondOcclusion()
        {
            var tracker = new SimpleObjectTracker();
            tracker.Update(0.0, new[] { Person(0.4f, 0.5f, 0.2f, 0.4f) });
            int id = tracker.Update(
                0.1,
                new[] { Person(0.42f, 0.5f, 0.2f, 0.4f) })[0].TrackId;
            tracker.Update(0.85, Array.Empty<DynamicObjectDetection>());
            IReadOnlyList<TrackedDynamicObject> recovered = tracker.Update(
                0.90,
                new[] { Person(0.48f, 0.5f, 0.2f, 0.4f) });

            Assert.That(recovered.Count, Is.EqualTo(1));
            Assert.That(recovered[0].TrackId, Is.EqualTo(id));
            Assert.That(recovered[0].ReidentifiedThisFrame, Is.True);
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
            var farTracked = new TrackedDynamicObject(2, detection, 0f);
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
                farTracked,
                MetricDistance(2, 3f));

            float closeRisk =
                riskEstimator.Estimate(tracked, close, steady).Score;
            float farRisk =
                riskEstimator.Estimate(farTracked, far, steady).Score;

            Assert.That(closeRisk, Is.GreaterThan(farRisk));
        }

        [Test]
        public void TrackerExpiresWorldPointWhenDepthStopsUpdating()
        {
            var tracker = new SimpleObjectTracker();
            var box = new NormalizedBoundingBox(0.5f, 0.5f, 0.3f, 0.6f);
            var withWorld = new DynamicObjectDetection(
                "person",
                0.9f,
                box,
                0,
                true,
                new Vector3(0f, 1f, 2f),
                0.8f);
            tracker.Update(0.0, new[] { withWorld });
            tracker.Update(0.1, new[] { withWorld });

            IReadOnlyList<TrackedDynamicObject> updated = tracker.Update(
                1.0,
                new[] { Person(0.5f, 0.5f, 0.3f, 0.6f, 0.9f) });

            Assert.That(updated.Count, Is.EqualTo(1));
            Assert.That(updated[0].Detection.hasWorldPoint, Is.False);
        }

        [Test]
        public void TrackerDoesNotCreateVelocityFromDuplicateTimestamp()
        {
            var tracker = new SimpleObjectTracker();
            tracker.Update(
                0.0,
                new[] { Person(0.50f, 0.5f, 0.3f, 0.6f, 0.9f) });
            tracker.Update(
                0.0,
                new[] { Person(0.51f, 0.5f, 0.3f, 0.6f, 0.9f) });

            IReadOnlyList<TrackedDynamicObject> predicted = tracker.Update(
                0.1,
                Array.Empty<DynamicObjectDetection>());

            Assert.That(predicted.Count, Is.EqualTo(1));
            Assert.That(
                predicted[0].Detection.boundingBox.centerX,
                Is.LessThan(0.60f));
        }

        [Test]
        public void DynamicRiskPipelineAllowsTentativeUltraCloseEmergency()
        {
            var pipeline = new DynamicRiskPipeline();

            DynamicRiskFrame first = pipeline.Process(
                0.0,
                new[] { Person(0.5f, 0.5f, 0.75f, 0.96f, 0.80f) });

            Assert.That(first.Assessments.Count, Is.EqualTo(1));
            Assert.That(
                first.Assessments[0].Lifecycle,
                Is.EqualTo(TrackLifecycle.Tentative));
            Assert.That(first.Assessments[0].ForcePassthrough, Is.True);
            Assert.That(first.ForcePassthrough, Is.True);
        }

        [Test]
        public void UltraCloseBoundingBoxForcesDangerousPersonRisk()
        {
            DynamicObjectDetection detection =
                Person(0.5f, 0.5f, 0.96f, 0.96f, 0.80f);
            var tracked = new TrackedDynamicObject(11, detection, 0f);
            RelativeLocationEstimate location =
                new RelativeLocationEstimator().Estimate(tracked);
            var steady = new MotionEstimate(
                DynamicMotionState.Steady,
                0f,
                null,
                0f,
                4,
                0.5,
                1f);

            DynamicRiskAssessment risk =
                new DynamicRiskEstimator().Estimate(
                    tracked,
                    location,
                    steady);

            Assert.That(risk.Score, Is.GreaterThanOrEqualTo(0.90f));
            Assert.That(risk.Reasons, Does.Contain("ultra_close_force"));
            Assert.That(risk.ForcePassthrough, Is.True);
            var frame = new DynamicRiskFrame(0.0, new[] { risk });
            Assert.That(frame.ForcePassthrough, Is.True);
            Assert.That(frame.PolicyRisk, Is.EqualTo(1f));
        }

        [Test]
        public void PortraitCloseBoundingBoxDoesNotForceWithoutRequiredArea()
        {
            DynamicObjectDetection detection =
                Person(0.5f, 0.5f, 0.30f, 0.96f, 0.80f);
            var tracked = new TrackedDynamicObject(41, detection, 0f);
            RelativeLocationEstimate location =
                new RelativeLocationEstimator().Estimate(tracked);
            var steady = new MotionEstimate(
                DynamicMotionState.Steady,
                0f,
                null,
                0f,
                4,
                0.5,
                1f);

            DynamicRiskAssessment risk =
                new DynamicRiskEstimator().Estimate(
                    tracked,
                    location,
                    steady);

            Assert.That(risk.ForcePassthrough, Is.False);
            Assert.That(risk.Reasons, Does.Not.Contain("ultra_close_force"));
        }

        [Test]
        public void WeakUltraCloseBoundingBoxRequiresTwoConfirmations()
        {
            var estimator = new DynamicRiskEstimator();
            var tracked = new TrackedDynamicObject(
                21,
                Person(0.5f, 0.5f, 0.70f, 0.93f, 0.65f),
                0f);
            RelativeLocationEstimate location =
                new RelativeLocationEstimator().Estimate(tracked);
            var steady = new MotionEstimate(
                DynamicMotionState.Steady,
                0f,
                null,
                0f,
                4,
                0.5,
                1f);

            DynamicRiskAssessment first = estimator.Estimate(
                tracked,
                location,
                steady);
            DynamicRiskAssessment second = estimator.Estimate(
                tracked,
                location,
                steady);

            Assert.That(first.ForcePassthrough, Is.False);
            Assert.That(second.ForcePassthrough, Is.True);
            Assert.That(second.Reasons,
                Does.Contain("confirmed_bbox_close"));
        }

        [Test]
        public void UltraCloseRiskRequiresThreeFarConfirmationsToRelease()
        {
            var estimator = new DynamicRiskEstimator();
            var relative = new RelativeLocationEstimator();
            var steady = new MotionEstimate(
                DynamicMotionState.Steady,
                0f,
                null,
                0f,
                4,
                0.5,
                1f);
            var closeTracked = new TrackedDynamicObject(
                12,
                Person(0.5f, 0.5f, 0.55f, 0.80f, 0.90f),
                0f);

            DynamicRiskAssessment close = estimator.Estimate(
                closeTracked,
                relative.Estimate(closeTracked, MetricDistance(12, 0.55f)),
                steady);
            Assert.That(close.Reasons, Does.Contain("ultra_close_force"));

            var farTracked = new TrackedDynamicObject(
                12,
                Person(0.5f, 0.5f, 0.20f, 0.50f, 0.90f),
                1f);
            DynamicRiskAssessment first = estimator.Estimate(
                farTracked,
                relative.Estimate(farTracked, MetricDistance(12, 1.0f)),
                steady);
            DynamicRiskAssessment second = estimator.Estimate(
                farTracked,
                relative.Estimate(farTracked, MetricDistance(12, 1.0f)),
                steady);
            DynamicRiskAssessment third = estimator.Estimate(
                farTracked,
                relative.Estimate(farTracked, MetricDistance(12, 1.0f)),
                steady);

            Assert.That(first.Reasons, Does.Contain("ultra_close_force"));
            Assert.That(second.Reasons, Does.Contain("ultra_close_force"));
            Assert.That(third.Reasons, Does.Not.Contain("ultra_close_force"));
            Assert.That(first.ForcePassthrough, Is.True);
            Assert.That(second.ForcePassthrough, Is.True);
            Assert.That(third.ForcePassthrough, Is.False);
        }

        [Test]
        public void UltraCloseRiskReleasesFromQuestSizedBoxWithoutMetricDepth()
        {
            var estimator = new DynamicRiskEstimator();
            var relative = new RelativeLocationEstimator();
            var steady = new MotionEstimate(
                DynamicMotionState.Steady,
                0f,
                null,
                0f,
                4,
                0.5,
                1f);
            var closeTracked = new TrackedDynamicObject(
                31,
                Person(0.455f, 0.498f, 0.671f, 0.995f, 0.774f),
                0f);

            DynamicRiskAssessment close = estimator.Estimate(
                closeTracked,
                relative.Estimate(closeTracked),
                steady);
            Assert.That(close.ForcePassthrough, Is.True);

            // Reproduces the latest Quest log: height had fallen to about
            // 0.80 and area to 0.25, but the old 0.65/0.25 conjunction kept
            // risk pinned to 0.9 for roughly 50 seconds.
            var backedAway = new TrackedDynamicObject(
                31,
                Person(0.5f, 0.5f, 0.3125f, 0.80f, 0.47f),
                1f);
            RelativeLocationEstimate noMetric = relative.Estimate(backedAway);
            DynamicRiskAssessment first = estimator.Estimate(
                backedAway,
                noMetric,
                steady);
            DynamicRiskAssessment second = estimator.Estimate(
                backedAway,
                noMetric,
                steady);
            DynamicRiskAssessment third = estimator.Estimate(
                backedAway,
                noMetric,
                steady);

            Assert.That(first.ForcePassthrough, Is.True);
            Assert.That(second.ForcePassthrough, Is.True);
            Assert.That(third.ForcePassthrough, Is.False);
            Assert.That(third.Reasons, Does.Not.Contain("ultra_close_force"));
        }

        [Test]
        public void NoDepthReleaseRequiresUnclippedConfidentSmallBox()
        {
            var estimator = new DynamicRiskEstimator();
            var relative = new RelativeLocationEstimator();
            var steady = new MotionEstimate(
                DynamicMotionState.Steady, 0f, null, 0f, 4, 0.5, 1f);
            var close = new TrackedDynamicObject(
                71,
                Person(0.5f, 0.5f, 0.96f, 0.96f, 0.90f),
                0f);
            estimator.Estimate(close, relative.Estimate(close), steady);

            var tall = new TrackedDynamicObject(
                71,
                Person(0.5f, 0.5f, 0.458f, 0.96f, 0.80f),
                0f);
            for (int i = 0; i < 3; i++)
            {
                DynamicRiskAssessment assessment = estimator.Estimate(
                    tall,
                    relative.Estimate(tall),
                    steady);
                Assert.That(assessment.ForcePassthrough, Is.True);
            }

            var clipped = new TrackedDynamicObject(
                71,
                Person(0.15f, 0.5f, 0.30f, 0.80f, 0.80f),
                0f);
            for (int i = 0; i < 3; i++)
            {
                DynamicRiskAssessment assessment = estimator.Estimate(
                    clipped,
                    relative.Estimate(clipped),
                    steady);
                Assert.That(assessment.ForcePassthrough, Is.True);
            }
        }

        [Test]
        public void TrackerExpiresBeforeMatchingAndCreatesNewId()
        {
            var tracker = new SimpleObjectTracker(
                maximumMissedFrames: 100,
                maximumUnobservedSeconds: 0.5);
            IReadOnlyList<TrackedDynamicObject> first = tracker.Update(
                0.0,
                new[] { Person(0.5f, 0.5f, 0.3f, 0.6f, 0.9f) });
            int firstId = first[0].TrackId;

            IReadOnlyList<TrackedDynamicObject> returned = tracker.Update(
                0.6,
                new[] { Person(0.5f, 0.5f, 0.3f, 0.6f, 0.9f) });

            Assert.That(returned.Count, Is.EqualTo(1));
            Assert.That(returned[0].TrackId, Is.Not.EqualTo(firstId));
        }

        [Test]
        public void DynamicFrameIncludesTentativeLiveTrackIds()
        {
            var pipeline = new DynamicRiskPipeline();
            DynamicRiskFrame frame = pipeline.Process(
                0.0,
                new[] { Person(0.5f, 0.5f, 0.3f, 0.6f, 0.8f) });

            Assert.That(frame.LiveTrackIds.Count, Is.EqualTo(1));
            Assert.That(frame.LiveTrackIds[0], Is.GreaterThan(0));
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
