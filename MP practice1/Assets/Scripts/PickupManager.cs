using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

[ExecuteAlways]
public class PickupManager : MonoBehaviour
{
    [SerializeField] private GameObject _healthPickupPrefab;
    [SerializeField] private List<Transform> _spawnPoints = new();
    [SerializeField] private float _respawnDelay = 10f;
    [SerializeField] private float _spawnHeightOffset = 0.55f;
    [SerializeField] private Color _gizmoColor = new(0.2f, 0.95f, 0.45f, 0.95f);
    [SerializeField] private float _gizmoRadius = 0.45f;
    private bool _spawnedForCurrentSession;

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        RegisterNetworkCallbacks();
        TrySpawnInitialPickups();
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        UnregisterNetworkCallbacks();
    }

    private void Start()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        RegisterNetworkCallbacks();
        TrySpawnInitialPickups();
    }

    private void HandleServerStarted()
    {
        _spawnedForCurrentSession = false;
        TrySpawnInitialPickups();
    }

    private void HandleServerStopped(bool _)
    {
        _spawnedForCurrentSession = false;
    }

    private void TrySpawnInitialPickups()
    {
        if (_spawnedForCurrentSession)
        {
            return;
        }

        if (!IsServerRunning())
        {
            return;
        }

        if (GetValidSpawnPointCount() == 0)
        {
            Debug.LogWarning("PickupManager has no spawn points assigned.", this);
            return;
        }

        SpawnAll();
        _spawnedForCurrentSession = true;
    }

    public void OnPickedUp(Transform spawnPoint)
    {
        if (spawnPoint == null)
        {
            return;
        }

        StartCoroutine(RespawnAfterDelay(spawnPoint));
    }

    private IEnumerator RespawnAfterDelay(Transform spawnPoint)
    {
        yield return new WaitForSeconds(_respawnDelay);

        if (this == null || !gameObject.activeInHierarchy || !IsServerRunning())
        {
            yield break;
        }

        SpawnPickup(spawnPoint);
    }

    private void SpawnAll()
    {
        foreach (Transform point in _spawnPoints)
        {
            if (point == null)
            {
                continue;
            }

            SpawnPickup(point);
        }
    }

    private void SpawnPickup(Transform spawnPoint)
    {
        if (_healthPickupPrefab == null || spawnPoint == null)
        {
            return;
        }

        Vector3 spawnPosition = spawnPoint.position + Vector3.up * _spawnHeightOffset;
        GameObject pickupObject = Instantiate(_healthPickupPrefab, spawnPosition, spawnPoint.rotation);
        HealthPickup pickup = pickupObject.GetComponent<HealthPickup>();
        if (pickup != null)
        {
            pickup.Init(this, spawnPoint);
        }

        NetworkObject networkObject = pickupObject.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            NetworkManager networkManager = ResolveNetworkManager();
            if (networkManager != null && networkManager.IsServerStarted)
            {
                networkManager.ServerManager.Spawn(networkObject);
            }
            else
            {
                Destroy(pickupObject);
            }
        }
        else
        {
            Debug.LogError("Health pickup prefab must contain a NetworkObject.", pickupObject);
            Destroy(pickupObject);
        }
    }

    private void OnDrawGizmos()
    {
        DrawSpawnPointGizmos(selectedOnly: false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawSpawnPointGizmos(selectedOnly: true);
    }

    private void DrawSpawnPointGizmos(bool selectedOnly)
    {
        int index = 0;
        foreach (Transform point in _spawnPoints)
        {
            if (point == null)
            {
                continue;
            }

            Color fillColor = _gizmoColor;
            fillColor.a = selectedOnly ? 0.56f : 0.35f;
            Gizmos.color = fillColor;
            Gizmos.DrawSphere(point.position, _gizmoRadius);

            Gizmos.color = _gizmoColor;
            Gizmos.DrawWireSphere(point.position, _gizmoRadius);
            Gizmos.DrawLine(point.position, point.position + Vector3.up * 1f);
            Gizmos.DrawWireCube(point.position + Vector3.up * 0.5f, Vector3.one * 0.32f);

#if UNITY_EDITOR
            UnityEditor.Handles.color = _gizmoColor;
            UnityEditor.Handles.Label(point.position + Vector3.up * 1.1f, $"Health Pickup {index + 1}");
#endif
            index++;
        }
    }

    private int GetValidSpawnPointCount()
    {
        int count = 0;
        foreach (Transform point in _spawnPoints)
        {
            if (point != null)
            {
                count++;
            }
        }

        return count;
    }

    private void RegisterNetworkCallbacks()
    {
        NetworkManager networkManager = ResolveNetworkManager();
        if (networkManager == null)
        {
            return;
        }

        networkManager.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
        networkManager.ServerManager.OnServerConnectionState += HandleServerConnectionState;
    }

    private void UnregisterNetworkCallbacks()
    {
        NetworkManager networkManager = ResolveNetworkManager();
        if (networkManager == null)
        {
            return;
        }

        networkManager.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
    }

    private void HandleServerConnectionState(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            HandleServerStarted();
        }
        else if (args.ConnectionState == LocalConnectionState.Stopped)
        {
            HandleServerStopped(true);
        }
    }

    private static NetworkManager ResolveNetworkManager()
    {
        NetworkManager networkManager = InstanceFinder.NetworkManager;
        return networkManager != null ? networkManager : FindObjectOfType<NetworkManager>();
    }

    private static bool IsServerRunning()
    {
        NetworkManager networkManager = ResolveNetworkManager();
        return networkManager != null && networkManager.IsServerStarted;
    }
}
