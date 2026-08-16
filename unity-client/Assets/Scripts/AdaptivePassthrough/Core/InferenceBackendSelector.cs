using System;
using System.Collections.Generic;
using Unity.InferenceEngine;

namespace TeamVR.AdaptivePassthrough
{
    public readonly struct InferenceBackendBenchmark
    {
        public readonly BackendType Backend;
        public readonly float MedianInferenceMilliseconds;
        public readonly float FrameP95Milliseconds;

        public InferenceBackendBenchmark(
            BackendType backend,
            float medianInferenceMilliseconds,
            float frameP95Milliseconds)
        {
            Backend = backend;
            MedianInferenceMilliseconds = Math.Max(
                0f,
                medianInferenceMilliseconds);
            FrameP95Milliseconds = Math.Max(0f, frameP95Milliseconds);
        }
    }

    public static class InferenceBackendSelector
    {
        public static BackendType Select(
            IReadOnlyList<InferenceBackendBenchmark> candidates,
            float frameBudgetMilliseconds = 13.9f,
            float tieToleranceMilliseconds = 0.10f)
        {
            BackendType selected = BackendType.CPU;
            float bestMedian = float.MaxValue;
            if (candidates == null)
            {
                return selected;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                InferenceBackendBenchmark candidate = candidates[i];
                if (candidate.FrameP95Milliseconds
                    > frameBudgetMilliseconds)
                {
                    continue;
                }

                if (candidate.MedianInferenceMilliseconds
                    < bestMedian - tieToleranceMilliseconds)
                {
                    selected = candidate.Backend;
                    bestMedian = candidate.MedianInferenceMilliseconds;
                }
                else if (Math.Abs(
                        candidate.MedianInferenceMilliseconds - bestMedian)
                    <= tieToleranceMilliseconds
                    && candidate.Backend == BackendType.CPU)
                {
                    selected = BackendType.CPU;
                }
            }

            return selected;
        }
    }
}
