using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CharacterGridManager : MonoBehaviour
{
    private readonly List<CharacterGridItem> gridItems = new List<CharacterGridItem>();
    private Transform contentPanel;
    private OwnedCharacterData selectedCharacter;
    private Action<OwnedCharacterData> onContinue;
    private Button continueButton;
    private RectTransform scrollRectTransform;
    private Vector2 originalScrollOffsetMin;
    private bool scrollAreaAdjusted;
    private Material outlineMaterial;
    private Material unselectedMaterial;

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

        contentPanel = scrollRect.content;
        scrollRectTransform = scrollRect.GetComponent<RectTransform>();
        if (scrollAreaAdjusted)
        {
            scrollRectTransform.offsetMin = originalScrollOffsetMin;
        }
        else
        {
            originalScrollOffsetMin = scrollRectTransform.offsetMin;
            scrollAreaAdjusted = true;
        }
        scrollRectTransform.offsetMin += new Vector2(0, 70);
        EnsureContinueButton();

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
        if (scrollRectTransform != null)
        {
            scrollRectTransform.offsetMin = originalScrollOffsetMin;
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

    private void EnsureContinueButton()
    {
        if (continueButton != null)
        {
            return;
        }

        GameObject buttonObject = new GameObject(
            "ContinueButton",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(transform, false);
        buttonObject.layer = gameObject.layer;
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0);
        buttonRect.anchorMax = new Vector2(0.5f, 0);
        buttonRect.pivot = new Vector2(0.5f, 0);
        buttonRect.anchoredPosition = new Vector2(0, 10);
        buttonRect.sizeDelta = new Vector2(200, 50);

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = Color.white;
        continueButton = buttonObject.GetComponent<Button>();
        continueButton.targetGraphic = buttonImage;
        continueButton.onClick.AddListener(HandleContinueClicked);
        continueButton.interactable = false;

        GameObject labelObject = new GameObject(
            "Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObject.transform.SetParent(buttonObject.transform, false);
        labelObject.layer = gameObject.layer;
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        Text label = labelObject.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 24;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.black;
        label.text = "Continue";
        label.raycastTarget = false;
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
