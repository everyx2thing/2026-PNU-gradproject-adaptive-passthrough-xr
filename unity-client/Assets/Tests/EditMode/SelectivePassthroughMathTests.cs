using NUnit.Framework;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class SelectivePassthroughMathTests
    {
        [Test]
        public void StaticDecisionRiskUsesIndependentStaticAndStateWeights()
        {
            float risk = SelectivePassthroughMath.StaticDecisionRisk(
                0.8f,
                0.5f);

            Assert.That(risk, Is.EqualTo(0.68f).Within(0.0001f));
        }

        [Test]
        public void PersonWindowMapsTopDownDetectionIntoCameraViewport()
        {
            Rect result = SelectivePassthroughMath.PersonWindowRect(
                new NormalizedBoundingBox(0.5f, 0.25f, 0.2f, 0.3f),
                new Rect(0.05f, 0.18f, 0.90f, 0.72f),
                0f);

            Assert.That(result.center.x, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(result.center.y, Is.EqualTo(0.72f).Within(0.0001f));
            Assert.That(result.width, Is.EqualTo(0.18f).Within(0.0001f));
            Assert.That(result.height, Is.EqualTo(0.216f).Within(0.0001f));
        }

        [Test]
        public void RearOrOutOfViewWallDirectionIsNotVisible()
        {
            Assert.That(
                SelectivePassthroughMath.IsViewportDirectionVisible(
                    new Vector3(0.5f, 0.5f, -1f)),
                Is.False);
            Assert.That(
                SelectivePassthroughMath.IsViewportDirectionVisible(
                    new Vector3(1.1f, 0.5f, 1f)),
                Is.False);
        }

        [Test]
        public void FocusedPersonWindowClampsClosePersonSize()
        {
            Rect result =
                SelectivePassthroughMath.FocusedPersonWindowRect(
                    new NormalizedBoundingBox(
                        0.5f,
                        0.5f,
                        0.9f,
                        0.9f),
                    new Rect(0.05f, 0.18f, 0.90f, 0.72f),
                    1f);

            Assert.That(result.width, Is.LessThanOrEqualTo(0.32f));
            Assert.That(result.height, Is.LessThanOrEqualTo(0.48f));
        }

        [Test]
        public void VisibleWallWindowStaysWithinViewport()
        {
            Rect left = SelectivePassthroughMath.WallDirectionWindowRect(
                0.1f,
                1f);
            Rect right = SelectivePassthroughMath.WallDirectionWindowRect(
                0.9f,
                1f);

            Assert.That(left.xMin, Is.EqualTo(0f));
            Assert.That(left.xMax, Is.LessThanOrEqualTo(1f));
            Assert.That(right.xMax, Is.EqualTo(1f));
            Assert.That(right.xMin, Is.GreaterThanOrEqualTo(0f));
        }
    }
}
