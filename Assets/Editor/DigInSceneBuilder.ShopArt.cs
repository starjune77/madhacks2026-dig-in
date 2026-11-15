using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Pictures for the cat's price board, rendered once from the real models (like the memory
// cards) onto the tile's own paper colour: the eleven minerals, the four spade tiers, the
// bags and the lanterns. Saved in Assets/Textures/Shop; delete a PNG to render it again.
// Also the spade tiers' materials: one Meshy spade, each tier a little cleaner and
// brighter on the blade (the metallic map keeps the wooden handle wood).
public static partial class DigInSceneBuilder
{
    const string ShopArtDir = "Assets/Textures/Shop";
    static readonly Color TileColor = new Color(0.97f, 0.93f, 0.84f);

    // Bag and lantern tiers (Poly Haven models), in DigShop's level order.
    static readonly string[] BagModels = { "wicker_basket_02", "wicker_basket_01", "wooden_bucket_01", "vintage_suitcase" };
    static readonly string[] LanternModels = { "wooden_lantern_01", "Lantern_01", "vintage_oil_lamp" };

    static Material[] SpadeTierMaterials()
    {
        var baseMat = AssetDatabase.LoadAssetAtPath<Material>(ModelMatDir + "/Meshy Spade.mat");
        var tiers = new Material[4];
        tiers[0] = baseMat;
        if (baseMat == null) return tiers;
        var metal = ImportScan(MeshyRoot + "/Spade/Spade_textures/metallic.png", TextureImporterType.Default, false, 2048);
        var defs = new (Color tint, float smooth)[]
        {
            (new Color(1f, 0.97f, 0.92f), 0.4f),     // Garden: cleaned up
            (new Color(0.9f, 0.93f, 0.97f), 0.52f),  // Steel: cooler, harder
            (new Color(1f, 1f, 1f), 0.66f),          // Fine Steel: bright
        };
        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            tiers[i + 1] = LoadOrCreate(ModelMatDir + "/Spade Tier " + (i + 2) + ".mat", () =>
            {
                var m = new Material(baseMat);
                if (metal != null)
                {
                    m.SetTexture("_MetallicGlossMap", metal);
                    m.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                m.SetColor("_BaseColor", d.tint);
                m.SetFloat("_Smoothness", d.smooth);
                return m;
            });
        }
        return tiers;
    }

    static void BuildShopArt(DigShop shop, MineralData[] minerals)
    {
        EnsureFolder(ShopArtDir);
        if (minerals != null)
            foreach (var m in minerals)
            {
                if (m == null || m.model == null) continue;
                var icon = RenderItem("Mineral " + m.title, h => MeshyModel(m.model, h, null, 1f), Quaternion.Euler(30f, 205f, 0f));
                if (m.icon != icon) { m.icon = icon; EditorUtility.SetDirty(m); }
            }

        var spade = MeshyAsset("Spade", 0.3f);
        var tiers = SpadeTierMaterials();
        if (spade != null)
            shop.spadeIcons = tiers.Select((mat, i) => RenderItem("Spade " + (i + 1), h =>
            {
                h.localRotation = Quaternion.Euler(0f, 0f, -40f);   // leaning across the picture
                MeshyModel(spade, h, mat, 1f);
            }, Quaternion.Euler(10f, 180f, 0f), 0.6f)).ToArray();
        shop.bagIcons = BagModels.Select(id => RenderPolyHaven("Bag " + id, id, id == "vintage_suitcase" ? "_01_" : null)).ToArray();
        shop.lanternIcons = LanternModels.Select(id => RenderPolyHaven("Lantern " + id, id)).ToArray();
        EditorUtility.SetDirty(shop);
    }

    // `keep`: only the parts whose names contain it (the suitcase model is a pair).
    static Sprite RenderPolyHaven(string name, string id, string keep = null)
    {
        var asset = PolyHavenAsset(id);
        if (asset == null) return null;
        return RenderItem(name, h =>
        {
            var model = MeshyModel(asset, h, null, 1f);
            if (keep != null)
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                    if (!r.name.Contains(keep)) Object.DestroyImmediate(r.gameObject);
        }, Quaternion.Euler(24f, 205f, 0f));
    }

    static Sprite RenderItem(string name, System.Action<Transform> build, Quaternion view, float tight = 0.62f)
    {
        return RenderStill(ShopArtDir + "/" + name + ".png", 440, 300, TileColor, build, view, tight);
    }

    // Renders a model built by `build` (under a holder, standing on its origin) to a PNG
    // sprite at `path`, seen along `view`, on a flat `background`, with a soft key light of
    // its own so it never depends on the scene's sun. Skipped if the PNG exists.
    static Sprite RenderStill(string path, int w, int h, Color background, System.Action<Transform> build, Quaternion view, float tight)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null) { Skipped(path); return existing; }

        var stage = new GameObject("Still Stage (temporary)");
        stage.transform.position = new Vector3(0f, -500f, 0f);
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        bool fog = RenderSettings.fog;
        float ambient = RenderSettings.ambientIntensity;
        try
        {
            var holder = new GameObject("Model").transform;
            holder.SetParent(stage.transform, false);
            build(holder);
            var b = WorldBounds(holder.gameObject);

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(stage.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.fieldOfView = 24f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.aspect = (float)w / h;
            cam.targetTexture = rt;
            var dir = view * Vector3.forward;
            float radius = b.extents.magnitude;
            cam.transform.position = b.center - dir * (radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * tight);
            cam.transform.rotation = Quaternion.LookRotation(dir);

            var keyGo = new GameObject("Key");
            keyGo.transform.SetParent(stage.transform, false);
            keyGo.transform.position = b.center - dir * radius * 2f + Vector3.up * radius * 2f + cam.transform.right * radius * 1.5f;
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Point;
            key.color = new Color(1f, 0.96f, 0.9f);
            key.range = radius * 12f;
            key.intensity = 0.8f;
            key.shadows = LightShadows.None;

            RenderSettings.fog = false;
            RenderSettings.ambientIntensity = 0.7f;   // the scene's sky would wash it out
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else cam.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
        }
        finally
        {
            RenderSettings.fog = fog;
            RenderSettings.ambientIntensity = ambient;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(stage);
        }

        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
        Created(path);
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
