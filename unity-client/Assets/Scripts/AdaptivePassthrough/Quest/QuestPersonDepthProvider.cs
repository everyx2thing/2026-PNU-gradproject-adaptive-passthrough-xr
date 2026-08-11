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
        private static readonly Vector2[] BBoxSamplePoints =
        {
            new Vector2(0.50f, 0.30f),
            new Vector2(0.35f, 0.42f),
            new Vector2(0.50f, 0.42f),
            new Vector2(0.65f, 0.42f),
            new Vector2(0.40f, 0.60f),
            new Vector2(0.50f, 0.60f),
            new Vector2(0.60f, 0.60f)
        };

        [SerializeField] private PassthroughCameraAccess cameraAccess;
        [SerializeField] private EnvironmentRaycastManager raycastManager;
        [SerializeField, Min(0.2f)] private float maximumDistanceMeters = 6f;

        private readonly List<float> sampleDistances =
            new List<float>(BBoxSamplePoints.Length);
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

            sampleDistances.Clear();
            NormalizedBoundingBox box =
                tracked.Detection.boundingBox;
            for (int i = 0; i < BBoxSamplePoints.Length; i++)
            {
                Vector2 relative = BBoxSamplePoints[i];
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
                }
            }

            PersonDistanceMeasurement result =
                distanceFilter.UpdateMetric(
                    tracked.TrackId,
                    frameTimestampSeconds,
                    sampleDistances,
                    BBoxSamplePoints.Length,
                    bboxArea);
            IsDepthReady = result.HasMetricDistance;
            LastFailureReason = result.FailureReason;
            return result;
        }

        public void PruneExcept(IEnumerable<int> liveTrackIds)
        {
            distanceFilter.PruneExcept(liveTrackIds);
        }

        public void ResetProvider()
        {
            distanceFilter.Reset();
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
    }
}
#endif
