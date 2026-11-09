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
    private const float FloatingDamageStartOffset = 0.05f;
    private const float FloatingDamageEndOffset = 0.2f;
    private const float AttackDashDuration = 0.25f;
    private const float AttackDashDistance = 0.15f;
    private const float VictoryDelay = 1f;
    private const float EnemyDisappearDuration = 1.5f;
    private const float VictoryCheerDuration = 1f;
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
        }

        return characterCount > 0 && enemyCount > 0;
    }

    private bool InitializeCombatants(List<BattleCombatant> combatants)
    {
        combatantsById.Clear();
        uiByCombatantId.Clear();
        namesById.Clear();
        lastAttackTimesById.Clear();
        RectTransform canvasRect = (RectTransform)logText.canvas.rootCanvas.transform;

        foreach (Image image in characterImages)
        {
            image.gameObject.SetActive(false);
            image.color = Color.white;
        }
        foreach (Image image in enemyImages)
        {
            image.gameObject.SetActive(false);
            image.color = Color.white;
        }

        int characterIndex = 0;
        int enemyIndex = 0;
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
                lastAttackTimesById[action.attacker_id] = actionTime;
                SetCooldown(attackerUi.cooldownSlider, 0f);
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
                StartCoroutine(ShowFloatingText(
                    targetUi.image, "-" + action.damage, new Color(0.6f, 0.08f, 0.08f, 1f)));
            }
            else if (action.evaded)
            {
                StartCoroutine(ShowFloatingText(
                    targetUi.image, "Evade", new Color(0.2f, 0.2f, 0.2f, 1f)));
            }

            string attackerName = namesById[action.attacker_id];
            string targetName = namesById[action.target_id];
            if (action.evaded)
            {
                AppendLog(FormatTime(action.time_seconds) + " " + targetName + " evaded "
                    + attackerName + "'s attack #" + action.attacker_attack_count + ".");
            }
            else
            {
                AppendLog(FormatTime(action.time_seconds) + " " + attackerName + " attack #"
                    + action.attacker_attack_count + " hit "
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

        public CombatantUi(Image image, Slider slider, Slider cooldownSlider)
        {
            this.image = image;
            this.slider = slider;
            this.cooldownSlider = cooldownSlider;
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
    }
}
