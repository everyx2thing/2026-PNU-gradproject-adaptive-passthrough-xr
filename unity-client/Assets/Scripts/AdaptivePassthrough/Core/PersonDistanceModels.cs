using System;
using System.Collections.Generic;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    public readonly struct PersonObservation
    {
        public readonly DynamicObjectDetection Detection;
        public readonly Pose CapturePose;
        public readonly double CaptureTimestampSeconds;
        public readonly float RawClusterDistanceMeters;
        public readonly float ClusterDispersionMeters;
        public readonly int ClusterSampleCount;
        public readonly bool HasWorldPoint;
        public readonly Vector3 WorldPoint;

        public PersonObservation(
            DynamicObjectDetection detection,
            Pose capturePose,
            double captureTimestampSeconds,
            float rawClusterDistanceMeters,
            float clusterDispersionMeters,
            int clusterSampleCount,
            bool hasWorldPoint = false,
            Vector3 worldPoint = default)
        {
            Detection = detection;
            CapturePose = capturePose;
            CaptureTimestampSeconds = Math.Max(0.0, captureTimestampSeconds);
            RawClusterDistanceMeters = Mathf.Max(
                0f,
                rawClusterDistanceMeters);
            ClusterDispersionMeters = Mathf.Max(0f, clusterDispersionMeters);
            ClusterSampleCount = Mathf.Max(0, clusterSampleCount);
            HasWorldPoint = hasWorldPoint;
            WorldPoint = hasWorldPoint ? worldPoint : Vector3.zero;
        }
    }

    public enum PersonDistanceSource
    {
        Unavailable,
        EnvironmentDepth,
        BoundingBoxProxy
    }

    public enum PersonDepthClusterKind
    {
        Unavailable,
        Tracking,
        Safety
    }

    public readonly struct PersonDepthClusterMeasurement
    {
        public readonly PersonDepthClusterKind Kind;
        public readonly int ClusterId;
        public readonly bool Available;
        public readonly float DistanceMeters;
        public readonly float Confidence;
        public readonly int SupportCount;
        public readonly float SpanMeters;
        public readonly ulong SelectionMask;
        public readonly bool HasWorldCenter;
        public readonly Vector3 WorldCenter;
        public readonly float TemporalConsistency;
        public readonly string SelectionReason;

        public PersonDepthClusterMeasurement(
            PersonDepthClusterKind kind,
            int clusterId,
            bool available,
            float distanceMeters,
            float confidence,
            int supportCount,
            float spanMeters,
            ulong selectionMask,
            bool hasWorldCenter = false,
            Vector3 worldCenter = default,
            float temporalConsistency = 0f,
            string selectionReason = null)
        {
            Kind = kind;
            ClusterId = Math.Max(0, clusterId);
            Available = available;
            DistanceMeters = available && IsFinite(distanceMeters)
                ? Mathf.Max(0f, distanceMeters)
                : 0f;
            Confidence = available && IsFinite(confidence)
                ? Mathf.Clamp01(confidence)
                : 0f;
            SupportCount = available ? Math.Max(0, supportCount) : 0;
            SpanMeters = available && IsFinite(spanMeters)
                ? Mathf.Max(0f, spanMeters)
                : 0f;
            SelectionMask = available ? selectionMask : 0UL;
            HasWorldCenter = available
                && hasWorldCenter
                && IsFinite(worldCenter);
            WorldCenter = HasWorldCenter ? worldCenter : Vector3.zero;
            TemporalConsistency = available
                ? Mathf.Clamp01(temporalConsistency)
                : 0f;
            SelectionReason = selectionReason ?? string.Empty;
        }

        public static PersonDepthClusterMeasurement Unavailable(
            PersonDepthClusterKind kind,
            string reason)
        {
            return new PersonDepthClusterMeasurement(
                kind,
                0,
                false,
                0f,
                0f,
                0,
                0f,
                0UL,
                false,
                Vector3.zero,
                0f,
                reason);
        }

        public PersonDepthClusterMeasurement WithWorldCenter(Vector3 center)
        {
            return new PersonDepthClusterMeasurement(
                Kind,
                ClusterId,
                Available,
                DistanceMeters,
                Confidence,
                SupportCount,
                SpanMeters,
                SelectionMask,
                true,
                center,
                TemporalConsistency,
                SelectionReason);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x)
                && IsFinite(value.y)
                && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// A depth sample keeps its position inside the detected person box so
    /// cluster selection can distinguish body-supported surfaces from a wall
    /// or furniture that merely occupies more pixels.
    /// </summary>
    public readonly struct PersonDepthSample
    {
        public readonly int SampleIndex;
        public readonly Vector2 BoxRelativePosition;
        public readonly Vector2 CameraViewportPosition;
        public readonly float DistanceMeters;
        public readonly float Weight;
        public readonly bool IsTorsoCore;
        public readonly bool IsUpperBody;
        public readonly bool HasWorldPoint;
        public readonly Vector3 WorldPoint;

        public PersonDepthSample(
            float distanceMeters,
            float weight = 1f,
            bool isTorsoCore = false,
            bool isUpperBody = true)
            : this(
                -1,
                Vector2.zero,
                Vector2.zero,
                distanceMeters,
                weight,
                isTorsoCore,
                isUpperBody,
                false,
                Vector3.zero)
        {
        }

        public PersonDepthSample(
            int sampleIndex,
            Vector2 boxRelativePosition,
            Vector2 cameraViewportPosition,
            float distanceMeters,
            float weight,
            bool isTorsoCore,
            bool isUpperBody,
            bool hasWorldPoint,
            Vector3 worldPoint)
        {
            SampleIndex = sampleIndex;
            BoxRelativePosition = boxRelativePosition;
            CameraViewportPosition = cameraViewportPosition;
            DistanceMeters = distanceMeters;
            Weight = Mathf.Max(0.05f, weight);
            IsTorsoCore = isTorsoCore;
            IsUpperBody = isUpperBody;
            HasWorldPoint = hasWorldPoint && IsFinite(worldPoint);
            WorldPoint = HasWorldPoint ? worldPoint : Vector3.zero;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x)
                && IsFinite(value.y)
                && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public enum PersonDepthSampleDecision
    {
        NotRequested = 0,
        NoHit = 1,
        InvalidDistance = 2,
        OtherCluster = 3,
        SelectedTracking = 4,
        SelectedSafety = 5
    }

    [Serializable]
    public readonly struct PersonDepthSampleDiagnostic
    {
        public readonly int SampleIndex;
        public readonly Vector2 BoxRelativePosition;
        public readonly Vector2 CameraViewportPosition;
        public readonly bool HasHit;
        public readonly float DistanceMeters;
        public readonly bool HasWorldPoint;
        public readonly Vector3 WorldPoint;
        public readonly bool IsTorsoCore;
        public readonly bool IsUpperBody;
        public readonly PersonDepthSampleDecision Decision;

        public PersonDepthSampleDiagnostic(
            int sampleIndex,
            Vector2 boxRelativePosition,
            Vector2 cameraViewportPosition,
            bool hasHit,
            float distanceMeters,
            bool hasWorldPoint,
            Vector3 worldPoint,
            bool isTorsoCore,
            bool isUpperBody,
            PersonDepthSampleDecision decision)
        {
            SampleIndex = Math.Max(0, sampleIndex);
            BoxRelativePosition = boxRelativePosition;
            CameraViewportPosition = cameraViewportPosition;
            HasHit = hasHit;
            DistanceMeters = IsFinite(distanceMeters)
                ? Mathf.Max(0f, distanceMeters)
                : 0f;
            HasWorldPoint = hasWorldPoint && IsFinite(worldPoint);
            WorldPoint = HasWorldPoint ? worldPoint : Vector3.zero;
            IsTorsoCore = isTorsoCore;
            IsUpperBody = isUpperBody;
            Decision = decision;
        }

        public PersonDepthSampleDiagnostic WithDecision(
            PersonDepthSampleDecision decision)
        {
            return new PersonDepthSampleDiagnostic(
                SampleIndex,
                BoxRelativePosition,
                CameraViewportPosition,
                HasHit,
                DistanceMeters,
                HasWorldPoint,
                WorldPoint,
                IsTorsoCore,
                IsUpperBody,
                decision);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x)
                && IsFinite(value.y)
                && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public sealed class PersonDepthSamplingSnapshot
    {
        public readonly int TrackId;
        public readonly double TimestampSeconds;
        public readonly bool ExpandedPattern;
        public readonly int RequestedSampleCount;
        public readonly int HitSampleCount;
        public readonly ulong TrackingSelectionMask;
        public readonly ulong SafetySelectionMask;
        public readonly PersonDepthSampleDiagnostic[] Samples;
        public readonly bool HasTrackingWorldCenter;
        public readonly Vector3 TrackingWorldCenter;
        public readonly string SelectionReason;
        public readonly PersonDepthClusterMeasurement TrackingCluster;
        public readonly PersonDepthClusterMeasurement SafetyCluster;

        public PersonDepthSamplingSnapshot(
            int trackId,
            double timestampSeconds,
            bool expandedPattern,
            int requestedSampleCount,
            int hitSampleCount,
            ulong trackingSelectionMask,
            ulong safetySelectionMask,
            PersonDepthSampleDiagnostic[] samples,
            bool hasTrackingWorldCenter,
            Vector3 trackingWorldCenter,
            string selectionReason,
            PersonDepthClusterMeasurement trackingCluster = default,
            PersonDepthClusterMeasurement safetyCluster = default)
        {
            TrackId = Math.Max(0, trackId);
            TimestampSeconds = Math.Max(0.0, timestampSeconds);
            ExpandedPattern = expandedPattern;
            RequestedSampleCount = Math.Max(0, requestedSampleCount);
            HitSampleCount = Math.Max(0, hitSampleCount);
            TrackingSelectionMask = trackingSelectionMask;
            SafetySelectionMask = safetySelectionMask;
            Samples = samples ?? Array.Empty<PersonDepthSampleDiagnostic>();
            HasTrackingWorldCenter = hasTrackingWorldCenter;
            TrackingWorldCenter = hasTrackingWorldCenter
                ? trackingWorldCenter
                : Vector3.zero;
            SelectionReason = selectionReason ?? string.Empty;
            TrackingCluster = trackingCluster;
            SafetyCluster = safetyCluster;
        }

        public static PersonDepthSamplingSnapshot Empty { get; } =
            new PersonDepthSamplingSnapshot(
                0,
                0.0,
                false,
                0,
                0,
                0UL,
                0UL,
                Array.Empty<PersonDepthSampleDiagnostic>(),
                false,
                Vector3.zero,
                string.Empty,
                PersonDepthClusterMeasurement.Unavailable(
                    PersonDepthClusterKind.Tracking,
                    "no_samples"),
                PersonDepthClusterMeasurement.Unavailable(
                    PersonDepthClusterKind.Safety,
                    "no_samples"));
    }

    public static class PersonImageCoordinates
    {
        public static Vector2 BoxRelativeToCameraViewport(
            NormalizedBoundingBox box,
            Vector2 relativePoint)
        {
            float x = box.Left + box.width * Mathf.Clamp01(relativePoint.x);
            float topDownY = box.Top
                + box.height * Mathf.Clamp01(relativePoint.y);
            return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(1f - topDownY));
        }
    }

    public static class PersonDepthWorldCenter
    {
        public static bool TryCalculate(
            IReadOnlyList<PersonDepthSample> samples,
            ulong trackingMask,
            out Vector3 center)
        {
            center = Vector3.zero;
            if (samples == null || trackingMask == 0UL)
            {
                return false;
            }

            float totalWeight = 0f;
            int supportCount = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                PersonDepthSample sample = samples[i];
                if (!IsSelectedTrackingSample(sample, trackingMask))
                {
                    continue;
                }

                float weight = Mathf.Max(0.05f, sample.Weight);
                center += sample.WorldPoint * weight;
                totalWeight += weight;
                supportCount++;
            }

            if (supportCount < 2 || totalWeight <= 0.0001f)
            {
                center = Vector3.zero;
                return false;
            }

            center /= totalWeight;
            for (int iteration = 0; iteration < 5; iteration++)
            {
                Vector3 numerator = Vector3.zero;
                float denominator = 0f;
                for (int i = 0; i < samples.Count; i++)
                {
                    PersonDepthSample sample = samples[i];
                    if (!IsSelectedTrackingSample(sample, trackingMask))
                    {
                        continue;
                    }

                    float inverseDistance = 1f / Mathf.Max(
                        0.01f,
                        Vector3.Distance(center, sample.WorldPoint));
                    float weight = Mathf.Max(0.05f, sample.Weight)
                        * inverseDistance;
                    numerator += sample.WorldPoint * weight;
                    denominator += weight;
                }

                if (denominator > 0.0001f)
                {
                    center = numerator / denominator;
                }
            }

            return IsFinite(center);
        }

        private static bool IsSelectedTrackingSample(
            PersonDepthSample sample,
            ulong trackingMask)
        {
            return sample.SampleIndex >= 0
                && sample.SampleIndex < 64
                && (trackingMask & (1UL << sample.SampleIndex)) != 0UL
                && sample.HasWorldPoint;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x)
                && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y)
                && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z)
                && !float.IsInfinity(value.z);
        }
    }

    public enum PersonPresentationGeometrySource
    {
        Unavailable,
        MetricDepth,
        TrackHistory,
        BoundingBoxEstimate
    }

    public enum PersonPresentationStatus
    {
        FeatureDisabled,
        NoFrame,
        PolicyOff,
        NoQualifiedPerson,
        GeometryUnavailable,
        RevealAreaLimited,
        SlotUnavailable,
        Rendered
    }

    public interface IPersonPresentationDiagnosticsProvider
    {
        int ActivePersonWindowCount { get; }
        int LatestQualifiedPersonCount { get; }
        int LatestGeometryReadyPersonCount { get; }
        PersonPresentationGeometrySource LatestPersonGeometrySource { get; }
        PersonPresentationStatus LatestPersonPresentationStatus { get; }
    }

    public static class PersonPresentationDiagnostics
    {
        public static bool IsRevealEligible(
            DynamicRiskAssessment assessment,
            bool featureEnabled,
            bool policyEnabled,
            float minimumRisk)
        {
            return assessment != null
                && assessment.ObservedThisFrame
                && featureEnabled
                && (assessment.ForcePassthrough
                    || assessment.Score >= Mathf.Clamp01(minimumRisk));
        }

        public static PersonPresentationStatus ResolveStatus(
            bool featureEnabled,
            bool hasFrame,
            bool policyEnabled,
            int renderedWindowCount,
            int qualifiedPersonCount,
            int geometryReadyPersonCount,
            bool revealAreaLimited,
            bool slotUnavailable)
        {
            if (renderedWindowCount > 0)
            {
                return PersonPresentationStatus.Rendered;
            }

            if (!featureEnabled)
            {
                return PersonPresentationStatus.FeatureDisabled;
            }

            if (!hasFrame)
            {
                return PersonPresentationStatus.NoFrame;
            }

            if (!policyEnabled)
            {
                return PersonPresentationStatus.PolicyOff;
            }

            if (qualifiedPersonCount <= 0)
            {
                return PersonPresentationStatus.NoQualifiedPerson;
            }

            if (geometryReadyPersonCount <= 0)
            {
                return PersonPresentationStatus.GeometryUnavailable;
            }

            if (revealAreaLimited)
            {
                return PersonPresentationStatus.RevealAreaLimited;
            }

            return slotUnavailable
                ? PersonPresentationStatus.SlotUnavailable
                : PersonPresentationStatus.GeometryUnavailable;
        }
    }

    public sealed class PersonDistanceMeasurement
    {
        public readonly int TrackId;
        public readonly double TimestampSeconds;
        public readonly PersonDistanceSource Source;
        public readonly bool Available;
        public readonly float RawDistanceMeters;
        public readonly float FilteredDistanceMeters;
        public readonly float Confidence;
        public readonly int RequestedSampleCount;
        public readonly int ValidSampleCount;
        public readonly float SourceAgeSeconds;
        public readonly float BoundingBoxArea;
        public readonly float SampleDispersionMeters;
        public readonly bool BoundingBoxDepthConflict;
        public readonly bool HasWorldPoint;
        public readonly Vector3 WorldPoint;
        public readonly bool HasWorldVelocity;
        public readonly Vector3 WorldVelocity;
        public readonly string FailureReason;
        public readonly bool IsMetricReliable;
        public readonly string DepthRejectedReason;
        public readonly bool HasPresentationGeometry;
        public readonly HazardPresentationGeometry PresentationGeometry;
        public readonly PersonPresentationGeometrySource
            PresentationGeometrySource;
        public readonly float PresentationDistanceMeters;
        public readonly float RawSafetyDistanceMeters;
        public readonly float SafetyDistanceMeters;
        public readonly float SafetyConfidence;
        public readonly int TorsoSupportCount;
        public readonly int SafetySupportCount;
        public readonly string ClusterSelectionReason;
        public readonly PersonDepthClusterMeasurement TrackingCluster;
        public readonly PersonDepthClusterMeasurement SafetyCluster;

        public PersonDistanceMeasurement(
            int trackId,
            double timestampSeconds,
            PersonDistanceSource source,
            bool available,
            float rawDistanceMeters,
            float filteredDistanceMeters,
            float confidence,
            int requestedSampleCount,
            int validSampleCount,
            float sourceAgeSeconds,
            float boundingBoxArea,
            string failureReason = null,
            float sampleDispersionMeters = 0f,
            bool boundingBoxDepthConflict = false,
            bool hasWorldPoint = false,
            Vector3 worldPoint = default,
            bool? isMetricReliable = null,
            string depthRejectedReason = null,
            bool hasWorldVelocity = false,
            Vector3 worldVelocity = default,
            HazardPresentationGeometry presentationGeometry = default,
            PersonPresentationGeometrySource presentationGeometrySource =
                PersonPresentationGeometrySource.Unavailable,
            float presentationDistanceMeters = 0f,
            float rawSafetyDistanceMeters = -1f,
            float safetyDistanceMeters = -1f,
            float safetyConfidence = -1f,
            int torsoSupportCount = 0,
            int safetySupportCount = 0,
            string clusterSelectionReason = null,
            PersonDepthClusterMeasurement trackingCluster = default,
            PersonDepthClusterMeasurement safetyCluster = default)
        {
            TrackId = Math.Max(0, trackId);
            TimestampSeconds = Math.Max(0.0, timestampSeconds);
            Source = source;
            Available = available;
            RawDistanceMeters = NonNegativeFinite(rawDistanceMeters);
            FilteredDistanceMeters = NonNegativeFinite(filteredDistanceMeters);
            Confidence = Clamp01(confidence);
            RequestedSampleCount = Math.Max(0, requestedSampleCount);
            ValidSampleCount = Math.Max(0, validSampleCount);
            SourceAgeSeconds = NonNegativeFinite(sourceAgeSeconds);
            BoundingBoxArea = Clamp01(boundingBoxArea);
            SampleDispersionMeters = NonNegativeFinite(
                sampleDispersionMeters);
            BoundingBoxDepthConflict = boundingBoxDepthConflict;
            FailureReason = failureReason ?? string.Empty;
            bool defaultReliable = Available
                && Source == PersonDistanceSource.EnvironmentDepth
                && FilteredDistanceMeters >= 0.20f
                && Confidence >= 0.45f
                && ValidSampleCount >= 3
                && SampleDispersionMeters <= 0.35f
                && !BoundingBoxDepthConflict;
            IsMetricReliable = isMetricReliable ?? defaultReliable;
            DepthRejectedReason = IsMetricReliable
                ? string.Empty
                : depthRejectedReason ?? string.Empty;
            HasWorldPoint = hasWorldPoint && IsMetricReliable;
            WorldPoint = HasWorldPoint ? worldPoint : Vector3.zero;
            HasWorldVelocity = HasWorldPoint && hasWorldVelocity;
            WorldVelocity = HasWorldVelocity
                ? worldVelocity
                : Vector3.zero;
            HasPresentationGeometry = presentationGeometry.Available;
            PresentationGeometry = HasPresentationGeometry
                ? presentationGeometry
                : default;
            PresentationGeometrySource = HasPresentationGeometry
                ? presentationGeometrySource
                : PersonPresentationGeometrySource.Unavailable;
            PresentationDistanceMeters = HasPresentationGeometry
                ? NonNegativeFinite(presentationDistanceMeters)
                : 0f;
            RawSafetyDistanceMeters = IsFinite(rawSafetyDistanceMeters)
                && rawSafetyDistanceMeters >= 0f
                    ? rawSafetyDistanceMeters
                    : RawDistanceMeters;
            SafetyDistanceMeters = IsFinite(safetyDistanceMeters)
                && safetyDistanceMeters >= 0f
                    ? safetyDistanceMeters
                    : FilteredDistanceMeters;
            SafetyConfidence = IsFinite(safetyConfidence)
                && safetyConfidence >= 0f
                    ? Clamp01(safetyConfidence)
                    : Confidence;
            TorsoSupportCount = Math.Max(0, torsoSupportCount);
            SafetySupportCount = Math.Max(0, safetySupportCount);
            ClusterSelectionReason = clusterSelectionReason ?? string.Empty;
            TrackingCluster = trackingCluster.Available
                ? trackingCluster
                : PersonDepthClusterMeasurement.Unavailable(
                    PersonDepthClusterKind.Tracking,
                    ClusterSelectionReason);
            SafetyCluster = safetyCluster.Available
                ? safetyCluster
                : PersonDepthClusterMeasurement.Unavailable(
                    PersonDepthClusterKind.Safety,
                    ClusterSelectionReason);
        }

        public float TrackingRawDistanceMeters
        {
            get { return RawDistanceMeters; }
        }

        public float TrackingDistanceMeters
        {
            get { return FilteredDistanceMeters; }
        }

        public bool HasMetricDistance
        {
            get
            {
                return Available
                    && Source == PersonDistanceSource.EnvironmentDepth
                    && FilteredDistanceMeters >= 0.20f;
            }
        }

        public bool HasReliableMetricDistance
        {
            get { return HasMetricDistance && IsMetricReliable; }
        }

        public bool HasReliableSafetyDistance
        {
            get
            {
                return Available
                    && Source == PersonDistanceSource.EnvironmentDepth
                    && SafetyDistanceMeters >= 0.20f
                    && SafetyConfidence >= 0.55f
                    && SafetySupportCount >= 2;
            }
        }

        public PersonDistanceMeasurement WithWorldPoint(Vector3 worldPoint)
        {
            return WithWorldPoint(worldPoint, Vector3.zero, false);
        }

        public PersonDistanceMeasurement WithWorldPoint(
            Vector3 worldPoint,
            Vector3 worldVelocity)
        {
            return WithWorldPoint(worldPoint, worldVelocity, true);
        }

        public PersonDistanceMeasurement WithWorldPoint(
            Vector3 worldPoint,
            Vector3 worldVelocity,
            bool hasWorldVelocity)
        {
            return new PersonDistanceMeasurement(
                TrackId,
                TimestampSeconds,
                Source,
                Available,
                RawDistanceMeters,
                FilteredDistanceMeters,
                Confidence,
                RequestedSampleCount,
                ValidSampleCount,
                SourceAgeSeconds,
                BoundingBoxArea,
                FailureReason,
                SampleDispersionMeters,
                BoundingBoxDepthConflict,
                true,
                worldPoint,
                IsMetricReliable,
                DepthRejectedReason,
                hasWorldVelocity,
                worldVelocity,
                PresentationGeometry,
                PresentationGeometrySource,
                PresentationDistanceMeters,
                RawSafetyDistanceMeters,
                SafetyDistanceMeters,
                SafetyConfidence,
                TorsoSupportCount,
                SafetySupportCount,
                ClusterSelectionReason,
                TrackingCluster,
                SafetyCluster);
        }

        public PersonDistanceMeasurement WithPresentationGeometry(
            HazardPresentationGeometry geometry,
            PersonPresentationGeometrySource geometrySource =
                PersonPresentationGeometrySource.MetricDepth,
            float presentationDistanceMeters = 0f)
        {
            return new PersonDistanceMeasurement(
                TrackId,
                TimestampSeconds,
                Source,
                Available,
                RawDistanceMeters,
                FilteredDistanceMeters,
                Confidence,
                RequestedSampleCount,
                ValidSampleCount,
                SourceAgeSeconds,
                BoundingBoxArea,
                FailureReason,
                SampleDispersionMeters,
                BoundingBoxDepthConflict,
                HasWorldPoint,
                WorldPoint,
                IsMetricReliable,
                DepthRejectedReason,
                HasWorldVelocity,
                WorldVelocity,
                geometry,
                geometrySource,
                presentationDistanceMeters,
                RawSafetyDistanceMeters,
                SafetyDistanceMeters,
                SafetyConfidence,
                TorsoSupportCount,
                SafetySupportCount,
                ClusterSelectionReason,
                TrackingCluster,
                SafetyCluster);
        }

        public PersonDistanceMeasurement WithDepthClusters(
            PersonDepthClusterMeasurement trackingCluster,
            PersonDepthClusterMeasurement safetyCluster)
        {
            return new PersonDistanceMeasurement(
                TrackId,
                TimestampSeconds,
                Source,
                Available,
                RawDistanceMeters,
                FilteredDistanceMeters,
                Confidence,
                RequestedSampleCount,
                ValidSampleCount,
                SourceAgeSeconds,
                BoundingBoxArea,
                FailureReason,
                SampleDispersionMeters,
                BoundingBoxDepthConflict,
                HasWorldPoint,
                WorldPoint,
                IsMetricReliable,
                DepthRejectedReason,
                HasWorldVelocity,
                WorldVelocity,
                PresentationGeometry,
                PresentationGeometrySource,
                PresentationDistanceMeters,
                RawSafetyDistanceMeters,
                SafetyDistanceMeters,
                SafetyConfidence,
                TorsoSupportCount,
                SafetySupportCount,
                ClusterSelectionReason,
                trackingCluster,
                safetyCluster);
        }

        public PersonDistanceMeasurement WithMetricReliability(
            bool reliable,
            string rejectedReason = null)
        {
            return new PersonDistanceMeasurement(
                TrackId,
                TimestampSeconds,
                Source,
                Available,
                RawDistanceMeters,
                FilteredDistanceMeters,
                Confidence,
                RequestedSampleCount,
                ValidSampleCount,
                SourceAgeSeconds,
                BoundingBoxArea,
                FailureReason,
                SampleDispersionMeters,
                BoundingBoxDepthConflict,
                reliable && HasWorldPoint,
                WorldPoint,
                reliable,
                reliable ? string.Empty : rejectedReason,
                reliable && HasWorldVelocity,
                WorldVelocity,
                reliable
                    || PresentationGeometrySource
                        != PersonPresentationGeometrySource.MetricDepth
                    ? PresentationGeometry
                    : default,
                reliable
                    || PresentationGeometrySource
                        != PersonPresentationGeometrySource.MetricDepth
                    ? PresentationGeometrySource
                    : PersonPresentationGeometrySource.Unavailable,
                reliable
                    || PresentationGeometrySource
                        != PersonPresentationGeometrySource.MetricDepth
                    ? PresentationDistanceMeters
                    : 0f,
                RawSafetyDistanceMeters,
                SafetyDistanceMeters,
                SafetyConfidence,
                TorsoSupportCount,
                SafetySupportCount,
                ClusterSelectionReason,
                TrackingCluster,
                SafetyCluster);
        }

        public static PersonDistanceMeasurement BoundingBoxFallback(
            int trackId,
            double timestampSeconds,
            float boundingBoxArea,
            string reason = "depth_unavailable")
        {
            return new PersonDistanceMeasurement(
                trackId,
                timestampSeconds,
                PersonDistanceSource.BoundingBoxProxy,
                true,
                0f,
                0f,
                0f,
                0,
                0,
                0f,
                boundingBoxArea,
                reason);
        }

        public static PersonDistanceMeasurement Unavailable(
            int trackId,
            double timestampSeconds,
            float boundingBoxArea,
            string reason)
        {
            return new PersonDistanceMeasurement(
                trackId,
                timestampSeconds,
                PersonDistanceSource.Unavailable,
                false,
                0f,
                0f,
                0f,
                0,
                0,
                0f,
                boundingBoxArea,
                reason);
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

    public static class PersonDepthReliability
    {
        public const float MinimumConfidence = 0.45f;
        public const int MinimumSamples = 3;
        public const float MaximumDispersionMeters = 0.35f;
        public const float BackgroundDistanceMeters = 2.0f;
        public const float LargeBoxHeight = 0.75f;
        public const float LargeBoxArea = 0.35f;

        public static bool IsBackgroundSuspected(
            NormalizedBoundingBox box,
            float distanceMeters)
        {
            return distanceMeters > BackgroundDistanceMeters
                && IsLargeBox(box);
        }

        public static bool IsLargeBox(NormalizedBoundingBox box)
        {
            return box.height >= LargeBoxHeight
                || box.Area >= LargeBoxArea;
        }

        public static bool IsReliable(
            PersonDistanceMeasurement measurement,
            NormalizedBoundingBox box,
            out string rejectedReason)
        {
            if (measurement == null || !measurement.HasMetricDistance)
            {
                rejectedReason = measurement == null
                    ? "depth_unavailable"
                    : measurement.FailureReason;
                return false;
            }

            if (measurement.BoundingBoxDepthConflict)
            {
                rejectedReason = "bbox_depth_conflict";
                return false;
            }

            if (measurement.Confidence < MinimumConfidence)
            {
                rejectedReason = "low_depth_confidence";
                return false;
            }

            if (measurement.ValidSampleCount < MinimumSamples)
            {
                rejectedReason = "insufficient_depth_samples";
                return false;
            }

            if (measurement.SampleDispersionMeters
                > MaximumDispersionMeters)
            {
                rejectedReason = "depth_dispersion";
                return false;
            }

            rejectedReason = string.Empty;
            return true;
        }
    }
}
