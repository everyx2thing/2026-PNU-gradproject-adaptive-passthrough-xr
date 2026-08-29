using System;
using System.Collections.Generic;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    [DisallowMultipleComponent]
    public sealed class DynamicRiskController : MonoBehaviour
    {
        [Header("Report-based dynamic risk settings")]
        [SerializeField] private DynamicRiskSettings riskSettings = new DynamicRiskSettings();
        [SerializeField] private string targetLabel = "person";

        private DynamicRiskPipeline pipeline;

        public event Action<DynamicRiskFrame> FrameProcessed;
        public event Action PipelineReset;

        public DynamicRiskFrame LatestFrame { get; private set; }
        public long LatestFrameSequence { get; private set; }
        public double LatestFrameProcessedRealtimeSeconds { get; private set; }
        public double LatestFrameCaptureRealtimeSeconds { get; private set; }

        public float LatestMaximumRisk
        {
            get { return LatestFrame == null ? 0f : LatestFrame.MaximumRisk; }
        }

        public DynamicRiskLevel LatestMaximumLevel
        {
            get
            {
                return LatestFrame == null
                    ? DynamicRiskLevel.Safe
                    : LatestFrame.MaximumLevel;
            }
        }

        private void Awake()
        {
            RebuildPipeline();
        }

        public DynamicRiskFrame SubmitDetections(
            double timestampSeconds,
            IReadOnlyList<DynamicObjectDetection> detections)
        {
            return SubmitDetections(
                timestampSeconds,
                detections,
                null);
        }

        public DynamicRiskFrame SubmitDetections(
            double timestampSeconds,
            IReadOnlyList<DynamicObjectDetection> detections,
            Func<TrackedDynamicObject, PersonDistanceMeasurement>
                distanceResolver)
        {
            return SubmitDetections(
                timestampSeconds,
                detections,
                distanceResolver,
                Time.realtimeSinceStartupAsDouble);
        }

        public DynamicRiskFrame SubmitDetections(
            double timestampSeconds,
            IReadOnlyList<DynamicObjectDetection> detections,
            Func<TrackedDynamicObject, PersonDistanceMeasurement>
                distanceResolver,
            double captureRealtimeSeconds)
        {
            if (pipeline == null)
            {
                RebuildPipeline();
            }

            LatestFrame = pipeline.Process(
                timestampSeconds,
                detections,
                distanceResolver);
            LatestFrameSequence++;
            LatestFrameProcessedRealtimeSeconds =
                Time.realtimeSinceStartupAsDouble;
            LatestFrameCaptureRealtimeSeconds =
                captureRealtimeSeconds > 0.0
                && captureRealtimeSeconds
                    <= LatestFrameProcessedRealtimeSeconds
                    ? captureRealtimeSeconds
                    : LatestFrameProcessedRealtimeSeconds;
            FrameProcessed?.Invoke(LatestFrame);
            return LatestFrame;
        }

        public DynamicRiskFrame SubmitDetections(
            IReadOnlyList<DynamicObjectDetection> detections)
        {
            return SubmitDetections(Time.realtimeSinceStartupAsDouble, detections);
        }

        public void ResetPipeline()
        {
            if (pipeline != null)
            {
                pipeline.Reset();
            }

            LatestFrame = null;
            LatestFrameSequence = 0;
            LatestFrameProcessedRealtimeSeconds = 0.0;
            LatestFrameCaptureRealtimeSeconds = 0.0;
            PipelineReset?.Invoke();
        }

        private void RebuildPipeline()
        {
            pipeline = new DynamicRiskPipeline(riskSettings, targetLabel);
            LatestFrame = null;
            LatestFrameSequence = 0;
            LatestFrameProcessedRealtimeSeconds = 0.0;
            LatestFrameCaptureRealtimeSeconds = 0.0;
            PipelineReset?.Invoke();
        }
    }
}
