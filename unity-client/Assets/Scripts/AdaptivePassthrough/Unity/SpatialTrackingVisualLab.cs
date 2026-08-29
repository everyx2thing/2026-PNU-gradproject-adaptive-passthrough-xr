using System;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    /// <summary>
    /// Quest-independent binocular lab. Two ordinary cameras render the same
    /// world-corner/SDF shader path as production into 512x512 eye textures.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpatialTrackingVisualLab : MonoBehaviour
    {
        public enum LabScenario
        {
            FrontWall,
            AngledWall,
            PersonApproach,
            PersonCrossing,
            PersonOcclusion,
            LowBox,
            Chair,
            Step
        }

        public enum LabFusionMode
        {
            Agreement,
            DepthUnavailable,
            Conflict
        }

        private static readonly int WorldBottomLeft = Shader.PropertyToID("_WorldBottomLeft");
        private static readonly int WorldBottomRight = Shader.PropertyToID("_WorldBottomRight");
        private static readonly int WorldTopRight = Shader.PropertyToID("_WorldTopRight");
        private static readonly int WorldTopLeft = Shader.PropertyToID("_WorldTopLeft");
        private static readonly int Shape = Shader.PropertyToID("_Shape");
        private static readonly int Aspect = Shader.PropertyToID("_Aspect");
        private static readonly int Feather = Shader.PropertyToID("_Feather");
        private static readonly int RevealStrength = Shader.PropertyToID("_RevealStrength");
        private static readonly int DebugColor = Shader.PropertyToID("_DebugColor");
        private static readonly int Pulse = Shader.PropertyToID("_Pulse");
        private static readonly int CueMode = Shader.PropertyToID("_CueMode");

        [SerializeField] private LabScenario scenario = LabScenario.FrontWall;
        [SerializeField] private LabFusionMode fusionMode = LabFusionMode.Agreement;
        [SerializeField, Range(0.058f, 0.072f)] private float ipd = 0.064f;
        [SerializeField, Range(0.25f, 1.50f)] private float wallDistance = 1f;
        [SerializeField, Range(-60f, 60f)] private float wallYaw;
        [SerializeField, Range(-0.75f, 0.75f)] private float hmdLateral;
        [SerializeField, Range(-45f, 45f)] private float hmdYaw;
        [SerializeField, Range(-20f, 70f)] private float hmdPitch;
        [SerializeField, Range(0f, 1f)] private float risk = 0.85f;
        [SerializeField, Range(0f, 1f)] private float confidence = 0.90f;
        [SerializeField] private bool policyEnabled = true;
        [SerializeField] private bool floorAvailable = true;
        [SerializeField] private bool playing = true;
        [SerializeField] private int depthLossMode;
        [SerializeField, Range(2f, 16f)] private float sequenceSeconds = 8f;

        private Transform hmd;
        private Camera leftCamera;
        private Camera rightCamera;
        private RenderTexture leftTexture;
        private RenderTexture rightTexture;
        private Mesh sharedQuad;
        private Material debugMaterial;
        private Material corridorMaterial;
        private Material pulseMaterial;
        private MeshRenderer hazardRenderer;
        private MeshRenderer corridorRenderer;
        private MeshRenderer pulseRenderer;
        private MaterialPropertyBlock hazardProperties;
        private MaterialPropertyBlock corridorProperties;
        private MaterialPropertyBlock pulseProperties;
        private LineRenderer corridorOutline;
        private PassthroughPresentationState state;
        private readonly SpatialObstacleFusionFilter fusionFilter =
            new SpatialObstacleFusionFilter();
        private double timelineSeconds;
        private double previousRealtime;
        private double manualRealtime;
        private bool manualClockEnabled;
        private HazardPresentationGeometry latestGeometry;
        private HazardPresentationGeometry latestCorridor;
        private Texture2D leftMetricReadback;
        private Texture2D rightMetricReadback;
        private double nextMetricReadbackAt;
        private float measuredCornerErrorPixels = -1f;
        private SpatialObstacleMeasurement latestFusionMeasurement;
        private string latestFusionConflictReason = string.Empty;

        public Camera LeftCamera => leftCamera;
        public Camera RightCamera => rightCamera;
        public RenderTexture LeftTexture => leftTexture;
        public RenderTexture RightTexture => rightTexture;
        public HazardPresentationGeometry CurrentGeometry => latestGeometry;
        public HazardPresentationGeometry CurrentCorridor => latestCorridor;
        public PassthroughAnimationPhase AnimationPhase => state == null
            ? PassthroughAnimationPhase.Hidden
            : state.Phase;
        public float CurrentIpd => ipd;
        public float CurrentPulse01 => state == null ? 0f : state.Pulse01;
        public LabFusionMode CurrentFusionMode => fusionMode;
        public SpatialObstacleMeasurement CurrentFusionMeasurement =>
            latestFusionMeasurement;
        public string CurrentFusionConflictReason =>
            latestFusionConflictReason ?? string.Empty;

        private void OnEnable()
        {
            DynamicRiskDebugOverlay overlay =
                FindAnyObjectByType<DynamicRiskDebugOverlay>();
            if (overlay != null)
            {
                overlay.enabled = false;
            }
            state = new PassthroughPresentationState();
            timelineSeconds = 0.0;
            previousRealtime = Time.realtimeSinceStartupAsDouble;
            manualRealtime = previousRealtime;
            manualClockEnabled = false;
            fusionFilter.Reset();
            EnsureLab();
        }

        private void Update()
        {
            if (manualClockEnabled)
            {
                return;
            }

            EnsureLab();
            double now = Time.realtimeSinceStartupAsDouble;
            float delta = (float)Math.Max(0.0, now - previousRealtime);
            previousRealtime = now;
            AdvanceFrame(now, delta);
        }

        private void AdvanceFrame(double now, float delta)
        {
            if (playing)
            {
                timelineSeconds += delta;
            }

            UpdateHmdPose();
            HazardPresentationGeometry measured = BuildScenarioGeometry(
                timelineSeconds,
                now);
            state.BeginFrame();
            state.SetPresentationPolicyActive(policyEnabled);
            if (policyEnabled && DepthGeometryAvailable(timelineSeconds))
            {
                state.Observe(measured, risk, now, true);
            }
            state.Update(now, delta);
            latestGeometry = state.Geometry;
            UpdateRenderedGeometry(latestGeometry, state.Opacity);
        }

        public void SetScenario(LabScenario value)
        {
            scenario = value;
            bool lowObstacle = value == LabScenario.LowBox
                || value == LabScenario.Chair
                || value == LabScenario.Step;
            hmdPitch = lowObstacle ? 60f : 0f;
            ResetTimeline();
        }

        public void SetIpd(float value)
        {
            ipd = Mathf.Clamp(value, 0.058f, 0.072f);
            UpdateHmdPose();
        }

        public void SetWall(float distanceMeters, float yawDegrees)
        {
            wallDistance = Mathf.Clamp(distanceMeters, 0.25f, 1.50f);
            wallYaw = Mathf.Clamp(yawDegrees, -60f, 60f);
        }

        public void SetHmdPose(float lateralMeters, float yawDegrees)
        {
            hmdLateral = Mathf.Clamp(lateralMeters, -0.75f, 0.75f);
            hmdYaw = Mathf.Clamp(yawDegrees, -45f, 45f);
            UpdateHmdPose();
        }

        public void SetDepthLossMode(int mode)
        {
            depthLossMode = Mathf.Clamp(mode, 0, 2);
            ResetTimeline();
        }

        public void SetFusionMode(LabFusionMode value)
        {
            fusionMode = value;
            ResetTimeline();
        }

        public void SetPlayback(bool value)
        {
            playing = value;
        }

        public void SetPolicyEnabled(bool value)
        {
            policyEnabled = value;
            state?.SetPresentationPolicyActive(value);
        }

        public void SetFloorAvailable(bool value)
        {
            floorAvailable = value;
        }

        public void Step(float seconds = 1f / 30f)
        {
            timelineSeconds += Mathf.Max(0f, seconds);
        }

        public void EnableManualClock(double initialRealtimeSeconds = 1.0)
        {
            manualClockEnabled = true;
            manualRealtime = Math.Max(0.001, initialRealtimeSeconds);
            previousRealtime = manualRealtime;
            timelineSeconds = 0.0;
            state?.Reset();
            EnsureLab();
        }

        public void AdvancePresentation(float seconds)
        {
            if (!manualClockEnabled)
            {
                EnableManualClock();
            }

            float delta = Mathf.Max(0f, seconds);
            manualRealtime += delta;
            AdvanceFrame(manualRealtime, delta);
        }

        public void ResetTimeline()
        {
            timelineSeconds = 0.0;
            state?.Reset();
            fusionFilter.Reset();
            latestFusionMeasurement = SpatialObstacleMeasurement.Unavailable(
                manualClockEnabled
                    ? manualRealtime
                    : Time.realtimeSinceStartupAsDouble);
            latestFusionConflictReason = string.Empty;
        }

        private void EnsureLab()
        {
            if (hmd == null)
            {
                var rig = new GameObject("Presentation Lab Virtual HMD");
                rig.transform.SetParent(transform, false);
                hmd = rig.transform;
                hmd.localPosition = new Vector3(0f, 1.60f, 0f);
                leftCamera = CreateEyeCamera("Lab Left Eye", hmd, -1f);
                rightCamera = CreateEyeCamera("Lab Right Eye", hmd, 1f);
            }

            if (sharedQuad != null)
            {
                return;
            }

            sharedQuad = CreateQuad();
            Shader shader = Shader.Find(
                "TeamVR/AdaptivePassthrough/HazardPresentationDebug");
            debugMaterial = new Material(shader)
            {
                name = "Presentation Lab Hazard Material",
                hideFlags = HideFlags.DontSave
            };
            Shader cueShader = Shader.Find(
                "TeamVR/AdaptivePassthrough/HazardCue");
            corridorMaterial = new Material(cueShader)
            {
                name = "Presentation Lab Corridor Material",
                hideFlags = HideFlags.DontSave
            };
            corridorMaterial.SetFloat("_CueMode", 1f);
            corridorMaterial.SetFloat("_Pulse", 0f);
            pulseMaterial = new Material(cueShader)
            {
                name = "Presentation Lab Warning Pulse Material",
                hideFlags = HideFlags.DontSave
            };
            pulseMaterial.SetFloat("_CueMode", 0f);
            hazardRenderer = CreateRenderer("Presentation Lab Hazard Mask", debugMaterial);
            corridorRenderer = CreateRenderer("Presentation Lab Floor Corridor", corridorMaterial);
            pulseRenderer = CreateRenderer("Presentation Lab Warning Pulse", pulseMaterial);
            hazardProperties = new MaterialPropertyBlock();
            corridorProperties = new MaterialPropertyBlock();
            pulseProperties = new MaterialPropertyBlock();
            corridorOutline = CreateOutline(
                "Expected Corridor Outline",
                new Color(1f, 0.85f, 0.05f, 1f));
        }

        private Camera CreateEyeCamera(string objectName, Transform parent, float side)
        {
            var eye = new GameObject(objectName);
            eye.transform.SetParent(parent, false);
            Camera camera = eye.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.012f, 0.018f, 0.035f);
            camera.fieldOfView = 70f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 20f;
            var texture = new RenderTexture(512, 512, 24)
            {
                name = objectName + " 512x512",
                hideFlags = HideFlags.DontSave
            };
            texture.Create();
            camera.targetTexture = texture;
            if (side < 0f) leftTexture = texture;
            else rightTexture = texture;
            return camera;
        }

        private void UpdateHmdPose()
        {
            if (hmd == null) return;
            hmd.localPosition = new Vector3(hmdLateral, 1.60f, 0f);
            hmd.localRotation = Quaternion.Euler(hmdPitch, hmdYaw, 0f);
            if (leftCamera != null)
                leftCamera.transform.localPosition = Vector3.left * ipd * 0.5f;
            if (rightCamera != null)
                rightCamera.transform.localPosition = Vector3.right * ipd * 0.5f;
        }

        private HazardPresentationGeometry BuildScenarioGeometry(
            double time,
            double observationTime)
        {
            float phase = sequenceSeconds <= 0f
                ? 0f
                : (float)(time % sequenceSeconds) / sequenceSeconds;
            switch (scenario)
            {
                case LabScenario.AngledWall:
                    return BuildWall(
                        wallDistance,
                        wallYaw == 0f ? 38f : wallYaw,
                        observationTime);
                case LabScenario.PersonApproach:
                    return BuildPerson(
                        Mathf.Lerp(3f, 0.60f, PingPong01(phase)),
                        0f,
                        observationTime);
                case LabScenario.PersonCrossing:
                    return BuildPerson(
                        1.5f,
                        Mathf.Lerp(-0.85f, 0.85f, PingPong01(phase)),
                        observationTime,
                        new Vector3(0.42f, 0f, 0f));
                case LabScenario.PersonOcclusion:
                    return BuildPerson(1.25f, 0f, observationTime);
                case LabScenario.LowBox:
                    return BuildLowObstacle(
                        0.28f, 0.70f, 0.55f, observationTime);
                case LabScenario.Chair:
                    return BuildLowObstacle(
                        0.48f, 0.85f, 0.75f, observationTime);
                case LabScenario.Step:
                    return BuildLowObstacle(
                        0.16f, 1.10f, 1.10f, observationTime);
                default:
                    return BuildWall(wallDistance, wallYaw, observationTime);
            }
        }

        private HazardPresentationGeometry BuildWall(
            float distanceMeters,
            float yawDegrees,
            double observationTime)
        {
            Vector3 center = new Vector3(0f, 1.55f, distanceMeters);
            Vector3 normal = Quaternion.Euler(0f, yawDegrees, 0f) * -Vector3.forward;
            return BuildSpatialFusionGeometry(
                10,
                HazardVisualKind.WallPlane,
                center,
                normal,
                Mathf.Clamp(distanceMeters * 0.62f, 0.22f, 1.35f),
                Mathf.Clamp(distanceMeters * 0.85f, 0.30f, 1.65f),
                distanceMeters,
                SpatialProbePurpose.Standard,
                observationTime,
                false,
                0f);
        }

        private HazardPresentationGeometry BuildPerson(
            float distanceMeters,
            float lateral,
            double observationTime,
            Vector3 velocity = default)
        {
            Vector3 center = new Vector3(lateral, 1.38f, distanceMeters);
            HazardPresentationGeometry plane = HazardPresentationGeometry.CreatePlanePatch(
                20, HazardVisualKind.PersonCapsule, center, -Vector3.forward,
                Vector3.up,
                0.58f * HazardPresentationGeometry.DefaultPaddingScale,
                1.48f * HazardPresentationGeometry.UpperBodyFraction
                    * HazardPresentationGeometry.DefaultPaddingScale,
                observationTime, confidence,
                SpatialObstacleSource.EnvironmentDepth, SpatialProbeOwner.Head,
                SpatialProbePurpose.Standard, risk);
            if (velocity.sqrMagnitude > 0f)
            {
                plane = plane.WithVelocity(velocity);
            }

            SpatialObstacleMeasurement environment =
                fusionMode == LabFusionMode.DepthUnavailable
                    ? SpatialObstacleMeasurement.Unavailable(
                        observationTime,
                        SpatialProbeOwner.Head,
                        SpatialProbePurpose.Standard)
                    : CreateMockMeasurement(
                        SpatialObstacleSource.EnvironmentDepth,
                        distanceMeters,
                        plane,
                        observationTime);
            SpatialObstacleMeasurement room =
                SpatialObstacleMeasurement.Unavailable(
                    observationTime,
                    SpatialProbeOwner.Head,
                    SpatialProbePurpose.Standard);
            latestFusionMeasurement = fusionFilter.Fuse(
                SpatialProbeOwner.Head,
                environment,
                room,
                observationTime);
            latestFusionConflictReason = environment.Available
                ? "person_room_scene_not_applicable"
                : "person_depth_unavailable";
            return latestFusionMeasurement.PresentationGeometry;
        }

        private HazardPresentationGeometry BuildLowObstacle(
            float height,
            float distanceMeters,
            float width,
            double observationTime)
        {
            return BuildSpatialFusionGeometry(
                30 + (int)scenario,
                HazardVisualKind.LowObstaclePatch,
                new Vector3(0f, height * 0.65f, distanceMeters),
                -Vector3.forward,
                width * HazardPresentationGeometry.DefaultPaddingScale,
                height * HazardPresentationGeometry.DefaultPaddingScale,
                distanceMeters,
                SpatialProbePurpose.LocomotionCorridor,
                observationTime,
                floorAvailable,
                0f);
        }

        private HazardPresentationGeometry BuildSpatialFusionGeometry(
            long stableId,
            HazardVisualKind kind,
            Vector3 roomCenter,
            Vector3 roomNormal,
            float width,
            float height,
            float roomDistanceMeters,
            SpatialProbePurpose purpose,
            double observationTime,
            bool hasFreshFloor,
            float floorHeight)
        {
            float roomWidth = Mathf.Max(0.02f, width * 0.90f);
            float roomHeight = Mathf.Max(0.02f, height * 0.90f);
            HazardPresentationGeometry roomGeometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    stableId + 1000,
                    kind,
                    roomCenter,
                    roomNormal,
                    Vector3.up,
                    roomWidth,
                    roomHeight,
                    observationTime,
                    Mathf.Clamp01(confidence * 0.94f),
                    SpatialObstacleSource.RoomScene,
                    SpatialProbeOwner.Head,
                    purpose,
                    risk,
                    hasFreshFloor,
                    floorHeight);
            SpatialObstacleMeasurement room = CreateMockMeasurement(
                SpatialObstacleSource.RoomScene,
                roomDistanceMeters,
                roomGeometry,
                observationTime);

            SpatialObstacleMeasurement environment;
            switch (fusionMode)
            {
                case LabFusionMode.DepthUnavailable:
                    environment = SpatialObstacleMeasurement.Unavailable(
                        observationTime,
                        SpatialProbeOwner.Head,
                        purpose);
                    latestFusionConflictReason =
                        "depth_unavailable_room_fallback";
                    break;
                case LabFusionMode.Conflict:
                    Vector3 conflictNormal = Quaternion.AngleAxis(
                        90f,
                        Vector3.up) * roomNormal;
                    HazardPresentationGeometry conflictGeometry =
                        HazardPresentationGeometry.CreatePlanePatch(
                            stableId,
                            kind,
                            roomCenter - roomNormal * 0.10f,
                            conflictNormal,
                            Vector3.up,
                            width,
                            height,
                            observationTime,
                            confidence,
                            SpatialObstacleSource.EnvironmentDepth,
                            SpatialProbeOwner.Head,
                            purpose,
                            risk,
                            hasFreshFloor,
                            floorHeight);
                    environment = CreateMockMeasurement(
                        SpatialObstacleSource.EnvironmentDepth,
                        roomDistanceMeters + 0.10f,
                        conflictGeometry,
                        observationTime);
                    latestFusionConflictReason = "normal_mismatch";
                    break;
                default:
                    HazardPresentationGeometry environmentGeometry =
                        HazardPresentationGeometry.CreatePlanePatch(
                            stableId,
                            kind,
                            roomCenter + roomNormal * 0.08f,
                            roomNormal,
                            Vector3.up,
                            width,
                            height,
                            observationTime,
                            confidence,
                            SpatialObstacleSource.EnvironmentDepth,
                            SpatialProbeOwner.Head,
                            purpose,
                            risk,
                            hasFreshFloor,
                            floorHeight);
                    environment = CreateMockMeasurement(
                        SpatialObstacleSource.EnvironmentDepth,
                        Mathf.Max(0.01f, roomDistanceMeters - 0.08f),
                        environmentGeometry,
                        observationTime);
                    latestFusionConflictReason = string.Empty;
                    break;
            }

            latestFusionMeasurement = fusionFilter.Fuse(
                SpatialProbeOwner.Head,
                environment,
                room,
                observationTime);
            if (fusionMode == LabFusionMode.Agreement
                && latestFusionMeasurement.Source
                    != SpatialObstacleSource.Fused)
            {
                latestFusionConflictReason =
                    "unexpected_spatial_incompatibility";
            }
            return latestFusionMeasurement.PresentationGeometry;
        }

        private SpatialObstacleMeasurement CreateMockMeasurement(
            SpatialObstacleSource source,
            float distanceMeters,
            HazardPresentationGeometry geometry,
            double observationTime)
        {
            return new SpatialObstacleMeasurement(
                source,
                observationTime,
                geometry.Available,
                distanceMeters,
                geometry.Center,
                geometry.SurfaceNormal,
                0f,
                geometry.Confidence,
                5,
                0.015f,
                0f,
                false,
                false,
                5,
                SpatialProbeOwner.Head,
                source,
                source == SpatialObstacleSource.EnvironmentDepth
                    ? distanceMeters
                    : -1f,
                source == SpatialObstacleSource.RoomScene
                    ? distanceMeters
                    : -1f,
                false,
                string.Empty,
                geometry.ProbePurpose,
                (int)geometry.StableId,
                geometry);
        }

        private bool DepthGeometryAvailable(double time)
        {
            float local = (float)(time % Math.Max(0.1f, sequenceSeconds));
            if (scenario == LabScenario.PersonOcclusion && local >= 3f && local < 3.75f)
                return false;
            if (depthLossMode == 1)
                return local < 2f || local >= 2.5f;
            return depthLossMode != 2 || local < 2f;
        }

        private void UpdateRenderedGeometry(
            HazardPresentationGeometry geometry,
            float opacity)
        {
            bool visible = geometry.Available && opacity > 0f;
            hazardRenderer.enabled = visible;
            if (!visible)
            {
                corridorRenderer.enabled = false;
                pulseRenderer.enabled = false;
                corridorOutline.enabled = false;
                latestCorridor = default;
                return;
            }

            ApplyGeometry(
                hazardProperties, hazardRenderer, geometry, opacity,
                new Color(1f, 0.04f, 0.02f, 0.62f));
            float pulse = state == null ? 0f : state.Pulse01;
            pulseRenderer.enabled = pulse > 0f;
            if (pulseRenderer.enabled)
            {
                ApplyGeometry(
                    pulseProperties,
                    pulseRenderer,
                    geometry,
                    opacity,
                    Color.red);
                pulseProperties.SetFloat(Pulse, pulse);
                pulseProperties.SetFloat(CueMode, 0f);
                pulseRenderer.SetPropertyBlock(pulseProperties);
            }
            bool showCorridor = geometry.Kind == HazardVisualKind.LowObstaclePatch
                && geometry.HasFreshFloor;
            if (!showCorridor)
            {
                corridorRenderer.enabled = false;
                corridorOutline.enabled = false;
                latestCorridor = default;
                return;
            }

            Vector3 forward = Vector3.ProjectOnPlane(hmd.forward, Vector3.up);
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            Vector3 near = hmd.position + forward * 0.30f;
            near.y = 0.015f;
            Vector3 far = geometry.Center;
            far.y = 0.015f;
            latestCorridor = HazardPresentationGeometry.CreateFloorCorridor(
                geometry.StableId, near, far, Vector3.up, 0.20f, 0.45f,
                Time.realtimeSinceStartupAsDouble, confidence, risk, geometry.Source);
            corridorRenderer.enabled = true;
            corridorOutline.enabled = true;
            ApplyGeometry(
                corridorProperties, corridorRenderer, latestCorridor, opacity,
                new Color(1f, 0.18f, 0.01f, 0.30f));
            SetOutline(corridorOutline, latestCorridor);
        }

        private static void ApplyGeometry(
            MaterialPropertyBlock properties,
            MeshRenderer renderer,
            HazardPresentationGeometry geometry,
            float opacity,
            Color color)
        {
            properties.Clear();
            properties.SetVector(WorldBottomLeft, geometry.BottomLeft);
            properties.SetVector(WorldBottomRight, geometry.BottomRight);
            properties.SetVector(WorldTopRight, geometry.TopRight);
            properties.SetVector(WorldTopLeft, geometry.TopLeft);
            properties.SetFloat(
                Shape,
                geometry.Kind == HazardVisualKind.PersonCapsule ? 1f : 0f);
            properties.SetFloat(Aspect, geometry.Width / Mathf.Max(0.001f, geometry.Height));
            properties.SetFloat(Feather, 0.065f);
            properties.SetFloat(RevealStrength, opacity);
            properties.SetColor(DebugColor, color);
            renderer.SetPropertyBlock(properties);
        }

        private MeshRenderer CreateRenderer(string name, Material material)
        {
            var target = new GameObject(name);
            target.transform.SetParent(transform, false);
            MeshFilter filter = target.AddComponent<MeshFilter>();
            MeshRenderer renderer = target.AddComponent<MeshRenderer>();
            filter.sharedMesh = sharedQuad;
            renderer.sharedMaterial = material;
            return renderer;
        }

        private LineRenderer CreateOutline(string name, Color color)
        {
            var target = new GameObject(name);
            target.transform.SetParent(transform, false);
            LineRenderer line = target.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = 4;
            line.widthMultiplier = 0.002f;
            line.material = new Material(
                Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"))
            {
                color = color,
                hideFlags = HideFlags.DontSave
            };
            line.material.renderQueue = 4998;
            line.startColor = color;
            line.endColor = color;
            return line;
        }

        private static void SetOutline(
            LineRenderer outline,
            HazardPresentationGeometry geometry)
        {
            outline.SetPosition(0, geometry.BottomLeft);
            outline.SetPosition(1, geometry.BottomRight);
            outline.SetPosition(2, geometry.TopRight);
            outline.SetPosition(3, geometry.TopLeft);
        }

        private static Mesh CreateQuad()
        {
            var mesh = new Mesh
            {
                name = "Presentation Lab World Corner Quad",
                hideFlags = HideFlags.DontSave
            };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.one, Vector3.up };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            return mesh;
        }

        private void OnGUI()
        {
            if (leftTexture == null || rightTexture == null) return;
            if (Event.current.type == EventType.Repaint
                && Time.realtimeSinceStartupAsDouble >= nextMetricReadbackAt)
            {
                measuredCornerErrorPixels = MeasureCornerErrorPixels();
                nextMetricReadbackAt =
                    Time.realtimeSinceStartupAsDouble + 0.25;
            }
            float width = Mathf.Min(Screen.width * 0.48f, 512f);
            GUI.DrawTexture(new Rect(8f, 8f, width, width), leftTexture, ScaleMode.ScaleToFit, false);
            GUI.DrawTexture(new Rect(16f + width, 8f, width, width), rightTexture, ScaleMode.ScaleToFit, false);
            GUI.Box(new Rect(8f, 8f, 92f, 24f), "LEFT EYE");
            GUI.Box(new Rect(16f + width, 8f, 98f, 24f), "RIGHT EYE");
            if (!DepthGeometryAvailable(timelineSeconds)
                && !latestGeometry.Available)
            {
                DrawStereoFallbackOverlay();
            }

            GUILayout.BeginArea(new Rect(8f, width + 20f, Screen.width - 16f, 300f), GUI.skin.box);
            GUILayout.Label(
                $"PRESENTATION LAB  Scenario={scenario} Phase={AnimationPhase} "
                + $"Opacity={(state == null ? 0f : state.Opacity):F2} IPD={ipd:F3}m "
                + $"disparity={CenterDisparityPixels():F1}px  "
                + $"corner error={(measuredCornerErrorPixels < 0f ? "n/a" : measuredCornerErrorPixels.ToString("F1"))}px "
                + "padding=12.5% feather=6.5%");
            GUILayout.Label(
                $"FUSION={fusionMode}  Depth={FormatDistance(latestFusionMeasurement.EnvironmentDistanceMeters)} "
                + $"Room={FormatDistance(latestFusionMeasurement.RoomSceneDistanceMeters)} "
                + $"published={latestFusionMeasurement.Source} "
                + $"selected={latestFusionMeasurement.SelectedSource} "
                + $"conflict={(string.IsNullOrEmpty(latestFusionConflictReason) ? "none" : latestFusionConflictReason)}");
            GUILayout.BeginHorizontal();
            DrawScenarioButton("WALL FRONT", LabScenario.FrontWall);
            DrawScenarioButton("WALL ANGLED", LabScenario.AngledWall);
            DrawScenarioButton("PERSON APPROACH", LabScenario.PersonApproach);
            DrawScenarioButton("PERSON CROSS", LabScenario.PersonCrossing);
            DrawScenarioButton("PERSON OCCLUDE", LabScenario.PersonOcclusion);
            DrawScenarioButton("LOW BOX", LabScenario.LowBox);
            DrawScenarioButton("CHAIR", LabScenario.Chair);
            DrawScenarioButton("STEP", LabScenario.Step);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Fusion:", GUILayout.Width(55f));
            DrawFusionModeButton("AGREE", LabFusionMode.Agreement);
            DrawFusionModeButton("DEPTH OFF", LabFusionMode.DepthUnavailable);
            DrawFusionModeButton("CONFLICT", LabFusionMode.Conflict);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0.25m")) wallDistance = 0.25f;
            if (GUILayout.Button("0.6m")) wallDistance = 0.60f;
            if (GUILayout.Button("1.0m")) wallDistance = 1.00f;
            if (GUILayout.Button("1.5m")) wallDistance = 1.50f;
            if (GUILayout.Button(playing ? "PAUSE" : "PLAY")) playing = !playing;
            if (GUILayout.Button("FRAME")) Step();
            if (GUILayout.Button("RESET")) ResetTimeline();
            if (GUILayout.Button(policyEnabled ? "POLICY ON" : "POLICY OFF"))
                SetPolicyEnabled(!policyEnabled);
            if (GUILayout.Button(floorAvailable ? "FLOOR FRESH" : "FLOOR STALE")) floorAvailable = !floorAvailable;
            GUILayout.EndHorizontal();
            GUILayout.Label($"Wall yaw {wallYaw:F0}°");
            wallYaw = GUILayout.HorizontalSlider(wallYaw, -60f, 60f);
            GUILayout.Label($"IPD {ipd:F3}m / HMD lateral {hmdLateral:F2}m / HMD yaw {hmdYaw:F0}° / pitch {hmdPitch:F0}°");
            ipd = GUILayout.HorizontalSlider(ipd, 0.058f, 0.072f);
            hmdLateral = GUILayout.HorizontalSlider(hmdLateral, -0.75f, 0.75f);
            hmdYaw = GUILayout.HorizontalSlider(hmdYaw, -45f, 45f);
            hmdPitch = GUILayout.HorizontalSlider(hmdPitch, -20f, 70f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Stream:", GUILayout.Width(55f));
            if (GUILayout.Button("NORMAL")) SetDepthLossMode(0);
            if (GUILayout.Button("0.5s LOSS")) SetDepthLossMode(1);
            if (GUILayout.Button("LONG LOSS")) SetDepthLossMode(2);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private static void DrawStereoFallbackOverlay()
        {
            Color previous = GUI.color;
            GUI.color = new Color(1f, 0.02f, 0.01f, 0.92f);
            const float thickness = 8f;
            GUI.DrawTexture(
                new Rect(0f, 0f, Screen.width, thickness),
                Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(0f, Screen.height - thickness, Screen.width, thickness),
                Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(0f, 0f, thickness, Screen.height),
                Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(Screen.width - thickness, 0f, thickness, Screen.height),
                Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Box(
                new Rect(Screen.width * 0.5f - 145f, 38f, 290f, 28f),
                "GEOMETRY EXPIRED — BORDER + HAPTICS");
        }

        private void DrawScenarioButton(string label, LabScenario value)
        {
            if (GUILayout.Button(label)) SetScenario(value);
        }

        private void DrawFusionModeButton(string label, LabFusionMode value)
        {
            if (GUILayout.Button(label)) SetFusionMode(value);
        }

        private static string FormatDistance(float distanceMeters)
        {
            return distanceMeters >= 0f
                ? distanceMeters.ToString("F2") + "m"
                : "n/a";
        }

        private float CenterDisparityPixels()
        {
            if (!latestGeometry.Available || leftCamera == null || rightCamera == null)
                return 0f;
            Vector3 left = leftCamera.WorldToViewportPoint(latestGeometry.Center);
            Vector3 right = rightCamera.WorldToViewportPoint(latestGeometry.Center);
            return Mathf.Abs(left.x - right.x) * 512f;
        }

        private float MeasureCornerErrorPixels()
        {
            if (!latestGeometry.Available)
            {
                return -1f;
            }

            leftCamera.Render();
            rightCamera.Render();
            float leftError = MeasureEyeCornerError(
                leftCamera,
                leftTexture,
                ref leftMetricReadback);
            float rightError = MeasureEyeCornerError(
                rightCamera,
                rightTexture,
                ref rightMetricReadback);
            return leftError < 0f || rightError < 0f
                ? -1f
                : Mathf.Max(leftError, rightError);
        }

        private float MeasureEyeCornerError(
            Camera camera,
            RenderTexture texture,
            ref Texture2D readback)
        {
            if (readback == null)
            {
                readback = new Texture2D(
                    texture.width,
                    texture.height,
                    TextureFormat.RGBA32,
                    false)
                {
                    hideFlags = HideFlags.DontSave
                };
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            readback.ReadPixels(
                new Rect(0f, 0f, texture.width, texture.height),
                0,
                0,
                false);
            readback.Apply(false, false);
            RenderTexture.active = previous;

            Color32[] pixels = readback.GetPixels32();
            Vector2 expectedBottomLeft = ProjectPixel(
                camera,
                latestGeometry.BottomLeft,
                texture);
            Vector2 expectedBottomRight = ProjectPixel(
                camera,
                latestGeometry.BottomRight,
                texture);
            Vector2 expectedTopRight = ProjectPixel(
                camera,
                latestGeometry.TopRight,
                texture);
            Vector2 expectedTopLeft = ProjectPixel(
                camera,
                latestGeometry.TopLeft,
                texture);
            float bottomLeftDistanceSq = float.PositiveInfinity;
            float bottomRightDistanceSq = float.PositiveInfinity;
            float topRightDistanceSq = float.PositiveInfinity;
            float topLeftDistanceSq = float.PositiveInfinity;
            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    Color32 color = pixels[y * texture.width + x];
                    if (color.g <= 80 || color.b <= 80 || color.r >= 120)
                    {
                        continue;
                    }

                    var pixel = new Vector2(x, y);
                    bottomLeftDistanceSq = Mathf.Min(
                        bottomLeftDistanceSq,
                        (pixel - expectedBottomLeft).sqrMagnitude);
                    bottomRightDistanceSq = Mathf.Min(
                        bottomRightDistanceSq,
                        (pixel - expectedBottomRight).sqrMagnitude);
                    topRightDistanceSq = Mathf.Min(
                        topRightDistanceSq,
                        (pixel - expectedTopRight).sqrMagnitude);
                    topLeftDistanceSq = Mathf.Min(
                        topLeftDistanceSq,
                        (pixel - expectedTopLeft).sqrMagnitude);
                }
            }

            if (float.IsInfinity(bottomLeftDistanceSq)
                || float.IsInfinity(bottomRightDistanceSq)
                || float.IsInfinity(topRightDistanceSq)
                || float.IsInfinity(topLeftDistanceSq))
            {
                return -1f;
            }

            return Mathf.Sqrt(Mathf.Max(
                Mathf.Max(bottomLeftDistanceSq, bottomRightDistanceSq),
                Mathf.Max(topRightDistanceSq, topLeftDistanceSq)));
        }

        private static Vector2 ProjectPixel(
            Camera camera,
            Vector3 world,
            RenderTexture texture)
        {
            Vector3 viewport = camera.WorldToViewportPoint(world);
            return new Vector2(
                viewport.x * texture.width,
                viewport.y * texture.height);
        }

        private static float PingPong01(float value)
        {
            return 1f - Mathf.Abs(value * 2f - 1f);
        }

        private void OnDestroy()
        {
            ReleaseTexture(leftTexture);
            ReleaseTexture(rightTexture);
            DestroyRuntime(sharedQuad);
            DestroyRuntime(debugMaterial);
            DestroyRuntime(corridorMaterial);
            DestroyRuntime(pulseMaterial);
            DestroyRuntime(leftMetricReadback);
            DestroyRuntime(rightMetricReadback);
        }

        private static void ReleaseTexture(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            DestroyRuntime(texture);
        }

        private static void DestroyRuntime(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
