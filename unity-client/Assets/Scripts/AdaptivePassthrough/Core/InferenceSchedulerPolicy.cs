using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    public enum InferenceWatchdogState
    {
        Idle,
        Normal,
        Recovery,
        Reset
    }

    public static class InferenceSchedulerPolicy
    {
        public const float RecoveryThresholdMilliseconds = 750f;
        public const float ResetThresholdMilliseconds = 1500f;

        public static float UpdateSliceBudget(
            TrackingQualityProfile profile,
            float configuredBudgetMilliseconds,
            float currentBudgetMilliseconds,
            float frameP95Milliseconds,
            float projectedCompletionMilliseconds,
            float wallMilliseconds)
        {
            float configured = Mathf.Max(
                0.1f,
                configuredBudgetMilliseconds);
            if (wallMilliseconds >= RecoveryThresholdMilliseconds)
            {
                return 4f;
            }

            if (profile != TrackingQualityProfile.Balanced)
            {
                return configured;
            }

            float current = currentBudgetMilliseconds <= 0f
                ? configured
                : currentBudgetMilliseconds;
            if (projectedCompletionMilliseconds > 500f
                && frameP95Milliseconds <= 12.5f)
            {
                return Mathf.Min(2.25f, current + 0.25f);
            }

            return Mathf.MoveTowards(current, configured, 0.25f);
        }

        public static InferenceWatchdogState GetWatchdogState(
            bool active,
            float wallMilliseconds)
        {
            if (!active)
            {
                return InferenceWatchdogState.Idle;
            }

            if (wallMilliseconds >= ResetThresholdMilliseconds)
            {
                return InferenceWatchdogState.Reset;
            }

            return wallMilliseconds >= RecoveryThresholdMilliseconds
                ? InferenceWatchdogState.Recovery
                : InferenceWatchdogState.Normal;
        }

        public static float EstimateCompletionMilliseconds(
            float wallMilliseconds,
            int scheduledLayers,
            int expectedLayerCount)
        {
            if (scheduledLayers <= 0 || expectedLayerCount <= 0)
            {
                return wallMilliseconds;
            }

            return wallMilliseconds
                * expectedLayerCount
                / scheduledLayers;
        }
    }
}
