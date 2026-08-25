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

        private readonly float[] pendingDepthDistances = new float[3];
        private readonly int[] pendingDepthConfirmations = new int[3];
        private readonly float[] acceptedDepthDistances = new float[3];
        private readonly bool[] hasAcceptedDepth = new bool[3];
        private readonly SpatialObstacleMeasurement[] lastApproved =
            new SpatialObstacleMeasurement[3];
        private readonly double[] lastApprovedAt = new double[3];
        private readonly bool[] hasLastApproved = new bool[3];

        public SpatialObstacleMeasurement Fuse(
            SpatialProbeOwner owner,
            SpatialObstacleMeasurement environment,
            SpatialObstacleMeasurement room,
            double timestampSeconds)
        {
            int index = Mathf.Clamp((int)owner, 0, 2);
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
                publishedSource = SpatialObstacleSource.Fused;
                float difference = Mathf.Abs(
                    environmentDistance - roomDistance);
                if (difference <= AgreementDistanceMeters)
                {
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
                }
                else
                {
                    ResetPending(index);
                    selected = room;
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
                environment.RejectionReason);
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
                environment.RejectionReason);
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
                environment.RejectionReason);
        }
    }
}
