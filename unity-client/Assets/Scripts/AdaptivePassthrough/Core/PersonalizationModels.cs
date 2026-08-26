using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    [Serializable]
    public struct PersonalizationFeatureVector
    {
        public float ActivationFrequency;
        public float ManualCancelRatio;
        public float MeanPassthroughDurationSeconds;
        public float MeanHeadSpeedMetersPerSecond;
        public float MaximumHeadSpeedMetersPerSecond;
        public float NormalizedSpaceArea;
        public float NormalizedSessionElapsed;

        public PersonalizationFeatureVector(
            float activationFrequency,
            float manualCancelRatio,
            float meanPassthroughDurationSeconds,
            float meanHeadSpeedMetersPerSecond,
            float maximumHeadSpeedMetersPerSecond,
            float normalizedSpaceArea,
            float normalizedSessionElapsed)
        {
            ActivationFrequency = Clamp01(activationFrequency);
            ManualCancelRatio = Clamp01(manualCancelRatio);
            MeanPassthroughDurationSeconds =
                NonNegative(meanPassthroughDurationSeconds);
            MeanHeadSpeedMetersPerSecond =
                NonNegative(meanHeadSpeedMetersPerSecond);
            MaximumHeadSpeedMetersPerSecond = Mathf.Max(
                MeanHeadSpeedMetersPerSecond,
                NonNegative(maximumHeadSpeedMetersPerSecond));
            NormalizedSpaceArea = Clamp01(normalizedSpaceArea);
            NormalizedSessionElapsed =
                Clamp01(normalizedSessionElapsed);
        }

        public float[] ToArray()
        {
            return new[]
            {
                ActivationFrequency,
                ManualCancelRatio,
                MeanPassthroughDurationSeconds,
                MeanHeadSpeedMetersPerSecond,
                MaximumHeadSpeedMetersPerSecond,
                NormalizedSpaceArea,
                NormalizedSessionElapsed
            };
        }

        private static float Clamp01(float value)
        {
            return Mathf.Clamp01(Safe(value));
        }

        private static float NonNegative(float value)
        {
            return Mathf.Max(0f, Safe(value));
        }

        private static float Safe(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : value;
        }
    }

    [Serializable]
    public struct PersonalizedThresholds
    {
        public float StableOnThreshold;
        public float RapidOnThreshold;
        public float HandFullThreshold;
        public float DynamicOnThreshold;
        public float DynamicOffThreshold;
        public float AdjustmentDelta;

        public PersonalizedThresholds(
            float stableOnThreshold,
            float rapidOnThreshold,
            float handFullThreshold)
            : this(
                stableOnThreshold,
                rapidOnThreshold,
                handFullThreshold,
                PersonalizationMath.DefaultDynamicOnThreshold,
                PersonalizationMath.DefaultDynamicOffThreshold)
        {
        }

        public PersonalizedThresholds(
            float stableOnThreshold,
            float rapidOnThreshold,
            float handFullThreshold,
            float dynamicOnThreshold,
            float dynamicOffThreshold)
            : this(
                stableOnThreshold,
                rapidOnThreshold,
                handFullThreshold,
                dynamicOnThreshold,
                dynamicOffThreshold,
                CalculateAdjustmentDelta(
                    stableOnThreshold,
                    rapidOnThreshold,
                    handFullThreshold,
                    dynamicOnThreshold,
                    dynamicOffThreshold))
        {
        }

        public PersonalizedThresholds(
            float stableOnThreshold,
            float rapidOnThreshold,
            float handFullThreshold,
            float dynamicOnThreshold,
            float dynamicOffThreshold,
            float adjustmentDelta)
        {
            StableOnThreshold = Mathf.Clamp01(stableOnThreshold);
            RapidOnThreshold = Mathf.Clamp01(rapidOnThreshold);
            HandFullThreshold = Mathf.Clamp01(handFullThreshold);
            DynamicOnThreshold = Mathf.Clamp01(dynamicOnThreshold);
            DynamicOffThreshold = Mathf.Min(
                DynamicOnThreshold,
                Mathf.Clamp01(dynamicOffThreshold));
            AdjustmentDelta = Mathf.Clamp(
                Safe(adjustmentDelta),
                0f,
                PersonalizationMath.MaximumAdjustmentDelta);
        }

        private static float CalculateAdjustmentDelta(
            float stableOnThreshold,
            float rapidOnThreshold,
            float handFullThreshold,
            float dynamicOnThreshold,
            float dynamicOffThreshold)
        {
            return Mathf.Max(
                0f,
                stableOnThreshold
                    - PersonalizationMath.DefaultStableOnThreshold,
                rapidOnThreshold
                    - PersonalizationMath.DefaultRapidOnThreshold,
                handFullThreshold
                    - PersonalizationMath.DefaultHandFullThreshold,
                dynamicOnThreshold
                    - PersonalizationMath.DefaultDynamicOnThreshold,
                dynamicOffThreshold
                    - PersonalizationMath.DefaultDynamicOffThreshold);
        }

        private static float Safe(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : value;
        }
    }

    public static class PersonalizationMath
    {
        public const float DefaultStableOnThreshold = 0.65f;
        public const float DefaultRapidOnThreshold = 0.45f;
        public const float DefaultHandFullThreshold = 0.85f;
        public const float DefaultDynamicOnThreshold = 0.60f;
        public const float DefaultDynamicOffThreshold = 0.45f;
        public const float MaximumAdjustmentDelta = 0.10f;
        public const float DefaultAdjustmentScale = 0.20f;
        public const float DefaultMaximumThreshold = 0.95f;
        public const float DefaultSpaceMinimumSquareMeters = 4f;
        public const float DefaultSpaceMaximumSquareMeters = 8f;
        public const float DefaultSessionMaximumSeconds = 30f * 60f;
        public const int DefaultMinimumSessionCount = 5;
        public const int DefaultEventWindowSize = 3;

        public static PersonalizedThresholds Defaults
        {
            get
            {
                return new PersonalizedThresholds(
                    DefaultStableOnThreshold,
                    DefaultRapidOnThreshold,
                    DefaultHandFullThreshold,
                    DefaultDynamicOnThreshold,
                    DefaultDynamicOffThreshold,
                    0f);
            }
        }

        public static PersonalizationFeatureVector BuildFeatureVector(
            int activationCount,
            int eventWindowSize,
            int manualCancelCount,
            float meanDurationSeconds,
            float meanHeadSpeedMetersPerSecond,
            float maximumHeadSpeedMetersPerSecond,
            float spaceAreaSquareMeters,
            float sessionElapsedSeconds,
            float spaceMinimumSquareMeters =
                DefaultSpaceMinimumSquareMeters,
            float spaceMaximumSquareMeters =
                DefaultSpaceMaximumSquareMeters,
            float sessionMaximumSeconds =
                DefaultSessionMaximumSeconds)
        {
            int safeWindow = Math.Max(1, eventWindowSize);
            int safeActivations = Math.Max(0, activationCount);
            float activationFrequency = Mathf.Clamp01(
                (float)safeActivations / safeWindow);
            float cancelRatio = safeActivations == 0
                ? 0f
                : Mathf.Clamp01(
                    (float)Math.Max(0, manualCancelCount)
                    / safeActivations);
            float spaceNorm = InverseLerpSafe(
                spaceMinimumSquareMeters,
                spaceMaximumSquareMeters,
                spaceAreaSquareMeters);
            float sessionNorm = Mathf.Clamp01(
                Safe(sessionElapsedSeconds)
                / Mathf.Max(1f, Safe(sessionMaximumSeconds)));

            return new PersonalizationFeatureVector(
                activationFrequency,
                cancelRatio,
                meanDurationSeconds,
                meanHeadSpeedMetersPerSecond,
                maximumHeadSpeedMetersPerSecond,
                spaceNorm,
                sessionNorm);
        }

        public static PersonalizedThresholds FromNegativeProbability(
            float negativeProbability,
            float adjustmentScale = DefaultAdjustmentScale,
            float maximumThreshold = DefaultMaximumThreshold)
        {
            float probability = Mathf.Clamp01(Safe(negativeProbability));
            float scale = Mathf.Max(0f, Safe(adjustmentScale));
            float maximum = Mathf.Clamp01(Safe(maximumThreshold));
            float delta = Mathf.Clamp(
                Mathf.Max((probability - 0.5f) * scale, 0f),
                0f,
                MaximumAdjustmentDelta);

            float stableDelta = CappedDelta(
                delta,
                DefaultStableOnThreshold,
                maximum);
            float rapidDelta = CappedDelta(
                delta,
                DefaultRapidOnThreshold,
                maximum);
            float handDelta = CappedDelta(
                delta,
                DefaultHandFullThreshold,
                maximum);
            float dynamicDelta = CappedDelta(
                delta,
                DefaultDynamicOnThreshold,
                maximum);

            return new PersonalizedThresholds(
                DefaultStableOnThreshold + stableDelta,
                DefaultRapidOnThreshold + rapidDelta,
                DefaultHandFullThreshold + handDelta,
                DefaultDynamicOnThreshold + dynamicDelta,
                DefaultDynamicOffThreshold + dynamicDelta,
                delta);
        }

        public static bool IsColdStart(
            int accumulatedSessionCount,
            int minimumSessionCount = DefaultMinimumSessionCount)
        {
            return Math.Max(0, accumulatedSessionCount)
                < Math.Max(0, minimumSessionCount);
        }

        private static float CappedDelta(
            float requestedDelta,
            float baseline,
            float maximumThreshold)
        {
            return Mathf.Min(
                Mathf.Max(0f, requestedDelta),
                Mathf.Max(0f, maximumThreshold - baseline));
        }

        private static float InverseLerpSafe(
            float minimum,
            float maximum,
            float value)
        {
            float safeMinimum = Safe(minimum);
            float safeMaximum = Safe(maximum);
            if (safeMaximum <= safeMinimum + 0.0001f)
            {
                return 0f;
            }

            return Mathf.InverseLerp(
                safeMinimum,
                safeMaximum,
                Safe(value));
        }

        private static float Safe(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : value;
        }
    }
}
