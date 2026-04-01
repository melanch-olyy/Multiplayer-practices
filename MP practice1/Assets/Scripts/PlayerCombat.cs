using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerNetwork))]
public class PlayerCombat : NetworkBehaviour
{
    [SerializeField] private PlayerNetwork _playerNetwork;
    [SerializeField] private int _damage = 10;

    private void Awake()
    {
        if (_playerNetwork == null)
        {
            _playerNetwork = GetComponent<PlayerNetwork>();
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            TryAttack();
        }
    }

    public void TryAttack()
    {
        if (!IsSpawned || !IsOwner || _playerNetwork == null)
        {
            return;
        }

        if (_playerNetwork.HP.Value <= 0)
        {
            return;
        }

        RequestAttackServerRpc(Mathf.Max(0, _damage));
    }

    [ServerRpc]
    private void RequestAttackServerRpc(int damage)
    {
        if (_playerNetwork == null || _playerNetwork.HP.Value <= 0)
        {
            return;
        }

        PlayerNetwork target = FindClosestTarget();
        if (target == null)
        {
            return;
        }

        target.ApplyDamage(damage);
    }

    private PlayerNetwork FindClosestTarget()
    {
        if (NetworkManager == null)
        {
            return null;
        }

        PlayerNetwork bestTarget = null;
        float bestDistance = float.MaxValue;

        foreach (NetworkObject spawnedObject in NetworkManager.SpawnManager.SpawnedObjectsList)
        {
            if (spawnedObject == null || spawnedObject == NetworkObject)
            {
                continue;
            }

            PlayerNetwork candidate = spawnedObject.GetComponent<PlayerNetwork>();
            if (!IsValidTarget(candidate))
            {
                continue;
            }

            float sqrDistance = (candidate.transform.position - transform.position).sqrMagnitude;
            if (sqrDistance >= bestDistance)
            {
                continue;
            }

            bestDistance = sqrDistance;
            bestTarget = candidate;
        }

        return bestTarget;
    }

    private bool IsValidTarget(PlayerNetwork candidate)
    {
        return candidate != null
            && candidate != _playerNetwork
            && candidate.IsSpawned
            && candidate.HP.Value > 0;
    }
}
