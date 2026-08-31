using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    public sealed class PresentationNormalStabilizer
    {
        private readonly float smoothingSeconds;
        private readonly float maximumDegreesPerSecond;
        private readonly float jumpDegrees;
        private Vector3 accepted;
        private Vector3 pending;
        private int pendingConfirmations;
        private bool pendingTargetConfirmed;
        private double lastAcceptedAt;
        private bool hasSurfaceId;
        private int surfaceId;

        public PresentationNormalStabilizer(
            float smoothingSeconds = 0.20f,
            float maximumDegreesPerSecond = 60f,
            float jumpDegrees = 25f)
        {
            this.smoothingSeconds = Mathf.Max(0.001f, smoothingSeconds);
            this.maximumDegreesPerSecond = Mathf.Max(
                0f,
                maximumDegreesPerSecond);
            this.jumpDegrees = Mathf.Max(0f, jumpDegrees);
        }

        public bool HasAcceptedNormal { get; private set; }
        public Vector3 AcceptedNormal => HasAcceptedNormal
            ? accepted
            : Vector3.zero;
        public float LastInputAngleDegrees { get; private set; }

        public Vector3 UpdateForSurface(
            int stableSurfaceId,
            Vector3 measuredNormal,
            double timestampSeconds)
        {
            if (!hasSurfaceId || surfaceId != stableSurfaceId)
            {
                Reset();
                hasSurfaceId = true;
                surfaceId = stableSurfaceId;
            }
            return Update(measuredNormal, timestampSeconds);
        }

        public Vector3 Update(Vector3 measuredNormal, double timestampSeconds)
        {
            Vector3 safe = measuredNormal.sqrMagnitude > 0.0001f
                ? measuredNormal.normalized
                : Vector3.forward;
            double now = Math.Max(0.0, timestampSeconds);
            if (!HasAcceptedNormal)
            {
                HasAcceptedNormal = true;
                accepted = safe;
                lastAcceptedAt = now;
                LastInputAngleDegrees = 0f;
                return accepted;
            }

            if (Vector3.Dot(accepted, safe) < 0f)
            {
                safe = -safe;
            }
            float rawAngle = Vector3.Angle(accepted, safe);
            LastInputAngleDegrees = rawAngle;
            if (rawAngle > jumpDegrees)
            {
                if (pendingTargetConfirmed
                    && pending.sqrMagnitude > 0.0001f
                    && Vector3.Angle(pending, safe) <= 5f)
                {
                    safe = pending;
                }
                else if (pending.sqrMagnitude > 0.0001f
                    && Vector3.Angle(pending, safe) <= 5f)
                {
                    pendingConfirmations++;
                }
                else
                {
                    pending = safe;
                    pendingConfirmations = 1;
                }

                if (pendingConfirmations < 2)
                {
                    return accepted;
                }

                pendingTargetConfirmed = true;
                safe = pending;
            }
            else
            {
                pending = Vector3.zero;
                pendingConfirmations = 0;
                pendingTargetConfirmed = false;
            }

            float elapsed = (float)Math.Max(0.0, now - lastAcceptedAt);
            float alpha = 1f - Mathf.Exp(-elapsed / smoothingSeconds);
            float maximumRadians = maximumDegreesPerSecond
                * Mathf.Deg2Rad * elapsed;
            accepted = Vector3.RotateTowards(
                accepted,
                safe,
                Mathf.Min(maximumRadians, rawAngle * Mathf.Deg2Rad * alpha),
                0f).normalized;
            lastAcceptedAt = now;
            if (!pendingTargetConfirmed
                || Vector3.Angle(accepted, pending) <= jumpDegrees)
            {
                pending = Vector3.zero;
                pendingConfirmations = 0;
                pendingTargetConfirmed = false;
            }
            return accepted;
        }

        public void Reset()
        {
            HasAcceptedNormal = false;
            accepted = Vector3.zero;
            pending = Vector3.zero;
            pendingConfirmations = 0;
            pendingTargetConfirmed = false;
            lastAcceptedAt = 0.0;
            LastInputAngleDegrees = 0f;
            hasSurfaceId = false;
            surfaceId = 0;
        }
    }
}
