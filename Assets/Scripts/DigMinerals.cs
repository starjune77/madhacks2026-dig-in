using System.Collections.Generic;
using UnityEngine;

// Minerals in the ground. Their spots are fixed (a seed), mostly near the life path, and
// which kind lies where depends on the stratum. Near the player each one is shown where
// it lies, inside the earth: only the part a wall cuts through shows, with a faint glint,
// so they can be spotted. A scoop that reaches one brings it out: it lies on the floor of
// that scoop, a little sunk in, glinting faintly (one per scoop at most, never thrown, never
// floating). Aim at it within reach and it brightens; E picks it up (it lifts toward you and
// is gone into the bag). Sits on "Dig Site/Minerals"; the kinds and the glint prefab are assets.
public class DigMinerals : MonoBehaviour
{
    public DigVolume volume;
    public DigPath path;
    public MineralData[] kinds;
    [Tooltip("A small sparkle, shown on each visible mineral.")]
    public ParticleSystem glintPrefab;   // also used by DigGame's hint toward the next memory

    [Header("Where they lie")]
    public int seed = 1234;
    [Tooltip("Spots per metre of life path.")]
    public float perMetre = 2.6f;
    [Tooltip("How far from the path they scatter (m).")]
    public float spread = 3.2f;
    [Tooltip("Extra spots anywhere in the volume, for those who wander.")]
    public int strays = 60;

    [Header("Showing and picking up")]
    [Tooltip("Every specimen is this many times its kind's size, so it reads from 2-3 m away.")]
    public float sizeScale = 2f;
    public float showDistance = 5f;
    [Tooltip("A scoop frees a mineral within its radius plus this much.")]
    public float freeMargin = 0.2f;
    [Tooltip("At most this many minerals lie loose in the world; the oldest go quietly.")]
    public int maxLoose = 8;

    [Header("How often (one per scoop at most)")]
    [Tooltip("The very first find is guaranteed by this scoop.")]
    public int firstFindBy = 6;
    [Tooltip("Scoops after a find before the next natural find can come, per stratum.")]
    public int[] minScoopsBetween = { 3, 4, 5 };
    [Tooltip("Scoops in a row without a find before the next scoop brings one, per stratum.")]
    public int[] pityAfter = { 5, 6, 7 };

    public class Spot
    {
        public Vector3 position;
        public Quaternion rotation;
        public MineralData kind;
        public GameObject shown;     // the specimen in the wall while near, or lying in the hole
        public bool isLoose;         // dug out, lying where it fell, waiting to be picked up
        public bool taken;
    }

    readonly List<Spot> spots = new List<Spot>();
    readonly List<Spot> loose = new List<Spot>();
    float checkTimer, looseTimer;
    int sinceFind;
    bool foundAny;

    // Picking up: the one under the crosshair (a little brighter) and the ones lifting away.
    Spot aimed, lit;
    MaterialPropertyBlock glow;
    readonly List<(Transform t, Vector3 from, float time)> lifting = new List<(Transform, Vector3, float)>();

    public int Count => spots.Count;

    // The loose mineral the player is aiming at within reach (null if none).
    public MineralData Aim(Ray ray, float reach)
    {
        aimed = null;
        float best = float.MaxValue;
        foreach (var s in loose)
        {
            if (s.shown == null || s.taken) continue;
            Vector3 c = Bounds(s.shown).center;
            Vector3 to = c - ray.origin;
            float along = Vector3.Dot(to, ray.direction);
            if (along < 0.2f || along > reach) continue;
            float off = (to - ray.direction * along).magnitude;
            float allowed = 0.12f + s.kind.size * sizeScale * 0.6f + along * 0.04f;   // a forgiving cone
            if (off > allowed || off / allowed + along * 0.01f > best) continue;
            best = off / allowed + along * 0.01f;
            aimed = s;
        }
        return aimed?.kind;
    }

    // Picks up the aimed mineral: it lifts toward the camera and is gone into the bag.
    public MineralData PickUp(Vector3 to)
    {
        var s = aimed;
        if (s == null || s.shown == null) return null;
        s.taken = true;
        loose.Remove(s);
        aimed = null;
        Light(null);
        foreach (var ps in s.shown.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        lifting.Add((s.shown.transform, s.shown.transform.position, 0f));
        return s.kind;
    }

    void Start()
    {
        var rng = new System.Random(seed);
        float R() => (float)rng.NextDouble();
        int n = Mathf.RoundToInt(path.Length * perMetre);
        for (int i = 0; i < n; i++)
        {
            float d = Mathf.Lerp(3f, path.Length - 1f, R());
            Add(path.PointAt(d) + Random3(rng) * spread * Mathf.Sqrt(R()), rng);
        }
        for (int i = 0; i < strays; i++)
            Add(DigVolume.Min + new Vector3(R() * DigVolume.Size.x, R() * (DigVolume.Size.y - 6f), R() * DigVolume.Size.z), rng);
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
        var kind = PickKind(volume.StratumAt(p), (float)rng.NextDouble());
        if (kind != null) spots.Add(new Spot { position = p, kind = kind, rotation = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, 0f) });
    }

    MineralData PickKind(int stratum, float roll)
    {
        float total = 0f;
        foreach (var k in kinds) total += Mathf.Max(0f, k.weightByStratum[stratum]);
        if (total <= 0f) return null;
        float pick = roll * total;
        foreach (var k in kinds)
        {
            pick -= Mathf.Max(0f, k.weightByStratum[stratum]);
            if (pick <= 0f) return k;
        }
        return kinds[kinds.Length - 1];
    }

    // Called when a scoop has opened (DigGame). At most one mineral comes out, and it lies
    // on the floor of this scoop: the nearest one the scoop reached (if it has been long
    // enough since the last find), or, after a few empty scoops, one anyway. Others the
    // scoop laid bare go quietly, so nothing hangs in the air.
    public void Freed(Vector3 center, float radius, Vector3 toward)
    {
        sinceFind++;
        int stratum = Mathf.Clamp(volume.StratumAt(center), 0, pityAfter.Length - 1);
        float r = radius + freeMargin;
        Spot nearest = null;
        float bestD = float.MaxValue;
        foreach (var s in spots)
        {
            if (s.taken || s.isLoose) continue;
            float d = (s.position - center).sqrMagnitude;
            if (d > r * r) continue;
            if (volume.Solid(s.position) && d > radius * radius * 0.7f) continue;   // still held by earth
            if (d < bestD) { bestD = d; nearest = s; }
        }
        bool natural = nearest != null && sinceFind > minScoopsBetween[stratum];
        bool pity = sinceFind >= (foundAny ? pityAfter[stratum] : firstFindBy);
        foreach (var s in spots)
        {
            if (s.taken || s.isLoose || (natural && s == nearest)) continue;
            if ((s.position - center).sqrMagnitude > r * r || volume.Solid(s.position)) continue;
            Vanish(s);   // laid bare but not this time's find: never left floating
        }
        if (!natural && !pity) return;
        Spot found = natural ? nearest : new Spot { kind = PickKind(volume.StratumAt(center), Random.value), rotation = Random.rotation };
        if (found.kind == null) return;
        if (!natural) spots.Add(found);
        Lay(found, center + Random.insideUnitSphere * radius * 0.25f);
        sinceFind = 0;
        foundAny = true;
    }

    void Vanish(Spot s)
    {
        s.taken = true;
        if (s.shown != null) Destroy(s.shown);
        s.shown = null;
    }

    // Puts a mineral on the open ground under `at` (the scoop's floor), slightly sunk in.
    void Lay(Spot s, Vector3 at)
    {
        s.position = at;
        if (s.shown == null) Show(s);
        Settle(s, at);
        s.isLoose = true;
        loose.Add(s);
        while (loose.Count > maxLoose) { Vanish(loose[0]); loose.RemoveAt(0); }
    }

    void Settle(Spot s, Vector3 from)
    {
        Vector3 floor = FloorUnder(OpenNear(from));
        float size = s.kind.size * sizeScale;
        Vector3 c = Bounds(s.shown).center;
        s.shown.transform.position += floor + Vector3.up * size * 0.3f - c;   // its lower part in the soil
    }

    // The nearest open point beside or below `p` (never above it); `p` if it is open.
    Vector3 OpenNear(Vector3 p)
    {
        if (!volume.Solid(p)) return p;
        for (float r = 0.1f; r <= 1.2f; r += 0.1f)
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                Vector3 side = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 q = p + side * r;
                if (!volume.Solid(q)) return q;
                q = p + (side * 0.7f + Vector3.down * 0.7f) * r;
                if (!volume.Solid(q)) return q;
            }
        return p;
    }

    // Straight down from an open point to where the earth begins.
    Vector3 FloorUnder(Vector3 p)
    {
        for (int k = 0; k < 120 && !volume.Solid(p - Vector3.up * 0.05f); k++) p -= Vector3.up * 0.05f;
        return p;
    }

    void Show(Spot s)
    {
        var go = Instantiate(s.kind.model, s.position, s.rotation, transform);
        go.name = s.kind.title;
        var b = Bounds(go);
        float scale = s.kind.size * sizeScale / Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.y, b.size.z));
        go.transform.localScale *= scale;
        b = Bounds(go);
        go.transform.position += s.position - b.center;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (glintPrefab != null)
        {
            // The prefab's own small, occasional sparkle, on the stone itself.
            var glint = Instantiate(glintPrefab, go.transform);
            glint.transform.localPosition = go.transform.InverseTransformPoint(Bounds(go).center);
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
        foreach (var r in rs) if (!(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds);
        return b;
    }

    // The aimed mineral is a touch brighter (no lights).
    void Light(Spot s)
    {
        if (lit == s) return;
        if (lit != null && lit.shown != null)
            foreach (var r in lit.shown.GetComponentsInChildren<MeshRenderer>()) r.SetPropertyBlock(null);
        lit = s;
        if (s == null || s.shown == null) return;
        if (glow == null) glow = new MaterialPropertyBlock();
        glow.SetColor("_BaseColor", new Color(1.35f, 1.3f, 1.2f));
        foreach (var r in s.shown.GetComponentsInChildren<MeshRenderer>()) r.SetPropertyBlock(glow);
    }

    void Update()
    {
        var game = DigGame.I;
        if (game == null) return;
        Vector3 player = game.player.transform.position;
        Light(aimed);

        // Show what is near in the walls, hide what is far (a few times a second is plenty).
        checkTimer -= Time.deltaTime;
        if (checkTimer <= 0f)
        {
            checkTimer = 0.25f;
            float show2 = showDistance * showDistance;
            foreach (var s in spots)
            {
                if (s.taken || s.isLoose) continue;
                bool near = (s.position - player).sqrMagnitude < show2;
                if (near && s.shown == null) Show(s);
                else if (!near && s.shown != null) { Destroy(s.shown); s.shown = null; }
            }
        }

        // Loose minerals stay where they lie. If earth lands on one, or the ground under it
        // is dug away, it settles again on the open ground beside or below.
        looseTimer -= Time.deltaTime;
        if (looseTimer <= 0f)
        {
            looseTimer = 0.25f;
            for (int i = loose.Count - 1; i >= 0; i--)
            {
                var s = loose[i];
                if (s.shown == null) { loose.RemoveAt(i); continue; }
                Vector3 c = Bounds(s.shown).center;
                float size = s.kind.size * sizeScale;
                bool buried = volume.Solid(c + Vector3.up * size * 0.25f);
                bool hanging = !volume.Solid(c - Vector3.up * (size * 0.5f + 0.08f));
                if (buried || hanging) Settle(s, c);
            }
        }

        // Picked up: a short lift toward the camera, shrinking away.
        for (int i = lifting.Count - 1; i >= 0; i--)
        {
            var (t, from, time) = lifting[i];
            if (t == null) { lifting.RemoveAt(i); continue; }
            time += Time.deltaTime / 0.3f;
            float e = Mathf.SmoothStep(0f, 1f, time);
            Vector3 target = game.player.cam.position + game.player.cam.forward * 0.35f - game.player.cam.up * 0.12f;
            t.position = Vector3.Lerp(from, target, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 0.12f;
            t.localScale *= 1f - Time.deltaTime * 2.2f * e;
            if (time >= 1f) { Destroy(t.gameObject); lifting.RemoveAt(i); }
            else lifting[i] = (t, from, time);
        }
    }

    // Demo jumps: minerals laid bare by a freshly dug tunnel would hang in the air; they go.
    public void ClearInside(Vector3 a, float radius)
    {
        foreach (var s in spots)
            if (!s.taken && !s.isLoose && (s.position - a).sqrMagnitude < radius * radius && !volume.Solid(s.position))
                Vanish(s);
    }
}
