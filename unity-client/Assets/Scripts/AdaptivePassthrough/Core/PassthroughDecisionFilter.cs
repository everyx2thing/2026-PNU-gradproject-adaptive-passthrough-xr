using System;

namespace TeamVR.AdaptivePassthrough
{
    [Serializable]
    public sealed class PassthroughDecisionFilterSettings
    {
        public PassthroughDecisionMode mode =
            PassthroughDecisionMode.CompatibilityThreshold;
        public float onThreshold = 0.60f;
        public float offThreshold = 0.50f;
        public float minimumHoldSeconds = 0.50f;
        public float releaseDelaySeconds = 0.25f;
    }

    public sealed class PassthroughDecisionFilter
    {
        private readonly PassthroughDecisionFilterSettings settings;
        private bool enabled;
        private double stateChangedAt;
        private double releaseCandidateSince;
        private bool releasePending;
        private bool initialized;

        public PassthroughDecisionFilter(
            PassthroughDecisionFilterSettings settings = null)
        {
            this.settings = settings ?? new PassthroughDecisionFilterSettings();
        }

        public PassthroughDecisionSnapshot Evaluate(
            double timestampSeconds,
            bool overallAvailable,
            float totalRisk)
        {
            double now = SanitizeTimestamp(timestampSeconds);
            float onThreshold = Clamp01(settings.onThreshold);
            float offThreshold = Math.Min(
                onThreshold,
                Clamp01(settings.offThreshold));
            float minimumHoldSeconds = Math.Max(
                0f,
                SanitizeFinite(settings.minimumHoldSeconds));
            float releaseDelaySeconds = Math.Max(
                0f,
                SanitizeFinite(settings.releaseDelaySeconds));

            if (!initialized)
            {
                initialized = true;
                stateChangedAt = now;
            }

            if (!overallAvailable)
            {
                if (settings.mode == PassthroughDecisionMode.Hysteresis
                    && enabled)
                {
                    return HoldOrRelease(
                        now,
                        minimumHoldSeconds,
                        releaseDelaySeconds,
                        PassthroughDecisionReason.NoRiskInputs,
                        onThreshold,
                        offThreshold);
                }

                SetEnabled(false, now);
                return Create(
                    PassthroughDecisionReason.NoRiskInputs,
                    now,
                    onThreshold,
                    offThreshold);
            }

            float risk = Clamp01(totalRisk);
            if (settings.mode == PassthroughDecisionMode.CompatibilityThreshold)
            {
                bool requested = risk >= onThreshold;
                SetEnabled(requested, now);
                return Create(
                    requested
                        ? PassthroughDecisionReason.AtOrAboveThreshold
                        : PassthroughDecisionReason.BelowThreshold,
                    now,
                    onThreshold,
                    offThreshold);
            }

            if (!enabled)
            {
                if (risk >= onThreshold)
                {
                    SetEnabled(true, now);
                    return Create(
                        PassthroughDecisionReason.AtOrAboveThreshold,
                        now,
                        onThreshold,
                        offThreshold);
                }

                return Create(
                    PassthroughDecisionReason.BelowThreshold,
                    now,
                    onThreshold,
                    offThreshold);
            }

            if (risk < offThreshold)
            {
                return HoldOrRelease(
                    now,
                    minimumHoldSeconds,
                    releaseDelaySeconds,
                    PassthroughDecisionReason.BelowThreshold,
                    onThreshold,
                    offThreshold);
            }

            CancelPendingRelease();
            return Create(
                PassthroughDecisionReason.HysteresisHeld,
                now,
                onThreshold,
                offThreshold);
        }

        public void Reset()
        {
            enabled = false;
            stateChangedAt = 0.0;
            releaseCandidateSince = 0.0;
            releasePending = false;
            initialized = false;
        }

        private PassthroughDecisionSnapshot HoldOrRelease(
            double now,
            float minimumHoldSeconds,
            float releaseDelaySeconds,
            PassthroughDecisionReason releasedReason,
            float onThreshold,
            float offThreshold)
        {
            if (!releasePending)
            {
                releasePending = true;
                releaseCandidateSince = now;
            }

            if (ElapsedSinceStateChange(now) < minimumHoldSeconds)
            {
                return Create(
                    PassthroughDecisionReason.MinimumHoldActive,
                    now,
                    onThreshold,
                    offThreshold);
            }

            if (now - releaseCandidateSince < releaseDelaySeconds)
            {
                return Create(
                    PassthroughDecisionReason.ReleaseDelayActive,
                    now,
                    onThreshold,
                    offThreshold);
            }

            SetEnabled(false, now);
            return Create(
                releasedReason,
                now,
                onThreshold,
                offThreshold);
        }

        private PassthroughDecisionSnapshot Create(
            PassthroughDecisionReason reason,
            double now,
            float onThreshold,
            float offThreshold)
        {
            return new PassthroughDecisionSnapshot(
                enabled,
                reason,
                onThreshold,
                offThreshold,
                ElapsedSinceStateChange(now));
        }

        private void SetEnabled(bool value, double now)
        {
            if (enabled == value)
            {
                return;
            }

            enabled = value;
            stateChangedAt = now;
            CancelPendingRelease();
        }

        private void CancelPendingRelease()
        {
            releaseCandidateSince = 0.0;
            releasePending = false;
        }

        private float ElapsedSinceStateChange(double now)
        {
            return (float)Math.Max(0.0, now - stateChangedAt);
        }

        private static double SanitizeTimestamp(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? 0.0
                : Math.Max(0.0, value);
        }

        private static float SanitizeFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, SanitizeFinite(value)));
        }
    }
}
