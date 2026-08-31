using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    // Runtime-only fusion logic; no scene component is required.
    /// <summary>
    /// Stateful selector for simultaneous Environment Depth and Room Scene
    /// measurements. A conflicting, newly-nearer depth result must be seen
    /// twice before it can replace a stable Room Scene result, except for the
    /// fixed 0.25 m emergency distance.
    /// </summary>
    public sealed class SpatialObstacleFusionFilter
    {
        public const float AgreementDistanceMeters = 0.35f;
        public const float EmergencyDistanceMeters = 0.25f;
        public const float MinimumAgreementNormalDot = 0.866f;
        public const float MaximumAgreementPlaneOffsetMeters = 0.25f;
        public const float RoomBoundsToleranceMeters = 0.05f;

        private const int PurposeCount = 2;
        private const int FusionStateCount = 3 * PurposeCount;
        private readonly float[] pendingDepthDistances =
            new float[FusionStateCount];
        private readonly int[] pendingDepthConfirmations =
            new int[FusionStateCount];
        private readonly float[] acceptedDepthDistances =
            new float[FusionStateCount];
        private readonly bool[] hasAcceptedDepth =
            new bool[FusionStateCount];
        private readonly SpatialObstacleMeasurement[] lastApproved =
            new SpatialObstacleMeasurement[FusionStateCount];
        private readonly double[] lastApprovedAt =
            new double[FusionStateCount];
        private readonly bool[] hasLastApproved =
            new bool[FusionStateCount];

        public SpatialObstacleMeasurement Fuse(
            SpatialProbeOwner owner,
            SpatialObstacleMeasurement environment,
            SpatialObstacleMeasurement room,
            double timestampSeconds)
        {
            SpatialProbePurpose purpose = environment.Available
                ? environment.ProbePurpose
                : room.ProbePurpose;
            int index = Mathf.Clamp((int)owner, 0, 2) * PurposeCount
                + Mathf.Clamp((int)purpose, 0, PurposeCount - 1);
            bool environmentValid = environment.Available
                && !environment.SelfRejected
                && environment.Confidence > 0f;
            bool roomValid = room.Available && room.Confidence > 0f;
            float environmentDiagnosticDistance = environmentValid
                ? environment.DistanceMeters
                : environment.EnvironmentDistanceMeters;
            float environmentDistance = environmentValid
                ? environment.DistanceMeters
                : -1f;
            float roomDistance = roomValid ? room.DistanceMeters : -1f;

            if (!environmentValid && !roomValid)
            {
                ResetPending(index);
                double heldAge = Math.Max(
                    0.0,
                    timestampSeconds - lastApprovedAt[index]);
                if (hasLastApproved[index] && heldAge <= 0.25)
                {
                    return CopyHeld(
                        owner,
                        lastApproved[index],
                        environment,
                        timestampSeconds,
                        (float)heldAge,
                        environmentDiagnosticDistance,
                        roomDistance);
                }

                return UnavailableWithDiagnostics(
                    owner,
                    timestampSeconds,
                    environment,
                    environmentDiagnosticDistance,
                    roomDistance);
            }

            SpatialObstacleMeasurement selected;
            SpatialObstacleSource publishedSource;
            if (environmentValid && roomValid)
            {
                float difference = Mathf.Abs(
                    environmentDistance - roomDistance);
                bool samePresentationSurface =
                    AreMeasurementsSpatiallyCompatible(environment, room);
                if (difference <= AgreementDistanceMeters
                    && samePresentationSurface)
                {
                    publishedSource = SpatialObstacleSource.Fused;
                    ResetPending(index);
                    selected = environmentDistance <= roomDistance
                        ? environment
                        : room;
                }
                else if (environmentDistance < roomDistance)
                {
                    bool immediate = environmentDistance
                        <= EmergencyDistanceMeters;
                    selected = immediate
                        || ConfirmNearDepth(index, environmentDistance)
                            ? environment
                            : room;
                    publishedSource = selected.Source;
                }
                else
                {
                    ResetPending(index);
                    selected = room;
                    publishedSource = SpatialObstacleSource.RoomScene;
                }
            }
            else if (environmentValid)
            {
                bool newNearDepth = hasAcceptedDepth[index]
                    && environmentDistance
                        < acceptedDepthDistances[index]
                            - AgreementDistanceMeters;
                bool immediate = environmentDistance
                    <= EmergencyDistanceMeters;
                bool requiresNearConfirmation = !immediate
                    && environmentDistance <= 1.50f
                    && (!hasAcceptedDepth[index]
                        || environment.ValidRayHitCount <= 1
                        || newNearDepth);
                if (!requiresNearConfirmation
                    || immediate
                    || ConfirmNearDepth(index, environmentDistance))
                {
                    selected = environment;
                }
                else
                {
                    double heldAge = Math.Max(
                        0.0,
                        timestampSeconds - lastApprovedAt[index]);
                    if (hasLastApproved[index] && heldAge <= 0.25)
                    {
                        return CopyHeld(
                            owner,
                            lastApproved[index],
                            environment,
                            timestampSeconds,
                            (float)heldAge,
                            environmentDiagnosticDistance,
                            roomDistance);
                    }

                    return UnavailableWithDiagnostics(
                        owner,
                        timestampSeconds,
                        environment,
                        environmentDiagnosticDistance,
                        roomDistance);
                }
                publishedSource = SpatialObstacleSource.EnvironmentDepth;
            }
            else
            {
                ResetPending(index);
                selected = room;
                publishedSource = SpatialObstacleSource.RoomScene;
            }

            if (selected.Source == SpatialObstacleSource.EnvironmentDepth)
            {
                acceptedDepthDistances[index] = environmentDistance;
                hasAcceptedDepth[index] = true;
                ResetPending(index);
            }

            SpatialObstacleMeasurement result = CopySelected(
                owner,
                selected,
                environment,
                room,
                publishedSource,
                timestampSeconds,
                environmentDiagnosticDistance,
                roomDistance);
            lastApproved[index] = result;
            lastApprovedAt[index] = timestampSeconds;
            hasLastApproved[index] = true;
            return result;
        }

        public void Reset()
        {
            Array.Clear(
                pendingDepthDistances,
                0,
                pendingDepthDistances.Length);
            Array.Clear(
                pendingDepthConfirmations,
                0,
                pendingDepthConfirmations.Length);
            Array.Clear(
                acceptedDepthDistances,
                0,
                acceptedDepthDistances.Length);
            Array.Clear(
                hasAcceptedDepth,
                0,
                hasAcceptedDepth.Length);
            Array.Clear(lastApproved, 0, lastApproved.Length);
            Array.Clear(lastApprovedAt, 0, lastApprovedAt.Length);
            Array.Clear(hasLastApproved, 0, hasLastApproved.Length);
        }

        public static bool IsDirectionTowardEstimatedChest(
            Vector3 handPosition,
            Vector3 direction,
            Vector3 estimatedChestPosition,
            float minimumDot = 0.35f)
        {
            Vector3 toChest = estimatedChestPosition - handPosition;
            if (toChest.sqrMagnitude <= 0.0001f
                || direction.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            return Vector3.Dot(direction.normalized, toChest.normalized)
                >= Mathf.Clamp(minimumDot, -1f, 1f);
        }

        public static bool IsPointInsideEstimatedTorso(
            Vector3 point,
            Vector3 headPosition,
            Vector3 headUp,
            Vector3 headForward,
            float halfWidthMeters = 0.32f,
            float halfHeightMeters = 0.50f,
            float halfDepthMeters = 0.24f)
        {
            Vector3 up = Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(headForward, up);
            forward = forward.sqrMagnitude > 0.0001f
                ? forward.normalized
                : Vector3.forward;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            Vector3 torsoCenter = headPosition
                - up * 0.52f
                - forward * 0.08f;
            Vector3 local = point - torsoCenter;
            float x = Vector3.Dot(local, right)
                / Mathf.Max(0.01f, halfWidthMeters);
            float y = Vector3.Dot(local, up)
                / Mathf.Max(0.01f, halfHeightMeters);
            float z = Vector3.Dot(local, forward)
                / Mathf.Max(0.01f, halfDepthMeters);
            return x * x + y * y + z * z <= 1f;
        }

        public static bool AreMeasurementsSpatiallyCompatible(
            SpatialObstacleMeasurement environment,
            SpatialObstacleMeasurement room)
        {
            if (!environment.Available || !room.Available)
            {
                return false;
            }

            HazardPresentationGeometry environmentGeometry =
                environment.PresentationGeometry;
            HazardPresentationGeometry roomGeometry =
                RoomBoundsGeometry(room);
            Vector3 environmentNormal = environmentGeometry.Available
                ? environmentGeometry.SurfaceNormal
                : environment.HitNormal;
            Vector3 roomNormal = roomGeometry.Available
                ? roomGeometry.SurfaceNormal
                : room.HitNormal;
            if (environmentNormal.sqrMagnitude > 0.0001f
                && roomNormal.sqrMagnitude > 0.0001f
                && Mathf.Abs(Vector3.Dot(
                    environmentNormal.normalized,
                    roomNormal.normalized)) < MinimumAgreementNormalDot)
            {
                return false;
            }

            Vector3 safeRoomNormal = roomNormal.sqrMagnitude > 0.0001f
                ? roomNormal.normalized
                : Vector3.zero;
            Vector3 environmentPoint = environment.HitPoint;
            Vector3 roomPoint = roomGeometry.Available
                ? roomGeometry.Center
                : room.HitPoint;
            if (safeRoomNormal.sqrMagnitude > 0.0001f
                && Mathf.Abs(Vector3.Dot(
                    environmentPoint - roomPoint,
                    safeRoomNormal)) > MaximumAgreementPlaneOffsetMeters)
            {
                return false;
            }

            return !roomGeometry.Available
                || IsPointInsideRoomBounds(
                    environmentPoint,
                    roomGeometry,
                    RoomBoundsToleranceMeters);
        }

        private bool ConfirmNearDepth(int index, float distanceMeters)
        {
            if (pendingDepthConfirmations[index] > 0
                && Mathf.Abs(
                    pendingDepthDistances[index] - distanceMeters) <= 0.20f)
            {
                pendingDepthConfirmations[index]++;
            }
            else
            {
                pendingDepthDistances[index] = distanceMeters;
                pendingDepthConfirmations[index] = 1;
            }

            return pendingDepthConfirmations[index] >= 2;
        }

        private void ResetPending(int index)
        {
            pendingDepthDistances[index] = 0f;
            pendingDepthConfirmations[index] = 0;
        }

        private static SpatialObstacleMeasurement CopySelected(
            SpatialProbeOwner owner,
            SpatialObstacleMeasurement selected,
            SpatialObstacleMeasurement environment,
            SpatialObstacleMeasurement room,
            SpatialObstacleSource publishedSource,
            double timestampSeconds,
            float environmentDistance,
            float roomDistance)
        {
            bool confirmedHeadOverlap = owner == SpatialProbeOwner.Head
                && environment.SafetyVolumeOverlap;
            return new SpatialObstacleMeasurement(
                publishedSource,
                timestampSeconds,
                true,
                selected.DistanceMeters,
                selected.HitPoint,
                selected.HitNormal,
                selected.ClosingSpeedMetersPerSecond,
                selected.Confidence,
                selected.SampleCount,
                selected.SampleDispersionMeters,
                selected.AgeSeconds,
                confirmedHeadOverlap,
                environment.RawSafetyVolumeOverlap,
                selected.ValidRayHitCount,
                owner,
                selected.Source,
                environmentDistance,
                roomDistance,
                environment.SelfRejected,
                environment.RejectionReason,
                selected.ProbePurpose,
                selected.SurfaceId,
                SelectPresentationGeometry(
                    selected,
                    environment,
                    room,
                    publishedSource),
                selected.PresentationNormalChangeDegrees,
                room.SurfaceBoundsGeometry.Available
                    ? room.SurfaceBoundsGeometry
                    : selected.SurfaceBoundsGeometry);
        }

        private static SpatialObstacleMeasurement CopyHeld(
            SpatialProbeOwner owner,
            SpatialObstacleMeasurement held,
            SpatialObstacleMeasurement environment,
            double timestampSeconds,
            float ageSeconds,
            float environmentDistance,
            float roomDistance)
        {
            return new SpatialObstacleMeasurement(
                held.Source,
                timestampSeconds,
                true,
                held.DistanceMeters,
                held.HitPoint,
                held.HitNormal,
                held.ClosingSpeedMetersPerSecond,
                held.Confidence * Mathf.Exp(-ageSeconds * 2f),
                held.SampleCount,
                held.SampleDispersionMeters,
                ageSeconds,
                owner == SpatialProbeOwner.Head
                    && environment.SafetyVolumeOverlap,
                environment.RawSafetyVolumeOverlap,
                held.ValidRayHitCount,
                owner,
                held.SelectedSource,
                environmentDistance >= 0f
                    ? environmentDistance
                    : held.EnvironmentDistanceMeters,
                roomDistance >= 0f
                    ? roomDistance
                    : held.RoomSceneDistanceMeters,
                environment.SelfRejected,
                environment.RejectionReason,
                held.ProbePurpose,
                held.SurfaceId,
                held.PresentationGeometry,
                held.PresentationNormalChangeDegrees,
                held.SurfaceBoundsGeometry);
        }

        private static SpatialObstacleMeasurement UnavailableWithDiagnostics(
            SpatialProbeOwner owner,
            double timestampSeconds,
            SpatialObstacleMeasurement environment,
            float environmentDistance,
            float roomDistance)
        {
            return new SpatialObstacleMeasurement(
                SpatialObstacleSource.Unavailable,
                timestampSeconds,
                false,
                0f,
                Vector3.zero,
                Vector3.zero,
                0f,
                0f,
                0,
                0f,
                0f,
                owner == SpatialProbeOwner.Head
                    && environment.SafetyVolumeOverlap,
                environment.RawSafetyVolumeOverlap,
                0,
                owner,
                SpatialObstacleSource.Unavailable,
                environmentDistance,
                roomDistance,
                environment.SelfRejected,
                environment.RejectionReason,
                environment.ProbePurpose,
                environment.SurfaceId,
                default,
                environment.PresentationNormalChangeDegrees);
        }

        private static HazardPresentationGeometry SelectPresentationGeometry(
            SpatialObstacleMeasurement selected,
            SpatialObstacleMeasurement environment,
            SpatialObstacleMeasurement room,
            SpatialObstacleSource publishedSource)
        {
            HazardPresentationGeometry geometry = selected.PresentationGeometry;
            if (publishedSource == SpatialObstacleSource.Fused
                && environment.PresentationGeometry.Available
                && room.PresentationGeometry.Available
                && Mathf.Abs(
                    environment.DistanceMeters - room.DistanceMeters)
                    <= AgreementDistanceMeters
                && AreMeasurementsSpatiallyCompatible(environment, room))
            {
                HazardPresentationGeometry environmentGeometry =
                    environment.PresentationGeometry;
                HazardPresentationGeometry roomGeometry =
                    RoomBoundsGeometry(room);
                Vector3 roomNormal = roomGeometry.SurfaceNormal;
                float planeOffset = Vector3.Dot(
                    environmentGeometry.Center - roomGeometry.Center,
                    roomNormal);
                geometry = ProjectIntoRoomBounds(
                    environmentGeometry.Translated(
                        -roomNormal * planeOffset),
                    roomGeometry);
            }

            if (!geometry.Available
                && publishedSource == SpatialObstacleSource.Fused)
            {
                geometry = environment.PresentationGeometry.Available
                    ? environment.PresentationGeometry
                    : room.PresentationGeometry;
            }

            if (!geometry.Available || publishedSource != SpatialObstacleSource.Fused)
            {
                return geometry;
            }

            return new HazardPresentationGeometry(
                geometry.StableId,
                geometry.Kind,
                geometry.BottomLeft,
                geometry.BottomRight,
                geometry.TopRight,
                geometry.TopLeft,
                geometry.SurfaceNormal,
                geometry.CaptureTimestampSeconds,
                geometry.Confidence,
                geometry.Risk,
                SpatialObstacleSource.Fused,
                geometry.Owner,
                geometry.ProbePurpose,
                geometry.HasWorldVelocity,
                geometry.WorldVelocity,
                geometry.HasFreshFloor,
                geometry.FloorHeight);
        }

        private static HazardPresentationGeometry ProjectIntoRoomBounds(
            HazardPresentationGeometry projected,
            HazardPresentationGeometry room)
        {
            Vector3 right = room.BottomRight - room.BottomLeft;
            Vector3 up = room.TopLeft - room.BottomLeft;
            if (right.sqrMagnitude <= 0.0001f || up.sqrMagnitude <= 0.0001f)
            {
                return room;
            }

            right.Normalize();
            up.Normalize();
            float halfWidth = Mathf.Min(projected.Width, room.Width) * 0.5f;
            float halfHeight = Mathf.Min(projected.Height, room.Height) * 0.5f;
            Vector3 centerOffset = projected.Center - room.Center;
            float x = Mathf.Clamp(
                Vector3.Dot(centerOffset, right),
                -room.Width * 0.5f + halfWidth,
                room.Width * 0.5f - halfWidth);
            float y = Mathf.Clamp(
                Vector3.Dot(centerOffset, up),
                -room.Height * 0.5f + halfHeight,
                room.Height * 0.5f - halfHeight);
            Vector3 center = room.Center + right * x + up * y;
            return new HazardPresentationGeometry(
                room.StableId,
                projected.Kind,
                center - right * halfWidth - up * halfHeight,
                center + right * halfWidth - up * halfHeight,
                center + right * halfWidth + up * halfHeight,
                center - right * halfWidth + up * halfHeight,
                room.SurfaceNormal,
                projected.CaptureTimestampSeconds,
                Mathf.Min(projected.Confidence, room.Confidence),
                projected.Risk,
                projected.Source,
                projected.Owner,
                projected.ProbePurpose,
                projected.HasWorldVelocity,
                projected.WorldVelocity,
                projected.HasFreshFloor,
                projected.FloorHeight);
        }

        private static HazardPresentationGeometry RoomBoundsGeometry(
            SpatialObstacleMeasurement room)
        {
            return room.SurfaceBoundsGeometry.Available
                ? room.SurfaceBoundsGeometry
                : room.PresentationGeometry;
        }

        private static bool IsPointInsideRoomBounds(
            Vector3 point,
            HazardPresentationGeometry room,
            float toleranceMeters)
        {
            Vector3 right = room.BottomRight - room.BottomLeft;
            Vector3 up = room.TopLeft - room.BottomLeft;
            if (right.sqrMagnitude <= 0.0001f || up.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            float halfWidth = room.Width * 0.5f
                + Mathf.Max(0f, toleranceMeters);
            float halfHeight = room.Height * 0.5f
                + Mathf.Max(0f, toleranceMeters);
            Vector3 offset = point - room.Center;
            return Mathf.Abs(Vector3.Dot(offset, right.normalized)) <= halfWidth
                && Mathf.Abs(Vector3.Dot(offset, up.normalized)) <= halfHeight;
        }
    }
}
