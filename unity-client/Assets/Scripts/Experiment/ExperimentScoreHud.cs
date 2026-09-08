using TMPro;
using UnityEngine;

namespace TeamVR.Experiment
{
    // Drives a hand-authored world-space score readout (see the ScoreHud
    // GameObject in the scene). Its position is fixed to the purple tower's
    // authored anchor while its horizontal yaw follows the viewer. Never
    // references the AdaptivePassthrough risk pipeline.
    [DefaultExecutionOrder(760)]
    [DisallowMultipleComponent]
    public sealed class ExperimentScoreHud : MonoBehaviour
    {
        [SerializeField] private ExperimentScoreSystem scoreSystem;
        [SerializeField, Min(1f)] private float refreshRateHz = 10f;

        [Tooltip("Hand-placed in the scene - drag the score readout's text here.")]
        [SerializeField] private TMP_Text scoreText;

        [Tooltip("Optional fixed anchor to snap/face toward every frame. Leave empty to keep the HUD wherever it was placed (e.g. by WorldSpacePanelPlacementController).")]
        [SerializeField] private Transform scoreAnchor;
        [Tooltip("Optional HMD transform. Resolved from OVRCameraRig when empty.")]
        [SerializeField] private Transform viewer;

        private double nextRefreshAt;

        public bool ValidateConfiguration(out string error)
        {
            ResolveReferences();
            if (scoreSystem == null || scoreText == null || viewer == null)
            {
                error = "Experiment score HUD references are incomplete.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            nextRefreshAt = 0.0;
        }

        private void LateUpdate()
        {
            ResolveReferences();
            FaceViewerWithoutMoving();

            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextRefreshAt)
            {
                return;
            }

            nextRefreshAt = now + 1.0 / Mathf.Max(1f, refreshRateHz);
            Refresh();
        }

        public void Refresh()
        {
            ResolveReferences();
            if (scoreText == null)
            {
                return;
            }

            int score = scoreSystem != null ? scoreSystem.Score : 0;
            scoreText.text = "SCORE\n" + score;
        }

        public void ConfigureAnchor(Transform anchor, Transform viewerTransform)
        {
            scoreAnchor = anchor;
            viewer = viewerTransform;
        }

        public void FaceViewerWithoutMoving()
        {
            if (scoreAnchor == null)
            {
                return;
            }

            transform.position = scoreAnchor.position;
            if (viewer == null)
            {
                return;
            }

            Vector3 direction = Vector3.ProjectOnPlane(
                transform.position - viewer.position,
                Vector3.up);
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(
                    direction.normalized,
                    Vector3.up);
            }
        }

        private void ResolveReferences()
        {
            if (scoreSystem == null)
            {
                scoreSystem = FindAnyObjectByType<ExperimentScoreSystem>();
            }

            if (viewer == null)
            {
                OVRCameraRig rig = FindAnyObjectByType<OVRCameraRig>();
                if (rig != null)
                {
                    viewer = rig.centerEyeAnchor;
                }
            }

            if (viewer == null && Camera.main != null)
            {
                viewer = Camera.main.transform;
            }
        }
    }
}
