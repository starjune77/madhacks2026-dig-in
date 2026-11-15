using UnityEngine;

// The old spade, stuck upright in the soft ground at the dig spot, waiting to be taken.
// Taking it (E, DigGame): it pulls up out of the soil with a little wobble, then comes to
// your hands, and the spade in view takes over. Lives on "Environment/Dig Spot/Old Spade".
public class DigWorldSpade : MonoBehaviour
{
    [Tooltip("Height of the spade from its tip in the soil to the grip (m).")]
    public float length = 0.9f;
    public float pullSeconds = 0.4f;
    public float toHandSeconds = 0.3f;

    bool taking;
    float t;
    Transform cam;
    System.Action done;
    Vector3 start;
    Quaternion startRot;
    Vector3 startScale;

    public bool Taking => taking;
    public Vector3 Bottom => transform.position;
    public Vector3 Top => transform.position + transform.up * length;

    // Is `ray` pointing at the spade, within `reach`?
    public bool AimedBy(Ray ray, float reach)
    {
        if (taking || !isActiveAndEnabled) return false;
        // Closest approach between the ray and the spade's shaft.
        Vector3 a = Bottom, b = Top;
        for (int i = 0; i <= 8; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, i / 8f);
            Vector3 to = p - ray.origin;
            float along = Vector3.Dot(to, ray.direction);
            if (along < 0.2f || along > reach) continue;
            if ((to - ray.direction * along).magnitude < 0.12f + along * 0.03f) return true;
        }
        return false;
    }

    public void Take(Transform viewer, System.Action onInHand)
    {
        if (taking) return;
        taking = true;
        t = 0f;
        cam = viewer;
        done = onInHand;
        start = transform.position;
        startRot = transform.rotation;
        startScale = transform.localScale;
    }

    void Update()
    {
        if (!taking) return;
        t += Time.deltaTime;
        if (t < pullSeconds)
        {
            // Out of the soil: up, with a small rocking wobble that settles.
            float e = Mathf.SmoothStep(0f, 1f, t / pullSeconds);
            float wobble = Mathf.Sin(t * 38f) * 4f * (1f - e);
            transform.position = start + Vector3.up * 0.42f * e;
            transform.rotation = startRot * Quaternion.Euler(wobble, 0f, wobble * 0.6f);
            return;
        }
        float k = Mathf.Clamp01((t - pullSeconds) / toHandSeconds);
        float s = Mathf.SmoothStep(0f, 1f, k);
        Vector3 lifted = start + Vector3.up * 0.42f;
        Vector3 hand = cam.position + cam.forward * 0.45f + cam.right * 0.25f - cam.up * 0.35f;
        transform.position = Vector3.Lerp(lifted, hand, s);
        transform.rotation = Quaternion.Slerp(startRot, Quaternion.LookRotation(cam.forward, cam.up) * Quaternion.Euler(-60f, 0f, 0f), s);
        transform.localScale = startScale * Mathf.Lerp(1f, 0.55f, s);
        if (k >= 1f)
        {
            taking = false;
            gameObject.SetActive(false);
            done?.Invoke();
        }
    }
}
