using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UI;

public class QuestSceneDistanceLogger : MonoBehaviour
{
    private struct Surface
    {
        public Vector3 position;
        public Vector3 normal;
        public string label;
    }

    [SerializeField] private Text labelText;
    [SerializeField] private Transform labelRoot;

    private const string ScenePermission = "com.oculus.permission.USE_SCENE";
    private readonly List<Surface> _surfaces = new();
    private string _displayText = "Initializing...";
    private bool _loaded = false;

    // OVRCameraRig and tracked transforms
    private OVRCameraRig _cameraRig;
    private Transform _hmdTransform;
    private Transform _leftHandTransform;
    private Transform _rightHandTransform;

    // Motion state
    private Vector3 _prevHmdPos;
    private Vector3 _prevHmdVelocity;
    private Quaternion _prevHmdRot;
    private Vector3 _prevLeftPos;
    private Vector3 _prevRightPos;
    private bool _firstFrame = true;

    void Start()
    {
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
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => LoadScene();
            callbacks.PermissionDenied += _ => _displayText = "SCENE permission denied.";
            Permission.RequestUserPermission(ScenePermission, callbacks);
        }
        else
        {
            LoadScene();
        }
    }

    async void LoadScene()
    {
        _displayText = "Loading scene data...";

        var roomAnchors = new List<OVRAnchor>();
        var result = await OVRAnchor.FetchAnchorsAsync(roomAnchors, new OVRAnchor.FetchOptions
        {
            SingleComponentType = typeof(OVRRoomLayout)
        });

        if (!result.Success || roomAnchors.Count == 0)
        {
            _displayText = "No rooms found.\nRun Space Setup on your headset first.";
            return;
        }

        if (_cameraRig == null)
        {
            _displayText = "OVRCameraRig not found in scene.";
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

        Debug.Log($"[SceneDistLogger] Total child anchors: {childAnchors.Count}");

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

            _surfaces.Add(new Surface { position = worldPos, normal = normal, label = label });
            Debug.Log($"[SceneDistLogger] Added wall surface: {label} at {worldPos}");
        }

        _displayText = $"Loaded {_surfaces.Count} wall surfaces.";
        _loaded = true;
    }

    private static float Safe(float v) =>
        float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;

    void Update()
    {
        float dt = Time.deltaTime;

        // Motion features
        float hmdSpeed = 0f, hmdAccel = 0f, hmdAngularSpeed = 0f;
        float leftSpeed = 0f, rightSpeed = 0f;
        Vector3 hmdPos = Vector3.zero;
        Vector3 hmdVelocity = Vector3.zero;

        if (_hmdTransform != null)
        {
            hmdPos = _hmdTransform.position;
            Quaternion hmdRot = _hmdTransform.rotation;

            if (_firstFrame)
            {
                _prevHmdPos = hmdPos;
                _prevHmdVelocity = Vector3.zero;
                _prevHmdRot = hmdRot;
                _prevLeftPos = _leftHandTransform != null ? _leftHandTransform.position : Vector3.zero;
                _prevRightPos = _rightHandTransform != null ? _rightHandTransform.position : Vector3.zero;
                _firstFrame = false;
            }
            else if (dt > 0f)
            {
                hmdVelocity = (hmdPos - _prevHmdPos) / dt;
                hmdSpeed = Safe(hmdVelocity.magnitude);

                Vector3 accelVec = (hmdVelocity - _prevHmdVelocity) / dt;
                hmdAccel = Safe(accelVec.magnitude);

                float angleDeg = Quaternion.Angle(hmdRot, _prevHmdRot);
                hmdAngularSpeed = Safe(angleDeg * Mathf.Deg2Rad / dt);

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

                _prevHmdPos = hmdPos;
                _prevHmdVelocity = hmdVelocity;
                _prevHmdRot = hmdRot;
            }
        }

        float avgHandSpeed = (leftSpeed + rightSpeed) * 0.5f;
        float handHeadRatio = Safe(avgHandSpeed / (hmdSpeed + 0.001f));

        // UI panel fixed 2m ahead of camera
        Transform cam = _hmdTransform != null ? _hmdTransform
            : Camera.main != null ? Camera.main.transform : null;
        if (cam != null && labelRoot != null)
        {
            labelRoot.position = cam.position + cam.forward * 2f - cam.up * 0.15f;
            labelRoot.rotation = cam.rotation;
        }

        // Distance measurement (WallFace / InvisibleWallFace only)
        if (_loaded && _surfaces.Count > 0)
        {
            float minDist = float.MaxValue;
            string closestLabel = "";
            Surface closestWall = default;

            var sb = new StringBuilder();
            sb.AppendLine("[Scene Distance]");
            sb.AppendLine($"Head: ({hmdPos.x:F2}, {hmdPos.y:F2}, {hmdPos.z:F2})");
            sb.AppendLine();

            foreach (var surface in _surfaces)
            {
                float dist = Mathf.Abs(Vector3.Dot(hmdPos - surface.position, surface.normal));
                sb.AppendLine($"Wall: {dist:F3}m");

                if (dist < minDist)
                {
                    minDist = dist;
                    closestLabel = surface.label;
                    closestWall = surface;
                }
            }

            sb.AppendLine();
            sb.AppendLine($"Closest Wall: {closestLabel}");
            sb.AppendLine($"Distance: {minDist:F3}m");
            sb.AppendLine();
            sb.AppendLine("[Motion Features]");
            sb.AppendLine($"Head Speed: {hmdSpeed:F3} m/s");
            sb.AppendLine($"Head Accel: {hmdAccel:F3} m/s²");
            sb.AppendLine($"Head Angular: {hmdAngularSpeed:F3} rad/s");
            sb.AppendLine($"Left Hand Speed: {leftSpeed:F3} m/s");
            sb.AppendLine($"Right Hand Speed: {rightSpeed:F3} m/s");
            sb.AppendLine($"Hand Avg Speed: {avgHandSpeed:F3} m/s");
            sb.AppendLine($"Hand/Head Ratio: {handHeadRatio:F2}");

            // Wall approach: is the HMD actually closing in on the closest wall?
            float signedDist = Vector3.Dot(hmdPos - closestWall.position, closestWall.normal);
            Vector3 dirToWall = -Mathf.Sign(signedDist) * closestWall.normal;
            float towardWallSpeed = Safe(Mathf.Max(0f, Vector3.Dot(hmdVelocity, dirToWall)));
            bool approachingWall = towardWallSpeed > 0.01f;
            float ttc = approachingWall ? minDist / towardWallSpeed : float.PositiveInfinity;

            sb.AppendLine();
            sb.AppendLine("[Wall Approach]");
            sb.AppendLine($"Toward Wall Speed: {towardWallSpeed:F3} m/s");
            sb.AppendLine(float.IsInfinity(ttc) ? "TTC: Infinity" : $"TTC: {ttc:F2} s");
            sb.AppendLine($"Approaching Wall: {approachingWall}");

            _displayText = sb.ToString();
        }

        if (labelText != null)
            labelText.text = _displayText;
    }
}
