using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
            BoundaryVisibilityOverride.ForceSuppressed)]
        [TestCase(
            1,
            ExperimentPresentationOverride.SuppressAll,
            BoundaryVisibilityOverride.ForceVisible)]
        [TestCase(
            2,
            ExperimentPresentationOverride.UseUserSettings,
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
    }
}
