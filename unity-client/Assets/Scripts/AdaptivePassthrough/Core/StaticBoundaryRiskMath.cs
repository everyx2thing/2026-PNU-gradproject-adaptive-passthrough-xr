using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    public static class StaticBoundaryRiskMath
    {
        private const float MinimumPositiveValue = 0.01f;

        public static float ContinuousUserState(
            float netTranslationSpeed,
            float fullMotionSpeed)
        {
            return Mathf.Clamp01(
                NonNegative(netTranslationSpeed)
                / Positive(fullMotionSpeed));
        }

        public static float NetTranslationSpeed(
            Vector3 oldestPosition,
            Vector3 currentPosition,
            float elapsedSeconds)
        {
            return NonNegative(
                Vector3.Distance(oldestPosition, currentPosition)
                / Positive(elapsedSeconds));
        }

        public static float DistanceRisk(
            float distanceMeters,
            float safeDistanceMeters)
        {
            return Mathf.Clamp01(
                1f - NonNegative(distanceMeters)
                / Positive(safeDistanceMeters));
        }

        public static float TimeToCollisionRisk(
            float distanceMeters,
            float towardBoundarySpeed,
            float safeTimeSeconds,
            float minimumApproachSpeed = 0f)
        {
            float speed = NonNegative(towardBoundarySpeed);
            if (speed <= NonNegative(minimumApproachSpeed))
            {
                return 0f;
            }

            float ttc = NonNegative(distanceMeters) / Positive(speed);
            return Mathf.Clamp01(1f - ttc / Positive(safeTimeSeconds));
        }

        public static float SpeedRisk(
            float closingSpeedMetersPerSecond,
            float startSpeedMetersPerSecond,
            float fullSpeedMetersPerSecond)
        {
            float start = NonNegative(startSpeedMetersPerSecond);
            float full = Mathf.Max(
                start + MinimumPositiveValue,
                NonNegative(fullSpeedMetersPerSecond));
            float value = Mathf.Clamp01(
                (NonNegative(closingSpeedMetersPerSecond) - start)
                / (full - start));
            return value * value * (3f - 2f * value);
        }

        public static float AccelerationRisk(
            float towardBoundaryAcceleration,
            float maximumApproachAcceleration)
        {
            return Mathf.Clamp01(
                NonNegative(towardBoundaryAcceleration)
                / Positive(maximumApproachAcceleration));
        }

        public static float BlindSpotRisk(float angleToBoundaryDegrees)
        {
            float angle = Mathf.Clamp(NonNegative(angleToBoundaryDegrees), 0f, 180f);
            if (angle < 60f)
            {
                return 0.2f;
            }

            return angle < 120f ? 0.5f : 0.8f;
        }

        public static float WeightedHeadRisk(
            float distanceRisk,
            float ttcRisk,
            float accelerationRisk,
            float blindSpotRisk,
            float distanceWeight,
            float ttcWeight,
            float accelerationWeight,
            float blindSpotWeight)
        {
            float wd = NonNegative(distanceWeight);
            float wt = NonNegative(ttcWeight);
            float wa = NonNegative(accelerationWeight);
            float wb = NonNegative(blindSpotWeight);
            float sum = wd + wt + wa + wb;
            if (sum <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(
                (wd * ClampRisk(distanceRisk)
                + wt * ClampRisk(ttcRisk)
                + wa * ClampRisk(accelerationRisk)
                + wb * ClampRisk(blindSpotRisk)) / sum);
        }

        public static float WeightedHeadRiskWithSpeed(
            float distanceRisk,
            float speedRisk,
            float ttcRisk,
            float blindSpotRisk,
            float distanceWeight,
            float speedWeight,
            float ttcWeight,
            float blindSpotWeight)
        {
            float wd = NonNegative(distanceWeight);
            float ws = NonNegative(speedWeight);
            float wt = NonNegative(ttcWeight);
            float wb = NonNegative(blindSpotWeight);
            float sum = wd + ws + wt + wb;
            if (sum <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(
                (wd * ClampRisk(distanceRisk)
                + ws * ClampRisk(speedRisk)
                + wt * ClampRisk(ttcRisk)
                + wb * ClampRisk(blindSpotRisk)) / sum);
        }

        public static float ReachGate(
            float wallDistanceMeters,
            float handExtensionMeters,
            float armReachMeters,
            float transitionMarginMeters)
        {
            float remainingReach = Mathf.Max(
                0f,
                NonNegative(armReachMeters)
                - NonNegative(handExtensionMeters));
            float margin = Positive(transitionMarginMeters);
            return Mathf.Clamp01(
                (remainingReach
                - NonNegative(wallDistanceMeters)
                + margin) / margin);
        }

        public static float WeightedHandRisk(
            float reachGate,
            float distanceRisk,
            float ttcRisk,
            float distanceWeight,
            float ttcWeight)
        {
            float wd = NonNegative(distanceWeight);
            float wt = NonNegative(ttcWeight);
            float sum = wd + wt;
            if (sum <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(
                ClampRisk(reachGate)
                * (wd * ClampRisk(distanceRisk)
                + wt * ClampRisk(ttcRisk)) / sum);
        }

        public static float WeightedHandRiskWithSpeed(
            float reachGate,
            float distanceRisk,
            float speedRisk,
            float ttcRisk,
            float distanceWeight,
            float speedWeight,
            float ttcWeight)
        {
            float wd = NonNegative(distanceWeight);
            float ws = NonNegative(speedWeight);
            float wt = NonNegative(ttcWeight);
            float sum = wd + ws + wt;
            if (sum <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(
                ClampRisk(reachGate)
                * (wd * ClampRisk(distanceRisk)
                + ws * ClampRisk(speedRisk)
                + wt * ClampRisk(ttcRisk)) / sum);
        }

        public static float EffectiveOnThreshold(
            float stableOnThreshold,
            float rapidOnThreshold,
            float userState01)
        {
            float stable = ClampRisk(stableOnThreshold);
            float rapid = ClampRisk(rapidOnThreshold);
            return Mathf.Lerp(
                Mathf.Max(stable, rapid),
                Mathf.Min(stable, rapid),
                ClampRisk(userState01));
        }

        public static float EffectiveOffThreshold(
            float effectiveOnThreshold,
            float hysteresisWidth)
        {
            return Mathf.Clamp01(
                ClampRisk(effectiveOnThreshold)
                - NonNegative(hysteresisWidth));
        }

        public static float HandReleaseThreshold(
            float handFullThreshold,
            float hysteresisWidth)
        {
            return Mathf.Clamp01(
                ClampRisk(handFullThreshold)
                - NonNegative(hysteresisWidth));
        }

        public static float ClampRisk(float value)
        {
            return Mathf.Clamp01(Safe(value));
        }

        public static float NonNegative(float value)
        {
            return Mathf.Max(0f, Safe(value));
        }

        private static float Positive(float value)
        {
            return Mathf.Max(MinimumPositiveValue, NonNegative(value));
        }

        private static float Safe(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : value;
        }
    }

    internal sealed class LegacyStaticBoundaryPolicy
    {
        private readonly StaticBoundaryPolicySettings settings;

        private bool enabled;
        private bool emergencyActive;
        private StaticActivationCause activeCause = StaticActivationCause.None;
        private double enabledSince;
        private double releaseCandidateSince;
        private bool releasePending;
        private Vector3 lastHazardDirection;
        private bool lastHazardDirectionAvailable;
        private double lastTimestamp;

        public LegacyStaticBoundaryPolicy(
            StaticBoundaryPolicySettings settings = null)
        {
            this.settings = settings ?? new StaticBoundaryPolicySettings();
        }

        public void Reset()
        {
            enabled = false;
            emergencyActive = false;
            activeCause = StaticActivationCause.None;
            enabledSince = 0.0;
            releaseCandidateSince = 0.0;
            releasePending = false;
            lastHazardDirection = Vector3.zero;
            lastHazardDirectionAvailable = false;
            lastTimestamp = 0.0;
        }

        public StaticPassthroughDecision Evaluate(
            long sequence,
            double timestampSeconds,
            StaticBoundaryRiskFrame frame)
        {
            double now = SanitizeTimestamp(timestampSeconds);
            float userState = frame == null ? 0f : frame.UserState01;
            float onThreshold = StaticBoundaryRiskMath.EffectiveOnThreshold(
                settings.stableOnThreshold,
                settings.rapidOnThreshold,
                userState);
            float offThreshold = StaticBoundaryRiskMath.EffectiveOffThreshold(
                onThreshold,
                settings.hysteresisWidth);
            float handOn = StaticBoundaryRiskMath.ClampRisk(
                settings.handFullThreshold);
            float handOff = StaticBoundaryRiskMath.HandReleaseThreshold(
                handOn,
                settings.hysteresisWidth);
            bool available = frame != null && frame.Available;

            if (!available)
            {
                PassthroughDecisionReason unavailableReason =
                    PassthroughDecisionReason.NoRiskInputs;
                if (enabled)
                {
                    emergencyActive = false;
                    unavailableReason = HoldOrRelease(
                        now,
                        PassthroughDecisionReason.NoRiskInputs);
                    if (enabled)
                    {
                        return BuildDecision(
                            sequence,
                            now,
                            frame,
                            true,
                            unavailableReason,
                            StaticWarningLevel.Full,
                            activeCause,
                            onThreshold,
                            offThreshold,
                            handOff,
                            false,
                            false,
                            lastHazardDirection,
                            lastHazardDirectionAvailable);
                    }
                }

                return BuildDecision(
                    sequence,
                    now,
                    frame,
                    false,
                    unavailableReason,
                    StaticWarningLevel.None,
                    StaticActivationCause.None,
                    onThreshold,
                    offThreshold,
                    handOff,
                    false,
                    false,
                    Vector3.zero,
                    false);
            }

            float headRisk = frame.Head.Risk;
            float lowObstacleRisk = frame.LowObstacle.Risk;
            float primaryRisk = Mathf.Max(headRisk, lowObstacleRisk);
            float handRisk = frame.MaximumHandRisk;
            bool headApproachingForEmergency =
                frame.Head.TowardBoundarySpeed
                > StaticBoundaryRiskMath.NonNegative(
                    settings.emergencyApproachSpeed);
            float emergencyDistance = StaticBoundaryRiskMath.NonNegative(
                settings.emergencyDistance);
            float releaseMargin = StaticBoundaryRiskMath.NonNegative(
                settings.emergencyReleaseMargin);
            bool lowApproachingForEmergency =
                frame.LowObstacle.TowardBoundarySpeed
                > StaticBoundaryRiskMath.NonNegative(
                    settings.emergencyApproachSpeed);
            bool headEmergency = frame.Head.Available
                && frame.Head.ClosestDistanceMeters <= emergencyDistance;
            bool lowEmergency = frame.LowObstacle.Available
                && frame.LowObstacle.ClosestDistanceMeters <= emergencyDistance;
            bool emergencyTrigger = frame.HeadSafetyOverlapEmergency
                || headEmergency
                || lowEmergency;

            if (emergencyTrigger)
            {
                emergencyActive = true;
            }
            else if ((!frame.Head.Available
                    || !headApproachingForEmergency
                    || frame.Head.ClosestDistanceMeters
                        > emergencyDistance + releaseMargin)
                && (!frame.LowObstacle.Available
                    || !lowApproachingForEmergency
                    || frame.LowObstacle.ClosestDistanceMeters
                        > emergencyDistance + releaseMargin))
            {
                emergencyActive = false;
            }

            bool wasEnabled = enabled;
            PassthroughDecisionReason heldReason =
                PassthroughDecisionReason.HysteresisHeld;
            if (!enabled)
            {
                if (emergencyTrigger)
                {
                    Enable(now, StaticActivationCause.Emergency);
                }
                else if (primaryRisk >= onThreshold)
                {
                    Enable(
                        now,
                        lowObstacleRisk > headRisk
                            ? StaticActivationCause.LowObstacle
                            : StaticActivationCause.Head);
                }
                else if (handRisk >= handOn)
                {
                    Enable(now, StaticActivationCause.Hand);
                }
            }
            else if (!emergencyActive
                && primaryRisk <= offThreshold
                && handRisk <= handOff)
            {
                heldReason = HoldOrRelease(
                    now,
                    PassthroughDecisionReason.BelowThreshold);
            }
            else
            {
                CancelPendingRelease();
                if (activeCause == StaticActivationCause.LowObstacle)
                {
                    activeCause = StaticActivationCause.Head;
                }
                UpdateActiveCause(
                    primaryRisk,
                    handRisk,
                    onThreshold,
                    offThreshold,
                    handOn,
                    handOff);
                if (activeCause == StaticActivationCause.Head
                    && lowObstacleRisk > headRisk)
                {
                    activeCause = StaticActivationCause.LowObstacle;
                }
            }

            float combinedRisk = frame.CombinedRisk;
            bool aware =
                frame.AnyApproach(settings.awareApproachSpeed)
                && combinedRisk
                    >= StaticBoundaryRiskMath.ClampRisk(
                        settings.awareThreshold);
            StaticWarningLevel warningLevel = enabled
                ? StaticWarningLevel.Full
                : aware ? StaticWarningLevel.Aware : StaticWarningLevel.None;
            StaticActivationCause displayCause = enabled
                ? activeCause
                : aware ? HighestRiskCause(frame) : StaticActivationCause.None;
            SelectDirection(
                frame,
                displayCause,
                out Vector3 hazardDirection,
                out bool hazardAvailable);
            HazardPresentationGeometry presentationGeometry = SelectGeometry(
                frame,
                displayCause);
            if (enabled && hazardAvailable)
            {
                lastHazardDirection = hazardDirection;
                lastHazardDirectionAvailable = true;
            }
            else if (enabled && lastHazardDirectionAvailable)
            {
                hazardDirection = lastHazardDirection;
                hazardAvailable = true;
            }

            PassthroughDecisionReason reason = !enabled
                ? PassthroughDecisionReason.BelowThreshold
                : !wasEnabled
                    ? PassthroughDecisionReason.AtOrAboveThreshold
                    : heldReason;
            return BuildDecision(
                sequence,
                now,
                frame,
                enabled,
                reason,
                warningLevel,
                displayCause,
                onThreshold,
                offThreshold,
                handOff,
                emergencyTrigger,
                emergencyActive,
                hazardDirection,
                hazardAvailable,
                presentationGeometry);
        }

        private StaticPassthroughDecision BuildDecision(
            long sequence,
            double timestampSeconds,
            StaticBoundaryRiskFrame frame,
            bool isEnabled,
            PassthroughDecisionReason reason,
            StaticWarningLevel warningLevel,
            StaticActivationCause cause,
            float onThreshold,
            float offThreshold,
            float handReleaseThreshold,
            bool emergencyTrigger,
            bool emergencyHold,
            Vector3 hazardDirection,
            bool hazardAvailable,
            HazardPresentationGeometry presentationGeometry = default)
        {
            bool available = frame != null && frame.Available;
            float headRisk = available
                ? Mathf.Max(frame.Head.Risk, frame.LowObstacle.Risk)
                : 0f;
            float handRisk = available ? frame.MaximumHandRisk : 0f;
            float combinedRisk = available ? frame.CombinedRisk : 0f;
            float heldSeconds = isEnabled
                ? (float)Math.Max(0.0, timestampSeconds - enabledSince)
                : 0f;
            var filterDecision = new PassthroughDecisionSnapshot(
                isEnabled,
                reason,
                onThreshold,
                offThreshold,
                heldSeconds);
            var sourceDecision = new PassthroughSourceDecision(
                sequence,
                timestampSeconds,
                PassthroughRiskSource.Static,
                available,
                combinedRisk,
                filterDecision);
            return new StaticPassthroughDecision(
                sourceDecision,
                warningLevel,
                cause,
                headRisk,
                handRisk,
                combinedRisk,
                frame == null ? 0f : frame.UserState01,
                onThreshold,
                offThreshold,
                handReleaseThreshold,
                emergencyTrigger,
                emergencyHold,
                hazardDirection,
                hazardAvailable,
                presentationGeometry);
        }

        private void Enable(double timestampSeconds, StaticActivationCause cause)
        {
            enabled = true;
            enabledSince = timestampSeconds;
            activeCause = cause;
            CancelPendingRelease();
        }

        private PassthroughDecisionReason HoldOrRelease(
            double timestampSeconds,
            PassthroughDecisionReason releasedReason)
        {
            if (!releasePending)
            {
                releasePending = true;
                releaseCandidateSince = timestampSeconds;
            }

            float heldSeconds = (float)Math.Max(
                0.0,
                timestampSeconds - enabledSince);
            float minimumHoldSeconds = StaticBoundaryRiskMath.NonNegative(
                settings.minimumHoldSeconds);
            if (heldSeconds < minimumHoldSeconds)
            {
                return PassthroughDecisionReason.MinimumHoldActive;
            }

            float releaseDelaySeconds = StaticBoundaryRiskMath.NonNegative(
                settings.releaseDelaySeconds);
            if (timestampSeconds - releaseCandidateSince
                < releaseDelaySeconds)
            {
                return PassthroughDecisionReason.ReleaseDelayActive;
            }

            ClearRuntimeState();
            return releasedReason;
        }

        private void CancelPendingRelease()
        {
            releaseCandidateSince = 0.0;
            releasePending = false;
        }

        private void UpdateActiveCause(
            float headRisk,
            float handRisk,
            float headOn,
            float headOff,
            float handOn,
            float handOff)
        {
            if (emergencyActive)
            {
                activeCause = StaticActivationCause.Emergency;
                return;
            }

            if (headRisk >= headOn && handRisk < handOn)
            {
                activeCause = StaticActivationCause.Head;
            }
            else if (handRisk >= handOn && headRisk < headOn)
            {
                activeCause = StaticActivationCause.Hand;
            }
            else if (activeCause == StaticActivationCause.Emergency)
            {
                activeCause = headRisk >= handRisk
                    ? StaticActivationCause.Head
                    : StaticActivationCause.Hand;
            }
            else if (activeCause == StaticActivationCause.Head
                && headRisk <= headOff
                && handRisk > handOff)
            {
                activeCause = StaticActivationCause.Hand;
            }
            else if (activeCause == StaticActivationCause.Hand
                && handRisk <= handOff
                && headRisk > headOff)
            {
                activeCause = StaticActivationCause.Head;
            }
        }

        private static StaticActivationCause HighestRiskCause(
            StaticBoundaryRiskFrame frame)
        {
            if (frame.MaximumHandRisk > Mathf.Max(
                    frame.Head.Risk,
                    frame.LowObstacle.Risk))
            {
                return StaticActivationCause.Hand;
            }

            return frame.LowObstacle.Risk > frame.Head.Risk
                ? StaticActivationCause.LowObstacle
                : StaticActivationCause.Head;
        }

        private static void SelectDirection(
            StaticBoundaryRiskFrame frame,
            StaticActivationCause cause,
            out Vector3 direction,
            out bool available)
        {
            if (frame == null)
            {
                direction = Vector3.zero;
                available = false;
                return;
            }

            if (cause == StaticActivationCause.Hand)
            {
                StaticHandRiskMeasurement hand =
                    frame.LeftHand.Risk >= frame.RightHand.Risk
                        ? frame.LeftHand
                        : frame.RightHand;
                direction = hand.HazardDirectionWorld;
                available = hand.HasHazardDirection;
                return;
            }

            if (cause == StaticActivationCause.LowObstacle
                || cause == StaticActivationCause.Emergency
                    && frame.LowObstacle.Risk > frame.Head.Risk
                    && frame.LowObstacle.Risk >= frame.MaximumHandRisk)
            {
                direction = frame.LowObstacleHazardDirectionWorld;
                available = frame.LowObstacleHazardDirectionAvailable;
                return;
            }

            direction = frame.HeadHazardDirectionWorld;
            available = frame.HeadHazardDirectionAvailable;
        }

        private static HazardPresentationGeometry SelectGeometry(
            StaticBoundaryRiskFrame frame,
            StaticActivationCause cause)
        {
            if (frame == null)
            {
                return default;
            }

            if (cause == StaticActivationCause.LowObstacle)
            {
                return frame.LowObstaclePresentationGeometry;
            }

            if (cause == StaticActivationCause.Hand)
            {
                return frame.LeftHand.Risk >= frame.RightHand.Risk
                    ? frame.LeftHandPresentationGeometry
                    : frame.RightHandPresentationGeometry;
            }

            if (cause == StaticActivationCause.Emergency)
            {
                float lowRisk = frame.LowObstacle.Risk;
                float handRisk = frame.MaximumHandRisk;
                if (lowRisk >= frame.Head.Risk && lowRisk >= handRisk)
                {
                    return frame.LowObstaclePresentationGeometry;
                }

                if (handRisk > frame.Head.Risk)
                {
                    return frame.LeftHand.Risk >= frame.RightHand.Risk
                        ? frame.LeftHandPresentationGeometry
                        : frame.RightHandPresentationGeometry;
                }
            }

            return frame.HeadPresentationGeometry;
        }

        private double SanitizeTimestamp(double timestampSeconds)
        {
            if (double.IsNaN(timestampSeconds)
                || double.IsInfinity(timestampSeconds))
            {
                timestampSeconds = lastTimestamp;
            }

            lastTimestamp = Math.Max(lastTimestamp, Math.Max(0.0, timestampSeconds));
            return lastTimestamp;
        }

        private void ClearRuntimeState()
        {
            enabled = false;
            emergencyActive = false;
            activeCause = StaticActivationCause.None;
            enabledSince = 0.0;
            releaseCandidateSince = 0.0;
            releasePending = false;
            lastHazardDirection = Vector3.zero;
            lastHazardDirectionAvailable = false;
        }
    }
}
