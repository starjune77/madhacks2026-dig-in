using UnityEngine;

// The family cat, who keeps the shop. Breathes, settles, and purrs while you trade
// (the Animator plays the Cat clips in Assets/Animations); turns a little to look at
// you when you come near. Lives on the cat in the shop corner and in the bedroom.
[RequireComponent(typeof(Animator))]
public class DigCat : MonoBehaviour
{
    [Tooltip("Turns toward the player within this distance (m). 0 = never turns.")]
    public float watchDistance = 6f;
    [Tooltip("Most it turns away from its resting direction (degrees).")]
    public float maxTurn = 35f;
    public float turnSpeed = 1.5f;
    [Tooltip("Purrs while this is true (the shop sets it).")]
    public bool purring;

    static readonly int PurrParam = Animator.StringToHash("Purr");

    Animator animator;
    Quaternion rest;
    float yaw;

    void Awake()
    {
        animator = GetComponent<Animator>();
        rest = transform.localRotation;
        // Not every cat breathes in step.
        animator.Play(0, 0, Random.value);
    }

    void Update()
    {
        animator.SetBool(PurrParam, purring || (DigGame.I != null && DigGame.I.shop != null && DigGame.I.shop.IsOpen && watchDistance > 0f));

        if (watchDistance <= 0f || DigGame.I == null) return;
        Vector3 to = DigGame.I.player.transform.position - transform.position;
        to.y = 0f;
        float target = 0f;
        if (to.sqrMagnitude < watchDistance * watchDistance)
        {
            Vector3 forward = transform.parent != null ? transform.parent.rotation * rest * Vector3.forward : rest * Vector3.forward;
            target = Mathf.Clamp(Vector3.SignedAngle(new Vector3(forward.x, 0f, forward.z), to, Vector3.up), -maxTurn, maxTurn);
        }
        yaw = Mathf.Lerp(yaw, target, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
        transform.localRotation = rest * Quaternion.Euler(0f, yaw, 0f);
    }
}
