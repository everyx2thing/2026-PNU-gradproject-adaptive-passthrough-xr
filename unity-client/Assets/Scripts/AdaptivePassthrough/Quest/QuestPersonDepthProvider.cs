#if ADAPTIVE_PASSTHROUGH_QUEST_CAMERA
using System;
using System.Collections.Generic;
using Meta.XR;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    [DefaultExecutionOrder(-20)]
    [DisallowMultipleComponent]
    public sealed class QuestPersonDepthProvider : MonoBehaviour
    {
        private const float MinimumDepthDistanceMeters = 0.20f;
        private sealed class WorldTrackState
        {
            public bool Initialized;
            public Vector3 Position;
            public Vector3 Velocity;
            public double TimestampSeconds;
        }
        private readonly struct ObservationDepthSample
        {
            public readonly NormalizedBoundingBox Box;
            public readonly float LeftDistance;
            public readonly float CenterDistance;
            public readonly float RightDistance;

            public ObservationDepthSample(
                NormalizedBoundingBox box,
                float leftDistance,
                float centerDistance,
                float rightDistance)
            {
                Box = box;
                LeftDistance = leftDistance;
                CenterDistance = centerDistance;
                RightDistance = rightDistance;
            }
        }
        private static readonly Vector2[] DefaultSamplePoints =
        {
            new Vector2(0.50f, 0.22f),
            new Vector2(0.38f, 0.34f),
            new Vector2(0.50f, 0.34f),
            new Vector2(0.62f, 0.34f),
            new Vector2(0.32f, 0.48f),
            new Vector2(0.42f, 0.48f),
            new Vector2(0.50f, 0.48f),
            new Vector2(0.58f, 0.48f),
            new Vector2(0.68f, 0.48f),
            new Vector2(0.38f, 0.64f),
            new Vector2(0.50f, 0.64f),
            new Vector2(0.62f, 0.64f),
            new Vector2(0.50f, 0.78f)
        };

        private static readonly float[] DefaultSampleWeights =
        {
            1.2f,
            1.4f, 2.0f, 1.4f,
            1.0f, 1.8f, 2.4f, 1.8f, 1.0f,
            1.2f, 1.8f, 1.2f,
            1.0f
        };

        private static readonly Vector2[] ExpandedSamplePoints =
        {
            new Vector2(0.30f, 0.20f), new Vector2(0.40f, 0.20f),
            new Vector2(0.50f, 0.20f), new Vector2(0.60f, 0.20f),
            new Vector2(0.70f, 0.20f), new Vector2(0.30f, 0.35f),
            new Vector2(0.40f, 0.35f), new Vector2(0.50f, 0.35f),
            new Vector2(0.60f, 0.35f), new Vector2(0.70f, 0.35f),
            new Vector2(0.30f, 0.50f), new Vector2(0.40f, 0.50f),
            new Vector2(0.50f, 0.50f), new Vector2(0.60f, 0.50f),
            new Vector2(0.70f, 0.50f), new Vector2(0.30f, 0.65f),
            new Vector2(0.40f, 0.65f), new Vector2(0.50f, 0.65f),
            new Vector2(0.60f, 0.65f), new Vector2(0.70f, 0.65f),
            new Vector2(0.30f, 0.80f), new Vector2(0.40f, 0.80f),
            new Vector2(0.50f, 0.80f), new Vector2(0.60f, 0.80f),
            new Vector2(0.70f, 0.80f)
        };

        private static readonly Vector2[] ObservationWorldSamplePoints =
        {
            new Vector2(0.42f, 0.48f),
            new Vector2(0.50f, 0.48f),
            new Vector2(0.58f, 0.48f)
        };

        [SerializeField] private PassthroughCameraAccess cameraAccess;
        [SerializeField] private EnvironmentRaycastManager raycastManager;
        [SerializeField] private TrackingQualityController trackingQuality;
        [SerializeField, Min(0.2f)] private float maximumDistanceMeters = 6f;
        [SerializeField, Min(0.05f)] private float maximumCaptureAgeSeconds = 0.40f;

        private readonly List<PersonDepthSample> personDepthSamples =
            new List<PersonDepthSample>(ExpandedSamplePoints.Length);
        private readonly HashSet<int> expandNextFrame = new HashSet<int>();
        private readonly Dictionary<int, WorldTrackState> worldTrackStates =
            new Dictionary<int, WorldTrackState>();
        private readonly Dictionary<int, PersonDepthSamplingSnapshot>
            samplingSnapshots =
                new Dictionary<int, PersonDepthSamplingSnapshot>();
        private readonly HashSet<int> liveTrackScratch = new HashSet<int>();
        private readonly List<int> expiredWorldTracks = new List<int>();
        private readonly List<ObservationDepthSample> observationDepthCache =
            new List<ObservationDepthSample>(10);
        private readonly PersonDistanceFilter distanceFilter =
            new PersonDistanceFilter();
        private Pose cameraPoseAtCapture;
        private double frameTimestampSeconds;
        private bool hasFrameContext;

        public bool IsDepthSupported
        {
            get { return EnvironmentRaycastManager.IsSupported; }
        }

        public bool IsDepthReady { get; private set; }

        public string LastFailureReason { get; private set; }

        public PersonPresentationGeometrySource LastPresentationGeometrySource
        {
            get;
            private set;
        }

        public PassthroughCameraAccess CameraAccess
        {
            get { return cameraAccess; }
        }

        public PersonDepthSamplingSnapshot LatestSamplingSnapshot
        {
            get;
            private set;
        } = PersonDepthSamplingSnapshot.Empty;

        public bool TryGetSamplingSnapshot(
            int trackId,
            out PersonDepthSamplingSnapshot snapshot)
        {
            return samplingSnapshots.TryGetValue(trackId, out snapshot);
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDisable()
        {
            ResetProvider();
        }

        public void Configure(
            PassthroughCameraAccess camera,
            EnvironmentRaycastManager environmentRaycastManager)
        {
            cameraAccess = camera;
            raycastManager = environmentRaycastManager;
            ResolveReferences();
        }

        public void BeginFrame(
            double timestampSeconds,
            Pose capturePose)
        {
            frameTimestampSeconds = Math.Max(0.0, timestampSeconds);
            cameraPoseAtCapture = capturePose;
            observationDepthCache.Clear();
            hasFrameContext = true;
        }

        public PersonDistanceMeasurement Measure(
            TrackedDynamicObject tracked)
        {
            if (tracked == null || tracked.Detection == null)
            {
                return PersonDistanceMeasurement.Unavailable(
                    0,
                    frameTimestampSeconds,
                    0f,
                    "invalid_track");
            }

            ResolveReferences();
            float bboxArea = tracked.Detection.boundingBox.Area;
            if (!hasFrameContext
                || cameraAccess == null
                || !cameraAccess.IsPlaying)
            {
                return Fallback(
                    tracked,
                    tracked.Detection.boundingBox,
                    "camera_not_ready");
            }

            if (raycastManager == null
                || !EnvironmentRaycastManager.IsSupported)
            {
                return Fallback(
                    tracked,
                    tracked.Detection.boundingBox,
                    "environment_depth_not_supported");
            }

            float captureAgeSeconds = Mathf.Max(
                0f,
                (float)(Time.realtimeSinceStartupAsDouble
                    - frameTimestampSeconds));
            string preRaycastRejection = PreRaycastRejectionReason(
                tracked.KinematicSampleCount,
                captureAgeSeconds,
                tracked.ViewportCenterVelocity,
                tracked.ViewportSizeVelocity,
                maximumCaptureAgeSeconds);
            if (!string.IsNullOrEmpty(preRaycastRejection))
            {
                expandNextFrame.Remove(tracked.TrackId);
                IsDepthReady = false;
                LastFailureReason = preRaycastRejection;
                return Fallback(
                    tracked,
                    tracked.Detection.boundingBox,
                    preRaycastRejection);
            }

            personDepthSamples.Clear();
            NormalizedBoundingBox box =
                tracked.Detection.boundingBox;
            bool expanded = expandNextFrame.Remove(tracked.TrackId);
            Vector2[] points = expanded
                ? ExpandedSamplePoints
                : DefaultSamplePoints;
            // Always raycast the exact inference frame. Reusing the older
            // observation cache kept only scalar distances and therefore
            // could not provide the real body surface points needed for
            // world-space person presentation.
            var sampleDiagnostics =
                new PersonDepthSampleDiagnostic[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 relative = points[i];
                Vector2 cameraViewportPoint =
                    PersonImageCoordinates.BoxRelativeToCameraViewport(
                        box,
                        relative);
                bool torsoCore = IsTorsoCore(relative);
                bool upperBody = IsUpperBody(relative);
                sampleDiagnostics[i] = new PersonDepthSampleDiagnostic(
                    i,
                    relative,
                    cameraViewportPoint,
                    false,
                    0f,
                    false,
                    Vector3.zero,
                    torsoCore,
                    upperBody,
                    PersonDepthSampleDecision.NoHit);
                Ray ray = cameraAccess.ViewportPointToRay(
                    cameraViewportPoint,
                    cameraPoseAtCapture);
                EnvironmentRaycastHit hit;
                if (!raycastManager.Raycast(
                        ray,
                        out hit,
                        maximumDistanceMeters)
                    || hit.status != EnvironmentRaycastHitStatus.Hit)
                {
                    continue;
                }

                float distance = Vector3.Distance(
                    ray.origin,
                    hit.point);
                if (IsFinite(distance)
                    && distance >= MinimumDepthDistanceMeters
                    && distance <= maximumDistanceMeters)
                {
                    AddDepthSample(
                        i,
                        distance,
                        relative,
                        cameraViewportPoint,
                        expanded
                            ? ExpandedWeight(relative)
                            : DefaultSampleWeights[i],
                        hit.point);
                    sampleDiagnostics[i] =
                        new PersonDepthSampleDiagnostic(
                            i,
                            relative,
                            cameraViewportPoint,
                            true,
                            distance,
                            true,
                            hit.point,
                            torsoCore,
                            upperBody,
                            PersonDepthSampleDecision.OtherCluster);
                }
                else
                {
                    sampleDiagnostics[i] =
                        sampleDiagnostics[i].WithDecision(
                            PersonDepthSampleDecision.InvalidDistance);
                }
            }

            PersonDistanceMeasurement result =
                distanceFilter.UpdateMetric(
                    tracked.TrackId,
                    frameTimestampSeconds,
                    personDepthSamples,
                    points.Length,
                    bboxArea,
                    PersonDepthReliability.MinimumConfidence,
                    float.PositiveInfinity,
                    PersonDepthReliability.MaximumDispersionMeters);
            string rejectedReason;
            bool reliable = PersonDepthReliability.IsReliable(
                result,
                box,
                out rejectedReason);
            result = result.WithMetricReliability(
                reliable,
                rejectedReason);
            if (!reliable
                && !expanded
                && ShouldExpandNextSample(rejectedReason))
            {
                expandNextFrame.Add(tracked.TrackId);
            }

            ulong trackingMask = distanceFilter.LatestTrackingSelectionMask;
            ulong safetyMask = distanceFilter.LatestSafetySelectionMask;
            ApplySampleDecisions(
                sampleDiagnostics,
                trackingMask,
                safetyMask);
            bool hasTrackingWorldCenter = PersonDepthWorldCenter.TryCalculate(
                personDepthSamples,
                trackingMask,
                out Vector3 trackingWorldCenter);
            if (hasTrackingWorldCenter)
            {
                result = result.WithDepthClusters(
                    result.TrackingCluster.WithWorldCenter(
                        trackingWorldCenter),
                    result.SafetyCluster);
            }
            PublishSamplingSnapshot(
                tracked.TrackId,
                expanded,
                sampleDiagnostics,
                trackingMask,
                safetyMask,
                hasTrackingWorldCenter,
                trackingWorldCenter,
                string.IsNullOrEmpty(result.ClusterSelectionReason)
                    ? rejectedReason
                    : result.ClusterSelectionReason,
                result.TrackingCluster,
                result.SafetyCluster);
            result = AttachPresentationGeometry(
                tracked,
                box,
                result,
                reliable && hasTrackingWorldCenter,
                trackingWorldCenter);
            IsDepthReady = result.HasReliableMetricDistance
                || result.HasReliableSafetyDistance;
            LastFailureReason = result.IsMetricReliable
                ? result.FailureReason
                : result.DepthRejectedReason;
            return result;
        }

        public void PruneExcept(IEnumerable<int> liveTrackIds)
        {
            distanceFilter.PruneExcept(liveTrackIds);
            liveTrackScratch.Clear();
            if (liveTrackIds != null)
            {
                foreach (int trackId in liveTrackIds)
                {
                    liveTrackScratch.Add(trackId);
                }
            }

            expiredWorldTracks.Clear();
            foreach (int trackId in worldTrackStates.Keys)
            {
                if (!liveTrackScratch.Contains(trackId))
                {
                    expiredWorldTracks.Add(trackId);
                }
            }

            for (int i = 0; i < expiredWorldTracks.Count; i++)
            {
                int trackId = expiredWorldTracks[i];
                worldTrackStates.Remove(trackId);
                samplingSnapshots.Remove(trackId);
                expandNextFrame.Remove(trackId);
            }
        }

        public void ResetProvider()
        {
            distanceFilter.Reset();
            expandNextFrame.Clear();
            worldTrackStates.Clear();
            samplingSnapshots.Clear();
            observationDepthCache.Clear();
            hasFrameContext = false;
            IsDepthReady = false;
            LastFailureReason = string.Empty;
            LastPresentationGeometrySource =
                PersonPresentationGeometrySource.Unavailable;
            LatestSamplingSnapshot = PersonDepthSamplingSnapshot.Empty;
        }

        private PersonDistanceMeasurement Fallback(
            TrackedDynamicObject tracked,
            NormalizedBoundingBox box,
            string reason)
        {
            IsDepthReady = false;
            LastFailureReason = reason;
            PublishSamplingSnapshot(
                tracked.TrackId,
                false,
                Array.Empty<PersonDepthSampleDiagnostic>(),
                0UL,
                0UL,
                false,
                Vector3.zero,
                reason);
            PersonDistanceMeasurement fallback =
                distanceFilter.GetHeldOrBoundingBoxFallback(
                tracked.TrackId,
                frameTimestampSeconds,
                box.Area,
                reason);
            return AttachPresentationGeometry(tracked, box, fallback);
        }

        private PersonDistanceMeasurement AttachPresentationGeometry(
            TrackedDynamicObject tracked,
            NormalizedBoundingBox box,
            PersonDistanceMeasurement measurement,
            bool hasTrackingWorldCenter = false,
            Vector3 trackingWorldCenter = default)
        {
            LastPresentationGeometrySource =
                PersonPresentationGeometrySource.Unavailable;
            if (tracked == null
                || measurement == null
                || !hasFrameContext
                || cameraAccess == null
                || !cameraAccess.IsPlaying)
            {
                return measurement;
            }

            NormalizedBoundingBox capsuleBox =
                HazardPresentationGeometry.UpperBodyExpandedBox(box);
            Ray bottomLeftRay = cameraAccess.ViewportPointToRay(
                PersonImageCoordinates.BoxRelativeToCameraViewport(
                    capsuleBox,
                    new Vector2(0f, 1f)),
                cameraPoseAtCapture);
            Ray bottomRightRay = cameraAccess.ViewportPointToRay(
                PersonImageCoordinates.BoxRelativeToCameraViewport(
                    capsuleBox,
                    new Vector2(1f, 1f)),
                cameraPoseAtCapture);
            Ray topRightRay = cameraAccess.ViewportPointToRay(
                PersonImageCoordinates.BoxRelativeToCameraViewport(
                    capsuleBox,
                    new Vector2(1f, 0f)),
                cameraPoseAtCapture);
            Ray topLeftRay = cameraAccess.ViewportPointToRay(
                PersonImageCoordinates.BoxRelativeToCameraViewport(
                    capsuleBox,
                    Vector2.zero),
                cameraPoseAtCapture);
            Ray centerRay = cameraAccess.ViewportPointToRay(
                PersonImageCoordinates.BoxRelativeToCameraViewport(
                    box,
                    new Vector2(0.5f, 0.5f)),
                cameraPoseAtCapture);
            Ray topCenterRay = cameraAccess.ViewportPointToRay(
                PersonImageCoordinates.BoxRelativeToCameraViewport(
                    capsuleBox,
                    new Vector2(0.5f, 0f)),
                cameraPoseAtCapture);
            Ray bottomCenterRay = cameraAccess.ViewportPointToRay(
                PersonImageCoordinates.BoxRelativeToCameraViewport(
                    capsuleBox,
                    new Vector2(0.5f, 1f)),
                cameraPoseAtCapture);
            Vector3 captureForward = cameraPoseAtCapture.rotation
                * Vector3.forward;

            if (measurement.HasReliableMetricDistance)
            {
                Vector3 worldVelocity;
                bool hasWorldVelocity;
                Vector3 observedWorldPoint = hasTrackingWorldCenter
                    ? trackingWorldCenter
                    : centerRay.origin
                        + centerRay.direction
                            * measurement.FilteredDistanceMeters;
                Vector3 worldPoint = FilterWorldPoint(
                    tracked.TrackId,
                    observedWorldPoint,
                    frameTimestampSeconds,
                    out worldVelocity,
                    out hasWorldVelocity);
                measurement = measurement.WithWorldPoint(
                    worldPoint,
                    worldVelocity,
                    hasWorldVelocity);
                if (HazardPresentationGeometry.TryCreatePersonCapsule(
                        tracked.TrackId,
                        capsuleBox,
                        bottomLeftRay,
                        bottomRightRay,
                        topRightRay,
                        topLeftRay,
                        worldPoint,
                        captureForward,
                        frameTimestampSeconds,
                        measurement.Confidence,
                        0f,
                        worldVelocity,
                        hasWorldVelocity,
                        out HazardPresentationGeometry metricGeometry))
                {
                    LastPresentationGeometrySource =
                        PersonPresentationGeometrySource.MetricDepth;
                    return measurement.WithPresentationGeometry(
                        metricGeometry,
                        LastPresentationGeometrySource,
                        measurement.FilteredDistanceMeters);
                }
            }

            WorldTrackState history;
            if (worldTrackStates.TryGetValue(tracked.TrackId, out history)
                && history.Initialized
                && PersonPresentationGeometryMath.TryHistoryDistance(
                    cameraPoseAtCapture.position,
                    captureForward,
                    history.Position,
                    history.Velocity,
                    frameTimestampSeconds - history.TimestampSeconds,
                    out float historyDistance)
                && PersonPresentationGeometryMath.TryCreateAtDistance(
                    tracked.TrackId,
                    capsuleBox,
                    bottomLeftRay,
                    bottomRightRay,
                    topRightRay,
                    topLeftRay,
                    centerRay,
                    captureForward,
                    historyDistance,
                    frameTimestampSeconds,
                    Mathf.Max(0.20f, measurement.Confidence),
                    history.Velocity,
                    true,
                    out HazardPresentationGeometry historyGeometry))
            {
                LastPresentationGeometrySource =
                    PersonPresentationGeometrySource.TrackHistory;
                return measurement.WithPresentationGeometry(
                    historyGeometry,
                    LastPresentationGeometrySource,
                    historyDistance);
            }

            float estimatedDistance =
                PersonPresentationGeometryMath.EstimateDistanceMeters(
                    box,
                    topCenterRay,
                    bottomCenterRay);
            if (PersonPresentationGeometryMath.TryCreateAtDistance(
                    tracked.TrackId,
                    capsuleBox,
                    bottomLeftRay,
                    bottomRightRay,
                    topRightRay,
                    topLeftRay,
                    centerRay,
                    captureForward,
                    estimatedDistance,
                    frameTimestampSeconds,
                    Mathf.Clamp01(tracked.Detection.confidence * 0.55f),
                    Vector3.zero,
                    false,
                    out HazardPresentationGeometry estimatedGeometry))
            {
                LastPresentationGeometrySource =
                    PersonPresentationGeometrySource.BoundingBoxEstimate;
                return measurement.WithPresentationGeometry(
                    estimatedGeometry,
                    LastPresentationGeometrySource,
                    estimatedDistance);
            }

            return measurement;
        }

        private void ResolveReferences()
        {
            if (cameraAccess == null)
            {
                cameraAccess =
                    FindAnyObjectByType<PassthroughCameraAccess>();
            }

            if (raycastManager == null)
            {
                raycastManager =
                    FindAnyObjectByType<EnvironmentRaycastManager>();
            }

            if (trackingQuality == null)
            {
                trackingQuality =
                    FindAnyObjectByType<TrackingQualityController>();
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void AddDepthSample(
            int sampleIndex,
            float distanceMeters,
            Vector2 relativePoint,
            Vector2 cameraViewportPoint,
            float weight,
            Vector3 worldPoint)
        {
            personDepthSamples.Add(new PersonDepthSample(
                sampleIndex,
                relativePoint,
                cameraViewportPoint,
                distanceMeters,
                weight,
                IsTorsoCore(relativePoint),
                IsUpperBody(relativePoint),
                true,
                worldPoint));
        }

        private static bool IsTorsoCore(Vector2 relativePoint)
        {
            return relativePoint.x >= 0.35f
                && relativePoint.x <= 0.65f
                && relativePoint.y >= 0.28f
                && relativePoint.y <= 0.68f;
        }

        private static bool IsUpperBody(Vector2 relativePoint)
        {
            return relativePoint.y <= 0.70f;
        }

        private void PublishSamplingSnapshot(
            int trackId,
            bool expanded,
            PersonDepthSampleDiagnostic[] samples,
            ulong trackingMask,
            ulong safetyMask,
            bool hasTrackingWorldCenter,
            Vector3 trackingWorldCenter,
            string selectionReason,
            PersonDepthClusterMeasurement trackingCluster = default,
            PersonDepthClusterMeasurement safetyCluster = default)
        {
            int hitCount = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                if (samples[i].HasHit)
                {
                    hitCount++;
                }
            }

            var snapshot = new PersonDepthSamplingSnapshot(
                trackId,
                frameTimestampSeconds,
                expanded,
                samples.Length,
                hitCount,
                trackingMask,
                safetyMask,
                samples,
                hasTrackingWorldCenter,
                trackingWorldCenter,
                selectionReason,
                trackingCluster,
                safetyCluster);
            samplingSnapshots[trackId] = snapshot;
            LatestSamplingSnapshot = snapshot;
            trackingQuality?.RecordPersonDepthSamples(snapshot);
        }

        private static void ApplySampleDecisions(
            PersonDepthSampleDiagnostic[] samples,
            ulong trackingMask,
            ulong safetyMask)
        {
            if (samples == null)
            {
                return;
            }

            for (int i = 0; i < samples.Length && i < 64; i++)
            {
                if (!samples[i].HasHit)
                {
                    continue;
                }

                ulong bit = 1UL << i;
                PersonDepthSampleDecision decision =
                    (safetyMask & bit) != 0UL
                        ? PersonDepthSampleDecision.SelectedSafety
                        : (trackingMask & bit) != 0UL
                            ? PersonDepthSampleDecision.SelectedTracking
                            : PersonDepthSampleDecision.OtherCluster;
                samples[i] = samples[i].WithDecision(decision);
            }
        }

        public static bool ShouldExpandNextSample(string rejectedReason)
        {
            return string.Equals(
                    rejectedReason,
                    "depth_dispersion",
                    StringComparison.Ordinal)
                || string.Equals(
                    rejectedReason,
                    "bbox_depth_conflict",
                    StringComparison.Ordinal)
                || string.Equals(
                    rejectedReason,
                    "torso_cluster_unavailable",
                    StringComparison.Ordinal)
                || string.Equals(
                    rejectedReason,
                    "insufficient_tracking_cluster",
                    StringComparison.Ordinal)
                || string.Equals(
                    rejectedReason,
                    "insufficient_safety_cluster",
                    StringComparison.Ordinal)
                || string.Equals(
                    rejectedReason,
                    "insufficient_depth_samples",
                    StringComparison.Ordinal);
        }

        public static string PreRaycastRejectionReason(
            int kinematicSampleCount,
            float captureAgeSeconds,
            Vector2 viewportCenterVelocity,
            Vector2 viewportSizeVelocity,
            float maximumAgeSeconds = 0.40f)
        {
            if (captureAgeSeconds > Mathf.Max(0f, maximumAgeSeconds))
            {
                return "depth_capture_stale";
            }

            if (kinematicSampleCount < 2)
            {
                return "depth_track_warming";
            }

            return viewportCenterVelocity.magnitude * captureAgeSeconds
                    > 0.08f
                || viewportSizeVelocity.magnitude * captureAgeSeconds
                    > 0.10f
                ? "depth_motion_mismatch"
                : string.Empty;
        }

        private bool IsFrameContextStale()
        {
            return frameTimestampSeconds > 0.0
                && Time.realtimeSinceStartupAsDouble - frameTimestampSeconds
                    > maximumCaptureAgeSeconds;
        }

        public bool TryMeasureObservationWorldPoint(
            NormalizedBoundingBox box,
            out Vector3 worldPoint,
            out float confidence)
        {
            worldPoint = Vector3.zero;
            confidence = 0f;
            ResolveReferences();
            if (!hasFrameContext
                || cameraAccess == null
                || !cameraAccess.IsPlaying
                || raycastManager == null
                || !EnvironmentRaycastManager.IsSupported)
            {
                return false;
            }

            if (IsFrameContextStale())
            {
                return false;
            }

            Vector3 pointSum = Vector3.zero;
            int hitCount = 0;
            float minimum = float.PositiveInfinity;
            float maximum = 0f;
            float distanceSum = 0f;
            float leftDistance = 0f;
            float centerDistance = 0f;
            float rightDistance = 0f;
            for (int i = 0; i < ObservationWorldSamplePoints.Length; i++)
            {
                Vector2 relative = ObservationWorldSamplePoints[i];
                Vector2 viewport =
                    PersonImageCoordinates.BoxRelativeToCameraViewport(
                        box,
                        relative);
                Ray ray = cameraAccess.ViewportPointToRay(
                    viewport,
                    cameraPoseAtCapture);
                EnvironmentRaycastHit hit;
                if (!raycastManager.Raycast(
                        ray,
                        out hit,
                        maximumDistanceMeters)
                    || hit.status != EnvironmentRaycastHitStatus.Hit)
                {
                    continue;
                }

                float distance = Vector3.Distance(ray.origin, hit.point);
                if (!IsFinite(distance))
                {
                    continue;
                }

                pointSum += hit.point;
                minimum = Mathf.Min(minimum, distance);
                maximum = Mathf.Max(maximum, distance);
                distanceSum += distance;
                if (i == 0)
                {
                    leftDistance = distance;
                }
                else if (i == 1)
                {
                    centerDistance = distance;
                }
                else
                {
                    rightDistance = distance;
                }
                hitCount++;
            }

            if (hitCount < PersonDepthReliability.MinimumSamples)
            {
                return false;
            }

            float dispersion = maximum - minimum;
            if (dispersion > 0.45f)
            {
                return false;
            }

            float averageDistance = distanceSum / hitCount;
            if (PersonDepthReliability.IsBackgroundSuspected(
                    box,
                    averageDistance))
            {
                return false;
            }

            worldPoint = pointSum / hitCount;
            confidence = Mathf.Clamp01(
                hitCount / (float)ObservationWorldSamplePoints.Length
                * Mathf.Exp(-dispersion / 0.30f));
            if (confidence < PersonDepthReliability.MinimumConfidence)
            {
                return false;
            }

            observationDepthCache.Add(new ObservationDepthSample(
                box,
                leftDistance,
                centerDistance,
                rightDistance));
            return true;
        }

        private int FindObservationDepthSample(NormalizedBoundingBox box)
        {
            int bestIndex = -1;
            float bestOverlap = 0.50f;
            for (int i = 0; i < observationDepthCache.Count; i++)
            {
                NormalizedBoundingBox cached = observationDepthCache[i].Box;
                float left = Mathf.Max(box.Left, cached.Left);
                float top = Mathf.Max(box.Top, cached.Top);
                float right = Mathf.Min(box.Right, cached.Right);
                float bottom = Mathf.Min(box.Bottom, cached.Bottom);
                float intersection = Mathf.Max(0f, right - left)
                    * Mathf.Max(0f, bottom - top);
                float union = box.Area + cached.Area - intersection;
                float overlap = union > 0f ? intersection / union : 0f;
                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static float ExpandedWeight(Vector2 point)
        {
            float horizontal = 1f - Mathf.Abs(point.x - 0.5f) * 1.5f;
            float torso = 1f - Mathf.Abs(point.y - 0.5f);
            return Mathf.Max(0.5f, horizontal * torso * 2.2f);
        }

        private Vector3 FilterWorldPoint(
            int trackId,
            Vector3 observation,
            double timestampSeconds,
            out Vector3 worldVelocity,
            out bool hasWorldVelocity)
        {
            WorldTrackState state;
            if (!worldTrackStates.TryGetValue(trackId, out state))
            {
                state = new WorldTrackState();
                worldTrackStates.Add(trackId, state);
            }

            if (!state.Initialized)
            {
                state.Initialized = true;
                state.Position = observation;
                state.Velocity = Vector3.zero;
                state.TimestampSeconds = timestampSeconds;
                worldVelocity = Vector3.zero;
                hasWorldVelocity = false;
                return observation;
            }

            float elapsed = (float)Math.Max(
                0.0001,
                timestampSeconds - state.TimestampSeconds);
            if (elapsed > 0.75f)
            {
                state.Position = observation;
                state.Velocity = Vector3.zero;
                state.TimestampSeconds = timestampSeconds;
                worldVelocity = Vector3.zero;
                hasWorldVelocity = false;
                return observation;
            }
            Vector3 prediction = state.Position + state.Velocity * elapsed;
            Vector3 residual = observation - prediction;
            // Three-Hz inference needs a more responsive center update than
            // the original distance-only estimate; render-time prediction
            // handles the remaining capture latency.
            const float alpha = 0.78f;
            const float beta = 0.20f;
            state.Position = prediction + alpha * residual;
            state.Velocity = Vector3.ClampMagnitude(
                state.Velocity + beta * residual / elapsed,
                3f);
            state.TimestampSeconds = timestampSeconds;
            worldVelocity = state.Velocity;
            hasWorldVelocity = true;
            return state.Position;
        }
    }
}
#endif
