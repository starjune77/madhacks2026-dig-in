using System.Collections.Generic;
using UnityEngine;

// Minerals in the ground. Their spots are fixed (a seed), mostly near the life path, and
// which kind lies where depends on the stratum. Near the player each one is shown where
// it lies, inside the earth: only the part a wall cuts through shows, with a faint glint,
// so they can be spotted. A scoop that reaches one pops it out and it comes to rest on the
// ground, glinting. Aim at it within reach and it lights up softly; E picks it up (it lifts
// toward you and is gone into the bag). Sits on "Dig Site/Minerals"; the kinds and the
// glint prefab are assets.
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

    [Header("A generous start, and never too long without a find")]
    [Tooltip("Extra spots per metre along the first stretch of the path (common, shallow).")]
    public float earlyPerMetre = 4f;
    public float earlyLength = 14f;
    [Tooltip("Scoops before the very first find is guaranteed.")]
    public int firstFindScoops = 7;
    [Tooltip("Scoops in a row without a find before the next one brings one, per stratum (deeper is rarer).")]
    public int[] pityScoops = { 4, 6, 7 };
    int dryScoops;
    bool foundAny;

    [Header("Showing and picking up")]
    [Tooltip("Every specimen is this many times its kind's size, so it reads from 2-3 m away.")]
    public float sizeScale = 2f;
    [Tooltip("Glint: bigger and more frequent sparkles than the prefab's.")]
    public float glintSize = 1.8f;
    public float glintRate = 1.6f;
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
        public Vector3 poppedAt;     // where it came out (open air), to catch it if it slips through
        public float poppedTime;
        public bool taken;
    }

    readonly List<Spot> spots = new List<Spot>();
    readonly List<Spot> loose = new List<Spot>();
    float checkTimer;

    // Picking up: the one under the crosshair, a soft light on it, and the ones lifting away.
    Spot aimed;
    Light highlight;
    readonly List<(Transform t, Vector3 from, float time)> lifting = new List<(Transform, Vector3, float)>();

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
        UpdateHighlight();
        return aimed?.kind;
    }

    // Picks up the aimed mineral: it lifts toward `to` and fades into the bag.
    public MineralData PickUp(Vector3 to)
    {
        var s = aimed;
        if (s == null || s.shown == null) return null;
        s.taken = true;
        loose.Remove(s);
        aimed = null;
        UpdateHighlight();
        var go = s.shown;
        if (s.loose != null) Destroy(s.loose);
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        lifting.Add((go.transform, go.transform.position, 0f));
        liftTarget = to;
        return s.kind;
    }
    Vector3 liftTarget;
    float looseTimer;

    void UpdateHighlight()
    {
        if (highlight == null)
        {
            highlight = new GameObject("Mineral Highlight").AddComponent<Light>();
            highlight.transform.SetParent(transform, false);
            highlight.type = LightType.Point;
            highlight.color = new Color(1f, 0.9f, 0.72f);
            highlight.range = 0.9f;
            highlight.intensity = 0f;
            highlight.shadows = LightShadows.None;
        }
        highlight.enabled = aimed != null && aimed.shown != null;
        if (highlight.enabled) highlight.transform.position = Bounds(aimed.shown).center + Vector3.up * 0.3f;
    }

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
        int early = Mathf.RoundToInt(earlyLength * earlyPerMetre);
        for (int i = 0; i < early; i++)
        {
            float d = Mathf.Lerp(0.5f, earlyLength, R());
            Vector3 p = path.PointAt(d) + Random3(rng) * spread * 0.8f * Mathf.Sqrt(R());
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

    // Called when a scoop has opened (DigGame): minerals it reached pop out. After a few
    // scoops in a row with nothing, this one brings one anyway (sooner for the very first).
    public void Freed(Vector3 center, float radius, Vector3 toward)
    {
        float r = radius + freeMargin;
        int freed = 0;
        foreach (var s in spots)
        {
            if (s.taken || s.loose != null) continue;
            if ((s.position - center).sqrMagnitude > r * r) continue;
            if (volume.Solid(s.position) && (s.position - center).sqrMagnitude > radius * radius * 0.7f) continue;   // still held by earth
            PopOut(s, toward);
            freed++;
        }
        if (freed > 0) { dryScoops = 0; foundAny = true; return; }
        dryScoops++;
        int stratum = Mathf.Clamp(volume.StratumAt(center), 0, pityScoops.Length - 1);
        if (dryScoops < (foundAny ? pityScoops[stratum] : firstFindScoops)) return;
        dryScoops = 0;
        foundAny = true;
        // In the open air of the fresh scoop, a little toward the player (never inside the earth).
        Vector3 at = center + toward.normalized * radius * 0.25f;
        if (volume.Solid(at)) at = center;
        Grant(at, toward, volume.StratumAt(center));
    }

    // A mineral of this stratum's kinds, out of the scoop just made.
    void Grant(Vector3 at, Vector3 toward, int stratum)
    {
        float total = 0f;
        foreach (var k in kinds) total += Mathf.Max(0f, k.weightByStratum[stratum]);
        if (total <= 0f) return;
        float pick = Random.value * total;
        MineralData kind = kinds[0];
        foreach (var k in kinds)
        {
            pick -= Mathf.Max(0f, k.weightByStratum[stratum]);
            if (pick <= 0f) { kind = k; break; }
        }
        var s = new Spot { position = at, kind = kind, rotation = Random.rotation };
        spots.Add(s);
        PopOut(s, toward);
    }

    void PopOut(Spot s, Vector3 toward)
    {
        if (s.shown == null) Show(s);
        // Out into the open: a stone still inside the earth would sink through it.
        Vector3 c = Bounds(s.shown).center, p = c, dir = toward.sqrMagnitude > 0.0001f ? toward.normalized : Vector3.up;
        for (int k = 0; k < 24 && volume.Solid(p); k++) p += dir * 0.1f;
        s.shown.transform.position += p - c;
        var body = s.shown.AddComponent<Rigidbody>();
        body.mass = 0.3f;
        body.linearDamping = 0.2f;
        body.angularDamping = 1.5f;
        // Sized in world metres: the model's own units can be anything (a 0.5-unit sphere
        // on a Meshy model was 3 m across and choked the physics).
        var col = s.shown.AddComponent<SphereCollider>();
        col.radius = s.kind.size * sizeScale * 0.4f / Mathf.Max(0.0001f, s.shown.transform.lossyScale.x);
        col.center = s.shown.transform.InverseTransformPoint(Bounds(s.shown).center);
        int layer = LayerMask.NameToLayer("Debris");
        if (layer >= 0) s.shown.layer = layer;
        body.linearVelocity = (toward.normalized * 0.6f + Vector3.up * 1.4f) + Random.insideUnitSphere * 0.3f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.angularVelocity = Random.insideUnitSphere * 6f;
        s.loose = body;
        s.poppedAt = Bounds(s.shown).center;
        s.poppedTime = Time.time;
        loose.Add(s);
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
            var glint = Instantiate(glintPrefab, go.transform);
            glint.transform.localPosition = Vector3.zero;
            glint.transform.localScale = Vector3.one / Mathf.Max(0.001f, scale);
            var main = glint.main;
            main.startColor = s.kind.glint;
            main.startSizeMultiplier *= glintSize;
            main.maxParticles = 6;
            var emission = glint.emission;
            emission.rateOverTimeMultiplier *= glintRate;
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

        // Loose minerals rest where they fell until picked up.
        looseTimer -= Time.deltaTime;
        bool looseCheck = looseTimer <= 0f;
        if (looseCheck) looseTimer = 0.25f;
        for (int i = loose.Count - 1; i >= 0; i--)
        {
            var s = loose[i];
            if (s.shown == null) { loose.RemoveAt(i); continue; }
            // Earth tossed onto a resting stone (heaps), or fresh ground swapping its collider,
            // can leave it inside the soil, where it would sink. It comes up to the surface
            // of that earth instead, so a find is never lost.
            // Once it has come to rest it stays put (no physics): the ground under it is rebuilt
            // with every scoop nearby, and a sleeping body can drop through a swapped collider.
            if (s.loose != null && looseCheck)
            {
                var body = s.loose;
                Vector3 c = Bounds(s.shown).center, p = c;
                if (c.y < s.poppedAt.y - 2.5f) p = s.poppedAt;
                for (int k = 0; k < 40 && volume.Solid(p); k++) p += Vector3.up * 0.05f;
                if (p != c)
                {
                    body.isKinematic = true;
                    body.position += p - c + Vector3.up * 0.02f;
                }
                else if (!body.isKinematic && Time.time - s.poppedTime > 0.8f && body.linearVelocity.sqrMagnitude < 0.01f)
                    body.isKinematic = true;
                else if (body.isKinematic && !Physics.Raycast(c, Vector3.down, s.kind.size * sizeScale * 0.5f + 0.12f, DigGame.AimMask, QueryTriggerInteraction.Ignore))
                {
                    body.isKinematic = false;   // the ground under it was dug away: it falls again
                    body.WakeUp();
                }
            }
        }

        // The soft light on the aimed one pulses gently.
        if (highlight != null && highlight.enabled)
            highlight.intensity = 0.9f + 0.35f * Mathf.Sin(Time.time * 4f);

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

    // Demo jumps: minerals inside a freshly dug tunnel would hang in the air.
    public void ClearInside(Vector3 a, float radius)
    {
        foreach (var s in spots)
            if (!s.taken && s.loose == null && (s.position - a).sqrMagnitude < radius * radius && !volume.Solid(s.position))
                PopOut(s, Vector3.up);
    }
}
