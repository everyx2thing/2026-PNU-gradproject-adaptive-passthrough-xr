using NUnit.Framework;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class PersonalizationMathTests
    {
        [Test]
        public void BuildFeatureVectorUsesFixedSevenFeatureOrder()
        {
            PersonalizationFeatureVector features =
                PersonalizationMath.BuildFeatureVector(
                    2,
                    4,
                    1,
                    1.25f,
                    0.40f,
                    0.75f,
                    6f,
                    900f);

            float[] values = features.ToArray();
            Assert.That(values, Has.Length.EqualTo(7));
            Assert.That(values[0], Is.EqualTo(0.50f).Within(0.0001f));
            Assert.That(values[1], Is.EqualTo(0.50f).Within(0.0001f));
            Assert.That(values[2], Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(values[3], Is.EqualTo(0.40f).Within(0.0001f));
            Assert.That(values[4], Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(values[5], Is.EqualTo(0.50f).Within(0.0001f));
            Assert.That(values[6], Is.EqualTo(0.50f).Within(0.0001f));
        }

        [TestCase(0, true)]
        [TestCase(4, true)]
        [TestCase(5, false)]
        [TestCase(10, false)]
        public void ColdStartRequiresFiveAccumulatedSessions(
            int sessions,
            bool expected)
        {
            Assert.That(
                PersonalizationMath.IsColdStart(sessions),
                Is.EqualTo(expected));
        }

        [Test]
        public void LowNegativeProbabilityKeepsSafeDefaults()
        {
            PersonalizedThresholds thresholds =
                PersonalizationMath.FromNegativeProbability(0.25f);

            Assert.That(
                thresholds.StableOnThreshold,
                Is.EqualTo(0.65f).Within(0.0001f));
            Assert.That(
                thresholds.RapidOnThreshold,
                Is.EqualTo(0.45f).Within(0.0001f));
            Assert.That(
                thresholds.HandFullThreshold,
                Is.EqualTo(0.85f).Within(0.0001f));
        }

        [Test]
        public void HighNegativeProbabilityRaisesAndCapsThresholds()
        {
            PersonalizedThresholds thresholds =
                PersonalizationMath.FromNegativeProbability(
                    1f,
                    1f,
                    0.95f);

            Assert.That(thresholds.StableOnThreshold, Is.EqualTo(0.95f));
            Assert.That(thresholds.RapidOnThreshold, Is.EqualTo(0.95f));
            Assert.That(thresholds.HandFullThreshold, Is.EqualTo(0.95f));
        }
    }
}
