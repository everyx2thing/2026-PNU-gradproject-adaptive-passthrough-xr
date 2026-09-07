using System;
using System.Reflection;
using NUnit.Framework;
using TeamVR.AdaptivePassthrough;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class RearHazardWarningPolicyTests
    {
        [Test]
        public void NonEmergencyRequiresRearAngleAndActualClosingSpeed()
        {
            var policy = CreatePolicy();

            RearHazardWarningSnapshot notRear = policy.Evaluate(
                1.0,
                Vector3.forward,
                Vector3.right,
                Hazards(DirectionAtYaw(119f), 0.75f, 0.20f),
                true,
                StaticRiskChannelMask.All);
            Assert.That(notRear.QualifyingNow, Is.False);

            RearHazardWarningSnapshot stationary = policy.Evaluate(
                1.1,
                Vector3.forward,
                Vector3.right,
                Hazards(DirectionAtYaw(130f), 0.75f, 0f),
                true,
                StaticRiskChannelMask.All);
            Assert.That(stationary.QualifyingNow, Is.False);

            RearHazardWarningSnapshot approaching = policy.Evaluate(
                1.2,
                Vector3.forward,
                Vector3.right,
                Hazards(DirectionAtYaw(130f), 0.75f, 0.075f),
                true,
                StaticRiskChannelMask.All);
            Assert.That(approaching.QualifyingNow, Is.True);
            Assert.That(approaching.WarningActive, Is.True);
        }

        [Test]
        public void EmergencyDistanceBypassesClosingSpeedButNotRearDirection()
        {
            var policy = CreatePolicy();

            RearHazardWarningSnapshot frontEmergency = policy.Evaluate(
                2.0,
                Vector3.forward,
                Vector3.right,
                Hazards(Vector3.forward, 0.25f, 0f),
                true,
                StaticRiskChannelMask.All);
            Assert.That(frontEmergency.QualifyingNow, Is.False);

            RearHazardWarningSnapshot rearEmergency = policy.Evaluate(
                2.1,
                Vector3.forward,
                Vector3.right,
                Hazards(Vector3.back, 0.25f, 0f),
                true,
                StaticRiskChannelMask.All);
            Assert.That(rearEmergency.QualifyingNow, Is.True);
            Assert.That(rearEmergency.Emergency, Is.True);
            Assert.That(rearEmergency.Intensity, Is.EqualTo(1f));
        }

        [Test]
        public void HorizontalYawIgnoresViewPitch()
        {
            var policy = CreatePolicy();
            Vector3 pitchedForward = Quaternion.Euler(65f, 0f, 0f)
                * Vector3.forward;

            RearHazardWarningSnapshot warning = policy.Evaluate(
                3.0,
                pitchedForward,
                Vector3.right,
                Hazards(Vector3.back, 0.8f, 0.15f),
                true,
                StaticRiskChannelMask.All);

            Assert.That(warning.QualifyingNow, Is.True);
            Assert.That(warning.AngleDegrees, Is.EqualTo(180f).Within(0.01f));
        }

        [Test]
        public void HapticsSelectLeftRightAndBothForRearDirection()
        {
            Assert.That(
                RearHazardWarningPolicy.ResolveHapticTarget(
                    DirectionAtYaw(-135f),
                    Vector3.right),
                Is.EqualTo(SafetyHapticTarget.Left));
            Assert.That(
                RearHazardWarningPolicy.ResolveHapticTarget(
                    DirectionAtYaw(135f),
                    Vector3.right),
                Is.EqualTo(SafetyHapticTarget.Right));
            Assert.That(
                RearHazardWarningPolicy.ResolveHapticTarget(
                    Vector3.back,
                    Vector3.right),
                Is.EqualTo(SafetyHapticTarget.Both));
        }

        [Test]
        public void CloserDistanceAndShorterTtcIncreaseIntensity()
        {
            var settings = new RearHazardWarningSettings();
            float distant = RearHazardWarningPolicy.WarningIntensity(
                2.0f,
                3.5f,
                settings);
            float close = RearHazardWarningPolicy.WarningIntensity(
                0.6f,
                0.8f,
                settings);

            Assert.That(close, Is.GreaterThan(distant));
        }

        [Test]
        public void WarningUsesShortBurstsHoldAndRewarningInterval()
        {
            var policy = CreatePolicy();
            StaticHazardDecision[] hazard = Hazards(
                Vector3.back,
                0.8f,
                0.20f);

            RearHazardWarningSnapshot started = policy.Evaluate(
                10.0,
                Vector3.forward,
                Vector3.right,
                hazard,
                true,
                StaticRiskChannelMask.All);
            Assert.That(started.BorderPulseActive, Is.True);
            Assert.That(started.HapticBurstActive, Is.True);

            RearHazardWarningSnapshot heldWithoutObservation = policy.Evaluate(
                10.4,
                Vector3.forward,
                Vector3.right,
                null,
                true,
                StaticRiskChannelMask.All);
            Assert.That(heldWithoutObservation.WarningActive, Is.True);
            Assert.That(heldWithoutObservation.BorderPulseActive, Is.False);
            Assert.That(heldWithoutObservation.HapticBurstActive, Is.True);

            RearHazardWarningSnapshot quietCooldown = policy.Evaluate(
                11.6,
                Vector3.forward,
                Vector3.right,
                hazard,
                true,
                StaticRiskChannelMask.All);
            Assert.That(quietCooldown.WarningActive, Is.False);
            Assert.That(quietCooldown.QualifyingNow, Is.True);

            RearHazardWarningSnapshot rewarned = policy.Evaluate(
                13.01,
                Vector3.forward,
                Vector3.right,
                hazard,
                true,
                StaticRiskChannelMask.All);
            Assert.That(rewarned.WarningActive, Is.True);
            Assert.That(rewarned.BorderPulseActive, Is.True);
        }

        [Test]
        public void DisabledChannelCannotProduceRearWarning()
        {
            var policy = CreatePolicy();
            RearHazardWarningSnapshot warning = policy.Evaluate(
                1.0,
                Vector3.forward,
                Vector3.right,
                Hazards(Vector3.back, 0.2f, 0f),
                true,
                StaticRiskChannelMask.Hands);

            Assert.That(warning.QualifyingNow, Is.False);
            Assert.That(warning.WarningActive, Is.False);
        }

        [Test]
        public void RedBorderModeCannotBypassRearApproachRequirement()
        {
            Type presentationType = Type.GetType(
                "SelectivePassthroughController, Assembly-CSharp");
            Type staticControllerType = Type.GetType(
                "StaticPassthroughPolicyController, Assembly-CSharp");
            Assert.That(presentationType, Is.Not.Null);
            Assert.That(staticControllerType, Is.Not.Null);

            var owner = new GameObject("Rear warning integration test");
            owner.SetActive(false);
            var cameraObject = new GameObject("Rear warning camera");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                Component staticController = owner.AddComponent(
                    staticControllerType);
                Component presentation = owner.AddComponent(presentationType);
                SetPrivateField(
                    presentationType,
                    presentation,
                    "staticPolicy",
                    staticController);
                SetPrivateField(
                    presentationType,
                    presentation,
                    "presentationCamera",
                    camera);
                presentationType.GetProperty(
                        "StaticWindowVisible",
                        BindingFlags.Instance | BindingFlags.Public)
                    .GetSetMethod(true)
                    .Invoke(presentation, new object[] { true });

                MethodInfo allowsGeneral = presentationType.GetMethod(
                    "HasGeneralStaticFeedbackCandidate",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(allowsGeneral, Is.Not.Null);

                SetLatestStatic(
                    staticControllerType,
                    staticController,
                    CreateStaticDecision(Vector3.back, 0.8f, 0f, true));
                Assert.That(
                    (bool)allowsGeneral.Invoke(presentation, null),
                    Is.False,
                    "A stationary rear wall must not reach the generic "
                    + "red-border/haptics path.");

                SetLatestStatic(
                    staticControllerType,
                    staticController,
                    CreateStaticDecision(Vector3.back, 0.8f, 0.10f, true));
                Assert.That(
                    (bool)allowsGeneral.Invoke(presentation, null),
                    Is.True);

                SetLatestStatic(
                    staticControllerType,
                    staticController,
                    CreateStaticDecision(Vector3.forward, 0.8f, 0f, true));
                Assert.That(
                    (bool)allowsGeneral.Invoke(presentation, null),
                    Is.True,
                    "The rear-only gate must not suppress an ordinary "
                    + "front-facing warning.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static RearHazardWarningPolicy CreatePolicy()
        {
            return new RearHazardWarningPolicy(
                new RearHazardWarningSettings
                {
                    rearAngleDegrees = 120f,
                    minimumClosingSpeedMetersPerSecond = 0.075f,
                    emergencyDistanceMeters = 0.25f,
                    borderPulseSeconds = 0.30f,
                    hapticBurstSeconds = 0.70f,
                    minimumHoldSeconds = 1.50f,
                    rewarningIntervalSeconds = 1.50f
                });
        }

        private static StaticHazardDecision[] Hazards(
            Vector3 direction,
            float distance,
            float closingSpeed)
        {
            return new[]
            {
                new StaticHazardDecision(
                    StaticHazardKey.Head,
                    StaticRiskChannelMask.Head,
                    true,
                    true,
                    false,
                    distance <= 0.25f,
                    false,
                    0.5f,
                    distance,
                    closingSpeed,
                    0.5f,
                    0.42f,
                    0f,
                    PassthroughDecisionReason.BelowThreshold,
                    direction,
                    true,
                    default,
                    0.5f,
                    0.5f,
                    0.5f)
            };
        }

        private static StaticPassthroughDecision CreateStaticDecision(
            Vector3 direction,
            float distance,
            float closingSpeed,
            bool enabled)
        {
            StaticHazardDecision hazard = Hazards(
                direction,
                distance,
                closingSpeed)[0];
            if (enabled)
            {
                hazard = new StaticHazardDecision(
                    hazard.Key,
                    hazard.Channel,
                    true,
                    true,
                    true,
                    hazard.EmergencyTrigger,
                    false,
                    hazard.Risk,
                    hazard.DistanceMeters,
                    hazard.ClosingSpeedMetersPerSecond,
                    hazard.OnThreshold,
                    hazard.OffThreshold,
                    0f,
                    hazard.Reason,
                    hazard.HazardDirectionWorld,
                    true,
                    default,
                    hazard.DistanceRisk,
                    hazard.SpeedRisk,
                    hazard.TtcRisk);
            }

            return new StaticPassthroughDecision(
                null,
                StaticWarningLevel.Full,
                StaticActivationCause.Head,
                hazard.Risk,
                0f,
                hazard.Risk,
                0f,
                hazard.OnThreshold,
                hazard.OffThreshold,
                0.32f,
                hazard.EmergencyTrigger,
                false,
                direction,
                true,
                default,
                StaticRiskChannelMask.All,
                new[] { hazard });
        }

        private static void SetLatestStatic(
            Type controllerType,
            Component controller,
            StaticPassthroughDecision decision)
        {
            FieldInfo field = controllerType.GetField(
                "<LatestStatic>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(controller, decision);
        }

        private static void SetPrivateField(
            Type type,
            object target,
            string name,
            object value)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        private static Vector3 DirectionAtYaw(float degrees)
        {
            return Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
        }
    }
}
