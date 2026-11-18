using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CharacterGridItem : MonoBehaviour
{
    private Button itemButton;
    private Image characterImage;
    private Material selectedMaterial;
    private Material unselectedMaterial;
    private OwnedCharacterData character;
    private Action<OwnedCharacterData> onSelected;

    public string CharacterId => character?.character_id;

    public void Initialize(
        OwnedCharacterData characterData,
        bool isSelected,
        Material selectedMaterial,
        Material unselectedMaterial,
        Action<OwnedCharacterData> onSelect)
    {
        character = characterData;
        onSelected = onSelect;
        this.selectedMaterial = selectedMaterial;
        this.unselectedMaterial = unselectedMaterial;
        itemButton = GetComponent<Button>();
        characterImage = GetComponent<Image>();

        if (itemButton == null || characterImage == null)
        {
            Debug.LogError("Character grid item requires a Button and an Image.", this);
            return;
        }

        characterImage.sprite = Resources.Load<Sprite>($"Images/Characters/{character.character_id}");
        characterImage.preserveAspect = true;
        itemButton.transition = Selectable.Transition.None;
        itemButton.targetGraphic = characterImage;
        CreateCharacterLabel();
        SetSelected(isSelected);
        itemButton.onClick.AddListener(HandleClick);
    }

    private void CreateCharacterLabel()
    {
        GameObject background = new GameObject("CharacterLabel", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        background.layer = gameObject.layer;
        background.transform.SetParent(transform, false);
        RectTransform backgroundRect = (RectTransform)background.transform;
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.right;
        backgroundRect.pivot = new Vector2(0.5f, 0f);
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = new Vector2(0f, 58f);
        Image backgroundImage = background.GetComponent<Image>();
        backgroundImage.color = new Color(0f, 0f, 0f, 0.75f);
        backgroundImage.raycastTarget = false;

        string characterName = character.character?.name;
        if (string.IsNullOrWhiteSpace(characterName))
        {
            Debug.LogWarning($"Missing name for player character {character.character_id}.", this);
            characterName = character.character_id;
        }

        TMP_Text nameText = CreateLabelText(background.transform, "Name", 0f, 0.55f);
        nameText.richText = false;
        nameText.text = characterName;
        TMP_Text starsText = CreateLabelText(background.transform, "Stars", 0.55f, 1f);
        if (character.tier < 1 || character.tier > 5)
        {
            Debug.LogWarning($"Invalid star tier {character.tier} for player character {character.character_id}.", this);
            starsText.text = "?";
        }
        else
        {
            starsText.text = CharacterData.FormatStars(character.tier);
        }
    }

    private static TMP_Text CreateLabelText(Transform parent, string name, float bottom, float top)
    {
        GameObject label = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        label.layer = parent.gameObject.layer;
        label.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)label.transform;
        rect.anchorMin = new Vector2(0f, bottom);
        rect.anchorMax = new Vector2(1f, top);
        rect.offsetMin = new Vector2(4f, 0f);
        rect.offsetMax = new Vector2(-4f, 0f);
        TMP_Text text = label.GetComponent<TMP_Text>();
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.fontSize = 21f;
        text.enableAutoSizing = true;
        text.fontSizeMin = 10f;
        text.fontSizeMax = 21f;
        text.raycastTarget = false;
        return text;
    }

    public void SetSelected(bool isSelected)
    {
        if (characterImage == null)
        {
            return;
        }

        characterImage.material = isSelected ? selectedMaterial : unselectedMaterial;
        characterImage.SetMaterialDirty();
        characterImage.SetVerticesDirty();
    }

    private void HandleClick()
    {
        onSelected?.Invoke(character);
    }
}
