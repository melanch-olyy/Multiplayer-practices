using System;
using System.Collections;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class PlayerNetwork : NetworkBehaviour
{
    [SerializeField] private int _startingHp = 100;
    [SerializeField] private float _spawnSpacing = 3f;
    [SerializeField] private float _respawnDelay = 3f;

    public readonly SyncVar<string> Nickname = new(string.Empty);
    public readonly SyncVar<int> HP = new(100);
    public readonly SyncVar<int> Score = new(0);
    public readonly SyncVar<bool> IsAlive = new(true);
    public readonly SyncVar<float> RespawnEndTime = new(0f);

    public int MaxHp => _startingHp;
    public ulong OwnerIdAsUlong => OwnerId < 0 ? 0UL : (ulong)OwnerId;

    private CharacterController _characterController;
    private PlayerCombat _playerCombat;
    private PlayerMovement _playerMovement;
    private Coroutine _respawnRoutine;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _playerCombat = GetComponent<PlayerCombat>();
        _playerMovement = GetComponent<PlayerMovement>();
    }

    public override void OnStartNetwork()
    {
        HP.OnChange += OnHpChanged;
        IsAlive.OnChange += OnIsAliveChanged;
        ApplyControllerOwnershipState();
    }

    public override void OnStopNetwork()
    {
        HP.OnChange -= OnHpChanged;
        IsAlive.OnChange -= OnIsAliveChanged;
        ApplyControllerOwnershipState();
    }

    public override void OnStartServer()
    {
        Nickname.Value = BuildDefaultNickname(OwnerIdAsUlong);
        HP.Value = Mathf.Max(0, _startingHp);
        Score.Value = 0;
        IsAlive.Value = true;
        RespawnEndTime.Value = 0f;
        MoveToSpawnPoint(initialSpawn: true);

        ApplyControllerOwnershipState();
    }

    public override void OnStopServer()
    {
        if (_respawnRoutine != null)
        {
            StopCoroutine(_respawnRoutine);
            _respawnRoutine = null;
        }
    }

    public override void OnStartClient()
    {
        if (base.IsOwner)
        {
            SubmitNicknameServerRpc(ConnectionUI.PlayerNickname);
        }

        ApplyControllerOwnershipState();
    }

    public override void OnOwnershipClient(NetworkConnection prevOwner)
    {
        ApplyControllerOwnershipState();
    }

    [ServerRpc]
    private void SubmitNicknameServerRpc(string nickname)
    {
        Nickname.Value = SanitizeNickname(nickname, OwnerIdAsUlong);
    }

    public bool ApplyDamage(int damage)
    {
        return ApplyDamage(damage, null);
    }

    public bool ApplyDamage(int damage, PlayerNetwork attacker)
    {
        if (!IsServerStarted || !IsAlive.Value)
        {
            return false;
        }

        int safeDamage = Mathf.Max(0, damage);
        if (safeDamage == 0 || HP.Value <= 0)
        {
            return false;
        }

        int previousHp = HP.Value;
        HP.Value = Mathf.Max(0, HP.Value - safeDamage);
        if (previousHp > 0 && HP.Value <= 0 && attacker != null && attacker != this)
        {
            attacker.AddScore(1);
        }

        return true;
    }

    public bool ApplyHeal(int amount)
    {
        if (!IsServerStarted || !IsAlive.Value)
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

    public void AddScore(int amount)
    {
        if (!IsServerStarted || amount <= 0)
        {
            return;
        }

        Score.Value += amount;
    }

    public void ResetForMatch(bool resetScore)
    {
        if (!IsServerStarted)
        {
            return;
        }

        if (_respawnRoutine != null)
        {
            StopCoroutine(_respawnRoutine);
            _respawnRoutine = null;
        }

        if (resetScore)
        {
            Score.Value = 0;
        }

        MoveToSpawnPoint(initialSpawn: true);
        HP.Value = Mathf.Max(0, _startingHp);
        IsAlive.Value = true;
        RespawnEndTime.Value = 0f;
        _playerCombat?.RestoreAmmo();
        ApplyControllerOwnershipState();
    }

    private void OnHpChanged(int previousValue, int nextValue, bool asServer)
    {
        if (!asServer || !IsServerStarted)
        {
            return;
        }

        if (nextValue > 0 || !IsAlive.Value)
        {
            return;
        }

        IsAlive.Value = false;
        RespawnEndTime.Value = GetNetworkTimeSeconds() + _respawnDelay;

        if (_respawnRoutine != null)
        {
            StopCoroutine(_respawnRoutine);
        }

        _respawnRoutine = StartCoroutine(RespawnRoutine());
    }

    private void OnIsAliveChanged(bool previousValue, bool nextValue, bool asServer)
    {
        if (asServer && IsServerStarted && nextValue)
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
                ? PlayerSpawnManager.Instance.TryGetInitialSpawnPoint(OwnerIdAsUlong, out spawnPosition)
                : PlayerSpawnManager.Instance.TryGetRespawnPoint(OwnerIdAsUlong, out spawnPosition);
        }
        else
        {
            spawnPosition = default;
        }

        if (!hasSpawnPoint)
        {
            spawnPosition = BuildFallbackSpawnPosition(OwnerIdAsUlong);
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

        if (_playerMovement != null)
        {
            _playerMovement.SetPredictedState(position, rotation, 0f);
        }

        if (wasControllerEnabled)
        {
            _characterController.enabled = true;
        }
    }

    private static string SanitizeNickname(string nickname, ulong ownerClientId)
    {
        string trimmedValue = string.IsNullOrWhiteSpace(nickname) ? string.Empty : nickname.Trim();
        if (string.IsNullOrEmpty(trimmedValue))
        {
            return BuildDefaultNickname(ownerClientId);
        }

        return trimmedValue.Length <= 32 ? trimmedValue : trimmedValue.Substring(0, 32);
    }

    private static string BuildDefaultNickname(ulong ownerClientId)
    {
        return $"Player_{ownerClientId}";
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

        bool shouldEnableController = IsSpawned && IsAlive.Value && (IsOwner || IsServerStarted);
        if (_characterController.enabled == shouldEnableController)
        {
            return;
        }

        _characterController.enabled = shouldEnableController;
    }

    private float GetNetworkTimeSeconds()
    {
        return TimeManager != null ? (float)TimeManager.TicksToTime(TimeManager.Tick) : Time.time;
    }
}
