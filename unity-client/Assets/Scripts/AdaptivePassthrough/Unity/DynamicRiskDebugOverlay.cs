using System;
using System.Collections.Generic;
using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    [DisallowMultipleComponent]
    public sealed class DynamicRiskDebugOverlay : MonoBehaviour
    {
        [SerializeField] private DynamicRiskController controller;
        [SerializeField] private bool visible = true;
        [SerializeField] private bool developmentBuildOnly = true;
        [SerializeField] private bool drawPanelBackground;
        [SerializeField] private bool showHeader;
        [SerializeField] private Rect normalizedViewport = new Rect(0.05f, 0.18f, 0.90f, 0.72f);
        [SerializeField] private int fontSize = 22;

        private Texture2D pixel;
        private GUIStyle labelStyle;
        private GUIStyle panelStyle;
        private readonly List<Rect> usedLabelRects = new List<Rect>();

        private void Awake()
        {
            if (controller == null)
            {
                controller = GetComponent<DynamicRiskController>();
            }

            pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
        }

        private void OnDestroy()
        {
            if (pixel != null)
            {
                Destroy(pixel);
            }
        }

        private void OnGUI()
        {
            if (!visible
                || controller == null
                || (developmentBuildOnly
                    && !Debug.isDebugBuild
                    && !Application.isEditor))
            {
                return;
            }

            EnsureStyles();
            Rect viewport = new Rect(
                Screen.width * normalizedViewport.x,
                Screen.height * normalizedViewport.y,
                Screen.width * normalizedViewport.width,
                Screen.height * normalizedViewport.height);

            Color previousColor = GUI.color;
            if (drawPanelBackground)
            {
                GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.90f);
                GUI.DrawTexture(viewport, pixel);
                GUI.color = previousColor;
            }

            DynamicRiskFrame frame = controller.LatestFrame;
            if (frame == null)
            {
                if (showHeader)
                {
                    GUI.Label(
                        new Rect(
                            viewport.x + 16f,
                            viewport.y + 16f,
                            viewport.width - 32f,
                            80f),
                        "Dynamic-risk pipeline: waiting for detections",
                        labelStyle);
                }

                return;
            }

            if (showHeader)
            {
                GUI.Label(
                    new Rect(
                        viewport.x + 16f,
                        viewport.y + 12f,
                        viewport.width - 32f,
                        42f),
                    string.Format(
                        "People: {0}   Max Rdynamic: {1:F3}   Level: {2}",
                        frame.ConfirmedPersonCount,
                        frame.MaximumRisk,
                        frame.MaximumLevel),
                    labelStyle);
            }

            usedLabelRects.Clear();
            for (int i = 0; i < frame.Assessments.Count; i++)
            {
                DynamicRiskAssessment assessment = frame.Assessments[i];
                NormalizedBoundingBox box = assessment.Detection.boundingBox;
                Rect boxRect = new Rect(
                    viewport.x + box.Left * viewport.width,
                    viewport.y + box.Top * viewport.height,
                    box.width * viewport.width,
                    box.height * viewport.height);

                Color riskColor = ColorFor(assessment.Level);
                DrawBorder(boxRect, 4f, riskColor);

                string ttc = assessment.Motion.TtcSecondsApprox.HasValue
                    ? assessment.Motion.TtcSecondsApprox.Value.ToString("F1") + "s"
                    : "-";
                string text = string.Format(
                    "ID {0}  {1:F2} {2}\n{3} / {4} / TTC {5}",
                    assessment.TrackId,
                    assessment.Score,
                    assessment.Level,
                    assessment.Location.UserRelativeDirection,
                    assessment.Motion.State,
                    ttc);

                Rect labelRect = FindLabelRect(boxRect, viewport);
                GUI.color = new Color(0f, 0f, 0f, 0.78f);
                GUI.DrawTexture(labelRect, pixel);
                GUI.color = riskColor;
                GUI.Label(
                    new Rect(
                        labelRect.x + 6f,
                        labelRect.y + 3f,
                        labelRect.width - 10f,
                        labelRect.height - 4f),
                    text,
                    panelStyle);
                GUI.color = previousColor;
            }
        }

        private Rect FindLabelRect(Rect boxRect, Rect viewport)
        {
            const float width = 330f;
            const float height = 58f;
            const float spacing = 4f;
            float minimumY = showHeader ? viewport.y + 48f : viewport.y;
            float x = Mathf.Clamp(
                boxRect.x,
                viewport.x,
                Math.Max(viewport.x, viewport.xMax - width));
            float y = Math.Max(minimumY, boxRect.y - height);
            Rect candidate = new Rect(x, y, width, height);

            for (int attempt = 0; attempt < usedLabelRects.Count + 2; attempt++)
            {
                bool overlaps = false;
                for (int i = 0; i < usedLabelRects.Count; i++)
                {
                    if (candidate.Overlaps(usedLabelRects[i]))
                    {
                        overlaps = true;
                        candidate.y = usedLabelRects[i].yMax + spacing;
                        break;
                    }
                }

                if (!overlaps)
                {
                    break;
                }
            }

            if (candidate.yMax > viewport.yMax)
            {
                candidate.y = Math.Max(minimumY, viewport.yMax - height);
            }

            usedLabelRects.Add(candidate);
            return candidate;
        }

        private void EnsureStyles()
        {
            if (labelStyle != null)
            {
                return;
            }

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            panelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Math.Max(13, fontSize - 6),
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
        }

        private void DrawBorder(Rect rect, float thickness, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), pixel);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), pixel);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), pixel);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), pixel);
            GUI.color = previous;
        }

        private static Color ColorFor(DynamicRiskLevel level)
        {
            switch (level)
            {
                case DynamicRiskLevel.Danger:
                    return new Color(1f, 0.15f, 0.12f);
                case DynamicRiskLevel.Warning:
                    return new Color(1f, 0.55f, 0.05f);
                case DynamicRiskLevel.Caution:
                    return new Color(1f, 0.90f, 0.10f);
                default:
                    return new Color(0.20f, 0.90f, 0.45f);
            }
        }
    }
}
