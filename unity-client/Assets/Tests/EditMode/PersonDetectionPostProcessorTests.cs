using System;
using NUnit.Framework;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class PersonDetectionPostProcessorTests
    {
        [Test]
        public void DuplicateCenterPixelBoxesCollapseToHighestConfidence()
        {
            PersonDetectionPostProcessResult result = Processor().Process(
                new[]
                {
                    320f, 320f, 160f, 320f,
                    321f, 320f, 160f, 320f,
                    319f, 321f, 160f, 320f,
                    320f, 319f, 160f, 320f,
                    320f, 320f, 158f, 318f
                },
                new[] { 0, 0, 0, 0, 0 },
                new[] { 0.91f, 0.82f, 0.78f, 0.72f, 0.65f },
                640,
                640);

            Assert.That(result.Detections.Count, Is.EqualTo(1));
            Assert.That(result.SuppressedByNmsCount, Is.EqualTo(4));
            Assert.That(result.Detections[0].confidence, Is.EqualTo(0.91f));
            Assert.That(
                result.Detections[0].boundingBox.centerX,
                Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void InvalidCoordinatesAreRejectedBeforeClipping()
        {
            PersonDetectionPostProcessResult result = Processor().Process(
                new[]
                {
                    -500f, 320f, 20f, 100f,
                    float.NaN, 320f, 20f, 100f,
                    320f, 320f, -20f, 100f
                },
                new[] { 0, 0, 0 },
                new[] { 0.9f, 0.9f, 0.9f },
                640,
                640);

            Assert.That(result.Detections, Is.Empty);
            Assert.That(result.InvalidBoxCount, Is.EqualTo(3));
        }

        [Test]
        public void NormalizedCoordinatesAreNotDividedByInputSizeAgain()
        {
            var settings = new PersonDetectionPostProcessorSettings
            {
                coordinateSpace = PersonBoxCoordinateSpace.Normalized,
                boxFormat = PersonBoxFormat.CenterXYWH,
                confidenceThreshold = 0.5f
            };
            PersonDetectionPostProcessResult result =
                new PersonDetectionPostProcessor(settings).Process(
                    new[] { 0.5f, 0.4f, 0.25f, 0.5f },
                    new[] { 0 },
                    new[] { 0.9f },
                    640,
                    640);

            Assert.That(result.Detections.Count, Is.EqualTo(1));
            NormalizedBoundingBox box = result.Detections[0].boundingBox;
            Assert.That(box.centerX, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(box.centerY, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(box.width, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(box.height, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void CornerBoxesAreValidatedThenClippedOnce()
        {
            var settings = new PersonDetectionPostProcessorSettings
            {
                coordinateSpace = PersonBoxCoordinateSpace.Normalized,
                boxFormat = PersonBoxFormat.CornersXYXY,
                confidenceThreshold = 0.5f,
                minimumVisibleFraction = 0.1f
            };
            PersonDetectionPostProcessResult result =
                new PersonDetectionPostProcessor(settings).Process(
                    new[] { -0.1f, 0.2f, 0.3f, 0.8f },
                    new[] { 0 },
                    new[] { 0.9f },
                    640,
                    640);

            Assert.That(result.Detections.Count, Is.EqualTo(1));
            NormalizedBoundingBox box = result.Detections[0].boundingBox;
            Assert.That(box.Left, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(box.Right, Is.EqualTo(0.3f).Within(0.0001f));
        }

        [Test]
        public void NmsLeavesNoPairAtOrAboveConfiguredIou()
        {
            PersonDetectionPostProcessResult result = Processor().Process(
                new[]
                {
                    160f, 320f, 120f, 300f,
                    165f, 320f, 120f, 300f,
                    480f, 320f, 120f, 300f
                },
                new[] { 0, 0, 0 },
                new[] { 0.9f, 0.8f, 0.85f },
                640,
                640);

            Assert.That(result.Detections.Count, Is.EqualTo(2));
            for (int i = 0; i < result.Detections.Count; i++)
            {
                for (int j = i + 1; j < result.Detections.Count; j++)
                {
                    float iou = SimpleObjectTracker.IntersectionOverUnion(
                        result.Detections[i].boundingBox,
                        result.Detections[j].boundingBox);
                    Assert.That(iou, Is.LessThan(0.45f));
                }
            }
        }

        private static PersonDetectionPostProcessor Processor()
        {
            return new PersonDetectionPostProcessor(
                new PersonDetectionPostProcessorSettings
                {
                    coordinateSpace = PersonBoxCoordinateSpace.ModelPixels,
                    boxFormat = PersonBoxFormat.CenterXYWH,
                    personClassId = 0,
                    confidenceThreshold = 0.55f,
                    iouThreshold = 0.45f
                });
        }
    }
}
