using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Rigidbody2D))]
public class TriangleController2D : NetworkBehaviour
{
    [Header("Movement")]
    [Tooltip("Horizontal move speed in units/second.")]
    [SerializeField] private float moveSpeed = 6f;

    [Tooltip("Upward jump velocity applied when jumping.")]
    [SerializeField] private float jumpVelocity = 12f;

    [Tooltip("Optional override for Rigidbody2D.gravityScale on Awake. Set <= 0 to leave as-is.")]
    [SerializeField] private float gravityScale = 3f;

    [Header("Ground Check")]
    [Tooltip("Layers considered as ground.")]
    [SerializeField] private LayerMask groundLayers = ~0;

    [Tooltip("Position (local) of the ground check circle under the character.")]
    [SerializeField] private Vector2 groundCheckLocalOffset = new Vector2(0f, -0.6f);

    [Tooltip("Radius of the ground check circle.")]
    [SerializeField] private float groundCheckRadius = 0.15f;

    [Header("Input")]
    [Tooltip("Horizontal axis name (Input Manager).")]
    [SerializeField] private string horizontalAxis = "Horizontal";

    [Tooltip("Jump button name (Input Manager).")]
    [SerializeField] private string jumpButton = "Jump";

    private Rigidbody2D _rb;

    private float _clMoveInput;
    private bool _clJumpPressed;

    private float _svMoveInput;
    private bool _svJumpQueued;

    private bool _isGrounded;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        if (gravityScale > 0f)
            _rb.gravityScale = gravityScale;

        _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        _rb.freezeRotation = true; 
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        if (_rb != null)
            _rb.simulated = IsServer;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        if (_rb != null)
            _rb.simulated = false;
    }

    private void Update()
    {
        if (!IsOwner)
            return;

#if ENABLE_INPUT_SYSTEM
        float axis = 0f;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
                axis -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
                axis += 1f;
        }
        if (Gamepad.current != null)
        {
            float gAxis = Gamepad.current.leftStick.ReadValue().x;
            if (Mathf.Abs(gAxis) > Mathf.Abs(axis))
                axis = gAxis;
        }
        _clMoveInput = Mathf.Clamp(axis, -1f, 1f);
        bool jumpPressed = (Keyboard.current?.spaceKey.wasPressedThisFrame ?? false) || (Gamepad.current?.buttonSouth.wasPressedThisFrame ?? false);
        if (jumpPressed)
            _clJumpPressed = true;
#else
        _clMoveInput = Input.GetAxisRaw(horizontalAxis);
        if (Input.GetButtonDown(jumpButton))
            _clJumpPressed = true;
#endif
        SendInputServerRpc(_clMoveInput, _clJumpPressed);
        _clJumpPressed = false;
    }

    private void FixedUpdate()
    {
        if (!IsServer)
            return;

        UpdateGrounded();

        var vel = _rb.linearVelocity;
        vel.x = _svMoveInput * moveSpeed;
        _rb.linearVelocity = vel;

        if (_svJumpQueued)
        {
            _svJumpQueued = false;
            if (_isGrounded)
            {
                vel = _rb.linearVelocity;
                vel.y = jumpVelocity;
                _rb.linearVelocity = vel;
            }
        }
    }

    [ServerRpc]
    private void SendInputServerRpc(float moveInput, bool jumpPressed)
    {
        _svMoveInput = Mathf.Clamp(moveInput, -1f, 1f);
        if (jumpPressed)
            _svJumpQueued = true;
    }

    private void UpdateGrounded()
    {
        Vector2 worldPos = (Vector2)transform.position + groundCheckLocalOffset;
        _isGrounded = Physics2D.OverlapCircle(worldPos, groundCheckRadius, groundLayers) is not null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = _isGrounded ? Color.green : Color.red;
        Vector2 worldPos = (Application.isPlaying ? (Vector2)transform.position : (Vector2)transform.position) + groundCheckLocalOffset;
        Gizmos.DrawWireSphere(worldPos, groundCheckRadius);
    }
}
