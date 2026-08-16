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
        private const float SlowDurationSeconds = 2f;
        private const float RecoveryDurationSeconds = 5f;
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

        private float spatialMilliseconds;
        private float measuredSpatialRateHz;
        private double previousSpatialAt;
        private SpatialObstacleMeasurement spatialMeasurement;
        private float inferenceMilliseconds;
        private float measuredInferenceRateHz;
        private double previousInferenceAt;
        private int personTrackId;
        private float personRawDistance;
        private float personFilteredDistance;
        private string personMotion = "Unavailable";
        private float personMissingSeconds;

        public event Action<TrackingQualityProfile> ProfileChanged;
        public event Action<string, double> TestMarkerRequested;

        public TrackingQualityProfile Profile => profile;
        public int AdaptiveLevel => adaptiveLevel;
        public TrackingQualitySettings EffectiveSettings =>
            CalculateEffectiveSettings(profile, adaptiveLevel);

        private void Awake()
        {
            profile = LoadProfile();
        }

        private void Update()
        {
            float deltaSeconds = Mathf.Max(0.00001f, Time.unscaledDeltaTime);
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
            else
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

        public void RecordInference(
            double captureTimestampSeconds,
            float elapsedMilliseconds)
        {
            measuredInferenceRateHz = SmoothedRate(
                measuredInferenceRateHz,
                previousInferenceAt,
                captureTimestampSeconds);
            previousInferenceAt = captureTimestampSeconds;
            inferenceMilliseconds = Mathf.Max(0f, elapsedMilliseconds);
        }

        public void RecordPerson(
            int trackId,
            float rawDistanceMeters,
            float filteredDistanceMeters,
            string motion,
            float missingSeconds)
        {
            personTrackId = Mathf.Max(0, trackId);
            personRawDistance = Mathf.Max(0f, rawDistanceMeters);
            personFilteredDistance = Mathf.Max(0f, filteredDistanceMeters);
            personMotion = motion ?? "Unavailable";
            personMissingSeconds = Mathf.Max(0f, missingSeconds);
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
                measuredInferenceRateHz > 0f
                    ? measuredInferenceRateHz
                    : settings.personInferenceRateHz,
                inferenceMilliseconds,
                personTrackId,
                personRawDistance,
                personFilteredDistance,
                personMotion,
                personMissingSeconds,
                measuredFps,
                frameP95);
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
                    Mathf.Max(10f, Mathf.Min(selected.spatialRateHz, 20f)),
                    Mathf.Max(6, Mathf.Min(selected.spatialRayCount, 12)),
                    Mathf.Max(3f, Mathf.Min(selected.personInferenceRateHz, 5f)));
            }

            return new TrackingQualitySettings(
                selectedProfile,
                10f,
                6,
                3f);
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
