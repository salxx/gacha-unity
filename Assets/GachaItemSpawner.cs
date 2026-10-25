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

/// <summary>
/// Presents pulled rewards, either all at once in a line or one by one with an anticipation reveal.
/// </summary>
public class GachaItemSpawner : Singleton<GachaItemSpawner>
{
    [Header("Strategy")]
    [SerializeField] private SpawnStrategy strategy = SpawnStrategy.Line;

    [Header("Single Item UI")]
    [Tooltip("Advances to the next reward when using the SingleItemOnClick strategy.")]
    [SerializeField] private Button nextButton;

    [Tooltip("Skips common pulls until the next rare or SR.")]
    [SerializeField] private Toggle nextPullToggle;

    [Header("Pull Indicators (Click Reveal)")]
    [SerializeField] private Canvas pullCanvas;
    [SerializeField] private GameObject pullImagePrefab;
    [SerializeField] private float indicatorSpacing = 65f;
    [SerializeField] private Vector2 indicatorOffset = Vector2.zero;

    [Header("Indicator Colors")]
    [SerializeField] private Color unknownColor = new Color(0.75f, 0.75f, 0.75f, 1f);
    [SerializeField] private Color commonColor = new Color(0.75f, 0.77f, 0.8f, 1f);
    [SerializeField] private Color rareColor = new Color(0.1f, 0.6f, 1f, 1f);
    [SerializeField] private Color superRareColor = new Color(1f, 0.84f, 0f, 1f);

    [Header("Audio Clips Per Tier")]
    [SerializeField] private AudioClip commonAudioClip;
    [SerializeField] private AudioClip rareAudioClip;
    [SerializeField] private AudioClip superRareAudioClip;

    [Header("Reward Info / UI Canvas")]
    [Tooltip("Prefab with a TMP_Text child, attached to each reward in the Line strategy.")]
    [SerializeField] private GameObject rewardCanvasPrefab;

    [Tooltip("UI text showing the current reward in the SingleItemOnClick strategy.")]
    [SerializeField] private TMP_Text rewardInfoText;

    [Header("Assembly Placement")]
    [SerializeField] private Vector3 spawnCoordinates = Vector3.zero;
    [SerializeField] private float itemSpacing = 2.0f;
    [SerializeField] private Vector3 lineDirection = Vector3.right;
    [SerializeField] private bool centerLine = true;

    [Header("Anticipation & Dramatic Reveal")]
    [SerializeField] private bool enableAnticipation = true;
    [SerializeField] private float anticipationDuration = 1.5f;

    [Tooltip("Optional. If empty, a procedural disc placeholder is generated.")]
    [SerializeField] private GameObject anticipationPrefab;

    [Tooltip("Optional material for the procedural placeholder.")]
    [SerializeField] private Material anticipationMaterial;

    [SerializeField] private bool animateRewardPopIn = true;
    [SerializeField] private float rewardPopInDuration = 0.35f;

    [Tooltip("Seconds a common pull stays visible before auto-advancing while Skip Commons is on.")]
    [SerializeField] private float skipCommonDisplayDuration = 1.0f;

    private const string PrefabFolder = "Prefabs/"; // Assets/Resources/Prefabs
    private const string SupportCardSuffix = "-SC";

    private const float PopInDuration = 0.25f;
    private const float CollapseDuration = 0.08f;
    private static readonly Color PlaceholderOuterColor = new Color(0.18f, 0.20f, 0.26f, 1f);
    private static readonly Color PlaceholderCoreColor = new Color(0.95f, 0.85f, 0.25f, 1f);

    private readonly Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();
    private readonly List<GameObject> spawnedItems = new List<GameObject>();
    private readonly List<GameObject> indicatorObjects = new List<GameObject>();
    private readonly List<Image> indicatorImages = new List<Image>();
    private readonly List<Button> indicatorButtons = new List<Button>();

    private GachaReward[] queuedRewards;
    private int maxRevealedIndex = -1;
    private bool isRevealing;
    private Coroutine revealCoroutine;
    private GameObject activePlaceholder;
    private AudioSource audioSource;

    public event Action OnSequenceStarted;
    public event Action OnSequenceEnded;

    public bool IsSequenceActive { get; private set; }

    private bool IsSkipCommonsActive => nextPullToggle.isOn;

    protected override void Awake()
    {
        base.Awake();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }

        nextButton.onClick.AddListener(OnNextButtonClicked);
        SetSequenceControlsVisible(false);
    }

    protected override void OnDestroy()
    {
        nextButton.onClick.RemoveListener(OnNextButtonClicked);
        base.OnDestroy();
    }

    private void OnDisable()
    {
        StopReveal();
        EndSequence();
    }

    /// <summary>
    /// Replaces any currently shown rewards with the given ones, using the configured strategy.
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
        else
        {
            AssembleRewardsSingleItem(rewards);
        }
    }

    /// <summary>
    /// Destroys all spawned rewards and indicators and ends any active sequence.
    /// </summary>
    public void ClearSpawnedItems()
    {
        StopReveal();
        DestroyAll(spawnedItems);
        ClearPullIndicators();
        queuedRewards = null;
        EndSequence();
    }

    #region Tier lookups

    private Color GetColorForTier(int tier) => PickByTier(tier, commonColor, rareColor, superRareColor);

    /// <summary>
    /// Loads Prefabs/{Common|Rare|SuperRare}[-SC] from Assets/Resources.
    /// </summary>
    private GameObject GetPrefab(int tier, bool supportCard)
    {
        string path = PrefabFolder + PickByTier(tier, "Common", "Rare", "SuperRare") + (supportCard ? SupportCardSuffix : "");
        if (!prefabCache.TryGetValue(path, out GameObject prefab))
        {
            prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"Missing prefab at Assets/Resources/{path}.prefab");
            }
            prefabCache[path] = prefab;
        }

        return prefab;
    }

    private AudioClip GetAudioClipForTier(int tier) => PickByTier(tier, commonAudioClip, rareAudioClip, superRareAudioClip);

    private static T PickByTier<T>(int tier, T common, T rare, T superRare)
    {
        switch (Tiers.Clamp(tier))
        {
            case Tiers.Rare: return rare;
            case Tiers.SuperRare: return superRare;
            default: return common;
        }
    }

    #endregion

    #region Line strategy

    private void AssembleRewardsLine(GachaReward[] rewards)
    {
        Vector3 direction = lineDirection.sqrMagnitude > 0.0001f ? lineDirection.normalized : Vector3.right;

        for (int i = 0; i < rewards.Length; i++)
        {
            float offset = centerLine ? i - (rewards.Length - 1) / 2f : i;
            GameObject item = SpawnRewardItem(rewards[i], spawnCoordinates + direction * (offset * itemSpacing), i);

            GameObject canvas = Instantiate(rewardCanvasPrefab, item.transform, false);
            canvas.GetComponentInChildren<TMP_Text>().text = rewards[i].Label;
        }
    }

    #endregion

    #region Single item strategy

    private void AssembleRewardsSingleItem(GachaReward[] rewards)
    {
        queuedRewards = (GachaReward[])rewards.Clone();
        maxRevealedIndex = -1;
        IsSequenceActive = true;

        SetSequenceControlsVisible(true);
        nextButton.interactable = false;
        nextPullToggle.interactable = false;

        SpawnPullIndicators(queuedRewards.Length);
        OnSequenceStarted?.Invoke();

        AdvanceSingleItemSequence();
    }

    private void OnNextButtonClicked()
    {
        if (IsSequenceActive && !isRevealing)
        {
            AdvanceSingleItemSequence();
        }
    }

    private void AdvanceSingleItemSequence()
    {
        int nextIndex = maxRevealedIndex + 1;
        if (nextIndex < queuedRewards.Length)
        {
            StopReveal();
            revealCoroutine = StartCoroutine(RevealSequenceRoutine(nextIndex));
        }
        else
        {
            ClearSpawnedItems();
        }
    }

    private void OnIndicatorClicked(int index)
    {
        // Only already revealed pulls can be re-displayed.
        if (IsSequenceActive && !isRevealing && index <= maxRevealedIndex)
        {
            DisplayRewardAt(index);
        }
    }

    private void DisplayRewardAt(int index)
    {
        DestroyAll(spawnedItems);
        SpawnRewardItem(queuedRewards[index], spawnCoordinates, index);
        ShowRewardInfo(queuedRewards[index]);
    }

    /// <summary>
    /// Plays the anticipation build-up and reveal for the reward at startIndex. While Skip Commons is on,
    /// commons are revealed without anticipation and the sequence keeps going until a rare or better shows up.
    /// </summary>
    private IEnumerator RevealSequenceRoutine(int startIndex)
    {
        isRevealing = true;
        nextButton.interactable = false;
        nextPullToggle.interactable = true;

        for (int index = startIndex; index < queuedRewards.Length; index++)
        {
            GachaReward reward = queuedRewards[index];
            bool skipThisCommon = reward.Tier == Tiers.Common && IsSkipCommonsActive;

            rewardInfoText.gameObject.SetActive(false);
            DestroyAll(spawnedItems);

            if (!skipThisCommon && enableAnticipation && anticipationDuration > 0f)
            {
                nextPullToggle.interactable = false;
                yield return PlayAnticipation();
            }

            maxRevealedIndex = index;
            GameObject rewardObj = SpawnRewardItem(reward, spawnCoordinates, index);
            audioSource.PlayOneShot(GetAudioClipForTier(reward.Tier));

            if (animateRewardPopIn && rewardPopInDuration > 0f)
            {
                yield return PlayPopIn(rewardObj.transform);
            }

            indicatorImages[index].color = GetColorForTier(reward.Tier);
            indicatorButtons[index].interactable = true;
            ShowRewardInfo(reward);

            bool hasMore = index + 1 < queuedRewards.Length;
            if (!skipThisCommon || !hasMore)
            {
                break;
            }

            nextPullToggle.interactable = true;
            if (skipCommonDisplayDuration > 0f)
            {
                yield return new WaitForSeconds(skipCommonDisplayDuration);
            }
        }

        isRevealing = false;
        revealCoroutine = null;
        nextButton.interactable = true;
        nextPullToggle.interactable = true;
    }

    private IEnumerator PlayAnticipation()
    {
        Vector3 basePos = spawnCoordinates + new Vector3(0f, 0.5f, 0f);
        activePlaceholder = anticipationPrefab != null
            ? Instantiate(anticipationPrefab, basePos, Quaternion.identity)
            : CreateProceduralPlaceholder(basePos);
        Transform placeholder = activePlaceholder.transform;

        for (float elapsed = 0f; elapsed < anticipationDuration; elapsed += Time.deltaTime)
        {
            float t = elapsed / anticipationDuration;

            // Quick pop-in, then a pulse that grows in frequency and amplitude to build tension.
            float popScale = elapsed < PopInDuration ? Mathf.Sin(elapsed / PopInDuration * Mathf.PI * 0.5f) : 1f;
            float pulse = Mathf.Sin(elapsed * Mathf.Lerp(8f, 32f, t)) * Mathf.Lerp(0.08f, 0.35f, t);
            placeholder.localScale = Vector3.one * (popScale * (1f + pulse));

            // Bobbing plus a rumble that intensifies toward the reveal.
            float bob = Mathf.Sin(elapsed * 7f) * 0.12f;
            Vector3 shake = UnityEngine.Random.insideUnitSphere * Mathf.Lerp(0f, 0.09f, t * t);
            placeholder.position = basePos + new Vector3(0f, bob, 0f) + shake;

            yield return null;
        }

        // Climax collapse right before the real item appears.
        Vector3 startScale = placeholder.localScale;
        for (float elapsed = 0f; elapsed < CollapseDuration; elapsed += Time.deltaTime)
        {
            placeholder.localScale = Vector3.Lerp(startScale, Vector3.zero, elapsed / CollapseDuration);
            yield return null;
        }

        Destroy(activePlaceholder);
        activePlaceholder = null;
    }

    private IEnumerator PlayPopIn(Transform target)
    {
        Vector3 targetScale = target.localScale;

        for (float elapsed = 0f; elapsed < rewardPopInDuration; elapsed += Time.deltaTime)
        {
            float t = elapsed / rewardPopInDuration;
            float overshoot = 1f + 0.25f * Mathf.Sin(t * Mathf.PI);
            target.localScale = targetScale * (t * overshoot);
            yield return null;
        }

        target.localScale = targetScale;
    }

    #endregion

    #region Indicators

    private void SpawnPullIndicators(int count)
    {
        ClearPullIndicators();

        float startX = -(count - 1) * indicatorSpacing * 0.5f;
        for (int i = 0; i < count; i++)
        {
            GameObject indicator = Instantiate(pullImagePrefab, pullCanvas.transform, false);
            indicator.name = $"Pull_Indicator_{i + 1}";
            indicator.GetComponent<RectTransform>().anchoredPosition =
                new Vector2(startX + i * indicatorSpacing + indicatorOffset.x, indicatorOffset.y);

            Image image = indicator.GetComponentInChildren<Image>();
            image.color = unknownColor;

            if (!indicator.TryGetComponent(out Button button))
            {
                button = indicator.AddComponent<Button>();
            }
            button.interactable = false;
            int index = i;
            button.onClick.AddListener(() => OnIndicatorClicked(index));

            indicatorObjects.Add(indicator);
            indicatorImages.Add(image);
            indicatorButtons.Add(button);
        }
    }

    private void ClearPullIndicators()
    {
        DestroyAll(indicatorObjects);
        indicatorImages.Clear();
        indicatorButtons.Clear();
        maxRevealedIndex = -1;
    }

    #endregion

    #region Helpers

    private GameObject SpawnRewardItem(GachaReward reward, Vector3 position, int index)
    {
        GameObject item = Instantiate(GetPrefab(reward.Tier, reward.IsSupportCard), position, Quaternion.identity);
        item.name = $"Reward_{index + 1}_Tier{reward.Tier}_{reward.id}";
        spawnedItems.Add(item);
        return item;
    }

    private void ShowRewardInfo(GachaReward reward)
    {
        rewardInfoText.text = reward.Label;
        rewardInfoText.gameObject.SetActive(true);
    }

    private void SetSequenceControlsVisible(bool visible)
    {
        nextButton.gameObject.SetActive(visible);
        nextPullToggle.gameObject.SetActive(visible);
        rewardInfoText.gameObject.SetActive(false);
    }

    private void StopReveal()
    {
        if (revealCoroutine != null)
        {
            StopCoroutine(revealCoroutine);
            revealCoroutine = null;
        }

        if (activePlaceholder != null)
        {
            Destroy(activePlaceholder);
            activePlaceholder = null;
        }

        isRevealing = false;
    }

    private void EndSequence()
    {
        nextButton.interactable = true;
        nextPullToggle.interactable = true;
        SetSequenceControlsVisible(false);

        if (IsSequenceActive)
        {
            IsSequenceActive = false;
            OnSequenceEnded?.Invoke();
        }
    }

    private static void DestroyAll(List<GameObject> objects)
    {
        foreach (GameObject obj in objects)
        {
            if (obj != null)
            {
                Destroy(obj);
            }
        }

        objects.Clear();
    }

    /// <summary>
    /// Builds a two-layer disc (dark outer ring with a glowing core) used when no anticipation prefab is set.
    /// </summary>
    private GameObject CreateProceduralPlaceholder(Vector3 position)
    {
        Mesh disc = CreateCircleMesh(1f, 32);

        GameObject placeholder = CreateDisc("Anticipation_Placeholder", disc, GetPrefab(Tiers.SuperRare, false), PlaceholderOuterColor);
        placeholder.transform.position = position;
        placeholder.transform.localScale = Vector3.zero;

        GameObject core = CreateDisc("Anticipation_Core", disc, GetPrefab(Tiers.Rare, false), PlaceholderCoreColor);
        core.transform.SetParent(placeholder.transform, false);
        core.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        core.transform.localScale = Vector3.one * 0.55f;

        return placeholder;
    }

    private GameObject CreateDisc(string objectName, Mesh mesh, GameObject materialSource, Color color)
    {
        GameObject disc = new GameObject(objectName);
        disc.AddComponent<MeshFilter>().sharedMesh = mesh;
        disc.AddComponent<MeshRenderer>().material = anticipationMaterial != null
            ? anticipationMaterial
            : CreateTintedMaterial(materialSource, color);
        return disc;
    }

    private static Material CreateTintedMaterial(GameObject source, Color color)
    {
        Renderer template = source.GetComponentInChildren<Renderer>();
        Material material = template != null && template.sharedMaterial != null
            ? new Material(template.sharedMaterial)
            : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        material.color = color;
        return material;
    }

    /// <summary>
    /// Creates a double-sided disc mesh in the XY plane.
    /// </summary>
    private static Mesh CreateCircleMesh(float radius, int segments)
    {
        int vertexCount = segments + 1;
        Vector3[] vertices = new Vector3[vertexCount * 2];
        Vector3[] normals = new Vector3[vertexCount * 2];
        Vector2[] uvs = new Vector2[vertexCount * 2];
        int[] triangles = new int[segments * 6];

        for (int side = 0; side < 2; side++)
        {
            int offset = side * vertexCount;
            Vector3 normal = side == 0 ? Vector3.back : Vector3.forward;

            vertices[offset] = Vector3.zero;
            normals[offset] = normal;
            uvs[offset] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                vertices[offset + i + 1] = new Vector3(cos * radius, sin * radius, 0f);
                normals[offset + i + 1] = normal;
                uvs[offset + i + 1] = new Vector2((cos + 1f) * 0.5f, (sin + 1f) * 0.5f);

                int current = offset + i + 1;
                int next = offset + (i + 1) % segments + 1;
                int tri = side * segments * 3 + i * 3;
                triangles[tri] = offset;
                // Opposite winding on the back face so both sides render.
                triangles[tri + 1] = side == 0 ? current : next;
                triangles[tri + 2] = side == 0 ? next : current;
            }
        }

        Mesh mesh = new Mesh { name = "Procedural_Circle2D" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    #endregion
}
