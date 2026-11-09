using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LoginScript : MonoBehaviour
{
    [SerializeField] private Canvas loginCanvas;
    [SerializeField] private Canvas contentCanvas;
    [SerializeField] private Image loginImage;
    [SerializeField] private TMP_InputField playerIdInputField;
    [SerializeField] private Button loginButton;
    [SerializeField] private TMP_Text statusText;

    private GameClient gameClient;
    private bool isLoggingIn;

    private void Awake()
    {
        SetCanvasActive(loginCanvas, true);
        SetCanvasActive(contentCanvas, false);
    }

    private void Start()
    {
        gameClient = GameClient.Instance;
        if (gameClient == null)
        {
            Debug.LogError("LoginScript requires an active GameClient in the scene.", this);
            enabled = false;
            return;
        }

        if (loginButton == null || playerIdInputField == null || loginImage == null
            || loginCanvas == null || contentCanvas == null)
        {
            Debug.LogError("LoginScript is missing one or more required UI or canvas references.", this);
            enabled = false;
            return;
        }

        loginButton.onClick.AddListener(OnLoginButtonClicked);
    }

    private void OnDestroy()
    {
        if (loginButton != null)
        {
            loginButton.onClick.RemoveListener(OnLoginButtonClicked);
        }
    }

    private void OnLoginButtonClicked()
    {
        if (isLoggingIn)
        {
            return;
        }

        string playerId = playerIdInputField.text.Trim();
        if (string.IsNullOrEmpty(playerId))
        {
            SetStatus("Player ID cannot be empty.");
            return;
        }

        isLoggingIn = true;
        loginButton.interactable = false;
        SetStatus(string.Empty);
        gameClient.Login(playerId, OnLoginSuccess, OnLoginFailed);
    }

    private void OnLoginSuccess()
    {
        isLoggingIn = false;
        loginImage.gameObject.SetActive(false);
        SetCanvasActive(loginCanvas, false);
        SetCanvasActive(contentCanvas, true);
    }

    private void OnLoginFailed(string error)
    {
        isLoggingIn = false;
        loginButton.interactable = true;
        SetStatus($"Failed to connect: {error}");
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private static void SetCanvasActive(Canvas canvas, bool active)
    {
        if (canvas != null)
        {
            canvas.gameObject.SetActive(active);
        }
    }
}
