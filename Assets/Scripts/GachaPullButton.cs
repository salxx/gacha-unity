using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pulls a pack from one banner. Visible only while logged in and no reveal sequence is running.
/// </summary>
public class GachaPullButton : MonoBehaviour
{
    [SerializeField] private Button pullButton;

    [Tooltip("Target gacha banner: 'character' or 'support_card'")]
    [SerializeField] private string targetGacha = "character";

    private GameClient client;
    private GachaItemSpawner spawner;

    private void Start()
    {
        client = GameClient.Instance;
        spawner = GachaItemSpawner.Instance;

        pullButton.onClick.AddListener(OnPullButtonClicked);
        client.OnLoginSuccess += RefreshVisibility;
        client.OnPullFinished += RefreshVisibility;
        client.OnPullStarted += Hide;
        spawner.OnSequenceEnded += RefreshVisibility;

        RefreshVisibility();
    }

    private void OnDestroy()
    {
        pullButton.onClick.RemoveListener(OnPullButtonClicked);

        if (client != null)
        {
            client.OnLoginSuccess -= RefreshVisibility;
            client.OnPullFinished -= RefreshVisibility;
            client.OnPullStarted -= Hide;
        }

        if (spawner != null)
        {
            spawner.OnSequenceEnded -= RefreshVisibility;
        }
    }

    private void OnPullButtonClicked() => client.Pull(targetGacha);

    private void RefreshVisibility() => pullButton.gameObject.SetActive(client.IsLoggedIn && !spawner.IsSequenceActive);

    private void Hide() => pullButton.gameObject.SetActive(false);
}
