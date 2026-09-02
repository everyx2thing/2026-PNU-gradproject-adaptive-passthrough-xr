using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    /// <summary>
    /// Expands a trusted wall plane for human-readable presentation without
    /// changing the measured distance, normal, source, or static risk score.
    /// </summary>
    public static class WallPresentationGeometrySizing
    {
        public const float MinimumAngularWidthDegrees = 24f;
        public const float MinimumAngularHeightDegrees = 32f;
        public const float HighRiskAngularWidthDegrees = 36f;
        public const float HighRiskAngularHeightDegrees = 48f;
        public const float EmergencyAngularWidthDegrees = 45f;
        public const float EmergencyAngularHeightDegrees = 60f;
        public const float EnvironmentPaddingScale = 1.20f;

        public static HazardPresentationGeometry ExpandForRisk(
            HazardPresentationGeometry geometry,
            Vector3 observerWorldPosition,
            float risk,
            bool emergency)
        {
            if (!geometry.Available
                || geometry.Kind != HazardVisualKind.WallPlane)
            {
                return geometry;
            }

            float distance = Mathf.Max(
                0.10f,
                Vector3.Distance(observerWorldPosition, geometry.Center));
            float risk01 = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.45f, 1f, Mathf.Clamp01(risk)));
            float widthDegrees = emergency
                ? EmergencyAngularWidthDegrees
                : Mathf.Lerp(
                    MinimumAngularWidthDegrees,
                    HighRiskAngularWidthDegrees,
                    risk01);
            float heightDegrees = emergency
                ? EmergencyAngularHeightDegrees
                : Mathf.Lerp(
                    MinimumAngularHeightDegrees,
                    HighRiskAngularHeightDegrees,
                    risk01);
            float sourcePadding = geometry.Source
                    == SpatialObstacleSource.RoomScene
                ? 1f
                : EnvironmentPaddingScale;
            float width = Mathf.Max(
                geometry.Width * sourcePadding,
                AngularSpanMeters(distance, widthDegrees));
            float height = Mathf.Max(
                geometry.Height * sourcePadding,
                AngularSpanMeters(distance, heightDegrees));
            return Resize(geometry, width, height);
        }

        public static float AngularSpanMeters(
            float distanceMeters,
            float angleDegrees)
        {
            return 2f * Mathf.Max(0.01f, distanceMeters)
                * Mathf.Tan(Mathf.Clamp(angleDegrees, 0.1f, 170f)
                    * Mathf.Deg2Rad * 0.5f);
        }

        private static HazardPresentationGeometry Resize(
            HazardPresentationGeometry geometry,
            float width,
            float height)
        {
            Vector3 right = geometry.BottomRight - geometry.BottomLeft
                + geometry.TopRight - geometry.TopLeft;
            Vector3 up = geometry.TopLeft - geometry.BottomLeft
                + geometry.TopRight - geometry.BottomRight;
            if (right.sqrMagnitude <= 0.0001f
                || up.sqrMagnitude <= 0.0001f)
            {
                HazardPresentationGeometry.ResolvePlaneBasis(
                    geometry.SurfaceNormal,
                    Vector3.up,
                    out right,
                    out up);
            }
            else
            {
                right.Normalize();
                up.Normalize();
            }

            float halfWidth = Mathf.Max(0.01f, width * 0.5f);
            float halfHeight = Mathf.Max(0.01f, height * 0.5f);
            Vector3 center = geometry.Center;
            return new HazardPresentationGeometry(
                geometry.StableId,
                geometry.Kind,
                center - right * halfWidth - up * halfHeight,
                center + right * halfWidth - up * halfHeight,
                center + right * halfWidth + up * halfHeight,
                center - right * halfWidth + up * halfHeight,
                geometry.SurfaceNormal,
                geometry.CaptureTimestampSeconds,
                geometry.Confidence,
                geometry.Risk,
                geometry.Source,
                geometry.Owner,
                geometry.ProbePurpose,
                geometry.HasWorldVelocity,
                geometry.WorldVelocity,
                geometry.HasFreshFloor,
                geometry.FloorHeight);
        }
    }
}
