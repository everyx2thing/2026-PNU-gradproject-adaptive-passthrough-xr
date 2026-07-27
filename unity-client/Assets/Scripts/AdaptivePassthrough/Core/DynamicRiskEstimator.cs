using System;
using System.Collections.Generic;

namespace TeamVR.AdaptivePassthrough
{
    [Serializable]
    public sealed class DynamicRiskSettings
    {
        public float proximityWeight = 0.30f;
        public float approachWeight = 0.30f;
        public float ttcWeight = 0.15f;
        public float collisionPathWeight = 0.10f;
        public float objectTypeWeight = 0.05f;
        public float proximityApproachWeight = 0.10f;
        public float recedingMultiplier = 0.55f;
        public float approachRateAtMaximumRisk = 0.15f;
        public float criticalTtcSeconds = 2f;
        public float zeroRiskTtcSeconds = 15f;
        public float centerApproachRateAtMaximumRisk = 0.30f;
    }

    public sealed class DynamicRiskEstimator
    {
        private readonly DynamicRiskSettings settings;

        public DynamicRiskEstimator(DynamicRiskSettings settings = null)
        {
            this.settings = settings ?? new DynamicRiskSettings();
        }

        public DynamicRiskAssessment Estimate(
            TrackedDynamicObject tracked,
            RelativeLocationEstimate location,
            MotionEstimate motion)
        {
            DynamicObjectDetection detection = tracked.Detection;
            NormalizedBoundingBox box = detection.boundingBox;

            float proximityFactor;
            switch (location.DistanceBand)
            {
                case DistanceBand.Near:
                    proximityFactor = 1f;
                    break;
                case DistanceBand.Mid:
                    proximityFactor = 0.55f;
                    break;
                default:
                    proximityFactor = 0.15f;
                    break;
            }

            float approachFactor = motion.State == DynamicMotionState.Approaching
                ? Clamp01(motion.ScaleRatePerSecond / settings.approachRateAtMaximumRisk)
                    * motion.Reliability
                : 0f;
            float ttcFactor = TtcFactor(motion);
            float typeFactor = ObjectTypeFactor(detection.label);

            float centerX = box.centerX - 0.5f;
            float centerY = box.centerY - 0.5f;
            float centerDistance = (float)Math.Sqrt(centerX * centerX + centerY * centerY);
            float centralityFactor = Math.Max(0f, 1f - centerDistance / 0.5f);
            float centerMotionBoost = Clamp01(
                motion.CenterApproachRatePerSecond
                / settings.centerApproachRateAtMaximumRisk);
            float collisionPathFactor = Clamp01(
                centralityFactor + 0.25f * centerMotionBoost);

            float interactionFactor = proximityFactor * approachFactor;
            float rawScore =
                settings.proximityWeight * proximityFactor
                + settings.approachWeight * approachFactor
                + settings.ttcWeight * ttcFactor
                + settings.collisionPathWeight * collisionPathFactor
                + settings.objectTypeWeight * typeFactor
                + settings.proximityApproachWeight * interactionFactor;

            // The detector confidence correction used by the prototype keeps a
            // minimum 0.5 multiplier so one low-confidence frame cannot hide a
            // persistently tracked approach.
            float confidenceFactor = 0.5f + 0.5f * Clamp01(detection.confidence);
            float score = rawScore * confidenceFactor;
            if (motion.State == DynamicMotionState.Receding)
            {
                score *= settings.recedingMultiplier;
            }

            score = Clamp01(score);
            var reasons = new List<string>();
            if (location.DistanceBand == DistanceBand.Near)
            {
                reasons.Add("near_object");
            }

            switch (motion.State)
            {
                case DynamicMotionState.Approaching:
                    reasons.Add("approach_confirmed");
                    break;
                case DynamicMotionState.Receding:
                    reasons.Add("receding");
                    break;
                case DynamicMotionState.Unknown:
                    reasons.Add("insufficient_motion_history");
                    break;
            }

            if (motion.TtcSecondsApprox.HasValue)
            {
                if (motion.TtcSecondsApprox.Value <= 2f)
                {
                    reasons.Add("ttc_under_2s");
                }
                else if (motion.TtcSecondsApprox.Value <= 4f)
                {
                    reasons.Add("ttc_under_4s");
                }
            }

            if (typeFactor >= 0.6f)
            {
                reasons.Add(detection.label.ToLowerInvariant());
            }

            if (collisionPathFactor >= 0.7f)
            {
                reasons.Add("collision_corridor");
            }

            if (reasons.Count == 0)
            {
                reasons.Add("low_risk");
            }

            var breakdown = new DynamicRiskBreakdown(
                proximityFactor,
                approachFactor,
                ttcFactor,
                collisionPathFactor,
                typeFactor,
                interactionFactor,
                confidenceFactor);

            return new DynamicRiskAssessment(
                tracked.TrackId,
                detection,
                location,
                motion,
                score,
                LevelForScore(score),
                reasons.ToArray(),
                breakdown);
        }

        public DynamicRiskLevel LevelForScore(float score)
        {
            if (score < 0.25f)
            {
                return DynamicRiskLevel.Safe;
            }

            if (score < 0.50f)
            {
                return DynamicRiskLevel.Caution;
            }

            if (score < 0.75f)
            {
                return DynamicRiskLevel.Warning;
            }

            return DynamicRiskLevel.Danger;
        }

        private float TtcFactor(MotionEstimate motion)
        {
            if (motion.State != DynamicMotionState.Approaching
                || !motion.TtcSecondsApprox.HasValue)
            {
                return 0f;
            }

            float ttc = motion.TtcSecondsApprox.Value;
            if (ttc <= settings.criticalTtcSeconds)
            {
                return 1f;
            }

            if (ttc >= settings.zeroRiskTtcSeconds)
            {
                return 0f;
            }

            float range = settings.zeroRiskTtcSeconds - settings.criticalTtcSeconds;
            return range <= 0f
                ? 0f
                : (settings.zeroRiskTtcSeconds - ttc) / range;
        }

        private static float ObjectTypeFactor(string label)
        {
            switch ((label ?? string.Empty).ToLowerInvariant())
            {
                case "car":
                case "truck":
                    return 1f;
                case "bus":
                    return 0.95f;
                case "motorcycle":
                    return 0.90f;
                case "bicycle":
                    return 0.75f;
                case "person":
                    return 0.65f;
                case "dog":
                    return 0.45f;
                default:
                    return 0.30f;
            }
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
