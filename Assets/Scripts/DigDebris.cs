using UnityEngine;

// The earth that comes out of the ground.
//   Clods: a capped pool of small rigid bodies that arc, bounce, roll and pile up.
//   Crumbs: a particle system of tiny mesh grains that fall and scatter.
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
    public Vector2 clodSize = new Vector2(0.06f, 0.16f);
    [Tooltip("How quickly a rolling clod comes to rest. Low values let them roll across the yard.")]
    public float rollDamping = 3.5f;

    [Header("Particles")]
    public ParticleSystem crumbs;
    public ParticleSystem dust;
    [Tooltip("Crumb and dust colour per stratum.")]
    public Color[] stratumColors =
    {
        new Color(0.33f, 0.23f, 0.15f),
        new Color(0.46f, 0.27f, 0.18f),
        new Color(0.36f, 0.35f, 0.36f),
    };

    [Header("How much comes out")]
    public int impactCrumbs = 14;
    public int impactClods = 3;
    public int tossClods = 12;
    public int tossCrumbs = 14;
    [Tooltip("Speed of tossed earth (m/s). Lower lands it closer, in a pile.")]
    public Vector2 tossSpeed = new Vector2(1.5f, 2.7f);

    class Clod
    {
        public Transform transform;
        public Rigidbody body;
        public SphereCollider collider;
        public MeshFilter filter;
        public MeshRenderer renderer;
        public float size, age;
    }

    Clod[] clods;
    int next;
    int debrisLayer;

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
    // The blade hits the ground: crumbs spray back, a few clods jump, dust puffs.
    public void Impact(Vector3 point, Vector3 normal, Vector3 toward, int stratum)
    {
        Vector3 dir = (normal + toward * 0.6f + Vector3.up * 0.5f).normalized;
        EmitCrumbs(point + normal * 0.05f, dir, 2.4f, 0.75f, stratum, impactCrumbs);
        for (int i = 0; i < impactClods; i++)
            Throw(point + normal * 0.08f + Random.insideUnitSphere * 0.12f, dir * Random.Range(1.2f, 2.4f) + Random.insideUnitSphere * 0.9f, stratum, 0.75f);
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
        EmitCrumbs(point, Vector3.down, 0.4f, 1f, stratum, 5);
        if (Random.value < 0.6f) Throw(point, Random.insideUnitSphere * 0.4f, stratum, 0.6f);
        Puff(point, Vector3.up, stratum, 1, 0.5f);
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
        var c = clods[next];
        next = (next + 1) % clods.Length;

        // Mostly small, a few big: squaring the roll skews it.
        float roll = Random.value;
        c.size = Mathf.Lerp(clodSize.x, clodSize.y, roll * roll) * sizeScale;
        c.age = 0f;
        c.filter.sharedMesh = clodMeshes[Random.Range(0, clodMeshes.Length)];
        c.renderer.sharedMaterial = clodMaterials[Mathf.Clamp(stratum, 0, clodMaterials.Length - 1)];
        c.collider.radius = 0.42f;                       // in mesh units; the mesh is about 1 unit across
        c.transform.SetPositionAndRotation(position, Random.rotation);
        c.transform.localScale = Vector3.one * (c.size * 0.3f);
        c.transform.gameObject.SetActive(true);

        c.body.mass = Mathf.Max(0.02f, 1600f * c.size * c.size * c.size);   // about the density of soil
        c.body.linearVelocity = velocity;
        c.body.angularVelocity = Random.insideUnitSphere * 9f;
    }

    void Update()
    {
        // Clods pop out of the blade small and reach full size almost at once.
        for (int i = 0; i < clods.Length; i++)
        {
            var c = clods[i];
            if (c.age >= 0.08f || !c.transform.gameObject.activeSelf) continue;
            c.age += Time.deltaTime;
            c.transform.localScale = Vector3.one * (c.size * Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(c.age / 0.08f)));
        }
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
            e.startColor = Tint(stratum, 1f) * Random.Range(0.8f, 1.1f);
            e.startSize = Random.Range(0.012f, 0.04f);
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
