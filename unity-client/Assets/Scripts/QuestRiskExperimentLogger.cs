using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UI;

// 개입 강도. 손과 몸통을 같은 출력으로 내보내지 않기 위해 나눈다.
//   Aware : 그 방향에 경량 표시(화살표/가장자리) 정도. 오작동 비용이 낮다.
//   Full  : 전체 Passthrough. 몸통이 이동할 때, 또는 손 접촉이 임박했을 때만.
public enum WarningLevel
{
    None,
    Aware,
    Full
}

public enum RiskLevel
{
    Safe,
    Caution,
    Warning,
    Danger
}

public class QuestRiskExperimentLogger : MonoBehaviour
{
    private struct WallSurface
    {
        public Vector3 position;
        public Vector3 normal;
        public string label;
        public int index;
    }

    // UserState(머리 순변위 속도) 계산용 롤링 윈도우 샘플. `time`은 Time.unscaledTime이라
    // 일시정지/timeScale에 영향받지 않는다. headPosition은 눈이 아니라 목 회전축 추정
    // 지점이다 (GetNeckPoint 참고).
    private struct StateSample
    {
        public float time;
        public Vector3 headPosition;
    }

    // Smallest divisor allowed for risk-formula denominators / durations, so a stray 0
    // (or negative value) typed into the Inspector can never produce Infinity/NaN.
    private const float MinPositiveValue = 0.01f;

    [SerializeField] private Text labelText;
    [SerializeField] private Text riskLabelText;
    [SerializeField] private Transform labelRoot;
    [SerializeField] private OVRPassthroughLayer passthroughLayer;

    [Header("User State - Window")]
    [Min(MinPositiveValue)]
    [SerializeField] private float stateWindowDuration = 0.5f;
    // 손 속도는 벡터를 저역통과한 뒤 크기를 취한다. 크기를 먼저 취하면 항상 양수라
    // 트래킹 지터가 상쇄되지 않고 그대로 남는다.
    [Min(MinPositiveValue)]
    [SerializeField] private float handVelocitySmoothingTime = 0.15f;

    [Header("User State - Neck Pivot")]
    // centerEyeAnchor는 목 회전축보다 앞·위에 있다. 보정하지 않으면 고개를 180도 한 번
    // 돌리는 것만으로 0.41 m/s의 가짜 위치 이동이 잡혀 UserState가 잘못 높게 나온다.
    // 목 지점을 추적하면 같은 동작이 0.02 m/s로 떨어진다.
    [Min(0f)]
    [SerializeField] private float neckPivotForwardOffset = 0.10f;
    [Min(0f)]
    [SerializeField] private float neckPivotUpOffset = 0.10f;

    [Header("User State - Classification")]
    // UserState = clamp01(머리 순변위 속도 / thresholdHeadSpeedScale). 0 = 완전히 정적,
    // 1 = 이 속도 이상으로 움직이면 완전히 동적. 손 속도는 일부러 반영하지 않는다 -
    // 반영하면 벽 가까이 등지고 앉아 팔만 흔들어도 UserState가 올라가서 거리·사각지대만으로
    // 쌓인 위험도를 오작동으로 넘겨버릴 수 있다.
    [Min(MinPositiveValue)]
    [SerializeField] private float thresholdHeadSpeedScale = 1.0f;

    [Header("Collision Risk Parameters")]
    // 필드 이름을 바꾼 이유: Unity는 [SerializeField] 값을 필드 "이름" 기준으로 씬에 저장하므로,
    // 코드 기본값만 고치면 씬에 남은 예전 값이 계속 이긴다. 이름을 바꾸면 연결이 끊겨 기본값이 적용된다.
    [Min(MinPositiveValue)]
    [SerializeField] private float safeDistanceMeters = 2.5f;
    // "몇 초 전에 켤 것인가"를 정하는 값. 4.5면 보행 속도에 따라 1.4~1.8초 전에 켜진다.
    [Min(MinPositiveValue)]
    [SerializeField] private float safeTimeSeconds = 4.5f;
    [Min(MinPositiveValue)]
    [SerializeField] private float maxApproachAccel = 5.0f;

    [Header("Collision Risk Weights")]
    [Range(0f, 1f)]
    [SerializeField] private float weightDistance = 0.30f;
    [Range(0f, 1f)]
    [SerializeField] private float weightTTC = 0.30f;
    // 등속 보행에서 Ra는 항상 0이라 이 가중치가 통째로 버려진다. 0.25를 유지하면
    // R_static_head 상한이 0.63에 묶여 임계값 0.65를 못 넘는다.
    [Range(0f, 1f)]
    [SerializeField] private float weightApproachAcceleration = 0.0f;
    [Range(0f, 1f)]
    [SerializeField] private float weightBlind = 0.15f;

    [Header("Hand Collision Risk")]
    // 손 움직임을 위험도에서 빼지 않는다. 대신 "지금 그 손이 그 벽에 실제로 닿을 수 있는가"로
    // 가중한다. 팔 길이를 넘는 거리의 벽은 접촉가능도가 0이 되어 자동으로 빠진다.
    // 억제 규칙이 아니라 물리적 사실이므로, 팔 닿는 거리에서는 손 속도가 그대로 반영된다.
    [SerializeField] private bool enableHandRisk = true;
    // 팔 길이. 사용 중 관측된 머리~손 최대 거리로 자동 보정된다(하한이자 초기값).
    [Min(0.1f)]
    [SerializeField] private float personalReachLength = 0.70f;
    [SerializeField] private bool autoCalibrateReach = true;
    // 트래킹 이상치가 팔 길이를 비현실적으로 밀어올리는 것을 막는 상한.
    [Min(0.1f)]
    [SerializeField] private float maxPlausibleReach = 1.00f;
    // 접촉가능도가 1에서 0으로 넘어가는 구간의 폭.
    [Min(MinPositiveValue)]
    [SerializeField] private float reachTransitionMargin = 0.15f;
    [Min(MinPositiveValue)]
    [SerializeField] private float safeHandDistance = 0.50f;
    [Min(MinPositiveValue)]
    [SerializeField] private float safeHandTime = 1.00f;
    [Range(0f, 1f)]
    [SerializeField] private float weightHandDistance = 0.40f;
    [Range(0f, 1f)]
    [SerializeField] private float weightHandTTC = 0.60f;
    // 손이 벽 쪽으로 이 속도 미만이면 접근으로 보지 않는다(벽 옆에 손을 둔 상태 등).
    [Min(0f)]
    [SerializeField] private float handApproachSpeedMin = 0.05f;

    [Header("Warning Level")]
    // 이 값을 넘으면 Aware. 경량 표시용이라 낮게 잡아도 비용이 작다.
    [Range(0f, 1f)]
    [SerializeField] private float awareThreshold = 0.40f;
    // Aware 도 "접근 중"일 때만 낸다.
    // R_static_head 는 가중평균이라 접근이 0이어도 거리(Rd)와 사각지대(Rblind)만으로 값이 쌓인다.
    // 벽을 등지고 앉아 있으면 blind 0.8 이 0.12 를 깔아서 1.0 m 에서 이미 0.40 을 넘는다.
    // UserState로 막지 않는 이유: UserState의 민감도 전환 구간(대략 머리 0.30 m/s 부근)을
    // 그대로 컷오프로 쓰면, 초당 29 cm로 다가가는(충돌 1.7초 전) 진짜 접근까지 함께
    // 억제된다. UserState는 Passthrough 민감도를 조절하는 값이지 안전 게이트가 아니다.
    [Min(0f)]
    [SerializeField] private float awareApproachSpeed = 0.05f;
    // 손 위험도만으로 Full Passthrough까지 올릴 임계값. 머리 임계값보다 훨씬 높게 둬서
    // 일상적인 팔 뻗기로는 넘지 못하고 접촉이 임박한 경우에만 넘도록 한다.
    [Range(0f, 1f)]
    [SerializeField] private float handFullThreshold = 0.85f;

    [Header("Passthrough Decision - Motion-Based Threshold (experimental initial values, not paper-final)")]
    [Range(0f, 1f)]
    [SerializeField] private float stableOnThreshold = 0.65f;
    [Range(0f, 1f)]
    [SerializeField] private float rapidOnThreshold = 0.45f;
    [Range(0f, 1f)]
    [SerializeField] private float hysteresisWidth = 0.08f;

    [Header("Passthrough Decision - Emergency Distance Override (experimental initial values, not paper-final)")]
    [Min(0f)]
    [SerializeField] private float emergencyDistance = 0.25f;
    [Min(0f)]
    [SerializeField] private float emergencyReleaseMargin = 0.05f;
    // 긴급 override가 발동하기 위한 최소 벽 방향 접근 속도.
    // 이 값 미만이면 거리가 아무리 가까워도 발동하지 않는다.
    [Min(0f)]
    [SerializeField] private float emergencyApproachSpeed = 0.10f;

    [Header("UI Refresh")]
    // The risk/passthrough decision itself still runs every frame; only the text panels
    // are throttled to this rate to avoid per-frame StringBuilder/GC churn on-device.
    [Min(MinPositiveValue)]
    [SerializeField] private float uiRefreshInterval = 0.15f;

    private const string ScenePermission = "com.oculus.permission.USE_SCENE";
    private readonly List<WallSurface> _wallSurfaces = new();
    private string _displayText = "Initializing (Risk Experiment)...";
    private string _riskDisplayText =
        "[User Motion]\nWaiting for motion data...\n\n[Collision Risk]\nWaiting for scene data...\n\n[Passthrough Decision]\nWaiting for scene data...";
    private bool _sceneLoaded = false;
    private bool _passthroughEnabled;
    private float _nextUiRefreshTime;

    // OVRCameraRig and tracked transforms
    private OVRCameraRig _cameraRig;
    private Transform _hmdTransform;
    private Transform _leftHandTransform;
    private Transform _rightHandTransform;

    // Instantaneous motion state (previous-frame snapshot for per-frame derivative computation)
    private Vector3 _prevHmdPos;
    private Vector3 _prevHmdVelocity;
    private Quaternion _prevHmdRot;
    private Vector3 _prevLeftPos;
    private Vector3 _prevRightPos;
    private bool _firstFrame = true;
    // True once a real (non-seed) velocity sample exists, so acceleration has something
    // valid to diff against. Prevents a huge fake spike on the first velocity frame, where
    // diffing against the zero-velocity seed would otherwise look like a hard acceleration.
    private bool _hasValidVelocity = false;

    // True once minDist has actually dropped to/below emergencyDistance, cleared only once
    // minDist rises back out past emergencyDistance + emergencyReleaseMargin. This is what
    // "emergency hold" means: keep Passthrough on because we entered an emergency and haven't
    // cleared the release margin yet - not merely "we happen to be near that band right now".
    private bool _emergencyActive = false;

    // UserState(머리 순변위 속도 기반) 계산용 롤링 윈도우.
    private readonly Queue<StateSample> _stateSamples = new();
    private Vector3 _smoothedLeftHandVelocity;
    private Vector3 _smoothedRightHandVelocity;
    private float _headTranslationSpeed;

    // ---- 손-벽 거리 (표시 전용. 위험도나 판정에는 전혀 관여하지 않는다) ----
    private float _leftWallDistance = float.PositiveInfinity;
    private float _rightWallDistance = float.PositiveInfinity;
    private float _leftTowardWallSpeed;
    private float _rightTowardWallSpeed;
    private int _leftWallIndex = -1;
    private int _rightWallIndex = -1;
    // 최근 도달한 최소 거리. 팔을 왕복시키면 이 값이 곧 "반환점"이 된다.
    // 천천히 되돌아오게 해서 순간적으로 지나간 값도 눈으로 읽을 수 있다.
    private float _leftMinWallDistance = float.PositiveInfinity;
    private float _rightMinWallDistance = float.PositiveInfinity;

    // ---- 손 위험도 (R_static_hand) ----
    private float _observedMaxReach;
    private float _leftHandExtension, _rightHandExtension;
    private float _leftReachGate, _rightReachGate;
    private float _leftStaticHandRisk, _rightStaticHandRisk;
    private float _staticHandRisk;
    private Vector3 _leftDirToWall, _rightDirToWall;
    private WarningLevel _warningLevel = WarningLevel.None;
    private float _combinedRisk;
    private bool _anyApproach;
    // Full 이 켜진 순간 어느 경로였는지. 정지 상태에서 켜지는 문제를 추적하기 위한 것.
    private string _lastFullCause = "-";

    // 상위 표시/제어 레이어를 위한 출력.
    public WarningLevel CurrentWarningLevel => _warningLevel;
    public float CurrentCombinedRisk => _combinedRisk;
    public float CurrentStaticHandRisk => _staticHandRisk;
    // 손 위험(R_static_hand)이 가장 높은 쪽의 벽 방향(world). 화살표를 그릴 때 쓴다.
    public Vector3 StaticHandRiskDirection =>
        _leftStaticHandRisk >= _rightStaticHandRisk ? _leftDirToWall : _rightDirToWall;

    void Start()
    {
        InitializePassthroughSystem();

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

    // 임의의 지점(손 등)에서 가장 가까운 벽까지의 거리와 그 벽을 향하는 방향을 구한다.
    // 머리 쪽 계산과 동일한 방식이며, 표시용으로만 쓴다.
    private bool TryGetClosestWall(Vector3 point, out float distance,
        out Vector3 directionToWall, out int wallIndex)
    {
        distance = float.PositiveInfinity;
        directionToWall = Vector3.zero;
        wallIndex = -1;

        foreach (var surface in _wallSurfaces)
        {
            float signed = Vector3.Dot(point - surface.position, surface.normal);
            float dist = Mathf.Abs(signed);

            if (dist >= distance)
                continue;

            distance = dist;
            wallIndex = surface.index;
            directionToWall = -Mathf.Sign(signed) * surface.normal;
        }

        return wallIndex >= 0;
    }

    // 손별 거리/접근속도/최소거리 갱신. 접근 속도는 지터를 줄이려고 스무딩된 속도를 쓴다.
    private void UpdateHandWallDistances(float dt)
    {
        UpdateOneHand(dt, _leftHandTransform, _smoothedLeftHandVelocity,
            ref _leftWallDistance, ref _leftTowardWallSpeed,
            ref _leftWallIndex, ref _leftMinWallDistance,
            ref _leftHandExtension, ref _leftReachGate, ref _leftStaticHandRisk, ref _leftDirToWall);

        UpdateOneHand(dt, _rightHandTransform, _smoothedRightHandVelocity,
            ref _rightWallDistance, ref _rightTowardWallSpeed,
            ref _rightWallIndex, ref _rightMinWallDistance,
            ref _rightHandExtension, ref _rightReachGate, ref _rightStaticHandRisk, ref _rightDirToWall);

        // 한 손의 위험이 다른 손 때문에 희석되면 안 되므로 평균이 아니라 최대.
        _staticHandRisk = Mathf.Max(_leftStaticHandRisk, _rightStaticHandRisk);
    }

    private void UpdateOneHand(float dt, Transform hand, Vector3 smoothedVelocity,
        ref float distance, ref float towardSpeed, ref int wallIndex, ref float minDistance,
        ref float extension, ref float reachGate, ref float risk, ref Vector3 dirToWall)
    {
        if (hand == null || _hmdTransform == null ||
            !TryGetClosestWall(hand.position, out float d, out Vector3 dir, out int idx))
        {
            distance = float.PositiveInfinity;
            towardSpeed = 0f;
            wallIndex = -1;
            extension = 0f;
            reachGate = 0f;
            risk = 0f;
            dirToWall = Vector3.zero;
            return;
        }

        distance = Safe(d);
        wallIndex = idx;
        dirToWall = dir;
        towardSpeed = Safe(Mathf.Max(0f, Vector3.Dot(smoothedVelocity, dir)));

        minDistance = float.IsInfinity(minDistance)
            ? distance
            : Mathf.Min(distance, minDistance + dt * 0.2f);

        // 현재 팔이 뻗어 있는 정도. 관측 최대값이 곧 이 사용자의 팔 길이 추정치가 된다.
        extension = Safe(Vector3.Distance(hand.position, _hmdTransform.position));

        if (autoCalibrateReach &&
            extension <= Mathf.Max(0.1f, maxPlausibleReach) &&
            extension > _observedMaxReach)
        {
            _observedMaxReach = extension;
        }

        if (!enableHandRisk)
        {
            reachGate = 0f;
            risk = 0f;
            return;
        }

        // 접촉가능도: 남은 뻗을 여유 안에 벽이 들어오는가.
        // 머리-벽 거리가 팔 길이를 넘으면 이 값이 0이 되어 손이 자동으로 빠진다.
        float armReach = Mathf.Clamp(
            Mathf.Max(personalReachLength, autoCalibrateReach ? _observedMaxReach : 0f),
            0.1f, Mathf.Max(0.1f, maxPlausibleReach));

        float remainingReach = Mathf.Max(0f, armReach - extension);
        float margin = Mathf.Max(reachTransitionMargin, MinPositiveValue);

        reachGate = Safe(Mathf.Clamp01((remainingReach - distance + margin) / margin));

        bool approaching = towardSpeed > Mathf.Max(0f, handApproachSpeedMin);

        float handTtc = approaching
            ? distance / Mathf.Max(towardSpeed, MinPositiveValue)
            : float.PositiveInfinity;

        float rdHand = 1f - Mathf.Clamp01(distance / Mathf.Max(safeHandDistance, MinPositiveValue));
        float rttcHand = approaching
            ? 1f - Mathf.Clamp01(handTtc / Mathf.Max(safeHandTime, MinPositiveValue))
            : 0f;

        float weightSum = weightHandDistance + weightHandTTC;
        if (weightSum <= 0f) weightSum = 1f;

        risk = Safe(Mathf.Clamp01(
            reachGate * (weightHandDistance * rdHand + weightHandTTC * rttcHand) / weightSum));
    }

    private static string FormatHandWall(string tag, int wallIndex, float distance,
        float towardSpeed, float minDistance)
    {
        if (wallIndex < 0 || float.IsInfinity(distance))
            return $"{tag}: tracking unavailable";

        return $"{tag}: #{wallIndex} {distance:F3}m"
             + $" | toward {towardSpeed:F2} m/s"
             + $" | min {minDistance:F3}m";
    }

    private static float Safe(float v) =>
        float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;

    // Maps a raw feature value to a 0..1 score between lowThreshold and highThreshold.
    // low/high are re-sorted internally so a reversed Inspector entry (high < low) can never
    // flip the sign of the score (bigger raw value would otherwise yield a *smaller* score).
    private static float NormalizeFeature(float value, float lowThreshold, float highThreshold)
    {
        float low = Mathf.Min(lowThreshold, highThreshold);
        float high = Mathf.Max(lowThreshold, highThreshold);
        float range = high - low;
        if (Mathf.Approximately(range, 0f)) return 0f;
        return Safe(Mathf.Clamp01((value - low) / range));
    }

    // 눈 위치에서 목 회전축 지점을 역산한다. 순수한 고개 회전은 이 점을 거의 움직이지
    // 않으므로, 여기서 나오는 이동은 실제 몸의 위치 이동이다.
    // 충돌 판정에는 쓰지 않는다 - 벽에 닿는 것은 이마이지 목이 아니다.
    private Vector3 GetNeckPoint()
    {
        if (_hmdTransform == null)
            return Vector3.zero;

        return _hmdTransform.position
             - _hmdTransform.forward * Mathf.Max(0f, neckPivotForwardOffset)
             - _hmdTransform.up * Mathf.Max(0f, neckPivotUpOffset);
    }

    // 머리 위치 이동(순 변위 / 경과시간)만으로 UserState를 계산하기 위한 롤링 윈도우 갱신.
    // 손 속도 스무딩도 여기서 같이 하지만, 그 결과(_smoothedLeftHandVelocity/
    // _smoothedRightHandVelocity)는 R_static_hand의 접근 속도 계산에 쓰이는 것이지
    // UserState 계산에는 들어가지 않는다.
    // 각속도(고개 돌리기)와 가속도는 UserState 목적에 잡음만 들여오므로 쓰지 않는다.
    private void UpdateUserState(float dt, Vector3 neckPosition,
        Vector3 leftHandVelocity, Vector3 rightHandVelocity)
    {
        float blend = dt > 0f
            ? 1f - Mathf.Exp(-dt / Mathf.Max(handVelocitySmoothingTime, MinPositiveValue))
            : 0f;

        _smoothedLeftHandVelocity =
            Vector3.Lerp(_smoothedLeftHandVelocity, leftHandVelocity, blend);
        _smoothedRightHandVelocity =
            Vector3.Lerp(_smoothedRightHandVelocity, rightHandVelocity, blend);

        _stateSamples.Enqueue(new StateSample
        {
            time = Time.unscaledTime,
            headPosition = neckPosition
        });

        float window = Mathf.Max(stateWindowDuration, MinPositiveValue);
        while (_stateSamples.Count > 0 &&
               Time.unscaledTime - _stateSamples.Peek().time > window)
        {
            _stateSamples.Dequeue();
        }

        if (_stateSamples.Count < 2)
            return;

        StateSample oldest = _stateSamples.Peek();
        float span = Mathf.Max(Time.unscaledTime - oldest.time, MinPositiveValue);

        // 순 변위 / 경과시간. 왕복하면 0에 가까워진다 = 위치 이동이 아니다.
        _headTranslationSpeed =
            Safe(Vector3.Distance(neckPosition, oldest.headPosition) / span);
    }

    private static RiskLevel ClassifyRiskLevel(float r)
    {
        if (r < 0.3f) return RiskLevel.Safe;
        if (r < 0.6f) return RiskLevel.Caution;
        if (r < 0.8f) return RiskLevel.Warning;
        return RiskLevel.Danger;
    }

    // Enables the Insight Passthrough system exactly once at startup, and forces the actual
    // layer visibility to match the internal _passthroughEnabled default (false/OFF). Without
    // this, a Passthrough Layer left "visible" in the Inspector between runs would disagree
    // with our internal state from frame one, and the experiment's starting condition would
    // depend on whatever was last left checked rather than being deterministic.
    private void InitializePassthroughSystem()
    {
        if (OVRManager.instance != null)
            OVRManager.instance.isInsightPassthroughEnabled = true;

        _passthroughEnabled = false;
        if (passthroughLayer != null)
            passthroughLayer.hidden = true;
    }

    private void ApplyPassthroughState(bool enabled, float rStaticHead)
    {
        _passthroughEnabled = enabled;

        if (passthroughLayer != null)
            passthroughLayer.hidden = !enabled;

        Debug.Log($"[Passthrough] {(enabled ? "ON" : "OFF")} - R_static_head: {rStaticHead:F3}");
    }

    void Update()
    {
        bool refreshUi = Time.unscaledTime >= _nextUiRefreshTime;
        if (refreshUi)
        {
            _nextUiRefreshTime = Time.unscaledTime + Mathf.Max(uiRefreshInterval, MinPositiveValue);
        }

        // Without a valid HMD transform there is no trustworthy position to compute wall
        // distance/collision risk from (falling back to Vector3.zero would silently treat the
        // world origin as the user's head), so risk judgment is paused entirely this frame.
        if (_hmdTransform == null)
        {
            if (refreshUi)
            {
                _displayText = "HMD transform not found - motion/risk tracking paused.";
                _riskDisplayText = _displayText;

                if (labelText != null) labelText.text = _displayText;
                if (riskLabelText != null) riskLabelText.text = _riskDisplayText;
            }
            return;
        }

        float dt = Time.unscaledDeltaTime;

        // Instantaneous motion features
        float hmdSpeed = 0f, hmdAccelMag = 0f, hmdAngularSpeed = 0f;
        float leftSpeed = 0f, rightSpeed = 0f;
        Vector3 hmdVelocity = Vector3.zero;
        Vector3 hmdAccelVector = Vector3.zero;
        // 손은 크기만으로는 지터를 걸러낼 수 없으므로 벡터를 살려둔다.
        Vector3 leftHandVelocity = Vector3.zero;
        Vector3 rightHandVelocity = Vector3.zero;
        bool motionSampleValid = false;

        Vector3 hmdPos = _hmdTransform.position;
        Quaternion hmdRot = _hmdTransform.rotation;

        if (_firstFrame)
        {
            _prevHmdPos = hmdPos;
            _prevHmdVelocity = Vector3.zero;
            _prevHmdRot = hmdRot;
            _prevLeftPos = _leftHandTransform != null ? _leftHandTransform.position : Vector3.zero;
            _prevRightPos = _rightHandTransform != null ? _rightHandTransform.position : Vector3.zero;
            _firstFrame = false;
            _hasValidVelocity = false;
        }
        else if (dt > 0f)
        {
            hmdVelocity = (hmdPos - _prevHmdPos) / dt;
            hmdSpeed = Safe(hmdVelocity.magnitude);

            if (_hasValidVelocity)
            {
                hmdAccelVector = (hmdVelocity - _prevHmdVelocity) / dt;
                hmdAccelMag = Safe(hmdAccelVector.magnitude);
            }
            else
            {
                // First frame with a real velocity sample: there is no earlier valid velocity
                // to diff against yet, so report zero acceleration instead of diffing against
                // the zero-velocity seed (which would look like a hard, fake spike).
                hmdAccelVector = Vector3.zero;
                hmdAccelMag = 0f;
                _hasValidVelocity = true;
            }

            float angleDeg = Quaternion.Angle(hmdRot, _prevHmdRot);
            hmdAngularSpeed = Safe(angleDeg * Mathf.Deg2Rad / dt);

            if (_leftHandTransform != null)
            {
                Vector3 currentLeftPosition = _leftHandTransform.position;
                leftHandVelocity = (currentLeftPosition - _prevLeftPos) / dt;
                leftSpeed = Safe(leftHandVelocity.magnitude);
                _prevLeftPos = currentLeftPosition;
            }

            if (_rightHandTransform != null)
            {
                Vector3 currentRightPosition = _rightHandTransform.position;
                rightHandVelocity = (currentRightPosition - _prevRightPos) / dt;
                rightSpeed = Safe(rightHandVelocity.magnitude);
                _prevRightPos = currentRightPosition;
            }

            _prevHmdPos = hmdPos;
            _prevHmdVelocity = hmdVelocity;
            _prevHmdRot = hmdRot;
            motionSampleValid = true;
        }

        float avgHandSpeed = (leftSpeed + rightSpeed) * 0.5f;
        float handHeadRatio = Safe(avgHandSpeed / (hmdSpeed + 0.001f));

        // ===== 사용자 상태 =====
        // 첫 프레임은 이전 값과의 차분이 없으므로 샘플로 넣지 않는다.
        if (motionSampleValid)
        {
            UpdateUserState(dt, GetNeckPoint(), leftHandVelocity, rightHandVelocity);
        }

        // 창에 샘플이 두 개는 있어야 순 변위를 잴 수 있다.
        bool motionWindowWarmedUp = _stateSamples.Count >= 2;

        // UserState: 머리 순변위 속도만 0~1로 정규화한 값. 0에 가까울수록 정적(가만히
        // 있음), 1에 가까울수록 동적(빠르게 이동 중)이며, 이 값 하나로 Passthrough ON
        // 임계값의 민감도를 조절한다. 손 움직임은 일부러 반영하지 않는다 - 반영하면 벽
        // 가까이 등지고 앉아 팔만 흔들어도 UserState가 올라가면서 거리·사각지대만으로
        // 쌓인 위험도(0.53 수준)를 넘어버린다. 그때 R_static_head는 하나도 안 변한다 -
        // 민감도만 내려온 것뿐이다.
        float userState = motionWindowWarmedUp
            ? Mathf.Clamp01(_headTranslationSpeed / thresholdHeadSpeedScale)
            : 0f;

        // UI panel fixed 2m ahead of the HMD
        if (labelRoot != null)
        {
            labelRoot.position = _hmdTransform.position + _hmdTransform.forward * 2f - _hmdTransform.up * 0.15f;
            labelRoot.rotation = _hmdTransform.rotation;
        }

        // Distance + risk (WallFace / InvisibleWallFace only). Computed every frame regardless
        // of UI refresh timing, since the Passthrough decision must react in real time; only the
        // text panels further below are throttled.
        bool sceneRiskAvailable = _sceneLoaded && _wallSurfaces.Count > 0;

        float minDist = 0f;
        int closestIndex = -1;
        float towardWallSpeed = 0f, towardWallSpeedRaw = 0f;
        float towardWallAccel = 0f, ttc = float.PositiveInfinity;
        bool approachingWall = false;
        float rd = 0f, rttc = 0f, ra = 0f, rBlind = 0f, thetaToWall = 0f, rStaticHead = 0f;
        RiskLevel riskLevel = RiskLevel.Safe;
        float effectiveOnThreshold = 0f, effectiveOffThreshold = 0f;
        bool emergencyTrigger = false, emergencyHold = false;

        if (sceneRiskAvailable)
        {
            minDist = float.MaxValue;
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

            // 손-벽 거리 갱신 (표시 전용)
            UpdateHandWallDistances(dt);

            // Wall approach
            float signedDist = Vector3.Dot(hmdPos - closestWall.position, closestWall.normal);
            Vector3 dirToWall = -Mathf.Sign(signedDist) * closestWall.normal;
            // 원시 순간 속도는 체간 동요만으로도 쉽게 튄다. 벽 0.20 m 앞에서는 0.064 m/s
            // 면 임계값을 넘는데, 선 자세의 자연스러운 흔들림이 0.02~0.08 m/s 다.
            // 그래서 상태 분류에 쓴 것과 같이 "최근 창의 순 변위 / 경과시간"을 쓴다.
            // 왕복 성분은 상쇄되고 실제로 다가간 만큼만 남는다.
            towardWallSpeedRaw = Safe(Mathf.Max(0f, Vector3.Dot(hmdVelocity, dirToWall)));

            towardWallSpeed = towardWallSpeedRaw;

            if (_stateSamples.Count >= 2)
            {
                StateSample oldestSample = _stateSamples.Peek();
                float approachSpan =
                    Mathf.Max(Time.unscaledTime - oldestSample.time, MinPositiveValue);

                Vector3 netDisplacement = GetNeckPoint() - oldestSample.headPosition;

                towardWallSpeed = Safe(Mathf.Max(0f,
                    Vector3.Dot(netDisplacement, dirToWall) / approachSpan));
            }
            towardWallAccel = Safe(Mathf.Max(0f, Vector3.Dot(hmdAccelVector, dirToWall)));
            approachingWall = towardWallSpeed > 0.01f;
            ttc = approachingWall ? minDist / towardWallSpeed : float.PositiveInfinity;

            // Collision risk score, purely from environment/geometry features.
            // UserState must NOT feed into this formula - it is only used later to
            // adjust the Passthrough threshold.
            // Denominators are floored to MinPositiveValue so a 0 (or negative) Inspector
            // value can never produce Infinity/NaN here.
            float safeDistanceGuarded = Mathf.Max(safeDistanceMeters, MinPositiveValue);
            float safeTimeGuarded = Mathf.Max(safeTimeSeconds, MinPositiveValue);
            float maxApproachAccelGuarded = Mathf.Max(maxApproachAccel, MinPositiveValue);

            rd = Safe(1f - Mathf.Clamp01(minDist / safeDistanceGuarded));
            rttc = (!approachingWall || float.IsInfinity(ttc)) ? 0f : Safe(1f - Mathf.Clamp01(ttc / safeTimeGuarded));
            ra = Safe(Mathf.Clamp01(towardWallAccel / maxApproachAccelGuarded));

            // Blind-spot risk: angle between head forward direction and wall approach direction
            Vector3 headForward = _hmdTransform.forward;
            thetaToWall = Vector3.Angle(headForward, dirToWall);
            if (thetaToWall < 60f) rBlind = 0.2f;
            else if (thetaToWall < 120f) rBlind = 0.5f;
            else rBlind = 0.8f;

            float collisionWeightSum = weightDistance + weightTTC + weightApproachAcceleration + weightBlind;
            if (collisionWeightSum == 0f) collisionWeightSum = 1f;

            // R_static_head is the sole judgment risk used for the Passthrough decision below.
            // Rdynamic (dynamic-object risk) / Rintent (approach-intent risk) are future,
            // not-yet-implemented risk terms - placeholders only, they do not affect this formula.
            rStaticHead = Safe(
                (weightDistance * rd
                + weightTTC * rttc
                + weightApproachAcceleration * ra
                + weightBlind * rBlind) / collisionWeightSum);

            riskLevel = ClassifyRiskLevel(rStaticHead);

            // UserState continuously moves the Passthrough ON threshold between the
            // static and dynamic endpoints - no discrete per-state switch. Regardless of which
            // Inspector field (stableOnThreshold/rapidOnThreshold) numerically holds the
            // larger value, static (UserState = 0) always maps to the higher threshold and
            // dynamic (UserState = 1) always maps to the lower one.
            float clampedStableOn = Mathf.Clamp01(stableOnThreshold);
            float clampedRapidOn = Mathf.Clamp01(rapidOnThreshold);
            float highOnThreshold = Mathf.Max(clampedStableOn, clampedRapidOn);
            float lowOnThreshold = Mathf.Min(clampedStableOn, clampedRapidOn);
            effectiveOnThreshold = Mathf.Lerp(highOnThreshold, lowOnThreshold, Mathf.Clamp01(userState));
            // Inspector attributes ([Range(0,1)]) constrain UI input, but don't protect against
            // values set via script or already-serialized out-of-range data, so hysteresisWidth
            // is floored again here - a negative value must never widen effectiveOffThreshold
            // above effectiveOnThreshold (which would invert the ON/OFF hysteresis direction).
            effectiveOffThreshold = Mathf.Clamp01(effectiveOnThreshold - Mathf.Max(0f, hysteresisWidth));

            // Same double-defense as above: [Min(0f)] guards the Inspector, this guards the math.
            float validEmergencyDistance = Mathf.Max(0f, emergencyDistance);
            float validEmergencyReleaseMargin = Mathf.Max(0f, emergencyReleaseMargin);

            // 긴급 override에도 "접근 중" 조건을 건다.
            // 이 조건이 없으면 벽 0.25 m 안쪽에 가만히 앉아 있기만 해도 emergencyTrigger가
            // 계속 참이 되고, emergencyHold가 OFF 분기를 막아 패스스루가 영원히 꺼지지 않는다.
            // 벽 가까이 정지한 상태는 위험하지 않다는 것이 이 시스템의 전제이므로,
            // 거리만으로는 발동하지 않아야 한다.
            bool headApproachingForEmergency =
                towardWallSpeed > Mathf.Max(0f, emergencyApproachSpeed);

            emergencyTrigger =
                minDist <= validEmergencyDistance &&
                headApproachingForEmergency;

            // 진입과 해제 양쪽에 같은 조건을 걸어야 데드밴드가 생기지 않는다.
            // 접근이 멈추면 거리와 무관하게 즉시 해제된다.
            if (emergencyTrigger)
            {
                _emergencyActive = true;
            }
            else if (!headApproachingForEmergency ||
                     minDist > validEmergencyDistance + validEmergencyReleaseMargin)
            {
                _emergencyActive = false;
            }

            emergencyHold = _emergencyActive;

            bool wasPassthroughEnabled = _passthroughEnabled;

            // Full Passthrough 는 기본적으로 머리/몸통 위험도만 본다.
            // 몸이 이동할 때는 어디로 가는지 모르니 시야 전체가 필요하지만, 손은 국소적
            // 문제라 방향 표시로 충분하다. 손이 벽에 닿는 것과 몸이 부딪히는 것은
            // 결과의 심각도도 다르므로 개입 강도를 다르게 둔다.
            //
            // 예외: 손 위험도가 handFullThreshold(기본 0.85)를 넘으면 접촉이 임박한
            // 것이므로 Full 까지 허용한다. 머리 임계값보다 훨씬 높아 일상적인 팔 뻗기로는
            // 넘지 못한다.
            float handFull = Mathf.Clamp01(handFullThreshold);
            float handRelease = Mathf.Max(0f, handFull - Mathf.Max(0f, hysteresisWidth));

            if (!_passthroughEnabled)
            {
                if (emergencyTrigger ||
                    rStaticHead >= effectiveOnThreshold ||
                    _staticHandRisk >= handFull)
                {
                    _passthroughEnabled = true;

                    _lastFullCause =
                        emergencyTrigger ? $"emergency (d {minDist:F2}, v {towardWallSpeed:F2})"
                        : rStaticHead >= effectiveOnThreshold
                            ? $"R_static_head {rStaticHead:F2} >= {effectiveOnThreshold:F2}"
                            : $"R_static_hand {_staticHandRisk:F2} >= {handFull:F2}";
                }
            }
            else
            {
                if (!emergencyHold &&
                    rStaticHead <= effectiveOffThreshold &&
                    _staticHandRisk <= handRelease)
                {
                    _passthroughEnabled = false;
                }
            }

            // 경고 등급. Aware 는 아직 출력 대상이 없으므로 신호로만 내보낸다.
            // 화살표/가장자리 표시 레이어가 붙으면 CurrentWarningLevel 을 구독하면 된다.
            _combinedRisk = Mathf.Max(rStaticHead, _staticHandRisk);

            // 머리든 손이든 하나라도 벽 쪽으로 다가가고 있어야 Aware 를 낸다.
            // 정지 상태에서는 아무리 가까워도 None.
            float awareApproachGate = Mathf.Max(0f, awareApproachSpeed);

            _anyApproach =
                towardWallSpeed > awareApproachGate ||
                _leftTowardWallSpeed > awareApproachGate ||
                _rightTowardWallSpeed > awareApproachGate;

            // Full 은 건드리지 않는다. 긴급 override 에 이미 접근 조건이 걸려 있고,
            // 그 경로까지 여기서 다시 막으면 이중 게이트가 된다.
            _warningLevel =
                _passthroughEnabled ? WarningLevel.Full
                : (_anyApproach && _combinedRisk >= Mathf.Clamp01(awareThreshold))
                    ? WarningLevel.Aware
                    : WarningLevel.None;

            if (_passthroughEnabled != wasPassthroughEnabled)
            {
                ApplyPassthroughState(_passthroughEnabled, rStaticHead);
            }
        }

        if (!refreshUi)
        {
            return;
        }

        var sbRight = new StringBuilder();
        sbRight.AppendLine("[User State]");
        sbRight.AppendLine($"UserState: {userState:F2}  (0=static, 1=dynamic; head speed only)");
        sbRight.AppendLine($"Head Translation: {_headTranslationSpeed:F3} m/s");
        sbRight.AppendLine($"Head Angular (log only): {hmdAngularSpeed:F3} rad/s");
        sbRight.AppendLine($"Head Accel (log only): {hmdAccelMag:F3} m/s²");
        sbRight.AppendLine($"Warmed Up: {motionWindowWarmedUp}");

        if (sceneRiskAvailable)
        {
            var sbLeft = new StringBuilder();
            sbLeft.AppendLine("[Scene Distance]");
            sbLeft.AppendLine($"Head: ({hmdPos.x:F2}, {hmdPos.y:F2}, {hmdPos.z:F2})");
            sbLeft.AppendLine($"Closest Wall: #{closestIndex}");
            sbLeft.AppendLine($"Distance: {minDist:F3}m");
            sbLeft.AppendLine();
            sbLeft.AppendLine("[Motion Features]");
            sbLeft.AppendLine($"Head Speed: {hmdSpeed:F3} m/s");
            sbLeft.AppendLine($"Head Accel: {hmdAccelMag:F3} m/s²");
            sbLeft.AppendLine($"Head Angular: {hmdAngularSpeed:F3} rad/s");
            sbLeft.AppendLine($"Left Hand Speed: {leftSpeed:F3} m/s");
            sbLeft.AppendLine($"Right Hand Speed: {rightSpeed:F3} m/s");
            sbLeft.AppendLine($"Hand Avg Speed: {avgHandSpeed:F3} m/s");
            sbLeft.AppendLine($"Hand/Head Ratio: {handHeadRatio:F2}");
            sbLeft.AppendLine();
            sbLeft.AppendLine("[Hand-Wall Distance]");
            sbLeft.AppendLine($"Arm Reach (learned): {Mathf.Max(personalReachLength, _observedMaxReach):F3} m");
            sbLeft.AppendLine(FormatHandWall("L", _leftWallIndex, _leftWallDistance,
                _leftTowardWallSpeed, _leftMinWallDistance));
            sbLeft.AppendLine(FormatHandWall("R", _rightWallIndex, _rightWallDistance,
                _rightTowardWallSpeed, _rightMinWallDistance));
            sbLeft.AppendLine();
            sbLeft.AppendLine("[Wall Approach]");
            sbLeft.AppendLine($"Toward Wall Speed: {towardWallSpeed:F3} m/s  (net)");
            sbLeft.AppendLine($"  raw instant: {towardWallSpeedRaw:F3} m/s");
            sbLeft.AppendLine($"Toward Wall Accel: {towardWallAccel:F3} m/s²");
            sbLeft.AppendLine(float.IsInfinity(ttc) ? "TTC: Infinity" : $"TTC: {ttc:F2} s");
            sbLeft.AppendLine($"Approaching Wall: {approachingWall}");

            sbRight.AppendLine();
            sbRight.AppendLine("[Collision Risk]");
            sbRight.AppendLine($"Rd: {rd:F2}");
            sbRight.AppendLine($"RTTC: {rttc:F2}");
            sbRight.AppendLine($"Ra: {ra:F2}");
            sbRight.AppendLine($"Theta To Wall: {thetaToWall:F1} deg");
            sbRight.AppendLine($"Rblind: {rBlind:F2}");
            sbRight.AppendLine($"R_static_head: {rStaticHead:F2}");
            sbRight.AppendLine();
            sbRight.AppendLine("[Hand Risk]");
            sbRight.AppendLine(
                $"L: gate {_leftReachGate:F2} ext {_leftHandExtension:F2}m R {_leftStaticHandRisk:F2}");
            sbRight.AppendLine(
                $"R: gate {_rightReachGate:F2} ext {_rightHandExtension:F2}m R {_rightStaticHandRisk:F2}");
            sbRight.AppendLine($"R_static_hand (max): {_staticHandRisk:F2}  (Full at {handFullThreshold:F2})");
            sbRight.AppendLine();
            sbRight.AppendLine(
                $"Combined: {_combinedRisk:F2}"
                + $" | approach {(_anyApproach ? "O" : "X")}"
                + $"  ->  {_warningLevel}");
            // "Physical Risk Level" is a fixed banding of R_static_head alone; the final
            // Passthrough state below can differ from it because UserState also shifts the
            // effective threshold (e.g. Caution-level R_static_head can still trigger ON while UserState is high).
            sbRight.AppendLine($"Physical Risk Level: {riskLevel}");
            sbRight.AppendLine();
            sbRight.AppendLine("[Passthrough Decision]");
            sbRight.AppendLine($"Effective ON Threshold: {effectiveOnThreshold:F2}");
            sbRight.AppendLine($"Effective OFF Threshold: {effectiveOffThreshold:F2}");
            sbRight.AppendLine($"Emergency Trigger: {emergencyTrigger}");
            sbRight.AppendLine($"  (needs approach > {emergencyApproachSpeed:F2} m/s)");
            sbRight.AppendLine($"Emergency Hold: {emergencyHold}");
            sbRight.AppendLine($"Adaptive Passthrough State: {(_passthroughEnabled ? "ON" : "OFF")}");
            sbRight.AppendLine($"Last Full cause: {_lastFullCause}");

            _displayText = sbLeft.ToString();
        }
        else
        {
            sbRight.AppendLine();
            sbRight.AppendLine("[Collision Risk]");
            sbRight.AppendLine("Waiting for scene data...");
            sbRight.AppendLine();
            sbRight.AppendLine("[Passthrough Decision]");
            sbRight.AppendLine("Waiting for scene data...");
        }

        _riskDisplayText = sbRight.ToString();

        if (labelText != null)
            labelText.text = _displayText;

        if (riskLabelText != null)
            riskLabelText.text = _riskDisplayText;
    }
}