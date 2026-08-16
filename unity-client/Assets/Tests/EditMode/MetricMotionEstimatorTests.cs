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
