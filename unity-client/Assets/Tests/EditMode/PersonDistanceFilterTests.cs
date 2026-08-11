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
    }
}
