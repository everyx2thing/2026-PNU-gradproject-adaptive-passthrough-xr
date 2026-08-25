using NUnit.Framework;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class PersonDistanceFilterTests
    {
        [Test]
        public void NearestCoherentClusterRejectsBackgroundWall()
        {
            float median;
            int count;
            float dispersion;
            bool selected =
                PersonDistanceFilter.TrySelectNearestCluster(
                    new[]
                    {
                        1.18f, 1.20f, 1.22f, 1.19f, 1.21f,
                        3.80f, 3.90f
                    },
                    0.20f,
                    6f,
                    0.35f,
                    3,
                    out median,
                    out count,
                    out dispersion);

            Assert.That(selected, Is.True);
            Assert.That(median, Is.EqualTo(1.20f).Within(0.001f));
            Assert.That(count, Is.EqualTo(5));
            Assert.That(dispersion, Is.EqualTo(0.04f).Within(0.001f));
        }

        [Test]
        public void TooFewDepthSamplesUsesBoundingBoxFallback()
        {
            var filter = new PersonDistanceFilter();
            PersonDistanceMeasurement result = filter.UpdateMetric(
                3,
                1.0,
                new[] { 1.1f, 1.2f },
                7,
                0.08f);

            Assert.That(
                result.Source,
                Is.EqualTo(PersonDistanceSource.BoundingBoxProxy));
            Assert.That(result.HasMetricDistance, Is.False);
            Assert.That(result.BoundingBoxArea, Is.EqualTo(0.08f));
        }

        [Test]
        public void SingleLargeJumpIsSuppressed()
        {
            var filter = new PersonDistanceFilter();
            PersonDistanceMeasurement baseline = filter.UpdateMetric(
                1,
                0.0,
                new[] { 1f, 1.02f, 0.98f, 1.01f },
                4,
                0.1f);
            PersonDistanceMeasurement jump = filter.UpdateMetric(
                1,
                0.1,
                new[] { 3f, 3.02f, 2.98f, 3.01f },
                4,
                0.1f);

            Assert.That(baseline.HasMetricDistance, Is.True);
            Assert.That(
                jump.FilteredDistanceMeters,
                Is.EqualTo(baseline.FilteredDistanceMeters)
                    .Within(0.001f));
            Assert.That(
                jump.FailureReason,
                Is.EqualTo("distance_jump_suppressed"));
        }

        [Test]
        public void MetricValueIsHeldThenFallsBack()
        {
            var filter = new PersonDistanceFilter();
            filter.UpdateMetric(
                1,
                0.0,
                new[] { 1f, 1.02f, 0.98f },
                3,
                0.1f);

            PersonDistanceMeasurement held =
                filter.GetHeldOrBoundingBoxFallback(
                    1,
                    0.3,
                    0.1f,
                    "no_hit");
            PersonDistanceMeasurement fallback =
                filter.GetHeldOrBoundingBoxFallback(
                    1,
                    0.6,
                    0.1f,
                    "no_hit");

            Assert.That(
                held.Source,
                Is.EqualTo(PersonDistanceSource.EnvironmentDepth));
            Assert.That(held.HasMetricDistance, Is.True);
            Assert.That(
                fallback.Source,
                Is.EqualTo(PersonDistanceSource.BoundingBoxProxy));
        }

        [Test]
        public void CenterSupportCanRejectCloserEdgeCluster()
        {
            float median;
            int count;
            float dispersion;
            bool selected =
                PersonDistanceFilter.TrySelectSupportedForegroundCluster(
                    new[] { 0.50f, 0.52f, 0.54f, 1.48f, 1.50f, 1.52f, 1.54f },
                    new[] { 0.1f, 0.1f, 0.1f, 2f, 2f, 2f, 2f },
                    0.20f,
                    6f,
                    0.20f,
                    3,
                    out median,
                    out count,
                    out dispersion);

            Assert.That(selected, Is.True);
            Assert.That(median, Is.EqualTo(1.51f).Within(0.001f));
            Assert.That(count, Is.EqualTo(4));
        }

        [Test]
        public void GrowingBoxSuppressesZeroPointThreeTwoToOnePointTwoTwoJump()
        {
            var filter = new PersonDistanceFilter();
            PersonDistanceMeasurement baseline = filter.UpdateMetric(
                4,
                0.0,
                new[] { 0.31f, 0.32f, 0.33f, 0.32f },
                4,
                0.10f);
            PersonDistanceMeasurement conflict = filter.UpdateMetric(
                4,
                0.2,
                new[] { 1.20f, 1.22f, 1.24f, 1.22f },
                4,
                0.18f);

            Assert.That(conflict.BoundingBoxDepthConflict, Is.True);
            Assert.That(conflict.FilteredDistanceMeters,
                Is.EqualTo(baseline.FilteredDistanceMeters).Within(0.001f));
            Assert.That(conflict.Confidence, Is.LessThan(0.45f));
        }

        [Test]
        public void BackgroundDepthIsLoggedButNotCommittedToFilter()
        {
            var filter = new PersonDistanceFilter();
            PersonDistanceMeasurement rejected = filter.UpdateMetric(
                8,
                1.0,
                new[] { 2.64f, 2.70f, 2.76f, 2.72f },
                4,
                0.70f,
                null,
                0.45f,
                2.0f,
                0.35f);
            PersonDistanceMeasurement fallback =
                filter.GetHeldOrBoundingBoxFallback(
                    8,
                    1.1,
                    0.70f,
                    "no_hit");

            Assert.That(rejected.RawDistanceMeters,
                Is.EqualTo(2.71f).Within(0.04f));
            Assert.That(rejected.IsMetricReliable, Is.False);
            Assert.That(rejected.DepthRejectedReason,
                Is.EqualTo("background_depth_suspected"));
            Assert.That(fallback.Source,
                Is.EqualTo(PersonDistanceSource.BoundingBoxProxy));
        }

        [Test]
        public void LargeBoundingBoxRejectsTwoPointSevenMeterDepth()
        {
            var measurement = new PersonDistanceMeasurement(
                3,
                1.0,
                PersonDistanceSource.EnvironmentDepth,
                true,
                2.7f,
                2.7f,
                0.8f,
                13,
                10,
                0f,
                0.60f,
                null,
                0.10f);
            string reason;
            bool reliable = PersonDepthReliability.IsReliable(
                measurement,
                new NormalizedBoundingBox(0.5f, 0.5f, 0.75f, 0.90f),
                out reason);

            Assert.That(reliable, Is.False);
            Assert.That(reason, Is.EqualTo("background_depth_suspected"));
        }
    }
}
