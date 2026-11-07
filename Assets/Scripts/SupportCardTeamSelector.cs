using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class SupportCardTeamSelector : MonoBehaviour
{
    private const int RequiredCardCount = 6;

    [SerializeField] private Button[] cardButtons = new Button[RequiredCardCount];
    [SerializeField] private Button continueButton;
    [SerializeField] private GameObject cardGridObject;

    private readonly SupportCardData[] selectedCards = new SupportCardData[RequiredCardCount];
    private readonly Sprite[] defaultCardSprites = new Sprite[RequiredCardCount];
    private readonly Sprite[] defaultFacilitySprites = new Sprite[RequiredCardCount];
    private UnityAction[] slotClickHandlers;
    private CardGridManager cardGridManager;

    public event Action<SupportCardData[]> OnContinue;

    public SupportCardData[] SelectedCards => (SupportCardData[])selectedCards.Clone();

    private void Awake()
    {
        if (cardButtons == null || cardButtons.Length != RequiredCardCount)
        {
            Debug.LogError($"Assign exactly {RequiredCardCount} card buttons.", this);
        }
        else
        {
            slotClickHandlers = new UnityAction[cardButtons.Length];
            for (int i = 0; i < cardButtons.Length; i++)
            {
                if (cardButtons[i] == null)
                {
                    Debug.LogError($"Card button {i + 1} is not assigned.", this);
                    continue;
                }

                int slot = i;
                CacheSlotSprites(i, cardButtons[i]);
                slotClickHandlers[i] = () => OpenCardGrid(slot);
                cardButtons[i].onClick.AddListener(slotClickHandlers[i]);
            }
        }

        if (continueButton == null)
        {
            Debug.LogError("Continue button is not assigned.", this);
        }
        else
        {
            continueButton.interactable = false;
            continueButton.onClick.AddListener(HandleContinueClicked);
        }

        if (cardGridObject == null)
        {
            Debug.LogError("Card grid GameObject is not assigned.", this);
            return;
        }

        cardGridManager = cardGridObject.GetComponent<CardGridManager>();
        if (cardGridManager == null)
        {
            Debug.LogError("Card grid GameObject does not have a CardGridManager.", this);
            return;
        }

        cardGridManager.enabled = false;
        cardGridObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (cardButtons != null && slotClickHandlers != null)
        {
            for (int i = 0; i < cardButtons.Length; i++)
            {
                if (cardButtons[i] != null && slotClickHandlers[i] != null)
                {
                    cardButtons[i].onClick.RemoveListener(slotClickHandlers[i]);
                }
            }
        }

        if (continueButton != null)
        {
            continueButton.onClick.RemoveListener(HandleContinueClicked);
        }
    }

    private void OpenCardGrid(int slot)
    {
        if (cardGridManager == null)
        {
            Debug.LogError("Cannot open card grid because CardGridManager is unavailable.", this);
            return;
        }

        cardGridObject.SetActive(true);
        cardGridManager.FetchAndInitializeGrid(
            card => HandleCardSelected(slot, card),
            error =>
            {
                Debug.LogError($"Failed to load support cards: {error}", this);
                cardGridObject.SetActive(false);
            });
    }

    public void LoadPreset(string[] cardIds, Action<string> onError = null)
    {
        if (cardButtons == null || cardButtons.Length != RequiredCardCount)
        {
            onError?.Invoke("Exactly 6 card buttons must be assigned.");
            return;
        }

        ClearSlots();
        if (cardIds == null || cardIds.Length == 0)
        {
            return;
        }

        var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string cardId in cardIds)
        {
            if (string.IsNullOrWhiteSpace(cardId) || !uniqueIds.Add(cardId))
            {
                onError?.Invoke("Saved support card preset contains invalid or duplicate card IDs.");
                return;
            }
        }

        if (cardIds.Length != selectedCards.Length)
        {
            onError?.Invoke("Saved support card preset must contain exactly 6 cards.");
            return;
        }

        for (int i = 0; i < cardButtons.Length; i++)
        {
            if (cardButtons[i] == null)
            {
                onError?.Invoke($"Card button {i + 1} is not assigned.");
                return;
            }
        }

        GameClient client = GameClient.Instance;
        if (client == null)
        {
            onError?.Invoke("Game client is unavailable.");
            return;
        }

        client.FetchSupportCards(cards =>
        {
            var cardsById = new Dictionary<string, SupportCardData>(StringComparer.Ordinal);
            foreach (SupportCardData card in cards)
            {
                if (card != null && !string.IsNullOrWhiteSpace(card.id))
                {
                    cardsById[card.id] = card;
                }
            }

            var presetCards = new SupportCardData[selectedCards.Length];
            for (int i = 0; i < cardIds.Length; i++)
            {
                if (!cardsById.TryGetValue(cardIds[i], out presetCards[i]))
                {
                    onError?.Invoke($"Saved support card '{cardIds[i]}' is no longer in the catalog.");
                    return;
                }
            }

            for (int i = 0; i < presetCards.Length; i++)
            {
                selectedCards[i] = presetCards[i];
                TMP_Text label = cardButtons[i].GetComponentInChildren<TMP_Text>(true);
                CardGridItem.SetCardDisplay(presetCards[i], cardButtons[i], label, this);
            }

            UpdateContinueButton();
        }, error => onError?.Invoke($"Failed to load support cards: {error}"));
    }

    private void ClearSlots()
    {
        for (int i = 0; i < selectedCards.Length; i++)
        {
            selectedCards[i] = null;
            Button button = cardButtons[i];
            if (button == null)
            {
                continue;
            }

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = "Select Card";
            }

            foreach (Image image in button.GetComponentsInChildren<Image>(true))
            {
                if (image == button.image)
                {
                    image.sprite = defaultCardSprites[i];
                }
                else
                {
                    image.sprite = defaultFacilitySprites[i];
                }
            }
        }

        UpdateContinueButton();
    }

    private void CacheSlotSprites(int slot, Button button)
    {
        defaultCardSprites[slot] = button.image != null ? button.image.sprite : null;
        foreach (Image image in button.GetComponentsInChildren<Image>(true))
        {
            if (image != button.image)
            {
                defaultFacilitySprites[slot] = image.sprite;
                break;
            }
        }
    }

    private void HandleCardSelected(int slot, SupportCardData card)
    {
        if (card == null)
        {
            Debug.LogError("Card grid returned no selected card.", this);
        }
        else
        {
            selectedCards[slot] = card;
            TMP_Text label = cardButtons[slot].GetComponentInChildren<TMP_Text>(true);
            CardGridItem.SetCardDisplay(card, cardButtons[slot], label, this);
        }

        UpdateContinueButton();
        cardGridObject.SetActive(false);
    }

    private void HandleContinueClicked()
    {
        if (!HasSixUniqueCards())
        {
            UpdateContinueButton();
            return;
        }

        OnContinue?.Invoke(SelectedCards);
    }

    private void UpdateContinueButton()
    {
        if (continueButton != null)
        {
            continueButton.interactable = HasSixUniqueCards();
        }
    }

    private bool HasSixUniqueCards()
    {
        var cardIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (SupportCardData card in selectedCards)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.id) || !cardIds.Add(card.id))
            {
                return false;
            }
        }

        return true;
    }
}
