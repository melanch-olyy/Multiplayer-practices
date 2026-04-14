using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class Projectile : NetworkBehaviour
{
    [SerializeField] private float _speed = 18f;
    [SerializeField] private int _damage = 20;
    [SerializeField] private float _lifetime = 3f;
    [SerializeField] private float _positionSyncThreshold = 0.02f;
    [SerializeField] private float _rotationSyncThreshold = 1f;

    private Rigidbody _rigidbody;
    private NetworkTransform _networkTransform;
    private Vector3 _moveDirection = Vector3.forward;
    private float _despawnAt;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _networkTransform = GetComponent<NetworkTransform>();
        ConfigureNetworkTransform();
    }

    public override void OnNetworkSpawn()
    {
        if (_rigidbody == null)
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        if (_networkTransform == null)
        {
            _networkTransform = GetComponent<NetworkTransform>();
        }

        ConfigureNetworkTransform();

        if (_rigidbody != null)
        {
            _rigidbody.useGravity = false;
            _rigidbody.isKinematic = true;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        if (IsServer)
        {
            _despawnAt = Time.time + _lifetime;
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        float step = _speed * Time.fixedDeltaTime;
        Vector3 delta = _moveDirection * step;
        if (_rigidbody != null)
        {
            _rigidbody.MovePosition(_rigidbody.position + delta);
        }
        else
        {
            transform.position += delta;
        }

        if (Time.time >= _despawnAt)
        {
            NetworkObject.Despawn(true);
        }
    }

    public void Initialize(Vector3 direction)
    {
        _moveDirection = direction.sqrMagnitude < 0.001f ? Vector3.forward : direction.normalized;
        transform.rotation = Quaternion.LookRotation(_moveDirection, Vector3.up);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        PlayerNetwork target = other.GetComponentInParent<PlayerNetwork>();
        if (target != null)
        {
            if (target.OwnerClientId == OwnerClientId)
            {
                return;
            }

            target.ApplyDamage(_damage);
            NetworkObject.Despawn(true);
            return;
        }

        if (!other.isTrigger)
        {
            NetworkObject.Despawn(true);
        }
    }

    private void ConfigureNetworkTransform()
    {
        if (_networkTransform == null)
        {
            return;
        }

        _networkTransform.UseUnreliableDeltas = true;
        _networkTransform.Interpolate = true;
        _networkTransform.SyncScaleX = false;
        _networkTransform.SyncScaleY = false;
        _networkTransform.SyncScaleZ = false;
        _networkTransform.SyncRotAngleX = false;
        _networkTransform.SyncRotAngleY = true;
        _networkTransform.SyncRotAngleZ = false;
        _networkTransform.PositionThreshold = Mathf.Max(0.0001f, _positionSyncThreshold);
        _networkTransform.RotAngleThreshold = Mathf.Max(0.01f, _rotationSyncThreshold);
    }
}
