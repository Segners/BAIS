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
    [Tooltip("Offset local appliqué au bras quand le joueur est accroupi (permet de baisser légèrement le bras).")]
    [SerializeField] private Vector2 armCrouchLocalOffset = new Vector2(0f, -0.12f);
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

    [Header("Shooting")]
    [Tooltip("Prefab NetworkObject du projectile (doit contenir NetworkObject + BulletProjectile + Collider2D + Rigidbody2D optionnel).")]
    [SerializeField] private NetworkObject bulletPrefab;
    [Tooltip("Transform du point de tir (muzzle). Par défaut on utilise handTip si non renseigné.")]
    [SerializeField] private Transform muzzle;
    [Tooltip("Cadence de tir (balles par seconde). 0 = sans limite.")]
    [SerializeField] private float fireRate = 6f;
    [Tooltip("Vitesse du projectile en unités/seconde.")]
    [SerializeField] private float bulletSpeed = 14f;
    [Tooltip("Si true, on utilise bulletSpeed pour forcer la vitesse du projectile; sinon on laisse le prefab du projectile décider.")]
    [SerializeField] private bool overrideBulletSpeed = false;
    [Tooltip("Décalage d'angle (degrés) appliqué UNIQUEMENT au projectile pour aligner l'art du bullet si nécessaire. Laissez 0 la plupart du temps.")]
    [SerializeField] private float bulletAngleOffset = 0f;
    [Tooltip("Durée de vie du projectile (secondes). 0 ou négatif = infini (déconseillé).")]
    [SerializeField] private float bulletLifetime = 3f;
    [Tooltip("Si true, on utilise bulletLifetime pour forcer la durée de vie; sinon on laisse le prefab du projectile décider.")]
    [SerializeField] private bool overrideBulletLifetime = false;
    [Tooltip("Décalage supplémentaire depuis le muzzle dans la direction de tir (unités). Permet de faire partir la balle légèrement devant le canon.")]
    [SerializeField] private float muzzleForwardOffset = 0f;

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
    private float _nextFireTime;
    private float _svNextFireTime; // sécurité cadence côté serveur
    
    // Arm base local position, used to offset while crouching
    private Vector3 _armBaseLocalPos;
    
    [Header("Health")]
    [Tooltip("Nombre de projectiles nécessaires pour éliminer le joueur.")]
    [SerializeField] private int hitsToEliminate = 3;
    private int _currentHits;
    private bool _isEliminated;

    [Header("Crouch")]
    [Tooltip("Maintenir pour s'accroupir. Multiplie la vitesse horizontale par ce facteur.")]
    [SerializeField] private float crouchSpeedMultiplier = 0.5f;
    [Tooltip("Empêche de sauter lorsqu'on est accroupi.")]
    [SerializeField] private bool crouchDisablesJump = true;
    [Tooltip("Animator (optionnel) pour piloter l'animation d'accroupissement.")]
    [SerializeField] private Animator animator;
    [Tooltip("Nom du booléen Animator qui reflète l'état accroupi. Laisser vide pour ne pas l'utiliser.")]
    [SerializeField] private string crouchAnimatorBool = "IsCrouching";
    [Tooltip("Nom de l'état/clip à jouer lors de l'accroupissement (ex: 'sitdown1'). Laisser vide pour ignorer.")]
    [SerializeField] private string crouchStateName = "sitdown1";
    [Tooltip("Nom du paramètre float Animator pour la vitesse de course (absolue). Laisser vide pour ne pas l'utiliser.")]
    [SerializeField] private string runSpeedAnimatorFloat = "Speed";
    [Tooltip("Nom du paramètre bool Animator pour l'état au sol. Laisser vide pour ne pas l'utiliser.")]
    [SerializeField] private string groundedAnimatorBool = "IsGrounded";
    [Tooltip("Ajuster le collider pendant l'accroupissement pour éviter que le personnage flotte.")]
    [SerializeField] private bool adjustColliderOnCrouch = true;
    [Tooltip("Référence au collider principal à ajuster (si vide, auto: Capsule2D puis Box2D).")]
    [SerializeField] private Collider2D mainCollider;
    [Tooltip("Facteur appliqué à la hauteur du collider en crouch (ex: 0.7 = 70% de la hauteur).")]
    [Range(0.3f, 1f)]
    [SerializeField] private float crouchHeightMultiplier = 0.7f;
    [Tooltip("Petite marge pour vérifier l'espace lors de la relève.")]
    [SerializeField] private float standUpCeilingCheckPadding = 0.02f;

    // Client -> Server inputs
    private bool _clCrouchHeld;
    // Authoritative state on server (also cached on clients via RPC)
    private bool _svCrouchHeld;

    // Cache des paramètres initiaux du collider
    private CapsuleCollider2D _capsule;
    private BoxCollider2D _box;
    private Vector2 _origCapsuleSize;
    private Vector2 _origCapsuleOffset;
    private Vector2 _origBoxSize;
    private Vector2 _origBoxOffset;

    // Cache d'animation locomotion (serveur → observateurs)
    private float _lastSentRunSpeed;
    private bool _lastSentGrounded;
    private float _lastLocomotionSendTime;

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

        // Cache le point de base du bras pour pouvoir l'abaisser en crouch
        if (arm != null)
            _armBaseLocalPos = arm.localPosition;

        // Auto-détection du collider principal
        if (mainCollider == null)
        {
            TryGetComponent(out _capsule);
            if (_capsule != null)
                mainCollider = _capsule;
            else
            {
                TryGetComponent(out _box);
                if (_box != null)
                    mainCollider = _box;
            }
        }
        else
        {
            _capsule = mainCollider as CapsuleCollider2D;
            _box = mainCollider as BoxCollider2D;
        }

        if (_capsule != null)
        {
            _origCapsuleSize = _capsule.size;
            _origCapsuleOffset = _capsule.offset;
        }
        if (_box != null)
        {
            _origBoxSize = _box.size;
            _origBoxOffset = _box.offset;
        }
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        if (_rb != null)
            _rb.simulated = IsServerInitialized;

        // Ensure visuals are in sync with current facing on spawn.
        ApplyFacing();

        // Seed facing state for observers (FishNet v4, no SyncVar). Buffer the last value so late joiners get it.
        if (IsServerInitialized)
            RpcFacingChanged(_facing);

        // Also seed arm angle for observers so late joiners get initial aim.
        if (IsServerInitialized && arm != null)
        {
            float z = arm.eulerAngles.z;
            _currentArmAngleDeg = z;
            RpcArmAngleChanged(z);
        }

        // Seed crouch state so tous les observateurs (late joiners) reçoivent l'état actuel
        if (IsServerInitialized)
        {
            RpcCrouchChanged(_svCrouchHeld);
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

        // Si éliminé: pas d'input envoyé (immobilisé jusqu'à la prochaine manche)
        if (_isEliminated)
        {
            // On laisse éventuellement le bras dans sa dernière orientation visuelle, mais on n'envoie rien.
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
        // Désactivation du crouch: on force l'état client à false
        _clCrouchHeld = false;
#else
        _clMoveInput = Input.GetAxisRaw(horizontalAxis);
        if (Input.GetButtonDown(jumpButton))
            _clJumpPressed = true;
        // Désactivation du crouch: on force l'état client à false
        _clCrouchHeld = false;
#endif
        // Send movement/jump to server for authoritative movement.
        SendInputServerRpc(_clMoveInput, _clJumpPressed, _clCrouchHeld);
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

        // Mise à jour visuelle locale de la locomotion pour l'owner (réactivité immédiate)
        if (animator != null)
        {
            float mul = _clCrouchHeld ? Mathf.Clamp01(crouchSpeedMultiplier) : 1f;
            float localSpeed = Mathf.Abs(_clMoveInput) * moveSpeed * mul;
            // Estimation locale du grounded (visuelle uniquement)
            Vector2 worldPos = (Vector2)transform.position + groundCheckLocalOffset;
            bool localGrounded = Physics2D.OverlapCircle(worldPos, groundCheckRadius, groundLayers) is not null;
            ApplyLocomotionVisual(localSpeed, localGrounded);
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

                // 1) Angle visuel du bras (pivot = arm), avec éventuel offset pour compenser l'art.
                Vector2 armDir = (Vector2)(mouseWorld - arm.position);
                float armAngle = Mathf.Atan2(armDir.y, armDir.x) * Mathf.Rad2Deg + armAngleOffset;
                arm.rotation = Quaternion.Euler(0f, 0f, armAngle);
                _currentArmAngleDeg = armAngle;

                // Send to server so it can relay to observers, with simple threshold and rate limiting.
                TrySendArmAngle(armAngle);

                // Tir (client owner uniquement) : click gauche souris / bouton RT manette
#if ENABLE_INPUT_SYSTEM
                bool fire = (Mouse.current?.leftButton.wasPressedThisFrame ?? false) || (Gamepad.current?.rightTrigger.wasPressedThisFrame ?? false);
#else
                bool fire = Input.GetMouseButtonDown(0);
#endif
                if (fire)
                {
                    // 2) Angle de tir basé sur le point de tir réel (muzzle si présent), sans appliquer l'offset du bras.
                    Transform m = GetMuzzle();
                    Vector3 src = (m != null) ? m.position : arm.position;
                    Vector2 shotDir = (Vector2)(mouseWorld - src);
                    float bulletAngle = Mathf.Atan2(shotDir.y, shotDir.x) * Mathf.Rad2Deg + bulletAngleOffset;
                    TryFire(bulletAngle);
                }
            }
        }
    }

    private void FixedUpdate()
    {
        if (!IsServerInitialized)
            return;

        // Si éliminé côté serveur, on fige les déplacements horizontaux et ignore les sauts.
        if (_isEliminated)
        {
            var vel0 = _rb.linearVelocity;
            vel0.x = 0f;
            _rb.linearVelocity = vel0;
            _svMoveInput = 0f;
            _svJumpQueued = false;

            // Pas besoin de gérer le reste tant que KO.
            UpdateGrounded();
            // Màj visuelle Idle côté serveur/host et clients
            if (animator != null)
                ApplyLocomotionVisual(0f, _isGrounded);
            TrySendLocomotionToObservers(0f, _isGrounded);
            return;
        }

        UpdateGrounded();

        var vel = _rb.linearVelocity;
        float speedMul = _svCrouchHeld ? Mathf.Clamp01(crouchSpeedMultiplier) : 1f;
        vel.x = _svMoveInput * moveSpeed * speedMul;
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

        if (_svJumpQueued && !(_svCrouchHeld && crouchDisablesJump))
        {
            _svJumpQueued = false;
            if (_isGrounded)
            {
                vel = _rb.linearVelocity;
                vel.y = jumpVelocity;
                _rb.linearVelocity = vel;
            }
        }

        // Màj visuelle de locomotion côté serveur/host
        if (animator != null)
            ApplyLocomotionVisual(Mathf.Abs(_rb.linearVelocity.x), _isGrounded);

        // Diffuse l'état de locomotion (run speed + grounded) aux observateurs à un rythme limité
        TrySendLocomotionToObservers(Mathf.Abs(_rb.linearVelocity.x), _isGrounded);
    }

    [ServerRpc]
    private void SendInputServerRpc(float moveInput, bool jumpPressed, bool crouchHeld)
    {
        _svMoveInput = Mathf.Clamp(moveInput, -1f, 1f);
        if (jumpPressed)
            _svJumpQueued = true;
        // Désactivation du crouch côté serveur: ignorer toute demande et forcer à false
        SetCrouchServer(false);
    }

    private void UpdateGrounded()
    {
        Vector2 worldPos = (Vector2)transform.position + groundCheckLocalOffset;
        _isGrounded = Physics2D.OverlapCircle(worldPos, groundCheckRadius, groundLayers) is not null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = _isGrounded ? Color.green : Color.red;
        Vector2 worldPos = (Vector2)transform.position + groundCheckLocalOffset;
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
        if (!IsServerInitialized)
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

    // -------------------- Crouch Sync --------------------
    [ObserversRpc(BufferLast = true)]
    private void RpcCrouchChanged(bool crouching)
    {
        _svCrouchHeld = crouching;
        ApplyCrouchVisual(crouching);
        ApplyCrouchCollider(crouching);
    }

    private void ApplyCrouchVisual(bool crouching)
    {
        if (animator == null)
        {
            // Même si pas d'Animator, on peut tout de même appliquer l'offset du bras
            if (arm != null)
                arm.localPosition = _armBaseLocalPos + (Vector3)(crouching ? armCrouchLocalOffset : Vector2.zero);
            return;
        }
        if (!string.IsNullOrEmpty(crouchAnimatorBool))
        {
            animator.SetBool(crouchAnimatorBool, crouching);
        }
        if (crouching && !string.IsNullOrEmpty(crouchStateName))
        {
            // Lance l'animation d'accroupissement si précisée
            animator.CrossFade(crouchStateName, 0.05f);
        }
        // Applique l'offset local du bras pendant le crouch (visuel léger)
        if (arm != null)
            arm.localPosition = _armBaseLocalPos + (Vector3)(crouching ? armCrouchLocalOffset : Vector2.zero);
    }

    // -------------------- Locomotion (Run) Sync --------------------
    private void ApplyLocomotionVisual(float runSpeedAbs, bool grounded)
    {
        if (animator == null)
            return;
        if (!string.IsNullOrEmpty(runSpeedAnimatorFloat))
        {
            // Un léger damping pour fluidifier (0.1s)
            animator.SetFloat(runSpeedAnimatorFloat, runSpeedAbs, 0.1f, Time.deltaTime);
        }
        if (!string.IsNullOrEmpty(groundedAnimatorBool))
        {
            animator.SetBool(groundedAnimatorBool, grounded);
        }
    }

    private void TrySendLocomotionToObservers(float runSpeedAbs, bool grounded)
    {
        if (!IsServerInitialized)
            return;

        const float minDelta = 0.05f; // seuil de vitesse pour éviter le spam
        const float maxRate = 20f;    // 20 msg/s max
        float now = Time.unscaledTime;
        float minInterval = 1f / maxRate;

        bool speedChanged = Mathf.Abs(runSpeedAbs - _lastSentRunSpeed) >= minDelta;
        bool groundedChanged = grounded != _lastSentGrounded;
        bool timeOk = (now - _lastLocomotionSendTime) >= minInterval;

        if (!(speedChanged || groundedChanged || timeOk))
            return;

        _lastSentRunSpeed = runSpeedAbs;
        _lastSentGrounded = grounded;
        _lastLocomotionSendTime = now;
        RpcApplyLocomotion(runSpeedAbs, grounded);
    }

    [ObserversRpc(BufferLast = true, ExcludeOwner = true)]
    private void RpcApplyLocomotion(float runSpeedAbs, bool grounded)
    {
        ApplyLocomotionVisual(runSpeedAbs, grounded);
    }

    // Ajuste la taille/offset du collider pendant l'accroupissement pour que les pieds restent au sol
    private void ApplyCrouchCollider(bool crouching)
    {
        if (!adjustColliderOnCrouch)
            return;

        if (_capsule != null)
        {
            if (!crouching)
            {
                _capsule.size = _origCapsuleSize;
                _capsule.offset = _origCapsuleOffset;
            }
            else
            {
                var size = _origCapsuleSize;
                float newH = Mathf.Max(0.01f, size.y * Mathf.Clamp01(crouchHeightMultiplier));
                float delta = size.y - newH;
                _capsule.size = new Vector2(size.x, newH);
                // Décale le centre vers le bas pour garder le bas au même endroit
                _capsule.offset = _origCapsuleOffset + new Vector2(0f, -delta * 0.5f);
            }
        }
        else if (_box != null)
        {
            if (!crouching)
            {
                _box.size = _origBoxSize;
                _box.offset = _origBoxOffset;
            }
            else
            {
                var size = _origBoxSize;
                float newH = Mathf.Max(0.01f, size.y * Mathf.Clamp01(crouchHeightMultiplier));
                float delta = size.y - newH;
                _box.size = new Vector2(size.x, newH);
                _box.offset = _origBoxOffset + new Vector2(0f, -delta * 0.5f);
            }
        }
    }

    // Applique la demande d'accroupissement côté serveur avec vérif d'espace pour se relever
    private void SetCrouchServer(bool wantCrouch)
    {
        if (!IsServerInitialized)
            return;

        if (wantCrouch == _svCrouchHeld)
            return;

        if (!wantCrouch)
        {
            // On veut se relever: vérifier qu'il y a de la place
            if (!CanStandUp())
            {
                // Pas la place: rester accroupi
                return;
            }
        }

        _svCrouchHeld = wantCrouch;
        // Appliquer immédiatement côté serveur (host) pour collider/visu
        ApplyCrouchVisual(_svCrouchHeld);
        ApplyCrouchCollider(_svCrouchHeld);
        // Diffuser aux observateurs
        RpcCrouchChanged(_svCrouchHeld);
    }

    private bool CanStandUp()
    {
        if (!adjustColliderOnCrouch)
            return true;

        // Construire la forme du collider "debout"
        Vector2 worldCenter;
        Vector2 size;
        float angle = 0f;
        bool hasShape = false;

        if (_capsule != null)
        {
            var standSize = _origCapsuleSize;
            worldCenter = (Vector2)transform.position + _origCapsuleOffset;
            size = standSize + new Vector2(standUpCeilingCheckPadding * 2f, standUpCeilingCheckPadding * 2f);
            hasShape = true;
        }
        else if (_box != null)
        {
            var standSize = _origBoxSize;
            worldCenter = (Vector2)transform.position + _origBoxOffset;
            size = standSize + new Vector2(standUpCeilingCheckPadding * 2f, standUpCeilingCheckPadding * 2f);
            angle = _box.transform.eulerAngles.z;
            hasShape = true;
        }
        else
        {
            return true; // pas de collider à gérer
        }

        if (!hasShape)
            return true;

        // Vérifier s'il y a un chevauchement avec l'environnement (en ignorant nos propres colliders)
        var hits = Physics2D.OverlapBoxAll(worldCenter, size, angle, groundLayers);
        foreach (var h in hits)
        {
            if (h == null)
                continue;
            if (h.attachedRigidbody != null && h.attachedRigidbody.gameObject == gameObject)
                continue;
            if (h.transform.root == transform.root)
                continue; // ignore soi-même
            return false; // obstacle détecté
        }
        return true;
    }

    // -------------------- Shooting --------------------
    private Transform GetMuzzle()
    {
        if (muzzle != null)
            return muzzle;
        if (handTip != null)
            return handTip;
        return arm != null ? arm : transform;
    }

    private void TryFire(float angleDeg)
    {
        if (bulletPrefab == null)
            return;

        if (_isEliminated)
            return; // KO: pas de tir

        float minInterval = (fireRate > 0f) ? (1f / fireRate) : 0f;
        if (minInterval > 0f && Time.time < _nextFireTime)
            return;

        _nextFireTime = Time.time + minInterval;

        Transform m = GetMuzzle();
        Vector3 spawnPos = (m != null) ? m.position : transform.position;
        // Avance légèrement le point de spawn dans la direction de tir si demandé.
        if (muzzleForwardOffset != 0f)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            spawnPos += (Vector3)(dir * muzzleForwardOffset);
        }
        if (m == null)
        {
            // Aide au debug: informer si aucun point de tir n'est assigné.
            // Cela explique des balles qui semblent partir du centre du joueur.
            Debug.LogWarning("[PlayerController] Aucun 'muzzle' ni 'handTip' n'est assigné. Le projectile apparaîtra à la position du joueur.");
        }
        // Log côté client (owner) pour vérifier les valeurs envoyées au serveur, même si on n'est pas Host.
        Debug.Log($"[PlayerController] [ClientOwner] Fire -> lifetime={bulletLifetime:F2}s speed={bulletSpeed:F2} angle={angleDeg:F1} spawnPos={spawnPos}");
        // Respecte les overrides : on passe -1 pour laisser le prefab décider.
        float speedToSend = overrideBulletSpeed ? bulletSpeed : -1f;
        float lifetimeToSend = overrideBulletLifetime ? bulletLifetime : -1f;
        SendFireServerRpc(spawnPos, angleDeg, speedToSend, lifetimeToSend);
    }

    [ServerRpc]
    private void SendFireServerRpc(Vector3 spawnPos, float angleDeg, float speed, float lifetime)
    {
        if (bulletPrefab == null)
            return;

        // KO côté serveur: ignorer la demande
        if (_isEliminated)
            return;

        // Sécurité côté serveur : respecte la cadence max
        float minInterval = (fireRate > 0f) ? (1f / fireRate) : 0f;
        if (minInterval > 0f && Time.time < _svNextFireTime)
            return;
        _svNextFireTime = Time.time + minInterval;

        // Log de diagnostic: vérifier la valeur de lifetime reçue côté serveur
        Debug.Log($"[PlayerController] [Server] Fire -> lifetime={lifetime:F2}s speed={speed:F2} angle={angleDeg:F1} spawnPos={spawnPos}");

        // Instantiate le projectile côté serveur, initialise sa direction, puis le Spawn vers les clients.
        NetworkObject bullet = Instantiate(bulletPrefab, spawnPos, Quaternion.Euler(0f, 0f, angleDeg));
        var proj = bullet.GetComponent<BulletProjectile>();
        if (proj != null)
        {
            proj.ServerInitialize(angleDeg, speed, lifetime, this.NetworkObject);
        }

        // Diffuse l'objet réseau aux observateurs.
        NetworkManager.ServerManager.Spawn(bullet);
        // Configure la direction/vitesse côté clients observateurs pour empêcher la chute locale.
        if (proj != null)
        {
            proj.RpcSetup(angleDeg, speed);
        }
    }

    // -------------------- Hits & Elimination --------------------
    /// <summary>
    /// Appelé côté serveur lorsqu'un projectile touche ce joueur.
    /// </summary>
    public void ServerRegisterHit()
    {
        if (!IsServerInitialized)
            return;
        if (_isEliminated)
            return;

        _currentHits++;
        Debug.Log($"[PlayerController] [Server] Hit reçu {_currentHits}/{hitsToEliminate} par {name}");

        if (_currentHits >= Mathf.Max(1, hitsToEliminate))
        {
            EliminateServer();
        }
        else
        {
            RpcOnHitFeedback(_currentHits, hitsToEliminate);
        }
    }

    private void EliminateServer()
    {
        if (!IsServerInitialized)
            return;
        if (_isEliminated)
            return;

        _isEliminated = true;
        Debug.Log($"[PlayerController] [Server] {name} est éliminé (KO)");

        // Forcer la fin de l'accroupissement à l'élimination pour éviter les conflits d'animations
        if (_svCrouchHeld)
        {
            _svCrouchHeld = false;
            RpcCrouchChanged(false);
        }

        // Optionnel: désactiver les collisions du joueur éliminé pour éviter d'autres hits.
        var cols = GetComponentsInChildren<Collider2D>(true);
        foreach (var c in cols)
            c.enabled = false;

        RpcSetEliminated(true, _currentHits, hitsToEliminate);
    }

    /// <summary>
    /// Réinitialise l'état pour la prochaine manche (à appeler côté serveur par un gestionnaire de manche).
    /// </summary>
    public void ResetForNewRound()
    {
        if (!IsServerInitialized)
            return;

        _currentHits = 0;
        bool wasElim = _isEliminated;
        _isEliminated = false;

        // Sort de l'état accroupi au reset
        if (_svCrouchHeld)
        {
            _svCrouchHeld = false;
            RpcCrouchChanged(false);
        }

        // Réactiver les collisions si elles avaient été coupées.
        var cols = GetComponentsInChildren<Collider2D>(true);
        foreach (var c in cols)
            c.enabled = true;

        if (wasElim)
        {
            // Remettre à zéro la vélocité horizontale.
            var v = _rb.linearVelocity;
            v.x = 0f;
            _rb.linearVelocity = v;
        }

        RpcSetEliminated(false, _currentHits, hitsToEliminate);
        Debug.Log($"[PlayerController] [Server] {name} reset pour nouvelle manche");
    }

    [ObserversRpc]
    private void RpcOnHitFeedback(int hits, int max)
    {
        // Petit feedback visuel: flash de couleur si SpriteRenderer disponible
        if (spriteToFlip != null)
        {
            // Clignotement simple et non bloquant
            spriteToFlip.color = new Color(1f, 0.5f, 0.5f, 1f);
            // On planifie un retour à la normale via Coroutine légère si dispo
            StopAllCoroutines();
            StartCoroutine(ResetColorNextFrame());
        }
    }

    private System.Collections.IEnumerator ResetColorNextFrame()
    {
        // Attendre un court délai
        yield return new WaitForSeconds(0.06f);
        if (spriteToFlip != null)
            spriteToFlip.color = Color.white;
    }

    [ObserversRpc(BufferLast = true)]
    private void RpcSetEliminated(bool eliminated, int hits, int max)
    {
        _isEliminated = eliminated;
        _currentHits = hits;

        // Visuel: rendre semi-transparent si KO
        if (spriteToFlip != null)
        {
            var c = spriteToFlip.color;
            c.a = eliminated ? 0.4f : 1f;
            spriteToFlip.color = c;
        }
    }
}
