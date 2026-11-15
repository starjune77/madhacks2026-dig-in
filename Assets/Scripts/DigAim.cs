using UnityEngine;

// A faint ring on the ground where the next scoop will land. It hugs the surface, so it
// reads on slopes and tunnel walls too. Sits on "Dig Site/Aim Ring" with a LineRenderer.
[RequireComponent(typeof(LineRenderer))]
public class DigAim : MonoBehaviour
{
    public int segments = 40;
    [Tooltip("Ring size as a fraction of the scoop radius.")]
    public float ringScale = 0.8f;
    public Color color = new Color(1f, 0.93f, 0.8f, 0.16f);
    public Color hardColor = new Color(0.75f, 0.72f, 0.7f, 0.08f);
    public float fadeSpeed = 6f;

    LineRenderer line;
    Vector3[] points;
    float alpha;
    bool wanted, hard;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.positionCount = segments;
        line.loop = true;
        line.useWorldSpace = true;
        points = new Vector3[segments];
        line.enabled = false;
    }

    // Call every frame while the player is aiming at diggable ground.
    public void Show(RaycastHit hit, float radius, bool tooHard, int mask)
    {
        wanted = true;
        hard = tooHard;

        Vector3 n = hit.normal;
        Vector3 t = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 b = Vector3.Cross(n, t);
        float r = radius * ringScale;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector3 p = hit.point + (t * Mathf.Cos(a) + b * Mathf.Sin(a)) * r;
            // Drop each point onto the real surface.
            if (Physics.Raycast(p + n * 0.6f, -n, out RaycastHit s, 1.8f, mask, QueryTriggerInteraction.Ignore)) p = s.point + s.normal * 0.02f;
            else p += n * 0.02f;
            points[i] = p;
        }
        line.SetPositions(points);
    }

    public void Hide()
    {
        wanted = false;
    }

    void LateUpdate()
    {
        alpha = Mathf.MoveTowards(alpha, wanted ? 1f : 0f, fadeSpeed * Time.deltaTime);
        wanted = false;   // must be asked for again next frame
        line.enabled = alpha > 0.01f;
        if (!line.enabled) return;

        Color c = hard ? hardColor : color;
        c.a *= alpha * (0.85f + 0.15f * Mathf.Sin(Time.time * 3f));
        line.startColor = line.endColor = c;
    }
}
