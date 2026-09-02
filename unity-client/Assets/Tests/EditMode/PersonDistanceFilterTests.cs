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
        public void TwoConnectedDepthSamplesProvideSafetyOnly()
        {
            var filter = new PersonDistanceFilter();
            PersonDistanceMeasurement result = filter.UpdateMetric(
                3,
                1.0,
                new[] { 1.1f, 1.2f },
                7,
                0.08f);

            Assert.That(result.Source,
                Is.EqualTo(PersonDistanceSource.EnvironmentDepth));
            Assert.That(result.HasMetricDistance, Is.False);
            Assert.That(result.HasReliableSafetyDistance, Is.True);
            Assert.That(result.SafetySupportCount, Is.EqualTo(2));
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
            Assert.That(conflict.IsMetricReliable, Is.False);
            Assert.That(conflict.DepthRejectedReason,
                Is.EqualTo("bbox_depth_conflict"));
            Assert.That(conflict.HasReliableSafetyDistance, Is.True);
        }

        [Test]
        public void RejectedTrackingDepthCanStillProvideSafetyDistance()
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
            Assert.That(rejected.HasReliableSafetyDistance, Is.True);
            Assert.That(fallback.Source,
                Is.EqualTo(PersonDistanceSource.EnvironmentDepth));
        }

        [Test]
        public void ExpandedSamplingDoesNotPenalizeSupportedForegroundCluster()
        {
            var filter = new PersonDistanceFilter();
            PersonDistanceMeasurement result = filter.UpdateMetric(
                9,
                1.0,
                new[] { 1.00f, 1.02f, 0.98f },
                25,
                0.20f,
                null,
                0.45f,
                float.PositiveInfinity,
                0.35f);

            Assert.That(result.HasReliableMetricDistance, Is.True);
            Assert.That(result.ValidSampleCount, Is.EqualTo(3));
            Assert.That(result.Confidence, Is.GreaterThanOrEqualTo(0.45f));
        }

        [Test]
        public void LargeBoundingBoxDoesNotGloballyRejectMetricDepth()
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

            Assert.That(reliable, Is.True);
            Assert.That(reason, Is.Empty);
        }

        [Test]
        public void DenseBackgroundTrackingDoesNotHideCloserSafetyCluster()
        {
            PersonDepthSample[] samples =
            {
                Body(0.82f), Body(0.86f), Body(0.89f),
                Background(2.55f), Background(2.58f),
                Background(2.61f), Background(2.64f),
                Background(2.67f), Background(2.70f),
                Background(2.73f)
            };

            bool selected = PersonDistanceFilter.TrySelectTorsoSupportedCluster(
                samples,
                0.20f,
                6f,
                0.35f,
                3,
                out float tracking,
                out float safety,
                out int count,
                out int torsoSupport,
                out int safetySupport,
                out float dispersion,
                out string reason);

            Assert.That(selected, Is.True);
            Assert.That(tracking, Is.EqualTo(2.64f).Within(0.001f));
            Assert.That(safety, Is.LessThan(0.90f));
            Assert.That(count, Is.EqualTo(7));
            Assert.That(torsoSupport, Is.EqualTo(7));
            Assert.That(safetySupport, Is.GreaterThanOrEqualTo(2));
            Assert.That(dispersion, Is.EqualTo(0.18f).Within(0.001f));
            Assert.That(reason, Is.EqualTo("connected_tracking_and_safety"));
        }

        [Test]
        public void IsolatedNearNoiseDoesNotBecomePersonSafetyDistance()
        {
            PersonDepthSample[] samples =
            {
                Background(0.40f),
                Body(1.82f), Body(1.90f), Body(1.98f),
                Body(2.04f), Body(2.08f),
                Background(3.10f), Background(3.12f), Background(3.14f)
            };

            var filter = new PersonDistanceFilter();
            PersonDistanceMeasurement result = filter.UpdateMetric(
                17,
                1.0,
                samples,
                samples.Length,
                0.18f);

            Assert.That(result.HasReliableMetricDistance, Is.True);
            Assert.That(result.TrackingDistanceMeters,
                Is.EqualTo(1.98f).Within(0.001f));
            Assert.That(result.SafetyDistanceMeters, Is.GreaterThan(1.70f));
            Assert.That(result.SafetyDistanceMeters, Is.LessThanOrEqualTo(
                result.TrackingDistanceMeters));
        }

        [Test]
        public void TrackingMedianAndSupportedSafetyPercentileAreSeparated()
        {
            var filter = new PersonDistanceFilter();
            PersonDistanceMeasurement result = filter.UpdateMetric(
                21,
                1.0,
                new[]
                {
                    Body(0.70f), Body(0.78f), Body(0.82f),
                    Body(0.90f), Body(0.94f)
                },
                5,
                0.30f);

            Assert.That(result.TrackingDistanceMeters,
                Is.EqualTo(0.82f).Within(0.001f));
            Assert.That(result.RawSafetyDistanceMeters,
                Is.EqualTo(0.764f).Within(0.001f));
            Assert.That(result.SafetyDistanceMeters,
                Is.LessThan(result.TrackingDistanceMeters));
            Assert.That(result.HasReliableSafetyDistance, Is.True);
            Assert.That(result.ClusterSelectionReason,
                Is.EqualTo("connected_tracking_and_safety"));
        }

        [Test]
        public void OffCenterClusterDoesNotRequireTorsoSupport()
        {
            bool selected = PersonDistanceFilter.TrySelectTorsoSupportedCluster(
                new[]
                {
                    Background(0.80f), Background(0.85f),
                    Background(0.90f), Background(0.95f)
                },
                0.20f,
                6f,
                0.35f,
                3,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out string reason);

            Assert.That(selected, Is.True);
            Assert.That(reason, Is.EqualTo("connected_tracking_and_safety"));
        }

        [Test]
        public void OffCenterConnectedForegroundBecomesSafetyCluster()
        {
            PersonDepthSample[] samples =
            {
                Indexed(0, 0.08f, 0.25f, 0.92f),
                Indexed(1, 0.20f, 0.33f, 0.98f),
                Indexed(2, 0.70f, 0.30f, 3.00f),
                Indexed(3, 0.78f, 0.36f, 3.04f),
                Indexed(4, 0.84f, 0.42f, 3.08f)
            };

            PersonDistanceFilter.TrySelectConnectedClusters(
                samples,
                0.20f,
                6f,
                -1f,
                out PersonDepthClusterMeasurement tracking,
                out PersonDepthClusterMeasurement safety);

            Assert.That(tracking.Available, Is.True);
            Assert.That(tracking.DistanceMeters, Is.GreaterThan(2.9f));
            Assert.That(safety.Available, Is.True);
            Assert.That(safety.DistanceMeters, Is.LessThan(1.0f));
            Assert.That(safety.SupportCount, Is.EqualTo(2));
            Assert.That(safety.Confidence, Is.GreaterThanOrEqualTo(0.55f));
        }

        [Test]
        public void DisconnectedNearSamplesDoNotCreateSafetyCluster()
        {
            PersonDepthSample[] samples =
            {
                Indexed(0, 0.05f, 0.20f, 0.72f),
                Indexed(1, 0.80f, 0.75f, 0.76f),
                Indexed(2, 0.35f, 0.30f, 2.20f),
                Indexed(3, 0.45f, 0.38f, 2.24f),
                Indexed(4, 0.55f, 0.46f, 2.28f)
            };

            PersonDistanceFilter.TrySelectConnectedClusters(
                samples,
                0.20f,
                6f,
                -1f,
                out _,
                out PersonDepthClusterMeasurement safety);

            Assert.That(safety.Available, Is.True);
            Assert.That(safety.DistanceMeters, Is.GreaterThan(2.0f));
            Assert.That((safety.SelectionMask & 0b11UL), Is.Zero);
        }

        [Test]
        public void PreviousTrackingDistanceStabilizesClusterSelection()
        {
            PersonDepthSample[] samples =
            {
                Indexed(0, 0.20f, 0.30f, 1.18f),
                Indexed(1, 0.32f, 0.36f, 1.22f),
                Indexed(2, 0.44f, 0.42f, 1.26f),
                Indexed(3, 0.62f, 0.30f, 2.90f),
                Indexed(4, 0.70f, 0.36f, 2.94f),
                Indexed(5, 0.78f, 0.42f, 2.98f),
                Indexed(6, 0.86f, 0.48f, 3.02f)
            };

            PersonDistanceFilter.TrySelectConnectedClusters(
                samples,
                0.20f,
                6f,
                1.20f,
                out PersonDepthClusterMeasurement tracking,
                out _);

            Assert.That(tracking.DistanceMeters,
                Is.EqualTo(1.22f).Within(0.01f));
            Assert.That(tracking.TemporalConsistency, Is.GreaterThan(0.9f));
        }

        private static PersonDepthSample Body(float distanceMeters)
        {
            return new PersonDepthSample(
                distanceMeters,
                2f,
                true,
                true);
        }

        private static PersonDepthSample Background(float distanceMeters)
        {
            return new PersonDepthSample(
                distanceMeters,
                1f,
                false,
                false);
        }

        private static PersonDepthSample Indexed(
            int index,
            float x,
            float y,
            float distanceMeters)
        {
            return new PersonDepthSample(
                index,
                new UnityEngine.Vector2(x, y),
                new UnityEngine.Vector2(x, 1f - y),
                distanceMeters,
                1f,
                false,
                false,
                true,
                new UnityEngine.Vector3(x, y, distanceMeters));
        }
    }
}
