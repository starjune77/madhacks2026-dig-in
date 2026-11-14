using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// The world around the shaft, built from the imported Kenney kits:
//
//   Backyard (south of the shaft): the old family house's back wall with the door, the
//   memory shelf beside it, a workbench under a lean-to, a fence, lanterns, odds and ends.
//   The shaft itself gets a plank frame.
//   Excavation (north, past the back fence): terraces stepping down 3 m at a time, their
//   faces banded dirt / clay / stone like the shaft walls, a ramp, a small camp and props.
//   Rock formations frame the view; pine-covered hills ring everything and fade into fog.
//
// Models are placed by their measured bounds, so it does not matter which units or pivots
// a kit uses. Modular pieces (walls, fences) are turned by measuring which side of the
// tile their slab sits on.
public static partial class DigInSceneBuilder
{
    // ---- Layout ----------------------------------------------------------------
    static readonly Vector3 ShelfPos = new Vector3(0.8f, 0f, -15.35f);
    static readonly Vector3 BenchPos = new Vector3(12.3f, 0f, -7f);

    const float YardMinX = -12f, YardMaxX = 17f, YardMinZ = -16f, YardMaxZ = 10f;
    const float HouseTile = 3.2f;          // one Fantasy Town tile, scaled up
    const float HouseMinX = -4f;           // west end of the house
    const int HouseTiles = 4;
    const int DoorTile = 2;

    // Excavation: an ellipse of terraces north of the yard.
    const float PitCx = 2.5f, PitCz = 50f, PitRx = 48f, PitRz = 36f;
    const float TerracePeriod = 7f, TerraceFace = 2.5f, StepDepth = 3f;
    const int TerraceLevels = 4;
    const float RampX = 21f;

    // Terrain: 256 m square, 0.5 m height cells aligned so the shaft (0..5 m) is exactly
    // ten cells, which become a hole.
    const float TerrainSize = 256f, TerrainBaseY = -14f, TerrainHeight = 52f;
    static readonly Vector3 TerrainOrigin = new Vector3(-126f, TerrainBaseY, -126f);
    const int HeightRes = 513, SplatRes = 1024;

    static void BuildEnvironment(Scene scene)
    {
        if (!AssetDatabase.IsValidFolder(KenneyDir))
        {
            report.AppendLine("  environment skipped: " + KenneyDir + " is missing");
            return;
        }

        var env = Root(scene, "Environment", out _);
        Ensure(env, "Terrain", BuildTerrain);
        Ensure(env, "House", BuildHouse);
        Ensure(env, "Yard", BuildYard);
        Ensure(env, "Shaft Rim", BuildRim);
        Ensure(env, "Excavation", BuildExcavation);
        Ensure(env, "Rock Formations", BuildFormations);
    }

    // ---- Height function (shared by terrain and prop placement) --------------------
    static float Smooth(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

    // Metres inside the excavation rim (negative outside).
    static float PitInside(float x, float z)
    {
        float nx = (x - PitCx) / PitRx, nz = (z - PitCz) / PitRz;
        float r = Mathf.Sqrt(nx * nx + nz * nz);
        float wobble = (Mathf.PerlinNoise(x * 0.05f + 3.1f, z * 0.05f + 7.3f) - 0.5f) * 0.08f;
        return (1f - r + wobble) * PitRz;
    }

    static float Terraces(float inside)
    {
        int k = Mathf.FloorToInt(inside / TerracePeriod);
        if (k >= TerraceLevels) return -StepDepth * TerraceLevels;
        float t = inside - k * TerracePeriod;
        return -StepDepth * (k + Smooth(0f, TerraceFace, t));
    }

    static float RectDistance(float x, float z, float minX, float maxX, float minZ, float maxZ)
    {
        float dx = Mathf.Max(minX - x, 0f, x - maxX);
        float dz = Mathf.Max(minZ - z, 0f, z - maxZ);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static float GroundHeight(float x, float z)
    {
        // The yard (and house) is perfectly flat at 0; mounds grow away from it.
        float dYard = RectDistance(x, z, YardMinX - 2f, YardMaxX + 2f, YardMinZ - 5f, YardMaxZ + 1.5f);
        float mound = (Mathf.PerlinNoise(x * 0.035f + 20f, z * 0.035f + 40f) - 0.45f) * 5f * Smooth(2f, 22f, dYard);
        float r = Vector2.Distance(new Vector2(x, z), new Vector2(2.5f, 10f));
        float hills = Smooth(55f, 115f, r) * (14f + 18f * Mathf.PerlinNoise(x * 0.013f + 11f, z * 0.013f + 5f));

        float inside = PitInside(x, z);
        if (inside <= 0f) return (mound + hills) * Smooth(0f, 12f, -inside);

        float depth = Terraces(inside);
        float ramp = 1f - Smooth(2.5f, 4.5f, Mathf.Abs(x - RampX));
        if (ramp > 0f)
            depth = Mathf.Lerp(depth, -StepDepth * TerraceLevels * Mathf.Clamp01(inside / (TerracePeriod * TerraceLevels)), ramp);
        return depth;
    }

    static float Slope(float x, float z)
    {
        const float e = 0.3f;
        float dx = (GroundHeight(x + e, z) - GroundHeight(x - e, z)) / (2f * e);
        float dz = (GroundHeight(x, z + e) - GroundHeight(x, z - e)) / (2f * e);
        return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
    }

    static Vector3 OnGround(float x, float z, float sink = 0f) => new Vector3(x, GroundHeight(x, z) - sink, z);

    // ---- Terrain ------------------------------------------------------------------
    static GameObject BuildTerrain()
    {
        string dataPath = TerrainDir + "/Dig Site Terrain.asset";
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
        if (data == null)
        {
            data = new TerrainData();
            data.heightmapResolution = HeightRes;
            data.alphamapResolution = SplatRes;
            data.baseMapResolution = 512;
            data.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);
            AssetDatabase.CreateAsset(data, dataPath);

            data.terrainLayers = new[]
            {
                GroundLayer("Grass", new Color(0.45f, 0.52f, 0.27f)),
                GroundLayer("Dirt", StrataColors[0]),
                GroundLayer("Clay", StrataColors[1]),
                GroundLayer("Stone", StrataColors[2]),
            };
            FillHeights(data);
            FillSplat(data);
            FillTrees(data);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Created(dataPath);
        }

        var go = Terrain.CreateTerrainGameObject(data);
        go.transform.position = TerrainOrigin;
        var terrain = go.GetComponent<Terrain>();
        terrain.materialTemplate = LoadOrCreate(TerrainDir + "/Terrain.mat",
            () => new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")));
        terrain.heightmapPixelError = 4f;
        terrain.basemapDistance = 300f;
        terrain.treeDistance = 400f;
        terrain.treeBillboardDistance = 400f;
        terrain.drawInstanced = true;
        return go;
    }

    static TerrainLayer GroundLayer(string name, Color color)
    {
        string texPath = TexDir + "/Ground " + name + ".png";
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) == null)
        {
            // Nearly flat colour with soft mottling, to suit the low-poly kits.
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var rng = new System.Random(name.GetHashCode());
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (float)x / n * Mathf.PI * 2f, v = (float)y / n * Mathf.PI * 2f;
                // Tileable noise from periodic Perlin samples.
                float k = Mathf.PerlinNoise(Mathf.Cos(u) * 1.5f + 10f, Mathf.Sin(v) * 1.5f + Mathf.Sin(u) + 10f);
                float speck = (float)rng.NextDouble() < 0.04f ? -0.06f : 0f;
                float shade = 0.93f + 0.12f * k + speck;
                tex.SetPixel(x, y, new Color(color.r * shade, color.g * shade, color.b * shade, 1f));
            }
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
            Created(texPath);
        }

        return LoadOrCreate(TerrainDir + "/" + name + ".terrainlayer", () => new TerrainLayer
        {
            diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath),
            tileSize = new Vector2(4f, 4f),
            smoothness = 0f,
            metallic = 0f,
        });
    }

    static void FillHeights(TerrainData data)
    {
        float cell = TerrainSize / (HeightRes - 1);
        var h = new float[HeightRes, HeightRes];
        for (int iz = 0; iz < HeightRes; iz++)
        for (int ix = 0; ix < HeightRes; ix++)
        {
            float x = TerrainOrigin.x + ix * cell, z = TerrainOrigin.z + iz * cell;
            h[iz, ix] = Mathf.Clamp01((GroundHeight(x, z) - TerrainBaseY) / TerrainHeight);
        }
        data.SetHeights(0, 0, h);

        // The shaft opening: cells covering x, z in [0, 5].
        int start = Mathf.RoundToInt(-TerrainOrigin.x / cell);
        int count = Mathf.RoundToInt(DigTerrain.Size / cell);
        data.SetHoles(start, start, new bool[count, count]);   // false = hole
    }

    // Layers: 0 grass, 1 dirt, 2 clay, 3 stone.
    static void FillSplat(TerrainData data)
    {
        float cell = TerrainSize / SplatRes;
        var a = new float[SplatRes, SplatRes, 4];
        Vector2 door = new Vector2(HouseMinX + HouseTile * (DoorTile + 0.5f), YardMinZ + 0.3f);
        Vector2 shaft = new Vector2(2.5f, -1.2f);

        for (int iz = 0; iz < SplatRes; iz++)
        for (int ix = 0; ix < SplatRes; ix++)
        {
            float x = TerrainOrigin.x + (ix + 0.5f) * cell, z = TerrainOrigin.z + (iz + 0.5f) * cell;
            float inside = PitInside(x, z);
            float slope = Slope(x, z);
            var w = new float[4];

            if (inside > -1.5f)
            {
                if (slope > 24f && inside > 0f)
                {
                    // A terrace face: dirt over clay over stone, deeper faces stonier.
                    float depth = -GroundHeight(x, z);
                    int k = Mathf.FloorToInt(depth / StepDepth);
                    float local = depth / StepDepth - k;
                    int band = Mathf.Clamp(Mathf.FloorToInt(local * 3f) + (k >= 2 ? 1 : 0), 0, 2);
                    w[1 + band] = 1f;
                }
                else
                {
                    w[1] = 1f;
                    if (inside < 0f) w[0] = Smooth(0f, -1.5f, inside);   // grass creeping to the rim
                }
            }
            else
            {
                w[0] = 1f;
                if (slope > 30f) { w[3] = Smooth(30f, 42f, slope); w[1] = 0.4f * w[3]; }
            }

            // A worn path from the back door to the shaft, and trampled earth around the shaft.
            float path = DistToSegment(new Vector2(x, z), door, shaft);
            float trample = RectDistance(x, z, -1f, 6f, -1f, 6f);
            float bare = Mathf.Max(1f - Smooth(0.5f, 1.1f, path), 0.8f * (1f - Smooth(0.3f, 1.8f, trample)));
            if (bare > 0f) { for (int l = 0; l < 4; l++) w[l] *= 1f - bare; w[1] += bare; }

            float sum = w[0] + w[1] + w[2] + w[3];
            for (int l = 0; l < 4; l++) a[iz, ix, l] = w[l] / sum;
        }
        data.SetAlphamaps(0, 0, a);
    }

    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    // Pines on the surrounding hills, kept away from the yard and the excavation.
    static void FillTrees(TerrainData data)
    {
        string[] pines =
        {
            "NatureKit/tree_pineTallA", "NatureKit/tree_pineTallB", "NatureKit/tree_pineTallC", "NatureKit/tree_pineTallD",
            "NatureKit/tree_pineRoundA", "NatureKit/tree_pineRoundC", "NatureKit/tree_pineDefaultA", "NatureKit/tree_pineDefaultB",
        };
        var protos = new List<TreePrototype>();
        var heights = new List<float>();
        foreach (var p in pines)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KenneyDir + "/" + p + ".fbx");
            if (prefab == null) continue;
            protos.Add(new TreePrototype { prefab = prefab });
            heights.Add(ModelHeight(p));
        }
        if (protos.Count == 0) return;
        data.treePrototypes = protos.ToArray();

        var rng = new System.Random(4242);
        var trees = new List<TreeInstance>();
        for (float z = TerrainOrigin.z + 3f; z < TerrainOrigin.z + TerrainSize - 3f; z += 4.5f)
        for (float x = TerrainOrigin.x + 3f; x < TerrainOrigin.x + TerrainSize - 3f; x += 4.5f)
        {
            float px = x + (float)(rng.NextDouble() - 0.5) * 3.5f;
            float pz = z + (float)(rng.NextDouble() - 0.5) * 3.5f;
            float r = Vector2.Distance(new Vector2(px, pz), new Vector2(2.5f, 10f));
            if (r < 34f || PitInside(px, pz) > -4f) continue;
            if (RectDistance(px, pz, YardMinX, YardMaxX, YardMinZ - 4f, YardMaxZ) < 8f) continue;
            if (Slope(px, pz) > 34f) continue;

            float density = Mathf.PerlinNoise(px * 0.04f + 70f, pz * 0.04f + 30f);
            float threshold = Mathf.Lerp(0.72f, 0.38f, Smooth(34f, 80f, r));   // sparse near, dense far
            if (density < threshold) continue;

            int k = rng.Next(protos.Count);
            float size = Mathf.Lerp(6f, 11f, (float)rng.NextDouble()) / Mathf.Max(0.1f, heights[k]);
            trees.Add(new TreeInstance
            {
                prototypeIndex = k,
                position = new Vector3((px - TerrainOrigin.x) / TerrainSize,
                                       (GroundHeight(px, pz) - TerrainBaseY) / TerrainHeight,
                                       (pz - TerrainOrigin.z) / TerrainSize),
                widthScale = size,
                heightScale = size,
                rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                color = Color.white,
                lightmapColor = Color.white,
            });
        }
        data.SetTreeInstances(trees.ToArray(), true);
        report.AppendLine("  placed " + trees.Count + " pines");
    }

    // ---- House --------------------------------------------------------------------
    // A narrow two-storey wooden house, back wall facing the yard (+Z).
    static GameObject BuildHouse()
    {
        var house = new GameObject("House");
        float scale = KitScale("FantasyTownKit/wall-wood", HouseTile);
        float zc = YardMinZ - HouseTile * 0.5f;

        for (int floor = 0; floor < 2; floor++)
        {
            float y = floor * HouseTile;
            for (int i = 0; i < HouseTiles; i++)
            {
                var c = new Vector3(HouseMinX + HouseTile * (i + 0.5f), y, zc);
                string back = floor == 0
                    ? (i == DoorTile ? "wall-wood-door" : i == DoorTile - 1 ? "wall-wood" : "wall-wood-window-shutters")
                    : (i % 2 == 0 ? "wall-wood-window-glass" : "wall-wood-window-small");
                Edge(house.transform, "FantasyTownKit/" + back, c, Vector3.forward, scale, true);
                Edge(house.transform, "FantasyTownKit/" + (i == 1 && floor == 0 ? "wall-wood-window-small" : "wall-wood"), c, Vector3.back, scale, true);
                if (i == 0) Edge(house.transform, "FantasyTownKit/" + (floor == 0 ? "wall-wood" : "wall-wood-window-round"), c, Vector3.left, scale, true);
                if (i == HouseTiles - 1) Edge(house.transform, "FantasyTownKit/wall-wood", c, Vector3.right, scale, true);
            }
        }
        for (int i = 0; i < HouseTiles; i++)
        {
            var roof = InstantiateKit("FantasyTownKit/roof-gable", house.transform);
            if (roof == null) continue;
            roof.transform.localScale = Vector3.one * scale;
            roof.transform.position = new Vector3(HouseMinX + HouseTile * (i + 0.5f), 2f * HouseTile, zc);
        }

        // Back step and a lamp by the door.
        float doorX = HouseMinX + HouseTile * (DoorTile + 0.5f);
        Place(house.transform, "FantasyTownKit/stairs-wood", new Vector3(doorX, 0f, YardMinZ + 0.35f), 0f, height: 0.35f, collider: true);
        Lantern(house.transform, new Vector3(doorX + 1.9f, 0f, YardMinZ + 0.6f), 2.0f);
        return house;
    }

    // ---- Yard ---------------------------------------------------------------------
    static GameObject BuildYard()
    {
        var yard = new GameObject("Yard");
        var t = yard.transform;

        // Fence on three sides; the house closes the south side.
        float houseMaxX = HouseMinX + HouseTile * HouseTiles;
        FenceLine(t, new Vector3(YardMinX, 0f, YardMinZ), new Vector3(YardMinX, 0f, YardMaxZ), Vector3.left, -1f);
        FenceLine(t, new Vector3(YardMaxX, 0f, YardMinZ), new Vector3(YardMaxX, 0f, YardMaxZ), Vector3.right, -1f);
        FenceLine(t, new Vector3(YardMinX, 0f, YardMaxZ), new Vector3(YardMaxX, 0f, YardMaxZ), Vector3.forward, 2.5f);
        FenceLine(t, new Vector3(YardMinX, 0f, YardMinZ), new Vector3(HouseMinX - 0.2f, 0f, YardMinZ), Vector3.back, -1f);
        FenceLine(t, new Vector3(houseMaxX + 0.2f, 0f, YardMinZ), new Vector3(YardMaxX, 0f, YardMinZ), Vector3.back, -1f);

        // Odds and ends of a lived-in yard.
        Place(t, "SurvivalKit/barrel", new Vector3(15.3f, 0f, -13.8f), 20f, height: 0.95f, collider: true);
        Place(t, "SurvivalKit/barrel-open", new Vector3(14.2f, 0f, -14.5f), 0f, height: 0.95f, collider: true);
        Place(t, "SurvivalKit/box-large", new Vector3(-10.3f, 0f, -14.2f), 15f, height: 0.8f, collider: true);
        Place(t, "SurvivalKit/box", new Vector3(-9.4f, 0f, -13.3f), -20f, height: 0.5f, collider: true);
        Place(t, "NatureKit/log_stack", new Vector3(-10.6f, 0f, -6f), 90f, height: 1.1f, collider: true);
        Place(t, "FantasyTownKit/cart", new Vector3(-8.8f, 0f, 4.2f), 60f, height: 1.7f, collider: true);
        Place(t, "SurvivalKit/bucket", new Vector3(6.5f, 0f, -0.9f), 0f, height: 0.42f, collider: true);
        Place(t, "SurvivalKit/bucket", new Vector3(-1.4f, 0f, 6.2f), 40f, height: 0.42f, collider: true);
        Place(t, "SurvivalKit/resource-stone-large", new Vector3(7.2f, 0f, 4.6f), 30f, height: 0.7f, collider: true);
        Place(t, "SurvivalKit/signpost", new Vector3(5.2f, 0f, 9.1f), 0f, height: 1.6f);

        Place(t, "NatureKit/plant_bushLarge", new Vector3(-2.6f, 0f, YardMinZ + 0.6f), 0f, height: 1.0f);
        Place(t, "NatureKit/plant_bushLarge", new Vector3(7.7f, 0f, YardMinZ + 0.6f), 90f, height: 0.9f);
        Place(t, "NatureKit/plant_bush", new Vector3(-11.2f, 0f, -10f), 0f, height: 0.7f);
        Place(t, "NatureKit/plant_bush", new Vector3(16.2f, 0f, 2f), 0f, height: 0.7f);
        Place(t, "NatureKit/flower_yellowA", new Vector3(-1.7f, 0f, YardMinZ + 0.5f), 0f, height: 0.35f);
        Place(t, "NatureKit/flower_redA", new Vector3(6.7f, 0f, YardMinZ + 0.5f), 0f, height: 0.35f);
        Place(t, "NatureKit/flower_purpleA", new Vector3(-3.4f, 0f, YardMinZ + 0.9f), 0f, height: 0.35f);

        // Grass tufts, away from the path and the shaft.
        var rng = new System.Random(77);
        for (int n = 0, tries = 0; n < 40 && tries < 400; tries++)
        {
            float x = Mathf.Lerp(YardMinX + 0.8f, YardMaxX - 0.8f, (float)rng.NextDouble());
            float z = Mathf.Lerp(YardMinZ + 1.5f, YardMaxZ - 0.8f, (float)rng.NextDouble());
            if (RectDistance(x, z, -1.5f, 6.5f, -1.5f, 6.5f) < 1f) continue;
            if (Mathf.Abs(x - 3f) < 1.6f && z < 0f) continue;
            if (Vector2.Distance(new Vector2(x, z), new Vector2(BenchPos.x, BenchPos.z)) < 2.5f) continue;
            Place(t, rng.NextDouble() < 0.5 ? "NatureKit/grass_large" : "NatureKit/grass", new Vector3(x, 0f, z),
                (float)rng.NextDouble() * 360f, height: 0.25f + 0.2f * (float)rng.NextDouble());
            n++;
        }

        // Trees just outside the fence.
        Place(t, "NatureKit/tree_oak", new Vector3(-15.5f, 0f, -13f), 0f, height: 7f);
        Place(t, "NatureKit/tree_default", new Vector3(20.5f, 0f, -12f), 40f, height: 6f);
        Place(t, "NatureKit/tree_detailed", new Vector3(-15.5f, 0f, 5f), 90f, height: 7.5f);
        Place(t, "NatureKit/tree_fat", new Vector3(21f, 0f, 4f), 0f, height: 5.5f);
        Place(t, "NatureKit/tree_oak_fall", new Vector3(-13.5f, 0f, -21f), 200f, height: 7f);

        // Evening lamps.
        Lantern(t, new Vector3(YardMinX + 0.7f, 0f, YardMaxZ - 0.7f), 2.2f);
        Lantern(t, new Vector3(YardMaxX - 0.7f, 0f, YardMaxZ - 0.7f), 2.2f);
        return yard;
    }

    // Fence segments from a to b; slabs sit on the `outward` side. A gate replaces the
    // segment nearest gateX (pass a negative value for no gate).
    static void FenceLine(Transform parent, Vector3 a, Vector3 b, Vector3 outward, float gateX)
    {
        float length = Vector3.Distance(a, b);
        int n = Mathf.Max(1, Mathf.RoundToInt(length / 2.5f));
        float seg = length / n;
        float scale = KitScale("FantasyTownKit/fence", seg);
        Vector3 dir = (b - a).normalized;
        int gate = gateX < 0f ? -1 : Mathf.Clamp(Mathf.FloorToInt((gateX - a.x) / seg), 0, n - 1);
        for (int i = 0; i < n; i++)
        {
            Vector3 c = a + dir * (seg * (i + 0.5f)) - outward * (seg * 0.5f);
            var piece = Edge(parent, i == gate ? "FantasyTownKit/fence-gate" : "FantasyTownKit/fence", c, outward, scale, false);
            if (piece == null) continue;
            var col = AddBoxCollider(piece);
            float tall = 2.5f / piece.transform.lossyScale.y;   // too tall to hop over
            col.center = new Vector3(col.center.x, tall * 0.5f, col.center.z);
            col.size = new Vector3(col.size.x, tall, col.size.z);
        }
    }

    // ---- Shaft rim ----------------------------------------------------------------
    static GameObject BuildRim()
    {
        var rim = new GameObject("Shaft Rim");
        float s = DigTerrain.Size;
        for (int x = -1; x <= (int)s; x++)
        for (int z = -1; z <= (int)s; z++)
        {
            bool inside = x >= 0 && x < s && z >= 0 && z < s;
            if (inside) continue;
            Place(rim.transform, "FantasyTownKit/planks", new Vector3(x + 0.5f, 0f, z + 0.5f), ((x + z) & 1) * 90f, width: 1.0f, collider: true);
        }
        foreach (var corner in new[] { new Vector2(-0.6f, -0.6f), new Vector2(s + 0.6f, -0.6f), new Vector2(-0.6f, s + 0.6f), new Vector2(s + 0.6f, s + 0.6f) })
            Place(rim.transform, "FantasyTownKit/pillar-wood", new Vector3(corner.x, 0.04f, corner.y), 0f, height: 1.0f, collider: true);
        return rim;
    }

    // ---- Excavation ----------------------------------------------------------------
    static GameObject BuildExcavation()
    {
        var ex = new GameObject("Excavation");
        var t = ex.transform;

        // A small camp on the floor.
        Place(t, "SurvivalKit/tent", OnGround(-5f, 52f), 20f, height: 2.0f);
        Place(t, "SurvivalKit/tent", OnGround(1.5f, 56f), -15f, height: 2.0f);
        Place(t, "SurvivalKit/campfire-pit", OnGround(-1.5f, 51.5f), 0f, height: 0.45f);
        Place(t, "NatureKit/log", OnGround(-3.2f, 50.2f), 70f, height: 0.4f);
        Lantern(t, OnGround(3f, 49f), 2.2f);

        // Supplies.
        Place(t, "SurvivalKit/barrel", OnGround(12f, 44f), 0f, height: 1.0f);
        Place(t, "SurvivalKit/barrel", OnGround(13.1f, 45f), 40f, height: 1.0f);
        Place(t, "SurvivalKit/box-large", OnGround(11f, 45.6f), 10f, height: 1.0f);
        Place(t, "SurvivalKit/box", OnGround(13.6f, 43f), -25f, height: 0.6f);
        Place(t, "FantasyTownKit/cart", OnGround(RampX, 30f), 0f, height: 1.8f);
        Place(t, "SurvivalKit/resource-planks", OnGround(-15f, 37f), 30f, width: 2.4f);
        Place(t, "SurvivalKit/resource-wood", OnGround(-17f, 38.5f), 0f, height: 0.8f);
        Place(t, "SurvivalKit/bucket", OnGround(-13.5f, 36.5f), 0f, height: 0.45f);
        Place(t, "SurvivalKit/tool-pickaxe", OnGround(-12.6f, 37.2f), 30f, height: 0.7f);
        Place(t, "SurvivalKit/workbench-grind", OnGround(-8f, 60f), 160f, height: 1.0f);
        Place(t, "SurvivalKit/resource-stone-large", OnGround(8f, 60f), 0f, height: 1.2f);
        Place(t, "SurvivalKit/resource-stone", OnGround(9.5f, 61f), 0f, height: 0.6f);

        Lantern(t, OnGround(-14f, 33f), 2.2f);
        Lantern(t, OnGround(14f, 33f), 2.2f);
        Lantern(t, OnGround(RampX - 2f, 15.5f), 2.2f);

        // Boulders on the terraces.
        var boulders = new (string m, float x, float z, float h)[]
        {
            ("rock_largeA", -26f, 30f, 1.6f), ("rock_largeB", 28f, 33f, 2.2f), ("rock_largeC", -10f, 66f, 1.8f),
            ("rock_largeD", 17f, 66f, 2.0f), ("rock_largeE", 33f, 50f, 2.4f), ("rock_largeF", -33f, 50f, 2.0f),
            ("stone_largeA", 6f, 24f, 1.2f), ("stone_largeC", -20f, 58f, 1.5f),
        };
        var rng = new System.Random(5);
        foreach (var b in boulders)
            Place(t, "NatureKit/" + b.m, OnGround(b.x, b.z, 0.2f), (float)rng.NextDouble() * 360f, height: b.h);
        return ex;
    }

    // Tall rocks either side of the excavation and on the far rim, framing the view north.
    static GameObject BuildFormations()
    {
        var f = new GameObject("Rock Formations");
        var rocks = new (string m, float x, float z, float h)[]
        {
            ("rock_tallA", -44f, 24f, 14f), ("rock_tallC", -38f, 31f, 9f), ("rock_tallE", -49f, 17f, 7f), ("stone_tallB", -51f, 31f, 11f),
            ("rock_tallB", 49f, 22f, 16f), ("rock_tallD", 44f, 29f, 10f), ("rock_tallF", 54f, 16f, 7f), ("stone_tallD", 56f, 29f, 12f),
            ("rock_tallG", 4f, 95f, 20f), ("rock_tallH", -8f, 98f, 13f), ("rock_tallI", 15f, 97f, 10f),
        };
        var rng = new System.Random(9);
        foreach (var r in rocks)
            Place(f.transform, "NatureKit/" + r.m, OnGround(r.x, r.z, 0.8f), (float)rng.NextDouble() * 360f, height: r.h);
        return f;
    }

    // ---- Placement helpers ---------------------------------------------------------
    static GameObject InstantiateKit(string kitModel, Transform parent)
    {
        string path = KenneyDir + "/" + kitModel + ".fbx";
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (src == null)
        {
            report.AppendLine("  MISSING model " + path);
            return null;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return go;
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // Bounds of all meshes under root, in root's local space.
    static Bounds LocalBounds(Transform root)
    {
        bool any = false;
        var b = new Bounds();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }
        return b;
    }

    static float ModelHeight(string kitModel)
    {
        var go = InstantiateKit(kitModel, null);
        if (go == null) return 1f;
        float h = WorldBounds(go).size.y;
        Object.DestroyImmediate(go);
        return h;
    }

    // Uniform scale that makes a modular piece `tileLength` long.
    static float KitScale(string kitModel, float tileLength)
    {
        var go = InstantiateKit(kitModel, null);
        if (go == null) return 1f;
        var b = WorldBounds(go);
        Object.DestroyImmediate(go);
        return tileLength / Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.z));
    }

    // Scales `model` (a child of holder) to the given height or width and stands it on
    // holder's origin.
    static void FitInside(GameObject model, Transform holder, float height = 0f, float width = 0f)
    {
        var b = LocalBounds(holder);
        float s = height > 0f ? height / Mathf.Max(0.001f, b.size.y) : width / Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.z));
        model.transform.localScale *= s;
        b = LocalBounds(holder);
        model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
    }

    // Places a model standing on `ground`, turned by yaw, scaled to a height or width.
    static GameObject Place(Transform parent, string kitModel, Vector3 ground, float yaw,
                            float height = 0f, float width = 0f, bool collider = false)
    {
        var go = InstantiateKit(kitModel, parent);
        if (go == null) return null;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        var b = WorldBounds(go);
        float s = height > 0f ? height / Mathf.Max(0.001f, b.size.y)
                : width > 0f ? width / Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.z)) : 1f;
        go.transform.localScale *= s;
        b = WorldBounds(go);
        go.transform.position += new Vector3(ground.x - b.center.x, ground.y - b.min.y, ground.z - b.center.z);
        if (collider) AddBoxCollider(go);
        return go;
    }

    // Places a modular edge piece (wall, fence) whose pivot is the tile centre, turned so
    // its slab is on the `outward` side of the tile.
    static GameObject Edge(Transform parent, string kitModel, Vector3 tileCenter, Vector3 outward, float scale, bool collider)
    {
        var go = InstantiateKit(kitModel, parent);
        if (go == null) return null;
        go.transform.localScale = Vector3.one * scale;
        go.transform.position = tileCenter;
        var off = WorldBounds(go).center - tileCenter;
        off.y = 0f;
        float yaw = off.sqrMagnitude > 0.0001f ? Vector3.SignedAngle(off, outward, Vector3.up) : 0f;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (collider) AddBoxCollider(go);
        return go;
    }

    static BoxCollider AddBoxCollider(GameObject go)
    {
        var b = LocalBounds(go.transform);
        var col = go.AddComponent<BoxCollider>();
        col.center = b.center;
        col.size = b.size;
        return col;
    }

    // A Fantasy Town lantern post with a warm light in it.
    static void Lantern(Transform parent, Vector3 ground, float height)
    {
        var post = Place(parent, "FantasyTownKit/lantern", ground, 0f, height: height, collider: true);
        if (post == null) return;
        var lightGo = new GameObject("Light");
        lightGo.transform.SetParent(post.transform, true);
        lightGo.transform.position = ground + Vector3.up * (height * 0.86f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.72f, 0.4f);
        light.range = 7f;
        light.intensity = 1.4f;
        light.shadows = LightShadows.None;
    }
}
