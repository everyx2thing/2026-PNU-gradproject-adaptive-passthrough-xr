using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    public enum SafetyHapticTarget
    {
        None = 0,
        Left = 1,
        Right = 2,
        Both = 3
    }

    [Serializable]
    public sealed class RearHazardWarningSettings
    {
        [Range(90f, 180f)] public float rearAngleDegrees = 120f;
        [Min(0f)] public float minimumClosingSpeedMetersPerSecond = 0.075f;
        [Min(0f)] public float emergencyDistanceMeters = 0.25f;
        [Range(0f, 45f)] public float bothControllersRearConeDegrees = 15f;
        [Min(0.01f)] public float borderPulseSeconds = 0.30f;
        [Min(0.01f)] public float hapticBurstSeconds = 0.70f;
        [Min(0f)] public float minimumHoldSeconds = 1.50f;
        [Min(0f)] public float rewarningIntervalSeconds = 1.50f;
        [Min(0.01f)] public float distanceRiskFarMeters = 2.50f;
        [Min(0.01f)] public float ttcRiskSafeSeconds = 4.0f;
        [Min(0.01f)] public float ttcRiskFullSeconds = 0.50f;
    }

    public readonly struct RearHazardWarningSnapshot
    {
        public readonly bool QualifyingNow;
        public readonly bool WarningActive;
        public readonly bool BorderPulseActive;
        public readonly bool HapticBurstActive;
        public readonly bool Emergency;
        public readonly SafetyHapticTarget HapticTarget;
        public readonly StaticHazardKey HazardKey;
        public readonly float Intensity;
        public readonly float AngleDegrees;
        public readonly float DistanceMeters;
        public readonly float ClosingSpeedMetersPerSecond;
        public readonly float TtcSeconds;
        public readonly float HoldRemainingSeconds;
        public readonly float RearmRemainingSeconds;

        public RearHazardWarningSnapshot(
            bool qualifyingNow,
            bool warningActive,
            bool borderPulseActive,
            bool hapticBurstActive,
            bool emergency,
            SafetyHapticTarget hapticTarget,
            StaticHazardKey hazardKey,
            float intensity,
            float angleDegrees,
            float distanceMeters,
            float closingSpeedMetersPerSecond,
            float ttcSeconds,
            float holdRemainingSeconds,
            float rearmRemainingSeconds)
        {
            QualifyingNow = qualifyingNow;
            WarningActive = warningActive;
            BorderPulseActive = borderPulseActive;
            HapticBurstActive = hapticBurstActive;
            Emergency = emergency;
            HapticTarget = warningActive ? hapticTarget : SafetyHapticTarget.None;
            HazardKey = warningActive ? hazardKey : StaticHazardKey.None;
            Intensity = Mathf.Clamp01(intensity);
            AngleDegrees = Mathf.Clamp(angleDegrees, 0f, 180f);
            DistanceMeters = Mathf.Max(0f, distanceMeters);
            ClosingSpeedMetersPerSecond = Mathf.Max(
                0f,
                closingSpeedMetersPerSecond);
            TtcSeconds = Mathf.Max(0f, ttcSeconds);
            HoldRemainingSeconds = Mathf.Max(0f, holdRemainingSeconds);
            RearmRemainingSeconds = Mathf.Max(0f, rearmRemainingSeconds);
        }

        public static RearHazardWarningSnapshot Inactive => default;
    }

    /// <summary>
    /// Produces a short directional warning for hazards outside the useful
    /// visual field. Non-emergency warnings always require measured approach
    /// toward the hazard, which avoids vibrating while resting beside a wall.
    /// </summary>
    public sealed class RearHazardWarningPolicy
    {
        private readonly struct Candidate
        {
            public readonly StaticHazardKey Key;
            public readonly bool Emergency;
            public readonly SafetyHapticTarget HapticTarget;
            public readonly float Intensity;
            public readonly float AngleDegrees;
            public readonly float DistanceMeters;
            public readonly float ClosingSpeedMetersPerSecond;
            public readonly float TtcSeconds;

            public Candidate(
                StaticHazardKey key,
                bool emergency,
                SafetyHapticTarget hapticTarget,
                float intensity,
                float angleDegrees,
                float distanceMeters,
                float closingSpeedMetersPerSecond,
                float ttcSeconds)
            {
                Key = key;
                Emergency = emergency;
                HapticTarget = hapticTarget;
                Intensity = Mathf.Clamp01(intensity);
                AngleDegrees = angleDegrees;
                DistanceMeters = distanceMeters;
                ClosingSpeedMetersPerSecond = closingSpeedMetersPerSecond;
                TtcSeconds = ttcSeconds;
            }
        }

        private readonly RearHazardWarningSettings settings;
        private bool warningActive;
        private bool emergencyQualifiedLastEvaluation;
        private double warningStartedAt;
        private double warningHeldUntil;
        private double nextWarningAllowedAt;
        private double lastTimestamp;
        private Candidate latchedCandidate;

        public RearHazardWarningPolicy(
            RearHazardWarningSettings settings = null)
        {
            this.settings = settings ?? new RearHazardWarningSettings();
        }

        public void Reset()
        {
            warningActive = false;
            emergencyQualifiedLastEvaluation = false;
            warningStartedAt = 0.0;
            warningHeldUntil = 0.0;
            nextWarningAllowedAt = 0.0;
            lastTimestamp = 0.0;
            latchedCandidate = default;
        }

        public RearHazardWarningSnapshot Evaluate(
            double timestampSeconds,
            Vector3 viewForwardWorld,
            Vector3 viewRightWorld,
            StaticHazardDecision[] hazards,
            bool outputEnabled,
            StaticRiskChannelMask enabledChannels)
        {
            double now = Math.Max(0.0, Sanitize(timestampSeconds));
            if (lastTimestamp > 0.0 && now + 0.001 < lastTimestamp)
            {
                Reset();
            }
            lastTimestamp = now;

            if (!outputEnabled)
            {
                Reset();
                return RearHazardWarningSnapshot.Inactive;
            }

            bool hasCandidate = TrySelectCandidate(
                viewForwardWorld,
                viewRightWorld,
                hazards,
                enabledChannels,
                out Candidate candidate);
            bool emergencyRisingEdge = hasCandidate
                && candidate.Emergency
                && !emergencyQualifiedLastEvaluation;
            emergencyQualifiedLastEvaluation = hasCandidate
                && candidate.Emergency;

            if (warningActive && now >= warningHeldUntil)
            {
                warningActive = false;
            }

            if (!warningActive
                && hasCandidate
                && (now >= nextWarningAllowedAt || emergencyRisingEdge))
            {
                StartWarning(candidate, now);
            }
            else if (warningActive && hasCandidate)
            {
                // Keep the direction stable for the burst, but never discard
                // a stronger reading while the warning is held.
                if (candidate.Intensity > latchedCandidate.Intensity)
                {
                    latchedCandidate = new Candidate(
                        latchedCandidate.Key,
                        latchedCandidate.Emergency || candidate.Emergency,
                        latchedCandidate.HapticTarget,
                        candidate.Intensity,
                        latchedCandidate.AngleDegrees,
                        Mathf.Min(
                            latchedCandidate.DistanceMeters,
                            candidate.DistanceMeters),
                        Mathf.Max(
                            latchedCandidate.ClosingSpeedMetersPerSecond,
                            candidate.ClosingSpeedMetersPerSecond),
                        Mathf.Min(
                            latchedCandidate.TtcSeconds,
                            candidate.TtcSeconds));
                }
            }

            if (!warningActive)
            {
                return new RearHazardWarningSnapshot(
                    hasCandidate,
                    false,
                    false,
                    false,
                    hasCandidate && candidate.Emergency,
                    SafetyHapticTarget.None,
                    StaticHazardKey.None,
                    hasCandidate ? candidate.Intensity : 0f,
                    hasCandidate ? candidate.AngleDegrees : 0f,
                    hasCandidate ? candidate.DistanceMeters : 0f,
                    hasCandidate ? candidate.ClosingSpeedMetersPerSecond : 0f,
                    hasCandidate ? candidate.TtcSeconds : 0f,
                    0f,
                    (float)Math.Max(0.0, nextWarningAllowedAt - now));
            }

            float elapsed = (float)Math.Max(0.0, now - warningStartedAt);
            return new RearHazardWarningSnapshot(
                hasCandidate,
                true,
                elapsed < Positive(settings.borderPulseSeconds),
                elapsed < Positive(settings.hapticBurstSeconds),
                latchedCandidate.Emergency,
                latchedCandidate.HapticTarget,
                latchedCandidate.Key,
                latchedCandidate.Intensity,
                latchedCandidate.AngleDegrees,
                latchedCandidate.DistanceMeters,
                latchedCandidate.ClosingSpeedMetersPerSecond,
                latchedCandidate.TtcSeconds,
                (float)Math.Max(0.0, warningHeldUntil - now),
                (float)Math.Max(0.0, nextWarningAllowedAt - now));
        }

        private void StartWarning(Candidate candidate, double now)
        {
            warningActive = true;
            latchedCandidate = candidate;
            warningStartedAt = now;
            float outputDuration = Mathf.Max(
                Positive(settings.borderPulseSeconds),
                Positive(settings.hapticBurstSeconds));
            float hold = Mathf.Max(
                Mathf.Max(0f, settings.minimumHoldSeconds),
                outputDuration);
            warningHeldUntil = now + hold;
            nextWarningAllowedAt = warningHeldUntil
                + Mathf.Max(0f, settings.rewarningIntervalSeconds);
        }

        private bool TrySelectCandidate(
            Vector3 viewForwardWorld,
            Vector3 viewRightWorld,
            StaticHazardDecision[] hazards,
            StaticRiskChannelMask enabledChannels,
            out Candidate selected)
        {
            selected = default;
            Vector3 forward = HorizontalDirection(viewForwardWorld);
            Vector3 right = HorizontalDirection(viewRightWorld);
            if (forward == Vector3.zero || right == Vector3.zero
                || hazards == null)
            {
                return false;
            }

            bool found = false;
            enabledChannels &= StaticRiskChannelMask.All;
            for (int i = 0; i < hazards.Length; i++)
            {
                StaticHazardDecision hazard = hazards[i];
                if (hazard == null
                    || !hazard.Available
                    || !hazard.HazardDirectionAvailable
                    || (enabledChannels & hazard.Channel)
                        == StaticRiskChannelMask.None)
                {
                    continue;
                }

                Vector3 direction = HorizontalDirection(
                    hazard.HazardDirectionWorld);
                if (direction == Vector3.zero)
                {
                    continue;
                }

                float angle = Vector3.Angle(forward, direction);
                if (angle + 0.001f
                    < Mathf.Clamp(settings.rearAngleDegrees, 90f, 180f))
                {
                    continue;
                }

                bool emergency = hazard.DistanceMeters
                    <= Mathf.Max(0f, settings.emergencyDistanceMeters);
                if (!emergency
                    && hazard.ClosingSpeedMetersPerSecond + 0.0001f
                        < Mathf.Max(
                            0f,
                            settings.minimumClosingSpeedMetersPerSecond))
                {
                    continue;
                }

                float ttc = hazard.ClosingSpeedMetersPerSecond > 0.001f
                    ? hazard.DistanceMeters
                        / hazard.ClosingSpeedMetersPerSecond
                    : float.MaxValue;
                float intensity = emergency
                    ? 1f
                    : WarningIntensity(hazard.DistanceMeters, ttc, settings);
                SafetyHapticTarget target = ResolveHapticTarget(
                    direction,
                    right,
                    settings.bothControllersRearConeDegrees);
                var candidate = new Candidate(
                    hazard.Key,
                    emergency,
                    target,
                    intensity,
                    angle,
                    hazard.DistanceMeters,
                    hazard.ClosingSpeedMetersPerSecond,
                    ttc);
                if (!found || IsStronger(candidate, selected))
                {
                    selected = candidate;
                    found = true;
                }
            }

            return found;
        }

        public static SafetyHapticTarget ResolveHapticTarget(
            Vector3 hazardDirectionWorld,
            Vector3 viewRightWorld,
            float bothControllersRearConeDegrees = 15f)
        {
            Vector3 direction = HorizontalDirection(hazardDirectionWorld);
            Vector3 right = HorizontalDirection(viewRightWorld);
            if (direction == Vector3.zero || right == Vector3.zero)
            {
                return SafetyHapticTarget.None;
            }

            float bothThreshold = Mathf.Sin(
                Mathf.Clamp(bothControllersRearConeDegrees, 0f, 45f)
                * Mathf.Deg2Rad);
            float side = Vector3.Dot(direction, right);
            if (Mathf.Abs(side) <= bothThreshold)
            {
                return SafetyHapticTarget.Both;
            }

            return side < 0f
                ? SafetyHapticTarget.Left
                : SafetyHapticTarget.Right;
        }

        public static float WarningIntensity(
            float distanceMeters,
            float ttcSeconds,
            RearHazardWarningSettings settings = null)
        {
            RearHazardWarningSettings value = settings
                ?? new RearHazardWarningSettings();
            float distanceRisk = 1f - Mathf.InverseLerp(
                Mathf.Max(0f, value.emergencyDistanceMeters),
                Mathf.Max(
                    value.emergencyDistanceMeters + 0.01f,
                    value.distanceRiskFarMeters),
                Mathf.Max(0f, distanceMeters));
            float safeTtc = Mathf.Max(
                value.ttcRiskFullSeconds + 0.01f,
                value.ttcRiskSafeSeconds);
            float ttcRisk = 1f - Mathf.InverseLerp(
                Mathf.Max(0f, value.ttcRiskFullSeconds),
                safeTtc,
                Mathf.Max(0f, ttcSeconds));
            return Mathf.Clamp01(
                0.25f + 0.40f * distanceRisk + 0.35f * ttcRisk);
        }

        private static bool IsStronger(Candidate left, Candidate right)
        {
            if (left.Emergency != right.Emergency)
            {
                return left.Emergency;
            }
            if (!Mathf.Approximately(left.Intensity, right.Intensity))
            {
                return left.Intensity > right.Intensity;
            }
            return left.TtcSeconds < right.TtcSeconds;
        }

        private static Vector3 HorizontalDirection(Vector3 value)
        {
            if (!IsFinite(value.x)
                || !IsFinite(value.y)
                || !IsFinite(value.z))
            {
                return Vector3.zero;
            }

            value = Vector3.ProjectOnPlane(value, Vector3.up);
            return value.sqrMagnitude > 0.0001f
                ? value.normalized
                : Vector3.zero;
        }

        private static double Sanitize(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? 0.0
                : value;
        }

        private static float Positive(float value)
        {
            return Mathf.Max(0.01f, value);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
