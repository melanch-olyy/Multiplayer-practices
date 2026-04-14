using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
public class HealthPickup : NetworkBehaviour
{
    [SerializeField] private int _healAmount = 40;
    [SerializeField] private float _maxPickupDistance = 2f;

    private PickupManager _manager;
    private Transform _spawnPoint;
    private bool _isConsumed;
    private float _nextClientRequestTime;
    private Rigidbody _rigidbody;

    private void Awake()
    {
        EnsureTriggerPhysics();
    }

    public void Init(PickupManager manager, Transform spawnPoint)
    {
        _manager = manager;
        _spawnPoint = spawnPoint;
        _isConsumed = false;
        _nextClientRequestTime = 0f;
        EnsureTriggerPhysics();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_isConsumed)
        {
            return;
        }

        PlayerNetwork player = other.GetComponentInParent<PlayerNetwork>();
        if (player == null)
        {
            return;
        }

        if (IsServer)
        {
            TryConsume(player);
            return;
        }

        TryRequestPickupFromClient(player);
    }

    private void OnTriggerStay(Collider other)
    {
        if (IsServer || _isConsumed || !IsClient)
        {
            return;
        }

        PlayerNetwork player = other.GetComponentInParent<PlayerNetwork>();
        if (player == null)
        {
            return;
        }

        TryRequestPickupFromClient(player);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPickupServerRpc(ulong playerObjectId, ServerRpcParams rpc = default)
    {
        if (_isConsumed || !IsSpawned || NetworkManager == null)
        {
            return;
        }

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerObjectId, out NetworkObject playerObject))
        {
            return;
        }

        PlayerNetwork player = playerObject.GetComponent<PlayerNetwork>();
        if (player == null || player.OwnerClientId != rpc.Receive.SenderClientId)
        {
            return;
        }

        float maxDistanceSqr = _maxPickupDistance * _maxPickupDistance;
        if ((player.transform.position - transform.position).sqrMagnitude > maxDistanceSqr)
        {
            return;
        }

        TryConsume(player);
    }

    private void TryRequestPickupFromClient(PlayerNetwork player)
    {
        if (player == null || !player.IsOwner || Time.unscaledTime < _nextClientRequestTime)
        {
            return;
        }

        _nextClientRequestTime = Time.unscaledTime + 0.15f;
        RequestPickupServerRpc(player.NetworkObjectId);
    }

    private void TryConsume(PlayerNetwork player)
    {
        if (!IsServer || _isConsumed || player == null || !player.IsAlive.Value)
        {
            return;
        }

        if (!player.ApplyHeal(_healAmount))
        {
            return;
        }

        _isConsumed = true;
        _manager?.OnPickedUp(_spawnPoint);
        NetworkObject.Despawn(true);
    }

    private void EnsureTriggerPhysics()
    {
        Collider triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }

        if (_rigidbody == null)
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        if (_rigidbody == null)
        {
            _rigidbody = gameObject.AddComponent<Rigidbody>();
        }

        _rigidbody.useGravity = false;
        _rigidbody.isKinematic = true;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _rigidbody.constraints = RigidbodyConstraints.FreezeAll;
    }
}
