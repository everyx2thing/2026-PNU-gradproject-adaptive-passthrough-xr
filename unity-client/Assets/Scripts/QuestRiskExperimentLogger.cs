using System.Collections.Generic;
using System.Text;
using TeamVR.AdaptivePassthrough;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UI;

public class QuestRiskExperimentLogger : MonoBehaviour
{
    private struct WallSurface
    {
        public Vector3 position;
        public Vector3 normal;
        public string label;
        public int index;
    }

    [SerializeField] private Text labelText;
    [SerializeField] private Text riskLabelText;
    [SerializeField] private Transform labelRoot;

    [Header("User State Stabilization")]
    [SerializeField] private UserMotionStateFilterSettings userMotionSettings =
        new UserMotionStateFilterSettings();

    [Header("Risk Parameters")]
    [SerializeField] private float safeDistance = 0.8f;
    [SerializeField] private float safeTime = 2.0f;
    [SerializeField] private float maxApproachAccel = 5.0f;

    [Header("Collision Risk Weights")]
    [Range(0f, 1f)]
    [SerializeField] private float weightDistance = 0.30f;
    [Range(0f, 1f)]
    [SerializeField] private float weightTTC = 0.30f;
    [Range(0f, 1f)]
    [SerializeField] private float weightApproachAccel = 0.25f;
    [Range(0f, 1f)]
    [SerializeField] private float weightBlind = 0.15f;

    [Header("Dynamic Object Risk")]
    [SerializeField] private DynamicRiskController dynamicRiskController;

    private const string ScenePermission = "com.oculus.permission.USE_SCENE";
    private readonly List<WallSurface> _wallSurfaces = new();
    private string _displayText = "Initializing (Risk Experiment)...";
    private string _riskDisplayText = "[User State]\nWaiting for scene data...\n\n[Risk Score]\nWaiting for scene data...";
    private bool _sceneLoaded = false;
    private QuestRiskSnapshotController _snapshotController;

    [System.Obsolete("Use QuestRiskSnapshotController.Latest.Static.Risk.")]
    public float LastStaticRisk
    {
        get
        {
            return _snapshotController != null
                && _snapshotController.Latest != null
                ? _snapshotController.Latest.Static.Risk
                : 0f;
        }
    }

    [System.Obsolete("Use QuestRiskSnapshotController.Latest.UserState.Risk.")]
    public float LastStateRisk
    {
        get
        {
            return _snapshotController != null
                && _snapshotController.Latest != null
                ? _snapshotController.Latest.UserState.Risk
                : 0f;
        }
    }

    [System.Obsolete("Use QuestRiskSnapshotController.Latest.Dynamic.MaximumRisk.")]
    public float LastDynamicRisk
    {
        get
        {
            return _snapshotController != null
                && _snapshotController.Latest != null
                ? _snapshotController.Latest.Dynamic.MaximumRisk
                : 0f;
        }
    }

    [System.Obsolete("Use QuestRiskSnapshotController.Latest.Overall.TotalRisk.")]
    public float LastTotalRisk
    {
        get
        {
            return _snapshotController != null
                && _snapshotController.Latest != null
                ? _snapshotController.Latest.Overall.TotalRisk
                : 0f;
        }
    }

    [System.Obsolete("Use QuestRiskSnapshotController.Latest.Passthrough.Enabled.")]
    public bool LastPassthroughDecision
    {
        get
        {
            return _snapshotController != null
                && _snapshotController.Latest != null
                && _snapshotController.Latest.Passthrough.Enabled;
        }
    }

    public UserMotionState CurrentUserState { get; private set; } =
        UserMotionState.Static;
    public UserMotionSnapshot CurrentMotionSnapshot { get; private set; }
    public StaticRiskMeasurement CurrentStaticMeasurement { get; private set; } =
        StaticRiskMeasurement.Unavailable;
    public bool SceneDataAvailable
    {
        get { return _sceneLoaded && _wallSurfaces.Count > 0; }
    }

    // OVRCameraRig and tracked transforms
    private OVRCameraRig _cameraRig;
    private Transform _hmdTransform;
    private Transform _leftHandTransform;
    private Transform _rightHandTransform;

    // Motion state
    private Vector3 _prevLeftPos;
    private Vector3 _prevRightPos;
    private bool _firstHandFrame = true;
    private UserMotionStateFilter _motionStateFilter;

    void Start()
    {
        _motionStateFilter = new UserMotionStateFilter(userMotionSettings);

        if (dynamicRiskController == null)
        {
            dynamicRiskController = FindObjectOfType<DynamicRiskController>();
        }

        _snapshotController =
            FindObjectOfType<QuestRiskSnapshotController>();

        _cameraRig = FindObjectOfType<OVRCameraRig>();
        if (_cameraRig != null)
        {
            _hmdTransform = _cameraRig.centerEyeAnchor != null
                ? _cameraRig.centerEyeAnchor
                : Camera.main != null ? Camera.main.transform : null;
            _leftHandTransform = _cameraRig.leftHandAnchor;
            _rightHandTransform = _cameraRig.rightHandAnchor;
        }
        else
        {
            _hmdTransform = Camera.main != null ? Camera.main.transform : null;
        }

        if (!Permission.HasUserAuthorizedPermission(ScenePermission))
        {
            _displayText = "Requesting SCENE permission...";
            _riskDisplayText = _displayText;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => LoadScene();
            callbacks.PermissionDenied += _ => _riskDisplayText = _displayText = "SCENE permission denied.";
            Permission.RequestUserPermission(ScenePermission, callbacks);
        }
        else
        {
            LoadScene();
        }
    }

    async void LoadScene()
    {
        _riskDisplayText = _displayText = "Loading scene data (Risk Experiment)...";

        var roomAnchors = new List<OVRAnchor>();
        var result = await OVRAnchor.FetchAnchorsAsync(roomAnchors, new OVRAnchor.FetchOptions
        {
            SingleComponentType = typeof(OVRRoomLayout)
        });

        if (!result.Success || roomAnchors.Count == 0)
        {
            _riskDisplayText = _displayText = "No rooms found.\nRun Space Setup on your headset first.";
            return;
        }

        if (_cameraRig == null)
        {
            _riskDisplayText = _displayText = "OVRCameraRig not found in scene.";
            return;
        }
        Transform trackingSpace = _cameraRig.trackingSpace;

        var childAnchors = new List<OVRAnchor>();
        foreach (var room in roomAnchors)
        {
            if (!room.TryGetComponent(out OVRAnchorContainer container))
                continue;
            await container.FetchChildrenAsync(childAnchors);
        }

        Debug.Log($"[RiskExperimentLogger] Total child anchors: {childAnchors.Count}");

        foreach (var anchor in childAnchors)
        {
            if (!anchor.TryGetComponent(out OVRSemanticLabels labels))
                continue;

            string label = labels.Labels;

            bool isWall =
                label.Contains(OVRSceneManager.Classification.WallFace) ||
                label.Contains(OVRSceneManager.Classification.InvisibleWallFace);

            if (!isWall) continue;

            if (!anchor.TryGetComponent(out OVRLocatable locatable))
                continue;

            await locatable.SetEnabledAsync(true);

            if (!locatable.TryGetSceneAnchorPose(out var pose))
                continue;

            Vector3 worldPos = pose.ComputeWorldPosition(trackingSpace) ?? Vector3.zero;
            Quaternion worldRot = pose.ComputeWorldRotation(trackingSpace) ?? Quaternion.identity;
            Vector3 normal = worldRot * Vector3.forward;

            _wallSurfaces.Add(new WallSurface { position = worldPos, normal = normal, label = label, index = _wallSurfaces.Count });
            Debug.Log($"[RiskExperimentLogger] Added wall surface: {label} at {worldPos}");
        }

        _riskDisplayText = _displayText = $"Loaded {_wallSurfaces.Count} wall surfaces.";
        _sceneLoaded = true;
    }

    private static float Safe(float v) =>
        float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;

    private static float GetStateRisk(UserMotionState state)
    {
        switch (state)
        {
            case UserMotionState.Static:
                return 0.0f;
            case UserMotionState.Agitated:
                return 1.0f;
            default:
                return 0.5f;
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // Motion features
        float hmdSpeed = 0f, hmdAccelMag = 0f, hmdAngularSpeed = 0f;
        float leftSpeed = 0f, rightSpeed = 0f;
        Vector3 hmdPos = Vector3.zero;
        Vector3 hmdVelocity = Vector3.zero;
        Vector3 hmdAccelVector = Vector3.zero;

        if (_hmdTransform != null)
        {
            hmdPos = _hmdTransform.position;
            Quaternion hmdRot = _hmdTransform.rotation;
            if (_motionStateFilter == null)
            {
                _motionStateFilter = new UserMotionStateFilter(userMotionSettings);
            }

            CurrentMotionSnapshot = _motionStateFilter.Update(
                Time.realtimeSinceStartupAsDouble,
                hmdPos,
                hmdRot);
            hmdVelocity = CurrentMotionSnapshot.FilteredVelocity;
            hmdSpeed = Safe(CurrentMotionSnapshot.FilteredSpeed);
            hmdAccelVector = CurrentMotionSnapshot.FilteredAcceleration;
            hmdAccelMag = Safe(CurrentMotionSnapshot.FilteredAccelerationMagnitude);
            hmdAngularSpeed = Safe(CurrentMotionSnapshot.FilteredAngularSpeed);
            CurrentUserState = CurrentMotionSnapshot.StableState;

            if (CurrentMotionSnapshot.StateChanged)
            {
                Debug.Log(
                    string.Format(
                        "[UserMotion] state={0} speed={1:F3} accel={2:F3} angular={3:F3}",
                        CurrentUserState,
                        hmdSpeed,
                        hmdAccelMag,
                        hmdAngularSpeed));
            }

            if (_firstHandFrame)
            {
                _prevLeftPos = _leftHandTransform != null ? _leftHandTransform.position : Vector3.zero;
                _prevRightPos = _rightHandTransform != null ? _rightHandTransform.position : Vector3.zero;
                _firstHandFrame = false;
            }
            else if (dt > 0f)
            {
                if (_leftHandTransform != null)
                {
                    leftSpeed = Safe((_leftHandTransform.position - _prevLeftPos).magnitude / dt);
                    _prevLeftPos = _leftHandTransform.position;
                }

                if (_rightHandTransform != null)
                {
                    rightSpeed = Safe((_rightHandTransform.position - _prevRightPos).magnitude / dt);
                    _prevRightPos = _rightHandTransform.position;
                }
            }
        }

        float avgHandSpeed = (leftSpeed + rightSpeed) * 0.5f;
        float handHeadRatio = Safe(avgHandSpeed / (hmdSpeed + 0.001f));
        UserMotionState userState = CurrentUserState;
        float rState = GetStateRisk(userState);
        float rDynamic = dynamicRiskController != null
            ? dynamicRiskController.LatestMaximumRisk
            : 0f;
        float rIntent = 0f;

        // UI panel fixed 2m ahead of camera
        Transform cam = _hmdTransform != null ? _hmdTransform
            : Camera.main != null ? Camera.main.transform : null;
        if (cam != null && labelRoot != null)
        {
            labelRoot.position = cam.position + cam.forward * 2f - cam.up * 0.15f;
            labelRoot.rotation = cam.rotation;
        }

        if (!_sceneLoaded || _wallSurfaces.Count == 0)
        {
            CurrentStaticMeasurement = StaticRiskMeasurement.Unavailable;
            int trackedPeople = dynamicRiskController != null
                && dynamicRiskController.LatestFrame != null
                ? dynamicRiskController.LatestFrame.ConfirmedPersonCount
                : 0;

            var sb = new StringBuilder();
            sb.AppendLine("[Scene Distance]");
            sb.AppendLine("Unavailable (Space Setup is optional for dynamic-risk testing)");
            sb.AppendLine();
            sb.AppendLine("[Dynamic Detection]");
            sb.AppendLine($"Tracked People: {trackedPeople}");
            sb.AppendLine($"Maximum Rdynamic: {rDynamic:F2}");
            sb.AppendLine(
                trackedPeople == 0
                    ? "Status: Waiting for camera detections"
                    : "Status: Dynamic-risk pipeline active");
            _displayText = sb.ToString();

            var sbRight = new StringBuilder();
            sbRight.AppendLine("[User State]");
            sbRight.AppendLine($"State: {userState}");
            sbRight.AppendLine($"Rstate: {rState:F2}");
            sbRight.AppendLine();
            sbRight.AppendLine("[Risk Snapshot Input]");
            sbRight.AppendLine("Rstatic: unavailable");
            sbRight.AppendLine($"Rdynamic: {rDynamic:F2}");
            sbRight.AppendLine($"Rintent: {rIntent:F2}");
            sbRight.AppendLine("Final risk: see compact snapshot HUD");
            _riskDisplayText = sbRight.ToString();
        }
        // Distance + risk (WallFace / InvisibleWallFace only)
        else
        {
            float minDist = float.MaxValue;
            int closestIndex = -1;
            WallSurface closestWall = default;

            foreach (var surface in _wallSurfaces)
            {
                float dist = Mathf.Abs(Vector3.Dot(hmdPos - surface.position, surface.normal));
                if (dist < minDist)
                {
                    minDist = dist;
                    closestIndex = surface.index;
                    closestWall = surface;
                }
            }

            // Wall approach
            float signedDist = Vector3.Dot(hmdPos - closestWall.position, closestWall.normal);
            Vector3 dirToWall = -Mathf.Sign(signedDist) * closestWall.normal;
            float towardWallSpeed = Safe(Mathf.Max(0f, Vector3.Dot(hmdVelocity, dirToWall)));
            float towardWallAccel = Safe(Mathf.Max(0f, Vector3.Dot(hmdAccelVector, dirToWall)));
            bool approachingWall = towardWallSpeed > 0.01f;
            float ttc = approachingWall ? minDist / towardWallSpeed : float.PositiveInfinity;

            // Static collision risk score
            float rd = Safe(1f - Mathf.Clamp01(minDist / safeDistance));
            float rttc = (!approachingWall || float.IsInfinity(ttc)) ? 0f : Safe(1f - Mathf.Clamp01(ttc / safeTime));
            float ra = Safe(Mathf.Clamp01(towardWallAccel / maxApproachAccel));

            // Blind-spot risk: angle between head forward direction and wall approach direction
            Vector3 headForward = _hmdTransform != null ? _hmdTransform.forward : Vector3.forward;
            float thetaToWall = Vector3.Angle(headForward, dirToWall);
            float rBlind;
            if (thetaToWall < 60f) rBlind = 0.2f;
            else if (thetaToWall < 120f) rBlind = 0.5f;
            else rBlind = 0.8f;

            float collisionWeightSum = weightDistance + weightTTC + weightApproachAccel + weightBlind;
            if (collisionWeightSum == 0f) collisionWeightSum = 1f;

            float rStatic = Safe(
                (weightDistance * rd
                + weightTTC * rttc
                + weightApproachAccel * ra
                + weightBlind * rBlind) / collisionWeightSum);
            CurrentStaticMeasurement = new StaticRiskMeasurement(
                true,
                minDist,
                float.IsInfinity(ttc) ? 0f : ttc,
                !float.IsInfinity(ttc),
                towardWallSpeed,
                towardWallAccel,
                rd,
                rttc,
                ra,
                rBlind,
                rStatic);

            var sb = new StringBuilder();
            sb.AppendLine("[Scene Distance]");
            sb.AppendLine($"Head: ({hmdPos.x:F2}, {hmdPos.y:F2}, {hmdPos.z:F2})");
            sb.AppendLine($"Closest Wall: #{closestIndex}");
            sb.AppendLine($"Distance: {minDist:F3}m");
            sb.AppendLine();
            sb.AppendLine("[Motion Features]");
            sb.AppendLine($"Head Speed: {hmdSpeed:F3} m/s");
            sb.AppendLine($"Head Accel: {hmdAccelMag:F3} m/s²");
            sb.AppendLine($"Head Angular: {hmdAngularSpeed:F3} rad/s");
            sb.AppendLine($"Left Hand Speed: {leftSpeed:F3} m/s");
            sb.AppendLine($"Right Hand Speed: {rightSpeed:F3} m/s");
            sb.AppendLine($"Hand Avg Speed: {avgHandSpeed:F3} m/s");
            sb.AppendLine($"Hand/Head Ratio: {handHeadRatio:F2}");
            sb.AppendLine();
            sb.AppendLine("[Wall Approach]");
            sb.AppendLine($"Toward Wall Speed: {towardWallSpeed:F3} m/s");
            sb.AppendLine($"Toward Wall Accel: {towardWallAccel:F3} m/s²");
            sb.AppendLine(float.IsInfinity(ttc) ? "TTC: Infinity" : $"TTC: {ttc:F2} s");
            sb.AppendLine($"Approaching Wall: {approachingWall}");

            var sbRight = new StringBuilder();
            sbRight.AppendLine("[User State]");
            sbRight.AppendLine($"State: {userState}");
            sbRight.AppendLine($"Rstate: {rState:F2}");
            sbRight.AppendLine();
            sbRight.AppendLine("[Static Environment Risk]");
            sbRight.AppendLine($"Rd: {rd:F2}");
            sbRight.AppendLine($"RTTC: {rttc:F2}");
            sbRight.AppendLine($"Ra: {ra:F2}");
            sbRight.AppendLine($"Theta To Wall: {thetaToWall:F1} deg");
            sbRight.AppendLine($"Rblind: {rBlind:F2}");
            sbRight.AppendLine($"Rstatic: {rStatic:F2}");
            sbRight.AppendLine();
            sbRight.AppendLine("[Risk Snapshot Input]");
            sbRight.AppendLine($"Rdynamic = {rDynamic:F2}");
            sbRight.AppendLine($"Rintent = {rIntent:F2}");
            sbRight.AppendLine("Final risk: see compact snapshot HUD");

            _displayText = sb.ToString();
            _riskDisplayText = sbRight.ToString();
        }

        if (labelText != null)
            labelText.text = _displayText;

        if (riskLabelText != null)
            riskLabelText.text = _riskDisplayText;
    }
}
