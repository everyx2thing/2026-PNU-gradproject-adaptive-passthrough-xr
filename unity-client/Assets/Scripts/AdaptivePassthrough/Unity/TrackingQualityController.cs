using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class TrackingQualityController : MonoBehaviour
    {
        public const string PlayerPrefsKey =
            "adaptive_passthrough.tracking_quality_profile.v1";

        private const float FrameBudgetMilliseconds = 13.9f;
        private const float RecoveryBudgetMilliseconds = 12.5f;
        private const float SlowDurationSeconds = 2f;
        private const float RecoveryDurationSeconds = 5f;
        private const float MaximumAcceptedFrameSeconds = 0.25f;
        private const int FrameWindowCapacity = 256;

        [SerializeField] private TrackingQualityProfile profile =
            TrackingQualityProfile.Balanced;
        [SerializeField] private bool adaptiveRateEnabled = true;

        private readonly float[] frameMilliseconds =
            new float[FrameWindowCapacity];
        private readonly float[] p95Scratch = new float[FrameWindowCapacity];
        private int frameSampleCount;
        private int frameSampleCursor;
        private int adaptiveLevel;
        private float overBudgetSeconds;
        private float stableSeconds;
        private float measuredFps;
        private float frameP95;
        private double lastFrameMetricsAt;
        private bool applicationPaused;

        private float spatialMilliseconds;
        private float measuredSpatialRateHz;
        private double previousSpatialAt;
        private SpatialObstacleMeasurement spatialMeasurement;
        private float inferenceMilliseconds;
        private float inferenceActiveMilliseconds;
        private float inferenceWallMilliseconds;
        private float inferenceCaptureAgeMilliseconds;
        private int inferenceScheduledLayerCount;
        private string inferenceBackend = "Unavailable";
        private bool inferenceActive;
        private float measuredInferenceRateHz;
        private double previousInferenceAt;
        private bool hasInferenceMeasurement;
        private int personTrackId;
        private float personRawDistance;
        private float personFilteredDistance;
        private string personMotion = "Unavailable";
        private float personMissingSeconds;
        private bool personMetricReliable;
        private string personDepthRejectedReason = string.Empty;
        private SpatialOwnerDiagnostics headSpatial;
        private SpatialOwnerDiagnostics leftHandSpatial;
        private SpatialOwnerDiagnostics rightHandSpatial;
        private bool cameraReady;
        private int rawCandidateCount;
        private int personCandidateCount;
        private int confidenceRejectedCount;
        private int boxRejectedCount;
        private int classRejectedCount;
        private string inferenceWatchdogState = "idle";
        private float inferenceSliceBudgetMilliseconds;

        public event Action<TrackingQualityProfile> ProfileChanged;
        public event Action<string, double> TestMarkerRequested;
        public event Action<TrackingTestMarker> ScenarioMarkerRequested;

        private string activeScenarioId = string.Empty;
        private string activeScenario = string.Empty;
        private int scenarioSequence;

        public TrackingQualityProfile Profile => profile;
        public int AdaptiveLevel => adaptiveLevel;
        public string ActiveScenarioId => activeScenarioId;
        public string ActiveScenario => activeScenario;
        public TrackingQualitySettings EffectiveSettings =>
            CalculateEffectiveSettings(profile, adaptiveLevel);

        private void Awake()
        {
            profile = LoadProfile();
        }

        private void Update()
        {
            if (applicationPaused)
            {
                return;
            }

            float deltaSeconds = Mathf.Max(0.00001f, Time.unscaledDeltaTime);
            if (deltaSeconds > MaximumAcceptedFrameSeconds)
            {
                ResetFrameStatistics();
                return;
            }

            float milliseconds = deltaSeconds * 1000f;
            frameMilliseconds[frameSampleCursor] = milliseconds;
            frameSampleCursor = (frameSampleCursor + 1) % FrameWindowCapacity;
            frameSampleCount = Mathf.Min(
                FrameWindowCapacity,
                frameSampleCount + 1);

            double now = Time.realtimeSinceStartupAsDouble;
            if (now - lastFrameMetricsAt >= 0.5)
            {
                measuredFps = 1000f / Mathf.Max(0.001f, AverageFrameTime());
                frameP95 = Percentile95FrameTime();
                lastFrameMetricsAt = now;
            }

            if (!adaptiveRateEnabled)
            {
                adaptiveLevel = 0;
                overBudgetSeconds = 0f;
                stableSeconds = 0f;
                return;
            }

            if (frameP95 > FrameBudgetMilliseconds)
            {
                overBudgetSeconds += deltaSeconds;
                stableSeconds = 0f;
                if (overBudgetSeconds >= SlowDurationSeconds
                    && adaptiveLevel < 2)
                {
                    adaptiveLevel++;
                    overBudgetSeconds = 0f;
                }
            }
            else if (frameP95 <= RecoveryBudgetMilliseconds)
            {
                overBudgetSeconds = 0f;
                stableSeconds += deltaSeconds;
                if (stableSeconds >= RecoveryDurationSeconds
                    && adaptiveLevel > 0)
                {
                    adaptiveLevel--;
                    stableSeconds = 0f;
                }
            }
            else
            {
                overBudgetSeconds = 0f;
                stableSeconds = 0f;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            ResetRuntimeMeasurements();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                applicationPaused = true;
                ResetRuntimeMeasurements();
            }
            else
            {
                applicationPaused = false;
                ResetRuntimeMeasurements();
            }
        }

        public void SetProfile(TrackingQualityProfile value)
        {
            if (!Enum.IsDefined(typeof(TrackingQualityProfile), value))
            {
                value = TrackingQualityProfile.Balanced;
            }

            profile = value;
            adaptiveLevel = 0;
            PlayerPrefs.SetInt(PlayerPrefsKey, (int)profile);
            PlayerPrefs.Save();
            ProfileChanged?.Invoke(profile);
        }

        public void ResetSavedProfile()
        {
            PlayerPrefs.DeleteKey(PlayerPrefsKey);
            SetProfile(TrackingQualityProfile.Balanced);
        }

        public void RecordSpatial(
            SpatialObstacleMeasurement measurement,
            float elapsedMilliseconds)
        {
            double now = measurement.TimestampSeconds;
            measuredSpatialRateHz = SmoothedRate(
                measuredSpatialRateHz,
                previousSpatialAt,
                now);
            previousSpatialAt = now;
            spatialMilliseconds = Mathf.Max(0f, elapsedMilliseconds);
            spatialMeasurement = measurement;
        }

        public void RecordSpatialOwners(
            SpatialObstacleMeasurement headMeasurement,
            SpatialObstacleMeasurement leftHandMeasurement,
            SpatialObstacleMeasurement rightHandMeasurement)
        {
            headSpatial = new SpatialOwnerDiagnostics(
                SpatialProbeOwner.Head,
                headMeasurement);
            leftHandSpatial = new SpatialOwnerDiagnostics(
                SpatialProbeOwner.LeftHand,
                leftHandMeasurement);
            rightHandSpatial = new SpatialOwnerDiagnostics(
                SpatialProbeOwner.RightHand,
                rightHandMeasurement);
        }

        public void RecordInference(
            double captureTimestampSeconds,
            float elapsedMilliseconds)
        {
            RecordInference(
                captureTimestampSeconds,
                elapsedMilliseconds,
                elapsedMilliseconds,
                0f,
                0,
                "Unavailable",
                false);
        }

        public void RecordInference(
            double captureTimestampSeconds,
            float activeMilliseconds,
            float wallMilliseconds,
            float captureAgeMilliseconds,
            int scheduledLayerCount,
            string backendName,
            bool isActive)
        {
            measuredInferenceRateHz = SmoothedRate(
                measuredInferenceRateHz,
                previousInferenceAt,
                captureTimestampSeconds);
            previousInferenceAt = captureTimestampSeconds;
            hasInferenceMeasurement = true;
            inferenceMilliseconds = Mathf.Max(0f, activeMilliseconds);
            inferenceActiveMilliseconds = Mathf.Max(0f, activeMilliseconds);
            inferenceWallMilliseconds = Mathf.Max(0f, wallMilliseconds);
            inferenceCaptureAgeMilliseconds = Mathf.Max(
                0f,
                captureAgeMilliseconds);
            inferenceScheduledLayerCount = Mathf.Max(
                0,
                scheduledLayerCount);
            inferenceBackend = string.IsNullOrWhiteSpace(backendName)
                ? "Unavailable"
                : backendName;
            inferenceActive = isActive;
        }

        public void SetInferenceActive(bool value, string backendName)
        {
            inferenceActive = value;
            if (!string.IsNullOrWhiteSpace(backendName))
            {
                inferenceBackend = backendName;
            }
        }

        public void RecordInferenceDiagnostics(
            bool isCameraReady,
            int rawCandidates,
            int personCandidates,
            int confidenceRejected,
            int boxRejected,
            int classRejected,
            string watchdogState,
            float sliceBudgetMilliseconds)
        {
            cameraReady = isCameraReady;
            rawCandidateCount = Mathf.Max(0, rawCandidates);
            personCandidateCount = Mathf.Max(0, personCandidates);
            confidenceRejectedCount = Mathf.Max(0, confidenceRejected);
            boxRejectedCount = Mathf.Max(0, boxRejected);
            classRejectedCount = Mathf.Max(0, classRejected);
            inferenceWatchdogState = string.IsNullOrWhiteSpace(watchdogState)
                ? "idle"
                : watchdogState;
            inferenceSliceBudgetMilliseconds = Mathf.Max(
                0f,
                sliceBudgetMilliseconds);
        }

        public void RecordPerson(
            int trackId,
            float rawDistanceMeters,
            float filteredDistanceMeters,
            string motion,
            float missingSeconds,
            bool metricReliable = false,
            string depthRejectedReason = null)
        {
            personTrackId = Mathf.Max(0, trackId);
            personRawDistance = Mathf.Max(0f, rawDistanceMeters);
            personFilteredDistance = Mathf.Max(0f, filteredDistanceMeters);
            personMotion = motion ?? "Unavailable";
            personMissingSeconds = Mathf.Max(0f, missingSeconds);
            personMetricReliable = metricReliable;
            personDepthRejectedReason = metricReliable
                ? string.Empty
                : depthRejectedReason ?? string.Empty;
        }

        public void ClearPerson()
        {
            personTrackId = 0;
            personRawDistance = 0f;
            personFilteredDistance = 0f;
            personMotion = "Unavailable";
            personMissingSeconds = 0f;
            personMetricReliable = false;
            personDepthRejectedReason = string.Empty;
        }

        public void AddTestMarker(string marker)
        {
            if (string.IsNullOrWhiteSpace(marker))
            {
                return;
            }

            TestMarkerRequested?.Invoke(
                marker.Trim(),
                Time.realtimeSinceStartupAsDouble);
            StartTestScenario(marker.Trim());
        }

        public void StartTestScenario(string scenario)
        {
            if (string.IsNullOrWhiteSpace(scenario))
            {
                return;
            }

            scenarioSequence++;
            activeScenario = scenario.Trim();
            activeScenarioId = string.Format(
                "{0}-{1:D3}",
                activeScenario,
                scenarioSequence);
            EmitScenarioMarker("START", -1f);
        }

        public void AddGroundTruthMarker(float distanceMeters)
        {
            if (string.IsNullOrEmpty(activeScenarioId))
            {
                StartTestScenario("unassigned");
            }

            EmitScenarioMarker("SAMPLE", Mathf.Max(0f, distanceMeters));
        }

        public void EndTestScenario()
        {
            if (string.IsNullOrEmpty(activeScenarioId))
            {
                return;
            }

            EmitScenarioMarker("END", -1f);
            activeScenarioId = string.Empty;
            activeScenario = string.Empty;
        }

        public TrackingDiagnosticsSnapshot GetSnapshot()
        {
            TrackingQualitySettings settings = EffectiveSettings;
            return new TrackingDiagnosticsSnapshot(
                profile,
                adaptiveLevel,
                measuredSpatialRateHz > 0f
                    ? measuredSpatialRateHz
                    : settings.spatialRateHz,
                settings.spatialRayCount,
                spatialMilliseconds,
                spatialMeasurement.Source,
                spatialMeasurement.DistanceMeters,
                spatialMeasurement.Confidence,
                measuredInferenceRateHz,
                inferenceMilliseconds,
                personTrackId,
                personRawDistance,
                personFilteredDistance,
                personMotion,
                personMissingSeconds,
                measuredFps,
                frameP95,
                inferenceActiveMilliseconds,
                inferenceWallMilliseconds,
                inferenceCaptureAgeMilliseconds,
                inferenceScheduledLayerCount,
                inferenceBackend,
                inferenceActive,
                applicationPaused,
                personMetricReliable,
                personDepthRejectedReason,
                headSpatial,
                leftHandSpatial,
                rightHandSpatial,
                cameraReady,
                rawCandidateCount,
                personCandidateCount,
                confidenceRejectedCount,
                boxRejectedCount,
                classRejectedCount,
                inferenceWatchdogState,
                inferenceSliceBudgetMilliseconds,
                settings.personInferenceRateHz,
                measuredInferenceRateHz,
                hasInferenceMeasurement);
        }

        public static TrackingQualityProfile LoadProfile()
        {
            int value = PlayerPrefs.GetInt(
                PlayerPrefsKey,
                (int)TrackingQualityProfile.Balanced);
            return Enum.IsDefined(typeof(TrackingQualityProfile), value)
                ? (TrackingQualityProfile)value
                : TrackingQualityProfile.Balanced;
        }

        public static TrackingQualitySettings CalculateEffectiveSettings(
            TrackingQualityProfile selectedProfile,
            int degradationLevel)
        {
            TrackingQualitySettings selected =
                TrackingQualitySettings.For(selectedProfile);
            int level = Mathf.Clamp(degradationLevel, 0, 2);
            if (level == 0)
            {
                return selected;
            }

            if (level == 1)
            {
                return new TrackingQualitySettings(
                    selectedProfile,
                    selected.spatialRateHz,
                    selected.spatialRayCount,
                    Mathf.Max(3f, selected.personInferenceRateHz),
                    Mathf.Min(selected.inferenceSliceMilliseconds, 1f),
                    Mathf.Min(selected.maximumLayersPerFrame, 64));
            }

            return new TrackingQualitySettings(
                selectedProfile,
                10f,
                12,
                3f,
                0.75f,
                48);
        }

        public void ResetRuntimeMeasurements()
        {
            ResetFrameStatistics();
            measuredSpatialRateHz = 0f;
            previousSpatialAt = 0.0;
            measuredInferenceRateHz = 0f;
            previousInferenceAt = 0.0;
            hasInferenceMeasurement = false;
            inferenceMilliseconds = 0f;
            inferenceActiveMilliseconds = 0f;
            inferenceWallMilliseconds = 0f;
            inferenceCaptureAgeMilliseconds = 0f;
            inferenceScheduledLayerCount = 0;
            inferenceActive = false;
            cameraReady = false;
            rawCandidateCount = 0;
            personCandidateCount = 0;
            confidenceRejectedCount = 0;
            boxRejectedCount = 0;
            classRejectedCount = 0;
            inferenceWatchdogState = "idle";
            inferenceSliceBudgetMilliseconds = 0f;
        }

        private void ResetFrameStatistics()
        {
            Array.Clear(frameMilliseconds, 0, frameMilliseconds.Length);
            frameSampleCount = 0;
            frameSampleCursor = 0;
            measuredFps = 0f;
            frameP95 = 0f;
            overBudgetSeconds = 0f;
            stableSeconds = 0f;
            lastFrameMetricsAt = Time.realtimeSinceStartupAsDouble;
        }

        private void EmitScenarioMarker(string phase, float distanceMeters)
        {
            ScenarioMarkerRequested?.Invoke(
                new TrackingTestMarker(
                    activeScenarioId,
                    activeScenario,
                    phase,
                    distanceMeters,
                    Time.realtimeSinceStartupAsDouble));
        }

        private float AverageFrameTime()
        {
            if (frameSampleCount <= 0)
            {
                return 0f;
            }

            float sum = 0f;
            for (int i = 0; i < frameSampleCount; i++)
            {
                sum += frameMilliseconds[i];
            }

            return sum / frameSampleCount;
        }

        private float Percentile95FrameTime()
        {
            if (frameSampleCount <= 0)
            {
                return 0f;
            }

            Array.Copy(
                frameMilliseconds,
                p95Scratch,
                frameSampleCount);
            Array.Sort(p95Scratch, 0, frameSampleCount);
            int index = Mathf.Clamp(
                Mathf.CeilToInt(frameSampleCount * 0.95f) - 1,
                0,
                frameSampleCount - 1);
            return p95Scratch[index];
        }

        private static float SmoothedRate(
            float previousRate,
            double previousTimestamp,
            double currentTimestamp)
        {
            double elapsed = currentTimestamp - previousTimestamp;
            if (previousTimestamp <= 0.0 || elapsed <= 0.0001)
            {
                return previousRate;
            }

            float currentRate = (float)(1.0 / elapsed);
            return previousRate <= 0f
                ? currentRate
                : Mathf.Lerp(previousRate, currentRate, 0.2f);
        }
    }
}
