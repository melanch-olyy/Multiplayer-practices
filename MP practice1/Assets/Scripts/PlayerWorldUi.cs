using FishNet;
using FishNet.Managing.Timing;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerNetwork))]
public class PlayerWorldUi : MonoBehaviour
{
    [SerializeField] private PlayerNetwork _playerNetwork;
    [SerializeField] private PlayerCombat _playerCombat;
    [SerializeField] private Transform _billboardRoot;
    [SerializeField] private Canvas _canvas;
    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private TextMeshProUGUI _nicknameText;
    [SerializeField] private TextMeshProUGUI _hpText;
    [SerializeField] private TextMeshProUGUI _ammoText;
    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private TextMeshProUGUI _respawnText;
    [SerializeField] private Image _hpFill;
    [SerializeField] private Slider _hpSlider;
    [SerializeField] private bool _faceMainCamera = true;
    [SerializeField] private bool _showDeadStatus = true;
    [SerializeField] private bool _showAmmoForOwnerOnly = true;
    [SerializeField] private float _syncRefreshInterval = 0.1f;
    [SerializeField] private string _hpFormat = "HP: {0}";
    [SerializeField] private string _ammoFormat = "Ammo: {0}/{1}";
    [SerializeField] private string _respawnFormat = "Respawn: {0:0.0}s";
    [SerializeField] private string _deadText = "DEAD";
    [SerializeField] private Color _aliveTextColor = Color.white;
    [SerializeField] private Color _deadTextColor = new(1f, 0.42f, 0.42f);

    private Camera _cachedMainCamera;
    private bool _subscribed;
    private float _nextSyncRefreshTime;

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        Subscribe();
        RefreshAll();
        _nextSyncRefreshTime = 0f;
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void LateUpdate()
    {
        RefreshSyncedValues();
        RefreshRespawnTime();

        if (!_faceMainCamera)
        {
            return;
        }

        Transform root = _billboardRoot != null ? _billboardRoot : (_canvas != null ? _canvas.transform : null);
        if (root == null)
        {
            return;
        }

        Camera mainCamera = _cachedMainCamera != null ? _cachedMainCamera : Camera.main;
        if (mainCamera == null)
        {
            return;
        }

        _cachedMainCamera = mainCamera;
        root.forward = mainCamera.transform.forward;

        if (_canvas != null && _canvas.worldCamera == null)
        {
            _canvas.worldCamera = mainCamera;
        }
    }

    private void ResolveReferences()
    {
        if (_playerNetwork == null)
        {
            _playerNetwork = GetComponent<PlayerNetwork>();
        }

        if (_playerCombat == null)
        {
            _playerCombat = GetComponent<PlayerCombat>();
        }

        if (_canvas == null)
        {
            _canvas = GetComponentInChildren<Canvas>(true);
        }

        if (_canvasGroup == null && _canvas != null)
        {
            _canvasGroup = _canvas.GetComponent<CanvasGroup>();
        }

        if (_hpSlider == null)
        {
            _hpSlider = GetComponentInChildren<Slider>(true);
        }

        if (_hpFill == null && _hpSlider != null && _hpSlider.fillRect != null)
        {
            _hpFill = _hpSlider.fillRect.GetComponent<Image>();
        }
    }

    private void Subscribe()
    {
        ResolveReferences();
        if (_playerNetwork == null || _subscribed)
        {
            return;
        }

        _playerNetwork.Nickname.OnChange += OnNicknameChanged;
        _playerNetwork.HP.OnChange += OnHpChanged;
        _playerNetwork.IsAlive.OnChange += OnIsAliveChanged;
        if (_playerCombat != null)
        {
            _playerCombat.CurrentAmmo.OnChange += OnAmmoChanged;
        }

        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (_playerNetwork == null || !_subscribed)
        {
            return;
        }

        _playerNetwork.Nickname.OnChange -= OnNicknameChanged;
        _playerNetwork.HP.OnChange -= OnHpChanged;
        _playerNetwork.IsAlive.OnChange -= OnIsAliveChanged;
        if (_playerCombat != null)
        {
            _playerCombat.CurrentAmmo.OnChange -= OnAmmoChanged;
        }

        _subscribed = false;
    }

    private void OnNicknameChanged(string previousValue, string nextValue, bool asServer)
    {
        if (_nicknameText != null)
        {
            _nicknameText.text = nextValue;
        }
    }

    private void OnHpChanged(int previousValue, int nextValue, bool asServer)
    {
        RefreshHp(nextValue);
    }

    private void OnIsAliveChanged(bool previousValue, bool nextValue, bool asServer)
    {
        RefreshAlive(nextValue);
        RefreshHp(_playerNetwork != null ? _playerNetwork.HP.Value : 0);
    }

    private void OnAmmoChanged(int previousValue, int nextValue, bool asServer)
    {
        RefreshAmmo(nextValue);
    }

    private void RefreshAll()
    {
        if (_playerNetwork == null)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);
        OnNicknameChanged(default, _playerNetwork.Nickname.Value, false);
        RefreshAlive(_playerNetwork.IsAlive.Value);
        RefreshHp(_playerNetwork.HP.Value);
        RefreshAmmo(_playerCombat != null ? _playerCombat.CurrentAmmo.Value : 0);
    }

    private void RefreshSyncedValues()
    {
        if (_playerNetwork == null)
        {
            return;
        }

        if (Time.unscaledTime < _nextSyncRefreshTime)
        {
            return;
        }

        _nextSyncRefreshTime = Time.unscaledTime + Mathf.Max(0.02f, _syncRefreshInterval);
        RefreshAlive(_playerNetwork.IsAlive.Value);
        RefreshHp(_playerNetwork.HP.Value);
        RefreshAmmo(_playerCombat != null ? _playerCombat.CurrentAmmo.Value : 0);
    }

    private void RefreshHp(int hp)
    {
        if (_playerNetwork == null)
        {
            return;
        }

        bool isAlive = _playerNetwork.IsAlive.Value;
        int maxHp = Mathf.Max(1, _playerNetwork.MaxHp);
        int clampedHp = Mathf.Clamp(hp, 0, maxHp);
        int displayedHp = isAlive ? clampedHp : 0;
        if (_hpText != null)
        {
            _hpText.text = isAlive ? string.Format(_hpFormat, displayedHp) : _deadText;
            _hpText.color = isAlive ? _aliveTextColor : _deadTextColor;
        }

        float normalizedHp = Mathf.Clamp01(displayedHp / (float)maxHp);
        if (_hpFill != null)
        {
            _hpFill.fillAmount = normalizedHp;
        }

        if (_hpSlider != null)
        {
            _hpSlider.interactable = false;
            _hpSlider.minValue = 0f;
            _hpSlider.maxValue = maxHp;
            _hpSlider.wholeNumbers = true;
            _hpSlider.SetValueWithoutNotify(displayedHp);
        }
    }

    private void RefreshAmmo(int ammo)
    {
        if (_ammoText == null)
        {
            return;
        }

        bool showAmmo = _playerNetwork != null && _playerCombat != null && (!_showAmmoForOwnerOnly || _playerNetwork.IsOwner);
        _ammoText.gameObject.SetActive(showAmmo);
        if (!showAmmo)
        {
            return;
        }

        _ammoText.text = string.Format(_ammoFormat, ammo, _playerCombat.MaxAmmo);
        _ammoText.color = _playerNetwork.IsAlive.Value ? _aliveTextColor : _deadTextColor;
    }

    private void RefreshAlive(bool isAlive)
    {
        if (_nicknameText != null)
        {
            _nicknameText.color = isAlive ? _aliveTextColor : _deadTextColor;
        }

        if (_statusText != null)
        {
            _statusText.gameObject.SetActive(_showDeadStatus && !isAlive);
            _statusText.text = isAlive ? string.Empty : _deadText;
            _statusText.color = _deadTextColor;
        }

        if (_respawnText != null)
        {
            _respawnText.gameObject.SetActive(!isAlive);
            RefreshRespawnTime();
        }

        if (_ammoText != null)
        {
            RefreshAmmo(_playerCombat != null ? _playerCombat.CurrentAmmo.Value : 0);
        }
    }

    private void RefreshRespawnTime()
    {
        if (_respawnText == null || _playerNetwork == null || _playerNetwork.IsAlive.Value)
        {
            return;
        }

        float remaining = Mathf.Max(0f, _playerNetwork.RespawnEndTime.Value - GetNetworkTimeSeconds());
        _respawnText.text = string.Format(_respawnFormat, remaining);
        _respawnText.color = _deadTextColor;
    }

    private void SetVisible(bool visible)
    {
        if (_canvas != null)
        {
            _canvas.enabled = visible;
        }

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }
    }

    private static float GetNetworkTimeSeconds()
    {
        TimeManager timeManager = InstanceFinder.TimeManager;
        return timeManager != null ? (float)timeManager.TicksToTime(timeManager.Tick) : Time.time;
    }
}
