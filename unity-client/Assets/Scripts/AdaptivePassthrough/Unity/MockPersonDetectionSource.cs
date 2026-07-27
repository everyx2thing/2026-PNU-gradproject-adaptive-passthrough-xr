using System;
using System.Collections.Generic;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DynamicRiskController))]
    public sealed class MockPersonDetectionSource : MonoBehaviour
    {
        [SerializeField] private DynamicRiskController controller;
        [SerializeField, Min(1f)] private float inferenceRateHz = 10f;
        [SerializeField] private bool loop = true;
        [SerializeField] private bool includeSecondPerson = true;
        [SerializeField] private bool runOnAndroid;

        private readonly List<DynamicObjectDetection> detections =
            new List<DynamicObjectDetection>(2);
        private double nextFrameAt;
        private double sequenceStartedAt;

        private void Awake()
        {
            if (controller == null)
            {
                controller = GetComponent<DynamicRiskController>();
            }
        }

        private void OnEnable()
        {
            sequenceStartedAt = Time.realtimeSinceStartupAsDouble;
            nextFrameAt = sequenceStartedAt;
        }

        private void Update()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!runOnAndroid)
            {
                return;
            }
#endif
            if (controller == null)
            {
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextFrameAt)
            {
                return;
            }

            nextFrameAt = now + 1.0 / Math.Max(1f, inferenceRateHz);
            double elapsed = now - sequenceStartedAt;
            if (loop)
            {
                elapsed %= 12.0;
            }

            detections.Clear();
            detections.Add(BuildPrimaryPerson(elapsed));
            if (includeSecondPerson && elapsed >= 2.0 && elapsed <= 9.0)
            {
                detections.Add(new DynamicObjectDetection(
                    "person",
                    0.82f,
                    new NormalizedBoundingBox(
                        0.79f,
                        0.49f,
                        0.12f,
                        0.30f)));
            }

            controller.SubmitDetections(now, detections);
        }

        private static DynamicObjectDetection BuildPrimaryPerson(double elapsed)
        {
            float width;
            float height;
            float centerX;

            if (elapsed < 5.0)
            {
                float t = (float)(elapsed / 5.0);
                width = Lerp(0.10f, 0.42f, t);
                height = Lerp(0.24f, 0.82f, t);
                centerX = Lerp(0.35f, 0.50f, t);
            }
            else if (elapsed < 7.0)
            {
                width = 0.42f;
                height = 0.82f;
                centerX = 0.50f;
            }
            else
            {
                float t = (float)((elapsed - 7.0) / 5.0);
                width = Lerp(0.42f, 0.10f, t);
                height = Lerp(0.82f, 0.24f, t);
                centerX = Lerp(0.50f, 0.65f, t);
            }

            return new DynamicObjectDetection(
                "person",
                0.94f,
                new NormalizedBoundingBox(centerX, 0.52f, width, height));
        }

        private static float Lerp(float from, float to, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return from + (to - from) * t;
        }
    }
}
