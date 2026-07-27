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

        public DynamicRiskFrame LatestFrame { get; private set; }

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
            if (pipeline == null)
            {
                RebuildPipeline();
            }

            LatestFrame = pipeline.Process(timestampSeconds, detections);
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
        }

        private void RebuildPipeline()
        {
            pipeline = new DynamicRiskPipeline(riskSettings, targetLabel);
            LatestFrame = null;
        }
    }
}
