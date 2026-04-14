using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerNetwork))]
public class PlayerCombat : NetworkBehaviour
{
    [Header("Melee")]
    [SerializeField] private int _meleeDamage = 10;
    [SerializeField] private float _meleeRange = 2.25f;

    [Header("Projectile")]
    [SerializeField] private GameObject _projectilePrefab;
    [SerializeField] private Transform _firePoint;
    [SerializeField] private float _cooldown = 0.4f;
    [SerializeField] private int _maxAmmo = 10;
    [SerializeField] private Vector3 _projectileSpawnOffset = new(0f, 0.35f, 0.9f);
    [SerializeField] private float _maxClientShotOriginOffset = 2.5f;
    [SerializeField] private KeyCode _projectileHotkey = KeyCode.Space;

    public NetworkVariable<int> CurrentAmmo = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public int MaxAmmo => _maxAmmo;

    private PlayerNetwork _playerNetwork;
    private double _nextServerShotTime;

    private void Awake()
    {
        _playerNetwork = GetComponent<PlayerNetwork>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            RestoreAmmo();
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner || _playerNetwork == null)
        {
            return;
        }

        if (!_playerNetwork.IsAlive.Value)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            RequestMeleeAttackServerRpc(Mathf.Max(0, _meleeDamage));
        }

        if (Input.GetKeyDown(_projectileHotkey))
        {
            ShootServerRpc(GetClientShotOrigin(), GetCursorDirectionXZ());
        }
    }

    [ServerRpc]
    private void ShootServerRpc(Vector3 shotOrigin, Vector3 shotDirection, ServerRpcParams rpc = default)
    {
        if (rpc.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        if (_playerNetwork == null || !_playerNetwork.IsAlive.Value || _playerNetwork.HP.Value <= 0)
        {
            return;
        }

        if (_projectilePrefab == null)
        {
            Debug.LogWarning("Projectile prefab is not assigned.", this);
            return;
        }

        if (CurrentAmmo.Value <= 0)
        {
            return;
        }

        double serverTime = NetworkManager.ServerTime.Time;
        if (serverTime < _nextServerShotTime)
        {
            return;
        }

        Vector3 direction = shotDirection;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = transform.forward;
            direction.y = 0f;
        }

        direction = direction.normalized;
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector3.forward;
        }

        _nextServerShotTime = serverTime + _cooldown;
        CurrentAmmo.Value--;

        Vector3 validatedOrigin = ValidateShotOrigin(shotOrigin);
        float forwardOffset = Mathf.Abs(_projectileSpawnOffset.z) < 0.2f ? 0.9f : Mathf.Abs(_projectileSpawnOffset.z);
        float verticalOffset = Mathf.Abs(_projectileSpawnOffset.y) < 0.05f ? 0.45f : _projectileSpawnOffset.y;
        Vector3 spawnPosition = validatedOrigin + Vector3.up * verticalOffset + direction * forwardOffset;
        Quaternion spawnRotation = Quaternion.LookRotation(direction, Vector3.up);

        GameObject projectileObject = Instantiate(_projectilePrefab, spawnPosition, spawnRotation);

        Collider projectileCollider = projectileObject.GetComponent<Collider>();
        if (projectileCollider != null)
        {
            Collider[] shooterColliders = GetComponentsInChildren<Collider>();
            foreach (Collider shooterCollider in shooterColliders)
            {
                if (shooterCollider == null)
                {
                    continue;
                }

                Physics.IgnoreCollision(projectileCollider, shooterCollider, true);
            }
        }

        Projectile projectile = projectileObject.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.Initialize(direction);
        }

        NetworkObject projectileNetworkObject = projectileObject.GetComponent<NetworkObject>();
        if (projectileNetworkObject == null)
        {
            Debug.LogError("Projectile prefab must contain a NetworkObject.", projectileObject);
            Destroy(projectileObject);
            return;
        }

        projectileNetworkObject.SpawnWithOwnership(rpc.Receive.SenderClientId);
    }

    [ServerRpc]
    private void RequestMeleeAttackServerRpc(int damage, ServerRpcParams rpc = default)
    {
        if (rpc.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        if (_playerNetwork == null || !_playerNetwork.IsAlive.Value || _playerNetwork.HP.Value <= 0)
        {
            return;
        }

        int safeDamage = Mathf.Max(0, damage);
        if (safeDamage == 0)
        {
            return;
        }

        if (!TryGetNearestMeleeTarget(out PlayerNetwork target))
        {
            return;
        }

        target.ApplyDamage(safeDamage);
    }

    public void RestoreAmmo()
    {
        if (!IsServer)
        {
            return;
        }

        CurrentAmmo.Value = _maxAmmo;
        _nextServerShotTime = NetworkManager != null
            ? NetworkManager.ServerTime.Time
            : 0d;
    }

    private Vector3 GetClientShotOrigin()
    {
        if (_firePoint != null)
        {
            return _firePoint.position;
        }

        return transform.position;
    }

    private Vector3 ValidateShotOrigin(Vector3 requestedOrigin)
    {
        Vector3 fallbackOrigin = _firePoint != null ? _firePoint.position : transform.position;
        if ((requestedOrigin - fallbackOrigin).sqrMagnitude > _maxClientShotOriginOffset * _maxClientShotOriginOffset)
        {
            return fallbackOrigin;
        }

        return requestedOrigin;
    }

    private Vector3 GetCursorDirectionXZ()
    {
        Vector3 fireOrigin = _firePoint != null ? _firePoint.position : transform.position;

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Ray cursorRay = mainCamera.ScreenPointToRay(Input.mousePosition);
            Plane horizontalPlane = new(Vector3.up, new Vector3(0f, fireOrigin.y, 0f));

            Vector3 targetPoint;
            if (horizontalPlane.Raycast(cursorRay, out float enterDistance))
            {
                targetPoint = cursorRay.GetPoint(enterDistance);
            }
            else
            {
                targetPoint = cursorRay.origin + cursorRay.direction * 25f;
            }

            Vector3 direction = targetPoint - fireOrigin;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
            {
                return direction.normalized;
            }
        }

        Vector3 fallbackDirection = transform.forward;
        fallbackDirection.y = 0f;
        if (fallbackDirection.sqrMagnitude < 0.001f)
        {
            fallbackDirection = Vector3.forward;
        }

        return fallbackDirection.normalized;
    }

    private bool TryGetNearestMeleeTarget(out PlayerNetwork target)
    {
        target = null;
        if (NetworkManager == null || NetworkObject == null)
        {
            return false;
        }

        float maxDistanceSqr = _meleeRange * _meleeRange;
        float bestDistanceSqr = float.MaxValue;
        Vector3 attackerPosition = transform.position;

        foreach (NetworkObject spawnedObject in NetworkManager.SpawnManager.SpawnedObjectsList)
        {
            if (spawnedObject == null || spawnedObject == NetworkObject)
            {
                continue;
            }

            PlayerNetwork candidate = spawnedObject.GetComponent<PlayerNetwork>();
            if (!IsValidMeleeTarget(candidate))
            {
                continue;
            }

            float distanceSqr = (candidate.transform.position - attackerPosition).sqrMagnitude;
            if (distanceSqr > maxDistanceSqr || distanceSqr >= bestDistanceSqr)
            {
                continue;
            }

            bestDistanceSqr = distanceSqr;
            target = candidate;
        }

        return target != null;
    }

    private bool IsValidMeleeTarget(PlayerNetwork candidate)
    {
        return candidate != null
            && candidate != _playerNetwork
            && candidate.IsSpawned
            && candidate.IsAlive.Value
            && candidate.HP.Value > 0;
    }
}
