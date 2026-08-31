using System;
using System.Reflection;
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
        public void HandOnlyFrameRemainsAvailableAndCanActivatePolicy()
        {
            StaticHandRiskMeasurement hand = Hand(
                0,
                1f,
                Vector3.forward);
            var frame = new StaticBoundaryRiskFrame(
                1L,
                0.0,
                true,
                StaticRiskMeasurement.Unavailable,
                0f,
                true,
                hand,
                StaticHandRiskMeasurement.Unavailable,
                Vector3.zero,
                false,
                -1,
                0.7f);

            StaticPassthroughDecision decision =
                new StaticBoundaryPolicy().Evaluate(1L, 0.0, frame);

            Assert.That(frame.Available, Is.True);
            Assert.That(frame.Head.Available, Is.False);
            Assert.That(frame.LeftHand.Available, Is.True);
            Assert.That(decision.Enabled, Is.True);
            Assert.That(decision.Cause, Is.EqualTo(StaticActivationCause.Hand));
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
        public void EmergencyTriggersWhileStationaryAndReleasesAfterDistanceClears()
        {
            var policy = new StaticBoundaryPolicy();

            StaticPassthroughDecision stationary = policy.Evaluate(
                1L,
                0.0,
                Frame(headRisk: 0.1f, distance: 0.20f, headTowardSpeed: 0f));
            StaticPassthroughDecision stillClose = policy.Evaluate(
                2L,
                0.1,
                Frame(headRisk: 0.1f, distance: 0.20f, headTowardSpeed: 0f));
            StaticPassthroughDecision cleared = policy.Evaluate(
                3L,
                0.2,
                Frame(headRisk: 0.1f, distance: 0.50f, headTowardSpeed: 0f));
            StaticPassthroughDecision released = policy.Evaluate(
                4L,
                1.6,
                Frame(headRisk: 0.1f, distance: 0.50f, headTowardSpeed: 0f));

            Assert.That(stationary.Enabled, Is.True);
            Assert.That(stationary.EmergencyTrigger, Is.True);
            Assert.That(stationary.Cause, Is.EqualTo(StaticActivationCause.Emergency));
            Assert.That(stillClose.EmergencyHold, Is.True);
            Assert.That(cleared.EmergencyHold, Is.False);
            Assert.That(cleared.Enabled, Is.True);
            Assert.That(released.Enabled, Is.False);
        }

        [Test]
        public void ConfirmedHeadOverlapTriggersWithoutSyntheticDistance()
        {
            var policy = new StaticBoundaryPolicy();
            var frame = new StaticBoundaryRiskFrame(
                1L,
                0.0,
                true,
                StaticRiskMeasurement.Unavailable,
                0f,
                true,
                StaticHandRiskMeasurement.Unavailable,
                StaticHandRiskMeasurement.Unavailable,
                Vector3.zero,
                false,
                -1,
                0f,
                true);

            StaticPassthroughDecision decision = policy.Evaluate(
                1L,
                0.0,
                frame);

            Assert.That(frame.Available, Is.True);
            Assert.That(frame.Head.Available, Is.False);
            Assert.That(frame.HeadSafetyOverlapEmergency, Is.True);
            Assert.That(decision.Enabled, Is.True);
            Assert.That(decision.EmergencyTrigger, Is.True);
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

        [Test]
        public void DisabledHeadChannelKeepsRawMeasurementButCannotContribute()
        {
            var policy = new StaticBoundaryPolicy();
            StaticPassthroughDecision decision = policy.Evaluate(
                1L,
                0.0,
                Frame(headRisk: 0.9f, leftHandRisk: 0.9f),
                StaticRiskChannelMask.Hands);

            StaticHazardDecision head = Hazard(
                decision,
                StaticHazardKey.Head);
            StaticHazardDecision hand = Hazard(
                decision,
                StaticHazardKey.LeftHand);
            Assert.That(head.Available, Is.True);
            Assert.That(head.ChannelEnabled, Is.False);
            Assert.That(head.Enabled, Is.False);
            Assert.That(hand.ChannelEnabled, Is.True);
            Assert.That(hand.Enabled, Is.True);
            Assert.That(decision.Enabled, Is.True);
        }

        [Test]
        public void DisablingOneChannelCancelsOnlyItsLatch()
        {
            var policy = new StaticBoundaryPolicy();
            policy.Evaluate(
                1L,
                0.0,
                Frame(headRisk: 0.9f, leftHandRisk: 0.9f));

            StaticPassthroughDecision decision = policy.Evaluate(
                2L,
                0.1,
                Frame(headRisk: 0.9f, leftHandRisk: 0.9f),
                StaticRiskChannelMask.Hands);

            Assert.That(Hazard(decision, StaticHazardKey.Head).Enabled,
                Is.False);
            Assert.That(Hazard(decision, StaticHazardKey.LeftHand).Enabled,
                Is.True);
            Assert.That(decision.Enabled, Is.True);
        }

        [Test]
        public void CloseAvailableHandTriggersEmergencyEvenWhenRiskIsZero()
        {
            StaticHandRiskMeasurement closeHand = Hand(
                0,
                0f,
                Vector3.forward,
                0.20f);
            var frame = new StaticBoundaryRiskFrame(
                1L,
                0.0,
                true,
                StaticRiskMeasurement.Unavailable,
                0f,
                true,
                closeHand,
                StaticHandRiskMeasurement.Unavailable,
                Vector3.zero,
                false,
                -1,
                0f);

            StaticPassthroughDecision decision =
                new StaticBoundaryPolicy().Evaluate(1L, 0.0, frame);
            StaticHazardDecision hand = Hazard(
                decision,
                StaticHazardKey.LeftHand);

            Assert.That(hand.EmergencyTrigger, Is.True);
            Assert.That(hand.Enabled, Is.True);
            Assert.That(decision.Enabled, Is.True);
        }

        [Test]
        public void ImmediateChannelDisableResetsLatchBeforeNextEvaluation()
        {
            var policy = new StaticBoundaryPolicy();
            policy.Evaluate(1L, 0.0, Frame(headRisk: 0.70f));

            policy.SetEnabledChannels(StaticRiskChannelMask.Hands);
            policy.SetEnabledChannels(StaticRiskChannelMask.All);
            StaticPassthroughDecision decision = policy.Evaluate(
                2L,
                0.1,
                Frame(headRisk: 0.60f),
                StaticRiskChannelMask.All);

            Assert.That(Hazard(decision, StaticHazardKey.Head).Enabled,
                Is.False);
            Assert.That(decision.Enabled, Is.False);
        }

        [Test]
        public void StaticSlotArbiterShowsOnlyTwoHighestPriorityHazards()
        {
            var arbiter = new StaticHazardSlotArbiter();
            StaticHazardSlotCandidate[] candidates =
            {
                Candidate(StaticHazardKey.Head, 0.70f),
                Candidate(StaticHazardKey.LeftHand, 0.60f),
                Candidate(StaticHazardKey.LowObstacle, 0.50f)
            };

            arbiter.Update(candidates, candidates.Length, 0.0);

            Assert.That(arbiter.GetKey(0), Is.EqualTo(StaticHazardKey.Head));
            Assert.That(arbiter.GetKey(1),
                Is.EqualTo(StaticHazardKey.LeftHand));
        }

        [Test]
        public void StaticSlotReplacementRequiresMarginForThreeHundredMilliseconds()
        {
            var arbiter = new StaticHazardSlotArbiter();
            StaticHazardSlotCandidate[] initial =
            {
                Candidate(StaticHazardKey.Head, 0.60f),
                Candidate(StaticHazardKey.LeftHand, 0.55f)
            };
            arbiter.Update(initial, initial.Length, 0.0);

            StaticHazardSlotCandidate[] challenged =
            {
                initial[0],
                initial[1],
                Candidate(StaticHazardKey.LowObstacle, 0.75f)
            };
            arbiter.Update(challenged, challenged.Length, 0.10);
            Assert.That(Contains(arbiter, StaticHazardKey.LeftHand), Is.True);
            arbiter.Update(challenged, challenged.Length, 0.39);
            Assert.That(Contains(arbiter, StaticHazardKey.LeftHand), Is.True);
            arbiter.Update(challenged, challenged.Length, 0.41);

            Assert.That(Contains(arbiter, StaticHazardKey.LowObstacle), Is.True);
            Assert.That(Contains(arbiter, StaticHazardKey.LeftHand), Is.False);
            Assert.That(arbiter.LastReplacementReason,
                Is.EqualTo("confirmed-margin"));
        }

        [Test]
        public void StaticSlotEmergencyImmediatelyPreemptsNonEmergency()
        {
            var arbiter = new StaticHazardSlotArbiter();
            StaticHazardSlotCandidate[] initial =
            {
                Candidate(StaticHazardKey.Head, 0.80f),
                Candidate(StaticHazardKey.LeftHand, 0.70f)
            };
            arbiter.Update(initial, initial.Length, 0.0);
            StaticHazardSlotCandidate[] emergency =
            {
                initial[0],
                initial[1],
                Candidate(StaticHazardKey.LowObstacle, 0.20f, true)
            };

            arbiter.Update(emergency, emergency.Length, 0.01);

            Assert.That(Contains(arbiter, StaticHazardKey.LowObstacle), Is.True);
            Assert.That(arbiter.LastReplacementReason, Is.EqualTo("emergency"));
        }

        [Test]
        public void StaticSlotAssignmentRestartsAppearanceAndPulseAtAssignment()
        {
            object slot = CreateControllerStaticSlot();

            RequestControllerStaticSlot(
                slot,
                StaticHazardKey.Head,
                10.0,
                false);

            Assert.That(GetSlotField<float>(slot, "StaticVisualOpacity"),
                Is.EqualTo(0f));
            Assert.That(GetSlotField<double>(slot, "StaticPulseStartedAt"),
                Is.EqualTo(10.0));
            Assert.That(ControllerStaticSlotPulse(slot, 10.15),
                Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void StaticSlotReplacementFadesOutgoingThenAppearsIncoming()
        {
            object slot = CreateControllerStaticSlot();
            RequestControllerStaticSlot(
                slot,
                StaticHazardKey.Head,
                10.0,
                false);
            AdvanceControllerStaticSlot(slot, 10.125, 0.125f);
            Assert.That(GetSlotField<float>(slot, "StaticVisualOpacity"),
                Is.EqualTo(1f).Within(0.001f));

            RequestControllerStaticSlot(
                slot,
                StaticHazardKey.LeftHand,
                11.0,
                false);
            AdvanceControllerStaticSlot(slot, 11.075, 0.075f);
            Assert.That(GetSlotField<StaticHazardKey>(slot, "StaticKey"),
                Is.EqualTo(StaticHazardKey.Head));
            Assert.That(GetSlotField<float>(slot, "StaticVisualOpacity"),
                Is.EqualTo(0.5f).Within(0.001f));

            AdvanceControllerStaticSlot(slot, 11.151, 0.076f);
            Assert.That(GetSlotField<StaticHazardKey>(slot, "StaticKey"),
                Is.EqualTo(StaticHazardKey.LeftHand));
            Assert.That(GetSlotField<float>(slot, "StaticVisualOpacity"),
                Is.EqualTo(0f).Within(0.001f));

            AdvanceControllerStaticSlot(slot, 11.2135, 0.0625f);
            Assert.That(GetSlotField<float>(slot, "StaticVisualOpacity"),
                Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(ControllerStaticSlotPulse(slot, 11.301),
                Is.GreaterThan(0f));
        }

        [Test]
        public void StaticSlotEmergencyPreemptionIsImmediateButRestartsCue()
        {
            object slot = CreateControllerStaticSlot();
            RequestControllerStaticSlot(
                slot,
                StaticHazardKey.Head,
                10.0,
                false);
            AdvanceControllerStaticSlot(slot, 10.125, 0.125f);

            RequestControllerStaticSlot(
                slot,
                StaticHazardKey.LowObstacle,
                11.0,
                true);

            Assert.That(GetSlotField<StaticHazardKey>(slot, "StaticKey"),
                Is.EqualTo(StaticHazardKey.LowObstacle));
            Assert.That(GetSlotField<float>(slot, "StaticVisualOpacity"),
                Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(GetSlotField<double>(slot, "StaticPulseStartedAt"),
                Is.EqualTo(11.0));
        }

        [Test]
        public void PendingStaticReplacementPreemptsImmediatelyWhenEmergency()
        {
            object slot = CreateControllerStaticSlot();
            RequestControllerStaticSlot(
                slot,
                StaticHazardKey.Head,
                10.0,
                false);
            AdvanceControllerStaticSlot(slot, 10.125, 0.125f);
            RequestControllerStaticSlot(
                slot,
                StaticHazardKey.LowObstacle,
                11.0,
                false);

            RequestControllerStaticSlot(
                slot,
                StaticHazardKey.LowObstacle,
                11.01,
                true);

            Assert.That(GetSlotField<StaticHazardKey>(slot, "StaticKey"),
                Is.EqualTo(StaticHazardKey.LowObstacle));
            Assert.That(
                GetSlotField<bool>(slot, "HasPendingStaticReplacement"),
                Is.False);
            Assert.That(GetSlotField<float>(slot, "StaticVisualOpacity"),
                Is.EqualTo(0.35f).Within(0.001f));
        }

        [Test]
        public void StaticSlotVisibilityRequiresActualRendererGeometryAndOpacity()
        {
            object slot = CreateControllerStaticSlot();
            var slotObject = new GameObject("Static slot visibility test");
            try
            {
                MeshRenderer renderer = slotObject.AddComponent<MeshRenderer>();
                SetSlotField(slot, "StaticKey", StaticHazardKey.Head);
                SetSlotField(slot, "Renderer", renderer);
                SetSlotField(slot, "StaticRenderedGeometryAvailable", true);
                SetSlotField(slot, "StaticRenderedOpacity", 1f);
                renderer.enabled = true;
                Assert.That(ControllerStaticSlotIsVisible(slot), Is.True);

                renderer.enabled = false;
                Assert.That(ControllerStaticSlotIsVisible(slot), Is.False);
                renderer.enabled = true;
                SetSlotField(slot, "StaticRenderedOpacity", 0f);
                Assert.That(ControllerStaticSlotIsVisible(slot), Is.False);
                SetSlotField(slot, "StaticRenderedOpacity", 1f);
                SetSlotField(slot, "StaticRenderedGeometryAvailable", false);
                Assert.That(ControllerStaticSlotIsVisible(slot), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(slotObject);
            }
        }

        [Test]
        public void StaticChannelPreferencesRoundTripAndDefaultToAllEnabled()
        {
            var preferences = new StaticRiskChannelPreferences
            {
                HeadFeatureEnabled = false,
                HandsFeatureEnabled = true,
                LowObstacleFeatureEnabled = false
            };
            string json = JsonUtility.ToJson(preferences);
            StaticRiskChannelPreferences restored =
                JsonUtility.FromJson<StaticRiskChannelPreferences>(json);

            Assert.That(restored.ToMask(),
                Is.EqualTo(StaticRiskChannelMask.Hands));
            Assert.That(new StaticRiskChannelPreferences().ToMask(),
                Is.EqualTo(StaticRiskChannelMask.All));

            restored.PreserveAllOnForLegacyJson(
                "{\"Version\":2,\"StaticFeatureEnabled\":true}");
            Assert.That(restored.ToMask(),
                Is.EqualTo(StaticRiskChannelMask.All));
        }

        private static StaticHazardSlotCandidate Candidate(
            StaticHazardKey key,
            float risk,
            bool emergency = false)
        {
            return new StaticHazardSlotCandidate(
                key,
                true,
                emergency,
                risk,
                risk,
                risk);
        }

        private static bool Contains(
            StaticHazardSlotArbiter arbiter,
            StaticHazardKey key)
        {
            for (int i = 0; i < arbiter.SlotCount; i++)
            {
                if (arbiter.GetKey(i) == key)
                {
                    return true;
                }
            }
            return false;
        }

        private static Type ControllerType()
        {
            return Type.GetType(
                "SelectivePassthroughController, Assembly-CSharp",
                true);
        }

        private static object CreateControllerStaticSlot()
        {
            Type slotType = ControllerType().GetNestedType(
                "WindowSlot",
                BindingFlags.NonPublic);
            Assert.That(slotType, Is.Not.Null);
            return Activator.CreateInstance(slotType, true);
        }

        private static void RequestControllerStaticSlot(
            object slot,
            StaticHazardKey key,
            double now,
            bool emergency)
        {
            InvokeControllerStatic(
                "RequestStaticSlotKey",
                slot,
                key,
                now,
                emergency);
        }

        private static void AdvanceControllerStaticSlot(
            object slot,
            double now,
            float deltaTime)
        {
            InvokeControllerStatic(
                "AdvanceStaticSlotTransition",
                slot,
                now,
                deltaTime);
        }

        private static float ControllerStaticSlotPulse(
            object slot,
            double now)
        {
            return (float)InvokeControllerStatic(
                "StaticSlotPulse01",
                slot,
                now);
        }

        private static bool ControllerStaticSlotIsVisible(object slot)
        {
            return (bool)InvokeControllerStatic(
                "IsStaticSlotActuallyVisible",
                slot);
        }

        private static object InvokeControllerStatic(
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = ControllerType().GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, methodName);
            return method.Invoke(null, arguments);
        }

        private static T GetSlotField<T>(object slot, string fieldName)
        {
            FieldInfo field = slot.GetType().GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, fieldName);
            return (T)field.GetValue(slot);
        }

        private static void SetSlotField(
            object slot,
            string fieldName,
            object value)
        {
            FieldInfo field = slot.GetType().GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(slot, value);
        }

        private static StaticHazardDecision Hazard(
            StaticPassthroughDecision decision,
            StaticHazardKey key)
        {
            for (int i = 0; i < decision.Hazards.Length; i++)
            {
                if (decision.Hazards[i].Key == key)
                {
                    return decision.Hazards[i];
                }
            }

            Assert.Fail("Missing static hazard " + key);
            return null;
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
            Vector3 direction,
            float distanceMeters = -1f)
        {
            float effectiveDistance = distanceMeters >= 0f
                ? distanceMeters
                : risk > 0f ? 0.1f : 1f;
            return new StaticHandRiskMeasurement(
                true,
                wallIndex,
                effectiveDistance,
                risk > 0f ? 0.2f : 0f,
                effectiveDistance,
                0.6f,
                risk > 0f ? 1f : 0f,
                risk,
                risk,
                risk,
                direction == default ? Vector3.forward : direction);
        }
    }
}
