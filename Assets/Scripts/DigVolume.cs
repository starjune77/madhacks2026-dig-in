using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

// The diggable ground: a density field (positive = earth) meshed with marching cubes,
// so the player can dig down, sideways and through curving tunnels.
//
//   48 x 40 x 48 m, 0.25 m voxels, 4 m chunks. Each chunk has its own mesh and MeshCollider
//   and is only rebuilt when something near it changes. Meshing and carving run in Burst.
//
// Sits on "Dig Site/Volume" at the world origin. The field, the chunk objects and their
// meshes are runtime state; the material and the references are scene data.
[DefaultExecutionOrder(-50)]
public class DigVolume : MonoBehaviour
{
    public const float Voxel = 0.25f;
    public const int ChunkCells = 16;
    public const int ChunksX = 12, ChunksY = 10, ChunksZ = 12;
    public const int Nx = ChunksX * ChunkCells + 1, Ny = ChunksY * ChunkCells + 1, Nz = ChunksZ * ChunkCells + 1;

    // World-space min corner and size. The setup script cuts the Unity Terrain's hole to match.
    public static readonly Vector3 Min = new Vector3(-22f, -36f, -22f);
    public static readonly Vector3 Size = new Vector3(ChunksX, ChunksY, ChunksZ) * (ChunkCells * Voxel);

    [Header("Scene")]
    [Tooltip("The starting surface is read from this terrain's heightmap.")]
    public Terrain surface;
    public Material material;
    public DigPath path;

    [Header("Strata")]
    [Tooltip("Depth (m) at which School Days and Growing Up begin.")]
    public Vector2 stratumDepth = new Vector2(10.5f, 21.5f);
    [Tooltip("How much the boundaries wobble (m).")]
    public float wobble = 1f;

    [Header("Where the ground may be dug")]
    [Tooltip("Near the surface, only this circle can be dug (the bare patch in the yard).")]
    public Vector2 patchCentre = new Vector2(2.5f, 2.5f);
    public float patchRadius = 4.5f;
    [Tooltip("Below this depth the whole volume is open.")]
    public float protectedDepth = 2f;
    [Tooltip("Thickness of the undiggable shell at the sides and bottom (m).")]
    public float shell = 1.5f;

    [Header("Shape of a scoop")]
    [Tooltip("How ragged the edge of a scoop is (fraction of its radius).")]
    public float scoopNoise = 0.16f;

    [Header("Life path look")]
    [Tooltip("Soil is fully warm within this distance of the path and normal beyond the second value.")]
    public Vector2 warmRange = new Vector2(1f, 5f);

    class Chunk
    {
        public GameObject go;
        public Mesh mesh;
        public MeshCollider collider;
        public int vertexCount;
    }

    NativeArray<float> density;
    NativeArray<sbyte> triTable, edgeA, edgeB;
    NativeArray<int> iota;
    Chunk[] chunks;
    readonly HashSet<int> dirty = new HashSet<int>();
    readonly HashSet<int> colliderDirty = new HashSet<int>();
    Texture3D pathTexture;
    Texture2D wobbleTexture;

    // Where the top has been dug or covered with spoil, so grass no longer grows there
    // (0..255 per 0.25 m column). Uploaded to the soil shader when it changes.
    Texture2D disturbTexture;
    NativeArray<byte> disturbed;
    bool disturbDirty;

    // ---- Queries -------------------------------------------------------------------
    static float WobbleAt(float x, float z, float amount)
    {
        return noise.snoise(new float2(x, z) * 0.07f) * amount;
    }

    static int Stratum(float3 p, float2 boundaries, float wobbleAmount)
    {
        float depth = -p.y + WobbleAt(p.x, p.z, wobbleAmount);
        return depth < boundaries.x ? 0 : depth < boundaries.y ? 1 : 2;
    }

    public int StratumAt(Vector3 p)
    {
        return Stratum(p, stratumDepth, wobble);
    }

    static bool Protected(float3 p, float3 innerMin, float3 innerMax, float2 patch, float patchRadius, float protectedDepth)
    {
        if (math.any(p < innerMin) || math.any(p.xz > innerMax.xz)) return true;
        return p.y > -protectedDepth && math.distance(p.xz, patch) > patchRadius;
    }

    // True where the ground cannot be dug: the outer shell, and the surface outside the bare patch.
    public bool IsProtected(Vector3 p)
    {
        return Protected(p, (float3)Min + shell, (float3)(Min + Size) - shell, patchCentre, patchRadius, protectedDepth);
    }

    public bool Owns(Collider c)
    {
        return c != null && c.transform.parent == transform;
    }

    public bool Contains(Vector3 p)
    {
        return p.x >= Min.x && p.y >= Min.y && p.z >= Min.z && p.x <= Min.x + Size.x && p.y <= Min.y + Size.y && p.z <= Min.z + Size.z;
    }

    static int Index(int x, int y, int z)
    {
        return x + Nx * (y + Ny * z);
    }

    // True if `p` is inside earth.
    public bool Solid(Vector3 p)
    {
        if (!Contains(p)) return false;
        Vector3 g = (p - Min) / Voxel;
        return density[Index(Mathf.RoundToInt(g.x), Mathf.RoundToInt(g.y), Mathf.RoundToInt(g.z))] > 0f;
    }

    // ---- Start ---------------------------------------------------------------------
    void Awake()
    {
        density = new NativeArray<float>(Nx * Ny * Nz, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        triTable = new NativeArray<sbyte>(MarchTables.Triangulation, Allocator.Persistent);
        edgeA = new NativeArray<sbyte>(MarchTables.EdgeA, Allocator.Persistent);
        edgeB = new NativeArray<sbyte>(MarchTables.EdgeB, Allocator.Persistent);
        iota = new NativeArray<int>(ChunkCells * ChunkCells * ChunkCells * 15, Allocator.Persistent);
        for (int i = 0; i < iota.Length; i++) iota[i] = i;
        chunks = new Chunk[ChunksX * ChunksY * ChunksZ];

        // The starting ground follows the terrain's heightmap (which still has heights under its hole).
        var heights = new NativeArray<float>(Nx * Nz, Allocator.TempJob);
        float baseY = surface != null ? surface.transform.position.y : 0f;
        for (int z = 0; z < Nz; z++)
        for (int x = 0; x < Nx; x++)
        {
            var w = new Vector3(Min.x + x * Voxel, 0f, Min.z + z * Voxel);
            heights[x + Nx * z] = surface != null ? surface.SampleHeight(w) + baseY : 0f;
        }
        new FillJob { heights = heights, density = density, minY = Min.y }.Schedule(Nz, 8).Complete();
        heights.Dispose();

        SetShaderGlobals();

        var flags = new NativeArray<bool>(chunks.Length, Allocator.TempJob);
        new ScanJob { density = density, flags = flags }.Schedule(chunks.Length, 16).Complete();
        for (int i = 0; i < flags.Length; i++) if (flags[i]) dirty.Add(i);
        flags.Dispose();
        Flush(true);
    }

    void OnDestroy()
    {
        if (density.IsCreated) density.Dispose();
        if (triTable.IsCreated) triTable.Dispose();
        if (edgeA.IsCreated) edgeA.Dispose();
        if (edgeB.IsCreated) edgeB.Dispose();
        if (iota.IsCreated) iota.Dispose();
        if (pathTexture != null) Destroy(pathTexture);
        if (wobbleTexture != null) Destroy(wobbleTexture);
        if (disturbTexture != null) Destroy(disturbTexture);
        if (disturbed.IsCreated) disturbed.Dispose();
        if (chunks != null)
            foreach (var c in chunks)
                if (c != null && c.mesh != null) Destroy(c.mesh);
    }

    // What the soil shader needs to know about the volume: where it is, how the strata
    // wobble (the same function hardness uses) and how close each spot is to the life path.
    void SetShaderGlobals()
    {
        Shader.SetGlobalVector("_DigVolumeMin", Min);
        Shader.SetGlobalVector("_DigVolumeSize", Size);
        Shader.SetGlobalVector("_DigStrata", new Vector4(stratumDepth.x, stratumDepth.y, 0f, 0f));

        const int wr = 128;
        wobbleTexture = new Texture2D(wr, wr, TextureFormat.RFloat, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Dig Strata Wobble" };
        var wdata = new NativeArray<float>(wr * wr, Allocator.Temp);
        for (int z = 0; z < wr; z++)
        for (int x = 0; x < wr; x++)
            wdata[x + wr * z] = WobbleAt(Min.x + (x + 0.5f) / wr * Size.x, Min.z + (z + 0.5f) / wr * Size.z, wobble);
        wobbleTexture.SetPixelData(wdata, 0);
        wobbleTexture.Apply(false, true);
        wdata.Dispose();
        Shader.SetGlobalTexture("_DigWobbleTex", wobbleTexture);

        disturbed = new NativeArray<byte>(Nx * Nz, Allocator.Persistent);
        disturbTexture = new Texture2D(Nx, Nz, TextureFormat.R8, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Dig Disturbed Ground" };
        disturbTexture.SetPixelData(disturbed, 0);
        disturbTexture.Apply(false, false);
        Shader.SetGlobalTexture("_DigDisturbTex", disturbTexture);

        int px = Mathf.RoundToInt(Size.x), py = Mathf.RoundToInt(Size.y), pz = Mathf.RoundToInt(Size.z);   // 1 m texels
        pathTexture = new Texture3D(px, py, pz, TextureFormat.R8, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Dig Path Warmth" };
        var pdata = new NativeArray<byte>(px * py * pz, Allocator.TempJob);
        if (path != null)
        {
            var src = path.Line;
            int count = (src.Length + 3) / 4;                    // every metre is plenty
            var line = new NativeArray<float3>(count + 1, Allocator.TempJob);
            for (int i = 0; i < count; i++) line[i] = src[Mathf.Min(i * 4, src.Length - 1)];
            line[count] = src[src.Length - 1];
            new PathWarmthJob { line = line, data = pdata, size = new int3(px, py, pz), min = Min, range = warmRange }.Schedule(pz, 2).Complete();
            line.Dispose();
            Shader.SetGlobalVector("_DigStart", new Vector4(src[0].x, src[0].y, src[0].z, 2.6f));
        }
        else
        {
            Shader.SetGlobalVector("_DigStart", new Vector4(patchCentre.x, 0f, patchCentre.y, 2.6f));
        }
        pathTexture.SetPixelData(pdata, 0);
        pathTexture.Apply(false, true);
        pdata.Dispose();
        Shader.SetGlobalTexture("_DigPathTex", pathTexture);
    }

    // ---- Digging -------------------------------------------------------------------
    // Removes a rounded, slightly ragged scoop. `axis` is the direction of the dig: the
    // scoop is `stretch` times longer along it. Earth harder than `shovelLevel` and
    // protected ground stay. Returns the volume removed (m3). Call Flush() afterwards.
    public float Carve(Vector3 center, float radius, Vector3 axis, float stretch, int shovelLevel)
    {
        if (center.y > -1.5f) Disturb(center, radius * 1.35f);
        float reach = radius * Mathf.Max(1f, stretch) * (1f + scoopNoise) + Voxel;
        Vector3 lo = (center - Vector3.one * reach - Min) / Voxel, hi = (center + Vector3.one * reach - Min) / Voxel;
        var a = new int3(Mathf.Max(0, Mathf.FloorToInt(lo.x)), Mathf.Max(0, Mathf.FloorToInt(lo.y)), Mathf.Max(0, Mathf.FloorToInt(lo.z)));
        var b = new int3(Mathf.Min(Nx - 1, Mathf.CeilToInt(hi.x)), Mathf.Min(Ny - 1, Mathf.CeilToInt(hi.y)), Mathf.Min(Nz - 1, Mathf.CeilToInt(hi.z)));
        if (a.x > b.x || a.y > b.y || a.z > b.z) return 0f;

        var removed = new NativeArray<float>(1, Allocator.TempJob);
        new CarveJob
        {
            density = density, lo = a, hi = b, min = Min, center = center,
            axis = axis.sqrMagnitude > 0.001f ? (float3)axis.normalized : new float3(0f, -1f, 0f),
            stretch = Mathf.Max(1f, stretch), radius = radius, noiseAmount = scoopNoise, level = shovelLevel,
            strata = stratumDepth, wobble = wobble,
            innerMin = (float3)Min + shell, innerMax = (float3)(Min + Size) - shell,
            patch = patchCentre, patchRadius = patchRadius, protectedDepth = protectedDepth, removed = removed,
        }.Run();
        float volume = removed[0] * Voxel * Voxel * Voxel;
        removed.Dispose();

        if (volume > 0f) MarkDirty(a, b);
        return volume;
    }

    // Loose earth settling onto the ground: adds a low, rounded mound of `cubicMetres`
    // around `point`, only where it rests on earth already there (a heap, not a bridge).
    public void Deposit(Vector3 point, float cubicMetres)
    {
        float r = Mathf.Max(Voxel * 0.9f, Mathf.Pow(cubicMetres * 0.75f / Mathf.PI, 1f / 3f) * 1.4f);
        float reach = r * 1.8f + Voxel;
        Vector3 lo = (point - new Vector3(reach, r, reach) - Min) / Voxel, hi = (point + new Vector3(reach, r, reach) - Min) / Voxel;
        var a = new int3(Mathf.Max(1, Mathf.FloorToInt(lo.x)), Mathf.Max(1, Mathf.FloorToInt(lo.y)), Mathf.Max(1, Mathf.FloorToInt(lo.z)));
        var b = new int3(Mathf.Min(Nx - 2, Mathf.CeilToInt(hi.x)), Mathf.Min(Ny - 2, Mathf.CeilToInt(hi.y)), Mathf.Min(Nz - 2, Mathf.CeilToInt(hi.z)));
        if (a.x > b.x || a.y > b.y || a.z > b.z) return;
        new DepositJob { density = density, lo = a, hi = b, min = Min, center = point, radius = r }.Run();
        if (point.y > -1.5f) Disturb(point, r * 1.8f);
        MarkDirty(a, b);
    }

    void Disturb(Vector3 point, float radius)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt((point.x - radius - Min.x) / Voxel)), x1 = Mathf.Min(Nx - 1, Mathf.CeilToInt((point.x + radius - Min.x) / Voxel));
        int z0 = Mathf.Max(0, Mathf.FloorToInt((point.z - radius - Min.z) / Voxel)), z1 = Mathf.Min(Nz - 1, Mathf.CeilToInt((point.z + radius - Min.z) / Voxel));
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            float d = new Vector2(Min.x + x * Voxel - point.x, Min.z + z * Voxel - point.z).magnitude / radius;
            if (d >= 1f) continue;
            byte v = (byte)(Mathf.Clamp01((1f - d) * 2.5f) * 255f);
            int i = x + Nx * z;
            if (v > disturbed[i]) { disturbed[i] = v; disturbDirty = true; }
        }
    }

    void LateUpdate()
    {
        if (!disturbDirty) return;
        disturbDirty = false;
        disturbTexture.SetPixelData(disturbed, 0);
        disturbTexture.Apply(false, false);
    }

    // Soil must never float. Around a fresh scoop, finds earth that is no longer joined to
    // the rest of the ground (and thin slivers that could not hold themselves up), removes
    // it from the field and returns where it was, so it can fall as clods. Returns m3.
    public float CollapseLoose(Vector3 center, float radius, List<Vector3> fallen)
    {
        float reach = radius * 2f + 1.5f;
        Vector3 lo = (center - Vector3.one * reach - Min) / Voxel, hi = (center + Vector3.one * reach - Min) / Voxel;
        var a = new int3(Mathf.Max(0, Mathf.FloorToInt(lo.x)), Mathf.Max(0, Mathf.FloorToInt(lo.y)), Mathf.Max(0, Mathf.FloorToInt(lo.z)));
        var b = new int3(Mathf.Min(Nx - 1, Mathf.CeilToInt(hi.x)), Mathf.Min(Ny - 1, Mathf.CeilToInt(hi.y)), Mathf.Min(Nz - 1, Mathf.CeilToInt(hi.z)));
        int3 size = b - a + 1;
        if (math.any(size < 3)) return 0f;

        var state = new NativeArray<byte>(size.x * size.y * size.z, Allocator.TempJob);
        var queue = new NativeList<int>(1024, Allocator.TempJob);
        var removed = new NativeList<float3>(64, Allocator.TempJob);
        new LooseJob { density = density, state = state, queue = queue, removed = removed, lo = a, size = size, min = Min }.Run();

        float volume = removed.Length * Voxel * Voxel * Voxel;
        if (fallen != null)
            for (int i = 0; i < removed.Length; i++) fallen.Add(removed[i]);
        state.Dispose();
        queue.Dispose();
        removed.Dispose();

        if (volume > 0f) MarkDirty(a, b);
        return volume;
    }

    // Opens a walkable tunnel through `points`, ignoring hardness (demo jumps, pockets).
    public void CarveTunnel(IList<Vector3> points, float radius)
    {
        for (int i = 0; i < points.Count; i++) Carve(points[i], radius, Vector3.down, 1f, 99);
        Flush(true);
    }

    void MarkDirty(int3 lo, int3 hi)
    {
        // A chunk reads the points of its cells plus one more on each side (for normals).
        int x0 = Mathf.Max(0, Mathf.CeilToInt((lo.x - 17) / 16f)), x1 = Mathf.Min(ChunksX - 1, (hi.x + 1) / 16);
        int y0 = Mathf.Max(0, Mathf.CeilToInt((lo.y - 17) / 16f)), y1 = Mathf.Min(ChunksY - 1, (hi.y + 1) / 16);
        int z0 = Mathf.Max(0, Mathf.CeilToInt((lo.z - 17) / 16f)), z1 = Mathf.Min(ChunksZ - 1, (hi.z + 1) / 16);
        for (int z = z0; z <= z1; z++)
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
            dirty.Add(x + ChunksX * (y + ChunksY * z));
    }

    // Rebuilds the meshes of every chunk that changed. Colliders are rebuilt only when
    // asked, so a scoop that grows over several frames pays for them once.
    public void Flush(bool colliders)
    {
        if (dirty.Count > 0)
        {
            int n = dirty.Count;
            var ids = new int[n];
            dirty.CopyTo(ids);
            dirty.Clear();

            var handles = new NativeArray<JobHandle>(n, Allocator.Temp);
            var verts = new NativeList<float3>[n];
            var normals = new NativeList<float3>[n];
            for (int i = 0; i < n; i++)
            {
                int id = ids[i];
                verts[i] = new NativeList<float3>(1024, Allocator.TempJob);
                normals[i] = new NativeList<float3>(1024, Allocator.TempJob);
                handles[i] = new MeshJob
                {
                    density = density, triTable = triTable, edgeA = edgeA, edgeB = edgeB, min = Min,
                    chunk = new int3(id % ChunksX, id / ChunksX % ChunksY, id / (ChunksX * ChunksY)),
                    verts = verts[i], normals = normals[i],
                }.Schedule();
            }
            JobHandle.CompleteAll(handles);
            handles.Dispose();

            for (int i = 0; i < n; i++)
            {
                Upload(ids[i], verts[i], normals[i]);
                colliderDirty.Add(ids[i]);
                verts[i].Dispose();
                normals[i].Dispose();
            }
        }

        if (colliders && colliderDirty.Count > 0)
        {
            foreach (int id in colliderDirty)
            {
                var c = chunks[id];
                if (c == null) continue;
                c.collider.sharedMesh = null;
                if (c.vertexCount > 0) c.collider.sharedMesh = c.mesh;
            }
            colliderDirty.Clear();
        }
    }

    void Upload(int id, NativeList<float3> verts, NativeList<float3> normals)
    {
        var c = chunks[id];
        if (verts.Length == 0)
        {
            if (c != null)
            {
                c.vertexCount = 0;
                c.go.SetActive(false);
            }
            return;
        }

        if (c == null)
        {
            c = chunks[id] = new Chunk();
            int cx = id % ChunksX, cy = id / ChunksX % ChunksY, cz = id / (ChunksX * ChunksY);
            c.go = new GameObject("Chunk " + cx + " " + cy + " " + cz);
            c.go.transform.SetParent(transform, false);
            c.mesh = new Mesh { name = c.go.name, indexFormat = IndexFormat.UInt32 };
            c.mesh.MarkDynamic();
            // Collider first: added after a MeshFilter it would grab the still-empty mesh.
            c.collider = c.go.AddComponent<MeshCollider>();
            c.collider.cookingOptions = MeshColliderCookingOptions.UseFastMidphase;
            c.go.AddComponent<MeshFilter>().sharedMesh = c.mesh;
            var r = c.go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.TwoSided;   // tunnel roofs must block the sun
        }

        c.go.SetActive(true);
        c.vertexCount = verts.Length;
        c.mesh.Clear();
        c.mesh.SetVertices(verts.AsArray().Reinterpret<Vector3>());
        c.mesh.SetNormals(normals.AsArray().Reinterpret<Vector3>());
        c.mesh.SetIndices(iota.GetSubArray(0, verts.Length), MeshTopology.Triangles, 0, false);
        float chunkSize = ChunkCells * Voxel;
        int x = id % ChunksX, y = id / ChunksX % ChunksY, z = id / (ChunksX * ChunksY);
        c.mesh.bounds = new Bounds(Min + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * chunkSize, Vector3.one * chunkSize);
    }

    // ---- Jobs ----------------------------------------------------------------------
    // A flattened, slightly lumpy mound. A voxel only fills if earth is below it, so a
    // heap grows up from the ground and never hangs in the air.
    [BurstCompile]
    struct DepositJob : IJob
    {
        public NativeArray<float> density;
        public int3 lo, hi;
        public float3 min, center;
        public float radius;

        public void Execute()
        {
            for (int z = lo.z; z <= hi.z; z++)
            for (int x = lo.x; x <= hi.x; x++)
            for (int y = lo.y; y <= hi.y; y++)   // bottom up, so each layer rests on the last
            {
                float3 p = min + new float3(x, y, z) * Voxel;
                float3 q = p - center;
                q.y *= 1.8f;
                float r = radius * (1f + 0.25f * noise.snoise(p * 3.1f));
                float fill = (r - math.length(q)) / Voxel;
                if (fill <= -1f) continue;
                int i = x + Nx * (y + Ny * z);
                if (density[i - Nx] <= 0f) continue;        // nothing under it
                density[i] = math.max(density[i], math.min(fill, 1f));
            }
        }
    }

    // Within a box: (1) knock off slivers - earth with air on five or six sides, or a
    // one-voxel sheet with air above and below; (2) flood-fill the earth from the box's
    // faces (it continues beyond them, so it is held up); whatever the fill does not reach
    // is an island floating in the air. Both are turned into air and reported.
    [BurstCompile]
    struct LooseJob : IJob
    {
        public NativeArray<float> density;
        public NativeArray<byte> state;          // 0 air, 1 earth, 2 earth that is held up
        public NativeList<int> queue;
        public NativeList<float3> removed;
        public int3 lo, size;
        public float3 min;

        int Local(int x, int y, int z) { return x + size.x * (y + size.y * z); }
        int Global(int x, int y, int z) { return (lo.x + x) + Nx * ((lo.y + y) + Ny * (lo.z + z)); }

        bool Air(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= size.x || y >= size.y || z >= size.z)
            {
                int3 g = lo + new int3(x, y, z);
                if (math.any(g < 0) || g.x >= Nx || g.y >= Ny || g.z >= Nz) return false;   // the edge of the world holds things up
                return density[g.x + Nx * (g.y + Ny * g.z)] <= 0f;
            }
            return state[Local(x, y, z)] == 0;
        }

        void Remove(int x, int y, int z)
        {
            int g = Global(x, y, z);
            density[g] = math.min(density[g], -0.5f);
            state[Local(x, y, z)] = 0;
            removed.Add(min + (float3)(lo + new int3(x, y, z)) * Voxel);
        }

        public void Execute()
        {
            for (int z = 0; z < size.z; z++)
            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
                state[Local(x, y, z)] = (byte)(density[Global(x, y, z)] > 0f ? 1 : 0);

            // (1) Slivers, twice: knocking one off can leave another.
            for (int pass = 0; pass < 2; pass++)
            for (int z = 1; z < size.z - 1; z++)
            for (int y = 1; y < size.y - 1; y++)
            for (int x = 1; x < size.x - 1; x++)
            {
                if (state[Local(x, y, z)] == 0) continue;
                bool below = Air(x, y - 1, z), above = Air(x, y + 1, z);
                int air = (below ? 1 : 0) + (above ? 1 : 0)
                        + (Air(x - 1, y, z) ? 1 : 0) + (Air(x + 1, y, z) ? 1 : 0)
                        + (Air(x, y, z - 1) ? 1 : 0) + (Air(x, y, z + 1) ? 1 : 0);
                if (air >= 5 || (below && above && air >= 4)) Remove(x, y, z);
            }

            // (2) Hold up everything joined to the box's faces.
            for (int z = 0; z < size.z; z++)
            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
            {
                bool face = x == 0 || y == 0 || z == 0 || x == size.x - 1 || y == size.y - 1 || z == size.z - 1;
                int i = Local(x, y, z);
                if (face && state[i] == 1) { state[i] = 2; queue.Add(i); }
            }
            int head = 0;
            while (head < queue.Length)
            {
                int i = queue[head++];
                int x = i % size.x, y = i / size.x % size.y, z = i / (size.x * size.y);
                Visit(x - 1, y, z); Visit(x + 1, y, z);
                Visit(x, y - 1, z); Visit(x, y + 1, z);
                Visit(x, y, z - 1); Visit(x, y, z + 1);
            }

            for (int z = 0; z < size.z; z++)
            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
                if (state[Local(x, y, z)] == 1) Remove(x, y, z);
        }

        void Visit(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= size.x || y >= size.y || z >= size.z) return;
            int i = Local(x, y, z);
            if (state[i] != 1) return;
            state[i] = 2;
            queue.Add(i);
        }
    }

    [BurstCompile]
    struct FillJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> heights;
        [NativeDisableParallelForRestriction] public NativeArray<float> density;
        public float minY;

        public void Execute(int z)
        {
            for (int y = 0; y < Ny; y++)
            {
                float wy = minY + y * Voxel;
                for (int x = 0; x < Nx; x++)
                    density[x + Nx * (y + Ny * z)] = math.clamp((heights[x + Nx * z] - wy) / Voxel, -1f, 1f);
            }
        }
    }

    // Which chunks have a surface in them at all.
    [BurstCompile]
    struct ScanJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> density;
        [WriteOnly] public NativeArray<bool> flags;

        public void Execute(int id)
        {
            int cx = id % ChunksX * ChunkCells, cy = id / ChunksX % ChunksY * ChunkCells, cz = id / (ChunksX * ChunksY) * ChunkCells;
            bool earth = false, air = false;
            for (int z = cz; z <= cz + ChunkCells; z++)
            for (int y = cy; y <= cy + ChunkCells; y++)
            for (int x = cx; x <= cx + ChunkCells; x++)
            {
                if (density[x + Nx * (y + Ny * z)] < 0f) air = true; else earth = true;
            }
            flags[id] = earth && air;
        }
    }

    [BurstCompile]
    struct CarveJob : IJob
    {
        public NativeArray<float> density;
        public NativeArray<float> removed;
        public int3 lo, hi;
        public float3 min, center, axis, innerMin, innerMax;
        public float stretch, radius, noiseAmount, wobble, patchRadius, protectedDepth;
        public float2 strata, patch;
        public int level;

        public void Execute()
        {
            float sum = 0f;
            for (int z = lo.z; z <= hi.z; z++)
            for (int y = lo.y; y <= hi.y; y++)
            for (int x = lo.x; x <= hi.x; x++)
            {
                float3 p = min + new float3(x, y, z) * Voxel;
                float3 q = p - center;
                float alongAxis = math.dot(q, axis);
                float3 across = q - axis * alongAxis;
                float dist = math.sqrt(math.lengthsq(across) + alongAxis * alongAxis / (stretch * stretch));

                float r = radius * (1f + noiseAmount * noise.snoise(p * 2.1f));
                float cut = (dist - r) / Voxel;
                if (cut >= 1f) continue;

                int i = x + Nx * (y + Ny * z);
                float old = density[i];
                if (old <= cut) continue;
                if (Protected(p, innerMin, innerMax, patch, patchRadius, protectedDepth)) continue;
                if (Stratum(p, strata, wobble) > level) continue;

                float now = math.max(cut, -1f);
                density[i] = now;
                sum += (math.saturate(old * 0.5f + 0.5f) - math.saturate(now * 0.5f + 0.5f));
            }
            removed[0] = sum;
        }
    }

    [BurstCompile]
    struct MeshJob : IJob
    {
        [ReadOnly] public NativeArray<float> density;
        [ReadOnly] public NativeArray<sbyte> triTable, edgeA, edgeB;
        public float3 min;
        public int3 chunk;
        public NativeList<float3> verts, normals;

        static int3 Corner(int i)
        {
            switch (i)
            {
                case 0: return new int3(0, 0, 0);
                case 1: return new int3(1, 0, 0);
                case 2: return new int3(1, 0, 1);
                case 3: return new int3(0, 0, 1);
                case 4: return new int3(0, 1, 0);
                case 5: return new int3(1, 1, 0);
                case 6: return new int3(1, 1, 1);
                default: return new int3(0, 1, 1);
            }
        }

        float At(int3 p)
        {
            p = math.clamp(p, int3.zero, new int3(Nx - 1, Ny - 1, Nz - 1));
            return density[p.x + Nx * (p.y + Ny * p.z)];
        }

        // Points into the earth (density rises); the surface normal is its opposite.
        float3 Gradient(int3 p)
        {
            return new float3(
                At(p + new int3(1, 0, 0)) - At(p - new int3(1, 0, 0)),
                At(p + new int3(0, 1, 0)) - At(p - new int3(0, 1, 0)),
                At(p + new int3(0, 0, 1)) - At(p - new int3(0, 0, 1)));
        }

        public void Execute()
        {
            var d = new NativeArray<float>(8, Allocator.Temp);
            var edgePos = new NativeArray<float3>(12, Allocator.Temp);
            var edgeNormal = new NativeArray<float3>(12, Allocator.Temp);
            int3 start = chunk * ChunkCells;

            for (int z = 0; z < ChunkCells; z++)
            for (int y = 0; y < ChunkCells; y++)
            for (int x = 0; x < ChunkCells; x++)
            {
                int3 cell = start + new int3(x, y, z);
                int config = 0;
                for (int i = 0; i < 8; i++)
                {
                    int3 c = cell + Corner(i);
                    d[i] = density[c.x + Nx * (c.y + Ny * c.z)];
                    if (d[i] < 0f) config |= 1 << i;
                }
                if (config == 0 || config == 255) continue;

                int row = config * 16;
                for (int t = 0; t < 16 && triTable[row + t] >= 0; t++)
                {
                    int e = triTable[row + t];
                    int a = edgeA[e], b = edgeB[e];
                    int3 pa = cell + Corner(a), pb = cell + Corner(b);
                    float k = math.saturate(d[a] / (d[a] - d[b]));
                    edgePos[e] = min + math.lerp((float3)pa, (float3)pb, k) * Voxel;
                    edgeNormal[e] = -math.normalizesafe(math.lerp(Gradient(pa), Gradient(pb), k), new float3(0f, 1f, 0f));
                }

                // The table's triangles face the earth; reverse them to face the air.
                for (int t = 0; t < 16 && triTable[row + t] >= 0; t += 3)
                {
                    int e0 = triTable[row + t + 2], e1 = triTable[row + t + 1], e2 = triTable[row + t];
                    verts.Add(edgePos[e0]); verts.Add(edgePos[e1]); verts.Add(edgePos[e2]);
                    normals.Add(edgeNormal[e0]); normals.Add(edgeNormal[e1]); normals.Add(edgeNormal[e2]);
                }
            }
        }
    }

    // How "on the path" each cubic metre is, for the soil shader (1 = on it).
    [BurstCompile]
    struct PathWarmthJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> line;
        [NativeDisableParallelForRestriction] public NativeArray<byte> data;
        public int3 size;
        public float3 min;
        public float2 range;

        public void Execute(int z)
        {
            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
            {
                float3 p = min + new float3(x + 0.5f, y + 0.5f, z + 0.5f);
                float best = float.MaxValue;
                for (int i = 0; i < line.Length - 1; i++)
                {
                    float3 a = line[i], ab = line[i + 1] - a;
                    float t = math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.lengthsq(ab)));
                    best = math.min(best, math.lengthsq(p - (a + ab * t)));
                }
                float w = 1f - math.smoothstep(range.x, range.y, math.sqrt(best));
                data[x + size.x * (y + size.y * z)] = (byte)(w * 255f);
            }
        }
    }
}
