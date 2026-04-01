using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class ConnectionUI : MonoBehaviour
{
    private const string DefaultNickname = "Player";

    [SerializeField] private TMP_InputField _nicknameInput;
    [SerializeField] private GameObject _menuRoot;
    [SerializeField] private TMP_Text _statusText;

    public static string PlayerNickname { get; private set; } = DefaultNickname;

    private void Start()
    {
        RegisterCallbacks();
        if (_nicknameInput != null && string.IsNullOrWhiteSpace(_nicknameInput.text))
        {
            _nicknameInput.text = PlayerNickname;
        }

        SetStatus("Choose Host or Client.");
        SetMenuVisible(true);
    }

    private void OnDestroy()
    {
        UnregisterCallbacks();
    }

    public void StartAsHost()
    {
        StartSession(asHost: true);
    }

    public void StartAsClient()
    {
        StartSession(asHost: false);
    }

    private void StartSession(bool asHost)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            Debug.LogError("NetworkManager.Singleton was not found in the scene.");
            SetStatus("NetworkManager is missing.");
            return;
        }

        if (networkManager.IsListening)
        {
            SetStatus("Session is already running.");
            return;
        }

        SaveNickname();

        bool started = asHost ? networkManager.StartHost() : networkManager.StartClient();
        if (!started)
        {
            SetStatus(asHost ? "Host failed to start." : "Client failed to start.");
            return;
        }

        SetStatus(asHost ? $"Host started on {GetTransportAddress(networkManager)}." : $"Connecting to {GetTransportAddress(networkManager)}...");
    }

    private void RegisterCallbacks()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            return;
        }

        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientConnectedCallback += HandleClientConnected;

        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    private void UnregisterCallbacks()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            return;
        }

        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    private void HandleClientConnected(ulong clientId)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || clientId != networkManager.LocalClientId)
        {
            return;
        }

        SetMenuVisible(false);
        SetStatus(networkManager.IsHost ? "Host is running." : "Client connected.");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || clientId != networkManager.LocalClientId)
        {
            return;
        }

        SetMenuVisible(true);

        string disconnectReason = string.IsNullOrWhiteSpace(networkManager.DisconnectReason)
            ? "Disconnected."
            : $"Disconnected: {networkManager.DisconnectReason}";

        SetStatus(disconnectReason);
    }

    private void SaveNickname()
    {
        string rawValue = _nicknameInput != null ? _nicknameInput.text : string.Empty;
        PlayerNickname = NormalizeNickname(rawValue);
    }

    private static string NormalizeNickname(string rawValue)
    {
        return string.IsNullOrWhiteSpace(rawValue) ? DefaultNickname : rawValue.Trim();
    }

    private void SetMenuVisible(bool visible)
    {
        if (_menuRoot != null)
        {
            _menuRoot.SetActive(visible);
        }
    }

    private void SetStatus(string message)
    {
        if (_statusText != null)
        {
            _statusText.text = message;
        }
    }

    private static string GetTransportAddress(NetworkManager networkManager)
    {
        if (networkManager.NetworkConfig?.NetworkTransport is UnityTransport unityTransport)
        {
            return $"{unityTransport.ConnectionData.Address}:{unityTransport.ConnectionData.Port}";
        }

        return "configured transport";
    }
}
