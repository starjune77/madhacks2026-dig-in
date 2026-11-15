using UnityEngine;

// The family cat, who keeps the shop. Breathes, settles, and purrs while you trade or pet
// it (the Animator plays the Cat clips in Assets/Animations); turns a little to look at
// you when you come near. Now and then, while you are not looking, it starts licking a
// paw (a second pose of the same cat), and later sits up again, so it never visibly pops.
// When you pet it, it leans into your hand and tilts its head, and purrs louder.
// Lives on the cat in the shop corner and in the bedroom.
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

    [Header("Poses (optional)")]
    public GameObject sitPose;
    public GameObject lickPose;
    [Tooltip("Seconds sitting before it may start licking (when you look away).")]
    public Vector2 sitTime = new Vector2(14f, 32f);
    [Tooltip("Seconds it keeps licking.")]
    public Vector2 lickTime = new Vector2(6f, 12f);

    [Header("Petting")]
    public float petSeconds = 3.2f;
    [Tooltip("Said now and then after a pet.")]
    public string[] afterPet =
    {
        "Mrrp.",
        "...Just there. Yes.",
        "You always did it like that.",
        "Five more minutes.",
        "I missed you, you know.",
    };

    static readonly int PurrParam = Animator.StringToHash("Purr");

    Animator animator;
    Quaternion rest;
    float yaw, lean, petTimer, poseTimer;
    bool licking, spoke;
    Camera viewer;

    public bool Petting => petTimer > 0f;

    void Awake()
    {
        animator = GetComponent<Animator>();
        rest = transform.localRotation;
        // Not every cat breathes in step.
        animator.Play(0, 0, Random.value);
        poseTimer = Random.Range(sitTime.x, sitTime.y);
        SetPose(false);
    }

    // A small moment: the cat leans into the hand and purrs, and may say something after.
    public void Pet()
    {
        if (licking) SetPose(false);   // it looks up for you
        poseTimer = Random.Range(sitTime.x, sitTime.y);
        petTimer = petSeconds;
        spoke = false;
        DigAudio.I?.Pet(transform.position, petSeconds + 1.5f);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        var game = DigGame.I;
        bool shopOpen = game != null && game.shop != null && game.shop.IsOpen && watchDistance > 0f;
        petTimer = Mathf.Max(0f, petTimer - dt);
        animator.SetBool(PurrParam, purring || shopOpen || petTimer > 0f);

        // After the pet, sometimes, a few words.
        if (petTimer <= 0f && !spoke && lean > 0.5f && game != null)
        {
            spoke = true;
            if (Random.value < 0.45f && afterPet.Length > 0)
                game.hud.ShowMessage("<i>“" + afterPet[Random.Range(0, afterPet.Length)] + "”</i>", 3f);
        }
        lean = Mathf.MoveTowards(lean, petTimer > 0f ? 1f : 0f, dt * (petTimer > 0f ? 1.8f : 0.9f));

        // The paw: only while you are looking elsewhere, and never mid-conversation.
        if (lickPose != null && sitPose != null && game != null)
        {
            poseTimer -= dt;
            if (poseTimer <= 0f && !shopOpen && petTimer <= 0f && !Seen(game))
            {
                SetPose(!licking);
                poseTimer = licking ? Random.Range(lickTime.x, lickTime.y) : Random.Range(sitTime.x, sitTime.y);
            }
        }

        if (watchDistance <= 0f || game == null) { Apply(); return; }
        Vector3 to = game.player.transform.position - transform.position;
        to.y = 0f;
        float target = 0f;
        if (to.sqrMagnitude < watchDistance * watchDistance && !licking)
        {
            Vector3 forward = transform.parent != null ? transform.parent.rotation * rest * Vector3.forward : rest * Vector3.forward;
            target = Mathf.Clamp(Vector3.SignedAngle(new Vector3(forward.x, 0f, forward.z), to, Vector3.up), -maxTurn, maxTurn);
        }
        yaw = Mathf.Lerp(yaw, target, 1f - Mathf.Exp(-turnSpeed * dt));
        Apply();
    }

    // Turned toward you, leaning into the hand with the head tipped a little.
    void Apply()
    {
        float e = Mathf.SmoothStep(0f, 1f, lean);
        transform.localRotation = rest * Quaternion.Euler(4f * e, yaw, 9f * e);
    }

    void SetPose(bool lick)
    {
        licking = lick && lickPose != null;
        if (sitPose != null) sitPose.SetActive(!licking);
        if (lickPose != null) lickPose.SetActive(licking);
    }

    // Is the cat on screen (or very close)?
    bool Seen(DigGame game)
    {
        if (viewer == null) viewer = game.player.cam.GetComponent<Camera>();
        if (viewer == null || !viewer.isActiveAndEnabled) return false;
        var planes = GeometryUtility.CalculateFrustumPlanes(viewer);
        return GeometryUtility.TestPlanesAABB(planes, new Bounds(transform.position + Vector3.up * 0.15f, Vector3.one * 0.6f));
    }
}
