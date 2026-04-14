using TMPro;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Canvas))]
public class PlayerHud : MonoBehaviour
{
    [SerializeField] private Vector2 _statsAnchor = new(22f, -22f);
    [SerializeField] private Vector2 _respawnAnchor = new(0f, 110f);

    private PlayerNetwork _localPlayer;
    private PlayerCombat _localCombat;
    private TextMeshProUGUI _statsText;
    private TextMeshProUGUI _respawnText;
    private float _nextLookupTime;

    private void Awake()
    {
        Canvas canvas = GetComponent<Canvas>();
        CreateRuntimeHud(canvas.transform as RectTransform);
    }

    private void Update()
    {
        if (!TryBindLocalPlayer())
        {
            SetHudVisible(false);
            return;
        }

        SetHudVisible(true);
        _statsText.text = $"HP {_localPlayer.HP.Value}   Ammo {_localCombat.CurrentAmmo.Value}/{_localCombat.MaxAmmo}";

        bool isDead = !_localPlayer.IsAlive.Value;
        _respawnText.gameObject.SetActive(isDead);
        if (!isDead)
        {
            return;
        }

        float remaining = Mathf.Max(0f, _localPlayer.RespawnEndTime.Value - (float)NetworkManager.Singleton.ServerTime.Time);
        _respawnText.text = $"Respawn in {remaining:0.0}s";
    }

    private bool TryBindLocalPlayer()
    {
        if (_localPlayer != null && _localCombat != null && _localPlayer.IsSpawned && _localPlayer.IsOwner)
        {
            return true;
        }

        if (Time.unscaledTime < _nextLookupTime)
        {
            return false;
        }

        _nextLookupTime = Time.unscaledTime + 0.5f;

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            _localPlayer = null;
            _localCombat = null;
            return false;
        }

        PlayerNetwork[] players = FindObjectsOfType<PlayerNetwork>();
        foreach (PlayerNetwork player in players)
        {
            if (!player.IsOwner)
            {
                continue;
            }

            PlayerCombat combat = player.GetComponent<PlayerCombat>();
            if (combat == null)
            {
                continue;
            }

            _localPlayer = player;
            _localCombat = combat;
            return true;
        }

        _localPlayer = null;
        _localCombat = null;
        return false;
    }

    private void SetHudVisible(bool visible)
    {
        if (_statsText != null)
        {
            _statsText.gameObject.SetActive(visible);
        }

        if (_respawnText != null && !visible)
        {
            _respawnText.gameObject.SetActive(false);
        }
    }

    private void CreateRuntimeHud(RectTransform canvasTransform)
    {
        if (canvasTransform == null)
        {
            return;
        }

        _statsText = CreateText(canvasTransform, "RuntimeStats", _statsAnchor, TextAlignmentOptions.TopLeft, 28);
        _respawnText = CreateText(canvasTransform, "RuntimeRespawn", _respawnAnchor, TextAlignmentOptions.Center, 34);
        _respawnText.fontStyle = FontStyles.Bold;
        _respawnText.color = new Color(1f, 0.88f, 0.35f, 1f);
        _respawnText.gameObject.SetActive(false);
    }

    private TextMeshProUGUI CreateText(RectTransform parent, string objectName, Vector2 anchoredPosition, TextAlignmentOptions alignment, float fontSize)
    {
        GameObject textObject = new(objectName, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);

        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = alignment == TextAlignmentOptions.TopLeft ? new Vector2(0f, 1f) : new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = rectTransform.anchorMin;
        rectTransform.pivot = alignment == TextAlignmentOptions.TopLeft ? new Vector2(0f, 1f) : new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = new Vector2(600f, 70f);

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = string.Empty;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.enableWordWrapping = false;

        return text;
    }
}
