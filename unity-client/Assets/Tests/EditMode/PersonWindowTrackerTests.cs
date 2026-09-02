using NUnit.Framework;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class PersonWindowTrackerTests
    {
        [Test]
        public void BriefLossKeepsSameTrackWindowVisible()
        {
            var tracker = new PersonWindowTracker();
            tracker.BeginFrame();
            tracker.Observe(
                4,
                new Rect(0.4f, 0.3f, 0.2f, 0.3f),
                0.8f,
                0.0);
            tracker.Update(0.0, 0.2f);

            tracker.BeginFrame();
            tracker.Update(0.5, 0.1f);
            var snapshots = tracker.GetSnapshots(3);

            Assert.That(snapshots.Count, Is.EqualTo(1));
            Assert.That(snapshots[0].TrackId, Is.EqualTo(4));
            Assert.That(snapshots[0].Opacity, Is.GreaterThan(0.9f));
        }

        [Test]
        public void LostWindowFadesAfterHold()
        {
            var tracker = new PersonWindowTracker();
            tracker.BeginFrame();
            tracker.Observe(
                1,
                new Rect(0.4f, 0.3f, 0.2f, 0.3f),
                0.8f,
                0.0);
            tracker.Update(0.0, 0.2f);

            tracker.BeginFrame();
            tracker.Update(0.8, 0.2f);
            var snapshots = tracker.GetSnapshots(3);

            Assert.That(snapshots.Count, Is.EqualTo(1));
            Assert.That(snapshots[0].Opacity, Is.LessThan(1f));
            Assert.That(snapshots[0].Opacity, Is.GreaterThan(0f));
        }

        [Test]
        public void PositionMovesSmoothlyInsteadOfJumping()
        {
            var tracker = new PersonWindowTracker();
            tracker.BeginFrame();
            tracker.Observe(
                1,
                new Rect(0.1f, 0.3f, 0.2f, 0.3f),
                0.8f,
                0.0);
            tracker.Update(0.0, 0.2f);

            tracker.BeginFrame();
            tracker.Observe(
                1,
                new Rect(0.7f, 0.3f, 0.2f, 0.3f),
                0.8f,
                0.1);
            tracker.Update(0.1, 0.05f);
            PersonWindowSnapshot snapshot = tracker.GetSnapshots(3)[0];

            Assert.That(snapshot.Rect.x, Is.GreaterThan(0.1f));
            Assert.That(snapshot.Rect.x, Is.LessThan(0.7f));
        }

        [Test]
        public void PredictsBetweenFramesAndStopsAfterHalfSecond()
        {
            var tracker = new PersonWindowTracker(
                maximumPredictionSeconds: 0.5f,
                maximumViewportSpeed: 1.5f);
            tracker.BeginFrame();
            tracker.Observe(2, new Rect(0.10f, 0.3f, 0.2f, 0.3f), 0.8f, 0.0);
            tracker.Update(0.0, 0.2f);
            tracker.BeginFrame();
            tracker.Observe(2, new Rect(0.20f, 0.3f, 0.2f, 0.3f), 0.8f, 0.1);
            tracker.Update(0.1, 0.1f);

            tracker.BeginFrame();
            tracker.Update(0.3, 0.1f);
            float predicted = tracker.GetSnapshots(1)[0].Rect.x;
            tracker.BeginFrame();
            tracker.Update(0.8, 0.1f);
            PersonWindowSnapshot capped = tracker.GetSnapshots(1)[0];
            tracker.BeginFrame();
            tracker.Update(1.0, 0.1f);
            PersonWindowSnapshot stillCapped = tracker.GetSnapshots(1)[0];

            Assert.That(predicted, Is.GreaterThan(0.20f));
            Assert.That(capped.Rect.x, Is.GreaterThanOrEqualTo(predicted));
            Assert.That(capped.PredictionAgeSeconds,
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(stillCapped.PredictionAgeSeconds,
                Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void WorldPointReprojectionMovesCenterWithoutRefreshingHold()
        {
            var tracker = new PersonWindowTracker(
                lostHoldSeconds: 1.5f);
            tracker.BeginFrame();
            tracker.Observe(
                8,
                new Rect(0.20f, 0.30f, 0.20f, 0.30f),
                0.8f,
                0.0);
            tracker.Update(0.0, 0.2f);

            tracker.BeginFrame();
            Assert.That(
                tracker.ReprojectCenter(
                    8,
                    new Vector2(0.70f, 0.50f),
                    new Rect(0f, 0f, 1f, 1f)),
                Is.True);
            tracker.Update(1.0, 0.2f);
            PersonWindowSnapshot snapshot = tracker.GetSnapshots(1)[0];

            Assert.That(snapshot.Rect.center.x, Is.GreaterThan(0.30f));
            Assert.That(snapshot.ObservedThisFrame, Is.False);
            Assert.That(snapshot.HoldRemainingSeconds,
                Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void CaptureTimePredictionDoesNotConsumePresentationHold()
        {
            var tracker = new PersonWindowTracker(lostHoldSeconds: 1.5f);
            tracker.BeginFrame();
            tracker.Observe(
                3,
                new Rect(0.10f, 0.30f, 0.20f, 0.30f),
                0.8f,
                0.0,
                0.0);
            tracker.Update(0.0, 0.2f);

            tracker.BeginFrame();
            tracker.Observe(
                3,
                new Rect(0.20f, 0.30f, 0.20f, 0.30f),
                0.8f,
                0.10,
                0.40);
            tracker.Update(0.40, 0.10f);
            PersonWindowSnapshot snapshot = tracker.GetSnapshots(1)[0];

            Assert.That(snapshot.PredictionAgeSeconds,
                Is.EqualTo(0.30f).Within(0.001f));
            Assert.That(snapshot.Rect.x, Is.GreaterThan(0.20f));
            Assert.That(snapshot.HoldRemainingSeconds,
                Is.EqualTo(1.50f).Within(0.001f));
        }

        [Test]
        public void WorldGeometryFreshnessBridgesTheNextInferenceInterval()
        {
            var tracker = new PersonWindowTracker(lostHoldSeconds: 1.5f);
            HazardPresentationGeometry geometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    31,
                    HazardVisualKind.PersonCapsule,
                    new Vector3(0f, 1f, 1f),
                    Vector3.back,
                    Vector3.up,
                    0.5f,
                    1f,
                    2.0,
                    1f,
                    SpatialObstacleSource.EnvironmentDepth,
                    SpatialProbeOwner.Head,
                    SpatialProbePurpose.Standard);

            tracker.BeginFrame();
            tracker.Observe(
                31,
                new Rect(0.3f, 0.2f, 0.4f, 0.7f),
                0.9f,
                2.0,
                2.4,
                true,
                geometry);
            tracker.Update(2.4, 0.125f);
            Assert.That(tracker.GetSnapshots(1)[0]
                .PresentationGeometry.Available, Is.True);

            tracker.BeginFrame();
            tracker.Update(3.24, 0f);
            PersonWindowSnapshot held = tracker.GetSnapshots(1)[0];
            Assert.That(held.PresentationGeometry.Available, Is.True);
            Assert.That(held.Opacity, Is.GreaterThan(0f));

            tracker.BeginFrame();
            tracker.Update(3.26, 0f);
            held = tracker.GetSnapshots(1)[0];
            Assert.That(held.PresentationGeometry.Available, Is.False);
            Assert.That(held.Opacity, Is.GreaterThan(0f));
        }

        [Test]
        public void WorldGeometryRejectsAnAlreadyStaleInferenceResult()
        {
            var tracker = new PersonWindowTracker(lostHoldSeconds: 1.5f);
            HazardPresentationGeometry geometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    32,
                    HazardVisualKind.PersonCapsule,
                    new Vector3(0f, 1f, 1f),
                    Vector3.back,
                    Vector3.up,
                    0.5f,
                    1f,
                    2.0,
                    1f,
                    SpatialObstacleSource.EnvironmentDepth,
                    SpatialProbeOwner.Head,
                    SpatialProbePurpose.Standard);

            tracker.BeginFrame();
            tracker.Observe(
                32,
                new Rect(0.3f, 0.2f, 0.4f, 0.7f),
                0.9f,
                2.0,
                2.8,
                true,
                geometry);
            tracker.Update(2.8, 0.125f);

            PersonWindowSnapshot snapshot = tracker.GetSnapshots(1)[0];
            Assert.That(snapshot.PresentationGeometry.Available, Is.False);
            Assert.That(snapshot.Opacity, Is.GreaterThan(0f));
        }

        [Test]
        public void LowRiskObservationWarmsTrackingWithoutOpeningWindow()
        {
            var tracker = new PersonWindowTracker(lostHoldSeconds: 1.5f);
            tracker.BeginFrame();
            tracker.Observe(
                10,
                new Rect(0.2f, 0.3f, 0.2f, 0.3f),
                0.2f,
                0.0,
                0.0,
                false);
            tracker.Update(0.0, 0.2f);

            Assert.That(tracker.GetSnapshots(3), Is.Empty);

            tracker.BeginFrame();
            tracker.Observe(
                10,
                new Rect(0.3f, 0.3f, 0.2f, 0.3f),
                0.8f,
                0.3,
                0.3,
                true);
            tracker.Update(0.3, 0.2f);
            PersonWindowSnapshot shown = tracker.GetSnapshots(3)[0];

            Assert.That(shown.TrackId, Is.EqualTo(10));
            Assert.That(shown.Rect.center.x, Is.GreaterThan(0.3f));
        }

        [Test]
        public void LowRiskObservationDoesNotExtendQualifiedHold()
        {
            var tracker = new PersonWindowTracker(lostHoldSeconds: 1.5f);
            tracker.BeginFrame();
            tracker.Observe(
                11,
                new Rect(0.2f, 0.3f, 0.2f, 0.3f),
                0.8f,
                0.0,
                0.0,
                true);
            tracker.Update(0.0, 0.2f);

            tracker.BeginFrame();
            tracker.Observe(
                11,
                new Rect(0.3f, 0.3f, 0.2f, 0.3f),
                0.2f,
                1.0,
                1.0,
                false);
            tracker.Update(1.0, 0.1f);
            PersonWindowSnapshot held = tracker.GetSnapshots(1)[0];

            Assert.That(held.HoldRemainingSeconds,
                Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(held.Risk, Is.EqualTo(0.8f));
        }

        [Test]
        public void HeldHigherRiskWindowSurvivesThreeWindowLimit()
        {
            var tracker = new PersonWindowTracker(lostHoldSeconds: 1.5f);
            Rect rect = new Rect(0.2f, 0.2f, 0.2f, 0.4f);
            tracker.BeginFrame();
            tracker.Observe(1, rect, 0.9f, 0.0, 0.0, true);
            tracker.Observe(2, rect, 0.6f, 0.0, 0.0, true);
            tracker.Observe(3, rect, 0.6f, 0.0, 0.0, true);
            tracker.Observe(4, rect, 0.6f, 0.0, 0.0, true);
            tracker.Update(0.0, 0.2f);

            tracker.BeginFrame();
            tracker.Observe(2, rect, 0.6f, 0.1, 0.1, true);
            tracker.Observe(3, rect, 0.6f, 0.1, 0.1, true);
            tracker.Observe(4, rect, 0.6f, 0.1, 0.1, true);
            tracker.Update(0.1, 0.1f);

            var snapshots = tracker.GetSnapshots(3);
            bool containsHeldHighRisk = false;
            for (int i = 0; i < snapshots.Count; i++)
            {
                containsHeldHighRisk |= snapshots[i].TrackId == 1;
            }

            Assert.That(containsHeldHighRisk, Is.True);
            Assert.That(snapshots[0].TrackId, Is.EqualTo(1));
        }
    }
}
