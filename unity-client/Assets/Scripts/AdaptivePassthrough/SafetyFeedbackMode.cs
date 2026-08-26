using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    public enum SafetyFeedbackMode
    {
        Passthrough = 0,
        RedBorderAndHaptics = 1
    }

    public static class SafetyFeedbackPulse
    {
        public static bool IsHapticPulseOn(
            float timeSeconds,
            float intensity)
        {
            float risk = Mathf.Clamp01(intensity);
            float period = Mathf.Lerp(0.42f, 0.22f, risk);
            float onDuration = period * Mathf.Lerp(0.32f, 0.52f, risk);
            float phase = Mathf.Repeat(
                Mathf.Max(0f, timeSeconds),
                period);
            return phase <= onDuration;
        }

        public static float VisualPulse(float timeSeconds, float intensity)
        {
            float speed = Mathf.Lerp(
                3.5f,
                7.5f,
                Mathf.Clamp01(intensity));
            return 0.68f + 0.32f * (0.5f + 0.5f
                * Mathf.Sin(Mathf.Max(0f, timeSeconds) * speed));
        }
    }
}
