using System;
using System.Collections.Generic;

namespace TeamVR.AdaptivePassthrough
{
    public sealed class PersonDetectionPostProcessResult
    {
        public readonly IReadOnlyList<DynamicObjectDetection> Detections;
        public readonly int RawCandidateCount;
        public readonly int PersonCandidateCount;
        public readonly int BelowConfidenceCount;
        public readonly int InvalidBoxCount;
        public readonly int SuppressedByNmsCount;
        public readonly int LimitedCandidateCount;

        public PersonDetectionPostProcessResult(
            IReadOnlyList<DynamicObjectDetection> detections,
            int rawCandidateCount,
            int personCandidateCount,
            int belowConfidenceCount,
            int invalidBoxCount,
            int suppressedByNmsCount,
            int limitedCandidateCount)
        {
            Detections = detections ?? Array.Empty<DynamicObjectDetection>();
            RawCandidateCount = rawCandidateCount;
            PersonCandidateCount = personCandidateCount;
            BelowConfidenceCount = belowConfidenceCount;
            InvalidBoxCount = invalidBoxCount;
            SuppressedByNmsCount = suppressedByNmsCount;
            LimitedCandidateCount = limitedCandidateCount;
        }
    }

    public sealed class PersonDetectionPostProcessor
    {
        private sealed class Candidate
        {
            public int SourceIndex;
            public float Confidence;
            public NormalizedBoundingBox Box;
        }

        private readonly PersonDetectionPostProcessorSettings settings;

        public PersonDetectionPostProcessor(
            PersonDetectionPostProcessorSettings settings = null)
        {
            this.settings = settings ?? new PersonDetectionPostProcessorSettings();
        }

        public PersonDetectionPostProcessResult Process(
            IReadOnlyList<float> boxes,
            IReadOnlyList<int> classIds,
            IReadOnlyList<float> scores,
            int inputWidth,
            int inputHeight)
        {
            if (boxes == null || classIds == null || scores == null)
            {
                return Empty();
            }

            if (inputWidth <= 0 || inputHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inputWidth),
                    "Model input dimensions must be positive.");
            }

            int rawCount = Math.Min(
                boxes.Count / 4,
                Math.Min(classIds.Count, scores.Count));
            int personCount = 0;
            int belowConfidence = 0;
            int invalidCount = 0;
            var candidates = new List<Candidate>();

            for (int i = 0; i < rawCount; i++)
            {
                if (classIds[i] != settings.personClassId)
                {
                    continue;
                }

                personCount++;
                float score = scores[i];
                if (!IsFinite(score) || score < settings.confidenceThreshold)
                {
                    belowConfidence++;
                    continue;
                }

                if (!TryDecode(
                        boxes,
                        i * 4,
                        inputWidth,
                        inputHeight,
                        out NormalizedBoundingBox decoded))
                {
                    invalidCount++;
                    continue;
                }

                candidates.Add(new Candidate
                {
                    SourceIndex = i,
                    Confidence = Clamp01(score),
                    Box = decoded
                });
            }

            candidates.Sort((a, b) =>
            {
                int confidenceOrder = b.Confidence.CompareTo(a.Confidence);
                return confidenceOrder != 0
                    ? confidenceOrder
                    : a.SourceIndex.CompareTo(b.SourceIndex);
            });

            int limitedCandidates = 0;
            int maximumCandidates = Math.Max(1, settings.maximumCandidates);
            if (candidates.Count > maximumCandidates)
            {
                limitedCandidates = candidates.Count - maximumCandidates;
                candidates.RemoveRange(
                    maximumCandidates,
                    candidates.Count - maximumCandidates);
            }

            int maximumDetections = Math.Max(1, settings.maximumDetections);
            float iouThreshold = Clamp01(settings.iouThreshold);
            int suppressed = 0;
            var selected = new List<DynamicObjectDetection>();
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate candidate = candidates[i];
                bool overlaps = false;
                for (int j = 0; j < selected.Count; j++)
                {
                    float iou = SimpleObjectTracker.IntersectionOverUnion(
                        candidate.Box,
                        selected[j].boundingBox);
                    if (iou >= iouThreshold)
                    {
                        overlaps = true;
                        suppressed++;
                        break;
                    }
                }

                if (overlaps)
                {
                    continue;
                }

                if (selected.Count >= maximumDetections)
                {
                    limitedCandidates++;
                    continue;
                }

                selected.Add(new DynamicObjectDetection(
                    "person",
                    candidate.Confidence,
                    candidate.Box,
                    settings.personClassId));
            }

            return new PersonDetectionPostProcessResult(
                selected,
                rawCount,
                personCount,
                belowConfidence,
                invalidCount,
                suppressed,
                limitedCandidates);
        }

        private bool TryDecode(
            IReadOnlyList<float> boxes,
            int offset,
            int inputWidth,
            int inputHeight,
            out NormalizedBoundingBox box)
        {
            box = default;
            float a = boxes[offset];
            float b = boxes[offset + 1];
            float c = boxes[offset + 2];
            float d = boxes[offset + 3];
            if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c) || !IsFinite(d))
            {
                return false;
            }

            float xScale = settings.coordinateSpace == PersonBoxCoordinateSpace.ModelPixels
                ? inputWidth
                : 1f;
            float yScale = settings.coordinateSpace == PersonBoxCoordinateSpace.ModelPixels
                ? inputHeight
                : 1f;

            float left;
            float top;
            float right;
            float bottom;
            if (settings.boxFormat == PersonBoxFormat.CenterXYWH)
            {
                float centerX = a / xScale;
                float centerY = b / yScale;
                float width = c / xScale;
                float height = d / yScale;
                if (width <= 0f || height <= 0f)
                {
                    return false;
                }

                left = centerX - width * 0.5f;
                top = centerY - height * 0.5f;
                right = centerX + width * 0.5f;
                bottom = centerY + height * 0.5f;
            }
            else
            {
                left = a / xScale;
                top = b / yScale;
                right = c / xScale;
                bottom = d / yScale;
            }

            if (!IsFinite(left)
                || !IsFinite(top)
                || !IsFinite(right)
                || !IsFinite(bottom)
                || right <= left
                || bottom <= top)
            {
                return false;
            }

            if (settings.flipVertical)
            {
                float flippedTop = 1f - bottom;
                bottom = 1f - top;
                top = flippedTop;
            }

            float rawWidth = right - left;
            float rawHeight = bottom - top;
            float maximumDimension = Math.Max(1f, settings.maximumNormalizedDimension);
            if (rawWidth > maximumDimension || rawHeight > maximumDimension)
            {
                return false;
            }

            float clippedLeft = Clamp01(left);
            float clippedTop = Clamp01(top);
            float clippedRight = Clamp01(right);
            float clippedBottom = Clamp01(bottom);
            if (clippedRight <= clippedLeft || clippedBottom <= clippedTop)
            {
                return false;
            }

            float rawArea = rawWidth * rawHeight;
            float visibleArea =
                (clippedRight - clippedLeft) * (clippedBottom - clippedTop);
            float visibleFraction = rawArea <= 0f ? 0f : visibleArea / rawArea;
            if (visibleFraction < Clamp01(settings.minimumVisibleFraction))
            {
                return false;
            }

            float clippedWidth = clippedRight - clippedLeft;
            float clippedHeight = clippedBottom - clippedTop;
            box = new NormalizedBoundingBox(
                (clippedLeft + clippedRight) * 0.5f,
                (clippedTop + clippedBottom) * 0.5f,
                clippedWidth,
                clippedHeight);
            return box.Area > 0f;
        }

        private static PersonDetectionPostProcessResult Empty()
        {
            return new PersonDetectionPostProcessResult(
                Array.Empty<DynamicObjectDetection>(),
                0,
                0,
                0,
                0,
                0,
                0);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
