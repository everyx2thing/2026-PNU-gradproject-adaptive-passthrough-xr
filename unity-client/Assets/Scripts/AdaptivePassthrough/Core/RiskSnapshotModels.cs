using System;

namespace TeamVR.AdaptivePassthrough
{
    public enum OverallRiskLevel
    {
        Safe,
        Caution,
        Warning,
        Danger
    }

    public enum PassthroughDecisionMode
    {
        CompatibilityThreshold,
        Hysteresis
    }

    public enum PassthroughDecisionReason
    {
        NoRiskInputs,
        BelowThreshold,
        AtOrAboveThreshold,
        HysteresisHeld,
        MinimumHoldActive,
        ReleaseDelayActive
    }

    public interface IRiskSnapshotSequenceProvider
    {
        long LatestSnapshotSequence { get; }
    }

    public readonly struct StaticRiskMeasurement
    {
        public readonly bool Available;
        public readonly float ClosestDistanceMeters;
        public readonly float TimeToCollisionSeconds;
        public readonly bool HasTimeToCollision;
        public readonly float TowardBoundarySpeed;
        public readonly float TowardBoundaryAcceleration;
        public readonly float DistanceRisk;
        public readonly float TtcRisk;
        public readonly float AccelerationRisk;
        public readonly float BlindSpotRisk;
        public readonly float Risk;

        public StaticRiskMeasurement(
            bool available,
            float closestDistanceMeters,
            float timeToCollisionSeconds,
            bool hasTimeToCollision,
            float towardBoundarySpeed,
            float towardBoundaryAcceleration,
            float distanceRisk,
            float ttcRisk,
            float accelerationRisk,
            float blindSpotRisk,
            float risk)
        {
            Available = available;
            ClosestDistanceMeters = closestDistanceMeters;
            TimeToCollisionSeconds = timeToCollisionSeconds;
            HasTimeToCollision = hasTimeToCollision;
            TowardBoundarySpeed = towardBoundarySpeed;
            TowardBoundaryAcceleration = towardBoundaryAcceleration;
            DistanceRisk = distanceRisk;
            TtcRisk = ttcRisk;
            AccelerationRisk = accelerationRisk;
            BlindSpotRisk = blindSpotRisk;
            Risk = risk;
        }

        public static StaticRiskMeasurement Unavailable
        {
            get
            {
                return new StaticRiskMeasurement(
                    false,
                    0f,
                    0f,
                    false,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f);
            }
        }
    }

    public sealed class RiskSourceState
    {
        public readonly bool CameraReady;
        public readonly bool RoomSceneReady;
        public readonly bool MotionReady;
        public readonly bool DynamicReady;
        public readonly bool IntentReady;
        public readonly double DynamicSourceTimestampSeconds;
        public readonly float DynamicSourceAgeSeconds;

        public RiskSourceState(
            bool cameraReady,
            bool roomSceneReady,
            bool motionReady,
            bool dynamicReady,
            bool intentReady,
            double dynamicSourceTimestampSeconds,
            float dynamicSourceAgeSeconds)
        {
            CameraReady = cameraReady;
            RoomSceneReady = roomSceneReady;
            MotionReady = motionReady;
            DynamicReady = dynamicReady;
            IntentReady = intentReady;
            DynamicSourceTimestampSeconds = dynamicSourceTimestampSeconds;
            DynamicSourceAgeSeconds = dynamicSourceAgeSeconds;
        }
    }

    public sealed class StaticRiskSnapshot
    {
        public readonly bool Available;
        public readonly float ClosestDistanceMeters;
        public readonly float TimeToCollisionSeconds;
        public readonly bool HasTimeToCollision;
        public readonly float TowardBoundarySpeed;
        public readonly float TowardBoundaryAcceleration;
        public readonly float DistanceRisk;
        public readonly float TtcRisk;
        public readonly float AccelerationRisk;
        public readonly float BlindSpotRisk;
        public readonly float Risk;

        public StaticRiskSnapshot(
            bool available,
            float closestDistanceMeters,
            float timeToCollisionSeconds,
            bool hasTimeToCollision,
            float towardBoundarySpeed,
            float towardBoundaryAcceleration,
            float distanceRisk,
            float ttcRisk,
            float accelerationRisk,
            float blindSpotRisk,
            float risk)
        {
            Available = available;
            ClosestDistanceMeters = closestDistanceMeters;
            TimeToCollisionSeconds = timeToCollisionSeconds;
            HasTimeToCollision = hasTimeToCollision;
            TowardBoundarySpeed = towardBoundarySpeed;
            TowardBoundaryAcceleration = towardBoundaryAcceleration;
            DistanceRisk = distanceRisk;
            TtcRisk = ttcRisk;
            AccelerationRisk = accelerationRisk;
            BlindSpotRisk = blindSpotRisk;
            Risk = risk;
        }
    }

    public sealed class UserStateRiskSnapshot
    {
        public readonly bool Available;
        public readonly UserMotionState StableState;
        public readonly float FilteredSpeed;
        public readonly float FilteredAcceleration;
        public readonly float FilteredAngularSpeed;
        public readonly float Risk;

        public UserStateRiskSnapshot(
            bool available,
            UserMotionState stableState,
            float filteredSpeed,
            float filteredAcceleration,
            float filteredAngularSpeed,
            float risk)
        {
            Available = available;
            StableState = stableState;
            FilteredSpeed = filteredSpeed;
            FilteredAcceleration = filteredAcceleration;
            FilteredAngularSpeed = filteredAngularSpeed;
            Risk = risk;
        }
    }

    public sealed class DynamicRiskSnapshot
    {
        public readonly bool Available;
        public readonly double SourceTimestampSeconds;
        public readonly float SourceAgeSeconds;
        public readonly int ConfirmedPersonCount;
        public readonly float MaximumRisk;
        public readonly DynamicRiskLevel MaximumLevel;
        public readonly int PrimaryTrackId;
        public readonly float PrimaryConfidence;
        public readonly DynamicMotionState PrimaryMotionState;

        public DynamicRiskSnapshot(
            bool available,
            double sourceTimestampSeconds,
            float sourceAgeSeconds,
            int confirmedPersonCount,
            float maximumRisk,
            DynamicRiskLevel maximumLevel,
            int primaryTrackId,
            float primaryConfidence,
            DynamicMotionState primaryMotionState)
        {
            Available = available;
            SourceTimestampSeconds = sourceTimestampSeconds;
            SourceAgeSeconds = sourceAgeSeconds;
            ConfirmedPersonCount = confirmedPersonCount;
            MaximumRisk = maximumRisk;
            MaximumLevel = maximumLevel;
            PrimaryTrackId = primaryTrackId;
            PrimaryConfidence = primaryConfidence;
            PrimaryMotionState = primaryMotionState;
        }
    }

    public sealed class IntentRiskSnapshot
    {
        public readonly bool Available;
        public readonly float Risk;
        public readonly float Confidence;

        public IntentRiskSnapshot(bool available, float risk, float confidence)
        {
            Available = available;
            Risk = risk;
            Confidence = confidence;
        }
    }

    public sealed class PassthroughDecisionSnapshot
    {
        public readonly bool Enabled;
        public readonly PassthroughDecisionReason Reason;
        public readonly float OnThreshold;
        public readonly float OffThreshold;
        public readonly float HeldSeconds;

        public PassthroughDecisionSnapshot(
            bool enabled,
            PassthroughDecisionReason reason,
            float onThreshold,
            float offThreshold,
            float heldSeconds)
        {
            Enabled = enabled;
            Reason = reason;
            OnThreshold = onThreshold;
            OffThreshold = offThreshold;
            HeldSeconds = heldSeconds;
        }
    }

    public sealed class RiskSnapshot
    {
        public const int CurrentSchemaVersion = 1;

        public readonly int SchemaVersion;
        public readonly long Sequence;
        public readonly double TimestampSeconds;
        public readonly RiskSourceState Sources;
        public readonly StaticRiskSnapshot Static;
        public readonly UserStateRiskSnapshot UserState;
        public readonly DynamicRiskSnapshot Dynamic;
        public readonly IntentRiskSnapshot Intent;
        public readonly OverallRiskResult Overall;
        public readonly OverallRiskLevel OverallLevel;
        public readonly PassthroughDecisionSnapshot Passthrough;

        public RiskSnapshot(
            long sequence,
            double timestampSeconds,
            RiskSourceState sources,
            StaticRiskSnapshot staticRisk,
            UserStateRiskSnapshot userState,
            DynamicRiskSnapshot dynamicRisk,
            IntentRiskSnapshot intent,
            OverallRiskResult overall,
            OverallRiskLevel overallLevel,
            PassthroughDecisionSnapshot passthrough)
        {
            SchemaVersion = CurrentSchemaVersion;
            Sequence = sequence;
            TimestampSeconds = timestampSeconds;
            Sources = sources;
            Static = staticRisk;
            UserState = userState;
            Dynamic = dynamicRisk;
            Intent = intent;
            Overall = overall;
            OverallLevel = overallLevel;
            Passthrough = passthrough;
        }
    }
}
