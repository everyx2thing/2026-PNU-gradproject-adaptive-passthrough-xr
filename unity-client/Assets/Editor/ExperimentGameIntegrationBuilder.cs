using System;
using System.Linq;
using TeamVR.AdaptivePassthrough;
using TeamVR.Experiment;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ExperimentGameIntegrationBuilder
{
    private const string SourceScene =
        "Assets/Scenes/ExperimentGameTest.unity";
    private const string TargetScene = "Assets/Scenes/SampleScene.unity";
    private const string PrefabPath =
        "Assets/Prefabs/Experiment/ExperimentGameRoot.prefab";

    [InitializeOnLoadMethod]
    private static void RebuildMissingIntegrationOnImport()
    {
        EditorApplication.delayCall += () =>
        {
            if (IntegrationRequiresRebuild())
            {
                RebuildIntegratedGamePrefab();
            }
        };
    }

    [MenuItem("Tools/Experiment/Rebuild Integrated Game Prefab %#i")]
    public static void RebuildIntegratedGamePrefab()
    {
        Scene source = EditorSceneManager.OpenScene(
            SourceScene,
            OpenSceneMode.Single);

        GameObject environment = FindSceneObject(source, "Environment");
        GameObject systems = FindSceneObject(source, "Experiment Systems");
        GameObject menu = FindSceneObject(source, "ExperimentMenuCanvas");
        GameObject score = FindSceneObject(source, "ScoreHud");
        GameObject purpleTower = FindSceneObject(
            source,
            "tower-round-build-d (1)");
        ExperimentThreatIndicatorController threat =
            UnityEngine.Object.FindFirstObjectByType<
                ExperimentThreatIndicatorController>(
                FindObjectsInactive.Include);

        Require(environment, "Environment");
        Require(systems, "Experiment Systems");
        Require(menu, "ExperimentMenuCanvas");
        Require(score, "ScoreHud");
        Require(purpleTower, "tower-round-build-d (1)");
        Require(threat, "ExperimentThreatIndicator");

        var root = new GameObject("ExperimentGameRoot");
        environment.transform.SetParent(root.transform, true);
        systems.transform.SetParent(root.transform, true);
        menu.transform.SetParent(root.transform, true);
        score.transform.SetParent(root.transform, true);
        threat.transform.SetParent(root.transform, true);

        ExperimentRoundController round =
            systems.GetComponent<ExperimentRoundController>();
        ExperimentBallSpawner spawner =
            systems.GetComponent<ExperimentBallSpawner>();
        PassthroughConditionSwitcher switcher =
            systems.GetComponent<PassthroughConditionSwitcher>();
        ExperimentScoreSystem scoreSystem =
            systems.GetComponent<ExperimentScoreSystem>();
        ExperimentGun gun =
            systems.GetComponentInChildren<ExperimentGun>(true);
        ExperimentMenuController menuController =
            menu.GetComponent<ExperimentMenuController>();
        ExperimentScoreHud scoreHud = score.GetComponent<ExperimentScoreHud>();

        Require(round, nameof(ExperimentRoundController));
        Require(spawner, nameof(ExperimentBallSpawner));
        Require(switcher, nameof(PassthroughConditionSwitcher));
        Require(scoreSystem, nameof(ExperimentScoreSystem));
        Require(gun, nameof(ExperimentGun));
        Require(menuController, nameof(ExperimentMenuController));
        Require(scoreHud, nameof(ExperimentScoreHud));

        round.Configure(switcher, spawner);
        SetFloat(
            round,
            "maxRoundSeconds",
            ExperimentRoundController.DefaultRoundDurationSeconds);
        SetFloat(round, "guardianReadyTimeoutSeconds", 2f);
        SetObjectReference(scoreSystem, "roundController", round);
        SetObjectReference(gun, "roundController", round);
        SetObjectReference(threat, "ballSpawner", spawner);
        SetObjectReference(menuController, "roundController", round);
        SetObjectReference(scoreHud, "scoreSystem", scoreSystem);

        Transform spawnPoints = systems.transform.Find("SpawnPoints");
        Require(spawnPoints, "SpawnPoints");
        SetObjectReference(spawner, "spawnLayoutOrigin", spawnPoints);
        SetObjectReference(
            spawner,
            "leftSpawnPoint",
            FindRequiredChild(spawnPoints, "LeftSpawnPoint"));
        SetObjectReference(
            spawner,
            "leftMidSpawnPoint",
            FindRequiredChild(spawnPoints, "LeftMidSpawnPoint"));
        SetObjectReference(
            spawner,
            "frontLeftSpawnPoint",
            FindRequiredChild(spawnPoints, "FrontLeftSpawnPoint"));
        SetObjectReference(
            spawner,
            "frontLeftMidSpawnPoint",
            FindRequiredChild(spawnPoints, "FrontLeftMidSpawnPoint"));
        SetObjectReference(
            spawner,
            "frontSpawnPoint",
            FindRequiredChild(spawnPoints, "FrontSpawnPoint"));
        SetObjectReference(
            spawner,
            "frontRightMidSpawnPoint",
            FindRequiredChild(spawnPoints, "FrontRightMidSpawnPoint"));
        SetObjectReference(
            spawner,
            "frontRightSpawnPoint",
            FindRequiredChild(spawnPoints, "FrontRightSpawnPoint"));
        SetObjectReference(
            spawner,
            "rightMidSpawnPoint",
            FindRequiredChild(spawnPoints, "RightMidSpawnPoint"));
        SetObjectReference(
            spawner,
            "rightSpawnPoint",
            FindRequiredChild(spawnPoints, "RightSpawnPoint"));

        TMP_Text operatorStatus = BuildOperatorStatus(menuController, menu);
        SetObjectReference(
            menuController,
            "operatorStatusText",
            operatorStatus);

        ConfigurePanelPlacement(menu, 1.15f, 0.32f, 0.75f);
        Transform scoreAnchor = CreateScoreAnchor(purpleTower, score);
        SetObjectReference(scoreHud, "scoreAnchor", scoreAnchor);
        SetObjectReference(scoreHud, "viewer", null);

        ExperimentGameRoot gameRoot = root.AddComponent<ExperimentGameRoot>();
        SetObjectReference(gameRoot, "roundController", round);
        SetObjectReference(gameRoot, "ballSpawner", spawner);
        SetObjectReference(gameRoot, "menuController", menuController);
        SetObjectReference(gameRoot, "gun", gun);
        SetObjectReference(gameRoot, "scoreSystem", scoreSystem);
        SetObjectReference(gameRoot, "scoreHud", scoreHud);
        SetObjectReference(gameRoot, "threatIndicator", threat);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        if (prefab == null)
        {
            throw new InvalidOperationException(
                "Failed to save " + PrefabPath + ".");
        }

        Scene target = EditorSceneManager.OpenScene(
            TargetScene,
            OpenSceneMode.Single);
        ExperimentGameRoot[] oldRoots =
            UnityEngine.Object.FindObjectsByType<ExperimentGameRoot>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        for (int i = 0; i < oldRoots.Length; i++)
        {
            UnityEngine.Object.DestroyImmediate(oldRoots[i].gameObject);
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
            prefab,
            target);
        instance.name = "ExperimentGameRoot";
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;

        EnsureSingle<OVRCameraRig>();
        EnsureSingle<OVRManager>();
        EnsureSingle<UnityEngine.EventSystems.EventSystem>();
        EnsureSingle<SelectivePassthroughController>();
        EnsureSingle<StaticPassthroughPolicyController>();
        EnsureSingle<DynamicPassthroughPolicyController>();
        EnsureSingle<DynamicRiskSessionLogger>();

        EditorSceneManager.MarkSceneDirty(target);
        EditorSceneManager.SaveScene(target);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeGameObject = instance;
        Debug.Log(
            "[Experiment] Integrated game prefab rebuilt and added to SampleScene.");
    }

    private static TMP_Text BuildOperatorStatus(
        ExperimentMenuController controller,
        GameObject menuCanvas)
    {
        Transform existing = menuCanvas.transform.Find("OperatorStatusText");
        if (existing != null)
        {
            UnityEngine.Object.DestroyImmediate(existing.gameObject);
        }

        var statusObject = new GameObject(
            "OperatorStatusText",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        statusObject.transform.SetParent(menuCanvas.transform, false);
        RectTransform rect = statusObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 18f);
        rect.sizeDelta = new Vector2(760f, 72f);

        TextMeshProUGUI text = statusObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = 24f;
        text.color = new Color(1f, 0.48f, 0.38f, 1f);
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        text.text = string.Empty;
        return text;
    }

    private static void ConfigurePanelPlacement(
        GameObject target,
        float distance,
        float height,
        float scale)
    {
        WorldSpacePanelPlacementController placement =
            target.GetComponent<WorldSpacePanelPlacementController>();
        Require(placement, target.name + " placement controller");
        var serialized = new SerializedObject(placement);
        serialized.FindProperty("distanceMeters").floatValue = distance;
        serialized.FindProperty("heightMeters").floatValue = height;
        serialized.FindProperty("scaleMultiplier").floatValue = scale;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Transform CreateScoreAnchor(
        GameObject purpleTower,
        GameObject score)
    {
        Renderer[] renderers = purpleTower.GetComponentsInChildren<Renderer>(
            true);
        if (renderers.Length == 0)
        {
            throw new InvalidOperationException(
                "The purple tower has no Renderer bounds.");
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        Transform oldAnchor = purpleTower.transform.Find(
            "ScoreAnchor_PurpleTowerTop");
        if (oldAnchor != null)
        {
            UnityEngine.Object.DestroyImmediate(oldAnchor.gameObject);
        }

        var anchorObject = new GameObject("ScoreAnchor_PurpleTowerTop");
        Transform anchor = anchorObject.transform;
        anchor.SetParent(purpleTower.transform, true);
        anchor.position = new Vector3(
            bounds.center.x,
            bounds.max.y + 0.10f,
            bounds.center.z);
        anchor.rotation = Quaternion.identity;

        WorldSpacePanelPlacementController placement =
            score.GetComponent<WorldSpacePanelPlacementController>();
        if (placement != null)
        {
            UnityEngine.Object.DestroyImmediate(placement);
        }

        score.transform.SetParent(anchor, true);
        score.transform.position = anchor.position;
        score.transform.rotation = Quaternion.identity;
        return anchor;
    }

    private static void SetObjectReference(
        UnityEngine.Object target,
        string field,
        UnityEngine.Object value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            throw new MissingFieldException(target.GetType().Name, field);
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetFloat(
        UnityEngine.Object target,
        string field,
        float value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            throw new MissingFieldException(target.GetType().Name, field);
        }

        property.floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject FindSceneObject(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            Transform match = transforms.FirstOrDefault(
                candidate => candidate.name == name);
            if (match != null)
            {
                return match.gameObject;
            }
        }

        return null;
    }

    private static Transform FindRequiredChild(Transform root, string name)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        Transform match = transforms.FirstOrDefault(
            candidate => candidate.name == name);
        Require(match, name);
        return match;
    }

    private static bool IntegrationRequiresRebuild()
    {
        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            return true;
        }

        ExperimentBallSpawner spawner =
            prefab.GetComponentInChildren<ExperimentBallSpawner>(true);
        if (spawner == null)
        {
            return true;
        }

        var serialized = new SerializedObject(spawner);
        string[] requiredFields =
        {
            "leftSpawnPoint",
            "leftMidSpawnPoint",
            "frontLeftSpawnPoint",
            "frontLeftMidSpawnPoint",
            "frontSpawnPoint",
            "frontRightMidSpawnPoint",
            "frontRightSpawnPoint",
            "rightMidSpawnPoint",
            "rightSpawnPoint",
            "spawnLayoutOrigin"
        };
        for (int i = 0; i < requiredFields.Length; i++)
        {
            SerializedProperty property =
                serialized.FindProperty(requiredFields[i]);
            if (property == null || property.objectReferenceValue == null)
            {
                return true;
            }
        }

        ExperimentScoreHud scoreHud =
            prefab.GetComponentInChildren<ExperimentScoreHud>(true);
        if (scoreHud == null
            || scoreHud.GetComponent<WorldSpacePanelPlacementController>()
                != null)
        {
            return true;
        }

        var scoreSerialized = new SerializedObject(scoreHud);
        SerializedProperty scoreAnchor =
            scoreSerialized.FindProperty("scoreAnchor");
        if (scoreAnchor == null
            || scoreAnchor.objectReferenceValue == null
            || scoreAnchor.objectReferenceValue.name
                != "ScoreAnchor_PurpleTowerTop")
        {
            return true;
        }

        return false;
    }

    private static void EnsureSingle<T>() where T : UnityEngine.Object
    {
        int count = UnityEngine.Object.FindObjectsByType<T>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None).Length;
        if (count != 1)
        {
            throw new InvalidOperationException(
                typeof(T).Name + " count must be 1, but was " + count + ".");
        }
    }

    private static void Require(UnityEngine.Object value, string description)
    {
        if (value == null)
        {
            throw new InvalidOperationException(
                "Required experiment object is missing: " + description);
        }
    }
}
