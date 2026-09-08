using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TeamVR.Experiment
{
    [Serializable]
    public sealed class ExperimentRoundSlot
    {
        [Tooltip(
            "Neutral label shown to the participant, e.g. \"Round 1\". "
            + "Must not reveal which condition this button maps to.")]
        public string displayLabel = "Round 1";

        public ProjectileScheduleSet scheduleSet;

        [Tooltip("Hand-placed in the scene - drag the round's Button here.")]
        public Button button;
        [Tooltip("Hand-placed in the scene - drag the round button's Image here (its color is driven by idle/experienced state).")]
        public Image buttonImage;
        [Tooltip("Hand-placed in the scene - drag the round button's \"EXPERIENCED\" label here.")]
        public TMP_Text experiencedTag;

        [NonSerialized] public bool experienced;
    }

    // Drives the blind experiment menu: three neutrally-labeled round
    // buttons plus a "next participant" reset button. The UI itself is
    // hand-authored in the scene (under this GameObject) so it can be laid
    // out and restyled by clicking around in the Editor - this controller
    // only wires click handlers and pushes idle/experienced colors onto the
    // Inspector-assigned references below. Button-to-condition mapping is
    // deliberately fixed by slot index so an Inspector edit cannot change
    // the experiment protocol.
    [DefaultExecutionOrder(740)]
    [DisallowMultipleComponent]
    public sealed class ExperimentMenuController : MonoBehaviour
    {
        [SerializeField]
        private ExperimentRoundController roundController;
        [SerializeField]
        private PassthroughConditionSwitcher conditionSwitcher;

        [SerializeField]
        private ExperimentRoundSlot[] slots =
        {
            new ExperimentRoundSlot { displayLabel = "Round 1" },
            new ExperimentRoundSlot { displayLabel = "Round 2" },
            new ExperimentRoundSlot { displayLabel = "Round 3" }
        };

        [Header("Hand-Authored UI (drag from Hierarchy)")]
        [Tooltip("The panel to show/hide when a round starts/ends.")]
        [SerializeField] private GameObject menuRoot;
        [Tooltip("The \"NEXT PARTICIPANT (RESET)\" button.")]
        [SerializeField] private Button resetButton;
        [Tooltip("Operator-only setup/Guardian errors. Never shown as a condition label.")]
        [SerializeField] private TMP_Text operatorStatusText;

        [Header("Tutorial")]
        [SerializeField] private ExperimentTutorialController tutorialController;
        [SerializeField] private Button tutorialButton;
        [SerializeField] private Image tutorialButtonImage;
        [SerializeField] private TMP_Text tutorialCompletedTag;
        private bool tutorialCompleted;

        [Header("Style")]
        [SerializeField] private Color idleColor =
            new Color(0.14f, 0.19f, 0.26f, 0.98f);
        [SerializeField] private Color experiencedColor =
            new Color(0.10f, 0.42f, 0.24f, 0.98f);
        [SerializeField] private Color resetColor =
            new Color(0.50f, 0.18f, 0.18f, 0.98f);

        private ExperimentRoundSlot pendingSlot;
        private CanvasGroup menuCanvasGroup;

        public void Configure(
            ExperimentRoundController controller,
            GameObject root,
            Button reset,
            TMP_Text status,
            ExperimentRoundSlot[] roundSlots)
        {
            roundController = controller;
            menuRoot = root;
            resetButton = reset;
            operatorStatusText = status;
            slots = roundSlots ?? Array.Empty<ExperimentRoundSlot>();
        }

        public bool ValidateConfiguration(out string error)
        {
            ResolveReferences();
            if (roundController == null || menuRoot == null
                || resetButton == null || operatorStatusText == null)
            {
                error = "Experiment menu controller references are incomplete.";
                return false;
            }

            if (slots == null || slots.Length != 3)
            {
                error = "Exactly three experiment round slots are required.";
                return false;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                ExperimentRoundSlot slot = slots[i];
                if (slot == null || slot.scheduleSet == null
                    || slot.button == null || slot.buttonImage == null
                    || slot.experiencedTag == null)
                {
                    error = "Every round slot needs schedule and UI references.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private void Awake()
        {
            ResolveReferences();
            WireButtons();
            for (int i = 0; i < slots.Length; i++)
            {
                RefreshSlotVisual(slots[i]);
            }

            if (resetButton != null)
            {
                resetButton.image.color = resetColor;
            }

            SetOperatorStatus(string.Empty);
            RefreshTutorialVisual();
            conditionSwitcher?.ApplyCondition(
                ExperimentCondition.GuardianDefault);
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (tutorialController != null)
            {
                tutorialController.TutorialStarted += HandleTutorialStarted;
                tutorialController.TutorialReturnedToMenu += HandleTutorialReturnedToMenu;
                tutorialController.TutorialCompleted += HandleTutorialCompleted;
            }
            if (roundController != null)
            {
                roundController.RoundStarted += HandleRoundStarted;
                roundController.RoundEnded += HandleRoundEnded;
                roundController.RoundStartFailed += HandleRoundStartFailed;
            }
        }

        private void OnDisable()
        {
            if (tutorialController != null)
            {
                tutorialController.TutorialStarted -= HandleTutorialStarted;
                tutorialController.TutorialReturnedToMenu -= HandleTutorialReturnedToMenu;
                tutorialController.TutorialCompleted -= HandleTutorialCompleted;
            }
            if (roundController != null)
            {
                roundController.RoundStarted -= HandleRoundStarted;
                roundController.RoundEnded -= HandleRoundEnded;
                roundController.RoundStartFailed -= HandleRoundStartFailed;
            }
        }

        private void HandleTutorialStarted()
        {
            SetOperatorStatus(string.Empty);
            SetMenuVisible(false);
        }

        private void HandleTutorialReturnedToMenu()
        {
            SetMenuVisible(true);
        }

        private void HandleTutorialCompleted()
        {
            tutorialCompleted = true;
            RefreshTutorialVisual();
        }

        private void StartTutorial()
        {
            if (tutorialController == null
                || (roundController != null
                    && (roundController.RoundActive || roundController.Transitioning)))
            {
                return;
            }

            tutorialController.BeginTutorial();
        }

        private void RefreshTutorialVisual()
        {
            if (tutorialButtonImage != null)
            {
                tutorialButtonImage.color = tutorialCompleted ? experiencedColor : idleColor;
            }

            if (tutorialCompletedTag != null)
            {
                tutorialCompletedTag.text = tutorialCompleted ? "연습 완료" : string.Empty;
            }
        }

        private void HandleRoundStarted(ExperimentCondition condition)
        {
            if (pendingSlot != null)
            {
                pendingSlot.experienced = true;
                RefreshSlotVisual(pendingSlot);
                pendingSlot = null;
            }

            SetOperatorStatus(string.Empty);
            SetMenuVisible(false);
        }

        private void HandleRoundEnded()
        {
            pendingSlot = null;
            SetMenuVisible(true);
        }

        private void HandleRoundStartFailed(string error)
        {
            pendingSlot = null;
            SetOperatorStatus(error);
            SetMenuVisible(true);
        }

        public static ExperimentCondition ConditionForRoundIndex(
            int roundIndex)
        {
            switch (roundIndex)
            {
                case 0: return ExperimentCondition.GuardianDefault;
                case 1: return ExperimentCondition.StaticOnly;
                case 2: return ExperimentCondition.StaticAndDynamic;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(roundIndex),
                        "Experiment rounds are indexed from 0 through 2.");
            }
        }

        private void StartRound(int roundIndex, ExperimentRoundSlot slot)
        {
            if (roundController == null
                || roundController.RoundActive
                || roundController.Transitioning
                || (tutorialController != null && tutorialController.IsActive)
                || slot == null)
            {
                return;
            }

            pendingSlot = slot;
            SetOperatorStatus("Checking experiment condition...");
            ExperimentCondition condition =
                ConditionForRoundIndex(roundIndex);
            if (!roundController.BeginRound(condition, slot.scheduleSet))
            {
                pendingSlot = null;
                SetOperatorStatus(roundController.LastStartError);
            }
        }

        private void ResetExperiencedState()
        {
            tutorialCompleted = false;
            RefreshTutorialVisual();
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].experienced = false;
                RefreshSlotVisual(slots[i]);
            }
        }

        private void RefreshSlotVisual(ExperimentRoundSlot slot)
        {
            if (slot.buttonImage != null)
            {
                slot.buttonImage.color =
                    slot.experienced ? experiencedColor : idleColor;
            }

            if (slot.experiencedTag != null)
            {
                slot.experiencedTag.text =
                    slot.experienced ? "EXPERIENCED" : string.Empty;
            }
        }

        private void WireButtons()
        {
            if (tutorialButton != null)
            {
                tutorialButton.onClick.RemoveAllListeners();
                tutorialButton.onClick.AddListener(StartTutorial);
            }
            for (int i = 0; i < slots.Length; i++)
            {
                int roundIndex = i;
                ExperimentRoundSlot slot = slots[i];
                if (slot.button == null)
                {
                    continue;
                }

                slot.button.onClick.RemoveAllListeners();
                slot.button.onClick.AddListener(
                    () => StartRound(roundIndex, slot));
            }

            if (resetButton != null)
            {
                resetButton.onClick.RemoveAllListeners();
                resetButton.onClick.AddListener(ResetExperiencedState);
            }
        }

        private void ResolveReferences()
        {
            if (tutorialController == null)
            {
                tutorialController = FindAnyObjectByType<ExperimentTutorialController>();
            }
            if (roundController == null)
            {
                roundController =
                    FindAnyObjectByType<ExperimentRoundController>();
            }
            if (conditionSwitcher == null)
            {
                conditionSwitcher =
                    FindAnyObjectByType<PassthroughConditionSwitcher>();
            }
        }

        private void SetMenuVisible(bool visible)
        {
            if (menuRoot == null)
            {
                return;
            }

            if (menuCanvasGroup == null)
            {
                menuCanvasGroup = menuRoot.GetComponent<CanvasGroup>();
                if (menuCanvasGroup == null)
                {
                    menuCanvasGroup = menuRoot.AddComponent<CanvasGroup>();
                }
            }

            menuCanvasGroup.alpha = visible ? 1f : 0f;
            menuCanvasGroup.interactable = visible;
            menuCanvasGroup.blocksRaycasts = visible;

            // Round 2/3 are the only conditions meant to actually show our
            // adaptive passthrough - the round-select menu itself is idle
            // time, not part of any round, so it must never inherit
            // whatever passthrough state a previous round (or plain user
            // settings) would otherwise leave active behind it.
            if (visible)
            {
                ResolveReferences();
                conditionSwitcher?.ApplyCondition(
                    ExperimentCondition.GuardianDefault);
            }
        }

        private void SetOperatorStatus(string message)
        {
            if (operatorStatusText != null)
            {
                operatorStatusText.text = message ?? string.Empty;
            }
        }
    }
}
