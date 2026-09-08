using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TeamVR.Experiment
{
    // Drives the pre-experiment practice tutorial as a self-contained stage
    // sequence, kept deliberately separate from ExperimentRoundController so
    // it can never affect round completion state, condition order, or
    // logged score: it never calls ExperimentRoundController.BeginRound and
    // uses its own hand-tuned practice schedule rather than any
    // ProjectileScheduleSet_Condition* asset. It does reuse the real
    // gun/ball/score/SFX pipeline (ExperimentBallSpawner.SpawnSingleBall,
    // ExperimentBall's hit/expire events, ExperimentScoreSystem) so the
    // rules a participant learns here are exactly the rules of the real
    // rounds. It does force passthrough to the plain Guardian condition
    // (same override ExperimentRoundController applies for round index 0)
    // for the tutorial's duration, so trainees never see our adaptive/
    // selective passthrough before their first real round.
    [DefaultExecutionOrder(735)]
    [DisallowMultipleComponent]
    public sealed class ExperimentTutorialController : MonoBehaviour
    {
        [SerializeField] private OVRCameraRig cameraRig;
        [SerializeField] private ExperimentBallSpawner ballSpawner;
        [SerializeField] private ExperimentScoreSystem scoreSystem;
        [SerializeField] private PassthroughConditionSwitcher conditionSwitcher;

        [Header("Hand-Authored UI (drag from Hierarchy)")]
        [Tooltip("Instruction panel shown before each practice stage and hidden during practice.")]
        [SerializeField] private GameObject tutorialPanel;
        [SerializeField] private TMP_Text instructionText;
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private TMP_Text feedbackText;
        [SerializeField] private Button exitTutorialButton;
        [SerializeField] private Button confirmInstructionButton;

        [Tooltip("Panel shown once stage 4 finishes.")]
        [SerializeField] private GameObject completePanel;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button backToMenuButton;

        [Header("Stage 1 - Aim & Fire (fixed target)")]
        [SerializeField, Min(1)] private int stage1RequiredHits = 1;
        [SerializeField, Min(0.05f)] private float stage1DriftSpeedMetersPerSecond = 0.8f;
        [SerializeField] private float stage1SpawnHeightOffsetMeters = 0f;

        [Header("Stage 2 - Moving Targets")]
        [SerializeField, Min(1)] private int stage2TargetCount = 3;
        [SerializeField, Min(0.1f)] private float stage2SpeedMetersPerSecond = 0.8f;
        [SerializeField, Min(0.5f)] private float stage2SpawnDistanceMeters = 4f;
        [SerializeField, Min(0f)] private float stage2GapSecondsBetweenTargets = 1f;

        [Header("Stage 3 - Bomb Avoidance")]
        [SerializeField] private GameObject previewTargetPrefab;
        [SerializeField] private GameObject previewBombPrefab;
        [Tooltip("Where to float the target/bomb preview - drag the round-select UI panel (e.g. ExperimentMenuCanvas) here. Falls back to head-forward + previewDistanceMeters when left unassigned.")]
        [SerializeField] private Transform previewAnchor;
        [SerializeField, Min(0.5f)] private float previewDistanceMeters = 3.0f;
        [SerializeField, Min(0.1f)] private float previewSpacingMeters = 1.5f;
        [SerializeField, Min(0.1f)] private float previewLabelHeightOffset = 0.28f;
        [SerializeField, Min(0.1f)] private float previewLabelFontSize = 0.8f;
        [SerializeField, Min(0.1f)] private float stage3SpeedMetersPerSecond = 1.3f;
        [SerializeField, Min(0.5f)] private float stage3SpawnDistanceMeters = 4f;
        [SerializeField, Min(0f)] private float stage3GapSecondsBetweenBombs = 1f;

        [Header("Stage 4 - Combined Practice")]
        [SerializeField, Min(0.5f)] private float stage4SpawnIntervalSeconds = 2.5f;
        [SerializeField, Min(0.1f)] private float stage4SpeedMetersPerSecond = 0.9f;
        [SerializeField, Min(0.5f)] private float stage4SpawnDistanceMeters = 4f;

        // Fixed spawn order so every participant practices the exact same
        // routine - 5 targets, 2 bombs, always in this order (not randomized
        // per spawn like the old duration/ratio-based loop was).
        private static readonly BallType[] Stage4Sequence =
        {
            BallType.OptionalHit,
            BallType.OptionalHit,
            BallType.MustAvoid,
            BallType.OptionalHit,
            BallType.OptionalHit,
            BallType.MustAvoid,
            BallType.OptionalHit
        };

        [Header("Feedback")]
        [SerializeField, Min(0.5f)] private float feedbackHoldSeconds = 2f;

        private static readonly BallDirection[] SafeDirections =
        {
            BallDirection.Front,
            BallDirection.FrontLeftMid,
            BallDirection.FrontRightMid,
            BallDirection.FrontLeft,
            BallDirection.FrontRight
        };

        private readonly List<GameObject> previewModels = new List<GameObject>();

        private Coroutine sequenceCoroutine;
        private Coroutine feedbackClearCoroutine;
        private int directionCursor;
        private bool waitingForConfirmation;

        public bool IsActive { get; private set; }

        // Fired when a tutorial run begins (subscribed by
        // ExperimentMenuController to hide the round-select menu, exactly
        // like a real round starting).
        public event Action TutorialStarted;

        // Fired when control returns to the round-select menu, whether via
        // the mid-tutorial "연습 종료" button or the completion screen's
        // "라운드 선택으로" button.
        public event Action TutorialReturnedToMenu;

        // Fired once, the moment stage 4 finishes and the completion screen
        // is shown. Does not fire again on "다시 연습".
        public event Action TutorialCompleted;

        private void Awake()
        {
            ResolveReferences();
            WireButtons();
            if (tutorialPanel != null)
            {
                tutorialPanel.SetActive(false);
            }

            if (completePanel != null)
            {
                completePanel.SetActive(false);
            }
        }

        private void OnDisable()
        {
            StopAllActivity();
            if (IsActive)
            {
                IsActive = false;
                conditionSwitcher?.RestoreConfiguredBehavior();
            }
        }

        private void WireButtons()
        {
            if (confirmInstructionButton != null)
            {
                confirmInstructionButton.onClick.AddListener(ConfirmInstruction);
            }

            if (exitTutorialButton != null)
            {
                exitTutorialButton.onClick.RemoveAllListeners();
                exitTutorialButton.onClick.AddListener(ReturnToMenu);
            }

            if (retryButton != null)
            {
                retryButton.onClick.RemoveAllListeners();
                retryButton.onClick.AddListener(RestartTutorial);
            }

            if (backToMenuButton != null)
            {
                backToMenuButton.onClick.RemoveAllListeners();
                backToMenuButton.onClick.AddListener(ReturnToMenu);
            }
        }

        public void BeginTutorial()
        {
            if (IsActive)
            {
                return;
            }

            ResolveReferences();
            if (tutorialPanel == null || confirmInstructionButton == null)
            {
                Debug.LogError("Assign the tutorial panel and confirmation button before starting the tutorial.", this);
                return;
            }

            StopAllActivity();
            directionCursor = 0;
            IsActive = true;
            conditionSwitcher?.ApplyCondition(ExperimentCondition.GuardianDefault);
            TutorialStarted?.Invoke();
            EnterStagePanel();
            sequenceCoroutine = StartCoroutine(RunTutorialSequence());
        }

        private void RestartTutorial()
        {
            if (!IsActive)
            {
                return;
            }

            StopAllActivity();
            directionCursor = 0;
            EnterStagePanel();
            sequenceCoroutine = StartCoroutine(RunTutorialSequence());
        }

        private void ReturnToMenu()
        {
            if (!IsActive)
            {
                return;
            }

            StopAllActivity();
            IsActive = false;
            conditionSwitcher?.RestoreConfiguredBehavior();
            SetPanelsActive(false, false);
            TutorialReturnedToMenu?.Invoke();
        }

        private void EnterStagePanel()
        {
            SetPanelsActive(true, false);
            SetProgressText(string.Empty);
            SetFeedbackText(string.Empty);
        }

        private void SetPanelsActive(bool showStage, bool showComplete)
        {
            if (tutorialPanel != null)
            {
                tutorialPanel.SetActive(showStage);
            }

            if (completePanel != null)
            {
                completePanel.SetActive(showComplete);
            }
        }

        // Stops any in-flight coroutine, clears leftover projectiles and
        // preview models, and resets score - called at every stage/tutorial
        // boundary (start, restart, cancel, complete) so nothing bleeds
        // across a boundary. Real rounds reset score independently via
        // ExperimentRoundController.RoundStarted; this covers the tutorial's
        // own entry/exit points, which never fire that event.
        private void StopAllActivity()
        {
            waitingForConfirmation = false;
            if (sequenceCoroutine != null)
            {
                StopCoroutine(sequenceCoroutine);
                sequenceCoroutine = null;
            }

            if (feedbackClearCoroutine != null)
            {
                StopCoroutine(feedbackClearCoroutine);
                feedbackClearCoroutine = null;
            }

            ballSpawner?.StopRound();
            ClearPreviewModels();
            scoreSystem?.ResetScore();
        }

        private void ConfirmInstruction()
        {
            if (IsActive && waitingForConfirmation)
            {
                waitingForConfirmation = false;
                confirmInstructionButton.interactable = false;
            }
        }

        private IEnumerator WaitForStageConfirmation(string instruction)
        {
            if (feedbackClearCoroutine != null)
            {
                StopCoroutine(feedbackClearCoroutine);
                feedbackClearCoroutine = null;
            }

            EnterStagePanel();
            SetInstruction(instruction);
            waitingForConfirmation = true;
            confirmInstructionButton.interactable = true;
            yield return new WaitUntil(() => !waitingForConfirmation);
            SetPanelsActive(false, false);
            // Allow the UI trigger press to finish before creating a target.
            yield return null;
        }

        // Bomb-trial failure feedback (shot it / got hit) needs the player to
        // actually read it before the next bomb spawns - a fixed auto-hide
        // timer was closing it out from under them, sometimes before they'd
        // even looked. Blocks on the same confirm button as
        // WaitForStageConfirmation instead of timing out on its own.
        private IEnumerator WaitForFeedbackConfirmation(string message)
        {
            if (feedbackClearCoroutine != null)
            {
                StopCoroutine(feedbackClearCoroutine);
                feedbackClearCoroutine = null;
            }

            SetPanelsActive(true, false);
            SetInstruction(string.Empty);
            SetFeedbackText(message);
            waitingForConfirmation = true;
            confirmInstructionButton.interactable = true;
            yield return new WaitUntil(() => !waitingForConfirmation);
            SetFeedbackText(string.Empty);
            SetPanelsActive(false, false);
            yield return null;
        }

        private IEnumerator RunTutorialSequence()
        {
            yield return RunStage1AimAndFire();
            yield return RunStage2MovingTargets();
            yield return RunStage3BombAvoidance();
            yield return RunStage4Combined();

            sequenceCoroutine = null;
            SetPanelsActive(false, true);
            TutorialCompleted?.Invoke();
        }

        private IEnumerator RunStage1AimAndFire()
        {
            yield return WaitForStageConfirmation("\n\n\n\n\n오른손으로 과녁을 조준하고 검지 트리거를 누르세요. \n\n\n과녁을 맞히면 +100점입니다.");
            int hits = 0;
            SetProgressText($"명중 {hits}/{stage1RequiredHits}");

            while (hits < stage1RequiredHits)
            {
                BallSpawnEntry entry = new BallSpawnEntry
                {
                    direction = BallDirection.Front,
                    spawnHeightOffsetMeters = stage1SpawnHeightOffsetMeters,
                    targetHeightOffsetMeters = 0f,
                    speedMetersPerSecond = stage1DriftSpeedMetersPerSecond,
                    ballType = BallType.OptionalHit,
                    spawnDistanceMeters = 4f
                };

                GameObject ball = ballSpawner != null ? ballSpawner.SpawnSingleBall(entry) : null;
                bool wasHit = false;
                yield return WaitForBallResolution(
                    ball,
                    onShotHit: () => wasHit = true);

                if (wasHit)
                {
                    hits++;
                    ShowFeedback("명중! +100");
                    SetProgressText($"명중 {hits}/{stage1RequiredHits}");
                }

                yield return new WaitForSeconds(0.4f);
            }
        }

        private IEnumerator RunStage2MovingTargets()
        {
            yield return WaitForStageConfirmation("\n\n\n\n\n날아오는 과녁을 맞히세요. \n\n\n\n명중하면 +100점\n\n\n몸에 닿으면 -100점");
            int remaining = stage2TargetCount;
            SetProgressText($"남은 과녁 {remaining}");

            while (remaining > 0)
            {
                BallSpawnEntry entry = new BallSpawnEntry
                {
                    direction = NextSafeDirection(),
                    spawnDistanceMeters = stage2SpawnDistanceMeters,
                    speedMetersPerSecond = stage2SpeedMetersPerSecond,
                    ballType = BallType.OptionalHit
                };

                GameObject ball = ballSpawner != null ? ballSpawner.SpawnSingleBall(entry) : null;
                yield return WaitForBallResolution(
                    ball,
                    onShotHit: () => ShowFeedback("명중! +100"),
                    onBodyHit: () => ShowFeedback("몸에 닿았어요 -100"),
                    onExpired: () => ShowFeedback("놓쳤어요"));

                remaining--;
                if (remaining > 0)
                {
                    SetProgressText($"남은 과녁 {remaining}");
                    yield return new WaitForSeconds(stage2GapSecondsBetweenTargets);
                }
                else
                {
                    SetProgressText("완료");
                }
            }
        }

        private IEnumerator RunStage3BombAvoidance()
        {
            yield return ShowBombVsTargetPreview();

            // One bomb at a time - a failed dodge (shot or body hit) just
            // means another single bomb, repeated until the trainee lands
            // an evasion, rather than a fixed batch of attempts.
            bool evaded = false;
            int trialNumber = 0;
            while (!evaded)
            {
                trialNumber++;
                yield return RunSingleBombTrial(trialNumber);
                evaded = lastBombTrialOutcome == BombTrialOutcome.Evaded;

                if (!evaded)
                {
                    yield return new WaitForSeconds(stage3GapSecondsBetweenBombs);
                }
            }
        }

        // Coroutines can't return values directly - RunSingleBombTrial
        // writes its outcome here right before returning, and
        // RunStage3BombAvoidance reads it immediately after yielding on the
        // trial to decide whether to loop again.
        private BombTrialOutcome lastBombTrialOutcome;

        private enum BombTrialOutcome
        {
            Evaded,
            Shot,
            BodyHit
        }

        private IEnumerator RunSingleBombTrial(int trialNumber)
        {
            BallSpawnEntry entry = new BallSpawnEntry
            {
                direction = NextSafeDirection(),
                spawnDistanceMeters = stage3SpawnDistanceMeters,
                speedMetersPerSecond = stage3SpeedMetersPerSecond,
                ballType = BallType.MustAvoid
            };

            SetProgressText($"폭탄 연습 {trialNumber}");
            GameObject ball = ballSpawner != null ? ballSpawner.SpawnSingleBall(entry) : null;

            // Actions run synchronously from inside WaitForBallResolution's
            // event handlers - they can't yield, so the outcome is just
            // recorded here and acted on afterward, once we're back on this
            // coroutine and can safely wait on a confirmation.
            BombTrialOutcome outcome = BombTrialOutcome.Evaded;
            yield return WaitForBallResolution(
                ball,
                onShotHit: () => outcome = BombTrialOutcome.Shot,
                onBodyHit: () => outcome = BombTrialOutcome.BodyHit,
                onExpired: () => outcome = BombTrialOutcome.Evaded);

            switch (outcome)
            {
                case BombTrialOutcome.Shot:
                    yield return WaitForFeedbackConfirmation("폭탄을 쏘면 점수가 절반으로 줄어요.");
                    break;
                case BombTrialOutcome.BodyHit:
                    yield return WaitForFeedbackConfirmation("폭탄을 피하지 못했어요! 점수가 0점이 돼요.");
                    break;
                default:
                    ShowFeedback("회피 성공!");
                    break;
            }

            lastBombTrialOutcome = outcome;
        }

        private IEnumerator ShowBombVsTargetPreview()
        {
            Vector3 center;
            Vector3 forward;
            if (previewAnchor != null)
            {
                // Reuse the round-select UI's own placement (it's already
                // positioned/faced comfortably for the current player via
                // WorldSpacePanelPlacementController) instead of computing a
                // fresh spot from whatever direction the head happens to be
                // facing the instant stage 3 starts.
                center = previewAnchor.position;
                forward = FlatForward(previewAnchor.forward);
            }
            else
            {
                Vector3 origin = transform.position;
                forward = Vector3.forward;
                if (cameraRig != null && cameraRig.centerEyeAnchor != null)
                {
                    origin = cameraRig.centerEyeAnchor.position;
                    forward = FlatForward(cameraRig.centerEyeAnchor.forward);
                }

                center = origin + forward * previewDistanceMeters;
            }

            Quaternion facing = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 right = facing * Vector3.right;
            // Preview models stand still facing the player, mirroring how a
            // real ball's forward points at the player mid-flight - not the
            // "facing" rotation above, which points away from the player
            // toward the preview spot.
            Quaternion facingPlayer = Quaternion.LookRotation(-forward, Vector3.up);

            SpawnPreview(
                previewTargetPrefab,
                center - right * (previewSpacingMeters * 0.8f),
                facingPlayer,
                "과녁",
                new Color(0.25f, 0.85f, 0.35f));
            SpawnPreview(
                previewBombPrefab,
                center + right * (previewSpacingMeters * 0.8f),
                facingPlayer,
                "폭탄",
                new Color(0.9f, 0.25f, 0.25f));

            yield return WaitForStageConfirmation("\n\n\n\n왼쪽은 과녁, 오른쪽은 폭탄입니다. \n\n\n폭탄을 쏘면 점수가 절반으로 줄고, \n\n몸에 닿으면 0점이 됩니다. \n\n\n폭탄은 쏘지 말고 몸을 살짝 움직여 피하세요.");
            ClearPreviewModels();
        }

        private void SpawnPreview(GameObject prefab, Vector3 position, Quaternion facing, string label, Color labelColor)
        {
            if (prefab == null)
            {
                return;
            }

            // Preview-only prop: strip the collider and disable ExperimentBall
            // immediately so it can never be hit-scanned by the gun or
            // register a score/collision outcome, per the "no hit/score
            // judging on the preview models" requirement. Disabling (rather
            // than removing) ExperimentBall also prevents its Update() from
            // running on an un-Configure()'d instance the same frame.
            GameObject instance = Instantiate(prefab, position, Quaternion.identity, transform);
            ExperimentBall ballComponent = instance.GetComponent<ExperimentBall>();
            if (ballComponent != null)
            {
                // Instantiate always drops the prefab's authored rotation in
                // world space, ignoring the per-model faceRotationOffset the
                // real in-flight ball applies (ExperimentBall.Configure) -
                // that's why the preview kept showing up turned relative to
                // how the model looks once it's actually flying.
                instance.transform.rotation =
                    facing * Quaternion.Euler(ballComponent.FaceRotationOffsetEulerAngles);
                ballComponent.enabled = false;
            }
            else
            {
                instance.transform.rotation = facing;
            }

            Collider ballCollider = instance.GetComponent<Collider>();
            if (ballCollider != null)
            {
                ballCollider.enabled = false;
            }

            GameObject labelObject = new GameObject("GeneratedPreviewLabel");
            labelObject.transform.SetParent(instance.transform, false);
            labelObject.transform.localPosition = Vector3.up * previewLabelHeightOffset;

            TextMeshPro label3D = labelObject.AddComponent<TextMeshPro>();
            label3D.text = label;
            label3D.fontSize = previewLabelFontSize;
            label3D.color = labelColor;
            label3D.alignment = TextAlignmentOptions.Center;

            if (cameraRig != null && cameraRig.centerEyeAnchor != null)
            {
                Vector3 toCamera = cameraRig.centerEyeAnchor.position - labelObject.transform.position;
                if (toCamera.sqrMagnitude > 0.0001f)
                {
                    labelObject.transform.rotation =
                        Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
                }
            }

            previewModels.Add(instance);
        }

        private void ClearPreviewModels()
        {
            for (int i = 0; i < previewModels.Count; i++)
            {
                if (previewModels[i] != null)
                {
                    Destroy(previewModels[i]);
                }
            }

            previewModels.Clear();
        }

        private IEnumerator RunStage4Combined()
        {
            yield return WaitForStageConfirmation("\n\n\n\n\n이제 과녁과 폭탄이 함께 나옵니다.");
            SetProgressText("종합 연습 진행 중...");

            GameObject lastBall = null;
            for (int i = 0; i < Stage4Sequence.Length; i++)
            {
                BallSpawnEntry entry = new BallSpawnEntry
                {
                    direction = NextSafeDirection(),
                    spawnDistanceMeters = stage4SpawnDistanceMeters,
                    speedMetersPerSecond = stage4SpeedMetersPerSecond,
                    ballType = Stage4Sequence[i]
                };

                GameObject ball = ballSpawner != null ? ballSpawner.SpawnSingleBall(entry) : null;
                if (i == Stage4Sequence.Length - 1)
                {
                    lastBall = ball;
                }
                else
                {
                    yield return new WaitForSeconds(stage4SpawnIntervalSeconds);
                }
            }

            // Don't advance to the completion screen while the last target
            // is still in flight - wait for it to actually resolve (shot,
            // body hit, or expired) so the round-select UI never appears
            // before the trainee has finished the last trial.
            yield return WaitForBallResolution(lastBall);

            SetProgressText("종합 연습 완료");
            // Time's up regardless of score or remaining in-flight balls -
            // force-clear immediately so nothing lingers into the
            // completion screen.
            ballSpawner?.StopRound();
        }

        private IEnumerator WaitForBallResolution(
            GameObject ballObject,
            Action onShotHit = null,
            Action onBodyHit = null,
            Action onExpired = null)
        {
            ExperimentBall ball = ballObject != null
                ? ballObject.GetComponent<ExperimentBall>()
                : null;
            if (ball == null)
            {
                yield break;
            }

            bool resolved = false;
            void HandleShot(ExperimentBall b)
            {
                resolved = true;
                onShotHit?.Invoke();
            }

            void HandleBody(ExperimentBall b)
            {
                resolved = true;
                onBodyHit?.Invoke();
            }

            void HandleExpired(ExperimentBall b)
            {
                resolved = true;
                onExpired?.Invoke();
            }

            ball.ShotHit += HandleShot;
            ball.BodyHit += HandleBody;
            ball.Expired += HandleExpired;

            yield return new WaitUntil(() => resolved);

            ball.ShotHit -= HandleShot;
            ball.BodyHit -= HandleBody;
            ball.Expired -= HandleExpired;
        }

        private BallDirection NextSafeDirection()
        {
            BallDirection direction = SafeDirections[directionCursor % SafeDirections.Length];
            directionCursor++;
            return direction;
        }

        private static Vector3 FlatForward(Vector3 forward)
        {
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            return flat.sqrMagnitude < 0.0001f ? Vector3.forward : flat.normalized;
        }

        private void SetInstruction(string text)
        {
            if (instructionText != null)
            {
                instructionText.text = text;
            }
        }

        private void SetProgressText(string text)
        {
            if (progressText != null)
            {
                progressText.text = text;
            }
        }

        private void ShowFeedback(string text)
        {
            if (feedbackText == null)
            {
                return;
            }

            feedbackText.text = text;

            if (feedbackClearCoroutine != null)
            {
                StopCoroutine(feedbackClearCoroutine);
            }

            feedbackClearCoroutine = StartCoroutine(ClearFeedbackAfterDelay());
        }

        private IEnumerator ClearFeedbackAfterDelay()
        {
            yield return new WaitForSeconds(feedbackHoldSeconds);
            SetFeedbackText(string.Empty);
            feedbackClearCoroutine = null;
        }

        private void SetFeedbackText(string text)
        {
            if (feedbackText != null)
            {
                feedbackText.text = text;
            }
        }

        private void ResolveReferences()
        {
            if (cameraRig == null)
            {
                cameraRig = FindAnyObjectByType<OVRCameraRig>();
            }

            if (ballSpawner == null)
            {
                ballSpawner = FindAnyObjectByType<ExperimentBallSpawner>();
            }

            if (scoreSystem == null)
            {
                scoreSystem = FindAnyObjectByType<ExperimentScoreSystem>();
            }

            if (conditionSwitcher == null)
            {
                conditionSwitcher = FindAnyObjectByType<PassthroughConditionSwitcher>();
            }
        }
    }
}
