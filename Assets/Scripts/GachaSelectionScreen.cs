using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class GachaSelectionScreen : MonoBehaviour
{
    [SerializeField] private Button backgroundButton;
    [SerializeField] private Button[] buttons = new Button[4];
    [SerializeField] private TMP_Text[] labels = new TMP_Text[4];
    [SerializeField] private Image[] portraits = new Image[4];
    [SerializeField] private TMP_Text status;
    [SerializeField] private float characterLabelWithPortraitMinX = 0.39f;
    [SerializeField] private float supportCardLabelWithPortraitMinX = 0.30f;

    private readonly Vector2[] labelAnchorMins = new Vector2[4];
    private GameClient client;
    private Action<string, DailyFeaturedGacha> onPull;
    private DailyGachaBannersResponse dailyBanners;
    private DateTimeOffset characterExpiry;
    private DateTimeOffset supportCardExpiry;
    private string pullError;
    private int generation;
    private double nextCountdownUpdate;

    public static GachaSelectionScreen Create(
        Transform parent, GameClient gameClient, Action<string, DailyFeaturedGacha> pull)
    {
        GameObject prefab = Resources.Load<GameObject>("Prefabs/GachaSelectionScreen");
        if (prefab == null)
        {
            Debug.LogError("Gacha selection prefab not found at Resources/Prefabs/GachaSelectionScreen.");
            return null;
        }
        GachaSelectionScreen template = prefab.GetComponent<GachaSelectionScreen>();
        if (template == null)
        {
            Debug.LogError("Gacha selection prefab is missing its GachaSelectionScreen component.", prefab);
            return null;
        }
        GachaSelectionScreen screen = Instantiate(template, parent, false);
        screen.gameObject.SetActive(false);
        if (!screen.Initialize(gameClient, pull))
        {
            Destroy(screen.gameObject);
            return null;
        }
        return screen;
    }

    private bool Initialize(GameClient gameClient, Action<string, DailyFeaturedGacha> pull)
    {
        if (backgroundButton == null || status == null || buttons == null || buttons.Length != 4
            || labels == null || labels.Length != 4 || portraits == null || portraits.Length != 4)
        {
            Debug.LogError("Gacha selection prefab has missing or invalid UI references.", this);
            return false;
        }
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null || labels[i] == null || portraits[i] == null)
            {
                Debug.LogError($"Gacha selection prefab has missing references for banner {i}.", this);
                return false;
            }
        }
        client = gameClient;
        onPull = pull;
        backgroundButton.onClick.AddListener(() => gameObject.SetActive(false));
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            labelAnchorMins[i] = labels[i].rectTransform.anchorMin;
            buttons[i].onClick.AddListener(() =>
            {
                DailyFeaturedGacha featured = index == 1 ? dailyBanners?.character
                    : index == 3 ? dailyBanners?.support_card : null;
                if ((index == 1 || index == 3) && featured == null) return;
                pullError = null;
                onPull(index < 2 ? "CHARACTER" : "SUPPORT_CARD", featured);
            });
        }
        return true;
    }

    public void Open()
    {
        pullError = null;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        RefreshBanners();
    }

    public void RefreshBanners()
    {
        if (!gameObject.activeInHierarchy) return;
        int requestGeneration = ++generation;
        string playerId = client.CurrentPlayer.playerId;
        dailyBanners = null;
        Refresh();
        status.text = pullError ?? "Loading daily featured gachas...";
        client.FetchDailyGachaBanners(response =>
        {
            if (!IsCurrent(requestGeneration, playerId)) return;
            characterExpiry = DateTimeOffset.Parse(response.character.ends_at, CultureInfo.InvariantCulture);
            supportCardExpiry = DateTimeOffset.Parse(response.support_card.ends_at, CultureInfo.InvariantCulture);
            dailyBanners = response;
            status.text = pullError ?? "Choose a gacha. Click the background to close.";
            Refresh();
        }, error =>
        {
            if (!IsCurrent(requestGeneration, playerId)) return;
            status.text = (pullError == null ? "" : pullError + "\n")
                + "Could not load daily gachas: " + error + " Close and reopen to retry.";
        });
    }

    public void ShowError(string error)
    {
        pullError = error;
        status.text = error;
    }

    private void OnDisable()
    {
        generation++;
    }

    private void Update()
    {
        if (dailyBanners == null || Time.realtimeSinceStartupAsDouble < nextCountdownUpdate) return;
        nextCountdownUpdate = Time.realtimeSinceStartupAsDouble + 1;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        UpdateDailyText(1, "Characters", dailyBanners.character, characterExpiry, now);
        UpdateDailyText(3, "Support Cards", dailyBanners.support_card, supportCardExpiry, now);
    }

    private bool IsCurrent(int requestGeneration, string playerId)
    {
        return gameObject.activeInHierarchy && generation == requestGeneration && client.IsLoggedIn
            && client.CurrentPlayer.playerId == playerId;
    }

    private void Refresh()
    {
        labels[0].text = StandardText("Characters");
        labels[2].text = StandardText("Support Cards");
        ShowDaily(1, "Characters", dailyBanners?.character, characterExpiry);
        ShowDaily(3, "Support Cards", dailyBanners?.support_card, supportCardExpiry);
    }

    private static string StandardText(string category)
    {
        return $"{category}\nGacha\n10 pulls - {GameClient.PackCost} tokens";
    }

    private void ShowDaily(int index, string category, DailyFeaturedGacha featured, DateTimeOffset expiresAt)
    {
        buttons[index].interactable = featured != null;
        if (featured == null)
        {
            labels[index].text = StandardText(category) + "\nFeatured: Loading...\nExpires in: --:--:--";
            SetPortrait(index, null);
            return;
        }

        UpdateDailyText(index, category, featured, expiresAt, DateTimeOffset.UtcNow);
        SetPortrait(index, index == 1 ? featured.character_id : featured.featured_id);
    }

    private void UpdateDailyText(
        int index, string category, DailyFeaturedGacha featured, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        labels[index].text = StandardText(category)
            + $"\nFeatured: {featured.name}\nExpires in: {FormatCountdown(expiresAt, now)}";
    }

    private static string FormatCountdown(DateTimeOffset expiresAt, DateTimeOffset now)
    {
        long seconds = Math.Max(0L, (long)Math.Ceiling((expiresAt - now).TotalSeconds));
        return $"{seconds / 3600:D2}:{seconds / 60 % 60:D2}:{seconds % 60:D2}";
    }

    private void SetPortrait(int index, string featuredId)
    {
        Sprite sprite = null;
        if (featuredId != null)
        {
            string path = (index == 1 ? "Images/Characters/" : "Images/Cards/") + featuredId;
            sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogWarning($"Featured image not found at Resources/{path}.", this);
            }
        }
        portraits[index].sprite = sprite;
        portraits[index].gameObject.SetActive(sprite != null);
        Vector2 anchor = labelAnchorMins[index];
        if (sprite != null)
        {
            float portraitMinX = index == 1 ? characterLabelWithPortraitMinX : supportCardLabelWithPortraitMinX;
            anchor.x = Mathf.Max(anchor.x, portraitMinX);
        }
        labels[index].rectTransform.anchorMin = anchor;
    }
}
