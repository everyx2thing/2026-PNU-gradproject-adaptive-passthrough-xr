using System;
using System.Collections.Generic;

namespace TeamVR.AdaptivePassthrough
{
    public enum DynamicMotionState
    {
        Unknown,
        Approaching,
        Steady,
        Receding
    }

    public enum DistanceBand
    {
        Far,
        Mid,
        Near
    }

    public enum HorizontalZone
    {
        Left,
        Center,
        Right
    }

    public enum DynamicRiskLevel
    {
        Safe,
        Caution,
        Warning,
        Danger
    }

    public enum TrackLifecycle
    {
        Tentative,
        Confirmed,
        Lost
    }

    [Serializable]
    public struct NormalizedBoundingBox
    {
        public float centerX;
        public float centerY;
        public float width;
        public float height;

        public NormalizedBoundingBox(float centerX, float centerY, float width, float height)
        {
            this.centerX = Clamp01(centerX);
            this.centerY = Clamp01(centerY);
            this.width = Clamp01(width);
            this.height = Clamp01(height);
        }

        public float Area
        {
            get { return Math.Max(0f, width) * Math.Max(0f, height); }
        }

        public float Left
        {
            get { return Clamp01(centerX - width * 0.5f); }
        }

        public float Top
        {
            get { return Clamp01(centerY - height * 0.5f); }
        }

        public float Right
        {
            get { return Clamp01(centerX + width * 0.5f); }
        }

        public float Bottom
        {
            get { return Clamp01(centerY + height * 0.5f); }
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }
    }

    [Serializable]
    public sealed class DynamicObjectDetection
    {
        public string label;
        public int classId;
        public float confidence;
        public NormalizedBoundingBox boundingBox;

        public DynamicObjectDetection(
            string label,
            float confidence,
            NormalizedBoundingBox boundingBox,
            int classId = 0)
        {
            this.label = string.IsNullOrWhiteSpace(label) ? "unknown" : label;
            this.classId = classId;
            this.confidence = Math.Max(0f, Math.Min(1f, confidence));
            this.boundingBox = boundingBox;
        }
    }

    public sealed class TrackedDynamicObject
    {
        public readonly int TrackId;
        public readonly DynamicObjectDetection Detection;
        public readonly float AreaGrowthRatePerSecond;
        public readonly TrackLifecycle Lifecycle;
        public readonly bool ObservedThisFrame;
        public readonly int MissedFrames;

        public TrackedDynamicObject(
            int trackId,
            DynamicObjectDetection detection,
            float areaGrowthRatePerSecond)
            : this(
                trackId,
                detection,
                areaGrowthRatePerSecond,
                TrackLifecycle.Confirmed,
                true,
                0)
        {
        }

        public TrackedDynamicObject(
            int trackId,
            DynamicObjectDetection detection,
            float areaGrowthRatePerSecond,
            TrackLifecycle lifecycle,
            bool observedThisFrame,
            int missedFrames)
        {
            TrackId = trackId;
            Detection = detection;
            AreaGrowthRatePerSecond = areaGrowthRatePerSecond;
            Lifecycle = lifecycle;
            ObservedThisFrame = observedThisFrame;
            MissedFrames = Math.Max(0, missedFrames);
        }

        public bool IsConfirmed
        {
            get { return Lifecycle == TrackLifecycle.Confirmed; }
        }
    }

    public sealed class MotionEstimate
    {
        public readonly DynamicMotionState State;
        public readonly float ScaleRatePerSecond;
        public readonly float? TtcSecondsApprox;
        public readonly float CenterApproachRatePerSecond;
        public readonly int SampleCount;
        public readonly double ObservationSeconds;
        public readonly float Reliability;

        public MotionEstimate(
            DynamicMotionState state,
            float scaleRatePerSecond,
            float? ttcSecondsApprox,
            float centerApproachRatePerSecond,
            int sampleCount,
            double observationSeconds,
            float reliability)
        {
            State = state;
            ScaleRatePerSecond = scaleRatePerSecond;
            TtcSecondsApprox = ttcSecondsApprox;
            CenterApproachRatePerSecond = centerApproachRatePerSecond;
            SampleCount = sampleCount;
            ObservationSeconds = observationSeconds;
            Reliability = Math.Max(0f, Math.Min(1f, reliability));
        }
    }

    public sealed class RelativeLocationEstimate
    {
        public readonly HorizontalZone ScreenZone;
        public readonly string UserRelativeDirection;
        public readonly DistanceBand DistanceBand;
        public readonly float BearingDegrees;
        public readonly float BoundingBoxArea;

        public RelativeLocationEstimate(
            HorizontalZone screenZone,
            string userRelativeDirection,
            DistanceBand distanceBand,
            float bearingDegrees,
            float boundingBoxArea)
        {
            ScreenZone = screenZone;
            UserRelativeDirection = userRelativeDirection;
            DistanceBand = distanceBand;
            BearingDegrees = bearingDegrees;
            BoundingBoxArea = boundingBoxArea;
        }
    }

    public sealed class DynamicRiskBreakdown
    {
        public readonly float Proximity;
        public readonly float Approach;
        public readonly float Ttc;
        public readonly float CollisionPath;
        public readonly float ObjectType;
        public readonly float ProximityApproachInteraction;
        public readonly float ConfidenceFactor;

        public DynamicRiskBreakdown(
            float proximity,
            float approach,
            float ttc,
            float collisionPath,
            float objectType,
            float proximityApproachInteraction,
            float confidenceFactor)
        {
            Proximity = proximity;
            Approach = approach;
            Ttc = ttc;
            CollisionPath = collisionPath;
            ObjectType = objectType;
            ProximityApproachInteraction = proximityApproachInteraction;
            ConfidenceFactor = confidenceFactor;
        }
    }

    public sealed class DynamicRiskAssessment
    {
        public readonly int TrackId;
        public readonly DynamicObjectDetection Detection;
        public readonly RelativeLocationEstimate Location;
        public readonly MotionEstimate Motion;
        public readonly float Score;
        public readonly DynamicRiskLevel Level;
        public readonly string[] Reasons;
        public readonly DynamicRiskBreakdown Breakdown;

        public DynamicRiskAssessment(
            int trackId,
            DynamicObjectDetection detection,
            RelativeLocationEstimate location,
            MotionEstimate motion,
            float score,
            DynamicRiskLevel level,
            string[] reasons,
            DynamicRiskBreakdown breakdown)
        {
            TrackId = trackId;
            Detection = detection;
            Location = location;
            Motion = motion;
            Score = score;
            Level = level;
            Reasons = reasons ?? Array.Empty<string>();
            Breakdown = breakdown;
        }
    }

    public sealed class DynamicRiskFrame
    {
        public readonly double TimestampSeconds;
        public readonly IReadOnlyList<DynamicRiskAssessment> Assessments;
        public readonly int ConfirmedPersonCount;
        public readonly float MaximumRisk;
        public readonly DynamicRiskLevel MaximumLevel;

        public DynamicRiskFrame(
            double timestampSeconds,
            IReadOnlyList<DynamicRiskAssessment> assessments,
            int confirmedPersonCount = -1)
        {
            TimestampSeconds = timestampSeconds;
            Assessments = assessments ?? Array.Empty<DynamicRiskAssessment>();
            ConfirmedPersonCount = confirmedPersonCount < 0
                ? Assessments.Count
                : confirmedPersonCount;

            float maximumRisk = 0f;
            DynamicRiskLevel maximumLevel = DynamicRiskLevel.Safe;
            for (int i = 0; i < Assessments.Count; i++)
            {
                DynamicRiskAssessment assessment = Assessments[i];
                if (assessment.Score >= maximumRisk)
                {
                    maximumRisk = assessment.Score;
                    maximumLevel = assessment.Level;
                }
            }

            MaximumRisk = maximumRisk;
            MaximumLevel = maximumLevel;
        }
    }

    public sealed class OverallRiskResult
    {
        public readonly bool Available;
        public readonly float AvailableWeightSum;
        public readonly float StaticRisk;
        public readonly float StateRisk;
        public readonly float DynamicRisk;
        public readonly float IntentRisk;
        public readonly float TotalRisk;

        public OverallRiskResult(
            float staticRisk,
            float stateRisk,
            float dynamicRisk,
            float intentRisk,
            float totalRisk)
            : this(
                true,
                1f,
                staticRisk,
                stateRisk,
                dynamicRisk,
                intentRisk,
                totalRisk)
        {
        }

        public OverallRiskResult(
            bool available,
            float availableWeightSum,
            float staticRisk,
            float stateRisk,
            float dynamicRisk,
            float intentRisk,
            float totalRisk)
        {
            Available = available;
            AvailableWeightSum = availableWeightSum;
            StaticRisk = staticRisk;
            StateRisk = stateRisk;
            DynamicRisk = dynamicRisk;
            IntentRisk = intentRisk;
            TotalRisk = totalRisk;
        }
    }
}
