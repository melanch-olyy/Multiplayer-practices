using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerNetwork))]
public class PlayerMovement : NetworkBehaviour
{
    [SerializeField] private float _speed = 5f;
    [SerializeField] private float _gravity = -9.81f;
    [SerializeField] private float _rotationSpeed = 18f;

    private CharacterController _characterController;
    private PlayerNetwork _playerNetwork;
    private float _verticalVelocity;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _playerNetwork = GetComponent<PlayerNetwork>();
    }

    public override void OnNetworkSpawn()
    {
        if (_characterController != null && (IsOwner || IsServer))
        {
            _characterController.enabled = true;
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner || _playerNetwork == null)
        {
            return;
        }

        if (!_playerNetwork.IsAlive.Value)
        {
            _verticalVelocity = 0f;
            return;
        }

        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        Vector3 planarMove = new Vector3(horizontal, 0f, vertical);
        if (planarMove.sqrMagnitude > 1f)
        {
            planarMove.Normalize();
        }

        if (planarMove.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(planarMove, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, _rotationSpeed * Time.deltaTime);
        }

        bool canUseCharacterController = _characterController != null && _characterController.enabled;
        if (canUseCharacterController && _characterController.isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -1f;
        }

        _verticalVelocity += _gravity * Time.deltaTime;

        Vector3 velocity = planarMove * _speed;
        velocity.y = _verticalVelocity;

        if (canUseCharacterController)
        {
            _characterController.Move(velocity * Time.deltaTime);
        }
        else
        {
            transform.position += velocity * Time.deltaTime;
            _verticalVelocity = 0f;
        }
    }
}
