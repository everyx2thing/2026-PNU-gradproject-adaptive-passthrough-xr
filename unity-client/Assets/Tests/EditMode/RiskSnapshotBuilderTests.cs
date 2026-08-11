using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class RiskSnapshotBuilderTests
    {
        [Test]
        public void AllAvailableInputsMatchConfiguredFusionWeights()
        {
            var builder = new RiskSnapshotBuilder();
            RiskSnapshot snapshot = builder.Build(new RiskSnapshotBuildInput
            {
                TimestampSeconds = 1.0,
                CameraReady = true,
                RoomSceneReady = true,
                MotionReady = true,
                StaticMeasurement = StaticMeasurement(0.2f),
                MotionSnapshot = Motion(UserMotionState.Dynamic),
                DynamicFrame = DynamicFrame(0.9, 0.9f)
            });

            Assert.That(snapshot.SchemaVersion, Is.EqualTo(1));
            Assert.That(snapshot.Sequence, Is.EqualTo(1));
            Assert.That(snapshot.Overall.Available, Is.True);
            Assert.That(snapshot.Overall.TotalRisk, Is.EqualTo(0.54f).Within(0.00001f));
        }

        [Test]
        public void UnavailableRoomAndIntentAreExcludedFromWeightSum()
        {
            var builder = new RiskSnapshotBuilder();
            RiskSnapshot snapshot = builder.Build(new RiskSnapshotBuildInput
            {
                TimestampSeconds = 1.0,
                CameraReady = true,
                RoomSceneReady = false,
                MotionReady = true,
                StaticMeasurement = StaticMeasurement(1f),
                MotionSnapshot = Motion(UserMotionState.Dynamic),
                DynamicFrame = DynamicFrame(0.9, 0.9f),
                IntentReady = false,
                IntentRisk = 1f
            });

            float expected = (0.2f * 0.5f + 0.4f * 0.9f) / 0.6f;
            Assert.That(snapshot.Static.Available, Is.False);
            Assert.That(snapshot.Intent.Available, Is.False);
            Assert.That(snapshot.Overall.AvailableWeightSum, Is.EqualTo(0.6f).Within(0.00001f));
            Assert.That(snapshot.Overall.TotalRisk, Is.EqualTo(expected).Within(0.00001f));
        }

        [Test]
        public void StaleDynamicFrameDoesNotLeavePeopleOrRiskInSnapshot()
        {
            var builder = new RiskSnapshotBuilder();
            RiskSnapshot snapshot = builder.Build(new RiskSnapshotBuildInput
            {
                TimestampSeconds = 1.51,
                CameraReady = true,
                MotionReady = true,
                MotionSnapshot = Motion(UserMotionState.Static),
                DynamicFrame = DynamicFrame(1.0, 0.95f)
            });

            Assert.That(snapshot.Sources.DynamicReady, Is.False);
            Assert.That(snapshot.Dynamic.ConfirmedPersonCount, Is.Zero);
            Assert.That(snapshot.Dynamic.MaximumRisk, Is.Zero);
            Assert.That(snapshot.Dynamic.SourceAgeSeconds, Is.EqualTo(0.51f).Within(0.0001f));
            Assert.That(snapshot.Overall.TotalRisk, Is.Zero);
        }

        [Test]
        public void NoAvailableRiskInputsProduceSafeDisabledDecision()
        {
            var builder = new RiskSnapshotBuilder();
            RiskSnapshot snapshot = builder.Build(new RiskSnapshotBuildInput
            {
                TimestampSeconds = 2.0
            });

            Assert.That(snapshot.Overall.Available, Is.False);
            Assert.That(snapshot.Overall.TotalRisk, Is.Zero);
            Assert.That(snapshot.OverallLevel, Is.EqualTo(OverallRiskLevel.Safe));
            Assert.That(snapshot.Passthrough.Enabled, Is.False);
            Assert.That(
                snapshot.Passthrough.Reason,
                Is.EqualTo(PassthroughDecisionReason.NoRiskInputs));
        }

        [Test]
        public void SequenceIncreasesAndTimestampNeverMovesBackward()
        {
            var builder = new RiskSnapshotBuilder();
            RiskSnapshot first = builder.Build(new RiskSnapshotBuildInput
            {
                TimestampSeconds = 2.0
            });
            RiskSnapshot second = builder.Build(new RiskSnapshotBuildInput
            {
                TimestampSeconds = 1.0
            });

            Assert.That(second.Sequence, Is.EqualTo(first.Sequence + 1));
            Assert.That(second.TimestampSeconds, Is.EqualTo(first.TimestampSeconds));
            Assert.That(builder.InvalidValueCount, Is.EqualTo(1));
        }

        [Test]
        public void PrimaryDynamicObjectComesFromHighestRiskAssessment()
        {
            DynamicRiskFrame frame = new DynamicRiskFrame(
                3.0,
                new[]
                {
                    Assessment(10, 0.4f, DynamicRiskLevel.Caution),
                    Assessment(20, 0.8f, DynamicRiskLevel.Danger)
                },
                2);
            var builder = new RiskSnapshotBuilder();
            RiskSnapshot snapshot = builder.Build(new RiskSnapshotBuildInput
            {
                TimestampSeconds = 3.1,
                CameraReady = true,
                DynamicFrame = frame
            });

            Assert.That(snapshot.Dynamic.Available, Is.True);
            Assert.That(snapshot.Dynamic.PrimaryTrackId, Is.EqualTo(20));
            Assert.That(snapshot.Dynamic.MaximumRisk, Is.EqualTo(0.8f).Within(0.00001f));
            Assert.That(snapshot.Dynamic.PrimaryMotionState, Is.EqualTo(DynamicMotionState.Approaching));
        }

        [Test]
        public void SnapshotPublicInstanceFieldsAreReadonly()
        {
            AssertReadonlyFields(typeof(RiskSnapshot));
            AssertReadonlyFields(typeof(RiskSourceState));
            AssertReadonlyFields(typeof(StaticRiskSnapshot));
            AssertReadonlyFields(typeof(UserStateRiskSnapshot));
            AssertReadonlyFields(typeof(DynamicRiskSnapshot));
            AssertReadonlyFields(typeof(IntentRiskSnapshot));
            AssertReadonlyFields(typeof(PassthroughDecisionSnapshot));
        }

        [Test]
        public void CompatibilityModeMatchesLegacyPointSixThreshold()
        {
            var filter = new PassthroughDecisionFilter(
                new PassthroughDecisionFilterSettings
                {
                    mode = PassthroughDecisionMode.CompatibilityThreshold,
                    onThreshold = 0.6f
                });

            Assert.That(filter.Evaluate(0.0, true, 0.599f).Enabled, Is.False);
            Assert.That(filter.Evaluate(0.1, true, 0.6f).Enabled, Is.True);
        }

        [Test]
        public void HysteresisHonorsMinimumHoldAndOffThreshold()
        {
            var filter = new PassthroughDecisionFilter(
                new PassthroughDecisionFilterSettings
                {
                    mode = PassthroughDecisionMode.Hysteresis,
                    onThreshold = 0.6f,
                    offThreshold = 0.5f,
                    minimumHoldSeconds = 0.5f
                });

            Assert.That(filter.Evaluate(0.0, true, 0.7f).Enabled, Is.True);
            PassthroughDecisionSnapshot held =
                filter.Evaluate(0.2, true, 0.2f);
            Assert.That(held.Enabled, Is.True);
            Assert.That(
                held.Reason,
                Is.EqualTo(PassthroughDecisionReason.MinimumHoldActive));
            Assert.That(filter.Evaluate(0.6, true, 0.2f).Enabled, Is.False);
        }

        [Test]
        public void HysteresisRequiresStableReleaseAndCancelsTransientDrop()
        {
            var filter = new PassthroughDecisionFilter(
                new PassthroughDecisionFilterSettings
                {
                    mode = PassthroughDecisionMode.Hysteresis,
                    onThreshold = 0.6f,
                    offThreshold = 0.5f,
                    minimumHoldSeconds = 0.5f,
                    releaseDelaySeconds = 0.35f
                });

            Assert.That(filter.Evaluate(0.0, true, 0.7f).Enabled, Is.True);
            PassthroughDecisionSnapshot firstDrop =
                filter.Evaluate(1.0, true, 0.2f);
            Assert.That(firstDrop.Enabled, Is.True);
            Assert.That(
                firstDrop.Reason,
                Is.EqualTo(PassthroughDecisionReason.ReleaseDelayActive));

            Assert.That(filter.Evaluate(1.1, true, 0.7f).Enabled, Is.True);
            Assert.That(filter.Evaluate(1.2, true, 0.2f).Enabled, Is.True);
            Assert.That(filter.Evaluate(1.54, true, 0.2f).Enabled, Is.True);
            Assert.That(filter.Evaluate(1.56, true, 0.2f).Enabled, Is.False);
        }

        [Test]
        public void HysteresisHoldsThroughBriefInputLoss()
        {
            var filter = new PassthroughDecisionFilter(
                new PassthroughDecisionFilterSettings
                {
                    mode = PassthroughDecisionMode.Hysteresis,
                    onThreshold = 0.6f,
                    offThreshold = 0.5f,
                    minimumHoldSeconds = 1.5f,
                    releaseDelaySeconds = 0.35f
                });

            Assert.That(filter.Evaluate(0.0, true, 0.7f).Enabled, Is.True);
            PassthroughDecisionSnapshot held =
                filter.Evaluate(0.2, false, 0f);

            Assert.That(held.Enabled, Is.True);
            Assert.That(
                held.Reason,
                Is.EqualTo(PassthroughDecisionReason.MinimumHoldActive));
            Assert.That(filter.Evaluate(0.3, true, 0.7f).Enabled, Is.True);
        }

        private static StaticRiskMeasurement StaticMeasurement(float risk)
        {
            return new StaticRiskMeasurement(
                true,
                0.5f,
                1.0f,
                true,
                0.2f,
                0.3f,
                risk,
                risk,
                risk,
                risk,
                risk);
        }

        private static UserMotionSnapshot Motion(UserMotionState state)
        {
            return new UserMotionSnapshot(
                1.0,
                true,
                Vector3.zero,
                Vector3.zero,
                Vector3.zero,
                Vector3.zero,
                0f,
                0f,
                state,
                state,
                false);
        }

        private static DynamicRiskFrame DynamicFrame(
            double timestamp,
            float risk)
        {
            return new DynamicRiskFrame(
                timestamp,
                new[]
                {
                    Assessment(7, risk, DynamicRiskLevel.Danger)
                },
                1);
        }

        private static DynamicRiskAssessment Assessment(
            int trackId,
            float score,
            DynamicRiskLevel level)
        {
            var detection = new DynamicObjectDetection(
                "person",
                0.9f,
                new NormalizedBoundingBox(0.5f, 0.5f, 0.3f, 0.5f));
            var location = new RelativeLocationEstimate(
                HorizontalZone.Center,
                "front",
                DistanceBand.Near,
                0f,
                detection.boundingBox.Area);
            var motion = new MotionEstimate(
                DynamicMotionState.Approaching,
                0.2f,
                2f,
                0f,
                5,
                0.5,
                1f);
            var breakdown = new DynamicRiskBreakdown(
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                1f);
            return new DynamicRiskAssessment(
                trackId,
                detection,
                location,
                motion,
                score,
                level,
                Array.Empty<string>(),
                breakdown);
        }

        private static void AssertReadonlyFields(Type type)
        {
            FieldInfo[] fields = type.GetFields(
                BindingFlags.Public | BindingFlags.Instance);
            Assert.That(fields, Is.Not.Empty);
            for (int i = 0; i < fields.Length; i++)
            {
                Assert.That(
                    fields[i].IsInitOnly,
                    Is.True,
                    type.Name + "." + fields[i].Name);
            }
        }
    }
}
