using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    public static class FiniteSpatialBoundsMath
    {
        public static Vector3 ClosestPointOnPlane(
            Vector3 worldPoint,
            Vector3 anchorPosition,
            Quaternion anchorRotation,
            Rect localBounds)
        {
            Vector3 local = Quaternion.Inverse(anchorRotation)
                * (worldPoint - anchorPosition);
            Vector3 closestLocal = new Vector3(
                Mathf.Clamp(local.x, localBounds.xMin, localBounds.xMax),
                Mathf.Clamp(local.y, localBounds.yMin, localBounds.yMax),
                0f);
            return anchorPosition + anchorRotation * closestLocal;
        }

        public static Vector3 ClosestPointOnVolume(
            Vector3 worldPoint,
            Vector3 anchorPosition,
            Quaternion anchorRotation,
            Bounds localBounds,
            out bool inside)
        {
            Vector3 local = Quaternion.Inverse(anchorRotation)
                * (worldPoint - anchorPosition);
            inside = localBounds.Contains(local);
            Vector3 closestLocal = inside
                ? ClosestSurfacePoint(local, localBounds)
                : localBounds.ClosestPoint(local);
            return anchorPosition + anchorRotation * closestLocal;
        }

        public static bool IsInsideLocomotionCorridor(
            Vector3 deltaFromProbeOrigin,
            Vector3 probeDirection,
            float safetyRadius,
            out float alongRayMeters,
            out float offAxisMeters)
        {
            Vector3 direction = probeDirection.sqrMagnitude > 0.0001f
                ? probeDirection.normalized
                : new Vector3(0f, -0.7f, 0.7f).normalized;
            alongRayMeters = Vector3.Dot(deltaFromProbeOrigin, direction);
            offAxisMeters = (
                deltaFromProbeOrigin - direction * alongRayMeters).magnitude;
            float corridorRadius = Mathf.Max(
                0.35f,
                Mathf.Max(0f, safetyRadius) + 0.25f);
            return alongRayMeters >= 0.05f
                && alongRayMeters <= 3.0f
                && offAxisMeters <= corridorRadius
                && deltaFromProbeOrigin.y <= 0.30f
                && deltaFromProbeOrigin.y >= -2.20f;
        }

        public static bool IsLowObstacleSemantic(
            string semanticLabel,
            bool hasVolumeBounds)
        {
            string label = semanticLabel ?? string.Empty;
            bool forbidden = label.IndexOf(
                    "WALL",
                    StringComparison.OrdinalIgnoreCase) >= 0
                || label.IndexOf(
                    "WINDOW",
                    StringComparison.OrdinalIgnoreCase) >= 0
                || label.IndexOf(
                    "DOOR",
                    StringComparison.OrdinalIgnoreCase) >= 0
                || label.IndexOf(
                    "CEILING",
                    StringComparison.OrdinalIgnoreCase) >= 0
                || label.IndexOf(
                    "FLOOR",
                    StringComparison.OrdinalIgnoreCase) >= 0;
            if (forbidden)
            {
                return false;
            }

            return hasVolumeBounds || !string.IsNullOrWhiteSpace(label);
        }

        public static bool IsLowObstacleHeight(
            bool hasVolumeBounds,
            float worldVerticalSizeMeters,
            float maximumHeightMeters = 1.25f)
        {
            return !hasVolumeBounds
                || Mathf.Max(0f, worldVerticalSizeMeters)
                    <= Mathf.Max(0.10f, maximumHeightMeters);
        }

        public static bool IsSamePresentationSurface(
            Vector3 previousPoint,
            Vector3 previousNormal,
            Vector3 point,
            Vector3 normal,
            float maximumPointJumpMeters = 0.75f,
            float maximumPlaneOffsetMeters = 0.25f,
            float minimumNormalDot = 0.866f)
        {
            Vector3 safePreviousNormal = previousNormal.sqrMagnitude > 0.0001f
                ? previousNormal.normalized
                : Vector3.forward;
            Vector3 safeNormal = normal.sqrMagnitude > 0.0001f
                ? normal.normalized
                : Vector3.forward;
            Vector3 delta = point - previousPoint;
            return Vector3.Dot(safePreviousNormal, safeNormal)
                    >= Mathf.Clamp(minimumNormalDot, -1f, 1f)
                && Mathf.Abs(Vector3.Dot(safeNormal, delta))
                    <= Mathf.Max(0f, maximumPlaneOffsetMeters)
                && delta.magnitude <= Mathf.Max(0f, maximumPointJumpMeters);
        }

        private static Vector3 ClosestSurfacePoint(
            Vector3 localPoint,
            Bounds bounds)
        {
            Vector3 minimum = bounds.min;
            Vector3 maximum = bounds.max;
            float xMinimum = localPoint.x - minimum.x;
            float xMaximum = maximum.x - localPoint.x;
            float yMinimum = localPoint.y - minimum.y;
            float yMaximum = maximum.y - localPoint.y;
            float zMinimum = localPoint.z - minimum.z;
            float zMaximum = maximum.z - localPoint.z;

            float nearest = xMinimum;
            int face = 0;
            SelectCloser(xMaximum, 1, ref nearest, ref face);
            SelectCloser(yMinimum, 2, ref nearest, ref face);
            SelectCloser(yMaximum, 3, ref nearest, ref face);
            SelectCloser(zMinimum, 4, ref nearest, ref face);
            SelectCloser(zMaximum, 5, ref nearest, ref face);

            Vector3 result = localPoint;
            switch (face)
            {
                case 0:
                    result.x = minimum.x;
                    break;
                case 1:
                    result.x = maximum.x;
                    break;
                case 2:
                    result.y = minimum.y;
                    break;
                case 3:
                    result.y = maximum.y;
                    break;
                case 4:
                    result.z = minimum.z;
                    break;
                default:
                    result.z = maximum.z;
                    break;
            }

            return result;
        }

        private static void SelectCloser(
            float candidate,
            int candidateFace,
            ref float nearest,
            ref int face)
        {
            if (candidate >= nearest)
            {
                return;
            }

            nearest = candidate;
            face = candidateFace;
        }
    }
}
