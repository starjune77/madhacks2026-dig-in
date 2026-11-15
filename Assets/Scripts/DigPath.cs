using UnityEngine;

// The hidden life path: a spline that starts in the backyard hollow and descends while
// wandering. Nothing draws it in the game. It decides where the soil is soft and warm,
// where memories lie, and where "today" is. Sits on "Dig Site/Life Path"; the control
// points are scene data (select the object to see the path in the Scene view).
public class DigPath : MonoBehaviour
{
    [Tooltip("Control points in world space, from the surface to the end.")]
    public Vector3[] points = new Vector3[0];

    const float Step = 0.25f;

    // Not serialized: the editor would otherwise bring them back as empty arrays after a reload.
    [System.NonSerialized] Vector3[] line;      // the spline sampled about every Step metres
    [System.NonSerialized] float[] along;       // distance from the start at each sample

    public float Length { get { EnsureBaked(); return along[along.Length - 1]; } }
    public Vector3[] Line { get { EnsureBaked(); return line; } }
    public Vector3 End { get { EnsureBaked(); return line[line.Length - 1]; } }

    void Awake()
    {
        Bake();
    }

    void EnsureBaked()
    {
        if (line == null || line.Length < 2) Bake();
    }

    public void Bake()
    {
        var list = new System.Collections.Generic.List<Vector3>();
        int n = points.Length;
        if (n < 2)
        {
            line = new[] { transform.position, transform.position + Vector3.down };
        }
        else
        {
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = points[Mathf.Max(i - 1, 0)], p1 = points[i], p2 = points[i + 1], p3 = points[Mathf.Min(i + 2, n - 1)];
                int steps = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(p1, p2) / Step));
                for (int s = 0; s < steps; s++) list.Add(CatmullRom(p0, p1, p2, p3, (float)s / steps));
            }
            list.Add(points[n - 1]);
            line = list.ToArray();
        }

        along = new float[line.Length];
        for (int i = 1; i < line.Length; i++) along[i] = along[i - 1] + Vector3.Distance(line[i - 1], line[i]);
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    int IndexAt(float distance, out float t)
    {
        EnsureBaked();
        distance = Mathf.Clamp(distance, 0f, along[along.Length - 1]);
        int lo = 0, hi = along.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (along[mid] <= distance) lo = mid; else hi = mid;
        }
        t = Mathf.InverseLerp(along[lo], along[hi], distance);
        return lo;
    }

    public Vector3 PointAt(float distance)
    {
        int i = IndexAt(distance, out float t);
        return Vector3.Lerp(line[i], line[i + 1], t);
    }

    public Vector3 TangentAt(float distance)
    {
        int i = IndexAt(distance, out _);
        return (line[i + 1] - line[i]).normalized;
    }

    // Distance from `p` to the path, and how far along the path the nearest point is.
    public float Distance(Vector3 p, out float distanceAlong)
    {
        EnsureBaked();
        float best = float.MaxValue;
        distanceAlong = 0f;
        for (int i = 0; i < line.Length - 1; i++)
        {
            Vector3 a = line[i], ab = line[i + 1] - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            float d = (p - (a + ab * t)).sqrMagnitude;
            if (d < best)
            {
                best = d;
                distanceAlong = Mathf.Lerp(along[i], along[i + 1], t);
            }
        }
        return Mathf.Sqrt(best);
    }

    public float Distance(Vector3 p)
    {
        return Distance(p, out _);
    }

    // The first distance along the path at which it is at least `depth` metres down.
    public float DistanceAtDepth(float depth)
    {
        EnsureBaked();
        for (int i = 0; i < line.Length; i++)
            if (-line[i].y >= depth) return along[i];
        return along[along.Length - 1];
    }

    void OnDrawGizmosSelected()
    {
        if (points == null || points.Length < 2) return;
        Bake();
        Gizmos.color = new Color(1f, 0.75f, 0.3f);
        for (int i = 0; i < line.Length - 1; i++) Gizmos.DrawLine(line[i], line[i + 1]);
        Gizmos.color = new Color(1f, 0.9f, 0.6f);
        foreach (var p in points) Gizmos.DrawSphere(p, 0.2f);
    }
}
