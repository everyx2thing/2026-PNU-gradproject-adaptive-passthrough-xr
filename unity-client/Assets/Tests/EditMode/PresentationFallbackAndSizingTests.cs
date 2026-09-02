using NUnit.Framework;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace TeamVR.AdaptivePassthrough.Tests
{
    public sealed class PresentationFallbackAndSizingTests
    {
        [Test]
        public void PresentationGeometryCanExistWithoutMetricRiskDistance()
        {
            HazardPresentationGeometry geometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    7,
                    HazardVisualKind.PersonCapsule,
                    new Vector3(0f, 1.2f, 1.1f),
                    Vector3.back,
                    Vector3.up,
                    0.5f,
                    1.1f,
                    1.0,
                    0.35f,
                    SpatialObstacleSource.Unavailable,
                    SpatialProbeOwner.Head,
                    SpatialProbePurpose.Standard);
            var measurement = new PersonDistanceMeasurement(
                7,
                1.0,
                PersonDistanceSource.BoundingBoxProxy,
                true,
                0f,
                0f,
                0f,
                0,
                0,
                0f,
                0.42f,
                "depth_unavailable",
                presentationGeometry: geometry,
                presentationGeometrySource:
                    PersonPresentationGeometrySource.BoundingBoxEstimate,
                presentationDistanceMeters: 1.1f);

            Assert.That(measurement.IsMetricReliable, Is.False);
            Assert.That(measurement.HasPresentationGeometry, Is.True);
            Assert.That(
                measurement.PresentationGeometrySource,
                Is.EqualTo(
                    PersonPresentationGeometrySource.BoundingBoxEstimate));
            Assert.That(
                measurement.PresentationDistanceMeters,
                Is.EqualTo(1.1f).Within(0.001f));
        }

        [Test]
        public void BboxPresentationEstimatePlacesStrongClosePersonNearby()
        {
            var box = new NormalizedBoundingBox(
                0.5f,
                0.5f,
                0.82f,
                0.96f);

            float distance =
                PersonPresentationGeometryMath.EstimateDistanceMeters(
                    box,
                    new Ray(Vector3.zero, new Vector3(0f, 0.45f, 1f)),
                    new Ray(Vector3.zero, new Vector3(0f, -0.45f, 1f)));

            Assert.That(
                distance,
                Is.InRange(
                    PersonPresentationGeometryMath
                        .MinimumEstimatedDistanceMeters,
                    PersonPresentationGeometryMath
                        .StrongCloseMaximumDistanceMeters));
        }

        [Test]
        public void PresentationTrackHistoryExpiresAfterConfiguredWindow()
        {
            Assert.That(
                PersonPresentationGeometryMath.TryHistoryDistance(
                    Vector3.zero,
                    Vector3.forward,
                    new Vector3(0f, 1f, 1.5f),
                    Vector3.zero,
                    0.50,
                    out float heldDistance),
                Is.True);
            Assert.That(heldDistance, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(
                PersonPresentationGeometryMath.TryHistoryDistance(
                    Vector3.zero,
                    Vector3.forward,
                    new Vector3(0f, 1f, 1.5f),
                    Vector3.zero,
                    0.751,
                    out _),
                Is.False);
        }

        [Test]
        public void OffCentrePresentationUsesCaptureForwardDepth()
        {
            var box = new NormalizedBoundingBox(0.75f, 0.50f, 0.20f, 0.50f);
            Vector3 center = new Vector3(0.45f, 0f, 1f).normalized;
            Assert.That(
                PersonPresentationGeometryMath.TryCreateAtDistance(
                    11,
                    box,
                    new Ray(Vector3.zero, new Vector3(0.30f, -0.3f, 1f)),
                    new Ray(Vector3.zero, new Vector3(0.60f, -0.3f, 1f)),
                    new Ray(Vector3.zero, new Vector3(0.60f, 0.3f, 1f)),
                    new Ray(Vector3.zero, new Vector3(0.30f, 0.3f, 1f)),
                    new Ray(Vector3.zero, center),
                    Vector3.forward,
                    1.5f,
                    1.0,
                    0.5f,
                    Vector3.zero,
                    false,
                    out HazardPresentationGeometry geometry),
                Is.True);
            Assert.That(
                Vector3.Dot(geometry.Center, Vector3.forward),
                Is.EqualTo(1.5f).Within(0.001f));
        }

        [Test]
        public void WallPresentationExpandsWithRiskAndPreservesPlane()
        {
            HazardPresentationGeometry wall =
                HazardPresentationGeometry.CreatePlanePatch(
                    31,
                    HazardVisualKind.WallPlane,
                    new Vector3(0f, 1.5f, 1f),
                    Vector3.back,
                    Vector3.up,
                    0.20f,
                    0.30f,
                    1.0,
                    0.9f,
                    SpatialObstacleSource.EnvironmentDepth,
                    SpatialProbeOwner.Head,
                    SpatialProbePurpose.Standard);

            HazardPresentationGeometry lowRisk =
                WallPresentationGeometrySizing.ExpandForRisk(
                    wall,
                    new Vector3(0f, 1.5f, 0f),
                    0.45f,
                    false);
            HazardPresentationGeometry highRisk =
                WallPresentationGeometrySizing.ExpandForRisk(
                    wall,
                    new Vector3(0f, 1.5f, 0f),
                    1f,
                    false);
            HazardPresentationGeometry emergency =
                WallPresentationGeometrySizing.ExpandForRisk(
                    wall,
                    new Vector3(0f, 1.5f, 0f),
                    1f,
                    true);

            Assert.That(lowRisk.Width, Is.GreaterThan(wall.Width));
            Assert.That(lowRisk.Height, Is.GreaterThan(wall.Height));
            Assert.That(
                lowRisk.Width,
                Is.GreaterThanOrEqualTo(
                    WallPresentationGeometrySizing.AngularSpanMeters(
                        1f,
                        WallPresentationGeometrySizing
                            .MinimumAngularWidthDegrees) - 0.001f));
            Assert.That(
                lowRisk.Height,
                Is.GreaterThanOrEqualTo(
                    WallPresentationGeometrySizing.AngularSpanMeters(
                        1f,
                        WallPresentationGeometrySizing
                            .MinimumAngularHeightDegrees) - 0.001f));
            Assert.That(highRisk.Width, Is.GreaterThan(lowRisk.Width));
            Assert.That(emergency.Width, Is.GreaterThan(highRisk.Width));
            Assert.That(emergency.Height, Is.GreaterThan(highRisk.Height));
            Assert.That(emergency.StableId, Is.EqualTo(wall.StableId));
            Assert.That(
                Vector3.Angle(emergency.SurfaceNormal, wall.SurfaceNormal),
                Is.LessThan(0.01f));
        }

        [Test]
        public void PersonStatusSeparatesPolicyFromActualRendering()
        {
            Assert.That(
                PersonPresentationDiagnostics.ResolveStatus(
                    true,
                    true,
                    true,
                    0,
                    1,
                    0,
                    false,
                    false),
                Is.EqualTo(PersonPresentationStatus.GeometryUnavailable));
            Assert.That(
                PersonPresentationDiagnostics.ResolveStatus(
                    true,
                    true,
                    true,
                    1,
                    1,
                    1,
                    false,
                    false),
                Is.EqualTo(PersonPresentationStatus.Rendered));
        }

        [Test]
        public void LostAssessmentCannotRefreshRevealEligibility()
        {
            var lostForce = new DynamicRiskAssessment(
                9,
                null,
                null,
                null,
                0.20f,
                DynamicRiskLevel.Danger,
                null,
                null,
                TrackLifecycle.Lost,
                false,
                forcePassthrough: true);

            Assert.That(
                PersonPresentationDiagnostics.IsRevealEligible(
                    lostForce,
                    true,
                    true,
                    0.50f),
                Is.False);
        }

        [Test]
        public void RedBorderModeDoesNotReportPassthroughAsRendered()
        {
            var owner = new GameObject("Presentation mode diagnostics test");
            owner.SetActive(false);
            try
            {
                Type controllerType = Type.GetType(
                    "SelectivePassthroughController, Assembly-CSharp");
                Assert.That(controllerType, Is.Not.Null);
                Component controller = owner.AddComponent(controllerType);
                PropertyInfo countProperty = controllerType.GetProperty(
                    "ActivePersonWindowCount",
                    BindingFlags.Instance | BindingFlags.Public);
                PropertyInfo renderedProperty = controllerType.GetProperty(
                    "DynamicPassthroughRendered",
                    BindingFlags.Instance | BindingFlags.Public);
                FieldInfo modeField = controllerType.GetField(
                    "feedbackMode",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(countProperty, Is.Not.Null);
                Assert.That(renderedProperty, Is.Not.Null);
                Assert.That(modeField, Is.Not.Null);

                countProperty.GetSetMethod(true).Invoke(
                    controller,
                    new object[] { 1 });
                modeField.SetValue(
                    controller,
                    Enum.Parse(modeField.FieldType, "Passthrough"));
                Assert.That(
                    (bool)renderedProperty.GetValue(controller),
                    Is.True);

                modeField.SetValue(
                    controller,
                    Enum.Parse(
                        modeField.FieldType,
                        "RedBorderAndHaptics"));
                Assert.That(
                    (bool)renderedProperty.GetValue(controller),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void RuntimePanelButtonsAutoSizeAndClipLongLabels()
        {
            var parent = new GameObject("Panel Test", typeof(RectTransform));
            try
            {
                Type panelType = Type.GetType(
                    "PersonalizationRuntimePanel, Assembly-CSharp");
                Assert.That(panelType, Is.Not.Null);
                MethodInfo createButton = panelType.GetMethod(
                        "CreateActionButton",
                        BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(createButton, Is.Not.Null);
                var button = (Button)createButton.Invoke(
                    null,
                    new object[]
                    {
                        parent.transform,
                        "OUTPUT: RED BORDER + VIBRATION",
                        Vector2.zero,
                        Vector2.one,
                        (Action)null,
                        Color.white
                    });
                Component label = null;
                Component[] components = button.GetComponentsInChildren<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] != null
                        && components[i].GetType().Name
                            == "TextMeshProUGUI")
                    {
                        label = components[i];
                        break;
                    }
                }

                Assert.That(label, Is.Not.Null);
                Type labelType = label.GetType();
                Assert.That(
                    (bool)labelType.GetProperty("enableAutoSizing")
                        .GetValue(label),
                    Is.True);
                float minimum = (float)labelType.GetProperty("fontSizeMin")
                    .GetValue(label);
                float maximum = (float)labelType.GetProperty("fontSizeMax")
                    .GetValue(label);
                Assert.That(minimum, Is.LessThan(maximum));
                Assert.That(
                    labelType.GetProperty("overflowMode").GetValue(label)
                        .ToString(),
                    Is.EqualTo("Truncate"));
                Assert.That(
                    button.GetComponent<RectMask2D>(),
                    Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }
    }
}
