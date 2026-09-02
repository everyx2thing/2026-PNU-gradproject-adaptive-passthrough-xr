using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TeamVR.AdaptivePassthrough.PlayModeTests
{
    public sealed class ExperimentGamePlayModeTests
    {
        [UnityTest]
        public IEnumerator ConditionsOnlyOverrideOutputAndBoundaryRequest()
        {
            Type presentationType = FindType("SelectivePassthroughController");
            Type boundaryType = FindType("BoundaryVisibilityController");
            Type switcherType = FindType(
                "TeamVR.Experiment.PassthroughConditionSwitcher");
            Type conditionType = FindType("TeamVR.Experiment.ExperimentCondition");

            var root = new GameObject("ExperimentOverridePlayModeTest");
            MonoBehaviour presentation =
                root.AddComponent(presentationType) as MonoBehaviour;
            MonoBehaviour boundary =
                root.AddComponent(boundaryType) as MonoBehaviour;
            MonoBehaviour switcher =
                root.AddComponent(switcherType) as MonoBehaviour;
            try
            {
                switcherType.GetMethod("Configure").Invoke(
                    switcher,
                    new object[] { presentation, boundary });
                MethodInfo apply = switcherType.GetMethod(
                    "ApplyCondition",
                    new[] { conditionType, typeof(string).MakeByRefType() });

                ExperimentPresentationOverride[] expectedPresentation =
                {
                    ExperimentPresentationOverride.SuppressAll,
                    ExperimentPresentationOverride.SuppressAll,
                    ExperimentPresentationOverride.UseUserSettings
                };
                BoundaryVisibilityOverride[] expectedBoundary =
                {
                    BoundaryVisibilityOverride.ForceSuppressed,
                    BoundaryVisibilityOverride.ForceVisible,
                    BoundaryVisibilityOverride.ForceSuppressed
                };

                for (int i = 0; i < 3; i++)
                {
                    object[] arguments =
                    {
                        Enum.ToObject(conditionType, i),
                        null
                    };
                    Assert.That(
                        (bool)apply.Invoke(switcher, arguments),
                        Is.True);
                    Assert.That(presentation.enabled, Is.True);
                    Assert.That(boundary.enabled, Is.True);
                    Assert.That(switcher.enabled, Is.True);
                    Assert.That(
                        ReadProperty(
                            presentation,
                            "ExperimentPresentationOverride"),
                        Is.EqualTo(expectedPresentation[i]));
                    Assert.That(
                        ReadProperty(boundary, "ExperimentOverride"),
                        Is.EqualTo(expectedBoundary[i]));
                }

                switcherType.GetMethod("RestoreConfiguredBehavior")
                    .Invoke(switcher, null);
                Assert.That(
                    ReadProperty(
                        presentation,
                        "ExperimentPresentationOverride"),
                    Is.EqualTo(
                        ExperimentPresentationOverride.UseUserSettings));
                Assert.That(
                    ReadProperty(boundary, "ExperimentOverride"),
                    Is.EqualTo(
                        BoundaryVisibilityOverride.UseConfiguredPolicy));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            yield return null;
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            Assert.Fail("Type not found: " + fullName);
            return null;
        }

        private static object ReadProperty(object target, string name)
        {
            return target.GetType().GetProperty(name).GetValue(target);
        }
    }
}
