using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
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
    [SerializeField] private Canvas mainCanvas;
    [SerializeField] private Image loginField;
    [SerializeField] private TMP_InputField tmpPlayerIdInputField;
    [SerializeField] private Button connectButton;
    [SerializeField] private TMP_Text playerIdText;
    [SerializeField] private Button trainingButton;
    [SerializeField] private TMP_Text trainingButtonText;
    [SerializeField] private SupportCardTeamSelector supportCardTeamSelector;

    private GameObject characterSelectorInstance;
    private CharacterGridManager characterGridManager;
    private string selectedCharacterId;

    public PlayerData CurrentPlayer { get; private set; }
    public TrainingRunData CurrentTrainingRun { get; private set; }
    public bool IsLoggedIn => CurrentPlayer != null;
    public bool CanAffordPack => IsLoggedIn && CurrentPlayer.Tokens >= PackCost;

    public event Action OnLoginSuccess;
    public event Action OnPullStarted;

    /// <summary>Raised when a pull request completes, whether it succeeded or failed.</summary>
    public event Action OnPullFinished;

    public event Action<TrainingRunData> OnTrainingRunUpdated;

    private bool isCheckingTrainingState;
    private bool isStartingTraining;

    private void Start()
    {
        connectButton.onClick.AddListener(OnConnectButtonClicked);
        SubscribeToCardSelector();
        if (trainingButton != null)
        {
            trainingButton.onClick.AddListener(OnTrainingButtonClicked);
            trainingButton.gameObject.SetActive(false);
        }

        OnPullStarted += () => trainingButton.gameObject.SetActive(false);
        GachaItemSpawner.Instance.OnSequenceEnded += () => trainingButton.gameObject.SetActive(true);
        
        StartCoroutine(Get("/", _ => Debug.Log($"Backend reachable at {backendUrl}"), _ => { }));
    }

    protected override void OnDestroy()
    {
        connectButton.onClick.RemoveListener(OnConnectButtonClicked);
        if (trainingButton != null)
        {
            trainingButton.onClick.RemoveListener(OnTrainingButtonClicked);
        }

        if (supportCardTeamSelector != null)
        {
            supportCardTeamSelector.OnContinue -= StartTrainingWithCards;
        }

        if (characterSelectorInstance != null)
        {
            Destroy(characterSelectorInstance);
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

            FetchTrainingState();
            ShowStatus();
            OnLoginSuccess?.Invoke();
        }, error => ShowStatus($"Failed to connect: {error}")));
    }

    public void OnTrainingButtonClicked()
    {
        if (!IsLoggedIn || isCheckingTrainingState || isStartingTraining)
        {
            return;
        }

        isCheckingTrainingState = true;
        if (trainingButton != null)
        {
            trainingButton.interactable = false;
        }

        FetchTrainingState(run =>
        {
            isCheckingTrainingState = false;
            if (trainingButton != null)
            {
                trainingButton.interactable = true;
            }

            if (run != null && run.status == "active")
            {
                LoadTrainingSceneAdditive();
            }
            else
            {
                OpenCharacterSelector();
            }
        }, error =>
        {
            isCheckingTrainingState = false;
            if (trainingButton != null)
            {
                trainingButton.interactable = true;
            }

            ShowStatus($"Failed to load training state: {error}");
        });
    }

    private void OpenCharacterSelector()
    {
        if (characterSelectorInstance == null)
        {
            GameObject selectorPrefab = Resources.Load<GameObject>("Prefabs/CharacterSelectorScrollView");
            if (selectorPrefab == null)
            {
                ShowStatus("Character selector prefab could not be loaded.");
                return;
            }

            characterSelectorInstance = Instantiate(selectorPrefab, mainCanvas.transform, false);
            characterGridManager = characterSelectorInstance.GetComponent<CharacterGridManager>();
            if (characterGridManager == null)
            {
                characterGridManager = characterSelectorInstance.AddComponent<CharacterGridManager>();
            }
        }
        else
        {
            characterSelectorInstance.SetActive(true);
        }

        FetchCharacterSelection(characterId =>
            characterGridManager.Open(characterId, HandleCharacterSelected, ShowStatus),
            error => ShowStatus($"Failed to load saved character selection: {error}"));
    }

    private void HandleCharacterSelected(OwnedCharacterData character)
    {
        if (character == null || string.IsNullOrWhiteSpace(character.character_id))
        {
            ShowStatus("Selected character is invalid.");
            return;
        }

        SaveCharacterSelection(character.character_id, () =>
        {
            selectedCharacterId = character.character_id;
            if (characterSelectorInstance != null)
            {
                Destroy(characterSelectorInstance);
                characterSelectorInstance = null;
                characterGridManager = null;
            }

            OpenCardSelector();
        }, error => ShowStatus($"Failed to save character selection: {error}"));
    }

    private void OpenCardSelector()
    {
        SubscribeToCardSelector();
        if (supportCardTeamSelector == null)
        {
            ShowStatus("Card selector is not assigned.");
            return;
        }

        supportCardTeamSelector.gameObject.SetActive(true);
        FetchSupportCardPreset(cardIds =>
        {
            if (supportCardTeamSelector != null)
            {
                supportCardTeamSelector.LoadPreset(cardIds, error => ShowStatus(error));
            }
        }, error => ShowStatus($"Failed to load saved support card preset: {error}"));
    }

    private void SubscribeToCardSelector()
    {
        if (supportCardTeamSelector == null)
        {
            supportCardTeamSelector =
                FindFirstObjectByType<SupportCardTeamSelector>(FindObjectsInactive.Include);
        }

        if (supportCardTeamSelector == null)
        {
            return;
        }

        supportCardTeamSelector.OnContinue -= StartTrainingWithCards;
        supportCardTeamSelector.OnContinue += StartTrainingWithCards;
    }

    private void StartTrainingWithCards(SupportCardData[] selectedCards)
    {
        if (isStartingTraining)
        {
            return;
        }

        isStartingTraining = true;
        if (supportCardTeamSelector != null)
        {
            supportCardTeamSelector.gameObject.SetActive(false);
        }

        StartOrResumeTraining(selectedCharacterId, selectedCards, _ =>
        {
            isStartingTraining = false;
            LoadTrainingSceneAdditive();
        }, error =>
        {
            isStartingTraining = false;
            ShowStatus($"Training failed to start: {error}");
            if (supportCardTeamSelector != null)
            {
                supportCardTeamSelector.gameObject.SetActive(true);
            }
        });
    }

    public void LoadTrainingSceneAdditive()
    {
        mainCanvas.gameObject.SetActive(false);

        if (!SceneManager.GetSceneByName(TrainingSceneName).isLoaded)
        {
            SceneManager.LoadSceneAsync(TrainingSceneName, LoadSceneMode.Additive);
        }
    }

    public void UnloadTrainingScene(Action onCompleted = null)
    {
        if (SceneManager.GetSceneByName(TrainingSceneName).isLoaded)
        {
            AsyncOperation op = SceneManager.UnloadSceneAsync(TrainingSceneName);
            if (op != null)
            {
                op.completed += _ =>
                {
                    mainCanvas.gameObject.SetActive(true);
                    onCompleted?.Invoke();
                };
                return;
            }
        }

        mainCanvas.gameObject.SetActive(true);
        UpdateTrainingButtonLabel();
        onCompleted?.Invoke();
    }

    public void UpdateTrainingButtonLabel()
    {
        string labelText = (CurrentTrainingRun != null && CurrentTrainingRun.status == "active")
            ? "Continue Training"
            : "Start Training";

        if (trainingButtonText != null)
        {
            trainingButtonText.text = labelText;
        }
        else if (trainingButton != null)
        {
            TMP_Text childText = trainingButton.GetComponentInChildren<TMP_Text>();
            if (childText != null)
            {
                childText.text = labelText;
            }
        }
    }

    public void FetchTrainingState(Action<TrainingRunData> onSuccess = null, Action<string> onError = null)
    {
        if (!IsLoggedIn) return;

        string query = $"/training/state?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}";
        StartCoroutine(Get(query, json =>
        {
            TrainingResponse response = JsonUtility.FromJson<TrainingResponse>(json);
            CurrentTrainingRun = response.run;
            UpdateTrainingButtonLabel();
            OnTrainingRunUpdated?.Invoke(CurrentTrainingRun);
            EnsureTrainingRunPresetSaved(CurrentTrainingRun);
            onSuccess?.Invoke(CurrentTrainingRun);
        }, error =>
        {
            CurrentTrainingRun = null;
            UpdateTrainingButtonLabel();
            onError?.Invoke(error);
        }, () =>
        {
            CurrentTrainingRun = null;
            UpdateTrainingButtonLabel();
            OnTrainingRunUpdated?.Invoke(null);
            onSuccess?.Invoke(null);
        }));
    }

    public void StartOrResumeTraining(Action<TrainingRunData> onSuccess, Action<string> onError = null)
    {
        if (!IsLoggedIn) return;
        RequestStartOrResumeTraining(null, null, onSuccess, onError);
    }

    public void StartOrResumeTraining(
            SupportCardData[] supportCards, Action<TrainingRunData> onSuccess, Action<string> onError = null)
    {
        StartOrResumeTraining(selectedCharacterId, supportCards, onSuccess, onError);
    }

    public void StartOrResumeTraining(
            string characterId, SupportCardData[] supportCards,
            Action<TrainingRunData> onSuccess, Action<string> onError = null)
    {
        if (!IsLoggedIn) return;
        if (!HasSixUniqueCards(supportCards))
        {
            onError?.Invoke("Select exactly 6 cards with unique IDs.");
            return;
        }

        string supportCardIds = string.Join(",", Array.ConvertAll(supportCards, card => card.id));
        RequestStartOrResumeTraining(characterId, supportCardIds, onSuccess, onError);
    }

    private void RequestStartOrResumeTraining(
            string characterId, string supportCardIds, Action<TrainingRunData> onSuccess, Action<string> onError)
    {
        string query = $"/training/start-or-resume?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}";
        if (!string.IsNullOrWhiteSpace(characterId))
        {
            query += $"&character_id={UnityWebRequest.EscapeURL(characterId)}";
        }

        if (!string.IsNullOrEmpty(supportCardIds))
        {
            query += $"&support_card_ids={UnityWebRequest.EscapeURL(supportCardIds)}";
        }

        StartCoroutine(Get(query, json =>
        {
            TrainingResponse response = JsonUtility.FromJson<TrainingResponse>(json);
            CurrentTrainingRun = response.run;
            UpdateTrainingButtonLabel();
            OnTrainingRunUpdated?.Invoke(CurrentTrainingRun);
            onSuccess?.Invoke(CurrentTrainingRun);
        }, error => { onError?.Invoke(error); }));
    }

    private static bool HasSixUniqueCards(SupportCardData[] cards)
    {
        if (cards == null || cards.Length != 6)
        {
            return false;
        }

        var cardIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (SupportCardData card in cards)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.id) || !cardIds.Add(card.id))
            {
                return false;
            }
        }

        return true;
    }

    public void EndAndResetTraining(Action onSuccess = null, Action<string> onError = null)
    {
        if (!IsLoggedIn) return;

        string query = $"/training/reset?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}";
        StartCoroutine(Get(query, json =>
        {
            CurrentTrainingRun = null;
            UpdateTrainingButtonLabel();
            OnTrainingRunUpdated?.Invoke(null);
            onSuccess?.Invoke();
        }, error =>
        {
            CurrentTrainingRun = null;
            UpdateTrainingButtonLabel();
            OnTrainingRunUpdated?.Invoke(null);
            onError?.Invoke(error);
        }));
    }

    public void TrainFacility(string facility, Action<TrainingResponse> onSuccess, Action<string> onError = null)
    {
        if (!IsLoggedIn) return;

        string query =
            $"/training/train?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}&facility={UnityWebRequest.EscapeURL(facility)}";
        StartCoroutine(Get(query, json =>
        {
            TrainingResponse response = JsonUtility.FromJson<TrainingResponse>(json);
            CurrentTrainingRun = response.run;
            UpdateTrainingButtonLabel();
            OnTrainingRunUpdated?.Invoke(CurrentTrainingRun);
            onSuccess?.Invoke(response);
        }, error => { onError?.Invoke(error); }));
    }

    public void RestTraining(Action<TrainingResponse> onSuccess, Action<string> onError = null)
    {
        if (!IsLoggedIn) return;

        string query = $"/training/rest?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}";
        StartCoroutine(Get(query, json =>
        {
            TrainingResponse response = JsonUtility.FromJson<TrainingResponse>(json);
            CurrentTrainingRun = response.run;
            UpdateTrainingButtonLabel();
            OnTrainingRunUpdated?.Invoke(CurrentTrainingRun);
            onSuccess?.Invoke(response);
        }, error => { onError?.Invoke(error); }));
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

        string query =
            $"/gacha-pack?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}&target={UnityWebRequest.EscapeURL(banner)}";
        StartCoroutine(Get(query, json =>
        {
            GachaResponse response = JsonUtility.FromJson<GachaResponse>(json);
            if (response.rewards == null || response.rewards.Length
                == 0)
            {
                throw new FormatException("response contained no rewards");
            }

            CurrentPlayer.stats = response.stats ?? CurrentPlayer.stats;
            CurrentPlayer.AddRewards(response.rewards, banner.Equals("CHARACTER"));
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

    public void FetchSupportCards(Action<SupportCardData[]> onSuccess, Action<string> onError = null)
    {
        if (!IsLoggedIn)
        {
            onError?.Invoke("Connect to a player before fetching support cards.");
            return;
        }

        string query = $"/player-support-cards?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}";
        StartCoroutine(Get(query, json =>
        {
            SupportCardsResponse response = JsonUtility.FromJson<SupportCardsResponse>(json);
            if (response == null || !response.success || response.support_cards == null)
            {
                throw new FormatException("response did not contain support cards");
            }

            onSuccess?.Invoke(response.support_cards);
        }, error => onError?.Invoke(error)));
    }

    public void FetchPlayerCharacters(Action<OwnedCharacterData[]> onSuccess, Action<string> onError = null)
    {
        if (!IsLoggedIn)
        {
            onError?.Invoke("Connect to a player before fetching characters.");
            return;
        }

        string query = $"/player-characters?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}";
        StartCoroutine(Get(query, json =>
        {
            PlayerCharactersResponse response = JsonUtility.FromJson<PlayerCharactersResponse>(json);
            if (response == null || !response.success || response.characters == null)
            {
                throw new FormatException("response did not contain player characters");
            }

            onSuccess?.Invoke(response.characters);
        }, error => onError?.Invoke(error)));
    }

    public void FetchCharacterSelection(Action<string> onSuccess, Action<string> onError = null)
    {
        if (!IsLoggedIn)
        {
            onError?.Invoke("Connect to a player before loading a character selection.");
            return;
        }

        string query =
            $"/player-support-card-presets?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}&preset_id=default";
        StartCoroutine(Get(query, json =>
        {
            SupportCardPresetResponse response = JsonUtility.FromJson<SupportCardPresetResponse>(json);
            if (response == null || !response.success)
            {
                throw new FormatException("response did not contain a character selection");
            }

            onSuccess?.Invoke(response.character_id);
        }, error => onError?.Invoke(error)));
    }

    public void FetchSupportCardPreset(Action<string[]> onSuccess, Action<string> onError = null)
    {
        if (!IsLoggedIn)
        {
            onError?.Invoke("Connect to a player before loading a support card preset.");
            return;
        }

        string query =
            $"/player-support-card-presets?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}&preset_id=default";
        StartCoroutine(Get(query, json =>
        {
            SupportCardPresetResponse response = JsonUtility.FromJson<SupportCardPresetResponse>(json);
            if (response == null || !response.success || response.support_card_ids == null)
            {
                throw new FormatException("response did not contain a support card preset");
            }

            onSuccess?.Invoke(response.support_card_ids);
        }, error => onError?.Invoke(error)));
    }

    private void EnsureTrainingRunPresetSaved(TrainingRunData run)
    {
        if (run?.support_cards == null || run.support_cards.Length != 6)
        {
            return;
        }

        FetchSupportCardPreset(cardIds =>
        {
            if (cardIds.Length == 0)
            {
                SaveSupportCardPreset(run.support_cards, error =>
                    Debug.LogWarning($"Failed to save the previous training card preset: {error}"));
            }
        }, error => Debug.LogWarning($"Failed to check the saved training card preset: {error}"));
    }

    private void SaveSupportCardPreset(SupportCardData[] cards, Action<string> onError = null)
    {
        if (!HasSixUniqueCards(cards))
        {
            onError?.Invoke("Select exactly 6 cards with unique IDs.");
            return;
        }

        var update = new SupportCardPresetUpdateRequest
        {
            support_card_ids = Array.ConvertAll(cards, card => card.id)
        };
        string query = $"/player-support-card-presets?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}";
        StartCoroutine(Post(query, JsonUtility.ToJson(update), null, error => onError?.Invoke(error)));
    }

    private void SaveCharacterSelection(string characterId, Action onSuccess, Action<string> onError = null)
    {
        var update = new SupportCardPresetUpdateRequest
        {
            character_id = characterId
        };
        string query = $"/player-support-card-presets?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}";
        StartCoroutine(Post(query, JsonUtility.ToJson(update), onSuccess, onError));
    }

    private IEnumerator Post(string path, string json, Action onSuccess, Action<string> onError)
    {
        string url = backendUrl.TrimEnd('/') + path;
        using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            string body = request.downloadHandler.text;
            Debug.LogError($"POST {url} failed: {request.error} (HTTP {request.responseCode}) {body}");
            onError?.Invoke(ParseErrorMessage(body) ?? request.error);
            yield break;
        }

        onSuccess?.Invoke();
    }

    /// <summary>
    /// Shows the logged-in player's name and tokens, optionally followed by an error line.
    /// </summary>
    public void ShowStatus(string errorMessage = null)
    {
        string text = IsLoggedIn
            ? $"Logged in as: {CurrentPlayer.DisplayName}\nTokens: {CurrentPlayer.Tokens}"
            : string.Empty;
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
    private IEnumerator Get(string path, Action<string> onSuccess, Action<string> onError,
        Action onNoActiveTrainingRun = null)
    {
        string url = backendUrl.TrimEnd('/') + path;
        using UnityWebRequest request = UnityWebRequest.Get(url);
        yield return request.SendWebRequest();

        string body = request.downloadHandler.text;
        if (request.result != UnityWebRequest.Result.Success)
        {
            if (request.responseCode == 404
                && onNoActiveTrainingRun != null
                && ParseErrorMessage(body) == "No active training run found for player")
            {
                onNoActiveTrainingRun();
                yield break;
            }

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

            return error.required_tokens > 0
                ? $"{error.message} ({error.required_tokens} tokens required)"
                : error.message;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}