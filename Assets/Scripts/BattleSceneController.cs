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
    private readonly Dictionary<string, BattleCombatant> combatantsById =
        new Dictionary<string, BattleCombatant>();
    private readonly Dictionary<string, CombatantUi> uiByCombatantId =
        new Dictionary<string, CombatantUi>();
    private readonly Dictionary<string, string> namesById =
        new Dictionary<string, string>();
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
            if (combatant == null || string.IsNullOrEmpty(combatant.id))
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

            Slider slider = image.GetComponentInChildren<Slider>(true);
            if (slider == null)
            {
                ShowError("The " + combatant.side + " image at index " + sideIndex
                    + " does not have a child Slider.");
                return false;
            }

            combatant.health = combatant.initial_health;
            combatantsById.Add(combatant.id, combatant);
            uiByCombatantId.Add(combatant.id, new CombatantUi(image, slider));
            namesById.Add(combatant.id, string.IsNullOrEmpty(combatant.name)
                ? combatant.side
                : combatant.name);
            SetHealth(slider, combatant.max_health, combatant.initial_health);
            image.gameObject.SetActive(true);
        }

        return true;
    }

    private IEnumerator PlayActions(List<BattleAction> actions)
    {
        float previousTime = 0f;
        foreach (BattleAction action in actions)
        {
            float waitTime = Mathf.Max(0f, action.time_seconds - previousTime) / PlaybackSpeed;
            if (waitTime > 0f)
            {
                yield return new WaitForSeconds(waitTime);
            }
            previousTime = action.time_seconds;

            if (!combatantsById.TryGetValue(action.target_id, out BattleCombatant target)
                || !uiByCombatantId.TryGetValue(action.target_id, out CombatantUi targetUi))
            {
                ShowError("Battle replay references an unknown target: " + action.target_id);
                yield break;
            }

            targetUi.slider.value = Mathf.Clamp(action.target_health, 0, target.max_health);
            if (action.target_health <= 0)
            {
                targetUi.image.color = Color.gray;
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

    private static void SetHealth(Slider slider, int maxHealth, int health)
    {
        slider.minValue = 0;
        slider.maxValue = Mathf.Max(1, maxHealth);
        slider.wholeNumbers = true;
        slider.value = Mathf.Clamp(health, 0, slider.maxValue);
    }

    private sealed class CombatantUi
    {
        public readonly Image image;
        public readonly Slider slider;

        public CombatantUi(Image image, Slider slider)
        {
            this.image = image;
            this.slider = slider;
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
        public int max_health;
        public int initial_health;
        public int health;
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
