using FishNet.Object;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    [Tooltip("Horizontal move speed in units/second.")]
    [SerializeField] public float moveSpeed = 3f;

    [Tooltip("Upward jump velocity applied when jumping.")]
    [SerializeField] private float jumpVelocity = 6f;

    [Tooltip("Optional override for Rigidbody2D.gravityScale on Awake. Set <= 0 to leave as-is.")]
    [SerializeField] private float gravityScale = 1.5f;

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

    [Header("Arm Aim")]
    [Tooltip("Child transform of the player which should rotate on Z to aim at the mouse.")]
    [SerializeField] private Transform arm;
    [Tooltip("If true, arm aiming is only processed for the local owner (recommended).")]
    [SerializeField] private bool aimOnlyForOwner = true;
    [Tooltip("Angle offset in degrees to compensate sprite art forward direction (0 means sprite points to +X).")]
    [SerializeField] private float armAngleOffset = 0f;
    [Tooltip("Optional hand tip Transform (end of the hand/weapon). Used for debug/visualization of reach.")]
    [SerializeField] private Transform handTip;
    [Tooltip("If true and a handTip is set, we visualize the reachable circle so you can align the tip to cursor the closest possible (Option C).")]
    [SerializeField] private bool visualizeReachCircle = true;
    [Tooltip("Minimum angle change (degrees) before syncing to other clients.")]
    [SerializeField] private float armSyncMinDelta = 1.5f;
    [Tooltip("Maximum send rate for arm angle updates (messages per second). Set to 0 to disable rate limit.")]
    [SerializeField] private float armSyncMaxRate = 20f;

    [Header("Facing")]
    [Tooltip("Sprite to flip horizontally when facing left/right. If not set, we will flip the root transform scale instead.")]
    [SerializeField] private SpriteRenderer spriteToFlip;
    [Tooltip("When true, we also predict facing locally from input so the flip feels immediate for the owner.")]
    [SerializeField] private bool predictFacingLocally = true;

    private Rigidbody2D _rb;

    private float _clMoveInput;
    private bool _clJumpPressed;

    private float _svMoveInput;
    private bool _svJumpQueued;

    private bool _isGrounded;

    // 1 = facing right, -1 = facing left. Managed manually (FishNet v4: no SyncVar).
    private int _facing = 1;

    // Arm aim networking cache.
    private float _currentArmAngleDeg;
    private float _lastSentArmAngleDeg;
    private float _lastArmSendTime;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        if (gravityScale > 0f)
            _rb.gravityScale = gravityScale;

        _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        _rb.freezeRotation = true; 
        // Make sure initial facing is applied even before networking starts (useful in single-player/testing)
        ApplyFacing();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        if (_rb != null)
            _rb.simulated = IsServer;

        // Ensure visuals are in sync with current facing on spawn.
        ApplyFacing();

        // Seed facing state for observers (FishNet v4, no SyncVar). Buffer the last value so late joiners get it.
        if (IsServer)
            RpcFacingChanged(_facing);

        // Also seed arm angle for observers so late joiners get initial aim.
        if (IsServer && arm != null)
        {
            float z = arm.eulerAngles.z;
            _currentArmAngleDeg = z;
            RpcArmAngleChanged(z);
        }
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        if (_rb != null)
            _rb.simulated = false;
    }

    private void Update()
    {
        // Only the owning client should handle local input.
        if (!IsOwner)
        {
            // For non-owners, if we have a replicated arm angle, make sure it's applied locally.
            if (arm != null && aimOnlyForOwner)
            {
                // _currentArmAngleDeg already contains offset.
                arm.rotation = Quaternion.Euler(0f, 0f, _currentArmAngleDeg);
            }
            return;
        }

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
        // Send movement/jump to server for authoritative movement.
        SendInputServerRpc(_clMoveInput, _clJumpPressed);
        _clJumpPressed = false;

        // Optional local prediction so the flip happens instantly for the owner.
        if (predictFacingLocally)
        {
            if (_clMoveInput > 0.001f && _facing != 1)
            {
                _facing = 1; // local-only change; server will correct via SyncVar if different
                ApplyFacing();
            }
            else if (_clMoveInput < -0.001f && _facing != -1)
            {
                _facing = -1;
                ApplyFacing();
            }
        }

        // Rotate the arm (child) around Z to face the mouse position.
        if (arm != null && (!aimOnlyForOwner || IsOwner))
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 mouseScreen =
#if ENABLE_INPUT_SYSTEM
                    (Mouse.current != null) ? (Vector3)Mouse.current.position.ReadValue() : Input.mousePosition;
#else
                    Input.mousePosition;
#endif
                Vector3 mouseWorld = cam.ScreenToWorldPoint(mouseScreen);
                // Force Z to match the arm plane to avoid slight parallax/off-plane errors.
                mouseWorld.z = arm.position.z;

                Vector2 dir = (Vector2)(mouseWorld - arm.position);
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + armAngleOffset;
                arm.rotation = Quaternion.Euler(0f, 0f, angle);
                _currentArmAngleDeg = angle;

                // Send to server so it can relay to observers, with simple threshold and rate limiting.
                TrySendArmAngle(angle);
            }
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer)
            return;

        UpdateGrounded();

        var vel = _rb.linearVelocity;
        vel.x = _svMoveInput * moveSpeed;
        _rb.linearVelocity = vel;

        // Update facing from last non-zero move input so the character looks in the last used direction.
        if (_svMoveInput > 0.001f && _facing != 1)
        {
            SetFacingServer(1);
        }
        else if (_svMoveInput < -0.001f && _facing != -1)
        {
            SetFacingServer(-1);
        }

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
        Gizmos.DrawWireSphere(new Vector3(worldPos.x, worldPos.y, 0f), groundCheckRadius);

        // Visualize the reachable circle for the hand tip (Option C) to help align art and expectations.
        if (visualizeReachCircle && arm != null && handTip != null)
        {
            Gizmos.color = new Color(1f, 0.64f, 0f, 0.9f); // orange
            float radius = Vector2.Distance(arm.position, handTip.position);
            // Draw a simple approximation of a circle in the XY plane.
            const int seg = 48;
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= seg; i++)
            {
                float t = (float)i / seg * Mathf.PI * 2f;
                Vector3 p = arm.position + new Vector3(Mathf.Cos(t) * radius, Mathf.Sin(t) * radius, 0f);
                if (i > 0) Gizmos.DrawLine(prev, p);
                prev = p;
            }

            // Also draw a line from the arm to the projected point on the circle closest to the mouse.
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 mouseScreen =
#if ENABLE_INPUT_SYSTEM
                    (Mouse.current != null) ? (Vector3)Mouse.current.position.ReadValue() : Input.mousePosition;
#else
                    Input.mousePosition;
#endif
                Vector3 mouseWorld = cam.ScreenToWorldPoint(mouseScreen);
                mouseWorld.z = arm.position.z;
                Vector3 dir3 = (mouseWorld - arm.position).normalized;
                Vector3 projected = arm.position + dir3 * radius;
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(arm.position, projected);
                Gizmos.DrawSphere(projected, 0.03f);
            }
        }
    }

    private void ApplyFacing()
    {
        int dir = (_facing >= 0) ? 1 : -1;
        if (spriteToFlip != null)
        {
            spriteToFlip.flipX = (dir < 0);
        }
        else
        {
            // Fallback: flip the local scale on X
            Vector3 s = transform.localScale;
            s.x = Mathf.Abs(s.x) * dir;
            transform.localScale = s;
        }
    }

    // -------------------- Facing Networking (FishNet v4 compatible) --------------------
    // Server-only: set and broadcast to observers when the authoritative facing changes.
    private void SetFacingServer(int dir)
    {
        if (!IsServer)
            return;
        dir = (dir >= 0) ? 1 : -1;
        if (_facing == dir)
            return;
        _facing = dir;
        RpcFacingChanged(dir);
        // Apply on server instance too (host visuals)
        ApplyFacing();
    }

    // Sent to all observing clients (including owner) when facing changes.
    [ObserversRpc(BufferLast = true)]
    private void RpcFacingChanged(int dir)
    {
        _facing = (dir >= 0) ? 1 : -1;
        ApplyFacing();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // We rely on RpcFacingChanged buffered to observers for initial state.
        // ApplyFacing() was already called in Awake/OnStartNetwork for local instance.
    }

    // -------------------- Arm Aim Networking --------------------
    private void TrySendArmAngle(float angle)
    {
        // Only the owner should request a relay to others.
        if (!IsOwner)
            return;

        // Threshold check to avoid spamming tiny changes.
        if (Mathf.Abs(Mathf.DeltaAngle(_lastSentArmAngleDeg, angle)) < armSyncMinDelta)
        {
            // Still allow periodic refresh to fight drift if a max rate is set.
            float minInterval = (armSyncMaxRate > 0f) ? (1f / armSyncMaxRate) : 0f;
            if (minInterval <= 0f || Time.unscaledTime - _lastArmSendTime < minInterval)
                return;
        }

        _lastSentArmAngleDeg = angle;
        _lastArmSendTime = Time.unscaledTime;
        SendArmAngleServerRpc(angle);
    }

    [ServerRpc]
    private void SendArmAngleServerRpc(float angle)
    {
        // Cache on server (host visuals may use it if desired beyond this script).
        _currentArmAngleDeg = angle;
        // Relay to observers; owner excluded to prevent double-application.
        RpcArmAngleChanged(angle);
    }

    [ObserversRpc(BufferLast = true, ExcludeOwner = true)]
    private void RpcArmAngleChanged(float angle)
    {
        _currentArmAngleDeg = angle;
        if (arm != null)
            arm.rotation = Quaternion.Euler(0f, 0f, angle);
    }
}
