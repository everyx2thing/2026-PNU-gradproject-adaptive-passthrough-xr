using NUnit.Framework;
using System;
using System.Reflection;
using UnityEngine;
using Unity.InferenceEngine;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class TrackingQualityTests
    {
        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(TrackingQualityController.PlayerPrefsKey);
        }

        [Test]
        public void ProfilesExposePlannedRates()
        {
            TrackingQualitySettings balanced =
                TrackingQualitySettings.For(TrackingQualityProfile.Balanced);
            TrackingQualitySettings accuracy =
                TrackingQualitySettings.For(TrackingQualityProfile.Accuracy);
            TrackingQualitySettings performance =
                TrackingQualitySettings.For(TrackingQualityProfile.Performance);

            Assert.That(balanced.spatialRateHz, Is.EqualTo(20f));
            Assert.That(balanced.spatialRayCount, Is.EqualTo(12));
            Assert.That(balanced.personInferenceRateHz, Is.EqualTo(5f));
            Assert.That(accuracy.spatialRateHz, Is.EqualTo(30f));
            Assert.That(accuracy.spatialRayCount, Is.EqualTo(24));
            Assert.That(accuracy.personInferenceRateHz, Is.EqualTo(8f));
            Assert.That(performance.spatialRateHz, Is.EqualTo(10f));
            Assert.That(performance.spatialRayCount, Is.EqualTo(6));
            Assert.That(performance.personInferenceRateHz, Is.EqualTo(3f));
        }

        [Test]
        public void AdaptiveFloorDoesNotDropBelowTenHzSixRaysThreeHz()
        {
            TrackingQualitySettings degraded =
                TrackingQualityController.CalculateEffectiveSettings(
                    TrackingQualityProfile.Accuracy,
                    2);

            Assert.That(degraded.spatialRateHz, Is.EqualTo(10f));
            Assert.That(degraded.spatialRayCount, Is.EqualTo(6));
            Assert.That(degraded.personInferenceRateHz, Is.EqualTo(3f));
        }

        [Test]
        public void ProfilePersistsAndResetReturnsBalanced()
        {
            var gameObject = new GameObject("TrackingQualityTest");
            try
            {
                var controller =
                    gameObject.AddComponent<TrackingQualityController>();
                controller.SetProfile(TrackingQualityProfile.Accuracy);
                Assert.That(
                    TrackingQualityController.LoadProfile(),
                    Is.EqualTo(TrackingQualityProfile.Accuracy));

                controller.ResetSavedProfile();
                Assert.That(controller.Profile,
                    Is.EqualTo(TrackingQualityProfile.Balanced));
                Assert.That(TrackingQualityController.LoadProfile(),
                    Is.EqualTo(TrackingQualityProfile.Balanced));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void BackendSelectionRejectsBudgetViolationAndPrefersCpuTie()
        {
            BackendType selected = InferenceBackendSelector.Select(
                new[]
                {
                    new InferenceBackendBenchmark(
                        BackendType.GPUCompute, 6.0f, 15.0f),
                    new InferenceBackendBenchmark(
                        BackendType.CPU, 8.0f, 13.0f)
                });
            Assert.That(selected, Is.EqualTo(BackendType.CPU));

            BackendType tie = InferenceBackendSelector.Select(
                new[]
                {
                    new InferenceBackendBenchmark(
                        BackendType.GPUCompute, 7.00f, 13.0f),
                    new InferenceBackendBenchmark(
                        BackendType.CPU, 7.05f, 13.0f)
                });
            Assert.That(tie, Is.EqualTo(BackendType.CPU));
        }

        [Test]
        public void FinitePlaneDistanceUsesTheNearestEdgeOutsideBounds()
        {
            Vector3 closest = FiniteSpatialBoundsMath.ClosestPointOnPlane(
                new Vector3(2f, 0f, 0.2f),
                Vector3.zero,
                Quaternion.identity,
                new Rect(-0.5f, -0.5f, 1f, 1f));

            Assert.That(closest.x, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(closest.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(closest.z, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(
                Vector3.Distance(new Vector3(2f, 0f, 0.2f), closest),
                Is.EqualTo(Mathf.Sqrt(2.29f)).Within(0.0001f));
        }

        [Test]
        public void FiniteVolumeReturnsZeroHazardDistanceWhenInside()
        {
            bool inside;
            Vector3 closest = FiniteSpatialBoundsMath.ClosestPointOnVolume(
                new Vector3(0.1f, 0.2f, 0.3f),
                Vector3.zero,
                Quaternion.identity,
                new Bounds(Vector3.zero, Vector3.one),
                out inside);

            Assert.That(inside, Is.True);
            Assert.That(closest.x, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(closest.y, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(closest.z, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void RoomSceneFallbackWaitsUntilEnvironmentDepthIsStale()
        {
            Assert.That(
                SpatialProviderSelection.ShouldUseRoomSceneFallback(
                    false,
                    false,
                    0f,
                    0.25f),
                Is.True);
            Assert.That(
                SpatialProviderSelection.ShouldUseRoomSceneFallback(
                    true,
                    true,
                    0.20f,
                    0.25f),
                Is.False);
            Assert.That(
                SpatialProviderSelection.ShouldUseRoomSceneFallback(
                    true,
                    true,
                    0.26f,
                    0.25f),
                Is.True);
        }

        [Test]
        public void LogSchemaTwoRetainsLegacyAndAddsTrackingFields()
        {
            Type recordType = typeof(DynamicRiskSessionLogger)
                .GetNestedType("LogRecord", BindingFlags.NonPublic);
            Assert.That(recordType, Is.Not.Null);
            object record = Activator.CreateInstance(recordType, true);

            Assert.That(
                (int)recordType.GetField("schemaVersion").GetValue(record),
                Is.EqualTo(2));
            Assert.That(recordType.GetField("dynamicRisk"), Is.Not.Null);
            Assert.That(recordType.GetField("reasons"), Is.Not.Null);
            Assert.That(recordType.GetField("trackingProfile"), Is.Not.Null);
            Assert.That(recordType.GetField("bboxDepthConflict"), Is.Not.Null);
            Assert.That(recordType.GetField("idHandoff"), Is.Not.Null);
        }
    }
}
