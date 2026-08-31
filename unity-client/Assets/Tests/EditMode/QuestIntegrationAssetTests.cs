using Meta.XR;
using Meta.XR.EnvironmentDepth;
using NUnit.Framework;
using System.IO;
using System.Reflection;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class QuestIntegrationAssetTests
    {
        private const string ModelPath = "Assets/Models/yolov9sentis.sentis";
        private const string PersonalizationSourceModelPath =
            "Assets/Models/rf_personalization_real.onnx.source";
        private const string QuestScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SafetyAlertShaderPath =
            "Assets/Shaders/AdaptivePassthrough/SafetyAlertBorder.shader";
        private const string HazardCueShaderPath =
            "Assets/Shaders/AdaptivePassthrough/HazardCue.shader";

        [Test]
        public void PersonModelImportsWithExpectedThreeOutputContract()
        {
            ModelAsset asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
            Assert.That(asset, Is.Not.Null, "The Quest person detector model must be imported.");

            Model model = ModelLoader.Load(asset);
            Assert.That(model, Is.Not.Null);
            Assert.That(model.inputs.Count, Is.EqualTo(1));
            Assert.That(model.inputs[0].shape.ToIntArray().Length, Is.EqualTo(4));
            Assert.That(model.outputs.Count, Is.EqualTo(3));
        }

        [Test]
        public void QuestSceneContainsCameraDetectorAndRiskController()
        {
            Scene scene = EditorSceneManager.OpenScene(QuestScenePath, OpenSceneMode.Additive);
            try
            {
                DynamicRiskController controller = FindInScene<DynamicRiskController>(scene);
                QuestPersonDetectionRunner runner = FindInScene<QuestPersonDetectionRunner>(scene);
                PassthroughCameraAccess cameraAccess = FindInScene<PassthroughCameraAccess>(scene);
                QuestPersonDepthProvider depthProvider =
                    FindInScene<QuestPersonDepthProvider>(scene);
                EnvironmentDepthManager environmentDepth =
                    FindInScene<EnvironmentDepthManager>(scene);
                EnvironmentRaycastManager environmentRaycast =
                    FindInScene<EnvironmentRaycastManager>(scene);
                QuestCameraPermissionCoordinator permission =
                    FindInScene<QuestCameraPermissionCoordinator>(scene);

                Assert.That(controller, Is.Not.Null);
                Assert.That(runner, Is.Not.Null);
                Assert.That(cameraAccess, Is.Not.Null);
                Assert.That(depthProvider, Is.Not.Null);
                Assert.That(environmentDepth, Is.Not.Null);
                Assert.That(environmentRaycast, Is.Not.Null);
                Assert.That(permission, Is.Not.Null);

                var serializedRunner = new SerializedObject(runner);
                Assert.That(
                    serializedRunner.FindProperty("modelAsset").objectReferenceValue,
                    Is.Not.Null);
                Assert.That(
                    serializedRunner.FindProperty("cameraAccess").objectReferenceValue,
                    Is.SameAs(cameraAccess));
                Assert.That(
                    serializedRunner.FindProperty("depthProvider").objectReferenceValue,
                    Is.SameAs(depthProvider));
                Assert.That(
                    serializedRunner.FindProperty("controller").objectReferenceValue,
                    Is.SameAs(controller));
                Assert.That(
                    serializedRunner.FindProperty("confidenceThreshold").floatValue,
                    Is.EqualTo(0.55f).Within(0.0001f));
                Assert.That(
                    serializedRunner
                        .FindProperty("trackingConfidenceThreshold")
                        .floatValue,
                    Is.EqualTo(0.35f).Within(0.0001f));
                Assert.That(
                    serializedRunner.FindProperty("boxesAreNormalized").boolValue,
                    Is.False);
                Assert.That(
                    serializedRunner
                        .FindProperty("benchmarkCpuGpuOnQuest")
                        .boolValue,
                    Is.False);
                Assert.That(
                    serializedRunner.FindProperty("backend").intValue,
                    Is.EqualTo((int)BackendType.CPU));
                Assert.That(
                    serializedRunner.FindProperty("inferenceRateHz").floatValue,
                    Is.EqualTo(3f).Within(0.0001f));
                Assert.That(environmentDepth.RemoveHands, Is.True);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void QuestInferenceUsesLayeredSchedulingAndPauseRecovery()
        {
            string source = File.ReadAllText(
                Path.Combine(
                    Application.dataPath,
                    "Scripts/AdaptivePassthrough/Quest/QuestPersonDetectionRunner.cs"));

            Assert.That(source, Does.Contain("ScheduleIterable(activeInput)"));
            Assert.That(source, Does.Contain("AdvanceInferenceSchedule"));
            Assert.That(source, Does.Contain("Stopwatch.GetTimestamp()"));
            Assert.That(source, Does.Contain("InferenceSchedulerPolicy"));
            Assert.That(source, Does.Contain("AbortStalledInference"));
            Assert.That(source, Does.Contain("CancelInferenceAndWorker"));
            Assert.That(source, Does.Contain("OnApplicationPause"));
            Assert.That(source, Does.Not.Contain("worker.Schedule(input)"));
        }

        [Test]
        public void QuestSceneUsesIndependentHudAndNonOverlappingDebugMode()
        {
            Scene scene = EditorSceneManager.OpenScene(QuestScenePath, OpenSceneMode.Additive);
            try
            {
                DynamicRiskDebugOverlay overlay =
                    FindInScene<DynamicRiskDebugOverlay>(scene);
                Assert.That(overlay, Is.Not.Null);

                var serializedOverlay = new SerializedObject(overlay);
                Assert.That(
                    serializedOverlay.FindProperty("drawPanelBackground").boolValue,
                    Is.False);
                Assert.That(
                    serializedOverlay.FindProperty("showHeader").boolValue,
                    Is.False);
                Assert.That(
                    serializedOverlay.FindProperty("developmentBuildOnly").boolValue,
                    Is.True);

                int independentHudCount = 0;
                int legacyHudCount = 0;
                GameObject legacyDistance = null;
                GameObject legacyRisk = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    MonoBehaviour[] behaviours =
                        root.GetComponentsInChildren<MonoBehaviour>(true);
                    for (int i = 0; i < behaviours.Length; i++)
                    {
                        if (behaviours[i] != null
                            && behaviours[i].GetType().Name
                                == "IndependentPassthroughHud")
                        {
                            independentHudCount++;
                        }
                        else if (behaviours[i] != null
                            && behaviours[i].GetType().Name
                                == "QuestRiskHud")
                        {
                            legacyHudCount++;
                        }
                    }

                    Transform distance = FindChild(root.transform, "DistanceText");
                    Transform risk = FindChild(root.transform, "RiskText");
                    if (distance != null) legacyDistance = distance.gameObject;
                    if (risk != null) legacyRisk = risk.gameObject;
                }

                Assert.That(independentHudCount, Is.EqualTo(1));
                Assert.That(legacyHudCount, Is.Zero);
                Assert.That(legacyDistance, Is.Not.Null);
                Assert.That(legacyRisk, Is.Not.Null);
                Assert.That(legacyDistance.activeSelf, Is.False);
                Assert.That(legacyRisk.activeSelf, Is.False);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void QuestSceneUsesIndependentStaticAndDynamicPassthrough()
        {
            Scene scene = EditorSceneManager.OpenScene(
                QuestScenePath,
                OpenSceneMode.Additive);
            try
            {
                MonoBehaviour staticPolicy = null;
                MonoBehaviour dynamicPolicy = null;
                MonoBehaviour presentation = null;
                MonoBehaviour hud = null;
                MonoBehaviour togglePanel = null;
                MonoBehaviour boundaryVisibility = null;
                MonoBehaviour passthroughLayer = null;
                MonoBehaviour inputModule = null;
                MonoBehaviour ovrRaycaster = null;
                MonoBehaviour controllerLaser = null;
                MonoBehaviour rightControllerHelper = null;
                MonoBehaviour personalization = null;
                MonoBehaviour personalizationPanel = null;
                MonoBehaviour spatialProvider = null;
                MonoBehaviour trackingQuality = null;
                MonoBehaviour panelPlacement = null;
                MonoBehaviour alertFeedback = null;
                int legacySnapshotCount = 0;
                int passthroughLayerCount = 0;
                int controllerLaserCount = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    MonoBehaviour[] behaviours =
                        root.GetComponentsInChildren<MonoBehaviour>(true);
                    for (int i = 0; i < behaviours.Length; i++)
                    {
                        MonoBehaviour behaviour = behaviours[i];
                        if (behaviour == null)
                        {
                            continue;
                        }

                        switch (behaviour.GetType().Name)
                        {
                            case "StaticPassthroughPolicyController":
                                staticPolicy = behaviour;
                                break;
                            case "DynamicPassthroughPolicyController":
                                dynamicPolicy = behaviour;
                                break;
                            case "SelectivePassthroughController":
                                presentation = behaviour;
                                break;
                            case "IndependentPassthroughHud":
                                hud = behaviour;
                                break;
                            case "PassthroughFeatureTogglePanel":
                                togglePanel = behaviour;
                                break;
                            case "BoundaryVisibilityController":
                                boundaryVisibility = behaviour;
                                break;
                            case "PersonalizationRuntimeController":
                                personalization = behaviour;
                                break;
                            case "PersonalizationRuntimePanel":
                                personalizationPanel = behaviour;
                                break;
                            case "QuestSpatialObstacleProvider":
                                spatialProvider = behaviour;
                                break;
                            case "TrackingQualityController":
                                trackingQuality = behaviour;
                                break;
                            case "WorldSpacePanelPlacementController":
                                panelPlacement = behaviour;
                                break;
                            case "SafetyAlertFeedbackController":
                                alertFeedback = behaviour;
                                break;
                            case "OVRPassthroughLayer":
                                passthroughLayer = behaviour;
                                passthroughLayerCount++;
                                break;
                            case "OVRInputModule":
                                inputModule = behaviour;
                                break;
                            case "OVRRaycaster":
                                ovrRaycaster = behaviour;
                                break;
                            case "OVRRayHelper":
                                controllerLaser = behaviour;
                                controllerLaserCount++;
                                break;
                            case "QuestRiskSnapshotController":
                            case "RiskSnapshotSessionLogger":
                            case "QuestRiskHud":
                                legacySnapshotCount++;
                                break;
                        }
                    }
                }

                Assert.That(staticPolicy, Is.Not.Null);
                Assert.That(dynamicPolicy, Is.Not.Null);
                Assert.That(presentation, Is.Not.Null);
                Assert.That(hud, Is.Not.Null);
                Assert.That(togglePanel, Is.Not.Null);
                Assert.That(boundaryVisibility, Is.Not.Null);
                Assert.That(personalization, Is.Not.Null);
                Assert.That(personalizationPanel, Is.Not.Null);
                Assert.That(passthroughLayer, Is.Not.Null);
                Assert.That(passthroughLayerCount, Is.EqualTo(1));
                Assert.That(inputModule, Is.Not.Null);
                Assert.That(ovrRaycaster, Is.Not.Null);
                Assert.That(controllerLaser, Is.Not.Null);
                Assert.That(controllerLaserCount, Is.EqualTo(1));
                if (controllerLaser != null
                    && controllerLaser.transform.parent != null)
                {
                    MonoBehaviour[] parentBehaviours =
                        controllerLaser.transform.parent
                            .GetComponents<MonoBehaviour>();
                    for (int i = 0; i < parentBehaviours.Length; i++)
                    {
                        if (parentBehaviours[i] != null
                            && parentBehaviours[i].GetType().Name
                                == "OVRControllerHelper")
                        {
                            rightControllerHelper = parentBehaviours[i];
                            break;
                        }
                    }
                }
                Assert.That(rightControllerHelper, Is.Not.Null);
                Assert.That(legacySnapshotCount, Is.Zero);
                MonoBehaviour experimentLogger =
                    FindBehaviourInScene(scene, "QuestRiskExperimentLogger");
                Assert.That(experimentLogger, Is.Not.Null);
                Assert.That(spatialProvider, Is.Not.Null);
                Assert.That(trackingQuality, Is.Not.Null);
                Assert.That(panelPlacement, Is.Not.Null);
                Assert.That(alertFeedback, Is.Not.Null);
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<Shader>(
                        SafetyAlertShaderPath),
                    Is.Not.Null);
                Transform labelRoot = new SerializedObject(experimentLogger)
                    .FindProperty("labelRoot")
                    .objectReferenceValue as Transform;
                Assert.That(labelRoot, Is.Not.Null);
                Assert.That(
                    labelRoot.localScale.x,
                    Is.EqualTo(0.00075f).Within(0.000001f));
                Assert.That(
                    labelRoot.localScale.y,
                    Is.EqualTo(0.00075f).Within(0.000001f));
                Assert.That(panelPlacement.transform, Is.SameAs(labelRoot));
                CanvasGroup panelCanvasGroup = personalizationPanel
                    .transform.parent.GetComponent<CanvasGroup>();
                Assert.That(panelCanvasGroup, Is.Not.Null);
                Assert.That(panelCanvasGroup.interactable, Is.True);
                Assert.That(panelCanvasGroup.blocksRaycasts, Is.True);
                Assert.That(
                    new SerializedObject(personalizationPanel)
                        .FindProperty("panelOpacity")
                        .floatValue,
                    Is.EqualTo(1f).Within(0.0001f));
                Assert.That(
                    experimentLogger.GetType().GetField(
                        "passthroughLayer",
                        BindingFlags.Instance | BindingFlags.NonPublic),
                    Is.Null,
                    "The measurement provider must not control the layer directly.");
                var serializedStaticPolicy = new SerializedObject(staticPolicy);
                Assert.That(
                    serializedStaticPolicy
                        .FindProperty("enabledChannels")
                        .intValue,
                    Is.EqualTo((int)StaticRiskChannelMask.All));
                Assert.That(
                    serializedStaticPolicy
                        .FindProperty("measurementProvider")
                        .objectReferenceValue,
                    Is.SameAs(spatialProvider));
                var serializedSpatialProvider =
                    new SerializedObject(spatialProvider);
                Assert.That(
                    serializedSpatialProvider
                        .FindProperty("roomSceneProviderBehaviour")
                        .objectReferenceValue,
                    Is.SameAs(experimentLogger));
                Assert.That(
                    serializedSpatialProvider
                        .FindProperty("qualityController")
                        .objectReferenceValue,
                    Is.SameAs(trackingQuality));
                var serializedStaticPresentation =
                    new SerializedObject(presentation);
                Assert.That(
                    serializedStaticPresentation
                        .FindProperty("enabledStaticChannels")
                        .intValue,
                    Is.EqualTo((int)StaticRiskChannelMask.All));
                Assert.That(
                    serializedStaticPresentation
                        .FindProperty("maximumStaticWindows")
                        .intValue,
                    Is.EqualTo(2));
                SerializedProperty staticSettings =
                    serializedStaticPolicy.FindProperty("policySettings");
                Assert.That(staticSettings, Is.Not.Null);
                Assert.That(
                    staticSettings
                        .FindPropertyRelative("stableOnThreshold")
                        .floatValue,
                    Is.EqualTo(0.65f).Within(0.0001f));
                Assert.That(
                    staticSettings
                        .FindPropertyRelative("rapidOnThreshold")
                        .floatValue,
                    Is.EqualTo(0.45f).Within(0.0001f));
                Assert.That(
                    staticSettings
                        .FindPropertyRelative("handFullThreshold")
                        .floatValue,
                    Is.EqualTo(0.85f).Within(0.0001f));
                Assert.That(
                    staticSettings
                        .FindPropertyRelative("minimumHoldSeconds")
                        .floatValue,
                    Is.EqualTo(1.5f).Within(0.0001f));
                Assert.That(
                    staticSettings
                        .FindPropertyRelative("releaseDelaySeconds")
                        .floatValue,
                    Is.EqualTo(0.35f).Within(0.0001f));
                var serializedDynamicPolicy =
                    new SerializedObject(dynamicPolicy);
                SerializedProperty dynamicSettings =
                    serializedDynamicPolicy.FindProperty("decisionSettings");
                Assert.That(
                    dynamicSettings
                        .FindPropertyRelative("onThreshold")
                        .floatValue,
                    Is.EqualTo(0.60f).Within(0.0001f));
                Assert.That(
                    dynamicSettings
                        .FindPropertyRelative("offThreshold")
                        .floatValue,
                    Is.EqualTo(0.45f).Within(0.0001f));
                Assert.That(
                    dynamicSettings
                        .FindPropertyRelative("minimumHoldSeconds")
                        .floatValue,
                    Is.EqualTo(1.5f).Within(0.0001f));
                Assert.That(
                    dynamicSettings
                        .FindPropertyRelative("releaseDelaySeconds")
                        .floatValue,
                    Is.EqualTo(0.35f).Within(0.0001f));
                Assert.That(
                    new SerializedObject(hud)
                        .FindProperty("staticPolicy")
                        .objectReferenceValue,
                    Is.SameAs(staticPolicy));
                Assert.That(
                    new SerializedObject(hud)
                        .FindProperty("dynamicPolicy")
                        .objectReferenceValue,
                    Is.SameAs(dynamicPolicy));
                Assert.That(
                    new SerializedObject(hud)
                        .FindProperty("presentation")
                        .objectReferenceValue,
                    Is.SameAs(presentation));
                Assert.That(
                    new SerializedObject(presentation)
                        .FindProperty("passthroughLayer")
                        .objectReferenceValue,
                    Is.SameAs(passthroughLayer));
                Assert.That(
                    new SerializedObject(presentation)
                        .FindProperty("alertFeedback")
                        .objectReferenceValue,
                    Is.SameAs(alertFeedback));
                Assert.That(
                    new SerializedObject(alertFeedback)
                        .FindProperty("borderShader")
                        .objectReferenceValue,
                    Is.SameAs(
                        AssetDatabase.LoadAssetAtPath<Shader>(
                            SafetyAlertShaderPath)));
                Shader hazardCueShader =
                    AssetDatabase.LoadAssetAtPath<Shader>(
                        HazardCueShaderPath);
                Assert.That(hazardCueShader, Is.Not.Null);
                var serializedPresentation =
                    new SerializedObject(presentation);
                Assert.That(
                    serializedPresentation.FindProperty("cueShader")
                        .objectReferenceValue,
                    Is.SameAs(hazardCueShader));
                Assert.That(
                    serializedPresentation.FindProperty("personEdgeFeather")
                        .floatValue,
                    Is.EqualTo(0.065f).Within(0.0001f));
                Assert.That(
                    serializedPresentation
                        .FindProperty("personPositionSmoothingSeconds")
                        .floatValue,
                    Is.EqualTo(0.10f).Within(0.0001f));
                Assert.That(
                    serializedPresentation
                        .FindProperty("personSizeSmoothingSeconds")
                        .floatValue,
                    Is.EqualTo(0.10f).Within(0.0001f));
                Assert.That(
                    serializedPresentation.FindProperty("personFadeInSeconds")
                        .floatValue,
                    Is.EqualTo(0.125f).Within(0.0001f));
                Assert.That(
                    serializedPresentation.FindProperty("personFadeOutSeconds")
                        .floatValue,
                    Is.EqualTo(0.30f).Within(0.0001f));
                Assert.That(
                    serializedPresentation.FindProperty("wallEdgeFeather")
                        .floatValue,
                    Is.EqualTo(0.065f).Within(0.0001f));
                Assert.That(
                    new SerializedObject(presentation)
                        .FindProperty("staticFeatureEnabled")
                        .boolValue,
                    Is.True);
                Assert.That(
                    new SerializedObject(presentation)
                        .FindProperty("dynamicFeatureEnabled")
                        .boolValue,
                    Is.True);
                Assert.That(
                    new SerializedObject(presentation)
                        .FindProperty("feedbackMode")
                        .intValue,
                    Is.EqualTo((int)SafetyFeedbackMode.Passthrough));
                Assert.That(
                    new SerializedObject(presentation)
                        .FindProperty("personMaximumWidth")
                        .floatValue,
                    Is.EqualTo(0.42f).Within(0.0001f));
                Assert.That(
                    new SerializedObject(presentation)
                        .FindProperty("personMaximumHeight")
                        .floatValue,
                    Is.EqualTo(0.62f).Within(0.0001f));
                Assert.That(
                    new SerializedObject(presentation)
                        .FindProperty("personLostHoldSeconds")
                        .floatValue,
                    Is.EqualTo(1.5f).Within(0.0001f));
                var serializedBoundary =
                    new SerializedObject(boundaryVisibility);
                Assert.That(
                    serializedBoundary
                        .FindProperty("preferFullBoundaryless")
                        .boolValue,
                    Is.True);
                Assert.That(
                    serializedBoundary
                        .FindProperty("contextualFallbackEnabled")
                        .boolValue,
                    Is.True);
                Assert.That(
                    serializedBoundary.FindProperty("presentation")
                        .objectReferenceValue,
                    Is.SameAs(presentation));
                Assert.That(
                    serializedBoundary.FindProperty("passthroughLayer")
                        .objectReferenceValue,
                    Is.SameAs(passthroughLayer));
                MonoBehaviour manager =
                    FindBehaviourInScene(scene, "OVRManager");
                Assert.That(manager, Is.Not.Null);
                Assert.That(
                    serializedBoundary.FindProperty("ovrManager")
                        .objectReferenceValue,
                    Is.SameAs(manager));
                var serializedManager = new SerializedObject(manager);
                Assert.That(
                    serializedManager
                        .FindProperty("isInsightPassthroughEnabled")
                        .boolValue,
                    Is.True);
                Assert.That(
                    serializedManager.FindProperty("_trackingOriginType")
                        .enumValueIndex,
                    Is.EqualTo(1));
                Assert.That(
                    new SerializedObject(togglePanel)
                        .FindProperty("presentation")
                        .objectReferenceValue,
                    Is.SameAs(presentation));
                Assert.That(
                    new SerializedObject(togglePanel)
                        .FindProperty("staticToggleButton")
                        .objectReferenceValue,
                    Is.Not.Null);
                Assert.That(
                    new SerializedObject(togglePanel)
                        .FindProperty("dynamicToggleButton")
                        .objectReferenceValue,
                    Is.Not.Null);
                var serializedPersonalization =
                    new SerializedObject(personalization);
                Assert.That(
                    serializedPersonalization
                        .FindProperty("staticPolicy")
                        .objectReferenceValue,
                    Is.SameAs(staticPolicy));
                Assert.That(
                    serializedPersonalization
                        .FindProperty("dynamicPolicy")
                        .objectReferenceValue,
                    Is.SameAs(dynamicPolicy));
                Assert.That(
                    serializedPersonalization
                        .FindProperty("presentation")
                        .objectReferenceValue,
                    Is.SameAs(presentation));
                Assert.That(
                    serializedPersonalization
                        .FindProperty("sourceModelArtifact")
                        .objectReferenceValue,
                    Is.Not.Null);
                Assert.That(
                    serializedPersonalization
                        .FindProperty("modelAsset")
                        .objectReferenceValue,
                    Is.Not.Null,
                    "The Unity-compatible Neutral-risk ONNX must be assigned.");
                Assert.That(
                    AssetDatabase.GetAssetPath(
                        serializedPersonalization
                            .FindProperty("modelAsset")
                            .objectReferenceValue),
                    Is.EqualTo(
                        "Assets/Models/personalization_runtime.onnx"));
                Assert.That(
                    serializedPersonalization
                        .FindProperty("probabilityOutputName")
                        .stringValue,
                    Is.EqualTo("risk_probability"));
                Assert.That(
                    serializedPersonalization
                        .FindProperty("shadowMode")
                        .boolValue,
                    Is.True);
                Assert.That(
                    serializedPersonalization
                        .FindProperty("applyPersonalization")
                        .boolValue,
                    Is.False);
                Assert.That(
                    serializedPersonalization
                        .FindProperty("automaticInference")
                        .boolValue,
                    Is.False,
                    "ML OFF must stop background inference as well as apply.");
                var serializedPersonalizationPanel =
                    new SerializedObject(personalizationPanel);
                Assert.That(
                    serializedPersonalizationPanel
                        .FindProperty("personalization")
                        .objectReferenceValue,
                    Is.SameAs(personalization));
                Assert.That(
                    serializedPersonalizationPanel
                        .FindProperty("staticPolicy")
                        .objectReferenceValue,
                    Is.SameAs(staticPolicy));
                Assert.That(
                    serializedPersonalizationPanel
                        .FindProperty("dynamicPolicy")
                        .objectReferenceValue,
                    Is.SameAs(dynamicPolicy));

                personalization.GetType()
                    .GetMethod("SetBypassColdStartForTesting")
                    .Invoke(personalization, new object[] { true });
                personalization.GetType()
                    .GetMethod("SetNegativeProbabilityOverride")
                    .Invoke(personalization, new object[] { true });
                personalization.GetType()
                    .GetMethod("SetOverriddenNegativeProbability")
                    .Invoke(personalization, new object[] { 1f });
                personalization.GetType()
                    .GetMethod("RunNow")
                    .Invoke(personalization, null);
                Assert.That(
                    (bool)personalization.GetType()
                        .GetProperty("HasNegativeProbability")
                        .GetValue(personalization),
                    Is.True);
                object inferredThresholds = personalization.GetType()
                    .GetProperty("LastThresholds")
                    .GetValue(personalization);
                Assert.That(
                    (float)inferredThresholds.GetType()
                        .GetField("StableOnThreshold")
                        .GetValue(inferredThresholds),
                    Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(
                    (float)inferredThresholds.GetType()
                        .GetField("DynamicOnThreshold")
                        .GetValue(inferredThresholds),
                    Is.EqualTo(0.70f).Within(0.0001f));
                Assert.That(
                    (float)inferredThresholds.GetType()
                        .GetField("DynamicOffThreshold")
                        .GetValue(inferredThresholds),
                    Is.EqualTo(0.55f).Within(0.0001f));

                float emergencyBefore = staticSettings
                    .FindPropertyRelative("emergencyDistance")
                    .floatValue;
                personalization.GetType()
                    .GetMethod(
                        "SetThresholdPreview",
                        new[]
                        {
                            typeof(float),
                            typeof(float),
                            typeof(float)
                        })
                    .Invoke(
                        personalization,
                        new object[] { 0.70f, 0.50f, 0.90f });
                personalization.GetType()
                    .GetMethod("ApplyThresholdPreview")
                    .Invoke(personalization, null);
                serializedStaticPolicy.Update();
                serializedDynamicPolicy.Update();
                Assert.That(
                    staticSettings
                        .FindPropertyRelative("stableOnThreshold")
                        .floatValue,
                    Is.EqualTo(0.70f).Within(0.0001f));
                Assert.That(
                    staticSettings
                        .FindPropertyRelative("rapidOnThreshold")
                        .floatValue,
                    Is.EqualTo(0.50f).Within(0.0001f));
                Assert.That(
                    staticSettings
                        .FindPropertyRelative("handFullThreshold")
                        .floatValue,
                    Is.EqualTo(0.90f).Within(0.0001f));
                Assert.That(
                    staticSettings
                        .FindPropertyRelative("emergencyDistance")
                        .floatValue,
                    Is.EqualTo(emergencyBefore).Within(0.0001f));
                Assert.That(
                    dynamicSettings
                        .FindPropertyRelative("onThreshold")
                        .floatValue,
                    Is.EqualTo(0.70f).Within(0.0001f));
                Assert.That(
                    dynamicSettings
                        .FindPropertyRelative("offThreshold")
                        .floatValue,
                    Is.EqualTo(0.55f).Within(0.0001f));
                personalization.GetType()
                    .GetMethod("RestoreSafeDefaults")
                    .Invoke(personalization, null);
                serializedStaticPolicy.Update();
                serializedDynamicPolicy.Update();
                Assert.That(
                    staticSettings
                        .FindPropertyRelative("stableOnThreshold")
                        .floatValue,
                    Is.EqualTo(0.65f).Within(0.0001f));
                Assert.That(
                    dynamicSettings
                        .FindPropertyRelative("onThreshold")
                        .floatValue,
                    Is.EqualTo(0.60f).Within(0.0001f));
                Assert.That(
                    dynamicSettings
                        .FindPropertyRelative("offThreshold")
                        .floatValue,
                    Is.EqualTo(0.45f).Within(0.0001f));
                Assert.That(
                    new SerializedObject(rightControllerHelper)
                        .FindProperty("RayHelper")
                        .objectReferenceValue,
                    Is.SameAs(controllerLaser));
                Assert.That(
                    new SerializedObject(inputModule)
                        .FindProperty("rayTransform")
                        .objectReferenceValue,
                    Is.SameAs(rightControllerHelper.transform));
                var serializedLaser = new SerializedObject(controllerLaser);
                Assert.That(
                    serializedLaser.FindProperty("DefaultLength").floatValue,
                    Is.EqualTo(5f).Within(0.0001f));
                Assert.That(
                    serializedLaser.FindProperty("Renderer")
                        .objectReferenceValue,
                    Is.Not.Null);
                Assert.That(
                    serializedLaser.FindProperty("Cursor")
                        .objectReferenceValue,
                    Is.Not.Null);

                togglePanel.GetType()
                    .GetMethod("ToggleStatic")
                    .Invoke(togglePanel, null);
                Assert.That(
                    (bool)presentation.GetType()
                        .GetProperty("StaticFeatureEnabled")
                        .GetValue(presentation),
                    Is.False);
                togglePanel.GetType()
                    .GetMethod("ToggleStatic")
                    .Invoke(togglePanel, null);

                togglePanel.GetType()
                    .GetMethod("ToggleDynamic")
                    .Invoke(togglePanel, null);
                Assert.That(
                    (bool)presentation.GetType()
                        .GetProperty("DynamicFeatureEnabled")
                        .GetValue(presentation),
                    Is.False);
                togglePanel.GetType()
                    .GetMethod("ToggleDynamic")
                    .Invoke(togglePanel, null);

                DynamicRiskSessionLogger dynamicLogger =
                    FindInScene<DynamicRiskSessionLogger>(scene);
                Assert.That(dynamicLogger, Is.Not.Null);
                Assert.That(
                    new SerializedObject(dynamicLogger)
                        .FindProperty("snapshotSequenceProviderBehaviour")
                        .objectReferenceValue,
                    Is.Null);
                Assert.That(
                    new SerializedObject(dynamicLogger)
                        .FindProperty("staticPolicyBehaviour")
                        .objectReferenceValue,
                    Is.SameAs(staticPolicy));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void RuntimePanelRestoresSelectedPageFromVersionTwoSettings()
        {
            const string key =
                "TeamVR.AdaptivePassthrough.RuntimePanelSettings.v2";
            bool hadPrevious = PlayerPrefs.HasKey(key);
            string previous = hadPrevious ? PlayerPrefs.GetString(key) : null;
            Scene scene = EditorSceneManager.OpenScene(
                QuestScenePath,
                OpenSceneMode.Additive);
            try
            {
                MonoBehaviour panel = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    MonoBehaviour[] behaviours =
                        root.GetComponentsInChildren<MonoBehaviour>(true);
                    for (int i = 0; i < behaviours.Length; i++)
                    {
                        if (behaviours[i] != null
                            && behaviours[i].GetType().Name
                                == "PersonalizationRuntimePanel")
                        {
                            panel = behaviours[i];
                            break;
                        }
                    }

                    if (panel != null)
                    {
                        break;
                    }
                }

                Assert.That(panel, Is.Not.Null);
                PlayerPrefs.SetString(
                    key,
                    "{\"Version\":2,\"SelectedPage\":1,"
                    + "\"StaticFeatureEnabled\":true,"
                    + "\"DynamicFeatureEnabled\":true,"
                    + "\"HeadFeatureEnabled\":true,"
                    + "\"HandsFeatureEnabled\":true,"
                    + "\"LowObstacleFeatureEnabled\":true}");
                MethodInfo load = panel.GetType().GetMethod(
                    "LoadRuntimeSettings",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo currentPage = panel.GetType().GetField(
                    "currentPage",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(load, Is.Not.Null);
                Assert.That(currentPage, Is.Not.Null);
                load.Invoke(panel, null);

                Assert.That((int)currentPage.GetValue(panel), Is.EqualTo(1));
            }
            finally
            {
                if (hadPrevious)
                {
                    PlayerPrefs.SetString(key, previous);
                }
                else
                {
                    PlayerPrefs.DeleteKey(key);
                }
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void SpatialFusionRemovesHandOverlapAndRoomSceneIsConcurrent()
        {
            string providerSource = File.ReadAllText(
                Path.Combine(
                    Application.dataPath,
                    "Scripts/AdaptivePassthrough/Quest/QuestSpatialObstacleProvider.cs"));
            string roomSource = File.ReadAllText(
                Path.Combine(
                    Application.dataPath,
                    "Scripts/QuestRiskExperimentLogger.cs"));
            string legacySceneSource = File.ReadAllText(
                Path.Combine(
                    Application.dataPath,
                    "Scripts/QuestSceneDistanceLogger.cs"));

            Assert.That(providerSource, Does.Contain("fusionFilter.Fuse"));
            Assert.That(providerSource, Does.Contain("HandRayOriginOffsetMeters"));
            Assert.That(providerSource, Does.Contain("ClearSafetyOverlap(leftState)"));
            Assert.That(providerSource, Does.Contain("ClearSafetyOverlap(rightState)"));
            Assert.That(providerSource, Does.Not.Contain(
                "UpdateSafetyOverlap(\r\n                leftState"));
            Assert.That(roomSource, Does.Contain("ISpatialObstacleProvider"));
            Assert.That(roomSource, Does.Not.Contain("UpdatePanelPose();"));
            Assert.That(legacySceneSource, Does.Not.Contain(
                "labelRoot.position = cam.position"));
        }

        [Test]
        public void PassthroughWindowShaderImports()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Shaders/AdaptivePassthrough/PassthroughWindow.shader");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
        }

        [Test]
        public void IncompatibleRandomForestModelIsPreservedAsSourceOnly()
        {
            Object source = AssetDatabase.LoadMainAssetAtPath(
                PersonalizationSourceModelPath);
            Assert.That(source, Is.Not.Null);
            Assert.That(source, Is.Not.InstanceOf<ModelAsset>());
            Assert.That(
                File.Exists(Path.Combine(
                    Application.dataPath,
                    "Models",
                    "rf_personalization_real.onnx")),
                Is.False,
                "The unsupported .onnx extension would trigger a broken import.");
        }

        [Test]
        public void BoundarylessManifestPrioritizesFullModeWithContextualFallback()
        {
            ScriptableObject config =
                AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                    "Assets/Oculus/OculusProjectConfig.asset");
            Assert.That(config, Is.Not.Null);
            var serializedConfig = new SerializedObject(config);
            Assert.That(
                serializedConfig
                    .FindProperty("allowOptional3DofHeadTracking")
                    .boolValue,
                Is.False);
            Assert.That(
                serializedConfig
                    .FindProperty("boundaryVisibilitySupport")
                    .enumValueIndex,
                Is.GreaterThan(0));
            Assert.That(
                serializedConfig
                    .FindProperty("_insightPassthroughSupport")
                    .enumValueIndex,
                Is.GreaterThan(0));

            string manifestPath = Path.Combine(
                Application.dataPath,
                "Plugins",
                "Android",
                "AndroidManifest.xml");
            Assert.That(File.Exists(manifestPath), Is.True);
            string manifest = File.ReadAllText(manifestPath);
            StringAssert.Contains(
                "com.oculus.feature.BOUNDARYLESS_APP",
                manifest);
            StringAssert.Contains(
                "com.oculus.permission.BOUNDARY_VISIBILITY",
                manifest);
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T component = roots[i].GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static MonoBehaviour FindBehaviourInScene(
            Scene scene,
            string typeName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                MonoBehaviour[] behaviours =
                    root.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] != null
                        && behaviours[i].GetType().Name == typeName)
                    {
                        return behaviours[i];
                    }
                }
            }

            return null;
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform result = FindChild(root.GetChild(i), name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
