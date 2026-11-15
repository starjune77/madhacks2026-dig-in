using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// The economy as assets: the eleven minerals (Meshy models, Assets/Data/Minerals), the
// glint that makes them easy to spot, and the cat's price board in the HUD.
public static partial class DigInSceneBuilder
{
    const string MineralDir = "Assets/Data/Minerals";

    // title, Meshy folder, coins, weight in Childhood / School Days / Growing Up, smoothness, metallic, glint, line
    static readonly (string title, string folder, int value, Vector3 weights, float smooth, float metal, Color glint, string line)[] MineralDefs =
    {
        ("Quartz", "Quartz", 3, new Vector3(3f, 1.5f, 0.6f), 0.75f, 0f, new Color(1f, 1f, 1f), "Clear, like the mornings before school."),
        ("Feldspar", "Feldspar", 4, new Vector3(2.5f, 1.5f, 0.8f), 0.45f, 0f, new Color(1f, 0.85f, 0.8f), "Pink as a sunburn after a long day outside."),
        ("Mica", "Mica", 5, new Vector3(2f, 1.5f, 0.6f), 0.7f, 0.3f, new Color(1f, 0.95f, 0.8f), "Thin layers, like the pages you meant to finish."),
        ("Jasper", "Jasper", 8, new Vector3(1.2f, 1.5f, 0.8f), 0.55f, 0f, new Color(1f, 0.75f, 0.6f), "Warm in the hand, like a pocket stone you never threw."),
        ("Garnet", "Garnet", 14, new Vector3(0.4f, 1.2f, 1f), 0.7f, 0f, new Color(1f, 0.55f, 0.55f), "Deep red, like a ribbon on a birthday you almost forgot."),
        ("Malachite", "Malachite", 18, new Vector3(0.2f, 1f, 1f), 0.55f, 0f, new Color(0.7f, 1f, 0.8f), "Green rings, one for every summer."),
        ("Rose Quartz", "RoseQuartz", 22, new Vector3(0.3f, 0.8f, 0.8f), 0.65f, 0f, new Color(1f, 0.8f, 0.88f), "Soft pink, like the first time someone held your hand."),
        ("Fluorite", "Fluorite", 26, new Vector3(0f, 0.6f, 1f), 0.75f, 0f, new Color(0.8f, 1f, 0.9f), "Two colours that never quite mix. Like home, after moving."),
        ("Amethyst", "Amethyst", 30, new Vector3(0f, 0.5f, 1f), 0.75f, 0f, new Color(0.9f, 0.8f, 1f), "Violet, the colour of the sky the night before you left."),
        ("Pyrite", "Pyrite", 35, new Vector3(0f, 0.3f, 0.9f), 0.6f, 0.9f, new Color(1f, 0.92f, 0.6f), "Fool's gold. It looked like everything, once."),
        ("Opal", "Opal", 70, new Vector3(0f, 0.05f, 0.4f), 0.8f, 0f, new Color(0.85f, 0.95f, 1f), "All the colours at once, if you hold it to the light."),
    };

    static MineralData[] BuildMinerals()
    {
        EnsureFolder(MineralDir);
        var list = new List<MineralData>();
        for (int i = 0; i < MineralDefs.Length; i++)
        {
            var d = MineralDefs[i];
            var model = MeshyAsset(d.folder, d.smooth, d.metal);
            if (model == null) continue;
            var data = LoadOrCreate(MineralDir + "/" + (i + 1).ToString("00") + " " + d.title + ".asset", () =>
            {
                var m = ScriptableObject.CreateInstance<MineralData>();
                m.title = d.title;
                m.line = d.line;
                m.value = d.value;
                m.weightByStratum = d.weights;
                m.glint = d.glint;
                return m;
            });
            if (data.model != model) { data.model = model; EditorUtility.SetDirty(data); }
            list.Add(data);
        }
        report.AppendLine("  " + list.Count + " minerals");
        return list.ToArray();
    }

    // A slow, occasional sparkle.
    static ParticleSystem BuildGlintPrefab(Material dust)
    {
        string path = PrefabDir + "/Mineral Glint.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) { Skipped(path); return existing.GetComponent<ParticleSystem>(); }

        var mat = LoadOrCreate(MatDir + "/Glint.mat", () =>
        {
            var m = new Material(dust);
            m.SetFloat("_Blend", 2f);   // additive
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetColor("_BaseColor", new Color(1f, 0.95f, 0.85f, 1f));
            return m;
        });

        var go = new GameObject("Mineral Glint");
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = 0.7f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.035f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 4;
        var emission = ps.emission;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.04f;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        Created(path);
        return prefab.GetComponent<ParticleSystem>();
    }

    static GameObject BuildMineralField(DigVolume volume, DigPath path, MineralData[] kinds, ParticleSystem glint)
    {
        var go = new GameObject("Minerals");
        var m = go.AddComponent<DigMinerals>();
        m.volume = volume;
        m.path = path;
        m.kinds = kinds;
        m.glintPrefab = glint;
        return go;
    }

    // The cat's price board: warm paper on the right, the cat's words underneath.
    static void BuildShopUi(Transform root, DigHud hud, TMP_FontAsset font, TMP_FontAsset italic)
    {
        var node = UiNode("Shop", root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-70f, 0f), new Vector2(760f, 640f));
        node.pivot = new Vector2(1f, 0.5f);
        hud.shopGroup = node.gameObject.AddComponent<CanvasGroup>();
        hud.shopGroup.alpha = 0f;
        hud.shopGroup.blocksRaycasts = false;
        var paper = Paper("Board", node, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(740f, 620f));
        paper.color = new Color(0.93f, 0.86f, 0.72f, 0.97f);

        var top = new Vector2(0.5f, 1f);
        var title = Ink("Title", paper.transform, font, 30f, TextAlignmentOptions.Center, "The Shop", top, new Vector2(0f, -46f), new Vector2(640f, 46f), 0.75f);
        title.characterSpacing = 4f;
        hud.shopBoard = Ink("Prices", paper.transform, italic, 30f, TextAlignmentOptions.TopLeft, "1   Sell", top, new Vector2(0f, -110f), new Vector2(640f, 330f));
        hud.shopBoard.lineSpacing = 18f;
        hud.shopCat = Ink("Cat", paper.transform, italic, 28f, TextAlignmentOptions.Center, "“Mrrp.”", new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(640f, 110f), 0.85f);
    }
}
