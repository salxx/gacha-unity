using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class SkillShopController : MonoBehaviour, IPointerClickHandler
{
    private const string BasicTemplateMarker = "(Basic)";
    private const string AdvancedTemplateMarker = "(Advanced)";

    private sealed class SkillEntry
    {
        public SkillDefinitionData Data;
        public Button Button;
        public Image Background;
        public TMP_Text SkillText;
        public TMP_Text CostText;
        public Color OriginalColor;
        public bool Selected;
        public bool PrerequisiteSelection;
    }

    private readonly List<SkillEntry> entries = new List<SkillEntry>();
    private TMP_Text spText;
    private TMP_Text acceptButtonText;
    private Button acceptButton;
    private Transform content;
    private Transform basicTemplate;
    private Transform advancedTemplate;
    private int startingSkillPoints;
    private bool hidePurchasedSkills;
    private bool isSubmitting;
    private Action<string[]> onAccept;
    private Action onClose;

    public void Initialize(
        int skillPoints,
        bool hidePurchased,
        Action<string[]> acceptCallback,
        Action closeCallback)
    {
        startingSkillPoints = skillPoints;
        hidePurchasedSkills = hidePurchased;
        onAccept = acceptCallback;
        onClose = closeCallback;

        Transform spTextTransform = transform.Find("SPText (TMP)");
        Transform acceptButtonTransform = transform.Find("AcceptButton");
        content = transform.Find("Scroll View/Viewport/Content");
        if (spTextTransform == null || acceptButtonTransform == null || content == null)
        {
            Debug.LogError("SkillShop prefab is missing SPText, AcceptButton, or Scroll View content.");
            return;
        }

        spText = spTextTransform.GetComponent<TMP_Text>();
        acceptButton = acceptButtonTransform.GetComponent<Button>();
        acceptButtonText = acceptButtonTransform.GetComponentInChildren<TMP_Text>();
        if (spText == null || acceptButton == null || acceptButtonText == null)
        {
            Debug.LogError("SkillShop prefab is missing its SP text, AcceptButton, or purchase button text.");
            return;
        }

        acceptButton.onClick.AddListener(AcceptSelection);
        FindTemplates();
        UpdateDisplay();
    }

    public void LoadSkills(SkillDefinitionData[] skills)
    {
        if (content == null || basicTemplate == null || advancedTemplate == null)
        {
            Debug.LogError("SkillShop templates could not be initialized.");
            return;
        }

        foreach (Transform child in content)
        {
            if (child != basicTemplate && child != advancedTemplate)
            {
                Destroy(child.gameObject);
            }
        }
        entries.Clear();

        if (skills == null)
        {
            Debug.LogError("SkillShop received a null skills list.");
            return;
        }

        List<SkillDefinitionData> orderedSkills = new List<SkillDefinitionData>();
        List<SkillDefinitionData> purchasedSkills = new List<SkillDefinitionData>();
        foreach (SkillDefinitionData skill in skills)
        {
            if (skill == null)
            {
                Debug.LogError("SkillShop received an empty skill entry.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(skill.id) || string.IsNullOrWhiteSpace(skill.family_id)
                || string.IsNullOrWhiteSpace(skill.name) || skill.purchase_cost < 0)
            {
                Debug.LogError("SkillShop received a skill with invalid identifying or cost data.");
                continue;
            }
            if (skill.purchased && hidePurchasedSkills)
            {
                continue;
            }

            (skill.purchased ? purchasedSkills : orderedSkills).Add(skill);
        }
        orderedSkills.AddRange(purchasedSkills);

        foreach (SkillDefinitionData skill in orderedSkills)
        {
            Transform template = FindTemplateFor(skill);
            if (template == null)
            {
                Debug.LogError($"No SkillShop template matches skill '{skill.name}'.");
                continue;
            }

            Transform item = Instantiate(template, content, false);
            item.name = $"PurchasableSkill_{skill.id}";
            item.gameObject.SetActive(true);

            SkillEntry entry = CreateEntry(item, skill);
            if (entry != null)
            {
                entries.Add(entry);
            }
        }

        UpdateDisplay();
    }

    public void SetSubmitting(bool submitting)
    {
        isSubmitting = submitting;
        UpdateDisplay();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.pointerPressRaycast.gameObject == gameObject)
        {
            Close();
        }
    }

    public void Close()
    {
        Action closed = onClose;
        onClose = null;
        Destroy(gameObject);
        closed?.Invoke();
    }

    private void FindTemplates()
    {
        for (int i = 0; i < content.childCount; i++)
        {
            Transform child = content.GetChild(i);
            if (child.name != "PurchasableSkill")
            {
                continue;
            }

            Transform skillText = child.Find("SkillText");
            TMP_Text text = skillText != null ? skillText.GetComponent<TMP_Text>() : null;
            if (text == null)
            {
                Debug.LogError("PurchasableSkill template is missing its SkillText TMP component.");
                continue;
            }

            if (text.text.Contains(AdvancedTemplateMarker))
            {
                advancedTemplate = child;
            }
            else if (text.text.Contains(BasicTemplateMarker))
            {
                basicTemplate = child;
            }
            else
            {
                Debug.LogError($"Unrecognized PurchasableSkill template '{text.text}'.");
            }
        }

        if (basicTemplate == null || advancedTemplate == null)
        {
            Debug.LogError("SkillShop requires both Basic and Advanced PurchasableSkill templates.");
            return;
        }

        basicTemplate.gameObject.SetActive(false);
        advancedTemplate.gameObject.SetActive(false);
    }

    private Transform FindTemplateFor(SkillDefinitionData skill)
    {
        if (skill.id.EndsWith("a", StringComparison.OrdinalIgnoreCase))
        {
            return basicTemplate;
        }
        if (skill.id.EndsWith("b", StringComparison.OrdinalIgnoreCase))
        {
            return advancedTemplate;
        }
        return null;
    }

    private SkillEntry CreateEntry(Transform item, SkillDefinitionData skill)
    {
        Button button = item.GetComponent<Button>();
        Image background = item.GetComponent<Image>();
        Transform skillTextTransform = item.Find("SkillText");
        Transform costTextTransform = item.Find("Cost");
        TMP_Text skillText = skillTextTransform != null
            ? skillTextTransform.GetComponent<TMP_Text>()
            : null;
        TMP_Text costText = costTextTransform != null
            ? costTextTransform.GetComponent<TMP_Text>()
            : null;
        if (button == null || background == null || skillText == null || costText == null)
        {
            Debug.LogError($"PurchasableSkill '{skill.id}' is missing a required Button, Image, or TMP component.");
            return null;
        }

        SkillEntry entry = new SkillEntry
        {
            Data = skill,
            Button = button,
            Background = background,
            SkillText = skillText,
            CostText = costText,
            OriginalColor = background.color
        };
        skillText.text = $"<b>{skill.name}</b>\n\n{skill.description}";
        costText.text = skill.purchase_cost.ToString();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => ToggleSelection(entry));
        return entry;
    }

    private void ToggleSelection(SkillEntry entry)
    {
        if (isSubmitting || entry.Data.purchased)
        {
            return;
        }

        if (entry.Selected)
        {
            if (IsBasicSkill(entry))
            {
                ClearFamilySelection(entry.Data.family_id);
            }
            else
            {
                entry.Selected = false;
                entry.PrerequisiteSelection = false;
                foreach (SkillEntry selected in entries)
                {
                    if (selected.Data.family_id == entry.Data.family_id
                        && selected.PrerequisiteSelection)
                    {
                        selected.PrerequisiteSelection = false;
                    }
                }
            }
        }
        else
        {
            int otherFamilyCost = 0;
            foreach (SkillEntry selected in entries)
            {
                if (IsChargeableSelection(selected)
                    && selected.Data.family_id != entry.Data.family_id)
                {
                    otherFamilyCost += selected.Data.purchase_cost;
                }
            }
            if (otherFamilyCost + entry.Data.purchase_cost > startingSkillPoints)
            {
                return;
            }

            ClearFamilySelection(entry.Data.family_id);
            entry.Selected = true;
            if (IsAdvancedSkill(entry))
            {
                SkillEntry basic = FindBasicSkill(entry.Data.family_id);
                if (basic != null && !basic.Data.purchased)
                {
                    basic.Selected = true;
                    basic.PrerequisiteSelection = true;
                }
            }
        }

        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        int selectedCost = 0;
        int selectedCount = 0;
        foreach (SkillEntry entry in entries)
        {
            if (entry.Selected && !entry.Data.purchased)
            {
                selectedCount++;
            }
            if (IsChargeableSelection(entry))
            {
                selectedCost += entry.Data.purchase_cost;
            }
        }

        if (spText != null)
        {
            spText.text = $"{startingSkillPoints}SP -> {startingSkillPoints - selectedCost}SP";
        }
        if (acceptButton != null)
        {
            acceptButton.interactable = !isSubmitting && selectedCost > 0;
        }
        if (acceptButtonText != null)
        {
            acceptButtonText.text = selectedCount > 0 ? $"Purchase ({selectedCount})" : "Purchase";
        }

        foreach (SkillEntry entry in entries)
        {
            int otherFamilyCost = 0;
            foreach (SkillEntry selected in entries)
            {
                if (IsChargeableSelection(selected)
                    && selected.Data.family_id != entry.Data.family_id)
                {
                    otherFamilyCost += selected.Data.purchase_cost;
                }
            }

            bool affordable = otherFamilyCost + entry.Data.purchase_cost <= startingSkillPoints;
            entry.Button.interactable = !isSubmitting && !entry.Data.purchased
                                        && (entry.Selected || affordable);
            entry.CostText.text = entry.Data.purchased
                ? string.Empty
                : entry.Data.purchase_cost.ToString();
            if (entry.Data.purchased)
            {
                entry.SkillText.text = $"<b>{entry.Data.name}</b>\n\n{entry.Data.description}\n\n<color=#777777>[Purchased]</color>";
                entry.Background.color = new Color(0.7f, 0.7f, 0.7f, entry.OriginalColor.a);
            }
            else
            {
                string selectionLabel = entry.Selected ? "\n\n<color=#2D7A25>[Selected]</color>" : string.Empty;
                entry.SkillText.text = $"<b>{entry.Data.name}</b>\n\n{entry.Data.description}{selectionLabel}";
                entry.Background.color = entry.Selected
                    ? new Color(0.58f, 0.88f, 0.52f, entry.OriginalColor.a)
                    : affordable
                        ? entry.OriginalColor
                        : new Color(0.65f, 0.65f, 0.65f, entry.OriginalColor.a);
            }
        }
    }

    private void AcceptSelection()
    {
        if (isSubmitting)
        {
            return;
        }

        List<string> selectedIds = new List<string>();
        foreach (SkillEntry entry in entries)
        {
            if (IsChargeableSelection(entry))
            {
                selectedIds.Add(entry.Data.id);
            }
        }
        if (selectedIds.Count == 0)
        {
            return;
        }

        onAccept?.Invoke(selectedIds.ToArray());
    }

    private void ClearFamilySelection(string familyId)
    {
        foreach (SkillEntry entry in entries)
        {
            if (entry.Data.family_id == familyId)
            {
                entry.Selected = false;
                entry.PrerequisiteSelection = false;
            }
        }
    }

    private SkillEntry FindBasicSkill(string familyId)
    {
        foreach (SkillEntry entry in entries)
        {
            if (entry.Data.family_id == familyId && IsBasicSkill(entry))
            {
                return entry;
            }
        }
        return null;
    }

    private static bool IsChargeableSelection(SkillEntry entry) =>
        entry.Selected && !entry.PrerequisiteSelection;

    private static bool IsBasicSkill(SkillEntry entry) =>
        entry.Data.id.EndsWith("a", StringComparison.OrdinalIgnoreCase);

    private static bool IsAdvancedSkill(SkillEntry entry) =>
        entry.Data.id.EndsWith("b", StringComparison.OrdinalIgnoreCase);
}
