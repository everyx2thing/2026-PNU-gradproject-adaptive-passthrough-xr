using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    /// <summary>
    /// Presentation-only person depth estimation. Values produced here are
    /// deliberately kept out of metric distance, motion, and risk estimation;
    /// they exist solely to place a binocularly projected safety mask when
    /// Environment Depth is temporarily unavailable or rejected.
    /// </summary>
    public static class PersonPresentationGeometryMath
    {
        public const float MinimumEstimatedDistanceMeters = 0.60f;
        public const float MaximumEstimatedDistanceMeters = 3.00f;
        public const float NominalUpperBodyHeightMeters = 1.20f;
        public const float StrongCloseMaximumDistanceMeters = 0.75f;
        public const float WeakCloseMaximumDistanceMeters = 0.90f;
        public const float TrackHistorySeconds = 0.75f;
        public const float MaximumHistoryPredictionSeconds = 0.50f;

        public static float EstimateDistanceMeters(
            NormalizedBoundingBox detectionBox,
            Ray topCenterRay,
            Ray bottomCenterRay,
            float nominalUpperBodyHeightMeters =
                NominalUpperBodyHeightMeters)
        {
            Vector3 top = SafeDirection(topCenterRay.direction);
            Vector3 bottom = SafeDirection(bottomCenterRay.direction);
            float angleRadians = Mathf.Max(
                3f * Mathf.Deg2Rad,
                Vector3.Angle(top, bottom) * Mathf.Deg2Rad);
            float estimated = Mathf.Max(0.10f, nominalUpperBodyHeightMeters)
                / Mathf.Max(0.01f, 2f * Mathf.Tan(angleRadians * 0.5f));

            if (detectionBox.height >= 0.95f
                || detectionBox.Area >= 0.65f)
            {
                estimated = Mathf.Min(
                    estimated,
                    StrongCloseMaximumDistanceMeters);
            }
            else if (detectionBox.height >= 0.92f
                || detectionBox.Area >= 0.55f)
            {
                estimated = Mathf.Min(
                    estimated,
                    WeakCloseMaximumDistanceMeters);
            }

            return Mathf.Clamp(
                estimated,
                MinimumEstimatedDistanceMeters,
                MaximumEstimatedDistanceMeters);
        }

        public static bool TryCreateAtDistance(
            int trackId,
            NormalizedBoundingBox capsuleBox,
            Ray bottomLeftRay,
            Ray bottomRightRay,
            Ray topRightRay,
            Ray topLeftRay,
            Ray centerRay,
            Vector3 captureForward,
            float distanceMeters,
            double timestampSeconds,
            float confidence,
            Vector3 worldVelocity,
            bool hasWorldVelocity,
            out HazardPresentationGeometry geometry)
        {
            float safeDistance = Mathf.Clamp(
                Finite(distanceMeters) ? distanceMeters : 0f,
                MinimumEstimatedDistanceMeters,
                MaximumEstimatedDistanceMeters);
            Vector3 safeForward = SafeDirection(captureForward);
            Vector3 centerDirection = SafeDirection(centerRay.direction);
            // Both metric history and the angular-size estimate describe
            // distance along the capture camera's forward axis. Convert that
            // forward depth to ray travel so off-centre people stay on the
            // intended capture plane instead of being pulled closer.
            float forwardAlignment = Mathf.Max(
                0.20f,
                Vector3.Dot(centerDirection, safeForward));
            Vector3 planePoint = centerRay.origin
                + centerDirection * (safeDistance / forwardAlignment);
            return HazardPresentationGeometry.TryCreatePersonCapsule(
                trackId,
                capsuleBox,
                bottomLeftRay,
                bottomRightRay,
                topRightRay,
                topLeftRay,
                planePoint,
                safeForward,
                timestampSeconds,
                Mathf.Clamp01(confidence),
                0f,
                worldVelocity,
                hasWorldVelocity,
                out geometry);
        }

        public static bool TryHistoryDistance(
            Vector3 capturePosition,
            Vector3 captureForward,
            Vector3 lastWorldPosition,
            Vector3 worldVelocity,
            double historyAgeSeconds,
            out float distanceMeters)
        {
            float age = Mathf.Max(0f, (float)historyAgeSeconds);
            if (age > TrackHistorySeconds)
            {
                distanceMeters = 0f;
                return false;
            }

            Vector3 predicted = lastWorldPosition
                + worldVelocity * Mathf.Min(
                    age,
                    MaximumHistoryPredictionSeconds);
            float forwardDistance = Vector3.Dot(
                predicted - capturePosition,
                SafeDirection(captureForward));
            if (!Finite(forwardDistance)
                || forwardDistance < MinimumEstimatedDistanceMeters
                || forwardDistance > MaximumEstimatedDistanceMeters)
            {
                distanceMeters = 0f;
                return false;
            }

            distanceMeters = forwardDistance;
            return true;
        }

        private static Vector3 SafeDirection(Vector3 value)
        {
            return Finite(value.x)
                && Finite(value.y)
                && Finite(value.z)
                && value.sqrMagnitude > 0.0001f
                    ? value.normalized
                    : Vector3.forward;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
