using FishNet.Object;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Simple 2D movement + jump controller for a triangle (or any 2D character) using Rigidbody2D and gravity.
/// - Horizontal movement with configurable speed
/// - Jump with ground detection
/// - Works in single-player and with FishNet: input only processed by the object owner (HasLocalControl)
///
/// Usage:
/// 1) Add this script to your Triangle GameObject.
/// 2) Ensure it has a Rigidbody2D (Body Type: Dynamic) and a Collider2D matching your shape.
/// 3) Set Rigidbody2D.gravityScale to a value you like, or edit it from the inspector here.
/// 4) Assign Ground Layers to what should be considered "ground".
/// 5) (Networking) Make sure the object has a NetworkObject and, ideally, a NetworkTransform to replicate motion.
/// </summary>
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
    private bool _jumpQueued;
    private float _moveInput;
    private bool _isGrounded;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        if (gravityScale > 0f)
            _rb.gravityScale = gravityScale;

        // Recommended Rigidbody2D settings for a platformer feel.
        _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        _rb.freezeRotation = true; // Keep triangle upright; remove if you want rotation.
    }

    private void Update()
    {
        // Only the local owner should drive input.
        if (!IsOwner)
            return;

        // Read input every frame.
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
            // Prefer the stronger input (analog can be between -1..1).
            if (Mathf.Abs(gAxis) > Mathf.Abs(axis))
                axis = gAxis;
        }
        _moveInput = Mathf.Clamp(axis, -1f, 1f);

        if ((Keyboard.current?.spaceKey.wasPressedThisFrame ?? false) || (Gamepad.current?.buttonSouth.wasPressedThisFrame ?? false))
            _jumpQueued = true;
#else
        _moveInput = Input.GetAxisRaw(horizontalAxis);
        if (Input.GetButtonDown(jumpButton))
            _jumpQueued = true;
#endif
    }

    private void FixedUpdate()
    {
        // Physics step
        UpdateGrounded();
        // Horizontal move: preserve vertical velocity, set horizontal based on input.
        var vel = _rb.linearVelocity;
        vel.x = _moveInput * moveSpeed;
        _rb.linearVelocity = vel;

        // Handle jump
        if (_jumpQueued)
        {
            _jumpQueued = false;
            if (_isGrounded)
            {
                // Reset vertical velocity and apply jump.
                vel = _rb.linearVelocity;
                vel.y = jumpVelocity;
                _rb.linearVelocity = vel;
            }
        }
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
