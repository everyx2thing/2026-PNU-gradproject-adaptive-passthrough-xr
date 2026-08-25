using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class PersonalizationPolicyApplicationTests
    {
        [Test]
        public void DynamicPolicyUpdatesThresholdsWithoutChangingHoldTime()
        {
            GameObject owner = new GameObject("Dynamic Policy Test");
            owner.SetActive(false);
            try
            {
                MonoBehaviour policy = AddBehaviour(
                    owner,
                    "DynamicPassthroughPolicyController");
                float holdBefore = FloatProperty(
                    policy,
                    "MinimumHoldSeconds");

                Invoke(
                    policy,
                    "ApplyPersonalizedThresholds",
                    0.70f,
                    0.55f);

                AssertFloat(policy, "OnThreshold", 0.70f);
                AssertFloat(policy, "OffThreshold", 0.55f);
                AssertFloat(policy, "MinimumHoldSeconds", holdBefore);

                Invoke(
                    policy,
                    "ApplyPersonalizedThresholds",
                    0.95f,
                    0.95f);
                AssertFloat(policy, "OnThreshold", 0.70f);
                AssertFloat(policy, "OffThreshold", 0.55f);

                Invoke(policy, "RestoreDefaultThresholds");
                AssertFloat(policy, "OnThreshold", 0.60f);
                AssertFloat(policy, "OffThreshold", 0.45f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void MutableFilterSettingsPreserveActiveHoldState()
        {
            var settings = new PassthroughDecisionFilterSettings
            {
                mode = PassthroughDecisionMode.Hysteresis,
                onThreshold = 0.60f,
                offThreshold = 0.45f,
                minimumHoldSeconds = 1.50f,
                releaseDelaySeconds = 0.35f
            };
            var filter = new PassthroughDecisionFilter(settings);

            Assert.That(filter.Evaluate(0.0, true, 0.70f).Enabled, Is.True);
            settings.onThreshold = 0.70f;
            settings.offThreshold = 0.55f;

            PassthroughDecisionSnapshot held =
                filter.Evaluate(0.50, true, 0f);
            Assert.That(held.Enabled, Is.True);
            Assert.That(
                held.Reason,
                Is.EqualTo(PassthroughDecisionReason.MinimumHoldActive));

            PassthroughDecisionSnapshot released =
                filter.Evaluate(1.86, true, 0f);
            Assert.That(released.Enabled, Is.False);
        }

        [Test]
        public void ApplyNowUpdatesBothPoliciesAndDisableRestoresDefaults()
        {
            GameObject owner = new GameObject("Personalization Apply Test");
            owner.SetActive(false);
            try
            {
                MonoBehaviour staticPolicy = AddBehaviour(
                    owner,
                    "StaticPassthroughPolicyController");
                MonoBehaviour dynamicPolicy = AddBehaviour(
                    owner,
                    "DynamicPassthroughPolicyController");
                MonoBehaviour runtime = AddBehaviour(
                    owner,
                    "PersonalizationRuntimeController");
                WireRuntime(runtime, staticPolicy, dynamicPolicy);
                Invoke(runtime, "SetBypassColdStartForTesting", true);
                Invoke(runtime, "SetNegativeProbabilityOverride", true);
                Invoke(runtime, "SetOverriddenNegativeProbability", 1f);

                Invoke(runtime, "ApplyModelNow");

                AssertFloat(staticPolicy, "StableOnThreshold", 0.75f);
                AssertFloat(staticPolicy, "RapidOnThreshold", 0.55f);
                AssertFloat(staticPolicy, "HandFullThreshold", 0.95f);
                AssertFloat(dynamicPolicy, "OnThreshold", 0.70f);
                AssertFloat(dynamicPolicy, "OffThreshold", 0.55f);
                Assert.That(
                    BoolProperty(runtime, "LastStaticThresholdsApplied"),
                    Is.True);
                Assert.That(
                    BoolProperty(runtime, "LastDynamicThresholdsApplied"),
                    Is.True);

                Invoke(runtime, "SetMlEnabled", false);

                AssertFloat(staticPolicy, "StableOnThreshold", 0.65f);
                AssertFloat(staticPolicy, "RapidOnThreshold", 0.45f);
                AssertFloat(staticPolicy, "HandFullThreshold", 0.85f);
                AssertFloat(dynamicPolicy, "OnThreshold", 0.60f);
                AssertFloat(dynamicPolicy, "OffThreshold", 0.45f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void ModelFailureRestoresBothPolicyDefaults()
        {
            GameObject owner = new GameObject("Personalization Failure Test");
            owner.SetActive(false);
            try
            {
                MonoBehaviour staticPolicy = AddBehaviour(
                    owner,
                    "StaticPassthroughPolicyController");
                MonoBehaviour dynamicPolicy = AddBehaviour(
                    owner,
                    "DynamicPassthroughPolicyController");
                MonoBehaviour runtime = AddBehaviour(
                    owner,
                    "PersonalizationRuntimeController");
                WireRuntime(runtime, staticPolicy, dynamicPolicy);
                Invoke(runtime, "SetBypassColdStartForTesting", true);
                Invoke(runtime, "SetNegativeProbabilityOverride", true);
                Invoke(runtime, "SetOverriddenNegativeProbability", 1f);
                Invoke(runtime, "ApplyModelNow");

                Invoke(runtime, "SetNegativeProbabilityOverride", false);
                Invoke(runtime, "ApplyModelNow");

                Assert.That(
                    BoolProperty(runtime, "HasNegativeProbability"),
                    Is.False);
                AssertFloat(staticPolicy, "StableOnThreshold", 0.65f);
                AssertFloat(staticPolicy, "RapidOnThreshold", 0.45f);
                AssertFloat(staticPolicy, "HandFullThreshold", 0.85f);
                AssertFloat(dynamicPolicy, "OnThreshold", 0.60f);
                AssertFloat(dynamicPolicy, "OffThreshold", 0.45f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void OverlappingSourcesProduceOneCombinedActivation()
        {
            GameObject owner = new GameObject("Unified Activation Test");
            owner.SetActive(false);
            try
            {
                MonoBehaviour runtime = AddBehaviour(
                    owner,
                    "PersonalizationRuntimeController");

                Invoke(
                    runtime,
                    "RecordPresentationVisibility",
                    true,
                    "static",
                    1.0);
                Invoke(
                    runtime,
                    "RecordPresentationVisibility",
                    true,
                    "static+dynamic",
                    1.2);
                Invoke(
                    runtime,
                    "RecordPresentationVisibility",
                    true,
                    "dynamic",
                    1.4);
                Invoke(
                    runtime,
                    "RecordPresentationVisibility",
                    false,
                    "none",
                    2.0);

                Assert.That(
                    IntProperty(runtime, "RecentActivationCount"),
                    Is.EqualTo(1));
                Assert.That(
                    BoolProperty(runtime, "ActivationActive"),
                    Is.False);
                Assert.That(
                    StringProperty(runtime, "CurrentActivationSource"),
                    Is.EqualTo("static+dynamic"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void PersonalizationLogRecordKeepsSchemaVersionTwoFields()
        {
            Type runtimeType = FindType("PersonalizationRuntimeController");
            Assert.That(runtimeType, Is.Not.Null);
            Type recordType = runtimeType.GetNestedType(
                "PersonalizationLogRecord",
                BindingFlags.NonPublic);
            Assert.That(recordType, Is.Not.Null);

            object record = Activator.CreateInstance(recordType);
            Assert.That(
                Convert.ToInt32(recordType.GetField("schemaVersion").GetValue(record)),
                Is.EqualTo(2));

            string[] additiveFields =
            {
                "adjustmentDelta",
                "dynamicOnThreshold",
                "dynamicOffThreshold",
                "staticThresholdsApplied",
                "dynamicThresholdsApplied",
                "activationSource"
            };
            for (int i = 0; i < additiveFields.Length; i++)
            {
                Assert.That(
                    recordType.GetField(additiveFields[i]),
                    Is.Not.Null,
                    additiveFields[i] + " must remain in schema v2.");
            }
        }

        private static MonoBehaviour AddBehaviour(
            GameObject owner,
            string typeName)
        {
            Type type = FindType(typeName);
            Assert.That(type, Is.Not.Null, typeName + " must compile.");
            return owner.AddComponent(type) as MonoBehaviour;
        }

        private static Type FindType(string typeName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(typeName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static void WireRuntime(
            MonoBehaviour runtime,
            MonoBehaviour staticPolicy,
            MonoBehaviour dynamicPolicy)
        {
            var serialized = new SerializedObject(runtime);
            serialized.FindProperty("staticPolicy").objectReferenceValue =
                staticPolicy;
            serialized.FindProperty("dynamicPolicy").objectReferenceValue =
                dynamicPolicy;
            serialized.FindProperty("presentation").objectReferenceValue = null;
            serialized.FindProperty("enableLogging").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static object Invoke(
            object target,
            string methodName,
            params object[] arguments)
        {
            MethodInfo[] methods = target.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == methodName
                    && methods[i].GetParameters().Length == arguments.Length)
                {
                    return methods[i].Invoke(target, arguments);
                }
            }

            Assert.Fail("Method not found: " + methodName);
            return null;
        }

        private static float FloatProperty(object target, string name)
        {
            return Convert.ToSingle(Property(target, name));
        }

        private static int IntProperty(object target, string name)
        {
            return Convert.ToInt32(Property(target, name));
        }

        private static bool BoolProperty(object target, string name)
        {
            return Convert.ToBoolean(Property(target, name));
        }

        private static string StringProperty(object target, string name)
        {
            return Convert.ToString(Property(target, name));
        }

        private static object Property(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null, "Property not found: " + name);
            return property.GetValue(target);
        }

        private static void AssertFloat(
            object target,
            string propertyName,
            float expected)
        {
            Assert.That(
                FloatProperty(target, propertyName),
                Is.EqualTo(expected).Within(0.0001f));
        }
    }
}
