using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameSessionHud : MonoBehaviour
{
    [Header("Roots")]
    [SerializeField] private GameObject _backgroundRoot;
    [SerializeField] private GameObject _lobbyRoot;
    [SerializeField] private GameObject _hostSettingsRoot;
    [SerializeField] private GameObject _matchRoot;
    [SerializeField] private GameObject _resultsRoot;

    [Header("Lobby")]
    [SerializeField] private TextMeshProUGUI _lobbyTitleText;
    [SerializeField] private TextMeshProUGUI _lobbyPlayersText;
    [SerializeField] private TextMeshProUGUI _lobbySettingsText;

    [Header("Host Controls")]
    [SerializeField] private TMP_InputField _matchDurationInput;
    [SerializeField] private TMP_InputField _scoreLimitInput;
    [SerializeField] private Button _startMatchButton;
    [SerializeField] private TextMeshProUGUI _startMatchButtonText;

    [Header("Match")]
    [SerializeField] private TextMeshProUGUI _timerText;
    [SerializeField] private TextMeshProUGUI _scoreboardText;

    [Header("Results")]
    [SerializeField] private TextMeshProUGUI _resultsText;

    private bool _inputsInitialized;

    private void OnEnable()
    {
        GameSessionManager.SnapshotChanged += Refresh;
        if (_startMatchButton != null)
        {
            _startMatchButton.onClick.AddListener(StartMatch);
        }

        if (_matchDurationInput != null)
        {
            _matchDurationInput.onEndEdit.AddListener(ApplyHostSettings);
        }

        if (_scoreLimitInput != null)
        {
            _scoreLimitInput.onEndEdit.AddListener(ApplyHostSettings);
        }

        Refresh();
    }

    private void OnDisable()
    {
        GameSessionManager.SnapshotChanged -= Refresh;
        if (_startMatchButton != null)
        {
            _startMatchButton.onClick.RemoveListener(StartMatch);
        }

        if (_matchDurationInput != null)
        {
            _matchDurationInput.onEndEdit.RemoveListener(ApplyHostSettings);
        }

        if (_scoreLimitInput != null)
        {
            _scoreLimitInput.onEndEdit.RemoveListener(ApplyHostSettings);
        }
    }

    private void Update()
    {
        Refresh();
    }

    private void Refresh()
    {
        GameSessionManager manager = GameSessionManager.Instance;
        if (manager == null || !manager.IsNetworkSessionActive)
        {
            SetAllVisible(false);
            return;
        }

        InitializeInputs(manager);

        bool showLobby = manager.CurrentState == MatchState.WaitingForPlayers;
        bool showMatch = manager.CurrentState == MatchState.InProgress;
        bool showResults = manager.CurrentState == MatchState.ShowingResults;

        SetActive(_backgroundRoot, showLobby || showResults);
        SetActive(_lobbyRoot, showLobby);
        SetActive(_matchRoot, showMatch);
        SetActive(_resultsRoot, showResults);
        SetActive(_hostSettingsRoot, showLobby && manager.IsLocalSessionController);

        switch (manager.CurrentState)
        {
            case MatchState.WaitingForPlayers:
                SetText(_lobbyTitleText, manager.IsLocalSessionController ? "Lobby" : "Waiting for host");
                SetText(_lobbyPlayersText, $"Players: {manager.ConnectedPlayers}/{manager.RequiredPlayers}");
                SetText(_lobbySettingsText, $"Match: {manager.MatchDuration:0}s   Target: {manager.ScoreToWin}");
                RefreshStartButton(manager);
                break;

            case MatchState.InProgress:
                SetText(_timerText, $"Time: {manager.MatchTimeRemaining:0}s");
                SetText(_scoreboardText, string.IsNullOrWhiteSpace(manager.ScoreboardText) ? "No score yet" : manager.ScoreboardText);
                break;

            case MatchState.ShowingResults:
                SetText(_resultsText, string.IsNullOrWhiteSpace(manager.ResultsText)
                    ? "No scores"
                    : manager.ResultsText);
                break;
        }
    }

    private void InitializeInputs(GameSessionManager manager)
    {
        if (_inputsInitialized)
        {
            return;
        }

        if (_matchDurationInput != null)
        {
            _matchDurationInput.text = Mathf.RoundToInt(manager.MatchDuration).ToString();
        }

        if (_scoreLimitInput != null)
        {
            _scoreLimitInput.text = manager.ScoreToWin.ToString();
        }

        _inputsInitialized = true;
    }

    private void RefreshStartButton(GameSessionManager manager)
    {
        if (_startMatchButton != null)
        {
            _startMatchButton.interactable = manager.IsLocalSessionController && manager.CanStartMatch;
        }

        if (_startMatchButtonText != null)
        {
            _startMatchButtonText.text = manager.CanStartMatch ? "Start Match" : "Waiting";
        }
    }

    private void ApplyHostSettings(string _)
    {
        GameSessionManager manager = GameSessionManager.Instance;
        if (manager == null || !manager.IsLocalSessionController)
        {
            return;
        }

        manager.ApplyHostSettings(ReadDuration(manager.MatchDuration), ReadScoreLimit(manager.ScoreToWin));
    }

    public void StartMatch()
    {
        GameSessionManager manager = GameSessionManager.Instance;
        if (manager == null || !manager.IsLocalSessionController)
        {
            return;
        }

        manager.TryStartMatchFromHost(ReadDuration(manager.MatchDuration), ReadScoreLimit(manager.ScoreToWin));
    }

    private float ReadDuration(float fallback)
    {
        if (_matchDurationInput != null && float.TryParse(_matchDurationInput.text, out float value))
        {
            return value;
        }

        return fallback;
    }

    private int ReadScoreLimit(int fallback)
    {
        if (_scoreLimitInput != null && int.TryParse(_scoreLimitInput.text, out int value))
        {
            return value;
        }

        return fallback;
    }

    private void SetAllVisible(bool visible)
    {
        SetActive(_backgroundRoot, visible);
        SetActive(_lobbyRoot, visible);
        SetActive(_hostSettingsRoot, visible);
        SetActive(_matchRoot, visible);
        SetActive(_resultsRoot, visible);
    }

    private static void SetText(TextMeshProUGUI text, string value)
    {
        if (text != null)
        {
            text.text = value;
        }
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }
}
