using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class GameClient : MonoBehaviour
{
    public const int PackCost = 900;

    [SerializeField] private string backendUrl = "http://127.0.0.1:5000";

    [SerializeField] private Image loginField;

    [SerializeField] private TMP_InputField tmpPlayerIdInputField;

    [SerializeField] private Button connectButton;

    [SerializeField] private TMP_Text playerIdText;

    [Header("Player Data")]
    [SerializeField] private PlayerData currentPlayer;

    public PlayerData CurrentPlayer => currentPlayer;

    [Header("Gacha")]
    [SerializeField] private Button pullButton;
    [SerializeField] private GachaItemSpawner gachaSpawner;

    public string BackendUrl
    {
        get => backendUrl;
        set => backendUrl = value;
    }

    public TMP_Text PlayerIdText
    {
        get => playerIdText;
        set => playerIdText = value;
    }

    public Button PullButton
    {
        get => pullButton;
        set => pullButton = value;
    }

    public GachaItemSpawner GachaSpawner
    {
        get => gachaSpawner;
        set
        {
            if (gachaSpawner != value)
            {
                gachaSpawner.OnSequenceEnded -= HandleSequenceEnded;
                gachaSpawner = value;
                gachaSpawner.OnSequenceEnded += HandleSequenceEnded;
            }
        }
    }

    private void Start()
    {
        StartCoroutine(CheckHealth());

        connectButton.onClick.AddListener(OnConnectButtonClicked);
        pullButton.onClick.AddListener(OnPullButtonClicked);
        pullButton.gameObject.SetActive(false);
        gachaSpawner.OnSequenceEnded += HandleSequenceEnded;
    }

    private void OnDestroy()
    {
        connectButton.onClick.RemoveListener(OnConnectButtonClicked);
        pullButton.onClick.RemoveListener(OnPullButtonClicked);
        gachaSpawner.OnSequenceEnded -= HandleSequenceEnded;
    }

    private void HandleSequenceEnded()
    {
        if (currentPlayer != null && !string.IsNullOrEmpty(currentPlayer.playerId))
        {
            pullButton.gameObject.SetActive(true);
        }
    }

    public void OnPullButtonClicked()
    {
        // Rejection check when local tokens do not suffice
        if (currentPlayer != null && currentPlayer.Tokens < PackCost)
        {
            UpdatePlayerInfoUI($"Insufficient tokens ({PackCost} tokens required)");
            if (!gachaSpawner.IsSequenceActive)
            {
                pullButton.gameObject.SetActive(true);
            }
            return;
        }

        pullButton.gameObject.SetActive(false);

        StartCoroutine(PullGachaPack());
    }

    public void OnConnectButtonClicked()
    {
        string playerId = GetPlayerId();
        if (string.IsNullOrWhiteSpace(playerId))
        {
            Debug.LogWarning("Player ID cannot be empty.");
            return;
        }

        StartCoroutine(Connect(playerId.Trim()));
    }

    private string GetPlayerId()
    {
        if (!string.IsNullOrEmpty(tmpPlayerIdInputField.text))
        {
            return tmpPlayerIdInputField.text;
        }

        return string.Empty;
    }

    public void UpdatePlayerInfoUI(string errorMessage = null)
    {
        if (currentPlayer == null || string.IsNullOrEmpty(currentPlayer.playerId))
        {
            playerIdText.text = string.Empty;
            return;
        }

        string displayName = currentPlayer.DisplayName;
        int tokens = currentPlayer.Tokens;
        string text = $"Logged in as: {displayName}\nTokens: {tokens}";

        if (!string.IsNullOrEmpty(errorMessage))
        {
            text += $"\n<color=red>{errorMessage}</color>";
        }

        playerIdText.text = text;
    }

    public void RefreshPlayerStats()
    {
        if (currentPlayer != null && !string.IsNullOrEmpty(currentPlayer.playerId))
        {
            StartCoroutine(FetchPlayerStats(currentPlayer.playerId));
        }
    }

    public IEnumerator FetchPlayerStats(string playerId, System.Action<PlayerStats> onComplete = null)
    {
        if (string.IsNullOrEmpty(playerId))
        {
            yield break;
        }

        string escapedId = UnityWebRequest.EscapeURL(playerId);
        string url = $"{backendUrl.TrimEnd('/')}/player-stats?player_id={escapedId}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string jsonResponse = request.downloadHandler.text;
                try
                {
                    PlayerStatsResponse response = JsonUtility.FromJson<PlayerStatsResponse>(jsonResponse);
                    if (response != null && response.stats != null)
                    {
                        if (currentPlayer != null && currentPlayer.playerId == (string.IsNullOrEmpty(response.player_id) ? playerId : response.player_id))
                        {
                            currentPlayer.stats = response.stats;
                            if (!string.IsNullOrEmpty(response.stats.name))
                            {
                                currentPlayer.playerName = response.stats.name;
                            }
                            UpdatePlayerInfoUI();
                        }
                        onComplete?.Invoke(response.stats);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"Failed to parse player stats response: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"Failed to fetch player stats for '{playerId}': {request.error}");
            }
        }
    }

    private IEnumerator CheckHealth()
    {
        string url = backendUrl.TrimEnd('/') + "/";
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to connect to backend at {url}: {request.error}");
            }
            else
            {
                Debug.Log($"Connected to backend at {url}. Response: {request.downloadHandler.text}");
            }
        }
    }

    private IEnumerator Connect(string playerId)
    {
        string escapedId = UnityWebRequest.EscapeURL(playerId);
        string url = $"{backendUrl.TrimEnd('/')}/connect?player_id={escapedId}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to connect player '{playerId}' to backend at {url}: {request.error}");
                UpdatePlayerInfoUI($"Failed to connect: {request.error}");
            }
            else
            {
                string jsonResponse = request.downloadHandler.text;
                Debug.Log($"Successfully connected player '{playerId}'! Response: {jsonResponse}");

                try
                {
                    ConnectResponse response = JsonUtility.FromJson<ConnectResponse>(jsonResponse);
                    if (response != null && response.userdata != null)
                    {
                        currentPlayer = new PlayerData(
                            string.IsNullOrEmpty(response.player_id) ? playerId : response.player_id,
                            response.userdata);
                    }
                    else
                    {
                        currentPlayer = new PlayerData(playerId, new UserData());
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Failed to parse connect response: {ex.Message}");
                    currentPlayer = new PlayerData(playerId, new UserData());
                }

                loginField.gameObject.SetActive(false);

                UpdatePlayerInfoUI();

                if (!gachaSpawner.IsSequenceActive)
                {
                    pullButton.gameObject.SetActive(true);
                }

                // Query /player-stats to retrieve the player's stored name and synced stats
                StartCoroutine(FetchPlayerStats(currentPlayer.playerId));
            }
        }
    }

    private IEnumerator PullGachaPack()
    {
        string playerId = currentPlayer != null && !string.IsNullOrEmpty(currentPlayer.playerId)
            ? currentPlayer.playerId
            : GetPlayerId();

        string url = string.IsNullOrEmpty(playerId)
            ? $"{backendUrl.TrimEnd('/')}/gacha-pack"
            : $"{backendUrl.TrimEnd('/')}/gacha-pack?player_id={UnityWebRequest.EscapeURL(playerId.Trim())}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                Debug.LogError($"Failed to pull gacha pack from backend at {url}: {request.error} (HTTP {request.responseCode}). Response: {responseText}");

                string errorMessage = "Failed to pull gacha pack.";

                if (request.responseCode == 403 || (!string.IsNullOrEmpty(responseText) && responseText.Contains("insufficient tokens")))
                {
                    try
                    {
                        ErrorResponse errorResp = JsonUtility.FromJson<ErrorResponse>(responseText);
                        if (errorResp != null && !string.IsNullOrEmpty(errorResp.message))
                        {
                            int reqTokens = errorResp.required_tokens > 0 ? errorResp.required_tokens : PackCost;
                            errorMessage = $"{errorResp.message} ({reqTokens} tokens required)";
                        }
                        else
                        {
                            errorMessage = $"Insufficient tokens ({PackCost} tokens required)";
                        }
                    }
                    catch
                    {
                        errorMessage = $"Insufficient tokens ({PackCost} tokens required)";
                    }
                }
                else if (!string.IsNullOrEmpty(responseText))
                {
                    try
                    {
                        ErrorResponse errorResp = JsonUtility.FromJson<ErrorResponse>(responseText);
                        if (errorResp != null && !string.IsNullOrEmpty(errorResp.message))
                        {
                            errorMessage = errorResp.message;
                        }
                    }
                    catch
                    {
                        // keep default errorMessage
                    }
                }

                UpdatePlayerInfoUI(errorMessage);

                if (!gachaSpawner.IsSequenceActive)
                {
                    pullButton.gameObject.SetActive(true);
                }
            }
            else
            {
                string jsonResponse = request.downloadHandler.text;
                Debug.Log($"Successfully received gacha pack! Response: {jsonResponse}");

                try
                {
                    GachaResponse response = JsonUtility.FromJson<GachaResponse>(jsonResponse);
                    if (response != null && response.rewards != null)
                    {
                        // Update player stats and token balance immediately from gacha pack response
                        if (response.stats != null)
                        {
                            if (currentPlayer == null)
                            {
                                currentPlayer = new PlayerData(
                                    string.IsNullOrEmpty(response.player_id) ? playerId : response.player_id,
                                    response.stats);
                            }
                            else
                            {
                                currentPlayer.stats = response.stats;
                                if (!string.IsNullOrEmpty(response.stats.name))
                                {
                                    currentPlayer.playerName = response.stats.name;
                                }
                            }
                        }

                        // Clear any error message and update token display immediately
                        UpdatePlayerInfoUI();

                        if (currentPlayer != null && response.rewards != null)
                        {
                            currentPlayer.AddRewards(response.rewards);
                        }

                        gachaSpawner.AssembleRewards(response.rewards);
                        if (gachaSpawner.Strategy != SpawnStrategy.SingleItemOnClick)
                        {
                            pullButton.gameObject.SetActive(true);
                        }
                    }
                    else
                    {
                        Debug.LogWarning("Gacha response contained no rewards or could not be parsed.");
                        UpdatePlayerInfoUI("Invalid response from server.");
                        pullButton.gameObject.SetActive(true);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Failed to parse gacha response: {ex.Message}");
                    UpdatePlayerInfoUI("Failed to parse reward data.");
                    pullButton.gameObject.SetActive(true);
                }
            }
        }
    }
}