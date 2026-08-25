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
        public float closingSpeedAtMaximumRisk = 1.20f;
        public float maximumRiskDistanceMeters = 0.60f;
        public float middleRiskDistanceMeters = 1.50f;
        public float lowRiskDistanceMeters = 3.00f;
        public float criticalTtcSeconds = 2f;
        public float zeroRiskTtcSeconds = 15f;
        public float centerApproachRateAtMaximumRisk = 0.30f;
    }

    public sealed class DynamicRiskEstimator
    {
        private sealed class CloseState
        {
            public bool Active;
            public int WeakConfirmations;
            public int ReleaseConfirmations;
        }

        private readonly DynamicRiskSettings settings;
        private readonly Dictionary<int, CloseState> closeStates =
            new Dictionary<int, CloseState>();

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

            float proximityFactor = location.HasMetricDistance
                ? MetricProximityFactor(
                    location.FilteredDistanceMeters)
                : LegacyProximityFactor(location.DistanceBand);

            float approachFactor = 0f;
            if (motion.State == DynamicMotionState.Approaching)
            {
                approachFactor = motion.HasMetricMotion
                    ? Clamp01(
                        motion.ClosingSpeedMetersPerSecond
                        / Math.Max(
                            0.01f,
                            settings.closingSpeedAtMaximumRisk))
                        * motion.Reliability
                    : Clamp01(
                        motion.ScaleRatePerSecond
                        / settings.approachRateAtMaximumRisk)
                        * motion.Reliability;
            }
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
                bool veryClose =
                    location.HasMetricDistance
                    && location.FilteredDistanceMeters
                        <= settings.maximumRiskDistanceMeters;
                score *= veryClose
                    ? Math.Max(0.85f, settings.recedingMultiplier)
                    : settings.recedingMultiplier;
            }

            bool person = string.Equals(
                detection.label,
                "person",
                StringComparison.OrdinalIgnoreCase);
            bool metricClose = person
                && location.HasMetricDistance
                && location.DistanceConfidence >= 0.45f
                && location.FilteredDistanceMeters <= 0.60f;
            bool strongBboxClose = person
                && detection.confidence >= 0.75f
                && box.height >= 0.95f
                && box.Area >= 0.65f;
            bool weakBboxClose = person
                && detection.confidence >= 0.60f
                && box.height >= 0.92f
                && box.Area >= 0.55f;
            CloseState closeState;
            if (!closeStates.TryGetValue(tracked.TrackId, out closeState))
            {
                closeState = new CloseState();
                closeStates.Add(tracked.TrackId, closeState);
            }

            if (metricClose || strongBboxClose)
            {
                closeState.Active = true;
                closeState.WeakConfirmations = 0;
                closeState.ReleaseConfirmations = 0;
            }
            else if (weakBboxClose)
            {
                closeState.WeakConfirmations++;
                if (closeState.WeakConfirmations >= 2)
                {
                    closeState.Active = true;
                    closeState.ReleaseConfirmations = 0;
                }
            }
            else
            {
                closeState.WeakConfirmations = 0;
                if (closeState.Active)
                {
                    bool releaseConfirmed = location.HasMetricDistance
                        ? location.FilteredDistanceMeters > 0.80f
                            && box.height < 0.65f
                        : box.height < 0.65f && box.Area < 0.25f;
                    closeState.ReleaseConfirmations = releaseConfirmed
                        ? closeState.ReleaseConfirmations + 1
                        : 0;
                    if (closeState.ReleaseConfirmations >= 3)
                    {
                        closeState.Active = false;
                        closeState.ReleaseConfirmations = 0;
                    }
                }
            }

            if (closeState.Active)
            {
                score = Math.Max(score, 0.90f);
            }

            score = Clamp01(score);
            var reasons = new List<string>();
            if (location.DistanceBand == DistanceBand.Near)
            {
                reasons.Add("near_object");
            }

            if (location.HasMetricDistance)
            {
                reasons.Add("metric_depth");
                if (location.FilteredDistanceMeters
                    <= settings.maximumRiskDistanceMeters)
                {
                    reasons.Add("very_close");
                }
            }
            else
            {
                reasons.Add("bbox_distance_fallback");
            }

            if (motion.MetricConflict)
            {
                reasons.Add("bbox_depth_conflict");
            }

            if (closeState.Active)
            {
                reasons.Add("ultra_close_force");
                if (strongBboxClose)
                {
                    reasons.Add("strong_bbox_close");
                }
                else if (weakBboxClose)
                {
                    reasons.Add("confirmed_bbox_close");
                }
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

            float? effectiveTtc = motion.HasMetricMotion
                ? motion.MetricTtcSeconds
                : motion.TtcSecondsApprox;
            if (effectiveTtc.HasValue)
            {
                if (effectiveTtc.Value <= 2f)
                {
                    reasons.Add("ttc_under_2s");
                }
                else if (effectiveTtc.Value <= 4f)
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
                breakdown,
                tracked.Lifecycle,
                tracked.ObservedThisFrame,
                0f,
                tracked.ReidentifiedThisFrame,
                closeState.Active);
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
            float? selectedTtc = motion.HasMetricMotion
                ? motion.MetricTtcSeconds
                : motion.TtcSecondsApprox;
            if (motion.State != DynamicMotionState.Approaching
                || !selectedTtc.HasValue)
            {
                return 0f;
            }

            float ttc = selectedTtc.Value;
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

        private float MetricProximityFactor(float distanceMeters)
        {
            float maximumDistance = Math.Max(
                0.20f,
                settings.maximumRiskDistanceMeters);
            float middleDistance = Math.Max(
                maximumDistance + 0.01f,
                settings.middleRiskDistanceMeters);
            float lowDistance = Math.Max(
                middleDistance + 0.01f,
                settings.lowRiskDistanceMeters);
            if (distanceMeters <= maximumDistance)
            {
                return 1f;
            }

            if (distanceMeters <= middleDistance)
            {
                return Lerp(
                    1f,
                    0.55f,
                    InverseLerp(
                        maximumDistance,
                        middleDistance,
                        distanceMeters));
            }

            if (distanceMeters <= lowDistance)
            {
                return Lerp(
                    0.55f,
                    0.15f,
                    InverseLerp(
                        middleDistance,
                        lowDistance,
                        distanceMeters));
            }

            return 0.15f;
        }

        private static float LegacyProximityFactor(DistanceBand distanceBand)
        {
            switch (distanceBand)
            {
                case DistanceBand.Near:
                    return 1f;
                case DistanceBand.Mid:
                    return 0.55f;
                default:
                    return 0.15f;
            }
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

        private static float InverseLerp(
            float minimum,
            float maximum,
            float value)
        {
            float range = maximum - minimum;
            return range <= 0f
                ? 0f
                : Clamp01((value - minimum) / range);
        }

        private static float Lerp(float from, float to, float amount)
        {
            return from + (to - from) * Clamp01(amount);
        }
    }
}
