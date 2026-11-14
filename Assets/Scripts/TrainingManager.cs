using System;
using System.Collections;
using System.Collections.Generic;
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
    private Image energyPreview;

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

    private const float EventFadeSeconds = 0.2f;
    private const float EventFadeInSeconds = 0.6f;
    private const float EventClickDelaySeconds = 0.5f;
    private const float EventFirstClickDelaySeconds = 2f;
    private const float EventFloatSeconds = 1.2f;
    private const float EventFloatStaggerSeconds = 0.3f;
    private const float EventFloatDistance = 150f;
    private static readonly Color OverlayBackgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f);
    private const float ActionOverlayHoldSeconds = 0.25f;

    private GameObject actionOverlay;
    private GameObject actionBackdrop;
    private bool actionRequestDone;
    private bool actionFailed;
    private float actionFloatsEndAt;

    private bool eventFlowActive;
    private bool eventFinishing;
    private GameObject eventOverlay;
    private CanvasGroup eventOverlayGroup;
    private TMP_Text eventTitleText;
    private TMP_Text eventMessageText;
    private RectTransform eventTrainee;
    private RectTransform eventSupport;
    private readonly List<Button> eventChoiceButtons = new List<Button>();
    private float eventImageHeight;
    private TrainingEventData currentEvent;
    private int eventLineIndex;
    private float eventClickableAt;

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
        if (eventOverlay != null)
        {
            Destroy(eventOverlay);
        }
        if (actionBackdrop != null)
        {
            Destroy(actionBackdrop);
        }
        if (actionOverlay != null)
        {
            Destroy(actionOverlay);
        }
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
            RefreshEnergyPreview(run);
            ExecuteAction(clickedAction);
        }
        else
        {
            pendingAction = clickedAction;
            DisplayActionPreview(clickedAction, run);
            RefreshEnergyPreview(run);
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
        rect.anchoredPosition = new Vector2(mirrored ? -10 : 10, -20);
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
        dialogueExpiresAt = Time.unscaledTime + 5f;
    }

    private void Update()
    {
        if (traineeDialogueText != null && traineeDialogueText.text.Length > 0
            && Time.unscaledTime >= dialogueExpiresAt)
        {
            traineeDialogueText.text = string.Empty;
        }
        foreach (Button button in eventChoiceButtons)
        {
            if (button != null)
            {
                button.interactable = !eventFinishing && Time.unscaledTime >= eventClickableAt;
            }
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
        RefreshEnergyPreview(run);
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

    /// <summary>Shows the energy the pending action would use (red) or restore (green) on the energy bar.</summary>
    private void RefreshEnergyPreview(TrainingRunData run)
    {
        if (energySlider == null || energySlider.fillRect == null)
        {
            return;
        }

        if (energyPreview == null)
        {
            var previewObject = new GameObject("EnergyPreview", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            previewObject.transform.SetParent(energySlider.fillRect.parent, false);
            RectTransform previewRect = (RectTransform)previewObject.transform;
            previewRect.offsetMin = previewRect.offsetMax = Vector2.zero;
            energyPreview = previewObject.GetComponent<Image>();
            energyPreview.raycastTarget = false;
        }

        int amount = 0;
        if (run != null && run.max_energy > 0 && pendingAction != ActionType.None)
        {
            if (pendingAction == ActionType.Rest)
            {
                amount = Mathf.Min(50, run.max_energy - run.energy);
            }
            else
            {
                FacilityPreview preview = run.facility_previews?.GetPreview(ActionToFacilityName(pendingAction));
                if (preview != null)
                {
                    amount = preview.energy_restore > 0 ? preview.energy_restore : -preview.energy_cost;
                }
            }
        }

        energyPreview.gameObject.SetActive(amount != 0);
        if (amount == 0)
        {
            return;
        }

        float current = (float)run.energy / run.max_energy;
        float other = Mathf.Clamp01((float)(run.energy + amount) / run.max_energy);
        RectTransform rect = energyPreview.rectTransform;
        rect.anchorMin = new Vector2(Mathf.Min(current, other), 0f);
        rect.anchorMax = new Vector2(Mathf.Max(current, other), 1f);
        energyPreview.color = amount > 0 ? new Color(0.6f, 1f, 0.7f, 0.85f) : new Color(1f, 0.3f, 0.25f, 0.9f);
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
        actionRequestDone = false;
        actionFloatsEndAt = 0f;
        CreateBackdrop();
        StartCoroutine(PlayActionOverlay(action));

        if (action == ActionType.Rest)
        {
            GameClient.Instance.RestTraining(response =>
            {
                SetBusy(false);
                actionRequestDone = true;
                if (response.action_result != null)
                {
                    if (actionOverlay != null && response.action_result.energy_recovered > 0)
                    {
                        StartCoroutine(FloatOverlayText(actionOverlay.transform,
                            $"<color=#4CFF6A>{response.action_result.energy_recovered}</color>", Vector2.zero, 0f));
                        actionFloatsEndAt = Time.unscaledTime + EventFloatSeconds;
                    }
                    ShowMessage($"Rested! Recovered {response.action_result.energy_recovered} Energy.");
                }

                UpdateUI(response.run);
            }, error =>
            {
                SetBusy(false);
                actionRequestDone = true;
                ShowMessage($"Rest failed: {error}", isError: true);
            });
            return;
        }

        string facName = ActionToFacilityName(action);
        GameClient.Instance.TrainFacility(facName, response =>
        {
            SetBusy(false);
            actionRequestDone = true;
            SpawnTrainingGainTexts(response.action_result);
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
            actionRequestDone = true;
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
            Image fillImage = energySlider.fillRect != null ? energySlider.fillRect.GetComponent<Image>() : null;
            if (fillImage != null)
            {
                float ratio = run.max_energy > 0 ? (float)run.energy / run.max_energy : 0f;
                fillImage.color = ratio >= 0.5f
                    ? Color.Lerp(new Color(0.95f, 0.8f, 0.2f), new Color(0.3f, 0.85f, 0.4f), (ratio - 0.5f) * 2f)
                    : Color.Lerp(new Color(0.9f, 0.25f, 0.2f), new Color(0.95f, 0.8f, 0.2f), ratio * 2f);
            }

            Image backgroundImage = energySlider.transform.Find("Background")?.GetComponent<Image>();
            if (backgroundImage != null)
            {
                backgroundImage.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            }
        }

        RefreshEnergyPreview(run);

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

        RefreshSupportCardsText(run);

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

        if (run.HasPendingEvent && !eventFlowActive)
        {
            BeginPendingEvent();
        }
    }

    private void BeginPendingEvent()
    {
        eventFlowActive = true;
        GameClient.Instance.FetchTrainingEvent(view =>
        {
            if (view.text == null || view.text.Length == 0)
            {
                eventFlowActive = false;
                ShowMessage("The event page has no text.", isError: true);
                return;
            }

            ShowEventOverlay(view);
        }, error =>
        {
            eventFlowActive = false;
            ShowMessage($"Failed to load event: {error}", isError: true);
        });
    }

    private void ShowEventOverlay(TrainingEventData view)
    {
        currentEvent = view;
        eventLineIndex = 0;
        eventFinishing = false;
        eventClickableAt = float.MaxValue;

        Transform canvasTransform = traineeImage.canvas.transform;
        Rect canvasRect = ((RectTransform)canvasTransform).rect;

        eventOverlay = CreateOverlayRoot("EventOverlay", out eventOverlayGroup);
        EventTrigger.Entry clickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        clickEntry.callback.AddListener(HandleEventClick);
        eventOverlay.AddComponent<EventTrigger>().triggers.Add(clickEntry);

        eventImageHeight = canvasRect.height * 0.5f;
        Sprite supportSprite = FindSupportCardEventSprite(view.event_id);
        RectTransform characterRect;
        if (supportSprite != null)
        {
            // Support card events: the card's character faces the trainee, as if teaching them.
            characterRect = CreateOverlayCharacter(eventOverlay.transform, canvasRect, null,
                -canvasRect.width * 0.2f, true, 0.3f);
            eventSupport = CreateOverlayCharacter(eventOverlay.transform, canvasRect, supportSprite,
                canvasRect.width * 0.2f, false, 0.3f);
        }
        else
        {
            characterRect = CreateOverlayCharacter(eventOverlay.transform, canvasRect);
            eventSupport = null;
        }

        eventTrainee = characterRect;
        if (characterRect != null)
        {
            eventImageHeight = characterRect.sizeDelta.y;
        }

        eventTitleText = CreateEventText("EventTitle", 48, TextAlignmentOptions.Bottom,
            new Vector2(0.5f, 0f), new Vector2(0f, eventImageHeight * 0.5f + 60f), new Vector2(1000f, 90f));
        eventTitleText.fontStyle = FontStyles.Bold;
        eventTitleText.text = view.name;
        eventMessageText = CreateEventText("EventMessage", 32, TextAlignmentOptions.Top,
            new Vector2(0.5f, 1f), new Vector2(0f, -eventImageHeight * 0.5f - 30f), new Vector2(1000f, 300f));
        eventMessageText.text = HasEventAnimation(view, 0) ? "" : view.text[0];

        StartCoroutine(FadeInEventOverlayWhenReady());
    }

    private Sprite FindSupportCardEventSprite(string eventId)
    {
        int marker = eventId == null ? -1 : eventId.IndexOf("_ev_", StringComparison.Ordinal);
        string ownerId = marker > 0 ? eventId.Substring(0, marker) : null;
        if (ownerId == null || !ownerId.StartsWith("sc_"))
        {
            return null;
        }

        SupportCardData[] cards = GameClient.Instance?.CurrentTrainingRun?.support_cards;
        if (cards == null)
        {
            return null;
        }

        foreach (SupportCardData card in cards)
        {
            if (card != null && card.id == ownerId && !string.IsNullOrEmpty(card.character_id))
            {
                return Resources.Load<Sprite>($"Images/Characters/{card.character_id}");
            }
        }

        return null;
    }

    private IEnumerator FadeInEventOverlayWhenReady()
    {
        // Wait until the training action overlay is completely gone, then fade in slowly.
        while (actionOverlay != null)
        {
            yield return null;
        }

        yield return FadeOverlay(eventOverlayGroup, 0f, 1f, EventFadeInSeconds);
        yield return DisplayEventLine(0, EventFirstClickDelaySeconds);
    }

    private GameObject CreateOverlayRoot(string objectName, out CanvasGroup group)
    {
        Transform canvasTransform = traineeImage.canvas.transform;
        GameObject root = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(CanvasGroup));
        root.transform.SetParent(canvasTransform, false);
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        root.GetComponent<CanvasRenderer>().cullTransparentMesh = false;
        Image blocker = root.GetComponent<Image>();
        blocker.color = OverlayBackgroundColor;
        blocker.raycastTarget = true;
        group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        root.transform.SetAsLastSibling();
        return root;
    }

    /// <summary>Spawns a character mirrored to face right (or left); null when there is no sprite.</summary>
    private RectTransform CreateOverlayCharacter(Transform parent, Rect canvasRect, Sprite sprite = null,
        float xOffset = 0f, bool faceRight = true, float widthFraction = 0.5f)
    {
        sprite = sprite != null ? sprite : traineeImage.sprite;
        if (sprite == null)
        {
            return null;
        }

        float scale = Mathf.Min(canvasRect.width * widthFraction / sprite.rect.width,
            canvasRect.height * 0.5f / sprite.rect.height);
        GameObject imageObject = new GameObject("OverlayCharacter", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image));
        imageObject.transform.SetParent(parent, false);
        RectTransform imageRect = (RectTransform)imageObject.transform;
        imageRect.anchorMin = imageRect.anchorMax = imageRect.pivot = new Vector2(0.5f, 0.5f);
        imageRect.anchoredPosition = new Vector2(xOffset, 0f);
        imageRect.sizeDelta = sprite.rect.size * scale;
        imageRect.localScale = new Vector3(faceRight ? -1f : 1f, 1f, 1f);
        Image characterImage = imageObject.GetComponent<Image>();
        characterImage.sprite = sprite;
        characterImage.preserveAspect = true;
        characterImage.raycastTarget = false;
        return imageRect;
    }

    private IEnumerator PlayActionOverlay(ActionType action)
    {
        actionFailed = false;
        actionOverlay = CreateOverlayRoot("ActionOverlay", out CanvasGroup group);
        Rect canvasRect = ((RectTransform)traineeImage.canvas.transform).rect;
        RectTransform character = CreateOverlayCharacter(actionOverlay.transform, canvasRect);
        yield return FadeOverlay(group, 0f, 1f);

        // Training animations depend on the outcome, so wait for the response (rest cannot fail).
        while (action != ActionType.Rest && !actionRequestDone)
        {
            yield return null;
        }

        if (character != null)
        {
            float distance = canvasRect.width * 0.12f;
            if (actionFailed)
            {
                yield return PlayFailureAnimation(character, distance, actionOverlay.transform);
            }
            else
            {
                yield return PlayActionAnimation(action, character, distance);
            }
        }

        yield return new WaitForSecondsRealtime(ActionOverlayHoldSeconds);
        while (!actionRequestDone)
        {
            yield return null;
        }

        while (Time.unscaledTime < actionFloatsEndAt)
        {
            yield return null;
        }

        // Wait for the event (if the new round has one) to be loaded so it can fade in at the same time.
        while (eventFlowActive && eventOverlay == null)
        {
            yield return null;
        }

        if (!eventFlowActive)
        {
            FadeOutBackdrop();
        }

        yield return FadeOverlay(group, 1f, 0f);
        Destroy(actionOverlay);
        actionOverlay = null;
    }

    private IEnumerator PlayActionAnimation(ActionType action, RectTransform character, float distance)
    {
        OverlayImageAnimations.Animation animation;
        switch (action)
        {
            case ActionType.TrainPower:
                animation = OverlayImageAnimations.Animation.Power;
                break;
            case ActionType.TrainAgility:
                animation = OverlayImageAnimations.Animation.Agility;
                break;
            case ActionType.TrainImpact:
                animation = OverlayImageAnimations.Animation.Impact;
                break;
            case ActionType.TrainDefense:
                animation = OverlayImageAnimations.Animation.Defense;
                break;
            case ActionType.TrainFinesse:
                animation = OverlayImageAnimations.Animation.Finesse;
                break;
            case ActionType.Rest:
                animation = OverlayImageAnimations.Animation.Rest;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "No animation for action");
        }
        yield return OverlayImageAnimations.Play(character, animation, distance);
    }

    private IEnumerator PlayFailureAnimation(RectTransform character, float distance, Transform overlay)
    {
        yield return OverlayImageAnimations.Play(character, OverlayImageAnimations.Animation.Failure, distance, () =>
        {
            StartCoroutine(FloatOverlayText(overlay, "<color=#FF4C4C>Failure</color>",
                new Vector2(0f, 120f), 0.1f, 96));
            actionFloatsEndAt = Time.unscaledTime + 0.1f + EventFloatSeconds;
        });
    }

    private TMP_Text CreateEventText(string objectName, float fontSize, TextAlignmentOptions alignment,
        Vector2 pivot, Vector2 position, Vector2 size)
    {
        return CreateEventText(eventOverlay.transform, objectName, fontSize, alignment, pivot, position, size);
    }

    private TMP_Text CreateEventText(Transform parent, string objectName, float fontSize,
        TextAlignmentOptions alignment, Vector2 pivot, Vector2 position, Vector2 size)
    {
        TMP_Text text = Instantiate(traineeNameText, parent, false);
        text.name = objectName;
        text.raycastTarget = false;
        text.richText = true;
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.alignment = alignment;
        text.fontSize = fontSize;
        text.color = Color.white;
        RectTransform rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        return text;
    }

    private void HandleEventClick(BaseEventData eventData)
    {
        if (!(eventData is PointerEventData pointer) || pointer.button != PointerEventData.InputButton.Left
            || eventFinishing || Time.unscaledTime < eventClickableAt)
        {
            return;
        }

        if (eventLineIndex < currentEvent.text.Length - 1)
        {
            StartCoroutine(DisplayEventLine(eventLineIndex + 1, EventClickDelaySeconds));
            return;
        }

        if (currentEvent.choices.Length > 0)
        {
            return;
        }

        if (currentEvent.pages_remaining > 0)
        {
            AdvanceEventPage(null);
        }
        else
        {
            StartCoroutine(FinishEvent(null));
        }
    }

    private static bool HasEventAnimation(TrainingEventData view, int line)
    {
        return view.animations != null && Array.Exists(view.animations, cue => cue.before_line == line);
    }

    private IEnumerator DisplayEventLine(int line, float delay)
    {
        eventFinishing = true;
        eventClickableAt = float.MaxValue;
        ClearEventChoices();
        eventLineIndex = line;
        if (HasEventAnimation(currentEvent, line))
        {
            eventMessageText.text = "";
            foreach (EventAnimationData cue in currentEvent.animations)
            {
                if (cue.before_line != line)
                {
                    continue;
                }

                RectTransform actor = cue.actor == "trainee" ? eventTrainee : eventSupport;
                if (actor == null || !Enum.TryParse(cue.animation, true, out OverlayImageAnimations.Animation animation))
                {
                    Debug.LogError($"Cannot play event animation '{cue.animation}' for '{cue.actor}'.", this);
                    continue;
                }

                Vector2 position = actor.anchoredPosition;
                Quaternion rotation = actor.localRotation;
                Vector3 scale = actor.localScale;
                float distance = ((RectTransform)traineeImage.canvas.transform).rect.width * 0.12f;
                yield return OverlayImageAnimations.Play(actor, animation, distance);
                actor.anchoredPosition = position;
                actor.localRotation = rotation;
                actor.localScale = scale;
            }
        }

        eventMessageText.text = currentEvent.text[line];
        if (line == currentEvent.text.Length - 1)
        {
            CreateEventChoices();
        }
        eventClickableAt = Time.unscaledTime + delay;
        eventFinishing = false;
    }

    private void CreateEventChoices()
    {
        float width = Mathf.Min(850f, ((RectTransform)traineeImage.canvas.transform).rect.width - 40f);
        for (int i = 0; i < currentEvent.choices.Length; i++)
        {
            EventChoiceData choice = currentEvent.choices[i];
            GameObject item = new GameObject("EventChoice", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            item.transform.SetParent(eventOverlay.transform, false);
            RectTransform rect = (RectTransform)item.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 30f + (currentEvent.choices.Length - 1 - i) * 54f);
            rect.sizeDelta = new Vector2(width, 48f);
            Image image = item.GetComponent<Image>();
            image.color = new Color(0.38f, 0.38f, 0.38f, 1f);
            Button button = item.GetComponent<Button>();
            button.targetGraphic = image;
            button.interactable = false;
            button.onClick.AddListener(() => SelectEventChoice(choice.id));
            eventChoiceButtons.Add(button);

            TMP_Text text = CreateEventText(item.transform, "ChoiceText", 26, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width - 20f, 44f));
            text.text = choice.text;
        }
    }

    private void ClearEventChoices()
    {
        foreach (Button button in eventChoiceButtons)
        {
            if (button != null)
            {
                button.gameObject.SetActive(false);
                Destroy(button.gameObject);
            }
        }
        eventChoiceButtons.Clear();
    }

    private void SelectEventChoice(string choiceId)
    {
        if (eventFinishing || Time.unscaledTime < eventClickableAt)
        {
            return;
        }
        if (currentEvent.pages_remaining > 0)
        {
            AdvanceEventPage(choiceId);
        }
        else
        {
            StartCoroutine(FinishEvent(choiceId));
        }
    }

    private void AdvanceEventPage(string choiceId)
    {
        eventFinishing = true;
        GameClient.Instance.AdvanceTrainingEvent(choiceId, response =>
        {
            if (response.finished || response.@event == null || response.@event.text == null
                || response.@event.text.Length == 0)
            {
                ShowMessage("Invalid event page response from server.", isError: true);
                eventFinishing = false;
                return;
            }
            currentEvent = response.@event;
            StartCoroutine(DisplayEventLine(0, EventClickDelaySeconds));
        }, error =>
        {
            ShowMessage($"Failed to advance event: {error}", isError: true);
            GameClient.Instance.FetchTrainingEvent(view =>
            {
                currentEvent = view;
                StartCoroutine(DisplayEventLine(0, EventClickDelaySeconds));
            }, refreshError =>
            {
                ShowMessage($"Failed to reload event: {refreshError}", isError: true);
                eventClickableAt = Time.unscaledTime + EventClickDelaySeconds;
                eventFinishing = false;
            });
        });
    }

    private IEnumerator FinishEvent(string choiceId)
    {
        eventFinishing = true;
        ClearEventChoices();
        eventTitleText.gameObject.SetActive(false);
        eventMessageText.gameObject.SetActive(false);

        bool succeeded = false;
        bool failed = false;
        GameClient.Instance.AdvanceTrainingEvent(choiceId, response =>
        {
            if (response.finished)
            {
                succeeded = true;
            }
            else
            {
                failed = true;
                ShowMessage("The event did not finish as expected.", isError: true);
            }
        }, error =>
        {
            failed = true;
            ShowMessage($"Failed to finish event: {error}", isError: true);
        });

        EventEffectData[] effects = currentEvent.effects;
        for (int i = 0; i < effects.Length; i++)
        {
            if (effects[i].decision == null || effects[i].decision == choiceId
                || Array.IndexOf(currentEvent.selected_decisions, effects[i].decision) >= 0)
            {
                StartCoroutine(FloatEffectText(effects[i], i * EventFloatStaggerSeconds, -i * 45f));
            }
        }

        if (effects.Length > 0)
        {
            yield return new WaitForSecondsRealtime(
                (effects.Length - 1) * EventFloatStaggerSeconds + EventFloatSeconds);
        }

        while (!succeeded && !failed)
        {
            yield return null;
        }

        if (failed)
        {
            eventTitleText.gameObject.SetActive(true);
            eventMessageText.gameObject.SetActive(true);
            CreateEventChoices();
            eventClickableAt = Time.unscaledTime + EventClickDelaySeconds;
            eventFinishing = false;
            yield break;
        }

        FadeOutBackdrop();
        yield return FadeOverlay(eventOverlayGroup, 1f, 0f);
        Destroy(eventOverlay);
        eventOverlay = null;
        eventFlowActive = false;
    }

    // Plain black cover shown the instant an action starts; stays until the animation and any event are done.
    private void CreateBackdrop()
    {
        FadeOutBackdrop();
        actionBackdrop = CreateOverlayRoot("ActionBackdrop", out CanvasGroup group);
        group.alpha = 1f;
    }

    private void FadeOutBackdrop()
    {
        if (actionBackdrop != null)
        {
            GameObject backdrop = actionBackdrop;
            actionBackdrop = null;
            StartCoroutine(FadeAndDestroy(backdrop));
        }
    }

    private static IEnumerator FadeAndDestroy(GameObject target)
    {
        yield return FadeOverlay(target.GetComponent<CanvasGroup>(), 1f, 0f);
        Destroy(target);
    }

    private IEnumerator FloatEffectText(EventEffectData effect, float delay, float yOffset)
    {
        string sign = effect.amount > 0 ? "+" : "";
        string label;
        if (effect.type == "restore_energy")
        {
            string color = effect.amount > 0 ? "#3CDC50" : "#FF4C4C";
            label = $"<color={color}>{sign}{effect.amount} Energy</color>";
        }
        else if (effect.type == "modify_trait" && !string.IsNullOrEmpty(effect.trait))
        {
            string trait = effect.trait.ToLowerInvariant();
            string text = $"<sprite=\"{trait}\" name=\"{trait}\"> {sign}{effect.amount}";
            label = effect.amount > 0 ? text : $"<color=#FF4C4C>{text}</color>";
        }
        else if (effect.type == "unlock_skill" && !string.IsNullOrEmpty(effect.skill_name))
        {
            label = $"Unlocked: {effect.skill_name}";
        }
        else
        {
            label = $"{effect.type} {effect.amount}";
        }

        return FloatOverlayText(eventOverlay.transform, label, new Vector2(0f, yOffset), delay);
    }

    private IEnumerator FloatOverlayText(Transform parent, string label, Vector2 startPosition, float delay,
        int fontSize = 44)
    {
        if (delay > 0f)
        {
            yield return new WaitForSecondsRealtime(delay);
        }

        TMP_Text text = CreateEventText(parent, "FloatingText", fontSize, TextAlignmentOptions.Center,
            new Vector2(0.5f, 0.5f), startPosition, new Vector2(600f, 80f));
        text.fontStyle = FontStyles.Bold;
        text.text = label;

        for (float elapsed = 0f; elapsed < EventFloatSeconds; elapsed += Time.unscaledDeltaTime)
        {
            float progress = elapsed / EventFloatSeconds;
            text.rectTransform.anchoredPosition = startPosition + new Vector2(0f, EventFloatDistance * progress);
            text.alpha = 1f - progress * progress;
            yield return null;
        }

        Destroy(text.gameObject);
    }

    private void SpawnTrainingGainTexts(TrainingActionResult result)
    {
        if (actionOverlay == null || result == null)
        {
            return;
        }

        if (!result.success)
        {
            actionFailed = true;
            return;
        }

        var labels = new List<string>
        {
            $"<sprite=\"{result.main_stat.ToLowerInvariant()}\" name=\"{result.main_stat.ToLowerInvariant()}\"> {result.main_gain}",
            $"<sprite=\"{result.secondary_stat.ToLowerInvariant()}\" name=\"{result.secondary_stat.ToLowerInvariant()}\"> {result.secondary_gain}",
            $"<color=#FFD84A>{result.skill_points_gain} SP</color>"
        };
        if (result.energy_recovered > 0)
        {
            labels.Add($"<color=#4CFF6A>{result.energy_recovered}</color>");
        }
        else if (result.energy_consumed > 0)
        {
            labels.Add($"<color=#FF4C4C>-{result.energy_consumed}</color>");
        }

        // One slot per text (2x2 grid, shuffled) with random jitter inside it, so texts stay apart but positions vary.
        var slots = new List<Vector2>
        {
            new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(-1f, -1f), new Vector2(1f, -1f)
        };
        for (int i = slots.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (slots[i], slots[j]) = (slots[j], slots[i]);
        }

        for (int i = 0; i < labels.Count; i++)
        {
            var position = new Vector2(
                slots[i].x * UnityEngine.Random.Range(60f, 300f),
                slots[i].y * UnityEngine.Random.Range(20f, 150f));
            StartCoroutine(FloatOverlayText(actionOverlay.transform, labels[i], position,
                i * EventFloatStaggerSeconds * 0.5f));
        }

        actionFloatsEndAt = Time.unscaledTime + (labels.Count - 1) * EventFloatStaggerSeconds * 0.5f +
                            EventFloatSeconds;
    }

    private static IEnumerator FadeOverlay(CanvasGroup group, float from, float to, float seconds = EventFadeSeconds)
    {
        for (float elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, elapsed / seconds);
            yield return null;
        }

        group.alpha = to;
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

    private void RefreshSupportCardsText(TrainingRunData run)
    {
        if (supportCardsText == null || run == null || run.support_cards == null)
        {
            return;
        }

        string selectedFacility = ActionToFacilityName(pendingAction);
        string cardsSummary = "<b>Support Team:</b>\n";
        foreach (var card in run.support_cards)
        {
            string loc = FindCardLocation(card.id, run.facility_previews);
            if (selectedFacility.Length > 0
                && !loc.Equals(selectedFacility, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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

    private void UpdateActionButtons(TrainingRunData run)
    {
        RefreshSupportCardsText(run);
        bool canTrain = !isProcessing && run != null && !run.IsCompleted && !run.HasPendingEvent;
        UpdateActionButton(powerFacilityButton, ActionType.TrainPower, canTrain);
        UpdateActionButton(agilityFacilityButton, ActionType.TrainAgility, canTrain);
        UpdateActionButton(impactFacilityButton, ActionType.TrainImpact, canTrain);
        UpdateActionButton(defenseFacilityButton, ActionType.TrainDefense, canTrain);
        UpdateActionButton(finesseFacilityButton, ActionType.TrainFinesse, canTrain);
        UpdateActionButton(restButton, ActionType.Rest, canTrain && run.energy < run.max_energy);
        endTrainingButton.interactable = !isProcessing && run != null;
        skillShopButton.interactable = !isProcessing && run != null && !run.HasPendingEvent;
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