using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BattleTeamSelector : MonoBehaviour
{
    private const string DefaultTeamId = "default";
    private static readonly string[] Lanes = { "front", "close", "mid", "back" };
    private static readonly string[] Grades = { "F", "E", "D", "C", "B", "A", "S" };
    private static readonly Color PanelColor = new Color(0.97f, 0.97f, 0.97f, 1f);
    private static readonly Color ButtonColor = new Color(0.88f, 0.88f, 0.88f, 1f);

    private readonly string[] selectedIds = new string[8];
    private readonly Button[] slotButtons = new Button[8];
    private readonly TMP_Text[] slotLabels = new TMP_Text[8];
    private readonly Image[] slotPortraits = new Image[8];
    private readonly Dictionary<string, FinishedTraineeData> traineesById =
        new Dictionary<string, FinishedTraineeData>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> affinityModifiers =
        new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly List<FinishedTraineeData> sortedTrainees = new List<FinishedTraineeData>();
    private GameClient client;
    private RectTransform candidateContent;
    private GameObject candidatePopup;
    private TMP_Text candidateTitle;
    private ScrollRect candidateScroll;
    private TMP_Text statusText;
    private Button startButton;
    private Button clearButton;
    private Button backgroundButton;
    private int selectedSlot;
    private int generation;
    private bool ready;
    private bool saving;

    public static BattleTeamSelector Create(Transform parent, GameClient gameClient)
    {
        GameObject root = new GameObject("BattleTeamSelector", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(BattleTeamSelector));
        root.layer = parent.gameObject.layer;
        root.transform.SetParent(parent, false);
        BattleTeamSelector selector = root.GetComponent<BattleTeamSelector>();
        selector.client = gameClient;
        selector.BuildUi();
        root.SetActive(false);
        return selector;
    }

    public void Open()
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        int requestGeneration = ++generation;
        string playerId = client.CurrentPlayer.playerId;
        ready = false;
        saving = false;
        selectedSlot = -1;
        candidatePopup.SetActive(false);
        Array.Clear(selectedIds, 0, selectedIds.Length);
        traineesById.Clear();
        sortedTrainees.Clear();
        affinityModifiers.Clear();
        SetStatus("Loading finished trainees...");
        Refresh();
        client.FetchFinishedTrainees(trainees =>
        {
            if (!IsCurrent(requestGeneration, playerId)) return;
            if (!LoadTrainees(trainees)) return;
            client.FetchBattleTeams(response =>
            {
                if (!IsCurrent(requestGeneration, playerId)) return;
                if (!LoadModifiers(response.affinity_modifiers) || !LoadSavedTeam(response.teams)) return;
                ready = true;
                SetStatus(sortedTrainees.Count == 0
                    ? "Finish training characters to create a battle team."
                    : "Choose a slot to select a character. At least 3 are required.");
                Refresh();
            }, error => ShowLoadError(requestGeneration, playerId, error));
        }, error => ShowLoadError(requestGeneration, playerId, error));
    }

    private void OnDisable()
    {
        generation++;
    }

    private bool IsCurrent(int requestGeneration, string playerId)
    {
        return gameObject.activeInHierarchy && generation == requestGeneration && client.IsLoggedIn
            && client.CurrentPlayer.playerId == playerId;
    }

    private void ShowLoadError(int requestGeneration, string playerId, string error)
    {
        if (IsCurrent(requestGeneration, playerId))
        {
            ready = false;
            SetStatus("Could not load battle selection: " + error);
            Refresh();
        }
    }

    private bool LoadTrainees(FinishedTraineeData[] trainees)
    {
        foreach (FinishedTraineeData finished in trainees)
        {
            if (finished == null || string.IsNullOrWhiteSpace(finished.trainee_id) || finished.trainee == null
                || string.IsNullOrWhiteSpace(finished.trainee.id)
                || finished.score < 0 || traineesById.ContainsKey(finished.trainee_id))
            {
                SetStatus("Finished trainee data is incomplete or invalid.");
                Debug.LogError("Finished trainee data is incomplete or invalid.", this);
                return false;
            }
            if (!finished.has_score)
            {
                SetStatus("A finished trainee has no score. Restart the backend to backfill scores.");
                return false;
            }
            if (finished.trainee.affinity == null)
            {
                Debug.LogWarning($"Finished trainee {finished.trainee_id} has no affinity; battle uses F.", this);
                finished.trainee.affinity = new PositionAffinity();
            }
            PositionAffinity affinity = finished.trainee.affinity;
            affinity.front = NormalizeAffinity(affinity.front, finished.trainee_id);
            affinity.close = NormalizeAffinity(affinity.close, finished.trainee_id);
            affinity.mid = NormalizeAffinity(affinity.mid, finished.trainee_id);
            affinity.back = NormalizeAffinity(affinity.back, finished.trainee_id);
            traineesById.Add(finished.trainee_id, finished);
            sortedTrainees.Add(finished);
        }
        sortedTrainees.Sort((a, b) =>
        {
            int scoreOrder = b.score.CompareTo(a.score);
            return scoreOrder != 0 ? scoreOrder : string.Compare(a.trainee_id, b.trainee_id, StringComparison.Ordinal);
        });
        return true;
    }

    private string NormalizeAffinity(string grade, string traineeId)
    {
        string normalized = grade?.Trim().ToUpperInvariant() ?? "";
        if (Grades.Contains(normalized)) return normalized;
        Debug.LogWarning($"Finished trainee {traineeId} has invalid affinity '{grade}'; battle uses F.", this);
        return "F";
    }

    private bool LoadModifiers(BattleAffinityModifierData[] modifiers)
    {
        foreach (BattleAffinityModifierData modifier in modifiers)
        {
            if (modifier == null || !Grades.Contains(modifier.grade)
                || affinityModifiers.ContainsKey(modifier.grade))
            {
                SetStatus("Battle affinity modifiers are invalid.");
                Debug.LogError("Battle affinity modifiers are invalid.", this);
                return false;
            }
            affinityModifiers.Add(modifier.grade, modifier.percent);
        }
        if (affinityModifiers.Count != Grades.Length)
        {
            SetStatus("Battle affinity modifiers are incomplete.");
            Debug.LogError("Battle affinity modifiers are incomplete.", this);
            return false;
        }
        foreach (FinishedTraineeData finished in sortedTrainees)
        {
            foreach (string lane in Lanes)
            {
                if (!affinityModifiers.ContainsKey(AffinityFor(finished.trainee.affinity, lane)))
                {
                    SetStatus("A finished trainee has an invalid lane affinity.");
                    Debug.LogError($"Invalid affinity for finished trainee {finished.trainee_id}.", this);
                    return false;
                }
            }
        }
        return true;
    }

    private bool LoadSavedTeam(BattleTeamData[] teams)
    {
        BattleTeamData saved = null;
        foreach (BattleTeamData team in teams)
        {
            if (team == null || string.IsNullOrWhiteSpace(team.team_id))
            {
                SetStatus("Saved battle team data is invalid.");
                return false;
            }
            if (team.team_id == DefaultTeamId)
            {
                if (saved != null)
                {
                    SetStatus("Multiple default battle teams were returned.");
                    return false;
                }
                saved = team;
            }
        }
        if (saved == null) return true;
        if (saved.slots == null)
        {
            SetStatus("Saved battle team slots are missing.");
            return false;
        }
        foreach (BattleTeamSlotData slot in saved.slots)
        {
            int laneIndex = slot == null ? -1 : Array.IndexOf(Lanes, slot.lane);
            if (laneIndex < 0 || slot.slot < 0 || slot.slot > 1 || string.IsNullOrWhiteSpace(slot.trainee_id)
                || selectedIds[laneIndex * 2 + slot.slot] != null)
            {
                SetStatus("Saved battle team slots are invalid.");
                return false;
            }
            selectedIds[laneIndex * 2 + slot.slot] = slot.trainee_id;
        }
        return true;
    }

    private void Refresh()
    {
        for (int i = 0; i < selectedIds.Length; i++)
        {
            slotButtons[i].interactable = ready && !saving;
            string id = selectedIds[i];
            if (id == null)
            {
                slotLabels[i].text = "Select character";
                slotPortraits[i].gameObject.SetActive(false);
            }
            else if (!traineesById.TryGetValue(id, out FinishedTraineeData finished))
            {
                slotLabels[i].text = "<color=#802020>Missing trainee\nReplace or clear</color>";
                slotPortraits[i].gameObject.SetActive(false);
            }
            else
            {
                string lane = Lanes[i / 2];
                string affinity = AffinityFor(finished.trainee.affinity, lane);
                int modifier = affinityModifiers[affinity];
                string modifierText = modifier == 0 ? "" : modifier > 0
                    ? $" <color=#19501E>+{modifier}%</color>"
                    : $" <color=#802020>{modifier}%</color>";
                slotLabels[i].text = $"{finished.trainee.name}\n{FinishedTraineeRanking.GetRank(finished.score)}"
                    + $" ({finished.score})\n{char.ToUpperInvariant(lane[0])}{lane.Substring(1)} {affinity}{modifierText}";
                SetPortrait(slotPortraits[i], finished.trainee.id);
            }
        }
        clearButton.interactable = ready && !saving && selectedSlot >= 0 && selectedIds[selectedSlot] != null;
        backgroundButton.interactable = !saving;
        startButton.interactable = ready && !saving && IsTeamValid();
        RenderCandidates();
    }

    private bool IsTeamValid()
    {
        int count = 0;
        var characters = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in selectedIds)
        {
            if (id == null) continue;
            if (!traineesById.TryGetValue(id, out FinishedTraineeData trainee)
                || !characters.Add(trainee.trainee.id)) return false;
            count++;
        }
        return count >= 3;
    }

    private void RenderCandidates()
    {
        foreach (Transform child in candidateContent)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        if (!ready || !candidatePopup.activeSelf) return;
        foreach (FinishedTraineeData finished in sortedTrainees)
        {
            FinishedTraineeData candidate = finished;
            Button button = MakeButton(candidateContent, "Trainee_" + candidate.trainee_id);
            button.gameObject.AddComponent<LayoutElement>().preferredHeight = 82f;
            button.interactable = !saving;
            Image portrait = MakePortrait(button.transform, new Vector2(0.01f, 0.06f), new Vector2(0.15f, 0.94f));
            SetPortrait(portrait, candidate.trainee.id);
            TMP_Text text = MakeText(button.transform, new Vector2(0.17f, 0.04f), new Vector2(0.99f, 0.96f), 22);
            PositionAffinity affinity = candidate.trainee.affinity;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.text = $"{candidate.trainee.name}  |  {FinishedTraineeRanking.GetRank(candidate.score)}"
                + $" ({candidate.score})\nFront {affinity.front}   Close {affinity.close}"
                + $"   Mid {affinity.mid}   Back {affinity.back}";
            button.onClick.AddListener(() =>
            {
                SelectCandidate(candidate);
                candidatePopup.SetActive(false);
                SetStatus("Choose a slot to select a character. At least 3 are required.");
                Refresh();
            });
        }
    }

    private void SelectCandidate(FinishedTraineeData candidate)
    {
        for (int i = 0; i < selectedIds.Length; i++)
        {
            if (i != selectedSlot && selectedIds[i] != null
                && traineesById.TryGetValue(selectedIds[i], out FinishedTraineeData selected)
                && selected.trainee.id == candidate.trainee.id)
            {
                selectedIds[i] = selectedIds[selectedSlot];
                break;
            }
        }
        selectedIds[selectedSlot] = candidate.trainee_id;
    }

    private void SaveAndStart()
    {
        if (!ready || saving || !IsTeamValid()) return;
        var slots = new List<BattleTeamSlotData>();
        for (int i = 0; i < selectedIds.Length; i++)
        {
            if (selectedIds[i] != null)
            {
                slots.Add(new BattleTeamSlotData { trainee_id = selectedIds[i], lane = Lanes[i / 2], slot = i % 2 });
            }
        }
        saving = true;
        SetStatus("Saving battle team...");
        Refresh();
        int requestGeneration = generation;
        string playerId = client.CurrentPlayer.playerId;
        client.SaveBattleTeam(new BattleTeamData { team_id = DefaultTeamId, slots = slots.ToArray() }, team =>
        {
            if (!IsCurrent(requestGeneration, playerId)) return;
            if (team.team_id != DefaultTeamId)
            {
                saving = false;
                SetStatus("The server returned a different battle team.");
                Refresh();
                return;
            }
            gameObject.SetActive(false);
            client.StartBattleWithTeam(team.team_id);
        }, error =>
        {
            if (!IsCurrent(requestGeneration, playerId)) return;
            saving = false;
            SetStatus("Could not save battle team: " + error);
            Refresh();
        });
    }

    private static string AffinityFor(PositionAffinity affinity, string lane)
    {
        string grade = lane switch
        {
            "front" => affinity.front,
            "close" => affinity.close,
            "mid" => affinity.mid,
            "back" => affinity.back,
            _ => null
        };
        return grade?.Trim().ToUpperInvariant() ?? "";
    }

    private void OpenCandidatePopup(int slot)
    {
        if (!ready || saving) return;
        selectedSlot = slot;
        candidateTitle.text = $"{Lanes[slot / 2].ToUpperInvariant()} - Select character";
        candidatePopup.SetActive(true);
        Refresh();
        candidateScroll.verticalNormalizedPosition = 1f;
    }

    private void BuildUi()
    {
        RectTransform root = (RectTransform)transform;
        Stretch(root, Vector2.zero, Vector2.one);
        GetComponent<Image>().raycastTarget = false;
        GetComponent<Image>().color = Color.clear;
        Image background = MakeImage(transform, "Background", new Color(0f, 0f, 0f, 0.35f),
            Vector2.zero, Vector2.one);
        backgroundButton = background.gameObject.AddComponent<Button>();
        backgroundButton.targetGraphic = background;
        backgroundButton.transition = Selectable.Transition.None;
        backgroundButton.navigation = new Navigation { mode = Navigation.Mode.None };
        backgroundButton.onClick.AddListener(() => gameObject.SetActive(false));

        Image panel = MakeImage(transform, "Panel", PanelColor, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f));
        TMP_Text title = MakeText(panel.transform, new Vector2(0.02f, 0.91f), new Vector2(0.98f, 0.99f), 32);
        title.text = "Choose battle team";
        title.alignment = TextAlignmentOptions.Center;
        RectTransform teamArea = MakeRect(panel.transform, "Lanes", new Vector2(0.04f, 0.18f), new Vector2(0.96f, 0.89f));
        for (int lane = 0; lane < Lanes.Length; lane++)
        {
            float left = 0.085f + (Lanes.Length - 1 - lane) * 0.225f;
            TMP_Text label = MakeText(teamArea, new Vector2(left, 0.88f), new Vector2(left + 0.19f, 0.99f), 22);
            label.text = Lanes[lane].ToUpperInvariant();
            label.alignment = TextAlignmentOptions.Center;
            for (int row = 0; row < 2; row++)
            {
                int index = lane * 2 + row;
                Button button = MakeButton(teamArea, "Slot_" + index);
                float slotLeft = left - row * 0.035f;
                float slotTop = row == 0 ? 0.84f : 0.44f;
                Stretch((RectTransform)button.transform, new Vector2(slotLeft, slotTop - 0.32f),
                    new Vector2(slotLeft + 0.19f, slotTop));
                slotButtons[index] = button;
                slotPortraits[index] = MakePortrait(button.transform, new Vector2(0.02f, 0.08f), new Vector2(0.32f, 0.92f));
                slotLabels[index] = MakeText(button.transform, new Vector2(0.34f, 0.04f), new Vector2(0.98f, 0.96f), 21);
                slotLabels[index].alignment = TextAlignmentOptions.Center;
                button.onClick.AddListener(() => OpenCandidatePopup(index));
            }
        }

        statusText = MakeText(panel.transform, new Vector2(0.02f, 0.09f), new Vector2(0.98f, 0.16f), 20);
        statusText.alignment = TextAlignmentOptions.MidlineLeft;
        startButton = MakeActionButton(panel.transform, "Start battle", new Vector2(0.79f, 0.02f), new Vector2(0.98f, 0.08f));
        startButton.onClick.AddListener(SaveAndStart);

        Image popupBackground = MakeImage(transform, "CharacterSelectorBackground",
            new Color(0f, 0f, 0f, 0.35f), Vector2.zero, Vector2.one);
        candidatePopup = popupBackground.gameObject;
        Image popupPanel = MakeImage(candidatePopup.transform, "CharacterSelector", PanelColor,
            new Vector2(0.23f, 0.13f), new Vector2(0.77f, 0.87f));
        candidateTitle = MakeText(popupPanel.transform, new Vector2(0.04f, 0.89f), new Vector2(0.96f, 0.98f), 27);
        candidateTitle.alignment = TextAlignmentOptions.Center;
        TMP_Text subtitle = MakeText(popupPanel.transform, new Vector2(0.04f, 0.83f), new Vector2(0.96f, 0.89f), 19);
        subtitle.text = "Finished trainees (highest score first)";
        subtitle.alignment = TextAlignmentOptions.Center;

        RectTransform scrollRoot = MakeRect(popupPanel.transform, "TraineeList", new Vector2(0.04f, 0.16f), new Vector2(0.96f, 0.82f));
        ScrollRect scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
        candidateScroll = scroll;
        Image viewportImage = MakeImage(scrollRoot, "Viewport", new Color(0.92f, 0.92f, 0.92f, 1f), Vector2.zero, Vector2.one);
        viewportImage.gameObject.AddComponent<RectMask2D>();
        RectTransform viewport = (RectTransform)viewportImage.transform;
        candidateContent = MakeRect(viewport, "Content", new Vector2(0f, 1f), Vector2.one);
        candidateContent.pivot = new Vector2(0.5f, 1f);
        VerticalLayoutGroup layout = candidateContent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 5f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = candidateContent.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport;
        scroll.content = candidateContent;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 35f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        clearButton = MakeActionButton(popupPanel.transform, "Clear slot", new Vector2(0.04f, 0.04f), new Vector2(0.34f, 0.13f));
        clearButton.onClick.AddListener(() =>
        {
            selectedIds[selectedSlot] = null;
            candidatePopup.SetActive(false);
            Refresh();
        });
        Button closePopup = MakeActionButton(popupPanel.transform, "Cancel", new Vector2(0.66f, 0.04f), new Vector2(0.96f, 0.13f));
        closePopup.onClick.AddListener(() => { candidatePopup.SetActive(false); Refresh(); });
        candidatePopup.SetActive(false);
    }

    private void SetStatus(string message) => statusText.text = message;

    private static RectTransform MakeRect(Transform parent, string name, Vector2 min, Vector2 max)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)child.transform;
        Stretch(rect, min, max);
        return rect;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Image MakeImage(Transform parent, string name, Color color, Vector2 min, Vector2 max)
    {
        RectTransform rect = MakeRect(parent, name, min, max);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static Button MakeButton(Transform parent, string name)
    {
        Image image = MakeImage(parent, name, Color.white, Vector2.zero, Vector2.one);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = ButtonColor;
        colors.highlightedColor = new Color(0.80f, 0.80f, 0.80f, 1f);
        colors.disabledColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        button.colors = colors;
        return button;
    }

    private static Button MakeActionButton(Transform parent, string title, Vector2 min, Vector2 max)
    {
        Button button = MakeButton(parent, title);
        Stretch((RectTransform)button.transform, min, max);
        TMP_Text label = MakeText(button.transform, Vector2.zero, Vector2.one, 22);
        label.text = title;
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private static TMP_Text MakeText(Transform parent, Vector2 min, Vector2 max, float fontSize)
    {
        RectTransform rect = MakeRect(parent, "Text", min, max);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.enableAutoSizing = true;
        text.fontSizeMin = 12f;
        text.fontSizeMax = fontSize;
        text.color = new Color(0.12f, 0.12f, 0.12f, 1f);
        text.raycastTarget = false;
        return text;
    }

    private static Image MakePortrait(Transform parent, Vector2 min, Vector2 max)
    {
        Image image = MakeImage(parent, "Portrait", Color.white, min, max);
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private void SetPortrait(Image image, string characterId)
    {
        Sprite sprite = Resources.Load<Sprite>("Images/Characters/" + characterId);
        if (sprite == null)
        {
            Debug.LogWarning($"Character image not found for {characterId}.", this);
            image.gameObject.SetActive(false);
            return;
        }
        image.sprite = sprite;
        image.gameObject.SetActive(true);
    }
}
