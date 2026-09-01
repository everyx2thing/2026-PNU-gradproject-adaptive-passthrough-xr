using System;
using System.Collections;
using UnityEngine;

namespace TeamVR.Experiment
{
    // Orchestrates one round: applies the blind passthrough condition, runs
    // the fixed ball schedule, and reports back when the round is over
    // (balls exhausted, or the 5 minute cap is hit).
    [DefaultExecutionOrder(730)]
    [DisallowMultipleComponent]
    public sealed class ExperimentRoundController : MonoBehaviour
    {
        [SerializeField]
        private PassthroughConditionSwitcher conditionSwitcher;
        [SerializeField] private ExperimentBallSpawner ballSpawner;
        [SerializeField, Min(10f)] private float maxRoundSeconds = 300f;

        private Coroutine timeoutCoroutine;
        private bool roundActive;

        public bool RoundActive => roundActive;

        public event Action<ExperimentCondition> RoundStarted;
        public event Action RoundEnded;

        private void Awake()
        {
            ResolveReferences();
        }

        public void BeginRound(
            ExperimentCondition condition,
            ProjectileScheduleSet scheduleSet)
        {
            if (roundActive)
            {
                return;
            }

            ResolveReferences();
            if (ballSpawner == null || scheduleSet == null)
            {
                Debug.LogWarning(
                    "[ExperimentRoundController] Missing ball spawner or "
                    + "schedule set - round not started.");
                return;
            }

            roundActive = true;
            conditionSwitcher?.ApplyCondition(condition);
            ballSpawner.StartRound(scheduleSet, HandleRoundComplete);
            timeoutCoroutine = StartCoroutine(RoundTimeout(maxRoundSeconds));
            RoundStarted?.Invoke(condition);
        }

        private IEnumerator RoundTimeout(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (roundActive)
            {
                ballSpawner.StopRound();
                HandleRoundComplete();
            }
        }

        private void HandleRoundComplete()
        {
            if (!roundActive)
            {
                return;
            }

            roundActive = false;
            if (timeoutCoroutine != null)
            {
                StopCoroutine(timeoutCoroutine);
                timeoutCoroutine = null;
            }

            RoundEnded?.Invoke();
        }

        private void ResolveReferences()
        {
            if (conditionSwitcher == null)
            {
                conditionSwitcher =
                    FindAnyObjectByType<PassthroughConditionSwitcher>();
            }

            if (ballSpawner == null)
            {
                ballSpawner = FindAnyObjectByType<ExperimentBallSpawner>();
            }
        }
    }
}
