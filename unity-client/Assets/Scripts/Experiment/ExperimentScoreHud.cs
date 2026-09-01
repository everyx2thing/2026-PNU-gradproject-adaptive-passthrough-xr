using TMPro;
using UnityEngine;

namespace TeamVR.Experiment
{
    // Drives a hand-authored world-space score readout (see the ScoreHud
    // GameObject in the scene, positioned via WorldSpacePanelPlacementController
    // like the experiment menu). This component only pushes the current
    // score into the Inspector-assigned text field - it does not build or
    // place any UI itself. Never references the AdaptivePassthrough risk
    // pipeline. Follows the same refresh-gated LateUpdate pattern as
    // QuestRiskHud.cs.
    [DefaultExecutionOrder(760)]
    [DisallowMultipleComponent]
    public sealed class ExperimentScoreHud : MonoBehaviour
    {
        [SerializeField] private ExperimentScoreSystem scoreSystem;
        [SerializeField, Min(1f)] private float refreshRateHz = 10f;

        [Tooltip("Hand-placed in the scene - drag the score readout's text here.")]
        [SerializeField] private TMP_Text scoreText;

        private double nextRefreshAt;

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

        private void ResolveReferences()
        {
            if (scoreSystem == null)
            {
                scoreSystem = FindAnyObjectByType<ExperimentScoreSystem>();
            }
        }
    }
}
