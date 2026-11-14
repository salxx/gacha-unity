using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Opens gacha selection and manages the pull scene.
/// </summary>
public class GachaPullButton : MonoBehaviour
{
    private const string GachaSceneName = "GachaScene";
    private static bool isGachaSceneBusy;

    [SerializeField] private Button pullButton;

    private GameClient client;
    private GachaSelectionScreen selectionScreen;
    private string targetGacha;
    private DailyFeaturedGacha featuredGacha;
    private GachaItemSpawner activeSpawner;
    private bool isLoadingGachaScene;
    private bool isWaitingForPullResponse;
    private bool hasPullResponse;
    private bool hasSequenceEnded;

    private void Start()
    {
        client = GameClient.Instance;
        isGachaSceneBusy = false;

        selectionScreen = GachaSelectionScreen.Create(pullButton.transform.parent, client, StartPull);
        if (selectionScreen == null)
        {
            pullButton.interactable = false;
        }
        else
        {
            pullButton.onClick.AddListener(OnPullButtonClicked);
        }
        client.OnLoginSuccess += RefreshVisibility;

        RefreshVisibility();
    }

    private void OnDestroy()
    {
        pullButton.onClick.RemoveListener(OnPullButtonClicked);

        if (client != null)
        {
            client.OnLoginSuccess -= RefreshVisibility;
            client.OnPullFinished -= HandlePullFinished;
        }

        if (activeSpawner != null)
        {
            activeSpawner.OnSequenceEnded -= HandleSequenceEnded;
        }
        if (selectionScreen != null)
        {
            Destroy(selectionScreen.gameObject);
        }
    }

    private void OnPullButtonClicked()
    {
        if (isLoadingGachaScene || isGachaSceneBusy || client == null || !client.IsLoggedIn)
        {
            return;
        }

        selectionScreen.Open();
    }

    private void StartPull(string target, DailyFeaturedGacha featured)
    {
        if (isLoadingGachaScene || isGachaSceneBusy || client == null || !client.IsLoggedIn)
        {
            return;
        }
        targetGacha = target;
        featuredGacha = featured;
        isLoadingGachaScene = true;
        isGachaSceneBusy = true;
        client.StartCoroutine(LoadGachaSceneAndPull());
    }

    private IEnumerator LoadGachaSceneAndPull()
    {
        if (!SceneManager.GetSceneByName(GachaSceneName).isLoaded)
        {
            AsyncOperation loadOperation = SceneManager.LoadSceneAsync(GachaSceneName, LoadSceneMode.Additive);
            if (loadOperation == null)
            {
                Debug.LogError($"Could not start loading scene '{GachaSceneName}'.");
                selectionScreen.ShowError("Could not load the gacha scene.");
                FinishGachaScene();
                yield break;
            }

            yield return loadOperation;
        }

        client.OnGachaSceneOpened();

        activeSpawner = GachaItemSpawner.Instance;
        if (activeSpawner == null)
        {
            Debug.LogError($"Scene '{GachaSceneName}' does not contain an active GachaItemSpawner.");
            selectionScreen.ShowError("The gacha scene is missing its reward display.");
            yield return UnloadGachaScene();
            yield break;
        }

        activeSpawner.OnSequenceEnded += HandleSequenceEnded;
        client.OnPullFinished += HandlePullFinished;
        isWaitingForPullResponse = true;
        hasPullResponse = false;
        hasSequenceEnded = false;
        client.Pull(targetGacha, featuredGacha, selectionScreen.ShowError);
    }

    private void HandlePullFinished()
    {
        client.OnPullFinished -= HandlePullFinished;
        isWaitingForPullResponse = false;
        hasPullResponse = true;

        if (!activeSpawner.IsSequenceActive || hasSequenceEnded)
        {
            client.StartCoroutine(UnloadGachaScene());
        }
    }

    private void HandleSequenceEnded()
    {
        hasSequenceEnded = true;
        if (hasPullResponse && !isWaitingForPullResponse)
        {
            client.StartCoroutine(UnloadGachaScene());
        }
    }

    private IEnumerator UnloadGachaScene()
    {
        if (!isLoadingGachaScene)
        {
            yield break;
        }

        if (activeSpawner != null)
        {
            activeSpawner.OnSequenceEnded -= HandleSequenceEnded;
            activeSpawner = null;
        }

        Scene gachaScene = SceneManager.GetSceneByName(GachaSceneName);
        if (gachaScene.isLoaded)
        {
            AsyncOperation unloadOperation = SceneManager.UnloadSceneAsync(gachaScene);
            if (unloadOperation != null)
            {
                yield return unloadOperation;
            }
        }

        FinishGachaScene();
    }

    private void FinishGachaScene()
    {
        isLoadingGachaScene = false;
        isWaitingForPullResponse = false;
        hasPullResponse = false;
        hasSequenceEnded = false;
        isGachaSceneBusy = false;
        client?.OnGachaSceneClosed();
        selectionScreen?.RefreshBanners();
    }

    private void RefreshVisibility()
    {
        if (pullButton != null)
        {
            pullButton.gameObject.SetActive(client != null && client.IsLoggedIn);
        }
    }
}
