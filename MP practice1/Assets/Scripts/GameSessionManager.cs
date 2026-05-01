using System.Collections.Generic;
using System.Text;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

public enum MatchState : byte
{
    WaitingForPlayers,
    InProgress,
    ShowingResults
}

public struct MatchSnapshotBroadcast : IBroadcast
{
    public MatchState State;
    public int ConnectedPlayers;
    public int RequiredPlayers;
    public int SessionControllerClientId;
    public float MatchTimeRemaining;
    public double MatchEndTime;
    public float MatchDuration;
    public int ScoreToWin;
    public bool CanStartMatch;
    public string ScoreboardText;
    public string ResultsText;
}

public enum MatchControlAction : byte
{
    ApplySettings,
    StartMatch
}

public struct MatchControlBroadcast : IBroadcast
{
    public MatchControlAction Action;
    public float MatchDuration;
    public int ScoreToWin;
}

public class GameSessionManager : MonoBehaviour
{
    private const int NoControllerClientId = -1;

    public static GameSessionManager Instance { get; private set; }
    public static event System.Action SnapshotChanged;

    [SerializeField] private int _requiredPlayers = 2;
    [SerializeField] private float _matchDuration = 60f;
    [SerializeField] private float _resultsDuration = 5f;
    [SerializeField] private int _scoreToWin = 3;
    [SerializeField] private float _snapshotInterval = 0.25f;

    public MatchState CurrentState { get; private set; } = MatchState.WaitingForPlayers;
    public int ConnectedPlayers { get; private set; }
    public int RequiredPlayers => Mathf.Max(1, _requiredPlayers);
    public int SessionControllerClientId { get; private set; } = NoControllerClientId;
    public float MatchTimeRemaining
    {
        get => CalculateMatchTimeRemaining();
        private set => _matchTimeRemaining = Mathf.Max(0f, value);
    }

    public float MatchDuration => _matchDuration;
    public int ScoreToWin => Mathf.Max(1, _scoreToWin);
    public bool CanStartMatch { get; private set; }
    public string ScoreboardText { get; private set; } = string.Empty;
    public string ResultsText { get; private set; } = string.Empty;
    public bool IsNetworkSessionActive => IsNetworkSessionStarted();
    public bool IsLocalSessionController => IsLocalController();

    private NetworkManager _networkManager;
    private bool _callbacksRegistered;
    private float _nextNetworkLookupTime;
    private float _nextSnapshotTime;
    private float _resultsEndTime;
    private float _matchTimeRemaining;
    private double _matchEndTime;

    public static bool IsGameplayActive
    {
        get
        {
            GameSessionManager manager = Instance;
            if (manager == null || !manager.IsNetworkSessionStarted())
            {
                return true;
            }

            return manager.CurrentState == MatchState.InProgress;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeInstance()
    {
        if (FindObjectOfType<GameSessionManager>() != null)
        {
            return;
        }

        GameObject managerObject = new("Game Session Manager");
        DontDestroyOnLoad(managerObject);
        managerObject.AddComponent<GameSessionManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        MatchTimeRemaining = _matchDuration;
    }

    private void OnDestroy()
    {
        UnregisterCallbacks();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (!TryResolveNetworkManager())
        {
            return;
        }

        if (_networkManager.IsServerStarted)
        {
            UpdateServerState();
        }
        else if (!_networkManager.IsClientStarted)
        {
            ApplySnapshot(new MatchSnapshotBroadcast
            {
                State = MatchState.WaitingForPlayers,
                ConnectedPlayers = 0,
                RequiredPlayers = RequiredPlayers,
                SessionControllerClientId = NoControllerClientId,
                MatchTimeRemaining = _matchDuration,
                MatchEndTime = 0d,
                MatchDuration = _matchDuration,
                ScoreToWin = ScoreToWin,
                CanStartMatch = false,
                ScoreboardText = string.Empty,
                ResultsText = string.Empty
            });
        }
    }

    private bool TryResolveNetworkManager()
    {
        if (_networkManager != null)
        {
            RegisterCallbacks();
            return true;
        }

        if (Time.unscaledTime < _nextNetworkLookupTime)
        {
            return false;
        }

        _nextNetworkLookupTime = Time.unscaledTime + 0.5f;
        _networkManager = InstanceFinder.NetworkManager;
        if (_networkManager == null)
        {
            _networkManager = FindObjectOfType<NetworkManager>();
        }

        RegisterCallbacks();
        return _networkManager != null;
    }

    private void RegisterCallbacks()
    {
        if (_networkManager == null || _callbacksRegistered)
        {
            return;
        }

        _networkManager.ClientManager.RegisterBroadcast<MatchSnapshotBroadcast>(OnMatchSnapshot);
        _networkManager.ServerManager.RegisterBroadcast<MatchControlBroadcast>(OnMatchControlRequest);
        _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
        _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
        _callbacksRegistered = true;
    }

    private void UnregisterCallbacks()
    {
        if (_networkManager == null || !_callbacksRegistered)
        {
            return;
        }

        _networkManager.ClientManager.UnregisterBroadcast<MatchSnapshotBroadcast>(OnMatchSnapshot);
        _networkManager.ServerManager.UnregisterBroadcast<MatchControlBroadcast>(OnMatchControlRequest);
        _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
        _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
        _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        _callbacksRegistered = false;
    }

    private void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Stopped)
        {
            ApplySnapshot(new MatchSnapshotBroadcast
            {
                State = MatchState.WaitingForPlayers,
                ConnectedPlayers = 0,
                RequiredPlayers = RequiredPlayers,
                SessionControllerClientId = NoControllerClientId,
                MatchTimeRemaining = _matchDuration,
                MatchEndTime = 0d,
                MatchDuration = _matchDuration,
                ScoreToWin = ScoreToWin,
                CanStartMatch = false,
                ScoreboardText = string.Empty,
                ResultsText = string.Empty
            });
        }
    }

    private void OnServerConnectionState(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            SessionControllerClientId = NoControllerClientId;
            ResetToLobby(resetPlayers: true);
        }
        else if (args.ConnectionState == LocalConnectionState.Stopped)
        {
            ApplySnapshot(new MatchSnapshotBroadcast
            {
                State = MatchState.WaitingForPlayers,
                ConnectedPlayers = 0,
                RequiredPlayers = RequiredPlayers,
                SessionControllerClientId = NoControllerClientId,
                MatchTimeRemaining = _matchDuration,
                MatchEndTime = 0d,
                MatchDuration = _matchDuration,
                ScoreToWin = ScoreToWin,
                CanStartMatch = false,
                ScoreboardText = string.Empty,
                ResultsText = string.Empty
            });
        }
    }

    private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (_networkManager == null || !_networkManager.IsServerStarted)
        {
            return;
        }

        UpdateConnectedPlayers();
        if (args.ConnectionState == RemoteConnectionState.Stopped && ConnectedPlayers < RequiredPlayers && CurrentState != MatchState.WaitingForPlayers)
        {
            ResetToLobby(resetPlayers: true);
            return;
        }

        BroadcastSnapshot(force: true);
    }

    private void OnMatchSnapshot(MatchSnapshotBroadcast snapshot, Channel channel)
    {
        ApplySnapshot(snapshot);
    }

    private void OnMatchControlRequest(NetworkConnection conn, MatchControlBroadcast request, Channel channel)
    {
        if (_networkManager == null || !_networkManager.IsServerStarted)
        {
            return;
        }

        if (!IsAuthorizedSessionController(conn))
        {
            Debug.LogWarning($"[Server] Ignored match control request from client {conn?.ClientId ?? NoControllerClientId}.");
            BroadcastSnapshot(force: true);
            return;
        }

        switch (request.Action)
        {
            case MatchControlAction.ApplySettings:
                ApplyHostSettingsOnServer(request.MatchDuration, request.ScoreToWin);
                break;

            case MatchControlAction.StartMatch:
                TryStartMatchOnServer(request.MatchDuration, request.ScoreToWin);
                break;
        }
    }

    private void UpdateServerState()
    {
        UpdateConnectedPlayers();
        CanStartMatch = CurrentState == MatchState.WaitingForPlayers
            && ConnectedPlayers >= RequiredPlayers
            && CountPlayerObjects() >= RequiredPlayers;
        ScoreboardText = BuildScoreboardText(includePositions: false);

        switch (CurrentState)
        {
            case MatchState.WaitingForPlayers:
                break;

            case MatchState.InProgress:
                MatchTimeRemaining = CalculateMatchTimeRemaining();
                if (MatchTimeRemaining <= 0f || HasPlayerReachedScoreLimit())
                {
                    EndMatch();
                }
                break;

            case MatchState.ShowingResults:
                if (Time.unscaledTime >= _resultsEndTime)
                {
                    ResetToLobby(resetPlayers: true);
                }
                break;
        }

        BroadcastSnapshot(force: false);
    }

    public void ApplyHostSettings(float matchDuration, int scoreToWin)
    {
        if (!TryResolveNetworkManager() || CurrentState != MatchState.WaitingForPlayers)
        {
            return;
        }

        if (_networkManager.IsServerStarted)
        {
            ApplyHostSettingsOnServer(matchDuration, scoreToWin);
            return;
        }

        if (_networkManager.IsClientStarted && IsLocalSessionController)
        {
            SendMatchControlRequest(MatchControlAction.ApplySettings, matchDuration, scoreToWin);
        }
    }

    private void ApplyHostSettingsOnServer(float matchDuration, int scoreToWin)
    {
        if (_networkManager == null || !_networkManager.IsServerStarted || CurrentState != MatchState.WaitingForPlayers)
        {
            return;
        }

        _matchDuration = Mathf.Clamp(matchDuration, 10f, 3600f);
        _scoreToWin = Mathf.Clamp(scoreToWin, 1, 999);
        _matchEndTime = 0d;
        MatchTimeRemaining = _matchDuration;
        BroadcastSnapshot(force: true);
    }

    public bool TryStartMatchFromHost(float matchDuration, int scoreToWin)
    {
        if (!TryResolveNetworkManager() || CurrentState != MatchState.WaitingForPlayers)
        {
            return false;
        }

        if (_networkManager.IsServerStarted)
        {
            return TryStartMatchOnServer(matchDuration, scoreToWin);
        }

        if (_networkManager.IsClientStarted && IsLocalSessionController)
        {
            SendMatchControlRequest(MatchControlAction.StartMatch, matchDuration, scoreToWin);
            return true;
        }

        return false;
    }

    private bool TryStartMatchOnServer(float matchDuration, int scoreToWin)
    {
        ApplyHostSettingsOnServer(matchDuration, scoreToWin);
        if (_networkManager == null || !_networkManager.IsServerStarted || CurrentState != MatchState.WaitingForPlayers)
        {
            return false;
        }

        UpdateConnectedPlayers();
        CanStartMatch = ConnectedPlayers >= RequiredPlayers && CountPlayerObjects() >= RequiredPlayers;
        if (!CanStartMatch)
        {
            BroadcastSnapshot(force: true);
            return false;
        }

        StartMatch();
        return true;
    }

    private void SendMatchControlRequest(MatchControlAction action, float matchDuration, int scoreToWin)
    {
        if (_networkManager == null || !_networkManager.IsClientStarted)
        {
            return;
        }

        MatchControlBroadcast request = new()
        {
            Action = action,
            MatchDuration = matchDuration,
            ScoreToWin = scoreToWin
        };
        _networkManager.ClientManager.Broadcast(request, Channel.Reliable);
    }

    private void StartMatch()
    {
        ResetPlayersForNewMatch();
        CurrentState = MatchState.InProgress;
        _matchEndTime = GetNetworkTimeSeconds() + _matchDuration;
        MatchTimeRemaining = _matchDuration;
        CanStartMatch = false;
        ScoreboardText = BuildScoreboardText(includePositions: false);
        ResultsText = string.Empty;
        Debug.Log("[Server] Match started.");
        BroadcastSnapshot(force: true);
    }

    private void EndMatch()
    {
        CurrentState = MatchState.ShowingResults;
        _matchEndTime = 0d;
        MatchTimeRemaining = 0f;
        ResultsText = BuildScoreboardText(includePositions: true);
        ScoreboardText = ResultsText;
        CanStartMatch = false;
        _resultsEndTime = Time.unscaledTime + _resultsDuration;
        Debug.Log("[Server] Match ended.");
        BroadcastSnapshot(force: true);
    }

    private void ResetToLobby(bool resetPlayers)
    {
        CurrentState = MatchState.WaitingForPlayers;
        _matchEndTime = 0d;
        MatchTimeRemaining = _matchDuration;
        CanStartMatch = false;
        ResultsText = string.Empty;

        if (resetPlayers)
        {
            ResetPlayersForLobby();
        }

        UpdateConnectedPlayers();
        ScoreboardText = BuildScoreboardText(includePositions: false);
        BroadcastSnapshot(force: true);
    }

    private void ResetPlayersForNewMatch()
    {
        foreach (PlayerNetwork player in GetPlayerObjects())
        {
            player.ResetForMatch(resetScore: true);
        }
    }

    private void ResetPlayersForLobby()
    {
        foreach (PlayerNetwork player in GetPlayerObjects())
        {
            player.ResetForMatch(resetScore: true);
        }
    }

    private void UpdateConnectedPlayers()
    {
        ConnectedPlayers = _networkManager != null && _networkManager.ServerManager != null
            ? _networkManager.ServerManager.Clients.Count
            : 0;
        UpdateSessionController();
    }

    private void UpdateSessionController()
    {
        if (_networkManager == null || _networkManager.ServerManager == null || ConnectedPlayers <= 0)
        {
            SessionControllerClientId = NoControllerClientId;
            return;
        }

        if (SessionControllerClientId != NoControllerClientId
            && _networkManager.ServerManager.Clients.ContainsKey(SessionControllerClientId))
        {
            return;
        }

        int selectedClientId = int.MaxValue;
        foreach (int clientId in _networkManager.ServerManager.Clients.Keys)
        {
            if (clientId >= 0 && clientId < selectedClientId)
            {
                selectedClientId = clientId;
            }
        }

        SessionControllerClientId = selectedClientId == int.MaxValue
            ? NoControllerClientId
            : selectedClientId;
    }

    private bool IsAuthorizedSessionController(NetworkConnection conn)
    {
        if (conn == null || !conn.IsValid)
        {
            return false;
        }

        UpdateConnectedPlayers();
        return conn.ClientId == SessionControllerClientId;
    }

    private bool IsLocalController()
    {
        if (!TryResolveNetworkManager() || !_networkManager.IsClientStarted)
        {
            return false;
        }

        NetworkConnection localConnection = _networkManager.ClientManager.Connection;
        return localConnection != null
            && localConnection.ClientId >= 0
            && localConnection.ClientId == SessionControllerClientId;
    }

    private int CountPlayerObjects()
    {
        int count = 0;
        foreach (PlayerNetwork _ in GetPlayerObjects())
        {
            count++;
        }

        return count;
    }

    private bool HasPlayerReachedScoreLimit()
    {
        if (_scoreToWin <= 0)
        {
            return false;
        }

        foreach (PlayerNetwork player in GetPlayerObjects())
        {
            if (player.Score.Value >= _scoreToWin)
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<PlayerNetwork> GetPlayerObjects()
    {
        if (_networkManager == null || _networkManager.ServerManager == null)
        {
            yield break;
        }

        foreach (FishNet.Object.NetworkObject networkObject in _networkManager.ServerManager.Objects.Spawned.Values)
        {
            if (networkObject == null)
            {
                continue;
            }

            PlayerNetwork player = networkObject.GetComponent<PlayerNetwork>();
            if (player != null)
            {
                yield return player;
            }
        }
    }

    private string BuildScoreboardText(bool includePositions)
    {
        List<PlayerScore> scores = new();
        foreach (PlayerNetwork player in GetPlayerObjects())
        {
            scores.Add(new PlayerScore
            {
                Nickname = string.IsNullOrWhiteSpace(player.Nickname.Value) ? $"Player_{player.OwnerIdAsUlong}" : player.Nickname.Value,
                Score = player.Score.Value
            });
        }

        scores.Sort((left, right) =>
        {
            int scoreCompare = right.Score.CompareTo(left.Score);
            return scoreCompare != 0 ? scoreCompare : string.CompareOrdinal(left.Nickname, right.Nickname);
        });

        if (scores.Count == 0)
        {
            return "No players";
        }

        StringBuilder builder = new();
        for (int i = 0; i < scores.Count; i++)
        {
            PlayerScore score = scores[i];
            if (includePositions)
            {
                builder.Append(i + 1);
                builder.Append(". ");
            }

            builder.Append(score.Nickname);
            builder.Append(" - ");
            builder.Append(score.Score);
            if (i < scores.Count - 1)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private void BroadcastSnapshot(bool force)
    {
        if (_networkManager == null || !_networkManager.IsServerStarted)
        {
            return;
        }

        if (!force && Time.unscaledTime < _nextSnapshotTime)
        {
            return;
        }

        _nextSnapshotTime = Time.unscaledTime + Mathf.Max(0.05f, _snapshotInterval);
        MatchSnapshotBroadcast snapshot = new()
        {
            State = CurrentState,
            ConnectedPlayers = ConnectedPlayers,
            RequiredPlayers = RequiredPlayers,
            SessionControllerClientId = SessionControllerClientId,
            MatchTimeRemaining = MatchTimeRemaining,
            MatchEndTime = CurrentState == MatchState.InProgress ? _matchEndTime : 0d,
            MatchDuration = _matchDuration,
            ScoreToWin = ScoreToWin,
            CanStartMatch = CanStartMatch,
            ScoreboardText = ScoreboardText ?? string.Empty,
            ResultsText = ResultsText ?? string.Empty
        };

        ApplySnapshot(snapshot);
        _networkManager.ServerManager.Broadcast(snapshot, true, Channel.Reliable);
    }

    private void ApplySnapshot(MatchSnapshotBroadcast snapshot)
    {
        CurrentState = snapshot.State;
        ConnectedPlayers = snapshot.ConnectedPlayers;
        _requiredPlayers = Mathf.Max(1, snapshot.RequiredPlayers);
        SessionControllerClientId = snapshot.SessionControllerClientId;
        _matchDuration = Mathf.Clamp(snapshot.MatchDuration <= 0f ? _matchDuration : snapshot.MatchDuration, 10f, 3600f);
        _scoreToWin = Mathf.Clamp(snapshot.ScoreToWin <= 0 ? _scoreToWin : snapshot.ScoreToWin, 1, 999);
        CanStartMatch = snapshot.CanStartMatch;
        _matchEndTime = snapshot.MatchEndTime;
        MatchTimeRemaining = snapshot.MatchTimeRemaining;
        ScoreboardText = snapshot.ScoreboardText ?? string.Empty;
        ResultsText = snapshot.ResultsText ?? string.Empty;
        SnapshotChanged?.Invoke();
    }

    private float CalculateMatchTimeRemaining()
    {
        if (CurrentState == MatchState.InProgress && _matchEndTime > 0d)
        {
            return Mathf.Max(0f, (float)(_matchEndTime - GetNetworkTimeSeconds()));
        }

        return _matchTimeRemaining;
    }

    private double GetNetworkTimeSeconds()
    {
        var timeManager = _networkManager != null ? _networkManager.TimeManager : InstanceFinder.TimeManager;
        return timeManager != null ? timeManager.TicksToTime(timeManager.Tick) : Time.unscaledTimeAsDouble;
    }

    private bool IsNetworkSessionStarted()
    {
        NetworkManager networkManager = _networkManager != null ? _networkManager : InstanceFinder.NetworkManager;
        return networkManager != null && (networkManager.IsClientStarted || networkManager.IsServerStarted);
    }

    private struct PlayerScore
    {
        public string Nickname;
        public int Score;
    }
}
