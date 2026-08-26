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
