using UnityEditor;
using UnityEngine;

// Where to dig, told without words. Nothing says "dig here"; the patch is simply the most
// interesting thing in the yard:
//   - framing: a loose ring of stones and a rough ring of small wildflowers around the bare
//     patch, as if someone marked it long ago, and a trowel left stuck in the dirt;
//   - light: a soft, warm pool of light on the patch, a touch brighter than the yard;
//   - curiosity: now and then, a faint glint in the soil.
// The light and the glint ("Lure", DigGame.digSpotLure) fade away once you start digging.
// Stones, flowers and trowel come loose and fall if the ground under them is dug
// (DigLoosen), so they never float. Poly Haven models (CC0).
public static partial class DigInSceneBuilder
{
    const string DigSpotVersion = "v6 old spade";

    static GameObject BuildDigSpot()
    {
        var spot = new GameObject("Dig Spot");
        var t = spot.transform;
        AddMarker(spot, DigSpotVersion);
        int debris = LayerMask.NameToLayer("Debris");
        var rng = new System.Random(31);

        // ---- A loose ring of stones around the bare patch ------------------------------------
        var stoneA = PolyHavenAsset("rock_07");
        var stoneB = PolyHavenAsset("rock_09");
        const int count = 13;
        for (int i = 0; i < count; i++)
        {
            float a = (i + (float)rng.NextDouble() * 0.5f) / count * Mathf.PI * 2f;
            float r = 2.9f + (float)rng.NextDouble() * 0.35f;
            float x = DigStart.x + Mathf.Cos(a) * r, z = DigStart.y + Mathf.Sin(a) * r;
            var asset = i % 2 == 0 ? stoneA : stoneB;
            if (asset == null) continue;
            float h = Mathf.Lerp(0.14f, 0.26f, (float)rng.NextDouble());
            var stone = PlaceAsset(t, asset, OnGround(x, z, h * 0.3f), (float)rng.NextDouble() * 360f, height: h);
            Loosen(stone, debris);
        }

        // ---- A rough ring of wildflowers, mostly just outside the stones ----------------------
        // Celandine is light (9k triangles); dandelions (64k) only now and then. Scanned
        // plants are heavy: keep the count low and let them cast no shadows.
        var celandine = PolyHavenAsset("celandine_01");
        var dandelion = PolyHavenAsset("dandelion_01");
        const int clumps = 17;
        for (int i = 0; i < clumps; i++)
        {
            float a = (i + (float)rng.NextDouble() * 0.7f) / clumps * Mathf.PI * 2f;
            float r = 3.2f + (float)rng.NextDouble() * 0.5f;
            var asset = i % 4 == 1 ? dandelion : celandine;
            if (asset == null) continue;
            // A small clump of three or four, like flowers that seeded themselves.
            int n = asset == dandelion ? 1 : 3 + rng.Next(2);
            for (int k = 0; k < n; k++)
            {
                float x = DigStart.x + Mathf.Cos(a) * r + ((float)rng.NextDouble() - 0.5f) * 0.45f;
                float z = DigStart.y + Mathf.Sin(a) * r + ((float)rng.NextDouble() - 0.5f) * 0.45f;
                float h = Mathf.Lerp(0.26f, 0.42f, (float)rng.NextDouble());
                var flower = PlaceAsset(t, asset, OnGround(x, z), (float)rng.NextDouble() * 360f, height: h);
                foreach (var rend in flower.GetComponentsInChildren<Renderer>()) rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Loosen(flower, debris);
            }
        }

        // ---- The old spade, stuck upright in the soil, waiting to be taken ---------------------
        var spadeAsset = MeshyAsset("Spade", 0.3f);
        if (spadeAsset != null)
        {
            const float length = 0.95f, sunk = 0.22f;
            var root = new GameObject("Old Spade");
            root.transform.SetParent(t, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(spadeAsset, root.transform);
            var b = LocalBounds(root.transform);
            model.transform.localScale *= length / Mathf.Max(0.001f, b.size.y);   // the model stands blade down
            b = LocalBounds(root.transform);
            model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            root.transform.position = OnGround(DigStart.x - 0.9f, DigStart.y - 1.1f) - Vector3.up * sunk;
            root.transform.rotation = Quaternion.Euler(5f, 25f, -6f);   // leaning a little, as if left mid-dig
            var ws = root.AddComponent<DigWorldSpade>();
            ws.length = length;
        }

        // ---- The lure: a pool of warm light and a glint in the soil ---------------------------
        var lure = new GameObject("Lure").transform;
        lure.SetParent(t, false);
        lure.position = OnGround(DigStart.x, DigStart.y);

        var pool = new GameObject("Pool of Light").AddComponent<Light>();
        pool.transform.SetParent(lure, false);
        pool.transform.localPosition = new Vector3(0.4f, 3.6f, -0.3f);
        pool.transform.rotation = Quaternion.LookRotation(Vector3.down + new Vector3(-0.1f, 0f, 0.08f));
        pool.type = LightType.Spot;
        pool.spotAngle = 56f;
        pool.innerSpotAngle = 22f;   // a soft edge
        pool.range = 6f;
        pool.intensity = 13f;
        pool.color = new Color(1f, 0.84f, 0.6f);
        pool.shadows = LightShadows.None;

        var glintPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Mineral Glint.prefab");
        if (glintPrefab != null)
        {
            var glint = (GameObject)PrefabUtility.InstantiatePrefab(glintPrefab, lure);
            glint.name = "Glint";
            glint.transform.position = OnGround(DigStart.x + 0.35f, DigStart.y - 0.25f, -0.04f);
            var ps = glint.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.13f);   // seen from a few metres away
            main.maxParticles = 2;
            var emission = ps.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            var shape = ps.shape;
            shape.radius = 0.15f;
        }
        return spot;
    }

    static Vector3 LongAxis(GameObject go)
    {
        var b = LocalBounds(go.transform);
        Vector3 s = b.size;
        return s.x >= s.y && s.x >= s.z ? Vector3.right : s.y >= s.z ? Vector3.up : Vector3.forward;
    }

    static void Loosen(GameObject go, int layer)
    {
        if (go == null) return;
        foreach (var tr in go.GetComponentsInChildren<Transform>(true)) if (layer >= 0) tr.gameObject.layer = layer;
        var b = LocalBounds(go.transform);
        var col = go.AddComponent<BoxCollider>();
        col.center = b.center;
        col.size = b.size * 0.9f;
        go.AddComponent<DigLoosen>();
    }
}
