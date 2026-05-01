using System.Collections;
using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

public class ServerAutoStart : MonoBehaviour
{
    private const string DefaultBatchBindAddress = "0.0.0.0";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForBatchMode()
    {
        if (!Application.isBatchMode || FindObjectOfType<ServerAutoStart>() != null)
        {
            return;
        }

        GameObject autoStartObject = new("Server Auto Start");
        DontDestroyOnLoad(autoStartObject);
        autoStartObject.AddComponent<ServerAutoStart>();
    }

    private IEnumerator Start()
    {
        if (!Application.isBatchMode)
        {
            yield break;
        }

        NetworkManager networkManager = null;
        float timeoutAt = Time.realtimeSinceStartup + 5f;
        while (networkManager == null && Time.realtimeSinceStartup < timeoutAt)
        {
            networkManager = ResolveNetworkManager();
            if (networkManager == null)
            {
                yield return null;
            }
        }

        if (networkManager == null)
        {
            Debug.LogError("[Server] NetworkManager was not found. Dedicated server cannot start.");
            yield break;
        }

        ConfigureTransport(networkManager);

        if (networkManager.IsServerStarted)
        {
            Debug.Log("[Server] Server is already running.");
            yield break;
        }

        Debug.Log("[Server] Headless mode detected. Starting FishNet server...");
        bool started = networkManager.ServerManager.StartConnection();
        Debug.Log(started ? "[Server] FishNet server started." : "[Server] FishNet server failed to start.");
    }

    private static void ConfigureTransport(NetworkManager networkManager)
    {
        Transport transport = networkManager.TransportManager != null
            ? networkManager.TransportManager.Transport
            : null;

        if (transport == null)
        {
            Debug.LogWarning("[Server] Transport was not found. Using NetworkManager defaults.");
            return;
        }

        string bindAddress = GetCommandLineValue("-bind")
            ?? GetCommandLineValue("--bind")
            ?? GetCommandLineValue("-serverBindAddress")
            ?? DefaultBatchBindAddress;

        transport.SetServerBindAddress(bindAddress, IPAddressType.IPv4);

        string portValue = GetCommandLineValue("-port") ?? GetCommandLineValue("--port");
        if (ushort.TryParse(portValue, out ushort port))
        {
            transport.SetPort(port);
        }

        Debug.Log($"[Server] Transport bind {transport.GetServerBindAddress(IPAddressType.IPv4)}:{transport.GetPort()}.");
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
}
