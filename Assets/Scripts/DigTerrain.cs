using UnityEngine;

// The diggable shaft floor: one deformable heightmap mesh, 5 m x 5 m, generated at
// runtime because it changes with every stroke. The GameObject, renderer, material and
// collider live in the scene (made by DigIn/Build Scene). Must sit at the world origin.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class DigTerrain : MonoBehaviour
{
    public const float Size = 5f;
    const float Cell = 0.25f;
    const int N = 21;                    // vertices per side (Size / Cell + 1)

    const float BaseScoop = 0.16f;       // metres removed at the centre of one stroke
    const float MaxScoopMult = 1.6f;     // cap for a strong shovel in soft ground
    const float FlatCore = 0.45f;        // inner part of the radius that gets the full depth
    const float ColorJitter = 0.12f;     // per-vertex depth offset for colour only, in metres

    // The strata texture covers depth -TexTop .. TotalDepth + TexExtra - TexTop.
    const float TexTop = 0.5f;
    const float TexExtra = 2f;

    static readonly float[] Hardness = { 1f, 2f, 4f };

    public struct DigResult
    {
        public float volume;     // cubic metres removed
        public int stratum;      // stratum at the aim point before the stroke
        public bool hitFloor;    // the aim point was already as deep as this shovel can go
    }

    public Collider Collider { get { return meshCollider; } }

    readonly float[] heights = new float[N * N];
    readonly float[] jitter = new float[N * N];
    readonly Vector3[] verts = new Vector3[N * N];
    readonly Vector2[] uvs = new Vector2[N * N];
    Mesh mesh;
    MeshCollider meshCollider;

    // ---- Strata colouring ------------------------------------------------------
    // The default lit material ignores vertex colours, so depth is mapped to the V
    // coordinate of a generated gradient texture instead. Same look, no custom shader.
    public static float DepthToV(float depth)
    {
        return 1f - (depth + TexTop) / (DigGame.TotalDepth + TexExtra);
    }

    public static int StratumAt(float depth)
    {
        return Mathf.Clamp(Mathf.FloorToInt(depth / DigGame.StratumDepth), 0, DigGame.StratumCount - 1);
    }

    // colors: one per stratum, plus one more for "today" at the very bottom.
    public static Texture2D MakeStrataTexture(Color[] colors)
    {
        const int w = 4, h = 1024;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.anisoLevel = 8;

        var pixels = new Color[w * h];
        for (int row = 0; row < h; row++)
        {
            float v = (row + 0.5f) / h;
            float depth = (1f - v) * (DigGame.TotalDepth + TexExtra) - TexTop;

            // The next stratum's colour starts a little above the boundary, so the floor
            // you cannot dig through already shows what it is made of.
            Color c = colors[0];
            for (int i = 1; i < colors.Length; i++)
            {
                float boundary = i * DigGame.StratumDepth;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(boundary - 0.45f, boundary - 0.2f, depth));
                c = Color.Lerp(c, colors[i], t);
            }

            // Soft horizontal banding so the walls read as layered earth.
            float shade = 1f
                + (Mathf.PerlinNoise(depth * 1.6f + 10f, 0.5f) - 0.5f) * 0.22f
                + (Mathf.PerlinNoise(depth * 6f + 10f, 9.1f) - 0.5f) * 0.08f;
            c = new Color(c.r * shade, c.g * shade, c.b * shade, 1f);

            for (int x = 0; x < w; x++) pixels[row * w + x] = c;
        }
        tex.SetPixels(pixels);
        tex.Apply(true);
        return tex;
    }

    // ---- Build -----------------------------------------------------------------
    void Awake()
    {
        var rng = new System.Random(12345);
        for (int z = 0; z < N; z++)
        {
            for (int x = 0; x < N; x++)
            {
                int i = z * N + x;
                heights[i] = 0f;
                jitter[i] = ((float)rng.NextDouble() - 0.5f) * 2f * ColorJitter;
                verts[i] = new Vector3(x * Cell, 0f, z * Cell);
                uvs[i] = new Vector2(0.5f, DepthToV(jitter[i]));
            }
        }

        var tris = new int[(N - 1) * (N - 1) * 6];
        int ti = 0;
        for (int z = 0; z < N - 1; z++)
        {
            for (int x = 0; x < N - 1; x++)
            {
                int v00 = z * N + x, v10 = v00 + 1, v01 = v00 + N, v11 = v01 + 1;
                if (((x + z) & 1) == 0)
                {
                    tris[ti++] = v00; tris[ti++] = v01; tris[ti++] = v11;
                    tris[ti++] = v00; tris[ti++] = v11; tris[ti++] = v10;
                }
                else
                {
                    // Alternate the diagonal so scoops look round instead of slanted.
                    tris[ti++] = v00; tris[ti++] = v01; tris[ti++] = v10;
                    tris[ti++] = v10; tris[ti++] = v01; tris[ti++] = v11;
                }
            }
        }

        mesh = new Mesh();
        mesh.name = "Dig Terrain";
        mesh.MarkDynamic();
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;
        meshCollider = GetComponent<MeshCollider>();
        meshCollider.sharedMesh = mesh;
    }

    // Four inward-facing quads that show the strata on the shaft walls. Called by the
    // editor setup script, which saves the result as a mesh asset.
    public static Mesh BuildWallMesh()
    {
        float s = Size;
        float bottom = DigGame.TotalDepth + 1f;
        var corners = new[]
        {
            new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, s),   // west wall, faces +x
            new Vector3(0f, 0f, s),  new Vector3(s, 0f, s),    // north wall, faces -z
            new Vector3(s, 0f, s),   new Vector3(s, 0f, 0f),   // east wall, faces -x
            new Vector3(s, 0f, 0f),  new Vector3(0f, 0f, 0f),  // south wall, faces +z
        };

        var wv = new Vector3[16];
        var wuv = new Vector2[16];
        var wt = new int[24];
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = corners[i * 2], b = corners[i * 2 + 1];
            int k = i * 4;
            wv[k] = a;
            wv[k + 1] = b;
            wv[k + 2] = b + Vector3.down * bottom;
            wv[k + 3] = a + Vector3.down * bottom;
            wuv[k] = wuv[k + 1] = new Vector2(0.5f, DepthToV(0f));
            wuv[k + 2] = wuv[k + 3] = new Vector2(0.5f, DepthToV(bottom));
            wt[i * 6] = k; wt[i * 6 + 1] = k + 1; wt[i * 6 + 2] = k + 2;
            wt[i * 6 + 3] = k; wt[i * 6 + 4] = k + 2; wt[i * 6 + 5] = k + 3;
        }

        var wallMesh = new Mesh();
        wallMesh.name = "Shaft Walls";
        wallMesh.vertices = wv;
        wallMesh.uv = wuv;
        wallMesh.triangles = wt;
        wallMesh.RecalculateNormals();
        wallMesh.RecalculateBounds();
        return wallMesh;
    }

    // ---- Digging ---------------------------------------------------------------
    public float HeightAt(float x, float z)
    {
        float fx = Mathf.Clamp(x / Cell, 0f, N - 1.001f);
        float fz = Mathf.Clamp(z / Cell, 0f, N - 1.001f);
        int ix = (int)fx, iz = (int)fz;
        float tx = fx - ix, tz = fz - iz;
        int i = iz * N + ix;
        float a = Mathf.Lerp(heights[i], heights[i + 1], tx);
        float b = Mathf.Lerp(heights[i + N], heights[i + N + 1], tx);
        return Mathf.Lerp(a, b, tz);
    }

    // Lowers the ground around `point`. Nothing goes below minHeight.
    public DigResult Dig(Vector3 point, float radius, float power, float minHeight)
    {
        var result = new DigResult();
        float centerHeight = HeightAt(point.x, point.z);
        result.stratum = StratumAt(-centerHeight);
        result.hitFloor = centerHeight <= minHeight + 0.05f;

        int x0 = Mathf.Max(0, Mathf.CeilToInt((point.x - radius) / Cell));
        int x1 = Mathf.Min(N - 1, Mathf.FloorToInt((point.x + radius) / Cell));
        int z0 = Mathf.Max(0, Mathf.CeilToInt((point.z - radius) / Cell));
        int z1 = Mathf.Min(N - 1, Mathf.FloorToInt((point.z + radius) / Cell));

        float removed = 0f;
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float dx = x * Cell - point.x, dz = z * Cell - point.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz) / radius;
                if (d >= 1f) continue;

                // Flat in the middle (so you can stand in your own hole), rounded at the rim.
                float w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlatCore, 1f, d));

                int i = z * N + x;
                float h = heights[i];
                float mult = Mathf.Min(MaxScoopMult, power / Hardness[StratumAt(-h)]);
                float lowered = Mathf.Max(h - BaseScoop * mult * w, minHeight);
                if (lowered < h)
                {
                    removed += h - lowered;
                    heights[i] = lowered;
                }
            }
        }

        if (removed > 0f) ApplyHeights();
        result.volume = removed * Cell * Cell;
        return result;
    }

    // Demo mode only: flattens the whole floor down to `height` (never raises it).
    // Separate from Dig(), which stays the only way the player shapes the ground.
    public void LowerTo(float height)
    {
        for (int i = 0; i < heights.Length; i++) heights[i] = Mathf.Min(heights[i], height);
        ApplyHeights();
    }

    void ApplyHeights()
    {
        for (int i = 0; i < heights.Length; i++)
        {
            verts[i].y = heights[i];
            uvs[i].y = DepthToV(-heights[i] + jitter[i]);
        }
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Reassigning forces the collider to rebuild from the changed mesh.
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;
    }
}
