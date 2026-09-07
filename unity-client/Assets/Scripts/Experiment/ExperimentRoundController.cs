using System;
using System.Collections;
using UnityEngine;

namespace TeamVR.Experiment
{
    [DefaultExecutionOrder(730)]
    [DisallowMultipleComponent]
    public sealed class ExperimentRoundController :
        MonoBehaviour,
        IExperimentConditionProvider,
        TeamVR.AdaptivePassthrough.IExperimentRuntimeContextProvider
    {
        public const float DefaultRoundDurationSeconds = 180f;

        [SerializeField]
        private PassthroughConditionSwitcher conditionSwitcher;
        [SerializeField]
        private ExperimentBallSpawner ballSpawner;
        [SerializeField, Min(10f)]
        private float maxRoundSeconds = DefaultRoundDurationSeconds;
        [SerializeField, Min(0.1f)]
        private float guardianReadyTimeoutSeconds = 2f;

        private Coroutine timeoutCoroutine;
        private Coroutine startCoroutine;
        private bool roundActive;

        public bool RoundActive => roundActive;
        public bool Transitioning => startCoroutine != null;
        public ExperimentCondition CurrentCondition { get; private set; } =
            ExperimentCondition.StaticAndDynamic;
        public long RoundSequence { get; private set; }
        public string RoundId { get; private set; } = string.Empty;
        public string LastStartError { get; private set; } = string.Empty;
        public string ConditionName => CurrentCondition.ToString();
        public string PresentationOverrideName =>
            PresentationOverride.ToString();
        public string BoundaryOverrideName => BoundaryOverride.ToString();

        public ExperimentPresentationOverride PresentationOverride =>
            conditionSwitcher == null
                ? ExperimentPresentationOverride.UseUserSettings
                : conditionSwitcher.PresentationOverride;

        public BoundaryVisibilityOverride BoundaryOverride =>
            conditionSwitcher == null
                ? BoundaryVisibilityOverride.UseConfiguredPolicy
                : conditionSwitcher.BoundaryOverride;

        public bool RequestedBoundarySuppression =>
            conditionSwitcher != null
            && conditionSwitcher.RequestedBoundarySuppression;

        public bool ActualBoundarySuppressed =>
            conditionSwitcher != null
            && conditionSwitcher.ActualBoundarySuppressed;

        public event Action<ExperimentCondition> RoundStarted;
        public event Action RoundEnded;
        public event Action<string> RoundStartFailed;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDisable()
        {
            StopRuntimeState(false);
        }

        public void Configure(
            PassthroughConditionSwitcher switcher,
            ExperimentBallSpawner spawner)
        {
            conditionSwitcher = switcher;
            ballSpawner = spawner;
        }

        public bool BeginRound(
            ExperimentCondition condition,
            ProjectileScheduleSet scheduleSet)
        {
            if (roundActive || startCoroutine != null)
            {
                return false;
            }

            ResolveReferences();
            if (!ValidateStart(scheduleSet, out string error))
            {
                FailStart(error);
                return false;
            }

            LastStartError = string.Empty;
            startCoroutine = StartCoroutine(
                BeginRoundWhenReady(condition, scheduleSet));
            return true;
        }

        public void EndRound()
        {
            if (ballSpawner != null)
            {
                ballSpawner.StopRound();
            }

            HandleRoundComplete();
        }

        private IEnumerator BeginRoundWhenReady(
            ExperimentCondition condition,
            ProjectileScheduleSet scheduleSet)
        {
            if (!conditionSwitcher.ApplyCondition(condition, out string error))
            {
                startCoroutine = null;
                FailStart(error);
                yield break;
            }

            if (condition == ExperimentCondition.GuardianDefault)
            {
                float deadline = Time.realtimeSinceStartup
                    + guardianReadyTimeoutSeconds;
                while (!conditionSwitcher.IsConditionReady()
                    && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                if (!conditionSwitcher.IsConditionReady())
                {
                    string readinessError =
                        conditionSwitcher.GetReadinessFailure();
                    conditionSwitcher.RestoreConfiguredBehavior();
                    startCoroutine = null;
                    FailStart(readinessError);
                    yield break;
                }
            }

            CurrentCondition = condition;
            RoundSequence++;
            RoundId = string.Format(
                "round-{0:D3}-{1}",
                RoundSequence,
                condition);
            roundActive = true;
            startCoroutine = null;
            // The experiment duration is fixed. Finishing the last projectile
            // early must not shorten a participant's round.
            ballSpawner.StartRound(scheduleSet, null);
            timeoutCoroutine = StartCoroutine(
                RoundTimeout(maxRoundSeconds));
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

            conditionSwitcher?.RestoreConfiguredBehavior();
            RoundEnded?.Invoke();
        }

        private bool ValidateStart(
            ProjectileScheduleSet scheduleSet,
            out string error)
        {
            if (conditionSwitcher == null)
            {
                error = "Passthrough condition switcher is missing.";
                return false;
            }

            if (!conditionSwitcher.ValidateReferences(out error))
            {
                return false;
            }

            if (ballSpawner == null)
            {
                error = "Ball spawner is missing.";
                return false;
            }

            if (scheduleSet == null || scheduleSet.Entries.Count == 0)
            {
                error = "Projectile schedule is missing or empty.";
                return false;
            }

            ExperimentGameRoot gameRoot =
                GetComponentInParent<ExperimentGameRoot>();
            if (gameRoot == null)
            {
                error = "Experiment round is not inside ExperimentGameRoot.";
                return false;
            }

            if (!gameRoot.ValidateConfiguration(out error))
            {
                return false;
            }

            return ballSpawner.ValidateConfiguration(out error);
        }

        private void FailStart(string error)
        {
            LastStartError = string.IsNullOrWhiteSpace(error)
                ? "Round could not start."
                : error;
            Debug.LogError(
                "[ExperimentRoundController] " + LastStartError,
                this);
            RoundStartFailed?.Invoke(LastStartError);
        }

        private void StopRuntimeState(bool notify)
        {
            if (startCoroutine != null)
            {
                StopCoroutine(startCoroutine);
                startCoroutine = null;
            }

            if (timeoutCoroutine != null)
            {
                StopCoroutine(timeoutCoroutine);
                timeoutCoroutine = null;
            }

            ballSpawner?.StopRound();
            bool wasActive = roundActive;
            roundActive = false;
            conditionSwitcher?.RestoreConfiguredBehavior();
            if (notify && wasActive)
            {
                RoundEnded?.Invoke();
            }
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
