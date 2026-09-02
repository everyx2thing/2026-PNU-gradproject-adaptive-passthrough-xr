#if ADAPTIVE_PASSTHROUGH_QUEST_CAMERA
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Stopwatch = System.Diagnostics.Stopwatch;
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
        [SerializeField] private bool benchmarkCpuGpuOnQuest;
        [SerializeField, Range(5, 30)] private int backendBenchmarkSamples = 12;
        [SerializeField, Range(0f, 1f)] private float confidenceThreshold = 0.55f;
        [SerializeField, Range(0f, 1f)] private float trackingConfidenceThreshold = 0.35f;
        [SerializeField, Range(0f, 1f)] private float iouThreshold = 0.45f;
        [SerializeField, Min(1f)] private float inferenceRateHz = 3f;
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
        private IEnumerator inferenceSchedule;
        private Tensor<float> activeInput;
        private double activeCaptureTimestampSeconds;
        private double activeCaptureRealtimeSeconds;
        private DateTime activeCaptureTimestamp;
        private long activeInferenceStartedTimestamp;
        private float activeInferenceMilliseconds;
        private int activeScheduledLayerCount;
        private int expectedScheduledLayerCount = 864;
        private float activeSliceBudgetMilliseconds;
        private string watchdogState = "idle";
        private bool watchdogWarningLogged;
        private int inferenceGeneration;
        private bool applicationPaused;
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
            applicationPaused = false;
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

            CancelInferenceAndWorker(false);
            depthProvider?.ResetProvider();
            controller?.ResetPipeline();
            qualityController?.ResetRuntimeMeasurements();
            worker?.Dispose();
            worker = null;
            runtimeModel = null;
        }

        private void Update()
        {
            UpdateInferenceDiagnostics();
            if (inferenceInProgress)
            {
                float wallMilliseconds = ActiveInferenceWallMilliseconds();
                InferenceWatchdogState state =
                    InferenceSchedulerPolicy.GetWatchdogState(
                        true,
                        wallMilliseconds);
                watchdogState = state.ToString().ToLowerInvariant();
                if (state == InferenceWatchdogState.Reset)
                {
                    AbortStalledInference(wallMilliseconds);
                    return;
                }

                if (state == InferenceWatchdogState.Recovery
                    && !watchdogWarningLogged)
                {
                    watchdogWarningLogged = true;
                    Debug.LogWarning(
                        "[PersonDetection] Inference exceeded 0.75s; "
                        + "temporarily increasing the CPU slice budget.");
                }

                if (inferenceSchedule != null)
                {
                    AdvanceInferenceSchedule();
                }

                return;
            }

            if (worker == null
                || controller == null
                || cameraAccess == null
                || !cameraAccess.IsPlaying
                || applicationPaused)
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

            StartInference(now);
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            CancelInferenceAndWorker(paused);
            depthProvider?.ResetProvider();
            controller?.ResetPipeline();
            qualityController?.ResetRuntimeMeasurements();
            if (!paused && isActiveAndEnabled)
            {
                shuttingDown = false;
                TryCreateWorker();
                nextInferenceAt = 0.0;
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                OnApplicationPause(true);
            }
            else if (applicationPaused)
            {
                OnApplicationPause(false);
            }
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
            activeBackend = BackendType.CPU;
            backend = BackendType.CPU;
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

        private void StartInference(double scheduledTimestampSeconds)
        {
            inferenceInProgress = true;
            activeCaptureTimestampSeconds = scheduledTimestampSeconds;
            activeCaptureRealtimeSeconds = scheduledTimestampSeconds;
            activeInferenceStartedTimestamp = Stopwatch.GetTimestamp();
            activeInferenceMilliseconds = 0f;
            activeScheduledLayerCount = 0;
            activeSliceBudgetMilliseconds = qualityController == null
                ? TrackingQualitySettings.For(
                    TrackingQualityProfile.Balanced)
                    .inferenceSliceMilliseconds
                : qualityController.EffectiveSettings
                    .inferenceSliceMilliseconds;
            watchdogState = "normal";
            watchdogWarningLogged = false;
            inferenceGeneration++;
            try
            {
                Texture cameraTexture = cameraAccess.GetTexture();
                if (cameraTexture == null)
                {
                    FinishInference(inferenceGeneration, false);
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

                DateTime captureTimestamp = cameraAccess.Timestamp;
                Pose cameraPoseAtCapture = cameraAccess.GetCameraPose();
                activeCaptureTimestamp = captureTimestamp;
                double observedRealtimeSeconds =
                    Time.realtimeSinceStartupAsDouble;
                activeCaptureRealtimeSeconds = CaptureRealtimeSeconds(
                    captureTimestamp,
                    DateTime.UtcNow,
                    observedRealtimeSeconds);
                activeCaptureTimestampSeconds = activeCaptureRealtimeSeconds;
                if (depthProvider != null)
                {
                    depthProvider.BeginFrame(
                        activeCaptureTimestampSeconds,
                        cameraPoseAtCapture);
                }

                activeInput = new Tensor<float>(modelInputShape);
                TextureTransform transform = new TextureTransform();
                TextureConverter.ToTensor(
                    cameraTexture,
                    activeInput,
                    transform);
                inferenceSchedule = worker.ScheduleIterable(activeInput);
                qualityController?.SetInferenceActive(
                    true,
                    activeBackend.ToString());
            }
            catch (Exception exception)
            {
                if (!shuttingDown)
                {
                    Debug.LogError(
                        "[DynamicRisk] Inference start failed: "
                        + exception.Message);
                }

                FinishInference(inferenceGeneration, false);
            }
        }

        private void AdvanceInferenceSchedule()
        {
            TrackingQualitySettings settings = qualityController == null
                ? TrackingQualitySettings.For(TrackingQualityProfile.Balanced)
                : qualityController.EffectiveSettings;
            float sliceMilliseconds = Mathf.Max(
                0.1f,
                settings.inferenceSliceMilliseconds);
            int maximumLayers = Mathf.Max(
                1,
                settings.maximumLayersPerFrame);
            float wallMilliseconds = ActiveInferenceWallMilliseconds();
            float projectedMilliseconds =
                InferenceSchedulerPolicy.EstimateCompletionMilliseconds(
                    wallMilliseconds,
                    activeScheduledLayerCount,
                    expectedScheduledLayerCount);
            float frameP95 = qualityController == null
                ? 0f
                : qualityController.GetSnapshot().FrameP95Milliseconds;
            activeSliceBudgetMilliseconds =
                InferenceSchedulerPolicy.UpdateSliceBudget(
                    settings.profile,
                    sliceMilliseconds,
                    activeSliceBudgetMilliseconds,
                    frameP95,
                    projectedMilliseconds,
                    wallMilliseconds);
            sliceMilliseconds = activeSliceBudgetMilliseconds;
            long sliceStartedAt = Stopwatch.GetTimestamp();
            bool hasMore = true;
            int layers = 0;
            try
            {
                while (layers < maximumLayers)
                {
                    hasMore = inferenceSchedule.MoveNext();
                    if (!hasMore)
                    {
                        break;
                    }

                    layers++;
                    if (ElapsedMilliseconds(sliceStartedAt)
                        >= sliceMilliseconds)
                    {
                        break;
                    }
                }

                activeScheduledLayerCount += layers;
                activeInferenceMilliseconds += ElapsedMilliseconds(
                    sliceStartedAt);
                if (!hasMore)
                {
                    expectedScheduledLayerCount = Mathf.Max(
                        1,
                        activeScheduledLayerCount);
                    (inferenceSchedule as IDisposable)?.Dispose();
                    inferenceSchedule = null;
                    CompleteInferenceAsync(inferenceGeneration);
                }
            }
            catch (Exception exception)
            {
                if (!shuttingDown)
                {
                    Debug.LogError(
                        "[DynamicRisk] Inference scheduling failed: "
                        + exception.Message);
                }

                FinishInference(inferenceGeneration, false);
            }
        }

        private async void CompleteInferenceAsync(int generation)
        {
            bool completedSuccessfully = false;
            try
            {

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
                    if (shuttingDown
                        || applicationPaused
                        || generation != inferenceGeneration
                        || controller == null)
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
                    qualityController?.RecordInferenceDiagnostics(
                        IsCameraReady,
                        result.RawCandidateCount,
                        result.PersonCandidateCount,
                        result.BelowConfidenceCount,
                        result.InvalidBoxCount,
                        Mathf.Max(
                            0,
                            result.RawCandidateCount
                                - result.PersonCandidateCount),
                        watchdogState,
                        activeSliceBudgetMilliseconds);
                    IReadOnlyList<DynamicObjectDetection> submittedDetections =
                        result.Detections;
                    DynamicRiskFrame frame = depthProvider == null
                        ? controller.SubmitDetections(
                            activeCaptureTimestampSeconds,
                            submittedDetections,
                            null,
                            activeCaptureRealtimeSeconds)
                        : controller.SubmitDetections(
                            activeCaptureTimestampSeconds,
                            submittedDetections,
                            depthProvider.Measure,
                            activeCaptureRealtimeSeconds);
                    if (depthProvider != null)
                    {
                        liveDepthTrackIds.Clear();
                        for (int i = 0; i < frame.LiveTrackIds.Count; i++)
                        {
                            liveDepthTrackIds.Add(frame.LiveTrackIds[i]);
                        }

                        depthProvider.PruneExcept(liveDepthTrackIds);
                    }

                    if (qualityController != null)
                    {
                        DynamicRiskAssessment primary =
                            SelectPrimaryAssessment(frame.Assessments);
                        if (primary == null)
                        {
                            qualityController.ClearPerson();
                        }
                        else
                        {
                            qualityController.RecordPerson(
                                primary.TrackId,
                                primary.Location.RawDistanceMeters,
                                primary.Location.FilteredDistanceMeters,
                                primary.Motion.State.ToString(),
                                primary.MissingSeconds,
                                primary.Location.IsMetricReliable,
                                primary.Location.DepthRejectedReason,
                                primary.Location.SafetyDistanceMeters,
                                primary.Location.SafetyDistanceConfidence,
                                primary.Location.TorsoSupportCount,
                                primary.Location.SafetySupportCount,
                                primary.Location.ClusterSelectionReason);
                            if (depthProvider != null
                                && depthProvider.TryGetSamplingSnapshot(
                                    primary.TrackId,
                                    out PersonDepthSamplingSnapshot samples))
                            {
                                qualityController.RecordPersonDepthSamples(
                                    samples);
                            }
                        }
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
                    completedSuccessfully = true;
                }
            }
            catch (Exception exception)
            {
                if (!shuttingDown
                    && !applicationPaused
                    && generation == inferenceGeneration)
                {
                    Debug.LogError("[DynamicRisk] Inference failed: " + exception.Message);
                }
            }
            finally
            {
                FinishInference(generation, completedSuccessfully);
            }
        }

        private void FinishInference(int generation, bool recordMetrics)
        {
            if (generation != inferenceGeneration)
            {
                return;
            }

            float wallMilliseconds = ActiveInferenceWallMilliseconds();
            float captureAgeMilliseconds = Mathf.Max(
                0f,
                wallMilliseconds);
            if (activeCaptureTimestamp != default)
            {
                double measuredCaptureAge =
                    (DateTime.UtcNow
                        - activeCaptureTimestamp.ToUniversalTime())
                    .TotalMilliseconds;
                if (measuredCaptureAge >= 0.0
                    && measuredCaptureAge <= 60000.0)
                {
                    captureAgeMilliseconds = (float)measuredCaptureAge;
                }
            }
            (inferenceSchedule as IDisposable)?.Dispose();
            inferenceSchedule = null;
            activeInput?.Dispose();
            activeInput = null;
            inferenceInProgress = false;
            activeCaptureTimestamp = default;
            activeCaptureRealtimeSeconds = 0.0;
            watchdogState = "idle";
            qualityController?.SetInferenceActive(
                false,
                activeBackend.ToString());
            if (!recordMetrics || applicationPaused || shuttingDown)
            {
                return;
            }

            qualityController?.RecordInference(
                activeCaptureTimestampSeconds,
                activeInferenceMilliseconds,
                wallMilliseconds,
                captureAgeMilliseconds,
                activeScheduledLayerCount,
                activeBackend.ToString(),
                false);
            RecordBackendBenchmark(wallMilliseconds);
        }

        private static DynamicRiskAssessment SelectPrimaryAssessment(
            IReadOnlyList<DynamicRiskAssessment> assessments)
        {
            DynamicRiskAssessment selected = null;
            if (assessments == null)
            {
                return null;
            }

            for (int i = 0; i < assessments.Count; i++)
            {
                DynamicRiskAssessment candidate = assessments[i];
                if (candidate == null)
                {
                    continue;
                }

                if (selected == null
                    || candidate.ObservedThisFrame
                        && !selected.ObservedThisFrame
                    || candidate.ObservedThisFrame
                        == selected.ObservedThisFrame
                        && candidate.ForcePassthrough
                        && !selected.ForcePassthrough
                    || candidate.ObservedThisFrame
                        == selected.ObservedThisFrame
                        && candidate.ForcePassthrough
                        == selected.ForcePassthrough
                        && candidate.Score > selected.Score)
                {
                    selected = candidate;
                }
            }

            return selected;
        }

        private void CancelInferenceAndWorker(bool recreateAfterResume)
        {
            inferenceGeneration++;
            (inferenceSchedule as IDisposable)?.Dispose();
            inferenceSchedule = null;
            activeInput?.Dispose();
            activeInput = null;
            inferenceInProgress = false;
            activeCaptureTimestamp = default;
            activeCaptureRealtimeSeconds = 0.0;
            watchdogState = "idle";
            qualityController?.SetInferenceActive(
                false,
                activeBackend.ToString());
            worker?.Dispose();
            worker = null;
            if (!recreateAfterResume)
            {
                runtimeModel = null;
            }
        }

        private void AbortStalledInference(float wallMilliseconds)
        {
            watchdogState = "reset";
            Debug.LogError(
                string.Format(
                    "[PersonDetection] Inference watchdog reset after {0:F0}ms "
                    + "({1} scheduled layers).",
                    wallMilliseconds,
                    activeScheduledLayerCount));
            UpdateInferenceDiagnostics();
            CancelInferenceAndWorker(true);
            if (!applicationPaused && !shuttingDown && isActiveAndEnabled)
            {
                TryCreateWorker();
                nextInferenceAt = 0.0;
            }
        }

        private void UpdateInferenceDiagnostics()
        {
            PersonDetectionPostProcessResult result = LastPostProcessResult;
            int raw = result == null ? 0 : result.RawCandidateCount;
            int persons = result == null ? 0 : result.PersonCandidateCount;
            int confidenceRejected = result == null
                ? 0
                : result.BelowConfidenceCount;
            int boxRejected = result == null ? 0 : result.InvalidBoxCount;
            qualityController?.RecordInferenceDiagnostics(
                IsCameraReady,
                raw,
                persons,
                confidenceRejected,
                boxRejected,
                Mathf.Max(0, raw - persons),
                watchdogState,
                activeSliceBudgetMilliseconds);
        }

        private float ActiveInferenceWallMilliseconds()
        {
            return activeInferenceStartedTimestamp <= 0
                ? 0f
                : ElapsedMilliseconds(activeInferenceStartedTimestamp);
        }

        private static float ElapsedMilliseconds(long startedTimestamp)
        {
            long elapsed = Stopwatch.GetTimestamp() - startedTimestamp;
            return Mathf.Max(
                0f,
                (float)(elapsed * 1000.0 / Stopwatch.Frequency));
        }

        public static double CaptureRealtimeSeconds(
            DateTime captureTimestamp,
            DateTime utcNow,
            double realtimeNowSeconds)
        {
            double safeRealtimeNow = Math.Max(0.0, realtimeNowSeconds);
            if (captureTimestamp == default || utcNow == default)
            {
                return safeRealtimeNow;
            }

            double ageSeconds = (utcNow.ToUniversalTime()
                    - captureTimestamp.ToUniversalTime())
                .TotalSeconds;
            if (double.IsNaN(ageSeconds)
                || double.IsInfinity(ageSeconds)
                || ageSeconds < 0.0
                || ageSeconds > 60.0)
            {
                return safeRealtimeNow;
            }

            return Math.Max(0.0, safeRealtimeNow - ageSeconds);
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
