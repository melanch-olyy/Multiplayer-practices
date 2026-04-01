using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class PlayerNetwork : NetworkBehaviour
{
    [SerializeField] private int _startingHp = 100;
    [SerializeField] private float _spawnSpacing = 3f;

    public NetworkVariable<FixedString32Bytes> Nickname = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<int> HP = new(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            transform.position = BuildSpawnPosition(OwnerClientId);
            Nickname.Value = BuildDefaultNickname(OwnerClientId);
            HP.Value = Mathf.Max(0, _startingHp);
        }

        if (IsOwner)
        {
            SubmitNicknameServerRpc(ConnectionUI.PlayerNickname);
        }
    }

    [ServerRpc]
    private void SubmitNicknameServerRpc(string nickname)
    {
        Nickname.Value = SanitizeNickname(nickname, OwnerClientId);
    }

    public void ApplyDamage(int damage)
    {
        if (!IsServer)
        {
            return;
        }

        int safeDamage = Mathf.Max(0, damage);
        if (safeDamage == 0 || HP.Value <= 0)
        {
            return;
        }

        HP.Value = Mathf.Max(0, HP.Value - safeDamage);
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

    private Vector3 BuildSpawnPosition(ulong ownerClientId)
    {
        int slot = (int)ownerClientId;
        int column = slot % 2;
        int row = slot / 2;

        float x = column == 0 ? -_spawnSpacing : _spawnSpacing;
        float z = row * _spawnSpacing;

        return new Vector3(x, 1f, z);
    }
}
