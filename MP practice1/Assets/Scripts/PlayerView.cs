using FishNet.Object;
using UnityEngine;

[RequireComponent(typeof(PlayerNetwork))]
public class PlayerView : NetworkBehaviour
{
    [SerializeField] private PlayerNetwork _playerNetwork;
    [SerializeField] private Renderer _bodyRenderer;
    [SerializeField] private Color _playerOneColor = new(0.36f, 0.84f, 0.44f);
    [SerializeField] private Color _playerTwoColor = new(0.25f, 0.48f, 1f);
    [SerializeField] private float _fallbackSaturation = 0.62f;
    [SerializeField] private float _fallbackValue = 0.92f;
    [SerializeField] private float _deadDarkenFactor = 0.36f;

    private void Awake()
    {
        if (_playerNetwork == null)
        {
            _playerNetwork = GetComponent<PlayerNetwork>();
        }

        if (_bodyRenderer == null)
        {
            _bodyRenderer = GetComponentInChildren<Renderer>();
        }
    }

    public override void OnStartNetwork()
    {
        _playerNetwork.IsAlive.OnChange += OnIsAliveChanged;

        ApplyVisualState(_playerNetwork.IsAlive.Value);
    }

    public override void OnStopNetwork()
    {
        if (_playerNetwork == null)
        {
            return;
        }

        _playerNetwork.IsAlive.OnChange -= OnIsAliveChanged;
    }

    private void OnIsAliveChanged(bool _, bool isAlive, bool asServer)
    {
        ApplyVisualState(isAlive);
    }

    private void ApplyVisualState(bool isAlive)
    {
        if (_bodyRenderer != null)
        {
            _bodyRenderer.enabled = isAlive;
            Color identityColor = ResolvePlayerColor();
            _bodyRenderer.material.color = isAlive
                ? identityColor
                : Color.Lerp(identityColor, Color.black, Mathf.Clamp01(_deadDarkenFactor));
        }
    }

    private Color ResolvePlayerColor()
    {
        ulong ownerId = GetOwnerIdAsUlong();
        if (ownerId == 0ul)
        {
            return _playerOneColor;
        }

        if (ownerId == 1ul)
        {
            return _playerTwoColor;
        }

        float goldenRatio = 0.61803395f;
        float hue = Mathf.Repeat((ownerId + 1ul) * goldenRatio, 1f);
        return Color.HSVToRGB(hue, Mathf.Clamp01(_fallbackSaturation), Mathf.Clamp01(_fallbackValue));
    }

    private ulong GetOwnerIdAsUlong()
    {
        return OwnerId < 0 ? 0UL : (ulong)OwnerId;
    }
}
