using Meta.XR;
using NUnit.Framework;
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
        private const string QuestScenePath = "Assets/Scenes/SampleScene.unity";

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
                QuestCameraPermissionCoordinator permission =
                    FindInScene<QuestCameraPermissionCoordinator>(scene);

                Assert.That(controller, Is.Not.Null);
                Assert.That(runner, Is.Not.Null);
                Assert.That(cameraAccess, Is.Not.Null);
                Assert.That(permission, Is.Not.Null);

                var serializedRunner = new SerializedObject(runner);
                Assert.That(
                    serializedRunner.FindProperty("modelAsset").objectReferenceValue,
                    Is.Not.Null);
                Assert.That(
                    serializedRunner.FindProperty("cameraAccess").objectReferenceValue,
                    Is.SameAs(cameraAccess));
                Assert.That(
                    serializedRunner.FindProperty("controller").objectReferenceValue,
                    Is.SameAs(controller));
                Assert.That(
                    serializedRunner.FindProperty("confidenceThreshold").floatValue,
                    Is.EqualTo(0.55f).Within(0.0001f));
                Assert.That(
                    serializedRunner.FindProperty("boxesAreNormalized").boolValue,
                    Is.False);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void QuestSceneUsesSingleCompactHudAndNonOverlappingDebugMode()
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

                int hudCount = 0;
                GameObject legacyDistance = null;
                GameObject legacyRisk = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    MonoBehaviour[] behaviours =
                        root.GetComponentsInChildren<MonoBehaviour>(true);
                    for (int i = 0; i < behaviours.Length; i++)
                    {
                        if (behaviours[i] != null
                            && behaviours[i].GetType().Name == "QuestRiskHud")
                        {
                            hudCount++;
                        }
                    }

                    Transform distance = FindChild(root.transform, "DistanceText");
                    Transform risk = FindChild(root.transform, "RiskText");
                    if (distance != null) legacyDistance = distance.gameObject;
                    if (risk != null) legacyRisk = risk.gameObject;
                }

                Assert.That(hudCount, Is.EqualTo(1));
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
        public void QuestSceneUsesOneRiskSnapshotProducerForHudAndLogs()
        {
            Scene scene = EditorSceneManager.OpenScene(
                QuestScenePath,
                OpenSceneMode.Additive);
            try
            {
                MonoBehaviour snapshotController = null;
                MonoBehaviour snapshotLogger = null;
                MonoBehaviour hud = null;
                int snapshotControllerCount = 0;
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
                            case "QuestRiskSnapshotController":
                                snapshotController = behaviour;
                                snapshotControllerCount++;
                                break;
                            case "RiskSnapshotSessionLogger":
                                snapshotLogger = behaviour;
                                break;
                            case "QuestRiskHud":
                                hud = behaviour;
                                break;
                        }
                    }
                }

                Assert.That(snapshotControllerCount, Is.EqualTo(1));
                Assert.That(snapshotLogger, Is.Not.Null);
                Assert.That(hud, Is.Not.Null);
                Assert.That(
                    new SerializedObject(hud)
                        .FindProperty("snapshotController")
                        .objectReferenceValue,
                    Is.SameAs(snapshotController));
                Assert.That(
                    new SerializedObject(snapshotLogger)
                        .FindProperty("snapshotController")
                        .objectReferenceValue,
                    Is.SameAs(snapshotController));

                DynamicRiskSessionLogger dynamicLogger =
                    FindInScene<DynamicRiskSessionLogger>(scene);
                Assert.That(dynamicLogger, Is.Not.Null);
                Assert.That(
                    new SerializedObject(dynamicLogger)
                        .FindProperty("snapshotSequenceProviderBehaviour")
                        .objectReferenceValue,
                    Is.SameAs(snapshotController));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
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
