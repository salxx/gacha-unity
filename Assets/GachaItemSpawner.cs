using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum SpawnStrategy
{
    Line,
    SingleItemOnClick
}

public class GachaItemSpawner : MonoBehaviour
{
    [Header("Strategy")]
    [SerializeField] private SpawnStrategy strategy = SpawnStrategy.Line;

    [Header("Single Item UI")]
    [Tooltip("UI Button used to advance to the next reward when using the SingleItemOnClick strategy.")]
    [SerializeField] private Button nextButton;

    [Tooltip("UI Toggle to skip common pulls until the next rare or SR.")]
    [SerializeField] private Toggle nextPullToggle;

    [Header("Pull Indicators (Click Reveal)")]
    [Tooltip("Canvas in the scene where pull indicator images will be instantiated.")]
    [SerializeField] private Canvas pullCanvas;

    [Tooltip("Prefab for the image representing each pull indicator.")]
    [SerializeField] private GameObject pullImagePrefab;

    [Tooltip("Horizontal spacing between pull indicator images.")]
    [SerializeField] private float indicatorSpacing = 65f;

    [Tooltip("Offset of the pull indicator group relative to the canvas center.")]
    [SerializeField] private Vector2 indicatorOffset = Vector2.zero;

    [Header("Indicator Colors")]
    [Tooltip("Color of the indicator before the pull is revealed.")]
    [SerializeField] private Color unknownColor = new Color(0.75f, 0.75f, 0.75f, 1f);

    [Tooltip("Color for common rarity pulls.")]
    [SerializeField] private Color commonColor = new Color(0.35f, 0.35f, 0.35f, 1f);

    [Tooltip("Color for rare rarity pulls.")]
    [SerializeField] private Color rareColor = new Color(1f, 0.85f, 0.1f, 1f);

    [Tooltip("Color for super rare rarity pulls.")]
    [SerializeField] private Color superRareColor = new Color(1f, 0.5f, 0f, 1f);

    [Header("Prefabs Per Tier")]
    [SerializeField] private GameObject commonPrefab;
    [SerializeField] private GameObject rarePrefab;
    [SerializeField] private GameObject superRarePrefab;

    [Header("Audio Clips Per Tier")]
    [Tooltip("Audio clip played for common rarity pulls.")]
    [SerializeField] private AudioClip commonAudioClip;

    [Tooltip("Audio clip played for rare rarity pulls.")]
    [SerializeField] private AudioClip rareAudioClip;

    [Tooltip("Audio clip played for super rare rarity pulls.")]
    [SerializeField] private AudioClip superRareAudioClip;

    [Header("Reward Info / UI Canvas")]
    [Tooltip("Prefab containing a Canvas with a TMP_Text child to display reward name and <type> (used for Line strategy).")]
    [SerializeField] private GameObject rewardCanvasPrefab;

    [Tooltip("UI TMP_Text element to display reward info for SingleItemOnClick strategy.")]
    [SerializeField] private TMP_Text rewardInfoText;

    [Header("Assembly Placement")]
    [Tooltip("Base spawn origin coordinates (default: 0,0,0).")]
    [SerializeField] private Vector3 spawnCoordinates = Vector3.zero;

    [Tooltip("Distance between consecutive items in the line.")]
    [SerializeField] private float itemSpacing = 2.0f;

    [Tooltip("Direction vector along which items are aligned.")]
    [SerializeField] private Vector3 lineDirection = Vector3.right;

    [Tooltip("Center the line of items around the spawn origin.")]
    [SerializeField] private bool centerLine = true;

    [Header("Anticipation & Dramatic Reveal")]
    [Tooltip("Whether to play a dramatic anticipation phase before revealing each reward.")]
    [SerializeField] private bool enableAnticipation = true;

    [Tooltip("Duration in seconds of the anticipation build-up before the reveal.")]
    [SerializeField] private float anticipationDuration = 1.5f;

    [Tooltip("Optional prefab to use as the anticipation placeholder. If null, a procedural mystery placeholder is generated.")]
    [SerializeField] private GameObject anticipationPrefab;

    [Tooltip("Optional material for the procedural anticipation placeholder.")]
    [SerializeField] private Material anticipationMaterial;

    [Tooltip("Whether to animate the revealed reward scaling up on reveal.")]
    [SerializeField] private bool animateRewardPopIn = true;

    [Tooltip("Duration in seconds of the reward pop-in animation.")]
    [SerializeField] private float rewardPopInDuration = 0.35f;

    [Tooltip("Duration in seconds to display a common pull before auto-advancing when Skip Commons is active.")]
    [SerializeField] private float skipCommonDisplayDuration = 1.0f;

    private readonly List<GameObject> currentSpawnedItems = new List<GameObject>();
    private readonly List<GameObject> spawnedIndicatorObjects = new List<GameObject>();
    private readonly List<Image> spawnedIndicatorImages = new List<Image>();
    private readonly List<Button> spawnedIndicatorButtons = new List<Button>();
    private GachaReward[] queuedRewards;
    private int currentRewardIndex = -1;
    private int maxRevealedIndex = -1;
    private int currentDisplayedIndex = -1;
    private bool isSequenceActive = false;
    private bool isRevealing = false;
    private Coroutine currentRevealCoroutine;
    private GameObject activePlaceholder;
    private AudioSource audioSource;

    public IReadOnlyList<GameObject> CurrentSpawnedItems => currentSpawnedItems;
    public IReadOnlyList<GameObject> SpawnedIndicatorObjects => spawnedIndicatorObjects;
    public IReadOnlyList<Image> SpawnedIndicatorImages => spawnedIndicatorImages;
    public IReadOnlyList<Button> SpawnedIndicatorButtons => spawnedIndicatorButtons;

    public event System.Action OnSequenceStarted;
    public event System.Action OnSequenceEnded;

    public Canvas PullCanvas
    {
        get => pullCanvas;
        set => pullCanvas = value;
    }

    public GameObject PullImagePrefab
    {
        get => pullImagePrefab;
        set => pullImagePrefab = value;
    }

    public float IndicatorSpacing
    {
        get => indicatorSpacing;
        set => indicatorSpacing = value;
    }

    public Vector2 IndicatorOffset
    {
        get => indicatorOffset;
        set => indicatorOffset = value;
    }

    public Color UnknownColor
    {
        get => unknownColor;
        set => unknownColor = value;
    }

    public Color CommonColor
    {
        get => commonColor;
        set => commonColor = value;
    }

    public Color RareColor
    {
        get => rareColor;
        set => rareColor = value;
    }

    public Color SuperRareColor
    {
        get => superRareColor;
        set => superRareColor = value;
    }

    public AudioClip CommonAudioClip
    {
        get => commonAudioClip;
        set => commonAudioClip = value;
    }

    public AudioClip RareAudioClip
    {
        get => rareAudioClip;
        set => rareAudioClip = value;
    }

    public AudioClip SuperRareAudioClip
    {
        get => superRareAudioClip;
        set => superRareAudioClip = value;
    }

    public AudioClip CommonAudio
    {
        get => commonAudioClip;
        set => commonAudioClip = value;
    }

    public AudioClip RareAudio
    {
        get => rareAudioClip;
        set => rareAudioClip = value;
    }

    public AudioClip SuperRareAudio
    {
        get => superRareAudioClip;
        set => superRareAudioClip = value;
    }

    public AudioClip SrAudioClip
    {
        get => superRareAudioClip;
        set => superRareAudioClip = value;
    }

    public AudioClip SrAudio
    {
        get => superRareAudioClip;
        set => superRareAudioClip = value;
    }

    public AudioSource AudioSource
    {
        get => audioSource;
        set => audioSource = value;
    }

    public SpawnStrategy Strategy
    {
        get => strategy;
        set => strategy = value;
    }

    public Button NextButton
    {
        get => nextButton;
        set
        {
            if (nextButton != value)
            {
                nextButton.onClick.RemoveListener(OnNextButtonClicked);
                nextButton = value;
                nextButton.onClick.AddListener(OnNextButtonClicked);
                nextButton.gameObject.SetActive(isSequenceActive && strategy == SpawnStrategy.SingleItemOnClick);
            }
        }
    }

    public Toggle NextPullToggle
    {
        get => nextPullToggle;
        set
        {
            nextPullToggle = value;
            nextPullToggle.gameObject.SetActive(isSequenceActive && strategy == SpawnStrategy.SingleItemOnClick);
        }
    }

    public bool IsSkipCommonsActive => nextPullToggle.isOn;

    public bool IsSequenceActive => isSequenceActive;

    public bool IsTrackingClicks => isSequenceActive;

    public int CurrentRewardIndex => currentDisplayedIndex >= 0 ? currentDisplayedIndex : maxRevealedIndex;

    public int MaxRevealedIndex => maxRevealedIndex;

    public int CurrentDisplayedIndex => currentDisplayedIndex;

    public GameObject RewardCanvasPrefab
    {
        get => rewardCanvasPrefab;
        set => rewardCanvasPrefab = value;
    }

    public TMP_Text RewardInfoText
    {
        get => rewardInfoText;
        set
        {
            rewardInfoText = value;
            rewardInfoText.gameObject.SetActive(isSequenceActive && strategy == SpawnStrategy.SingleItemOnClick && !isRevealing && currentDisplayedIndex >= 0);
        }
    }

    public float ItemSpacing
    {
        get => itemSpacing;
        set => itemSpacing = value;
    }

    public Vector3 SpawnCoordinates
    {
        get => spawnCoordinates;
        set => spawnCoordinates = value;
    }

    public Vector3 LineDirection
    {
        get => lineDirection;
        set => lineDirection = value;
    }

    public bool CenterLine
    {
        get => centerLine;
        set => centerLine = value;
    }

    public bool EnableAnticipation
    {
        get => enableAnticipation;
        set => enableAnticipation = value;
    }

    public float AnticipationDuration
    {
        get => anticipationDuration;
        set => anticipationDuration = value;
    }

    public GameObject AnticipationPrefab
    {
        get => anticipationPrefab;
        set => anticipationPrefab = value;
    }

    public Material AnticipationMaterial
    {
        get => anticipationMaterial;
        set => anticipationMaterial = value;
    }

    public bool AnimateRewardPopIn
    {
        get => animateRewardPopIn;
        set => animateRewardPopIn = value;
    }

    public float RewardPopInDuration
    {
        get => rewardPopInDuration;
        set => rewardPopInDuration = value;
    }

    public float SkipCommonDisplayDuration
    {
        get => skipCommonDisplayDuration;
        set => skipCommonDisplayDuration = value;
    }

    public bool IsRevealing => isRevealing;

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }
        }

        nextButton.onClick.AddListener(OnNextButtonClicked);
        nextButton.gameObject.SetActive(false);
        nextPullToggle.gameObject.SetActive(false);
        HideRewardInfoText();
    }

    private void OnDestroy()
    {
        nextButton.onClick.RemoveListener(OnNextButtonClicked);
    }

    private void OnDisable()
    {
        if (currentRevealCoroutine != null)
        {
            StopCoroutine(currentRevealCoroutine);
            currentRevealCoroutine = null;
        }

        if (activePlaceholder != null)
        {
            Destroy(activePlaceholder);
            activePlaceholder = null;
        }

        isRevealing = false;

        bool wasActive = isSequenceActive;
        isSequenceActive = false;
        nextButton.interactable = true;
        nextButton.gameObject.SetActive(false);
        nextPullToggle.interactable = true;
        nextPullToggle.gameObject.SetActive(false);
        HideRewardInfoText();
        if (wasActive)
        {
            OnSequenceEnded?.Invoke();
        }
    }

    /// <summary>
    /// Invoked when the UI advance button is clicked.
    /// Advances the sequence if the single-item strategy is active.
    /// </summary>
    public void OnNextButtonClicked()
    {
        if (!isSequenceActive || strategy != SpawnStrategy.SingleItemOnClick || isRevealing)
        {
            return;
        }

        AdvanceSingleItemSequence();
    }

    /// <summary>
    /// Cleans up and destroys all previously spawned gacha item instances and indicators, stopping any active sequence.
    /// </summary>
    public void ClearSpawnedItems()
    {
        if (currentRevealCoroutine != null)
        {
            StopCoroutine(currentRevealCoroutine);
            currentRevealCoroutine = null;
        }

        if (activePlaceholder != null)
        {
            Destroy(activePlaceholder);
            activePlaceholder = null;
        }

        isRevealing = false;

        ClearCurrentSpawnedItems();
        ClearPullIndicators();
        bool wasActive = isSequenceActive;
        isSequenceActive = false;
        queuedRewards = null;
        currentRewardIndex = -1;
        maxRevealedIndex = -1;
        currentDisplayedIndex = -1;
        nextButton.interactable = true;
        nextButton.gameObject.SetActive(false);
        nextPullToggle.interactable = true;
        nextPullToggle.gameObject.SetActive(false);
        HideRewardInfoText();
        if (wasActive)
        {
            OnSequenceEnded?.Invoke();
        }
    }

    /// <summary>
    /// Destroys only the currently displayed 3D reward instances.
    /// </summary>
    private void ClearCurrentSpawnedItems()
    {
        for (int i = 0; i < currentSpawnedItems.Count; i++)
        {
            if (currentSpawnedItems[i] != null)
            {
                Destroy(currentSpawnedItems[i]);
            }
        }
        currentSpawnedItems.Clear();
    }

    /// <summary>
    /// Cleans up and destroys all pull indicator image instances.
    /// </summary>
    public void ClearPullIndicators()
    {
        for (int i = 0; i < spawnedIndicatorObjects.Count; i++)
        {
            if (spawnedIndicatorObjects[i] != null)
            {
                Destroy(spawnedIndicatorObjects[i]);
            }
        }
        spawnedIndicatorObjects.Clear();
        spawnedIndicatorImages.Clear();
        spawnedIndicatorButtons.Clear();
        maxRevealedIndex = -1;
        currentDisplayedIndex = -1;
    }

    /// <summary>
    /// Resolves color based on tier (int or string, e.g. 1/2/3 or 'common', 'rare', 'super_rare').
    /// </summary>
    public Color GetColorForTier(string tier)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            return commonColor;
        }

        string normalizedTier = tier.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");

        switch (normalizedTier)
        {
            case "1":
            case "common":
            case "c":
                return commonColor;
            case "2":
            case "rare":
            case "r":
                return rareColor;
            case "3":
            case "super_rare":
            case "superrare":
            case "sr":
                return superRareColor;
            default:
                Debug.LogWarning($"Unknown tier '{tier}', falling back to common color.");
                return commonColor;
        }
    }

    public Color GetColorForTier(int tier)
    {
        switch (tier)
        {
            case 1: return commonColor;
            case 2: return rareColor;
            case 3: return superRareColor;
            default: return commonColor;
        }
    }

    /// <summary>
    /// Resolves prefab based on tier (int or string, e.g. 1/2/3 or 'common', 'rare', 'super_rare').
    /// </summary>
    public GameObject GetPrefabForTier(string tier)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            return commonPrefab;
        }

        string normalizedTier = tier.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");

        switch (normalizedTier)
        {
            case "1":
            case "common":
            case "c":
                return commonPrefab;
            case "2":
            case "rare":
            case "r":
                return rarePrefab;
            case "3":
            case "super_rare":
            case "superrare":
            case "sr":
                return superRarePrefab;
            default:
                Debug.LogWarning($"Unknown tier '{tier}', falling back to common prefab.");
                return commonPrefab;
        }
    }

    public GameObject GetPrefabForTier(int tier)
    {
        switch (tier)
        {
            case 1: return commonPrefab;
            case 2: return rarePrefab;
            case 3: return superRarePrefab;
            default: return commonPrefab;
        }
    }

    /// <summary>
    /// Resolves audio clip based on tier (int or string, e.g. 1/2/3 or 'common', 'rare', 'super_rare', 'sr').
    /// </summary>
    public AudioClip GetAudioClipForTier(string tier)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            return commonAudioClip;
        }

        string normalizedTier = tier.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");

        switch (normalizedTier)
        {
            case "1":
            case "common":
            case "c":
                return commonAudioClip;
            case "2":
            case "rare":
            case "r":
                return rareAudioClip;
            case "3":
            case "super_rare":
            case "superrare":
            case "sr":
                return superRareAudioClip;
            default:
                Debug.LogWarning($"Unknown tier '{tier}', falling back to common audio clip.");
                return commonAudioClip;
        }
    }

    public AudioClip GetAudioClipForTier(int tier)
    {
        switch (tier)
        {
            case 1: return commonAudioClip;
            case 2: return rareAudioClip;
            case 3: return superRareAudioClip;
            default: return commonAudioClip;
        }
    }

    /// <summary>
    /// Alias for GetAudioClipForTier.
    /// </summary>
    public AudioClip GetAudioForTier(string tier) => GetAudioClipForTier(tier);
    public AudioClip GetAudioForTier(int tier) => GetAudioClipForTier(tier);

    /// <summary>
    /// Plays the assigned audio clip using the attached AudioSource or PlayClipAtPoint fallback.
    /// </summary>
    public void PlayAudio(AudioClip clip)
    {
        if (clip == null)
        {
            return;
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null && gameObject != null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }
        }

        if (audioSource != null)
        {
            audioSource.PlayOneShot(clip);
        }
        else
        {
            AudioSource.PlayClipAtPoint(clip, transform.position);
        }
    }

    /// <summary>
    /// Plays the audio clip associated with the specified reward tier.
    /// </summary>
    public void PlayAudioForTier(string tier)
    {
        AudioClip clip = GetAudioClipForTier(tier);
        PlayAudio(clip);
    }

    public void PlayAudioForTier(int tier)
    {
        AudioClip clip = GetAudioClipForTier(tier);
        PlayAudio(clip);
    }

    public void PlayCommonAudio() => PlayAudio(commonAudioClip);
    public void PlayRareAudio() => PlayAudio(rareAudioClip);
    public void PlaySuperRareAudio() => PlayAudio(superRareAudioClip);
    public void PlaySRAudio() => PlayAudio(superRareAudioClip);

    /// <summary>
    /// Spawns pull indicator images aligned horizontally on the assigned canvas.
    /// </summary>
    private void SpawnPullIndicators(int count)
    {
        ClearPullIndicators();

        if (count <= 0)
        {
            return;
        }

        Transform parentTransform = pullCanvas.transform;
        float startX = -(count - 1) * indicatorSpacing * 0.5f;

        for (int i = 0; i < count; i++)
        {
            GameObject indicatorObj = Instantiate(pullImagePrefab, parentTransform, false);
            indicatorObj.name = $"Pull_Indicator_{i + 1}";

            RectTransform rt = indicatorObj.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchoredPosition = new Vector2(startX + i * indicatorSpacing + indicatorOffset.x, indicatorOffset.y);
            }

            Image img = indicatorObj.GetComponentInChildren<Image>();
            if (img != null)
            {
                img.color = unknownColor;
            }

            Button btn = indicatorObj.GetComponent<Button>();
            if (btn == null)
            {
                btn = indicatorObj.AddComponent<Button>();
            }
            btn.interactable = false;
            int indicatorIndex = i;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnIndicatorClicked(indicatorIndex));

            spawnedIndicatorObjects.Add(indicatorObj);
            spawnedIndicatorImages.Add(img);
            spawnedIndicatorButtons.Add(btn);
        }
    }

    /// <summary>
    /// Enables or disables the click button on a specific pull indicator.
    /// </summary>
    private void EnableIndicatorButton(int index, bool enable)
    {
        if (index >= 0 && index < spawnedIndicatorButtons.Count)
        {
            Button btn = spawnedIndicatorButtons[index];
            if (btn != null)
            {
                btn.interactable = enable;
            }
        }
    }

    /// <summary>
    /// Invoked when a pull indicator image is clicked.
    /// Reloads the corresponding reward if it has already been revealed.
    /// </summary>
    public void OnIndicatorClicked(int index)
    {
        if (!isSequenceActive || strategy != SpawnStrategy.SingleItemOnClick || isRevealing)
        {
            return;
        }

        // Only allow clicking already revealed pulls
        if (queuedRewards == null || index < 0 || index > maxRevealedIndex || index >= queuedRewards.Length)
        {
            return;
        }

        DisplayRewardAt(index);
    }

    /// <summary>
    /// Displays / reloads the reward item at the specified index in the center (spawnCoordinates).
    /// </summary>
    public void DisplayRewardAt(int index)
    {
        ClearCurrentSpawnedItems();

        currentDisplayedIndex = index;
        if (queuedRewards != null && index >= 0 && index < queuedRewards.Length)
        {
            SpawnRewardItem(queuedRewards[index], spawnCoordinates, index);
            if (strategy == SpawnStrategy.SingleItemOnClick)
            {
                UpdateRewardInfoText(queuedRewards[index]);
            }
        }
    }

    /// <summary>
    /// Updates the color of a specific pull indicator based on its reward tier.
    /// </summary>
    private void UpdateIndicatorColor(int index, string tier)
    {
        if (index >= 0 && index < spawnedIndicatorImages.Count)
        {
            Image img = spawnedIndicatorImages[index];
            if (img != null)
            {
                img.color = GetColorForTier(tier);
            }
        }
    }

    private void UpdateIndicatorColor(int index, int tier)
    {
        if (index >= 0 && index < spawnedIndicatorImages.Count)
        {
            Image img = spawnedIndicatorImages[index];
            if (img != null)
            {
                img.color = GetColorForTier(tier);
            }
        }
    }

    /// <summary>
    /// Spawns game objects for the rewards according to the selected strategy.
    /// Existing items are destroyed and replaced.
    /// </summary>
    public void AssembleRewards(GachaReward[] rewards)
    {
        ClearSpawnedItems();

        if (rewards == null || rewards.Length == 0)
        {
            return;
        }

        if (strategy == SpawnStrategy.Line)
        {
            AssembleRewardsLine(rewards);
        }
        else if (strategy == SpawnStrategy.SingleItemOnClick)
        {
            AssembleRewardsSingleItem(rewards);
        }
    }

    /// <summary>
    /// Spawns game objects for all rewards arranged along the line direction.
    /// </summary>
    private void AssembleRewardsLine(GachaReward[] rewards)
    {
        Vector3 normalizedDir = lineDirection.sqrMagnitude > 0.0001f ? lineDirection.normalized : Vector3.right;
        int count = rewards.Length;

        for (int i = 0; i < count; i++)
        {
            GachaReward reward = rewards[i];
            float offsetIndex = centerLine ? (i - (count - 1) / 2.0f) : i;
            Vector3 worldPos = spawnCoordinates + normalizedDir * (offsetIndex * itemSpacing);

            SpawnRewardItem(reward, worldPos, i);
        }
    }

    /// <summary>
    /// Checks whether a tier corresponds to Rare or higher (non-common).
    /// </summary>
    public bool IsRareOrHigher(string tier)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            return false;
        }

        string normalizedTier = tier.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
        return normalizedTier != "common" && normalizedTier != "1" && normalizedTier != "c";
    }

    public bool IsRareOrHigher(int tier)
    {
        return tier >= 2;
    }

    /// <summary>
    /// Initializes the single-item reveal sequence and begins the dramatic anticipation for the first item.
    /// All pull indicators are spawned in the unknown state without spoiling rarity.
    /// </summary>
    private void AssembleRewardsSingleItem(GachaReward[] rewards)
    {
        queuedRewards = (GachaReward[])rewards.Clone();
        maxRevealedIndex = -1;
        currentDisplayedIndex = -1;
        currentRewardIndex = -1;
        isSequenceActive = true;

        HideRewardInfoText();

        nextButton.gameObject.SetActive(true);
        nextButton.interactable = false;

        nextPullToggle.gameObject.SetActive(true);
        nextPullToggle.interactable = false;

        SpawnPullIndicators(queuedRewards.Length);

        OnSequenceStarted?.Invoke();

        AdvanceSingleItemSequence();
    }

    /// <summary>
    /// Advances the single-item sequence to reveal the next reward with anticipation, or completes if no more rewards remain.
    /// If NextPullToggle is active, skips common pulls automatically without anticipation sphere, showing each for skipCommonDisplayDuration until the next rare or SR.
    /// </summary>
    public void AdvanceSingleItemSequence()
    {
        if (isRevealing)
        {
            return;
        }

        int nextIndex = maxRevealedIndex + 1;
        if (queuedRewards != null && nextIndex < queuedRewards.Length)
        {
            StartRevealSequence(nextIndex);
        }
        else
        {
            ClearSpawnedItems();
        }
    }

    /// <summary>
    /// Initiates the anticipation and reveal sequence for the reward at the specified index.
    /// </summary>
    public void StartRevealSequence(int index)
    {
        if (currentRevealCoroutine != null)
        {
            StopCoroutine(currentRevealCoroutine);
            currentRevealCoroutine = null;
        }

        if (activePlaceholder != null)
        {
            Destroy(activePlaceholder);
            activePlaceholder = null;
        }

        if (gameObject.activeInHierarchy)
        {
            currentRevealCoroutine = StartCoroutine(RevealSequenceRoutine(index));
        }
        else
        {
            DisplayRewardAt(index);
            if (queuedRewards != null && index >= 0 && index < queuedRewards.Length)
            {
                var r = queuedRewards[index];
                maxRevealedIndex = Mathf.Max(maxRevealedIndex, index);
                currentRewardIndex = index;
                UpdateIndicatorColor(index, r != null ? r.TierInt : 1);
                EnableIndicatorButton(index, true);
                PlayAudioForTier(r != null ? r.TierInt : 1);
            }
        }
    }

    /// <summary>
    /// Coroutine managing the dramatic placeholder anticipation build-up, spoiler-free timing, and final reveal.
    /// When Skip Commons is active, common pulls are revealed without the anticipation sphere and displayed for
    /// skipCommonDisplayDuration seconds before advancing to the next item in sequence.
    /// </summary>
    private IEnumerator RevealSequenceRoutine(int startIndex)
    {
        isRevealing = true;

        nextButton.interactable = false;
        nextPullToggle.interactable = true;

        int currentIndex = startIndex;

        while (queuedRewards != null && currentIndex < queuedRewards.Length)
        {
            GachaReward reward = queuedRewards[currentIndex];
            int rewardTier = reward != null ? reward.TierInt : 1;
            bool isCommon = !IsRareOrHigher(rewardTier);
            bool skipThisCommon = isCommon && IsSkipCommonsActive;

            HideRewardInfoText();
            ClearCurrentSpawnedItems();

            // Dramatic anticipation sphere animation (skipped for common pulls when Skip Commons is enabled)
            if (!skipThisCommon && enableAnticipation && anticipationDuration > 0f)
            {
                nextPullToggle.interactable = false;

                Vector3 placeholderPos = spawnCoordinates + new Vector3(0f, 0.5f, 0f);
                GameObject placeholder;
                if (anticipationPrefab != null)
                {
                    placeholder = Instantiate(anticipationPrefab, placeholderPos, Quaternion.identity);
                    placeholder.name = "Anticipation_Placeholder";
                }
                else
                {
                    placeholder = CreateProceduralPlaceholder(placeholderPos);
                }
                activePlaceholder = placeholder;

                float elapsed = 0f;
                Vector3 basePos = placeholderPos;

                while (elapsed < anticipationDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / anticipationDuration);

                    if (placeholder != null)
                    {
                        // Smooth bounce pop-in during first 0.25s
                        float popScale = 1.0f;
                        if (elapsed < 0.25f)
                        {
                            float popT = elapsed / 0.25f;
                            popScale = Mathf.Sin(popT * Mathf.PI * 0.5f);
                        }

                        // Tension pulse building up in frequency and amplitude (compress and expand)
                        float pulseFreq = Mathf.Lerp(8f, 32f, t);
                        float pulseAmp = Mathf.Lerp(0.08f, 0.35f, t);
                        // Scale compress/expand in sync along axes to generate dramatic tension
                        float pulse = Mathf.Sin(elapsed * pulseFreq);
                        float uniformScale = popScale * (1f + pulse * pulseAmp);

                        placeholder.transform.localScale = new Vector3(uniformScale, uniformScale, uniformScale);

                        // Float bobbing and tension rumble shake
                        float bob = Mathf.Sin(elapsed * 7f) * 0.12f;
                        float shakeStrength = Mathf.Lerp(0f, 0.09f, t * t);
                        Vector3 shake = UnityEngine.Random.insideUnitSphere * shakeStrength;
                        placeholder.transform.position = basePos + new Vector3(0f, bob, 0f) + shake;
                    }

                    yield return null;
                }

                // Quick climax collapse before revealing the real item
                if (placeholder != null)
                {
                    float shrinkElapsed = 0f;
                    float shrinkDuration = 0.08f;
                    Vector3 currentScale = placeholder.transform.localScale;
                    while (shrinkElapsed < shrinkDuration)
                    {
                        shrinkElapsed += Time.deltaTime;
                        float st = Mathf.Clamp01(shrinkElapsed / shrinkDuration);
                        if (placeholder != null)
                        {
                            placeholder.transform.localScale = Vector3.Lerp(currentScale, Vector3.zero, st);
                        }
                        yield return null;
                    }

                    if (placeholder != null)
                    {
                        Destroy(placeholder);
                    }
                }
                activePlaceholder = null;
            }

            // Reveal the actual reward 3D object
            maxRevealedIndex = Mathf.Max(maxRevealedIndex, currentIndex);
            currentRewardIndex = currentIndex;
            currentDisplayedIndex = currentIndex;

            GameObject rewardObj = SpawnRewardItem(reward, spawnCoordinates, currentIndex);
            PlayAudioForTier(rewardTier);

            // Reward entrance pop-in animation
            if (animateRewardPopIn && rewardPopInDuration > 0f && rewardObj != null)
            {
                float popElapsed = 0f;
                Vector3 targetScale = rewardObj.transform.localScale;
                rewardObj.transform.localScale = Vector3.zero;

                while (popElapsed < rewardPopInDuration)
                {
                    popElapsed += Time.deltaTime;
                    float pt = Mathf.Clamp01(popElapsed / rewardPopInDuration);
                    float scaleFactor = Mathf.Lerp(0f, 1f, pt);
                    float overshoot = 1f + 0.25f * Mathf.Sin(pt * Mathf.PI);
                    if (rewardObj != null)
                    {
                        rewardObj.transform.localScale = targetScale * (scaleFactor * overshoot);
                    }
                    yield return null;
                }

                if (rewardObj != null)
                {
                    rewardObj.transform.localScale = targetScale;
                }
            }

            // Update UI indicator to revealed color and enable clicking now that the reward spawn animation is done
            UpdateIndicatorColor(currentIndex, rewardTier);
            EnableIndicatorButton(currentIndex, true);
            if (strategy == SpawnStrategy.SingleItemOnClick)
            {
                UpdateRewardInfoText(reward);
            }

            // If we are skipping this common and there are more items to reveal, show for duration then advance
            if (skipThisCommon && currentIndex + 1 < queuedRewards.Length)
            {
                nextPullToggle.interactable = true;

                if (skipCommonDisplayDuration > 0f)
                {
                    yield return new WaitForSeconds(skipCommonDisplayDuration);
                }

                currentIndex++;
            }
            else
            {
                break;
            }
        }

        isRevealing = false;
        currentRevealCoroutine = null;

        nextButton.interactable = true;
        nextButton.gameObject.SetActive(true);

        nextPullToggle.interactable = true;
        nextPullToggle.gameObject.SetActive(true);
    }

    /// <summary>
    /// Generates a stylized procedural 2D circle placeholder object for anticipation build-up when no prefab is provided.
    /// Creates a 2D disc / circle facing the camera in 2.5D space that can spin like a papercut model.
    /// </summary>
    private GameObject CreateProceduralPlaceholder(Vector3 position)
    {
        GameObject placeholder = new GameObject("Anticipation_Placeholder");
        placeholder.transform.position = position;
        placeholder.transform.localScale = Vector3.zero;

        MeshFilter mf = placeholder.AddComponent<MeshFilter>();
        mf.mesh = CreateCircleMesh(1f, 32);

        MeshRenderer rend = placeholder.AddComponent<MeshRenderer>();
        if (anticipationMaterial != null)
        {
            rend.material = anticipationMaterial;
        }
        else
        {
            Material mat = null;
            if (superRarePrefab != null)
            {
                Renderer srRend = superRarePrefab.GetComponentInChildren<Renderer>();
                if (srRend != null && srRend.sharedMaterial != null)
                {
                    mat = new Material(srRend.sharedMaterial);
                }
            }

            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
                if (shader != null)
                {
                    mat = new Material(shader);
                }
            }

            if (mat != null)
            {
                mat.color = new Color(0.18f, 0.20f, 0.26f, 1f);
                rend.material = mat;
            }
        }

        // Add an inner glowing accent circle
        GameObject innerCore = new GameObject("Anticipation_Core");
        innerCore.transform.SetParent(placeholder.transform, false);
        innerCore.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        innerCore.transform.localScale = Vector3.one * 0.55f;

        MeshFilter innerMf = innerCore.AddComponent<MeshFilter>();
        innerMf.mesh = CreateCircleMesh(1f, 32);

        MeshRenderer innerRend = innerCore.AddComponent<MeshRenderer>();
        if (anticipationMaterial != null)
        {
            innerRend.material = anticipationMaterial;
        }
        else
        {
            Material coreMat = null;
            if (rarePrefab != null)
            {
                Renderer rRend = rarePrefab.GetComponentInChildren<Renderer>();
                if (rRend != null && rRend.sharedMaterial != null)
                {
                    coreMat = new Material(rRend.sharedMaterial);
                }
            }

            if (coreMat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
                if (shader != null)
                {
                    coreMat = new Material(shader);
                }
            }

            if (coreMat != null)
            {
                coreMat.color = new Color(0.95f, 0.85f, 0.25f, 1f);
                innerRend.material = coreMat;
            }
        }

        return placeholder;
    }

    /// <summary>
    /// Creates a 2D double-sided circular disc mesh with normal and UV coordinates.
    /// </summary>
    private static Mesh CreateCircleMesh(float radius, int segments)
    {
        Mesh mesh = new Mesh();
        mesh.name = "Procedural_Circle2D";

        int vertexCount = segments + 1;
        Vector3[] vertices = new Vector3[vertexCount * 2];
        Vector3[] normals = new Vector3[vertexCount * 2];
        Vector2[] uvs = new Vector2[vertexCount * 2];

        // Front face center
        vertices[0] = Vector3.zero;
        normals[0] = -Vector3.forward;
        uvs[0] = new Vector2(0.5f, 0.5f);

        // Back face center
        vertices[vertexCount] = Vector3.zero;
        normals[vertexCount] = Vector3.forward;
        uvs[vertexCount] = new Vector2(0.5f, 0.5f);

        float angleStep = (Mathf.PI * 2f) / segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * angleStep;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            Vector3 pos = new Vector3(cos * radius, sin * radius, 0f);
            Vector2 uv = new Vector2((cos + 1f) * 0.5f, (sin + 1f) * 0.5f);

            // Front
            vertices[i + 1] = pos;
            normals[i + 1] = -Vector3.forward;
            uvs[i + 1] = uv;

            // Back
            vertices[vertexCount + i + 1] = pos;
            normals[vertexCount + i + 1] = Vector3.forward;
            uvs[vertexCount + i + 1] = uv;
        }

        int[] triangles = new int[segments * 3 * 2];
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;

            // Front face (clockwise)
            triangles[i * 3 + 0] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = next + 1;

            // Back face (counter-clockwise)
            int backOffset = segments * 3;
            triangles[backOffset + i * 3 + 0] = vertexCount;
            triangles[backOffset + i * 3 + 1] = vertexCount + next + 1;
            triangles[backOffset + i * 3 + 2] = vertexCount + i + 1;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        return mesh;
    }

    /// <summary>
    /// Instantiates a reward prefab (or placeholder) at the given world position and attaches the canvas.
    /// </summary>
    private GameObject SpawnRewardItem(GachaReward reward, Vector3 worldPos, int index)
    {
        int tierInt = reward != null ? reward.TierInt : 1;
        GameObject prefab = GetPrefabForTier(tierInt);
        GameObject itemInstance;
        string itemName = $"Reward_{index + 1}_Tier{tierInt}_{reward?.id ?? "Item"}";

        if (prefab != null)
        {
            itemInstance = Instantiate(prefab, worldPos, Quaternion.identity);
            itemInstance.name = itemName;
        }
        else
        {
            Debug.LogWarning($"No prefab available for tier '{tierInt}' at reward index {index}. Creating placeholder empty GameObject.");
            itemInstance = new GameObject(itemName);
            itemInstance.transform.position = worldPos;
            itemInstance.transform.rotation = Quaternion.identity;
        }

        AttachRewardInfoCanvas(itemInstance, reward);
        currentSpawnedItems.Add(itemInstance);
        return itemInstance;
    }

    private string FormatRewardLabel(GachaReward reward)
    {
        if (reward == null) return string.Empty;
        string nameStr = !string.IsNullOrEmpty(reward.name) ? reward.name : "Unknown";
        string typeStr = !string.IsNullOrEmpty(reward.type)
            ? reward.type
            : (reward.IsSupportCard() ? "Support Card" : "Character");

        if (reward.IsSupportCard())
        {
            var sc = reward.GetSupportCard();
            string tierStr = sc.GetTierDisplayName(); // C, R, SR
            int maxLvl = sc.MaxLevel;
            if (sc?.skills != null && sc.skills.Length > 0)
            {
                return $"{nameStr} [{tierStr} | Max Lv.{maxLvl}]\n<{typeStr}> ({string.Join(", ", sc.skills)})";
            }
            return $"{nameStr} [{tierStr} | Max Lv.{maxLvl}]\n<{typeStr}>";
        }
        else
        {
            var ch = reward.GetCharacter();
            string stars = ch.GetTierDisplayName(); // e.g., "1★", "2★", "3★"
            if (ch != null && ch.health > 0)
            {
                return $"{nameStr} [{stars}]\n<{typeStr}> (HP: {ch.health})";
            }
            return $"{nameStr} [{stars}]\n<{typeStr}>";
        }
    }

    /// <summary>
    /// Instantiates the reward info canvas prefab as a child and sets the text to name and <type>.
    /// Skipped for SingleItemOnClick strategy in favor of the dedicated UI TMP_Text element.
    /// </summary>
    private void AttachRewardInfoCanvas(GameObject itemInstance, GachaReward reward)
    {
        if (strategy == SpawnStrategy.SingleItemOnClick)
        {
            return;
        }

        if (itemInstance == null)
        {
            return;
        }

        GameObject canvasInstance = Instantiate(rewardCanvasPrefab, itemInstance.transform, false);
        TMP_Text textComponent = canvasInstance.GetComponentInChildren<TMP_Text>();
        if (textComponent != null)
        {
            textComponent.text = FormatRewardLabel(reward);
        }
        else
        {
            Debug.LogWarning("Reward canvas prefab does not contain a TMP_Text component.");
        }
    }

    /// <summary>
    /// Updates and shows the UI TMP_Text with the current reward's name and <type>.
    /// </summary>
    public void UpdateRewardInfoText(GachaReward reward)
    {
        rewardInfoText.text = FormatRewardLabel(reward);
        rewardInfoText.gameObject.SetActive(true);
    }

    /// <summary>
    /// Hides the UI TMP_Text component.
    /// </summary>
    public void HideRewardInfoText()
    {
        rewardInfoText.gameObject.SetActive(false);
    }

    /// <summary>
    /// Overload for List of rewards.
    /// </summary>
    public void AssembleRewards(List<GachaReward> rewards)
    {
        AssembleRewards(rewards?.ToArray());
    }
}
