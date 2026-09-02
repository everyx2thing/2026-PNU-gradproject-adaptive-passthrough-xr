using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TeamVR.Experiment
{
    // Head-locked world-space canvas showing an edge-of-screen arrow for
    // every active ball currently outside the player's field of view, so an
    // approach from outside peripheral vision (e.g. from the left while
    // looking right) is not missed. Purely a UI/awareness aid - never
    // references the AdaptivePassthrough risk pipeline.
    [DefaultExecutionOrder(760)]
    [DisallowMultipleComponent]
    public sealed class ExperimentThreatIndicatorController : MonoBehaviour
    {
        [SerializeField] private OVRCameraRig cameraRig;
        [SerializeField] private ExperimentBallSpawner ballSpawner;

        [Header("Placement")]
        [SerializeField, Min(0.2f)] private float canvasDistanceMeters = 0.6f;
        [SerializeField, Range(0.5f, 0.99f)] private float edgeInsetRatio = 0.85f;

        [Header("Style")]
        [SerializeField, Min(8f)] private float arrowSizePixels = 46f;
        [SerializeField] private Color targetArrowColor = new Color(1f, 0.85f, 0.2f, 0.95f);
        [SerializeField] private Color bombArrowColor = new Color(1f, 0.25f, 0.25f, 0.95f);

        private Camera eyeCamera;
        private RectTransform canvasRect;
        private Sprite arrowSprite;
        private readonly List<Image> arrowPool = new List<Image>();
        private readonly List<GameObject> visibleBalls = new List<GameObject>();

        public bool ValidateConfiguration(out string error)
        {
            ResolveReferences();
            if (cameraRig == null || cameraRig.centerEyeAnchor == null
                || eyeCamera == null || ballSpawner == null)
            {
                error = "Threat indicator camera or ball spawner is missing.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void Awake()
        {
            ResolveReferences();
            arrowSprite = CreateArrowSprite();
            BuildCanvas();
        }

        private void LateUpdate()
        {
            ResolveReferences();
            if (eyeCamera == null || canvasRect == null || ballSpawner == null)
            {
                return;
            }

            FollowHead();
            RefreshArrows();
        }

        private void FollowHead()
        {
            Transform eye = eyeCamera.transform;
            canvasRect.position = eye.position + eye.forward * canvasDistanceMeters;
            canvasRect.rotation = eye.rotation;

            float height = 2f * canvasDistanceMeters
                * Mathf.Tan(eyeCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float width = height * Mathf.Max(0.01f, eyeCamera.aspect);
            canvasRect.sizeDelta = new Vector2(width * 1000f, height * 1000f);
        }

        private void RefreshArrows()
        {
            visibleBalls.Clear();
            IReadOnlyList<GameObject> activeBalls = ballSpawner.ActiveBalls;
            for (int i = 0; i < activeBalls.Count; i++)
            {
                if (activeBalls[i] != null)
                {
                    visibleBalls.Add(activeBalls[i]);
                }
            }

            EnsurePoolSize(visibleBalls.Count);

            for (int i = 0; i < arrowPool.Count; i++)
            {
                if (i >= visibleBalls.Count)
                {
                    arrowPool[i].gameObject.SetActive(false);
                    continue;
                }

                UpdateArrow(arrowPool[i], visibleBalls[i]);
            }
        }

        private void UpdateArrow(Image arrow, GameObject ball)
        {
            Vector3 viewportPoint = eyeCamera.WorldToViewportPoint(ball.transform.position);
            bool behind = viewportPoint.z <= 0f;
            bool onScreen = !behind
                && viewportPoint.x > 0f && viewportPoint.x < 1f
                && viewportPoint.y > 0f && viewportPoint.y < 1f;

            if (onScreen)
            {
                arrow.gameObject.SetActive(false);
                return;
            }

            if (behind)
            {
                viewportPoint.x = 1f - viewportPoint.x;
                viewportPoint.y = 1f - viewportPoint.y;
            }

            Vector2 fromCenter = new Vector2(
                viewportPoint.x - 0.5f,
                viewportPoint.y - 0.5f);
            if (fromCenter.sqrMagnitude < 0.0001f)
            {
                fromCenter = Vector2.up;
            }

            Vector2 halfExtents = canvasRect.sizeDelta * 0.5f * edgeInsetRatio;
            Vector2 clamped = ClampToRectEdge(fromCenter, halfExtents);

            arrow.gameObject.SetActive(true);
            RectTransform rect = arrow.rectTransform;
            rect.anchoredPosition = clamped;
            float angle = Mathf.Atan2(clamped.y, clamped.x) * Mathf.Rad2Deg - 90f;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);

            ExperimentBall behaviour = ball.GetComponent<ExperimentBall>();
            arrow.color = behaviour != null && behaviour.BallType == BallType.MustAvoid
                ? bombArrowColor
                : targetArrowColor;
        }

        private static Vector2 ClampToRectEdge(Vector2 direction, Vector2 halfExtents)
        {
            float angle = Mathf.Atan2(direction.y, direction.x);
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            float scaleX = Mathf.Abs(cos) > 0.0001f
                ? halfExtents.x / Mathf.Abs(cos)
                : float.MaxValue;
            float scaleY = Mathf.Abs(sin) > 0.0001f
                ? halfExtents.y / Mathf.Abs(sin)
                : float.MaxValue;
            return new Vector2(cos, sin) * Mathf.Min(scaleX, scaleY);
        }

        private void EnsurePoolSize(int count)
        {
            while (arrowPool.Count < count)
            {
                arrowPool.Add(CreateArrowImage());
            }
        }

        private Image CreateArrowImage()
        {
            var arrowObject = new GameObject("GeneratedThreatArrow", typeof(RectTransform));
            arrowObject.transform.SetParent(canvasRect, false);
            RectTransform rect = arrowObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(arrowSizePixels, arrowSizePixels);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            Image image = arrowObject.AddComponent<Image>();
            image.sprite = arrowSprite;
            image.raycastTarget = false;
            return image;
        }

        private void BuildCanvas()
        {
            Transform existing = transform.Find("GeneratedThreatIndicatorCanvas");
            if (existing != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(existing.gameObject);
                }
                else
                {
                    DestroyImmediate(existing.gameObject);
                }
            }

            var canvasObject = new GameObject(
                "GeneratedThreatIndicatorCanvas",
                typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = eyeCamera;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.localScale = Vector3.one * 0.001f;
            canvasRect.sizeDelta = new Vector2(1000f, 1000f);
        }

        // 32x32 solid triangle pointing toward +Y (up), matched at runtime by
        // rotating the RectTransform to aim at the off-screen target -
        // avoids depending on an imported arrow texture asset.
        private static Sprite CreateArrowSprite()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float baseHalfWidth = size * 0.42f;

            for (int y = 0; y < size; y++)
            {
                float normalizedY = y / (float)(size - 1);
                float halfWidthAtY = baseHalfWidth * (1f - normalizedY);
                for (int x = 0; x < size; x++)
                {
                    bool inside = Mathf.Abs(x - center) <= halfWidthAtY;
                    pixels[y * size + x] = inside
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size);
        }

        private void OnDestroy()
        {
            if (arrowSprite == null)
            {
                return;
            }

            Texture2D texture = arrowSprite.texture;
            if (Application.isPlaying)
            {
                Destroy(arrowSprite);
                Destroy(texture);
            }
            else
            {
                DestroyImmediate(arrowSprite);
                DestroyImmediate(texture);
            }
        }

        private void ResolveReferences()
        {
            if (cameraRig == null)
            {
                cameraRig = FindAnyObjectByType<OVRCameraRig>();
            }

            if (eyeCamera == null && cameraRig != null && cameraRig.centerEyeAnchor != null)
            {
                eyeCamera = cameraRig.centerEyeAnchor.GetComponent<Camera>();
            }

            if (ballSpawner == null)
            {
                ballSpawner = FindAnyObjectByType<ExperimentBallSpawner>();
            }
        }
    }
}
