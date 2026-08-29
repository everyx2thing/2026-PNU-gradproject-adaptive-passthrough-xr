using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TeamVR.AdaptivePassthrough.PlayModeTests
{
    public sealed class StereoPresentationRenderTests
    {
        [UnityTearDown]
        public IEnumerator RemovePresentationLabsAfterEachTest()
        {
            foreach (SpatialTrackingVisualLab lab in
                Object.FindObjectsByType<SpatialTrackingVisualLab>(
                    FindObjectsSortMode.None))
            {
                Object.Destroy(lab.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator WorldCorners_RenderWithinTwoPixelsInBothEyes()
        {
            var root = new GameObject("Stereo Presentation Render Test");
            SpatialTrackingVisualLab lab =
                root.AddComponent<SpatialTrackingVisualLab>();
            lab.SetPlayback(false);
            lab.SetScenario(SpatialTrackingVisualLab.LabScenario.AngledWall);
            lab.SetWall(0.60f, 35f);
            yield return new WaitForSecondsRealtime(0.35f);

            AssertEyeOutline(lab.LeftCamera, lab.LeftTexture, lab.CurrentGeometry);
            AssertEyeOutline(lab.RightCamera, lab.RightTexture, lab.CurrentGeometry);

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LabFusionModes_UseRealFilterAndPublishActualSource()
        {
            var root = new GameObject("Deterministic Fusion Lab Test");
            SpatialTrackingVisualLab lab =
                root.AddComponent<SpatialTrackingVisualLab>();
            lab.EnableManualClock(20.0);
            lab.SetPlayback(false);
            lab.SetScenario(SpatialTrackingVisualLab.LabScenario.FrontWall);
            lab.SetWall(1f, 0f);

            lab.SetFusionMode(
                SpatialTrackingVisualLab.LabFusionMode.Agreement);
            lab.AdvancePresentation(0.125f);
            SpatialObstacleMeasurement agreement =
                lab.CurrentFusionMeasurement;
            Assert.That(agreement.Source,
                Is.EqualTo(SpatialObstacleSource.Fused));
            Assert.That(agreement.SelectedSource,
                Is.EqualTo(SpatialObstacleSource.EnvironmentDepth));
            Assert.That(agreement.PresentationGeometry.Source,
                Is.EqualTo(SpatialObstacleSource.Fused));
            Assert.That(agreement.PresentationGeometry.Center.z,
                Is.EqualTo(1f).Within(0.001f),
                "Environment geometry must be projected onto the Room plane.");
            Assert.That(agreement.PresentationGeometry.Width,
                Is.LessThanOrEqualTo(0.56f),
                "Fused geometry must remain inside finite Room bounds.");
            Assert.That(lab.CurrentFusionConflictReason, Is.Empty);

            lab.SetFusionMode(
                SpatialTrackingVisualLab.LabFusionMode.DepthUnavailable);
            lab.AdvancePresentation(0.125f);
            SpatialObstacleMeasurement fallback =
                lab.CurrentFusionMeasurement;
            Assert.That(fallback.Source,
                Is.EqualTo(SpatialObstacleSource.RoomScene));
            Assert.That(fallback.SelectedSource,
                Is.EqualTo(SpatialObstacleSource.RoomScene));
            Assert.That(fallback.EnvironmentDistanceMeters, Is.LessThan(0f));
            Assert.That(fallback.RoomSceneDistanceMeters,
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(lab.CurrentGeometry.Source,
                Is.EqualTo(SpatialObstacleSource.RoomScene));
            Assert.That(lab.CurrentFusionConflictReason,
                Is.EqualTo("depth_unavailable_room_fallback"));

            lab.SetFusionMode(
                SpatialTrackingVisualLab.LabFusionMode.Conflict);
            lab.AdvancePresentation(0.125f);
            SpatialObstacleMeasurement conflict =
                lab.CurrentFusionMeasurement;
            Assert.That(conflict.Source,
                Is.EqualTo(SpatialObstacleSource.RoomScene),
                "Conflicting inputs must publish the selected source, not Fused.");
            Assert.That(conflict.SelectedSource,
                Is.EqualTo(SpatialObstacleSource.RoomScene));
            Assert.That(conflict.PresentationGeometry.Source,
                Is.EqualTo(SpatialObstacleSource.RoomScene));
            Assert.That(lab.CurrentFusionConflictReason,
                Is.EqualTo("normal_mismatch"));

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ProductionShader_RendersWorldMaskInBothEyes()
        {
            const int testLayer = 30;
            Shader shader = Shader.Find(
                "TeamVR/AdaptivePassthrough/PassthroughWindow");
            Assert.That(shader, Is.Not.Null,
                "Production passthrough shader must be included and compilable.");

            var root = new GameObject("Production Stereo Shader Test");
            var material = new Material(shader);
            Mesh mesh = CreateTestQuad();
            var quad = new GameObject("Production World Corner Quad");
            quad.layer = testLayer;
            quad.transform.SetParent(root.transform, false);
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            HazardPresentationGeometry geometry =
                HazardPresentationGeometry.CreatePlanePatch(
                    701,
                    HazardVisualKind.WallPlane,
                    new Vector3(0f, 0f, 1.2f),
                    Vector3.back,
                    Vector3.up,
                    0.60f,
                    0.75f,
                    1.0,
                    1f,
                    SpatialObstacleSource.Fused,
                    SpatialProbeOwner.Head,
                    SpatialProbePurpose.Standard);
            var properties = new MaterialPropertyBlock();
            properties.SetVector("_WorldBottomLeft", geometry.BottomLeft);
            properties.SetVector("_WorldBottomRight", geometry.BottomRight);
            properties.SetVector("_WorldTopRight", geometry.TopRight);
            properties.SetVector("_WorldTopLeft", geometry.TopLeft);
            properties.SetFloat("_Shape", 0f);
            properties.SetFloat("_Aspect", geometry.Width / geometry.Height);
            properties.SetFloat("_Feather", 0.001f);
            properties.SetFloat("_RevealStrength", 1f);
            renderer.SetPropertyBlock(properties);

            Camera left = CreateProductionTestCamera(
                "Production Left Eye",
                root.transform,
                -0.032f,
                testLayer,
                out RenderTexture leftTexture);
            Camera right = CreateProductionTestCamera(
                "Production Right Eye",
                root.transform,
                0.032f,
                testLayer,
                out RenderTexture rightTexture);
            yield return null;

            AssertProductionMask(left, leftTexture, geometry);
            AssertProductionMask(right, rightTexture, geometry);

            leftTexture.Release();
            rightTexture.Release();
            Object.Destroy(root);
            Object.Destroy(material);
            Object.Destroy(mesh);
            Object.Destroy(leftTexture);
            Object.Destroy(rightTexture);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EyeDisparityChangesWithoutMovingWorldGeometry()
        {
            var root = new GameObject("Stereo World Lock Test");
            SpatialTrackingVisualLab lab =
                root.AddComponent<SpatialTrackingVisualLab>();
            lab.SetPlayback(false);
            lab.SetScenario(SpatialTrackingVisualLab.LabScenario.FrontWall);
            lab.SetWall(1f, 0f);
            yield return null;
            yield return null;
            HazardPresentationGeometry before = lab.CurrentGeometry;
            Vector3 leftBefore = lab.LeftCamera.WorldToViewportPoint(before.Center);

            lab.SetHmdPose(0.20f, 12f);
            yield return null;
            HazardPresentationGeometry after = lab.CurrentGeometry;
            Vector3 leftAfter = lab.LeftCamera.WorldToViewportPoint(after.Center);

            Assert.That(after.BottomLeft, Is.EqualTo(before.BottomLeft));
            Assert.That(after.TopRight, Is.EqualTo(before.TopRight));
            Assert.That(Vector2.Distance(leftBefore, leftAfter), Is.GreaterThan(0.01f));

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LowObstacle_CreatesFloorWedgeOnlyWithFreshFloor()
        {
            var root = new GameObject("Low Obstacle Corridor Test");
            SpatialTrackingVisualLab lab =
                root.AddComponent<SpatialTrackingVisualLab>();
            lab.EnableManualClock(5.0);
            lab.SetPlayback(false);
            lab.SetScenario(SpatialTrackingVisualLab.LabScenario.LowBox);
            lab.AdvancePresentation(0.125f);

            Assert.That(lab.CurrentGeometry.Kind,
                Is.EqualTo(HazardVisualKind.LowObstaclePatch));
            Assert.That(lab.CurrentCorridor.Available, Is.True);
            Assert.That(
                Vector3.Distance(
                    lab.CurrentCorridor.BottomLeft,
                    lab.CurrentCorridor.BottomRight),
                Is.EqualTo(0.20f).Within(0.001f));

            lab.SetFloorAvailable(false);
            lab.AdvancePresentation(1f / 60f);
            Assert.That(lab.CurrentGeometry.Kind,
                Is.EqualTo(HazardVisualKind.LowObstaclePatch),
                "A stale floor must not hide the obstacle patch itself.");
            Assert.That(lab.CurrentCorridor.Available, Is.False,
                "A stale floor must remove the corridor cue.");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PersonCapsule_RenderHasCurvedCapsAndUpperBodyExtent()
        {
            var root = new GameObject("Person Capsule Render Test");
            SpatialTrackingVisualLab lab =
                root.AddComponent<SpatialTrackingVisualLab>();
            lab.SetPlayback(false);
            lab.SetScenario(SpatialTrackingVisualLab.LabScenario.PersonApproach);
            yield return new WaitForSecondsRealtime(0.15f);

            lab.LeftCamera.Render();
            Color32[] pixels = ReadPixels(lab.LeftTexture);
            PixelBounds bounds = FindVisibleBounds(
                pixels,
                lab.LeftTexture.width,
                lab.LeftTexture.height);
            Assert.That(bounds.Available, Is.True);
            int middleSpan = VisibleSpanAtRow(
                pixels,
                lab.LeftTexture.width,
                bounds.CenterY);
            int upperCapSpan = VisibleSpanAtRow(
                pixels,
                lab.LeftTexture.width,
                bounds.MaxY - 4);
            int lowerCapSpan = VisibleSpanAtRow(
                pixels,
                lab.LeftTexture.width,
                bounds.MinY + 4);

            Assert.That(upperCapSpan, Is.GreaterThan(0));
            Assert.That(lowerCapSpan, Is.GreaterThan(0));
            Assert.That(upperCapSpan, Is.LessThan(middleSpan * 0.80f));
            Assert.That(lowerCapSpan, Is.LessThan(middleSpan * 0.80f));
            Assert.That(lab.CurrentGeometry.Height,
                Is.EqualTo(
                    1.48f
                    * HazardPresentationGeometry.UpperBodyFraction
                    * HazardPresentationGeometry.DefaultPaddingScale)
                    .Within(0.001f));

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WallFeather_IsSixPointFivePercentOfShortAxis()
        {
            var root = new GameObject("Wall Feather Render Test");
            SpatialTrackingVisualLab lab =
                root.AddComponent<SpatialTrackingVisualLab>();
            lab.SetPlayback(false);
            lab.SetScenario(SpatialTrackingVisualLab.LabScenario.FrontWall);
            lab.SetWall(1f, 0f);
            yield return new WaitForSecondsRealtime(0.35f);

            lab.LeftCamera.Render();
            Color32[] pixels = ReadPixels(lab.LeftTexture);
            PixelBounds projected = ProjectedBounds(
                lab.LeftCamera,
                lab.CurrentGeometry,
                lab.LeftTexture.width,
                lab.LeftTexture.height);
            int y = projected.CenterY;
            int lowThresholdX = FirstRedAtOrAbove(
                pixels,
                lab.LeftTexture.width,
                y,
                projected.MinX,
                projected.MaxX,
                30);
            int plateauX = FirstRedAtOrAbove(
                pixels,
                lab.LeftTexture.width,
                y,
                projected.MinX,
                projected.MaxX,
                145);
            int shortAxisPixels = Mathf.Min(
                projected.Width,
                projected.Height);
            // 30/145 red-byte thresholds correspond to roughly 18%/87%
            // smoothstep output, or 51% of the configured feather interval.
            float expectedTransition = shortAxisPixels * 0.065f * 0.51f;
            int measuredTransition = plateauX - lowThresholdX;

            Assert.That(lowThresholdX, Is.GreaterThanOrEqualTo(projected.MinX));
            Assert.That(plateauX, Is.GreaterThan(lowThresholdX));
            Assert.That(measuredTransition,
                Is.EqualTo(expectedTransition).Within(2f));

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LowObstacle_WedgeRendersOutlineAndCenterLinePixels()
        {
            var root = new GameObject("Low Obstacle Pixel Cue Test");
            SpatialTrackingVisualLab lab =
                root.AddComponent<SpatialTrackingVisualLab>();
            lab.SetPlayback(false);
            lab.SetScenario(SpatialTrackingVisualLab.LabScenario.LowBox);
            yield return new WaitForSecondsRealtime(0.15f);
            foreach (LineRenderer line in root.GetComponentsInChildren<LineRenderer>())
            {
                line.enabled = false;
            }

            lab.LeftCamera.Render();
            Color32[] pixels = ReadPixels(lab.LeftTexture);
            HazardPresentationGeometry corridor = lab.CurrentCorridor;
            Assert.That(corridor.Available, Is.True);
            int centerHits = 0;
            int edgeHits = 0;
            for (int i = 1; i <= 4; i++)
            {
                float v = i / 5f;
                centerHits += SampleIsRed(
                    pixels,
                    lab.LeftTexture.width,
                    lab.LeftTexture.height,
                    lab.LeftCamera,
                    Bilinear(corridor, 0.5f, v)) ? 1 : 0;
                edgeHits += SampleIsRed(
                    pixels,
                    lab.LeftTexture.width,
                    lab.LeftTexture.height,
                    lab.LeftCamera,
                    Bilinear(corridor, 0.02f, v)) ? 1 : 0;
            }

            Assert.That(centerHits, Is.GreaterThanOrEqualTo(3),
                "Expected the production center direction line.");
            Assert.That(edgeHits, Is.GreaterThanOrEqualTo(2),
                "Expected the production wedge outline.");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PolicyOff_HoldsThenFadesInsteadOfHardCutting()
        {
            var root = new GameObject("Policy Fade Render Test");
            SpatialTrackingVisualLab lab =
                root.AddComponent<SpatialTrackingVisualLab>();
            lab.EnableManualClock(10.0);
            lab.SetPlayback(false);
            lab.SetScenario(SpatialTrackingVisualLab.LabScenario.FrontWall);
            lab.AdvancePresentation(0.125f);

            lab.SetPolicyEnabled(false);
            AdvancePresentation(lab, 0.60f);
            Assert.That(lab.CurrentGeometry.Available, Is.True,
                "An explicit policy shutdown retains the last trusted geometry.");
            Assert.That(lab.AnimationPhase,
                Is.EqualTo(PassthroughAnimationPhase.Holding));
            AssertEyeHasVisibleMask(lab.LeftCamera, lab.LeftTexture);

            AdvancePresentation(lab, 0.90f);
            Assert.That(lab.CurrentGeometry.Available, Is.True);
            Assert.That(lab.AnimationPhase,
                Is.EqualTo(PassthroughAnimationPhase.Fading));
            AssertEyeHasVisibleMask(lab.LeftCamera, lab.LeftTexture);

            AdvancePresentation(lab, 0.30f);
            Assert.That(lab.AnimationPhase,
                Is.EqualTo(PassthroughAnimationPhase.Hidden));
            Assert.That(lab.CurrentGeometry.Available, Is.False);
            AssertEyeHasNoVisibleMask(lab.LeftCamera, lab.LeftTexture);

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WarningRing_ExpandsOnceThenDisappearsAt300Milliseconds()
        {
            var root = new GameObject("Warning Ring Pixel Test");
            SpatialTrackingVisualLab lab =
                root.AddComponent<SpatialTrackingVisualLab>();
            lab.SetPlayback(false);
            lab.SetScenario(SpatialTrackingVisualLab.LabScenario.FrontWall);
            yield return new WaitForSecondsRealtime(0.12f);

            lab.LeftCamera.Render();
            Color32[] duringPixels = ReadPixels(lab.LeftTexture);
            PixelBounds cyan = FindCyanBounds(
                duringPixels,
                lab.LeftTexture.width,
                lab.LeftTexture.height);
            PixelBounds redDuring = FindRedBounds(
                duringPixels,
                lab.LeftTexture.width,
                lab.LeftTexture.height);
            Assert.That(lab.CurrentPulse01, Is.GreaterThan(0f));
            Assert.That(redDuring.Available, Is.True);
            Assert.That(
                redDuring.MinX < cyan.MinX - 1
                    || redDuring.MaxX > cyan.MaxX + 1
                    || redDuring.MinY < cyan.MinY - 1
                    || redDuring.MaxY > cyan.MaxY + 1,
                Is.True,
                "The production warning ring must expand outside the base mask.");

            yield return new WaitForSecondsRealtime(0.24f);
            lab.LeftCamera.Render();
            Color32[] afterPixels = ReadPixels(lab.LeftTexture);
            PixelBounds cyanAfter = FindCyanBounds(
                afterPixels,
                lab.LeftTexture.width,
                lab.LeftTexture.height);
            PixelBounds redAfter = FindRedBounds(
                afterPixels,
                lab.LeftTexture.width,
                lab.LeftTexture.height);
            Assert.That(lab.CurrentPulse01, Is.EqualTo(0f).Within(0.001f));
            Assert.That(Mathf.Abs(redAfter.MinX - cyanAfter.MinX),
                Is.LessThanOrEqualTo(2));
            Assert.That(Mathf.Abs(redAfter.MaxX - cyanAfter.MaxX),
                Is.LessThanOrEqualTo(2));

            Object.Destroy(root);
            yield return null;
        }

        private static void AssertEyeOutline(
            Camera camera,
            RenderTexture texture,
            HazardPresentationGeometry geometry)
        {
            camera.Render();
            Color32[] pixels = ReadPixels(texture);
            PixelBounds actual = FindCyanBounds(pixels, texture.width, texture.height);
            PixelBounds expected = ProjectedBounds(camera, geometry, texture.width, texture.height);
            Vector2[] corners = ProjectedCorners(
                camera,
                geometry,
                texture.width,
                texture.height);
            AssertValidProjectedQuad(corners);
            Assert.That(actual.Available, Is.True, "Expected cyan projection outline.");
            Assert.That(Mathf.Abs(actual.MinX - expected.MinX), Is.LessThanOrEqualTo(2));
            Assert.That(Mathf.Abs(actual.MaxX - expected.MaxX), Is.LessThanOrEqualTo(2));
            Assert.That(Mathf.Abs(actual.MinY - expected.MinY), Is.LessThanOrEqualTo(2));
            Assert.That(Mathf.Abs(actual.MaxY - expected.MaxY), Is.LessThanOrEqualTo(2));
            for (int i = 0; i < corners.Length; i++)
            {
                AssertCyanNear(
                    pixels,
                    texture.width,
                    texture.height,
                    corners[i],
                    2,
                    $"Projected corner {i} did not reach the rendered outline.");
                AssertCyanAlongEdge(
                    pixels,
                    texture.width,
                    texture.height,
                    corners[i],
                    corners[(i + 1) % corners.Length],
                    3);
            }
        }

        private static Camera CreateProductionTestCamera(
            string name,
            Transform parent,
            float eyeOffset,
            int testLayer,
            out RenderTexture texture)
        {
            var target = new GameObject(name);
            target.transform.SetParent(parent, false);
            target.transform.localPosition = Vector3.right * eyeOffset;
            Camera camera = target.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.white;
            camera.cullingMask = 1 << testLayer;
            camera.fieldOfView = 70f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 10f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            texture = new RenderTexture(
                256,
                256,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            texture.Create();
            camera.targetTexture = texture;
            return camera;
        }

        private static Mesh CreateTestQuad()
        {
            var mesh = new Mesh { name = "Production Shader Test Quad" };
            mesh.vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.one,
                Vector3.up
            };
            mesh.uv = new[]
            {
                Vector2.zero,
                Vector2.right,
                Vector2.one,
                Vector2.up
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            return mesh;
        }

        private static void AssertProductionMask(
            Camera camera,
            RenderTexture texture,
            HazardPresentationGeometry geometry)
        {
            camera.Render();
            Color32[] pixels = ReadPixels(texture);
            PixelBounds actual = FindDarkBounds(
                pixels,
                texture.width,
                texture.height);
            PixelBounds expected = ProjectedBounds(
                camera,
                geometry,
                texture.width,
                texture.height);
            Assert.That(actual.Available, Is.True,
                "Production blend mask did not darken the destination.");
            Assert.That(Mathf.Abs(actual.MinX - expected.MinX),
                Is.LessThanOrEqualTo(2));
            Assert.That(Mathf.Abs(actual.MaxX - expected.MaxX),
                Is.LessThanOrEqualTo(2));
            Assert.That(Mathf.Abs(actual.MinY - expected.MinY),
                Is.LessThanOrEqualTo(2));
            Assert.That(Mathf.Abs(actual.MaxY - expected.MaxY),
                Is.LessThanOrEqualTo(2));

            Vector3 centerViewport = camera.WorldToViewportPoint(geometry.Center);
            int centerX = Mathf.RoundToInt(centerViewport.x * texture.width);
            int centerY = Mathf.RoundToInt(centerViewport.y * texture.height);
            Color32 center = pixels[centerY * texture.width + centerX];
            Assert.That(center.r, Is.LessThan(20));
            Assert.That(center.g, Is.LessThan(20));
            Assert.That(center.b, Is.LessThan(20));
            Assert.That(pixels[0].r, Is.GreaterThan(220),
                "Pixels outside the world mask must preserve the destination.");
        }

        private static Color32[] ReadPixels(RenderTexture texture)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            var copy = new Texture2D(
                texture.width,
                texture.height,
                TextureFormat.RGBA32,
                false);
            copy.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0);
            copy.Apply();
            Color32[] pixels = copy.GetPixels32();
            Object.DestroyImmediate(copy);
            RenderTexture.active = previous;
            return pixels;
        }

        private static void AdvancePresentation(
            SpatialTrackingVisualLab lab,
            float seconds)
        {
            float remaining = Mathf.Max(0f, seconds);
            const float step = 1f / 60f;
            while (remaining > 0.00001f)
            {
                float delta = Mathf.Min(step, remaining);
                lab.AdvancePresentation(delta);
                remaining -= delta;
            }
        }

        private static void AssertEyeHasVisibleMask(
            Camera camera,
            RenderTexture texture)
        {
            camera.Render();
            PixelBounds red = FindRedBounds(
                ReadPixels(texture),
                texture.width,
                texture.height);
            Assert.That(red.Available, Is.True,
                "Expected the hazard mask to remain visibly rendered.");
        }

        private static void AssertEyeHasNoVisibleMask(
            Camera camera,
            RenderTexture texture)
        {
            camera.Render();
            Color32[] pixels = ReadPixels(texture);
            Assert.That(FindRedBounds(pixels, texture.width, texture.height).Available,
                Is.False);
            Assert.That(FindCyanBounds(pixels, texture.width, texture.height).Available,
                Is.False);
        }

        private static Vector2[] ProjectedCorners(
            Camera camera,
            HazardPresentationGeometry geometry,
            int width,
            int height)
        {
            return new[]
            {
                ProjectPixel(camera, geometry.BottomLeft, width, height),
                ProjectPixel(camera, geometry.BottomRight, width, height),
                ProjectPixel(camera, geometry.TopRight, width, height),
                ProjectPixel(camera, geometry.TopLeft, width, height)
            };
        }

        private static Vector2 ProjectPixel(
            Camera camera,
            Vector3 world,
            int width,
            int height)
        {
            Vector3 viewport = camera.WorldToViewportPoint(world);
            Assert.That(viewport.z, Is.GreaterThan(0f),
                "All presentation corners must be in front of the eye camera.");
            return new Vector2(viewport.x * width, viewport.y * height);
        }

        private static void AssertValidProjectedQuad(Vector2[] corners)
        {
            float twiceArea = 0f;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 current = corners[i];
                Vector2 next = corners[(i + 1) % corners.Length];
                twiceArea += current.x * next.y - next.x * current.y;
            }

            Assert.That(Mathf.Abs(twiceArea), Is.GreaterThan(4f),
                "Projected quad must have non-zero area and stable winding.");
            Assert.That(SegmentsIntersect(
                corners[0], corners[1], corners[2], corners[3]), Is.False,
                "Opposite bottom/top edges must not cross.");
            Assert.That(SegmentsIntersect(
                corners[1], corners[2], corners[3], corners[0]), Is.False,
                "Opposite side edges must not cross.");
        }

        private static bool SegmentsIntersect(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d)
        {
            float abC = Cross(b - a, c - a);
            float abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c);
            float cdB = Cross(d - c, b - c);
            return abC * abD < 0f && cdA * cdB < 0f;
        }

        private static float Cross(Vector2 left, Vector2 right)
        {
            return left.x * right.y - left.y * right.x;
        }

        private static void AssertCyanNear(
            Color32[] pixels,
            int width,
            int height,
            Vector2 expected,
            int radius,
            string message)
        {
            int centerX = Mathf.RoundToInt(expected.x);
            int centerY = Mathf.RoundToInt(expected.y);
            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    if (x >= 0 && x < width && y >= 0 && y < height
                        && IsCyan(pixels[y * width + x]))
                    {
                        return;
                    }
                }
            }

            Assert.Fail(message);
        }

        private static void AssertCyanAlongEdge(
            Color32[] pixels,
            int width,
            int height,
            Vector2 start,
            Vector2 end,
            int radius)
        {
            for (int sample = 1; sample < 8; sample++)
            {
                float t = sample / 8f;
                AssertHazardNear(
                    pixels,
                    width,
                    height,
                    Vector2.Lerp(start, end, t),
                    radius,
                    $"Rendered outline diverged from edge at t={t:F3}.");
            }
        }

        private static void AssertHazardNear(
            Color32[] pixels,
            int width,
            int height,
            Vector2 expected,
            int radius,
            string message)
        {
            int centerX = Mathf.RoundToInt(expected.x);
            int centerY = Mathf.RoundToInt(expected.y);
            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    if (x < 0 || x >= width || y < 0 || y >= height)
                    {
                        continue;
                    }

                    Color32 color = pixels[y * width + x];
                    if (IsCyan(color)
                        || (color.r > 20 && color.g < 100 && color.b < 100))
                    {
                        return;
                    }
                }
            }

            Assert.Fail(message);
        }

        private static bool IsCyan(Color32 color)
        {
            return color.g > 80 && color.b > 80 && color.r < 120;
        }

        private static PixelBounds FindCyanBounds(Color32[] pixels, int width, int height)
        {
            var result = PixelBounds.Empty;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color32 color = pixels[y * width + x];
                    if (color.g > 80 && color.b > 80 && color.r < 120)
                    {
                        result.Include(x, y);
                    }
                }
            }
            return result;
        }

        private static PixelBounds FindVisibleBounds(
            Color32[] pixels,
            int width,
            int height)
        {
            var result = PixelBounds.Empty;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color32 color = pixels[y * width + x];
                    if (color.r > 20 || color.g > 40 || color.b > 40)
                    {
                        result.Include(x, y);
                    }
                }
            }
            return result;
        }

        private static PixelBounds FindRedBounds(
            Color32[] pixels,
            int width,
            int height)
        {
            var result = PixelBounds.Empty;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color32 color = pixels[y * width + x];
                    if (color.r > 35
                        && color.r > color.g * 1.8f
                        && color.r > color.b * 1.8f)
                    {
                        result.Include(x, y);
                    }
                }
            }
            return result;
        }

        private static PixelBounds FindDarkBounds(
            Color32[] pixels,
            int width,
            int height)
        {
            var result = PixelBounds.Empty;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color32 color = pixels[y * width + x];
                    if (color.r < 25 && color.g < 25 && color.b < 25)
                    {
                        result.Include(x, y);
                    }
                }
            }
            return result;
        }

        private static int VisibleSpanAtRow(
            Color32[] pixels,
            int width,
            int y)
        {
            int minimum = width;
            int maximum = -1;
            for (int x = 0; x < width; x++)
            {
                Color32 color = pixels[y * width + x];
                if (color.r <= 20 && color.g <= 40 && color.b <= 40)
                {
                    continue;
                }

                minimum = Mathf.Min(minimum, x);
                maximum = Mathf.Max(maximum, x);
            }
            return maximum >= minimum ? maximum - minimum + 1 : 0;
        }

        private static int FirstRedAtOrAbove(
            Color32[] pixels,
            int width,
            int y,
            int startX,
            int endX,
            byte threshold)
        {
            for (int x = Mathf.Max(0, startX); x <= Mathf.Min(width - 1, endX); x++)
            {
                Color32 color = pixels[y * width + x];
                if (color.r >= threshold && color.g < 100 && color.b < 100)
                {
                    return x;
                }
            }
            return -1;
        }

        private static bool SampleIsRed(
            Color32[] pixels,
            int width,
            int height,
            Camera camera,
            Vector3 world)
        {
            Vector3 viewport = camera.WorldToViewportPoint(world);
            int centerX = Mathf.RoundToInt(viewport.x * width);
            int centerY = Mathf.RoundToInt(viewport.y * height);
            for (int y = centerY - 2; y <= centerY + 2; y++)
            {
                for (int x = centerX - 2; x <= centerX + 2; x++)
                {
                    if (x < 0 || x >= width || y < 0 || y >= height)
                    {
                        continue;
                    }
                    Color32 color = pixels[y * width + x];
                    if (color.r > 80 && color.g < 100 && color.b < 100)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static Vector3 Bilinear(
            HazardPresentationGeometry geometry,
            float u,
            float v)
        {
            Vector3 bottom = Vector3.Lerp(
                geometry.BottomLeft,
                geometry.BottomRight,
                u);
            Vector3 top = Vector3.Lerp(
                geometry.TopLeft,
                geometry.TopRight,
                u);
            return Vector3.Lerp(bottom, top, v);
        }

        private static PixelBounds ProjectedBounds(
            Camera camera,
            HazardPresentationGeometry geometry,
            int width,
            int height)
        {
            var result = PixelBounds.Empty;
            IncludeProjected(camera, geometry.BottomLeft, width, height, ref result);
            IncludeProjected(camera, geometry.BottomRight, width, height, ref result);
            IncludeProjected(camera, geometry.TopRight, width, height, ref result);
            IncludeProjected(camera, geometry.TopLeft, width, height, ref result);
            return result;
        }

        private static void IncludeProjected(
            Camera camera,
            Vector3 world,
            int width,
            int height,
            ref PixelBounds bounds)
        {
            Vector3 point = camera.WorldToViewportPoint(world);
            bounds.Include(
                Mathf.RoundToInt(point.x * width),
                Mathf.RoundToInt(point.y * height));
        }

        private struct PixelBounds
        {
            public bool Available;
            public int MinX;
            public int MaxX;
            public int MinY;
            public int MaxY;

            public int Width => Available ? MaxX - MinX + 1 : 0;
            public int Height => Available ? MaxY - MinY + 1 : 0;
            public int CenterY => Available ? (MinY + MaxY) / 2 : 0;

            public static PixelBounds Empty => new PixelBounds
            {
                Available = false,
                MinX = int.MaxValue,
                MaxX = int.MinValue,
                MinY = int.MaxValue,
                MaxY = int.MinValue
            };

            public void Include(int x, int y)
            {
                Available = true;
                MinX = Mathf.Min(MinX, x);
                MaxX = Mathf.Max(MaxX, x);
                MinY = Mathf.Min(MinY, y);
                MaxY = Mathf.Max(MaxY, y);
            }
        }
    }
}
