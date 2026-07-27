using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    public enum UserMotionState
    {
        Static,
        Dynamic,
        Agitated
    }

    [Serializable]
    public sealed class UserMotionStateFilterSettings
    {
        [Header("EMA time constants")]
        [Min(0.01f)] public float velocityTimeConstant = 0.35f;
        [Min(0.01f)] public float accelerationTimeConstant = 0.45f;
        [Min(0.01f)] public float angularTimeConstant = 0.35f;

        [Header("Static enter")]
        [Min(0f)] public float staticEnterSpeed = 0.04f;
        [Min(0f)] public float staticEnterAcceleration = 0.25f;
        [Min(0f)] public float staticEnterAngularSpeed = 0.25f;
        [Min(0f)] public float staticEnterSeconds = 0.75f;

        [Header("Static exit")]
        [Min(0f)] public float staticExitSpeed = 0.08f;
        [Min(0f)] public float staticExitAcceleration = 0.50f;
        [Min(0f)] public float staticExitAngularSpeed = 0.60f;
        [Min(0f)] public float staticExitSeconds = 0.25f;

        [Header("Agitated enter")]
        [Min(0f)] public float agitatedEnterSpeed = 1.0f;
        [Min(0f)] public float agitatedEnterAcceleration = 5.0f;
        [Min(0f)] public float agitatedEnterAngularSpeed = 2.5f;
        [Min(0f)] public float agitatedEnterSeconds = 0.25f;

        [Header("Agitated exit")]
        [Min(0f)] public float agitatedExitSpeed = 0.75f;
        [Min(0f)] public float agitatedExitAcceleration = 3.5f;
        [Min(0f)] public float agitatedExitAngularSpeed = 1.8f;
        [Min(0f)] public float agitatedExitSeconds = 0.75f;

        [Header("Sampling")]
        [Min(0.001f)] public float minimumDeltaTime = 0.001f;
        [Min(0.01f)] public float maximumDeltaTime = 0.10f;
    }

    public sealed class UserMotionSnapshot
    {
        public readonly double TimestampSeconds;
        public readonly bool SampleAccepted;
        public readonly Vector3 RawVelocity;
        public readonly Vector3 FilteredVelocity;
        public readonly Vector3 RawAcceleration;
        public readonly Vector3 FilteredAcceleration;
        public readonly float RawAngularSpeed;
        public readonly float FilteredAngularSpeed;
        public readonly UserMotionState CandidateState;
        public readonly UserMotionState StableState;
        public readonly bool StateChanged;

        public UserMotionSnapshot(
            double timestampSeconds,
            bool sampleAccepted,
            Vector3 rawVelocity,
            Vector3 filteredVelocity,
            Vector3 rawAcceleration,
            Vector3 filteredAcceleration,
            float rawAngularSpeed,
            float filteredAngularSpeed,
            UserMotionState candidateState,
            UserMotionState stableState,
            bool stateChanged)
        {
            TimestampSeconds = timestampSeconds;
            SampleAccepted = sampleAccepted;
            RawVelocity = rawVelocity;
            FilteredVelocity = filteredVelocity;
            RawAcceleration = rawAcceleration;
            FilteredAcceleration = filteredAcceleration;
            RawAngularSpeed = rawAngularSpeed;
            FilteredAngularSpeed = filteredAngularSpeed;
            CandidateState = candidateState;
            StableState = stableState;
            StateChanged = stateChanged;
        }

        public float RawSpeed
        {
            get { return RawVelocity.magnitude; }
        }

        public float FilteredSpeed
        {
            get { return FilteredVelocity.magnitude; }
        }

        public float RawAccelerationMagnitude
        {
            get { return RawAcceleration.magnitude; }
        }

        public float FilteredAccelerationMagnitude
        {
            get { return FilteredAcceleration.magnitude; }
        }
    }

    public sealed class UserMotionStateFilter
    {
        private readonly UserMotionStateFilterSettings settings;

        private bool initialized;
        private double previousTimestamp;
        private Vector3 previousPosition;
        private Quaternion previousRotation;
        private Vector3 previousRawVelocity;
        private Vector3 filteredVelocity;
        private Vector3 previousFilteredVelocity;
        private Vector3 filteredAcceleration;
        private float filteredAngularSpeed;
        private UserMotionState stableState = UserMotionState.Static;
        private UserMotionState candidateState = UserMotionState.Static;
        private double candidateSince;
        private UserMotionSnapshot latest;

        public UserMotionStateFilter(
            UserMotionStateFilterSettings settings = null)
        {
            this.settings = settings ?? new UserMotionStateFilterSettings();
            latest = CreateInitial(0.0, false);
        }

        public UserMotionSnapshot Latest
        {
            get { return latest; }
        }

        public UserMotionSnapshot Update(
            double timestampSeconds,
            Vector3 position,
            Quaternion rotation)
        {
            if (!initialized)
            {
                Initialize(timestampSeconds, position, rotation);
                latest = CreateInitial(timestampSeconds, true);
                return latest;
            }

            double elapsed = timestampSeconds - previousTimestamp;
            if (elapsed < settings.minimumDeltaTime
                || elapsed > settings.maximumDeltaTime
                || double.IsNaN(elapsed)
                || double.IsInfinity(elapsed))
            {
                ResetKinematics(timestampSeconds, position, rotation);
                latest = new UserMotionSnapshot(
                    timestampSeconds,
                    false,
                    Vector3.zero,
                    filteredVelocity,
                    Vector3.zero,
                    filteredAcceleration,
                    0f,
                    filteredAngularSpeed,
                    candidateState,
                    stableState,
                    false);
                return latest;
            }

            float dt = (float)elapsed;
            Vector3 rawVelocity = (position - previousPosition) / dt;
            Vector3 rawAcceleration =
                (rawVelocity - previousRawVelocity) / dt;
            float rawAngularSpeed =
                Quaternion.Angle(previousRotation, rotation)
                * Mathf.Deg2Rad
                / dt;

            filteredVelocity = ExponentialAverage(
                filteredVelocity,
                rawVelocity,
                dt,
                settings.velocityTimeConstant);
            Vector3 accelerationFromFilteredVelocity =
                (filteredVelocity - previousFilteredVelocity) / dt;
            filteredAcceleration = ExponentialAverage(
                filteredAcceleration,
                accelerationFromFilteredVelocity,
                dt,
                settings.accelerationTimeConstant);
            filteredAngularSpeed = ExponentialAverage(
                filteredAngularSpeed,
                rawAngularSpeed,
                dt,
                settings.angularTimeConstant);

            previousTimestamp = timestampSeconds;
            previousPosition = position;
            previousRotation = rotation;
            previousRawVelocity = rawVelocity;
            previousFilteredVelocity = filteredVelocity;

            bool stateChanged = UpdateState(
                timestampSeconds,
                filteredVelocity.magnitude,
                filteredAcceleration.magnitude,
                filteredAngularSpeed);
            latest = new UserMotionSnapshot(
                timestampSeconds,
                true,
                rawVelocity,
                filteredVelocity,
                rawAcceleration,
                filteredAcceleration,
                rawAngularSpeed,
                filteredAngularSpeed,
                candidateState,
                stableState,
                stateChanged);
            return latest;
        }

        public void Reset()
        {
            initialized = false;
            previousTimestamp = 0.0;
            previousPosition = Vector3.zero;
            previousRotation = Quaternion.identity;
            previousRawVelocity = Vector3.zero;
            filteredVelocity = Vector3.zero;
            previousFilteredVelocity = Vector3.zero;
            filteredAcceleration = Vector3.zero;
            filteredAngularSpeed = 0f;
            stableState = UserMotionState.Static;
            candidateState = UserMotionState.Static;
            candidateSince = 0.0;
            latest = CreateInitial(0.0, false);
        }

        private void Initialize(
            double timestampSeconds,
            Vector3 position,
            Quaternion rotation)
        {
            initialized = true;
            previousTimestamp = timestampSeconds;
            previousPosition = position;
            previousRotation = rotation;
            previousRawVelocity = Vector3.zero;
            filteredVelocity = Vector3.zero;
            previousFilteredVelocity = Vector3.zero;
            filteredAcceleration = Vector3.zero;
            filteredAngularSpeed = 0f;
            candidateSince = timestampSeconds;
        }

        private void ResetKinematics(
            double timestampSeconds,
            Vector3 position,
            Quaternion rotation)
        {
            previousTimestamp = timestampSeconds;
            previousPosition = position;
            previousRotation = rotation;
            previousRawVelocity = Vector3.zero;
            previousFilteredVelocity = filteredVelocity;
        }

        private bool UpdateState(
            double timestampSeconds,
            float speed,
            float acceleration,
            float angularSpeed)
        {
            UserMotionState desired = DesiredState(
                speed,
                acceleration,
                angularSpeed);
            if (desired == stableState)
            {
                candidateState = stableState;
                candidateSince = timestampSeconds;
                return false;
            }

            if (desired != candidateState)
            {
                candidateState = desired;
                candidateSince = timestampSeconds;
                return false;
            }

            double requiredSeconds = RequiredTransitionSeconds(
                stableState,
                candidateState);
            if (timestampSeconds - candidateSince < requiredSeconds)
            {
                return false;
            }

            stableState = candidateState;
            candidateSince = timestampSeconds;
            return true;
        }

        private UserMotionState DesiredState(
            float speed,
            float acceleration,
            float angularSpeed)
        {
            bool agitatedEnter =
                speed > settings.agitatedEnterSpeed
                || acceleration > settings.agitatedEnterAcceleration
                || angularSpeed > settings.agitatedEnterAngularSpeed;
            if (stableState != UserMotionState.Agitated && agitatedEnter)
            {
                return UserMotionState.Agitated;
            }

            if (stableState == UserMotionState.Agitated)
            {
                bool agitatedExit =
                    speed < settings.agitatedExitSpeed
                    && acceleration < settings.agitatedExitAcceleration
                    && angularSpeed < settings.agitatedExitAngularSpeed;
                return agitatedExit
                    ? UserMotionState.Dynamic
                    : UserMotionState.Agitated;
            }

            if (stableState == UserMotionState.Static)
            {
                bool staticExit =
                    speed > settings.staticExitSpeed
                    || acceleration > settings.staticExitAcceleration
                    || angularSpeed > settings.staticExitAngularSpeed;
                return staticExit
                    ? UserMotionState.Dynamic
                    : UserMotionState.Static;
            }

            bool staticEnter =
                speed < settings.staticEnterSpeed
                && acceleration < settings.staticEnterAcceleration
                && angularSpeed < settings.staticEnterAngularSpeed;
            return staticEnter
                ? UserMotionState.Static
                : UserMotionState.Dynamic;
        }

        private double RequiredTransitionSeconds(
            UserMotionState from,
            UserMotionState to)
        {
            if (to == UserMotionState.Agitated)
            {
                return settings.agitatedEnterSeconds;
            }

            if (from == UserMotionState.Agitated)
            {
                return settings.agitatedExitSeconds;
            }

            if (to == UserMotionState.Static)
            {
                return settings.staticEnterSeconds;
            }

            return settings.staticExitSeconds;
        }

        private UserMotionSnapshot CreateInitial(
            double timestampSeconds,
            bool accepted)
        {
            return new UserMotionSnapshot(
                timestampSeconds,
                accepted,
                Vector3.zero,
                filteredVelocity,
                Vector3.zero,
                filteredAcceleration,
                0f,
                filteredAngularSpeed,
                candidateState,
                stableState,
                false);
        }

        private static Vector3 ExponentialAverage(
            Vector3 previous,
            Vector3 current,
            float dt,
            float timeConstant)
        {
            float alpha = ExponentialAlpha(dt, timeConstant);
            return Vector3.LerpUnclamped(previous, current, alpha);
        }

        private static float ExponentialAverage(
            float previous,
            float current,
            float dt,
            float timeConstant)
        {
            float alpha = ExponentialAlpha(dt, timeConstant);
            return Mathf.LerpUnclamped(previous, current, alpha);
        }

        private static float ExponentialAlpha(float dt, float timeConstant)
        {
            float tau = Mathf.Max(0.01f, timeConstant);
            return 1f - Mathf.Exp(-dt / tau);
        }
    }
}
