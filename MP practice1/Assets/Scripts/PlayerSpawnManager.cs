using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class PlayerSpawnManager : MonoBehaviour
{
    public static PlayerSpawnManager Instance { get; private set; }

    [SerializeField] private List<Transform> _spawnPoints = new();
    [SerializeField] private Color _gizmoColor = new(0.2f, 0.65f, 1f, 0.95f);
    [SerializeField] private float _gizmoRadius = 0.55f;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool TryGetInitialSpawnPoint(ulong clientId, out Vector3 position)
    {
        int validCount = GetValidSpawnPointCount();
        if (validCount == 0)
        {
            position = default;
            return false;
        }

        int index = (int)(clientId % (ulong)validCount);
        Transform spawnPoint = GetValidSpawnPointByIndex(index);
        if (spawnPoint == null)
        {
            position = default;
            return false;
        }

        position = spawnPoint.position;
        return true;
    }

    public bool TryGetRespawnPoint(ulong clientId, out Vector3 position)
    {
        int validCount = GetValidSpawnPointCount();
        if (validCount == 0)
        {
            position = default;
            return false;
        }

        int index = Random.Range(0, validCount);
        Transform spawnPoint = GetValidSpawnPointByIndex(index);
        if (spawnPoint == null)
        {
            position = default;
            return false;
        }

        position = spawnPoint.position;
        return true;
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
            fillColor.a = selectedOnly ? 0.55f : 0.32f;
            Gizmos.color = fillColor;
            Gizmos.DrawSphere(point.position, _gizmoRadius);

            Gizmos.color = _gizmoColor;
            Gizmos.DrawWireSphere(point.position, _gizmoRadius);
            Gizmos.DrawLine(point.position, point.position + Vector3.up * 1.4f);
            Gizmos.DrawWireCube(point.position + Vector3.up * 0.7f, new Vector3(0.36f, 1.4f, 0.36f));

#if UNITY_EDITOR
            UnityEditor.Handles.color = _gizmoColor;
            UnityEditor.Handles.Label(point.position + Vector3.up * 1.45f, $"Player Spawn {index + 1}");
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

    private Transform GetValidSpawnPointByIndex(int validIndex)
    {
        int currentIndex = 0;
        foreach (Transform point in _spawnPoints)
        {
            if (point == null)
            {
                continue;
            }

            if (currentIndex == validIndex)
            {
                return point;
            }

            currentIndex++;
        }

        return null;
    }
}
