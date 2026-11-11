using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public sealed class BattleSceneController : MonoBehaviour
{
    [SerializeField] private Image[] characterImages;
    [SerializeField] private Image[] enemyImages;
    [Tooltip("Array indexes align corresponding character slots across lanes: Front[0] aligns with Close[0], Mid[0], and Back[0].")]
    [SerializeField] private Transform[] characterFrontPositions;
    [Tooltip("Array indexes align corresponding character slots across lanes: Close[0] aligns with Front[0], Mid[0], and Back[0].")]
    [SerializeField] private Transform[] characterClosePositions;
    [Tooltip("Array indexes align corresponding character slots across lanes: Mid[0] aligns with Front[0], Close[0], and Back[0].")]
    [SerializeField] private Transform[] characterMidPositions;
    [Tooltip("Array indexes align corresponding character slots across lanes: Back[0] aligns with Front[0], Close[0], and Mid[0].")]
    [SerializeField] private Transform[] characterBackPositions;
    [Tooltip("Array indexes align corresponding enemy slots across lanes: Front[0] aligns with Mid[0] and Back[0].")]
    [SerializeField] private Transform[] enemyFrontPositions;
    [Tooltip("Array indexes align corresponding enemy slots across lanes: Mid[0] aligns with Front[0] and Back[0].")]
    [SerializeField] private Transform[] enemyMidPositions;
    [Tooltip("Array indexes align corresponding enemy slots across lanes: Back[0] aligns with Front[0] and Mid[0].")]
    [SerializeField] private Transform[] enemyBackPositions;
    [SerializeField] private TMP_Text logText;
    [SerializeField] private Button finishButton;

    private const float InitialPlaybackSpeed = 1f;
    private const float GearTriggerPauseDelay = 0.2f;
    private const float GearTriggerTransitionDuration = 0.3f;
    private const float GearTriggerDisplayDuration = 1f;
    private const float FloatingDamageDuration = 1.2f;
    private const float FloatingTextQueueGap = 0.3f;
    private const float FloatingDamageStartOffset = 0.05f;
    private const float FloatingDamageEndOffset = 0.2f;
    private static readonly Color CrippledTextColor = new Color(1f, 0.5f, 0f, 1f);
    private const float AttackDashDuration = 0.25f;
    private const float AttackDashDistance = 0.15f;
    private const float VictoryDelay = 1f;
    private const float EnemyDisappearDuration = 1.5f;
    private const float VictoryCheerDuration = 0.75f;
    private const float VictoryCheerHeight = 0.12f;
    private const float VictoryExitDuration = 2f;
    private float playbackSpeed = InitialPlaybackSpeed;
    private float previousTimeScale = 1f;
    private bool isBattlePaused;
    private bool isShowingGearIntro;
    private GameObject activeGearIntroObject;
    private readonly Dictionary<string, BattleCombatant> combatantsById =
        new Dictionary<string, BattleCombatant>();
    private readonly Dictionary<string, CombatantUi> uiByCombatantId =
        new Dictionary<string, CombatantUi>();
    private readonly Dictionary<string, string> namesById =
        new Dictionary<string, string>();
    private readonly Dictionary<string, float> lastAttackTimesById =
        new Dictionary<string, float>();
    private readonly Dictionary<string, CrippledTextState> crippledTextsByCombatantId =
        new Dictionary<string, CrippledTextState>();
    private readonly HashSet<string> combatantsWithGearIntro =
        new HashSet<string>();
    private readonly StringBuilder logBuilder = new StringBuilder();

    private void OnEnable()
    {
        previousTimeScale = Time.timeScale;
        isBattlePaused = false;
        isShowingGearIntro = false;
        ApplyPlaybackTimeScale();
        finishButton.onClick.AddListener(FinishBattle);
    }

    private void OnDisable()
    {
        finishButton.onClick.RemoveListener(FinishBattle);
        isBattlePaused = false;
        isShowingGearIntro = false;
        if (activeGearIntroObject != null)
        {
            Destroy(activeGearIntroObject);
            activeGearIntroObject = null;
        }
        Time.timeScale = previousTimeScale;
    }

    public void PauseBattle()
    {
        isBattlePaused = true;
        ApplyPlaybackTimeScale();
    }

    public void ResumeBattle()
    {
        isBattlePaused = false;
        ApplyPlaybackTimeScale();
    }

    public void SetPlaybackSpeed(float speed)
    {
        if (speed <= 0f || float.IsNaN(speed) || float.IsInfinity(speed))
        {
            throw new ArgumentOutOfRangeException(
                nameof(speed), "Playback speed must be positive and finite.");
        }

        playbackSpeed = speed;
        ApplyPlaybackTimeScale();
    }

    private void ApplyPlaybackTimeScale()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }
        Time.timeScale = isBattlePaused || isShowingGearIntro ? 0f : playbackSpeed;
    }

    private void Start()
    {
        logText.text = "Preparing battle...";
        StartCoroutine(LoadAndPlayBattle());
    }

    private IEnumerator LoadAndPlayBattle()
    {
        GameClient client = GameClient.Instance;
        if (!client.IsLoggedIn)
        {
            ShowError("Connect to a player before starting a battle.");
            yield break;
        }

        string playerId = client.CurrentPlayer.playerId;
        string url = client.BackendUrl.TrimEnd('/') + "/battle?player_id="
            + UnityWebRequest.EscapeURL(playerId);

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string responseDetails = request.downloadHandler == null
                    ? string.Empty
                    : request.downloadHandler.text;
                ShowError("Could not load battle: " + request.error
                    + (string.IsNullOrEmpty(responseDetails) ? string.Empty : "\n" + responseDetails));
                yield break;
            }

            BattleReplay replay;
            try
            {
                replay = JsonUtility.FromJson<BattleReplay>(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                ShowError("Could not read battle response: " + exception.Message);
                yield break;
            }

            if (!IsValidReplay(replay))
            {
                ShowError("The backend returned an incomplete battle replay.");
                yield break;
            }

            if (!InitializeCombatants(replay.combatants))
            {
                yield break;
            }
            ClearLog();
            yield return PlayActions(replay.actions);
            if (replay.winner == "character")
            {
                yield return PlayVictorySequence();
            }
            AppendLog("Battle finished. Winner: " + replay.winner + ".");
        }
    }

    private static bool IsValidReplay(BattleReplay replay)
    {
        if (replay == null || !replay.success || replay.combatants == null
            || replay.actions == null || string.IsNullOrEmpty(replay.winner))
        {
            return false;
        }

        int characterCount = 0;
        int enemyCount = 0;
        Dictionary<string, int> positionCounts = new Dictionary<string, int>();
        foreach (BattleCombatant combatant in replay.combatants)
        {
            if (combatant == null || string.IsNullOrEmpty(combatant.id)
                || combatant.traits == null)
            {
                return false;
            }

            if (combatant.side == "character")
            {
                characterCount++;
            }
            else if (combatant.side == "enemy")
            {
                enemyCount++;
            }
            else
            {
                return false;
            }

            string position = GetPosition(combatant);
            if (combatant.side == "enemy" && position == "close")
            {
                return false;
            }
            string key = combatant.side + ":" + position;
            positionCounts.TryGetValue(key, out int positionCount);
            positionCounts[key] = positionCount + 1;
            if (positionCounts[key] > (combatant.side == "character" ? 2 : 3))
            {
                return false;
            }
        }

        return characterCount > 0 && characterCount <= 8 && enemyCount > 0 && enemyCount <= 9;
    }

    private bool InitializeCombatants(List<BattleCombatant> combatants)
    {
        combatantsById.Clear();
        uiByCombatantId.Clear();
        namesById.Clear();
        lastAttackTimesById.Clear();
        crippledTextsByCombatantId.Clear();
        combatantsWithGearIntro.Clear();
        RectTransform canvasRect = (RectTransform)logText.canvas.rootCanvas.transform;

        int characterCount = 0;
        int enemyCount = 0;
        foreach (BattleCombatant combatant in combatants)
        {
            if (combatant.side == "character")
            {
                characterCount++;
            }
            else
            {
                enemyCount++;
            }
        }
        if (!EnsureImageCapacity(ref characterImages, characterCount, "character")
            || !EnsureImageCapacity(ref enemyImages, enemyCount, "enemy"))
        {
            return false;
        }
        if (!TryAssignRandomEnemySlots(combatants, out Dictionary<string, int> enemySlotIndices))
        {
            return false;
        }

        foreach (Image image in characterImages)
        {
            if (image == null)
            {
                continue;
            }
            image.gameObject.SetActive(false);
            image.color = Color.white;
        }
        foreach (Image image in enemyImages)
        {
            if (image == null)
            {
                continue;
            }
            image.gameObject.SetActive(false);
            image.color = Color.white;
        }

        int characterIndex = 0;
        int enemyIndex = 0;
        Dictionary<string, int> positionSlotIndices = new Dictionary<string, int>();
        List<CombatantRenderOrder> combatantRenderOrder =
            new List<CombatantRenderOrder>();

        foreach (BattleCombatant combatant in combatants)
        {
            Image[] sideImages = combatant.side == "character" ? characterImages : enemyImages;
            int sideIndex = combatant.side == "character" ? characterIndex++ : enemyIndex++;
            if (sideIndex >= sideImages.Length)
            {
                ShowError("Assign an image for every " + combatant.side + " combatant in the Inspector.");
                return false;
            }

            Image image = sideImages[sideIndex];
            string position = GetPosition(combatant);
            int slotIndex;
            if (combatant.side == "enemy")
            {
                slotIndex = enemySlotIndices[combatant.id];
            }
            else
            {
                string positionKey = combatant.side + ":" + position;
                positionSlotIndices.TryGetValue(positionKey, out slotIndex);
                positionSlotIndices[positionKey] = slotIndex + 1;
            }
            Transform[] lanePositions = GetLanePositions(combatant.side, position);
            if (slotIndex >= lanePositions.Length || lanePositions[slotIndex] == null)
            {
                ShowError("Assign a " + combatant.side + " position GameObject for "
                    + position + " slot " + (slotIndex + 1) + " in the Inspector.");
                return false;
            }
            if (combatant.side == "character" && !string.IsNullOrEmpty(combatant.character_id))
            {
                Sprite characterSprite = Resources.Load<Sprite>(
                    "Images/Characters/" + combatant.character_id);
                if (characterSprite != null)
                {
                    image.sprite = characterSprite;
                }
            }

            Slider[] sliders = image.GetComponentsInChildren<Slider>(true);
            if (sliders.Length < 2)
            {
                ShowError("The " + combatant.side + " image at index " + sideIndex
                    + " must have health and cooldown child Sliders.");
                return false;
            }

            PositionCombatantImage(
                image.rectTransform,
                sliders,
                canvasRect,
                lanePositions[slotIndex]);
            combatant.health = combatant.initial_health;
            combatantsById.Add(combatant.id, combatant);
            CombatantUi combatantUi = new CombatantUi(image, sliders[0], sliders[1]);
            uiByCombatantId.Add(combatant.id, combatantUi);
            combatantRenderOrder.Add(new CombatantRenderOrder(
                combatant.side, position, slotIndex, combatantUi));
            lastAttackTimesById.Add(combatant.id, 0f);
            namesById.Add(combatant.id, string.IsNullOrEmpty(combatant.name)
                ? combatant.side
                : combatant.name);
            SetHealth(sliders[0], combatant.max_health, combatant.initial_health);
            SetCooldown(sliders[1], 0f);
            image.gameObject.SetActive(true);
        }

        SortCombatantRenderOrder(combatantRenderOrder);
        return true;
    }

    private static void SortCombatantRenderOrder(List<CombatantRenderOrder> combatants)
    {
        combatants.Sort((left, right) =>
        {
            int sideOrder = string.CompareOrdinal(left.side, right.side);
            if (sideOrder != 0)
            {
                return sideOrder;
            }

            int laneOrder = string.CompareOrdinal(left.position, right.position);
            return laneOrder != 0
                ? laneOrder
                : left.slotIndex.CompareTo(right.slotIndex);
        });

        foreach (CombatantRenderOrder combatant in combatants)
        {
            combatant.ui.slider.transform.SetAsLastSibling();
            combatant.ui.cooldownSlider.transform.SetAsLastSibling();
            combatant.ui.image.transform.SetAsLastSibling();
        }
    }

    private bool TryAssignRandomEnemySlots(
        List<BattleCombatant> combatants,
        out Dictionary<string, int> slotIndices)
    {
        slotIndices = new Dictionary<string, int>();
        HashSet<string> processedLanes = new HashSet<string>();
        foreach (BattleCombatant combatant in combatants)
        {
            if (combatant.side != "enemy")
            {
                continue;
            }

            string lane = GetPosition(combatant);
            if (!processedLanes.Add(lane))
            {
                continue;
            }

            List<BattleCombatant> laneEnemies = new List<BattleCombatant>();
            foreach (BattleCombatant candidate in combatants)
            {
                if (candidate.side == "enemy" && GetPosition(candidate) == lane)
                {
                    laneEnemies.Add(candidate);
                }
            }

            Transform[] lanePositions = GetLanePositions("enemy", lane);
            List<int> availableSlots = new List<int>();
            for (int i = 0; i < lanePositions.Length; i++)
            {
                if (lanePositions[i] != null)
                {
                    availableSlots.Add(i);
                }
            }
            if (laneEnemies.Count > availableSlots.Count)
            {
                ShowError("Assign a position GameObject for every enemy in the " + lane + " lane.");
                return false;
            }

            for (int i = 0; i < availableSlots.Count; i++)
            {
                int swapIndex = UnityEngine.Random.Range(i, availableSlots.Count);
                (availableSlots[i], availableSlots[swapIndex]) =
                    (availableSlots[swapIndex], availableSlots[i]);
            }
            for (int i = 0; i < laneEnemies.Count; i++)
            {
                slotIndices.Add(laneEnemies[i].id, availableSlots[i]);
            }
        }
        return true;
    }

    private bool EnsureImageCapacity(ref Image[] images, int requiredCount, string side)
    {
        images = images ?? Array.Empty<Image>();
        if (requiredCount == 0)
        {
            return true;
        }
        Image template = null;
        foreach (Image image in images)
        {
            if (image != null)
            {
                template = image;
                break;
            }
        }
        if (template == null)
        {
            ShowError("Assign at least one " + side + " image template in the Inspector.");
            return false;
        }

        Image[] expandedImages = new Image[Mathf.Max(requiredCount, images.Length)];
        Array.Copy(images, expandedImages, images.Length);
        for (int i = 0; i < requiredCount; i++)
        {
            if (expandedImages[i] != null)
            {
                continue;
            }
            GameObject clone = Instantiate(
                template.gameObject, template.transform.parent, false);
            clone.name = template.name + " (" + (i + 1) + ")";
            clone.SetActive(false);
            expandedImages[i] = clone.GetComponent<Image>();
        }
        images = expandedImages;
        return true;
    }

    private static string GetPosition(BattleCombatant combatant)
    {
        if (combatant.position == "front" || combatant.position == "close"
            || combatant.position == "mid" || combatant.position == "back")
        {
            return combatant.position;
        }
        return combatant.side == "enemy" ? "mid" : "front";
    }

    private Transform[] GetLanePositions(string side, string lane)
    {
        if (side == "character")
        {
            return lane switch
            {
                "front" => characterFrontPositions ?? Array.Empty<Transform>(),
                "close" => characterClosePositions ?? Array.Empty<Transform>(),
                "mid" => characterMidPositions ?? Array.Empty<Transform>(),
                "back" => characterBackPositions ?? Array.Empty<Transform>(),
                _ => Array.Empty<Transform>()
            };
        }

        return lane switch
        {
            "front" => enemyFrontPositions ?? Array.Empty<Transform>(),
            "mid" => enemyMidPositions ?? Array.Empty<Transform>(),
            "back" => enemyBackPositions ?? Array.Empty<Transform>(),
            _ => Array.Empty<Transform>()
        };
    }

    private static void PositionCombatantImage(
        RectTransform imageRect,
        Slider[] sliders,
        RectTransform canvasRect,
        Transform lanePosition)
    {
        Vector3[] sliderOffsets = new Vector3[sliders.Length];
        for (int i = 0; i < sliders.Length; i++)
        {
            sliderOffsets[i] = imageRect.InverseTransformPoint(sliders[i].transform.position);
            sliders[i].transform.SetParent(canvasRect, true);
        }

        imageRect.SetParent(canvasRect, true);
        imageRect.position = lanePosition.position;

        for (int i = 0; i < sliders.Length; i++)
        {
            sliders[i].transform.position = imageRect.TransformPoint(sliderOffsets[i]);
        }
    }

    private IEnumerator PlayActions(List<BattleAction> actions)
    {
        float previousTime = 0f;
        foreach (BattleAction action in actions)
        {
            float actionTime = Mathf.Max(previousTime, action.time_seconds);
            while (previousTime < actionTime)
            {
                yield return null;
                previousTime = Mathf.Min(
                    actionTime, previousTime + Time.deltaTime);
                UpdateCooldowns(previousTime);
            }

            if (!combatantsById.TryGetValue(action.target_id, out BattleCombatant target)
                || !uiByCombatantId.TryGetValue(action.target_id, out CombatantUi targetUi))
            {
                ShowError("Battle replay references an unknown target: " + action.target_id);
                yield break;
            }

            if (IsGearEffectTrigger(action)
                && combatantsById.TryGetValue(action.attacker_id, out BattleCombatant gearAttacker)
                && gearAttacker.side == "character"
                && uiByCombatantId.TryGetValue(action.attacker_id, out CombatantUi gearAttackerUi)
                && combatantsWithGearIntro.Add(action.attacker_id))
            {
                yield return ShowGearTriggerIntro(gearAttackerUi.image);
            }

            if (combatantsById.TryGetValue(action.attacker_id, out BattleCombatant attacker)
                && uiByCombatantId.TryGetValue(action.attacker_id, out CombatantUi attackerUi))
            {
                float direction = attacker.side == "character" ? 1f : -1f;
                StartCoroutine(AnimateAttackDash(attackerUi.image.rectTransform, direction));
                if (action.type == "attack" || action.type == "execute")
                {
                    lastAttackTimesById[action.attacker_id] = actionTime;
                    SetCooldown(attackerUi.cooldownSlider, 0f);
                }
            }

            targetUi.slider.value = Mathf.Clamp(action.target_health, 0, target.max_health);
            if (action.target_health <= 0)
            {
                targetUi.image.color = Color.gray;
                targetUi.slider.gameObject.SetActive(false);
                targetUi.cooldownSlider.gameObject.SetActive(false);
            }
            if (action.damage > 0)
            {
                QueueFloatingText(
                    targetUi, "-" + action.damage, new Color(0.6f, 0.08f, 0.08f, 1f));
            }
            else if (action.evaded)
            {
                QueueFloatingText(targetUi, "Evade", new Color(0.2f, 0.2f, 0.2f, 1f));
            }

            string attackerName = namesById[action.attacker_id];
            string targetName = namesById[action.target_id];
            string attackName = action.type switch
            {
                "double_slash" => "double-slash",
                "execute" => "execute",
                _ => "attack"
            };
            if (action.triggered_skills != null)
            {
                foreach (BattleTriggeredSkill triggeredSkill in action.triggered_skills)
                {
                    if (triggeredSkill != null)
                    {
                        AppendLog(FormatTime(action.time_seconds) + " " + attackerName
                            + " triggered " + triggeredSkill.name + ".");
                    }
                }
            }
            if (action.triggered_effects != null)
            {
                foreach (BattleTriggeredEffect triggeredEffect in action.triggered_effects)
                {
                    if (triggeredEffect != null)
                    {
                        if (triggeredEffect.id == "gear_002")
                        {
                            ShowCrippledText(
                                target.id,
                                targetUi.image,
                                triggeredEffect.duration_seconds);
                        }
                        AppendLog(FormatTime(action.time_seconds) + " " + attackerName
                            + " triggered " + triggeredEffect.name + " on " + targetName
                            + ", halving remaining damage mitigation after Impact for "
                            + triggeredEffect.duration_seconds.ToString("0.###") + "s.");
                    }
                }
            }
            if (action.evaded)
            {
                AppendLog(FormatTime(action.time_seconds) + " " + targetName + " evaded "
                    + attackerName + "'s " + attackName + " #" + action.attacker_attack_count + ".");
            }
            else if (action.type == "execute")
            {
                AppendLog(FormatTime(action.time_seconds) + " " + attackerName
                    + " executed " + targetName + " for " + action.damage
                    + " damage. " + target.health + " -> " + action.target_health + " HP.");
            }
            else
            {
                AppendLog(FormatTime(action.time_seconds) + " " + attackerName + " "
                    + attackName + " #" + action.attacker_attack_count + " hit "
                    + targetName + " for " + action.damage + " damage. "
                    + target.health + " -> " + action.target_health + " HP.");
            }
            target.health = action.target_health;
        }
    }

    private static bool IsGearEffectTrigger(BattleAction action)
    {
        if (action.type == "execute" || action.type == "double_slash")
        {
            return true;
        }
        if (action.triggered_effects == null)
        {
            return false;
        }
        foreach (BattleTriggeredEffect effect in action.triggered_effects)
        {
            if (effect != null)
            {
                return true;
            }
        }
        return false;
    }

    private void UpdateCooldowns(float simulationTime)
    {
        foreach (KeyValuePair<string, CombatantUi> entry in uiByCombatantId)
        {
            BattleCombatant combatant = combatantsById[entry.Key];
            float interval = AttackInterval(combatant.traits.agility);
            float elapsed = simulationTime - lastAttackTimesById[entry.Key];
            SetCooldown(entry.Value.cooldownSlider, elapsed / interval);
        }
    }

    private static float AttackInterval(int agility)
    {
        float safeAgility = Mathf.Max(1f, agility);
        return Mathf.Max(0.5f, 3f - 1.25f * Mathf.Log10(safeAgility / 100f));
    }

    private void QueueFloatingText(CombatantUi combatantUi, string message, Color color)
    {
        combatantUi.floatingTexts.Enqueue(new FloatingTextRequest(message, color));
        if (!combatantUi.isDisplayingFloatingText)
        {
            StartCoroutine(DisplayFloatingTextQueue(combatantUi));
        }
    }

    private IEnumerator DisplayFloatingTextQueue(CombatantUi combatantUi)
    {
        combatantUi.isDisplayingFloatingText = true;
        while (combatantUi.floatingTexts.Count > 0)
        {
            FloatingTextRequest request = combatantUi.floatingTexts.Dequeue();
            StartCoroutine(ShowFloatingText(combatantUi.image, request.message, request.color));
            yield return new WaitForSeconds(FloatingTextQueueGap);
        }
        combatantUi.isDisplayingFloatingText = false;
    }

    private IEnumerator ShowFloatingText(Image targetImage, string message, Color textColor)
    {
        Canvas canvas = targetImage.canvas.rootCanvas;
        RectTransform canvasRect = (RectTransform)canvas.transform;
        RectTransform imageRect = targetImage.rectTransform;
        Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : canvas.worldCamera;
        Vector2 startPosition = GetTextPosition(
            imageRect, canvasRect, eventCamera, -FloatingDamageStartOffset);
        Vector2 endPosition = GetTextPosition(
            imageRect, canvasRect, eventCamera, FloatingDamageEndOffset);

        TextMeshProUGUI floatingText = CreateFloatingText(
            canvasRect, startPosition, message, textColor);
        RectTransform textRect = floatingText.rectTransform;

        float elapsed = 0f;
        while (elapsed < FloatingDamageDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / FloatingDamageDuration);
            textRect.anchoredPosition = Vector2.Lerp(startPosition, endPosition, progress);
            Color color = floatingText.color;
            color.a = 1f - Mathf.Clamp01((progress - 0.5f) * 2f);
            floatingText.color = color;
            yield return null;
        }
        Destroy(floatingText.gameObject);
    }

    private void ShowCrippledText(string targetId, Image targetImage, float duration)
    {
        float expiresAt = Time.time + Mathf.Max(0f, duration);
        if (crippledTextsByCombatantId.TryGetValue(targetId, out CrippledTextState existing))
        {
            existing.expiresAt = Mathf.Max(existing.expiresAt, expiresAt);
            return;
        }

        Canvas canvas = targetImage.canvas.rootCanvas;
        RectTransform canvasRect = (RectTransform)canvas.transform;
        Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : canvas.worldCamera;
        Vector2 position = GetTextPosition(
            targetImage.rectTransform, canvasRect, eventCamera, -FloatingDamageStartOffset);
        TextMeshProUGUI text = CreateFloatingText(
            canvasRect, position, "crippled", CrippledTextColor);
        CrippledTextState state = new CrippledTextState(text, expiresAt);
        crippledTextsByCombatantId.Add(targetId, state);
        StartCoroutine(RemoveCrippledTextWhenExpired(targetId, state));
    }

    private IEnumerator ShowGearTriggerIntro(Image characterImage)
    {
        isShowingGearIntro = true;
        ApplyPlaybackTimeScale();
        GameObject introObject = null;
        try
        {
            yield return new WaitForSecondsRealtime(GearTriggerPauseDelay);

            Canvas canvas = characterImage.canvas.rootCanvas;
            RectTransform canvasRect = (RectTransform)canvas.transform;
            introObject = new GameObject(
                "Gear Trigger Character",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            activeGearIntroObject = introObject;

            RectTransform introRect = (RectTransform)introObject.transform;
            introRect.SetParent(canvasRect, false);
            introRect.SetAsLastSibling();
            introRect.anchorMin = new Vector2(0.5f, 0.5f);
            introRect.anchorMax = new Vector2(0.5f, 0.5f);
            introRect.pivot = new Vector2(0.5f, 0.5f);
            introRect.sizeDelta = canvasRect.rect.size;

            Image introImage = introObject.GetComponent<Image>();
            introImage.sprite = characterImage.sprite;
            introImage.material = characterImage.material;
            introImage.preserveAspect = true;
            introImage.raycastTarget = false;
            Color visibleColor = characterImage.color;
            Color transparentColor = visibleColor;
            transparentColor.a = 0f;
            introImage.color = transparentColor;

            Vector2 startPosition = new Vector2(-canvasRect.rect.width, 0f);
            introRect.anchoredPosition = startPosition;
            float elapsed = 0f;
            while (elapsed < GearTriggerTransitionDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(
                    0f, 1f, Mathf.Clamp01(elapsed / GearTriggerTransitionDuration));
                introRect.anchoredPosition = Vector2.Lerp(startPosition, Vector2.zero, progress);
                Color color = visibleColor;
                color.a = visibleColor.a * progress;
                introImage.color = color;
                yield return null;
            }
            introRect.anchoredPosition = Vector2.zero;
            introImage.color = visibleColor;
            yield return new WaitForSecondsRealtime(GearTriggerDisplayDuration);

            Vector2 endPosition = new Vector2(canvasRect.rect.width, 0f);
            elapsed = 0f;
            while (elapsed < GearTriggerTransitionDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(
                    0f, 1f, Mathf.Clamp01(elapsed / GearTriggerTransitionDuration));
                introRect.anchoredPosition = Vector2.Lerp(Vector2.zero, endPosition, progress);
                Color color = visibleColor;
                color.a = visibleColor.a * (1f - progress);
                introImage.color = color;
                yield return null;
            }
            introRect.anchoredPosition = endPosition;
            Color hiddenColor = visibleColor;
            hiddenColor.a = 0f;
            introImage.color = hiddenColor;

            yield return new WaitForSecondsRealtime(GearTriggerPauseDelay);
        }
        finally
        {
            if (introObject != null)
            {
                Destroy(introObject);
            }
            if (activeGearIntroObject == introObject)
            {
                activeGearIntroObject = null;
            }
            isShowingGearIntro = false;
            ApplyPlaybackTimeScale();
        }
    }

    private IEnumerator RemoveCrippledTextWhenExpired(string targetId, CrippledTextState state)
    {
        while (Time.time < state.expiresAt)
        {
            yield return null;
        }

        crippledTextsByCombatantId.Remove(targetId);
        Destroy(state.text.gameObject);
    }

    private TextMeshProUGUI CreateFloatingText(
        RectTransform canvasRect, Vector2 position, string message, Color textColor)
    {
        GameObject textObject = new GameObject(
            "Floating Battle Text",
            typeof(RectTransform),
            typeof(TextMeshProUGUI));
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.SetParent(canvasRect, false);
        textRect.SetAsLastSibling();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(200f, 60f);
        textRect.anchoredPosition = position;

        TextMeshProUGUI floatingText = textObject.GetComponent<TextMeshProUGUI>();
        floatingText.font = logText.font;
        floatingText.fontSharedMaterial = logText.fontSharedMaterial;
        floatingText.fontSize = Mathf.Max(24f, logText.fontSize * 1.5f);
        floatingText.alignment = TextAlignmentOptions.Center;
        floatingText.text = message;
        floatingText.color = textColor;
        floatingText.fontStyle = FontStyles.Bold;
        return floatingText;
    }

    private static Vector2 GetTextPosition(
        RectTransform imageRect,
        RectTransform canvasRect,
        Camera eventCamera,
        float verticalOffset)
    {
        Rect imageBounds = imageRect.rect;
        float horizontalCenter = (imageBounds.xMin + imageBounds.xMax) * 0.5f;
        float top = imageBounds.yMax;
        Vector3 worldPosition = imageRect.TransformPoint(
            new Vector3(
                horizontalCenter,
                top + imageBounds.height * verticalOffset));
        return ToCanvasPosition(worldPosition, canvasRect, eventCamera);
    }

    private IEnumerator AnimateAttackDash(RectTransform attackerRect, float direction)
    {
        Vector3 startPosition = attackerRect.localPosition;
        float dashDistance = attackerRect.rect.width * AttackDashDistance * direction;
        float elapsed = 0f;
        while (elapsed < AttackDashDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / AttackDashDuration);
            float dash = Mathf.Sin(progress * Mathf.PI) * dashDistance;
            attackerRect.localPosition = startPosition + Vector3.right * dash;
            yield return null;
        }

        attackerRect.localPosition = startPosition;
    }

    private IEnumerator PlayVictorySequence()
    {
        yield return new WaitForSeconds(VictoryDelay);

        RectTransform canvasRect = (RectTransform)logText.canvas.rootCanvas.transform;
        List<RectTransform> enemies = new List<RectTransform>();
        List<Vector3> enemyScales = new List<Vector3>();
        List<RectTransform> survivingCharacters = new List<RectTransform>();

        foreach (KeyValuePair<string, CombatantUi> entry in uiByCombatantId)
        {
            BattleCombatant combatant = combatantsById[entry.Key];
            if (combatant.side == "enemy")
            {
                RectTransform enemyRect = entry.Value.image.rectTransform;
                enemies.Add(enemyRect);
                enemyScales.Add(enemyRect.localScale);
            }
            else
            {
                entry.Value.slider.gameObject.SetActive(false);
                entry.Value.cooldownSlider.gameObject.SetActive(false);
                if (combatant.health > 0)
                {
                    survivingCharacters.Add(entry.Value.image.rectTransform);
                }
            }
        }

        float elapsed = 0f;
        while (elapsed < EnemyDisappearDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / EnemyDisappearDuration);
            for (int i = 0; i < enemies.Count; i++)
            {
                enemies[i].localScale = Vector3.Lerp(enemyScales[i], Vector3.zero, progress);
            }
            yield return null;
        }

        for (int i = 0; i < enemies.Count; i++)
        {
            enemies[i].localScale = Vector3.zero;
        }

        List<Vector3> characterStartPositions = new List<Vector3>();
        List<float> characterJumpHeights = new List<float>();
        foreach (RectTransform character in survivingCharacters)
        {
            characterStartPositions.Add(character.localPosition);
            characterJumpHeights.Add(character.rect.height * VictoryCheerHeight);
        }

        elapsed = 0f;
        while (elapsed < VictoryCheerDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / VictoryCheerDuration);
            float jumpCycle = Mathf.Abs(Mathf.Sin(progress * Mathf.PI * 2f));
            for (int i = 0; i < survivingCharacters.Count; i++)
            {
                survivingCharacters[i].localPosition = characterStartPositions[i]
                    + Vector3.up * (jumpCycle * characterJumpHeights[i]);
            }
            yield return null;
        }

        List<Vector3> exitTargets = new List<Vector3>();
        for (int i = 0; i < survivingCharacters.Count; i++)
        {
            RectTransform character = survivingCharacters[i];
            character.localPosition = characterStartPositions[i];
            exitTargets.Add(GetOffscreenRightPosition(character, canvasRect));
        }

        List<Vector3> exitStartPositions = new List<Vector3>(characterStartPositions);
        elapsed = 0f;
        while (elapsed < VictoryExitDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / VictoryExitDuration);
            for (int i = 0; i < survivingCharacters.Count; i++)
            {
                survivingCharacters[i].localPosition =
                    Vector3.Lerp(exitStartPositions[i], exitTargets[i], progress);
            }
            yield return null;
        }

        for (int i = 0; i < survivingCharacters.Count; i++)
        {
            survivingCharacters[i].localPosition = exitTargets[i];
        }
    }

    private static Vector3 GetOffscreenRightPosition(
        RectTransform imageRect, RectTransform canvasRect)
    {
        Vector3[] imageCorners = new Vector3[4];
        imageRect.GetWorldCorners(imageCorners);
        Vector3 pivotInCanvas = canvasRect.InverseTransformPoint(imageRect.position);
        float rightExtentFromPivot = float.NegativeInfinity;
        foreach (Vector3 corner in imageCorners)
        {
            float cornerX = canvasRect.InverseTransformPoint(corner).x;
            rightExtentFromPivot = Mathf.Max(
                rightExtentFromPivot, cornerX - pivotInCanvas.x);
        }

        Vector3 targetInCanvas = new Vector3(
            canvasRect.rect.xMax + 50f + imageRect.rect.width * 2f - rightExtentFromPivot,
            pivotInCanvas.y,
            pivotInCanvas.z);
        Vector3 targetWorld = canvasRect.TransformPoint(targetInCanvas);
        return imageRect.parent.InverseTransformPoint(targetWorld);
    }

    private static Vector2 ToCanvasPosition(
        Vector3 worldPosition, RectTransform canvasRect, Camera eventCamera)
    {
        Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(
            eventCamera, worldPosition);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, screenPosition, eventCamera, out Vector2 canvasPosition);
        return canvasPosition;
    }

    private static void SetHealth(Slider slider, int maxHealth, int health)
    {
        slider.minValue = 0;
        slider.maxValue = Mathf.Max(1, maxHealth);
        slider.wholeNumbers = true;
        slider.value = Mathf.Clamp(health, 0, slider.maxValue);
    }

    private static void SetCooldown(Slider slider, float progress)
    {
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.value = Mathf.Clamp01(progress);
    }

    private sealed class CombatantUi
    {
        public readonly Image image;
        public readonly Slider slider;
        public readonly Slider cooldownSlider;
        public readonly Queue<FloatingTextRequest> floatingTexts =
            new Queue<FloatingTextRequest>();
        public bool isDisplayingFloatingText;

        public CombatantUi(Image image, Slider slider, Slider cooldownSlider)
        {
            this.image = image;
            this.slider = slider;
            this.cooldownSlider = cooldownSlider;
        }
    }

    private readonly struct CombatantRenderOrder
    {
        public readonly string side;
        public readonly string position;
        public readonly int slotIndex;
        public readonly CombatantUi ui;

        public CombatantRenderOrder(
            string side, string position, int slotIndex, CombatantUi ui)
        {
            this.side = side;
            this.position = position;
            this.slotIndex = slotIndex;
            this.ui = ui;
        }
    }

    private readonly struct FloatingTextRequest
    {
        public readonly string message;
        public readonly Color color;

        public FloatingTextRequest(string message, Color color)
        {
            this.message = message;
            this.color = color;
        }

    }

    private sealed class CrippledTextState
    {
        public readonly TextMeshProUGUI text;
        public float expiresAt;

        public CrippledTextState(TextMeshProUGUI text, float expiresAt)
        {
            this.text = text;
            this.expiresAt = expiresAt;
        }
    }

    private void ClearLog()
    {
        logBuilder.Clear();
        logText.text = string.Empty;
    }

    private void AppendLog(string message)
    {
        logBuilder.AppendLine(message);
        logText.text = logBuilder.ToString();
    }

    private void ShowError(string message)
    {
        Debug.LogError(message, this);
        AppendLog(message);
    }

    private static string FormatTime(float seconds)
    {
        return "[" + seconds.ToString("0.00") + "s]";
    }

    private void FinishBattle()
    {
        GameClient.Instance.UnloadBattleScene();
    }

    [Serializable]
    private sealed class BattleReplay
    {
        public bool success;
        public string winner;
        public List<BattleCombatant> combatants;
        public List<BattleAction> actions;
    }

    [Serializable]
    private sealed class BattleCombatant
    {
        public string id;
        public string side;
        public string name;
        public string character_id;
        public string enemy_id;
        public int tier;
        public string position;
        public BattleTraits traits;
        public int max_health;
        public int initial_health;
        public int health;
    }

    [Serializable]
    private sealed class BattleTraits
    {
        public int power;
        public int agility;
        public int impact;
        public int defense;
        public int finesse;
    }

    [Serializable]
    private sealed class BattleAction
    {
        public float time_seconds;
        public string type;
        public string attacker;
        public string attacker_id;
        public int attacker_attack_count;
        public string target;
        public string target_id;
        public bool evaded;
        public int damage;
        public int target_health;
        public BattleTriggeredSkill[] triggered_skills;
        public BattleTriggeredEffect[] triggered_effects;
    }

    [Serializable]
    private sealed class BattleTriggeredSkill
    {
        public string id;
        public string name;
    }

    [Serializable]
    private sealed class BattleTriggeredEffect
    {
        public string id;
        public string name;
        public float duration_seconds;
    }
}
