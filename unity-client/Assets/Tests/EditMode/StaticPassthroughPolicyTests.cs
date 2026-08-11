using NUnit.Framework;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class StaticPassthroughPolicyTests
    {
        [Test]
        public void MotionLowersHeadThresholdWithoutEnteringRiskScore()
        {
            var policy = new StaticBoundaryPolicy();

            StaticPassthroughDecision resting = policy.Evaluate(
                1L,
                0.0,
                Frame(headRisk: 0.55f, userState: 0f));
            StaticPassthroughDecision moving = policy.Evaluate(
                2L,
                0.1,
                Frame(headRisk: 0.55f, userState: 1f));

            Assert.That(resting.Enabled, Is.False);
            Assert.That(resting.HeadRisk, Is.EqualTo(0.55f));
            Assert.That(moving.HeadRisk, Is.EqualTo(0.55f));
            Assert.That(moving.Enabled, Is.True);
            Assert.That(moving.Cause, Is.EqualTo(StaticActivationCause.Head));
        }

        [Test]
        public void HeadHysteresisHoldsUntilOffThreshold()
        {
            var policy = new StaticBoundaryPolicy();

            StaticPassthroughDecision entered = policy.Evaluate(
                1L,
                0.0,
                Frame(headRisk: 0.70f));
            StaticPassthroughDecision held = policy.Evaluate(
                2L,
                0.1,
                Frame(headRisk: 0.60f));
            StaticPassthroughDecision minimumHeld = policy.Evaluate(
                3L,
                0.2,
                Frame(headRisk: 0.56f));
            StaticPassthroughDecision released = policy.Evaluate(
                4L,
                1.6,
                Frame(headRisk: 0.56f));

            Assert.That(entered.Enabled, Is.True);
            Assert.That(held.Enabled, Is.True);
            Assert.That(minimumHeld.Enabled, Is.True);
            Assert.That(
                minimumHeld.SourceDecision.FilterDecision.Reason,
                Is.EqualTo(PassthroughDecisionReason.MinimumHoldActive));
            Assert.That(released.Enabled, Is.False);
        }

        [Test]
        public void HandThresholdUsesHandDirection()
        {
            var policy = new StaticBoundaryPolicy();
            StaticBoundaryRiskFrame frame = Frame(
                headRisk: 0.1f,
                leftHandRisk: 0.90f,
                leftDirection: Vector3.left,
                rightHandRisk: 0.20f,
                rightDirection: Vector3.right);

            StaticPassthroughDecision decision = policy.Evaluate(
                1L,
                0.0,
                frame);

            Assert.That(decision.Enabled, Is.True);
            Assert.That(decision.Cause, Is.EqualTo(StaticActivationCause.Hand));
            Assert.That(decision.HazardDirectionAvailable, Is.True);
            Assert.That(decision.HazardDirectionWorld, Is.EqualTo(Vector3.left));
        }

        [Test]
        public void EmergencyRequiresApproachAndReleasesWhenApproachStops()
        {
            var policy = new StaticBoundaryPolicy();

            StaticPassthroughDecision stationary = policy.Evaluate(
                1L,
                0.0,
                Frame(headRisk: 0.1f, distance: 0.20f, headTowardSpeed: 0f));
            StaticPassthroughDecision approaching = policy.Evaluate(
                2L,
                0.1,
                Frame(headRisk: 0.1f, distance: 0.20f, headTowardSpeed: 0.2f));
            StaticPassthroughDecision stopped = policy.Evaluate(
                3L,
                0.2,
                Frame(headRisk: 0.1f, distance: 0.20f, headTowardSpeed: 0f));
            StaticPassthroughDecision released = policy.Evaluate(
                4L,
                1.6,
                Frame(headRisk: 0.1f, distance: 0.20f, headTowardSpeed: 0f));

            Assert.That(stationary.Enabled, Is.False);
            Assert.That(stationary.EmergencyTrigger, Is.False);
            Assert.That(approaching.Enabled, Is.True);
            Assert.That(approaching.Cause, Is.EqualTo(StaticActivationCause.Emergency));
            Assert.That(approaching.EmergencyHold, Is.True);
            Assert.That(stopped.EmergencyHold, Is.False);
            Assert.That(stopped.Enabled, Is.True);
            Assert.That(released.Enabled, Is.False);
        }

        [Test]
        public void AwareDoesNotEnablePassthrough()
        {
            var policy = new StaticBoundaryPolicy();

            StaticPassthroughDecision decision = policy.Evaluate(
                1L,
                0.0,
                Frame(headRisk: 0.45f, headTowardSpeed: 0.10f));

            Assert.That(decision.Enabled, Is.False);
            Assert.That(decision.WarningLevel, Is.EqualTo(StaticWarningLevel.Aware));
            Assert.That(decision.Cause, Is.EqualTo(StaticActivationCause.Head));
        }

        [Test]
        public void UnavailableFrameKeepsEnabledStateForMinimumHold()
        {
            var policy = new StaticBoundaryPolicy();
            policy.Evaluate(1L, 0.0, Frame(headRisk: 0.9f));

            StaticPassthroughDecision held = policy.Evaluate(
                2L,
                0.1,
                StaticBoundaryRiskFrame.Unavailable);
            StaticPassthroughDecision unavailable = policy.Evaluate(
                3L,
                1.6,
                StaticBoundaryRiskFrame.Unavailable);

            Assert.That(held.Enabled, Is.True);
            Assert.That(held.SourceDecision.Available, Is.False);
            Assert.That(held.HazardDirectionAvailable, Is.True);
            Assert.That(
                held.SourceDecision.FilterDecision.Reason,
                Is.EqualTo(PassthroughDecisionReason.MinimumHoldActive));
            Assert.That(unavailable.Enabled, Is.False);
            Assert.That(unavailable.SourceDecision.Available, Is.False);
            Assert.That(
                unavailable.SourceDecision.FilterDecision.Reason,
                Is.EqualTo(PassthroughDecisionReason.NoRiskInputs));
        }

        private static StaticBoundaryRiskFrame Frame(
            float headRisk = 0f,
            float userState = 0f,
            float distance = 1f,
            float headTowardSpeed = 0f,
            float leftHandRisk = 0f,
            Vector3 leftDirection = default,
            float rightHandRisk = 0f,
            Vector3 rightDirection = default)
        {
            var head = new StaticRiskMeasurement(
                true,
                distance,
                headTowardSpeed > 0f ? distance / headTowardSpeed : 0f,
                headTowardSpeed > 0f,
                headTowardSpeed,
                0f,
                headRisk,
                headRisk,
                0f,
                0f,
                headRisk);
            StaticHandRiskMeasurement left = Hand(
                0,
                leftHandRisk,
                leftDirection);
            StaticHandRiskMeasurement right = Hand(
                1,
                rightHandRisk,
                rightDirection);
            return new StaticBoundaryRiskFrame(
                1L,
                0.0,
                true,
                head,
                userState,
                true,
                left,
                right,
                Vector3.forward,
                true,
                2,
                0.7f);
        }

        private static StaticHandRiskMeasurement Hand(
            int wallIndex,
            float risk,
            Vector3 direction)
        {
            return new StaticHandRiskMeasurement(
                true,
                wallIndex,
                0.1f,
                risk > 0f ? 0.2f : 0f,
                0.1f,
                0.6f,
                risk > 0f ? 1f : 0f,
                risk,
                risk,
                risk,
                direction == default ? Vector3.forward : direction);
        }
    }
}
