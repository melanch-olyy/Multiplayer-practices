using FishNet.Connection;
using FishNet.Object;
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

        if (IsServerStarted)
        {
            TryConsume(player);
            return;
        }

        TryRequestPickupFromClient(player);
    }

    private void OnTriggerStay(Collider other)
    {
        if (IsServerStarted || _isConsumed || !IsClientStarted)
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
    private void RequestPickupServerRpc(int playerObjectId, NetworkConnection conn = null)
    {
        if (_isConsumed || !IsSpawned || ServerManager == null)
        {
            return;
        }

        if (!ServerManager.Objects.Spawned.TryGetValue(playerObjectId, out NetworkObject playerObject))
        {
            return;
        }

        PlayerNetwork player = playerObject.GetComponent<PlayerNetwork>();
        if (player == null || player.Owner != conn)
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
        RequestPickupServerRpc(player.ObjectId);
    }

    private void TryConsume(PlayerNetwork player)
    {
        if (!IsServerStarted || _isConsumed || player == null || !player.IsAlive.Value)
        {
            return;
        }

        if (!player.ApplyHeal(_healAmount))
        {
            return;
        }

        _isConsumed = true;
        _manager?.OnPickedUp(_spawnPoint);
        Despawn();
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
