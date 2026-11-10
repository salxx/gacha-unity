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
    [SerializeField] private TMP_Text logText;
    [SerializeField] private Button finishButton;

    private const float PlaybackSpeed = 1f;
    private const float FloatingDamageDuration = 1.2f;
    private const float FloatingTextQueueGap = 0.3f;
    private const float FloatingDamageStartOffset = 0.05f;
    private const float FloatingDamageEndOffset = 0.2f;
    private const float AttackDashDuration = 0.25f;
    private const float AttackDashDistance = 0.15f;
    private const float VictoryDelay = 1f;
    private const float EnemyDisappearDuration = 1.5f;
    private const float VictoryCheerDuration = 0.75f;
    private const float VictoryCheerHeight = 0.12f;
    private const float VictoryExitDuration = 2f;
    private readonly Dictionary<string, BattleCombatant> combatantsById =
        new Dictionary<string, BattleCombatant>();
    private readonly Dictionary<string, CombatantUi> uiByCombatantId =
        new Dictionary<string, CombatantUi>();
    private readonly Dictionary<string, string> namesById =
        new Dictionary<string, string>();
    private readonly Dictionary<string, float> lastAttackTimesById =
        new Dictionary<string, float>();
    private readonly StringBuilder logBuilder = new StringBuilder();

    private void OnEnable()
    {
        finishButton.onClick.AddListener(FinishBattle);
    }

    private void OnDisable()
    {
        finishButton.onClick.RemoveListener(FinishBattle);
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
        Dictionary<string, int> positionSlotCounts = new Dictionary<string, int>();
        Dictionary<string, int> positionSlotIndices = new Dictionary<string, int>();
        foreach (BattleCombatant combatant in combatants)
        {
            string position = GetPosition(combatant);
            string key = combatant.side + ":" + position;
            positionSlotCounts.TryGetValue(key, out int count);
            positionSlotCounts[key] = count + 1;
        }

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
            string positionKey = combatant.side + ":" + position;
            positionSlotIndices.TryGetValue(positionKey, out int slotIndex);
            positionSlotIndices[positionKey] = slotIndex + 1;
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

            sliders[0].transform.SetParent(canvasRect, true);
            sliders[1].transform.SetParent(canvasRect, true);
            PositionCombatantImage(
                image.rectTransform,
                sliders,
                canvasRect,
                combatant.side,
                position,
                slotIndex,
                positionSlotCounts[positionKey]);
            combatant.health = combatant.initial_health;
            combatantsById.Add(combatant.id, combatant);
            uiByCombatantId.Add(combatant.id, new CombatantUi(image, sliders[0], sliders[1]));
            lastAttackTimesById.Add(combatant.id, 0f);
            namesById.Add(combatant.id, string.IsNullOrEmpty(combatant.name)
                ? combatant.side
                : combatant.name);
            SetHealth(sliders[0], combatant.max_health, combatant.initial_health);
            SetCooldown(sliders[1], 0f);
            image.gameObject.SetActive(true);
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

    private static void PositionCombatantImage(
        RectTransform imageRect,
        Slider[] sliders,
        RectTransform canvasRect,
        string side,
        string position,
        int slotIndex,
        int positionCount)
    {
        Vector3[] sliderOffsets = new Vector3[sliders.Length];
        for (int i = 0; i < sliders.Length; i++)
        {
            sliderOffsets[i] = imageRect.InverseTransformPoint(sliders[i].transform.position);
        }

        imageRect.SetParent(canvasRect, true);
        imageRect.anchorMin = new Vector2(0.5f, 0.5f);
        imageRect.anchorMax = new Vector2(0.5f, 0.5f);
        imageRect.pivot = new Vector2(0.5f, 0.5f);

        bool isCharacter = side == "character";
        float scale = isCharacter
            ? (positionCount > 1 ? 0.65f : 0.8f)
            : (positionCount > 2 ? 0.36f : positionCount > 1 ? 0.44f : 0.52f);
        float xFraction = position == "front" ? 0.14f
            : position == "close" ? 0.24f
            : position == "mid" ? 0.34f
            : 0.44f;
        if (!isCharacter && position == "mid")
        {
            xFraction = 0.29f;
        }

        float rowSpacing = Mathf.Min(
            canvasRect.rect.height * 0.25f, imageRect.rect.height * scale * 1.08f);
        float centeredRow = slotIndex - (positionCount - 1) * 0.5f;
        float horizontalDirection = isCharacter ? -1f : 1f;
        imageRect.localScale = new Vector3(
            (isCharacter ? -1f : 1f) * scale, scale, 1f);
        imageRect.anchoredPosition = new Vector2(
            canvasRect.rect.width * xFraction * horizontalDirection,
            -centeredRow * rowSpacing);

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
                    actionTime, previousTime + Time.deltaTime * PlaybackSpeed);
                UpdateCooldowns(previousTime);
            }

            if (!combatantsById.TryGetValue(action.target_id, out BattleCombatant target)
                || !uiByCombatantId.TryGetValue(action.target_id, out CombatantUi targetUi))
            {
                ShowError("Battle replay references an unknown target: " + action.target_id);
                yield break;
            }

            if (combatantsById.TryGetValue(action.attacker_id, out BattleCombatant attacker)
                && uiByCombatantId.TryGetValue(action.attacker_id, out CombatantUi attackerUi))
            {
                float direction = attacker.side == "character" ? 1f : -1f;
                StartCoroutine(AnimateAttackDash(attackerUi.image.rectTransform, direction));
                if (action.type == "attack" || action.type == "assassinate")
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
                "assassinate" => "assassination",
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
            if (action.evaded)
            {
                AppendLog(FormatTime(action.time_seconds) + " " + targetName + " evaded "
                    + attackerName + "'s " + attackName + " #" + action.attacker_attack_count + ".");
            }
            else if (action.type == "assassinate")
            {
                AppendLog(FormatTime(action.time_seconds) + " " + attackerName
                    + " assassinated " + targetName + " for " + action.damage
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
        Rect imageBounds = imageRect.rect;
        Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : canvas.worldCamera;
        float horizontalCenter = (imageBounds.xMin + imageBounds.xMax) * 0.5f;
        float top = imageBounds.yMax;
        Vector3 startWorld = imageRect.TransformPoint(
            new Vector3(horizontalCenter, top - imageBounds.height * FloatingDamageStartOffset));
        Vector3 endWorld = imageRect.TransformPoint(
            new Vector3(horizontalCenter, top + imageBounds.height * FloatingDamageEndOffset));
        Vector2 startPosition = ToCanvasPosition(startWorld, canvasRect, eventCamera);
        Vector2 endPosition = ToCanvasPosition(endWorld, canvasRect, eventCamera);

        GameObject textObject = new GameObject(
            "Floating Damage Text",
            typeof(RectTransform),
            typeof(TextMeshProUGUI));
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.SetParent(canvasRect, false);
        textRect.SetAsLastSibling();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(200f, 60f);
        textRect.anchoredPosition = startPosition;

        TextMeshProUGUI floatingText = textObject.GetComponent<TextMeshProUGUI>();
        floatingText.font = logText.font;
        floatingText.fontSharedMaterial = logText.fontSharedMaterial;
        floatingText.fontSize = Mathf.Max(24f, logText.fontSize * 1.5f);
        floatingText.alignment = TextAlignmentOptions.Center;
        floatingText.text = message;
        floatingText.color = textColor;
        floatingText.fontStyle = FontStyles.Bold;

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
        Destroy(textObject);
        Destroy(textObject);
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
    }

    [Serializable]
    private sealed class BattleTriggeredSkill
    {
        public string id;
        public string name;
    }
}
