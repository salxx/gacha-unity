using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class DailyLoginPopup : MonoBehaviour
{
    private GameClient client;
    private RectTransform rewardsContent;
    private TMP_Text status;
    private TMP_Text claimLabel;
    private Button claimButton;
    private Button checkButton;
    private string rewardDate;
    private int generation;

    public static DailyLoginPopup Create(Transform parent, GameClient gameClient)
    {
        Image background = MakeImage(parent, "DailyLoginPopup", new Color(0f, 0f, 0f, 0.45f),
            Vector2.zero, Vector2.one);
        DailyLoginPopup popup = background.gameObject.AddComponent<DailyLoginPopup>();
        popup.client = gameClient;
        popup.BuildUi();
        popup.gameObject.SetActive(false);
        return popup;
    }

    public void Open()
    {
        gameObject.SetActive(false);
        CheckReward();
    }

    private void OnDisable()
    {
        generation++;
    }

    private void CheckReward()
    {
        int request = ++generation;
        rewardDate = null;
        if (gameObject.activeInHierarchy) Select(null);
        ClearRewards();
        status.text = "Checking daily login reward...";
        claimButton.gameObject.SetActive(false);
        checkButton.gameObject.SetActive(false);
        client.FetchDailyLogin(response =>
        {
            if (!IsCurrent(request)) return;
            if (!response.available)
            {
                gameObject.SetActive(false);
                return;
            }
            if (response.rewards.Length == 0)
            {
                ShowCheckError("The daily reward list is empty.");
                return;
            }
            foreach (DailyLoginReward reward in response.rewards)
            {
                if (reward == null || string.IsNullOrWhiteSpace(reward.type) || reward.amount <= 0)
                {
                    ClearRewards();
                    ShowCheckError("The daily reward list is invalid.");
                    return;
                }
                TMP_Text text = MakeText(rewardsContent, reward.type + "Reward",
                    Vector2.zero, Vector2.one, 26);
                text.text = $"{reward.amount} {reward.type}";
                text.alignment = TextAlignmentOptions.Center;
                text.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
            }
            rewardDate = response.date;
            status.text = "";
            claimLabel.text = "Claim";
            claimButton.gameObject.SetActive(true);
            claimButton.interactable = true;
            Show();
            Select(claimButton.gameObject);
        }, error =>
        {
            if (IsCurrent(request)) ShowCheckError("Could not check daily reward: " + error);
        });
    }

    private void Claim()
    {
        int request = ++generation;
        claimButton.interactable = false;
        Select(null);
        checkButton.gameObject.SetActive(false);
        status.text = "Claiming...";
        client.ClaimDailyLogin(rewardDate, () =>
        {
            if (IsCurrent(request)) gameObject.SetActive(false);
        }, error =>
        {
            if (!IsCurrent(request)) return;
            status.text = "Could not claim: " + error;
            claimLabel.text = "Retry claim";
            claimButton.interactable = true;
            checkButton.gameObject.SetActive(true);
            Select(claimButton.gameObject);
        });
    }

    private bool IsCurrent(int request)
    {
        return generation == request;
    }

    private void ShowCheckError(string message)
    {
        Debug.LogError(message, this);
        status.text = message;
        checkButton.gameObject.SetActive(true);
        Show();
        Select(checkButton.gameObject);
    }

    private void Show()
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    private static void Select(GameObject target)
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(target);
    }

    private void ClearRewards()
    {
        foreach (Transform child in rewardsContent)
        {
            Destroy(child.gameObject);
        }
    }

    private void BuildUi()
    {
        Image panel = MakeImage(transform, "Panel", new Color(0.97f, 0.97f, 0.97f, 1f),
            new Vector2(0.375f, 0.34f), new Vector2(0.625f, 0.66f));
        TMP_Text title = MakeText(panel.transform, "Title", new Vector2(0.05f, 0.86f),
            new Vector2(0.95f, 0.98f), 34);
        title.text = "Daily Login Bonus";
        title.alignment = TextAlignmentOptions.Center;
        status = MakeText(panel.transform, "Status", new Vector2(0.05f, 0.70f),
            new Vector2(0.95f, 0.86f), 20);
        status.alignment = TextAlignmentOptions.Center;

        RectTransform scrollRoot = MakeRect(panel.transform, "Rewards", new Vector2(0.08f, 0.27f),
            new Vector2(0.92f, 0.69f));
        ScrollRect scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
        Image viewport = MakeImage(scrollRoot, "Viewport", new Color(0.92f, 0.92f, 0.92f, 1f),
            Vector2.zero, Vector2.one);
        viewport.gameObject.AddComponent<RectMask2D>();
        rewardsContent = MakeRect(viewport.transform, "Content", new Vector2(0f, 1f), Vector2.one);
        rewardsContent.pivot = new Vector2(0.5f, 1f);
        VerticalLayoutGroup layout = rewardsContent.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        rewardsContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = (RectTransform)viewport.transform;
        scroll.content = rewardsContent;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 35f;

        claimButton = MakeButton(panel.transform, "Claim", new Vector2(0.30f, 0.12f), new Vector2(0.70f, 0.24f),
            out claimLabel);
        claimButton.onClick.AddListener(Claim);
        checkButton = MakeButton(panel.transform, "Check again", new Vector2(0.30f, 0.01f),
            new Vector2(0.70f, 0.11f), out _);
        checkButton.onClick.AddListener(CheckReward);
        Navigation claimNavigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = checkButton };
        Navigation checkNavigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = claimButton };
        claimButton.navigation = claimNavigation;
        checkButton.navigation = checkNavigation;
    }

    private static RectTransform MakeRect(Transform parent, string name, Vector2 min, Vector2 max)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)child.transform;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static Image MakeImage(Transform parent, string name, Color color, Vector2 min, Vector2 max)
    {
        Image image = MakeRect(parent, name, min, max).gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static TMP_Text MakeText(Transform parent, string name, Vector2 min, Vector2 max, float size)
    {
        TMP_Text text = MakeRect(parent, name, min, max).gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.enableAutoSizing = true;
        text.fontSizeMin = 12f;
        text.fontSizeMax = size;
        text.color = new Color(0.12f, 0.12f, 0.12f, 1f);
        text.raycastTarget = false;
        return text;
    }

    private static Button MakeButton(Transform parent, string name, Vector2 min, Vector2 max, out TMP_Text label)
    {
        Image image = MakeImage(parent, name, new Color(0.88f, 0.88f, 0.88f, 1f), min, max);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        label = MakeText(image.transform, "Label", Vector2.zero, Vector2.one, 22);
        label.text = name;
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }
}
