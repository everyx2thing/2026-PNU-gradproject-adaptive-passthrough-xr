using System;
using System.Collections.Generic;

namespace TeamVR.AdaptivePassthrough
{
    [Serializable]
    public sealed class DynamicRiskSettings
    {
        public float proximityWeight = 0.55f;
        public float approachWeight = 0.30f;
        public float ttcWeight = 0.10f;
        public float collisionPathWeight = 0.05f;
        public float objectTypeWeight = 0f;
        public float proximityApproachWeight = 0f;
        public float recedingMultiplier = 0.40f;
        public float approachRateAtMaximumRisk = 0.20f;
        public float closingSpeedAtMaximumRisk = 0.80f;
        public float maximumRiskDistanceMeters = 1.00f;
        public float middleRiskDistanceMeters = 1.50f;
        public float lowRiskDistanceMeters = 3.50f;
        public float criticalTtcSeconds = 2f;
        public float zeroRiskTtcSeconds = 15f;
        public float centerApproachRateAtMaximumRisk = 0.30f;
    }

    public sealed class DynamicRiskEstimator
    {
        private const float SafetyForceDistanceMeters = 1.00f;
        private const float SafetyReleaseDistanceMeters = 2.00f;
        private const float SafetyMinimumConfidence = 0.55f;
        private const float NoDepthReleaseMaximumHeight = 0.85f;
        private const float NoDepthReleaseMaximumArea = 0.45f;
        private const float ReleaseEdgeMargin = 0.02f;

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
            bool hasReliableSafetyDistance =
                location.HasReliableSafetyDistance;
            bool hasReliableTrackingDistance = location.HasMetricDistance
                && location.IsMetricReliable;
            DynamicRiskDistanceSource riskDistanceSource =
                hasReliableSafetyDistance
                    ? DynamicRiskDistanceSource.SafetyDistance
                    : hasReliableTrackingDistance
                        ? DynamicRiskDistanceSource.TrackingDistance
                        : DynamicRiskDistanceSource.BoundingBoxProxy;
            bool hasMetricRiskDistance = riskDistanceSource
                != DynamicRiskDistanceSource.BoundingBoxProxy;
            float riskDistanceMeters = hasReliableSafetyDistance
                ? location.SafetyDistanceMeters
                : hasReliableTrackingDistance
                    ? location.FilteredDistanceMeters
                    : 0f;
            float clusterConfidence = hasReliableSafetyDistance
                ? location.SafetyDistanceConfidence
                : hasReliableTrackingDistance
                    ? location.DistanceConfidence
                    : 0f;

            float confidenceFactor = 1f;
            float proximityFactor;
            if (hasMetricRiskDistance)
            {
                proximityFactor = MetricProximityFactor(riskDistanceMeters);
            }
            else
            {
                confidenceFactor = 0.5f
                    + 0.5f * Clamp01(detection.confidence);
                proximityFactor = LegacyProximityFactor(
                    location.DistanceBand) * confidenceFactor;
            }

            float approachFactor = 0f;
            if (motion.State == DynamicMotionState.Approaching)
            {
                approachFactor = motion.HasMetricMotion
                    ? SmoothStep(
                        0.05f,
                        Math.Max(0.051f,
                            settings.closingSpeedAtMaximumRisk),
                        motion.ClosingSpeedMetersPerSecond)
                        * motion.Reliability
                    : SmoothStep(
                        0.04f,
                        Math.Max(0.041f,
                            settings.approachRateAtMaximumRisk),
                        motion.ScaleRatePerSecond)
                        * motion.Reliability;
            }

            float ttcFactor = TtcFactor(
                motion,
                riskDistanceMeters,
                hasReliableSafetyDistance);
            float centerX = box.centerX - 0.5f;
            float centerY = box.centerY - 0.5f;
            float centerDistance = (float)Math.Sqrt(
                centerX * centerX + centerY * centerY);
            float centralityFactor = Math.Max(
                0f,
                1f - centerDistance / 0.5f);
            float centerMotionBoost = Clamp01(
                motion.CenterApproachRatePerSecond
                    / Math.Max(
                        0.01f,
                        settings.centerApproachRateAtMaximumRisk));
            float collisionPathFactor = Clamp01(
                centralityFactor + 0.25f * centerMotionBoost);

            float score = 0.55f * proximityFactor
                + 0.30f * approachFactor
                + 0.10f * ttcFactor
                + 0.05f * collisionPathFactor;
            float closeRiskFloor = 0f;
            if (hasMetricRiskDistance && riskDistanceMeters <= 1.50f)
            {
                closeRiskFloor = 0.65f
                    + 0.25f * Clamp01(
                        (1.50f - riskDistanceMeters) / 0.50f);
                score = Math.Max(score, closeRiskFloor);
            }

            float motionAttenuation = 1f;
            if (hasMetricRiskDistance
                && riskDistanceMeters > 1.50f
                && motion.State == DynamicMotionState.Receding)
            {
                motionAttenuation = Lerp(
                    0.75f,
                    0.40f,
                    InverseLerp(1.50f, 3.50f, riskDistanceMeters));
            }
            else if (hasMetricRiskDistance
                && riskDistanceMeters > 2.00f
                && (motion.State == DynamicMotionState.Steady
                    || motion.State == DynamicMotionState.Unknown))
            {
                motionAttenuation = Lerp(
                    1f,
                    0.70f,
                    InverseLerp(2.00f, 3.50f, riskDistanceMeters));
            }
            score *= motionAttenuation;

            if (!hasMetricRiskDistance)
            {
                if (motion.State == DynamicMotionState.Receding)
                {
                    score = Math.Min(score, 0.40f);
                }
                else if (motion.State == DynamicMotionState.Steady
                    || motion.State == DynamicMotionState.Unknown)
                {
                    score = Math.Min(score, 0.55f);
                }
            }

            bool person = string.Equals(
                detection.label,
                "person",
                StringComparison.OrdinalIgnoreCase);
            bool metricClose = person
                && hasReliableSafetyDistance
                && location.SafetySupportCount >= 2
                && location.SafetyDistanceConfidence
                    >= SafetyMinimumConfidence
                && location.SafetyDistanceMeters
                    <= SafetyForceDistanceMeters;
            bool strongBboxClose = person
                && detection.confidence >= 0.75f
                && box.height >= 0.95f
                && box.Area >= 0.65f;
            bool weakBboxClose = person
                && detection.confidence >= 0.60f
                && box.height >= 0.92f
                && box.Area >= 0.55f;
            if (!closeStates.TryGetValue(
                    tracked.TrackId,
                    out CloseState closeState))
            {
                closeState = new CloseState();
                closeStates.Add(tracked.TrackId, closeState);
            }
            bool wasCloseActive = closeState.Active;
            string closeTransitionReason = closeState.Active
                ? "latched"
                : "inactive";

            if (metricClose || strongBboxClose)
            {
                closeState.Active = true;
                closeState.WeakConfirmations = 0;
                closeState.ReleaseConfirmations = 0;
                closeTransitionReason = wasCloseActive
                    ? "latched"
                    : metricClose
                        ? "safety_1m_enter"
                        : "strong_bbox_close_enter";
            }
            else if (weakBboxClose)
            {
                closeState.WeakConfirmations++;
                if (closeState.WeakConfirmations >= 2)
                {
                    closeState.Active = true;
                    closeState.ReleaseConfirmations = 0;
                    closeTransitionReason = wasCloseActive
                        ? "latched"
                        : "weak_bbox_close_enter_2x";
                }
            }
            else
            {
                closeState.WeakConfirmations = 0;
                if (closeState.Active)
                {
                    bool metricRelease = hasReliableSafetyDistance
                        && location.SafetyDistanceMeters
                            > SafetyReleaseDistanceMeters;
                    bool bboxRelease = !hasReliableSafetyDistance
                        && box.height < NoDepthReleaseMaximumHeight
                        && box.Area < NoDepthReleaseMaximumArea
                        && detection.confidence >= 0.40f
                        && IsUnclipped(box, ReleaseEdgeMargin);
                    bool releaseConfirmed = metricRelease || bboxRelease;
                    closeState.ReleaseConfirmations = releaseConfirmed
                        ? closeState.ReleaseConfirmations + 1
                        : 0;
                    closeTransitionReason = releaseConfirmed
                        ? metricRelease
                            ? "safety_2m_release_pending"
                            : "bbox_release_pending"
                        : "release_blocked";
                    if (closeState.ReleaseConfirmations >= 3)
                    {
                        closeState.Active = false;
                        closeState.ReleaseConfirmations = 0;
                        closeTransitionReason = metricRelease
                            ? "safety_2m_release_3x"
                            : "bbox_release_3x";
                    }
                }
            }

            score = Clamp01(score);
            var reasons = new List<string>();
            if (hasReliableSafetyDistance)
            {
                reasons.Add("safety_depth");
            }
            else if (hasReliableTrackingDistance)
            {
                reasons.Add("tracking_depth");
            }
            else
            {
                reasons.Add("bbox_distance_fallback");
            }

            if (hasMetricRiskDistance && riskDistanceMeters <= 1.50f)
            {
                reasons.Add("close_metric_floor");
            }
            if (motion.MetricConflict)
            {
                reasons.Add("bbox_depth_conflict");
            }
            if (motionAttenuation < 0.999f)
            {
                reasons.Add("motion_attenuated");
            }
            if (closeState.Active)
            {
                reasons.Add("ultra_close_force");
                if (metricClose)
                {
                    reasons.Add("safety_1m_force");
                }
                else if (strongBboxClose)
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
            if (collisionPathFactor >= 0.7f)
            {
                reasons.Add("collision_corridor");
            }

            var breakdown = new DynamicRiskBreakdown(
                proximityFactor,
                approachFactor,
                ttcFactor,
                collisionPathFactor,
                0f,
                0f,
                confidenceFactor,
                riskDistanceSource,
                riskDistanceMeters,
                clusterConfidence,
                closeRiskFloor,
                motionAttenuation);

            return new DynamicRiskAssessment(
                tracked.TrackId,
                detection,
                location,
                motion,
                score,
                closeState.Active
                    ? DynamicRiskLevel.Danger
                    : LevelForScore(score),
                reasons.ToArray(),
                breakdown,
                tracked.Lifecycle,
                tracked.ObservedThisFrame,
                0f,
                tracked.ReidentifiedThisFrame,
                closeState.Active,
                closeState.Active,
                closeTransitionReason,
                closeState.ReleaseConfirmations);
        }

        public void MarkUnobserved(int trackId)
        {
            if (closeStates.TryGetValue(trackId, out CloseState state))
            {
                state.WeakConfirmations = 0;
            }
        }

        public static bool IsReleaseSafeClipping(
            NormalizedBoundingBox box,
            float edgeMargin)
        {
            float margin = Math.Max(0f, Math.Min(0.49f, edgeMargin));
            return box.Left >= margin
                && box.Top >= margin
                && box.Right <= 1f - margin;
        }

        public static bool IsUnclipped(
            NormalizedBoundingBox box,
            float edgeMargin)
        {
            float margin = Math.Max(0f, Math.Min(0.49f, edgeMargin));
            return box.Left >= margin
                && box.Top >= margin
                && box.Right <= 1f - margin
                && box.Bottom <= 1f - margin;
        }

        public void PruneExcept(IEnumerable<int> liveTrackIds)
        {
            var live = liveTrackIds == null
                ? new HashSet<int>()
                : new HashSet<int>(liveTrackIds);
            var expired = new List<int>();
            foreach (int trackId in closeStates.Keys)
            {
                if (!live.Contains(trackId))
                {
                    expired.Add(trackId);
                }
            }

            for (int i = 0; i < expired.Count; i++)
            {
                closeStates.Remove(expired[i]);
            }
        }

        public void Reset()
        {
            closeStates.Clear();
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

        private float TtcFactor(
            MotionEstimate motion,
            float riskDistanceMeters,
            bool useSafetyDistance)
        {
            float? selectedTtc;
            if (motion.HasMetricMotion
                && useSafetyDistance
                && motion.ClosingSpeedMetersPerSecond > 0.01f)
            {
                selectedTtc = riskDistanceMeters
                    / motion.ClosingSpeedMetersPerSecond;
            }
            else
            {
                selectedTtc = motion.HasMetricMotion
                    ? motion.MetricTtcSeconds
                    : motion.TtcSecondsApprox;
            }
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
            float range = settings.zeroRiskTtcSeconds
                - settings.criticalTtcSeconds;
            return range <= 0f
                ? 0f
                : (settings.zeroRiskTtcSeconds - ttc) / range;
        }

        private static float MetricProximityFactor(float distanceMeters)
        {
            if (distanceMeters <= 1.00f)
            {
                return 1f;
            }
            if (distanceMeters <= 1.50f)
            {
                return Lerp(
                    1f,
                    0.80f,
                    InverseLerp(1.00f, 1.50f, distanceMeters));
            }
            if (distanceMeters <= 2.50f)
            {
                return Lerp(
                    0.80f,
                    0.25f,
                    InverseLerp(1.50f, 2.50f, distanceMeters));
            }
            if (distanceMeters <= 3.50f)
            {
                return Lerp(
                    0.25f,
                    0f,
                    InverseLerp(2.50f, 3.50f, distanceMeters));
            }
            return 0f;
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

        private static float SmoothStep(
            float minimum,
            float maximum,
            float value)
        {
            float t = InverseLerp(minimum, maximum, value);
            return t * t * (3f - 2f * t);
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
