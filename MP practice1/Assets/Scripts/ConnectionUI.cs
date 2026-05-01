using TMPro;
using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;
using UnityEngine.UI;

public class ConnectionUI : MonoBehaviour
{
    private const string DefaultNickname = "Player";
    private const string DefaultAddress = "127.0.0.1";
    private const string AddressPrefsKey = "FishNet.ServerAddress";

    [SerializeField] private TMP_InputField _nicknameInput;
    [SerializeField] private TMP_InputField _addressInput;
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

        EnsureAddressInput();
        InitializeAddressInput();
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
        NetworkManager networkManager = ResolveNetworkManager();
        if (networkManager == null)
        {
            Debug.LogError("FishNet NetworkManager was not found in the scene.");
            SetStatus("NetworkManager is missing.");
            return;
        }

        if (networkManager.IsClientStarted || networkManager.IsServerStarted)
        {
            SetStatus("Session is already running.");
            return;
        }

        SaveNickname();
        ConfigureTransportForSession(networkManager, asHost);

        if (asHost)
        {
            if (!networkManager.ServerManager.StartConnection())
            {
                SetStatus("Host failed to start.");
                return;
            }

            if (!networkManager.ClientManager.StartConnection())
            {
                networkManager.ServerManager.StopConnection(true);
                SetStatus("Host client failed to start.");
                return;
            }
        }
        else if (!networkManager.ClientManager.StartConnection())
        {
            SetStatus("Client failed to start.");
            return;
        }

        SetStatus(asHost ? $"Host started on {GetTransportAddress(networkManager)}." : $"Connecting to {GetTransportAddress(networkManager)}...");
    }

    private void RegisterCallbacks()
    {
        NetworkManager networkManager = ResolveNetworkManager();
        if (networkManager == null)
        {
            return;
        }

        networkManager.ClientManager.OnClientConnectionState -= HandleClientConnectionState;
        networkManager.ClientManager.OnClientConnectionState += HandleClientConnectionState;

        networkManager.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
        networkManager.ServerManager.OnServerConnectionState += HandleServerConnectionState;
    }

    private void UnregisterCallbacks()
    {
        NetworkManager networkManager = ResolveNetworkManager();
        if (networkManager == null)
        {
            return;
        }

        networkManager.ClientManager.OnClientConnectionState -= HandleClientConnectionState;
        networkManager.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
    }

    private void HandleClientConnectionState(ClientConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            NetworkManager networkManager = ResolveNetworkManager();
            SetMenuVisible(false);
            SetStatus(networkManager != null && networkManager.IsServerStarted ? "Host is running." : "Client connected.");
        }
        else if (args.ConnectionState == LocalConnectionState.Stopped)
        {
            SetMenuVisible(true);
            SetStatus("Disconnected.");
        }
    }

    private void HandleServerConnectionState(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Stopped)
        {
            SetMenuVisible(true);
        }
    }

    private void SaveNickname()
    {
        string rawValue = _nicknameInput != null ? _nicknameInput.text : string.Empty;
        PlayerNickname = NormalizeNickname(rawValue);
    }

    private void ConfigureTransportForSession(NetworkManager networkManager, bool asHost)
    {
        if (networkManager.TransportManager == null || networkManager.TransportManager.Transport == null)
        {
            return;
        }

        Transport transport = networkManager.TransportManager.Transport;
        ApplyCommandLinePort(transport);

        if (asHost)
        {
            transport.SetClientAddress(DefaultAddress);
            return;
        }

        string rawAddress = _addressInput != null ? _addressInput.text : string.Empty;
        ApplyPortFromAddress(transport, rawAddress);

        string address = NormalizeAddress(rawAddress);
        PlayerPrefs.SetString(AddressPrefsKey, address);
        PlayerPrefs.Save();
        transport.SetClientAddress(address);
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

    private void EnsureAddressInput()
    {
        if (Application.isBatchMode || _addressInput != null || _menuRoot == null)
        {
            return;
        }

        RectTransform parent = _menuRoot.transform as RectTransform;
        if (parent == null)
        {
            return;
        }

        GameObject inputObject = new("Server Address InputField (TMP)", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        inputObject.layer = _menuRoot.layer;
        inputObject.transform.SetParent(parent, false);

        RectTransform inputRect = inputObject.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0.5f, 0.5f);
        inputRect.anchorMax = new Vector2(0.5f, 0.5f);
        inputRect.pivot = new Vector2(0.5f, 0.5f);
        inputRect.anchoredPosition = new Vector2(0f, -145f);
        inputRect.sizeDelta = new Vector2(260f, 34f);

        Image background = inputObject.GetComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.92f);

        GameObject textArea = new("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textArea.layer = inputObject.layer;
        textArea.transform.SetParent(inputRect, false);
        RectTransform textAreaRect = textArea.GetComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(8f, 4f);
        textAreaRect.offsetMax = new Vector2(-8f, -4f);

        TextMeshProUGUI placeholder = CreateInputText(textAreaRect, "Placeholder", "Server IP", new Color(0f, 0f, 0f, 0.45f), FontStyles.Italic);
        TextMeshProUGUI text = CreateInputText(textAreaRect, "Text", string.Empty, Color.black, FontStyles.Normal);

        TMP_InputField inputField = inputObject.GetComponent<TMP_InputField>();
        inputField.textViewport = textAreaRect;
        inputField.textComponent = text;
        inputField.placeholder = placeholder;
        inputField.targetGraphic = background;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.characterLimit = 64;
        _addressInput = inputField;
    }

    private static TextMeshProUGUI CreateInputText(RectTransform parent, string objectName, string value, Color color, FontStyles fontStyle)
    {
        GameObject textObject = new(objectName, typeof(RectTransform));
        textObject.layer = parent.gameObject.layer;
        textObject.transform.SetParent(parent, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = 20f;
        text.color = color;
        text.fontStyle = fontStyle;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        return text;
    }

    private void InitializeAddressInput()
    {
        if (_addressInput == null)
        {
            return;
        }

        string address = GetCommandLineValue("-address")
            ?? GetCommandLineValue("--address")
            ?? GetCommandLineValue("-connect")
            ?? PlayerPrefs.GetString(AddressPrefsKey, string.Empty);

        if (string.IsNullOrWhiteSpace(address))
        {
            NetworkManager networkManager = ResolveNetworkManager();
            address = networkManager != null ? GetConfiguredTransportAddress(networkManager) : DefaultAddress;
        }

        _addressInput.text = NormalizeAddress(address);
    }

    private static string NormalizeAddress(string rawValue)
    {
        string value = string.IsNullOrWhiteSpace(rawValue) ? DefaultAddress : rawValue.Trim();
        int colonIndex = value.LastIndexOf(':');
        if (colonIndex > 0 && colonIndex == value.IndexOf(':') && colonIndex < value.Length - 1)
        {
            return value.Substring(0, colonIndex);
        }

        return value;
    }

    private static void ApplyCommandLinePort(Transport transport)
    {
        string portValue = GetCommandLineValue("-port") ?? GetCommandLineValue("--port");
        if (ushort.TryParse(portValue, out ushort port))
        {
            transport.SetPort(port);
            return;
        }

        string addressValue = GetCommandLineValue("-address") ?? GetCommandLineValue("--address") ?? GetCommandLineValue("-connect");
        if (string.IsNullOrWhiteSpace(addressValue))
        {
            return;
        }

        int colonIndex = addressValue.LastIndexOf(':');
        if (colonIndex > 0
            && colonIndex == addressValue.IndexOf(':')
            && colonIndex < addressValue.Length - 1
            && ushort.TryParse(addressValue.Substring(colonIndex + 1), out port))
        {
            transport.SetPort(port);
        }
    }

    private static void ApplyPortFromAddress(Transport transport, string addressValue)
    {
        if (transport == null || string.IsNullOrWhiteSpace(addressValue))
        {
            return;
        }

        int colonIndex = addressValue.LastIndexOf(':');
        if (colonIndex > 0
            && colonIndex == addressValue.IndexOf(':')
            && colonIndex < addressValue.Length - 1
            && ushort.TryParse(addressValue.Substring(colonIndex + 1), out ushort port))
        {
            transport.SetPort(port);
        }
    }

    private static string GetCommandLineValue(string key)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == key)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static NetworkManager ResolveNetworkManager()
    {
        NetworkManager networkManager = InstanceFinder.NetworkManager;
        return networkManager != null ? networkManager : FindObjectOfType<NetworkManager>();
    }

    private static string GetConfiguredTransportAddress(NetworkManager networkManager)
    {
        if (networkManager.TransportManager != null && networkManager.TransportManager.Transport is Transport transport)
        {
            return transport is Tugboat tugboat ? tugboat.GetClientAddress() : transport.GetClientAddress();
        }

        return DefaultAddress;
    }

    private static string GetTransportAddress(NetworkManager networkManager)
    {
        if (networkManager.TransportManager != null && networkManager.TransportManager.Transport is Transport transport)
        {
            string address = transport is Tugboat tugboat ? tugboat.GetClientAddress() : transport.GetClientAddress();
            return $"{address}:{transport.GetPort()}";
        }

        return "configured transport";
    }
}
