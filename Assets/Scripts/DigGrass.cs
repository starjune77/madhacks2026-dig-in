using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Real grass on the diggable yard. The yard's ground is the dig volume, not Unity Terrain,
// so terrain detail grass cannot grow there: this places the same detail meshes (from
// the Unity Terrain demo pack) on the volume's surface at start, draws them instanced,
// and removes every clump on ground that gets dug or covered with spoil.
// Sits on "Dig Site/Grass". Meshes and materials are assets set in the scene.
public class DigGrass : MonoBehaviour
{
    [System.Serializable]
    public class Kind
    {
        public Mesh mesh;
        public Material material;
        [Tooltip("Relative share of this kind among all clumps.")]
        public float weight = 1f;
        public Vector2 scale = new Vector2(0.8f, 1.2f);
    }

    public DigVolume volume;
    public Kind[] kinds;
    [Tooltip("Area to cover (world x, z min and max).")]
    public Rect area = new Rect(-13f, -16f, 31f, 27f);
    [Tooltip("Clumps per square metre.")]
    public float density = 1.6f;
    [Tooltip("No grass within this distance of the start of the dig (the bare patch).")]
    public float bareRadius = 2.4f;
    public Vector2 bareCentre = new Vector2(2.5f, 2.5f);
    [Tooltip("Clumps are only drawn within this distance of the camera.")]
    public float drawDistance = 60f;
    public int seed = 7;

    struct Clump { public Vector3 position; public int kind; public Matrix4x4 matrix; }

    readonly List<Clump> clumps = new List<Clump>();
    List<Matrix4x4>[] batches;
    bool dirty = true;
    RenderParams[] renderParams;

    void Start()
    {
        var rng = new System.Random(seed);
        float total = 0f;
        foreach (var k in kinds) total += k.weight;
        int count = Mathf.RoundToInt(area.width * area.height * density);
        int mask = DigGame.AimMask;
        for (int i = 0; i < count; i++)
        {
            float x = area.xMin + (float)rng.NextDouble() * area.width;
            float z = area.yMin + (float)rng.NextDouble() * area.height;
            if (Vector2.Distance(new Vector2(x, z), bareCentre) < bareRadius + (float)rng.NextDouble() * 0.8f) continue;
            if (!Physics.Raycast(new Vector3(x, 6f, z), Vector3.down, out RaycastHit hit, 12f, mask, QueryTriggerInteraction.Ignore)) continue;
            if (volume != null && !volume.Owns(hit.collider)) continue;   // on a prop, a path, the house
            if (hit.normal.y < 0.85f) continue;

            float pick = (float)rng.NextDouble() * total;
            int kind = 0;
            for (; kind < kinds.Length - 1; kind++) { pick -= kinds[kind].weight; if (pick <= 0f) break; }
            var k = kinds[kind];
            float s = Mathf.Lerp(k.scale.x, k.scale.y, (float)rng.NextDouble());
            var rot = Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, hit.normal, 0.5f)) * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            clumps.Add(new Clump { position = hit.point, kind = kind, matrix = Matrix4x4.TRS(hit.point - Vector3.up * 0.02f, rot, Vector3.one * s) });
        }

        batches = new List<Matrix4x4>[kinds.Length];
        renderParams = new RenderParams[kinds.Length];
        for (int k = 0; k < kinds.Length; k++)
        {
            batches[k] = new List<Matrix4x4>();
            renderParams[k] = new RenderParams(kinds[k].material)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                worldBounds = new Bounds(new Vector3(area.center.x, 0f, area.center.y), new Vector3(area.width, 10f, area.height)),
            };
        }
        if (volume != null) volume.Disturbed += OnDisturbed;
    }

    void OnDestroy()
    {
        if (volume != null) volume.Disturbed -= OnDisturbed;
    }

    // The ground at `point` was dug or covered: the grass there is gone.
    void OnDisturbed(Vector3 point, float radius)
    {
        float r2 = radius * radius;
        int before = clumps.Count;
        clumps.RemoveAll(c => { Vector3 d = c.position - point; d.y = 0f; return d.sqrMagnitude < r2; });
        if (clumps.Count != before) dirty = true;
    }

    void Update()
    {
        if (batches == null) return;
        var cam = Camera.main;
        if (cam == null) return;

        // Re-sort into batches only when grass was removed or the camera moved a lot.
        if (dirty || (cam.transform.position - lastCamera).sqrMagnitude > 4f)
        {
            dirty = false;
            lastCamera = cam.transform.position;
            float d2 = drawDistance * drawDistance;
            foreach (var b in batches) b.Clear();
            foreach (var c in clumps)
                if ((c.position - lastCamera).sqrMagnitude < d2) batches[c.kind].Add(c.matrix);
        }

        for (int k = 0; k < kinds.Length; k++)
        {
            var list = batches[k];
            for (int start = 0; start < list.Count; start += 1023)
                Graphics.RenderMeshInstanced(renderParams[k], kinds[k].mesh, 0, list, Mathf.Min(1023, list.Count - start), start);
        }
    }

    Vector3 lastCamera = new Vector3(1e6f, 0f, 0f);
}
