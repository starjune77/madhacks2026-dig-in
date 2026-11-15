using UnityEngine;
using UnityEngine.InputSystem;

// First-person movement, looking, and digging input. Lives on the Player prefab.
// The swing is the shovel's animation; this starts it, and acts when the clip says the
// blade has landed (dig, hit-stop, camera kick) and when it flicks the earth away.
[RequireComponent(typeof(CharacterController))]
public class DigPlayer : MonoBehaviour
{
    public Transform cam;
    public DigShovel shovel;

    public const float EyeHeight = 0.55f;   // camera height above the controller centre

    // Frame of the click that re-grabbed the cursor after Esc. That click only grabs:
    // it must not dig, pick up, or close a card or letter (DigStory checks this).
    public static int CursorGrabFrame { get; private set; } = -1;

    [Header("Movement")]
    public float moveSpeed = 3.8f;
    public float groundAccel = 26f;
    public float groundDecel = 34f;
    public float airAccel = 7f;
    public float gravity = -20f;
    public float jumpSpeed = 6.2f;
    [Tooltip("Slopes steeper than this are scrambled up slowly; the controller's Slope Limit is where sliding starts.")]
    public float easySlope = 32f;
    [Tooltip("Speed up the steepest climbable slope, as a fraction of walking speed.")]
    public float scrambleSpeed = 0.3f;
    public float slideSpeed = 4f;

    [Header("Look and reach")]
    public float lookSensitivity = 0.12f;
    public float reach = 2.6f;

    [Header("Camera feel")]
    [Tooltip("Degrees the view dips when the blade bites.")]
    public float kickDig = 1.1f;
    [Tooltip("Degrees the view jolts up when the blade bounces off.")]
    public float kickBounce = 1.3f;
    public float kickRecover = 11f;
    public float bobHeight = 0.016f;

    // Walks without keys: x = strafe, y = forward, each -1..1. Zero means "use the keyboard".
    [System.NonSerialized] public Vector2 scriptedMove;

    CharacterController cc;
    float pitch;
    Vector3 velocity;        // horizontal walking velocity
    Vector3 slide;           // extra velocity from standing on a near-vertical slope
    float verticalSpeed;
    bool waitForRelease;     // ignore a held button until it is let go

    float eyeY;              // smoothed camera height in world space
    float eyeVelocity;
    float bobPhase;
    float bobAmount;
    float kickPitch, kickRoll, kickPush;
    Vector2 lookRate;        // degrees per second, for the shovel's lag

    public float FeetY { get { return transform.position.y - cc.height * 0.5f; } }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        eyeY = transform.position.y + EyeHeight;
    }

    void OnEnable()
    {
        shovel.Impact += OnImpact;
        shovel.Toss += OnToss;
    }

    void OnDisable()
    {
        shovel.Impact -= OnImpact;
        shovel.Toss -= OnToss;
    }

    void Start()
    {
        LockCursor(true);
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        // The shop frees the cursor while it is open; its own Esc and clicks are not ours.
        bool shopOpen = DigGame.I.shop != null && DigGame.I.shop.IsOpen;
        bool shopJustClosed = Time.frameCount - DigShop.LastClosedFrame <= 1;
        if (shopOpen || shopJustClosed)
        {
            waitForRelease = true;   // the click that closes the shop must not swing
            velocity = Vector3.zero;
            lookRate = Vector2.zero;
            DigGame.I.HideAim();
            UpdateCamera();
            return;
        }

        if (kb.escapeKey.wasPressedThisFrame) LockCursor(false);
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                LockCursor(true);
                CursorGrabFrame = Time.frameCount;
                waitForRelease = true;   // the click that grabs the cursor should not dig
            }
            DigGame.I.HideAim();
            return;
        }

        // A card, letter or the ending is open: hold still, and do not let the click that
        // closes it start a swing.
        if (DigGame.I.InputLocked)
        {
            waitForRelease = true;
            velocity = Vector3.zero;
            lookRate = Vector2.zero;
            DigGame.I.HideAim();
            UpdateCamera();
            return;
        }

        Look(mouse);
        Move(kb);
        Dig(mouse);
        UpdateCamera();

        if (kb.rKey.wasPressedThisFrame) DigGame.I.ToggleSurface();
        if (kb.eKey.wasPressedThisFrame) DigGame.I.Interact();
    }

    void Look(Mouse mouse)
    {
        Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;
        transform.Rotate(0f, delta.x, 0f);
        pitch = Mathf.Clamp(pitch - delta.y, -89f, 89f);
        lookRate = delta / Mathf.Max(Time.deltaTime, 0.0001f);
    }

    // ---- Movement ---------------------------------------------------------------
    void Move(Keyboard kb)
    {
        float dt = Time.deltaTime;
        float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        if (scriptedMove != Vector2.zero) { x = scriptedMove.x; z = scriptedMove.y; }
        Vector3 wish = transform.right * x + transform.forward * z;
        if (wish.sqrMagnitude > 1f) wish.Normalize();

        bool grounded = cc.isGrounded;
        Vector3 normal = Vector3.up;
        float slope = 0f;
        bool onGround = grounded && GroundNormal(out normal);
        if (onGround) slope = Vector3.Angle(normal, Vector3.up);

        // Steep ground can be climbed, but slowly: you scramble out of your own hole.
        float speed = moveSpeed;
        if (onGround && slope > easySlope && wish.sqrMagnitude > 0.01f)
        {
            Vector3 uphill = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
            float climbing = Mathf.Clamp01(Vector3.Dot(Vector3.ProjectOnPlane(wish, normal).normalized, uphill));
            float steep = Mathf.InverseLerp(easySlope, cc.slopeLimit, slope);
            speed *= Mathf.Lerp(1f, Mathf.Lerp(1f, scrambleSpeed, steep), climbing);
        }

        float accel = !grounded ? airAccel : (wish.sqrMagnitude > 0.01f ? groundAccel : groundDecel);
        velocity = Vector3.MoveTowards(velocity, wish * speed, accel * dt);

        Vector3 move = velocity;
        Vector3 slideTarget = Vector3.zero;
        if (grounded)
        {
            verticalSpeed = -2f;   // keeps the controller pressed to the ground
            if (onGround)
            {
                if (slope > cc.slopeLimit)
                    slideTarget = Vector3.ProjectOnPlane(Vector3.down, normal).normalized * slideSpeed;
                else
                    move = Vector3.ProjectOnPlane(velocity, normal);   // walk along the slope, not into it
            }
            if (kb.spaceKey.wasPressedThisFrame) verticalSpeed = jumpSpeed;
        }
        else
        {
            verticalSpeed += gravity * dt;
        }
        slide = Vector3.MoveTowards(slide, slideTarget, 20f * dt);

        cc.Move((move + slide + Vector3.up * verticalSpeed) * dt);

        if ((cc.collisionFlags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;
    }

    bool GroundNormal(out Vector3 normal)
    {
        Vector3 origin = transform.position + cc.center;
        float distance = cc.height * 0.5f - cc.radius + 0.25f;
        if (Physics.SphereCast(origin, cc.radius * 0.9f, Vector3.down, out RaycastHit hit, distance, DigGame.AimMask, QueryTriggerInteraction.Ignore))
        {
            normal = hit.normal;
            return true;
        }
        normal = Vector3.up;
        return false;
    }

    // ---- Digging ----------------------------------------------------------------
    void Dig(Mouse mouse)
    {
        bool held = mouse.leftButton.isPressed;
        if (!held) waitForRelease = false;

        var ray = new Ray(cam.position, cam.forward);
        DigGame.I.UpdateAim(ray, reach);

        // A fresh click on a found object picks it up instead of digging.
        if (mouse.leftButton.wasPressedThisFrame && !waitForRelease && DigGame.I.TryPickUp(ray, reach + 0.4f))
            waitForRelease = true;

        // Holding the button keeps the rhythm: the next swing starts as soon as the
        // clip says the last one has thrown its earth.
        if (held && !waitForRelease && shovel.Ready) shovel.Swing();
    }

    // Animation event: the blade lands.
    void OnImpact()
    {
        if (!Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, reach, DigGame.AimMask, QueryTriggerInteraction.Ignore)) return;

        switch (DigGame.I.Dig(hit, cam.forward))
        {
            case DigGame.DigOutcome.Dug:
                shovel.HitStop();
                kickPitch += kickDig;
                kickRoll += Random.Range(-0.5f, 0.5f);
                kickPush += 0.02f;
                break;
            case DigGame.DigOutcome.TooHard:
                shovel.Bounce();
                kickPitch -= kickBounce;
                kickRoll += Random.Range(-1.2f, 1.2f);
                break;
            default:
                shovel.Bounce();
                kickPitch -= kickBounce * 0.4f;
                break;
        }
    }

    // Animation event: the shovel flicks its load away, up and to the right.
    void OnToss()
    {
        Vector3 from = shovel.blade != null ? shovel.blade.position : cam.position + cam.forward * 0.6f;
        DigGame.I.Toss(from, cam.right * 0.75f + cam.up * 0.7f + cam.forward * 0.25f);
    }

    // ---- Camera -----------------------------------------------------------------
    void UpdateCamera()
    {
        float dt = Time.deltaTime;

        // Follow the body's height with a little smoothing so stepping over lumps and
        // into fresh scoops does not jolt the view.
        float targetY = transform.position.y + EyeHeight;
        eyeY = Mathf.SmoothDamp(eyeY, targetY, ref eyeVelocity, 0.07f);
        eyeY = Mathf.Clamp(eyeY, targetY - 0.35f, targetY + 0.35f);

        float speed01 = cc.isGrounded ? Mathf.Clamp01(velocity.magnitude / moveSpeed) : 0f;
        bobAmount = Mathf.MoveTowards(bobAmount, speed01, 5f * dt);
        bobPhase += dt * 8.5f * bobAmount;
        float bob = Mathf.Sin(bobPhase * 2f) * bobHeight * bobAmount;

        float decay = Mathf.Exp(-kickRecover * dt);
        kickPitch *= decay;
        kickRoll *= decay;
        kickPush *= decay;

        cam.localPosition = new Vector3(0f, eyeY - transform.position.y + bob, kickPush);
        cam.localEulerAngles = new Vector3(pitch + kickPitch, 0f, kickRoll);

        shovel.Sway(bobPhase, bobAmount, lookRate);
        lookRate = Vector2.Lerp(lookRate, Vector2.zero, 1f - Mathf.Exp(-12f * dt));
    }

    public void SetLook(float yaw, float newPitch)
    {
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        pitch = newPitch;
        kickPitch = 0f;
        kickRoll = 0f;
    }

    public void Teleport(Vector3 position)
    {
        cc.enabled = false;
        transform.position = position;
        cc.enabled = true;
        velocity = Vector3.zero;
        slide = Vector3.zero;
        verticalSpeed = 0f;
        eyeY = position.y + EyeHeight;
        eyeVelocity = 0f;
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
