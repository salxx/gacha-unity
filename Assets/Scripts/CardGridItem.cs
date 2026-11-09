using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class CardGridItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI itemText;
    [SerializeField] private Button itemButton; // Best practice: add a Button component to the Image

    private SupportCardData card;
    private Action<SupportCardData> onSelectedCallback;

    public void Setup(SupportCardData itemData, Action<SupportCardData> onSelect)
    {
        card = itemData;
        onSelectedCallback = onSelect;

        SetCardDisplay(card, itemButton, itemText, this);
        itemButton.interactable = true;

        itemButton.onClick.RemoveAllListeners();
        itemButton.onClick.AddListener(HandleClick);
    }

    public void SetInteractable(bool interactable)
    {
        if (itemButton != null)
        {
            itemButton.interactable = interactable;
        }
    }

    public static void SetCardDisplay(
        SupportCardData card, Button button, TMP_Text itemText, Component context)
    {
        if (card == null)
        {
            Debug.LogError("Cannot display a missing support card.", context);
            return;
        }

        if (button == null)
        {
            Debug.LogError("Cannot display a support card without a button.", context);
            return;
        }

        if (itemText == null)
        {
            Debug.LogError("Card item does not have a text component.", context);
            return;
        }

        string spriteName = card.tier switch
        {
            1 => "common",
            2 => "rare",
            3 => "super-rare",
            _ => null
        };
        if (spriteName == null)
        {
            Debug.LogError($"Cannot display support card with invalid tier {card.tier}.", context);
        }
        else if (button.image == null)
        {
            Debug.LogError("Card button does not have an Image component.", context);
        }
        else
        {
            Sprite cardSprite = Resources.Load<Sprite>($"Images/Cards/{spriteName}");
            if (cardSprite == null)
            {
                Debug.LogError($"Support card sprite not found: Images/Cards/{spriteName}.", context);
            }
            else
            {
                button.image.sprite = cardSprite;
            }
        }

        Image facilityImage = null;
        foreach (Image image in button.GetComponentsInChildren<Image>(true))
        {
            if (image != button.image)
            {
                facilityImage = image;
                break;
            }
        }

        if (facilityImage == null)
        {
            Debug.LogError("Card item does not have a child Image for its facility symbol.", context);
        }
        else if (string.IsNullOrWhiteSpace(card.facility))
        {
            Debug.LogError($"Support card {card.id} does not have a facility.", context);
        }
        else
        {
            string facilityName = card.facility.Trim().ToLowerInvariant();
            Sprite facilitySprite = Resources.Load<Sprite>($"Images/Symbols/{facilityName}");
            if (facilitySprite == null)
            {
                Debug.LogError($"Facility symbol not found: Images/Symbols/{facilityName}.", context);
            }
            else
            {
                facilityImage.sprite = facilitySprite;
            }
        }

        itemText.text = $"{Tiers.ShortName(card.tier)}\n{card.facility?.ToUpperInvariant()}\n{card.title}";
    }

    private void HandleClick()
    {
        // Trigger the callback passing the selected object back to the manager
        onSelectedCallback?.Invoke(card);
    }
}
