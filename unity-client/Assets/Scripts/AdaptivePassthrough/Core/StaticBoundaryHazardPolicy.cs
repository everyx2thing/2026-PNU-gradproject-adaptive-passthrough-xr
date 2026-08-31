using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    /// <summary>
    /// Evaluates each static hazard independently so one rapidly changing
    /// source cannot teleport or retrigger every other static presentation.
    /// The legacy aggregate result remains available on StaticPassthroughDecision.
    /// </summary>
    public sealed class StaticBoundaryPolicy
    {
        private sealed class HazardLatch
        {
            public bool Enabled;
            public bool EmergencyActive;
            public double EnabledSince;
            public double ReleaseCandidateSince;
            public bool ReleasePending;
            public Vector3 LastDirection;
            public bool HasLastDirection;

            public void Reset()
            {
                Enabled = false;
                EmergencyActive = false;
                EnabledSince = 0.0;
                ReleaseCandidateSince = 0.0;
                ReleasePending = false;
                LastDirection = Vector3.zero;
                HasLastDirection = false;
            }
        }

        private readonly StaticBoundaryPolicySettings settings;
        private readonly HazardLatch[] latches =
        {
            new HazardLatch(),
            new HazardLatch(),
            new HazardLatch(),
            new HazardLatch()
        };
        private readonly StaticHazardDecision[] hazardDecisions =
            new StaticHazardDecision[4];
        private StaticRiskChannelMask enabledChannels =
            StaticRiskChannelMask.All;
        private double lastTimestamp;

        public StaticBoundaryPolicy(
            StaticBoundaryPolicySettings settings = null)
        {
            this.settings = settings ?? new StaticBoundaryPolicySettings();
        }

        public void Reset()
        {
            for (int i = 0; i < latches.Length; i++)
            {
                latches[i].Reset();
                hazardDecisions[i] = null;
            }

            lastTimestamp = 0.0;
        }

        public void SetEnabledChannels(StaticRiskChannelMask channels)
        {
            channels &= StaticRiskChannelMask.All;
            StaticRiskChannelMask disabled = enabledChannels & ~channels;
            if ((disabled & StaticRiskChannelMask.Head) != 0)
            {
                latches[0].Reset();
            }
            if ((disabled & StaticRiskChannelMask.Hands) != 0)
            {
                latches[1].Reset();
                latches[2].Reset();
            }
            if ((disabled & StaticRiskChannelMask.LowObstacle) != 0)
            {
                latches[3].Reset();
            }

            enabledChannels = channels;
        }

        public StaticPassthroughDecision Evaluate(
            long sequence,
            double timestampSeconds,
            StaticBoundaryRiskFrame frame)
        {
            return Evaluate(
                sequence,
                timestampSeconds,
                frame,
                StaticRiskChannelMask.All);
        }

        public StaticPassthroughDecision Evaluate(
            long sequence,
            double timestampSeconds,
            StaticBoundaryRiskFrame frame,
            StaticRiskChannelMask enabledChannels)
        {
            double now = SanitizeTimestamp(timestampSeconds);
            enabledChannels &= StaticRiskChannelMask.All;
            SetEnabledChannels(enabledChannels);
            float userState = frame == null ? 0f : frame.UserState01;
            float headOn = StaticBoundaryRiskMath.EffectiveOnThreshold(
                settings.stableOnThreshold,
                settings.rapidOnThreshold,
                userState);
            float headOff = StaticBoundaryRiskMath.EffectiveOffThreshold(
                headOn,
                settings.hysteresisWidth);
            float handOn = StaticBoundaryRiskMath.ClampRisk(
                settings.handFullThreshold);
            float handOff = StaticBoundaryRiskMath.HandReleaseThreshold(
                handOn,
                settings.hysteresisWidth);

            hazardDecisions[0] = EvaluateHead(
                latches[0],
                now,
                frame,
                enabledChannels,
                headOn,
                headOff);
            hazardDecisions[1] = EvaluateHand(
                StaticHazardKey.LeftHand,
                latches[1],
                now,
                frame == null
                    ? StaticHandRiskMeasurement.Unavailable
                    : frame.LeftHand,
                frame == null ? default : frame.LeftHandPresentationGeometry,
                enabledChannels,
                handOn,
                handOff);
            hazardDecisions[2] = EvaluateHand(
                StaticHazardKey.RightHand,
                latches[2],
                now,
                frame == null
                    ? StaticHandRiskMeasurement.Unavailable
                    : frame.RightHand,
                frame == null ? default : frame.RightHandPresentationGeometry,
                enabledChannels,
                handOn,
                handOff);
            hazardDecisions[3] = EvaluateLowObstacle(
                latches[3],
                now,
                frame,
                enabledChannels,
                headOn,
                headOff);

            StaticHazardDecision primary = SelectPrimary(hazardDecisions);
            bool enabled = AnyEnabled(hazardDecisions);
            bool emergencyTrigger = AnyEmergencyTrigger(hazardDecisions);
            bool emergencyHold = AnyEmergencyHold(hazardDecisions);
            bool available = AnyContributingAvailable(hazardDecisions);
            float headRisk = MaximumRiskFor(
                hazardDecisions,
                StaticRiskChannelMask.Head | StaticRiskChannelMask.LowObstacle);
            float handRisk = MaximumRiskFor(
                hazardDecisions,
                StaticRiskChannelMask.Hands);
            float combinedRisk = Mathf.Max(headRisk, handRisk);
            bool aware = !enabled
                && AnyAware(hazardDecisions, settings.awareThreshold,
                    settings.awareApproachSpeed);
            StaticWarningLevel warningLevel = enabled
                ? StaticWarningLevel.Full
                : aware ? StaticWarningLevel.Aware : StaticWarningLevel.None;
            StaticHazardDecision displayed = primary
                ?? (aware ? SelectHighestAvailable(hazardDecisions) : null);
            StaticActivationCause cause = CauseFor(displayed);
            PassthroughDecisionReason reason = SummaryReason(
                hazardDecisions,
                enabled,
                available);
            float heldSeconds = enabled
                ? MaximumHeldSeconds(hazardDecisions)
                : 0f;
            var filterDecision = new PassthroughDecisionSnapshot(
                enabled,
                reason,
                displayed != null ? displayed.OnThreshold : headOn,
                displayed != null ? displayed.OffThreshold : headOff,
                heldSeconds);
            var sourceDecision = new PassthroughSourceDecision(
                sequence,
                now,
                PassthroughRiskSource.Static,
                available,
                combinedRisk,
                filterDecision);
            var publishedHazards = new StaticHazardDecision[hazardDecisions.Length];
            Array.Copy(hazardDecisions, publishedHazards, hazardDecisions.Length);
            return new StaticPassthroughDecision(
                sourceDecision,
                warningLevel,
                cause,
                headRisk,
                handRisk,
                combinedRisk,
                userState,
                headOn,
                headOff,
                handOff,
                emergencyTrigger,
                emergencyHold,
                displayed == null ? Vector3.zero : displayed.HazardDirectionWorld,
                displayed != null && displayed.HazardDirectionAvailable,
                displayed == null ? default : displayed.PresentationGeometry,
                enabledChannels,
                publishedHazards);
        }

        private StaticHazardDecision EvaluateHead(
            HazardLatch latch,
            double now,
            StaticBoundaryRiskFrame frame,
            StaticRiskChannelMask mask,
            float onThreshold,
            float offThreshold)
        {
            StaticRiskMeasurement measurement = frame == null
                ? StaticRiskMeasurement.Unavailable
                : frame.Head;
            bool overlap = frame != null && frame.HeadSafetyOverlapEmergency;
            bool emergency = overlap || IsEmergency(measurement);
            return EvaluateHazard(
                StaticHazardKey.Head,
                StaticRiskChannelMask.Head,
                latch,
                now,
                measurement.Available || overlap,
                measurement.Risk,
                measurement.ClosestDistanceMeters,
                measurement.TowardBoundarySpeed,
                measurement.DistanceRisk,
                measurement.SpeedRisk,
                measurement.TtcRisk,
                emergency,
                frame == null ? Vector3.zero : frame.HeadHazardDirectionWorld,
                frame != null && frame.HeadHazardDirectionAvailable,
                frame == null ? default : frame.HeadPresentationGeometry,
                mask,
                onThreshold,
                offThreshold);
        }

        private StaticHazardDecision EvaluateLowObstacle(
            HazardLatch latch,
            double now,
            StaticBoundaryRiskFrame frame,
            StaticRiskChannelMask mask,
            float onThreshold,
            float offThreshold)
        {
            StaticRiskMeasurement measurement = frame == null
                ? StaticRiskMeasurement.Unavailable
                : frame.LowObstacle;
            return EvaluateHazard(
                StaticHazardKey.LowObstacle,
                StaticRiskChannelMask.LowObstacle,
                latch,
                now,
                measurement.Available,
                measurement.Risk,
                measurement.ClosestDistanceMeters,
                measurement.TowardBoundarySpeed,
                measurement.DistanceRisk,
                measurement.SpeedRisk,
                measurement.TtcRisk,
                IsEmergency(measurement),
                frame == null
                    ? Vector3.zero
                    : frame.LowObstacleHazardDirectionWorld,
                frame != null && frame.LowObstacleHazardDirectionAvailable,
                frame == null ? default : frame.LowObstaclePresentationGeometry,
                mask,
                onThreshold,
                offThreshold);
        }

        private StaticHazardDecision EvaluateHand(
            StaticHazardKey key,
            HazardLatch latch,
            double now,
            StaticHandRiskMeasurement measurement,
            HazardPresentationGeometry geometry,
            StaticRiskChannelMask mask,
            float onThreshold,
            float offThreshold)
        {
            bool emergency = measurement.Available
                && measurement.DistanceMeters
                    <= StaticBoundaryRiskMath.NonNegative(
                        settings.emergencyDistance);
            return EvaluateHazard(
                key,
                StaticRiskChannelMask.Hands,
                latch,
                now,
                measurement.Available,
                measurement.Risk,
                measurement.DistanceMeters,
                measurement.TowardBoundarySpeed,
                measurement.DistanceRisk,
                measurement.SpeedRisk,
                measurement.TtcRisk,
                emergency,
                measurement.HazardDirectionWorld,
                measurement.HasHazardDirection,
                geometry,
                mask,
                onThreshold,
                offThreshold);
        }

        private StaticHazardDecision EvaluateHazard(
            StaticHazardKey key,
            StaticRiskChannelMask channel,
            HazardLatch latch,
            double now,
            bool available,
            float risk,
            float distance,
            float closingSpeed,
            float distanceRisk,
            float speedRisk,
            float ttcRisk,
            bool emergencyTrigger,
            Vector3 direction,
            bool directionAvailable,
            HazardPresentationGeometry geometry,
            StaticRiskChannelMask mask,
            float onThreshold,
            float offThreshold)
        {
            bool channelEnabled = (mask & channel) != 0;
            if (!channelEnabled)
            {
                latch.Reset();
                return BuildHazardDecision(
                    key,
                    channel,
                    false,
                    available,
                    latch,
                    now,
                    risk,
                    distance,
                    closingSpeed,
                    distanceRisk,
                    speedRisk,
                    ttcRisk,
                    false,
                    onThreshold,
                    offThreshold,
                    PassthroughDecisionReason.BelowThreshold,
                    direction,
                    directionAvailable,
                    geometry);
            }

            if (directionAvailable)
            {
                latch.LastDirection = direction.normalized;
                latch.HasLastDirection = true;
            }
            else if (latch.HasLastDirection)
            {
                direction = latch.LastDirection;
                directionAvailable = true;
            }

            PassthroughDecisionReason reason = available
                ? PassthroughDecisionReason.BelowThreshold
                : PassthroughDecisionReason.NoRiskInputs;
            if (emergencyTrigger)
            {
                latch.EmergencyActive = true;
                if (!latch.Enabled)
                {
                    Enable(latch, now);
                    reason = PassthroughDecisionReason.AtOrAboveThreshold;
                }
                else
                {
                    CancelPendingRelease(latch);
                    reason = PassthroughDecisionReason.HysteresisHeld;
                }
            }
            else
            {
                latch.EmergencyActive = false;
                if (!latch.Enabled && available && risk >= onThreshold)
                {
                    Enable(latch, now);
                    reason = PassthroughDecisionReason.AtOrAboveThreshold;
                }
                else if (latch.Enabled && (!available || risk <= offThreshold))
                {
                    reason = HoldOrRelease(
                        latch,
                        now,
                        available
                            ? PassthroughDecisionReason.BelowThreshold
                            : PassthroughDecisionReason.NoRiskInputs);
                }
                else if (latch.Enabled)
                {
                    CancelPendingRelease(latch);
                    reason = PassthroughDecisionReason.HysteresisHeld;
                }
            }

            return BuildHazardDecision(
                key,
                channel,
                true,
                available,
                latch,
                now,
                risk,
                distance,
                closingSpeed,
                distanceRisk,
                speedRisk,
                ttcRisk,
                emergencyTrigger,
                onThreshold,
                offThreshold,
                reason,
                direction,
                directionAvailable,
                geometry);
        }

        private StaticHazardDecision BuildHazardDecision(
            StaticHazardKey key,
            StaticRiskChannelMask channel,
            bool channelEnabled,
            bool available,
            HazardLatch latch,
            double now,
            float risk,
            float distance,
            float closingSpeed,
            float distanceRisk,
            float speedRisk,
            float ttcRisk,
            bool emergencyTrigger,
            float onThreshold,
            float offThreshold,
            PassthroughDecisionReason reason,
            Vector3 direction,
            bool directionAvailable,
            HazardPresentationGeometry geometry)
        {
            return new StaticHazardDecision(
                key,
                channel,
                channelEnabled,
                available,
                latch.Enabled,
                emergencyTrigger,
                latch.EmergencyActive,
                risk,
                distance,
                closingSpeed,
                onThreshold,
                offThreshold,
                latch.Enabled
                    ? (float)Math.Max(0.0, now - latch.EnabledSince)
                    : 0f,
                reason,
                direction,
                directionAvailable,
                geometry,
                distanceRisk,
                speedRisk,
                ttcRisk);
        }

        private static bool IsEmergency(StaticRiskMeasurement measurement)
        {
            return measurement.Available
                && measurement.ClosestDistanceMeters <= 0.25f;
        }

        private static void Enable(HazardLatch latch, double now)
        {
            latch.Enabled = true;
            latch.EnabledSince = now;
            CancelPendingRelease(latch);
        }

        private PassthroughDecisionReason HoldOrRelease(
            HazardLatch latch,
            double now,
            PassthroughDecisionReason releasedReason)
        {
            if (!latch.ReleasePending)
            {
                latch.ReleasePending = true;
                latch.ReleaseCandidateSince = now;
            }

            if (now - latch.EnabledSince
                < StaticBoundaryRiskMath.NonNegative(settings.minimumHoldSeconds))
            {
                return PassthroughDecisionReason.MinimumHoldActive;
            }

            if (now - latch.ReleaseCandidateSince
                < StaticBoundaryRiskMath.NonNegative(settings.releaseDelaySeconds))
            {
                return PassthroughDecisionReason.ReleaseDelayActive;
            }

            latch.Reset();
            return releasedReason;
        }

        private static void CancelPendingRelease(HazardLatch latch)
        {
            latch.ReleasePending = false;
            latch.ReleaseCandidateSince = 0.0;
        }

        private static StaticHazardDecision SelectPrimary(
            StaticHazardDecision[] hazards)
        {
            StaticHazardDecision selected = null;
            for (int i = 0; i < hazards.Length; i++)
            {
                StaticHazardDecision candidate = hazards[i];
                if (candidate == null || !candidate.Enabled)
                {
                    continue;
                }

                if (selected == null || Compare(candidate, selected) < 0)
                {
                    selected = candidate;
                }
            }

            return selected;
        }

        private static StaticHazardDecision SelectHighestAvailable(
            StaticHazardDecision[] hazards)
        {
            StaticHazardDecision selected = null;
            for (int i = 0; i < hazards.Length; i++)
            {
                StaticHazardDecision candidate = hazards[i];
                if (candidate == null
                    || !candidate.ChannelEnabled
                    || !candidate.Available)
                {
                    continue;
                }

                if (selected == null || Compare(candidate, selected) < 0)
                {
                    selected = candidate;
                }
            }

            return selected;
        }

        public static int Compare(
            StaticHazardDecision left,
            StaticHazardDecision right)
        {
            int emergency = right.EmergencyTrigger.CompareTo(
                left.EmergencyTrigger);
            if (emergency != 0)
            {
                return emergency;
            }

            int risk = right.Risk.CompareTo(left.Risk);
            if (risk != 0)
            {
                return risk;
            }

            int ttc = right.TtcRisk.CompareTo(left.TtcRisk);
            if (ttc != 0)
            {
                return ttc;
            }

            int closing = right.ClosingSpeedMetersPerSecond.CompareTo(
                left.ClosingSpeedMetersPerSecond);
            return closing != 0 ? closing : left.Key.CompareTo(right.Key);
        }

        private static bool AnyEnabled(StaticHazardDecision[] hazards)
        {
            for (int i = 0; i < hazards.Length; i++)
            {
                if (hazards[i] != null && hazards[i].Enabled)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AnyEmergencyTrigger(StaticHazardDecision[] hazards)
        {
            for (int i = 0; i < hazards.Length; i++)
            {
                if (hazards[i] != null
                    && hazards[i].ChannelEnabled
                    && hazards[i].EmergencyTrigger)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AnyEmergencyHold(StaticHazardDecision[] hazards)
        {
            for (int i = 0; i < hazards.Length; i++)
            {
                if (hazards[i] != null && hazards[i].EmergencyHold)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AnyContributingAvailable(
            StaticHazardDecision[] hazards)
        {
            for (int i = 0; i < hazards.Length; i++)
            {
                if (hazards[i] != null
                    && hazards[i].ChannelEnabled
                    && hazards[i].Available)
                {
                    return true;
                }
            }

            return false;
        }

        private static float MaximumRiskFor(
            StaticHazardDecision[] hazards,
            StaticRiskChannelMask channels)
        {
            float risk = 0f;
            for (int i = 0; i < hazards.Length; i++)
            {
                StaticHazardDecision hazard = hazards[i];
                if (hazard != null
                    && hazard.ChannelEnabled
                    && (hazard.Channel & channels) != 0)
                {
                    risk = Mathf.Max(risk, hazard.Risk);
                }
            }

            return risk;
        }

        private static float MaximumHeldSeconds(
            StaticHazardDecision[] hazards)
        {
            float held = 0f;
            for (int i = 0; i < hazards.Length; i++)
            {
                if (hazards[i] != null && hazards[i].Enabled)
                {
                    held = Mathf.Max(held, hazards[i].HeldSeconds);
                }
            }

            return held;
        }

        private static bool AnyAware(
            StaticHazardDecision[] hazards,
            float riskThreshold,
            float speedThreshold)
        {
            for (int i = 0; i < hazards.Length; i++)
            {
                StaticHazardDecision hazard = hazards[i];
                if (hazard != null
                    && hazard.ChannelEnabled
                    && hazard.Available
                    && hazard.Risk >= riskThreshold
                    && hazard.ClosingSpeedMetersPerSecond > speedThreshold)
                {
                    return true;
                }
            }

            return false;
        }

        private static PassthroughDecisionReason SummaryReason(
            StaticHazardDecision[] hazards,
            bool enabled,
            bool available)
        {
            if (!enabled)
            {
                return available
                    ? PassthroughDecisionReason.BelowThreshold
                    : PassthroughDecisionReason.NoRiskInputs;
            }

            PassthroughDecisionReason reason =
                PassthroughDecisionReason.HysteresisHeld;
            for (int i = 0; i < hazards.Length; i++)
            {
                StaticHazardDecision hazard = hazards[i];
                if (hazard == null || !hazard.Enabled)
                {
                    continue;
                }

                if (hazard.Reason == PassthroughDecisionReason.AtOrAboveThreshold)
                {
                    return hazard.Reason;
                }

                if (hazard.Reason == PassthroughDecisionReason.MinimumHoldActive)
                {
                    reason = hazard.Reason;
                }
                else if (hazard.Reason ==
                    PassthroughDecisionReason.ReleaseDelayActive
                    && reason != PassthroughDecisionReason.MinimumHoldActive)
                {
                    reason = hazard.Reason;
                }
            }

            return reason;
        }

        private static StaticActivationCause CauseFor(
            StaticHazardDecision hazard)
        {
            if (hazard == null)
            {
                return StaticActivationCause.None;
            }

            if ((hazard.EmergencyTrigger || hazard.EmergencyHold)
                && hazard.Key != StaticHazardKey.LeftHand
                && hazard.Key != StaticHazardKey.RightHand)
            {
                return StaticActivationCause.Emergency;
            }

            if (hazard.Key == StaticHazardKey.LowObstacle)
            {
                return StaticActivationCause.LowObstacle;
            }

            return hazard.Key == StaticHazardKey.LeftHand
                    || hazard.Key == StaticHazardKey.RightHand
                ? StaticActivationCause.Hand
                : StaticActivationCause.Head;
        }

        private double SanitizeTimestamp(double timestampSeconds)
        {
            if (double.IsNaN(timestampSeconds)
                || double.IsInfinity(timestampSeconds))
            {
                timestampSeconds = lastTimestamp;
            }

            lastTimestamp = Math.Max(
                lastTimestamp,
                Math.Max(0.0, timestampSeconds));
            return lastTimestamp;
        }
    }
}
