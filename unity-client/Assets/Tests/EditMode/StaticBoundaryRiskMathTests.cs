using NUnit.Framework;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class StaticBoundaryRiskMathTests
    {
        [Test]
        public void UserStateChangesThresholdWithoutChangingPhysicalRisk()
        {
            float riskAtRest = StaticBoundaryRiskMath.WeightedHeadRisk(
                0.8f,
                0.6f,
                0f,
                0.5f,
                0.3f,
                0.3f,
                0f,
                0.15f);
            float riskInMotion = StaticBoundaryRiskMath.WeightedHeadRisk(
                0.8f,
                0.6f,
                0f,
                0.5f,
                0.3f,
                0.3f,
                0f,
                0.15f);

            Assert.That(riskInMotion, Is.EqualTo(riskAtRest));
            Assert.That(
                StaticBoundaryRiskMath.EffectiveOnThreshold(
                    0.65f,
                    0.45f,
                    0f),
                Is.EqualTo(0.65f).Within(0.0001f));
            Assert.That(
                StaticBoundaryRiskMath.EffectiveOnThreshold(
                    0.65f,
                    0.45f,
                    1f),
                Is.EqualTo(0.45f).Within(0.0001f));
        }

        [Test]
        public void ReversedThresholdEndpointsRemainSafe()
        {
            Assert.That(
                StaticBoundaryRiskMath.EffectiveOnThreshold(
                    0.40f,
                    0.80f,
                    0f),
                Is.EqualTo(0.80f).Within(0.0001f));
            Assert.That(
                StaticBoundaryRiskMath.EffectiveOnThreshold(
                    0.40f,
                    0.80f,
                    1f),
                Is.EqualTo(0.40f).Within(0.0001f));
        }

        [Test]
        public void UnreachableWallProducesZeroHandRisk()
        {
            float gate = StaticBoundaryRiskMath.ReachGate(
                1.0f,
                0.4f,
                0.7f,
                0.15f);
            float risk = StaticBoundaryRiskMath.WeightedHandRisk(
                gate,
                1f,
                1f,
                0.4f,
                0.6f);

            Assert.That(gate, Is.Zero);
            Assert.That(risk, Is.Zero);
        }

        [Test]
        public void ReachableApproachingHandProducesRisk()
        {
            float gate = StaticBoundaryRiskMath.ReachGate(
                0.08f,
                0.55f,
                0.70f,
                0.15f);
            float distanceRisk = StaticBoundaryRiskMath.DistanceRisk(
                0.08f,
                0.50f);
            float ttcRisk = StaticBoundaryRiskMath.TimeToCollisionRisk(
                0.08f,
                0.40f,
                1.0f,
                0.05f);
            float risk = StaticBoundaryRiskMath.WeightedHandRisk(
                gate,
                distanceRisk,
                ttcRisk,
                0.4f,
                0.6f);

            Assert.That(gate, Is.GreaterThan(0f));
            Assert.That(risk, Is.GreaterThan(0.5f));
        }

        [Test]
        public void ZeroAndInvalidDenominatorsStayFinite()
        {
            float distance = StaticBoundaryRiskMath.DistanceRisk(0.2f, 0f);
            float ttc = StaticBoundaryRiskMath.TimeToCollisionRisk(
                0.2f,
                float.PositiveInfinity,
                0f);
            float acceleration = StaticBoundaryRiskMath.AccelerationRisk(
                1f,
                0f);

            Assert.That(float.IsNaN(distance), Is.False);
            Assert.That(float.IsInfinity(distance), Is.False);
            Assert.That(float.IsNaN(ttc), Is.False);
            Assert.That(float.IsInfinity(ttc), Is.False);
            Assert.That(float.IsNaN(acceleration), Is.False);
            Assert.That(float.IsInfinity(acceleration), Is.False);
        }

        [Test]
        public void NetTranslationCancelsReturnToStart()
        {
            float speed = StaticBoundaryRiskMath.NetTranslationSpeed(
                new Vector3(1f, 2f, 3f),
                new Vector3(1f, 2f, 3f),
                0.5f);

            Assert.That(speed, Is.Zero);
        }

        [Test]
        public void SpeedRiskIsZeroForRetreatAndLateralMotion()
        {
            Assert.That(
                StaticBoundaryRiskMath.SpeedRisk(-0.6f, 0.05f, 0.80f),
                Is.Zero);
            Assert.That(
                StaticBoundaryRiskMath.SpeedRisk(0f, 0.05f, 0.80f),
                Is.Zero);
        }

        [Test]
        public void FasterApproachRaisesOneMeterHeadRiskByAtLeastPointTwo()
        {
            float distanceRisk = StaticBoundaryRiskMath.DistanceRisk(1f, 1.5f);
            float slowSpeedRisk = StaticBoundaryRiskMath.SpeedRisk(
                0.1f,
                0.05f,
                0.80f);
            float fastSpeedRisk = StaticBoundaryRiskMath.SpeedRisk(
                0.6f,
                0.05f,
                0.80f);
            float slow = StaticBoundaryRiskMath.WeightedHeadRiskWithSpeed(
                distanceRisk,
                slowSpeedRisk,
                StaticBoundaryRiskMath.TimeToCollisionRisk(1f, 0.1f, 2f),
                0.2f,
                0.45f,
                0.30f,
                0.20f,
                0.05f);
            float fast = StaticBoundaryRiskMath.WeightedHeadRiskWithSpeed(
                distanceRisk,
                fastSpeedRisk,
                StaticBoundaryRiskMath.TimeToCollisionRisk(1f, 0.6f, 2f),
                0.2f,
                0.45f,
                0.30f,
                0.20f,
                0.05f);

            Assert.That(fast - slow, Is.GreaterThanOrEqualTo(0.20f));
        }

        [Test]
        public void RobustPlaneKeepsWallNormalWithTwentyPercentOutliers()
        {
            var points = new Vector3[10];
            var normals = new Vector3[10];
            var inliers = new bool[10];
            for (int i = 0; i < 8; i++)
            {
                points[i] = new Vector3(
                    (i % 4 - 1.5f) * 0.2f,
                    (i / 4 - 0.5f) * 0.3f,
                    2f + (i % 2 == 0 ? 0.002f : -0.002f));
                normals[i] = Vector3.back;
            }
            points[8] = new Vector3(-0.5f, 0.1f, 2.4f);
            points[9] = new Vector3(0.5f, -0.1f, 1.6f);
            normals[8] = Vector3.left;
            normals[9] = Vector3.right;

            bool fitted = RobustSpatialPlaneEstimator.TryFit(
                points,
                normals,
                points.Length,
                inliers,
                out Vector3 center,
                out Vector3 normal,
                out int inlierCount);

            Assert.That(fitted, Is.True);
            Assert.That(inlierCount, Is.EqualTo(8));
            Assert.That(Mathf.Abs(Vector3.Dot(normal, Vector3.back)),
                Is.GreaterThan(Mathf.Cos(5f * Mathf.Deg2Rad)));
            Assert.That(center.z, Is.EqualTo(2f).Within(0.01f));
        }

        [Test]
        public void RobustPlaneRejectsSplitSurfacesWithoutSixtyPercentSupport()
        {
            var points = new Vector3[10];
            var normals = new Vector3[10];
            var inliers = new bool[10];
            for (int i = 0; i < 5; i++)
            {
                points[i] = new Vector3(i * 0.1f, i * i * 0.03f, 2f);
                normals[i] = Vector3.back;
                points[i + 5] = new Vector3(0.7f, i * 0.1f,
                    1.3f + i * i * 0.03f);
                normals[i + 5] = Vector3.left;
            }

            bool fitted = RobustSpatialPlaneEstimator.TryFit(
                points,
                normals,
                points.Length,
                inliers,
                out _,
                out _,
                out _);

            Assert.That(fitted, Is.False);
        }

        [Test]
        public void NormalStabilizerRejectsOneJumpAndAcceptsRepeatedPlane()
        {
            var stabilizer = new PresentationNormalStabilizer();
            Vector3 initial = stabilizer.Update(Vector3.back, 0.0);
            Vector3 wrong = Quaternion.AngleAxis(40f, Vector3.up)
                * Vector3.back;

            Vector3 once = stabilizer.Update(wrong, 0.1);
            Vector3 twice = stabilizer.Update(wrong, 0.2);

            Assert.That(Vector3.Angle(initial, once), Is.LessThan(0.01f));
            Assert.That(Vector3.Angle(initial, twice),
                Is.GreaterThan(0f));
            Assert.That(Vector3.Angle(initial, twice),
                Is.LessThanOrEqualTo(12.01f),
                "The 60 degrees/second rate limit must cap elapsed time since the last accepted plane.");
        }

        [Test]
        public void NormalStabilizerAcceptsOrthogonalNewSurfaceImmediately()
        {
            var stabilizer = new PresentationNormalStabilizer();
            Vector3 first = stabilizer.UpdateForSurface(
                101,
                Vector3.back,
                0.0);
            Vector3 second = stabilizer.UpdateForSurface(
                202,
                Vector3.left,
                0.05);

            Assert.That(Vector3.Angle(first, Vector3.back), Is.LessThan(0.01f));
            Assert.That(Vector3.Angle(second, Vector3.left), Is.LessThan(0.01f));
            Assert.That(stabilizer.LastInputAngleDegrees, Is.EqualTo(0f));
        }

        [Test]
        public void ConfirmedNormalTargetConvergesOnEveryFollowingSample()
        {
            var stabilizer = new PresentationNormalStabilizer();
            Vector3 target = Quaternion.AngleAxis(50f, Vector3.up)
                * Vector3.back;
            Vector3 initial = stabilizer.Update(Vector3.back, 0.0);
            Vector3 rejectedOnce = stabilizer.Update(target, 0.1);
            Vector3 acceptedTwice = stabilizer.Update(target, 0.2);
            Vector3 continued = stabilizer.Update(target, 0.3);

            Assert.That(Vector3.Angle(initial, rejectedOnce), Is.LessThan(0.01f));
            Assert.That(Vector3.Angle(initial, acceptedTwice), Is.GreaterThan(0f));
            Assert.That(Vector3.Angle(initial, continued),
                Is.GreaterThan(Vector3.Angle(initial, acceptedTwice)));
        }

        [Test]
        public void RobustPlaneUsesCoherentNormalsForCollinearHandFan()
        {
            Vector3[] points =
            {
                new Vector3(-0.12f, 1.1f, 0.8f),
                new Vector3(0f, 1.1f, 0.8f),
                new Vector3(0.12f, 1.1f, 0.8f)
            };
            Vector3[] normals =
            {
                Vector3.back,
                Vector3.back,
                Vector3.back
            };
            var inliers = new bool[3];

            bool fitted = RobustSpatialPlaneEstimator.TryFit(
                points,
                normals,
                points.Length,
                inliers,
                out _,
                out Vector3 normal,
                out int inlierCount);

            Assert.That(fitted, Is.True);
            Assert.That(inlierCount, Is.EqualTo(3));
            Assert.That(Mathf.Abs(Vector3.Dot(normal, Vector3.back)),
                Is.GreaterThan(0.999f));
        }

        [Test]
        public void CollinearHandFanWithoutReliableNormalsHasNoPlaneGeometry()
        {
            Vector3[] points =
            {
                new Vector3(-0.12f, 1.1f, 0.8f),
                new Vector3(0f, 1.1f, 0.8f),
                new Vector3(0.12f, 1.1f, 0.8f)
            };
            var normals = new Vector3[3];
            var inliers = new bool[3];

            bool fitted = RobustSpatialPlaneEstimator.TryFit(
                points,
                normals,
                points.Length,
                inliers,
                out _,
                out _,
                out _);

            Assert.That(fitted, Is.False);
        }
    }
}
