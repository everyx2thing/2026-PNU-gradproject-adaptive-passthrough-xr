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
        [SerializeField, Min(0.2f)] private float maximumDistanceMeters = 6f;
        [SerializeField, Min(0.05f)] private float maximumCaptureAgeSeconds = 0.40f;

        private readonly List<float> sampleDistances =
            new List<float>(ExpandedSamplePoints.Length);
        private readonly List<float> sampleWeights =
            new List<float>(ExpandedSamplePoints.Length);
        private readonly HashSet<int> expandNextFrame = new HashSet<int>();
        private readonly Dictionary<int, WorldTrackState> worldTrackStates =
            new Dictionary<int, WorldTrackState>();
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

        public PassthroughCameraAccess CameraAccess
        {
            get { return cameraAccess; }
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDisable()
        {
            hasFrameContext = false;
            IsDepthReady = false;
            distanceFilter.Reset();
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
                    bboxArea,
                    "camera_not_ready");
            }

            if (raycastManager == null
                || !EnvironmentRaycastManager.IsSupported)
            {
                return Fallback(
                    tracked,
                    bboxArea,
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
                return Fallback(
                    tracked,
                    bboxArea,
                    preRaycastRejection);
            }

            sampleDistances.Clear();
            sampleWeights.Clear();
            NormalizedBoundingBox box =
                tracked.Detection.boundingBox;
            bool expanded = expandNextFrame.Remove(tracked.TrackId);
            Vector2[] points = expanded
                ? ExpandedSamplePoints
                : DefaultSamplePoints;
            int cachedSampleIndex = expanded
                ? -1
                : FindObservationDepthSample(box);
            if (cachedSampleIndex >= 0)
            {
                ObservationDepthSample cached =
                    observationDepthCache[cachedSampleIndex];
                sampleDistances.Add(cached.LeftDistance);
                sampleWeights.Add(DefaultSampleWeights[5]);
                sampleDistances.Add(cached.CenterDistance);
                sampleWeights.Add(DefaultSampleWeights[6]);
                sampleDistances.Add(cached.RightDistance);
                sampleWeights.Add(DefaultSampleWeights[7]);
            }
            for (int i = 0; i < points.Length; i++)
            {
                if (cachedSampleIndex >= 0 && i >= 5 && i <= 7)
                {
                    continue;
                }
                Vector2 relative = points[i];
                float x = box.Left + box.width * relative.x;
                float topDownY = box.Top + box.height * relative.y;
                Vector2 cameraViewportPoint =
                    new Vector2(x, 1f - topDownY);
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
                if (IsFinite(distance))
                {
                    sampleDistances.Add(distance);
                    sampleWeights.Add(expanded
                        ? ExpandedWeight(relative)
                        : DefaultSampleWeights[i]);
                }
            }

            PersonDistanceMeasurement result =
                distanceFilter.UpdateMetric(
                    tracked.TrackId,
                    frameTimestampSeconds,
                    sampleDistances,
                    points.Length,
                    bboxArea,
                    sampleWeights,
                    PersonDepthReliability.MinimumConfidence,
                    PersonDepthReliability.IsLargeBox(box)
                        ? PersonDepthReliability.BackgroundDistanceMeters
                        : float.PositiveInfinity,
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
            if (result.HasReliableMetricDistance)
            {
                Vector2 centerViewport = new Vector2(
                    box.centerX,
                    1f - box.centerY);
                Ray centerRay = cameraAccess.ViewportPointToRay(
                    centerViewport,
                    cameraPoseAtCapture);
                Vector3 worldVelocity;
                Vector3 worldPoint = FilterWorldPoint(
                    tracked.TrackId,
                    centerRay.origin
                        + centerRay.direction
                        * result.FilteredDistanceMeters,
                    frameTimestampSeconds,
                    out worldVelocity);
                result = result.WithWorldPoint(
                    worldPoint,
                    worldVelocity);
            }
            IsDepthReady = result.HasReliableMetricDistance;
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
                worldTrackStates.Remove(expiredWorldTracks[i]);
                expandNextFrame.Remove(expiredWorldTracks[i]);
            }
        }

        public void ResetProvider()
        {
            distanceFilter.Reset();
            expandNextFrame.Clear();
            worldTrackStates.Clear();
            observationDepthCache.Clear();
            hasFrameContext = false;
            IsDepthReady = false;
            LastFailureReason = string.Empty;
        }

        private PersonDistanceMeasurement Fallback(
            TrackedDynamicObject tracked,
            float bboxArea,
            string reason)
        {
            IsDepthReady = false;
            LastFailureReason = reason;
            return distanceFilter.GetHeldOrBoundingBoxFallback(
                tracked.TrackId,
                frameTimestampSeconds,
                bboxArea,
                reason);
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
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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
                Vector2 viewport = new Vector2(
                    box.Left + box.width * relative.x,
                    1f - (box.Top + box.height * relative.y));
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
            out Vector3 worldVelocity)
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
                return observation;
            }

            float elapsed = (float)Math.Max(
                0.0001,
                timestampSeconds - state.TimestampSeconds);
            Vector3 prediction = state.Position + state.Velocity * elapsed;
            Vector3 residual = observation - prediction;
            const float alpha = 0.65f;
            const float beta = 0.12f;
            state.Position = prediction + alpha * residual;
            state.Velocity += beta * residual / elapsed;
            state.TimestampSeconds = timestampSeconds;
            worldVelocity = state.Velocity;
            return state.Position;
        }
    }
}
#endif
