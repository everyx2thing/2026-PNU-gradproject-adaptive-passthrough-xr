using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class ExperimentGameIntegrationTests
    {
        private const string PrefabPath =
            "Assets/Prefabs/Experiment/ExperimentGameRoot.prefab";
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

        [TestCase(
            0,
            ExperimentPresentationOverride.SuppressAll,
            BoundaryVisibilityOverride.ForceVisible)]
        [TestCase(
            1,
            ExperimentPresentationOverride.StaticOnly,
            BoundaryVisibilityOverride.ForceSuppressed)]
        [TestCase(
            2,
            ExperimentPresentationOverride.StaticAndDynamic,
            BoundaryVisibilityOverride.ForceSuppressed)]
        public void ExperimentConditionsMapToNonPersistentOverrides(
            int condition,
            ExperimentPresentationOverride presentation,
            BoundaryVisibilityOverride boundary)
        {
            Assert.That(
                ExperimentRuntimeOverrideResolver.TryResolve(
                    condition,
                    out ExperimentPresentationOverride actualPresentation,
                    out BoundaryVisibilityOverride actualBoundary),
                Is.True);
            Assert.That(actualPresentation, Is.EqualTo(presentation));
            Assert.That(actualBoundary, Is.EqualTo(boundary));
        }

        [Test]
        public void RoundNumbersAndMenuMappingAreFixed()
        {
            Type conditionType = Type.GetType(
                "TeamVR.Experiment.ExperimentCondition, Assembly-CSharp");
            Type menuType = Type.GetType(
                "TeamVR.Experiment.ExperimentMenuController, Assembly-CSharp");
            Assert.That(conditionType, Is.Not.Null);
            Assert.That(menuType, Is.Not.Null);
            Assert.That(
                Convert.ToInt32(Enum.Parse(conditionType, "GuardianDefault")),
                Is.Zero);
            Assert.That(
                Convert.ToInt32(Enum.Parse(conditionType, "StaticOnly")),
                Is.EqualTo(1));
            Assert.That(
                Convert.ToInt32(Enum.Parse(
                    conditionType,
                    "StaticAndDynamic")),
                Is.EqualTo(2));
            MethodInfo mapping = menuType.GetMethod(
                "ConditionForRoundIndex",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(
                Convert.ToInt32(mapping.Invoke(null, new object[] { 0 })),
                Is.Zero);
            Assert.That(
                Convert.ToInt32(mapping.Invoke(null, new object[] { 1 })),
                Is.EqualTo(1));
            Assert.That(
                Convert.ToInt32(mapping.Invoke(null, new object[] { 2 })),
                Is.EqualTo(2));
            TargetInvocationException exception = Assert.Throws<
                TargetInvocationException>(
                () => mapping.Invoke(null, new object[] { 3 }));
            Assert.That(
                exception.InnerException,
                Is.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void ProjectileTargetsLatestHeadPositionOnce()
        {
            Vector3 latestHead = new Vector3(2f, 1.7f, -3f);
            Type spawnerType = Type.GetType(
                "TeamVR.Experiment.ExperimentBallSpawner, Assembly-CSharp");
            Type ballType = Type.GetType(
                "TeamVR.Experiment.ExperimentBall, Assembly-CSharp");
            Type ballKindType = Type.GetType(
                "TeamVR.Experiment.BallType, Assembly-CSharp");
            Assert.That(spawnerType, Is.Not.Null);
            Assert.That(ballType, Is.Not.Null);
            Assert.That(ballKindType, Is.Not.Null);
            Vector3 target = (Vector3)spawnerType.GetMethod(
                "ResolveTargetPosition",
                BindingFlags.Public | BindingFlags.Static).Invoke(
                    null,
                    new object[] { latestHead, -0.2f });
            Assert.That(target, Is.EqualTo(latestHead + Vector3.down * 0.2f));

            var body = new GameObject("projectile-body-reference");
            var ballObject = new GameObject("straight-projectile");
            try
            {
                MonoBehaviour ball =
                    ballObject.AddComponent(ballType) as MonoBehaviour;
                object mustAvoid = Enum.Parse(ballKindType, "MustAvoid");
                ballType.GetMethod("Configure").Invoke(
                    ball,
                    new object[]
                    {
                        Vector3.zero,
                        target,
                        1f,
                        mustAvoid,
                        body.transform,
                        null
                    });
                FieldInfo directionField = ballType.GetField(
                    "travelDirection",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Vector3 initial = (Vector3)directionField.GetValue(ball);
                body.transform.position = new Vector3(20f, 4f, 10f);
                Vector3 afterMove = (Vector3)directionField.GetValue(ball);
                Assert.That(afterMove, Is.EqualTo(initial));
            }
            finally
            {
                Object.DestroyImmediate(ballObject);
                Object.DestroyImmediate(body);
            }
        }

        [Test]
        public void ScoreHudStaysOnAnchorAndOnlyYawsTowardViewer()
        {
            Type scoreHudType = Type.GetType(
                "TeamVR.Experiment.ExperimentScoreHud, Assembly-CSharp");
            Assert.That(scoreHudType, Is.Not.Null);
            var anchor = new GameObject("score-anchor");
            var viewer = new GameObject("score-viewer");
            var hudObject = new GameObject("score-hud");
            try
            {
                anchor.transform.position = new Vector3(2f, 3f, 4f);
                viewer.transform.position = new Vector3(-1f, 1.5f, 0f);
                MonoBehaviour hud =
                    hudObject.AddComponent(scoreHudType) as MonoBehaviour;
                Invoke(
                    hud,
                    "ConfigureAnchor",
                    anchor.transform,
                    viewer.transform);
                Invoke(hud, "FaceViewerWithoutMoving");
                Assert.That(
                    hud.transform.position,
                    Is.EqualTo(anchor.transform.position));
                Vector3 expectedForward = Vector3.ProjectOnPlane(
                    anchor.transform.position - viewer.transform.position,
                    Vector3.up).normalized;
                Assert.That(
                    Vector3.Angle(hud.transform.forward, expectedForward),
                    Is.LessThan(0.01f));

                viewer.transform.position = new Vector3(8f, 6f, -2f);
                Invoke(hud, "FaceViewerWithoutMoving");
                Assert.That(
                    hud.transform.position,
                    Is.EqualTo(anchor.transform.position));
                expectedForward = Vector3.ProjectOnPlane(
                    anchor.transform.position - viewer.transform.position,
                    Vector3.up).normalized;
                Assert.That(
                    Vector3.Angle(hud.transform.forward, expectedForward),
                    Is.LessThan(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(hudObject);
                Object.DestroyImmediate(viewer);
                Object.DestroyImmediate(anchor);
            }
        }

        [Test]
        public void UnknownConditionRestoresConfiguredPolicy()
        {
            Assert.That(
                ExperimentRuntimeOverrideResolver.TryResolve(
                    99,
                    out ExperimentPresentationOverride presentation,
                    out BoundaryVisibilityOverride boundary),
                Is.False);
            Assert.That(
                presentation,
                Is.EqualTo(ExperimentPresentationOverride.UseUserSettings));
            Assert.That(
                boundary,
                Is.EqualTo(BoundaryVisibilityOverride.UseConfiguredPolicy));
        }

        [Test]
        public void ScoreRulesRemainStable()
        {
            Assert.That(ExperimentScoreRules.ApplyShotHit(200, true), Is.EqualTo(300));
            Assert.That(ExperimentScoreRules.ApplyShotHit(201, false), Is.EqualTo(100));
            Assert.That(ExperimentScoreRules.ApplyBodyHit(200, true), Is.EqualTo(100));
            Assert.That(ExperimentScoreRules.ApplyBodyHit(200, false), Is.Zero);
        }

        [TestCase("ProjectileScheduleSet_ConditionA.asset")]
        [TestCase("ProjectileScheduleSet_ConditionB.asset")]
        [TestCase("ProjectileScheduleSet_ConditionC.asset")]
        public void EveryConditionHasOrderedSeventyTwoEntrySchedule(
            string fileName)
        {
            string path = "Assets/Scripts/Experiment/Data/" + fileName;
            Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            Assert.That(asset, Is.Not.Null, path);
            var serialized = new SerializedObject(asset);
            SerializedProperty entries = serialized.FindProperty("entries");
            Assert.That(entries, Is.Not.Null);
            Assert.That(entries.arraySize, Is.EqualTo(72));

            float previous = -1f;
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty item = entries.GetArrayElementAtIndex(i);
                float current = item.FindPropertyRelative(
                    "spawnTimeSeconds").floatValue;
                Assert.That(current, Is.GreaterThanOrEqualTo(previous));
                previous = current;
            }
        }

        [Test]
        public void RuntimePlanUsesFortyFourProjectilesAcrossThreeMinutes()
        {
            Type planner = Type.GetType(
                "TeamVR.Experiment.ExperimentRoundSchedulePlanner, "
                + "Assembly-CSharp");
            Assert.That(planner, Is.Not.Null);

            const BindingFlags flags = BindingFlags.Public
                | BindingFlags.Static;
            int plannedCount = (int)planner.GetField(
                "PlannedProjectileCount",
                flags).GetRawConstantValue();
            MethodInfo getTime = planner.GetMethod(
                "GetSpawnTimeSeconds",
                flags);
            MethodInfo getSourceIndex = planner.GetMethod(
                "GetSourceIndex",
                flags);

            Assert.That(plannedCount, Is.EqualTo(44));
            var times = new float[plannedCount];
            var sourceIndices = new int[plannedCount];
            for (int i = 0; i < plannedCount; i++)
            {
                times[i] = Convert.ToSingle(
                    getTime.Invoke(null, new object[] { i }));
                sourceIndices[i] = Convert.ToInt32(
                    getSourceIndex.Invoke(null, new object[] { i, 72 }));
            }

            Assert.That(times, Is.Ordered.Ascending);
            Assert.That(times[0], Is.EqualTo(5f));
            Assert.That(times[plannedCount - 1], Is.EqualTo(175f));
            Assert.That(times.Count(value => value == 60f), Is.EqualTo(2));
            Assert.That(times.Count(value => value == 120f), Is.EqualTo(3));
            Assert.That(times.Count(value => value == 175f), Is.EqualTo(3));

            int[] phaseCounts = new int[6];
            foreach (float time in times)
            {
                int phase = Mathf.Clamp(
                    Mathf.CeilToInt(time / 30f) - 1,
                    0,
                    phaseCounts.Length - 1);
                phaseCounts[phase]++;
            }

            Assert.That(phaseCounts, Is.EqualTo(new[] { 6, 7, 7, 8, 8, 8 }));
            Assert.That(sourceIndices.Take(6), Is.EqualTo(Enumerable.Range(0, 6)));
            Assert.That(sourceIndices.Skip(plannedCount - 3),
                Is.EqualTo(new[] { 69, 70, 71 }));
            Assert.That(sourceIndices, Is.Ordered.Ascending);
        }

        [Test]
        public void PrefabUsesFixedThreeMinuteRoundDuration()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPath);
            MonoBehaviour round = prefab
                .GetComponentsInChildren<MonoBehaviour>(true)
                .First(item => item != null
                    && item.GetType().FullName
                        == "TeamVR.Experiment.ExperimentRoundController");
            var serialized = new SerializedObject(round);
            Assert.That(
                serialized.FindProperty("maxRoundSeconds").floatValue,
                Is.EqualTo(180f));
        }

        [Test]
        public void PrefabContainsCompleteGameModuleAndNoMissingScripts()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            string[] requiredTypes =
            {
                "TeamVR.Experiment.ExperimentGameRoot",
                "TeamVR.Experiment.ExperimentRoundController",
                "TeamVR.Experiment.ExperimentBallSpawner",
                "TeamVR.Experiment.PassthroughConditionSwitcher",
                "TeamVR.Experiment.ExperimentGun",
                "TeamVR.Experiment.ExperimentMenuController",
                "TeamVR.Experiment.ExperimentScoreSystem",
                "TeamVR.Experiment.ExperimentScoreHud",
                "TeamVR.Experiment.ExperimentThreatIndicatorController"
            };
            MonoBehaviour[] behaviours =
                prefab.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (string typeName in requiredTypes)
            {
                Assert.That(
                    behaviours.Count(item => item != null
                        && item.GetType().FullName == typeName),
                    Is.EqualTo(1),
                    typeName);
            }

            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        child.gameObject),
                    Is.Zero,
                    child.name);
            }

            MonoBehaviour spawner = behaviours.First(item =>
                item.GetType().FullName
                    == "TeamVR.Experiment.ExperimentBallSpawner");
            var serializedSpawner = new SerializedObject(spawner);
            string[] pointFields =
            {
                "leftSpawnPoint", "leftMidSpawnPoint",
                "frontLeftSpawnPoint", "frontLeftMidSpawnPoint",
                "frontSpawnPoint", "frontRightMidSpawnPoint",
                "frontRightSpawnPoint", "rightMidSpawnPoint",
                "rightSpawnPoint", "spawnLayoutOrigin",
                "bombBallPrefab", "targetBallPrefab"
            };
            foreach (string field in pointFields)
            {
                Assert.That(
                    serializedSpawner.FindProperty(field).objectReferenceValue,
                    Is.Not.Null,
                    field);
            }

            MonoBehaviour scoreHud = behaviours.First(item =>
                item != null
                && item.GetType().FullName
                    == "TeamVR.Experiment.ExperimentScoreHud");
            Assert.That(
                scoreHud.GetComponent<WorldSpacePanelPlacementController>(),
                Is.Null);
            var scoreSerialized = new SerializedObject(scoreHud);
            Transform scoreAnchor = scoreSerialized
                .FindProperty("scoreAnchor")
                .objectReferenceValue as Transform;
            Assert.That(scoreAnchor, Is.Not.Null);
            Assert.That(
                scoreAnchor.name,
                Is.EqualTo("ScoreAnchor_PurpleTowerTop"));
            Transform tower = prefab.GetComponentsInChildren<Transform>(true)
                .First(item => item.name == "tower-round-build-d (1)");
            Renderer[] towerRenderers =
                tower.GetComponentsInChildren<Renderer>(true);
            Bounds towerBounds = towerRenderers[0].bounds;
            for (int i = 1; i < towerRenderers.Length; i++)
            {
                towerBounds.Encapsulate(towerRenderers[i].bounds);
            }
            Assert.That(
                scoreAnchor.position.y,
                Is.EqualTo(towerBounds.max.y + 0.10f).Within(0.01f));
        }

        [Test]
        public void PresentationOverridesExposeEffectiveValuesWithoutChangingSettings()
        {
            var root = new GameObject("presentation-effective-settings-test");
            try
            {
                Type controllerType = Type.GetType(
                    "SelectivePassthroughController, Assembly-CSharp");
                Assert.That(controllerType, Is.Not.Null);
                MonoBehaviour controller =
                    root.AddComponent(controllerType) as MonoBehaviour;
                Invoke(controller, "SetStaticFeatureEnabled", false);
                Invoke(controller, "SetDynamicFeatureEnabled", true);
                Invoke(
                    controller,
                    "SetStaticChannelEnabled",
                    StaticRiskChannelMask.Head,
                    false);
                Invoke(
                    controller,
                    "SetFeedbackMode",
                    SafetyFeedbackMode.RedBorderAndHaptics);

                Invoke(
                    controller,
                    "SetExperimentPresentationOverride",
                    ExperimentPresentationOverride.StaticOnly);
                Assert.That(ReadProperty(controller, "StaticFeatureEnabled"), Is.False);
                Assert.That(ReadProperty(controller, "DynamicFeatureEnabled"), Is.True);
                Assert.That(
                    ReadProperty(controller, "FeedbackMode"),
                    Is.EqualTo(SafetyFeedbackMode.RedBorderAndHaptics));
                Assert.That(
                    ReadProperty(controller, "EffectiveStaticFeatureEnabled"),
                    Is.True);
                Assert.That(
                    ReadProperty(controller, "EffectiveDynamicFeatureEnabled"),
                    Is.False);
                Assert.That(
                    ReadProperty(controller, "EffectiveStaticChannels"),
                    Is.EqualTo(StaticRiskChannelMask.All));
                Assert.That(
                    ReadProperty(controller, "EffectiveFeedbackMode"),
                    Is.EqualTo(SafetyFeedbackMode.Passthrough));

                Invoke(
                    controller,
                    "SetExperimentPresentationOverride",
                    ExperimentPresentationOverride.StaticAndDynamic);
                Assert.That(
                    ReadProperty(controller, "EffectiveStaticFeatureEnabled"),
                    Is.True);
                Assert.That(
                    ReadProperty(controller, "EffectiveDynamicFeatureEnabled"),
                    Is.True);

                Invoke(
                    controller,
                    "SetExperimentPresentationOverride",
                    ExperimentPresentationOverride.UseUserSettings);
                Assert.That(
                    ReadProperty(controller, "EffectiveStaticFeatureEnabled"),
                    Is.False);
                Assert.That(
                    ReadProperty(controller, "EffectiveDynamicFeatureEnabled"),
                    Is.True);
                Assert.That(
                    ReadProperty(controller, "EffectiveFeedbackMode"),
                    Is.EqualTo(SafetyFeedbackMode.RedBorderAndHaptics));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BoundaryManifestRemovesForcedBoundarylessButKeepsPermission()
        {
            string manifestPath = Path.Combine(
                Application.dataPath,
                "Plugins/Android/AndroidManifest.xml");
            string manifest = File.ReadAllText(manifestPath);
            Assert.That(
                manifest,
                Does.Not.Contain("com.oculus.feature.BOUNDARYLESS_APP"));
            Assert.That(
                manifest,
                Does.Contain("com.oculus.permission.BOUNDARY_VISIBILITY"));

            string setupPath = Path.Combine(
                Application.dataPath,
                "Editor/AdaptivePassthroughBoundarySetup.cs");
            string setupSource = File.ReadAllText(setupPath);
            Assert.That(
                setupSource,
                Does.Contain("RemoveFullBoundarylessManifestFeature();"));
            Assert.That(
                setupSource,
                Does.Not.Contain("EnsureFullBoundarylessManifestFeature();"));
        }

        [Test]
        public void SampleSceneHasOneGameAndOneCoreRuntime()
        {
            Scene existing = SceneManager.GetSceneByPath(SampleScenePath);
            bool alreadyLoaded = existing.IsValid() && existing.isLoaded;
            Scene scene = alreadyLoaded
                ? existing
                : EditorSceneManager.OpenScene(
                    SampleScenePath,
                    OpenSceneMode.Additive);
            try
            {
                MonoBehaviour[] behaviours =
                    Object.FindObjectsByType<MonoBehaviour>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None);
                AssertTypeCount(
                    behaviours,
                    "TeamVR.Experiment.ExperimentGameRoot",
                    1);
                AssertTypeCount(behaviours, "OVRCameraRig", 1);
                AssertTypeCount(behaviours, "OVRManager", 1);
                AssertTypeCount(
                    behaviours,
                    "SelectivePassthroughController",
                    1);
                AssertTypeCount(
                    behaviours,
                    "StaticPassthroughPolicyController",
                    1);
                AssertTypeCount(
                    behaviours,
                    "DynamicPassthroughPolicyController",
                    1);
                AssertTypeCount(
                    behaviours,
                    "TeamVR.AdaptivePassthrough.DynamicRiskSessionLogger",
                    1);
                Assert.That(scene.GetRootGameObjects().Count(
                    root => root.name == "Quest UI EventSystem"), Is.EqualTo(1));

                WorldSpacePanelPlacementController[] placements =
                    Object.FindObjectsByType<
                        WorldSpacePanelPlacementController>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None);
                WorldSpacePanelPlacementController[] bTogglePanels =
                    placements.Where(item => item.ToggleVisibilityWithB)
                        .ToArray();
                Assert.That(bTogglePanels.Length, Is.EqualTo(1));
                Assert.That(
                    bTogglePanels[0].gameObject.name,
                    Is.EqualTo("DistanceCanvas"));

                string[] enabledScenes = EditorBuildSettings.scenes
                    .Where(item => item.enabled)
                    .Select(item => item.path)
                    .ToArray();
                Assert.That(
                    enabledScenes,
                    Is.EqualTo(new[] { SampleScenePath }));
            }
            finally
            {
                if (!alreadyLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void ConditionSwitcherNeverDisablesCoreComponents()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Scripts/Experiment/PassthroughConditionSwitcher.cs");
            string source = File.ReadAllText(path);
            Assert.That(source, Does.Not.Contain("staticPolicy.enabled"));
            Assert.That(source, Does.Not.Contain("dynamicPolicy.enabled"));
            Assert.That(source, Does.Not.Contain("selectivePassthrough.enabled"));
            Assert.That(source, Does.Not.Contain("logger.enabled"));
        }

        private static void AssertTypeCount(
            MonoBehaviour[] behaviours,
            string fullName,
            int expected)
        {
            int count = behaviours.Count(item => item != null
                && (item.GetType().FullName == fullName
                    || item.GetType().Name == fullName));
            Assert.That(count, Is.EqualTo(expected), fullName);
        }

        private static object ReadProperty(object target, string name)
        {
            return target.GetType().GetProperty(name).GetValue(target);
        }

        private static object Invoke(
            object target,
            string method,
            params object[] arguments)
        {
            Type[] argumentTypes = arguments
                .Select(argument => argument.GetType())
                .ToArray();
            return target.GetType().GetMethod(method, argumentTypes).Invoke(
                target,
                arguments);
        }
    }
}
