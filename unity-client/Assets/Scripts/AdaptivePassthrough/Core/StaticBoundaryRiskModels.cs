using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    [Flags]
    public enum StaticRiskChannelMask
    {
        None = 0,
        Head = 1 << 0,
        Hands = 1 << 1,
        LowObstacle = 1 << 2,
        All = Head | Hands | LowObstacle
    }

    public enum StaticHazardKey
    {
        None = 0,
        Head = 1,
        LeftHand = 2,
        RightHand = 3,
        LowObstacle = 4
    }

    [Serializable]
    public class StaticRiskChannelPreferences
    {
        public bool HeadFeatureEnabled = true;
        public bool HandsFeatureEnabled = true;
        public bool LowObstacleFeatureEnabled = true;

        public StaticRiskChannelMask ToMask()
        {
            StaticRiskChannelMask result = StaticRiskChannelMask.None;
            if (HeadFeatureEnabled)
            {
                result |= StaticRiskChannelMask.Head;
            }
            if (HandsFeatureEnabled)
            {
                result |= StaticRiskChannelMask.Hands;
            }
            if (LowObstacleFeatureEnabled)
            {
                result |= StaticRiskChannelMask.LowObstacle;
            }
            return result;
        }

        public void PreserveAllOnForLegacyJson(string json)
        {
            if (!string.IsNullOrEmpty(json)
                && json.IndexOf(
                    "\"HeadFeatureEnabled\"",
                    StringComparison.Ordinal) >= 0)
            {
                return;
            }

            HeadFeatureEnabled = true;
            HandsFeatureEnabled = true;
            LowObstacleFeatureEnabled = true;
        }
    }

    public interface IStaticRiskDiagnosticsProvider
    {
        StaticBoundaryRiskFrame CurrentStaticBoundaryFrame { get; }

        StaticPassthroughDecision CurrentStaticDecision { get; }
    }

    public interface IStaticPresentationDiagnosticsProvider
    {
        StaticRiskChannelMask EnabledStaticChannels { get; }

        bool StaticFeatureEnabled { get; }

        bool EffectiveStaticFeatureEnabled { get; }

        bool DynamicFeatureEnabled { get; }

        bool EffectiveDynamicFeatureEnabled { get; }

        StaticRiskChannelMask EffectiveStaticChannels { get; }

        SafetyFeedbackMode FeedbackMode { get; }

        SafetyFeedbackMode EffectiveFeedbackMode { get; }

        int ActiveStaticWindowCount { get; }

        string VisibleStaticHazardKeys { get; }

        string LastStaticReplacementReason { get; }
    }

    public enum StaticWarningLevel
    {
        None,
        Aware,
        Full
    }

    public enum StaticActivationCause
    {
        None = 0,
        Head = 1,
        Hand = 2,
        Emergency = 3,
        LowObstacle = 4
    }

    public readonly struct StaticHandRiskMeasurement
    {
        public readonly bool Available;
        public readonly int WallIndex;
        public readonly float DistanceMeters;
        public readonly float TowardBoundarySpeed;
        public readonly float MinimumObservedDistanceMeters;
        public readonly float ExtensionMeters;
        public readonly float ReachGate;
        public readonly float DistanceRisk;
        public readonly float SpeedRisk;
        public readonly float TtcRisk;
        public readonly float Risk;
        public readonly Vector3 HazardDirectionWorld;

        public StaticHandRiskMeasurement(
            bool available,
            int wallIndex,
            float distanceMeters,
            float towardBoundarySpeed,
            float minimumObservedDistanceMeters,
            float extensionMeters,
            float reachGate,
            float distanceRisk,
            float ttcRisk,
            float risk,
            Vector3 hazardDirectionWorld)
            : this(
                available,
                wallIndex,
                distanceMeters,
                towardBoundarySpeed,
                minimumObservedDistanceMeters,
                extensionMeters,
                reachGate,
                distanceRisk,
                0f,
                ttcRisk,
                risk,
                hazardDirectionWorld)
        {
        }

        public StaticHandRiskMeasurement(
            bool available,
            int wallIndex,
            float distanceMeters,
            float towardBoundarySpeed,
            float minimumObservedDistanceMeters,
            float extensionMeters,
            float reachGate,
            float distanceRisk,
            float speedRisk,
            float ttcRisk,
            float risk,
            Vector3 hazardDirectionWorld)
        {
            Available = available;
            WallIndex = available ? wallIndex : -1;
            DistanceMeters = NonNegative(distanceMeters);
            TowardBoundarySpeed = NonNegative(towardBoundarySpeed);
            MinimumObservedDistanceMeters =
                NonNegative(minimumObservedDistanceMeters);
            ExtensionMeters = NonNegative(extensionMeters);
            ReachGate = Clamp01(reachGate);
            DistanceRisk = Clamp01(distanceRisk);
            SpeedRisk = Clamp01(speedRisk);
            TtcRisk = Clamp01(ttcRisk);
            Risk = Clamp01(risk);
            HazardDirectionWorld =
                available ? SafeDirection(hazardDirectionWorld) : Vector3.zero;
        }

        public bool HasHazardDirection
        {
            get
            {
                return Available
                    && HazardDirectionWorld.sqrMagnitude > 0.0001f;
            }
        }

        public static StaticHandRiskMeasurement Unavailable
        {
            get
            {
                return new StaticHandRiskMeasurement(
                    false,
                    -1,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    Vector3.zero);
            }
        }

        private static float NonNegative(float value)
        {
            return Mathf.Max(0f, Safe(value));
        }

        private static float Clamp01(float value)
        {
            return Mathf.Clamp01(Safe(value));
        }

        private static float Safe(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : value;
        }

        private static Vector3 SafeDirection(Vector3 value)
        {
            if (!IsFinite(value.x)
                || !IsFinite(value.y)
                || !IsFinite(value.z)
                || value.sqrMagnitude <= 0.0001f)
            {
                return Vector3.zero;
            }

            return value.normalized;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public sealed class StaticBoundaryRiskFrame
    {
        public const int CurrentSchemaVersion = 2;

        public readonly int SchemaVersion;
        public readonly long Sequence;
        public readonly double TimestampSeconds;
        public readonly bool Available;
        public readonly StaticRiskMeasurement Head;
        public readonly float UserState01;
        public readonly bool MotionWindowWarmedUp;
        public readonly StaticHandRiskMeasurement LeftHand;
        public readonly StaticHandRiskMeasurement RightHand;
        public readonly Vector3 HeadHazardDirectionWorld;
        public readonly bool HeadHazardDirectionAvailable;
        public readonly int HeadWallIndex;
        public readonly float ObservedReachMeters;
        public readonly bool HeadSafetyOverlapEmergency;
        public readonly StaticRiskMeasurement LowObstacle;
        public readonly Vector3 LowObstacleHazardDirectionWorld;
        public readonly bool LowObstacleHazardDirectionAvailable;
        public readonly HazardPresentationGeometry HeadPresentationGeometry;
        public readonly HazardPresentationGeometry LeftHandPresentationGeometry;
        public readonly HazardPresentationGeometry RightHandPresentationGeometry;
        public readonly HazardPresentationGeometry LowObstaclePresentationGeometry;

        public StaticBoundaryRiskFrame(
            long sequence,
            double timestampSeconds,
            bool available,
            StaticRiskMeasurement head,
            float userState01,
            bool motionWindowWarmedUp,
            StaticHandRiskMeasurement leftHand,
            StaticHandRiskMeasurement rightHand,
            Vector3 headHazardDirectionWorld,
            bool headHazardDirectionAvailable,
            int headWallIndex,
            float observedReachMeters,
            bool headSafetyOverlapEmergency = false,
            StaticRiskMeasurement lowObstacle = default,
            Vector3 lowObstacleHazardDirectionWorld = default,
            bool lowObstacleHazardDirectionAvailable = false,
            HazardPresentationGeometry headPresentationGeometry = default,
            HazardPresentationGeometry leftHandPresentationGeometry = default,
            HazardPresentationGeometry rightHandPresentationGeometry = default,
            HazardPresentationGeometry lowObstaclePresentationGeometry = default)
        {
            SchemaVersion = CurrentSchemaVersion;
            Sequence = Math.Max(0L, sequence);
            TimestampSeconds = Math.Max(0.0, Safe(timestampSeconds));
            HeadSafetyOverlapEmergency = headSafetyOverlapEmergency;
            Available = available
                && (head.Available
                    || lowObstacle.Available
                    || leftHand.Available
                    || rightHand.Available
                    || HeadSafetyOverlapEmergency);
            Head = Available && head.Available
                ? head
                : StaticRiskMeasurement.Unavailable;
            LowObstacle = Available && lowObstacle.Available
                ? lowObstacle
                : StaticRiskMeasurement.Unavailable;
            UserState01 = Clamp01(userState01);
            MotionWindowWarmedUp = motionWindowWarmedUp;
            LeftHand = Available && leftHand.Available
                ? leftHand
                : StaticHandRiskMeasurement.Unavailable;
            RightHand = Available && rightHand.Available
                ? rightHand
                : StaticHandRiskMeasurement.Unavailable;
            HeadHazardDirectionWorld = Available && head.Available
                ? SafeDirection(headHazardDirectionWorld)
                : Vector3.zero;
            HeadHazardDirectionAvailable =
                Available
                && headHazardDirectionAvailable
                && HeadHazardDirectionWorld.sqrMagnitude > 0.0001f;
            HeadWallIndex = Available && head.Available ? headWallIndex : -1;
            ObservedReachMeters = Mathf.Max(0f, Safe(observedReachMeters));
            LowObstacleHazardDirectionWorld = Available
                && LowObstacle.Available
                ? SafeDirection(lowObstacleHazardDirectionWorld)
                : Vector3.zero;
            LowObstacleHazardDirectionAvailable = Available
                && LowObstacle.Available
                && lowObstacleHazardDirectionAvailable
                && LowObstacleHazardDirectionWorld.sqrMagnitude > 0.0001f;
            HeadPresentationGeometry = Available
                && headPresentationGeometry.Available
                ? headPresentationGeometry
                : default;
            LeftHandPresentationGeometry = Available
                && leftHandPresentationGeometry.Available
                ? leftHandPresentationGeometry
                : default;
            RightHandPresentationGeometry = Available
                && rightHandPresentationGeometry.Available
                ? rightHandPresentationGeometry
                : default;
            LowObstaclePresentationGeometry = Available
                && lowObstaclePresentationGeometry.Available
                ? lowObstaclePresentationGeometry
                : default;
        }

        public float MaximumHandRisk
        {
            get { return Mathf.Max(LeftHand.Risk, RightHand.Risk); }
        }

        public float CombinedRisk
        {
            get
            {
                return Mathf.Max(
                    Mathf.Max(Head.Risk, LowObstacle.Risk),
                    MaximumHandRisk);
            }
        }

        public bool AnyApproach(float minimumSpeed)
        {
            float threshold = Mathf.Max(0f, Safe(minimumSpeed));
            return Available
                && (Head.TowardBoundarySpeed > threshold
                    || LowObstacle.TowardBoundarySpeed > threshold
                    || LeftHand.TowardBoundarySpeed > threshold
                    || RightHand.TowardBoundarySpeed > threshold);
        }

        public static StaticBoundaryRiskFrame Unavailable
        {
            get
            {
                return new StaticBoundaryRiskFrame(
                    0L,
                    0.0,
                    false,
                    StaticRiskMeasurement.Unavailable,
                    0f,
                    false,
                    StaticHandRiskMeasurement.Unavailable,
                    StaticHandRiskMeasurement.Unavailable,
                    Vector3.zero,
                    false,
                    -1,
                    0f);
            }
        }

        private static float Clamp01(float value)
        {
            return Mathf.Clamp01(Safe(value));
        }

        private static float Safe(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : value;
        }

        private static double Safe(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? 0.0
                : value;
        }

        private static Vector3 SafeDirection(Vector3 value)
        {
            if (!IsFinite(value.x)
                || !IsFinite(value.y)
                || !IsFinite(value.z)
                || value.sqrMagnitude <= 0.0001f)
            {
                return Vector3.zero;
            }

            return value.normalized;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    [Serializable]
    public sealed class StaticBoundaryPolicySettings
    {
        [Range(0f, 1f)] public float stableOnThreshold = 0.50f;
        [Range(0f, 1f)] public float rapidOnThreshold = 0.45f;
        [Range(0f, 1f)] public float hysteresisWidth = 0.08f;
        [Range(0f, 1f)] public float handFullThreshold = 0.40f;
        [Range(0f, 1f)] public float awareThreshold = 0.40f;
        [Min(0f)] public float minimumHoldSeconds = 1.50f;
        [Min(0f)] public float releaseDelaySeconds = 0.35f;
        [Min(0f)] public float awareApproachSpeed = 0.05f;
        [Min(0f)] public float emergencyDistance = 0.25f;
        [Min(0f)] public float emergencyReleaseMargin = 0.05f;
        [Min(0f)] public float emergencyApproachSpeed = 0.10f;
    }

    public sealed class StaticPassthroughDecision
    {
        public readonly PassthroughSourceDecision SourceDecision;
        public readonly StaticWarningLevel WarningLevel;
        public readonly StaticActivationCause Cause;
        public readonly float HeadRisk;
        public readonly float HandRisk;
        public readonly float CombinedRisk;
        public readonly float UserState01;
        public readonly float EffectiveOnThreshold;
        public readonly float EffectiveOffThreshold;
        public readonly float HandReleaseThreshold;
        public readonly bool EmergencyTrigger;
        public readonly bool EmergencyHold;
        public readonly Vector3 HazardDirectionWorld;
        public readonly bool HazardDirectionAvailable;
        public readonly HazardPresentationGeometry PresentationGeometry;
        public readonly StaticRiskChannelMask EnabledChannels;
        public readonly StaticHazardDecision[] Hazards;

        public StaticPassthroughDecision(
            PassthroughSourceDecision sourceDecision,
            StaticWarningLevel warningLevel,
            StaticActivationCause cause,
            float headRisk,
            float handRisk,
            float combinedRisk,
            float userState01,
            float effectiveOnThreshold,
            float effectiveOffThreshold,
            float handReleaseThreshold,
            bool emergencyTrigger,
            bool emergencyHold,
            Vector3 hazardDirectionWorld,
            bool hazardDirectionAvailable,
            HazardPresentationGeometry presentationGeometry = default)
            : this(
                sourceDecision,
                warningLevel,
                cause,
                headRisk,
                handRisk,
                combinedRisk,
                userState01,
                effectiveOnThreshold,
                effectiveOffThreshold,
                handReleaseThreshold,
                emergencyTrigger,
                emergencyHold,
                hazardDirectionWorld,
                hazardDirectionAvailable,
                presentationGeometry,
                StaticRiskChannelMask.All,
                null)
        {
        }

        public StaticPassthroughDecision(
            PassthroughSourceDecision sourceDecision,
            StaticWarningLevel warningLevel,
            StaticActivationCause cause,
            float headRisk,
            float handRisk,
            float combinedRisk,
            float userState01,
            float effectiveOnThreshold,
            float effectiveOffThreshold,
            float handReleaseThreshold,
            bool emergencyTrigger,
            bool emergencyHold,
            Vector3 hazardDirectionWorld,
            bool hazardDirectionAvailable,
            HazardPresentationGeometry presentationGeometry,
            StaticRiskChannelMask enabledChannels,
            StaticHazardDecision[] hazards)
        {
            SourceDecision = sourceDecision;
            WarningLevel = warningLevel;
            Cause = cause;
            HeadRisk = Mathf.Clamp01(headRisk);
            HandRisk = Mathf.Clamp01(handRisk);
            CombinedRisk = Mathf.Clamp01(combinedRisk);
            UserState01 = Mathf.Clamp01(userState01);
            EffectiveOnThreshold = Mathf.Clamp01(effectiveOnThreshold);
            EffectiveOffThreshold = Mathf.Clamp01(effectiveOffThreshold);
            HandReleaseThreshold = Mathf.Clamp01(handReleaseThreshold);
            EmergencyTrigger = emergencyTrigger;
            EmergencyHold = emergencyHold;
            HazardDirectionWorld = hazardDirectionAvailable
                ? hazardDirectionWorld.normalized
                : Vector3.zero;
            HazardDirectionAvailable =
                hazardDirectionAvailable
                && HazardDirectionWorld.sqrMagnitude > 0.0001f;
            PresentationGeometry = presentationGeometry.Available
                ? presentationGeometry.WithRisk(CombinedRisk)
                : default;
            EnabledChannels = enabledChannels & StaticRiskChannelMask.All;
            Hazards = hazards ?? Array.Empty<StaticHazardDecision>();
        }

        public bool Enabled
        {
            get { return SourceDecision != null && SourceDecision.Enabled; }
        }
    }

    public sealed class StaticHazardDecision
    {
        public readonly StaticHazardKey Key;
        public readonly StaticRiskChannelMask Channel;
        public readonly bool ChannelEnabled;
        public readonly bool Available;
        public readonly bool Enabled;
        public readonly bool EmergencyTrigger;
        public readonly bool EmergencyHold;
        public readonly float Risk;
        public readonly float DistanceMeters;
        public readonly float ClosingSpeedMetersPerSecond;
        public readonly float DistanceRisk;
        public readonly float SpeedRisk;
        public readonly float TtcRisk;
        public readonly float OnThreshold;
        public readonly float OffThreshold;
        public readonly float HeldSeconds;
        public readonly PassthroughDecisionReason Reason;
        public readonly Vector3 HazardDirectionWorld;
        public readonly bool HazardDirectionAvailable;
        public readonly HazardPresentationGeometry PresentationGeometry;

        public StaticHazardDecision(
            StaticHazardKey key,
            StaticRiskChannelMask channel,
            bool channelEnabled,
            bool available,
            bool enabled,
            bool emergencyTrigger,
            bool emergencyHold,
            float risk,
            float distanceMeters,
            float closingSpeedMetersPerSecond,
            float onThreshold,
            float offThreshold,
            float heldSeconds,
            PassthroughDecisionReason reason,
            Vector3 hazardDirectionWorld,
            bool hazardDirectionAvailable,
            HazardPresentationGeometry presentationGeometry,
            float distanceRisk = 0f,
            float speedRisk = 0f,
            float ttcRisk = 0f)
        {
            Key = key;
            Channel = channel;
            ChannelEnabled = channelEnabled;
            Available = available;
            Enabled = enabled;
            EmergencyTrigger = emergencyTrigger;
            EmergencyHold = emergencyHold;
            Risk = Mathf.Clamp01(risk);
            DistanceMeters = Mathf.Max(0f, distanceMeters);
            ClosingSpeedMetersPerSecond = Mathf.Max(
                0f,
                closingSpeedMetersPerSecond);
            DistanceRisk = Mathf.Clamp01(distanceRisk);
            SpeedRisk = Mathf.Clamp01(speedRisk);
            TtcRisk = Mathf.Clamp01(ttcRisk);
            OnThreshold = Mathf.Clamp01(onThreshold);
            OffThreshold = Mathf.Clamp01(offThreshold);
            HeldSeconds = Mathf.Max(0f, heldSeconds);
            Reason = reason;
            HazardDirectionWorld = hazardDirectionAvailable
                ? hazardDirectionWorld.normalized
                : Vector3.zero;
            HazardDirectionAvailable = hazardDirectionAvailable
                && HazardDirectionWorld.sqrMagnitude > 0.0001f;
            PresentationGeometry = presentationGeometry.Available
                ? presentationGeometry.WithRisk(Risk)
                : default;
        }
    }
}
