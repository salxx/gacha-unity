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
    public const string BattleSceneName = "BattleScene";

    [SerializeField] private string backendUrl = "http://127.0.0.1:5000";
    [SerializeField] private Canvas mainCanvas;
    [SerializeField] private TMP_Text playerIdText;
    [SerializeField] private Button trainingButton;
    [SerializeField] private TMP_Text trainingButtonText;
    [SerializeField] private Button battleButton;
    [SerializeField] private SupportCardTeamSelector supportCardTeamSelector;
    [SerializeField] private GameObject characterSelectorBackground;
    [SerializeField] private CharacterGridManager characterGridManager;

    private Button characterSelectorBackgroundButton;
    private string selectedCharacterId;
    private int characterSelectionGeneration;
    private bool isLoadingBattle;

    public PlayerData CurrentPlayer { get; private set; }
    public string BackendUrl => backendUrl;
    public TrainingRunData CurrentTrainingRun { get; private set; }
    public string[] TrainingTexts { get; private set; } = Array.Empty<string>();
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
        SubscribeToCardSelector();
        InitializeCharacterSelectorBackground();
        if (trainingButton != null)
        {
            trainingButton.onClick.AddListener(OnTrainingButtonClicked);
            trainingButton.gameObject.SetActive(false);
        }
        if (battleButton != null)
        {
            battleButton.onClick.AddListener(OnBattleButtonClicked);
            battleButton.gameObject.SetActive(false);
        }

        OnPullStarted += HideTrainingButton;
        
        StartCoroutine(Get("/", _ => Debug.Log($"Backend reachable at {backendUrl}"), _ => { }));
    }

    protected override void OnDestroy()
    {
        OnPullStarted -= HideTrainingButton;
        if (characterSelectorBackgroundButton != null)
        {
            characterSelectorBackgroundButton.onClick.RemoveListener(CancelCharacterSelection);
        }

        if (characterGridManager != null)
        {
            characterGridManager.OnBackgroundClicked -= CancelCharacterSelection;
        }

        if (trainingButton != null)
        {
            trainingButton.onClick.RemoveListener(OnTrainingButtonClicked);
        }

        if (battleButton != null)
        {
            battleButton.onClick.RemoveListener(OnBattleButtonClicked);
        }

        if (supportCardTeamSelector != null)
        {
            supportCardTeamSelector.OnContinue -= StartTrainingWithCards;
            supportCardTeamSelector.OnCancel -= CancelTrainingSelection;
        }

        base.OnDestroy();
    }

    public void Login(string playerId, Action onSuccess, Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            onError?.Invoke("Player ID cannot be empty.");
            return;
        }

        string normalizedPlayerId = playerId.Trim();
        StartCoroutine(Get($"/connect?player_id={UnityWebRequest.EscapeURL(normalizedPlayerId)}", json =>
        {
            ConnectResponse response = JsonUtility.FromJson<ConnectResponse>(json);
            if (response == null || !response.success)
            {
                throw new FormatException("response did not confirm a successful login");
            }

            CurrentPlayer = new PlayerData(response.player_id ?? normalizedPlayerId, response.stats);
            TrainingTexts = Array.Empty<string>();
            if (trainingButton != null)
            {
                trainingButton.gameObject.SetActive(true);
            }
            if (battleButton != null)
            {
                battleButton.gameObject.SetActive(true);
            }

            FetchTrainingState();
            ShowStatus();
            OnLoginSuccess?.Invoke();
            onSuccess?.Invoke();
        }, error => onError?.Invoke(error)));
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

            if (run != null && (run.status == "active" || run.status == "completed"))
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
        if (characterSelectorBackground == null || characterGridManager == null)
        {
            ShowStatus("Character selector background or grid is not assigned.");
            return;
        }

        int selectionGeneration = ++characterSelectionGeneration;
        characterSelectorBackground.SetActive(true);
        FetchCharacterSelection(characterId =>
            characterGridManager.Open(
                characterId,
                character => HandleCharacterSelected(selectionGeneration, character),
                ShowStatus),
            error =>
            {
                if (selectionGeneration == characterSelectionGeneration)
                {
                    characterSelectorBackground.SetActive(false);
                    ShowStatus($"Failed to load saved character selection: {error}");
                }
            });
    }

    private void HandleCharacterSelected(int selectionGeneration, OwnedCharacterData character)
    {
        if (character == null || string.IsNullOrWhiteSpace(character.character_id))
        {
            ShowStatus("Selected character is invalid.");
            return;
        }

        SaveCharacterSelection(character.character_id, () =>
        {
            if (selectionGeneration != characterSelectionGeneration)
            {
                return;
            }

            selectedCharacterId = character.character_id;
            characterSelectorBackground.SetActive(false);
            OpenCardSelector();
        }, error => ShowStatus($"Failed to save character selection: {error}"));
    }

    private void InitializeCharacterSelectorBackground()
    {
        if (characterSelectorBackground == null)
        {
            Debug.LogError("Character selector background is not assigned.", this);
            return;
        }

        Image backgroundImage = characterSelectorBackground.GetComponent<Image>();
        if (backgroundImage == null)
        {
            Debug.LogError("Character selector background does not have an Image component.", this);
            return;
        }

        characterSelectorBackgroundButton = characterSelectorBackground.GetComponent<Button>();
        if (characterSelectorBackgroundButton == null)
        {
            characterSelectorBackgroundButton = characterSelectorBackground.AddComponent<Button>();
        }

        characterSelectorBackgroundButton.targetGraphic = backgroundImage;
        characterSelectorBackgroundButton.transition = Selectable.Transition.None;
        characterSelectorBackgroundButton.onClick.AddListener(CancelCharacterSelection);

        if (characterGridManager == null)
        {
            Debug.LogError("Character grid manager is not assigned.", this);
            return;
        }

        if (characterGridManager.GetComponent<Image>() == null)
        {
            Debug.LogError("Character selector scroll view does not have an Image component.", this);
            return;
        }

        characterGridManager.OnBackgroundClicked -= CancelCharacterSelection;
        characterGridManager.OnBackgroundClicked += CancelCharacterSelection;
    }

    private void CancelCharacterSelection()
    {
        characterSelectionGeneration++;
        if (characterSelectorBackground != null)
        {
            characterSelectorBackground.SetActive(false);
        }

        selectedCharacterId = null;
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
        supportCardTeamSelector.OnCancel -= CancelTrainingSelection;
        supportCardTeamSelector.OnCancel += CancelTrainingSelection;
    }

    private void CancelTrainingSelection()
    {
        selectedCharacterId = null;
        isStartingTraining = false;
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

    private void OnBattleButtonClicked()
    {
        if (!IsLoggedIn || isLoadingBattle)
        {
            return;
        }

        isLoadingBattle = true;
        battleButton.interactable = false;
        mainCanvas.gameObject.SetActive(false);

        Scene battleScene = SceneManager.GetSceneByName(BattleSceneName);
        if (battleScene.isLoaded)
        {
            isLoadingBattle = false;
            SceneManager.SetActiveScene(battleScene);
            return;
        }

        StartCoroutine(LoadBattleScene());
    }

    private IEnumerator LoadBattleScene()
    {
        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(BattleSceneName, LoadSceneMode.Additive);
        if (loadOperation == null)
        {
            RestoreHomeAfterBattle();
            ShowStatus($"Failed to load {BattleSceneName}.");
            yield break;
        }

        yield return loadOperation;

        Scene battleScene = SceneManager.GetSceneByName(BattleSceneName);
        if (!battleScene.isLoaded || !SceneManager.SetActiveScene(battleScene))
        {
            RestoreHomeAfterBattle();
            ShowStatus($"Failed to activate {BattleSceneName}.");
            yield break;
        }

        isLoadingBattle = false;
    }

    public void UnloadBattleScene(Action onCompleted = null)
    {
        Scene battleScene = SceneManager.GetSceneByName(BattleSceneName);
        if (!battleScene.isLoaded)
        {
            RestoreHomeAfterBattle();
            onCompleted?.Invoke();
            return;
        }

        Scene homeScene = gameObject.scene;
        if (!SceneManager.SetActiveScene(homeScene))
        {
            Debug.LogError("Could not reactivate the home scene before closing the battle.");
            return;
        }

        AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(battleScene);
        if (unloadOperation == null)
        {
            Debug.LogError("Could not unload the battle scene.");
            return;
        }

        unloadOperation.completed += _ =>
        {
            RestoreHomeAfterBattle();
            onCompleted?.Invoke();
        };
    }

    private void RestoreHomeAfterBattle()
    {
        isLoadingBattle = false;
        mainCanvas.gameObject.SetActive(true);
        battleButton.interactable = true;
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
        string labelText = (CurrentTrainingRun != null
                            && (CurrentTrainingRun.status == "active" || CurrentTrainingRun.status == "completed"))
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
            SetTrainingSession(response);
            UpdateTrainingButtonLabel();
            OnTrainingRunUpdated?.Invoke(CurrentTrainingRun);
            EnsureTrainingRunPresetSaved(CurrentTrainingRun);
            onSuccess?.Invoke(CurrentTrainingRun);
        }, error =>
        {
            CurrentTrainingRun = null;
            TrainingTexts = Array.Empty<string>();
            UpdateTrainingButtonLabel();
            onError?.Invoke(error);
        }, () =>
        {
            CurrentTrainingRun = null;
            TrainingTexts = Array.Empty<string>();
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
            SetTrainingSession(response);
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

    private void SetTrainingSession(TrainingResponse response)
    {
        if (response == null || !response.success || response.run == null || response.training_texts == null
            || Array.Exists(response.training_texts, string.IsNullOrWhiteSpace))
        {
            throw new FormatException("response did not contain the training session and its texts");
        }

        CurrentTrainingRun = response.run;
        TrainingTexts = response.training_texts;
    }

    public void EndAndResetTraining(Action onSuccess = null, Action<string> onError = null)
    {
        if (!IsLoggedIn) return;

        string query = $"/training/reset?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}";
        StartCoroutine(Get(query, json =>
        {
            CurrentTrainingRun = null;
            TrainingTexts = Array.Empty<string>();
            UpdateTrainingButtonLabel();
            OnTrainingRunUpdated?.Invoke(null);
            onSuccess?.Invoke();
        }, error =>
        {
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

    public void FetchTrainingSkills(
        Action<TrainingSkillsResponse> onSuccess, Action<string> onError = null)
    {
        if (!IsLoggedIn)
        {
            onError?.Invoke("You must be logged in to load training skills.");
            return;
        }

        string query = $"/training/skills?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}";
        StartCoroutine(Get(query, json =>
        {
            TrainingSkillsResponse response = JsonUtility.FromJson<TrainingSkillsResponse>(json);
            if (response == null || !response.success || response.skills == null)
            {
                onError?.Invoke("Invalid skill shop response from server.");
                return;
            }

            onSuccess?.Invoke(response);
        }, error => { onError?.Invoke(error); }));
    }

    public void PurchaseTrainingSkills(
        string[] skillIds, Action<TrainingRunData> onSuccess, Action<string> onError = null)
    {
        if (!IsLoggedIn)
        {
            onError?.Invoke("You must be logged in to purchase training skills.");
            return;
        }
        if (skillIds == null || skillIds.Length == 0)
        {
            onError?.Invoke("Select at least one skill to purchase.");
            return;
        }

        string query = $"/training/skills/purchase?player_id={UnityWebRequest.EscapeURL(CurrentPlayer.playerId)}";
        SkillPurchaseRequestData request = new SkillPurchaseRequestData { skill_ids = skillIds };
        StartCoroutine(PostWithResponse(query, JsonUtility.ToJson(request), json =>
        {
            TrainingResponse response = JsonUtility.FromJson<TrainingResponse>(json);
            if (response == null || !response.success || response.run == null)
            {
                onError?.Invoke("Invalid skill purchase response from server.");
                return;
            }

            CurrentTrainingRun = response.run;
            UpdateTrainingButtonLabel();
            OnTrainingRunUpdated?.Invoke(CurrentTrainingRun);
            onSuccess?.Invoke(CurrentTrainingRun);
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

    public void OnGachaSceneClosed()
    {
        if (mainCanvas != null)
        {
            mainCanvas.gameObject.SetActive(true);
        }

        if (trainingButton != null)
        {
            trainingButton.gameObject.SetActive(true);
        }
    }

    public void OnGachaSceneOpened()
    {
        if (mainCanvas != null)
        {
            mainCanvas.gameObject.SetActive(false);
        }
    }

    private void HideTrainingButton()
    {
        if (trainingButton != null)
        {
            trainingButton.gameObject.SetActive(false);
        }
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
        yield return PostWithResponse(path, json, _ => onSuccess?.Invoke(), onError);
    }

    private IEnumerator PostWithResponse(
        string path, string json, Action<string> onSuccess, Action<string> onError)
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

        try
        {
            onSuccess?.Invoke(request.downloadHandler.text);
        }
        catch (Exception ex)
        {
            string body = request.downloadHandler.text;
            Debug.LogError($"Failed to handle response from {url}: {ex.Message}\n{body}");
            onError?.Invoke("Invalid response from server.");
        }
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