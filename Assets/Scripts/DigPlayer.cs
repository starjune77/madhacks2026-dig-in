using UnityEngine;
using UnityEngine.InputSystem;

// First-person movement, looking, and the dig swing. Created at runtime by DigGame.
[RequireComponent(typeof(CharacterController))]
public class DigPlayer : MonoBehaviour
{
    public Transform cam;
    public DigShovel shovel;

    public const float EyeHeight = 0.7f;   // camera height above the controller centre

    const float MoveSpeed = 4.2f;
    const float GroundAccel = 32f;
    const float GroundDecel = 40f;
    const float AirAccel = 8f;
    const float Gravity = -20f;
    const float JumpSpeed = 7f;            // about 1.2 m high
    const float SlideSpeed = 5f;           // how fast you slip down slopes that are too steep
    const float LookSensitivity = 0.12f;
    const float Reach = 3.2f;
    const float SwingDuration = 0.46f;

    CharacterController cc;
    float pitch;
    Vector3 velocity;        // horizontal walking velocity
    Vector3 slide;           // extra velocity from standing on a too-steep slope
    float verticalSpeed;

    bool swinging;
    bool struck;
    float swingTime;
    bool waitForRelease;     // ignore a held button until it is let go

    float eyeY;              // smoothed camera height in world space
    float eyeVelocity;
    float bobPhase;
    float bobAmount;
    float kickPitch;
    float kickRoll;

    void Start()
    {
        cc = GetComponent<CharacterController>();
        eyeY = transform.position.y + EyeHeight;
        LockCursor(true);
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        if (kb.escapeKey.wasPressedThisFrame) LockCursor(false);
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                LockCursor(true);
                waitForRelease = true;   // the click that grabs the cursor should not dig
            }
            return;
        }

        Look(mouse);
        Move(kb);
        Dig(mouse);
        UpdateCamera();

        if (kb.rKey.wasPressedThisFrame) DigGame.I.ReturnToSurface();
        if (kb.eKey.wasPressedThisFrame) DigGame.I.TryUpgrade();
    }

    void Look(Mouse mouse)
    {
        Vector2 delta = mouse.delta.ReadValue() * LookSensitivity;
        transform.Rotate(0f, delta.x, 0f);
        pitch = Mathf.Clamp(pitch - delta.y, -89f, 89f);
    }

    // ---- Movement ---------------------------------------------------------------
    void Move(Keyboard kb)
    {
        float dt = Time.deltaTime;
        float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        Vector3 wish = transform.right * x + transform.forward * z;
        if (wish.sqrMagnitude > 1f) wish.Normalize();
        wish *= MoveSpeed;

        bool grounded = cc.isGrounded;
        float accel = !grounded ? AirAccel : (wish.sqrMagnitude > 0.01f ? GroundAccel : GroundDecel);
        velocity = Vector3.MoveTowards(velocity, wish, accel * dt);

        Vector3 move = velocity;
        Vector3 slideTarget = Vector3.zero;
        if (grounded)
        {
            verticalSpeed = -2f;   // keeps the controller pressed to the ground
            if (GroundNormal(out Vector3 normal))
            {
                if (Vector3.Angle(normal, Vector3.up) > cc.slopeLimit)
                    slideTarget = Vector3.ProjectOnPlane(Vector3.down, normal).normalized * SlideSpeed;
                else
                    move = Vector3.ProjectOnPlane(velocity, normal);   // walk along the slope, not into it
            }
            if (kb.spaceKey.wasPressedThisFrame) verticalSpeed = JumpSpeed;
        }
        else
        {
            verticalSpeed += Gravity * dt;
        }
        slide = Vector3.MoveTowards(slide, slideTarget, 25f * dt);

        cc.Move((move + slide + Vector3.up * verticalSpeed) * dt);

        if ((cc.collisionFlags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;
    }

    bool GroundNormal(out Vector3 normal)
    {
        Vector3 origin = transform.position + cc.center;
        float distance = cc.height * 0.5f - cc.radius + 0.25f;
        if (Physics.SphereCast(origin, cc.radius * 0.9f, Vector3.down, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
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

        // A fresh click on a found object picks it up instead of digging.
        if (mouse.leftButton.wasPressedThisFrame && !waitForRelease &&
            DigGame.I.TryPickUp(new Ray(cam.position, cam.forward), Reach))
        {
            waitForRelease = true;
        }

        if (!swinging && held && !waitForRelease)
        {
            swinging = true;
            struck = false;
            swingTime = 0f;
        }
        if (!swinging) return;

        swingTime += Time.deltaTime;
        if (!struck && swingTime >= SwingDuration * DigShovel.ImpactT)
        {
            struck = true;
            Strike();
        }
        if (swingTime >= SwingDuration) swinging = false;
    }

    void Strike()
    {
        if (!Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, Reach, ~0, QueryTriggerInteraction.Ignore)) return;

        bool bounced = DigGame.I.Strike(hit, transform.position);
        // The view dips with the blow, or jolts back up when the shovel bounces off.
        kickPitch += bounced ? -1.4f : 1.6f;
        kickRoll += Random.Range(-0.7f, 0.7f) * (bounced ? 2f : 1f);
    }

    // ---- Camera -----------------------------------------------------------------
    void UpdateCamera()
    {
        float dt = Time.deltaTime;

        // Follow the body's height with a little smoothing so sinking into a fresh
        // scoop or walking over bumps does not jolt the view.
        float targetY = transform.position.y + EyeHeight;
        eyeY = Mathf.SmoothDamp(eyeY, targetY, ref eyeVelocity, 0.06f);
        eyeY = Mathf.Clamp(eyeY, targetY - 0.4f, targetY + 0.4f);

        float speed01 = cc.isGrounded ? Mathf.Clamp01(velocity.magnitude / MoveSpeed) : 0f;
        bobAmount = Mathf.MoveTowards(bobAmount, speed01, 6f * dt);
        bobPhase += dt * 9f * bobAmount;
        float bob = Mathf.Sin(bobPhase * 2f) * 0.018f * bobAmount;

        float decay = Mathf.Exp(-14f * dt);
        kickPitch *= decay;
        kickRoll *= decay;

        cam.localPosition = new Vector3(0f, eyeY - transform.position.y + bob, 0f);
        cam.localEulerAngles = new Vector3(pitch + kickPitch, 0f, kickRoll);

        shovel.Pose(swinging ? swingTime / SwingDuration : -1f, bobPhase, bobAmount);
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
