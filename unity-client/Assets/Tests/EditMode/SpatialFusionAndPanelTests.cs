using System.Reflection;
using NUnit.Framework;
using TeamVR.AdaptivePassthrough;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class SpatialFusionAndPanelTests
    {
        [Test]
        public void Fusion_AgreeingSourcesSelectNearerAndPublishFused()
        {
            var filter = new SpatialObstacleFusionFilter();
            SpatialObstacleMeasurement result = filter.Fuse(
                SpatialProbeOwner.Head,
                Measurement(SpatialObstacleSource.EnvironmentDepth, 1.10f),
                Measurement(SpatialObstacleSource.RoomScene, 1.30f),
                1.0);

            Assert.That(result.Source, Is.EqualTo(SpatialObstacleSource.Fused));
            Assert.That(
                result.SelectedSource,
                Is.EqualTo(SpatialObstacleSource.EnvironmentDepth));
            Assert.That(result.DistanceMeters, Is.EqualTo(1.10f).Within(0.001f));
            Assert.That(result.EnvironmentDistanceMeters, Is.EqualTo(1.10f));
            Assert.That(result.RoomSceneDistanceMeters, Is.EqualTo(1.30f));
        }

        [Test]
        public void Fusion_ConflictingNearDepthRequiresTwoConfirmations()
        {
            var filter = new SpatialObstacleFusionFilter();
            SpatialObstacleMeasurement depth = Measurement(
                SpatialObstacleSource.EnvironmentDepth,
                0.80f);
            SpatialObstacleMeasurement room = Measurement(
                SpatialObstacleSource.RoomScene,
                1.60f);

            SpatialObstacleMeasurement first = filter.Fuse(
                SpatialProbeOwner.LeftHand, depth, room, 1.0);
            SpatialObstacleMeasurement second = filter.Fuse(
                SpatialProbeOwner.LeftHand, depth, room, 1.05);

            Assert.That(first.SelectedSource,
                Is.EqualTo(SpatialObstacleSource.RoomScene));
            Assert.That(first.Source,
                Is.EqualTo(SpatialObstacleSource.RoomScene));
            Assert.That(second.SelectedSource,
                Is.EqualTo(SpatialObstacleSource.EnvironmentDepth));
            Assert.That(second.Source,
                Is.EqualTo(SpatialObstacleSource.EnvironmentDepth));
        }

        [Test]
        public void Fusion_EmergencyDepthBypassesConfirmation()
        {
            var filter = new SpatialObstacleFusionFilter();
            SpatialObstacleMeasurement result = filter.Fuse(
                SpatialProbeOwner.RightHand,
                Measurement(SpatialObstacleSource.EnvironmentDepth, 0.25f),
                Measurement(SpatialObstacleSource.RoomScene, 1.40f),
                1.0);

            Assert.That(result.SelectedSource,
                Is.EqualTo(SpatialObstacleSource.EnvironmentDepth));
            Assert.That(result.DistanceMeters, Is.EqualTo(0.25f));
        }

        [Test]
        public void Fusion_SelfRejectedDepthUsesRoomScene()
        {
            var filter = new SpatialObstacleFusionFilter();
            SpatialObstacleMeasurement rejected = new SpatialObstacleMeasurement(
                SpatialObstacleSource.Unavailable,
                1.0,
                false,
                0f,
                Vector3.zero,
                Vector3.zero,
                0f,
                0f,
                0,
                0f,
                0f,
                false,
                false,
                0,
                SpatialProbeOwner.LeftHand,
                SpatialObstacleSource.Unavailable,
                0.15f,
                -1f,
                true,
                "toward-estimated-chest");
            SpatialObstacleMeasurement result = filter.Fuse(
                SpatialProbeOwner.LeftHand,
                rejected,
                Measurement(SpatialObstacleSource.RoomScene, 0.90f),
                1.0);

            Assert.That(result.Source,
                Is.EqualTo(SpatialObstacleSource.RoomScene));
            Assert.That(result.DistanceMeters, Is.EqualTo(0.90f));
            Assert.That(result.SelfRejected, Is.True);
            Assert.That(result.RejectionReason,
                Is.EqualTo("toward-estimated-chest"));
        }

        [Test]
        public void SelfFilter_RejectsOnlyEstimatedTorsoHits()
        {
            Assert.That(
                SpatialObstacleFusionFilter.IsPointInsideEstimatedTorso(
                    new Vector3(0f, 1.2f, -0.08f),
                    new Vector3(0f, 1.65f, 0f),
                    Vector3.up,
                    Vector3.forward),
                Is.True);
            Assert.That(
                SpatialObstacleFusionFilter.IsPointInsideEstimatedTorso(
                    new Vector3(0f, 1.2f, 0.80f),
                    new Vector3(0f, 1.65f, 0f),
                    Vector3.up,
                    Vector3.forward),
                Is.False);
        }

        [Test]
        public void SelfFilter_RejectsChestFacingHandRayButNotForwardRay()
        {
            Vector3 chest = new Vector3(0f, 1.2f, 0f);
            Vector3 hand = new Vector3(0.4f, 1.2f, 0.2f);

            Assert.That(SpatialObstacleFusionFilter
                .IsDirectionTowardEstimatedChest(
                    hand,
                    chest - hand,
                    chest,
                    0.80f), Is.True);
            Assert.That(SpatialObstacleFusionFilter
                .IsDirectionTowardEstimatedChest(
                    hand,
                    hand - chest,
                    chest,
                    0.80f), Is.False);
        }

        [Test]
        public void Fusion_EnvironmentOnlyLargeNearJumpRequiresConfirmation()
        {
            var filter = new SpatialObstacleFusionFilter();
            SpatialObstacleMeasurement unavailable =
                SpatialObstacleMeasurement.Unavailable(0.0);
            SpatialObstacleMeasurement baseline = filter.Fuse(
                SpatialProbeOwner.LeftHand,
                Measurement(SpatialObstacleSource.EnvironmentDepth, 2.00f),
                unavailable,
                0.0);
            SpatialObstacleMeasurement firstNear = filter.Fuse(
                SpatialProbeOwner.LeftHand,
                Measurement(SpatialObstacleSource.EnvironmentDepth, 0.60f),
                unavailable,
                0.05);
            SpatialObstacleMeasurement confirmedNear = filter.Fuse(
                SpatialProbeOwner.LeftHand,
                Measurement(SpatialObstacleSource.EnvironmentDepth, 0.61f),
                unavailable,
                0.10);

            Assert.That(baseline.Available, Is.True);
            Assert.That(firstNear.Available, Is.True);
            Assert.That(firstNear.DistanceMeters, Is.EqualTo(2.0f));
            Assert.That(firstNear.AgeSeconds, Is.EqualTo(0.05f).Within(0.001f));
            Assert.That(confirmedNear.Available, Is.True);
            Assert.That(confirmedNear.DistanceMeters, Is.EqualTo(0.61f));
        }

        [Test]
        public void Fusion_InitialEnvironmentNearValueNeedsTwoSamples()
        {
            var filter = new SpatialObstacleFusionFilter();
            SpatialObstacleMeasurement unavailable =
                SpatialObstacleMeasurement.Unavailable(0.0);

            SpatialObstacleMeasurement first = filter.Fuse(
                SpatialProbeOwner.Head,
                Measurement(SpatialObstacleSource.EnvironmentDepth, 1.0f),
                unavailable,
                1.0);
            SpatialObstacleMeasurement second = filter.Fuse(
                SpatialProbeOwner.Head,
                Measurement(SpatialObstacleSource.EnvironmentDepth, 1.1f),
                unavailable,
                1.05);

            Assert.That(first.Available, Is.False);
            Assert.That(second.Available, Is.True);
        }

        [Test]
        public void Fusion_HoldsLastApprovedValueForQuarterSecond()
        {
            var filter = new SpatialObstacleFusionFilter();
            SpatialObstacleMeasurement unavailable =
                SpatialObstacleMeasurement.Unavailable(0.0);
            filter.Fuse(
                SpatialProbeOwner.Head,
                Measurement(SpatialObstacleSource.EnvironmentDepth, 2.0f),
                unavailable,
                1.0);

            SpatialObstacleMeasurement held = filter.Fuse(
                SpatialProbeOwner.Head,
                unavailable,
                unavailable,
                1.20);
            SpatialObstacleMeasurement expired = filter.Fuse(
                SpatialProbeOwner.Head,
                unavailable,
                unavailable,
                1.26);

            Assert.That(held.Available, Is.True);
            Assert.That(held.DistanceMeters, Is.EqualTo(2.0f));
            Assert.That(held.AgeSeconds, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(expired.Available, Is.False);
        }

        [Test]
        public void DirectionalHeadStateIgnoresRetreatAndSidewaysMotion()
        {
            Assert.That(
                QuestSpatialObstacleProvider.CalculateDirectionalHeadUserState(
                    Vector3.forward,
                    Vector3.forward),
                Is.GreaterThan(0.9f));
            Assert.That(
                QuestSpatialObstacleProvider.CalculateDirectionalHeadUserState(
                    Vector3.back,
                    Vector3.forward),
                Is.Zero);
            Assert.That(
                QuestSpatialObstacleProvider.CalculateDirectionalHeadUserState(
                    Vector3.right,
                    Vector3.forward),
                Is.Zero);
        }

        [Test]
        public void PrimaryDirectionalHeadStateUsesHigherLowObstacleRisk()
        {
            StaticRiskMeasurement headRisk = Risk(0.20f);
            StaticRiskMeasurement lowObstacleRisk = Risk(0.80f);

            float towardLowObstacle = QuestSpatialObstacleProvider
                .CalculatePrimaryDirectionalHeadUserState(
                    Vector3.forward,
                    headRisk,
                    Vector3.right,
                    lowObstacleRisk,
                    Vector3.forward);
            float towardLowerRiskHeadHazard = QuestSpatialObstacleProvider
                .CalculatePrimaryDirectionalHeadUserState(
                    Vector3.right,
                    headRisk,
                    Vector3.right,
                    lowObstacleRisk,
                    Vector3.forward);

            Assert.That(towardLowObstacle, Is.GreaterThan(0.9f));
            Assert.That(towardLowerRiskHeadHazard, Is.Zero);
        }

        [Test]
        public void SafetyEdgeSamplingUsesFourRaycastsAndRotatesFullSweep()
        {
            Assert.That(QuestSpatialObstacleProvider
                .MaximumSafetyEdgeRaycastsPerMeasurement, Is.EqualTo(4));
            int edgeIndex = 0;
            edgeIndex = QuestSpatialObstacleProvider.AdvanceSafetyEdgeIndex(
                edgeIndex,
                QuestSpatialObstacleProvider
                    .MaximumSafetyEdgeRaycastsPerMeasurement);
            Assert.That(edgeIndex, Is.EqualTo(4));
            edgeIndex = QuestSpatialObstacleProvider.AdvanceSafetyEdgeIndex(
                edgeIndex,
                QuestSpatialObstacleProvider
                    .MaximumSafetyEdgeRaycastsPerMeasurement);
            Assert.That(edgeIndex, Is.EqualTo(8));
            edgeIndex = QuestSpatialObstacleProvider.AdvanceSafetyEdgeIndex(
                edgeIndex,
                QuestSpatialObstacleProvider
                    .MaximumSafetyEdgeRaycastsPerMeasurement);
            Assert.That(edgeIndex, Is.Zero);
        }

        [Test]
        public void SpatialTrackingSessionResetClearsBodyAndSweepState()
        {
            var gameObject = new GameObject("spatial-provider-reset-test");
            try
            {
                var provider = gameObject.AddComponent<
                    QuestSpatialObstacleProvider>();
                FieldInfo headStateField = typeof(QuestSpatialObstacleProvider)
                    .GetField(
                        "headState",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                object headState = headStateField.GetValue(provider);
                System.Type stateType = headState.GetType();
                stateType.GetField("velocity").SetValue(
                    headState,
                    Vector3.forward);
                stateType.GetField("nextSafetyEdgeIndex").SetValue(
                    headState,
                    8);
                stateType.GetField("safetyEdgeHitMask").SetValue(
                    headState,
                    15);

                provider.ResetTrackingSession();

                Assert.That(
                    (Vector3)stateType.GetField("velocity").GetValue(headState),
                    Is.EqualTo(Vector3.zero));
                Assert.That(
                    (int)stateType.GetField("nextSafetyEdgeIndex")
                        .GetValue(headState),
                    Is.Zero);
                Assert.That(
                    (int)stateType.GetField("safetyEdgeHitMask")
                        .GetValue(headState),
                    Is.Zero);
                Assert.That(provider.LatestMeasurement.Available, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void TorsoAndHandSpreadUseGravityAlignedAxes()
        {
            Vector3 point = new Vector3(0f, 1.2f, -0.08f);
            Assert.That(
                SpatialObstacleFusionFilter.IsPointInsideEstimatedTorso(
                    point,
                    new Vector3(0f, 1.65f, 0f),
                    Quaternion.Euler(60f, 0f, 0f) * Vector3.up,
                    Quaternion.Euler(60f, 0f, 0f) * Vector3.forward),
                Is.True);

            Vector3 direction = Vector3.forward;
            Vector3 axis = QuestSpatialObstacleProvider.HandSpreadAxis(
                direction,
                Vector3.right);
            Vector3 left = Quaternion.AngleAxis(-12f, axis) * direction;
            Vector3 right = Quaternion.AngleAxis(12f, axis) * direction;
            Assert.That(Mathf.Abs(Vector3.Dot(axis, direction)),
                Is.LessThan(0.0001f));
            Assert.That(Vector3.Angle(left, right), Is.GreaterThan(20f));
        }

        [Test]
        public void LocomotionCorridorDirectionIgnoresHeadPitchAndPointsDown()
        {
            Vector3 pitchedForward = Quaternion.Euler(60f, 25f, 0f)
                * Vector3.forward;
            Vector3 direction =
                QuestSpatialObstacleProvider.LocomotionCorridorDirection(
                    pitchedForward,
                    42f);

            Assert.That(direction.y, Is.LessThan(-0.60f));
            Assert.That(
                Vector3.Dot(
                    Vector3.ProjectOnPlane(direction, Vector3.up).normalized,
                    QuestSpatialObstacleProvider.HorizontalDirection(
                        pitchedForward,
                        Vector3.forward)),
                Is.GreaterThan(0.99f));
        }

        [Test]
        public void LocomotionFloorFilterKeepsRaisedAndVerticalObstacles()
        {
            Assert.That(
                QuestSpatialObstacleProvider.ShouldRejectLocomotionFloorHit(
                    new Vector3(0f, 0.02f, 1f),
                    Vector3.up,
                    true,
                    0f,
                    0.08f),
                Is.True);
            Assert.That(
                QuestSpatialObstacleProvider.ShouldRejectLocomotionFloorHit(
                    new Vector3(0f, 0.25f, 1f),
                    Vector3.up,
                    true,
                    0f,
                    0.08f),
                Is.False);
            Assert.That(
                QuestSpatialObstacleProvider.ShouldRejectLocomotionFloorHit(
                    new Vector3(0f, 0.02f, 1f),
                    Vector3.back,
                    false,
                    0f,
                    0.08f),
                Is.False);
            Assert.That(
                QuestSpatialObstacleProvider.ShouldRejectLocomotionFloorHit(
                    new Vector3(0f, 0.25f, 1f),
                    Vector3.up,
                    false,
                    0f,
                    0.08f),
                Is.False,
                "A stale floor estimate must only suppress the floor wedge, not the obstacle patch.");
        }

        [Test]
        public void RaySelectionDistanceChangeNeedsKinematicSupport()
        {
            float stationary = QuestSpatialObstacleProvider
                .CalculateSupportedClosingSpeed(
                    1.06f,
                    1.00f,
                    0.05f,
                    0f,
                    4f,
                    false);
            float moving = QuestSpatialObstacleProvider
                .CalculateSupportedClosingSpeed(
                    1.06f,
                    1.00f,
                    0.05f,
                    0.60f,
                    4f,
                    false);
            float rayHandoff = QuestSpatialObstacleProvider
                .CalculateSupportedClosingSpeed(
                    1.06f,
                    1.00f,
                    0.05f,
                    0.60f,
                    10f,
                    false);

            Assert.That(stationary, Is.EqualTo(0f));
            Assert.That(moving, Is.InRange(0.60f, 0.85f));
            Assert.That(rayHandoff, Is.EqualTo(0.60f));
        }

        [Test]
        public void HeadSafetyEdgeRayUsesUnitDirectionAndSeparateLength()
        {
            bool created = QuestSpatialObstacleProvider.TryCreateNormalizedRay(
                new Vector3(1f, 2f, 3f),
                new Vector3(1.32f, 2f, 3f),
                out Ray ray,
                out float distance);

            Assert.That(created, Is.True);
            Assert.That(ray.direction.magnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(distance, Is.EqualTo(0.32f).Within(0.0001f));
        }

        [Test]
        public void PersonDepthPreRaycastGateRejectsStaleWarmupAndMotion()
        {
            Assert.That(
                QuestPersonDepthProvider.PreRaycastRejectionReason(
                    2, 0.41f, Vector2.zero, Vector2.zero),
                Is.EqualTo("depth_capture_stale"));
            Assert.That(
                QuestPersonDepthProvider.PreRaycastRejectionReason(
                    1, 0.1f, Vector2.zero, Vector2.zero),
                Is.EqualTo("depth_track_warming"));
            Assert.That(
                QuestPersonDepthProvider.PreRaycastRejectionReason(
                    2, 0.2f, new Vector2(0.5f, 0f), Vector2.zero),
                Is.EqualTo("depth_motion_mismatch"));
            Assert.That(
                QuestPersonDepthProvider.PreRaycastRejectionReason(
                    2, 0.2f, new Vector2(0.1f, 0f), Vector2.zero),
                Is.Empty);
        }

        [Test]
        public void CameraFrameTimestampMapsIntoMonotonicRealtimeClock()
        {
            System.DateTime utcNow = new System.DateTime(
                2026, 8, 30, 12, 0, 1, System.DateTimeKind.Utc);
            System.DateTime capturedAt = utcNow.AddMilliseconds(-125.0);

            double captureRealtime =
                QuestPersonDetectionRunner.CaptureRealtimeSeconds(
                    capturedAt,
                    utcNow,
                    42.0);

            Assert.That(captureRealtime, Is.EqualTo(41.875).Within(0.0001));
            Assert.That(
                QuestPersonDetectionRunner.CaptureRealtimeSeconds(
                    utcNow.AddSeconds(1.0),
                    utcNow,
                    42.0),
                Is.EqualTo(42.0));
            Assert.That(
                QuestPersonDetectionRunner.CaptureRealtimeSeconds(
                    default,
                    utcNow,
                    42.0),
                Is.EqualTo(42.0));
        }

        [Test]
        public void HeadHistoryResetsAcrossDirectionAndDistanceDiscontinuities()
        {
            Assert.That(
                QuestSpatialObstacleProvider.ShouldResetDistanceHistory(
                    true,
                    Vector3.forward,
                    Vector3.back,
                    true,
                    1.2f,
                    1.1f),
                Is.True);
            Assert.That(
                QuestSpatialObstacleProvider.ShouldResetDistanceHistory(
                    true,
                    Vector3.forward,
                    Vector3.forward,
                    true,
                    2.1f,
                    0.8f),
                Is.True);
            Assert.That(
                QuestSpatialObstacleProvider.ShouldResetDistanceHistory(
                    true,
                    Vector3.forward,
                    Vector3.forward,
                    true,
                    1.2f,
                    1.0f),
                Is.False);
        }

        [Test]
        public void SchedulerProfilesUseRestoredLayerCaps()
        {
            TrackingQualitySettings balanced = TrackingQualitySettings.For(
                TrackingQualityProfile.Balanced);
            TrackingQualitySettings accuracy = TrackingQualitySettings.For(
                TrackingQualityProfile.Accuracy);
            TrackingQualitySettings performance = TrackingQualitySettings.For(
                TrackingQualityProfile.Performance);

            Assert.That(balanced.maximumLayersPerFrame, Is.EqualTo(64));
            Assert.That(accuracy.maximumLayersPerFrame, Is.EqualTo(96));
            Assert.That(performance.maximumLayersPerFrame, Is.EqualTo(48));
        }

        [Test]
        public void SchedulerExpandsBalancedBudgetAndEntersWatchdogRecovery()
        {
            float expanded = InferenceSchedulerPolicy.UpdateSliceBudget(
                TrackingQualityProfile.Balanced,
                1.5f,
                1.5f,
                12f,
                650f,
                400f);
            float recovery = InferenceSchedulerPolicy.UpdateSliceBudget(
                TrackingQualityProfile.Balanced,
                1.5f,
                expanded,
                15f,
                900f,
                760f);

            Assert.That(expanded, Is.EqualTo(1.75f));
            Assert.That(recovery, Is.EqualTo(4f));
            Assert.That(
                InferenceSchedulerPolicy.GetWatchdogState(true, 1499f),
                Is.EqualTo(InferenceWatchdogState.Recovery));
            Assert.That(
                InferenceSchedulerPolicy.GetWatchdogState(true, 1500f),
                Is.EqualTo(InferenceWatchdogState.Reset));
        }

        [Test]
        public void WorldPanelPlacesOnceAndClampsRuntimeControls()
        {
            var head = new GameObject("head");
            var panel = new GameObject("panel");
            try
            {
                head.transform.position = new Vector3(2f, 1.6f, 3f);
                head.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
                var placement = panel.AddComponent<
                    WorldSpacePanelPlacementController>();
                placement.Configure(panel.transform, head.transform);
                placement.BringHere();
                Vector3 placed = panel.transform.position;

                head.transform.position += Vector3.right;
                Assert.That(panel.transform.position, Is.EqualTo(placed));

                placement.SetDistance(10f);
                placement.SetHeight(-10f);
                placement.SetScaleMultiplier(10f);
                Assert.That(placement.DistanceMeters, Is.EqualTo(3f));
                Assert.That(placement.HeightMeters, Is.EqualTo(-0.75f));
                Assert.That(placement.ScaleMultiplier, Is.EqualTo(1.5f));

                panel.transform.position = head.transform.position
                    + new Vector3(1f, 0f, 1f);
                placement.FaceMe();
                Vector3 expectedForward = Vector3.ProjectOnPlane(
                    panel.transform.position - head.transform.position,
                    Vector3.up).normalized;
                Assert.That(
                    Vector3.Angle(panel.transform.forward, expectedForward),
                    Is.LessThan(0.1f));
            }
            finally
            {
                Object.DestroyImmediate(panel);
                Object.DestroyImmediate(head);
            }
        }

        [Test]
        public void SafetyFeedbackPulsesRemainBoundedAndIncludeRestIntervals()
        {
            Assert.That(
                SafetyFeedbackPulse.IsHapticPulseOn(0f, 1f),
                Is.True);
            Assert.That(
                SafetyFeedbackPulse.IsHapticPulseOn(0.18f, 1f),
                Is.False);
            Assert.That(
                SafetyFeedbackPulse.VisualPulse(0f, 0.5f),
                Is.InRange(0f, 1f));
            Assert.That(
                SafetyFeedbackPulse.VisualPulse(10f, 1f),
                Is.InRange(0f, 1f));
        }

        private static SpatialObstacleMeasurement Measurement(
            SpatialObstacleSource source,
            float distanceMeters)
        {
            return new SpatialObstacleMeasurement(
                source,
                1.0,
                true,
                distanceMeters,
                Vector3.forward * distanceMeters,
                Vector3.back,
                0f,
                0.8f,
                1,
                0f,
                0f,
                false,
                false,
                source == SpatialObstacleSource.EnvironmentDepth ? 1 : 0);
        }

        private static StaticRiskMeasurement Risk(float risk)
        {
            return new StaticRiskMeasurement(
                true,
                1f,
                0f,
                false,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                risk);
        }
    }
}
