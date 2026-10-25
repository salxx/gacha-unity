using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Talks to the gacha backend: login, player stats and pack pulls.
/// </summary>
public class GameClient : Singleton<GameClient>
{
    public const int PackCost = 900;
    public const string TrainingSceneName = "TrainingScene";

    [SerializeField] private string backendUrl = "http://127.0.0.1:5000";
    [SerializeField] private Image loginField;
    [SerializeField] private TMP_InputField tmpPlayerIdInputField;
    [SerializeField] private Button connectButton;
    [SerializeField] private TMP_Text playerIdText;
    [SerializeField] private Button trainingButton;

    public PlayerData CurrentPlayer { get; private set; }
    public TrainingRunData CurrentTrainingRun { get; private set; }
    public bool IsLoggedIn => CurrentPlayer != null;
    public bool CanAffordPack => IsLoggedIn && CurrentPlayer.Tokens >= PackCost;

    public event Action OnLoginSuccess;
    public event Action OnPullStarted;
    /// <summary>Raised when a pull request completes, whether it succeeded or failed.</summary>
    public event Action OnPullFinished;
    public event Action<TrainingRunData> OnTrainingRunUpdated;

    private void Start()
    {
        connectButton.onClick.AddListener(OnConnectButtonClicked);
        if (trainingButton != null)
        {
            trainingButton.onClick.AddListener(OnTrainingButtonClicked);
            trainingButton.gameObject.SetActive(false);
        }

        StartCoroutine(Get("/", _ => Debug.Log($"Backend reachable at {backendUrl}"), _ => { }));
    }

    protected override void OnDestroy()
    {
        connectButton.onClick.RemoveListener(OnConnectButtonClicked);
        if (trainingButton != null)
        {
            trainingButton.onClick.RemoveListener(OnTrainingButtonClicked);
        }
        base.OnDestroy();
    }

    private void OnConnectButtonClicked()
    {
        string playerId = tmpPlayerIdInputField.text.Trim();
        if (playerId.Length == 0)
        {
            ShowStatus("Player ID cannot be empty.");
            return;
        }

        StartCoroutine(Get($"/connect?player_id={UnityWebRequest.EscapeURL(playerId)}", json =>
        {
            ConnectResponse response = JsonUtility.FromJson<ConnectResponse>(json);
            CurrentPlayer = new PlayerData(response.player_id ?? playerId, response.stats);
            loginField.gameObject.SetActive(false);
            if (trainingButton != null)
            {
                trainingButton.gameObject.SetActive(true);
            }
            ShowStatus();
            OnLoginSuccess?.Invoke();
        }, error => ShowStatus($"Failed to connect: {error}")));
    }

    public void OnTrainingButtonClicked()
    {
        if (!IsLoggedIn)
        {
            return;
        }

        StartOrResumeTraining(run =>
        {
            LoadTrainingSceneAdditive();
        }, error => ShowStatus($"Training failed to load: {error}"));
    }

    public void LoadTrainingSceneAdditive()
    {
        if (!SceneManager.GetSceneByName(TrainingSceneName).isLoaded)
        {
            SceneManager.LoadSceneAsync(TrainingSceneName, LoadSceneMode.Additive);
        }
    }

    public void StartOrResumeTraining(Action<TrainingRunData> onSuccess, Action<string> onError = null)
    {
        if (!IsLoggedIn) return;

        string query = $"/training/start-or-resume?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}";
        StartCoroutine(Get(query, json =>
        {
            TrainingResponse response = JsonUtility.FromJson<TrainingResponse>(json);
            CurrentTrainingRun = response.run;
            OnTrainingRunUpdated?.Invoke(CurrentTrainingRun);
            onSuccess?.Invoke(CurrentTrainingRun);
        }, error =>
        {
            onError?.Invoke(error);
        }));
    }

    public void TrainFacility(string facility, Action<TrainingResponse> onSuccess, Action<string> onError = null)
    {
        if (!IsLoggedIn) return;

        string query = $"/training/train?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}&facility={UnityWebRequest.EscapeURL(facility)}";
        StartCoroutine(Get(query, json =>
        {
            TrainingResponse response = JsonUtility.FromJson<TrainingResponse>(json);
            CurrentTrainingRun = response.run;
            OnTrainingRunUpdated?.Invoke(CurrentTrainingRun);
            onSuccess?.Invoke(response);
        }, error =>
        {
            onError?.Invoke(error);
        }));
    }

    public void RestTraining(Action<TrainingResponse> onSuccess, Action<string> onError = null)
    {
        if (!IsLoggedIn) return;

        string query = $"/training/rest?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}";
        StartCoroutine(Get(query, json =>
        {
            TrainingResponse response = JsonUtility.FromJson<TrainingResponse>(json);
            CurrentTrainingRun = response.run;
            OnTrainingRunUpdated?.Invoke(CurrentTrainingRun);
            onSuccess?.Invoke(response);
        }, error =>
        {
            onError?.Invoke(error);
        }));
    }

    public void Pull(string banner)
    {
        if (!IsLoggedIn)
        {
            return;
        }

        if (!CanAffordPack)
        {
            ShowStatus($"Insufficient tokens ({PackCost} tokens required)");
            OnPullFinished?.Invoke();
            return;
        }

        OnPullStarted?.Invoke();

        string query = $"/gacha-pack?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}&target={UnityWebRequest.EscapeURL(banner)}";
        StartCoroutine(Get(query, json =>
        {
            GachaResponse response = JsonUtility.FromJson<GachaResponse>(json);
            if (response.rewards == null || response.rewards.Length == 0)
            {
                throw new FormatException("response contained no rewards");
            }

            CurrentPlayer.stats = response.stats ?? CurrentPlayer.stats;
            CurrentPlayer.AddRewards(response.rewards);
            ShowStatus();
            GachaItemSpawner.Instance.AssembleRewards(response.rewards);
            OnPullFinished?.Invoke();
        }, error =>
        {
            ShowStatus(error);
            OnPullFinished?.Invoke();
        }));
    }

    public void RefreshPlayerStats()
    {
        if (!IsLoggedIn)
        {
            return;
        }

        StartCoroutine(Get($"/player-stats?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}", json =>
        {
            CurrentPlayer.stats = JsonUtility.FromJson<PlayerStatsResponse>(json).stats ?? CurrentPlayer.stats;
            ShowStatus();
        }, error => Debug.LogWarning($"Failed to refresh player stats: {error}")));
    }

    /// <summary>
    /// Shows the logged-in player's name and tokens, optionally followed by an error line.
    /// </summary>
    public void ShowStatus(string errorMessage = null)
    {
        string text = IsLoggedIn ? $"Logged in as: {CurrentPlayer.DisplayName}\nTokens: {CurrentPlayer.Tokens}" : string.Empty;
        if (!string.IsNullOrEmpty(errorMessage))
        {
            text += $"\n<color=red>{errorMessage}</color>";
        }

        playerIdText.text = text;
    }

    /// <summary>
    /// Sends a GET request to the backend. onError receives the server's message when available.
    /// Exceptions thrown by onSuccess (e.g. malformed JSON) are reported through onError as well.
    /// </summary>
    private IEnumerator Get(string path, Action<string> onSuccess, Action<string> onError)
    {
        string url = backendUrl.TrimEnd('/') + path;
        using UnityWebRequest request = UnityWebRequest.Get(url);
        yield return request.SendWebRequest();

        string body = request.downloadHandler.text;
        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"GET {url} failed: {request.error} (HTTP {request.responseCode}) {body}");
            onError(ParseErrorMessage(body) ?? request.error);
            yield break;
        }

        try
        {
            onSuccess(body);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to handle response from {url}: {ex.Message}\n{body}");
            onError("Invalid response from server.");
        }
    }

    private static string ParseErrorMessage(string body)
    {
        try
        {
            ErrorResponse error = JsonUtility.FromJson<ErrorResponse>(body);
            if (string.IsNullOrEmpty(error?.message))
            {
                return null;
            }

            return error.required_tokens > 0 ? $"{error.message} ({error.required_tokens} tokens required)" : error.message;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
