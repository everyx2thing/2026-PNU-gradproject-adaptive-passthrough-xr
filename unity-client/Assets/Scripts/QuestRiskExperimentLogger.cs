using System.Collections.Generic;
using System.Text;
using TeamVR.AdaptivePassthrough;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UI;

public class QuestRiskExperimentLogger : MonoBehaviour,
    IStaticBoundaryFrameProvider,
    ISpatialObstacleProvider
{
    private struct WallSurface
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 normal;
        public bool hasPlaneBounds;
        public Rect planeBounds;
        public bool hasVolumeBounds;
        public Bounds volumeBounds;
        public string label;
        public int index;
    }

    private struct StateSample
    {
        public double time;
        public Vector3 neckPosition;
    }

    private const float MinimumPositiveValue = 0.01f;
    private const string ScenePermission =
        "com.oculus.permission.USE_SCENE";

    [SerializeField] private Text labelText;
    [SerializeField] private Text riskLabelText;
    [SerializeField] private Transform labelRoot;

    [Header("User State Stabilization")]
    [SerializeField] private UserMotionStateFilterSettings userMotionSettings =
        new UserMotionStateFilterSettings();

    [Header("User State - Net Translation Window")]
    [SerializeField, Min(MinimumPositiveValue)]
    private float stateWindowDuration = 0.5f;
    [SerializeField, Min(MinimumPositiveValue)]
    private float handVelocitySmoothingTime = 0.15f;
    [SerializeField, Min(0f)] private float neckPivotForwardOffset = 0.10f;
    [SerializeField, Min(0f)] private float neckPivotUpOffset = 0.10f;
    [SerializeField, Min(MinimumPositiveValue)]
    private float thresholdHeadSpeedScale = 1.0f;

    [Header("Head Collision Risk Parameters")]
    [SerializeField, Min(MinimumPositiveValue)]
    private float safeDistanceMeters = 2.5f;
    [SerializeField, Min(MinimumPositiveValue)]
    private float safeTimeSeconds = 4.5f;
    [SerializeField, Min(MinimumPositiveValue)]
    private float maxApproachAccel = 5.0f;

    [Header("Head Collision Risk Weights")]
    [SerializeField, Range(0f, 1f)] private float weightDistance = 0.30f;
    [SerializeField, Range(0f, 1f)] private float weightTTC = 0.30f;
    [SerializeField, Range(0f, 1f)]
    private float weightApproachAcceleration = 0.0f;
    [SerializeField, Range(0f, 1f)] private float weightBlind = 0.15f;

    [Header("Hand Collision Risk")]
    [SerializeField] private bool enableHandRisk = true;
    [SerializeField, Min(0.1f)] private float personalReachLength = 0.70f;
    [SerializeField] private bool autoCalibrateReach = true;
    [SerializeField, Min(0.1f)] private float maxPlausibleReach = 1.00f;
    [SerializeField, Min(MinimumPositiveValue)]
    private float reachTransitionMargin = 0.15f;
    [SerializeField, Min(MinimumPositiveValue)]
    private float safeHandDistance = 0.50f;
    [SerializeField, Min(MinimumPositiveValue)]
    private float safeHandTime = 1.00f;
    [SerializeField, Range(0f, 1f)]
    private float weightHandDistance = 0.40f;
    [SerializeField, Range(0f, 1f)] private float weightHandTTC = 0.60f;
    [SerializeField, Min(0f)] private float handApproachSpeedMin = 0.05f;

    [Header("UI Refresh")]
    [SerializeField, Min(MinimumPositiveValue)]
    private float uiRefreshInterval = 0.15f;

    private readonly List<WallSurface> wallSurfaces = new();
    private readonly Queue<StateSample> stateSamples = new();

    private string displayText = "Initializing (Risk Experiment)...";
    private string riskDisplayText =
        "[User Motion]\nWaiting for motion data...\n\n"
        + "[Collision Risk]\nWaiting for scene data...";
    private bool sceneLoaded;
    private float nextUiRefreshTime;

    public UserMotionState CurrentUserState { get; private set; } =
        UserMotionState.Static;
    public UserMotionSnapshot CurrentMotionSnapshot { get; private set; }
    public StaticRiskMeasurement CurrentStaticMeasurement { get; private set; } =
        StaticRiskMeasurement.Unavailable;
    public StaticBoundaryRiskFrame CurrentStaticBoundaryFrame
    {
        get;
        private set;
    } = StaticBoundaryRiskFrame.Unavailable;
    public Vector3 CurrentWallDirectionWorld { get; private set; }
    public bool CurrentWallDirectionAvailable { get; private set; }
    public int CurrentClosestWallIndex { get; private set; } = -1;
    public bool SceneDataAvailable
    {
        get { return sceneLoaded && wallSurfaces.Count > 0; }
    }
    public float CurrentStaticHandRisk
    {
        get { return CurrentStaticBoundaryFrame.MaximumHandRisk; }
    }
    public Vector3 StaticHandRiskDirection
    {
        get
        {
            return CurrentStaticBoundaryFrame.LeftHand.Risk
                >= CurrentStaticBoundaryFrame.RightHand.Risk
                    ? CurrentStaticBoundaryFrame.LeftHand
                        .HazardDirectionWorld
                    : CurrentStaticBoundaryFrame.RightHand
                        .HazardDirectionWorld;
        }
    }

    private OVRCameraRig cameraRig;
    private Transform hmdTransform;
    private Transform leftHandTransform;
    private Transform rightHandTransform;

    private Vector3 previousLeftPosition;
    private Vector3 previousRightPosition;
    private bool firstHandFrame = true;
    private UserMotionStateFilter motionStateFilter;
    private Vector3 smoothedLeftHandVelocity;
    private Vector3 smoothedRightHandVelocity;
    private Vector3 headNetVelocity;
    private float headTranslationSpeed;
    private bool motionWindowWarmedUp;
    private float observedMaxReach;
    private float leftMinimumWallDistance = float.PositiveInfinity;
    private float rightMinimumWallDistance = float.PositiveInfinity;
    private long staticSequence;
    private IStaticBoundaryFrameProvider fusedStaticProvider;

    private void Start()
    {
        motionStateFilter = new UserMotionStateFilter(userMotionSettings);
        ResolveTrackedTransforms();
        ResolveFusedStaticProvider();

        if (!Permission.HasUserAuthorizedPermission(ScenePermission))
        {
            displayText = "Requesting SCENE permission...";
            riskDisplayText = displayText;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => LoadScene();
            callbacks.PermissionDenied += _ =>
                riskDisplayText = displayText = "SCENE permission denied.";
            Permission.RequestUserPermission(ScenePermission, callbacks);
        }
        else
        {
            LoadScene();
        }
    }

    private void OnDisable()
    {
        CurrentStaticMeasurement = StaticRiskMeasurement.Unavailable;
        CurrentStaticBoundaryFrame = StaticBoundaryRiskFrame.Unavailable;
        CurrentWallDirectionWorld = Vector3.zero;
        CurrentWallDirectionAvailable = false;
        CurrentClosestWallIndex = -1;
    }

    private void ResolveTrackedTransforms()
    {
        cameraRig = FindObjectOfType<OVRCameraRig>();
        if (cameraRig != null)
        {
            hmdTransform = cameraRig.centerEyeAnchor != null
                ? cameraRig.centerEyeAnchor
                : Camera.main != null ? Camera.main.transform : null;
            leftHandTransform = cameraRig.leftHandAnchor;
            rightHandTransform = cameraRig.rightHandAnchor;
        }
        else
        {
            hmdTransform = Camera.main != null
                ? Camera.main.transform
                : null;
        }
    }

    private void ResolveFusedStaticProvider()
    {
        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
            FindObjectsSortMode.None);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null
                || ReferenceEquals(behaviour, this)
                || !string.Equals(
                    behaviour.GetType().Name,
                    "QuestSpatialObstacleProvider",
                    System.StringComparison.Ordinal))
            {
                continue;
            }

            fusedStaticProvider = behaviour as IStaticBoundaryFrameProvider;
            if (fusedStaticProvider != null)
            {
                return;
            }
        }
    }

    private async void LoadScene()
    {
        riskDisplayText = displayText =
            "Loading scene data (Risk Experiment)...";
        sceneLoaded = false;
        wallSurfaces.Clear();

        var roomAnchors = new List<OVRAnchor>();
        var result = await OVRAnchor.FetchAnchorsAsync(
            roomAnchors,
            new OVRAnchor.FetchOptions
            {
                SingleComponentType = typeof(OVRRoomLayout)
            });

        if (!result.Success || roomAnchors.Count == 0)
        {
            riskDisplayText = displayText =
                "No rooms found.\nRun Space Setup on your headset first.";
            return;
        }

        if (cameraRig == null)
        {
            riskDisplayText = displayText =
                "OVRCameraRig not found in scene.";
            return;
        }

        Transform trackingSpace = cameraRig.trackingSpace;
        var childAnchors = new List<OVRAnchor>();
        foreach (OVRAnchor room in roomAnchors)
        {
            if (!room.TryGetComponent(out OVRAnchorContainer container))
            {
                continue;
            }

            // Meta clears the destination list for each fetch. Aggregate each
            // room through a temporary list so earlier rooms are not lost.
            var roomChildren = new List<OVRAnchor>();
            await container.FetchChildrenAsync(roomChildren);
            childAnchors.AddRange(roomChildren);
        }

        Debug.Log(
            $"[RiskExperimentLogger] Total child anchors: {childAnchors.Count}");
        foreach (OVRAnchor anchor in childAnchors)
        {
            if (!anchor.TryGetComponent(out OVRSemanticLabels labels))
            {
                continue;
            }

            string label = labels.Labels;
            bool floorOrCeiling =
                label.Contains(OVRSceneManager.Classification.Floor)
                || label.Contains(OVRSceneManager.Classification.Ceiling);
            if (floorOrCeiling
                || !anchor.TryGetComponent(out OVRLocatable locatable))
            {
                continue;
            }

            bool hasPlaneBounds =
                anchor.TryGetComponent(out OVRBounded2D bounded2D)
                && bounded2D.IsEnabled;
            bool hasVolumeBounds =
                anchor.TryGetComponent(out OVRBounded3D bounded3D)
                && bounded3D.IsEnabled;
            if (!hasPlaneBounds && !hasVolumeBounds)
            {
                continue;
            }

            await locatable.SetEnabledAsync(true);
            if (!locatable.TryGetSceneAnchorPose(out var pose))
            {
                continue;
            }

            Vector3 worldPosition =
                pose.ComputeWorldPosition(trackingSpace) ?? Vector3.zero;
            Quaternion worldRotation =
                pose.ComputeWorldRotation(trackingSpace)
                ?? Quaternion.identity;
            wallSurfaces.Add(
                new WallSurface
                {
                    position = worldPosition,
                    rotation = worldRotation,
                    normal = worldRotation * Vector3.forward,
                    hasPlaneBounds = hasPlaneBounds,
                    planeBounds = hasPlaneBounds
                        ? bounded2D.BoundingBox
                        : default,
                    hasVolumeBounds = hasVolumeBounds,
                    volumeBounds = hasVolumeBounds
                        ? bounded3D.BoundingBox
                        : default,
                    label = label,
                    index = wallSurfaces.Count
                });
            Debug.Log(
                $"[RiskExperimentLogger] Added finite scene obstacle: "
                + $"{label} at {worldPosition}");
        }

        sceneLoaded = wallSurfaces.Count > 0;
        riskDisplayText = displayText =
            sceneLoaded
                ? $"Loaded {wallSurfaces.Count} wall surfaces."
                : "No wall surfaces found in the loaded room.";
    }

    private void Update()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        float dt = Time.unscaledDeltaTime;
        bool refreshUi = Time.unscaledTime >= nextUiRefreshTime;
        if (refreshUi)
        {
            nextUiRefreshTime =
                Time.unscaledTime
                + Mathf.Max(uiRefreshInterval, MinimumPositiveValue);
        }

        if (hmdTransform == null)
        {
            ResolveTrackedTransforms();
        }

        if (hmdTransform == null)
        {
            PublishUnavailable(now, 0f, false);
            if (refreshUi)
            {
                displayText = "HMD tracking unavailable.";
                riskDisplayText = displayText;
                ApplyUiText();
            }
            return;
        }

        // QuestSpatialObstacleProvider already executes the Room Scene query at
        // the configured spatial rate. When present, mirror its result instead
        // of repeating three full anchor scans every render frame.
        if (fusedStaticProvider != null)
        {
            StaticBoundaryRiskFrame fused =
                fusedStaticProvider.CurrentStaticBoundaryFrame;
            CurrentStaticBoundaryFrame = fused
                ?? StaticBoundaryRiskFrame.Unavailable;
            CurrentStaticMeasurement = CurrentStaticBoundaryFrame.Head;
            CurrentWallDirectionWorld =
                CurrentStaticBoundaryFrame.HeadHazardDirectionWorld;
            CurrentWallDirectionAvailable =
                CurrentStaticBoundaryFrame.HeadHazardDirectionAvailable;
            CurrentClosestWallIndex = CurrentStaticBoundaryFrame.HeadWallIndex;
            return;
        }

        Vector3 hmdPosition = hmdTransform.position;
        Vector3 neckPosition = GetNeckPoint();
        if (motionStateFilter == null)
        {
            motionStateFilter =
                new UserMotionStateFilter(userMotionSettings);
        }

        CurrentMotionSnapshot = motionStateFilter.Update(
            now,
            neckPosition,
            hmdTransform.rotation);
        CurrentUserState = CurrentMotionSnapshot.StableState;

        SampleHandVelocities(
            dt,
            out Vector3 leftHandVelocity,
            out Vector3 rightHandVelocity);
        UpdateMotionContext(
            now,
            dt,
            neckPosition,
            leftHandVelocity,
            rightHandVelocity);
        float userState01 = motionWindowWarmedUp
            ? StaticBoundaryRiskMath.ContinuousUserState(
                headTranslationSpeed,
                thresholdHeadSpeedScale)
            : 0f;

        if (!SceneDataAvailable)
        {
            PublishUnavailable(now, userState01, motionWindowWarmedUp);
            if (refreshUi)
            {
                BuildUnavailableUi(userState01);
                ApplyUiText();
            }
            return;
        }

        if (!TryGetClosestWall(
                hmdPosition,
                out float headDistance,
                out Vector3 headDirection,
                out int headWallIndex))
        {
            PublishUnavailable(now, userState01, motionWindowWarmedUp);
            if (refreshUi)
            {
                BuildUnavailableUi(userState01);
                ApplyUiText();
            }
            return;
        }

        Vector3 filteredAcceleration =
            CurrentMotionSnapshot.FilteredAcceleration;
        float headTowardSpeed = Safe(
            Mathf.Max(0f, Vector3.Dot(headNetVelocity, headDirection)));
        float headTowardAcceleration = Safe(
            Mathf.Max(
                0f,
                Vector3.Dot(filteredAcceleration, headDirection)));
        bool headApproaching = headTowardSpeed > 0.01f;
        float ttc = headApproaching
            ? headDistance / Mathf.Max(
                headTowardSpeed,
                MinimumPositiveValue)
            : float.PositiveInfinity;
        float distanceRisk = StaticBoundaryRiskMath.DistanceRisk(
            headDistance,
            safeDistanceMeters);
        float ttcRisk = StaticBoundaryRiskMath.TimeToCollisionRisk(
            headDistance,
            headTowardSpeed,
            safeTimeSeconds,
            0.01f);
        float accelerationRisk =
            StaticBoundaryRiskMath.AccelerationRisk(
                headTowardAcceleration,
                maxApproachAccel);
        float angleToWall = Vector3.Angle(
            hmdTransform.forward,
            headDirection);
        float blindRisk = StaticBoundaryRiskMath.BlindSpotRisk(
            angleToWall);
        float headRisk = StaticBoundaryRiskMath.WeightedHeadRisk(
            distanceRisk,
            ttcRisk,
            accelerationRisk,
            blindRisk,
            weightDistance,
            weightTTC,
            weightApproachAcceleration,
            weightBlind);
        CurrentStaticMeasurement = new StaticRiskMeasurement(
            true,
            headDistance,
            float.IsInfinity(ttc) ? 0f : ttc,
            !float.IsInfinity(ttc),
            headTowardSpeed,
            headTowardAcceleration,
            distanceRisk,
            ttcRisk,
            accelerationRisk,
            blindRisk,
            headRisk);

        StaticHandRiskMeasurement leftHand = MeasureHandRisk(
            leftHandTransform,
            smoothedLeftHandVelocity,
            dt,
            ref leftMinimumWallDistance);
        StaticHandRiskMeasurement rightHand = MeasureHandRisk(
            rightHandTransform,
            smoothedRightHandVelocity,
            dt,
            ref rightMinimumWallDistance);

        CurrentWallDirectionWorld = headDirection;
        CurrentWallDirectionAvailable =
            headDirection.sqrMagnitude > 0.0001f;
        CurrentClosestWallIndex = headWallIndex;
        staticSequence++;
        CurrentStaticBoundaryFrame = new StaticBoundaryRiskFrame(
            staticSequence,
            now,
            true,
            CurrentStaticMeasurement,
            userState01,
            motionWindowWarmedUp,
            leftHand,
            rightHand,
            headDirection,
            CurrentWallDirectionAvailable,
            headWallIndex,
            Mathf.Max(personalReachLength, observedMaxReach));

        if (refreshUi)
        {
            BuildAvailableUi(
                hmdPosition,
                angleToWall,
                userState01,
                leftHand,
                rightHand);
            ApplyUiText();
        }
    }

    private void SampleHandVelocities(
        float dt,
        out Vector3 leftVelocity,
        out Vector3 rightVelocity)
    {
        leftVelocity = Vector3.zero;
        rightVelocity = Vector3.zero;

        if (firstHandFrame)
        {
            previousLeftPosition = leftHandTransform != null
                ? leftHandTransform.position
                : Vector3.zero;
            previousRightPosition = rightHandTransform != null
                ? rightHandTransform.position
                : Vector3.zero;
            firstHandFrame = false;
            return;
        }

        if (dt <= 0f)
        {
            return;
        }

        if (leftHandTransform != null)
        {
            Vector3 current = leftHandTransform.position;
            leftVelocity = (current - previousLeftPosition) / dt;
            previousLeftPosition = current;
        }

        if (rightHandTransform != null)
        {
            Vector3 current = rightHandTransform.position;
            rightVelocity = (current - previousRightPosition) / dt;
            previousRightPosition = current;
        }
    }

    private void UpdateMotionContext(
        double timestampSeconds,
        float dt,
        Vector3 neckPosition,
        Vector3 leftHandVelocity,
        Vector3 rightHandVelocity)
    {
        float blend = dt > 0f
            ? 1f - Mathf.Exp(
                -dt
                / Mathf.Max(
                    handVelocitySmoothingTime,
                    MinimumPositiveValue))
            : 0f;
        smoothedLeftHandVelocity = Vector3.Lerp(
            smoothedLeftHandVelocity,
            leftHandVelocity,
            blend);
        smoothedRightHandVelocity = Vector3.Lerp(
            smoothedRightHandVelocity,
            rightHandVelocity,
            blend);

        stateSamples.Enqueue(
            new StateSample
            {
                time = timestampSeconds,
                neckPosition = neckPosition
            });
        double window = Mathf.Max(
            stateWindowDuration,
            MinimumPositiveValue);
        while (stateSamples.Count > 0
            && timestampSeconds - stateSamples.Peek().time > window)
        {
            stateSamples.Dequeue();
        }

        motionWindowWarmedUp = stateSamples.Count >= 2;
        if (!motionWindowWarmedUp)
        {
            headNetVelocity = Vector3.zero;
            headTranslationSpeed = 0f;
            return;
        }

        StateSample oldest = stateSamples.Peek();
        float elapsed = (float)Mathf.Max(
            (float)(timestampSeconds - oldest.time),
            MinimumPositiveValue);
        headNetVelocity = (neckPosition - oldest.neckPosition) / elapsed;
        if (!IsFinite(headNetVelocity))
        {
            headNetVelocity = Vector3.zero;
        }
        headTranslationSpeed = StaticBoundaryRiskMath.NetTranslationSpeed(
            oldest.neckPosition,
            neckPosition,
            elapsed);
    }

    private StaticHandRiskMeasurement MeasureHandRisk(
        Transform hand,
        Vector3 smoothedVelocity,
        float dt,
        ref float minimumObservedDistance)
    {
        if (hand == null
            || !TryGetClosestWall(
                hand.position,
                out float distance,
                out Vector3 direction,
                out int wallIndex))
        {
            return StaticHandRiskMeasurement.Unavailable;
        }

        float towardSpeed = Safe(
            Mathf.Max(0f, Vector3.Dot(smoothedVelocity, direction)));
        minimumObservedDistance = float.IsInfinity(minimumObservedDistance)
            ? distance
            : Mathf.Min(
                distance,
                minimumObservedDistance + Mathf.Max(0f, dt) * 0.2f);
        float extension = Safe(
            Vector3.Distance(hand.position, hmdTransform.position));
        if (autoCalibrateReach
            && extension <= Mathf.Max(0.1f, maxPlausibleReach)
            && extension > observedMaxReach)
        {
            observedMaxReach = extension;
        }

        float armReach = Mathf.Clamp(
            Mathf.Max(
                personalReachLength,
                autoCalibrateReach ? observedMaxReach : 0f),
            0.1f,
            Mathf.Max(0.1f, maxPlausibleReach));
        float reachGate = enableHandRisk
            ? StaticBoundaryRiskMath.ReachGate(
                distance,
                extension,
                armReach,
                reachTransitionMargin)
            : 0f;
        float distanceRisk = enableHandRisk
            ? StaticBoundaryRiskMath.DistanceRisk(
                distance,
                safeHandDistance)
            : 0f;
        float ttcRisk = enableHandRisk
            ? StaticBoundaryRiskMath.TimeToCollisionRisk(
                distance,
                towardSpeed,
                safeHandTime,
                handApproachSpeedMin)
            : 0f;
        float risk = enableHandRisk
            ? StaticBoundaryRiskMath.WeightedHandRisk(
                reachGate,
                distanceRisk,
                ttcRisk,
                weightHandDistance,
                weightHandTTC)
            : 0f;

        return new StaticHandRiskMeasurement(
            true,
            wallIndex,
            distance,
            towardSpeed,
            minimumObservedDistance,
            extension,
            reachGate,
            distanceRisk,
            ttcRisk,
            risk,
            direction);
    }

    private bool TryGetClosestWall(
        Vector3 point,
        out float distance,
        out Vector3 directionToWall,
        out int wallIndex)
    {
        distance = float.PositiveInfinity;
        directionToWall = Vector3.zero;
        wallIndex = -1;

        foreach (WallSurface surface in wallSurfaces)
        {
            Vector3 closestPoint = Vector3.zero;
            float candidateDistance = float.PositiveInfinity;
            if (surface.hasPlaneBounds)
            {
                Vector3 planePoint =
                    FiniteSpatialBoundsMath.ClosestPointOnPlane(
                        point,
                        surface.position,
                        surface.rotation,
                        surface.planeBounds);
                candidateDistance = Vector3.Distance(point, planePoint);
                closestPoint = planePoint;
            }

            bool insideVolume = false;
            if (surface.hasVolumeBounds)
            {
                Vector3 volumePoint =
                    FiniteSpatialBoundsMath.ClosestPointOnVolume(
                        point,
                        surface.position,
                        surface.rotation,
                        surface.volumeBounds,
                        out insideVolume);
                float volumeDistance = insideVolume
                    ? 0f
                    : Vector3.Distance(point, volumePoint);
                if (volumeDistance < candidateDistance)
                {
                    candidateDistance = volumeDistance;
                    closestPoint = volumePoint;
                }
            }

            if (candidateDistance >= distance)
            {
                continue;
            }

            distance = candidateDistance;
            wallIndex = surface.index;
            Vector3 delta = closestPoint - point;
            directionToWall = delta.sqrMagnitude > 0.000001f
                ? delta.normalized
                : -surface.normal;
        }

        return wallIndex >= 0;
    }

    public bool TryMeasure(
        SpatialProbe probe,
        out SpatialObstacleMeasurement measurement)
    {
        double now = Time.realtimeSinceStartupAsDouble;
        if (!SceneDataAvailable
            || !TryGetClosestWall(
                probe.Origin,
                out float distance,
                out Vector3 direction,
                out int wallIndex))
        {
            measurement = SpatialObstacleMeasurement.Unavailable(now);
            return false;
        }

        float closingSpeed = Mathf.Max(
            0f,
            Vector3.Dot(probe.Velocity, direction));
        measurement = new SpatialObstacleMeasurement(
            SpatialObstacleSource.RoomScene,
            now,
            true,
            distance,
            probe.Origin + direction * distance,
            -direction,
            closingSpeed,
            0.70f,
            1,
            0f,
            0f,
            false,
            false,
            0,
            probe.Owner,
            SpatialObstacleSource.RoomScene,
            -1f,
            distance,
            false,
            wallIndex >= 0 ? "" : "room-scene-unavailable");
        return true;
    }

    private Vector3 GetNeckPoint()
    {
        return hmdTransform.position
            - hmdTransform.forward * Mathf.Max(0f, neckPivotForwardOffset)
            - hmdTransform.up * Mathf.Max(0f, neckPivotUpOffset);
    }

    private void PublishUnavailable(
        double timestampSeconds,
        float userState01,
        bool warmedUp)
    {
        CurrentStaticMeasurement = StaticRiskMeasurement.Unavailable;
        CurrentWallDirectionWorld = Vector3.zero;
        CurrentWallDirectionAvailable = false;
        CurrentClosestWallIndex = -1;
        staticSequence++;
        CurrentStaticBoundaryFrame = new StaticBoundaryRiskFrame(
            staticSequence,
            timestampSeconds,
            false,
            StaticRiskMeasurement.Unavailable,
            userState01,
            warmedUp,
            StaticHandRiskMeasurement.Unavailable,
            StaticHandRiskMeasurement.Unavailable,
            Vector3.zero,
            false,
            -1,
            Mathf.Max(personalReachLength, observedMaxReach));
    }

    private void BuildUnavailableUi(float userState01)
    {
        var left = new StringBuilder();
        left.AppendLine("[Scene Distance]");
        left.AppendLine("Unavailable");
        left.AppendLine("Run Space Setup for static wall risk.");
        displayText = left.ToString();

        var right = new StringBuilder();
        right.AppendLine("[User Motion]");
        right.AppendLine($"Stable State: {CurrentUserState}");
        right.AppendLine($"UserState: {userState01:F2}");
        right.AppendLine(
            $"Net Translation: {headTranslationSpeed:F3} m/s");
        right.AppendLine($"Warmed Up: {motionWindowWarmedUp}");
        right.AppendLine();
        right.AppendLine("[Static Risk]");
        right.AppendLine("Unavailable");
        riskDisplayText = right.ToString();
    }

    private void BuildAvailableUi(
        Vector3 hmdPosition,
        float angleToWall,
        float userState01,
        StaticHandRiskMeasurement leftHand,
        StaticHandRiskMeasurement rightHand)
    {
        var left = new StringBuilder();
        left.AppendLine("[Scene Distance]");
        left.AppendLine(
            $"Head: ({hmdPosition.x:F2}, {hmdPosition.y:F2}, "
            + $"{hmdPosition.z:F2})");
        left.AppendLine(
            $"Closest Scene Obstacle: #{CurrentClosestWallIndex}");
        left.AppendLine(
            $"Distance: {CurrentStaticMeasurement.ClosestDistanceMeters:F3}m");
        left.AppendLine();
        left.AppendLine("[Head Approach]");
        left.AppendLine(
            $"Net Speed: "
            + $"{CurrentStaticMeasurement.TowardBoundarySpeed:F3} m/s");
        left.AppendLine(
            $"Acceleration: "
            + $"{CurrentStaticMeasurement.TowardBoundaryAcceleration:F3} m/s²");
        left.AppendLine(
            CurrentStaticMeasurement.HasTimeToCollision
                ? $"TTC: {CurrentStaticMeasurement.TimeToCollisionSeconds:F2} s"
                : "TTC: Infinity");
        left.AppendLine();
        left.AppendLine("[Hand-Wall]");
        left.AppendLine(FormatHand("L", leftHand));
        left.AppendLine(FormatHand("R", rightHand));
        left.AppendLine(
            $"Arm Reach: "
            + $"{Mathf.Max(personalReachLength, observedMaxReach):F3} m");
        displayText = left.ToString();

        var right = new StringBuilder();
        right.AppendLine("[User Motion]");
        right.AppendLine($"Stable State: {CurrentUserState}");
        right.AppendLine($"UserState: {userState01:F2}");
        right.AppendLine(
            $"Net Translation: {headTranslationSpeed:F3} m/s");
        right.AppendLine($"Warmed Up: {motionWindowWarmedUp}");
        right.AppendLine();
        right.AppendLine("[Head Risk]");
        right.AppendLine($"Rd: {CurrentStaticMeasurement.DistanceRisk:F2}");
        right.AppendLine($"RTTC: {CurrentStaticMeasurement.TtcRisk:F2}");
        right.AppendLine(
            $"Ra: {CurrentStaticMeasurement.AccelerationRisk:F2}");
        right.AppendLine($"Theta: {angleToWall:F1} deg");
        right.AppendLine(
            $"Rblind: {CurrentStaticMeasurement.BlindSpotRisk:F2}");
        right.AppendLine($"R_static_head: {CurrentStaticMeasurement.Risk:F2}");
        right.AppendLine();
        right.AppendLine("[Hand Risk]");
        right.AppendLine(
            $"L gate {leftHand.ReachGate:F2}  risk {leftHand.Risk:F2}");
        right.AppendLine(
            $"R gate {rightHand.ReachGate:F2}  risk {rightHand.Risk:F2}");
        right.AppendLine(
            $"R_static_hand: "
            + $"{CurrentStaticBoundaryFrame.MaximumHandRisk:F2}");
        riskDisplayText = right.ToString();
    }

    private static string FormatHand(
        string label,
        StaticHandRiskMeasurement hand)
    {
        if (!hand.Available)
        {
            return $"{label}: tracking unavailable";
        }

        return $"{label}: #{hand.WallIndex} {hand.DistanceMeters:F3}m"
            + $" | toward {hand.TowardBoundarySpeed:F2} m/s"
            + $" | min {hand.MinimumObservedDistanceMeters:F3}m";
    }

    private void ApplyUiText()
    {
        if (labelText != null)
        {
            labelText.text = displayText;
        }

        if (riskLabelText != null)
        {
            riskLabelText.text = riskDisplayText;
        }
    }

    private static float Safe(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value)
            ? 0f
            : value;
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x)
            && IsFinite(value.y)
            && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
