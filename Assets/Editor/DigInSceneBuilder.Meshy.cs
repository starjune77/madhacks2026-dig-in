using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Hero assets generated with Meshy. Each lives in Assets/ThirdParty/Meshy/<Name>/ as
// <Name>.fbx plus <Name>_base_color.png (never edited there). From those, this makes:
//   - a material in Assets/Materials,
//   - a find prefab for the memory (the object lying in the dirt),
//   - a card illustration rendered from the model (Assets/Textures/Cards).
// A memory without a Meshy asset keeps its stratum's Kenney stand-in and a blank card.
public static partial class DigInSceneBuilder
{
    const string MeshyDir = "Assets/ThirdParty/Meshy";
    const string CardDir = "Assets/Textures/Cards";
    const string MeshyFindVersion = "v2 meshy find";

    static void BuildMeshyFinds(MemoryData[] memories)
    {
        // memory index, asset folder, longest side in metres, yaw so its best side faces the card camera
        MeshyFind(memories[0], "BicycleBell", 0.55f, 0f);
    }

    static void MeshyFind(MemoryData data, string asset, float size, float cardYaw)
    {
        string fbxPath = MeshyDir + "/" + asset + "/" + asset + ".fbx";
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (source == null)
        {
            report.AppendLine("  meshy: " + fbxPath + " not found, " + data.title + " keeps its stand-in");
            return;
        }

        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(MeshyDir + "/" + asset + "/" + asset + "_base_color.png");
        var material = Mat("Meshy " + asset, Color.white, 0.12f, 0f, texture);

        string prefabPath = PrefabDir + "/Find " + data.title + ".prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null || prefab.transform.Find(MeshyFindVersion) == null)
        {
            var root = new GameObject("Find " + data.title);
            AddMarker(root, MeshyFindVersion);
            var col = root.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.35f;
            col.center = new Vector3(0f, 0.12f, 0f);
            var find = root.AddComponent<MemoryFind>();
            find.size = 1f;
            find.hover = 0.03f;

            var holder = new GameObject("Model").transform;
            holder.SetParent(root.transform, false);
            MeshyModel(source, holder, material, size);
            find.model = holder;

            // Softer than the stand-ins' glow: the object itself should be readable.
            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(root.transform, false);
            glowGo.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.88f, 0.62f);
            glow.range = 2.4f;
            glow.intensity = 0.9f;
            glow.shadows = LightShadows.None;
            prefab = SavePrefab(root, prefabPath);
        }
        else Skipped(prefabPath);

        var findPrefab = prefab.GetComponent<MemoryFind>();
        if (data.findPrefab != findPrefab) { data.findPrefab = findPrefab; EditorUtility.SetDirty(data); }

        var card = RenderCard(source, material, data.title, cardYaw);
        if (card != null && data.card != card) { data.card = card; EditorUtility.SetDirty(data); }
    }

    // The model, scaled so its longest side is `size` and standing on holder's origin.
    static GameObject MeshyModel(GameObject source, Transform holder, Material material, float size)
    {
        var model = (GameObject)PrefabUtility.InstantiatePrefab(source, holder);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        foreach (var r in model.GetComponentsInChildren<Renderer>())
            r.sharedMaterials = r.sharedMaterials.Select(_ => material).ToArray();

        var b = LocalBounds(holder);
        model.transform.localScale *= size / Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.y, b.size.z));
        b = LocalBounds(holder);
        model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
        return model;
    }

    // The card illustration: the model on warm paper, rendered once to a sprite. Delete
    // the PNG to render it again.
    static Sprite RenderCard(GameObject source, Material material, string title, float yaw)
    {
        EnsureFolder(CardDir);
        string path = CardDir + "/" + title + ".png";
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null) { Skipped(path); return existing; }

        const int w = 960, h = 660;                      // the card's illustration box, 2x
        var stage = new GameObject("Card Stage (temporary)");
        stage.transform.position = new Vector3(0f, -500f, 0f);
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        bool fog = RenderSettings.fog;
        try
        {
            var holder = new GameObject("Model").transform;
            holder.SetParent(stage.transform, false);
            holder.localRotation = Quaternion.Euler(0f, yaw, 0f);
            MeshyModel(source, holder, material, 1f);
            var b = WorldBounds(holder.gameObject);

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(stage.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.93f, 0.82f, 0.66f);
            cam.fieldOfView = 24f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.aspect = (float)w / h;
            cam.targetTexture = rt;
            var dir = Quaternion.Euler(24f, 205f, 0f) * Vector3.forward;   // from the front right, a little above
            float radius = b.extents.magnitude;
            cam.transform.position = b.center - dir * (radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.66f);   // tight: the bounding sphere is generous
            cam.transform.rotation = Quaternion.LookRotation(dir);

            // A soft key light of its own, so the card does not depend on the scene's sun.
            var keyGo = new GameObject("Key");
            keyGo.transform.SetParent(stage.transform, false);
            keyGo.transform.position = b.center - dir * radius * 2f + Vector3.up * radius * 2f + cam.transform.right * radius * 1.5f;
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Point;
            key.color = new Color(1f, 0.96f, 0.9f);
            key.range = radius * 12f;
            key.intensity = 2.2f;
            key.shadows = LightShadows.None;

            RenderSettings.fog = false;
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
