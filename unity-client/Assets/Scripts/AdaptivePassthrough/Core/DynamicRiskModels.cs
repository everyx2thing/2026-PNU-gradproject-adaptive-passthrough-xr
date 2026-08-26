using System;
using System.Collections.Generic;
using UnityEngine;

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
        public bool hasWorldPoint;
        public Vector3 worldPoint;
        public float worldPointConfidence;

        public DynamicObjectDetection(
            string label,
            float confidence,
            NormalizedBoundingBox boundingBox,
            int classId = 0,
            bool hasWorldPoint = false,
            Vector3 worldPoint = default,
            float worldPointConfidence = 0f)
        {
            this.label = string.IsNullOrWhiteSpace(label) ? "unknown" : label;
            this.classId = classId;
            this.confidence = Math.Max(0f, Math.Min(1f, confidence));
            this.boundingBox = boundingBox;
            this.hasWorldPoint = hasWorldPoint;
            this.worldPoint = hasWorldPoint ? worldPoint : Vector3.zero;
            this.worldPointConfidence = hasWorldPoint
                ? Math.Max(0f, Math.Min(1f, worldPointConfidence))
                : 0f;
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
        public readonly double UnobservedSeconds;
        public readonly bool ReidentifiedThisFrame;
        public readonly Vector2 ViewportCenterVelocity;
        public readonly Vector2 ViewportSizeVelocity;
        public readonly int KinematicSampleCount;

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
                0,
                0.0)
        {
        }

        public TrackedDynamicObject(
            int trackId,
            DynamicObjectDetection detection,
            float areaGrowthRatePerSecond,
            TrackLifecycle lifecycle,
            bool observedThisFrame,
            int missedFrames,
            double unobservedSeconds = 0.0,
            bool reidentifiedThisFrame = false,
            Vector2 viewportCenterVelocity = default,
            Vector2 viewportSizeVelocity = default,
            int kinematicSampleCount = 0)
        {
            TrackId = trackId;
            Detection = detection;
            AreaGrowthRatePerSecond = areaGrowthRatePerSecond;
            Lifecycle = lifecycle;
            ObservedThisFrame = observedThisFrame;
            MissedFrames = Math.Max(0, missedFrames);
            UnobservedSeconds = Math.Max(0.0, unobservedSeconds);
            ReidentifiedThisFrame = reidentifiedThisFrame;
            ViewportCenterVelocity = viewportCenterVelocity;
            ViewportSizeVelocity = viewportSizeVelocity;
            KinematicSampleCount = Math.Max(0, kinematicSampleCount);
        }

        public bool IsConfirmed
        {
            get
            {
                return Lifecycle == TrackLifecycle.Confirmed
                    || Lifecycle == TrackLifecycle.Lost;
            }
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
        public readonly PersonDistanceSource DistanceSource;
        public readonly bool HasMetricMotion;
        public readonly float ClosingSpeedMetersPerSecond;
        public readonly float? MetricTtcSeconds;
        public readonly bool MetricConflict;

        public MotionEstimate(
            DynamicMotionState state,
            float scaleRatePerSecond,
            float? ttcSecondsApprox,
            float centerApproachRatePerSecond,
            int sampleCount,
            double observationSeconds,
            float reliability)
            : this(
                state,
                scaleRatePerSecond,
                ttcSecondsApprox,
                centerApproachRatePerSecond,
                sampleCount,
                observationSeconds,
                reliability,
                PersonDistanceSource.BoundingBoxProxy,
                false,
                0f,
                null)
        {
        }

        public MotionEstimate(
            DynamicMotionState state,
            float scaleRatePerSecond,
            float? ttcSecondsApprox,
            float centerApproachRatePerSecond,
            int sampleCount,
            double observationSeconds,
            float reliability,
            PersonDistanceSource distanceSource,
            bool hasMetricMotion,
            float closingSpeedMetersPerSecond,
            float? metricTtcSeconds,
            bool metricConflict = false)
        {
            State = state;
            ScaleRatePerSecond = scaleRatePerSecond;
            TtcSecondsApprox = ttcSecondsApprox;
            CenterApproachRatePerSecond = centerApproachRatePerSecond;
            SampleCount = sampleCount;
            ObservationSeconds = observationSeconds;
            Reliability = Math.Max(0f, Math.Min(1f, reliability));
            DistanceSource = distanceSource;
            HasMetricMotion = hasMetricMotion;
            ClosingSpeedMetersPerSecond = IsFinite(
                closingSpeedMetersPerSecond)
                ? closingSpeedMetersPerSecond
                : 0f;
            MetricTtcSeconds = metricTtcSeconds.HasValue
                && IsFinite(metricTtcSeconds.Value)
                && metricTtcSeconds.Value >= 0f
                    ? metricTtcSeconds
                    : null;
            MetricConflict = metricConflict;
        }

        public MotionEstimate WithMetric(
            PersonDistanceSource distanceSource,
            bool hasMetricMotion,
            float closingSpeedMetersPerSecond,
            float? metricTtcSeconds,
            DynamicMotionState state,
            float reliability,
            int sampleCount,
            double observationSeconds,
            bool metricConflict = false)
        {
            return new MotionEstimate(
                state,
                ScaleRatePerSecond,
                TtcSecondsApprox,
                CenterApproachRatePerSecond,
                sampleCount,
                observationSeconds,
                reliability,
                distanceSource,
                hasMetricMotion,
                closingSpeedMetersPerSecond,
                metricTtcSeconds,
                metricConflict);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public sealed class RelativeLocationEstimate
    {
        public readonly HorizontalZone ScreenZone;
        public readonly string UserRelativeDirection;
        public readonly DistanceBand DistanceBand;
        public readonly float BearingDegrees;
        public readonly float BoundingBoxArea;
        public readonly PersonDistanceSource DistanceSource;
        public readonly bool HasMetricDistance;
        public readonly float RawDistanceMeters;
        public readonly float FilteredDistanceMeters;
        public readonly float DistanceConfidence;
        public readonly bool HasWorldPoint;
        public readonly Vector3 WorldPoint;
        public readonly bool HasWorldVelocity;
        public readonly Vector3 WorldVelocity;
        public readonly float DepthSampleDispersionMeters;
        public readonly bool IsMetricReliable;
        public readonly string DepthRejectedReason;

        public RelativeLocationEstimate(
            HorizontalZone screenZone,
            string userRelativeDirection,
            DistanceBand distanceBand,
            float bearingDegrees,
            float boundingBoxArea)
            : this(
                screenZone,
                userRelativeDirection,
                distanceBand,
                bearingDegrees,
                boundingBoxArea,
                PersonDistanceSource.BoundingBoxProxy,
                false,
                0f,
                0f,
                0f)
        {
        }

        public RelativeLocationEstimate(
            HorizontalZone screenZone,
            string userRelativeDirection,
            DistanceBand distanceBand,
            float bearingDegrees,
            float boundingBoxArea,
            PersonDistanceSource distanceSource,
            bool hasMetricDistance,
            float rawDistanceMeters,
            float filteredDistanceMeters,
            float distanceConfidence,
            bool hasWorldPoint = false,
            Vector3 worldPoint = default,
            float depthSampleDispersionMeters = 0f,
            bool isMetricReliable = false,
            string depthRejectedReason = null,
            bool hasWorldVelocity = false,
            Vector3 worldVelocity = default)
        {
            ScreenZone = screenZone;
            UserRelativeDirection = userRelativeDirection;
            DistanceBand = distanceBand;
            BearingDegrees = bearingDegrees;
            BoundingBoxArea = boundingBoxArea;
            DistanceSource = distanceSource;
            HasMetricDistance = hasMetricDistance;
            RawDistanceMeters = NonNegativeFinite(rawDistanceMeters);
            FilteredDistanceMeters = NonNegativeFinite(
                filteredDistanceMeters);
            DistanceConfidence = Clamp01(distanceConfidence);
            HasWorldPoint = hasWorldPoint;
            WorldPoint = hasWorldPoint ? worldPoint : Vector3.zero;
            HasWorldVelocity = hasWorldPoint && hasWorldVelocity;
            WorldVelocity = HasWorldVelocity
                ? worldVelocity
                : Vector3.zero;
            DepthSampleDispersionMeters = NonNegativeFinite(
                depthSampleDispersionMeters);
            IsMetricReliable = isMetricReliable;
            DepthRejectedReason = isMetricReliable
                ? string.Empty
                : depthRejectedReason ?? string.Empty;
        }

        private static float NonNegativeFinite(float value)
        {
            return IsFinite(value) ? Math.Max(0f, value) : 0f;
        }

        private static float Clamp01(float value)
        {
            return IsFinite(value)
                ? Math.Max(0f, Math.Min(1f, value))
                : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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
        public readonly TrackLifecycle Lifecycle;
        public readonly bool ObservedThisFrame;
        public readonly float MissingSeconds;
        public readonly bool IdHandoff;
        public readonly bool ForcePassthrough;
        public readonly bool ClosePassthroughActive;
        public readonly string CloseTransitionReason;
        public readonly int CloseReleaseConfirmationCount;

        public DynamicRiskAssessment(
            int trackId,
            DynamicObjectDetection detection,
            RelativeLocationEstimate location,
            MotionEstimate motion,
            float score,
            DynamicRiskLevel level,
            string[] reasons,
            DynamicRiskBreakdown breakdown)
            : this(
                trackId,
                detection,
                location,
                motion,
                score,
                level,
                reasons,
                breakdown,
                TrackLifecycle.Confirmed,
                true)
        {
        }

        public DynamicRiskAssessment(
            int trackId,
            DynamicObjectDetection detection,
            RelativeLocationEstimate location,
            MotionEstimate motion,
            float score,
            DynamicRiskLevel level,
            string[] reasons,
            DynamicRiskBreakdown breakdown,
            TrackLifecycle lifecycle,
            bool observedThisFrame,
            float missingSeconds = 0f,
            bool idHandoff = false,
            bool forcePassthrough = false,
            bool closePassthroughActive = false,
            string closeTransitionReason = null,
            int closeReleaseConfirmationCount = 0)
        {
            TrackId = trackId;
            Detection = detection;
            Location = location;
            Motion = motion;
            Score = score;
            Level = level;
            Reasons = reasons ?? Array.Empty<string>();
            Breakdown = breakdown;
            Lifecycle = lifecycle;
            ObservedThisFrame = observedThisFrame;
            MissingSeconds = Math.Max(0f, missingSeconds);
            IdHandoff = idHandoff;
            ForcePassthrough = forcePassthrough;
            ClosePassthroughActive = closePassthroughActive;
            CloseTransitionReason = closeTransitionReason ?? string.Empty;
            CloseReleaseConfirmationCount = Math.Max(
                0,
                closeReleaseConfirmationCount);
        }
    }

    public sealed class DynamicRiskFrame
    {
        public readonly double TimestampSeconds;
        public readonly IReadOnlyList<DynamicRiskAssessment> Assessments;
        public readonly int ConfirmedPersonCount;
        public readonly float MaximumRisk;
        public readonly DynamicRiskLevel MaximumLevel;
        public readonly bool ForcePassthrough;
        public readonly float PolicyRisk;
        public readonly IReadOnlyList<int> LiveTrackIds;

        public DynamicRiskFrame(
            double timestampSeconds,
            IReadOnlyList<DynamicRiskAssessment> assessments,
            int confirmedPersonCount = -1,
            IReadOnlyList<int> liveTrackIds = null)
        {
            TimestampSeconds = timestampSeconds;
            Assessments = assessments ?? Array.Empty<DynamicRiskAssessment>();
            ConfirmedPersonCount = confirmedPersonCount < 0
                ? Assessments.Count
                : confirmedPersonCount;
            LiveTrackIds = liveTrackIds ?? Array.Empty<int>();

            float maximumRisk = 0f;
            DynamicRiskLevel maximumLevel = DynamicRiskLevel.Safe;
            bool forcePassthrough = false;
            for (int i = 0; i < Assessments.Count; i++)
            {
                DynamicRiskAssessment assessment = Assessments[i];
                forcePassthrough |= assessment.ForcePassthrough;
                if (assessment.Score >= maximumRisk)
                {
                    maximumRisk = assessment.Score;
                    maximumLevel = assessment.Level;
                }
            }

            MaximumRisk = maximumRisk;
            MaximumLevel = maximumLevel;
            ForcePassthrough = forcePassthrough;
            PolicyRisk = forcePassthrough ? 1f : maximumRisk;
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
