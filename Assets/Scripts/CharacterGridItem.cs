using System;
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
        SetSelected(isSelected);
        itemButton.onClick.AddListener(HandleClick);
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
