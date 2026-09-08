using System;
using UnityEngine;

namespace TeamVR.Experiment
{
    // Tracks the participant's in-round score. Purely a game-feel/immersion
    // element - never references the AdaptivePassthrough risk pipeline, and
    // never persists or logs anything (score resets to 0 each round and is
    // discarded when the round ends).
    [DefaultExecutionOrder(760)]
    [DisallowMultipleComponent]
    public sealed class ExperimentScoreSystem : MonoBehaviour
    {
        [SerializeField] private ExperimentRoundController roundController;
        [SerializeField] private AudioSource audioSource;

        [Header("SFX (hit/penalty events only - spawn sound lives on ExperimentBall)")]
        [SerializeField] private AudioClip targetHitRewardClip;
        [SerializeField] private AudioClip bombShotExplosionClip;
        [SerializeField] private AudioClip targetBodyPenaltyClip;
        [SerializeField] private AudioClip bombBodyPenaltyClip;

        public int Score { get; private set; }

        public event Action<int> ScoreChanged;

        public bool ValidateConfiguration(out string error)
        {
            ResolveReferences();
            if (roundController == null || audioSource == null)
            {
                error = "Experiment score controller or AudioSource is missing.";
                return false;
            }

            if (targetHitRewardClip == null
                || bombShotExplosionClip == null
                || targetBodyPenaltyClip == null
                || bombBodyPenaltyClip == null)
            {
                error = "All experiment score AudioClips must be assigned.";
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
            ResolveReferences();
            if (roundController != null)
            {
                roundController.RoundStarted += HandleRoundStarted;
            }
        }

        private void OnDisable()
        {
            if (roundController != null)
            {
                roundController.RoundStarted -= HandleRoundStarted;
            }
        }

        public void ResetScore()
        {
            SetScore(0);
        }

        private void HandleRoundStarted(ExperimentCondition condition)
        {
            SetScore(0);
        }

        // Ball was hit with the gun's hitscan raycast.
        public void RegisterShotHit(BallType ballType)
        {
            bool optionalTarget = ballType == BallType.OptionalHit;
            SetScore(ExperimentScoreRules.ApplyShotHit(
                Score,
                optionalTarget));
            if (optionalTarget)
            {
                PlayClip(targetHitRewardClip);
            }
            else
            {
                PlayClip(bombShotExplosionClip);
            }
        }

        // Ball touched the participant's body without being shot first.
        public void RegisterBodyHit(BallType ballType)
        {
            bool optionalTarget = ballType == BallType.OptionalHit;
            SetScore(ExperimentScoreRules.ApplyBodyHit(
                Score,
                optionalTarget));
            if (optionalTarget)
            {
                PlayClip(targetBodyPenaltyClip);
            }
            else
            {
                PlayClip(bombBodyPenaltyClip);
            }
        }

        private void SetScore(int value)
        {
            Score = value;
            ScoreChanged?.Invoke(Score);
        }

        private void PlayClip(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        private void ResolveReferences()
        {
            if (roundController == null)
            {
                roundController = FindAnyObjectByType<ExperimentRoundController>();
            }

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }
        }
    }
}
