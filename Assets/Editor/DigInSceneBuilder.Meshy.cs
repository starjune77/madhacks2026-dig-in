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
    const string MeshyFindVersion = "v3 realistic find";

    static void BuildMeshyFinds(MemoryData[] memories)
    {
        // asset folder and longest side (m) per memory, in shelf order
        var defs = new (string folder, float size)[]
        {
            ("BicycleBell", 0.2f), ("Crayon", 0.16f), ("ToyCar", 0.22f),
            ("TestPaper", 0.26f), ("Eraser", 0.12f), ("FoldedNote", 0.14f),
            ("HouseKey", 0.16f), ("TrainTicket", 0.18f), ("CoffeeMug", 0.16f),
        };
        for (int i = 0; i < memories.Length && i < defs.Length; i++)
            MeshyFind(memories[i], defs[i].folder, defs[i].size, 0f);
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

        source = MeshyAsset(asset, 0.3f);
        var material = AssetDatabase.LoadAssetAtPath<Material>(ModelMatDir + "/Meshy " + asset + ".mat");

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
            glow.range = 1.6f;
            glow.intensity = 0.6f;
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
        if (material != null)
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
        return RenderStill(CardDir + "/" + title + ".png", 960, 660, new Color(0.93f, 0.82f, 0.66f), holder =>   // the card's illustration box, 2x
        {
            holder.localRotation = Quaternion.Euler(0f, yaw, 0f);
            MeshyModel(source, holder, material, 1f);
        }, Quaternion.Euler(24f, 205f, 0f), 0.8f);   // from the front right, a little above; tight: the bounding sphere is generous
    }
}
