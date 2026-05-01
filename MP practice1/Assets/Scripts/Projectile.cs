using FishNet.Object;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class Projectile : NetworkBehaviour
{
    [SerializeField] private float _speed = 18f;
    [SerializeField] private int _damage = 20;
    [SerializeField] private float _lifetime = 3f;

    private Rigidbody _rigidbody;
    private Vector3 _moveDirection = Vector3.forward;
    private PlayerNetwork _ownerPlayer;
    private float _despawnAt;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
    }

    public override void OnStartNetwork()
    {
        if (_rigidbody == null)
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        if (_rigidbody != null)
        {
            _rigidbody.useGravity = false;
            _rigidbody.isKinematic = true;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        if (IsServerStarted)
        {
            _despawnAt = Time.time + _lifetime;
        }
    }

    private void FixedUpdate()
    {
        if (!IsServerStarted || !IsSpawned)
        {
            return;
        }

        if (!GameSessionManager.IsGameplayActive)
        {
            Despawn();
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
            Despawn();
        }
    }

    public void Initialize(Vector3 direction)
    {
        Initialize(direction, null);
    }

    public void Initialize(Vector3 direction, PlayerNetwork ownerPlayer)
    {
        _ownerPlayer = ownerPlayer;
        _moveDirection = direction.sqrMagnitude < 0.001f ? Vector3.forward : direction.normalized;
        transform.rotation = Quaternion.LookRotation(_moveDirection, Vector3.up);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServerStarted || !IsSpawned || !GameSessionManager.IsGameplayActive)
        {
            return;
        }

        PlayerNetwork target = other.GetComponentInParent<PlayerNetwork>();
        if (target != null)
        {
            if (target.OwnerId == OwnerId)
            {
                return;
            }

            target.ApplyDamage(_damage, _ownerPlayer);
            Despawn();
            return;
        }

        if (!other.isTrigger)
        {
            Despawn();
        }
    }
}
