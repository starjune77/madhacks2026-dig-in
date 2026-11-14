using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// DigIn > Build Scene
//
// Creates the project's persistent content (materials, textures, meshes, memory data,
// prefabs) and the SampleScene objects, then saves everything. After it has run once,
// the content lives in Assets/ and in the scene and is edited there like any other.
//
// Re-running is safe: every asset and scene object is only created if it does not
// exist yet. To regenerate one piece, delete it (asset or scene object) and run again.
// Scene references on Game are filled in only where they are empty.
public static class DigInSceneBuilder
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string MatDir = "Assets/Materials";
    const string TexDir = "Assets/Textures";
    const string MeshDir = "Assets/Meshes";
    const string PrefabDir = "Assets/Prefabs";
    const string MemoryDir = "Assets/Data/Memories";
    const string FontDir = "Assets/Fonts";

    static readonly Color[] StrataColors =
    {
        new Color(0.55f, 0.38f, 0.24f),   // dirt (Childhood)
        new Color(0.70f, 0.42f, 0.28f),   // clay (School Days)
        new Color(0.45f, 0.45f, 0.48f),   // stone (Growing Up)
        new Color(0.92f, 0.90f, 0.84f),   // today (the very bottom)
    };

    static readonly Color TextColor = new Color(1f, 0.95f, 0.86f, 1f);

    static StringBuilder report;

    // ---- Entry point ---------------------------------------------------------
    [MenuItem("DigIn/Build Scene", false, 0)]
    public static void BuildScene()
    {
        if (!EnsureTmpResources()) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        report = new StringBuilder("DigIn/Build Scene\n");
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        foreach (var dir in new[] { MatDir, TexDir, MeshDir, PrefabDir, MemoryDir, FontDir }) EnsureFolder(dir);

        var m = BuildMaterials();
        var memories = BuildMemories();
        var font = BuildFont();
        var wallMesh = LoadOrCreate(MeshDir + "/ShaftWalls.asset", DigTerrain.BuildWallMesh);

        var playerPrefab = BuildPlayerPrefab(m);
        var findPrefab = BuildFindPrefab(m);
        var shelfPrefab = BuildShelfPrefab(m);
        var benchPrefab = BuildWorkbenchPrefab(m);
        var hudPrefab = BuildHudPrefab(font);
        ApplyFont(hudPrefab, font);

        BuildLighting(m);
        BuildScene(scene, m, wallMesh, memories, playerPrefab, findPrefab, shelfPrefab, benchPrefab, hudPrefab);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log(report.ToString());
    }

    // TMP needs its essential resources (shaders, settings, default font) in the project.
    static bool EnsureTmpResources()
    {
        if (TMP_Settings.instance != null) return true;
        TMP_PackageResourceImporter.ImportResources(true, false, false);
        EditorUtility.DisplayDialog("DigIn",
            "TextMesh Pro essential resources are being imported.\n\n" +
            "When the import finishes, run DigIn > Build Scene again.", "OK");
        return false;
    }

    // ---- Assets --------------------------------------------------------------
    class Mats
    {
        public Material strata, grass, earth, wood, woodDark, paper, find, sky;
        public Material[] clods;
        public Material[] shovelHandle, shovelBlade;
    }

    static Mats BuildMaterials()
    {
        var m = new Mats();

        var strataTex = BuildStrataTexture();
        m.strata = Mat("Strata", Color.white, 0.05f, 0f, strataTex);
        m.clods = new[]
        {
            Mat("Clod Dirt", StrataColors[0] * 0.9f, 0.05f),
            Mat("Clod Clay", StrataColors[1] * 0.9f, 0.05f),
            Mat("Clod Stone", StrataColors[2] * 0.9f, 0.05f),
        };

        // Placeholders: swapped for imported art later.
        m.grass = Mat("Placeholder Grass", new Color(0.42f, 0.52f, 0.28f), 0.05f);
        m.earth = Mat("Placeholder Earth", new Color(0.36f, 0.26f, 0.18f), 0.05f);
        m.wood = Mat("Placeholder Wood", new Color(0.55f, 0.39f, 0.25f), 0.2f);
        m.woodDark = Mat("Placeholder Wood Dark", new Color(0.30f, 0.21f, 0.14f), 0.2f);
        m.paper = Mat("Placeholder Card", new Color(0.95f, 0.91f, 0.82f), 0.1f);
        m.find = Mat("Placeholder Find", new Color(0.98f, 0.80f, 0.35f), 0.6f);

        m.shovelHandle = new[]
        {
            Mat("Shovel Toy Handle", new Color(0.95f, 0.78f, 0.20f), 0.3f),
            Mat("Shovel Garden Handle", new Color(0.58f, 0.40f, 0.22f), 0.3f),
            Mat("Shovel Steel Handle", new Color(0.42f, 0.28f, 0.16f), 0.3f),
        };
        m.shovelBlade = new[]
        {
            Mat("Shovel Toy Blade", new Color(0.86f, 0.22f, 0.20f), 0.5f),
            Mat("Shovel Garden Blade", new Color(0.22f, 0.36f, 0.27f), 0.4f),
            Mat("Shovel Steel Blade", new Color(0.74f, 0.76f, 0.80f), 0.8f, 0.6f),
        };

        m.sky = LoadOrCreate(MatDir + "/Sky Golden Hour.mat", () =>
        {
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunDisk", 2f);
            sky.SetFloat("_SunSize", 0.045f);
            sky.SetFloat("_SunSizeConvergence", 4f);
            sky.SetFloat("_AtmosphereThickness", 1.35f);
            sky.SetColor("_SkyTint", new Color(0.60f, 0.50f, 0.45f));
            sky.SetColor("_GroundColor", new Color(0.42f, 0.35f, 0.30f));
            sky.SetFloat("_Exposure", 1.15f);
            return sky;
        });
        return m;
    }

    static Texture2D BuildStrataTexture()
    {
        string path = TexDir + "/Strata Gradient.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) { Skipped(path); return existing; }

        var tex = DigTerrain.MakeStrataTexture(StrataColors);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 8;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        Created(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static MemoryData[] BuildMemories()
    {
        var defs = new (string title, string line, float depth)[]
        {
            ("Bicycle Bell", "Someone was holding the back of the seat. I didn't notice when they let go.", 2.5f),
            ("Crayon", "I drew a house with four windows. The real house only had two.", 5.0f),
            ("Toy Car", "One wheel was missing, but I still raced it across the kitchen floor.", 7.2f),
            ("Graded Test Paper", "I folded the paper before anyone could see the number at the top.", 10.5f),
            ("Worn Eraser", "We kept borrowing this from each other, even though the teacher had a whole box.", 13.0f),
            ("Folded Note", "It just said, 'Meet me after class.' I kept checking the clock.", 15.2f),
            ("House Key", "I stood outside the door for a moment, holding the key to a place that was finally mine.", 18.5f),
            ("Train Ticket", "I watched the platform disappear, even after there was nothing left to see.", 21.0f),
            ("Coffee Mug", "I reheated the same cup twice. By evening, it was still sitting beside my work.", 23.2f),
        };

        var result = new MemoryData[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            int index = i;
            result[i] = LoadOrCreate(MemoryDir + "/" + (i + 1).ToString("00") + " " + d.title + ".asset", () =>
            {
                var data = ScriptableObject.CreateInstance<MemoryData>();
                data.title = d.title;
                data.line = d.line;
                data.depth = d.depth;
                data.stratum = index / 3;
                return data;
            });
        }
        return result;
    }

    // Uses a font from Assets/Fonts if one has been imported (prefers "*-Regular"),
    // otherwise TMP's default font.
    static TMP_FontAsset BuildFont()
    {
        var fontPaths = Directory.GetFiles(FontDir)
            .Where(p => p.EndsWith(".ttf") || p.EndsWith(".otf"))
            .Select(p => p.Replace('\\', '/'))
            .OrderBy(p => p.Contains("-Regular") ? 0 : 1)
            .ToList();
        if (fontPaths.Count == 0)
        {
            report.AppendLine("  font: none in " + FontDir + ", using the TMP default");
            return TMP_Settings.defaultFontAsset;
        }

        string fontPath = fontPaths[0];
        string sdfPath = Path.GetDirectoryName(fontPath).Replace('\\', '/') + "/" + Path.GetFileNameWithoutExtension(fontPath) + " SDF.asset";
        var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(sdfPath);
        if (fontAsset != null) { Skipped(sdfPath); return fontAsset; }

        // Let TMP's own menu command build it, so it is set up exactly as TMP expects.
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
        EditorApplication.ExecuteMenuItem("Assets/Create/TextMeshPro/Font Asset/SDF");
        AssetDatabase.Refresh();
        fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(sdfPath);
        if (fontAsset == null)
        {
            report.AppendLine("  font: could not create " + sdfPath + ", using the TMP default");
            return TMP_Settings.defaultFontAsset;
        }
        Created(sdfPath);
        return fontAsset;
    }

    // A material preset for the font with a soft drop shadow, so text reads on any background.
    static Material FontShadowMaterial(TMP_FontAsset font)
    {
        return LoadOrCreate(FontDir + "/" + font.name + " Soft Shadow.mat", () =>
        {
            var mat = new Material(font.material);
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor("_UnderlayColor", new Color(0.08f, 0.05f, 0.03f, 0.55f));
            mat.SetFloat("_UnderlayOffsetX", 0.5f);
            mat.SetFloat("_UnderlayOffsetY", -0.5f);
            mat.SetFloat("_UnderlaySoftness", 0.45f);
            return mat;
        });
    }

    // ---- Prefabs -------------------------------------------------------------
    static GameObject BuildPlayerPrefab(Mats m)
    {
        string path = PrefabDir + "/Player.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) { Skipped(path); return existing; }

        var root = new GameObject("Player");
        var cc = root.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.stepOffset = 0.3f;
        cc.slopeLimit = 50f;
        var player = root.AddComponent<DigPlayer>();

        var camGo = new GameObject("Camera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(root.transform, false);
        camGo.transform.localPosition = new Vector3(0f, DigPlayer.EyeHeight, 0f);
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 400f;
        cam.fieldOfView = 68f;
        camGo.AddComponent<AudioListener>();
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

        var lanternGo = new GameObject("Lantern");
        lanternGo.transform.SetParent(camGo.transform, false);
        lanternGo.transform.localPosition = new Vector3(0.35f, -0.2f, 0.25f);
        var lantern = lanternGo.AddComponent<Light>();
        lantern.type = LightType.Point;
        lantern.color = new Color(1f, 0.72f, 0.42f);
        lantern.range = 8f;
        lantern.intensity = 0f;
        lantern.shadows = LightShadows.None;

        var shovelGo = new GameObject("Shovel");
        shovelGo.transform.SetParent(camGo.transform, false);
        var shovel = shovelGo.AddComponent<DigShovel>();
        shovel.models = new[]
        {
            ShovelModel("Toy Shovel (placeholder)", shovelGo.transform, 0.42f, 0.030f, 0.13f, 0.15f, m.shovelHandle[0], m.shovelBlade[0]),
            ShovelModel("Garden Shovel (placeholder)", shovelGo.transform, 0.60f, 0.032f, 0.16f, 0.20f, m.shovelHandle[1], m.shovelBlade[1]),
            ShovelModel("Steel Shovel (placeholder)", shovelGo.transform, 0.70f, 0.034f, 0.20f, 0.26f, m.shovelHandle[2], m.shovelBlade[2]),
        };
        shovel.models[1].SetActive(false);
        shovel.models[2].SetActive(false);
        shovelGo.transform.localPosition = shovel.keyPos[0];
        shovelGo.transform.localRotation = Quaternion.Euler(shovel.keyRot[0]);

        player.cam = camGo.transform;
        player.shovel = shovel;
        return SavePrefab(root, path);
    }

    // Origin = grip; the handle runs along +Z to the blade.
    static GameObject ShovelModel(string name, Transform parent, float length, float thickness,
                                  float bladeWidth, float bladeLength, Material handle, Material blade)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        Part(PrimitiveType.Cylinder, "Handle", root.transform, new Vector3(0f, 0f, length * 0.5f), new Vector3(90f, 0f, 0f),
            new Vector3(thickness, length * 0.5f, thickness), handle, shadows: false);
        Part(PrimitiveType.Cube, "Grip", root.transform, new Vector3(0f, 0f, -0.01f), Vector3.zero,
            new Vector3(0.09f, thickness, thickness), blade, shadows: false);
        Part(PrimitiveType.Cube, "Blade", root.transform, new Vector3(0f, -0.012f, length + bladeLength * 0.5f - 0.02f), new Vector3(8f, 0f, 0f),
            new Vector3(bladeWidth, 0.012f, bladeLength), blade, shadows: false);
        return root;
    }

    static GameObject BuildFindPrefab(Mats m)
    {
        string path = PrefabDir + "/Memory Find.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) { Skipped(path); return existing; }

        var root = new GameObject("Memory Find");
        var col = root.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.35f;
        var find = root.AddComponent<MemoryFind>();
        find.model = Part(PrimitiveType.Sphere, "Model (placeholder)", root.transform, Vector3.zero, Vector3.zero, Vector3.one, m.find).transform;

        var glowGo = new GameObject("Glow");
        glowGo.transform.SetParent(root.transform, false);
        glowGo.transform.localPosition = new Vector3(0f, 0.3f, 0f);
        var glow = glowGo.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.85f, 0.55f);
        glow.range = 1.6f;
        glow.intensity = 1.5f;
        glow.shadows = LightShadows.None;
        return SavePrefab(root, path);
    }

    // Front faces +Z. Rows top to bottom: Childhood, School Days, Growing Up.
    static GameObject BuildShelfPrefab(Mats m)
    {
        string path = PrefabDir + "/Memory Shelf.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) { Skipped(path); return existing; }

        const float w = 1.8f, h = 1.9f, d = 0.4f;
        var root = new GameObject("Memory Shelf");
        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, h * 0.5f, 0f);
        col.size = new Vector3(w, h, d);
        var shelf = root.AddComponent<MemoryShelf>();

        var model = new GameObject("Model (placeholder)").transform;
        model.SetParent(root.transform, false);
        Part(PrimitiveType.Cube, "Side L", model, new Vector3(-w * 0.5f + 0.03f, h * 0.5f, 0f), Vector3.zero, new Vector3(0.06f, h, d), m.wood);
        Part(PrimitiveType.Cube, "Side R", model, new Vector3(w * 0.5f - 0.03f, h * 0.5f, 0f), Vector3.zero, new Vector3(0.06f, h, d), m.wood);
        Part(PrimitiveType.Cube, "Back", model, new Vector3(0f, h * 0.5f, -d * 0.5f + 0.015f), Vector3.zero, new Vector3(w, h, 0.03f), m.woodDark);
        float[] boards = { 0.05f, 0.65f, 1.25f, h - 0.02f };
        for (int i = 0; i < boards.Length; i++)
            Part(PrimitiveType.Cube, "Board " + (i + 1), model, new Vector3(0f, boards[i], 0f), Vector3.zero, new Vector3(w, 0.04f, d), m.wood);

        var slotsRoot = new GameObject("Slots").transform;
        slotsRoot.SetParent(root.transform, false);
        shelf.slots = new MemoryShelf.Slot[9];
        for (int i = 0; i < 9; i++)
        {
            int row = i / 3, column = i % 3;
            var slot = new GameObject("Slot " + (i + 1)).transform;
            slot.SetParent(slotsRoot, false);
            slot.localPosition = new Vector3((column - 1) * 0.55f, boards[2 - row] + 0.02f, 0.02f);

            var empty = Part(PrimitiveType.Cube, "Empty", slot, new Vector3(0f, 0.004f, 0f), Vector3.zero, new Vector3(0.34f, 0.008f, 0.26f), m.woodDark);
            var card = Part(PrimitiveType.Cube, "Card (placeholder)", slot, new Vector3(0f, 0.19f, -0.06f), new Vector3(-10f, 0f, 0f), new Vector3(0.28f, 0.36f, 0.01f), m.paper);
            card.SetActive(false);
            shelf.slots[i] = new MemoryShelf.Slot { empty = empty, filled = card };
        }
        return SavePrefab(root, path);
    }

    // Front faces +Z; the player stands at "Interact Point".
    static GameObject BuildWorkbenchPrefab(Mats m)
    {
        string path = PrefabDir + "/Workbench.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) { Skipped(path); return existing; }

        var root = new GameObject("Workbench");
        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.45f, 0f);
        col.size = new Vector3(1.6f, 0.9f, 0.7f);
        var bench = root.AddComponent<Workbench>();

        var model = new GameObject("Model (placeholder)").transform;
        model.SetParent(root.transform, false);
        Part(PrimitiveType.Cube, "Top", model, new Vector3(0f, 0.86f, 0f), Vector3.zero, new Vector3(1.6f, 0.08f, 0.7f), m.wood);
        Part(PrimitiveType.Cube, "Shelf", model, new Vector3(0f, 0.25f, 0f), Vector3.zero, new Vector3(1.5f, 0.04f, 0.6f), m.woodDark);
        for (int i = 0; i < 4; i++)
        {
            float x = (i % 2 == 0 ? -1f : 1f) * 0.72f, z = (i < 2 ? -1f : 1f) * 0.29f;
            Part(PrimitiveType.Cube, "Leg " + (i + 1), model, new Vector3(x, 0.41f, z), Vector3.zero, new Vector3(0.08f, 0.82f, 0.08f), m.woodDark);
        }

        var point = new GameObject("Interact Point").transform;
        point.SetParent(root.transform, false);
        point.localPosition = new Vector3(0f, 0f, 0.9f);
        bench.interactPoint = point;
        return SavePrefab(root, path);
    }

    static GameObject BuildHudPrefab(TMP_FontAsset font)
    {
        string path = PrefabDir + "/HUD.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) { Skipped(path); return existing; }

        var root = new GameObject("HUD", typeof(RectTransform));
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var hud = root.AddComponent<DigHud>();
        var shadow = FontShadowMaterial(font);

        // Crosshair: a small soft dot.
        var cross = UiNode("Crosshair", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7f, 7f));
        var img = cross.gameObject.AddComponent<Image>();
        img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        img.color = new Color(1f, 0.97f, 0.9f, 0.8f);
        img.raycastTarget = false;

        // Status, bottom left.
        var status = UiNode("Status", root.transform, Vector2.zero, Vector2.zero, new Vector2(56f, 44f), new Vector2(520f, 150f));
        hud.depthText = Text("Depth", status, font, shadow, 46f, TextAlignmentOptions.BottomLeft, "0.0 m",
            new Vector2(0f, 76f), new Vector2(520f, 64f));
        hud.shovelText = Text("Shovel", status, font, shadow, 24f, TextAlignmentOptions.BottomLeft, "Toy Shovel",
            new Vector2(0f, 38f), new Vector2(520f, 34f));
        hud.scrapText = Text("Scrap", status, font, shadow, 24f, TextAlignmentOptions.BottomLeft, "0 / 8 scrap",
            new Vector2(0f, 4f), new Vector2(520f, 34f));
        hud.shovelText.alpha = 0.85f;

        // Contextual prompt, just under the crosshair.
        var prompt = UiNode("Prompt", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -130f), new Vector2(1200f, 60f));
        hud.promptGroup = prompt.gameObject.AddComponent<CanvasGroup>();
        hud.promptText = Text("Text", prompt, font, shadow, 28f, TextAlignmentOptions.Center, "Press <b>E</b> to make a Garden Shovel",
            Vector2.zero, new Vector2(1200f, 60f), stretch: true);

        // Messages and memory lines, lower centre.
        var message = UiNode("Message", root.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1300f, 220f));
        hud.messageGroup = message.gameObject.AddComponent<CanvasGroup>();
        hud.messageText = Text("Text", message, font, shadow, 34f, TextAlignmentOptions.Bottom, "Something is buried here.",
            Vector2.zero, new Vector2(1300f, 220f), stretch: true);

        // Controls, shown once at the start.
        var controls = UiNode("Controls", root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(1600f, 50f));
        hud.controlsGroup = controls.gameObject.AddComponent<CanvasGroup>();
        var c = Text("Text", controls, font, shadow, 22f, TextAlignmentOptions.Center,
            "<b>WASD</b> <alpha=#AA>walk</alpha>      <b>Mouse</b> <alpha=#AA>look</alpha>      " +
            "<b>Hold Left Click</b> <alpha=#AA>dig</alpha>      <b>E</b> <alpha=#AA>use</alpha>      " +
            "<b>R</b> <alpha=#AA>back to the surface</alpha>",
            Vector2.zero, new Vector2(1600f, 50f), stretch: true);
        c.characterSpacing = 2f;

        return SavePrefab(root, path);
    }

    // If a custom font has been imported since the HUD was made, switch the HUD to it.
    static void ApplyFont(GameObject hudPrefab, TMP_FontAsset font)
    {
        if (font == null || font == TMP_Settings.defaultFontAsset) return;
        var root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(hudPrefab));
        var shadow = FontShadowMaterial(font);
        bool changed = false;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t.font == font) continue;
            t.font = font;
            t.fontSharedMaterial = shadow;
            changed = true;
        }
        if (changed)
        {
            PrefabUtility.SaveAsPrefabAsset(root, AssetDatabase.GetAssetPath(hudPrefab));
            report.AppendLine("  updated HUD font to " + font.name);
        }
        PrefabUtility.UnloadPrefabContents(root);
    }

    // ---- Lighting ------------------------------------------------------------
    // Golden hour. Only applied the first time (when the scene does not use our sky yet),
    // so later tweaks in the Lighting window are kept.
    static void BuildLighting(Mats m)
    {
        if (RenderSettings.skybox == m.sky) { report.AppendLine("  skipped lighting (already set)"); return; }

        var sun = Object.FindObjectsByType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
        if (sun == null)
        {
            sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
        }
        sun.name = "Sun";
        sun.transform.rotation = Quaternion.Euler(14f, 120f, 0f);   // low, from the west-northwest
        sun.color = new Color(1f, 0.76f, 0.52f);
        sun.intensity = 1.8f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;

        RenderSettings.sun = sun;
        RenderSettings.skybox = m.sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.55f, 0.52f);
        RenderSettings.ambientEquatorColor = new Color(0.58f, 0.45f, 0.36f);
        RenderSettings.ambientGroundColor = new Color(0.25f, 0.20f, 0.16f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.85f, 0.68f, 0.52f);
        RenderSettings.fogDensity = 0.018f;
        DynamicGI.UpdateEnvironment();
        report.AppendLine("  set golden-hour lighting, sky and fog");
    }

    // ---- Scene ---------------------------------------------------------------
    static void BuildScene(Scene scene, Mats m, Mesh wallMesh, MemoryData[] memories,
                           GameObject playerPrefab, GameObject findPrefab, GameObject shelfPrefab,
                           GameObject benchPrefab, GameObject hudPrefab)
    {
        float s = DigTerrain.Size;

        // Dig site: the shaft floor (procedural), its walls, and the invisible earth around it.
        var digSite = Root(scene, "Dig Site", out bool newSite);
        if (newSite)
        {
            var floor = new GameObject("Shaft Floor", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            floor.transform.SetParent(digSite, false);
            floor.GetComponent<MeshRenderer>().sharedMaterial = m.strata;
            floor.AddComponent<DigTerrain>();

            var walls = new GameObject("Shaft Walls", typeof(MeshFilter), typeof(MeshRenderer));
            walls.transform.SetParent(digSite, false);
            walls.GetComponent<MeshFilter>().sharedMesh = wallMesh;
            var wr = walls.GetComponent<MeshRenderer>();
            wr.sharedMaterial = m.strata;
            wr.shadowCastingMode = ShadowCastingMode.Off;

            // Colliders and shadow casters only: the visible walls are "Shaft Walls".
            var earth = new GameObject("Shaft Earth").transform;
            earth.SetParent(digSite, false);
            const float w = 40f, h = 60f;
            EarthBox("West", earth, new Vector3(-w / 2f, -h / 2f, s / 2f), new Vector3(w, h, s + 2f * w), m.earth);
            EarthBox("East", earth, new Vector3(s + w / 2f, -h / 2f, s / 2f), new Vector3(w, h, s + 2f * w), m.earth);
            EarthBox("South", earth, new Vector3(s / 2f, -h / 2f, -w / 2f), new Vector3(s, h, w), m.earth);
            EarthBox("North", earth, new Vector3(s / 2f, -h / 2f, s + w / 2f), new Vector3(s, h, w), m.earth);

            var bottom = new GameObject("Safety Floor").AddComponent<BoxCollider>();
            bottom.transform.SetParent(digSite, false);
            bottom.transform.position = new Vector3(s / 2f, -DigGame.TotalDepth - 0.6f, s / 2f);
            bottom.size = new Vector3(s, 1f, s);

            var debris = new GameObject("Debris").AddComponent<DigDebris>();
            debris.transform.SetParent(digSite, false);
            debris.terrain = floor.GetComponent<DigTerrain>();
            debris.clodMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            debris.materials = m.clods;
        }

        // Placeholder ground. Replaced by the imported environment later.
        var ground = Root(scene, "Ground (placeholder)", out bool newGround);
        if (newGround)
        {
            const float w = 40f, g = 0.1f;
            Part(PrimitiveType.Cube, "Grass West", ground, new Vector3(-w / 2f, g / 2f, s / 2f), Vector3.zero, new Vector3(w, g, s + 2f * w), m.grass, collider: true);
            Part(PrimitiveType.Cube, "Grass East", ground, new Vector3(s + w / 2f, g / 2f, s / 2f), Vector3.zero, new Vector3(w, g, s + 2f * w), m.grass, collider: true);
            Part(PrimitiveType.Cube, "Grass South", ground, new Vector3(s / 2f, g / 2f, -w / 2f), Vector3.zero, new Vector3(s, g, w), m.grass, collider: true);
            Part(PrimitiveType.Cube, "Grass North", ground, new Vector3(s / 2f, g / 2f, s + w / 2f), Vector3.zero, new Vector3(s, g, w), m.grass, collider: true);
        }

        // Surface hub: spawn, memory shelf (left), workbench (right), the shaft ahead.
        var hub = Root(scene, "Surface Hub", out bool newHub);
        if (newHub)
        {
            var spawn = new GameObject("Spawn Point").transform;
            spawn.SetParent(hub, false);
            spawn.position = new Vector3(2.5f, 1.1f, -3f);

            Instance(shelfPrefab, hub, new Vector3(-2.2f, 0.1f, -3.2f), 90f);
            Instance(benchPrefab, hub, new Vector3(7.2f, 0.1f, -3.2f), -90f);
        }

        var player = Root(scene, "Player", out bool newPlayer, playerPrefab);
        if (newPlayer) player.position = new Vector3(2.5f, 1.1f, -3f);
        Root(scene, "HUD", out _, hudPrefab);

        // The template's camera is replaced by the one in the Player prefab.
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go.name == "Main Camera" && go.GetComponent<Camera>() != null)
            {
                Object.DestroyImmediate(go);
                report.AppendLine("  removed the old Main Camera");
            }
        }

        WireGame(scene, memories, findPrefab);
    }

    static void WireGame(Scene scene, MemoryData[] memories, GameObject findPrefab)
    {
        var gameRoot = Root(scene, "Game", out _);
        // Not "??": Unity's fake-null objects defeat it.
        var game = gameRoot.GetComponent<DigGame>();
        if (game == null) game = gameRoot.gameObject.AddComponent<DigGame>();
        var atmosphere = gameRoot.GetComponent<DigAtmosphere>();
        if (atmosphere == null) atmosphere = gameRoot.gameObject.AddComponent<DigAtmosphere>();

        var player = Find<DigPlayer>(scene);
        if (game.terrain == null) game.terrain = Find<DigTerrain>(scene);
        if (game.debris == null) game.debris = Find<DigDebris>(scene);
        if (game.player == null) game.player = player;
        if (game.shovel == null && player != null) game.shovel = player.shovel;
        if (game.hud == null) game.hud = Find<DigHud>(scene);
        if (game.workbench == null) game.workbench = Find<Workbench>(scene);
        if (game.shelf == null) game.shelf = Find<MemoryShelf>(scene);
        if (game.spawnPoint == null)
        {
            var hub = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Surface Hub");
            if (hub != null) game.spawnPoint = hub.transform.Find("Spawn Point");
        }
        if (game.memories == null || game.memories.Length == 0) game.memories = memories;
        if (game.findPrefab == null) game.findPrefab = findPrefab.GetComponent<MemoryFind>();
        EditorUtility.SetDirty(game);

        if (atmosphere.viewer == null && player != null) atmosphere.viewer = player.cam;
        if (atmosphere.sun == null) atmosphere.sun = RenderSettings.sun;
        if (atmosphere.lantern == null && player != null)
        {
            var lantern = player.cam.Find("Lantern");
            if (lantern != null) atmosphere.lantern = lantern.GetComponent<Light>();
        }
        EditorUtility.SetDirty(atmosphere);
        report.AppendLine("  wired Game references");
    }

    // ---- Helpers -------------------------------------------------------------
    static void Created(string what) { report.AppendLine("  created " + what); }
    static void Skipped(string what) { report.AppendLine("  kept    " + what); }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static T LoadOrCreate<T>(string path, System.Func<T> create) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) { Skipped(path); return asset; }
        asset = create();
        AssetDatabase.CreateAsset(asset, path);
        Created(path);
        return asset;
    }

    static Material Mat(string name, Color color, float smoothness, float metallic = 0f, Texture texture = null)
    {
        return LoadOrCreate(MatDir + "/" + name + ".mat", () =>
        {
            // Editor-only lookup: the saved material keeps a hard reference to the shader,
            // so builds include it.
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            if (texture != null) mat.SetTexture("_BaseMap", texture);
            return mat;
        });
    }

    static GameObject SavePrefab(GameObject root, string path)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        Created(path);
        return prefab;
    }

    static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localEuler,
                           Vector3 localScale, Material mat, bool collider = false, bool shadows = true)
    {
        var go = GameObject.CreatePrimitive(type);
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localEulerAngles = localEuler;
        go.transform.localScale = localScale;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    static void EarthBox(string name, Transform parent, Vector3 center, Vector3 size, Material mat)
    {
        var box = Part(PrimitiveType.Cube, name, parent, center, Vector3.zero, size, mat, collider: true);
        box.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
    }

    static RectTransform UiNode(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = anchorMin;   // anchors are always a single point here
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    static TMP_Text Text(string name, Transform parent, TMP_FontAsset font, Material material, float size,
                         TextAlignmentOptions align, string text, Vector2 pos, Vector2 boxSize, bool stretch = false)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        if (stretch)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
        else
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = pos;
            rt.sizeDelta = boxSize;
        }

        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font;
        t.fontSharedMaterial = material;
        t.fontSize = size;
        t.color = TextColor;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.raycastTarget = false;
        t.text = text;
        return t;
    }

    // Finds a root object by name, or creates it (empty, or from a prefab).
    static Transform Root(Scene scene, string name, out bool created, GameObject prefab = null)
    {
        var existing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
        if (existing != null) { created = false; Skipped("scene/" + name); return existing.transform; }

        GameObject go;
        if (prefab != null) go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        else
        {
            go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
        }
        go.name = name;
        created = true;
        Created("scene/" + name);
        return go.transform;
    }

    static void Instance(GameObject prefab, Transform parent, Vector3 position, float yaw)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    static T Find<T>(Scene scene) where T : Component
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var c = root.GetComponentInChildren<T>(true);
            if (c != null) return c;
        }
        return null;
    }
}
