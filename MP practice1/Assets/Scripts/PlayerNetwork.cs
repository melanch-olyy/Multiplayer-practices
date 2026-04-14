using System.Collections;
using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class PlayerNetwork : NetworkBehaviour
{
    [SerializeField] private int _startingHp = 100;
    [SerializeField] private float _spawnSpacing = 3f;
    [SerializeField] private float _respawnDelay = 3f;

    public NetworkVariable<FixedString32Bytes> Nickname = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<int> HP = new(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsAlive = new(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<float> RespawnEndTime = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public int MaxHp => _startingHp;

    private OwnerNetworkTransform _networkTransform;
    private CharacterController _characterController;
    private PlayerCombat _playerCombat;
    private Coroutine _respawnRoutine;

    private void Awake()
    {
        _networkTransform = GetComponent<OwnerNetworkTransform>();
        _characterController = GetComponent<CharacterController>();
        _playerCombat = GetComponent<PlayerCombat>();
    }

    public override void OnNetworkSpawn()
    {
        HP.OnValueChanged += OnHpChanged;
        IsAlive.OnValueChanged += OnIsAliveChanged;

        if (IsServer)
        {
            Nickname.Value = BuildDefaultNickname(OwnerClientId);
            HP.Value = Mathf.Max(0, _startingHp);
            IsAlive.Value = true;
            RespawnEndTime.Value = 0f;
            MoveToSpawnPoint(initialSpawn: true);
        }

        if (IsOwner)
        {
            SubmitNicknameServerRpc(ConnectionUI.PlayerNickname);
        }

        ApplyControllerOwnershipState();
    }

    public override void OnNetworkDespawn()
    {
        HP.OnValueChanged -= OnHpChanged;
        IsAlive.OnValueChanged -= OnIsAliveChanged;
    }

    public override void OnGainedOwnership()
    {
        base.OnGainedOwnership();
        ApplyControllerOwnershipState();
    }

    public override void OnLostOwnership()
    {
        base.OnLostOwnership();
        ApplyControllerOwnershipState();
    }

    [ServerRpc]
    private void SubmitNicknameServerRpc(string nickname)
    {
        Nickname.Value = SanitizeNickname(nickname, OwnerClientId);
    }

    public bool ApplyDamage(int damage)
    {
        if (!IsServer || !IsAlive.Value)
        {
            return false;
        }

        int safeDamage = Mathf.Max(0, damage);
        if (safeDamage == 0 || HP.Value <= 0)
        {
            return false;
        }

        HP.Value = Mathf.Max(0, HP.Value - safeDamage);
        return true;
    }

    public bool ApplyHeal(int amount)
    {
        if (!IsServer || !IsAlive.Value)
        {
            return false;
        }

        int safeAmount = Mathf.Max(0, amount);
        if (safeAmount == 0 || HP.Value >= _startingHp)
        {
            return false;
        }

        HP.Value = Mathf.Min(_startingHp, HP.Value + safeAmount);
        return true;
    }

    private void OnHpChanged(int previousValue, int nextValue)
    {
        if (!IsServer)
        {
            return;
        }

        if (nextValue > 0 || !IsAlive.Value)
        {
            return;
        }

        IsAlive.Value = false;
        RespawnEndTime.Value = (float)NetworkManager.ServerTime.Time + _respawnDelay;

        if (_respawnRoutine != null)
        {
            StopCoroutine(_respawnRoutine);
        }

        _respawnRoutine = StartCoroutine(RespawnRoutine());
    }

    private void OnIsAliveChanged(bool previousValue, bool nextValue)
    {
        if (IsServer && nextValue)
        {
            RespawnEndTime.Value = 0f;
        }

        ApplyControllerOwnershipState();
    }

    private IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(_respawnDelay);

        MoveToSpawnPoint(initialSpawn: false);
        HP.Value = _startingHp;
        IsAlive.Value = true;
        RespawnEndTime.Value = 0f;
        _playerCombat?.RestoreAmmo();
        _respawnRoutine = null;
    }

    private void MoveToSpawnPoint(bool initialSpawn)
    {
        Vector3 spawnPosition;
        bool hasSpawnPoint = false;

        if (PlayerSpawnManager.Instance != null)
        {
            hasSpawnPoint = initialSpawn
                ? PlayerSpawnManager.Instance.TryGetInitialSpawnPoint(OwnerClientId, out spawnPosition)
                : PlayerSpawnManager.Instance.TryGetRespawnPoint(OwnerClientId, out spawnPosition);
        }
        else
        {
            spawnPosition = default;
        }

        if (!hasSpawnPoint)
        {
            spawnPosition = BuildFallbackSpawnPosition(OwnerClientId);
        }

        spawnPosition = BuildSafeSpawnPosition(spawnPosition);
        Quaternion spawnRotation = Quaternion.identity;
        ApplyServerTeleport(spawnPosition, spawnRotation);
    }

    private Vector3 BuildSafeSpawnPosition(Vector3 markerPosition)
    {
        if (TryGetGroundHeight(markerPosition, out float groundY))
        {
            markerPosition.y = Mathf.Max(markerPosition.y, groundY);
        }

        if (_characterController == null)
        {
            markerPosition.y += 0.1f;
            return markerPosition;
        }

        float halfHeight = Mathf.Max(_characterController.height * 0.5f, _characterController.radius);
        float bottomOffset = _characterController.center.y - halfHeight;
        float minClearance = Mathf.Max(0.05f, _characterController.skinWidth + 0.02f);

        float requiredTransformY = markerPosition.y - bottomOffset + minClearance;
        markerPosition.y = Mathf.Max(markerPosition.y, requiredTransformY);
        return markerPosition;
    }

    private bool TryGetGroundHeight(Vector3 markerPosition, out float groundY)
    {
        const float rayStartOffset = 6f;
        const float rayDistance = 32f;

        RaycastHit[] hits = Physics.RaycastAll(
            markerPosition + Vector3.up * rayStartOffset,
            Vector3.down,
            rayDistance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
        {
            groundY = markerPosition.y;
            return false;
        }

        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (RaycastHit hit in hits)
        {
            Collider hitCollider = hit.collider;
            if (hitCollider == null)
            {
                continue;
            }

            if (hitCollider.GetComponentInParent<PlayerNetwork>() != null)
            {
                continue;
            }

            groundY = hit.point.y;
            return true;
        }

        groundY = markerPosition.y;
        return false;
    }

    private void ApplyServerTeleport(Vector3 position, Quaternion rotation)
    {
        bool wasControllerEnabled = _characterController != null && _characterController.enabled;
        if (wasControllerEnabled)
        {
            _characterController.enabled = false;
        }

        transform.SetPositionAndRotation(position, rotation);

        if (_networkTransform != null && _networkTransform.IsSpawned)
        {
            _networkTransform.SetState(position, rotation, transform.localScale, teleportDisabled: false);
        }

        if (wasControllerEnabled)
        {
            _characterController.enabled = true;
        }
    }

    private static FixedString32Bytes SanitizeNickname(string nickname, ulong ownerClientId)
    {
        string trimmedValue = string.IsNullOrWhiteSpace(nickname) ? string.Empty : nickname.Trim();
        if (string.IsNullOrEmpty(trimmedValue))
        {
            return BuildDefaultNickname(ownerClientId);
        }

        FixedString32Bytes safeNickname = default;
        if (safeNickname.Append(trimmedValue) != FormatError.None)
        {
            return BuildDefaultNickname(ownerClientId);
        }

        return safeNickname;
    }

    private static FixedString32Bytes BuildDefaultNickname(ulong ownerClientId)
    {
        FixedString32Bytes defaultNickname = default;
        defaultNickname.Append($"Player_{ownerClientId}");
        return defaultNickname;
    }

    private Vector3 BuildFallbackSpawnPosition(ulong ownerClientId)
    {
        int slot = (int)ownerClientId;
        int column = slot % 2;
        int row = slot / 2;

        float x = column == 0 ? -_spawnSpacing : _spawnSpacing;
        float z = row * _spawnSpacing;

        return new Vector3(x, 1f, z);
    }

    private void ApplyControllerOwnershipState()
    {
        if (_characterController == null)
        {
            return;
        }

        bool shouldEnableController = IsAlive.Value && (IsServer || IsOwner);
        if (_characterController.enabled == shouldEnableController)
        {
            return;
        }

        _characterController.enabled = shouldEnableController;
    }
}
