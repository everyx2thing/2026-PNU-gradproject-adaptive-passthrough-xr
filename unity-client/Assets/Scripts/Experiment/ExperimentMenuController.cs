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

        [Tooltip(
            "Actual condition this button triggers. Not shown to the "
            + "participant - change per participant to counterbalance "
            + "condition order.")]
        public ExperimentCondition condition = ExperimentCondition.Adaptive;

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
    // set per slot in the inspector so the experimenter can counterbalance
    // condition order without touching code.
    [DefaultExecutionOrder(740)]
    [DisallowMultipleComponent]
    public sealed class ExperimentMenuController : MonoBehaviour
    {
        [SerializeField]
        private ExperimentRoundController roundController;

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

        [Header("Style")]
        [SerializeField] private Color idleColor =
            new Color(0.14f, 0.19f, 0.26f, 0.98f);
        [SerializeField] private Color experiencedColor =
            new Color(0.10f, 0.42f, 0.24f, 0.98f);
        [SerializeField] private Color resetColor =
            new Color(0.50f, 0.18f, 0.18f, 0.98f);

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
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (roundController != null)
            {
                roundController.RoundStarted += HandleRoundStarted;
                roundController.RoundEnded += HandleRoundEnded;
            }
        }

        private void OnDisable()
        {
            if (roundController != null)
            {
                roundController.RoundStarted -= HandleRoundStarted;
                roundController.RoundEnded -= HandleRoundEnded;
            }
        }

        private void HandleRoundStarted(ExperimentCondition condition)
        {
            if (menuRoot != null)
            {
                menuRoot.SetActive(false);
            }
        }

        private void HandleRoundEnded()
        {
            if (menuRoot != null)
            {
                menuRoot.SetActive(true);
            }
        }

        private void StartRound(ExperimentRoundSlot slot)
        {
            if (roundController == null
                || roundController.RoundActive
                || slot == null)
            {
                return;
            }

            slot.experienced = true;
            RefreshSlotVisual(slot);
            roundController.BeginRound(slot.condition, slot.scheduleSet);
        }

        private void ResetExperiencedState()
        {
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
            for (int i = 0; i < slots.Length; i++)
            {
                ExperimentRoundSlot slot = slots[i];
                if (slot.button == null)
                {
                    continue;
                }

                slot.button.onClick.RemoveAllListeners();
                slot.button.onClick.AddListener(() => StartRound(slot));
            }

            if (resetButton != null)
            {
                resetButton.onClick.RemoveAllListeners();
                resetButton.onClick.AddListener(ResetExperiencedState);
            }
        }

        private void ResolveReferences()
        {
            if (roundController == null)
            {
                roundController =
                    FindAnyObjectByType<ExperimentRoundController>();
            }
        }
    }
}
