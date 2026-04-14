using Unity.Netcode.Components;
using UnityEngine;

[DisallowMultipleComponent]
public class OwnerNetworkTransform : NetworkTransform
{
    [SerializeField] private float _positionThreshold = 0.01f;
    [SerializeField] private float _rotationThreshold = 0.5f;

    protected override void Awake()
    {
        base.Awake();

        UseUnreliableDeltas = true;
        Interpolate = true;

        SyncRotAngleX = false;
        SyncRotAngleY = true;
        SyncRotAngleZ = false;

        SyncScaleX = false;
        SyncScaleY = false;
        SyncScaleZ = false;

        PositionThreshold = Mathf.Max(0.0001f, _positionThreshold);
        RotAngleThreshold = Mathf.Max(0.01f, _rotationThreshold);
    }

    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}
