using UnityEngine;

namespace TeamVR.AdaptivePassthrough
{
    [DisallowMultipleComponent]
    public sealed class SpatialTrackingVisualLab : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField, Min(4f)] private float sequenceSeconds = 12f;

        private GameObject personCapsule;
        private GameObject obstacleBox;
        private Material personMaterial;
        private Material obstacleMaterial;
        private double startedAt;

        private void OnEnable()
        {
            startedAt = Time.realtimeSinceStartupAsDouble;
            EnsureObjects();
        }

        private void Update()
        {
            if (!visible)
            {
                SetVisible(false);
                return;
            }

            EnsureObjects();
            float phase = (float)(
                (Time.realtimeSinceStartupAsDouble - startedAt)
                % sequenceSeconds);
            float normalized = phase / sequenceSeconds;
            float triangle = 1f - Mathf.Abs(normalized * 2f - 1f);
            personCapsule.transform.position = new Vector3(
                Mathf.Sin(phase * 0.8f) * 1.1f,
                1f,
                Mathf.Lerp(5.0f, 0.55f, triangle));
            obstacleBox.transform.position = new Vector3(
                -1.35f,
                0.55f,
                Mathf.Lerp(4.0f, 0.20f,
                    Mathf.PingPong(phase / 4f, 1f)));
            bool occluded = phase >= 5.25f && phase <= 6.0f;
            personCapsule.SetActive(!occluded);
            obstacleBox.SetActive(true);
        }

        private void OnDrawGizmos()
        {
            if (!visible)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector3 origin = camera.transform.position;
            Vector3 forward = camera.transform.forward;
            Gizmos.color = new Color(0.15f, 0.75f, 1f, 0.9f);
            DrawProbe(origin, forward, 5f);
            DrawProbe(origin,
                Quaternion.AngleAxis(-10f, camera.transform.up) * forward, 5f);
            DrawProbe(origin,
                Quaternion.AngleAxis(10f, camera.transform.up) * forward, 5f);
            DrawProbe(origin,
                Quaternion.AngleAxis(-10f, camera.transform.right) * forward, 5f);
            DrawProbe(origin,
                Quaternion.AngleAxis(10f, camera.transform.right) * forward, 5f);

            if (personCapsule != null && personCapsule.activeSelf)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(personCapsule.transform.position, 0.18f);
                Gizmos.DrawLine(origin, personCapsule.transform.position);
            }
        }

        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }

            float phase = (float)(
                (Time.realtimeSinceStartupAsDouble - startedAt)
                % sequenceSeconds);
            string state = phase < 5.25f
                ? "APPROACH"
                : phase <= 6.0f ? "OCCLUDED (0.75s)" : "RECEDE";
            GUI.Label(
                new Rect(18f, 18f, 560f, 70f),
                "SPATIAL TRACKING VISUAL LAB\n"
                    + "Person: " + state
                    + "   Cyan: probes   Yellow: tracked world point");
        }

        private void EnsureObjects()
        {
            if (personCapsule == null)
            {
                personCapsule = GameObject.CreatePrimitive(
                    PrimitiveType.Capsule);
                personCapsule.name = "Visual Lab Moving Person";
                personCapsule.transform.localScale =
                    new Vector3(0.55f, 1f, 0.55f);
                personMaterial = CreateMaterial(
                    new Color(0.18f, 0.88f, 0.42f));
                personCapsule.GetComponent<Renderer>().sharedMaterial =
                    personMaterial;
            }

            if (obstacleBox == null)
            {
                obstacleBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacleBox.name = "Visual Lab Moving Obstacle";
                obstacleBox.transform.localScale =
                    new Vector3(1.1f, 1.1f, 1.1f);
                obstacleMaterial = CreateMaterial(
                    new Color(0.92f, 0.34f, 0.12f));
                obstacleBox.GetComponent<Renderer>().sharedMaterial =
                    obstacleMaterial;
            }
        }

        private void SetVisible(bool value)
        {
            if (personCapsule != null)
            {
                personCapsule.SetActive(value);
            }

            if (obstacleBox != null)
            {
                obstacleBox.SetActive(value);
            }
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");
            var material = new Material(shader)
            {
                color = color,
                hideFlags = HideFlags.DontSave
            };
            return material;
        }

        private static void DrawProbe(
            Vector3 origin,
            Vector3 direction,
            float distance)
        {
            Gizmos.DrawLine(origin, origin + direction.normalized * distance);
        }

        private void OnDestroy()
        {
            DestroyRuntime(personCapsule);
            DestroyRuntime(obstacleBox);
            DestroyRuntime(personMaterial);
            DestroyRuntime(obstacleMaterial);
        }

        private static void DestroyRuntime(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
