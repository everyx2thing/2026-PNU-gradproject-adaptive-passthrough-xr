using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    public enum PassthroughRiskSource
    {
        Static,
        Dynamic
    }

    public sealed class PassthroughSourceDecision
    {
        public readonly long Sequence;
        public readonly double TimestampSeconds;
        public readonly PassthroughRiskSource Source;
        public readonly bool Available;
        public readonly float Risk;
        public readonly PassthroughDecisionSnapshot FilterDecision;

        public PassthroughSourceDecision(
            long sequence,
            double timestampSeconds,
            PassthroughRiskSource source,
            bool available,
            float risk,
            PassthroughDecisionSnapshot filterDecision)
        {
            Sequence = Math.Max(0L, sequence);
            TimestampSeconds = Math.Max(0.0, timestampSeconds);
            Source = source;
            Available = available;
            Risk = Clamp01(risk);
            FilterDecision = filterDecision;
        }

        public bool Enabled
        {
            get
            {
                return FilterDecision != null
                    && FilterDecision.Enabled;
            }
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            return Mathf.Clamp01(value);
        }
    }

    public static class SelectivePassthroughMath
    {
        public static float StateRisk(UserMotionState state)
        {
            switch (state)
            {
                case UserMotionState.Agitated:
                    return 1f;
                case UserMotionState.Dynamic:
                    return 0.5f;
                default:
                    return 0f;
            }
        }

        public static float StaticDecisionRisk(
            float staticRisk,
            float stateRisk,
            float staticWeight = 0.60f,
            float stateWeight = 0.40f)
        {
            float safeStaticWeight = NonNegative(staticWeight);
            float safeStateWeight = NonNegative(stateWeight);
            float weightSum = safeStaticWeight + safeStateWeight;
            if (weightSum <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(
                (safeStaticWeight * SanitizeRisk(staticRisk)
                + safeStateWeight * SanitizeRisk(stateRisk))
                / weightSum);
        }

        public static Rect PersonWindowRect(
            NormalizedBoundingBox box,
            Rect cameraViewport,
            float paddingRatio)
        {
            Rect safeViewport = ClampRect01(cameraViewport);
            float left = safeViewport.x + box.Left * safeViewport.width;
            float bottom =
                safeViewport.y
                + (1f - box.Bottom) * safeViewport.height;
            float width = box.width * safeViewport.width;
            float height = box.height * safeViewport.height;
            float safePadding = Mathf.Max(0f, SanitizeFinite(paddingRatio));
            float paddingX = width * safePadding;
            float paddingY = height * safePadding;

            return ClampRectTo(
                new Rect(
                    left - paddingX,
                    bottom - paddingY,
                    width + paddingX * 2f,
                    height + paddingY * 2f),
                safeViewport);
        }

        public static Rect FocusedPersonWindowRect(
            NormalizedBoundingBox box,
            Rect cameraViewport,
            float risk,
            float minimumWidth = 0.10f,
            float maximumWidth = 0.32f,
            float minimumHeight = 0.16f,
            float maximumHeight = 0.48f)
        {
            Rect safeViewport = ClampRect01(cameraViewport);
            Vector2 center = new Vector2(
                safeViewport.x
                    + box.centerX * safeViewport.width,
                safeViewport.y
                    + (1f - box.centerY) * safeViewport.height);
            float minWidth = Mathf.Clamp(
                SanitizeFinite(minimumWidth),
                0.01f,
                safeViewport.width);
            float maxWidth = Mathf.Clamp(
                SanitizeFinite(maximumWidth),
                minWidth,
                safeViewport.width);
            float minHeight = Mathf.Clamp(
                SanitizeFinite(minimumHeight),
                0.01f,
                safeViewport.height);
            float maxHeight = Mathf.Clamp(
                SanitizeFinite(maximumHeight),
                minHeight,
                safeViewport.height);
            float width = Mathf.Clamp(
                box.width * safeViewport.width * 0.82f + 0.01f,
                minWidth,
                maxWidth);
            float height = Mathf.Clamp(
                box.height * safeViewport.height * 0.78f + 0.01f,
                minHeight,
                maxHeight);
            float riskScale = Mathf.Lerp(
                0.90f,
                1.05f,
                SanitizeRisk(risk));
            width = Mathf.Min(maxWidth, width * riskScale);
            height = Mathf.Min(maxHeight, height * riskScale);

            return ClampRectTo(
                new Rect(
                    center.x - width * 0.5f,
                    center.y - height * 0.5f,
                    width,
                    height),
                safeViewport);
        }

        public static bool IsViewportDirectionVisible(
            Vector3 viewportPoint,
            float edgeMargin = 0f)
        {
            float margin = Mathf.Clamp(
                SanitizeFinite(edgeMargin),
                0f,
                0.49f);
            return IsFinite(viewportPoint.x)
                && IsFinite(viewportPoint.y)
                && IsFinite(viewportPoint.z)
                && viewportPoint.z > 0f
                && viewportPoint.x >= margin
                && viewportPoint.x <= 1f - margin
                && viewportPoint.y >= margin
                && viewportPoint.y <= 1f - margin;
        }

        public static Rect WallDirectionWindowRect(
            float viewportX,
            float risk,
            float minimumWidth = 0.24f,
            float maximumWidth = 0.52f,
            float verticalMargin = 0.08f)
        {
            float safeRisk = SanitizeRisk(risk);
            float minWidth = Mathf.Clamp01(SanitizeFinite(minimumWidth));
            float maxWidth = Mathf.Clamp(
                SanitizeFinite(maximumWidth),
                minWidth,
                1f);
            float width = Mathf.Lerp(minWidth, maxWidth, safeRisk);
            float margin = Mathf.Clamp(
                SanitizeFinite(verticalMargin),
                0f,
                0.45f);
            float x;
            if (viewportX < 0.40f)
            {
                x = 0f;
            }
            else if (viewportX > 0.60f)
            {
                x = 1f - width;
            }
            else
            {
                x = Mathf.Clamp01(viewportX) - width * 0.5f;
            }

            return ClampRect01(
                new Rect(
                    x,
                    margin,
                    width,
                    1f - margin * 2f));
        }

        private static Rect ClampRectTo(Rect rect, Rect bounds)
        {
            float xMin = Mathf.Clamp(rect.xMin, bounds.xMin, bounds.xMax);
            float xMax = Mathf.Clamp(rect.xMax, bounds.xMin, bounds.xMax);
            float yMin = Mathf.Clamp(rect.yMin, bounds.yMin, bounds.yMax);
            float yMax = Mathf.Clamp(rect.yMax, bounds.yMin, bounds.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static Rect ClampRect01(Rect rect)
        {
            float xMin = Mathf.Clamp01(Mathf.Min(rect.xMin, rect.xMax));
            float xMax = Mathf.Clamp01(Mathf.Max(rect.xMin, rect.xMax));
            float yMin = Mathf.Clamp01(Mathf.Min(rect.yMin, rect.yMax));
            float yMax = Mathf.Clamp01(Mathf.Max(rect.yMin, rect.yMax));
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static float NonNegative(float value)
        {
            return Mathf.Max(0f, SanitizeFinite(value));
        }

        private static float SanitizeRisk(float value)
        {
            return Mathf.Clamp01(SanitizeFinite(value));
        }

        private static float SanitizeFinite(float value)
        {
            return IsFinite(value) ? value : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
