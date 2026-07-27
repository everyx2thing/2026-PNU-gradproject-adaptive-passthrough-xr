using System;

namespace TeamVR.AdaptivePassthrough
{
    [Serializable]
    public sealed class OverallRiskWeights
    {
        public float staticRisk = 0.40f;
        public float stateRisk = 0.20f;
        public float dynamicRisk = 0.40f;
        public float intentRisk = 0f;
    }

    public static class OverallRiskFusion
    {
        public static OverallRiskResult Calculate(
            float staticRisk,
            float stateRisk,
            float dynamicRisk,
            float intentRisk,
            OverallRiskWeights weights = null)
        {
            weights = weights ?? new OverallRiskWeights();
            float weightSum = Math.Max(
                0f,
                weights.staticRisk + weights.stateRisk
                + weights.dynamicRisk + weights.intentRisk);
            if (weightSum <= 0f)
            {
                weightSum = 1f;
            }

            float total = (
                weights.staticRisk * Clamp01(staticRisk)
                + weights.stateRisk * Clamp01(stateRisk)
                + weights.dynamicRisk * Clamp01(dynamicRisk)
                + weights.intentRisk * Clamp01(intentRisk))
                / weightSum;

            return new OverallRiskResult(
                Clamp01(staticRisk),
                Clamp01(stateRisk),
                Clamp01(dynamicRisk),
                Clamp01(intentRisk),
                Clamp01(total));
        }

        public static OverallRiskResult CalculateAvailable(
            float staticRisk,
            bool staticAvailable,
            float stateRisk,
            bool stateAvailable,
            float dynamicRisk,
            bool dynamicAvailable,
            float intentRisk,
            bool intentAvailable,
            OverallRiskWeights weights = null)
        {
            weights = weights ?? new OverallRiskWeights();
            float safeStatic = staticAvailable ? Clamp01(staticRisk) : 0f;
            float safeState = stateAvailable ? Clamp01(stateRisk) : 0f;
            float safeDynamic = dynamicAvailable ? Clamp01(dynamicRisk) : 0f;
            float safeIntent = intentAvailable ? Clamp01(intentRisk) : 0f;

            float staticWeight = staticAvailable
                ? NonNegative(weights.staticRisk)
                : 0f;
            float stateWeight = stateAvailable
                ? NonNegative(weights.stateRisk)
                : 0f;
            float dynamicWeight = dynamicAvailable
                ? NonNegative(weights.dynamicRisk)
                : 0f;
            float intentWeight = intentAvailable
                ? NonNegative(weights.intentRisk)
                : 0f;
            float availableWeightSum =
                staticWeight + stateWeight + dynamicWeight + intentWeight;
            if (availableWeightSum <= 0f)
            {
                return new OverallRiskResult(
                    false,
                    0f,
                    safeStatic,
                    safeState,
                    safeDynamic,
                    safeIntent,
                    0f);
            }

            float total =
                (staticWeight * safeStatic
                + stateWeight * safeState
                + dynamicWeight * safeDynamic
                + intentWeight * safeIntent)
                / availableWeightSum;
            return new OverallRiskResult(
                true,
                availableWeightSum,
                safeStatic,
                safeState,
                safeDynamic,
                safeIntent,
                Clamp01(total));
        }

        private static float NonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            return Math.Max(0f, value);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
