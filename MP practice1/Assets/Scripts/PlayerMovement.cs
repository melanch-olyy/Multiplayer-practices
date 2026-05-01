using FishNet.Component.Transforming;
using FishNet.Connection;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerNetwork))]
public class PlayerMovement : TickNetworkBehaviour
{
    public struct MoveData : IReplicateData
    {
        public MoveData(float horizontal, float vertical)
        {
            Horizontal = horizontal;
            Vertical = vertical;
            _tick = 0;
        }

        public float Horizontal;
        public float Vertical;
        private uint _tick;

        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
        public void Dispose() { }
    }

    public struct ReconcileData : IReconcileData
    {
        public ReconcileData(Vector3 position, Quaternion rotation, Vector3 planarVelocity, float verticalVelocity, bool forceSnap)
        {
            Position = position;
            Rotation = rotation;
            PlanarVelocity = planarVelocity;
            VerticalVelocity = verticalVelocity;
            ForceSnap = forceSnap;
            _tick = 0;
        }

        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 PlanarVelocity;
        public float VerticalVelocity;
        public bool ForceSnap;
        private uint _tick;

        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
        public void Dispose() { }
    }

    [SerializeField] private float _speed = 5f;
    [SerializeField] private float _gravity = -9.81f;
    [SerializeField] private float _rotationSpeed = 18f;
    [SerializeField] private float _acceleration = 20f;
    [SerializeField] private float _deceleration = 24f;
    [SerializeField] private float _maxFallSpeed = 40f;
    [SerializeField] private uint _observerFuturePredictionTicks = 1;
    [Header("Observer smoothing")]
    [SerializeField] private bool _smoothObservers = true;
    [SerializeField] private float _observerPositionSmoothing = 18f;
    [SerializeField] private float _observerRotationSmoothing = 18f;
    [SerializeField] private float _observerSnapDistance = 3f;

    private CharacterController _characterController;
    private NetworkTransform _networkTransform;
    private PlayerNetwork _playerNetwork;
    private Vector2 _cachedInput;
    private MoveData _lastTickedMoveData;
    private Vector3 _planarVelocity;
    private float _verticalVelocity;
    private bool _hasObserverTarget;
    private Vector3 _observerTargetPosition;
    private Quaternion _observerTargetRotation;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _networkTransform = GetComponent<NetworkTransform>();
        _playerNetwork = GetComponent<PlayerNetwork>();
        SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
    }

    public override void OnOwnershipClient(NetworkConnection prevOwner)
    {
        UpdateControllerState();
    }

    public override void OnStartClient()
    {
        UpdateControllerState();
    }

    public override void OnStartServer()
    {
        UpdateControllerState();
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        if (_playerNetwork == null || !_playerNetwork.IsAlive.Value)
        {
            _cachedInput = Vector2.zero;
            return;
        }

        if (!GameSessionManager.IsGameplayActive)
        {
            _cachedInput = Vector2.zero;
            return;
        }

        _cachedInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
    }

    private void LateUpdate()
    {
        SmoothObserverTransform();
    }

    protected override void TimeManager_OnTick()
    {
        if (!IsSpawned)
        {
            return;
        }

        if (ShouldLetNetworkTransformMoveObserver())
        {
            _planarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            return;
        }

        PerformReplicate(BuildMoveData());
    }

    protected override void TimeManager_OnPostTick()
    {
        CreateReconcile();
    }

    public override void CreateReconcile()
    {
        if (!IsSpawned || !IsServerStarted)
        {
            return;
        }

        PerformReconcile(new ReconcileData(transform.position, transform.rotation, _planarVelocity, _verticalVelocity, false));
    }

    public void SetPredictedState(Vector3 position, Quaternion rotation, float verticalVelocity)
    {
        _verticalVelocity = verticalVelocity;
        _planarVelocity = Vector3.zero;
        _hasObserverTarget = false;
        ApplyTransformState(position, rotation);

        if (IsSpawned)
        {
            PerformReconcile(new ReconcileData(position, rotation, _planarVelocity, _verticalVelocity, true));
        }
    }

    private MoveData BuildMoveData()
    {
        if (!IsOwner)
        {
            return default;
        }

        return new MoveData(_cachedInput.x, _cachedInput.y);
    }

    [Replicate]
    private void PerformReplicate(MoveData data, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
    {
        if (ShouldLetNetworkTransformMoveObserver())
        {
            _planarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            return;
        }

        if (_playerNetwork == null || !_playerNetwork.IsAlive.Value || !GameSessionManager.IsGameplayActive)
        {
            _verticalVelocity = 0f;
            _planarVelocity = Vector3.zero;
            return;
        }

        data = ResolveObserverMoveData(data, state, out bool useDefaultMove);

        float delta = TimeManager != null ? (float)TimeManager.TickDelta : Time.fixedDeltaTime;
        Vector3 inputDirection = useDefaultMove ? Vector3.zero : new Vector3(data.Horizontal, 0f, data.Vertical);
        if (inputDirection.sqrMagnitude > 1f)
        {
            inputDirection.Normalize();
        }

        Vector3 targetPlanarVelocity = inputDirection * _speed;
        float velocityChangeRate = targetPlanarVelocity.sqrMagnitude > _planarVelocity.sqrMagnitude ? _acceleration : _deceleration;
        _planarVelocity = Vector3.MoveTowards(_planarVelocity, targetPlanarVelocity, velocityChangeRate * delta);

        if (_planarVelocity.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(_planarVelocity.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, _rotationSpeed * delta);
        }

        bool canUseCharacterController = _characterController != null && _characterController.enabled;
        if (canUseCharacterController && _characterController.isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -1f;
        }

        _verticalVelocity += _gravity * delta;
        _verticalVelocity = Mathf.Max(_verticalVelocity, -Mathf.Abs(_maxFallSpeed));

        Vector3 velocity = _planarVelocity;
        velocity.y = _verticalVelocity;
        if (useDefaultMove && velocity.sqrMagnitude < 0.001f)
        {
            velocity.y = -1f;
        }

        if (canUseCharacterController)
        {
            _characterController.Move(velocity * delta);
        }
        else
        {
            transform.position += velocity * delta;
        }
    }

    [Reconcile]
    private void PerformReconcile(ReconcileData data, Channel channel = Channel.Unreliable)
    {
        if (ShouldLetNetworkTransformMoveObserver() && !data.ForceSnap)
        {
            _planarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _hasObserverTarget = false;
            return;
        }

        _planarVelocity = data.PlanarVelocity;
        _verticalVelocity = data.VerticalVelocity;

        if (ShouldSmoothObserverTransform() && !data.ForceSnap)
        {
            SetObserverTarget(data.Position, data.Rotation);
            return;
        }

        _hasObserverTarget = false;
        ApplyTransformState(data.Position, data.Rotation);
    }

    private MoveData ResolveObserverMoveData(MoveData data, ReplicateState state, out bool useDefaultMove)
    {
        useDefaultMove = false;
        if (IsServerStarted || IsOwner)
        {
            return data;
        }

        if (state.ContainsTicked() && state.ContainsCreated())
        {
            _lastTickedMoveData = data;
            return data;
        }

        if (!state.IsFuture())
        {
            return data;
        }

        uint lastTick = _lastTickedMoveData.GetTick();
        uint currentTick = data.GetTick();
        if (lastTick != 0u && currentTick >= lastTick && currentTick - lastTick <= _observerFuturePredictionTicks)
        {
            return _lastTickedMoveData;
        }

        useDefaultMove = true;
        return data;
    }

    private void ApplyTransformState(Vector3 position, Quaternion rotation)
    {
        bool wasEnabled = _characterController != null && _characterController.enabled;
        if (wasEnabled)
        {
            _characterController.enabled = false;
        }

        transform.SetPositionAndRotation(position, rotation);

        if (wasEnabled)
        {
            _characterController.enabled = true;
        }
    }

    private void SetObserverTarget(Vector3 position, Quaternion rotation)
    {
        float snapDistanceSqr = _observerSnapDistance * _observerSnapDistance;
        if (!_hasObserverTarget || (transform.position - position).sqrMagnitude > snapDistanceSqr)
        {
            _observerTargetPosition = position;
            _observerTargetRotation = rotation;
            _hasObserverTarget = true;
            ApplyTransformState(position, rotation);
            return;
        }

        _observerTargetPosition = position;
        _observerTargetRotation = rotation;
        _hasObserverTarget = true;
    }

    private void SmoothObserverTransform()
    {
        if (!ShouldSmoothObserverTransform() || !_hasObserverTarget)
        {
            return;
        }

        float delta = Time.deltaTime;
        float positionT = 1f - Mathf.Exp(-Mathf.Max(0.01f, _observerPositionSmoothing) * delta);
        float rotationT = 1f - Mathf.Exp(-Mathf.Max(0.01f, _observerRotationSmoothing) * delta);

        Vector3 position = Vector3.Lerp(transform.position, _observerTargetPosition, positionT);
        Quaternion rotation = Quaternion.Slerp(transform.rotation, _observerTargetRotation, rotationT);
        ApplyTransformState(position, rotation);

        if ((transform.position - _observerTargetPosition).sqrMagnitude <= 0.0001f
            && Quaternion.Angle(transform.rotation, _observerTargetRotation) <= 0.1f)
        {
            ApplyTransformState(_observerTargetPosition, _observerTargetRotation);
        }
    }

    private bool ShouldSmoothObserverTransform()
    {
        return _smoothObservers && IsSpawned && !IsOwner && !IsServerStarted && !ShouldLetNetworkTransformMoveObserver();
    }

    private bool ShouldLetNetworkTransformMoveObserver()
    {
        return IsSpawned && !IsOwner && !IsServerStarted && _networkTransform != null && _networkTransform.enabled;
    }

    private void UpdateControllerState()
    {
        if (_characterController == null || _playerNetwork == null)
        {
            return;
        }

        bool shouldEnableController = IsSpawned
            && _playerNetwork.IsAlive.Value
            && (IsOwner || IsServerStarted || !ShouldLetNetworkTransformMoveObserver());
        _characterController.enabled = shouldEnableController;
    }
}
