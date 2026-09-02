using NUnit.Framework;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class PersonDepthPresentationFixTests
    {
        [Test]
        public void BoxCoordinatesMapTopDownToCameraViewportOnce()
        {
            var box = new NormalizedBoundingBox(0.5f, 0.4f, 0.4f, 0.6f);

            Vector2 topLeft = PersonImageCoordinates
                .BoxRelativeToCameraViewport(box, Vector2.zero);
            Vector2 center = PersonImageCoordinates
                .BoxRelativeToCameraViewport(box, new Vector2(0.5f, 0.5f));
            Vector2 bottomRight = PersonImageCoordinates
                .BoxRelativeToCameraViewport(box, Vector2.one);

            Assert.That(topLeft.x, Is.EqualTo(0.3f).Within(0.001f));
            Assert.That(topLeft.y, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(center.x, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(center.y, Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(bottomRight.x, Is.EqualTo(0.7f).Within(0.001f));
            Assert.That(bottomRight.y, Is.EqualTo(0.3f).Within(0.001f));
        }

        [Test]
        public void SelectedClusterMaskUsesAllTrackingWorldPoints()
        {
            PersonDepthSample[] samples =
            {
                Sample(0, 1.95f, new Vector3(-0.05f, 1.25f, 1.95f), true),
                Sample(1, 2.00f, new Vector3(0.00f, 1.30f, 2.00f), true),
                Sample(2, 2.04f, new Vector3(0.05f, 1.28f, 2.04f), false),
                Sample(3, 3.50f, new Vector3(-0.4f, 1.0f, 3.50f), false),
                Sample(4, 3.55f, new Vector3(0.0f, 1.0f, 3.55f), false),
                Sample(5, 3.60f, new Vector3(0.4f, 1.0f, 3.60f), false)
            };
            var filter = new PersonDistanceFilter();

            PersonDistanceMeasurement result = filter.UpdateMetric(
                4,
                1.0,
                samples,
                samples.Length,
                0.12f);
            ulong selected = filter.LatestTrackingSelectionMask;
            bool hasCenter = PersonDepthWorldCenter.TryCalculate(
                samples,
                selected,
                out Vector3 center);

            Assert.That(result.TrackingDistanceMeters,
                Is.EqualTo(2.00f).Within(0.001f));
            Assert.That((selected & (1UL << 0)) != 0UL, Is.True);
            Assert.That((selected & (1UL << 1)) != 0UL, Is.True);
            Assert.That((selected & (1UL << 2)) != 0UL, Is.True);
            Assert.That((selected & (1UL << 3)) != 0UL, Is.False);
            Assert.That(hasCenter, Is.True);
            Assert.That(center.x, Is.EqualTo(0f).Within(0.04f));
            Assert.That(center.z, Is.EqualTo(2f).Within(0.05f));
        }

        [Test]
        public void RevealUsesContinuousRiskWithoutDuplicateFarBlock()
        {
            var tracker = new PersonRevealEligibilityTracker();
            DynamicRiskAssessment assessment = Assessment(
                1,
                0.90f,
                true,
                3.0f,
                DynamicMotionState.Steady,
                0f,
                0f,
                0.9f);

            Assert.That(tracker.Evaluate(assessment, true, true, 0.60f),
                Is.True);
        }

        [Test]
        public void RiskQualifiedBboxRevealIsImmediate()
        {
            var tracker = new PersonRevealEligibilityTracker();
            DynamicRiskAssessment steady = Assessment(
                2,
                0.90f,
                false,
                0f,
                DynamicMotionState.Steady,
                0f,
                0f,
                0.9f);
            DynamicRiskAssessment approaching = Assessment(
                2,
                0.90f,
                false,
                0f,
                DynamicMotionState.Approaching,
                0.18f,
                0f,
                0.8f);

            Assert.That(tracker.Evaluate(steady, true, true, 0.60f),
                Is.True);
            Assert.That(tracker.Evaluate(approaching, true, true, 0.60f),
                Is.True);
        }

        [Test]
        public void UltraCloseForceRemainsImmediate()
        {
            var tracker = new PersonRevealEligibilityTracker();
            DynamicRiskAssessment forced = Assessment(
                3,
                0.10f,
                false,
                0f,
                DynamicMotionState.Unknown,
                0f,
                0f,
                0f,
                true);

            Assert.That(tracker.Evaluate(forced, true, false, 0.60f),
                Is.True);
        }

        [Test]
        public void PersonRevealUsesPointSixPointFourFiveHysteresis()
        {
            var tracker = new PersonRevealEligibilityTracker();
            DynamicRiskAssessment enter = Assessment(
                8, 0.60f, false, 0f,
                DynamicMotionState.Steady, 0f, 0f, 1f);
            DynamicRiskAssessment sustain = Assessment(
                8, 0.50f, false, 0f,
                DynamicMotionState.Steady, 0f, 0f, 1f);
            DynamicRiskAssessment exit = Assessment(
                8, 0.44f, false, 0f,
                DynamicMotionState.Steady, 0f, 0f, 1f);

            Assert.That(tracker.Evaluate(
                enter, true, 0.60f, 0.45f), Is.True);
            Assert.That(tracker.Evaluate(
                sustain, true, 0.60f, 0.45f), Is.True);
            Assert.That(tracker.Evaluate(
                exit, true, 0.60f, 0.45f), Is.False);
        }

        [Test]
        public void GeometryPredictionRebasesCaptureLatencyOnlyOnce()
        {
            HazardPresentationGeometry geometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    9,
                    HazardVisualKind.PersonCapsule,
                    new Vector3(0f, 1f, 2f),
                    Vector3.back,
                    Vector3.up,
                    0.5f,
                    1.2f,
                    1.0,
                    1f,
                    SpatialObstacleSource.EnvironmentDepth,
                    SpatialProbeOwner.Head,
                    SpatialProbePurpose.Standard)
                .WithVelocity(Vector3.right);

            HazardPresentationGeometry predicted = geometry.PredictedTo(
                1.4,
                0.5f);
            HazardPresentationGeometry residual = predicted.PredictedTo(
                1.5,
                0.5f);

            Assert.That(predicted.Center.x,
                Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(residual.Center.x,
                Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(residual.CaptureTimestampSeconds,
                Is.EqualTo(1.5).Within(0.0001));
        }

        private static PersonDepthSample Sample(
            int index,
            float distance,
            Vector3 worldPoint,
            bool torso)
        {
            return new PersonDepthSample(
                index,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                distance,
                torso ? 2f : 1f,
                torso,
                torso,
                true,
                worldPoint);
        }

        private static DynamicRiskAssessment Assessment(
            int trackId,
            float score,
            bool metric,
            float distance,
            DynamicMotionState motionState,
            float scaleRate,
            float closingSpeed,
            float reliability,
            bool force = false)
        {
            var box = new NormalizedBoundingBox(0.5f, 0.5f, 0.2f, 0.5f);
            var detection = new DynamicObjectDetection("person", 0.9f, box);
            var location = new RelativeLocationEstimate(
                HorizontalZone.Center,
                "front-center",
                metric ? DistanceBand.Mid : DistanceBand.Far,
                0f,
                box.Area,
                metric
                    ? PersonDistanceSource.EnvironmentDepth
                    : PersonDistanceSource.BoundingBoxProxy,
                metric,
                distance,
                distance,
                metric ? 0.9f : 0f,
                false,
                Vector3.zero,
                0.05f,
                metric,
                metric ? string.Empty : "depth_unavailable",
                false,
                Vector3.zero,
                default,
                PersonPresentationGeometrySource.Unavailable,
                0f,
                distance,
                distance,
                metric ? 0.9f : 0f,
                metric ? 3 : 0,
                metric ? 2 : 0);
            var motion = new MotionEstimate(
                motionState,
                scaleRate,
                null,
                0f,
                4,
                0.6,
                reliability,
                metric
                    ? PersonDistanceSource.EnvironmentDepth
                    : PersonDistanceSource.BoundingBoxProxy,
                metric,
                closingSpeed,
                null);
            return new DynamicRiskAssessment(
                trackId,
                detection,
                location,
                motion,
                score,
                score >= 0.75f
                    ? DynamicRiskLevel.Danger
                    : DynamicRiskLevel.Warning,
                new string[0],
                new DynamicRiskBreakdown(0f, 0f, 0f, 0f, 0f, 0f, 1f),
                TrackLifecycle.Confirmed,
                true,
                0f,
                false,
                force,
                force);
        }
    }
}
