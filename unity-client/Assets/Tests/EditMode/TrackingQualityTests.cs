using NUnit.Framework;
using System;
using System.Reflection;
using UnityEngine;
using Unity.InferenceEngine;
using Meta.XR;

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
            Assert.That(balanced.personInferenceRateHz, Is.EqualTo(3f));
            Assert.That(balanced.inferenceSliceMilliseconds, Is.EqualTo(1.5f));
            Assert.That(balanced.maximumLayersPerFrame, Is.EqualTo(64));
            Assert.That(accuracy.spatialRateHz, Is.EqualTo(30f));
            Assert.That(accuracy.spatialRayCount, Is.EqualTo(24));
            Assert.That(accuracy.personInferenceRateHz, Is.EqualTo(3f));
            Assert.That(accuracy.inferenceSliceMilliseconds, Is.EqualTo(2.5f));
            Assert.That(accuracy.maximumLayersPerFrame, Is.EqualTo(96));
            Assert.That(performance.spatialRateHz, Is.EqualTo(10f));
            Assert.That(performance.spatialRayCount, Is.EqualTo(6));
            Assert.That(performance.personInferenceRateHz, Is.EqualTo(3f));
            Assert.That(performance.inferenceSliceMilliseconds, Is.EqualTo(0.75f));
            Assert.That(performance.maximumLayersPerFrame, Is.EqualTo(48));
        }

        [Test]
        public void SixRayPlan_RetainsHandsAndLowObstacleCorridor()
        {
            var root = new GameObject("Six Ray Probe Plan");
            var head = new GameObject("Head");
            var left = new GameObject("Left");
            var right = new GameObject("Right");
            try
            {
                QuestSpatialObstacleProvider provider =
                    root.AddComponent<QuestSpatialObstacleProvider>();
                provider.Configure(
                    null,
                    null,
                    null,
                    head.transform,
                    left.transform,
                    right.transform);
                MethodInfo build = typeof(QuestSpatialObstacleProvider)
                    .GetMethod(
                        "BuildProbes",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo probeField = typeof(QuestSpatialObstacleProvider)
                    .GetField(
                        "probes",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                int count = (int)build.Invoke(provider, new object[] { 6 });
                var probes = (SpatialProbe[])probeField.GetValue(provider);
                int leftCount = 0;
                int rightCount = 0;
                int corridorCount = 0;
                for (int i = 0; i < count; i++)
                {
                    leftCount += probes[i].Owner
                            == SpatialProbeOwner.LeftHand
                        ? 1
                        : 0;
                    rightCount += probes[i].Owner
                            == SpatialProbeOwner.RightHand
                        ? 1
                        : 0;
                    corridorCount += probes[i].Purpose
                            == SpatialProbePurpose.LocomotionCorridor
                        ? 1
                        : 0;
                }

                Assert.That(count, Is.EqualTo(6));
                Assert.That(leftCount, Is.EqualTo(2));
                Assert.That(rightCount, Is.EqualTo(2));
                Assert.That(corridorCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(head);
                UnityEngine.Object.DestroyImmediate(left);
                UnityEngine.Object.DestroyImmediate(right);
            }
        }

        [Test]
        public void BalancedRayPlan_ProvidesThreeSamplesPerPresentationFamily()
        {
            var root = new GameObject("Balanced Ray Probe Plan");
            var head = new GameObject("Head");
            var left = new GameObject("Left");
            var right = new GameObject("Right");
            try
            {
                QuestSpatialObstacleProvider provider =
                    root.AddComponent<QuestSpatialObstacleProvider>();
                provider.Configure(
                    null,
                    null,
                    null,
                    head.transform,
                    left.transform,
                    right.transform);
                MethodInfo build = typeof(QuestSpatialObstacleProvider)
                    .GetMethod(
                        "BuildProbes",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo probeField = typeof(QuestSpatialObstacleProvider)
                    .GetField(
                        "probes",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                int count = (int)build.Invoke(provider, new object[] { 12 });
                var probes = (SpatialProbe[])probeField.GetValue(provider);
                int headStandardCount = 0;
                int leftCount = 0;
                int rightCount = 0;
                int corridorCount = 0;
                for (int i = 0; i < count; i++)
                {
                    headStandardCount += probes[i].Owner
                                == SpatialProbeOwner.Head
                            && probes[i].Purpose
                                == SpatialProbePurpose.Standard
                        ? 1
                        : 0;
                    leftCount += probes[i].Owner
                            == SpatialProbeOwner.LeftHand
                        ? 1
                        : 0;
                    rightCount += probes[i].Owner
                            == SpatialProbeOwner.RightHand
                        ? 1
                        : 0;
                    corridorCount += probes[i].Purpose
                            == SpatialProbePurpose.LocomotionCorridor
                        ? 1
                        : 0;
                }

                Assert.That(count, Is.EqualTo(12));
                Assert.That(headStandardCount, Is.EqualTo(3));
                Assert.That(leftCount, Is.EqualTo(3));
                Assert.That(rightCount, Is.EqualTo(3));
                Assert.That(corridorCount, Is.EqualTo(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(head);
                UnityEngine.Object.DestroyImmediate(left);
                UnityEngine.Object.DestroyImmediate(right);
            }
        }

        [Test]
        public void AdaptiveSecondStageKeepsHandsAndThreeHzInference()
        {
            TrackingQualitySettings degraded =
                TrackingQualityController.CalculateEffectiveSettings(
                    TrackingQualityProfile.Accuracy,
                    2);

            Assert.That(degraded.spatialRateHz, Is.EqualTo(10f));
            Assert.That(degraded.spatialRayCount, Is.EqualTo(6));
            Assert.That(degraded.personInferenceRateHz, Is.EqualTo(3f));
            Assert.That(degraded.inferenceSliceMilliseconds, Is.EqualTo(0.75f));
            Assert.That(degraded.maximumLayersPerFrame, Is.EqualTo(48));
        }

        [Test]
        public void AdaptiveFirstStageKeepsSpatialAndOnlyReducesInference()
        {
            TrackingQualitySettings degraded =
                TrackingQualityController.CalculateEffectiveSettings(
                    TrackingQualityProfile.Accuracy,
                    1);

            Assert.That(degraded.spatialRateHz, Is.EqualTo(30f));
            Assert.That(degraded.spatialRayCount, Is.EqualTo(24));
            Assert.That(degraded.personInferenceRateHz, Is.EqualTo(3f));
            Assert.That(degraded.inferenceSliceMilliseconds, Is.EqualTo(1f));
            Assert.That(degraded.maximumLayersPerFrame, Is.EqualTo(64));
        }

        [Test]
        public void RuntimeStatisticsResetRemovesPauseContamination()
        {
            var gameObject = new GameObject("TrackingQualityResetTest");
            try
            {
                var controller =
                    gameObject.AddComponent<TrackingQualityController>();
                controller.RecordInference(
                    1.0,
                    12f,
                    39000f,
                    39000f,
                    10,
                    "CPU",
                    false);

                controller.ResetRuntimeMeasurements();
                TrackingDiagnosticsSnapshot snapshot =
                    controller.GetSnapshot();
                Assert.That(snapshot.InferenceActiveMilliseconds, Is.Zero);
                Assert.That(snapshot.InferenceWallMilliseconds, Is.Zero);
                Assert.That(snapshot.CaptureAgeMilliseconds, Is.Zero);
                Assert.That(snapshot.FrameP95Milliseconds, Is.Zero);
                Assert.That(snapshot.HasInferenceMeasurement, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void DiagnosticsSeparateTargetFromMeasuredInferenceRate()
        {
            var gameObject = new GameObject("TrackingRateDiagnosticsTest");
            try
            {
                var controller =
                    gameObject.AddComponent<TrackingQualityController>();
                TrackingDiagnosticsSnapshot before = controller.GetSnapshot();
                Assert.That(before.TargetInferenceRateHz, Is.EqualTo(3f));
                Assert.That(before.MeasuredInferenceRateHz, Is.Zero);
                Assert.That(before.HasInferenceMeasurement, Is.False);
                Assert.That(before.InferenceRateHz, Is.Zero);

                controller.RecordInference(1.0, 5f);
                TrackingDiagnosticsSnapshot after = controller.GetSnapshot();
                Assert.That(after.HasInferenceMeasurement, Is.True);
                Assert.That(after.TargetInferenceRateHz, Is.EqualTo(3f));
                Assert.That(after.MeasuredInferenceRateHz, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void DynamicFramesExposeMonotonicSequenceAndRealtime()
        {
            var gameObject = new GameObject("DynamicFrameSequenceTest");
            try
            {
                var controller =
                    gameObject.AddComponent<DynamicRiskController>();
                controller.SubmitDetections(
                    1000000000.0,
                    Array.Empty<DynamicObjectDetection>());
                long firstSequence = controller.LatestFrameSequence;
                double firstRealtime =
                    controller.LatestFrameProcessedRealtimeSeconds;
                controller.SubmitDetections(
                    1000000001.0,
                    Array.Empty<DynamicObjectDetection>());

                Assert.That(firstSequence, Is.EqualTo(1));
                Assert.That(controller.LatestFrameSequence, Is.EqualTo(2));
                Assert.That(firstRealtime, Is.GreaterThanOrEqualTo(0.0));
                Assert.That(
                    controller.LatestFrameProcessedRealtimeSeconds,
                    Is.GreaterThanOrEqualTo(firstRealtime));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SafetyOverlapAcceptsOnlyActualHitStatus()
        {
            Assert.That(
                QuestSpatialObstacleProvider.IsSafetyOverlapStatus(
                    EnvironmentRaycastHitStatus.Hit),
                Is.True);
            Assert.That(
                QuestSpatialObstacleProvider.IsSafetyOverlapStatus(
                    EnvironmentRaycastHitStatus.HitPointOccluded),
                Is.False);
            Assert.That(
                QuestSpatialObstacleProvider.IsSafetyOverlapStatus(
                    EnvironmentRaycastHitStatus.RayOccluded),
                Is.False);
        }

        [Test]
        public void PositiveRayDistanceIsPreservedAlongsideOverlapState()
        {
            var measurement = new SpatialObstacleMeasurement(
                SpatialObstacleSource.EnvironmentDepth,
                1.0,
                true,
                0.42f,
                Vector3.forward * 0.42f,
                Vector3.back,
                0f,
                1f,
                4,
                0.02f,
                0f,
                true,
                true,
                4);

            Assert.That(measurement.DistanceMeters, Is.EqualTo(0.42f));
            Assert.That(measurement.SafetyVolumeOverlap, Is.True);
            Assert.That(measurement.RawSafetyVolumeOverlap, Is.True);
            Assert.That(measurement.ValidRayHitCount, Is.EqualTo(4));
        }

        [Test]
        public void SafetyOverlapRequiresTwoHitsAndTwoReleases()
        {
            bool confirmed = false;
            int confirmations = 0;
            int releases = 0;
            QuestSpatialObstacleProvider.UpdateSafetyOverlapConfirmation(
                true,
                ref confirmed,
                ref confirmations,
                ref releases);
            Assert.That(confirmed, Is.False);
            QuestSpatialObstacleProvider.UpdateSafetyOverlapConfirmation(
                true,
                ref confirmed,
                ref confirmations,
                ref releases);
            Assert.That(confirmed, Is.True);
            QuestSpatialObstacleProvider.UpdateSafetyOverlapConfirmation(
                false,
                ref confirmed,
                ref confirmations,
                ref releases);
            Assert.That(confirmed, Is.True);
            QuestSpatialObstacleProvider.UpdateSafetyOverlapConfirmation(
                false,
                ref confirmed,
                ref confirmations,
                ref releases);
            Assert.That(confirmed, Is.False);
        }

        [Test]
        public void RawOverlapAloneDoesNotForceStaticEmergency()
        {
            var rawOnly = new SpatialObstacleMeasurement(
                SpatialObstacleSource.EnvironmentDepth,
                1.0,
                true,
                0.42f,
                Vector3.forward * 0.42f,
                Vector3.back,
                0f,
                0.8f,
                2,
                0.02f,
                0f,
                false,
                true,
                2);
            var closeRay = new SpatialObstacleMeasurement(
                SpatialObstacleSource.EnvironmentDepth,
                1.0,
                true,
                0.25f,
                Vector3.forward * 0.25f,
                Vector3.back,
                0f,
                0.8f,
                1,
                0f,
                0f,
                false,
                false,
                1);

            Assert.That(
                QuestSpatialObstacleProvider.ShouldForceStaticEmergency(
                    rawOnly),
                Is.False);
            Assert.That(
                QuestSpatialObstacleProvider.ShouldForceStaticEmergency(
                    closeRay),
                Is.True);
        }

        [Test]
        public void FarConfirmedOverlapDoesNotForceStaticEmergency()
        {
            var farOverlap = new SpatialObstacleMeasurement(
                SpatialObstacleSource.EnvironmentDepth,
                1.0,
                true,
                1.20f,
                Vector3.forward * 1.20f,
                Vector3.back,
                0f,
                1f,
                3,
                0.02f,
                0f,
                true,
                true,
                3);

            Assert.That(
                QuestSpatialObstacleProvider.ShouldForceStaticEmergency(
                    farOverlap),
                Is.False);
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
            Assert.That(recordType.GetField("metricDepthReliable"), Is.Not.Null);
            Assert.That(recordType.GetField("depthRejectedReason"), Is.Not.Null);
            Assert.That(recordType.GetField("spatialRawOverlap"), Is.Not.Null);
            Assert.That(recordType.GetField("frameSequence"), Is.Not.Null);
            Assert.That(recordType.GetField("visibilitySource"), Is.Not.Null);
            Assert.That(recordType.GetField("scenarioId"), Is.Not.Null);
            Assert.That(
                (float)recordType.GetField("headStaticDistanceMeters")
                    .GetValue(record),
                Is.EqualTo(-1f));
            Assert.That(
                (float)recordType.GetField("leftHandStaticDistanceMeters")
                    .GetValue(record),
                Is.EqualTo(-1f));
            Assert.That(
                (float)recordType.GetField("rightHandStaticDistanceMeters")
                    .GetValue(record),
                Is.EqualTo(-1f));
            Assert.That(
                (float)recordType.GetField("lowObstacleStaticDistanceMeters")
                    .GetValue(record),
                Is.EqualTo(-1f));
            Assert.That(recordType.GetField("headStaticAvailable"), Is.Not.Null);
            Assert.That(
                recordType.GetField("primaryPolicyGeometryAvailable"),
                Is.Not.Null);
        }

        [Test]
        public void ResetPipelinePublishesSessionBoundary()
        {
            var gameObject = new GameObject("Dynamic Risk Reset Test");
            try
            {
                DynamicRiskController controller =
                    gameObject.AddComponent<DynamicRiskController>();
                int resetCount = 0;
                controller.PipelineReset += () => resetCount++;

                controller.ResetPipeline();

                Assert.That(resetCount, Is.EqualTo(1));
                Assert.That(controller.LatestFrame, Is.Null);
                Assert.That(controller.LatestFrameSequence, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }
    }
}
