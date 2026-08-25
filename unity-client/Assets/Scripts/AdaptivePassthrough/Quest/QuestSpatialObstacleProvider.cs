#if ADAPTIVE_PASSTHROUGH_QUEST_CAMERA
using System;
using Meta.XR;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    [DefaultExecutionOrder(410)]
    [DisallowMultipleComponent]
    public sealed class QuestSpatialObstacleProvider : MonoBehaviour,
        ISpatialObstacleProvider,
        IStaticBoundaryFrameProvider
    {
        private const int MaximumProbeCount = 24;
        private const float MovementDirectionSpeed = 0.15f;
        private static readonly float[] ExtraProbeAngles =
            { -20f, 20f, -30f, 30f };

        private sealed class BodyState
        {
            public bool hasPosition;
            public Vector3 previousPosition;
            public Vector3 velocity;
            public bool hasDistance;
            public float filteredDistance;
            public double lastMeasurementAt;
            public bool rawSafetyOverlap;
            public bool confirmedSafetyOverlap;
            public int overlapConfirmations;
            public int overlapReleaseConfirmations;
        }

        [SerializeField] private EnvironmentRaycastManager raycastManager;
        [SerializeField] private TrackingQualityController qualityController;
        [SerializeField] private MonoBehaviour roomSceneProviderBehaviour;
        [SerializeField, Min(0.25f)] private float maximumDistanceMeters = 6f;
        [SerializeField, Min(0.01f)] private float headSafetyRadius = 0.16f;
        [SerializeField, Min(0.01f)] private float handSafetyRadius = 0.10f;
        [SerializeField, Min(0.01f)] private float smoothingTimeSeconds = 0.18f;
        [SerializeField, Min(0.01f)] private float clusterGapMeters = 0.30f;
        [SerializeField, Min(0.01f)] private float staleAfterSeconds = 0.25f;

        private readonly SpatialProbe[] probes =
            new SpatialProbe[MaximumProbeCount];
        private readonly SpatialObstacleMeasurement[] measurements =
            new SpatialObstacleMeasurement[MaximumProbeCount];
        private readonly SpatialProbeOwner[] measurementOwners =
            new SpatialProbeOwner[MaximumProbeCount];
        private readonly int[] sortedMeasurementIndices =
            new int[MaximumProbeCount];
        private readonly BodyState headState = new BodyState();
        private readonly BodyState leftState = new BodyState();
        private readonly BodyState rightState = new BodyState();

        private IStaticBoundaryFrameProvider roomSceneProvider;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        private double nextMeasurementAt;
        private double previousUpdateAt;
        private double lastEnvironmentHitAt;
        private long sequence;

        public StaticBoundaryRiskFrame CurrentStaticBoundaryFrame
        {
            get;
            private set;
        } = StaticBoundaryRiskFrame.Unavailable;

        public SpatialObstacleMeasurement LatestMeasurement
        {
            get;
            private set;
        }

        public bool IsEnvironmentDepthSupported =>
            EnvironmentRaycastManager.IsSupported;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            nextMeasurementAt = 0.0;
            previousUpdateAt = 0.0;
            lastEnvironmentHitAt = 0.0;
        }

        private void OnDisable()
        {
            CurrentStaticBoundaryFrame = StaticBoundaryRiskFrame.Unavailable;
            LatestMeasurement = SpatialObstacleMeasurement.Unavailable(0.0);
            ResetState(headState);
            ResetState(leftState);
            ResetState(rightState);
        }

        private void Update()
        {
            ResolveReferences();
            double now = Time.realtimeSinceStartupAsDouble;
            TrackingQualitySettings settings = qualityController != null
                ? qualityController.EffectiveSettings
                : TrackingQualitySettings.For(
                    TrackingQualityProfile.Balanced);
            if (now < nextMeasurementAt)
            {
                return;
            }

            double interval = 1.0 / Mathf.Max(1f, settings.spatialRateHz);
            if (nextMeasurementAt <= 0.0)
            {
                nextMeasurementAt = now;
            }

            do
            {
                nextMeasurementAt += interval;
            }
            while (nextMeasurementAt <= now);

            float startedAt = Time.realtimeSinceStartup;
            MeasureFrame(
                now,
                Mathf.Clamp(settings.spatialRayCount, 1, MaximumProbeCount));
            float elapsedMs =
                (Time.realtimeSinceStartup - startedAt) * 1000f;
            qualityController?.RecordSpatial(LatestMeasurement, elapsedMs);
        }

        public void Configure(
            EnvironmentRaycastManager environmentRaycastManager,
            TrackingQualityController trackingQuality,
            MonoBehaviour roomSceneFallback,
            Transform headTransform = null,
            Transform leftHandTransform = null,
            Transform rightHandTransform = null)
        {
            raycastManager = environmentRaycastManager;
            qualityController = trackingQuality;
            roomSceneProviderBehaviour = roomSceneFallback;
            head = headTransform;
            leftHand = leftHandTransform;
            rightHand = rightHandTransform;
            ResolveReferences();
        }

        public bool TryMeasure(
            SpatialProbe probe,
            out SpatialObstacleMeasurement measurement)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (raycastManager == null
                || !EnvironmentRaycastManager.IsSupported)
            {
                measurement = SpatialObstacleMeasurement.Unavailable(now);
                return false;
            }

            EnvironmentRaycastHit hit;
            bool hasHit = raycastManager.Raycast(
                    new Ray(probe.Origin, probe.Direction),
                    out hit,
                    maximumDistanceMeters)
                && hit.status == EnvironmentRaycastHitStatus.Hit;
            if (!hasHit)
            {
                measurement = SpatialObstacleMeasurement.Unavailable(now);
                return false;
            }

            float distance = Vector3.Distance(probe.Origin, hit.point);
            Vector3 hitPoint = hit.point;
            Vector3 normal = hit.normal;
            float closingSpeed = Mathf.Max(
                0f,
                Vector3.Dot(probe.Velocity, probe.Direction));
            measurement = new SpatialObstacleMeasurement(
                SpatialObstacleSource.EnvironmentDepth,
                now,
                true,
                distance,
                hitPoint,
                normal,
                closingSpeed,
                0.75f,
                1,
                0f,
                0f,
                false,
                false,
                1);
            return true;
        }

        private void MeasureFrame(double now, int requestedProbeCount)
        {
            if (head == null)
            {
                PublishFallbackOrUnavailable(now);
                return;
            }

            float deltaSeconds = previousUpdateAt > 0.0
                ? (float)Math.Max(0.0001, now - previousUpdateAt)
                : 0f;
            previousUpdateAt = now;
            UpdateVelocity(headState, head, deltaSeconds);
            UpdateVelocity(leftState, leftHand, deltaSeconds);
            UpdateVelocity(rightState, rightHand, deltaSeconds);
            UpdateSafetyOverlap(
                headState,
                head,
                headSafetyRadius);
            UpdateSafetyOverlap(
                leftState,
                leftHand,
                handSafetyRadius);
            UpdateSafetyOverlap(
                rightState,
                rightHand,
                handSafetyRadius);

            int probeCount = BuildProbes(requestedProbeCount);
            int hitCount = 0;
            for (int i = 0; i < probeCount; i++)
            {
                if (TryMeasure(probes[i], out measurements[hitCount]))
                {
                    measurementOwners[hitCount] = probes[i].Owner;
                    hitCount++;
                }
            }

            if (hitCount <= 0)
            {
                SpatialObstacleMeasurement overlapHead =
                    CreateOverlapOnlyMeasurement(
                        SpatialProbeOwner.Head,
                        headState,
                        now);
                SpatialObstacleMeasurement overlapLeft =
                    CreateOverlapOnlyMeasurement(
                        SpatialProbeOwner.LeftHand,
                        leftState,
                        now);
                SpatialObstacleMeasurement overlapRight =
                    CreateOverlapOnlyMeasurement(
                        SpatialProbeOwner.RightHand,
                        rightState,
                        now);
                if (overlapHead.Available
                    || overlapLeft.Available
                    || overlapRight.Available)
                {
                    lastEnvironmentHitAt = now;
                    LatestMeasurement = Closest(
                        overlapHead,
                        Closest(overlapLeft, overlapRight));
                    CurrentStaticBoundaryFrame = BuildRiskFrame(
                        now,
                        overlapHead,
                        overlapLeft,
                        overlapRight);
                    return;
                }

                float environmentAge = lastEnvironmentHitAt > 0.0
                    ? (float)Math.Max(0.0, now - lastEnvironmentHitAt)
                    : float.PositiveInfinity;
                bool hasEnvironmentMeasurement =
                    LatestMeasurement.Available
                    && LatestMeasurement.Source
                        == SpatialObstacleSource.EnvironmentDepth;
                if (!SpatialProviderSelection.ShouldUseRoomSceneFallback(
                        EnvironmentRaycastManager.IsSupported,
                        hasEnvironmentMeasurement,
                        environmentAge,
                        staleAfterSeconds))
                {
                    HoldLatestEnvironmentMeasurement(now, environmentAge);
                    return;
                }

                PublishFallbackOrUnavailable(now);
                return;
            }

            lastEnvironmentHitAt = now;

            SpatialObstacleMeasurement headMeasurement = Aggregate(
                SpatialProbeOwner.Head,
                measurements,
                hitCount,
                headState,
                now);
            SpatialObstacleMeasurement leftMeasurement = Aggregate(
                SpatialProbeOwner.LeftHand,
                measurements,
                hitCount,
                leftState,
                now);
            SpatialObstacleMeasurement rightMeasurement = Aggregate(
                SpatialProbeOwner.RightHand,
                measurements,
                hitCount,
                rightState,
                now);

            if (!headMeasurement.Available)
            {
                headMeasurement = ClosestMeasurement(measurements, hitCount);
            }

            LatestMeasurement = Closest(
                headMeasurement,
                Closest(leftMeasurement, rightMeasurement));
            CurrentStaticBoundaryFrame = BuildRiskFrame(
                now,
                headMeasurement,
                leftMeasurement,
                rightMeasurement);
        }

        private int BuildProbes(int requestedCount)
        {
            int count = 0;
            Vector3 forward = head.forward;
            Vector3 up = head.up;
            Vector3 right = head.right;
            AddProbe(ref count, requestedCount, SpatialProbeOwner.Head,
                head.position, forward, headState.velocity, headSafetyRadius);
            AddProbe(ref count, requestedCount, SpatialProbeOwner.Head,
                head.position, Rotate(forward, right, -10f), headState.velocity,
                headSafetyRadius);
            AddProbe(ref count, requestedCount, SpatialProbeOwner.Head,
                head.position, Rotate(forward, right, 10f), headState.velocity,
                headSafetyRadius);
            AddProbe(ref count, requestedCount, SpatialProbeOwner.Head,
                head.position, Rotate(forward, up, -10f), headState.velocity,
                headSafetyRadius);
            AddProbe(ref count, requestedCount, SpatialProbeOwner.Head,
                head.position, Rotate(forward, up, 10f), headState.velocity,
                headSafetyRadius);
            AddProbe(ref count, requestedCount, SpatialProbeOwner.Head,
                head.position,
                headState.velocity.magnitude >= MovementDirectionSpeed
                    ? headState.velocity.normalized
                    : forward,
                headState.velocity,
                headSafetyRadius);

            AddHandProbes(
                ref count,
                requestedCount,
                SpatialProbeOwner.LeftHand,
                leftHand,
                leftState);
            AddHandProbes(
                ref count,
                requestedCount,
                SpatialProbeOwner.RightHand,
                rightHand,
                rightState);

            int angleIndex = 0;
            while (count < requestedCount)
            {
                float angle = ExtraProbeAngles[
                    angleIndex % ExtraProbeAngles.Length];
                Vector3 axis = (angleIndex / ExtraProbeAngles.Length) % 2 == 0
                    ? up
                    : right;
                AddProbe(
                    ref count,
                    requestedCount,
                    SpatialProbeOwner.Head,
                    head.position,
                    Rotate(forward, axis, angle),
                    headState.velocity,
                    headSafetyRadius);
                angleIndex++;
            }

            return count;
        }

        private void AddHandProbes(
            ref int count,
            int requestedCount,
            SpatialProbeOwner owner,
            Transform handTransform,
            BodyState state)
        {
            if (handTransform == null || count >= requestedCount)
            {
                return;
            }

            Vector3 direction = state.velocity.magnitude >= MovementDirectionSpeed
                ? state.velocity.normalized
                : handTransform.forward;
            AddProbe(ref count, requestedCount, owner, handTransform.position,
                direction, state.velocity, handSafetyRadius);
            AddProbe(ref count, requestedCount, owner, handTransform.position,
                Rotate(direction, handTransform.up, -12f), state.velocity,
                handSafetyRadius);
            AddProbe(ref count, requestedCount, owner, handTransform.position,
                Rotate(direction, handTransform.up, 12f), state.velocity,
                handSafetyRadius);
        }

        private void AddProbe(
            ref int count,
            int requestedCount,
            SpatialProbeOwner owner,
            Vector3 origin,
            Vector3 direction,
            Vector3 velocity,
            float safetyRadius)
        {
            if (count >= requestedCount || count >= MaximumProbeCount)
            {
                return;
            }

            probes[count++] = new SpatialProbe(
                owner,
                origin,
                direction,
                velocity,
                safetyRadius);
        }

        private SpatialObstacleMeasurement Aggregate(
            SpatialProbeOwner owner,
            SpatialObstacleMeasurement[] source,
            int sourceCount,
            BodyState state,
            double now)
        {
            int count = 0;
            for (int i = 0; i < sourceCount; i++)
            {
                if (measurementOwners[i] != owner || !source[i].Available)
                {
                    continue;
                }

                sortedMeasurementIndices[count++] = i;
            }

            if (count <= 0)
            {
                return CreateOverlapOnlyMeasurement(owner, state, now);
            }

            for (int i = 1; i < count; i++)
            {
                int value = sortedMeasurementIndices[i];
                int j = i - 1;
                while (j >= 0
                    && source[sortedMeasurementIndices[j]].DistanceMeters
                        > source[value].DistanceMeters)
                {
                    sortedMeasurementIndices[j + 1] =
                        sortedMeasurementIndices[j];
                    j--;
                }

                sortedMeasurementIndices[j + 1] = value;
            }

            int clusterCount = 1;
            while (clusterCount < count)
            {
                float previous = source[
                    sortedMeasurementIndices[clusterCount - 1]].DistanceMeters;
                float current = source[
                    sortedMeasurementIndices[clusterCount]].DistanceMeters;
                if (current - previous > clusterGapMeters)
                {
                    break;
                }

                clusterCount++;
            }

            int medianPosition = clusterCount / 2;
            int selectedIndex = sortedMeasurementIndices[medianPosition];
            SpatialObstacleMeasurement selected = source[selectedIndex];
            float rawDistance = selected.DistanceMeters;
            double elapsed = state.hasDistance
                ? Math.Max(0.0, now - state.lastMeasurementAt)
                : 0.0;
            float alpha = state.hasDistance
                ? 1f - Mathf.Exp(
                    -(float)elapsed / Mathf.Max(0.01f, smoothingTimeSeconds))
                : 1f;
            float previousDistance = state.filteredDistance;
            float filteredDistance = state.hasDistance
                ? Mathf.Lerp(previousDistance, rawDistance, alpha)
                : rawDistance;
            float distanceClosingSpeed = state.hasDistance && elapsed > 0.0001
                ? Mathf.Max(
                    0f,
                    (previousDistance - filteredDistance) / (float)elapsed)
                : 0f;
            state.hasDistance = true;
            state.filteredDistance = filteredDistance;
            state.lastMeasurementAt = now;

            float dispersion = clusterCount <= 1
                ? 0f
                : source[sortedMeasurementIndices[clusterCount - 1]]
                    .DistanceMeters
                    - source[sortedMeasurementIndices[0]].DistanceMeters;
            float consistency = Mathf.Exp(
                -dispersion / Mathf.Max(0.01f, clusterGapMeters));
            float sampleRatio = clusterCount / (float)Mathf.Max(1, count);
            bool confirmedOverlap = state.confirmedSafetyOverlap;
            float confidence = confirmedOverlap
                ? 1f
                : Mathf.Clamp01(0.35f + 0.65f * sampleRatio * consistency);
            return new SpatialObstacleMeasurement(
                SpatialObstacleSource.EnvironmentDepth,
                now,
                true,
                filteredDistance,
                selected.HitPoint,
                selected.HitNormal,
                Mathf.Max(selected.ClosingSpeedMetersPerSecond,
                    distanceClosingSpeed),
                confidence,
                clusterCount,
                dispersion,
                0f,
                confirmedOverlap,
                state.rawSafetyOverlap,
                clusterCount);
        }

        private StaticBoundaryRiskFrame BuildRiskFrame(
            double now,
            SpatialObstacleMeasurement headMeasurement,
            SpatialObstacleMeasurement leftMeasurement,
            SpatialObstacleMeasurement rightMeasurement)
        {
            StaticRiskMeasurement headRisk = ToHeadRisk(headMeasurement);
            StaticHandRiskMeasurement leftRisk = ToHandRisk(
                leftMeasurement,
                leftHand,
                0);
            StaticHandRiskMeasurement rightRisk = ToHandRisk(
                rightMeasurement,
                rightHand,
                1);
            Vector3 headDirection = headMeasurement.Available
                ? headMeasurement.HitPoint - head.position
                : Vector3.zero;
            sequence++;
            return new StaticBoundaryRiskFrame(
                sequence,
                now,
                headRisk.Available,
                headRisk,
                Mathf.Clamp01(headState.velocity.magnitude / 0.5f),
                previousUpdateAt > 0.0,
                leftRisk,
                rightRisk,
                headDirection,
                headDirection.sqrMagnitude > 0.0001f,
                -1,
                Mathf.Max(
                    leftHand != null
                        ? Vector3.Distance(head.position, leftHand.position)
                        : 0f,
                    rightHand != null
                        ? Vector3.Distance(head.position, rightHand.position)
                        : 0f));
        }

        private static StaticRiskMeasurement ToHeadRisk(
            SpatialObstacleMeasurement measurement)
        {
            if (!measurement.Available)
            {
                return StaticRiskMeasurement.Unavailable;
            }

            float distanceRisk = StaticBoundaryRiskMath.DistanceRisk(
                measurement.DistanceMeters,
                1.5f);
            float ttcRisk = StaticBoundaryRiskMath.TimeToCollisionRisk(
                measurement.DistanceMeters,
                measurement.ClosingSpeedMetersPerSecond,
                2f,
                0.01f);
            float risk = StaticBoundaryRiskMath.WeightedHeadRisk(
                distanceRisk,
                ttcRisk,
                0f,
                0.2f,
                0.55f,
                0.35f,
                0f,
                0.10f);
            if (ShouldForceStaticEmergency(measurement))
            {
                risk = 1f;
            }

            return new StaticRiskMeasurement(
                true,
                measurement.DistanceMeters,
                float.IsInfinity(measurement.TimeToCollisionSeconds)
                    ? 0f
                    : measurement.TimeToCollisionSeconds,
                !float.IsInfinity(measurement.TimeToCollisionSeconds),
                measurement.ClosingSpeedMetersPerSecond,
                0f,
                distanceRisk,
                ttcRisk,
                0f,
                0.2f,
                risk);
        }

        private StaticHandRiskMeasurement ToHandRisk(
            SpatialObstacleMeasurement measurement,
            Transform handTransform,
            int index)
        {
            if (!measurement.Available || handTransform == null)
            {
                return StaticHandRiskMeasurement.Unavailable;
            }

            float extension = Vector3.Distance(
                head.position,
                handTransform.position);
            float distanceRisk = StaticBoundaryRiskMath.DistanceRisk(
                measurement.DistanceMeters,
                0.75f);
            float ttcRisk = StaticBoundaryRiskMath.TimeToCollisionRisk(
                measurement.DistanceMeters,
                measurement.ClosingSpeedMetersPerSecond,
                1.25f,
                0.01f);
            float reachGate = StaticBoundaryRiskMath.ReachGate(
                measurement.DistanceMeters,
                extension,
                0.75f,
                0.15f);
            float risk = StaticBoundaryRiskMath.WeightedHandRisk(
                reachGate,
                distanceRisk,
                ttcRisk,
                0.55f,
                0.45f);
            if (ShouldForceStaticEmergency(measurement))
            {
                risk = 1f;
            }

            Vector3 direction = measurement.HitPoint - handTransform.position;
            return new StaticHandRiskMeasurement(
                true,
                index,
                measurement.DistanceMeters,
                measurement.ClosingSpeedMetersPerSecond,
                measurement.DistanceMeters,
                extension,
                reachGate,
                distanceRisk,
                ttcRisk,
                risk,
                direction);
        }

        private void UpdateSafetyOverlap(
            BodyState state,
            Transform trackedTransform,
            float safetyRadius)
        {
            bool rawOverlap = trackedTransform != null
                && raycastManager != null
                && EnvironmentRaycastManager.IsSupported
                && CheckBoxHitOnly(
                    trackedTransform.position,
                    Vector3.one * Mathf.Max(0.01f, safetyRadius),
                    trackedTransform.rotation);
            state.rawSafetyOverlap = rawOverlap;
            UpdateSafetyOverlapConfirmation(
                rawOverlap,
                ref state.confirmedSafetyOverlap,
                ref state.overlapConfirmations,
                ref state.overlapReleaseConfirmations);
        }

        public static void UpdateSafetyOverlapConfirmation(
            bool rawOverlap,
            ref bool confirmedOverlap,
            ref int confirmationCount,
            ref int releaseCount)
        {
            if (rawOverlap)
            {
                confirmationCount++;
                releaseCount = 0;
                if (confirmationCount >= 2)
                {
                    confirmedOverlap = true;
                }

                return;
            }

            confirmationCount = 0;
            if (!confirmedOverlap)
            {
                releaseCount = 0;
                return;
            }

            releaseCount++;
            if (releaseCount >= 2)
            {
                confirmedOverlap = false;
                releaseCount = 0;
            }
        }

        private bool CheckBoxHitOnly(
            Vector3 center,
            Vector3 halfExtents,
            Quaternion orientation)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                int crossA = (axis + 1) % 3;
                int crossB = (axis + 2) % 3;
                for (int signA = -1; signA <= 1; signA += 2)
                {
                    for (int signB = -1; signB <= 1; signB += 2)
                    {
                        Vector3 localStart = Vector3.zero;
                        Vector3 localEnd = Vector3.zero;
                        localStart[axis] = -halfExtents[axis];
                        localEnd[axis] = halfExtents[axis];
                        localStart[crossA] = halfExtents[crossA] * signA;
                        localEnd[crossA] = localStart[crossA];
                        localStart[crossB] = halfExtents[crossB] * signB;
                        localEnd[crossB] = localStart[crossB];
                        Vector3 start = center + orientation * localStart;
                        Vector3 end = center + orientation * localEnd;
                        Vector3 direction = end - start;
                        float distance = direction.magnitude;
                        if (distance <= 0.01f)
                        {
                            continue;
                        }

                        EnvironmentRaycastHit hit;
                        raycastManager.Raycast(
                            new Ray(start, direction),
                            out hit,
                            distance);
                        if (IsSafetyOverlapStatus(hit.status))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public static bool IsSafetyOverlapStatus(
            EnvironmentRaycastHitStatus status)
        {
            return status == EnvironmentRaycastHitStatus.Hit;
        }

        public static bool ShouldForceStaticEmergency(
            SpatialObstacleMeasurement measurement)
        {
            return measurement.Available
                && (measurement.SafetyVolumeOverlap
                    || measurement.DistanceMeters <= 0.25f);
        }

        private SpatialObstacleMeasurement CreateOverlapOnlyMeasurement(
            SpatialProbeOwner owner,
            BodyState state,
            double now)
        {
            if (!state.confirmedSafetyOverlap)
            {
                return SpatialObstacleMeasurement.Unavailable(now);
            }

            Transform trackedTransform;
            float radius;
            switch (owner)
            {
                case SpatialProbeOwner.LeftHand:
                    trackedTransform = leftHand;
                    radius = handSafetyRadius;
                    break;
                case SpatialProbeOwner.RightHand:
                    trackedTransform = rightHand;
                    radius = handSafetyRadius;
                    break;
                default:
                    trackedTransform = head;
                    radius = headSafetyRadius;
                    break;
            }

            if (trackedTransform == null)
            {
                return SpatialObstacleMeasurement.Unavailable(now);
            }

            Vector3 direction = state.velocity.magnitude
                    >= MovementDirectionSpeed
                ? state.velocity.normalized
                : trackedTransform.forward;
            float closingSpeed = Mathf.Max(
                0f,
                Vector3.Dot(state.velocity, direction));
            return new SpatialObstacleMeasurement(
                SpatialObstacleSource.EnvironmentDepth,
                now,
                true,
                0f,
                trackedTransform.position + direction * radius,
                -direction,
                closingSpeed,
                1f,
                0,
                0f,
                0f,
                true,
                state.rawSafetyOverlap,
                0);
        }

        private void PublishFallbackOrUnavailable(double now)
        {
            StaticBoundaryRiskFrame fallback = roomSceneProvider == null
                ? null
                : roomSceneProvider.CurrentStaticBoundaryFrame;
            if (fallback != null
                && fallback.Available
                && now - fallback.TimestampSeconds <= staleAfterSeconds)
            {
                CurrentStaticBoundaryFrame = fallback;
                LatestMeasurement = new SpatialObstacleMeasurement(
                    SpatialObstacleSource.RoomScene,
                    now,
                    true,
                    fallback.Head.ClosestDistanceMeters,
                    head != null
                        ? head.position
                            + fallback.HeadHazardDirectionWorld
                            * fallback.Head.ClosestDistanceMeters
                        : Vector3.zero,
                    -fallback.HeadHazardDirectionWorld,
                    fallback.Head.TowardBoundarySpeed,
                    0.65f,
                    1,
                    0f,
                    (float)Math.Max(0.0, now - fallback.TimestampSeconds),
                    false);
                return;
            }

            CurrentStaticBoundaryFrame = StaticBoundaryRiskFrame.Unavailable;
            LatestMeasurement = SpatialObstacleMeasurement.Unavailable(now);
        }

        private void HoldLatestEnvironmentMeasurement(
            double now,
            float ageSeconds)
        {
            SpatialObstacleMeasurement value = LatestMeasurement;
            LatestMeasurement = new SpatialObstacleMeasurement(
                value.Source,
                now,
                value.Available,
                value.DistanceMeters,
                value.HitPoint,
                value.HitNormal,
                value.ClosingSpeedMetersPerSecond,
                value.Confidence * Mathf.Exp(-ageSeconds * 2f),
                value.SampleCount,
                value.SampleDispersionMeters,
                ageSeconds,
                value.SafetyVolumeOverlap,
                value.RawSafetyVolumeOverlap,
                value.ValidRayHitCount);
        }

        private void ResolveReferences()
        {
            if (raycastManager == null)
            {
                raycastManager = FindAnyObjectByType<EnvironmentRaycastManager>();
            }

            if (qualityController == null)
            {
                qualityController = FindAnyObjectByType<TrackingQualityController>();
            }

            roomSceneProvider = roomSceneProviderBehaviour
                as IStaticBoundaryFrameProvider;
            if (head == null && Camera.main != null)
            {
                head = Camera.main.transform;
            }
        }

        private static void UpdateVelocity(
            BodyState state,
            Transform trackedTransform,
            float deltaSeconds)
        {
            if (trackedTransform == null)
            {
                state.velocity = Vector3.zero;
                state.hasPosition = false;
                return;
            }

            Vector3 position = trackedTransform.position;
            state.velocity = state.hasPosition && deltaSeconds > 0f
                ? Vector3.Lerp(
                    state.velocity,
                    (position - state.previousPosition) / deltaSeconds,
                    0.35f)
                : Vector3.zero;
            state.previousPosition = position;
            state.hasPosition = true;
        }

        private static void ResetState(BodyState state)
        {
            state.hasPosition = false;
            state.velocity = Vector3.zero;
            state.hasDistance = false;
            state.filteredDistance = 0f;
            state.lastMeasurementAt = 0.0;
            state.rawSafetyOverlap = false;
            state.confirmedSafetyOverlap = false;
            state.overlapConfirmations = 0;
            state.overlapReleaseConfirmations = 0;
        }

        private static Vector3 Rotate(
            Vector3 direction,
            Vector3 axis,
            float degrees)
        {
            return Quaternion.AngleAxis(degrees, axis) * direction;
        }

        private static SpatialObstacleMeasurement ClosestMeasurement(
            SpatialObstacleMeasurement[] source,
            int count)
        {
            SpatialObstacleMeasurement closest =
                SpatialObstacleMeasurement.Unavailable(0.0);
            for (int i = 0; i < count; i++)
            {
                closest = Closest(closest, source[i]);
            }

            return closest;
        }

        private static SpatialObstacleMeasurement Closest(
            SpatialObstacleMeasurement a,
            SpatialObstacleMeasurement b)
        {
            if (!a.Available)
            {
                return b;
            }

            if (!b.Available)
            {
                return a;
            }

            if (a.SafetyVolumeOverlap != b.SafetyVolumeOverlap)
            {
                return a.SafetyVolumeOverlap ? a : b;
            }

            return a.DistanceMeters <= b.DistanceMeters ? a : b;
        }
    }
}
#endif
