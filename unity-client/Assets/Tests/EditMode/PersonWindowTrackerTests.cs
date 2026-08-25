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
    }
}
