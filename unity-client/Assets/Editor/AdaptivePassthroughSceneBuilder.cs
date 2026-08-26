using System.Collections.Generic;
using System.IO;
using Meta.XR;
using Meta.XR.EnvironmentDepth;
using TeamVR.AdaptivePassthrough;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public static class AdaptivePassthroughSceneBuilder
{
    public const string MockScenePath = "Assets/Scenes/DynamicRiskMock.unity";
    public const string QuestScenePath = "Assets/Scenes/SampleScene.unity";
    public const string PersonModelPath = "Assets/Models/yolov9sentis.sentis";
    public const string PersonalizationSourceModelPath =
        "Assets/Models/rf_personalization_real.onnx.source";
    public const string PersonalizationRuntimeModelPath =
        "Assets/Models/personalization_runtime.onnx";
    public const string PassthroughWindowShaderPath =
        "Assets/Shaders/AdaptivePassthrough/PassthroughWindow.shader";
    public const string SafetyAlertBorderShaderPath =
        "Assets/Shaders/AdaptivePassthrough/SafetyAlertBorder.shader";
    public const string OvrRayHelperPrefabPath =
        "Packages/com.meta.xr.sdk.core/Prefabs/OVRRayHelper.prefab";

    [MenuItem("TeamVR/Adaptive Passthrough/Create or Replace Mock Test Scene")]
    public static void CreateMockTestScene()
    {
        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        scene.name = "DynamicRiskMock";

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.015f, 0.02f, 0.035f);
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);

        var system = new GameObject("Adaptive Dynamic Risk System");
        system.AddComponent<DynamicRiskController>();
        system.AddComponent<MockPersonDetectionSource>();
        system.AddComponent<DynamicRiskDebugOverlay>();
        system.AddComponent<DynamicRiskSessionLogger>();
        system.AddComponent<TrackingQualityController>();
        system.AddComponent<SpatialTrackingVisualLab>();

        Directory.CreateDirectory(Path.GetDirectoryName(MockScenePath));
        EditorSceneManager.SaveScene(scene, MockScenePath);
        AddSceneToBuildSettings(MockScenePath, false);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[DynamicRisk] Mock scene created at " + MockScenePath);
    }

    // Batch-mode entry point used by CI/local verification.
    public static void CreateMockTestSceneBatch()
    {
        CreateMockTestScene();
    }

    [MenuItem("TeamVR/Adaptive Passthrough/Install Runtime Into Sample Scene")]
    public static void InstallRuntimeIntoSampleScene()
    {
        AdaptivePassthroughBoundarySetup.ConfigureForQuestBuild();

        Scene scene = EditorSceneManager.OpenScene(QuestScenePath, OpenSceneMode.Single);
        DynamicRiskController controller =
            Object.FindAnyObjectByType<DynamicRiskController>();
        GameObject system;
        if (controller == null)
        {
            system = new GameObject("Adaptive Dynamic Risk System");
            controller = system.AddComponent<DynamicRiskController>();
        }
        else
        {
            system = controller.gameObject;
        }

        DynamicRiskDebugOverlay overlay = GetOrAdd<DynamicRiskDebugOverlay>(system);
        DynamicRiskSessionLogger dynamicSessionLogger =
            GetOrAdd<DynamicRiskSessionLogger>(system);
        QuestCameraPermissionCoordinator permission =
            GetOrAdd<QuestCameraPermissionCoordinator>(system);
        PassthroughCameraAccess cameraAccess =
            GetOrAdd<PassthroughCameraAccess>(system);
        EnvironmentDepthManager environmentDepth =
            Object.FindAnyObjectByType<EnvironmentDepthManager>();
        if (environmentDepth == null)
        {
            environmentDepth =
                system.AddComponent<EnvironmentDepthManager>();
        }

        environmentDepth.RemoveHands = true;
        EnvironmentRaycastManager environmentRaycast =
            Object.FindAnyObjectByType<EnvironmentRaycastManager>();
        if (environmentRaycast == null)
        {
            environmentRaycast =
                system.AddComponent<EnvironmentRaycastManager>();
        }

        TrackingQualityController trackingQuality =
            GetOrAdd<TrackingQualityController>(system);

        QuestPersonDepthProvider depthProvider =
            GetOrAdd<QuestPersonDepthProvider>(system);
        depthProvider.Configure(cameraAccess, environmentRaycast);
        QuestPersonDetectionRunner detectionRunner =
            GetOrAdd<QuestPersonDetectionRunner>(system);

        ModelAsset model = AssetDatabase.LoadAssetAtPath<ModelAsset>(PersonModelPath);
        var runnerObject = new SerializedObject(detectionRunner);
        runnerObject.FindProperty("controller").objectReferenceValue = controller;
        runnerObject.FindProperty("permissionCoordinator").objectReferenceValue = permission;
        runnerObject.FindProperty("cameraAccess").objectReferenceValue = cameraAccess;
        runnerObject.FindProperty("depthProvider").objectReferenceValue = depthProvider;
        runnerObject.FindProperty("qualityController").objectReferenceValue =
            trackingQuality;
        runnerObject.FindProperty("modelAsset").objectReferenceValue = model;
        runnerObject.FindProperty("benchmarkCpuGpuOnQuest").boolValue = false;
        runnerObject.FindProperty("confidenceThreshold").floatValue = 0.55f;
        runnerObject.FindProperty("trackingConfidenceThreshold").floatValue = 0.35f;
        runnerObject.FindProperty("iouThreshold").floatValue = 0.45f;
        runnerObject.FindProperty("inferenceRateHz").floatValue = 3f;
        runnerObject.FindProperty("personClassId").intValue = 0;
        runnerObject.FindProperty("boxesAreCenterFormat").boolValue = true;
        runnerObject.FindProperty("boxesAreNormalized").boolValue = false;
        runnerObject.FindProperty("maximumCandidates").intValue = 50;
        runnerObject.FindProperty("maximumDetections").intValue = 10;
        runnerObject.FindProperty("minimumVisibleFraction").floatValue = 0.15f;
        runnerObject.FindProperty("maximumNormalizedDimension").floatValue = 2f;
        runnerObject.FindProperty("diagnosticInferenceCount").intValue = 10;
        runnerObject.ApplyModifiedPropertiesWithoutUndo();

        ConfigureOverlay(overlay);
        QuestRiskExperimentLogger experimentLogger =
            Object.FindAnyObjectByType<QuestRiskExperimentLogger>();
        if (experimentLogger != null)
        {
            QuestSpatialObstacleProvider spatialProvider =
                GetOrAdd<QuestSpatialObstacleProvider>(system);
            OVRCameraRig trackingRig =
                Object.FindAnyObjectByType<OVRCameraRig>();
            spatialProvider.Configure(
                environmentRaycast,
                trackingQuality,
                experimentLogger,
                trackingRig == null ? null : trackingRig.centerEyeAnchor,
                trackingRig == null ? null : trackingRig.leftHandAnchor,
                trackingRig == null ? null : trackingRig.rightHandAnchor);
            StaticPassthroughPolicyController staticPolicy =
                GetOrAdd<StaticPassthroughPolicyController>(system);
            DynamicPassthroughPolicyController dynamicPolicy =
                GetOrAdd<DynamicPassthroughPolicyController>(system);
            PersonalizationRuntimeController personalization =
                GetOrAdd<PersonalizationRuntimeController>(system);
            SelectivePassthroughController presentation =
                GetOrAdd<SelectivePassthroughController>(system);
            SafetyAlertFeedbackController alertFeedback =
                GetOrAdd<SafetyAlertFeedbackController>(system);
            BoundaryVisibilityController boundaryVisibility =
                GetOrAdd<BoundaryVisibilityController>(system);
            OVRManager manager = Object.FindAnyObjectByType<OVRManager>();
            OVRPassthroughLayer passthroughLayer =
                Object.FindAnyObjectByType<OVRPassthroughLayer>();
            if (passthroughLayer == null)
            {
                GameObject layerOwner =
                    manager != null ? manager.gameObject : system;
                passthroughLayer =
                    layerOwner.AddComponent<OVRPassthroughLayer>();
            }

            Shader windowShader =
                AssetDatabase.LoadAssetAtPath<Shader>(
                    PassthroughWindowShaderPath);
            Shader alertShader =
                AssetDatabase.LoadAssetAtPath<Shader>(
                    SafetyAlertBorderShaderPath);
            ConfigureStaticBoundaryMeasurements(experimentLogger);
            ConfigureStaticBoundaryPolicy(staticPolicy);
            ConfigureDynamicPolicy(dynamicPolicy);
            staticPolicy.Configure(spatialProvider);
            dynamicPolicy.Configure(
                controller,
                detectionRunner);
            ModelAsset personalizationModel =
                AssetDatabase.LoadAssetAtPath<ModelAsset>(
                    PersonalizationRuntimeModelPath);
            Object personalizationSource =
                AssetDatabase.LoadMainAssetAtPath(
                    PersonalizationSourceModelPath);
            personalization.Configure(
                staticPolicy,
                dynamicPolicy,
                presentation,
                experimentLogger,
                personalizationModel,
                personalizationSource);
            presentation.Configure(
                staticPolicy,
                dynamicPolicy,
                passthroughLayer,
                windowShader,
                alertFeedback);
            alertFeedback.Configure(alertShader);
            ConfigureBoundaryManager(manager);
            boundaryVisibility.Configure(
                manager,
                presentation,
                passthroughLayer);
            var presentationObject = new SerializedObject(presentation);
            presentationObject
                .FindProperty("personEdgeFeather")
                .floatValue = 0.08f;
            presentationObject
                .FindProperty("personMinimumWidth")
                .floatValue = 0.10f;
            presentationObject
                .FindProperty("personMaximumWidth")
                .floatValue = 0.42f;
            presentationObject
                .FindProperty("personMinimumHeight")
                .floatValue = 0.16f;
            presentationObject
                .FindProperty("personMaximumHeight")
                .floatValue = 0.62f;
            presentationObject
                .FindProperty("maximumPersonRevealArea")
                .floatValue = 0.55f;
            presentationObject
                .FindProperty("personPositionSmoothingSeconds")
                .floatValue = 0.10f;
            presentationObject
                .FindProperty("personSizeSmoothingSeconds")
                .floatValue = 0.15f;
            presentationObject
                .FindProperty("personFadeInSeconds")
                .floatValue = 0.20f;
            presentationObject
                .FindProperty("personLostHoldSeconds")
                .floatValue = 1.50f;
            presentationObject
                .FindProperty("personFadeOutSeconds")
                .floatValue = 0.30f;
            presentationObject.ApplyModifiedPropertiesWithoutUndo();

            RemoveComponents<QuestRiskSnapshotController>();
            RemoveComponents<RiskSnapshotSessionLogger>();

            var dynamicLoggerObject =
                new SerializedObject(dynamicSessionLogger);
            dynamicLoggerObject
                .FindProperty("snapshotSequenceProviderBehaviour")
                .objectReferenceValue = null;
            dynamicLoggerObject
                .FindProperty("presentationBehaviour")
                .objectReferenceValue = presentation;
            dynamicLoggerObject.ApplyModifiedPropertiesWithoutUndo();

            InstallOrUpdateQuestHud(
                experimentLogger,
                staticPolicy,
                dynamicPolicy,
                presentation,
                personalization,
                trackingQuality);
            EditorUtility.SetDirty(staticPolicy);
            EditorUtility.SetDirty(dynamicPolicy);
            EditorUtility.SetDirty(presentation);
            EditorUtility.SetDirty(alertFeedback);
            EditorUtility.SetDirty(personalization);
            EditorUtility.SetDirty(boundaryVisibility);
            EditorUtility.SetDirty(passthroughLayer);
            EditorUtility.SetDirty(dynamicSessionLogger);
            EditorUtility.SetDirty(spatialProvider);
            EditorUtility.SetDirty(trackingQuality);
        }
        else
        {
            Debug.LogWarning(
                "[DynamicRisk] QuestRiskExperimentLogger was not found; "
                + "independent Passthrough could not be installed.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorUtility.SetDirty(environmentDepth);
        EditorUtility.SetDirty(environmentRaycast);
        EditorUtility.SetDirty(depthProvider);
        EditorUtility.SetDirty(trackingQuality);
        EditorSceneManager.SaveScene(scene, QuestScenePath);
        AddSceneToBuildSettings(QuestScenePath, true);
        AssetDatabase.SaveAssets();
        Debug.Log(
            model == null
                ? "[DynamicRisk] Runtime installed, but model asset is missing at " + PersonModelPath
                : "[DynamicRisk] Quest camera, person model, and runtime installed into " + QuestScenePath);
    }

    public static void InstallRuntimeIntoSampleSceneBatch()
    {
        InstallRuntimeIntoSampleScene();
    }

    private static void ConfigureOverlay(DynamicRiskDebugOverlay overlay)
    {
        var serialized = new SerializedObject(overlay);
        serialized.FindProperty("visible").boolValue = true;
        serialized.FindProperty("developmentBuildOnly").boolValue = true;
        serialized.FindProperty("drawPanelBackground").boolValue = false;
        serialized.FindProperty("showHeader").boolValue = false;
        serialized.FindProperty("normalizedViewport").rectValue =
            new Rect(0.05f, 0.18f, 0.90f, 0.72f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureStaticBoundaryMeasurements(
        QuestRiskExperimentLogger experimentLogger)
    {
        var serialized = new SerializedObject(experimentLogger);
        serialized.FindProperty("stateWindowDuration").floatValue = 0.5f;
        serialized.FindProperty("handVelocitySmoothingTime").floatValue = 0.15f;
        serialized.FindProperty("neckPivotForwardOffset").floatValue = 0.10f;
        serialized.FindProperty("neckPivotUpOffset").floatValue = 0.10f;
        serialized.FindProperty("thresholdHeadSpeedScale").floatValue = 1.0f;
        serialized.FindProperty("safeDistanceMeters").floatValue = 2.5f;
        serialized.FindProperty("safeTimeSeconds").floatValue = 4.5f;
        serialized.FindProperty("maxApproachAccel").floatValue = 5.0f;
        serialized.FindProperty("weightDistance").floatValue = 0.30f;
        serialized.FindProperty("weightTTC").floatValue = 0.30f;
        serialized.FindProperty("weightApproachAcceleration").floatValue = 0.0f;
        serialized.FindProperty("weightBlind").floatValue = 0.15f;
        serialized.FindProperty("enableHandRisk").boolValue = true;
        serialized.FindProperty("personalReachLength").floatValue = 0.70f;
        serialized.FindProperty("autoCalibrateReach").boolValue = true;
        serialized.FindProperty("maxPlausibleReach").floatValue = 1.00f;
        serialized.FindProperty("reachTransitionMargin").floatValue = 0.15f;
        serialized.FindProperty("safeHandDistance").floatValue = 0.50f;
        serialized.FindProperty("safeHandTime").floatValue = 1.00f;
        serialized.FindProperty("weightHandDistance").floatValue = 0.40f;
        serialized.FindProperty("weightHandTTC").floatValue = 0.60f;
        serialized.FindProperty("handApproachSpeedMin").floatValue = 0.05f;
        serialized.FindProperty("uiRefreshInterval").floatValue = 0.15f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureStaticBoundaryPolicy(
        StaticPassthroughPolicyController staticPolicy)
    {
        var serialized = new SerializedObject(staticPolicy);
        SerializedProperty settings = serialized.FindProperty("policySettings");
        settings.FindPropertyRelative("stableOnThreshold").floatValue = 0.65f;
        settings.FindPropertyRelative("rapidOnThreshold").floatValue = 0.45f;
        settings.FindPropertyRelative("hysteresisWidth").floatValue = 0.08f;
        settings.FindPropertyRelative("handFullThreshold").floatValue = 0.85f;
        settings.FindPropertyRelative("awareThreshold").floatValue = 0.40f;
        settings.FindPropertyRelative("minimumHoldSeconds").floatValue = 1.50f;
        settings.FindPropertyRelative("releaseDelaySeconds").floatValue = 0.35f;
        settings.FindPropertyRelative("awareApproachSpeed").floatValue = 0.05f;
        settings.FindPropertyRelative("emergencyDistance").floatValue = 0.25f;
        settings.FindPropertyRelative("emergencyReleaseMargin").floatValue = 0.05f;
        settings.FindPropertyRelative("emergencyApproachSpeed").floatValue = 0.10f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureDynamicPolicy(
        DynamicPassthroughPolicyController dynamicPolicy)
    {
        var serialized = new SerializedObject(dynamicPolicy);
        SerializedProperty settings = serialized.FindProperty("decisionSettings");
        settings.FindPropertyRelative("mode").enumValueIndex =
            (int)PassthroughDecisionMode.Hysteresis;
        settings.FindPropertyRelative("onThreshold").floatValue = 0.60f;
        settings.FindPropertyRelative("offThreshold").floatValue = 0.45f;
        settings.FindPropertyRelative("minimumHoldSeconds").floatValue = 1.50f;
        settings.FindPropertyRelative("releaseDelaySeconds").floatValue = 0.35f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureBoundaryManager(OVRManager manager)
    {
        if (manager == null)
        {
            Debug.LogWarning(
                "[AdaptivePassthrough] OVRManager was not found; "
                + "runtime boundary fallback could not be configured.");
            return;
        }

        manager.isInsightPassthroughEnabled = true;
        manager.shouldBoundaryVisibilityBeSuppressed = false;
        manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
        EditorUtility.SetDirty(manager);
    }

    private static void InstallOrUpdateQuestHud(
        QuestRiskExperimentLogger experimentLogger,
        StaticPassthroughPolicyController staticPolicy,
        DynamicPassthroughPolicyController dynamicPolicy,
        SelectivePassthroughController presentation,
        PersonalizationRuntimeController personalization,
        TrackingQualityController trackingQuality)
    {
        var loggerObject = new SerializedObject(experimentLogger);
        Text leftText =
            loggerObject.FindProperty("labelText").objectReferenceValue as Text;
        Text rightText =
            loggerObject.FindProperty("riskLabelText").objectReferenceValue as Text;
        Transform labelRoot =
            loggerObject.FindProperty("labelRoot").objectReferenceValue as Transform;
        if (leftText != null)
        {
            leftText.gameObject.SetActive(false);
        }

        if (rightText != null)
        {
            rightText.gameObject.SetActive(false);
        }

        if (labelRoot == null)
        {
            Debug.LogWarning(
                "[DynamicRisk] The existing labelRoot is missing; "
                + "the compact Quest HUD could not be installed.");
            return;
        }

        InstallQuestUiInput(labelRoot);
        WorldSpacePanelPlacementController panelPlacement =
            GetOrAdd<WorldSpacePanelPlacementController>(
                labelRoot.gameObject);
        panelPlacement.Configure(labelRoot);

        Transform existingPanel = labelRoot.Find("QuestRiskHudPanel");
        GameObject panelObject;
        if (existingPanel == null)
        {
            panelObject = new GameObject(
                "QuestRiskHudPanel",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            panelObject.transform.SetParent(labelRoot, false);
        }
        else
        {
            panelObject = existingPanel.gameObject;
        }

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(1280f, 920f);
        panelRect.anchoredPosition = Vector2.zero;

        Image background = GetOrAdd<Image>(panelObject);
        background.color = new Color(0.02f, 0.03f, 0.05f, 0.82f);
        background.raycastTarget = false;
        CanvasGroup panelCanvasGroup = GetOrAdd<CanvasGroup>(panelObject);
        panelCanvasGroup.alpha = 1f;
        panelCanvasGroup.interactable = true;
        panelCanvasGroup.blocksRaycasts = true;

        Transform existingText = panelObject.transform.Find("SummaryText");
        GameObject textObject;
        if (existingText == null)
        {
            textObject = new GameObject(
                "SummaryText",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(panelObject.transform, false);
        }
        else
        {
            textObject = existingText.gameObject;
        }

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 0.67f);
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.offsetMin = new Vector2(32f, 10f);
        textRect.offsetMax = new Vector2(-32f, -20f);

        TextMeshProUGUI summaryText = textObject.GetComponent<TextMeshProUGUI>();
        summaryText.text = "Initializing Quest risk HUD...";
        summaryText.fontSize = 24f;
        summaryText.alignment = TextAlignmentOptions.TopLeft;
        summaryText.color = Color.white;
        summaryText.textWrappingMode = TextWrappingModes.NoWrap;
        summaryText.overflowMode = TextOverflowModes.Ellipsis;
        summaryText.raycastTarget = false;
        textObject.SetActive(false);

        TMP_Text staticButtonText;
        Button staticButton = CreateOrUpdateToggleButton(
            panelObject.transform,
            "StaticToggleButton",
            new Vector2(-300f, 100f),
            out staticButtonText);
        TMP_Text dynamicButtonText;
        Button dynamicButton = CreateOrUpdateToggleButton(
            panelObject.transform,
            "DynamicToggleButton",
            new Vector2(300f, 100f),
            out dynamicButtonText);
        TextMeshProUGUI instructionText = CreateOrUpdateText(
            panelObject.transform,
            "ControlInstruction",
            new Vector2(0f, 45f),
            new Vector2(1180f, 34f));
        instructionText.text =
            "Right controller: aim with the laser and pull the index trigger";
        instructionText.fontSize = 22f;
        instructionText.alignment = TextAlignmentOptions.Center;
        instructionText.color = new Color(0.82f, 0.88f, 0.96f, 1f);
        instructionText.raycastTarget = false;
        staticButton.gameObject.SetActive(false);
        dynamicButton.gameObject.SetActive(false);
        instructionText.gameObject.SetActive(false);

        QuestRiskHud[] legacyHuds =
            panelObject.GetComponents<QuestRiskHud>();
        for (int i = 0; i < legacyHuds.Length; i++)
        {
            Object.DestroyImmediate(legacyHuds[i]);
        }

        IndependentPassthroughHud hud =
            GetOrAdd<IndependentPassthroughHud>(panelObject);
        hud.Configure(
            staticPolicy,
            dynamicPolicy,
            presentation,
            summaryText);
        PassthroughFeatureTogglePanel togglePanel =
            GetOrAdd<PassthroughFeatureTogglePanel>(panelObject);
        togglePanel.Configure(
            presentation,
            staticButton,
            staticButtonText,
            dynamicButton,
            dynamicButtonText);

        Transform existingControls =
            panelObject.transform.Find("PersonalizationControls");
        GameObject controlsObject;
        if (existingControls == null)
        {
            controlsObject = new GameObject(
                "PersonalizationControls",
                typeof(RectTransform));
            controlsObject.transform.SetParent(panelObject.transform, false);
        }
        else
        {
            controlsObject = existingControls.gameObject;
        }

        RectTransform controlsRect =
            controlsObject.GetComponent<RectTransform>();
        controlsRect.anchorMin = new Vector2(0.02f, 0.02f);
        controlsRect.anchorMax = new Vector2(0.98f, 0.98f);
        controlsRect.offsetMin = Vector2.zero;
        controlsRect.offsetMax = Vector2.zero;
        PersonalizationRuntimePanel personalizationPanel =
            GetOrAdd<PersonalizationRuntimePanel>(controlsObject);
        personalizationPanel.Configure(
            personalization,
            staticPolicy,
            dynamicPolicy,
            presentation,
            trackingQuality,
            panelPlacement);
        EditorUtility.SetDirty(experimentLogger);
        EditorUtility.SetDirty(hud);
        EditorUtility.SetDirty(togglePanel);
        EditorUtility.SetDirty(personalizationPanel);
        EditorUtility.SetDirty(panelPlacement);
        EditorUtility.SetDirty(panelCanvasGroup);
        EditorUtility.SetDirty(controlsRect);
        EditorUtility.SetDirty(summaryText);
        EditorUtility.SetDirty(instructionText);
    }

    private static void InstallQuestUiInput(Transform labelRoot)
    {
        Canvas canvas = labelRoot.GetComponent<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning(
                "[PassthroughTest] HUD root has no Canvas; "
                + "test buttons will not be interactive.");
            return;
        }

        RectTransform canvasRect =
            canvas.GetComponent<RectTransform>();
        if (canvasRect != null)
        {
            canvasRect.sizeDelta = new Vector2(1400f, 1050f);
            canvasRect.localScale = Vector3.one * 0.00075f;
        }

        GraphicRaycaster[] raycasters =
            labelRoot.GetComponents<GraphicRaycaster>();
        for (int i = 0; i < raycasters.Length; i++)
        {
            if (raycasters[i] != null
                && raycasters[i].GetType() == typeof(GraphicRaycaster))
            {
                Object.DestroyImmediate(raycasters[i]);
            }
        }

        GetOrAdd<OVRRaycaster>(labelRoot.gameObject);

        OVRCameraRig rig = Object.FindAnyObjectByType<OVRCameraRig>();
        if (rig != null
            && rig.centerEyeAnchor != null)
        {
            canvas.worldCamera =
                rig.centerEyeAnchor.GetComponent<Camera>();
        }

        EventSystem eventSystem =
            Object.FindAnyObjectByType<EventSystem>();
        GameObject eventSystemObject;
        if (eventSystem == null)
        {
            eventSystemObject = new GameObject(
                "Quest UI EventSystem",
                typeof(EventSystem));
            eventSystem =
                eventSystemObject.GetComponent<EventSystem>();
        }
        else
        {
            eventSystemObject = eventSystem.gameObject;
        }

        OVRInputModule inputModule =
            GetOrAdd<OVRInputModule>(eventSystemObject);
        if (rig != null)
        {
            inputModule.rayTransform =
                rig.rightControllerAnchor != null
                    ? rig.rightControllerAnchor
                    : rig.rightHandAnchor != null
                        ? rig.rightHandAnchor
                        : rig.centerEyeAnchor;

            InstallOrUpdateRightControllerLaser(rig, inputModule);
        }

        inputModule.joyPadClickButton = OVRInput.Button.One;
        EditorUtility.SetDirty(canvas);
        if (canvasRect != null)
        {
            EditorUtility.SetDirty(canvasRect);
        }
        EditorUtility.SetDirty(eventSystem);
        EditorUtility.SetDirty(inputModule);
    }

    private static void InstallOrUpdateRightControllerLaser(
        OVRCameraRig rig,
        OVRInputModule inputModule)
    {
        if (rig == null || rig.rightControllerAnchor == null)
        {
            Debug.LogWarning(
                "[PassthroughTest] Right controller anchor was not found; "
                + "the UI laser could not be installed.");
            return;
        }

        OVRControllerHelper controller =
            rig.rightControllerAnchor
                .GetComponentInChildren<OVRControllerHelper>(true);
        if (controller == null)
        {
            Debug.LogWarning(
                "[PassthroughTest] Right OVRControllerHelper was not found; "
                + "the UI laser could not be installed.");
            return;
        }

        OVRRayHelper rayHelper = controller.RayHelper;
        if (rayHelper == null)
        {
            Transform existing =
                controller.transform.Find("Right Controller Laser");
            rayHelper = existing == null
                ? null
                : existing.GetComponent<OVRRayHelper>();
        }

        if (rayHelper == null)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    OvrRayHelperPrefabPath);
            if (prefab == null)
            {
                Debug.LogError(
                    "[PassthroughTest] OVRRayHelper prefab was not found at "
                    + OvrRayHelperPrefabPath);
                return;
            }

            GameObject instance =
                PrefabUtility.InstantiatePrefab(
                    prefab,
                    controller.transform) as GameObject;
            if (instance == null)
            {
                Debug.LogError(
                    "[PassthroughTest] OVRRayHelper prefab could not be "
                    + "instantiated.");
                return;
            }

            instance.name = "Right Controller Laser";
            instance.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            rayHelper = instance.GetComponent<OVRRayHelper>();
        }

        if (rayHelper == null)
        {
            Debug.LogError(
                "[PassthroughTest] The controller laser has no "
                + "OVRRayHelper component.");
            return;
        }

        rayHelper.DefaultLength = 5f;
        if (rayHelper.Renderer != null)
        {
            rayHelper.Renderer.shadowCastingMode = ShadowCastingMode.Off;
            rayHelper.Renderer.receiveShadows = false;
            rayHelper.Renderer.lightProbeUsage = LightProbeUsage.Off;
            rayHelper.Renderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
        }

        controller.RayHelper = rayHelper;
        inputModule.rayTransform = controller.transform;
        rayHelper.gameObject.SetActive(true);
        EditorUtility.SetDirty(rayHelper);
        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(inputModule);
    }

    private static Button CreateOrUpdateToggleButton(
        Transform parent,
        string name,
        Vector2 anchoredPosition,
        out TMP_Text label)
    {
        Transform existing = parent.Find(name);
        GameObject buttonObject;
        if (existing == null)
        {
            buttonObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
        }
        else
        {
            buttonObject = existing.gameObject;
        }

        RectTransform rect =
            buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(450f, 82f);
        rect.anchoredPosition = anchoredPosition;

        Image image = GetOrAdd<Image>(buttonObject);
        image.color = new Color(0.10f, 0.48f, 0.28f, 0.95f);
        image.raycastTarget = true;
        Button button = GetOrAdd<Button>(buttonObject);
        button.targetGraphic = image;

        label = CreateOrUpdateText(
            buttonObject.transform,
            "Label",
            Vector2.zero,
            new Vector2(430f, 72f));
        label.text = name.StartsWith("Static")
            ? "STATIC PASSTHROUGH: ON"
            : "DYNAMIC PASSTHROUGH: ON";
        label.fontSize = 25f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        EditorUtility.SetDirty(button);
        EditorUtility.SetDirty(image);
        EditorUtility.SetDirty(label);
        return button;
    }

    private static TextMeshProUGUI CreateOrUpdateText(
        Transform parent,
        string name,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        Transform existing = parent.Find(name);
        GameObject textObject;
        if (existing == null)
        {
            textObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
        }
        else
        {
            textObject = existing.gameObject;
        }

        RectTransform rect =
            textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        return textObject.GetComponent<TextMeshProUGUI>();
    }

    private static void AddSceneToBuildSettings(string scenePath, bool enabled)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        for (int i = 0; i < scenes.Count; i++)
        {
            if (scenes[i].path == scenePath)
            {
                scenes[i] = new EditorBuildSettingsScene(scenePath, enabled);
                EditorBuildSettings.scenes = scenes.ToArray();
                return;
            }
        }

        scenes.Add(new EditorBuildSettingsScene(scenePath, enabled));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static void RemoveComponents<T>() where T : Component
    {
        T[] components = Object.FindObjectsByType<T>(
            FindObjectsInactive.Include);
        for (int i = 0; i < components.Length; i++)
        {
            Object.DestroyImmediate(components[i]);
        }
    }
}
