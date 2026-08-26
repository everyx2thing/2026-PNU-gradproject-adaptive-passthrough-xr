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
        // Keep the origin just ahead of controller geometry without creating a
        // 12 cm blind zone in front of the user's hand.
        private const float HandRayOriginOffsetMeters = 0.03f;
        private const float MaximumClosingSpeedMetersPerSecond = 2f;
        private const float MaximumDistanceHistoryJumpMeters = 0.75f;
        private const float MinimumSameDirectionDot = 0.65f;
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
            public bool selfRejectedThisFrame;
            public string selfRejectionReason;
            public bool hasHazardDirection;
            public Vector3 previousHazardDirection;
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
        private readonly SpatialObstacleFusionFilter fusionFilter =
            new SpatialObstacleFusionFilter();

        private IStaticBoundaryFrameProvider roomSceneProvider;
        private ISpatialObstacleProvider roomSceneSpatialProvider;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        private double nextMeasurementAt;
        private double previousUpdateAt;
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

        public SpatialObstacleMeasurement LatestHeadMeasurement
        {
            get;
            private set;
        }

        public SpatialObstacleMeasurement LatestLeftHandMeasurement
        {
            get;
            private set;
        }

        public SpatialObstacleMeasurement LatestRightHandMeasurement
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
        }

        private void OnDisable()
        {
            CurrentStaticBoundaryFrame = StaticBoundaryRiskFrame.Unavailable;
            LatestMeasurement = SpatialObstacleMeasurement.Unavailable(0.0);
            LatestHeadMeasurement = SpatialObstacleMeasurement.Unavailable(0.0);
            LatestLeftHandMeasurement = SpatialObstacleMeasurement.Unavailable(0.0);
            LatestRightHandMeasurement = SpatialObstacleMeasurement.Unavailable(0.0);
            fusionFilter.Reset();
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
            BodyState state = StateFor(probe.Owner);
            bool isHand = probe.Owner != SpatialProbeOwner.Head;
            if (raycastManager == null
                || !EnvironmentRaycastManager.IsSupported)
            {
                measurement = SpatialObstacleMeasurement.Unavailable(now);
                return false;
            }

            EnvironmentRaycastHit hit;
            float originOffset = isHand
                ? HandRayOriginOffsetMeters
                : 0f;
            Vector3 rayOrigin = probe.Origin
                + probe.Direction * originOffset;
            bool hasHit = raycastManager.Raycast(
                    new Ray(rayOrigin, probe.Direction),
                    out hit,
                    maximumDistanceMeters)
                && hit.status == EnvironmentRaycastHitStatus.Hit;
            if (!hasHit)
            {
                measurement = SpatialObstacleMeasurement.Unavailable(now);
                return false;
            }

            if (isHand
                && head != null
                && SpatialObstacleFusionFilter.IsPointInsideEstimatedTorso(
                    hit.point,
                    head.position,
                    head.up,
                    head.forward))
            {
                state.selfRejectedThisFrame = true;
                state.selfRejectionReason = "hit-inside-estimated-torso";
                measurement = RejectedEnvironmentMeasurement(
                    probe.Owner,
                    now,
                    state.selfRejectionReason,
                    Vector3.Distance(probe.Origin, hit.point));
                return false;
            }

            float distance = originOffset
                + Vector3.Distance(rayOrigin, hit.point);
            Vector3 hitPoint = hit.point;
            Vector3 normal = hit.normal;
            float closingSpeed = Mathf.Max(
                0f,
                Vector3.Dot(probe.Velocity, probe.Direction));
            closingSpeed = Mathf.Min(
                MaximumClosingSpeedMetersPerSecond,
                closingSpeed);
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
                1,
                probe.Owner,
                SpatialObstacleSource.EnvironmentDepth,
                distance,
                -1f);
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
            UpdateVelocity(headState, head, deltaSeconds, 0.15f, 1.0f);
            UpdateVelocity(leftState, leftHand, deltaSeconds, 0.50f, 2.0f);
            UpdateVelocity(rightState, rightHand, deltaSeconds, 0.50f, 2.0f);
            ClearSelfRejection(leftState);
            ClearSelfRejection(rightState);
            UpdateSafetyOverlap(
                headState,
                head,
                headSafetyRadius);
            ClearSafetyOverlap(leftState);
            ClearSafetyOverlap(rightState);

            int probeCount = BuildProbes(requestedProbeCount);
            int headProbeCount = 0;
            int leftProbeCount = 0;
            int rightProbeCount = 0;
            int hitCount = 0;
            for (int i = 0; i < probeCount; i++)
            {
                switch (probes[i].Owner)
                {
                    case SpatialProbeOwner.Head:
                        headProbeCount++;
                        break;
                    case SpatialProbeOwner.LeftHand:
                        leftProbeCount++;
                        break;
                    case SpatialProbeOwner.RightHand:
                        rightProbeCount++;
                        break;
                }
                if (TryMeasure(probes[i], out measurements[hitCount]))
                {
                    measurementOwners[hitCount] = probes[i].Owner;
                    hitCount++;
                }
            }

            SpatialObstacleMeasurement headEnvironment = Aggregate(
                SpatialProbeOwner.Head,
                measurements,
                hitCount,
                headProbeCount,
                headState,
                now);
            SpatialObstacleMeasurement leftEnvironment = Aggregate(
                SpatialProbeOwner.LeftHand,
                measurements,
                hitCount,
                leftProbeCount,
                leftState,
                now);
            SpatialObstacleMeasurement rightEnvironment = Aggregate(
                SpatialProbeOwner.RightHand,
                measurements,
                hitCount,
                rightProbeCount,
                rightState,
                now);

            SpatialObstacleMeasurement headRoom = MeasureRoomScene(
                SpatialProbeOwner.Head,
                head,
                headState,
                headSafetyRadius,
                now);
            SpatialObstacleMeasurement leftRoom = MeasureRoomScene(
                SpatialProbeOwner.LeftHand,
                leftHand,
                leftState,
                handSafetyRadius,
                now);
            SpatialObstacleMeasurement rightRoom = MeasureRoomScene(
                SpatialProbeOwner.RightHand,
                rightHand,
                rightState,
                handSafetyRadius,
                now);

            LatestHeadMeasurement = fusionFilter.Fuse(
                SpatialProbeOwner.Head,
                headEnvironment,
                headRoom,
                now);
            LatestLeftHandMeasurement = fusionFilter.Fuse(
                SpatialProbeOwner.LeftHand,
                leftEnvironment,
                leftRoom,
                now);
            LatestRightHandMeasurement = fusionFilter.Fuse(
                SpatialProbeOwner.RightHand,
                rightEnvironment,
                rightRoom,
                now);
            LatestMeasurement = Closest(
                LatestHeadMeasurement,
                Closest(
                    LatestLeftHandMeasurement,
                    LatestRightHandMeasurement));
            CurrentStaticBoundaryFrame = BuildRiskFrame(
                now,
                LatestHeadMeasurement,
                LatestLeftHandMeasurement,
                LatestRightHandMeasurement);
            qualityController?.RecordSpatialOwners(
                LatestHeadMeasurement,
                LatestLeftHandMeasurement,
                LatestRightHandMeasurement);
        }

        private int BuildProbes(int requestedCount)
        {
            int count = 0;
            Vector3 forward = head.forward;
            Vector3 up = head.up;
            Vector3 right = head.right;

            // Interleave owners so every quality level measures both hands.
            // The first six probes are distributed 2/2/2 instead of being
            // consumed entirely by the head.
            AddProbe(ref count, requestedCount, SpatialProbeOwner.Head,
                head.position, forward, headState.velocity, headSafetyRadius);
            AddHandProbe(ref count, requestedCount,
                SpatialProbeOwner.LeftHand, leftHand, leftState, 0f);
            AddHandProbe(ref count, requestedCount,
                SpatialProbeOwner.RightHand, rightHand, rightState, 0f);
            AddProbe(ref count, requestedCount, SpatialProbeOwner.Head,
                head.position,
                headState.velocity.magnitude >= MovementDirectionSpeed
                    ? headState.velocity.normalized
                    : forward,
                headState.velocity,
                headSafetyRadius);
            AddHandProbe(ref count, requestedCount,
                SpatialProbeOwner.LeftHand, leftHand, leftState, -12f);
            AddHandProbe(ref count, requestedCount,
                SpatialProbeOwner.RightHand, rightHand, rightState, -12f);
            AddProbe(ref count, requestedCount, SpatialProbeOwner.Head,
                head.position, Rotate(forward, right, -10f), headState.velocity,
                headSafetyRadius);
            AddProbe(ref count, requestedCount, SpatialProbeOwner.Head,
                head.position, Rotate(forward, right, 10f), headState.velocity,
                headSafetyRadius);
            AddHandProbe(ref count, requestedCount,
                SpatialProbeOwner.LeftHand, leftHand, leftState, 12f);
            AddHandProbe(ref count, requestedCount,
                SpatialProbeOwner.RightHand, rightHand, rightState, 12f);
            AddProbe(ref count, requestedCount, SpatialProbeOwner.Head,
                head.position, Rotate(forward, up, -10f), headState.velocity,
                headSafetyRadius);
            AddProbe(ref count, requestedCount, SpatialProbeOwner.Head,
                head.position, Rotate(forward, up, 10f), headState.velocity,
                headSafetyRadius);

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

        private void AddHandProbe(
            ref int count,
            int requestedCount,
            SpatialProbeOwner owner,
            Transform handTransform,
            BodyState state,
            float yawDegrees)
        {
            if (handTransform == null || count >= requestedCount)
            {
                return;
            }

            Vector3 direction = state.velocity.magnitude >= MovementDirectionSpeed
                ? state.velocity.normalized
                : handTransform.forward;
            if (Mathf.Abs(yawDegrees) > 0.01f)
            {
                direction = Rotate(
                    direction,
                    HandSpreadAxis(direction, handTransform.right),
                    yawDegrees);
            }

            AddProbe(ref count, requestedCount, owner, handTransform.position,
                direction, state.velocity, handSafetyRadius);
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
            int attemptedProbeCount,
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
                SpatialObstacleMeasurement overlap =
                    CreateOverlapOnlyMeasurement(owner, state, now);
                return overlap.Available
                    ? overlap
                    : UnavailableEnvironmentMeasurement(owner, state, now);
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
            Transform trackedTransform = TransformFor(owner);
            Vector3 hazardDirection = trackedTransform == null
                ? Vector3.zero
                : selected.HitPoint - trackedTransform.position;
            bool resetDistanceHistory = ShouldResetDistanceHistory(
                state.hasHazardDirection,
                state.previousHazardDirection,
                hazardDirection,
                state.hasDistance,
                state.filteredDistance,
                rawDistance);
            if (resetDistanceHistory)
            {
                state.hasDistance = false;
            }

            double elapsed = state.hasDistance
                ? Math.Max(0.0, now - state.lastMeasurementAt)
                : 0.0;
            float alpha = state.hasDistance
                ? 1f - Mathf.Exp(
                    -(float)elapsed / Mathf.Max(0.01f, smoothingTimeSeconds))
                : 1f;
            float previousDistance = state.filteredDistance;
            float smoothedDistance = state.hasDistance
                ? Mathf.Lerp(previousDistance, rawDistance, alpha)
                : rawDistance;
            // Approaching measurements must not be delayed by smoothing. This
            // also guarantees that a raw <= 0.25 m hit reaches emergency logic
            // in the same spatial sample.
            float filteredDistance = state.hasDistance
                ? Mathf.Min(rawDistance, smoothedDistance)
                : rawDistance;
            float distanceClosingSpeed = state.hasDistance && elapsed > 0.0001
                ? Mathf.Max(
                    0f,
                    (previousDistance - filteredDistance) / (float)elapsed)
                : 0f;
            state.hasDistance = true;
            state.filteredDistance = filteredDistance;
            state.lastMeasurementAt = now;
            state.hasHazardDirection = hazardDirection.sqrMagnitude > 0.0001f;
            state.previousHazardDirection = state.hasHazardDirection
                ? hazardDirection.normalized
                : Vector3.zero;

            float dispersion = clusterCount <= 1
                ? 0f
                : source[sortedMeasurementIndices[clusterCount - 1]]
                    .DistanceMeters
                    - source[sortedMeasurementIndices[0]].DistanceMeters;
            float consistency = Mathf.Exp(
                -dispersion / Mathf.Max(0.01f, clusterGapMeters));
            float sampleRatio = clusterCount
                / (float)Mathf.Max(1, attemptedProbeCount);
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
                Mathf.Min(
                    MaximumClosingSpeedMetersPerSecond,
                    Mathf.Max(
                        resetDistanceHistory
                            ? 0f
                            : selected.ClosingSpeedMetersPerSecond,
                        distanceClosingSpeed)),
                confidence,
                clusterCount,
                dispersion,
                0f,
                confirmedOverlap,
                state.rawSafetyOverlap,
                clusterCount,
                owner,
                SpatialObstacleSource.EnvironmentDepth,
                filteredDistance,
                -1f,
                false,
                string.Empty);
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
                headRisk.Available
                    || leftRisk.Available
                    || rightRisk.Available
                    || headState.confirmedSafetyOverlap,
                headRisk,
                CalculateDirectionalHeadUserState(
                    headState.velocity,
                    headDirection),
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
                        : 0f),
                headState.confirmedSafetyOverlap);
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
            float directProximityRisk = Mathf.Clamp01(
                distanceRisk * 0.70f + ttcRisk * 0.30f);
            risk = Mathf.Max(risk, directProximityRisk);
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
            int edgeHitCount = trackedTransform != null
                && raycastManager != null
                && EnvironmentRaycastManager.IsSupported
                ? CountBoxEdgeHits(
                    trackedTransform.position,
                    Vector3.one * Mathf.Max(0.01f, safetyRadius),
                    trackedTransform.rotation)
                : 0;
            bool rawOverlap = edgeHitCount >= 2;
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

        private int CountBoxEdgeHits(
            Vector3 center,
            Vector3 halfExtents,
            Quaternion orientation)
        {
            int hitCount = 0;
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
                            hitCount++;
                        }
                    }
                }
            }
            return hitCount;
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
                && measurement.DistanceMeters <= 0.25f;
        }

        private SpatialObstacleMeasurement CreateOverlapOnlyMeasurement(
            SpatialProbeOwner owner,
            BodyState state,
            double now)
        {
            // An overlap without a supporting ray is diagnostic only. It must
            // never synthesize a zero-metre obstacle or force risk to 1.0.
            return UnavailableEnvironmentMeasurement(owner, state, now);
        }

        private SpatialObstacleMeasurement MeasureRoomScene(
            SpatialProbeOwner owner,
            Transform trackedTransform,
            BodyState state,
            float safetyRadius,
            double now)
        {
            if (trackedTransform == null || roomSceneSpatialProvider == null)
            {
                return SpatialObstacleMeasurement.Unavailable(now);
            }

            Vector3 direction = state.velocity.magnitude
                    >= MovementDirectionSpeed
                ? state.velocity.normalized
                : trackedTransform.forward;
            var probe = new SpatialProbe(
                owner,
                trackedTransform.position,
                direction,
                state.velocity,
                safetyRadius);
            return roomSceneSpatialProvider.TryMeasure(
                    probe,
                    out SpatialObstacleMeasurement measurement)
                ? measurement
                : SpatialObstacleMeasurement.Unavailable(now);
        }

        private static SpatialObstacleMeasurement
            UnavailableEnvironmentMeasurement(
                SpatialProbeOwner owner,
                BodyState state,
                double now)
        {
            return new SpatialObstacleMeasurement(
                SpatialObstacleSource.Unavailable,
                now,
                false,
                0f,
                Vector3.zero,
                Vector3.zero,
                0f,
                0f,
                0,
                0f,
                0f,
                state.confirmedSafetyOverlap,
                state.rawSafetyOverlap,
                0,
                owner,
                SpatialObstacleSource.Unavailable,
                -1f,
                -1f,
                state.selfRejectedThisFrame,
                state.selfRejectionReason);
        }

        private static SpatialObstacleMeasurement RejectedEnvironmentMeasurement(
            SpatialProbeOwner owner,
            double now,
            string reason,
            float rawDistanceMeters = -1f)
        {
            return new SpatialObstacleMeasurement(
                SpatialObstacleSource.Unavailable,
                now,
                false,
                0f,
                Vector3.zero,
                Vector3.zero,
                0f,
                0f,
                0,
                0f,
                0f,
                false,
                false,
                0,
                owner,
                SpatialObstacleSource.Unavailable,
                rawDistanceMeters,
                -1f,
                true,
                reason);
        }

        private Vector3 EstimatedChestPosition()
        {
            if (head == null)
            {
                return Vector3.zero;
            }

            Vector3 yawForward = Vector3.ProjectOnPlane(
                head.forward,
                Vector3.up);
            yawForward = yawForward.sqrMagnitude > 0.0001f
                ? yawForward.normalized
                : Vector3.forward;
            return head.position - Vector3.up * 0.45f
                - yawForward * 0.10f;
        }

        public static Vector3 HandSpreadAxis(
            Vector3 direction,
            Vector3 controllerRight)
        {
            Vector3 normalizedDirection = direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.forward;
            Vector3 axis = Vector3.ProjectOnPlane(
                Vector3.up,
                normalizedDirection);
            if (axis.sqrMagnitude <= 0.0001f)
            {
                axis = Vector3.ProjectOnPlane(
                    controllerRight,
                    normalizedDirection);
            }

            if (axis.sqrMagnitude <= 0.0001f)
            {
                axis = Vector3.Cross(
                    normalizedDirection,
                    Vector3.forward);
            }

            return axis.sqrMagnitude > 0.0001f
                ? axis.normalized
                : Vector3.right;
        }

        public static float CalculateDirectionalHeadUserState(
            Vector3 headVelocity,
            Vector3 hazardDirection)
        {
            if (hazardDirection.sqrMagnitude <= 0.0001f)
            {
                return 0f;
            }

            float approachSpeed = Mathf.Max(
                0f,
                Vector3.Dot(
                    headVelocity,
                    hazardDirection.normalized));
            return Mathf.Clamp01((approachSpeed - 0.10f) / 0.90f);
        }

        private BodyState StateFor(SpatialProbeOwner owner)
        {
            switch (owner)
            {
                case SpatialProbeOwner.LeftHand:
                    return leftState;
                case SpatialProbeOwner.RightHand:
                    return rightState;
                default:
                    return headState;
            }
        }

        private Transform TransformFor(SpatialProbeOwner owner)
        {
            switch (owner)
            {
                case SpatialProbeOwner.LeftHand:
                    return leftHand;
                case SpatialProbeOwner.RightHand:
                    return rightHand;
                default:
                    return head;
            }
        }

        public static bool ShouldResetDistanceHistory(
            bool hasPreviousDirection,
            Vector3 previousDirection,
            Vector3 currentDirection,
            bool hasPreviousDistance,
            float previousDistance,
            float currentDistance)
        {
            if (!hasPreviousDirection
                || !hasPreviousDistance
                || currentDirection.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            float directionDot = Vector3.Dot(
                previousDirection.normalized,
                currentDirection.normalized);
            return directionDot < MinimumSameDirectionDot
                || Mathf.Abs(currentDistance - previousDistance)
                    > MaximumDistanceHistoryJumpMeters;
        }

        private static void ClearSelfRejection(BodyState state)
        {
            state.selfRejectedThisFrame = false;
            state.selfRejectionReason = string.Empty;
        }

        private static void ClearSafetyOverlap(BodyState state)
        {
            state.rawSafetyOverlap = false;
            state.confirmedSafetyOverlap = false;
            state.overlapConfirmations = 0;
            state.overlapReleaseConfirmations = 0;
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
            roomSceneSpatialProvider = roomSceneProviderBehaviour
                as ISpatialObstacleProvider;
            if (head == null && Camera.main != null)
            {
                head = Camera.main.transform;
            }
        }

        private static void UpdateVelocity(
            BodyState state,
            Transform trackedTransform,
            float deltaSeconds,
            float maximumPositionDeltaMeters,
            float maximumSpeedMetersPerSecond)
        {
            if (trackedTransform == null)
            {
                state.velocity = Vector3.zero;
                state.hasPosition = false;
                return;
            }

            Vector3 position = trackedTransform.position;
            if (state.hasPosition && deltaSeconds > 0f)
            {
                Vector3 positionDelta = position - state.previousPosition;
                Vector3 instantaneousVelocity = positionDelta.magnitude
                        > Mathf.Max(0.01f, maximumPositionDeltaMeters)
                    ? Vector3.zero
                    : Vector3.ClampMagnitude(
                        positionDelta / deltaSeconds,
                        Mathf.Max(0.01f, maximumSpeedMetersPerSecond));
                state.velocity = Vector3.Lerp(
                    state.velocity,
                    instantaneousVelocity,
                    0.35f);
            }
            else
            {
                state.velocity = Vector3.zero;
            }
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
            state.selfRejectedThisFrame = false;
            state.selfRejectionReason = string.Empty;
            state.hasHazardDirection = false;
            state.previousHazardDirection = Vector3.zero;
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
