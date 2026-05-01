using FishNet.Object;
using UnityEngine;

public class PlayerCamera : NetworkBehaviour
{
    [SerializeField] private Vector3 _offset = new(0f, 8f, -6f);
    [SerializeField] private Vector3 _lookOffset = new(0f, 1f, 0f);

    private Camera _mainCamera;

    public override void OnStartClient()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        _mainCamera = Camera.main;
        SnapCamera();
    }

    private void LateUpdate()
    {
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                return;
            }
        }

        SnapCamera();
    }

    private void SnapCamera()
    {
        _mainCamera.transform.position = transform.position + _offset;
        _mainCamera.transform.LookAt(transform.position + _lookOffset);
    }
}
