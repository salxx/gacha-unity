using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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

    [Header("Facility Buttons")] [SerializeField]
    private Button powerFacilityButton;

    [SerializeField] private Button agilityFacilityButton;
    [SerializeField] private Button impactFacilityButton;
    [SerializeField] private Button defenseFacilityButton;
    [SerializeField] private Button finesseFacilityButton;
    [SerializeField] private Button restButton;
    [SerializeField] private Button endTrainingButton;
    [SerializeField] private Button suspendTrainingButton;
    [SerializeField] private Image backgroundImage;

    [Header("Skill Shop")]
    [SerializeField] private Button skillShopButton;
    [SerializeField] private bool hidePurchasedSkills;

    [Header("Facility Trainee Image")]
    [SerializeField] private Image traineeImage;
    
    [Header("Facility Support Card Preview")]
    [SerializeField] private Sprite cardEmptyPreviewImage;
    [SerializeField] private Sprite cardPreviewImage;
    [SerializeField] private Sprite cardRainbowPreviewImage;

    [Header("Facility Level Texts (Optional)")] 
    [SerializeField] private TMP_Text powerLevelText;

    [SerializeField] private TMP_Text agilityLevelText;
    [SerializeField] private TMP_Text impactLevelText;
    [SerializeField] private TMP_Text defenseLevelText;
    [SerializeField] private TMP_Text finesseLevelText;

    [Header("Status & Stats Displays")] [SerializeField]
    private Slider energySlider;

    [SerializeField] private TMP_Text energyText;
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private TMP_Text traineeNameText;
    [SerializeField] private TMP_Text traineeStatsText;
    [SerializeField] private TMP_Text actionPreviewText;
    [SerializeField] private TMP_Text statusMessageText;
    [SerializeField] private TMP_Text supportCardsText;

    private ActionType pendingAction = ActionType.None;
    private bool isProcessing = false;
    private SkillShopController skillShop;
    private EventTrigger backgroundEventTrigger;
    private EventTrigger traineeEventTrigger;
    private EventTrigger.Entry traineeClickEntry;
    private TMP_Text traineeDialogueText;
    private string dialogueCharacterId;
    private int lastDialogueIndex = -1;
    private float dialogueExpiresAt;

    private void Start()
    {
        InitializeBackgroundReset();
        InitializeTraineeDialogue();
        powerFacilityButton.onClick.AddListener(() => OnActionButtonClicked(ActionType.TrainPower));
        agilityFacilityButton.onClick.AddListener(() => OnActionButtonClicked(ActionType.TrainAgility));
        impactFacilityButton.onClick.AddListener(() => OnActionButtonClicked(ActionType.TrainImpact));
        defenseFacilityButton.onClick.AddListener(() => OnActionButtonClicked(ActionType.TrainDefense));
        finesseFacilityButton.onClick.AddListener(() => OnActionButtonClicked(ActionType.TrainFinesse));
        restButton.onClick.AddListener(() => OnActionButtonClicked(ActionType.Rest));
        endTrainingButton.onClick.AddListener(OnEndTrainingButtonClicked);
        skillShopButton.onClick.AddListener(OnSkillShopButtonClicked);
        if (suspendTrainingButton != null)
        {
            suspendTrainingButton.onClick.AddListener(OnSuspendTrainingButtonClicked);
        }

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
        skillShopButton.onClick.RemoveListener(OnSkillShopButtonClicked);
        if (traineeEventTrigger != null && traineeClickEntry != null)
        {
            traineeEventTrigger.triggers.Remove(traineeClickEntry);
        }
        if (traineeDialogueText != null)
        {
            Destroy(traineeDialogueText.gameObject);
        }
        if (backgroundEventTrigger != null)
        {
            foreach (EventTrigger.Entry entry in backgroundEventTrigger.triggers)
            {
                if (entry.eventID == EventTriggerType.PointerClick)
                {
                    entry.callback.RemoveListener(HandleBackgroundPointerClick);
                }
            }
        }

        if (GameClient.Instance != null)
        {
            GameClient.Instance.OnTrainingRunUpdated -= OnTrainingRunUpdated;
        }
    }

    private void OnSkillShopButtonClicked()
    {
        if (isProcessing)
        {
            return;
        }

        TrainingRunData run = GameClient.Instance != null ? GameClient.Instance.CurrentTrainingRun : null;
        if (run == null)
        {
            ShowMessage("A training run is required to open the skill shop.", isError: true);
            return;
        }

        Canvas canvas = skillShopButton.GetComponentInParent<Canvas>();
        GameObject prefab = Resources.Load<GameObject>("Prefabs/SkillShop");
        if (canvas == null || prefab == null)
        {
            Debug.LogError(canvas == null
                ? "SkillShopButton is not under a Canvas."
                : "Could not load Resources/Prefabs/SkillShop.prefab.");
            ShowMessage("Failed to open the skill shop.", isError: true);
            return;
        }

        if (skillShop != null)
        {
            Destroy(skillShop.gameObject);
        }

        GameObject shopObject = Instantiate(prefab, canvas.transform, false);
        skillShop = shopObject.AddComponent<SkillShopController>();
        SkillShopController openedShop = skillShop;
        openedShop.Initialize(run.skill_points, hidePurchasedSkills, skillIds =>
        {
            openedShop.SetSubmitting(true);
            SetBusy(true);
            GameClient.Instance.PurchaseTrainingSkills(skillIds, _ =>
            {
                SetBusy(false);
                ShowMessage("Skill purchase complete.");
                if (openedShop != null)
                {
                    openedShop.Close();
                }
            }, error =>
            {
                SetBusy(false);
                ShowMessage($"Skill purchase failed: {error}", isError: true);
                if (openedShop != null)
                {
                    openedShop.SetSubmitting(false);
                }
            });
        }, () =>
        {
            if (skillShop == openedShop)
            {
                skillShop = null;
            }
        });

        GameClient.Instance.FetchTrainingSkills(response =>
        {
            if (openedShop != null)
            {
                openedShop.LoadSkills(response.skills);
            }
        }, error =>
        {
            if (openedShop != null)
            {
                ShowMessage($"Failed to load skill shop: {error}", isError: true);
            }
        });
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
            pendingAction = ActionType.None;
            ExecuteAction(clickedAction);
        }
        else
        {
            pendingAction = clickedAction;
            DisplayActionPreview(clickedAction, run);
            UpdateActionButtons(run);
        }
    }

    private void InitializeBackgroundReset()
    {
        backgroundImage.raycastTarget = true;
        if (backgroundImage.GetComponent<GraphicRaycaster>() == null)
        {
            backgroundImage.gameObject.AddComponent<GraphicRaycaster>();
        }

        backgroundEventTrigger = backgroundImage.GetComponent<EventTrigger>();
        if (backgroundEventTrigger == null)
        {
            backgroundEventTrigger = backgroundImage.gameObject.AddComponent<EventTrigger>();
        }

        EventTrigger.Entry entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        entry.callback.AddListener(HandleBackgroundPointerClick);
        backgroundEventTrigger.triggers.Add(entry);
    }

    private void InitializeTraineeDialogue()
    {
        traineeDialogueText = Instantiate(traineeNameText, traineeImage.transform, false);
        traineeDialogueText.name = "TraineeDialogue";
        traineeDialogueText.text = string.Empty;
        traineeDialogueText.raycastTarget = false;
        traineeDialogueText.richText = false;
        traineeDialogueText.alignment = TextAlignmentOptions.TopLeft;
        traineeDialogueText.fontSize = 24;
        RectTransform rect = traineeDialogueText.rectTransform;
        bool mirrored = traineeImage.rectTransform.localScale.x < 0;
        rect.anchorMin = rect.anchorMax = new Vector2(mirrored ? 0 : 1, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(mirrored ? -12 : 12, -20);
        rect.sizeDelta = new Vector2(300, 120);
        rect.localScale = new Vector3(mirrored ? -1 : 1, 1, 1);

        traineeImage.raycastTarget = true;
        traineeEventTrigger = traineeImage.GetComponent<EventTrigger>();
        if (traineeEventTrigger == null)
        {
            traineeEventTrigger = traineeImage.gameObject.AddComponent<EventTrigger>();
        }
        traineeClickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        traineeClickEntry.callback.AddListener(HandleTraineeClick);
        traineeEventTrigger.triggers.Add(traineeClickEntry);
    }

    private void HandleTraineeClick(BaseEventData eventData)
    {
        if (!(eventData is PointerEventData pointer) || pointer.button != PointerEventData.InputButton.Left)
        {
            return;
        }
        if (GameClient.Instance == null || GameClient.Instance.CurrentTrainingRun == null)
        {
            return;
        }
        string[] texts = GameClient.Instance.TrainingTexts;
        if (texts.Length == 0)
        {
            return;
        }

        int index;
        if (texts.Length > 1 && lastDialogueIndex >= 0 && lastDialogueIndex < texts.Length)
        {
            index = UnityEngine.Random.Range(0, texts.Length - 1);
            if (index >= lastDialogueIndex)
            {
                index++;
            }
        }
        else
        {
            index = UnityEngine.Random.Range(0, texts.Length);
        }
        lastDialogueIndex = index;
        traineeDialogueText.text = texts[index];
        dialogueExpiresAt = Time.unscaledTime + 3f;
    }

    private void Update()
    {
        if (traineeDialogueText != null && traineeDialogueText.text.Length > 0
            && Time.unscaledTime >= dialogueExpiresAt)
        {
            traineeDialogueText.text = string.Empty;
        }
    }

    private void HandleBackgroundPointerClick(BaseEventData eventData)
    {
        if (isProcessing || pendingAction == ActionType.None || !(eventData is PointerEventData))
        {
            return;
        }

        pendingAction = ActionType.None;
        actionPreviewText.text = "Select a facility or rest to preview stat gains.";
        TrainingRunData run = GameClient.Instance != null ? GameClient.Instance.CurrentTrainingRun : null;
        UpdateActionButtons(run);
    }

    private void OnEndTrainingButtonClicked()
    {
        if (isProcessing)
        {
            return;
        }

        TrainingRunData run = GameClient.Instance != null ? GameClient.Instance.CurrentTrainingRun : null;
        if (run == null)
        {
            ShowMessage("A training run is required to end training.", isError: true);
            return;
        }

        SetBusy(true);
        if (GameClient.Instance != null)
        {
            GameClient.Instance.EndAndResetTraining(() =>
            {
                SetBusy(false);
                GameClient.Instance.UnloadTrainingScene();
            }, error =>
            {
                SetBusy(false);
                ShowMessage($"Failed to end training: {error}", isError: true);
            });
        }
        else
        {
            SetBusy(false);
            ShowMessage("Unable to end training because the game client is unavailable.", isError: true);
        }
    }

    private void OnSuspendTrainingButtonClicked()
    {
        if (isProcessing)
        {
            return;
        }

        if (GameClient.Instance != null)
        {
            GameClient.Instance.UnloadTrainingScene();
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(GameClient.TrainingSceneName);
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
            ? $"<color=red>Failure Chance: {preview.failure_rate:F0}%</color>"
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
                    bool isRainbow = card.facility?.ToLowerInvariant() == facName.ToLowerInvariant() &&
                                     card.IsRainbowEligible;
                    string tag = isRainbow
                        ? " <color=#00FFFF>[Rainbow]</color>"
                         : "";
                    presentCardsList.Add(
                        $"{card.title} ({card.facility?.ToUpperInvariant().Substring(0,3)} | {card.bond}%){tag}");
                }
            }
        }

        string cardsInfo = presentCardsList.Count > 0
            ? $"• {string.Join(",\n• ", presentCardsList)}"
            : "";

        actionPreviewText.text = $"<b>[TRAIN {facName.ToUpperInvariant()} - LVL {preview.level}]</b>{rainbowInfo}\n" +
                                 $"<b>+{preview.main_gain} {preview.main_stat.ToUpperInvariant()}</b> | " +
                                 $"<b>+{preview.secondary_gain} {preview.secondary_stat.ToUpperInvariant()}</b> | " +
                                 $"<b>+{preview.skill_points_gain} SP</b>\n" +
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
                    string rainbowTag = response.action_result.rainbow_triggered
                        ? " <color=#00FFFF>[Rainbow!]</color>"
                        : "";
                    string energyTag = response.action_result.energy_recovered > 0
                        ? $" (Recovered +{response.action_result.energy_recovered} Energy)"
                        : "";
                    string spTag = response.action_result.skill_points_gain > 0
                        ? $", +{response.action_result.skill_points_gain} SP"
                        : "";
                    ShowMessage(
                        $"Training Successful!{rainbowTag}{energyTag} +{response.action_result.main_gain} {response.action_result.main_stat.ToUpperInvariant()}, +{response.action_result.secondary_gain} {response.action_result.secondary_stat.ToUpperInvariant()}{spTag}");
                }
                else
                {
                    ShowMessage(
                        $"<color=red>Training Failed! (Failure rate was {response.action_result.failure_rate:F1}%)</color> No stats gained.",
                        isError: true);
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
        UpdateActionButtons(run);
        string characterId = run?.trainee?.id;
        if (dialogueCharacterId != characterId)
        {
            dialogueCharacterId = characterId;
            lastDialogueIndex = -1;
            traineeDialogueText.text = string.Empty;
        }
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
            float failRate = run.energy < (run.max_energy * 0.5f)
                ? (50f - ((float)run.energy / run.max_energy) * 100f) * 2f
                : 0f;
            string energyStatus = failRate > 0 ? $" <color=red>(Fail Rate: {failRate:F0}%)</color>" : "";
            energyText.text = $"Energy: {run.energy}/{run.max_energy}{energyStatus}";
        }

        // Trainee info & stats
        if (run.trainee != null)
        {
            if (traineeNameText != null)
            {
                traineeNameText.text = $"{run.trainee.name} [{run.trainee.StarsDisplay}]\n<{run.trainee.title}>";
            }
            
            traineeImage.sprite = Resources.Load<Sprite>($"Images/Characters/{run.trainee.id}");

            if (traineeStatsText != null)
            {
                Traits t = run.trainee.traits;
                Traits g = run.trainee.growth;
                int sp = run.trainee.skill_points > 0 ? run.trainee.skill_points : run.skill_points;
                traineeStatsText.text = $"<sprite=\"power\" name=\"power\"> <b>Power:</b> {t.power} ({g.power:+0;-0;+0}%)\n" +
                                        $"<sprite=\"agility\" name=\"agility\"> <b>Agility:</b> {t.agility} ({g.agility:+0;-0;+0}%)\n" +
                                        $"<sprite=\"impact\" name=\"impact\"> <b>Impact:</b> {t.impact} ({g.impact:+0;-0;+0}%)\n" +
                                        $"<sprite=\"defense\" name=\"defense\"> <b>Defense:</b> {t.defense} ({g.defense:+0;-0;+0}%)\n" +
                                        $"<sprite=\"finesse\" name=\"finesse\"> <b>Finesse:</b> {t.finesse} ({g.finesse:+0;-0;+0}%)\n" +
                                        $"<b>Total Stats:</b> {t.Total}\n" + 
                                        $"<b>Skill Points:</b> {sp} SP";
            }
        }

        // Facility level texts
        UpdateFacilityLevelTextAndSupportCardPreview(powerLevelText, "POW", run.facility_levels?.power ?? 1,
            run.facility_previews.power, run.support_cards);
        UpdateFacilityLevelTextAndSupportCardPreview(agilityLevelText, "AGI", run.facility_levels?.agility ?? 1,
            run.facility_previews.agility, run.support_cards);
        UpdateFacilityLevelTextAndSupportCardPreview(impactLevelText, "IMP", run.facility_levels?.impact ?? 1,
            run.facility_previews.impact, run.support_cards);
        UpdateFacilityLevelTextAndSupportCardPreview(defenseLevelText, "DEF", run.facility_levels?.defense ?? 1,
            run.facility_previews.defense, run.support_cards);
        UpdateFacilityLevelTextAndSupportCardPreview(finesseLevelText, "FIN", run.facility_levels?.finesse ?? 1,
            run.facility_previews.finesse, run.support_cards);

        // Support cards display
        if (supportCardsText != null && run.support_cards != null)
        {
            string cardsSummary = "<b>Support Team:</b>\n";
            foreach (var card in run.support_cards)
            {
                string loc = FindCardLocation(card.id, run.facility_previews);
                bool isRainbow = loc.Equals(card.facility, StringComparison.OrdinalIgnoreCase) &&
                                 card.IsRainbowEligible;
                string rainbowTag = isRainbow
                    ? " <color=#00FFFF>[Rainbow Active!]</color>"
                    : (card.IsRainbowEligible ? " [Rainbow Ready]" : "");
                string facility = card.facility?.Trim().ToLowerInvariant();
                cardsSummary +=
                    $"<sprite=\"{facility}\" name=\"{facility}\"> [{card.facility?.ToUpperInvariant()} | {Tiers.ShortName(card.tier)}]\n  {card.title}\n  Bond: {card.bond}% | At: {loc.ToUpperInvariant()}{rainbowTag}\n\n";
            }

            supportCardsText.text = cardsSummary.TrimEnd();
        }

        if (run.IsCompleted)
        {
            pendingAction = ActionType.None;
            actionPreviewText.text =
                "<b><color=green>Training completed! Visit the skill shop or end training when ready.</color></b>";
        }
        else if (pendingAction == ActionType.None)
        {
            actionPreviewText.text = "Select a facility or rest to preview stat gains.";
        }
    }

    private static string FindCardLocation(string cardId, FacilityPreviewMap previews)
    {
        if (previews == null) return "Unknown";
        if (previews.power?.present_card_ids != null &&
            Array.IndexOf(previews.power.present_card_ids, cardId) >= 0) return "Power";
        if (previews.agility?.present_card_ids != null && Array.IndexOf(previews.agility.present_card_ids, cardId) >= 0)
            return "Agility";
        if (previews.impact?.present_card_ids != null && Array.IndexOf(previews.impact.present_card_ids, cardId) >= 0)
            return "Impact";
        if (previews.defense?.present_card_ids != null && Array.IndexOf(previews.defense.present_card_ids, cardId) >= 0)
            return "Defense";
        if (previews.finesse?.present_card_ids != null && Array.IndexOf(previews.finesse.present_card_ids, cardId) >= 0)
            return "Finesse";
        return "None";
    }

    private void UpdateFacilityLevelTextAndSupportCardPreview(TMP_Text textComponent, string prefix, int level,
        FacilityPreview facilityPreview, SupportCardData[] runSupportCards)
    {
        if (textComponent != null)
        {
            textComponent.text = $"{prefix}\nLv.{level}";
        }

        int maxPreview = facilityPreview.present_card_ids.Length;
        int index = 0;
        foreach (Image scPreview in textComponent.transform.parent.GetComponentsInChildren<Image>(true))
        {
            if (scPreview.transform == textComponent.transform.parent)
            {
                if (facilityPreview.rainbow_active)
                {
                    scPreview.sprite = cardRainbowPreviewImage;
                }
                else if(facilityPreview.present_card_ids.Length == 0)
                {
                    scPreview.sprite = cardEmptyPreviewImage;
                }
                else
                {
                    scPreview.sprite = cardPreviewImage;
                }
                continue;
            }
            scPreview.gameObject.SetActive(false);
            if (maxPreview-- > 0)
            {
                scPreview.gameObject.SetActive(true);
                string cardId = facilityPreview.present_card_ids[index++];
                foreach (SupportCardData card in runSupportCards)
                {
                    if (card.id == cardId)
                    {
                        if (card.IsRainbowEligible && facilityPreview.facility.Equals(card.facility))
                        {
                            scPreview.sprite = cardRainbowPreviewImage;
                        }
                        else
                        {
                            scPreview.sprite = cardPreviewImage;
                        }
                        break;
                    }
                }
            }
        }
    }

    private void SetBusy(bool busy)
    {
        isProcessing = busy;
        TrainingRunData run = GameClient.Instance != null ? GameClient.Instance.CurrentTrainingRun : null;
        UpdateActionButtons(run);
    }

    private void UpdateActionButtons(TrainingRunData run)
    {
        bool canTrain = !isProcessing && run != null && !run.IsCompleted;
        UpdateActionButton(powerFacilityButton, ActionType.TrainPower, canTrain);
        UpdateActionButton(agilityFacilityButton, ActionType.TrainAgility, canTrain);
        UpdateActionButton(impactFacilityButton, ActionType.TrainImpact, canTrain);
        UpdateActionButton(defenseFacilityButton, ActionType.TrainDefense, canTrain);
        UpdateActionButton(finesseFacilityButton, ActionType.TrainFinesse, canTrain);
        UpdateActionButton(restButton, ActionType.Rest, canTrain && run.energy < run.max_energy);
        endTrainingButton.interactable = !isProcessing && run != null;
        skillShopButton.interactable = !isProcessing && run != null;
    }

    private void UpdateActionButton(Button button, ActionType action, bool canTrain)
    {
        button.interactable = canTrain;
        CanvasGroup canvasGroup = button.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = button.gameObject.AddComponent<CanvasGroup>();
        }

        canvasGroup.alpha = button.interactable
                            && pendingAction != ActionType.None
                            && pendingAction != action
            ? 0.7f
            : 1f;
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