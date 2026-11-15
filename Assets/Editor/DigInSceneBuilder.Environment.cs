using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// The world around the dig, realistic:
//
//   The backyard: an old family house (Meshy) whose back door opens onto the yard, the
//   memory shelf beside the door, the cat's shop corner, a weathered picket fence (Meshy),
//   garden things (Poly Haven), real grass (DigGrass, on the diggable ground).
//   Around it: Unity Terrain with the demo pack's photoscanned grass layers, detail grass
//   and ferns, SpeedTree conifers and cypresses on the hills, Poly Haven boulders.
//
// Models are placed by their measured bounds, so units and pivots do not matter.
public static partial class DigInSceneBuilder
{
    // ---- Layout ----------------------------------------------------------------
    static readonly Vector3 ShelfPos = new Vector3(0.8f, 0f, -15.35f);
    static readonly Vector3 BenchPos = new Vector3(12.3f, 0f, -7f);     // the cat's shop
    static readonly Vector3 SpawnPos = new Vector3(3f, 0.8f, -8f);
    static readonly Vector2 DigStart = new Vector2(2.5f, 2.5f);      // the hollow where the dig begins

    const float YardMinX = -12f, YardMaxX = 17f, YardMinZ = -16f, YardMaxZ = 10f;
    const float HouseMinX = -4f, HouseMaxX = 9.6f;   // the house's back wall runs along z = YardMinZ
    const float HouseYaw = 0f;                       // turns the Meshy house so its back door faces the yard

    // Terrain: 256 m square, 0.5 m height cells. It has a hole exactly over the dig volume
    // (DigVolume), which draws and collides the ground there instead.
    const float TerrainSize = 256f, TerrainBaseY = -14f, TerrainHeight = 52f;
    static readonly Vector3 TerrainOrigin = new Vector3(-126f, TerrainBaseY, -126f);
    const int HeightRes = 513, SplatRes = 512, DetailRes = 512;

    static void BuildEnvironment(Scene scene)
    {
        var env = Root(scene, "Environment", out bool created);
        if (created) new GameObject(EnvVersion).transform.SetParent(env, false);
        Ensure(env, "Terrain", BuildTerrain);
        Ensure(env, "House", BuildHouse);
        Ensure(env, "Yard", BuildYard);
        Ensure(env, "Rocks and Trees", BuildSurroundings);
        Ensure(env, "Dig Spot", BuildDigSpot);
    }

    // ---- Height function (shared by terrain and prop placement) --------------------
    static float Smooth(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

    static float RectDistance(float x, float z, float minX, float maxX, float minZ, float maxZ)
    {
        float dx = Mathf.Max(minX - x, 0f, x - maxX);
        float dz = Mathf.Max(minZ - z, 0f, z - maxZ);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static float GroundHeight(float x, float z)
    {
        // The yard (and house) is flat at 0; gentle meadow swells away from it, hills ring everything.
        float dYard = RectDistance(x, z, YardMinX - 2f, YardMaxX + 2f, YardMinZ - 5f, YardMaxZ + 1.5f);
        float mound = (Mathf.PerlinNoise(x * 0.03f + 20f, z * 0.03f + 40f) - 0.45f) * 3.5f * Smooth(2f, 26f, dYard);
        float r = Vector2.Distance(new Vector2(x, z), new Vector2(2.5f, 0f));
        float hills = Smooth(50f, 115f, r) * (12f + 16f * Mathf.PerlinNoise(x * 0.013f + 11f, z * 0.013f + 5f));

        // The slight hollow in the grass where the dig begins.
        float hollow = 0.32f * (1f - Smooth(0.3f, 2.4f, Vector2.Distance(new Vector2(x, z), DigStart)));
        return mound + hills - hollow;
    }

    static float Slope(float x, float z)
    {
        const float e = 0.3f;
        float dx = (GroundHeight(x + e, z) - GroundHeight(x - e, z)) / (2f * e);
        float dz = (GroundHeight(x, z + e) - GroundHeight(x, z - e)) / (2f * e);
        return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
    }

    static Vector3 OnGround(float x, float z, float sink = 0f) => new Vector3(x, GroundHeight(x, z) - sink, z);

    static bool InVolume(float x, float z, float margin) =>
        x > DigVolume.Min.x - margin && x < DigVolume.Min.x + DigVolume.Size.x + margin &&
        z > DigVolume.Min.z - margin && z < DigVolume.Min.z + DigVolume.Size.z + margin;

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
                AssetDatabase.LoadAssetAtPath<TerrainLayer>(DemoPack + "/Terrain/Grass_A/Grass_A.terrainlayer"),
                AssetDatabase.LoadAssetAtPath<TerrainLayer>(DemoPack + "/Terrain/Grass_Soil_A/Grass_Soil_A.terrainlayer"),
            };
            FillHeights(data);
            FillSplat(data);
            FillTrees(data);
            FillDetails(data);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Created(dataPath);
        }

        var go = Terrain.CreateTerrainGameObject(data);
        go.transform.position = TerrainOrigin;
        var terrain = go.GetComponent<Terrain>();
        terrain.materialTemplate = LoadOrCreate(TerrainDir + "/Terrain.mat",
            () => new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")));
        terrain.heightmapPixelError = 3f;
        terrain.basemapDistance = 400f;
        terrain.treeDistance = 600f;
        terrain.treeBillboardDistance = 120f;
        terrain.treeCrossFadeLength = 20f;
        terrain.detailObjectDistance = 90f;
        terrain.detailObjectDensity = 1f;
        terrain.drawInstanced = true;
        return go;
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

        // A hole over the dig volume, which takes over there.
        int startX = Mathf.RoundToInt((DigVolume.Min.x - TerrainOrigin.x) / cell);
        int startZ = Mathf.RoundToInt((DigVolume.Min.z - TerrainOrigin.z) / cell);
        int countX = Mathf.RoundToInt(DigVolume.Size.x / cell), countZ = Mathf.RoundToInt(DigVolume.Size.z / cell);
        data.SetHoles(startX, startZ, new bool[countZ, countX]);   // false = hole
    }

    // Layers: 0 meadow grass, 1 grass worn down to soil (patches and steep ground).
    static void FillSplat(TerrainData data)
    {
        float cell = TerrainSize / SplatRes;
        var a = new float[SplatRes, SplatRes, 2];
        for (int iz = 0; iz < SplatRes; iz++)
        for (int ix = 0; ix < SplatRes; ix++)
        {
            float x = TerrainOrigin.x + (ix + 0.5f) * cell, z = TerrainOrigin.z + (iz + 0.5f) * cell;
            float soil = Mathf.Max(Smooth(22f, 36f, Slope(x, z)), Smooth(0.62f, 0.78f, Mathf.PerlinNoise(x * 0.06f + 3f, z * 0.06f + 9f)) * 0.8f);
            a[iz, ix, 0] = 1f - soil;
            a[iz, ix, 1] = soil;
        }
        data.SetAlphamaps(0, 0, a);
    }

    // SpeedTree conifers and cypresses on the hills; a few cypresses closer in.
    static void FillTrees(TerrainData data)
    {
        var conifer = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPack + "/Prefabs/Trees/Conifer/Conifer.prefab");
        var cypress = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPack + "/Prefabs/Trees/Cypress/Cypress_Forest_Desktop.prefab");
        if (conifer == null || cypress == null) { report.AppendLine("  MISSING SpeedTree prefabs in " + DemoPack); return; }
        data.treePrototypes = new[] { new TreePrototype { prefab = conifer }, new TreePrototype { prefab = cypress } };

        var rng = new System.Random(4242);
        var trees = new List<TreeInstance>();
        for (float z = TerrainOrigin.z + 3f; z < TerrainOrigin.z + TerrainSize - 3f; z += 5f)
        for (float x = TerrainOrigin.x + 3f; x < TerrainOrigin.x + TerrainSize - 3f; x += 5f)
        {
            float px = x + (float)(rng.NextDouble() - 0.5) * 4f;
            float pz = z + (float)(rng.NextDouble() - 0.5) * 4f;
            if (InVolume(px, pz, 2f)) continue;
            float r = Vector2.Distance(new Vector2(px, pz), new Vector2(2.5f, 0f));
            if (Slope(px, pz) > 32f) continue;
            float density = Mathf.PerlinNoise(px * 0.035f + 70f, pz * 0.035f + 30f);
            float threshold = Mathf.Lerp(0.78f, 0.36f, Smooth(32f, 90f, r));   // sparse near, dense far
            if (density < threshold) continue;

            bool near = r < 55f;
            int k = near ? (rng.NextDouble() < 0.7 ? 1 : 0) : (rng.NextDouble() < 0.75 ? 0 : 1);
            float s = k == 0 ? Mathf.Lerp(0.45f, 0.8f, (float)rng.NextDouble()) : Mathf.Lerp(0.8f, 1.25f, (float)rng.NextDouble());
            trees.Add(new TreeInstance
            {
                prototypeIndex = k,
                position = new Vector3((px - TerrainOrigin.x) / TerrainSize, (GroundHeight(px, pz) - TerrainBaseY) / TerrainHeight, (pz - TerrainOrigin.z) / TerrainSize),
                widthScale = s, heightScale = s * Mathf.Lerp(0.9f, 1.1f, (float)rng.NextDouble()),
                rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                color = Color.white, lightmapColor = Color.white,
            });
        }
        data.SetTreeInstances(trees.ToArray(), true);
        report.AppendLine("  placed " + trees.Count + " SpeedTrees");
    }

    // Detail meshes from the demo pack (grass clumps, dry grass, ferns), denser near the yard.
    static readonly string[] DetailPrefabs = { "Grass_B", "Grass_C", "Grass_D", "GrassDry_A", "Fern_A", "Grass_A" };

    static void FillDetails(TerrainData data)
    {
        var protos = new List<DetailPrototype>();
        foreach (var n in DetailPrefabs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPack + "/Prefabs/Details/" + n + ".prefab");
            if (prefab == null) continue;
            protos.Add(new DetailPrototype
            {
                prototype = prefab, usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.VertexLit,
                minWidth = 0.8f, maxWidth = 1.3f, minHeight = 0.8f, maxHeight = 1.3f, noiseSpread = 0.4f, useDensityScaling = true,
                alignToGround = 0.6f, positionJitter = 0.9f,
            });
        }
        data.SetDetailResolution(DetailRes, 32);
        data.detailPrototypes = protos.ToArray();
        data.RefreshPrototypes();

        float cell = TerrainSize / DetailRes;
        var rng = new System.Random(99);
        for (int k = 0; k < protos.Count; k++)
        {
            var map = new int[DetailRes, DetailRes];
            for (int iz = 0; iz < DetailRes; iz++)
            for (int ix = 0; ix < DetailRes; ix++)
            {
                float x = TerrainOrigin.x + (ix + 0.5f) * cell, z = TerrainOrigin.z + (iz + 0.5f) * cell;
                if (InVolume(x, z, 0f)) continue;
                float r = Vector2.Distance(new Vector2(x, z), new Vector2(2.5f, 0f));
                if (r > 95f) continue;
                float patch = Mathf.PerlinNoise(x * 0.08f + k * 13f, z * 0.08f + k * 7f);
                float want = k == 0 || k == 1 ? 2.2f : k == 2 ? 1.4f : k == 3 ? 1.0f * patch : k == 4 ? (patch > 0.62f ? 1.2f : 0f) : 2f;
                want *= Mathf.Lerp(1f, 0.35f, Smooth(40f, 95f, r));
                int n = Mathf.FloorToInt(want);
                if (rng.NextDouble() < want - n) n++;
                map[iz, ix] = n;
            }
            data.SetDetailLayer(0, 0, k, map);
        }
    }

    // ---- House --------------------------------------------------------------------
    // The Meshy house, scaled to span the back of the yard; a collider keeps you out.
    static GameObject BuildHouse()
    {
        var house = new GameObject("House");
        var model = MeshyAsset("House", 0.15f);
        float width = HouseMaxX - HouseMinX;
        var go = model != null ? PlaceAsset(house.transform, model, Vector3.zero, HouseYaw, width: width) : null;
        if (go != null)
        {
            var b = WorldBounds(go);
            go.transform.position += new Vector3((HouseMinX + HouseMaxX) * 0.5f - b.center.x, 0f, YardMinZ - b.max.z + 0.25f);
            var col = house.AddComponent<BoxCollider>();
            b = WorldBounds(go);
            col.center = b.center;
            col.size = b.size - new Vector3(0f, 0f, 0.5f);
        }

        // By the back door: a rocking chair and potted plants.
        Prop(house.transform, "Rockingchair_01", new Vector3(6.4f, 0f, YardMinZ + 0.9f), 200f, height: 1.0f, collider: true);
        Prop(house.transform, "shrub_04", new Vector3(-3.4f, 0f, YardMinZ + 0.7f), 0f, height: 0.7f);
        Prop(house.transform, "fern_02", new Vector3(9.4f, 0f, YardMinZ + 0.8f), 30f, height: 0.6f);
        return house;
    }

    // ---- Yard ---------------------------------------------------------------------
    static GameObject BuildYard()
    {
        var yard = new GameObject("Yard");
        var t = yard.transform;

        // Fence on three sides; the house closes the south side.
        var fence = MeshyAsset("Fence", 0.12f);
        if (fence != null)
        {
            FenceRun(t, fence, new Vector3(YardMinX, 0f, YardMinZ - 3f), new Vector3(YardMinX, 0f, YardMaxZ));
            FenceRun(t, fence, new Vector3(YardMaxX, 0f, YardMinZ - 3f), new Vector3(YardMaxX, 0f, YardMaxZ));
            FenceRun(t, fence, new Vector3(YardMinX, 0f, YardMaxZ), new Vector3(YardMaxX, 0f, YardMaxZ));
            // Behind the house too: where the house stands, the fence is hidden inside it.
            FenceRun(t, fence, new Vector3(YardMinX, 0f, YardMinZ - 3f), new Vector3(YardMaxX, 0f, YardMinZ - 3f));
        }

        // Garden things, a lived-in yard.
        Prop(t, "wooden_barrels_01", new Vector3(15.1f, 0f, -14.2f), 20f, height: 1.0f, collider: true);
        Prop(t, "wooden_crate_01", new Vector3(-10.3f, 0f, -14.2f), 15f, height: 0.45f, collider: true);
        Prop(t, "wooden_crate_01", new Vector3(-9.3f, 0f, -13.6f), -20f, height: 0.42f, collider: true);
        Prop(t, "WoodenTable_03", new Vector3(-6.8f, 0f, -12.6f), 80f, height: 0.78f, collider: true);
        Prop(t, "wicker_basket_01", new Vector3(-6.8f, 0.78f, -12.4f), 10f, height: 0.25f);
        Prop(t, "dead_tree_trunk", new Vector3(-10.4f, 0f, -5.5f), 85f, height: 0.4f, collider: true);
        Prop(t, "tree_stump_01", new Vector3(-9f, 0f, 4.5f), 0f, height: 0.55f, collider: true);
        Prop(t, "fern_02", new Vector3(-8.2f, 0f, 5.4f), 120f, height: 0.5f);
        Prop(t, "watering_can_metal_01", new Vector3(6.6f, 0f, -1.4f), 140f, height: 0.36f, collider: true);
        Prop(t, "garden_gnome", new Vector3(5.8f, 0f, 8.7f), 190f, height: 0.42f, collider: true);
        Prop(t, "boulder_01", new Vector3(8.2f, 0f, 5.2f), 30f, height: 0.75f, collider: true);
        Prop(t, "rock_moss_set_01", new Vector3(-11f, 0f, 9f), 0f, height: 0.6f);
        Prop(t, "shrub_02", new Vector3(-10.2f, 0f, 8.6f), 10f, height: 1.5f);
        Prop(t, "shrub_02", new Vector3(15.6f, 0f, 8.4f), 190f, height: 1.4f);
        Prop(t, "shrub_04", new Vector3(16.2f, 0f, 1.8f), 0f, height: 0.7f);
        Prop(t, "shrub_04", new Vector3(-11.3f, 0f, -9.5f), 90f, height: 0.8f);
        return yard;
    }

    // Fence sections from a to b, each scaled to fit, posts lined up.
    static void FenceRun(Transform parent, GameObject fence, Vector3 a, Vector3 b)
    {
        float length = Vector3.Distance(a, b);
        int n = Mathf.Max(1, Mathf.RoundToInt(length / 2.6f));
        float seg = length / n;
        Vector3 dir = (b - a).normalized;
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        // Which way is the model long? Sections run along that axis.
        var probe = (GameObject)PrefabUtility.InstantiatePrefab(fence);
        var pb = WorldBounds(probe);
        Object.DestroyImmediate(probe);
        float modelYaw = pb.size.x >= pb.size.z ? -90f : 0f;

        for (int i = 0; i < n; i++)
        {
            Vector3 c = a + dir * (seg * (i + 0.5f));
            var go = PlaceAsset(parent, fence, c, yaw + modelYaw, width: seg * 1.02f);
            if (go == null) continue;
            var col = go.AddComponent<BoxCollider>();
            var lb = LocalBounds(go.transform);
            float tall = 2.4f / go.transform.lossyScale.y;   // too tall to hop over
            col.center = new Vector3(lb.center.x, tall * 0.5f, lb.center.z);
            col.size = new Vector3(lb.size.x, tall, lb.size.z);
        }
    }

    // ---- Around the yard ------------------------------------------------------------
    // Big trees just past the fence and boulders on the meadow.
    static GameObject BuildSurroundings()
    {
        var f = new GameObject("Rocks and Trees");
        var cypress = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPack + "/Prefabs/Trees/Cypress/Cypress_Forest_Desktop.prefab");
        var conifer = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPack + "/Prefabs/Trees/Conifer/Conifer.prefab");
        var trees = new (GameObject prefab, float x, float z, float h)[]
        {
            (cypress, -15.5f, -13f, 11f), (cypress, 20.5f, -12f, 12f), (cypress, -16f, 5f, 10f), (conifer, 22f, 6f, 17f),
            (cypress, -14.5f, 13.5f, 11.5f), (conifer, -24f, -4f, 19f), (cypress, 12f, 14f, 10f), (cypress, 26f, -2f, 12.5f),
        };
        var rng = new System.Random(9);
        foreach (var t in trees)
        {
            if (t.prefab == null) continue;
            PlaceAsset(f.transform, t.prefab, OnGround(t.x, t.z, 0.1f), (float)rng.NextDouble() * 360f, height: t.h);
        }

        var rocks = new (string id, float x, float z, float h)[]
        {
            ("boulder_01", -20f, 16f, 1.4f), ("boulder_01", 24f, 15f, 1.1f), ("rock_moss_set_01", 19f, -18f, 0.9f),
            ("boulder_01", -2f, 26f, 1.6f), ("rock_moss_set_01", 9f, 24f, 1.0f), ("shrub_02", -6f, 18f, 1.6f), ("shrub_02", 14f, 20f, 1.4f),
        };
        foreach (var r in rocks)
            Prop(f.transform, r.id, OnGround(r.x, r.z, 0.15f), (float)rng.NextDouble() * 360f, height: r.h);
        return f;
    }

    static GameObject Prop(Transform parent, string polyHavenId, Vector3 ground, float yaw, float height = 0f, float width = 0f, bool collider = false)
    {
        var asset = PolyHavenAsset(polyHavenId);
        return asset == null ? null : PlaceAsset(parent, asset, ground, yaw, height, width, collider);
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
        return Fit(go, ground, yaw, height, width, collider);
    }

    static GameObject PlaceAsset(Transform parent, GameObject asset, Vector3 ground, float yaw,
                                 float height = 0f, float width = 0f, bool collider = false)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return Fit(go, ground, yaw, height, width, collider);
    }

    static GameObject Fit(GameObject go, Vector3 ground, float yaw, float height, float width, bool collider)
    {
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
