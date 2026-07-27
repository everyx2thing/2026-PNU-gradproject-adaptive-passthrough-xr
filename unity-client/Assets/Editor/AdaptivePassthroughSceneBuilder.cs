using System.Collections.Generic;
using System.IO;
using Meta.XR;
using TeamVR.AdaptivePassthrough;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public static class AdaptivePassthroughSceneBuilder
{
    public const string MockScenePath = "Assets/Scenes/DynamicRiskMock.unity";
    public const string QuestScenePath = "Assets/Scenes/SampleScene.unity";
    public const string PersonModelPath = "Assets/Models/yolov9sentis.sentis";

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
        QuestPersonDetectionRunner detectionRunner =
            GetOrAdd<QuestPersonDetectionRunner>(system);

        ModelAsset model = AssetDatabase.LoadAssetAtPath<ModelAsset>(PersonModelPath);
        var runnerObject = new SerializedObject(detectionRunner);
        runnerObject.FindProperty("controller").objectReferenceValue = controller;
        runnerObject.FindProperty("permissionCoordinator").objectReferenceValue = permission;
        runnerObject.FindProperty("cameraAccess").objectReferenceValue = cameraAccess;
        runnerObject.FindProperty("modelAsset").objectReferenceValue = model;
        runnerObject.FindProperty("confidenceThreshold").floatValue = 0.55f;
        runnerObject.FindProperty("iouThreshold").floatValue = 0.45f;
        runnerObject.FindProperty("inferenceRateHz").floatValue = 10f;
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
            QuestRiskSnapshotController snapshotController =
                GetOrAdd<QuestRiskSnapshotController>(system);
            snapshotController.Configure(
                experimentLogger,
                controller,
                detectionRunner);
            RiskSnapshotSessionLogger snapshotLogger =
                GetOrAdd<RiskSnapshotSessionLogger>(system);
            snapshotLogger.Configure(snapshotController);

            var dynamicLoggerObject =
                new SerializedObject(dynamicSessionLogger);
            dynamicLoggerObject
                .FindProperty("snapshotSequenceProviderBehaviour")
                .objectReferenceValue = snapshotController;
            dynamicLoggerObject.ApplyModifiedPropertiesWithoutUndo();

            InstallOrUpdateQuestHud(
                experimentLogger,
                snapshotController);
            EditorUtility.SetDirty(snapshotController);
            EditorUtility.SetDirty(snapshotLogger);
            EditorUtility.SetDirty(dynamicSessionLogger);
        }
        else
        {
            Debug.LogWarning(
                "[DynamicRisk] QuestRiskExperimentLogger was not found; "
                + "the compact Quest HUD could not be installed.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
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

    private static void InstallOrUpdateQuestHud(
        QuestRiskExperimentLogger experimentLogger,
        QuestRiskSnapshotController snapshotController)
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
        panelRect.sizeDelta = new Vector2(820f, 260f);
        panelRect.anchoredPosition = Vector2.zero;

        Image background = GetOrAdd<Image>(panelObject);
        background.color = new Color(0.02f, 0.03f, 0.05f, 0.82f);
        background.raycastTarget = false;

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
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.offsetMin = new Vector2(28f, 20f);
        textRect.offsetMax = new Vector2(-28f, -20f);

        TextMeshProUGUI summaryText = textObject.GetComponent<TextMeshProUGUI>();
        summaryText.text = "Initializing Quest risk HUD...";
        summaryText.fontSize = 30f;
        summaryText.alignment = TextAlignmentOptions.TopLeft;
        summaryText.color = Color.white;
        summaryText.textWrappingMode = TextWrappingModes.NoWrap;
        summaryText.overflowMode = TextOverflowModes.Ellipsis;
        summaryText.raycastTarget = false;

        QuestRiskHud hud = GetOrAdd<QuestRiskHud>(panelObject);
        hud.Configure(
            snapshotController,
            summaryText);
        EditorUtility.SetDirty(experimentLogger);
        EditorUtility.SetDirty(hud);
        EditorUtility.SetDirty(summaryText);
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
}
