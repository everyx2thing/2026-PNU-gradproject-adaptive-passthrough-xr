using NUnit.Framework;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class UserMotionStateFilterTests
    {
        [Test]
        public void StationaryTrackingNoiseRemainsStatic()
        {
            var filter = new UserMotionStateFilter();
            const double dt = 1.0 / 72.0;
            int transitions = 0;
            UserMotionSnapshot snapshot = null;
            for (int i = 0; i < 216; i++)
            {
                float noise = i % 2 == 0 ? 0.00005f : -0.00005f;
                snapshot = filter.Update(
                    i * dt,
                    new Vector3(noise, 1.65f, -noise),
                    Quaternion.Euler(0f, noise * 100f, 0f));
                if (snapshot.StateChanged)
                {
                    transitions++;
                }
            }

            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.StableState, Is.EqualTo(UserMotionState.Static));
            Assert.That(transitions, Is.EqualTo(0));
        }

        [Test]
        public void SingleFrameSpikeDoesNotBecomeAgitated()
        {
            var filter = new UserMotionStateFilter();
            const double dt = 1.0 / 72.0;
            bool becameAgitated = false;
            for (int i = 0; i < 144; i++)
            {
                Vector3 position = i == 40
                    ? new Vector3(0.08f, 1.65f, 0f)
                    : Vector3.up * 1.65f;
                UserMotionSnapshot snapshot = filter.Update(
                    i * dt,
                    position,
                    Quaternion.identity);
                becameAgitated |=
                    snapshot.StableState == UserMotionState.Agitated;
            }

            Assert.That(becameAgitated, Is.False);
        }

        [Test]
        public void SustainedTranslationTransitionsToDynamic()
        {
            var filter = new UserMotionStateFilter();
            const double dt = 1.0 / 72.0;
            UserMotionSnapshot snapshot = null;
            for (int i = 0; i < 144; i++)
            {
                snapshot = filter.Update(
                    i * dt,
                    new Vector3(i * 0.003f, 1.65f, 0f),
                    Quaternion.identity);
            }

            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.StableState, Is.EqualTo(UserMotionState.Dynamic));
        }

        [Test]
        public void SustainedFastRotationTransitionsToAgitated()
        {
            var filter = new UserMotionStateFilter();
            const double dt = 1.0 / 72.0;
            const float radiansPerSecond = 4f;
            UserMotionSnapshot snapshot = null;
            for (int i = 0; i < 180; i++)
            {
                float angleDegrees =
                    (float)(i * dt * radiansPerSecond * Mathf.Rad2Deg);
                snapshot = filter.Update(
                    i * dt,
                    Vector3.up * 1.65f,
                    Quaternion.Euler(0f, angleDegrees, 0f));
            }

            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.StableState, Is.EqualTo(UserMotionState.Agitated));
        }

        [Test]
        public void SimilarMotionAt72And90HzProducesSameStableState()
        {
            UserMotionState state72 = SimulateTranslation(72);
            UserMotionState state90 = SimulateTranslation(90);

            Assert.That(state72, Is.EqualTo(UserMotionState.Dynamic));
            Assert.That(state90, Is.EqualTo(state72));
        }

        private static UserMotionState SimulateTranslation(int frameRate)
        {
            var filter = new UserMotionStateFilter();
            double dt = 1.0 / frameRate;
            UserMotionSnapshot snapshot = null;
            for (int i = 0; i < frameRate * 2; i++)
            {
                snapshot = filter.Update(
                    i * dt,
                    new Vector3((float)(i * dt * 0.2), 1.65f, 0f),
                    Quaternion.identity);
            }

            return snapshot.StableState;
        }
    }
}
