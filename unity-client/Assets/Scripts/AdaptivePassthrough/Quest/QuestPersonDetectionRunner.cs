#if ADAPTIVE_PASSTHROUGH_QUEST_CAMERA
using System;
using System.Collections.Generic;
using System.Text;
using Meta.XR;
using Unity.InferenceEngine;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    /// <summary>
    /// Quest Passthrough Camera API + Unity Inference Engine adapter.
    /// Enable ADAPTIVE_PASSTHROUGH_QUEST_CAMERA only after installing MRUK and
    /// com.unity.ai.inference and assigning the three-output YOLO model.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QuestPersonDetectionRunner : MonoBehaviour
    {
        [SerializeField] private DynamicRiskController controller;
        [SerializeField] private QuestCameraPermissionCoordinator permissionCoordinator;
        [SerializeField] private PassthroughCameraAccess cameraAccess;
        [SerializeField] private QuestPersonDepthProvider depthProvider;
        [SerializeField] private TrackingQualityController qualityController;
        [SerializeField] private ModelAsset modelAsset;
        [SerializeField] private BackendType backend = BackendType.CPU;
        [SerializeField] private bool benchmarkCpuGpuOnQuest = true;
        [SerializeField, Range(5, 30)] private int backendBenchmarkSamples = 12;
        [SerializeField, Range(0f, 1f)] private float confidenceThreshold = 0.55f;
        [SerializeField, Range(0f, 1f)] private float trackingConfidenceThreshold = 0.35f;
        [SerializeField, Range(0f, 1f)] private float iouThreshold = 0.45f;
        [SerializeField, Min(1f)] private float inferenceRateHz = 10f;
        [SerializeField] private int personClassId;
        [SerializeField] private bool flipVertical;
        [Tooltip("The Meta YOLO sample emits center X, center Y, width, height.")]
        [SerializeField] private bool boxesAreCenterFormat = true;
        [Tooltip("Enable only when the model already emits coordinates in the 0-1 range.")]
        [SerializeField] private bool boxesAreNormalized;
        [SerializeField, Min(1)] private int maximumCandidates = 50;
        [SerializeField, Min(1)] private int maximumDetections = 10;
        [SerializeField, Range(0f, 1f)] private float minimumVisibleFraction = 0.15f;
        [SerializeField, Min(1f)] private float maximumNormalizedDimension = 2f;
        [SerializeField, Min(0)] private int diagnosticInferenceCount = 10;

        private Worker worker;
        private Model runtimeModel;
        private BackendType activeBackend = BackendType.CPU;
        private int backendBenchmarkPhase;
        private int backendSampleCount;
        private float backendFrameP95Maximum;
        private readonly float[] backendDurations = new float[30];
        private readonly float[] backendDurationScratch = new float[30];
        private readonly List<InferenceBackendBenchmark> backendBenchmarks =
            new List<InferenceBackendBenchmark>(2);
        private TensorShape modelInputShape;
        private PersonDetectionPostProcessor postProcessor;
        private int inputWidth;
        private int inputHeight;
        private bool inferenceInProgress;
        private bool shuttingDown;
        private bool outputContractLogged;
        private int completedInferenceCount;
        private double nextInferenceAt;
        private bool cameraResolutionLogged;
        private readonly List<int> liveDepthTrackIds = new List<int>();
        private readonly List<DynamicObjectDetection> enrichedDetections =
            new List<DynamicObjectDetection>(10);

        public event Action<PersonDetectionPostProcessResult> PostProcessCompleted;

        public PersonDetectionPostProcessResult LastPostProcessResult { get; private set; }

        public bool IsCameraReady
        {
            get { return cameraAccess != null && cameraAccess.IsPlaying; }
        }

        private void Awake()
        {
            if (controller == null)
            {
                controller = GetComponent<DynamicRiskController>();
            }

            if (permissionCoordinator == null)
            {
                permissionCoordinator = GetComponent<QuestCameraPermissionCoordinator>();
            }

            if (depthProvider == null)
            {
                depthProvider = GetComponent<QuestPersonDepthProvider>();
            }

            if (qualityController == null)
            {
                qualityController = GetComponent<TrackingQualityController>();
            }
        }

        private void OnEnable()
        {
            shuttingDown = false;
            if (permissionCoordinator != null)
            {
                permissionCoordinator.PermissionResolved += OnPermissionResolved;
            }

            TryCreateWorker();
        }

        private void OnDisable()
        {
            shuttingDown = true;
            if (permissionCoordinator != null)
            {
                permissionCoordinator.PermissionResolved -= OnPermissionResolved;
            }

            inferenceInProgress = false;
            worker?.Dispose();
            worker = null;
            runtimeModel = null;
        }

        private void Update()
        {
            if (worker == null
                || controller == null
                || cameraAccess == null
                || !cameraAccess.IsPlaying
                || inferenceInProgress)
            {
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextInferenceAt)
            {
                return;
            }

            float targetRate = qualityController == null
                ? inferenceRateHz
                : qualityController.EffectiveSettings.personInferenceRateHz;
            double interval = 1.0 / Math.Max(1f, targetRate);
            if (nextInferenceAt <= 0.0)
            {
                nextInferenceAt = now;
            }

            do
            {
                nextInferenceAt += interval;
            }
            while (nextInferenceAt <= now);

            RunInference(now);
        }

        private void OnPermissionResolved(bool granted)
        {
            if (granted)
            {
                TryCreateWorker();
            }
        }

        private void TryCreateWorker()
        {
            if (worker != null || modelAsset == null)
            {
                return;
            }

            Model model = ModelLoader.Load(modelAsset);
            if (model == null || model.inputs == null || model.inputs.Count == 0)
            {
                Debug.LogError("[DynamicRisk] The assigned model has no valid input.");
                return;
            }

            int[] dimensions = model.inputs[0].shape.ToIntArray();
            if (dimensions == null || dimensions.Length < 4)
            {
                Debug.LogError("[DynamicRisk] Expected a four-dimensional NCHW model input.");
                return;
            }

            modelInputShape = new TensorShape(dimensions);
            inputHeight = dimensions[2];
            inputWidth = dimensions[3];
            runtimeModel = model;
            bool runBenchmark = benchmarkCpuGpuOnQuest
                && Application.platform == RuntimePlatform.Android;
            activeBackend = runBenchmark ? BackendType.CPU : backend;
            backendBenchmarkPhase = runBenchmark ? 1 : 0;
            backendSampleCount = 0;
            backendFrameP95Maximum = 0f;
            backendBenchmarks.Clear();
            worker = new Worker(runtimeModel, activeBackend);
            postProcessor = CreatePostProcessor();

            Debug.Log(
                string.Format(
                    "[PersonDetection] model-input=[{0}] outputs={1} "
                    + "boxFormat={2} coordinates={3} newTrack={4:F2} "
                    + "tracking={5:F2} nmsIoU={6:F2}",
                    string.Join(",", dimensions),
                    model.outputs == null ? 0 : model.outputs.Count,
                    boxesAreCenterFormat ? "CenterXYWH" : "CornersXYXY",
                    boxesAreNormalized ? "Normalized" : "ModelPixels",
                    confidenceThreshold,
                    trackingConfidenceThreshold,
                    iouThreshold));
            Debug.Log(
                "[PersonDetection] active-backend=" + activeBackend
                + (runBenchmark ? " (Quest A/B warmup)" : string.Empty));
        }

        private async void RunInference(double scheduledTimestampSeconds)
        {
            inferenceInProgress = true;
            double captureTimestampSeconds = scheduledTimestampSeconds;
            float inferenceStartedAt = Time.realtimeSinceStartup;
            try
            {
                Texture cameraTexture = cameraAccess.GetTexture();
                if (cameraTexture == null)
                {
                    return;
                }

                if (!cameraResolutionLogged)
                {
                    cameraResolutionLogged = true;
                    Debug.Log(
                        string.Format(
                            "[PersonDetection] camera-input={0}x{1} expected=1280x960",
                            cameraTexture.width,
                            cameraTexture.height));
                }

                Pose cameraPoseAtCapture = cameraAccess.GetCameraPose();
                DateTime captureTimestamp = cameraAccess.Timestamp;
                if (captureTimestamp != default)
                {
                    captureTimestampSeconds = captureTimestamp.Ticks
                        / (double)TimeSpan.TicksPerSecond;
                }
                if (depthProvider != null)
                {
                    depthProvider.BeginFrame(
                        captureTimestampSeconds,
                        cameraPoseAtCapture);
                }

                using (Tensor<float> input = new Tensor<float>(modelInputShape))
                {
                    TextureTransform transform = new TextureTransform();
                    TextureConverter.ToTensor(cameraTexture, input, transform);
                    worker.Schedule(input);
                }

                Tensor<float> boxes = worker.PeekOutput(0) as Tensor<float>;
                Tensor<int> classIds = worker.PeekOutput(1) as Tensor<int>;
                Tensor<float> scores = worker.PeekOutput(2) as Tensor<float>;
                if (boxes == null || classIds == null || scores == null)
                {
                    Debug.LogError("[DynamicRisk] Model must expose boxes, class IDs, and scores.");
                    return;
                }

                if (!outputContractLogged)
                {
                    outputContractLogged = true;
                    Debug.Log(
                        string.Format(
                            "[PersonDetection] output-contract boxes={0} classes={1} scores={2}",
                            boxes.shape,
                            classIds.shape,
                            scores.shape));
                }

                using (Tensor<float> boxesCpu = await boxes.ReadbackAndCloneAsync())
                using (Tensor<int> classesCpu = await classIds.ReadbackAndCloneAsync())
                using (Tensor<float> scoresCpu = await scores.ReadbackAndCloneAsync())
                {
                    if (shuttingDown || controller == null)
                    {
                        return;
                    }

                    float[] boxesArray = boxesCpu.DownloadToArray();
                    int[] classesArray = classesCpu.DownloadToArray();
                    float[] scoresArray = scoresCpu.DownloadToArray();
                    if (postProcessor == null)
                    {
                        postProcessor = CreatePostProcessor();
                    }

                    PersonDetectionPostProcessResult result = postProcessor.Process(
                        boxesArray,
                        classesArray,
                        scoresArray,
                        inputWidth,
                        inputHeight);
                    LastPostProcessResult = result;
                    IReadOnlyList<DynamicObjectDetection> submittedDetections =
                        result.Detections;
                    if (depthProvider != null)
                    {
                        enrichedDetections.Clear();
                        for (int i = 0; i < result.Detections.Count; i++)
                        {
                            DynamicObjectDetection detection =
                                result.Detections[i];
                            Vector3 worldPoint;
                            float worldConfidence;
                            bool hasWorldPoint =
                                depthProvider.TryMeasureObservationWorldPoint(
                                    detection.boundingBox,
                                    out worldPoint,
                                    out worldConfidence);
                            enrichedDetections.Add(
                                new DynamicObjectDetection(
                                    detection.label,
                                    detection.confidence,
                                    detection.boundingBox,
                                    detection.classId,
                                    hasWorldPoint,
                                    worldPoint,
                                    worldConfidence));
                        }

                        submittedDetections = enrichedDetections;
                    }
                    DynamicRiskFrame frame = depthProvider == null
                        ? controller.SubmitDetections(
                            captureTimestampSeconds,
                            submittedDetections)
                        : controller.SubmitDetections(
                            captureTimestampSeconds,
                            submittedDetections,
                            depthProvider.Measure);
                    if (depthProvider != null)
                    {
                        liveDepthTrackIds.Clear();
                        for (int i = 0; i < frame.Assessments.Count; i++)
                        {
                            liveDepthTrackIds.Add(
                                frame.Assessments[i].TrackId);
                        }

                        depthProvider.PruneExcept(liveDepthTrackIds);
                    }

                    if (qualityController != null
                        && frame.Assessments.Count > 0)
                    {
                        DynamicRiskAssessment primary = frame.Assessments[0];
                        qualityController.RecordPerson(
                            primary.TrackId,
                            primary.Location.RawDistanceMeters,
                            primary.Location.FilteredDistanceMeters,
                            primary.Motion.State.ToString(),
                            primary.MissingSeconds);
                    }

                    PostProcessCompleted?.Invoke(result);

                    if (completedInferenceCount < diagnosticInferenceCount
                        || result.Detections.Count >= 5)
                    {
                        LogDiagnostics(
                            completedInferenceCount,
                            result,
                            boxesArray,
                            classesArray,
                            scoresArray);
                    }

                    completedInferenceCount++;
                }
            }
            catch (Exception exception)
            {
                if (!shuttingDown)
                {
                    Debug.LogError("[DynamicRisk] Inference failed: " + exception.Message);
                }
            }
            finally
            {
                inferenceInProgress = false;
                float elapsedMilliseconds =
                    (Time.realtimeSinceStartup - inferenceStartedAt) * 1000f;
                qualityController?.RecordInference(
                    captureTimestampSeconds,
                    elapsedMilliseconds);
                RecordBackendBenchmark(elapsedMilliseconds);
            }
        }

        private void RecordBackendBenchmark(float elapsedMilliseconds)
        {
            if (backendBenchmarkPhase <= 0
                || backendBenchmarkPhase > 2
                || runtimeModel == null)
            {
                return;
            }

            int required = Mathf.Clamp(backendBenchmarkSamples, 5, 30);
            if (backendSampleCount < required)
            {
                backendDurations[backendSampleCount++] =
                    Mathf.Max(0f, elapsedMilliseconds);
            }

            if (qualityController != null)
            {
                backendFrameP95Maximum = Mathf.Max(
                    backendFrameP95Maximum,
                    qualityController.GetSnapshot().FrameP95Milliseconds);
            }

            if (backendSampleCount < required)
            {
                return;
            }

            Array.Copy(
                backendDurations,
                backendDurationScratch,
                required);
            Array.Sort(backendDurationScratch, 0, required);
            float median = required % 2 == 0
                ? (backendDurationScratch[required / 2 - 1]
                    + backendDurationScratch[required / 2]) * 0.5f
                : backendDurationScratch[required / 2];
            backendBenchmarks.Add(new InferenceBackendBenchmark(
                activeBackend,
                median,
                backendFrameP95Maximum));

            if (backendBenchmarkPhase == 1)
            {
                backendBenchmarkPhase = 2;
                backendSampleCount = 0;
                backendFrameP95Maximum = 0f;
                if (!TrySwitchBackend(BackendType.GPUCompute))
                {
                    backendBenchmarkPhase = 0;
                    TrySwitchBackend(BackendType.CPU);
                }

                return;
            }

            BackendType selected = InferenceBackendSelector.Select(
                backendBenchmarks,
                13.9f,
                0.10f);
            backendBenchmarkPhase = 0;
            TrySwitchBackend(selected);
            Debug.Log(
                string.Format(
                    "[PersonDetection] Quest A/B selected={0} "
                    + "cpu={1:F1}ms/{2:F1}p95 gpu={3:F1}ms/{4:F1}p95",
                    selected,
                    backendBenchmarks[0].MedianInferenceMilliseconds,
                    backendBenchmarks[0].FrameP95Milliseconds,
                    backendBenchmarks.Count > 1
                        ? backendBenchmarks[1].MedianInferenceMilliseconds
                        : 0f,
                    backendBenchmarks.Count > 1
                        ? backendBenchmarks[1].FrameP95Milliseconds
                        : 0f));
        }

        private bool TrySwitchBackend(BackendType value)
        {
            try
            {
                worker?.Dispose();
                worker = new Worker(runtimeModel, value);
                activeBackend = value;
                backend = value;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[PersonDetection] Backend unavailable: "
                    + value + " - " + exception.Message);
                worker = null;
                return false;
            }
        }

        private PersonDetectionPostProcessor CreatePostProcessor()
        {
            return new PersonDetectionPostProcessor(
                new PersonDetectionPostProcessorSettings
                {
                    boxFormat = boxesAreCenterFormat
                        ? PersonBoxFormat.CenterXYWH
                        : PersonBoxFormat.CornersXYXY,
                    coordinateSpace = boxesAreNormalized
                        ? PersonBoxCoordinateSpace.Normalized
                        : PersonBoxCoordinateSpace.ModelPixels,
                    personClassId = personClassId,
                    confidenceThreshold = confidenceThreshold,
                    trackingConfidenceThreshold =
                        trackingConfidenceThreshold,
                    iouThreshold = iouThreshold,
                    maximumCandidates = maximumCandidates,
                    maximumDetections = maximumDetections,
                    minimumVisibleFraction = minimumVisibleFraction,
                    maximumNormalizedDimension = maximumNormalizedDimension,
                    flipVertical = flipVertical
                });
        }

        private void LogDiagnostics(
            int inferenceIndex,
            PersonDetectionPostProcessResult result,
            IReadOnlyList<float> boxes,
            IReadOnlyList<int> classIds,
            IReadOnlyList<float> scores)
        {
            var message = new StringBuilder();
            message.AppendFormat(
                "[PersonDetection] inference={0} raw={1} person={2} "
                + "belowTracking={3} lowTracking={4} invalid={5} "
                + "nmsSuppressed={6} limited={7} output={8}",
                inferenceIndex,
                result.RawCandidateCount,
                result.PersonCandidateCount,
                result.BelowConfidenceCount,
                result.LowConfidenceTrackingCount,
                result.InvalidBoxCount,
                result.SuppressedByNmsCount,
                result.LimitedCandidateCount,
                result.Detections.Count);

            var indices = new List<int>();
            int count = Math.Min(
                boxes.Count / 4,
                Math.Min(classIds.Count, scores.Count));
            for (int i = 0; i < count; i++)
            {
                if (classIds[i] == personClassId)
                {
                    indices.Add(i);
                }
            }

            indices.Sort((a, b) => scores[b].CompareTo(scores[a]));
            int details = Math.Min(10, indices.Count);
            for (int i = 0; i < details; i++)
            {
                int index = indices[i];
                int offset = index * 4;
                message.AppendFormat(
                    " | raw#{0} score={1:F3} box=({2:F2},{3:F2},{4:F2},{5:F2})",
                    index,
                    scores[index],
                    boxes[offset],
                    boxes[offset + 1],
                    boxes[offset + 2],
                    boxes[offset + 3]);
            }

            Debug.Log(message.ToString());
        }
    }
}
#endif
