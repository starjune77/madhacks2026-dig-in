using System.Collections.Generic;
using UnityEngine;

// Minerals in the ground. Their spots are fixed (a seed), mostly near the life path, and
// which kind lies where depends on the stratum. Near the player each one is shown where
// it lies, inside the earth: only the part a wall cuts through shows, with a faint glint,
// so they can be spotted. A scoop that reaches one pops it out; walking over it puts it
// in the bag. Sits on "Dig Site/Minerals"; the kinds and the glint prefab are assets.
public class DigMinerals : MonoBehaviour
{
    public DigVolume volume;
    public DigPath path;
    public MineralData[] kinds;
    [Tooltip("A small sparkle, shown on each visible mineral.")]
    public ParticleSystem glintPrefab;

    [Header("Where they lie")]
    public int seed = 1234;
    [Tooltip("Spots per metre of life path.")]
    public float perMetre = 2.6f;
    [Tooltip("How far from the path they scatter (m).")]
    public float spread = 3.2f;
    [Tooltip("Extra spots anywhere in the volume, for those who wander.")]
    public int strays = 60;

    [Header("Showing and picking up")]
    public float showDistance = 5f;
    public float pickupDistance = 1.3f;
    [Tooltip("A scoop frees a mineral within its radius plus this much.")]
    public float freeMargin = 0.2f;

    public class Spot
    {
        public Vector3 position;
        public Quaternion rotation;
        public MineralData kind;
        public GameObject shown;     // the specimen in the wall, while near
        public Rigidbody loose;      // after it popped out
        public bool taken;
    }

    readonly List<Spot> spots = new List<Spot>();
    readonly List<Spot> loose = new List<Spot>();
    float checkTimer;

    public int Count => spots.Count;

    void Start()
    {
        var rng = new System.Random(seed);
        float R() => (float)rng.NextDouble();

        int n = Mathf.RoundToInt(path.Length * perMetre);
        for (int i = 0; i < n; i++)
        {
            float d = Mathf.Lerp(3f, path.Length - 1f, R());
            Vector3 p = path.PointAt(d) + Random3(rng) * spread * Mathf.Sqrt(R());
            Add(p, rng);
        }
        for (int i = 0; i < strays; i++)
        {
            var p = DigVolume.Min + new Vector3(R() * DigVolume.Size.x, R() * (DigVolume.Size.y - 6f), R() * DigVolume.Size.z);
            Add(p, rng);
        }
    }

    static Vector3 Random3(System.Random rng)
    {
        Vector3 v;
        do v = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
        while (v.sqrMagnitude > 1f || v.sqrMagnitude < 0.01f);
        return v.normalized;
    }

    void Add(Vector3 p, System.Random rng)
    {
        if (p.y > -0.9f || !volume.Contains(p) || volume.IsProtected(p) || !volume.Solid(p)) return;
        int stratum = volume.StratumAt(p);
        float total = 0f;
        foreach (var k in kinds) total += Mathf.Max(0f, k.weightByStratum[stratum]);
        if (total <= 0f) return;
        float pick = (float)rng.NextDouble() * total;
        MineralData kind = kinds[0];
        foreach (var k in kinds)
        {
            pick -= Mathf.Max(0f, k.weightByStratum[stratum]);
            if (pick <= 0f) { kind = k; break; }
        }
        spots.Add(new Spot { position = p, kind = kind, rotation = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, 0f) });
    }

    // Called when a scoop has opened (DigGame): minerals it reached pop out.
    public void Freed(Vector3 center, float radius, Vector3 toward)
    {
        float r = radius + freeMargin;
        foreach (var s in spots)
        {
            if (s.taken || s.loose != null) continue;
            if ((s.position - center).sqrMagnitude > r * r) continue;
            if (volume.Solid(s.position) && (s.position - center).sqrMagnitude > radius * radius * 0.7f) continue;   // still held by earth
            PopOut(s, toward);
        }
    }

    void PopOut(Spot s, Vector3 toward)
    {
        if (s.shown == null) Show(s);
        var body = s.shown.AddComponent<Rigidbody>();
        body.mass = 0.3f;
        body.linearDamping = 0.2f;
        body.angularDamping = 1.5f;
        // Sized in world metres: the model's own units can be anything (a 0.5-unit sphere
        // on a Meshy model was 3 m across and choked the physics).
        var col = s.shown.AddComponent<SphereCollider>();
        col.radius = s.kind.size * 0.4f / Mathf.Max(0.0001f, s.shown.transform.lossyScale.x);
        col.center = s.shown.transform.InverseTransformPoint(Bounds(s.shown).center);
        int layer = LayerMask.NameToLayer("Debris");
        if (layer >= 0) s.shown.layer = layer;
        body.linearVelocity = (toward.normalized * 0.6f + Vector3.up * 1.4f) + Random.insideUnitSphere * 0.3f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.angularVelocity = Random.insideUnitSphere * 6f;
        s.loose = body;
        loose.Add(s);
    }

    void Show(Spot s)
    {
        var go = Instantiate(s.kind.model, s.position, s.rotation, transform);
        go.name = s.kind.title;
        var b = Bounds(go);
        float scale = s.kind.size / Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.y, b.size.z));
        go.transform.localScale *= scale;
        b = Bounds(go);
        go.transform.position += s.position - b.center;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (glintPrefab != null)
        {
            var glint = Instantiate(glintPrefab, go.transform);
            glint.transform.localPosition = Vector3.zero;
            glint.transform.localScale = Vector3.one / Mathf.Max(0.001f, scale);
            var main = glint.main;
            main.startColor = s.kind.glint;
        }
        s.shown = go;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    void Update()
    {
        var game = DigGame.I;
        if (game == null) return;
        Vector3 player = game.player.transform.position;

        // Show what is near, hide what is far (a few times a second is plenty).
        checkTimer -= Time.deltaTime;
        if (checkTimer <= 0f)
        {
            checkTimer = 0.25f;
            float show2 = showDistance * showDistance;
            foreach (var s in spots)
            {
                if (s.taken || s.loose != null) continue;
                bool near = (s.position - player).sqrMagnitude < show2;
                if (near && s.shown == null) Show(s);
                else if (!near && s.shown != null) { Destroy(s.shown); s.shown = null; }
            }
        }

        // Loose minerals near the player go into the bag.
        for (int i = loose.Count - 1; i >= 0; i--)
        {
            var s = loose[i];
            if (s.shown == null) { loose.RemoveAt(i); continue; }
            if (s.shown.transform.position.y < DigVolume.Min.y - 2f) { s.taken = true; Destroy(s.shown); loose.RemoveAt(i); continue; }
            if ((s.shown.transform.position - player).sqrMagnitude > pickupDistance * pickupDistance) continue;
            if (!game.TryBag(s.kind)) continue;
            s.taken = true;
            Destroy(s.shown);
            loose.RemoveAt(i);
        }
    }

    // Demo jumps: minerals inside a freshly dug tunnel would hang in the air.
    public void ClearInside(Vector3 a, float radius)
    {
        foreach (var s in spots)
            if (!s.taken && s.loose == null && (s.position - a).sqrMagnitude < radius * radius && !volume.Solid(s.position))
                PopOut(s, Vector3.up);
    }
}
