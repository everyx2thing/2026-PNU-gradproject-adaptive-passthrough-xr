using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    public enum HazardVisualKind
    {
        Unavailable = 0,
        WallPlane = 1,
        PersonCapsule = 2,
        LowObstaclePatch = 3,
        FloorCorridor = 4
    }

    /// <summary>
    /// Allocation-free world geometry retained from measurement until the
    /// presentation fade completes. Corners use clockwise order as seen from
    /// the visible side: bottom-left, bottom-right, top-right, top-left.
    /// </summary>
    public readonly struct HazardPresentationGeometry
    {
        public const float DefaultPaddingScale = 1.125f;
        public const float UpperBodyFraction = 0.82f;
        public const float NearHorizontalNormalDotThreshold = 0.90f;

        public readonly bool Available;
        public readonly long StableId;
        public readonly HazardVisualKind Kind;
        public readonly Vector3 BottomLeft;
        public readonly Vector3 BottomRight;
        public readonly Vector3 TopRight;
        public readonly Vector3 TopLeft;
        public readonly Vector3 SurfaceNormal;
        public readonly bool HasWorldVelocity;
        public readonly Vector3 WorldVelocity;
        public readonly double CaptureTimestampSeconds;
        public readonly float Confidence;
        public readonly float Risk;
        public readonly SpatialObstacleSource Source;
        public readonly SpatialProbeOwner Owner;
        public readonly SpatialProbePurpose ProbePurpose;
        public readonly bool HasFreshFloor;
        public readonly float FloorHeight;

        public HazardPresentationGeometry(
            long stableId,
            HazardVisualKind kind,
            Vector3 bottomLeft,
            Vector3 bottomRight,
            Vector3 topRight,
            Vector3 topLeft,
            Vector3 surfaceNormal,
            double captureTimestampSeconds,
            float confidence = 1f,
            float risk = 0f,
            SpatialObstacleSource source = SpatialObstacleSource.Unavailable,
            SpatialProbeOwner owner = SpatialProbeOwner.Head,
            SpatialProbePurpose probePurpose = SpatialProbePurpose.Standard,
            bool hasWorldVelocity = false,
            Vector3 worldVelocity = default,
            bool hasFreshFloor = false,
            float floorHeight = 0f)
        {
            bool finite = IsFinite(bottomLeft)
                && IsFinite(bottomRight)
                && IsFinite(topRight)
                && IsFinite(topLeft);
            float horizontalSpan = Mathf.Max(
                Vector3.Distance(bottomLeft, bottomRight),
                Vector3.Distance(topLeft, topRight));
            float verticalSpan = Mathf.Max(
                Vector3.Distance(bottomLeft, topLeft),
                Vector3.Distance(bottomRight, topRight));
            Available = kind != HazardVisualKind.Unavailable
                && finite
                && horizontalSpan > 0.001f
                && verticalSpan > 0.001f;
            StableId = Available ? stableId : 0L;
            Kind = Available ? kind : HazardVisualKind.Unavailable;
            BottomLeft = Available ? bottomLeft : Vector3.zero;
            BottomRight = Available ? bottomRight : Vector3.zero;
            TopRight = Available ? topRight : Vector3.zero;
            TopLeft = Available ? topLeft : Vector3.zero;
            SurfaceNormal = Available
                ? SafeDirection(surfaceNormal, CalculateNormal(
                    bottomLeft,
                    bottomRight,
                    topLeft))
                : Vector3.zero;
            CaptureTimestampSeconds = Math.Max(0.0, captureTimestampSeconds);
            Confidence = Available ? Mathf.Clamp01(confidence) : 0f;
            Risk = Available ? Mathf.Clamp01(risk) : 0f;
            Source = Available ? source : SpatialObstacleSource.Unavailable;
            Owner = owner;
            ProbePurpose = probePurpose;
            HasWorldVelocity = Available
                && hasWorldVelocity
                && IsFinite(worldVelocity);
            WorldVelocity = HasWorldVelocity ? worldVelocity : Vector3.zero;
            HasFreshFloor = Available && hasFreshFloor && IsFinite(floorHeight);
            FloorHeight = HasFreshFloor ? floorHeight : 0f;
        }

        public Vector3 Center
        {
            get
            {
                return (BottomLeft + BottomRight + TopRight + TopLeft) * 0.25f;
            }
        }

        public float Width
        {
            get
            {
                return 0.5f * (Vector3.Distance(BottomLeft, BottomRight)
                    + Vector3.Distance(TopLeft, TopRight));
            }
        }

        public float Height
        {
            get
            {
                return 0.5f * (Vector3.Distance(BottomLeft, TopLeft)
                    + Vector3.Distance(BottomRight, TopRight));
            }
        }

        public HazardPresentationGeometry WithRisk(float risk)
        {
            return Copy(Risk: risk);
        }

        public HazardPresentationGeometry WithVelocity(
            Vector3 velocity,
            bool available = true)
        {
            return Copy(
                hasWorldVelocity: available,
                worldVelocity: velocity);
        }

        public HazardPresentationGeometry Translated(Vector3 delta)
        {
            if (!Available || !IsFinite(delta))
            {
                return this;
            }

            return new HazardPresentationGeometry(
                StableId,
                Kind,
                BottomLeft + delta,
                BottomRight + delta,
                TopRight + delta,
                TopLeft + delta,
                SurfaceNormal,
                CaptureTimestampSeconds,
                Confidence,
                Risk,
                Source,
                Owner,
                ProbePurpose,
                HasWorldVelocity,
                WorldVelocity,
                HasFreshFloor,
                FloorHeight);
        }

        public HazardPresentationGeometry WithStableId(long stableId)
        {
            if (!Available)
            {
                return this;
            }

            return new HazardPresentationGeometry(
                stableId,
                Kind,
                BottomLeft,
                BottomRight,
                TopRight,
                TopLeft,
                SurfaceNormal,
                CaptureTimestampSeconds,
                Confidence,
                Risk,
                Source,
                Owner,
                ProbePurpose,
                HasWorldVelocity,
                WorldVelocity,
                HasFreshFloor,
                FloorHeight);
        }

        public static HazardPresentationGeometry Lerp(
            HazardPresentationGeometry from,
            HazardPresentationGeometry to,
            float t)
        {
            if (!from.Available)
            {
                return to;
            }

            if (!to.Available || from.StableId != to.StableId)
            {
                return to.Available ? to : from;
            }

            float alpha = Mathf.Clamp01(t);
            return new HazardPresentationGeometry(
                to.StableId,
                to.Kind,
                Vector3.Lerp(from.BottomLeft, to.BottomLeft, alpha),
                Vector3.Lerp(from.BottomRight, to.BottomRight, alpha),
                Vector3.Lerp(from.TopRight, to.TopRight, alpha),
                Vector3.Lerp(from.TopLeft, to.TopLeft, alpha),
                Vector3.Slerp(from.SurfaceNormal, to.SurfaceNormal, alpha),
                to.CaptureTimestampSeconds,
                Mathf.Lerp(from.Confidence, to.Confidence, alpha),
                to.Risk,
                to.Source,
                to.Owner,
                to.ProbePurpose,
                to.HasWorldVelocity,
                to.WorldVelocity,
                to.HasFreshFloor,
                to.FloorHeight);
        }

        public static HazardPresentationGeometry CreatePlanePatch(
            long stableId,
            HazardVisualKind kind,
            Vector3 center,
            Vector3 normal,
            Vector3 gravityUp,
            float width,
            float height,
            double timestampSeconds,
            float confidence,
            SpatialObstacleSource source,
            SpatialProbeOwner owner,
            SpatialProbePurpose purpose,
            float risk = 0f,
            bool hasFreshFloor = false,
            float floorHeight = 0f)
        {
            Vector3 safeNormal = SafeDirection(normal, Vector3.back);
            ResolvePlaneBasis(
                safeNormal,
                gravityUp,
                out Vector3 right,
                out Vector3 up);

            float halfWidth = Mathf.Max(0.01f, width * 0.5f);
            float halfHeight = Mathf.Max(0.01f, height * 0.5f);
            return new HazardPresentationGeometry(
                stableId,
                kind,
                center - right * halfWidth - up * halfHeight,
                center + right * halfWidth - up * halfHeight,
                center + right * halfWidth + up * halfHeight,
                center - right * halfWidth + up * halfHeight,
                safeNormal,
                timestampSeconds,
                confidence,
                risk,
                source,
                owner,
                purpose,
                false,
                Vector3.zero,
                hasFreshFloor,
                floorHeight);
        }

        /// <summary>
        /// Resolves the canonical in-plane axes shared by cluster extent
        /// measurement and presentation geometry. Near-horizontal surfaces
        /// use projected world-forward so small floor-normal noise cannot turn
        /// a downslope gravity projection into a 90-degree corner rotation.
        /// Wall-like surfaces keep gravity aligned with the visual vertical.
        /// </summary>
        public static void ResolvePlaneBasis(
            Vector3 normal,
            Vector3 gravityUp,
            out Vector3 right,
            out Vector3 up)
        {
            Vector3 safeNormal = SafeDirection(normal, Vector3.back);
            Vector3 safeGravityUp = SafeDirection(gravityUp, Vector3.up);
            bool nearHorizontal = Mathf.Abs(Vector3.Dot(
                safeNormal,
                safeGravityUp)) >= NearHorizontalNormalDotThreshold;
            Vector3 reference = nearHorizontal
                ? Vector3.forward
                : safeGravityUp;
            up = Vector3.ProjectOnPlane(reference, safeNormal);
            if (up.sqrMagnitude <= 0.0001f)
            {
                reference = nearHorizontal
                    ? Vector3.right
                    : Vector3.forward;
                up = Vector3.ProjectOnPlane(reference, safeNormal);
            }

            up = SafeDirection(up, reference);
            right = SafeDirection(
                Vector3.Cross(up, safeNormal),
                Vector3.right);
            up = SafeDirection(Vector3.Cross(safeNormal, right), up);

            Vector3 orientationReference = Vector3.ProjectOnPlane(
                nearHorizontal ? reference : safeGravityUp,
                safeNormal);
            if (orientationReference.sqrMagnitude > 0.0001f
                && Vector3.Dot(up, orientationReference) < 0f)
            {
                up = -up;
                right = -right;
            }
        }

        public static NormalizedBoundingBox UpperBodyExpandedBox(
            NormalizedBoundingBox box,
            float upperBodyFraction = UpperBodyFraction,
            float paddingScale = DefaultPaddingScale)
        {
            float fraction = Mathf.Clamp(upperBodyFraction, 0.1f, 1f);
            float scale = Mathf.Max(1f, paddingScale);
            float top = box.Top;
            float rawHeight = Mathf.Max(0.001f, box.height * fraction);
            float centerY = top + rawHeight * 0.5f;
            return new NormalizedBoundingBox(
                box.centerX,
                centerY,
                Mathf.Clamp01(box.width * scale),
                Mathf.Clamp01(rawHeight * scale));
        }

        public static bool TryCreatePersonCapsule(
            long stableId,
            NormalizedBoundingBox box,
            Ray bottomLeftRay,
            Ray bottomRightRay,
            Ray topRightRay,
            Ray topLeftRay,
            Vector3 planePoint,
            Vector3 planeNormal,
            double timestampSeconds,
            float confidence,
            float risk,
            Vector3 worldVelocity,
            bool hasWorldVelocity,
            out HazardPresentationGeometry geometry)
        {
            if (!TryIntersectPlane(bottomLeftRay, planePoint, planeNormal, out Vector3 bl)
                || !TryIntersectPlane(bottomRightRay, planePoint, planeNormal, out Vector3 br)
                || !TryIntersectPlane(topRightRay, planePoint, planeNormal, out Vector3 tr)
                || !TryIntersectPlane(topLeftRay, planePoint, planeNormal, out Vector3 tl))
            {
                geometry = default;
                return false;
            }

            geometry = new HazardPresentationGeometry(
                stableId,
                HazardVisualKind.PersonCapsule,
                bl,
                br,
                tr,
                tl,
                planeNormal,
                timestampSeconds,
                confidence,
                risk,
                SpatialObstacleSource.EnvironmentDepth,
                SpatialProbeOwner.Head,
                SpatialProbePurpose.Standard,
                hasWorldVelocity,
                worldVelocity);
            return geometry.Available;
        }

        public static HazardPresentationGeometry CreateFloorCorridor(
            long stableId,
            Vector3 nearCenter,
            Vector3 farCenter,
            Vector3 up,
            float nearWidth,
            float farWidth,
            double timestampSeconds,
            float confidence,
            float risk,
            SpatialObstacleSource source)
        {
            Vector3 forward = Vector3.ProjectOnPlane(farCenter - nearCenter, up);
            forward = SafeDirection(forward, Vector3.forward);
            Vector3 right = SafeDirection(Vector3.Cross(up, forward), Vector3.right);
            float nearHalf = Mathf.Max(0.01f, nearWidth * 0.5f);
            float farHalf = Mathf.Max(0.01f, farWidth * 0.5f);
            return new HazardPresentationGeometry(
                stableId,
                HazardVisualKind.FloorCorridor,
                nearCenter - right * nearHalf,
                nearCenter + right * nearHalf,
                farCenter + right * farHalf,
                farCenter - right * farHalf,
                up,
                timestampSeconds,
                confidence,
                risk,
                source,
                SpatialProbeOwner.Head,
                SpatialProbePurpose.LocomotionCorridor,
                false,
                Vector3.zero,
                true,
                nearCenter.y);
        }

        public static bool TryIntersectPlane(
            Ray ray,
            Vector3 planePoint,
            Vector3 planeNormal,
            out Vector3 intersection)
        {
            Vector3 normal = SafeDirection(planeNormal, Vector3.forward);
            float denominator = Vector3.Dot(ray.direction, normal);
            if (Mathf.Abs(denominator) <= 0.00001f)
            {
                intersection = Vector3.zero;
                return false;
            }

            float distance = Vector3.Dot(planePoint - ray.origin, normal)
                / denominator;
            if (!IsFinite(distance) || distance <= 0f)
            {
                intersection = Vector3.zero;
                return false;
            }

            intersection = ray.origin + ray.direction * distance;
            return IsFinite(intersection);
        }

        private HazardPresentationGeometry Copy(
            float? Risk = null,
            bool? hasWorldVelocity = null,
            Vector3 worldVelocity = default)
        {
            bool velocityAvailable = hasWorldVelocity ?? HasWorldVelocity;
            return new HazardPresentationGeometry(
                StableId,
                Kind,
                BottomLeft,
                BottomRight,
                TopRight,
                TopLeft,
                SurfaceNormal,
                CaptureTimestampSeconds,
                Confidence,
                Risk ?? this.Risk,
                Source,
                Owner,
                ProbePurpose,
                velocityAvailable,
                velocityAvailable
                    ? (hasWorldVelocity.HasValue ? worldVelocity : WorldVelocity)
                    : Vector3.zero,
                HasFreshFloor,
                FloorHeight);
        }

        private static Vector3 CalculateNormal(
            Vector3 bottomLeft,
            Vector3 bottomRight,
            Vector3 topLeft)
        {
            return Vector3.Cross(bottomRight - bottomLeft, topLeft - bottomLeft);
        }

        private static Vector3 SafeDirection(Vector3 value, Vector3 fallback)
        {
            if (!IsFinite(value) || value.sqrMagnitude <= 0.000001f)
            {
                value = fallback;
            }

            return value.sqrMagnitude > 0.000001f
                ? value.normalized
                : Vector3.forward;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
