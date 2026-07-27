using System;

namespace TeamVR.AdaptivePassthrough
{
    [Serializable]
    public sealed class RiskSnapshotBuilderSettings
    {
        public OverallRiskWeights weights = new OverallRiskWeights();
        public PassthroughDecisionFilterSettings passthrough =
            new PassthroughDecisionFilterSettings();
        public float dynamicStaleAfterSeconds = 0.50f;
    }

    public sealed class RiskSnapshotBuildInput
    {
        public double TimestampSeconds;
        public bool CameraReady;
        public bool RoomSceneReady;
        public bool MotionReady;
        public StaticRiskMeasurement StaticMeasurement =
            StaticRiskMeasurement.Unavailable;
        public UserMotionSnapshot MotionSnapshot;
        public DynamicRiskFrame DynamicFrame;
        public bool IntentReady;
        public float IntentRisk;
        public float IntentConfidence;
    }

    public sealed class RiskSnapshotBuilder
    {
        private readonly RiskSnapshotBuilderSettings settings;
        private readonly PassthroughDecisionFilter passthroughFilter;
        private long sequence;
        private double lastTimestampSeconds;

        public RiskSnapshotBuilder(
            RiskSnapshotBuilderSettings settings = null)
        {
            this.settings = settings ?? new RiskSnapshotBuilderSettings();
            passthroughFilter = new PassthroughDecisionFilter(
                this.settings.passthrough);
        }

        public int InvalidValueCount { get; private set; }

        public RiskSnapshot Build(RiskSnapshotBuildInput input)
        {
            input = input ?? new RiskSnapshotBuildInput();
            double timestamp = SanitizeTimestamp(input.TimestampSeconds);
            StaticRiskSnapshot staticRisk = BuildStatic(
                input.RoomSceneReady,
                input.StaticMeasurement);
            UserStateRiskSnapshot userState = BuildUserState(
                input.MotionReady,
                input.MotionSnapshot);
            DynamicRiskSnapshot dynamicRisk = BuildDynamic(
                timestamp,
                input.CameraReady,
                input.DynamicFrame);
            IntentRiskSnapshot intent = new IntentRiskSnapshot(
                input.IntentReady,
                input.IntentReady ? SanitizeRisk(input.IntentRisk) : 0f,
                input.IntentReady ? SanitizeRisk(input.IntentConfidence) : 0f);

            OverallRiskResult overall = OverallRiskFusion.CalculateAvailable(
                staticRisk.Risk,
                staticRisk.Available,
                userState.Risk,
                userState.Available,
                dynamicRisk.MaximumRisk,
                dynamicRisk.Available,
                intent.Risk,
                intent.Available,
                settings.weights);
            PassthroughDecisionSnapshot passthrough =
                passthroughFilter.Evaluate(
                    timestamp,
                    overall.Available,
                    overall.TotalRisk);

            sequence++;
            lastTimestampSeconds = timestamp;
            var sources = new RiskSourceState(
                input.CameraReady,
                staticRisk.Available,
                userState.Available,
                dynamicRisk.Available,
                intent.Available,
                dynamicRisk.SourceTimestampSeconds,
                dynamicRisk.SourceAgeSeconds);

            return new RiskSnapshot(
                sequence,
                timestamp,
                sources,
                staticRisk,
                userState,
                dynamicRisk,
                intent,
                overall,
                Classify(overall.TotalRisk),
                passthrough);
        }

        public void Reset()
        {
            sequence = 0;
            lastTimestampSeconds = 0.0;
            InvalidValueCount = 0;
            passthroughFilter.Reset();
        }

        private StaticRiskSnapshot BuildStatic(
            bool roomSceneReady,
            StaticRiskMeasurement measurement)
        {
            bool available = roomSceneReady && measurement.Available;
            if (!available)
            {
                return new StaticRiskSnapshot(
                    false,
                    0f,
                    0f,
                    false,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f);
            }

            bool hasTtc =
                measurement.HasTimeToCollision
                && IsFinite(measurement.TimeToCollisionSeconds)
                && measurement.TimeToCollisionSeconds >= 0f;
            return new StaticRiskSnapshot(
                true,
                SanitizeNonNegative(measurement.ClosestDistanceMeters),
                hasTtc
                    ? SanitizeNonNegative(measurement.TimeToCollisionSeconds)
                    : 0f,
                hasTtc,
                SanitizeNonNegative(measurement.TowardBoundarySpeed),
                SanitizeNonNegative(measurement.TowardBoundaryAcceleration),
                SanitizeRisk(measurement.DistanceRisk),
                SanitizeRisk(measurement.TtcRisk),
                SanitizeRisk(measurement.AccelerationRisk),
                SanitizeRisk(measurement.BlindSpotRisk),
                SanitizeRisk(measurement.Risk));
        }

        private UserStateRiskSnapshot BuildUserState(
            bool motionReady,
            UserMotionSnapshot motion)
        {
            bool available = motionReady && motion != null;
            if (!available)
            {
                return new UserStateRiskSnapshot(
                    false,
                    UserMotionState.Static,
                    0f,
                    0f,
                    0f,
                    0f);
            }

            return new UserStateRiskSnapshot(
                true,
                motion.StableState,
                SanitizeNonNegative(motion.FilteredSpeed),
                SanitizeNonNegative(motion.FilteredAccelerationMagnitude),
                SanitizeNonNegative(motion.FilteredAngularSpeed),
                GetStateRisk(motion.StableState));
        }

        private DynamicRiskSnapshot BuildDynamic(
            double timestamp,
            bool cameraReady,
            DynamicRiskFrame frame)
        {
            double sourceTimestamp =
                frame == null ? 0.0 : SanitizeSourceTimestamp(frame.TimestampSeconds);
            float age = frame == null
                ? 0f
                : (float)Math.Max(0.0, timestamp - sourceTimestamp);
            float staleAfter = Math.Max(
                0f,
                SanitizeFinite(settings.dynamicStaleAfterSeconds));
            bool available =
                cameraReady
                && frame != null
                && age <= staleAfter;

            if (!available)
            {
                return new DynamicRiskSnapshot(
                    false,
                    sourceTimestamp,
                    age,
                    0,
                    0f,
                    DynamicRiskLevel.Safe,
                    0,
                    0f,
                    DynamicMotionState.Unknown);
            }

            DynamicRiskAssessment primary = null;
            for (int i = 0; i < frame.Assessments.Count; i++)
            {
                DynamicRiskAssessment candidate = frame.Assessments[i];
                if (candidate != null
                    && (primary == null || candidate.Score > primary.Score))
                {
                    primary = candidate;
                }
            }

            return new DynamicRiskSnapshot(
                true,
                sourceTimestamp,
                age,
                Math.Max(0, frame.ConfirmedPersonCount),
                SanitizeRisk(frame.MaximumRisk),
                frame.MaximumLevel,
                primary == null ? 0 : Math.Max(0, primary.TrackId),
                primary == null
                    ? 0f
                    : SanitizeRisk(primary.Detection.confidence),
                primary == null
                    ? DynamicMotionState.Unknown
                    : primary.Motion.State);
        }

        private double SanitizeTimestamp(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
            {
                InvalidValueCount++;
                return lastTimestampSeconds;
            }

            if (value < lastTimestampSeconds)
            {
                InvalidValueCount++;
                return lastTimestampSeconds;
            }

            return value;
        }

        private double SanitizeSourceTimestamp(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
            {
                InvalidValueCount++;
                return 0.0;
            }

            return value;
        }

        private float SanitizeNonNegative(float value)
        {
            if (!IsFinite(value) || value < 0f)
            {
                InvalidValueCount++;
                return 0f;
            }

            return value;
        }

        private float SanitizeRisk(float value)
        {
            if (!IsFinite(value))
            {
                InvalidValueCount++;
                return 0f;
            }

            return Math.Max(0f, Math.Min(1f, value));
        }

        private static float SanitizeFinite(float value)
        {
            return IsFinite(value) ? value : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float GetStateRisk(UserMotionState state)
        {
            switch (state)
            {
                case UserMotionState.Static:
                    return 0f;
                case UserMotionState.Agitated:
                    return 1f;
                default:
                    return 0.5f;
            }
        }

        private static OverallRiskLevel Classify(float risk)
        {
            if (risk < 0.30f) return OverallRiskLevel.Safe;
            if (risk < 0.60f) return OverallRiskLevel.Caution;
            if (risk < 0.80f) return OverallRiskLevel.Warning;
            return OverallRiskLevel.Danger;
        }
    }
}
