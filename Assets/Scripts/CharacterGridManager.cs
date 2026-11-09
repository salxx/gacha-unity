using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CharacterGridManager : MonoBehaviour, IPointerClickHandler
{
    private readonly List<CharacterGridItem> gridItems = new List<CharacterGridItem>();
    private Transform contentPanel;
    private OwnedCharacterData selectedCharacter;
    private Action<OwnedCharacterData> onContinue;
    [SerializeField] private Button continueButton;
    private Material outlineMaterial;
    private Material unselectedMaterial;

    public event Action OnBackgroundClicked;

    private void Awake()
    {
        if (continueButton != null)
        {
            continueButton.onClick.AddListener(HandleContinueClicked);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.pointerCurrentRaycast.gameObject == gameObject)
        {
            OnBackgroundClicked?.Invoke();
        }
    }

    public void Open(
        string selectedCharacterId,
        Action<OwnedCharacterData> onContinueSelected,
        Action<string> onError = null)
    {
        onContinue = onContinueSelected;
        ScrollRect scrollRect = GetComponentInChildren<ScrollRect>(true);
        if (scrollRect == null || scrollRect.content == null)
        {
            onError?.Invoke("Character selector does not have a scroll view content panel.");
            return;
        }

        if (continueButton == null)
        {
            onError?.Invoke("Character selector continue button is not assigned.");
            return;
        }

        contentPanel = scrollRect.content;

        Shader outlineShader = Shader.Find("Custom/SpriteGlowOutline");
        if (outlineShader == null)
        {
            onError?.Invoke("Character outline shader could not be found.");
            return;
        }

        if (outlineMaterial != null)
        {
            Destroy(outlineMaterial);
        }
        outlineMaterial = new Material(outlineShader);
        outlineMaterial.SetFloat("_UseOutline", 1f);
        outlineMaterial.SetFloat("_OutlineWidth", 16f);
        outlineMaterial.SetColor("_OutlineColor", Color.yellow);
        if (unselectedMaterial != null)
        {
            Destroy(unselectedMaterial);
        }
        unselectedMaterial = new Material(outlineShader);
        unselectedMaterial.SetFloat("_UseOutline", 0f);

        ClearGrid();
        selectedCharacter = null;
        continueButton.interactable = false;

        GameClient client = GameClient.Instance;
        if (client == null)
        {
            onError?.Invoke("Game client is unavailable.");
            return;
        }

        client.FetchPlayerCharacters(characters =>
        {
            if (characters.Length == 0)
            {
                onError?.Invoke("This player has no characters to select.");
                return;
            }

            foreach (OwnedCharacterData character in characters)
            {
                if (character == null || string.IsNullOrWhiteSpace(character.character_id))
                {
                    Debug.LogError("Player character response contains an invalid character ID.", this);
                    continue;
                }

                GameObject itemObject = new GameObject(
                    $"Character_{character.character_id}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Button));
                itemObject.layer = contentPanel.gameObject.layer;
                itemObject.transform.SetParent(contentPanel, false);
                CharacterGridItem item = itemObject.AddComponent<CharacterGridItem>();
                bool isSelected = character.character_id == selectedCharacterId;
                item.Initialize(character, isSelected, outlineMaterial, unselectedMaterial, HandleCharacterSelected);
                gridItems.Add(item);
                if (isSelected)
                {
                    selectedCharacter = character;
                }
            }

            continueButton.interactable = selectedCharacter != null;
        }, error => onError?.Invoke($"Failed to load player characters: {error}"));
    }

    private void OnDestroy()
    {
        if (continueButton != null)
        {
            continueButton.onClick.RemoveListener(HandleContinueClicked);
        }

        if (outlineMaterial != null)
        {
            Destroy(outlineMaterial);
        }

        if (unselectedMaterial != null)
        {
            Destroy(unselectedMaterial);
        }
    }

    private void ClearGrid()
    {
        foreach (Transform child in contentPanel)
        {
            Destroy(child.gameObject);
        }

        gridItems.Clear();
    }

    private void HandleCharacterSelected(OwnedCharacterData character)
    {
        selectedCharacter = character;
        foreach (CharacterGridItem item in gridItems)
        {
            item.SetSelected(item.CharacterId == character.character_id);
        }

        continueButton.interactable = true;
    }

    private void HandleContinueClicked()
    {
        if (selectedCharacter != null)
        {
            onContinue?.Invoke(selectedCharacter);
        }
    }
}
