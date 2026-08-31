using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    public enum PassthroughAnimationPhase
    {
        Hidden,
        Appearing,
        Holding,
        Fading
    }

    /// <summary>
    /// Shared temporal state for static and person presentation. Geometry is
    /// smoothed in world space; stereo reprojection remains entirely in the
    /// current eye's vertex shader and is therefore never head-motion lagged.
    /// </summary>
    public sealed class PassthroughPresentationState
    {
        public const float DefaultAppearSeconds = 0.125f;
        public const float DefaultGeometrySmoothingSeconds = 0.100f;
        public const float DefaultPulseSeconds = 0.300f;
        public const float DefaultHoldSeconds = 1.500f;
        public const float DefaultFadeSeconds = 0.300f;
        public const float DefaultPredictionSeconds = 0.500f;
        public const float SurfaceHandoffSeconds = 0.250f;
        public const float SurfaceHandoffMaximumObservationGapSeconds = 0.400f;
        public const float SameSurfaceCenterDistanceMeters = 0.350f;
        public const float SameSurfaceNormalAngleDegrees = 20f;

        private readonly float appearSeconds;
        private readonly float smoothingSeconds;
        private readonly float pulseSeconds;
        private readonly float holdSeconds;
        private readonly float fadeSeconds;
        private readonly float predictionSeconds;

        private HazardPresentationGeometry currentGeometry;
        private HazardPresentationGeometry targetGeometry;
        private HazardPresentationGeometry pendingGeometry;
        private double pendingGeometrySince;
        private double pendingGeometryLastSeen;
        private int pendingGeometryConfirmations;
        private double firstQualifiedAt;
        private double lastQualifiedAt;
        private double lastGeometryAt;
        private double pulseStartedAt;
        private bool hasQualifiedObservation;
        private bool qualifiedThisFrame;
        private bool retainGeometryUntilHidden;
        private bool presentationPolicyActive = true;
        private float opacity;
        private float risk;

        public PassthroughPresentationState(
            float appearSeconds = DefaultAppearSeconds,
            float smoothingSeconds = DefaultGeometrySmoothingSeconds,
            float pulseSeconds = DefaultPulseSeconds,
            float holdSeconds = DefaultHoldSeconds,
            float fadeSeconds = DefaultFadeSeconds,
            float predictionSeconds = DefaultPredictionSeconds)
        {
            this.appearSeconds = Mathf.Max(0.001f, appearSeconds);
            this.smoothingSeconds = Mathf.Max(0.001f, smoothingSeconds);
            this.pulseSeconds = Mathf.Max(0.001f, pulseSeconds);
            this.holdSeconds = Mathf.Max(0f, holdSeconds);
            this.fadeSeconds = Mathf.Max(0.001f, fadeSeconds);
            this.predictionSeconds = Mathf.Max(0f, predictionSeconds);
        }

        public HazardPresentationGeometry Geometry => RenderGeometry(
            lastUpdateSeconds);
        public bool HasRenderableGeometry => Geometry.Available;
        public float Opacity => opacity;
        public float Risk => risk;
        public bool RetainsGeometryUntilHidden => retainGeometryUntilHidden;
        public PassthroughAnimationPhase Phase { get; private set; }
        public double LastUpdateSeconds { get; private set; }
        private double lastUpdateSeconds => LastUpdateSeconds;

        public float Pulse01
        {
            get
            {
                if (pulseStartedAt <= 0.0 || LastUpdateSeconds < pulseStartedAt)
                {
                    return 0f;
                }

                float age = (float)(LastUpdateSeconds - pulseStartedAt);
                return age < pulseSeconds
                    ? Mathf.Clamp01(age / pulseSeconds)
                    : 0f;
            }
        }

        public float HoldRemainingSeconds
        {
            get
            {
                if (!hasQualifiedObservation)
                {
                    return 0f;
                }

                double releaseAt = Math.Max(
                    firstQualifiedAt + holdSeconds,
                    lastQualifiedAt + holdSeconds);
                return Mathf.Max(0f, (float)(releaseAt - LastUpdateSeconds));
            }
        }

        public void BeginFrame()
        {
            qualifiedThisFrame = false;
        }

        /// <summary>
        /// Distinguishes an intentional presentation-policy shutdown from a
        /// missing sensor observation. An intentional shutdown keeps the last
        /// trusted world geometry through the existing hold and fade so the
        /// mask does not hard-cut. Sensor loss leaves this disabled and still
        /// expires geometry after <see cref="DefaultPredictionSeconds"/>.
        /// Existing callers that do not use this API preserve the sensor-stale
        /// behavior.
        /// </summary>
        public void SetPresentationPolicyActive(bool active)
        {
            SetPresentationPolicyActive(active, LastUpdateSeconds);
        }

        /// <summary>
        /// Timestamp-aware policy transition. Geometry may be retained only
        /// when it is still inside the trusted prediction window at the exact
        /// instant the policy turns off. Repeated inactive updates preserve a
        /// retention decision that was made while the geometry was fresh, but
        /// cannot resurrect geometry that had already expired.
        /// </summary>
        public void SetPresentationPolicyActive(
            bool active,
            double timestampSeconds)
        {
            if (active)
            {
                presentationPolicyActive = true;
                retainGeometryUntilHidden = false;
                return;
            }

            if (presentationPolicyActive)
            {
                retainGeometryUntilHidden = hasQualifiedObservation
                    && IsGeometryFresh(timestampSeconds);
            }

            presentationPolicyActive = false;
        }

        public void CancelHoldAndFade(double timestampSeconds)
        {
            SetPresentationPolicyActive(false, timestampSeconds);
            qualifiedThisFrame = false;
            hasQualifiedObservation = false;
            firstQualifiedAt = 0.0;
            lastQualifiedAt = 0.0;
        }

        public void Observe(
            HazardPresentationGeometry geometry,
            float observationRisk,
            double timestampSeconds,
            bool revealEligible)
        {
            Observe(
                geometry,
                observationRisk,
                timestampSeconds,
                timestampSeconds,
                revealEligible);
        }

        public void Observe(
            HazardPresentationGeometry geometry,
            float observationRisk,
            double geometryTimestampSeconds,
            double qualificationTimestampSeconds,
            bool revealEligible)
        {
            double safeGeometryTimestamp = Math.Max(
                0.0,
                geometryTimestampSeconds);
            double safeQualificationTimestamp = Math.Max(
                0.0,
                qualificationTimestampSeconds);
            if (geometry.Available)
            {
                bool hasRenderableIncumbent = currentGeometry.Available
                    && opacity > 0f
                    && Phase != PassthroughAnimationPhase.Hidden
                    && IsGeometryFresh(safeQualificationTimestamp);
                if (currentGeometry.Available && !hasRenderableIncumbent)
                {
                    // A stale or already hidden surface must never impose its
                    // handoff dwell on the next trusted surface. Restart the
                    // visual appearance as well, otherwise opacity and the
                    // one-shot pulse can finish while no geometry is rendered.
                    currentGeometry = default;
                    targetGeometry = default;
                    ClearPendingGeometry();
                    hasQualifiedObservation = false;
                    qualifiedThisFrame = false;
                    firstQualifiedAt = 0.0;
                    lastQualifiedAt = 0.0;
                    pulseStartedAt = 0.0;
                    opacity = 0f;
                    Phase = PassthroughAnimationPhase.Hidden;
                }

                HazardPresentationGeometry acceptedGeometry = geometry;
                if (currentGeometry.Available
                    && currentGeometry.StableId != geometry.StableId
                    && IsSamePhysicalSurface(currentGeometry, geometry))
                {
                    acceptedGeometry = geometry.WithStableId(
                        currentGeometry.StableId);
                }
                else if (currentGeometry.Available
                    && currentGeometry.StableId != geometry.StableId
                    && observationRisk < 0.999f)
                {
                    if (!pendingGeometry.Available
                        || pendingGeometry.StableId != geometry.StableId
                        || safeQualificationTimestamp
                            - pendingGeometryLastSeen
                                > SurfaceHandoffMaximumObservationGapSeconds)
                    {
                        pendingGeometry = geometry;
                        pendingGeometrySince = safeQualificationTimestamp;
                        pendingGeometryLastSeen = safeQualificationTimestamp;
                        pendingGeometryConfirmations = 1;
                        acceptedGeometry = default;
                    }
                    else
                    {
                        pendingGeometry = geometry;
                        pendingGeometryLastSeen = safeQualificationTimestamp;
                        pendingGeometryConfirmations++;
                        if (pendingGeometryConfirmations < 2
                            || safeQualificationTimestamp
                                - pendingGeometrySince
                                    < SurfaceHandoffSeconds)
                        {
                            acceptedGeometry = default;
                        }
                    }
                }

                if (acceptedGeometry.Available)
                {
                    if (!currentGeometry.Available
                        || currentGeometry.StableId
                            != acceptedGeometry.StableId)
                    {
                        currentGeometry = acceptedGeometry;
                    }

                    targetGeometry = acceptedGeometry;
                    lastGeometryAt = safeGeometryTimestamp;
                    ClearPendingGeometry();
                }
            }

            if (!revealEligible)
            {
                return;
            }

            qualifiedThisFrame = true;
            bool startsNewAppearance = !hasQualifiedObservation
                || opacity <= 0f;
            if (startsNewAppearance)
            {
                firstQualifiedAt = safeQualificationTimestamp;
                pulseStartedAt = safeQualificationTimestamp;
            }

            hasQualifiedObservation = true;
            lastQualifiedAt = safeQualificationTimestamp;
            risk = Mathf.Clamp01(observationRisk);
        }

        public void Update(double timestampSeconds, float deltaTime)
        {
            LastUpdateSeconds = Math.Max(0.0, timestampSeconds);
            if (pendingGeometry.Available
                && LastUpdateSeconds - pendingGeometryLastSeen
                    > SurfaceHandoffMaximumObservationGapSeconds)
            {
                ClearPendingGeometry();
            }
            float safeDelta = Mathf.Max(0f, deltaTime);
            if (currentGeometry.Available && targetGeometry.Available)
            {
                float alpha = 1f - Mathf.Exp(
                    -safeDelta / smoothingSeconds);
                currentGeometry = HazardPresentationGeometry.Lerp(
                    currentGeometry,
                    targetGeometry,
                    alpha);
            }

            bool holding = qualifiedThisFrame || HoldRemainingSeconds > 0f;
            if (holding)
            {
                opacity = Mathf.MoveTowards(
                    opacity,
                    1f,
                    safeDelta / appearSeconds);
                Phase = opacity < 0.999f
                    ? PassthroughAnimationPhase.Appearing
                    : PassthroughAnimationPhase.Holding;
                return;
            }

            opacity = Mathf.MoveTowards(
                opacity,
                0f,
                safeDelta / fadeSeconds);
            Phase = opacity > 0f
                ? PassthroughAnimationPhase.Fading
                : PassthroughAnimationPhase.Hidden;
            if (opacity <= 0f)
            {
                hasQualifiedObservation = false;
                retainGeometryUntilHidden = false;
                risk = 0f;
                currentGeometry = default;
                targetGeometry = default;
                ClearPendingGeometry();
            }
        }

        public HazardPresentationGeometry RenderGeometry(double timestampSeconds)
        {
            if (!currentGeometry.Available)
            {
                return default;
            }

            float age = (float)Math.Max(0.0, timestampSeconds - lastGeometryAt);
            if (!retainGeometryUntilHidden && age > predictionSeconds)
            {
                return default;
            }

            float predictionAge = Mathf.Min(predictionSeconds, age);
            return currentGeometry.HasWorldVelocity
                ? currentGeometry.Translated(
                    currentGeometry.WorldVelocity * predictionAge)
                : currentGeometry;
        }

        private bool IsGeometryFresh(double timestampSeconds)
        {
            if (!currentGeometry.Available)
            {
                return false;
            }

            float age = (float)Math.Max(
                0.0,
                Math.Max(0.0, timestampSeconds) - lastGeometryAt);
            return age <= predictionSeconds;
        }

        private static bool IsSamePhysicalSurface(
            HazardPresentationGeometry current,
            HazardPresentationGeometry next)
        {
            if (!current.Available || !next.Available)
            {
                return false;
            }

            if (Vector3.Distance(current.Center, next.Center)
                > SameSurfaceCenterDistanceMeters)
            {
                return false;
            }

            Vector3 a = current.SurfaceNormal;
            Vector3 b = next.SurfaceNormal;
            if (a.sqrMagnitude <= 0.0001f || b.sqrMagnitude <= 0.0001f)
            {
                return true;
            }

            return Vector3.Angle(a, b) <= SameSurfaceNormalAngleDegrees;
        }

        private void ClearPendingGeometry()
        {
            pendingGeometry = default;
            pendingGeometrySince = 0.0;
            pendingGeometryLastSeen = 0.0;
            pendingGeometryConfirmations = 0;
        }

        public void Reset()
        {
            currentGeometry = default;
            targetGeometry = default;
            ClearPendingGeometry();
            firstQualifiedAt = 0.0;
            lastQualifiedAt = 0.0;
            lastGeometryAt = 0.0;
            pulseStartedAt = 0.0;
            hasQualifiedObservation = false;
            qualifiedThisFrame = false;
            retainGeometryUntilHidden = false;
            presentationPolicyActive = true;
            opacity = 0f;
            risk = 0f;
            Phase = PassthroughAnimationPhase.Hidden;
            LastUpdateSeconds = 0.0;
        }
    }
}
