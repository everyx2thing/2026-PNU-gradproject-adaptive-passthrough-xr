using NUnit.Framework;
using System.IO;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class HazardPresentationGeometryTests
    {
        [Test]
        public void PersonBox_UsesUpperEightyTwoPercentAndPadding()
        {
            var box = new NormalizedBoundingBox(0.5f, 0.5f, 0.4f, 0.8f);

            NormalizedBoundingBox result =
                HazardPresentationGeometry.UpperBodyExpandedBox(box);

            Assert.That(result.width, Is.EqualTo(0.45f).Within(0.0001f));
            Assert.That(
                result.height,
                Is.EqualTo(0.8f * 0.82f * 1.125f).Within(0.0001f));
            float expectedExpandedTop = box.Top
                - box.height * 0.82f * (1.125f - 1f) * 0.5f;
            Assert.That(result.Top,
                Is.EqualTo(expectedExpandedTop).Within(0.0001f));
        }

        [Test]
        public void PersonCorners_IntersectCaptureCameraPlane()
        {
            Vector3 planePoint = new Vector3(0f, 1f, 2f);
            Vector3 normal = Vector3.back;
            var box = new NormalizedBoundingBox(0.5f, 0.5f, 0.4f, 0.8f);

            bool created = HazardPresentationGeometry.TryCreatePersonCapsule(
                42,
                box,
                new Ray(Vector3.zero, new Vector3(-0.2f, -0.3f, 1f)),
                new Ray(Vector3.zero, new Vector3(0.2f, -0.3f, 1f)),
                new Ray(Vector3.zero, new Vector3(0.2f, 0.3f, 1f)),
                new Ray(Vector3.zero, new Vector3(-0.2f, 0.3f, 1f)),
                planePoint,
                normal,
                1.0,
                0.9f,
                0.8f,
                Vector3.zero,
                false,
                out HazardPresentationGeometry geometry);

            Assert.That(created, Is.True);
            Assert.That(geometry.Kind, Is.EqualTo(HazardVisualKind.PersonCapsule));
            Assert.That(geometry.BottomLeft.z, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(geometry.TopRight.z, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void PresentationState_UsesConfiguredTimingAndGeometryExpiry()
        {
            var state = new PassthroughPresentationState();
            HazardPresentationGeometry geometry = CreateGeometry(7, 10.0);

            state.BeginFrame();
            state.Observe(geometry, 0.8f, 10.0, true);
            state.Update(10.125, 0.125f);

            Assert.That(state.Opacity, Is.EqualTo(1f).Within(0.001f));
            Assert.That(state.Phase, Is.EqualTo(PassthroughAnimationPhase.Holding));

            state.BeginFrame();
            state.Update(10.49, 0f);
            Assert.That(state.HasRenderableGeometry, Is.True);
            state.Update(10.51, 0f);
            Assert.That(state.HasRenderableGeometry, Is.False);

            state.Update(11.50, 0f);
            Assert.That(state.Phase, Is.EqualTo(PassthroughAnimationPhase.Fading));
            state.Update(11.80, 0.30f);
            Assert.That(state.Opacity, Is.Zero.Within(0.001f));
            Assert.That(state.Phase, Is.EqualTo(PassthroughAnimationPhase.Hidden));
        }

        [Test]
        public void PresentationState_SeparatesCaptureAgeFromQualifiedHold()
        {
            var state = new PassthroughPresentationState();
            HazardPresentationGeometry geometry = CreateGeometry(8, 10.0);

            state.BeginFrame();
            state.Observe(geometry, 0.8f, 10.0, 10.4, true);
            state.Update(10.4, 0.125f);

            Assert.That(state.HasRenderableGeometry, Is.True);
            Assert.That(state.HoldRemainingSeconds,
                Is.EqualTo(1.5f).Within(0.001f));

            state.BeginFrame();
            state.Update(10.51, 0f);
            Assert.That(state.HasRenderableGeometry, Is.False,
                "World geometry must expire 0.5s after capture, not inference completion.");
            Assert.That(state.HoldRemainingSeconds,
                Is.EqualTo(1.39f).Within(0.001f));
        }

        [Test]
        public void PresentationState_PolicyShutdownRetainsGeometryThroughFade()
        {
            var state = new PassthroughPresentationState();
            HazardPresentationGeometry geometry = CreateGeometry(81, 10.0);

            state.BeginFrame();
            state.Observe(geometry, 0.8f, 10.0, true);
            state.Update(10.125, 0.125f);
            state.SetPresentationPolicyActive(false);

            state.BeginFrame();
            state.Update(10.60, 0.10f);
            state.SetPresentationPolicyActive(false, 10.60);
            Assert.That(state.HasRenderableGeometry, Is.True);
            Assert.That(state.RetainsGeometryUntilHidden, Is.True);

            state.Update(11.50, 0.10f);
            Assert.That(state.Phase, Is.EqualTo(PassthroughAnimationPhase.Fading));
            Assert.That(state.HasRenderableGeometry, Is.True);

            state.Update(11.80, 0.30f);
            Assert.That(state.Phase, Is.EqualTo(PassthroughAnimationPhase.Hidden));
            Assert.That(state.HasRenderableGeometry, Is.False);
            Assert.That(state.RetainsGeometryUntilHidden, Is.False);
        }

        [Test]
        public void PresentationState_PolicyShutdownDoesNotReviveStaleGeometry()
        {
            var state = new PassthroughPresentationState();
            HazardPresentationGeometry geometry = CreateGeometry(82, 10.0);

            state.BeginFrame();
            state.Observe(geometry, 0.8f, 10.0, true);
            state.Update(10.60, 0.125f);
            Assert.That(state.HasRenderableGeometry, Is.False);

            state.SetPresentationPolicyActive(false, 10.60);
            state.BeginFrame();
            state.Update(10.61, 0.01f);

            Assert.That(state.RetainsGeometryUntilHidden, Is.False);
            Assert.That(state.HasRenderableGeometry, Is.False,
                "A policy transition must not bypass capture-age expiry.");
        }

        [Test]
        public void PresentationState_Uses125_300_1500_300MillisecondPhases()
        {
            var state = new PassthroughPresentationState();
            HazardPresentationGeometry first = CreateGeometry(9, 20.0);
            HazardPresentationGeometry moved = HazardPresentationGeometry.CreatePlanePatch(
                9, HazardVisualKind.WallPlane,
                new Vector3(1f, 1f, 1f), Vector3.back, Vector3.up,
                0.5f, 0.8f, 20.1, 1f,
                SpatialObstacleSource.EnvironmentDepth,
                SpatialProbeOwner.Head, SpatialProbePurpose.Standard);

            state.BeginFrame();
            state.Observe(first, 0.9f, 20.0, true);
            state.Update(20.0625, 0.0625f);
            Assert.That(state.Opacity, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(state.Pulse01, Is.EqualTo(0.2083f).Within(0.002f));

            state.BeginFrame();
            state.Observe(moved, 0.9f, 20.1, true);
            state.Update(20.1, 0.1f);
            Assert.That(state.Geometry.Center.x,
                Is.EqualTo(1f - Mathf.Exp(-1f)).Within(0.01f));

            state.BeginFrame();
            state.Update(21.60, 0f);
            Assert.That(state.Phase, Is.EqualTo(PassthroughAnimationPhase.Fading));
            state.Update(21.90, 0.30f);
            Assert.That(state.Phase, Is.EqualTo(PassthroughAnimationPhase.Hidden));
        }

        [Test]
        public void PresentationState_SourceIdChangeOnSameSurfaceKeepsGeometry()
        {
            var state = new PassthroughPresentationState();
            HazardPresentationGeometry first = CreateGeometry(100, 1.0);
            HazardPresentationGeometry next =
                HazardPresentationGeometry.CreatePlanePatch(
                    200,
                    HazardVisualKind.WallPlane,
                    first.Center + Vector3.right * 0.1f,
                    Quaternion.AngleAxis(10f, Vector3.up) * Vector3.back,
                    Vector3.up,
                    first.Width,
                    first.Height,
                    1.1,
                    1f,
                    SpatialObstacleSource.Fused,
                    SpatialProbeOwner.Head,
                    SpatialProbePurpose.Standard);

            state.BeginFrame();
            state.Observe(first, 0.8f, 1.0, true);
            state.Update(1.0, 0.125f);
            state.BeginFrame();
            state.Observe(next, 0.8f, 1.1, true);
            state.Update(1.1, 0.1f);

            Assert.That(state.HasRenderableGeometry, Is.True);
            Assert.That(state.Geometry.StableId, Is.EqualTo(100));
            Assert.That(state.Phase, Is.Not.EqualTo(
                PassthroughAnimationPhase.Hidden));
        }

        [Test]
        public void PresentationState_DifferentSurfaceNeedsQuarterSecondConfirmation()
        {
            var state = new PassthroughPresentationState();
            HazardPresentationGeometry first = CreateGeometry(101, 2.0);
            HazardPresentationGeometry next =
                HazardPresentationGeometry.CreatePlanePatch(
                    202,
                    HazardVisualKind.WallPlane,
                    first.Center + Vector3.right,
                    Vector3.left,
                    Vector3.up,
                    first.Width,
                    first.Height,
                    2.1,
                    1f,
                    SpatialObstacleSource.EnvironmentDepth,
                    SpatialProbeOwner.Head,
                    SpatialProbePurpose.Standard);

            state.BeginFrame();
            state.Observe(first, 0.8f, 2.0, true);
            state.Update(2.0, 0.125f);
            state.BeginFrame();
            state.Observe(next, 0.8f, 2.1, true);
            state.Update(2.1, 0.1f);
            Assert.That(state.Geometry.StableId, Is.EqualTo(101));

            state.BeginFrame();
            state.Observe(next, 0.8f, 2.36, true);
            state.Update(2.36, 0.1f);
            Assert.That(state.Geometry.StableId, Is.EqualTo(202));
        }

        [Test]
        public void PresentationState_StaleIncumbentDoesNotDelayOrHideNewSurface()
        {
            var state = new PassthroughPresentationState();
            HazardPresentationGeometry first = CreateGeometry(401, 10.0);
            HazardPresentationGeometry next =
                HazardPresentationGeometry.CreatePlanePatch(
                    402,
                    HazardVisualKind.WallPlane,
                    first.Center + Vector3.right,
                    Vector3.left,
                    Vector3.up,
                    first.Width,
                    first.Height,
                    10.60,
                    1f,
                    SpatialObstacleSource.EnvironmentDepth,
                    SpatialProbeOwner.Head,
                    SpatialProbePurpose.Standard);

            state.BeginFrame();
            state.Observe(first, 0.8f, 10.0, true);
            state.Update(10.125, 0.125f);
            state.BeginFrame();
            state.Update(10.60, 0f);
            Assert.That(state.HasRenderableGeometry, Is.False,
                "The incumbent is capture-stale while its policy hold remains active.");

            state.BeginFrame();
            state.Observe(next, 0.8f, 10.60, true);
            state.Update(10.6625, 0.0625f);

            Assert.That(state.Geometry.StableId, Is.EqualTo(402),
                "A stale incumbent must not impose the surface handoff dwell.");
            Assert.That(state.Opacity, Is.EqualTo(0.5f).Within(0.001f),
                "The replacement must run its visible 125 ms appearance.");
            Assert.That(state.Pulse01, Is.EqualTo(0.2083f).Within(0.002f),
                "The warning pulse must start when the replacement becomes renderable.");
        }

        [Test]
        public void PresentationState_HiddenIncumbentIsClearedBeforeNewSurface()
        {
            var state = new PassthroughPresentationState();
            HazardPresentationGeometry first = CreateGeometry(451, 30.0);
            HazardPresentationGeometry next =
                HazardPresentationGeometry.CreatePlanePatch(
                    452,
                    HazardVisualKind.WallPlane,
                    first.Center + Vector3.right,
                    Vector3.left,
                    Vector3.up,
                    first.Width,
                    first.Height,
                    31.60,
                    1f,
                    SpatialObstacleSource.EnvironmentDepth,
                    SpatialProbeOwner.Head,
                    SpatialProbePurpose.Standard);

            state.BeginFrame();
            state.Observe(first, 0.8f, 30.0, true);
            state.Update(30.125, 0.125f);
            state.BeginFrame();
            state.Update(31.80, 1.675f);
            Assert.That(state.Phase, Is.EqualTo(
                PassthroughAnimationPhase.Hidden));
            Assert.That(state.HasRenderableGeometry, Is.False);

            state.BeginFrame();
            state.Observe(next, 0.8f, 31.80, true);
            state.Update(31.8625, 0.0625f);

            Assert.That(state.Geometry.StableId, Is.EqualTo(452));
            Assert.That(state.Opacity, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(state.Pulse01, Is.GreaterThan(0f));
        }

        [Test]
        public void PresentationState_PendingSurfaceGapBreaksContinuousConfirmation()
        {
            var state = new PassthroughPresentationState();
            HazardPresentationGeometry first = CreateGeometry(501, 20.0);
            HazardPresentationGeometry next =
                HazardPresentationGeometry.CreatePlanePatch(
                    502,
                    HazardVisualKind.WallPlane,
                    first.Center + Vector3.right,
                    Vector3.left,
                    Vector3.up,
                    first.Width,
                    first.Height,
                    20.05,
                    1f,
                    SpatialObstacleSource.EnvironmentDepth,
                    SpatialProbeOwner.Head,
                    SpatialProbePurpose.Standard);

            state.BeginFrame();
            state.Observe(first, 0.8f, 20.0, true);
            state.Update(20.0, 0.125f);
            state.BeginFrame();
            state.Observe(next, 0.8f, 20.05, true);
            state.Update(20.05, 0.05f);
            Assert.That(state.Geometry.StableId, Is.EqualTo(501));

            state.BeginFrame();
            state.Update(20.46, 0f);
            state.BeginFrame();
            state.Observe(next, 0.8f, 20.46, true);
            state.Update(20.46, 0f);

            Assert.That(state.Geometry.StableId, Is.EqualTo(501),
                "A candidate returning after the maximum observation gap must restart confirmation.");
        }

        [Test]
        public void PresentationState_ChannelOffCancelsHoldAndUsesFadeOnly()
        {
            var state = new PassthroughPresentationState();
            state.BeginFrame();
            state.Observe(CreateGeometry(303, 3.0), 0.9f, 3.0, true);
            state.Update(3.125, 0.125f);
            Assert.That(state.Opacity, Is.EqualTo(1f).Within(0.001f));

            state.BeginFrame();
            state.CancelHoldAndFade(3.2);
            state.Update(3.35, 0.15f);

            Assert.That(state.HoldRemainingSeconds, Is.Zero);
            Assert.That(state.Phase, Is.EqualTo(
                PassthroughAnimationPhase.Fading));
            Assert.That(state.Opacity, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void FloorCorridor_HasSpecifiedNearAndFarWidths()
        {
            HazardPresentationGeometry geometry =
                HazardPresentationGeometry.CreateFloorCorridor(
                    5,
                    new Vector3(0f, 0f, 0.3f),
                    new Vector3(0f, 0f, 1.2f),
                    Vector3.up,
                    0.20f,
                    0.45f,
                    1.0,
                    1f,
                    1f,
                    SpatialObstacleSource.EnvironmentDepth);

            Assert.That(
                Vector3.Distance(geometry.BottomLeft, geometry.BottomRight),
                Is.EqualTo(0.20f).Within(0.001f));
            Assert.That(
                Vector3.Distance(geometry.TopLeft, geometry.TopRight),
                Is.EqualTo(0.45f).Within(0.001f));
        }

        [Test]
        public void NearlyHorizontalPlane_NormalNoiseKeepsAxesAndWindingStable()
        {
            HazardPresentationGeometry positiveTilt =
                HazardPresentationGeometry.CreatePlanePatch(
                    91,
                    HazardVisualKind.LowObstaclePatch,
                    new Vector3(0f, 0.4f, 1f),
                    new Vector3(0.001f, 1f, 0f).normalized,
                    Vector3.up,
                    1.2f,
                    0.8f,
                    1.0,
                    1f,
                    SpatialObstacleSource.EnvironmentDepth,
                    SpatialProbeOwner.Head,
                    SpatialProbePurpose.LocomotionCorridor);
            HazardPresentationGeometry negativeTilt =
                HazardPresentationGeometry.CreatePlanePatch(
                    91,
                    HazardVisualKind.LowObstaclePatch,
                    new Vector3(0f, 0.4f, 1f),
                    new Vector3(-0.001f, 1f, 0f).normalized,
                    Vector3.up,
                    1.2f,
                    0.8f,
                    1.1,
                    1f,
                    SpatialObstacleSource.EnvironmentDepth,
                    SpatialProbeOwner.Head,
                    SpatialProbePurpose.LocomotionCorridor);

            Vector3 positiveRight =
                (positiveTilt.BottomRight - positiveTilt.BottomLeft).normalized;
            Vector3 negativeRight =
                (negativeTilt.BottomRight - negativeTilt.BottomLeft).normalized;
            Vector3 positiveUp =
                (positiveTilt.TopLeft - positiveTilt.BottomLeft).normalized;
            Vector3 negativeUp =
                (negativeTilt.TopLeft - negativeTilt.BottomLeft).normalized;
            Assert.That(Vector3.Dot(positiveRight, negativeRight),
                Is.GreaterThan(0.99f));
            Assert.That(Vector3.Dot(positiveUp, negativeUp),
                Is.GreaterThan(0.99f));

            Vector3 positiveWinding = Vector3.Cross(
                positiveTilt.BottomRight - positiveTilt.BottomLeft,
                positiveTilt.TopLeft - positiveTilt.BottomLeft).normalized;
            Vector3 negativeWinding = Vector3.Cross(
                negativeTilt.BottomRight - negativeTilt.BottomLeft,
                negativeTilt.TopLeft - negativeTilt.BottomLeft).normalized;
            Assert.That(Vector3.Dot(
                    positiveWinding,
                    positiveTilt.SurfaceNormal),
                Is.GreaterThan(0.99f));
            Assert.That(Vector3.Dot(
                    negativeWinding,
                    negativeTilt.SurfaceNormal),
                Is.GreaterThan(0.99f));

            HazardPresentationGeometry midpoint =
                HazardPresentationGeometry.Lerp(
                    positiveTilt,
                    negativeTilt,
                    0.5f);
            Assert.That(midpoint.Width, Is.GreaterThan(1.1f));
            Assert.That(midpoint.Height, Is.GreaterThan(0.7f));
        }

        [Test]
        public void NearHorizontalPlane_OneToSevenDegreesKeepsBasisExtentAndWinding()
        {
            float[] tiltDegrees = { 1f, 5f, 6f, 7f };
            Vector3 previousRight = Vector3.zero;
            Vector3 previousUp = Vector3.zero;
            for (int i = 0; i < tiltDegrees.Length; i++)
            {
                Vector3 normal = Quaternion.AngleAxis(
                    tiltDegrees[i],
                    Vector3.forward) * Vector3.up;
                HazardPresentationGeometry.ResolvePlaneBasis(
                    normal,
                    Vector3.up,
                    out Vector3 expectedRight,
                    out Vector3 expectedUp);
                HazardPresentationGeometry geometry =
                    HazardPresentationGeometry.CreatePlanePatch(
                        100 + i,
                        HazardVisualKind.LowObstaclePatch,
                        new Vector3(0f, 0.4f, 1f),
                        normal,
                        Vector3.up,
                        1.2f,
                        0.4f,
                        1.0 + i * 0.1,
                        1f,
                        SpatialObstacleSource.EnvironmentDepth,
                        SpatialProbeOwner.Head,
                        SpatialProbePurpose.LocomotionCorridor);

                Vector3 actualRight = (geometry.BottomRight
                    - geometry.BottomLeft).normalized;
                Vector3 actualUp = (geometry.TopLeft
                    - geometry.BottomLeft).normalized;
                Vector3 winding = Vector3.Cross(
                    geometry.BottomRight - geometry.BottomLeft,
                    geometry.TopLeft - geometry.BottomLeft).normalized;
                Assert.That(Vector3.Dot(actualRight, expectedRight),
                    Is.GreaterThan(0.999f));
                Assert.That(Vector3.Dot(actualUp, expectedUp),
                    Is.GreaterThan(0.999f));
                Assert.That(geometry.Width, Is.EqualTo(1.2f).Within(0.001f));
                Assert.That(geometry.Height, Is.EqualTo(0.4f).Within(0.001f));
                Assert.That(Vector3.Dot(winding, geometry.SurfaceNormal),
                    Is.GreaterThan(0.999f));

                if (i > 0)
                {
                    Assert.That(Vector3.Dot(previousRight, actualRight),
                        Is.GreaterThan(0.99f));
                    Assert.That(Vector3.Dot(previousUp, actualUp),
                        Is.GreaterThan(0.99f));
                }

                previousRight = actualRight;
                previousUp = actualUp;
            }
        }

        [Test]
        public void Fusion_ProjectsEnvironmentPatchOntoRoomPlane()
        {
            HazardPresentationGeometry environmentGeometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    1, HazardVisualKind.WallPlane,
                    new Vector3(0f, 1f, 1.10f), Vector3.back, Vector3.up,
                    0.5f, 0.8f, 1.0, 0.9f,
                    SpatialObstacleSource.EnvironmentDepth,
                    SpatialProbeOwner.Head, SpatialProbePurpose.Standard);
            HazardPresentationGeometry roomGeometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    2, HazardVisualKind.WallPlane,
                    new Vector3(0f, 1f, 1.20f), Vector3.back, Vector3.up,
                    1f, 2f, 1.0, 0.8f,
                    SpatialObstacleSource.RoomScene,
                    SpatialProbeOwner.Head, SpatialProbePurpose.Standard);
            SpatialObstacleMeasurement environment = Measurement(
                SpatialObstacleSource.EnvironmentDepth,
                1.10f,
                environmentGeometry);
            SpatialObstacleMeasurement room = Measurement(
                SpatialObstacleSource.RoomScene,
                1.20f,
                roomGeometry);

            SpatialObstacleMeasurement result =
                new SpatialObstacleFusionFilter().Fuse(
                    SpatialProbeOwner.Head,
                    environment,
                    room,
                    1.0);

            Assert.That(result.Source, Is.EqualTo(SpatialObstacleSource.Fused));
            Assert.That(result.PresentationGeometry.Source,
                Is.EqualTo(SpatialObstacleSource.Fused));
            Assert.That(
                result.PresentationGeometry.Center.z,
                Is.EqualTo(roomGeometry.Center.z).Within(0.001f));
        }

        [Test]
        public void Fusion_UsesFiniteRoomBoundsAndPreservesZeroAnchorId()
        {
            HazardPresentationGeometry environmentGeometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    41, HazardVisualKind.WallPlane,
                    new Vector3(0.45f, 1f, 1.10f), Vector3.back, Vector3.up,
                    0.20f, 0.50f, 1.0, 0.9f,
                    SpatialObstacleSource.EnvironmentDepth,
                    SpatialProbeOwner.Head, SpatialProbePurpose.Standard);
            HazardPresentationGeometry localRoomPatch =
                HazardPresentationGeometry.CreatePlanePatch(
                    0, HazardVisualKind.WallPlane,
                    new Vector3(0f, 1f, 1.20f), Vector3.back, Vector3.up,
                    0.20f, 0.50f, 1.0, 0.8f,
                    SpatialObstacleSource.RoomScene,
                    SpatialProbeOwner.Head, SpatialProbePurpose.Standard);
            HazardPresentationGeometry finiteRoomBounds =
                HazardPresentationGeometry.CreatePlanePatch(
                    0, HazardVisualKind.WallPlane,
                    new Vector3(0f, 1f, 1.20f), Vector3.back, Vector3.up,
                    2.00f, 2.00f, 1.0, 0.8f,
                    SpatialObstacleSource.RoomScene,
                    SpatialProbeOwner.Head, SpatialProbePurpose.Standard);
            SpatialObstacleMeasurement room = Measurement(
                SpatialObstacleSource.RoomScene,
                1.20f,
                localRoomPatch,
                finiteRoomBounds);

            Assert.That(SpatialObstacleFusionFilter
                .AreMeasurementsSpatiallyCompatible(
                    Measurement(
                        SpatialObstacleSource.EnvironmentDepth,
                        1.10f,
                        environmentGeometry),
                    room), Is.True);

            SpatialObstacleMeasurement result =
                new SpatialObstacleFusionFilter().Fuse(
                    SpatialProbeOwner.Head,
                    Measurement(
                        SpatialObstacleSource.EnvironmentDepth,
                        1.10f,
                        environmentGeometry),
                    room,
                    1.0);

            Assert.That(result.Source, Is.EqualTo(SpatialObstacleSource.Fused));
            Assert.That(result.PresentationGeometry.StableId, Is.EqualTo(0));
        }

        [Test]
        public void FusionCompatibility_RejectsDifferentNormalPlaneAndBounds()
        {
            HazardPresentationGeometry environmentGeometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    11, HazardVisualKind.WallPlane,
                    new Vector3(0f, 1f, 1f), Vector3.back, Vector3.up,
                    0.4f, 0.8f, 1.0, 0.9f,
                    SpatialObstacleSource.EnvironmentDepth,
                    SpatialProbeOwner.Head, SpatialProbePurpose.Standard);
            SpatialObstacleMeasurement environment = Measurement(
                SpatialObstacleSource.EnvironmentDepth,
                1f,
                environmentGeometry);
            HazardPresentationGeometry rotatedRoomGeometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    12, HazardVisualKind.WallPlane,
                    new Vector3(0f, 1f, 1.1f), Vector3.left, Vector3.up,
                    1f, 2f, 1.0, 0.8f,
                    SpatialObstacleSource.RoomScene,
                    SpatialProbeOwner.Head, SpatialProbePurpose.Standard);
            HazardPresentationGeometry offsetRoomGeometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    13, HazardVisualKind.WallPlane,
                    new Vector3(0f, 1f, 1.4f), Vector3.back, Vector3.up,
                    1f, 2f, 1.0, 0.8f,
                    SpatialObstacleSource.RoomScene,
                    SpatialProbeOwner.Head, SpatialProbePurpose.Standard);
            HazardPresentationGeometry outsideRoomGeometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    14, HazardVisualKind.WallPlane,
                    new Vector3(1f, 1f, 1.1f), Vector3.back, Vector3.up,
                    0.2f, 2f, 1.0, 0.8f,
                    SpatialObstacleSource.RoomScene,
                    SpatialProbeOwner.Head, SpatialProbePurpose.Standard);

            Assert.That(SpatialObstacleFusionFilter
                .AreMeasurementsSpatiallyCompatible(
                    environment,
                    Measurement(
                        SpatialObstacleSource.RoomScene,
                        1.1f,
                        rotatedRoomGeometry)), Is.False);
            Assert.That(SpatialObstacleFusionFilter
                .AreMeasurementsSpatiallyCompatible(
                    environment,
                    Measurement(
                        SpatialObstacleSource.RoomScene,
                        1.4f,
                        offsetRoomGeometry)), Is.False);
            Assert.That(SpatialObstacleFusionFilter
                .AreMeasurementsSpatiallyCompatible(
                    environment,
                    Measurement(
                        SpatialObstacleSource.RoomScene,
                        1.1f,
                        outsideRoomGeometry)), Is.False);
        }

        [Test]
        public void Fusion_IncompatibleNearbySurfacesStayOnConflictPath()
        {
            HazardPresentationGeometry environmentGeometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    21, HazardVisualKind.WallPlane,
                    new Vector3(0f, 1f, 1f), Vector3.back, Vector3.up,
                    0.4f, 0.8f, 1.0, 0.9f,
                    SpatialObstacleSource.EnvironmentDepth,
                    SpatialProbeOwner.Head, SpatialProbePurpose.Standard);
            HazardPresentationGeometry roomGeometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    22, HazardVisualKind.WallPlane,
                    new Vector3(0f, 1f, 1.1f), Vector3.left, Vector3.up,
                    1f, 2f, 1.0, 0.8f,
                    SpatialObstacleSource.RoomScene,
                    SpatialProbeOwner.Head, SpatialProbePurpose.Standard);

            SpatialObstacleMeasurement result =
                new SpatialObstacleFusionFilter().Fuse(
                    SpatialProbeOwner.Head,
                    Measurement(
                        SpatialObstacleSource.EnvironmentDepth,
                        1f,
                        environmentGeometry),
                    Measurement(
                        SpatialObstacleSource.RoomScene,
                        1.1f,
                        roomGeometry),
                    1.0);

            Assert.That(result.SelectedSource,
                Is.EqualTo(SpatialObstacleSource.RoomScene));
            Assert.That(result.PresentationGeometry.StableId, Is.EqualTo(22));
        }

        [Test]
        public void Fusion_ConflictDoesNotBorrowEnvironmentGeometryWhenRoomWins()
        {
            HazardPresentationGeometry environmentGeometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    31, HazardVisualKind.WallPlane,
                    new Vector3(0f, 1f, 0.8f), Vector3.back, Vector3.up,
                    0.4f, 0.8f, 1.0, 0.9f,
                    SpatialObstacleSource.EnvironmentDepth,
                    SpatialProbeOwner.Head, SpatialProbePurpose.Standard);
            SpatialObstacleMeasurement environment = Measurement(
                SpatialObstacleSource.EnvironmentDepth,
                0.8f,
                environmentGeometry);
            SpatialObstacleMeasurement room = MeasurementWithoutGeometry(
                SpatialObstacleSource.RoomScene,
                1.2f,
                Vector3.left);

            SpatialObstacleMeasurement result =
                new SpatialObstacleFusionFilter().Fuse(
                    SpatialProbeOwner.Head,
                    environment,
                    room,
                    1.0);

            Assert.That(result.Source, Is.EqualTo(SpatialObstacleSource.RoomScene));
            Assert.That(result.SelectedSource,
                Is.EqualTo(SpatialObstacleSource.RoomScene));
            Assert.That(result.PresentationGeometry.Available, Is.False,
                "A Room-selected conflict must not publish Environment geometry.");
        }

        [Test]
        public void Fusion_SelfRejectedEnvironmentGeometryIsNotUsedForRoomResult()
        {
            HazardPresentationGeometry rejectedEnvironmentGeometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    32, HazardVisualKind.WallPlane,
                    new Vector3(0f, 1f, 0.4f), Vector3.back, Vector3.up,
                    0.4f, 0.8f, 1.0, 0.9f,
                    SpatialObstacleSource.EnvironmentDepth,
                    SpatialProbeOwner.Head, SpatialProbePurpose.Standard);
            SpatialObstacleMeasurement rejectedEnvironment =
                MeasurementWithSelfRejection(
                    0.4f,
                    rejectedEnvironmentGeometry);
            SpatialObstacleMeasurement room = MeasurementWithoutGeometry(
                SpatialObstacleSource.RoomScene,
                1.0f,
                Vector3.back);

            SpatialObstacleMeasurement result =
                new SpatialObstacleFusionFilter().Fuse(
                    SpatialProbeOwner.Head,
                    rejectedEnvironment,
                    room,
                    1.0);

            Assert.That(result.Source, Is.EqualTo(SpatialObstacleSource.RoomScene));
            Assert.That(result.SelectedSource,
                Is.EqualTo(SpatialObstacleSource.RoomScene));
            Assert.That(result.PresentationGeometry.Available, Is.False,
                "Self-rejected Environment geometry must not leak into a Room result.");
        }

        [Test]
        public void Fusion_EnvironmentEmergencyWithoutGeometryDoesNotBorrowRoomGeometry()
        {
            SpatialObstacleMeasurement environment = MeasurementWithoutGeometry(
                SpatialObstacleSource.EnvironmentDepth,
                0.2f,
                Vector3.back);
            HazardPresentationGeometry roomGeometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    33, HazardVisualKind.WallPlane,
                    new Vector3(0f, 1f, 1f), Vector3.back, Vector3.up,
                    1f, 2f, 1.0, 0.8f,
                    SpatialObstacleSource.RoomScene,
                    SpatialProbeOwner.Head, SpatialProbePurpose.Standard);

            SpatialObstacleMeasurement result =
                new SpatialObstacleFusionFilter().Fuse(
                    SpatialProbeOwner.Head,
                    environment,
                    Measurement(
                        SpatialObstacleSource.RoomScene,
                        1.0f,
                        roomGeometry),
                    1.0);

            Assert.That(result.Source,
                Is.EqualTo(SpatialObstacleSource.EnvironmentDepth));
            Assert.That(result.SelectedSource,
                Is.EqualTo(SpatialObstacleSource.EnvironmentDepth));
            Assert.That(result.PresentationGeometry.Available, Is.False,
                "An Environment-selected emergency must not borrow Room geometry.");
        }

        [Test]
        public void ProductionAndDebugShaders_ShareWorldCornerProjectionPath()
        {
            string shaderDirectory = Path.Combine(
                Application.dataPath,
                "Shaders",
                "AdaptivePassthrough");
            string production = File.ReadAllText(Path.Combine(
                shaderDirectory,
                "PassthroughWindow.shader"));
            string debug = File.ReadAllText(Path.Combine(
                shaderDirectory,
                "HazardPresentationDebug.shader"));

            StringAssert.Contains("HazardPresentationCommon.hlsl", production);
            StringAssert.Contains("HazardPresentationCommon.hlsl", debug);
            StringAssert.Contains("TransformWorldToHClip", production);
            StringAssert.Contains("#pragma multi_compile_instancing", production);
            StringAssert.Contains("UNITY_VERTEX_OUTPUT_STEREO", production);
            StringAssert.Contains("UNITY_SETUP_INSTANCE_ID", production);
            StringAssert.Contains(
                "UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO",
                production);
            StringAssert.DoesNotContain("_RectLeft", production);
            StringAssert.DoesNotContain("unity_StereoEyeIndex", production);
        }

        [Test]
        public void FusionState_DoesNotLeakBetweenStandardAndCorridorPurpose()
        {
            var filter = new SpatialObstacleFusionFilter();
            SpatialObstacleMeasurement standard = Measurement(
                SpatialObstacleSource.EnvironmentDepth,
                2f,
                CreateGeometry(4, 1.0));
            filter.Fuse(
                SpatialProbeOwner.Head,
                standard,
                SpatialObstacleMeasurement.Unavailable(1.0),
                1.0);
            SpatialObstacleMeasurement corridorUnavailable =
                new SpatialObstacleMeasurement(
                    SpatialObstacleSource.Unavailable,
                    1.1,
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
                    SpatialProbeOwner.Head,
                    SpatialObstacleSource.Unavailable,
                    -1f,
                    -1f,
                    false,
                    string.Empty,
                    SpatialProbePurpose.LocomotionCorridor);

            SpatialObstacleMeasurement result = filter.Fuse(
                SpatialProbeOwner.Head,
                corridorUnavailable,
                corridorUnavailable,
                1.1);

            Assert.That(result.Available, Is.False);
        }

        [Test]
        public void RoomCorridorGate_RejectsSideBehindAndWallPlane()
        {
            Vector3 direction = QuestSpatialObstacleProvider
                .LocomotionCorridorDirection(Vector3.forward, 42f);

            Assert.That(FiniteSpatialBoundsMath.IsInsideLocomotionCorridor(
                direction * 1.0f,
                direction,
                0.10f,
                out _,
                out _), Is.True);
            Assert.That(FiniteSpatialBoundsMath.IsInsideLocomotionCorridor(
                Vector3.right * 1.0f,
                direction,
                0.10f,
                out _,
                out _), Is.False);
            Assert.That(FiniteSpatialBoundsMath.IsInsideLocomotionCorridor(
                -direction,
                direction,
                0.10f,
                out _,
                out _), Is.False);
            Assert.That(FiniteSpatialBoundsMath.IsLowObstacleSemantic(
                "WALL_FACE",
                false), Is.False);
            Assert.That(FiniteSpatialBoundsMath.IsLowObstacleSemantic(
                "TABLE",
                true), Is.True);
            Assert.That(FiniteSpatialBoundsMath.IsLowObstacleSemantic(
                "WALL_FACE",
                true), Is.False);
            Assert.That(FiniteSpatialBoundsMath.IsLowObstacleHeight(
                true,
                2.2f), Is.False);
            Assert.That(FiniteSpatialBoundsMath.IsLowObstacleHeight(
                true,
                0.8f), Is.True);
        }

        [Test]
        public void QualityProfiles_RetainOriginalRayBudgets()
        {
            Assert.That(TrackingQualitySettings.For(
                TrackingQualityProfile.Balanced).spatialRayCount, Is.EqualTo(12));
            Assert.That(TrackingQualitySettings.For(
                TrackingQualityProfile.Accuracy).spatialRayCount, Is.EqualTo(24));
            Assert.That(TrackingQualitySettings.For(
                TrackingQualityProfile.Performance).spatialRayCount, Is.EqualTo(6));
        }

        [Test]
        public void PresentationSurfaceAssociation_KeepsPlaneAndSplitsDiscontinuity()
        {
            Assert.That(FiniteSpatialBoundsMath.IsSamePresentationSurface(
                Vector3.zero,
                Vector3.forward,
                new Vector3(0.4f, 0f, 0.05f),
                Vector3.forward), Is.True);
            Assert.That(FiniteSpatialBoundsMath.IsSamePresentationSurface(
                Vector3.zero,
                Vector3.forward,
                new Vector3(1.1f, 0f, 0f),
                Vector3.forward), Is.False);
            Assert.That(FiniteSpatialBoundsMath.IsSamePresentationSurface(
                Vector3.zero,
                Vector3.forward,
                new Vector3(0.1f, 0f, 0f),
                Vector3.right), Is.False);
        }

        private static SpatialObstacleMeasurement Measurement(
            SpatialObstacleSource source,
            float distance,
            HazardPresentationGeometry geometry,
            HazardPresentationGeometry surfaceBounds = default)
        {
            return new SpatialObstacleMeasurement(
                source, 1.0, true, distance, geometry.Center,
                geometry.SurfaceNormal, 0f, 0.9f, 3, 0.02f, 0f, false,
                false, 3, SpatialProbeOwner.Head, source,
                source == SpatialObstacleSource.EnvironmentDepth ? distance : -1f,
                source == SpatialObstacleSource.RoomScene ? distance : -1f,
                false, string.Empty, SpatialProbePurpose.Standard,
                (int)geometry.StableId, geometry, 0f, surfaceBounds);
        }

        private static SpatialObstacleMeasurement MeasurementWithoutGeometry(
            SpatialObstacleSource source,
            float distance,
            Vector3 hitNormal)
        {
            return new SpatialObstacleMeasurement(
                source, 1.0, true, distance, new Vector3(0f, 1f, distance),
                hitNormal, 0f, 0.9f, 3, 0.02f, 0f, false,
                false, 3, SpatialProbeOwner.Head, source,
                source == SpatialObstacleSource.EnvironmentDepth ? distance : -1f,
                source == SpatialObstacleSource.RoomScene ? distance : -1f,
                false, string.Empty, SpatialProbePurpose.Standard,
                0, default);
        }

        private static SpatialObstacleMeasurement MeasurementWithSelfRejection(
            float distance,
            HazardPresentationGeometry geometry)
        {
            return new SpatialObstacleMeasurement(
                SpatialObstacleSource.EnvironmentDepth,
                1.0,
                true,
                distance,
                geometry.Center,
                geometry.SurfaceNormal,
                0f,
                0.9f,
                3,
                0.02f,
                0f,
                false,
                false,
                3,
                SpatialProbeOwner.Head,
                SpatialObstacleSource.EnvironmentDepth,
                distance,
                -1f,
                true,
                "self_body",
                SpatialProbePurpose.Standard,
                (int)geometry.StableId,
                geometry);
        }

        private static HazardPresentationGeometry CreateGeometry(
            long id,
            double timestamp)
        {
            return HazardPresentationGeometry.CreatePlanePatch(
                id, HazardVisualKind.WallPlane,
                new Vector3(0f, 1f, 1f), Vector3.back, Vector3.up,
                0.5f, 0.8f, timestamp, 1f,
                SpatialObstacleSource.EnvironmentDepth,
                SpatialProbeOwner.Head, SpatialProbePurpose.Standard);
        }
    }
}
