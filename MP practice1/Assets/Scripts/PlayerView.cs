using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerNetwork))]
public class PlayerView : NetworkBehaviour
{
    [SerializeField] private PlayerNetwork _playerNetwork;
    [SerializeField] private Renderer _bodyRenderer;
    [SerializeField] private Vector3 _labelOffset = new(0f, 1.8f, 0f);
    [SerializeField] private float _labelScale = 0.32f;
    [SerializeField] private Color _ownerColor = new(0.36f, 0.84f, 0.44f);
    [SerializeField] private Color _remoteColor = new(0.25f, 0.48f, 1f);
    [SerializeField] private Color _deadLabelColor = new(1f, 0.42f, 0.42f);

    private Transform _labelRoot;
    private TextMeshPro _nicknameText;
    private TextMeshPro _hpText;
    private Camera _cachedMainCamera;

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

    public override void OnNetworkSpawn()
    {
        EnsureRuntimeLabel();

        _playerNetwork.Nickname.OnValueChanged += OnNicknameChanged;
        _playerNetwork.HP.OnValueChanged += OnHpChanged;
        _playerNetwork.IsAlive.OnValueChanged += OnIsAliveChanged;

        OnNicknameChanged(default, _playerNetwork.Nickname.Value);
        OnHpChanged(default, _playerNetwork.HP.Value);
        ApplyVisualState(_playerNetwork.IsAlive.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (_playerNetwork == null)
        {
            return;
        }

        _playerNetwork.Nickname.OnValueChanged -= OnNicknameChanged;
        _playerNetwork.HP.OnValueChanged -= OnHpChanged;
        _playerNetwork.IsAlive.OnValueChanged -= OnIsAliveChanged;
    }

    private void LateUpdate()
    {
        if (_labelRoot == null)
        {
            return;
        }

        Camera mainCamera = _cachedMainCamera != null ? _cachedMainCamera : Camera.main;
        if (mainCamera == null)
        {
            return;
        }

        _cachedMainCamera = mainCamera;
        _labelRoot.forward = mainCamera.transform.forward;
    }

    private void OnNicknameChanged(FixedString32Bytes _, FixedString32Bytes newValue)
    {
        if (_nicknameText != null)
        {
            _nicknameText.text = newValue.ToString();
        }
    }

    private void OnHpChanged(int _, int newValue)
    {
        if (_hpText == null)
        {
            return;
        }

        _hpText.text = _playerNetwork.IsAlive.Value ? $"HP: {newValue}" : "DEAD";
    }

    private void OnIsAliveChanged(bool _, bool isAlive)
    {
        ApplyVisualState(isAlive);
        OnHpChanged(default, _playerNetwork.HP.Value);
    }

    private void EnsureRuntimeLabel()
    {
        if (_labelRoot != null)
        {
            return;
        }

        GameObject root = new("PlayerRuntimeView");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = _labelOffset + GetStableHorizontalOffset();
        root.transform.localScale = Vector3.one * _labelScale;
        _labelRoot = root.transform;

        _nicknameText = CreateText("NicknameText", new Vector3(0f, 0.34f, 0f), 7f, FontStyles.Bold, Color.white);
        _hpText = CreateText("HpText", new Vector3(0f, -0.18f, 0f), 5.8f, FontStyles.Bold, new Color(1f, 0.85f, 0.35f));
    }

    private TextMeshPro CreateText(string objectName, Vector3 localPosition, float fontSize, FontStyles fontStyles, Color color)
    {
        GameObject textObject = new(objectName);
        textObject.transform.SetParent(_labelRoot, false);
        textObject.transform.localPosition = localPosition;

        TextMeshPro text = textObject.AddComponent<TextMeshPro>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = fontSize;
        text.fontStyle = fontStyles;
        text.color = color;
        text.enableWordWrapping = false;
        text.text = string.Empty;

        return text;
    }

    private void ApplyVisualState(bool isAlive)
    {
        if (_bodyRenderer != null)
        {
            _bodyRenderer.enabled = isAlive;
            _bodyRenderer.material.color = IsOwner ? _ownerColor : _remoteColor;
        }

        if (_nicknameText != null)
        {
            _nicknameText.color = isAlive ? Color.white : _deadLabelColor;
        }

        if (_hpText != null)
        {
            _hpText.color = isAlive ? new Color(1f, 0.85f, 0.35f) : _deadLabelColor;
        }
    }

    private Vector3 GetStableHorizontalOffset()
    {
        float horizontalOffset = OwnerClientId % 2 == 0 ? -0.45f : 0.45f;
        return new Vector3(horizontalOffset, 0f, 0f);
    }
}
