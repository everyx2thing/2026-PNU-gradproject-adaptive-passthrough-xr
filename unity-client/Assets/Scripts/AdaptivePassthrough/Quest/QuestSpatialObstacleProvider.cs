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
        private const float FloorNormalMinimumDot = 0.70f;
        private const float FloorEstimateMaximumJumpMeters = 0.25f;
        private const float FloorEstimateMaximumAgeSeconds = 2f;
        private const float FloorProbeForwardOffsetMeters = 0.12f;
        private const float FloorProbeMaximumDistanceMeters = 2.5f;
        private const float MinimumWallAngularWidthDegrees = 12f;
        private const float MaximumWallAngularWidthDegrees = 45f;
        private const float MinimumWallAngularHeightDegrees = 18f;
        private const float MaximumWallAngularHeightDegrees = 60f;
        public const int SafetyBoxEdgeCount = 12;
        public const int MaximumSafetyEdgeRaycastsPerMeasurement = 4;
        private const int AllSafetyEdgesSampledMask =
            (1 << SafetyBoxEdgeCount) - 1;
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
            public bool hasPresentationSurface;
            public int presentationSurfaceId;
            public Vector3 presentationSurfacePoint;
            public Vector3 presentationSurfaceNormal;
            public int nextSafetyEdgeIndex;
            public int safetyEdgeHitMask;
            public int safetyEdgeSampledMask;
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
        [SerializeField, Range(0.05f, 0.40f)]
        private float locomotionCorridorHalfWidth = 0.20f;
        [SerializeField, Range(0.03f, 0.30f)]
        private float minimumLowObstacleHeight = 0.08f;

        private readonly SpatialProbe[] probes =
            new SpatialProbe[MaximumProbeCount];
        private readonly SpatialObstacleMeasurement[] measurements =
            new SpatialObstacleMeasurement[MaximumProbeCount];
        private readonly SpatialProbeOwner[] measurementOwners =
            new SpatialProbeOwner[MaximumProbeCount];
        private readonly int[] sortedMeasurementIndices =
            new int[MaximumProbeCount];
        private readonly BodyState headState = new BodyState();
        private readonly BodyState locomotionState = new BodyState();
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
        private int nextEnvironmentSurfaceId = 2000;
        private bool hasFloorEstimate;
        private float filteredFloorHeight;
        private double lastFloorEstimateAt;
        private bool applicationPaused;

        public int LatestHeadSafetyEdgeRaycastCount
        {
            get;
            private set;
        }

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

        public SpatialObstacleMeasurement LatestLocomotionMeasurement
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
            applicationPaused = false;
            ResetTrackingSession();
        }

        private void OnDisable()
        {
            applicationPaused = false;
            ResetTrackingSession();
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            ResetTrackingSession();
        }

        private void OnApplicationFocus(bool focused)
        {
            applicationPaused = !focused;
            ResetTrackingSession();
        }

        private void Update()
        {
            if (applicationPaused)
            {
                return;
            }

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

        public void ResetTrackingSession()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            nextMeasurementAt = 0.0;
            previousUpdateAt = 0.0;
            CurrentStaticBoundaryFrame = StaticBoundaryRiskFrame.Unavailable;
            LatestMeasurement = SpatialObstacleMeasurement.Unavailable(now);
            LatestHeadMeasurement = SpatialObstacleMeasurement.Unavailable(now);
            LatestLocomotionMeasurement =
                SpatialObstacleMeasurement.Unavailable(
                    now,
                    SpatialProbeOwner.Head,
                    SpatialProbePurpose.LocomotionCorridor);
            LatestLeftHandMeasurement = SpatialObstacleMeasurement.Unavailable(
                now,
                SpatialProbeOwner.LeftHand,
                SpatialProbePurpose.Standard);
            LatestRightHandMeasurement = SpatialObstacleMeasurement.Unavailable(
                now,
                SpatialProbeOwner.RightHand,
                SpatialProbePurpose.Standard);
            fusionFilter.Reset();
            ResetState(headState);
            ResetState(locomotionState);
            ResetState(leftState);
            ResetState(rightState);
            hasFloorEstimate = false;
            filteredFloorHeight = 0f;
            lastFloorEstimateAt = 0.0;
            LatestHeadSafetyEdgeRaycastCount = 0;
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
            if (isHand
                && head != null
                && SpatialObstacleFusionFilter.IsDirectionTowardEstimatedChest(
                    rayOrigin,
                    probe.Direction,
                    EstimatedChestPosition(),
                    0.80f))
            {
                state.selfRejectedThisFrame = true;
                state.selfRejectionReason = "ray-toward-estimated-chest";
                measurement = RejectedEnvironmentMeasurement(
                    probe.Owner,
                    now,
                    state.selfRejectionReason,
                    -1f,
                    probe.Purpose);
                return false;
            }

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

            bool isLocomotionCorridor = probe.Purpose
                == SpatialProbePurpose.LocomotionCorridor;
            if (isLocomotionCorridor
                && head != null
                && SpatialObstacleFusionFilter.IsPointInsideEstimatedTorso(
                    hit.point,
                    head.position,
                    Vector3.up,
                    HorizontalDirection(head.forward, Vector3.forward)))
            {
                measurement = RejectedEnvironmentMeasurement(
                    probe.Owner,
                    now,
                    "locomotion-self-body",
                    Vector3.Distance(probe.Origin, hit.point),
                    probe.Purpose);
                return false;
            }

            bool floorEstimateFresh = hasFloorEstimate
                && now - lastFloorEstimateAt <= FloorEstimateMaximumAgeSeconds;
            if (isLocomotionCorridor
                && ShouldRejectLocomotionFloorHit(
                    hit.point,
                    hit.normal,
                    floorEstimateFresh,
                    filteredFloorHeight,
                    minimumLowObstacleHeight))
            {
                measurement = RejectedEnvironmentMeasurement(
                    probe.Owner,
                    now,
                    floorEstimateFresh
                        ? "locomotion-floor-baseline"
                        : "locomotion-horizontal-no-floor-baseline",
                    Vector3.Distance(probe.Origin, hit.point),
                    probe.Purpose);
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
                    Vector3.Distance(probe.Origin, hit.point),
                    probe.Purpose);
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
                -1f,
                false,
                null,
                probe.Purpose,
                EnvironmentSurfaceId(probe.Owner, probe.Purpose),
                CreateEnvironmentPatch(
                    probe,
                    hitPoint,
                    normal,
                    distance,
                    now,
                    0.75f));
            return true;
        }

        private void MeasureFrame(double now, int requestedProbeCount)
        {
            if (head == null)
            {
                PublishFallbackOrUnavailable(now);
                return;
            }

            bool motionWindowWarmedUp = previousUpdateAt > 0.0;
            float deltaSeconds = motionWindowWarmedUp
                ? (float)Math.Max(0.0001, now - previousUpdateAt)
                : 0f;
            previousUpdateAt = now;
            UpdateVelocity(headState, head, deltaSeconds, 0.15f, 1.0f);
            locomotionState.velocity = headState.velocity;
            UpdateVelocity(leftState, leftHand, deltaSeconds, 0.50f, 2.0f);
            UpdateVelocity(rightState, rightHand, deltaSeconds, 0.50f, 2.0f);
            UpdateFloorEstimate(now);
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
            int locomotionProbeCount = 0;
            int leftProbeCount = 0;
            int rightProbeCount = 0;
            int hitCount = 0;
            for (int i = 0; i < probeCount; i++)
            {
                switch (probes[i].Owner)
                {
                    case SpatialProbeOwner.Head:
                        if (probes[i].Purpose
                            == SpatialProbePurpose.LocomotionCorridor)
                        {
                            locomotionProbeCount++;
                        }
                        else
                        {
                            headProbeCount++;
                        }
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
                now,
                SpatialProbePurpose.Standard);
            SpatialObstacleMeasurement locomotionEnvironment = Aggregate(
                SpatialProbeOwner.Head,
                measurements,
                hitCount,
                locomotionProbeCount,
                locomotionState,
                now,
                SpatialProbePurpose.LocomotionCorridor);
            SpatialObstacleMeasurement leftEnvironment = Aggregate(
                SpatialProbeOwner.LeftHand,
                measurements,
                hitCount,
                leftProbeCount,
                leftState,
                now,
                SpatialProbePurpose.Standard);
            SpatialObstacleMeasurement rightEnvironment = Aggregate(
                SpatialProbeOwner.RightHand,
                measurements,
                hitCount,
                rightProbeCount,
                rightState,
                now,
                SpatialProbePurpose.Standard);

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
            SpatialObstacleMeasurement locomotionRoom = MeasureRoomScene(
                SpatialProbeOwner.Head,
                head,
                locomotionState,
                headSafetyRadius,
                now,
                SpatialProbePurpose.LocomotionCorridor);
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
            LatestLocomotionMeasurement = fusionFilter.Fuse(
                SpatialProbeOwner.Head,
                locomotionEnvironment,
                locomotionRoom,
                now);
            LatestRightHandMeasurement = fusionFilter.Fuse(
                SpatialProbeOwner.RightHand,
                rightEnvironment,
                rightRoom,
                now);
            LatestMeasurement = Closest(
                LatestHeadMeasurement,
                Closest(
                    LatestLocomotionMeasurement,
                    Closest(
                        LatestLeftHandMeasurement,
                        LatestRightHandMeasurement)));
            CurrentStaticBoundaryFrame = BuildRiskFrame(
                now,
                LatestHeadMeasurement,
                LatestLocomotionMeasurement,
                LatestLeftHandMeasurement,
                LatestRightHandMeasurement,
                motionWindowWarmedUp);
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

            // Interleave owners and one gravity-aligned corridor ray in the
            // first six so even Performance retains low-obstacle coverage.
            AddProbe(ref count, requestedCount, SpatialProbeOwner.Head,
                head.position, forward, headState.velocity, headSafetyRadius);
            AddHandProbe(ref count, requestedCount,
                SpatialProbeOwner.LeftHand, leftHand, leftState, 0f);
            AddHandProbe(ref count, requestedCount,
                SpatialProbeOwner.RightHand, rightHand, rightState, 0f);
            AddPrimaryLocomotionCorridorProbe(
                ref count,
                requestedCount,
                forward,
                headState.velocity);
            AddHandProbe(ref count, requestedCount,
                SpatialProbeOwner.LeftHand, leftHand, leftState, -12f);
            AddHandProbe(ref count, requestedCount,
                SpatialProbeOwner.RightHand, rightHand, rightState, -12f);
            AddProbe(ref count, requestedCount, SpatialProbeOwner.Head,
                head.position,
                headState.velocity.magnitude >= MovementDirectionSpeed
                    ? headState.velocity.normalized
                    : forward,
                headState.velocity,
                headSafetyRadius);
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
            // Balanced receives one supplemental centre ray. Accuracy also
            // receives lateral corridor coverage before extra head rays.
            AddSupplementalLocomotionCorridorProbes(
                ref count,
                requestedCount,
                forward,
                headState.velocity);

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
            AddProbe(
                ref count,
                requestedCount,
                owner,
                origin,
                direction,
                velocity,
                safetyRadius,
                SpatialProbePurpose.Standard);
        }

        private void AddProbe(
            ref int count,
            int requestedCount,
            SpatialProbeOwner owner,
            Vector3 origin,
            Vector3 direction,
            Vector3 velocity,
            float safetyRadius,
            SpatialProbePurpose purpose)
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
                safetyRadius,
                purpose);
        }

        private void AddPrimaryLocomotionCorridorProbe(
            ref int count,
            int requestedCount,
            Vector3 gazeForward,
            Vector3 headVelocity)
        {
            Vector3 horizontalVelocity = Vector3.ProjectOnPlane(
                headVelocity,
                Vector3.up);
            Vector3 horizontalForward = horizontalVelocity.magnitude
                    >= MovementDirectionSpeed
                ? horizontalVelocity.normalized
                : HorizontalDirection(gazeForward, Vector3.forward);
            Vector3 centerOrigin = head.position
                + horizontalForward * FloorProbeForwardOffsetMeters;

            AddProbe(
                ref count,
                requestedCount,
                SpatialProbeOwner.Head,
                centerOrigin,
                LocomotionCorridorDirection(horizontalForward, 42f),
                headVelocity,
                headSafetyRadius,
                SpatialProbePurpose.LocomotionCorridor);
        }

        private void AddSupplementalLocomotionCorridorProbes(
            ref int count,
            int requestedCount,
            Vector3 gazeForward,
            Vector3 headVelocity)
        {
            Vector3 horizontalVelocity = Vector3.ProjectOnPlane(
                headVelocity,
                Vector3.up);
            Vector3 horizontalForward = horizontalVelocity.magnitude
                    >= MovementDirectionSpeed
                ? horizontalVelocity.normalized
                : HorizontalDirection(gazeForward, Vector3.forward);
            Vector3 horizontalRight = Vector3.Cross(
                Vector3.up,
                horizontalForward).normalized;
            Vector3 centerOrigin = head.position
                + horizontalForward * FloorProbeForwardOffsetMeters;

            AddProbe(
                ref count,
                requestedCount,
                SpatialProbeOwner.Head,
                centerOrigin,
                LocomotionCorridorDirection(horizontalForward, 55f),
                headVelocity,
                headSafetyRadius,
                SpatialProbePurpose.LocomotionCorridor);
            AddProbe(
                ref count,
                requestedCount,
                SpatialProbeOwner.Head,
                centerOrigin - horizontalRight * locomotionCorridorHalfWidth,
                LocomotionCorridorDirection(horizontalForward, 42f),
                headVelocity,
                headSafetyRadius,
                SpatialProbePurpose.LocomotionCorridor);
            AddProbe(
                ref count,
                requestedCount,
                SpatialProbeOwner.Head,
                centerOrigin + horizontalRight * locomotionCorridorHalfWidth,
                LocomotionCorridorDirection(horizontalForward, 42f),
                headVelocity,
                headSafetyRadius,
                SpatialProbePurpose.LocomotionCorridor);
        }

        private SpatialObstacleMeasurement Aggregate(
            SpatialProbeOwner owner,
            SpatialObstacleMeasurement[] source,
            int sourceCount,
            int attemptedProbeCount,
            BodyState state,
            double now,
            SpatialProbePurpose purpose)
        {
            int count = 0;
            for (int i = 0; i < sourceCount; i++)
            {
                if (measurementOwners[i] != owner
                    || source[i].ProbePurpose != purpose
                    || !source[i].Available)
                {
                    continue;
                }

                sortedMeasurementIndices[count++] = i;
            }

            if (count <= 0)
            {
                SpatialObstacleMeasurement overlap = purpose
                        == SpatialProbePurpose.Standard
                    ? CreateOverlapOnlyMeasurement(owner, state, now)
                    : SpatialObstacleMeasurement.Unavailable(now);
                return overlap.Available
                    ? overlap
                    : UnavailableEnvironmentMeasurement(
                        owner,
                        state,
                        now,
                        purpose);
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
            HazardPresentationGeometry presentationGeometry =
                BuildClusterGeometry(
                    source,
                    clusterCount,
                    selected,
                    owner,
                    purpose,
                    filteredDistance,
                    now,
                    confidence);
            int surfaceId = ResolveEnvironmentSurfaceId(
                state,
                presentationGeometry.Center,
                presentationGeometry.SurfaceNormal);
            presentationGeometry = presentationGeometry.WithStableId(surfaceId);
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
                string.Empty,
                purpose,
                surfaceId,
                presentationGeometry);
        }

        private int ResolveEnvironmentSurfaceId(
            BodyState state,
            Vector3 point,
            Vector3 normal)
        {
            Vector3 safeNormal = normal.sqrMagnitude > 0.0001f
                ? normal.normalized
                : Vector3.forward;
            bool sameSurface = state.hasPresentationSurface
                && FiniteSpatialBoundsMath.IsSamePresentationSurface(
                    state.presentationSurfacePoint,
                    state.presentationSurfaceNormal,
                    point,
                    safeNormal);
            if (!sameSurface)
            {
                state.presentationSurfaceId = nextEnvironmentSurfaceId++;
            }

            state.hasPresentationSurface = true;
            state.presentationSurfacePoint = point;
            state.presentationSurfaceNormal = safeNormal;
            return state.presentationSurfaceId;
        }

        private StaticBoundaryRiskFrame BuildRiskFrame(
            double now,
            SpatialObstacleMeasurement headMeasurement,
            SpatialObstacleMeasurement locomotionMeasurement,
            SpatialObstacleMeasurement leftMeasurement,
            SpatialObstacleMeasurement rightMeasurement,
            bool motionWindowWarmedUp)
        {
            StaticRiskMeasurement headRisk = ToHeadRisk(headMeasurement);
            StaticRiskMeasurement lowObstacleRisk = ToHeadRisk(
                locomotionMeasurement);
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
            Vector3 lowObstacleDirection = locomotionMeasurement.Available
                ? locomotionMeasurement.HitPoint - head.position
                : Vector3.zero;
            float directionalUserState =
                CalculatePrimaryDirectionalHeadUserState(
                    headState.velocity,
                    headRisk,
                    headDirection,
                    lowObstacleRisk,
                    lowObstacleDirection);
            sequence++;
            return new StaticBoundaryRiskFrame(
                sequence,
                now,
                headRisk.Available
                    || leftRisk.Available
                    || rightRisk.Available
                    || lowObstacleRisk.Available
                    || headState.confirmedSafetyOverlap,
                headRisk,
                directionalUserState,
                motionWindowWarmedUp,
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
                headState.confirmedSafetyOverlap,
                lowObstacleRisk,
                lowObstacleDirection,
                lowObstacleDirection.sqrMagnitude > 0.0001f,
                headMeasurement.PresentationGeometry,
                leftMeasurement.PresentationGeometry,
                rightMeasurement.PresentationGeometry,
                locomotionMeasurement.PresentationGeometry);
        }

        private HazardPresentationGeometry BuildClusterGeometry(
            SpatialObstacleMeasurement[] source,
            int clusterCount,
            SpatialObstacleMeasurement selected,
            SpatialProbeOwner owner,
            SpatialProbePurpose purpose,
            float distanceMeters,
            double timestampSeconds,
            float confidence)
        {
            if (clusterCount <= 0)
            {
                return selected.PresentationGeometry;
            }

            Vector3 normal = Vector3.zero;
            Vector3 center = Vector3.zero;
            for (int i = 0; i < clusterCount; i++)
            {
                SpatialObstacleMeasurement sample =
                    source[sortedMeasurementIndices[i]];
                center += sample.HitPoint;
                Vector3 sampleNormal = sample.HitNormal;
                if (sampleNormal.sqrMagnitude > 0.0001f)
                {
                    if (normal.sqrMagnitude > 0.0001f
                        && Vector3.Dot(normal, sampleNormal) < 0f)
                    {
                        sampleNormal = -sampleNormal;
                    }

                    normal += sampleNormal.normalized;
                }
            }

            center /= clusterCount;
            normal = normal.sqrMagnitude > 0.0001f
                ? normal.normalized
                : selected.HitNormal;
            Transform tracked = TransformFor(owner);
            if (tracked != null
                && Vector3.Dot(normal, tracked.position - center) < 0f)
            {
                normal = -normal;
            }

            HazardPresentationGeometry.ResolvePlaneBasis(
                normal,
                Vector3.up,
                out Vector3 right,
                out Vector3 up);

            float minRight = 0f;
            float maxRight = 0f;
            float minUp = 0f;
            float maxUp = 0f;
            for (int i = 0; i < clusterCount; i++)
            {
                Vector3 offset = source[sortedMeasurementIndices[i]].HitPoint
                    - center;
                float x = Vector3.Dot(offset, right);
                float y = Vector3.Dot(offset, up);
                minRight = Mathf.Min(minRight, x);
                maxRight = Mathf.Max(maxRight, x);
                minUp = Mathf.Min(minUp, y);
                maxUp = Mathf.Max(maxUp, y);
            }

            float minimumWidth = AngularSpanMeters(
                distanceMeters,
                MinimumWallAngularWidthDegrees);
            float maximumWidth = AngularSpanMeters(
                distanceMeters,
                MaximumWallAngularWidthDegrees);
            float minimumHeight = purpose
                    == SpatialProbePurpose.LocomotionCorridor
                ? Mathf.Max(0.12f, minimumLowObstacleHeight)
                : AngularSpanMeters(
                    distanceMeters,
                    MinimumWallAngularHeightDegrees);
            float maximumHeight = AngularSpanMeters(
                distanceMeters,
                MaximumWallAngularHeightDegrees);
            float width = Mathf.Clamp(
                (maxRight - minRight) * HazardPresentationGeometry
                    .DefaultPaddingScale,
                minimumWidth,
                maximumWidth);
            float height = Mathf.Clamp(
                (maxUp - minUp) * HazardPresentationGeometry
                    .DefaultPaddingScale,
                minimumHeight,
                maximumHeight);
            bool floorFresh = hasFloorEstimate
                && timestampSeconds - lastFloorEstimateAt
                    <= FloorEstimateMaximumAgeSeconds;
            return HazardPresentationGeometry.CreatePlanePatch(
                selected.SurfaceId >= 0
                    ? selected.SurfaceId
                    : EnvironmentSurfaceId(owner, purpose),
                purpose == SpatialProbePurpose.LocomotionCorridor
                    ? HazardVisualKind.LowObstaclePatch
                    : HazardVisualKind.WallPlane,
                center,
                normal,
                Vector3.up,
                width,
                height,
                timestampSeconds,
                confidence,
                SpatialObstacleSource.EnvironmentDepth,
                owner,
                purpose,
                0f,
                floorFresh,
                floorFresh ? filteredFloorHeight : 0f);
        }

        private HazardPresentationGeometry CreateEnvironmentPatch(
            SpatialProbe probe,
            Vector3 hitPoint,
            Vector3 hitNormal,
            float distanceMeters,
            double timestampSeconds,
            float confidence)
        {
            bool low = probe.Purpose
                == SpatialProbePurpose.LocomotionCorridor;
            bool floorFresh = hasFloorEstimate
                && timestampSeconds - lastFloorEstimateAt
                    <= FloorEstimateMaximumAgeSeconds;
            float width = AngularSpanMeters(
                distanceMeters,
                MinimumWallAngularWidthDegrees);
            float height = low
                ? Mathf.Max(0.12f, minimumLowObstacleHeight)
                : AngularSpanMeters(
                    distanceMeters,
                    MinimumWallAngularHeightDegrees);
            return HazardPresentationGeometry.CreatePlanePatch(
                EnvironmentSurfaceId(probe.Owner, probe.Purpose),
                low
                    ? HazardVisualKind.LowObstaclePatch
                    : HazardVisualKind.WallPlane,
                hitPoint,
                hitNormal,
                Vector3.up,
                width,
                height,
                timestampSeconds,
                confidence,
                SpatialObstacleSource.EnvironmentDepth,
                probe.Owner,
                probe.Purpose,
                0f,
                floorFresh,
                floorFresh ? filteredFloorHeight : 0f);
        }

        private static int EnvironmentSurfaceId(
            SpatialProbeOwner owner,
            SpatialProbePurpose purpose)
        {
            return 1000 + (int)owner * 16 + (int)purpose;
        }

        private static float AngularSpanMeters(
            float distanceMeters,
            float degrees)
        {
            return 2f * Mathf.Max(0.05f, distanceMeters)
                * Mathf.Tan(0.5f * degrees * Mathf.Deg2Rad);
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
            bool canSample = trackedTransform != null
                && raycastManager != null
                && EnvironmentRaycastManager.IsSupported;
            if (!canSample)
            {
                LatestHeadSafetyEdgeRaycastCount = 0;
                ResetSafetyEdgeSweep(state);
                state.rawSafetyOverlap = false;
                UpdateSafetyOverlapConfirmation(
                    false,
                    ref state.confirmedSafetyOverlap,
                    ref state.overlapConfirmations,
                    ref state.overlapReleaseConfirmations);
                return;
            }

            LatestHeadSafetyEdgeRaycastCount = SampleBoxEdges(
                state,
                trackedTransform.position,
                Vector3.one * Mathf.Max(0.01f, safetyRadius),
                trackedTransform.rotation);
            if ((state.safetyEdgeSampledMask & AllSafetyEdgesSampledMask)
                != AllSafetyEdgesSampledMask)
            {
                return;
            }

            bool rawOverlap = CountSetBits(state.safetyEdgeHitMask) >= 2;
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

        private int SampleBoxEdges(
            BodyState state,
            Vector3 center,
            Vector3 halfExtents,
            Quaternion orientation)
        {
            int raycastCount = 0;
            int edgeIndex = Mathf.Clamp(
                state.nextSafetyEdgeIndex,
                0,
                SafetyBoxEdgeCount - 1);
            for (int sample = 0;
                sample < MaximumSafetyEdgeRaycastsPerMeasurement;
                sample++)
            {
                int axis = edgeIndex / 4;
                int corner = edgeIndex % 4;
                int crossA = (axis + 1) % 3;
                int crossB = (axis + 2) % 3;
                int signA = (corner & 1) == 0 ? -1 : 1;
                int signB = (corner & 2) == 0 ? -1 : 1;
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
                int edgeBit = 1 << edgeIndex;
                state.safetyEdgeSampledMask |= edgeBit;
                state.safetyEdgeHitMask &= ~edgeBit;
                if (distance > 0.01f)
                {
                    EnvironmentRaycastHit hit;
                    bool hasHit = raycastManager.Raycast(
                            new Ray(start, direction),
                            out hit,
                            distance)
                        && IsSafetyOverlapStatus(hit.status);
                    raycastCount++;
                    if (hasHit)
                    {
                        state.safetyEdgeHitMask |= edgeBit;
                    }
                }

                edgeIndex = AdvanceSafetyEdgeIndex(edgeIndex, 1);
            }

            state.nextSafetyEdgeIndex = edgeIndex;
            return raycastCount;
        }

        public static int AdvanceSafetyEdgeIndex(
            int currentIndex,
            int sampledEdgeCount)
        {
            int safeIndex = Mathf.Clamp(
                currentIndex,
                0,
                SafetyBoxEdgeCount - 1);
            int safeCount = Mathf.Max(0, sampledEdgeCount);
            return (safeIndex + safeCount) % SafetyBoxEdgeCount;
        }

        private static int CountSetBits(int value)
        {
            int count = 0;
            int remaining = value & AllSafetyEdgesSampledMask;
            while (remaining != 0)
            {
                remaining &= remaining - 1;
                count++;
            }

            return count;
        }

        private static void ResetSafetyEdgeSweep(BodyState state)
        {
            state.nextSafetyEdgeIndex = 0;
            state.safetyEdgeHitMask = 0;
            state.safetyEdgeSampledMask = 0;
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
            double now,
            SpatialProbePurpose purpose = SpatialProbePurpose.Standard)
        {
            if (trackedTransform == null || roomSceneSpatialProvider == null)
            {
                return UnavailableEnvironmentMeasurement(
                    owner,
                    state,
                    now,
                    purpose);
            }

            Vector3 origin = trackedTransform.position;
            Vector3 direction;
            if (purpose == SpatialProbePurpose.LocomotionCorridor)
            {
                Vector3 horizontalVelocity = Vector3.ProjectOnPlane(
                    state.velocity,
                    Vector3.up);
                Vector3 horizontalForward = horizontalVelocity.magnitude
                        >= MovementDirectionSpeed
                    ? horizontalVelocity.normalized
                    : HorizontalDirection(
                        trackedTransform.forward,
                        Vector3.forward);
                origin += horizontalForward * FloorProbeForwardOffsetMeters;
                direction = LocomotionCorridorDirection(
                    horizontalForward,
                    42f);
            }
            else
            {
                direction = state.velocity.magnitude
                        >= MovementDirectionSpeed
                    ? state.velocity.normalized
                    : trackedTransform.forward;
            }
            var probe = new SpatialProbe(
                owner,
                origin,
                direction,
                state.velocity,
                safetyRadius,
                purpose);
            return roomSceneSpatialProvider.TryMeasure(
                    probe,
                    out SpatialObstacleMeasurement measurement)
                ? measurement
                : UnavailableEnvironmentMeasurement(
                    owner,
                    state,
                    now,
                    purpose);
        }

        private static SpatialObstacleMeasurement
            UnavailableEnvironmentMeasurement(
                SpatialProbeOwner owner,
                BodyState state,
                double now,
                SpatialProbePurpose purpose = SpatialProbePurpose.Standard)
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
                state.selfRejectionReason,
                purpose);
        }

        private static SpatialObstacleMeasurement RejectedEnvironmentMeasurement(
            SpatialProbeOwner owner,
            double now,
            string reason,
            float rawDistanceMeters = -1f,
            SpatialProbePurpose purpose = SpatialProbePurpose.Standard)
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
                reason,
                purpose);
        }

        private void UpdateFloorEstimate(double now)
        {
            if (head == null
                || raycastManager == null
                || !EnvironmentRaycastManager.IsSupported)
            {
                return;
            }

            Vector3 horizontalForward = HorizontalDirection(
                head.forward,
                Vector3.forward);
            Vector3 origin = head.position
                + horizontalForward * FloorProbeForwardOffsetMeters;
            EnvironmentRaycastHit hit;
            bool hasHit = raycastManager.Raycast(
                    new Ray(origin, Vector3.down),
                    out hit,
                    FloorProbeMaximumDistanceMeters)
                && hit.status == EnvironmentRaycastHitStatus.Hit
                && Vector3.Dot(hit.normal.normalized, Vector3.up)
                    >= FloorNormalMinimumDot;
            if (!hasHit)
            {
                return;
            }

            float candidateHeight = hit.point.y;
            if (hasFloorEstimate
                && Mathf.Abs(candidateHeight - filteredFloorHeight)
                    > FloorEstimateMaximumJumpMeters)
            {
                return;
            }

            filteredFloorHeight = hasFloorEstimate
                ? Mathf.Lerp(filteredFloorHeight, candidateHeight, 0.25f)
                : candidateHeight;
            hasFloorEstimate = true;
            lastFloorEstimateAt = now;
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

        public static Vector3 HorizontalDirection(
            Vector3 direction,
            Vector3 fallback)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(
                direction,
                Vector3.up);
            if (horizontal.sqrMagnitude <= 0.0001f)
            {
                horizontal = Vector3.ProjectOnPlane(
                    fallback,
                    Vector3.up);
            }

            return horizontal.sqrMagnitude > 0.0001f
                ? horizontal.normalized
                : Vector3.forward;
        }

        public static Vector3 LocomotionCorridorDirection(
            Vector3 horizontalForward,
            float downwardDegrees)
        {
            Vector3 forward = HorizontalDirection(
                horizontalForward,
                Vector3.forward);
            float radians = Mathf.Clamp(downwardDegrees, 0f, 80f)
                * Mathf.Deg2Rad;
            return (forward * Mathf.Cos(radians)
                + Vector3.down * Mathf.Sin(radians)).normalized;
        }

        public static bool ShouldRejectLocomotionFloorHit(
            Vector3 hitPoint,
            Vector3 hitNormal,
            bool hasFreshFloorEstimate,
            float floorHeight,
            float minimumObstacleHeight)
        {
            Vector3 normal = hitNormal.sqrMagnitude > 0.0001f
                ? hitNormal.normalized
                : Vector3.zero;
            bool upwardFacing = Vector3.Dot(normal, Vector3.up)
                >= FloorNormalMinimumDot;
            if (!upwardFacing)
            {
                // Vertical faces are useful obstacle evidence even before a
                // stable floor plane has been measured.
                return false;
            }

            if (!hasFreshFloorEstimate)
            {
                // A horizontal hit without a floor reference is more likely
                // to be the floor itself than a safe low-obstacle signal.
                return true;
            }

            float requiredHeight = Mathf.Max(
                0.03f,
                minimumObstacleHeight);
            return hitPoint.y <= floorHeight + requiredHeight;
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

        public static float CalculatePrimaryDirectionalHeadUserState(
            Vector3 headVelocity,
            StaticRiskMeasurement headRisk,
            Vector3 headHazardDirection,
            StaticRiskMeasurement lowObstacleRisk,
            Vector3 lowObstacleHazardDirection)
        {
            bool lowDirectionAvailable = lowObstacleRisk.Available
                && lowObstacleHazardDirection.sqrMagnitude > 0.0001f;
            bool headDirectionAvailable = headRisk.Available
                && headHazardDirection.sqrMagnitude > 0.0001f;
            Vector3 selectedDirection = lowDirectionAvailable
                && (!headDirectionAvailable
                    || lowObstacleRisk.Risk > headRisk.Risk)
                ? lowObstacleHazardDirection
                : headHazardDirection;
            return CalculateDirectionalHeadUserState(
                headVelocity,
                selectedDirection);
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
            ResetSafetyEdgeSweep(state);
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
            state.hasPresentationSurface = false;
            state.presentationSurfaceId = 0;
            state.presentationSurfacePoint = Vector3.zero;
            state.presentationSurfaceNormal = Vector3.zero;
            ResetSafetyEdgeSweep(state);
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
