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
    }
}
