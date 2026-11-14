using UnityEngine;
using UnityEngine.InputSystem;

// First-person movement, looking, and digging. Created at runtime by DigGame.
[RequireComponent(typeof(CharacterController))]
public class DigPlayer : MonoBehaviour
{
    public Transform cam;

    const float MoveSpeed = 4.5f;
    const float Gravity = -20f;
    const float JumpSpeed = 7f;        // about 1.2 m high: enough to hop out of a one-block hole
    const float LookSensitivity = 0.12f;
    const float Reach = 3.2f;
    const float SwingInterval = 0.22f;

    CharacterController cc;
    float pitch;
    float verticalSpeed;
    float swingTimer;

    void Start()
    {
        cc = GetComponent<CharacterController>();
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
            if (mouse.leftButton.wasPressedThisFrame) LockCursor(true);
            return;
        }

        Look(mouse);
        Move(kb);
        Dig(mouse);

        if (kb.rKey.wasPressedThisFrame) DigGame.I.ReturnToSurface();
        if (kb.eKey.wasPressedThisFrame) DigGame.I.TryUpgrade();
    }

    void Look(Mouse mouse)
    {
        Vector2 delta = mouse.delta.ReadValue() * LookSensitivity;
        transform.Rotate(0f, delta.x, 0f);
        pitch = Mathf.Clamp(pitch - delta.y, -89f, 89f);
        cam.localEulerAngles = new Vector3(pitch, 0f, 0f);
    }

    void Move(Keyboard kb)
    {
        float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        Vector3 dir = transform.right * x + transform.forward * z;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        if (cc.isGrounded)
        {
            verticalSpeed = -1f;
            if (kb.spaceKey.wasPressedThisFrame) verticalSpeed = JumpSpeed;
        }
        else
        {
            verticalSpeed += Gravity * Time.deltaTime;
        }

        cc.Move((dir * MoveSpeed + Vector3.up * verticalSpeed) * Time.deltaTime);
    }

    void Dig(Mouse mouse)
    {
        swingTimer -= Time.deltaTime;
        if (!mouse.leftButton.isPressed || swingTimer > 0f) return;
        swingTimer = SwingInterval;

        if (Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, Reach))
        {
            var block = hit.collider.GetComponent<DigBlock>();
            if (block != null) DigGame.I.HitBlock(block);
        }
    }

    public void Teleport(Vector3 position)
    {
        cc.enabled = false;
        transform.position = position;
        cc.enabled = true;
        verticalSpeed = 0f;
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
