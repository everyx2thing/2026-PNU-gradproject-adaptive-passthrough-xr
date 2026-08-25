using NUnit.Framework;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class MetricMotionEstimatorTests
    {
        [Test]
        public void DecreasingDistanceProducesApproachAndMetricTtc()
        {
            var estimator = new MetricMotionEstimator();
            MotionEstimate result = null;
            for (int i = 0; i < 5; i++)
            {
                result = estimator.Estimate(
                    i * 0.1,
                    7,
                    Metric(7, i * 0.1, 2.0f - i * 0.1f),
                    BBoxFallback());
            }

            Assert.That(result, Is.Not.Null);
            Assert.That(result.HasMetricMotion, Is.True);
            Assert.That(
                result.State,
                Is.EqualTo(DynamicMotionState.Approaching));
            Assert.That(
                result.ClosingSpeedMetersPerSecond,
                Is.EqualTo(1f).Within(0.08f));
            Assert.That(result.MetricTtcSeconds.HasValue, Is.True);
        }

        [Test]
        public void IncreasingDistanceProducesRecedingState()
        {
            var estimator = new MetricMotionEstimator();
            MotionEstimate result = null;
            for (int i = 0; i < 5; i++)
            {
                result = estimator.Estimate(
                    i * 0.1,
                    2,
                    Metric(2, i * 0.1, 1.0f + i * 0.08f),
                    BBoxFallback());
            }

            Assert.That(result.HasMetricMotion, Is.True);
            Assert.That(
                result.State,
                Is.EqualTo(DynamicMotionState.Receding));
            Assert.That(
                result.ClosingSpeedMetersPerSecond,
                Is.LessThan(-0.5f));
            Assert.That(result.MetricTtcSeconds.HasValue, Is.False);
        }

        [Test]
        public void GrowingBoundingBoxBlocksConflictingRecedingState()
        {
            var estimator = new MetricMotionEstimator();
            MotionEstimate result = null;
            var bboxApproach = new MotionEstimate(
                DynamicMotionState.Approaching,
                0.20f,
                5f,
                0f,
                5,
                0.5,
                1f);
            for (int i = 0; i < 5; i++)
            {
                result = estimator.Estimate(
                    i * 0.1,
                    9,
                    Metric(9, i * 0.1, 0.32f + i * 0.20f),
                    bboxApproach);
            }

            Assert.That(result.MetricConflict, Is.True);
            Assert.That(result.State,
                Is.Not.EqualTo(DynamicMotionState.Receding));
        }

        [Test]
        public void LowConfidenceIncreasingDepthUsesBoundingBoxApproach()
        {
            var estimator = new MetricMotionEstimator();
            var bboxApproach = new MotionEstimate(
                DynamicMotionState.Approaching,
                0.20f,
                4f,
                0f,
                4,
                0.4,
                0.8f);
            MotionEstimate result = null;
            for (int i = 0; i < 5; i++)
            {
                result = estimator.Estimate(
                    i * 0.1,
                    15,
                    new PersonDistanceMeasurement(
                        15,
                        i * 0.1,
                        PersonDistanceSource.EnvironmentDepth,
                        true,
                        2.64f + i * 0.06f,
                        2.64f + i * 0.06f,
                        0.15f,
                        13,
                        3,
                        0f,
                        0.4f,
                        "low_depth_confidence",
                        0.1f,
                        false,
                        false,
                        default,
                        false,
                        "low_depth_confidence"),
                    bboxApproach);
            }

            Assert.That(result.HasMetricMotion, Is.False);
            Assert.That(result.State,
                Is.EqualTo(DynamicMotionState.Approaching));
            Assert.That(result.State,
                Is.Not.EqualTo(DynamicMotionState.Receding));
        }

        [Test]
        public void ThreeHertzCadenceStillProducesMetricApproach()
        {
            var estimator = new MetricMotionEstimator();
            MotionEstimate result = null;
            for (int i = 0; i < 4; i++)
            {
                double timestamp = i / 3.0;
                result = estimator.Estimate(
                    timestamp,
                    31,
                    Metric(31, timestamp, 2.0f - i * 0.20f),
                    BBoxFallback());
            }

            Assert.That(result.HasMetricMotion, Is.True);
            Assert.That(result.State,
                Is.EqualTo(DynamicMotionState.Approaching));
        }

        private static PersonDistanceMeasurement Metric(
            int trackId,
            double timestamp,
            float distance)
        {
            return new PersonDistanceMeasurement(
                trackId,
                timestamp,
                PersonDistanceSource.EnvironmentDepth,
                true,
                distance,
                distance,
                1f,
                7,
                7,
                0f,
                0.1f);
        }

        private static MotionEstimate BBoxFallback()
        {
            return new MotionEstimate(
                DynamicMotionState.Unknown,
                0f,
                null,
                0f,
                1,
                0.0,
                0f);
        }
    }
}
