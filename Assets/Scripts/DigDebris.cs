using UnityEngine;

// The earth that comes out of the ground.
//   Clods: a capped pool of rigid bodies in many sizes, wearing the same photoscanned
//          earth as the ground. They arc, bounce, roll and pile up; big ones that land
//          hard break into smaller pieces.
//   Crumbs: a particle system of tiny grains that fall and scatter.
//   Dust: a soft puff on every impact.
// The pool is runtime state. The meshes, materials, physics material and both particle
// systems are assets and scene objects (made by DigIn/Build Scene), referenced here.
// Sits on "Dig Site/Debris".
public class DigDebris : MonoBehaviour
{
    [Header("Clods")]
    public Mesh[] clodMeshes;
    [Tooltip("One per stratum.")]
    public Material[] clodMaterials;
    public PhysicsMaterial clodPhysics;
    [Tooltip("Most clods alive at once. The oldest is reused when the pool runs out.")]
    public int maxClods = 420;
    [Tooltip("Smallest and largest clod (m). Most are small: sizes are skewed toward the first value.")]
    public Vector2 clodSize = new Vector2(0.035f, 0.2f);
    [Tooltip("How quickly a rolling clod comes to rest. Low values let them roll across the yard.")]
    public float rollDamping = 3.5f;

    [Header("Settling into the heap")]
    [Tooltip("Where settled clods go: the ground volume. Empty = clods just stay where they are.")]
    public DigVolume volume;
    [Tooltip("Seconds a clod lies still before it sinks into the heap.")]
    public float settleDelay = 1.2f;
    [Tooltip("Loose earth takes more room than packed earth.")]
    public float bulking = 1.6f;
    [Tooltip("Heaps only build on the yard's surface, above this height. Below it (in the hole and the tunnels) settled earth just sinks away, so nothing fills back up.")]
    public float heapBelow = -0.35f;
    [Tooltip("Clods this close to one that settles join the heap with it (m).")]
    public float absorbRadius = 0.45f;

    [Header("Breaking apart")]
    [Tooltip("Clods at least this big (m) can break when they land.")]
    public float breakSize = 0.1f;
    [Tooltip("Landing speed (m/s) that breaks a clod.")]
    public float breakSpeed = 2.6f;
    [Range(0f, 1f)] public float breakChance = 0.6f;

    [Header("Particles")]
    public ParticleSystem crumbs;
    public ParticleSystem dust;
    [Tooltip("Crumb and dust colour per stratum.")]
    public Color[] stratumColors =
    {
        new Color(0.30f, 0.21f, 0.14f),
        new Color(0.40f, 0.24f, 0.16f),
        new Color(0.33f, 0.30f, 0.27f),
    };

    [Header("How much comes out")]
    public int impactCrumbs = 16;
    public int impactClods = 3;
    public int tossClods = 14;
    public int tossCrumbs = 18;
    [Tooltip("Speed of tossed earth (m/s). Lower lands it closer, in a pile.")]
    public Vector2 tossSpeed = new Vector2(1.5f, 2.7f);
    [Tooltip("Most clods made from one piece of collapsing earth.")]
    public int maxCollapseClods = 28;

    class Clod
    {
        public Transform transform;
        public Rigidbody body;
        public SphereCollider collider;
        public MeshFilter filter;
        public MeshRenderer renderer;
        public Vector3 shape;      // full-size scale
        public float size, age;
        public int stratum;
        public bool landed;
        public float still;        // seconds at rest
        public float sinking;      // > 0 while it melts into the heap
    }

    Clod[] clods;
    int next;
    int debrisLayer;
    float flushTimer;
    bool depositsPending;

    void Awake()
    {
        debrisLayer = LayerMask.NameToLayer("Debris");
        if (debrisLayer < 0) debrisLayer = 2;   // "Ignore Raycast": still out of the way of aiming
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0) Physics.IgnoreLayerCollision(debrisLayer, playerLayer, true);

        clods = new Clod[maxClods];
        for (int i = 0; i < maxClods; i++)
        {
            var go = new GameObject("Clod") { layer = debrisLayer };
            go.transform.SetParent(transform, false);
            var c = new Clod { transform = go.transform };
            c.filter = go.AddComponent<MeshFilter>();
            c.renderer = go.AddComponent<MeshRenderer>();
            c.collider = go.AddComponent<SphereCollider>();
            c.collider.sharedMaterial = clodPhysics;
            c.body = go.AddComponent<Rigidbody>();
            c.body.linearDamping = 0.08f;
            c.body.angularDamping = rollDamping;
            c.body.maxDepenetrationVelocity = 2f;
            var tag = go.AddComponent<DigClodBody>();
            tag.owner = this;
            tag.index = i;
            go.SetActive(false);
            clods[i] = c;
        }
    }

    Color Tint(int stratum, float alpha)
    {
        var c = stratumColors[Mathf.Clamp(stratum, 0, stratumColors.Length - 1)];
        c.a = alpha;
        return c;
    }

    // ---- Events --------------------------------------------------------------------
    // The blade hits the ground: crumbs spray back, a few small clods jump, dust puffs.
    public void Impact(Vector3 point, Vector3 normal, Vector3 toward, int stratum)
    {
        Vector3 dir = (normal + toward * 0.6f + Vector3.up * 0.5f).normalized;
        EmitCrumbs(point + normal * 0.05f, dir, 2.4f, 0.75f, stratum, impactCrumbs);
        for (int i = 0; i < impactClods; i++)
            Throw(point + normal * 0.08f + Random.insideUnitSphere * 0.12f, dir * Random.Range(1.2f, 2.4f) + Random.insideUnitSphere * 0.9f, stratum, 0.6f);
        Puff(point + normal * 0.1f, normal, stratum, 5, 1f);
    }

    // The shovel flicks its load away: the bulk of the earth, thrown in an arc.
    public void Toss(Vector3 from, Vector3 direction, int stratum, float amount)
    {
        direction.Normalize();
        int n = Mathf.RoundToInt(tossClods * amount);
        for (int i = 0; i < n; i++)
            Throw(from + Random.insideUnitSphere * 0.1f, direction * Random.Range(tossSpeed.x, tossSpeed.y) + Random.insideUnitSphere * 0.6f, stratum, 1f);
        EmitCrumbs(from, direction, tossSpeed.y, 0.5f, stratum, Mathf.RoundToInt(tossCrumbs * amount));
        Puff(from, direction, stratum, 2, 0.6f);
    }

    // A piece of the rim gives way.
    public void Crumble(Vector3 point, int stratum)
    {
        EmitCrumbs(point, Vector3.down, 0.4f, 1f, stratum, 6);
        if (Random.value < 0.7f) Throw(point, Random.insideUnitSphere * 0.4f, stratum, 0.7f);
        Puff(point, Vector3.up, stratum, 1, 0.5f);
    }

    // Earth that lost its hold (DigVolume.CollapseLoose): it falls as it was, in lumps.
    public void Collapse(System.Collections.Generic.List<Vector3> voxels, int stratum)
    {
        // Pick lumps spread over the piece; a big piece makes bigger lumps.
        int lumps = Mathf.Clamp(voxels.Count / 2, 1, maxCollapseClods);
        float scale = Mathf.Clamp(Mathf.Sqrt(voxels.Count / (float)lumps) * 0.9f, 0.7f, 1.6f);
        int step = Mathf.Max(1, voxels.Count / lumps);
        for (int i = 0; i < voxels.Count; i += step)
            Throw(voxels[i] + Random.insideUnitSphere * 0.05f, Random.insideUnitSphere * 0.3f, stratum, scale);
        Vector3 mid = voxels[voxels.Count / 2];
        EmitCrumbs(mid, Vector3.down, 0.5f, 1f, stratum, Mathf.Min(24, 4 + voxels.Count));
        Puff(mid, Vector3.down, stratum, Mathf.Min(4, 1 + voxels.Count / 8), 0.8f);
    }

    // The blade glances off ground it cannot dig: chips and a little dust, no earth.
    public void Chips(Vector3 point, Vector3 normal, int stratum)
    {
        EmitCrumbs(point + normal * 0.04f, normal, 3.2f, 0.9f, stratum, 6);
        Puff(point + normal * 0.08f, normal, stratum, 2, 0.6f);
    }

    // The ground under resting clods moved: let them fall.
    public void Wake(Vector3 point, float radius)
    {
        float r2 = radius * radius;
        for (int i = 0; i < clods.Length; i++)
        {
            var c = clods[i];
            if (c.transform.gameObject.activeSelf && (c.transform.position - point).sqrMagnitude < r2) c.body.WakeUp();
        }
    }

    // ---- Clods ---------------------------------------------------------------------
    void Throw(Vector3 position, Vector3 velocity, int stratum, float sizeScale)
    {
        // Mostly small, a few big: a cubed roll skews it hard toward small.
        float roll = Random.value;
        Spawn(position, velocity, stratum, Mathf.Lerp(clodSize.x, clodSize.y, roll * roll * roll) * sizeScale);
    }

    void Spawn(Vector3 position, Vector3 velocity, int stratum, float size)
    {
        var c = clods[next];
        next = (next + 1) % clods.Length;

        c.size = size;
        c.age = 0f;
        c.still = 0f;
        c.sinking = 0f;
        c.body.isKinematic = false;
        c.stratum = stratum;
        c.landed = false;
        c.filter.sharedMesh = clodMeshes[Random.Range(0, clodMeshes.Length)];
        c.renderer.sharedMaterial = clodMaterials[Mathf.Clamp(stratum, 0, clodMaterials.Length - 1)];
        // Squashed and stretched a little, so no two lumps look alike.
        c.shape = new Vector3(Random.Range(0.8f, 1.25f), Random.Range(0.6f, 1f), Random.Range(0.8f, 1.25f)) * size;
        c.collider.radius = 0.4f;                        // in mesh units; the mesh is about 1 unit across
        c.transform.SetPositionAndRotation(position, Random.rotation);
        c.transform.localScale = c.shape * 0.3f;
        c.transform.gameObject.SetActive(true);

        c.body.mass = Mathf.Max(0.01f, 1500f * size * size * size);   // about the density of soil
        c.body.linearVelocity = velocity;
        c.body.angularVelocity = Random.insideUnitSphere * 9f;
    }

    // Called by DigClodBody when a clod hits something.
    public void Landed(int index, float speed)
    {
        var c = clods[index];
        if (c.landed) return;
        c.landed = true;      // only the first hard landing counts
        if (c.size < breakSize || speed < breakSpeed || Random.value > breakChance) return;

        // Break into two to four pieces that carry on in the same direction.
        Vector3 p = c.transform.position, v = c.body.linearVelocity * 0.5f;
        float size = c.size;
        int stratum = c.stratum;
        c.transform.gameObject.SetActive(false);

        int pieces = Random.Range(2, 5);
        float pieceSize = size / Mathf.Pow(pieces, 1f / 3f);
        for (int i = 0; i < pieces; i++)
        {
            Vector3 off = Random.insideUnitSphere * size * 0.35f;
            Spawn(p + off, v + off.normalized * Random.Range(0.4f, 1.1f) + Vector3.up * Random.Range(0.3f, 0.9f), stratum, pieceSize * Random.Range(0.75f, 1.1f));
            clods[(next + clods.Length - 1) % clods.Length].landed = true;   // pieces do not break again
        }
        EmitCrumbs(p, Vector3.up, 1.2f, 1f, stratum, 8);
        Puff(p, Vector3.up, stratum, 2, 0.6f);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < clods.Length; i++)
        {
            var c = clods[i];
            if (!c.transform.gameObject.activeSelf) continue;

            // Clods come out of the ground small and reach full size almost at once.
            if (c.age < 0.08f)
            {
                c.age += dt;
                c.transform.localScale = c.shape * Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(c.age / 0.08f));
                continue;
            }
            if (volume == null) continue;

            // Once it has lain still for a moment, the clod sinks into a heap of real
            // earth: its volume is added to the ground where it lies.
            if (c.sinking > 0f)
            {
                c.sinking -= dt;
                c.transform.localScale = c.shape * Mathf.Clamp01(c.sinking / 0.5f);
                c.transform.position += Vector3.down * (c.size * 0.4f * dt);
                if (c.sinking <= 0f) c.transform.gameObject.SetActive(false);
                continue;
            }
            // Anything that slipped out of the world is recycled.
            if (c.transform.position.y < DigVolume.Min.y - 2f) { c.transform.gameObject.SetActive(false); continue; }

            bool resting = c.body.IsSleeping() || c.body.linearVelocity.sqrMagnitude < 0.01f;
            c.still = resting ? c.still + dt : 0f;
            if (c.still < settleDelay || !volume.Contains(c.transform.position)) continue;

            Vector3 p = c.transform.position;
            if (Physics.Raycast(p + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 0.6f, Physics.DefaultRaycastLayers & ~(1 << debrisLayer), QueryTriggerInteraction.Ignore)
                && volume.Owns(hit.collider) && hit.point.y > heapBelow && hit.normal.y > 0.5f && !NearPlayer(hit.point))
            {
                // The heap rises where it lies, so every clod resting there joins it too:
                // left alone, they would end up inside the new earth and drop through it.
                float v = 0f;
                for (int k = 0; k < clods.Length; k++)
                {
                    var o = clods[k];
                    if (!o.transform.gameObject.activeSelf || o.sinking > 0f || o.age < 0.08f) continue;
                    if ((o.transform.position - p).sqrMagnitude > absorbRadius * absorbRadius) continue;
                    v += o.shape.x * o.shape.y * o.shape.z * 0.42f * bulking;   // about the volume of the lump
                    o.body.isKinematic = true;
                    o.sinking = 0.5f;
                }
                volume.Deposit(hit.point + Vector3.up * (c.size * 0.15f), Mathf.Max(v, 0.0005f));
                depositsPending = true;
            }
            c.body.isKinematic = true;
            if (c.sinking <= 0f) c.sinking = 0.5f;
        }

        // Rebuild the heap's mesh and colliders a few times a second, not per clod.
        flushTimer -= dt;
        if (depositsPending && flushTimer <= 0f)
        {
            volume.Flush(true);
            depositsPending = false;
            flushTimer = 0.25f;
        }
    }

    // Earth must not pile up inside the player.
    bool NearPlayer(Vector3 p)
    {
        var player = DigGame.I != null ? DigGame.I.player : null;
        if (player == null) return false;
        Vector3 d = p - player.transform.position;
        return new Vector2(d.x, d.z).sqrMagnitude < 0.75f * 0.75f && Mathf.Abs(d.y) < 1.6f;
    }

    // ---- Particles -----------------------------------------------------------------
    void EmitCrumbs(Vector3 position, Vector3 direction, float speed, float spread, int stratum, int count)
    {
        if (crumbs == null || count <= 0) return;
        var e = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            e.position = position + Random.insideUnitSphere * 0.1f;
            e.velocity = (direction + Random.insideUnitSphere * spread).normalized * (speed * Random.Range(0.4f, 1.1f));
            e.startColor = Tint(stratum, 1f) * Random.Range(0.75f, 1.1f);
            float r = Random.value;
            e.startSize = Mathf.Lerp(0.008f, 0.035f, r * r);
            e.startLifetime = Random.Range(1.4f, 2.6f);
            e.rotation3D = Random.insideUnitSphere * 180f;
            crumbs.Emit(e, 1);
        }
    }

    void Puff(Vector3 position, Vector3 direction, int stratum, int count, float size)
    {
        if (dust == null) return;
        // Unlit dust would glow in a dark tunnel, so it takes its brightness from the ambient light.
        float light = Mathf.Clamp01(RenderSettings.ambientEquatorColor.maxColorComponent * 1.6f + 0.12f);
        var e = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            e.position = position + Random.insideUnitSphere * 0.12f;
            e.velocity = direction * Random.Range(0.15f, 0.5f) + Random.insideUnitSphere * 0.25f + Vector3.up * 0.1f;
            var c = Tint(stratum, Random.Range(0.10f, 0.22f)) * 1.5f;
            e.startColor = new Color(c.r * light, c.g * light, c.b * light, c.a);
            e.startSize = Random.Range(0.35f, 0.7f) * size;
            e.startLifetime = Random.Range(0.9f, 1.6f);
            e.rotation = Random.Range(0f, 360f);
            dust.Emit(e, 1);
        }
    }
}
