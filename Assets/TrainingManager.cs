using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the Training UI in the training scene.
/// Provides buttons for the 5 facilities (Power, Agility, Impact, Defense, Finesse) and Rest.
/// Implements two-click confirmation for all actions (Click 1 = preview gains/stats, Click 2 = confirm action).
/// </summary>
public class TrainingManager : MonoBehaviour
{
    private enum ActionType
    {
        None,
        TrainPower,
        TrainAgility,
        TrainImpact,
        TrainDefense,
        TrainFinesse,
        Rest
    }

    [Header("Facility Buttons")]
    [SerializeField] private Button powerFacilityButton;
    [SerializeField] private Button agilityFacilityButton;
    [SerializeField] private Button impactFacilityButton;
    [SerializeField] private Button defenseFacilityButton;
    [SerializeField] private Button finesseFacilityButton;
    [SerializeField] private Button restButton;

    [Header("Facility Level Texts (Optional)")]
    [SerializeField] private TMP_Text powerLevelText;
    [SerializeField] private TMP_Text agilityLevelText;
    [SerializeField] private TMP_Text impactLevelText;
    [SerializeField] private TMP_Text defenseLevelText;
    [SerializeField] private TMP_Text finesseLevelText;

    [Header("Status & Stats Displays")]
    [SerializeField] private Slider energySlider;
    [SerializeField] private TMP_Text energyText;
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private TMP_Text traineeNameText;
    [SerializeField] private TMP_Text traineeStatsText;
    [SerializeField] private TMP_Text actionPreviewText;
    [SerializeField] private TMP_Text statusMessageText;
    [SerializeField] private TMP_Text supportCardsText;

    private ActionType pendingAction = ActionType.None;
    private bool isProcessing = false;

    private void Start()
    {
        powerFacilityButton.onClick.AddListener(() => OnActionButtonClicked(ActionType.TrainPower));
        agilityFacilityButton.onClick.AddListener(() => OnActionButtonClicked(ActionType.TrainAgility));
        impactFacilityButton.onClick.AddListener(() => OnActionButtonClicked(ActionType.TrainImpact));
        defenseFacilityButton.onClick.AddListener(() => OnActionButtonClicked(ActionType.TrainDefense));
        finesseFacilityButton.onClick.AddListener(() => OnActionButtonClicked(ActionType.TrainFinesse));
        restButton.onClick.AddListener(() => OnActionButtonClicked(ActionType.Rest));

        if (GameClient.Instance != null)
        {
            GameClient.Instance.OnTrainingRunUpdated += OnTrainingRunUpdated;
            if (GameClient.Instance.CurrentTrainingRun != null)
            {
                UpdateUI(GameClient.Instance.CurrentTrainingRun);
            }
            else
            {
                RefreshTrainingState();
            }
        }
    }

    private void OnDestroy()
    {
        if (GameClient.Instance != null)
        {
            GameClient.Instance.OnTrainingRunUpdated -= OnTrainingRunUpdated;
        }
    }

    private void RefreshTrainingState()
    {
        SetBusy(true);
        GameClient.Instance.StartOrResumeTraining(run =>
        {
            SetBusy(false);
            UpdateUI(run);
        }, error =>
        {
            SetBusy(false);
            ShowMessage($"Failed to load training run: {error}", isError: true);
        });
    }

    private void OnTrainingRunUpdated(TrainingRunData run)
    {
        UpdateUI(run);
    }

    private void OnActionButtonClicked(ActionType clickedAction)
    {
        if (isProcessing)
        {
            return;
        }

        TrainingRunData run = GameClient.Instance.CurrentTrainingRun;
        if (run == null || run.IsCompleted)
        {
            ShowMessage("Training is complete or not active.", isError: true);
            return;
        }

        // Two-click confirmation logic:
        // First click -> select pending action and display preview/prompt
        // Second click on the SAME button -> execute action
        if (pendingAction == clickedAction)
        {
            ExecuteAction(clickedAction);
            pendingAction = ActionType.None;
        }
        else
        {
            pendingAction = clickedAction;
            DisplayActionPreview(clickedAction, run);
        }
    }

    private void DisplayActionPreview(ActionType action, TrainingRunData run)
    {
        string facName = ActionToFacilityName(action);
        if (action == ActionType.Rest)
        {
            if (run.energy >= run.max_energy)
            {
                actionPreviewText.text = "<color=red>Energy is already full! Cannot rest.</color>";
                pendingAction = ActionType.None;
                return;
            }

            int recovery = Mathf.Min(50, run.max_energy - run.energy);
            actionPreviewText.text = $"<b>[REST PREVIEW]</b>\n" +
                                     $"Recover: <b>+{recovery} Energy</b> (to {run.energy + recovery}/{run.max_energy})\n" +
                                     $"<color=yellow>Click 'Rest' again to confirm.</color>";
            return;
        }

        FacilityPreview preview = run.facility_previews?.GetPreview(facName);
        if (preview == null)
        {
            actionPreviewText.text = $"Train {facName.ToUpperInvariant()} (Click again to confirm)";
            return;
        }

        string failWarning = preview.failure_rate > 0
            ? $"<color=red>Failure Chance: {preview.failure_rate:F1}%</color>"
            : "<color=green>Failure Chance: 0%</color>";

        string rainbowInfo = preview.rainbow_active
            ? $" <color=#00FFFF><b>[RAINBOW +{preview.rainbow_bonus_percent}%]</b></color>"
            : "";

        string energyInfo = preview.energy_restore > 0
            ? $"Energy: <color=green>+{preview.energy_restore} (Restore)</color>"
            : $"Energy Cost: -{preview.energy_cost}";

        System.Collections.Generic.List<string> presentCardsList = new System.Collections.Generic.List<string>();
        if (run.support_cards != null && preview.present_card_ids != null)
        {
            foreach (var card in run.support_cards)
            {
                if (Array.IndexOf(preview.present_card_ids, card.id) >= 0)
                {
                    bool isRainbow = card.facility?.ToLowerInvariant() == facName.ToLowerInvariant() && card.IsRainbowEligible;
                    string tag = isRainbow ? " <color=#00FFFF>[Rainbow!]</color>" : (card.IsRainbowEligible ? " [Rainbow Ready]" : "");
                    presentCardsList.Add($"{card.name} ({card.facility?.ToUpperInvariant()} | Bond: {card.bond}%){tag}");
                }
            }
        }
        string cardsInfo = presentCardsList.Count > 0
            ? $"Support Cards: {string.Join(", ", presentCardsList)}"
            : "Support Cards: None";

        actionPreviewText.text = $"<b>[TRAIN {facName.ToUpperInvariant()} - LVL {preview.level}]</b>{rainbowInfo}\n" +
                                 $"Primary Gain: <b>+{preview.main_gain} {preview.main_stat.ToUpperInvariant()}</b> | " +
                                 $"Secondary Gain: <b>+{preview.secondary_gain} {preview.secondary_stat.ToUpperInvariant()}</b>\n" +
                                 $"{energyInfo} | {failWarning}\n" +
                                 $"{cardsInfo}\n" +
                                 $"<color=yellow>Click '{facName.ToUpperInvariant()}' again to confirm.</color>";
    }

    private void ExecuteAction(ActionType action)
    {
        SetBusy(true);
        actionPreviewText.text = "Executing action...";

        if (action == ActionType.Rest)
        {
            GameClient.Instance.RestTraining(response =>
            {
                SetBusy(false);
                if (response.action_result != null)
                {
                    ShowMessage($"Rested! Recovered {response.action_result.energy_recovered} Energy.");
                }
                UpdateUI(response.run);
            }, error =>
            {
                SetBusy(false);
                ShowMessage($"Rest failed: {error}", isError: true);
            });
            return;
        }

        string facName = ActionToFacilityName(action);
        GameClient.Instance.TrainFacility(facName, response =>
        {
            SetBusy(false);
            if (response.action_result != null)
            {
                if (response.action_result.success)
                {
                    string rainbowTag = response.action_result.rainbow_triggered ? " <color=#00FFFF>[Rainbow!]</color>" : "";
                    string energyTag = response.action_result.energy_recovered > 0 ? $" (Recovered +{response.action_result.energy_recovered} Energy)" : "";
                    ShowMessage($"Training Successful!{rainbowTag}{energyTag} +{response.action_result.main_gain} {response.action_result.main_stat.ToUpperInvariant()}, +{response.action_result.secondary_gain} {response.action_result.secondary_stat.ToUpperInvariant()}");
                }
                else
                {
                    ShowMessage($"<color=red>Training Failed! (Failure rate was {response.action_result.failure_rate:F1}%)</color> No stats gained.", isError: true);
                }
            }
            UpdateUI(response.run);
        }, error =>
        {
            SetBusy(false);
            ShowMessage($"Training request error: {error}", isError: true);
        });
    }

    public void UpdateUI(TrainingRunData run)
    {
        if (run == null)
        {
            return;
        }

        // Round display
        if (roundText != null)
        {
            roundText.text = run.IsCompleted
                ? $"<color=green>Training Completed ({run.max_rounds}/{run.max_rounds})</color>"
                : $"Round: {run.round} / {run.max_rounds} (Year {(run.round - 1) / 12 + 1}, Month {(run.round - 1) % 12 + 1})";
        }

        // Energy display
        if (energySlider != null)
        {
            energySlider.minValue = 0;
            energySlider.maxValue = run.max_energy;
            energySlider.value = run.energy;
        }

        if (energyText != null)
        {
            float failRate = run.energy < (run.max_energy * 0.5f) ? (50f - ((float)run.energy / run.max_energy) * 100f) * 2f : 0f;
            string energyStatus = failRate > 0 ? $" <color=red>(Fail Rate: {failRate:F0}%)</color>" : "";
            energyText.text = $"Energy: {run.energy}/{run.max_energy}{energyStatus}";
        }

        // Trainee info & stats
        if (run.trainee != null)
        {
            if (traineeNameText != null)
            {
                traineeNameText.text = $"{run.trainee.name} [{run.trainee.StarsDisplay}]";
            }

            if (traineeStatsText != null)
            {
                Traits t = run.trainee.traits;
                Traits g = run.trainee.growth;
                traineeStatsText.text = $"<b>Power:</b> {t.power} ({g.power:+0;-0;+0}%)\n" +
                                        $"<b>Agility:</b> {t.agility} ({g.agility:+0;-0;+0}%)\n" +
                                        $"<b>Impact:</b> {t.impact} ({g.impact:+0;-0;+0}%)\n" +
                                        $"<b>Defense:</b> {t.defense} ({g.defense:+0;-0;+0}%)\n" +
                                        $"<b>Finesse:</b> {t.finesse} ({g.finesse:+0;-0;+0}%)\n" +
                                        $"<b>Total Stats:</b> {t.Total}";
            }
        }

        // Facility level texts
        UpdateFacilityLevelText(powerLevelText, "POW", run.facility_levels?.power ?? 1);
        UpdateFacilityLevelText(agilityLevelText, "AGI", run.facility_levels?.agility ?? 1);
        UpdateFacilityLevelText(impactLevelText, "IMP", run.facility_levels?.impact ?? 1);
        UpdateFacilityLevelText(defenseLevelText, "DEF", run.facility_levels?.defense ?? 1);
        UpdateFacilityLevelText(finesseLevelText, "FIN", run.facility_levels?.finesse ?? 1);

        // Support cards display
        if (supportCardsText != null && run.support_cards != null)
        {
            string cardsSummary = "<b>Support Team:</b>\n";
            foreach (var card in run.support_cards)
            {
                string loc = FindCardLocation(card.id, run.facility_previews);
                bool isRainbow = loc.Equals(card.facility, StringComparison.OrdinalIgnoreCase) && card.IsRainbowEligible;
                string rainbowTag = isRainbow ? " <color=#00FFFF>[Rainbow Active!]</color>" : (card.IsRainbowEligible ? " [Rainbow Ready]" : "");
                cardsSummary += $"• {card.name} [{Tiers.ShortName(card.tier)} | {card.facility?.ToUpperInvariant()}] - Bond: {card.bond}% | At: {loc.ToUpperInvariant()}{rainbowTag}\n";
            }
            supportCardsText.text = cardsSummary.TrimEnd();
        }

        if (run.IsCompleted)
        {
            actionPreviewText.text = "<b><color=green>Training completed! Character ready for graduation.</color></b>";
        }
        else if (pendingAction == ActionType.None)
        {
            actionPreviewText.text = "Select a facility or rest to preview stat gains.";
        }
    }

    private static string FindCardLocation(string cardId, FacilityPreviewMap previews)
    {
        if (previews == null) return "Unknown";
        if (previews.power?.present_card_ids != null && Array.IndexOf(previews.power.present_card_ids, cardId) >= 0) return "Power";
        if (previews.agility?.present_card_ids != null && Array.IndexOf(previews.agility.present_card_ids, cardId) >= 0) return "Agility";
        if (previews.impact?.present_card_ids != null && Array.IndexOf(previews.impact.present_card_ids, cardId) >= 0) return "Impact";
        if (previews.defense?.present_card_ids != null && Array.IndexOf(previews.defense.present_card_ids, cardId) >= 0) return "Defense";
        if (previews.finesse?.present_card_ids != null && Array.IndexOf(previews.finesse.present_card_ids, cardId) >= 0) return "Finesse";
        return "None";
    }

    private void UpdateFacilityLevelText(TMP_Text textComponent, string prefix, int level)
    {
        if (textComponent != null)
        {
            textComponent.text = $"{prefix} Lv.{level}";
        }
    }

    private void SetBusy(bool busy)
    {
        isProcessing = busy;
        powerFacilityButton.interactable = !busy;
        agilityFacilityButton.interactable = !busy;
        impactFacilityButton.interactable = !busy;
        defenseFacilityButton.interactable = !busy;
        finesseFacilityButton.interactable = !busy;
        restButton.interactable = !busy;
    }

    private void ShowMessage(string message, bool isError = false)
    {
        if (statusMessageText != null)
        {
            statusMessageText.text = isError ? $"<color=red>{message}</color>" : message;
        }
    }

    private static string ActionToFacilityName(ActionType action) => action switch
    {
        ActionType.TrainPower => "power",
        ActionType.TrainAgility => "agility",
        ActionType.TrainImpact => "impact",
        ActionType.TrainDefense => "defense",
        ActionType.TrainFinesse => "finesse",
        _ => ""
    };
}
