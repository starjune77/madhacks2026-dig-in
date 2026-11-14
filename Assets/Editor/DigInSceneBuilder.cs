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
// Creates the project's persistent content (materials, textures, terrain, memory data,
// prefabs) and the SampleScene objects, then saves everything. After it has run, the
// content lives in Assets/ and in the scene and is edited there like any other.
//
// Re-running is safe: assets and scene objects are only created when missing. A prefab
// is rebuilt when it lacks its current version marker (an empty child named "vN ..."),
// and its scene instances are recreated. To regenerate anything else, delete it and run
// again. Scene references on Game are filled in only where they are empty.
//
// The environment (terrain, house, yard, excavation) lives in DigInSceneBuilder.Environment.cs.
public static partial class DigInSceneBuilder
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string MatDir = "Assets/Materials";
    const string TexDir = "Assets/Textures";
    const string MeshDir = "Assets/Meshes";
    const string PrefabDir = "Assets/Prefabs";
    const string MemoryDir = "Assets/Data/Memories";
    const string LetterDir = "Assets/Data/Letters";
    const string FontDir = "Assets/Fonts";
    const string TerrainDir = "Assets/Terrain";
    const string KenneyDir = "Assets/ThirdParty/Kenney";
    const string HdriPath = "Assets/ThirdParty/PolyHaven/evening_field_2k.hdr";

    // Prefab version markers. Bump one to rebuild that prefab on the next run.
    const string PlayerVersion = "v2 kenney shovels";
    const string FindVersion = "v2 kenney box";
    const string ShelfVersion = "v2 bookcase";
    const string BenchVersion = "v2 kenney bench";
    const string HudVersion = "v3 letters";

    static readonly Color[] StrataColors =
    {
        new Color(0.55f, 0.38f, 0.24f),   // dirt (Childhood)
        new Color(0.70f, 0.42f, 0.28f),   // clay (School Days)
        new Color(0.45f, 0.45f, 0.48f),   // stone (Growing Up)
        new Color(0.92f, 0.90f, 0.84f),   // today (the very bottom)
    };

    static readonly Color TextColor = new Color(1f, 0.95f, 0.86f, 1f);
    static readonly Color InkColor = new Color(0.22f, 0.16f, 0.11f, 1f);

    static StringBuilder report;

    // ---- Entry point ---------------------------------------------------------
    [MenuItem("DigIn/Build Scene", false, 0)]
    public static void BuildScene()
    {
        if (!EnsureTmpResources()) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        report = new StringBuilder("DigIn/Build Scene\n");
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        foreach (var dir in new[] { MatDir, TexDir, MeshDir, PrefabDir, MemoryDir, LetterDir, FontDir, TerrainDir }) EnsureFolder(dir);

        var m = BuildMaterials();
        var memories = BuildMemories();
        var letters = BuildLetters();
        var font = BuildFont("Lora-Regular");
        var italic = BuildFont("Lora-Italic");
        var wallMesh = LoadOrCreate(MeshDir + "/ShaftWalls.asset", DigTerrain.BuildWallMesh);

        var rebuilt = new HashSet<string>();
        var playerPrefab = BuildPlayerPrefab(m, rebuilt);
        var findPrefab = BuildFindPrefab(rebuilt);
        var shelfPrefab = BuildShelfPrefab(m, font, rebuilt);
        var benchPrefab = BuildWorkbenchPrefab(rebuilt);
        var hudPrefab = BuildHudPrefab(font, italic, rebuilt);
        ApplyFont(hudPrefab, font);

        BuildLighting(m);
        Migrate(scene, rebuilt);

        BuildDigSite(scene, m, wallMesh);
        BuildEnvironment(scene);
        BuildHub(scene, shelfPrefab, benchPrefab);

        var player = Root(scene, "Player", out bool newPlayer, playerPrefab);
        if (newPlayer) player.position = new Vector3(3f, 1f, -8f);
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

        WireGame(scene, memories, letters, findPrefab);
        DeleteUnusedAssets();

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

    // Removes scene objects that belong to an older version of the content.
    static void Migrate(Scene scene, HashSet<string> rebuilt)
    {
        DestroyRoot(scene, "Ground (placeholder)");
        if (rebuilt.Contains("Player")) DestroyRoot(scene, "Player");
        if (rebuilt.Contains("HUD")) DestroyRoot(scene, "HUD");
        if (rebuilt.Contains("Shelf") || rebuilt.Contains("Bench")) DestroyRoot(scene, "Surface Hub");

        var hub = FindRoot(scene, "Surface Hub");
        if (hub != null && hub.Find("Lean-to") == null) DestroyRoot(scene, "Surface Hub");

        // v1 had 40 m earth boxes around the shaft; they would cover the excavation.
        var earth = FindRoot(scene, "Dig Site")?.Find("Shaft Earth");
        var west = earth != null ? earth.Find("West") : null;
        if (west != null && west.localScale.x > 10f)
        {
            Object.DestroyImmediate(earth.gameObject);
            report.AppendLine("  removed v1 Shaft Earth");
        }
    }

    static void DeleteUnusedAssets()
    {
        string[] old =
        {
            "Placeholder Grass", "Placeholder Earth", "Placeholder Wood", "Placeholder Wood Dark", "Placeholder Card",
            "Placeholder Find", "Shovel Toy Handle", "Shovel Garden Handle", "Shovel Steel Handle",
            "Shovel Toy Blade", "Shovel Garden Blade", "Shovel Steel Blade",
        };
        foreach (var name in old)
        {
            string path = MatDir + "/" + name + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null && AssetDatabase.DeleteAsset(path))
                report.AppendLine("  deleted " + path);
        }
    }

    // ---- Assets --------------------------------------------------------------
    class Mats
    {
        public Material strata, earth, paper, frame, sky;
        public Material[] clods;
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
        m.earth = Mat("Shaft Earth", new Color(0.36f, 0.26f, 0.18f), 0.05f);
        m.paper = Mat("Card Paper", new Color(0.96f, 0.92f, 0.83f), 0.1f);
        m.frame = Mat("Card Frame", new Color(0.33f, 0.22f, 0.14f), 0.35f);

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

    // Letters 1-3 (one per stratum) and the ending, one line per sentence.
    static LetterData[] BuildLetters()
    {
        var defs = new (string file, string title, string[] lines)[]
        {
            ("1 Childhood", "Childhood", new[]
            {
                "I used to think afternoons lasted forever.",
                "The floor was a road, a cardboard box was a spaceship, and someone always called me in when it got dark.",
                "I don't remember what we had for dinner.",
                "I remember wanting five more minutes outside.",
            }),
            ("2 School Days", "School Days", new[]
            {
                "I spent so much time waiting for the next thing.",
                "The next bell, the next weekend, the next exam to be over.",
                "Somewhere between all that waiting, we laughed at things that weren't even funny.",
                "I wonder if the others remember them too.",
            }),
            ("3 Growing Up", "Growing Up", new[]
            {
                "My days became full of things I had to finish.",
                "There were messages to answer, places to be, and plans that kept changing.",
                "Sometimes I missed a call and forgot to call back.",
                "I thought I'd have more time later.",
            }),
            ("4 Ending - The Blank Page", "", new[]
            {
                "There was nothing written on the last page.",
                "Not because nothing happened today.",
                "But because today isn't over yet.",
                "Someday, this ordinary day might be something worth digging up.",
            }),
        };
        return defs.Select(d => LoadOrCreate(LetterDir + "/" + d.file + ".asset", () =>
        {
            var letter = ScriptableObject.CreateInstance<LetterData>();
            letter.title = d.title;
            letter.lines = d.lines;
            return letter;
        })).ToArray();
    }

    // A TMP font asset made from Assets/Fonts/<fileName>.ttf, or TMP's default font if
    // that file has not been imported.
    static TMP_FontAsset BuildFont(string fileName)
    {
        string fontPath = FontDir + "/" + fileName + ".ttf";
        string sdfPath = FontDir + "/" + fileName + " SDF.asset";
        var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(sdfPath);
        if (fontAsset != null) { Skipped(sdfPath); return fontAsset; }

        var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
        if (font == null)
        {
            report.AppendLine("  font: " + fontPath + " not found, using the TMP default");
            return TMP_Settings.defaultFontAsset;
        }

        // Let TMP's own menu command build it, so it is set up exactly as TMP expects.
        Selection.activeObject = font;
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
    // Returns the existing prefab if it is current; otherwise null (and logs why).
    static GameObject CurrentPrefab(string path, string version, string key, HashSet<string> rebuilt)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null && existing.transform.Find(version) != null) { Skipped(path); return existing; }
        if (existing != null) rebuilt.Add(key);
        return null;
    }

    static void AddMarker(GameObject root, string version)
    {
        new GameObject(version).transform.SetParent(root.transform, false);
    }

    static GameObject BuildPlayerPrefab(Mats m, HashSet<string> rebuilt)
    {
        string path = PrefabDir + "/Player.prefab";
        var current = CurrentPrefab(path, PlayerVersion, "Player", rebuilt);
        if (current != null) return current;

        var root = new GameObject("Player");
        AddMarker(root, PlayerVersion);
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

        // One Kenney shovel per level, told apart by size and tint.
        var baseMat = KitMaterial("SurvivalKit/tool-shovel");
        var toyMat = TintedMaterial("Shovel Toy", baseMat, new Color(1f, 0.62f, 0.38f), 0.45f, 0f);
        var steelMat = TintedMaterial("Shovel Steel", KitMaterial("SurvivalKit/tool-shovel-upgraded"), new Color(0.86f, 0.9f, 1f), 0.6f, 0.35f);
        shovel.models = new[]
        {
            ShovelModel("Toy Shovel", shovelGo.transform, "SurvivalKit/tool-shovel", 0.46f, toyMat),
            ShovelModel("Garden Shovel", shovelGo.transform, "SurvivalKit/tool-shovel", 0.64f, null),
            ShovelModel("Steel Shovel", shovelGo.transform, "SurvivalKit/tool-shovel-upgraded", 0.74f, steelMat),
        };
        shovel.models[1].SetActive(false);
        shovel.models[2].SetActive(false);
        shovelGo.transform.localPosition = shovel.keyPos[0];
        shovelGo.transform.localRotation = Quaternion.Euler(shovel.keyRot[0]);

        player.cam = camGo.transform;
        player.shovel = shovel;
        return SavePrefab(root, path);
    }

    // DigShovel expects: origin = grip, handle along +Z to the blade. The Kenney shovel
    // stands along +Y with the grip at the bottom, so it is tipped forward 90 degrees.
    static GameObject ShovelModel(string name, Transform parent, string kitModel, float length, Material tint)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        var model = InstantiateKit(kitModel, holder.transform);
        if (model == null) return holder;
        model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var b = LocalBounds(holder.transform);
        model.transform.localScale *= length / Mathf.Max(0.001f, b.size.z);
        b = LocalBounds(holder.transform);
        model.transform.localPosition -= new Vector3(b.center.x, b.center.y, b.min.z);

        foreach (var r in holder.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            if (tint != null) r.sharedMaterials = r.sharedMaterials.Select(_ => tint).ToArray();
        }
        return holder;
    }

    static GameObject BuildFindPrefab(HashSet<string> rebuilt)
    {
        string path = PrefabDir + "/Memory Find.prefab";
        var current = CurrentPrefab(path, FindVersion, "Find", rebuilt);
        if (current != null) return current;

        var root = new GameObject("Memory Find");
        AddMarker(root, FindVersion);
        var col = root.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.35f;
        col.center = new Vector3(0f, 0.15f, 0f);
        var find = root.AddComponent<MemoryFind>();
        find.size = 1f;
        find.hover = 0.02f;

        // A small closed cardboard box: something someone kept.
        var holder = new GameObject("Model").transform;
        holder.SetParent(root.transform, false);
        var box = InstantiateKit("FurnitureKit/cardboardBoxClosed", holder);
        if (box != null) FitInside(box, holder, height: 0.26f);
        find.model = holder;

        var glowGo = new GameObject("Glow");
        glowGo.transform.SetParent(root.transform, false);
        glowGo.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        var glow = glowGo.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.85f, 0.55f);
        glow.range = 1.8f;
        glow.intensity = 1.6f;
        glow.shadows = LightShadows.None;
        return SavePrefab(root, path);
    }

    // Kenney's open bookcase, widened, with nine card slots on its three shelves.
    // Rows top to bottom: Childhood, School Days, Growing Up. Front faces +Z.
    static GameObject BuildShelfPrefab(Mats m, TMP_FontAsset font, HashSet<string> rebuilt)
    {
        string path = PrefabDir + "/Memory Shelf.prefab";
        var current = CurrentPrefab(path, ShelfVersion, "Shelf", rebuilt);
        if (current != null) return current;

        var root = new GameObject("Memory Shelf");
        AddMarker(root, ShelfVersion);
        var shelf = root.AddComponent<MemoryShelf>();

        var holder = new GameObject("Model").transform;
        holder.SetParent(root.transform, false);
        var bookcase = InstantiateKit("FurnitureKit/bookcaseOpen", holder);
        Bounds b;
        if (bookcase != null)
        {
            FitInside(bookcase, holder, height: 2.1f);
            bookcase.transform.localScale = Vector3.Scale(bookcase.transform.localScale, new Vector3(1.5f, 1f, 1f));
            b = LocalBounds(holder);
            bookcase.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
        }
        b = LocalBounds(holder);
        if (b.size == Vector3.zero) b = new Bounds(new Vector3(0f, 1.05f, 0f), new Vector3(1.4f, 2.1f, 0.6f));

        var col = root.AddComponent<BoxCollider>();
        col.center = b.center;
        col.size = b.size;

        // Shelf board tops in the Kenney model, as a fraction of its height.
        float[] rowTop = { 6.1f / 8.8f, 3.7f / 8.8f, 1.3f / 8.8f };
        var slotsRoot = new GameObject("Slots").transform;
        slotsRoot.SetParent(root.transform, false);
        shelf.slots = new MemoryShelf.Slot[9];
        for (int i = 0; i < 9; i++)
        {
            int row = i / 3, column = i % 3;
            var slot = new GameObject("Slot " + (i + 1)).transform;
            slot.SetParent(slotsRoot, false);
            slot.localPosition = new Vector3(b.center.x + (column - 1) * b.size.x * 0.29f, b.min.y + b.size.y * rowTop[row] + 0.005f, b.center.z);
            shelf.slots[i] = BuildShelfCard(slot, m, font);
        }
        return SavePrefab(root, path);
    }

    // A small framed card standing on the shelf, leaning back. Built facing -Z (the way
    // quads and TMP text face) and turned around to face the room.
    static MemoryShelf.Slot BuildShelfCard(Transform slot, Mats m, TMP_FontAsset font)
    {
        const float w = 0.26f, h = 0.34f;
        var card = new GameObject("Card").transform;
        card.SetParent(slot, false);
        card.localRotation = Quaternion.Euler(8f, 180f, 0f);

        Part(PrimitiveType.Cube, "Frame", card, new Vector3(0f, h * 0.5f, 0.012f), Vector3.zero, new Vector3(w, h, 0.02f), m.frame);
        Part(PrimitiveType.Quad, "Paper", card, new Vector3(0f, h * 0.5f, -0.001f), Vector3.zero, new Vector3(w - 0.03f, h - 0.03f, 1f), m.paper);
        var art = Part(PrimitiveType.Quad, "Illustration", card, new Vector3(0f, h * 0.6f, -0.002f), Vector3.zero, new Vector3(w - 0.07f, h * 0.48f, 1f), m.paper);

        var titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(card, false);
        var rt = (RectTransform)titleGo.transform;
        rt.localPosition = new Vector3(0f, h * 0.2f, -0.003f);
        rt.sizeDelta = new Vector2(w - 0.05f, 0.06f);
        var title = titleGo.AddComponent<TextMeshPro>();
        title.font = font;
        title.enableAutoSizing = true;
        title.fontSizeMin = 0.05f;
        title.fontSizeMax = 0.3f;
        title.alignment = TextAlignmentOptions.Center;
        title.color = InkColor;
        title.text = "";

        card.gameObject.SetActive(false);
        return new MemoryShelf.Slot { filled = card.gameObject, title = title, illustration = art.GetComponent<Renderer>() };
    }

    static GameObject BuildWorkbenchPrefab(HashSet<string> rebuilt)
    {
        string path = PrefabDir + "/Workbench.prefab";
        var current = CurrentPrefab(path, BenchVersion, "Bench", rebuilt);
        if (current != null) return current;

        var root = new GameObject("Workbench");
        AddMarker(root, BenchVersion);
        var bench = root.AddComponent<Workbench>();

        var holder = new GameObject("Model").transform;
        holder.SetParent(root.transform, false);
        var model = InstantiateKit("SurvivalKit/workbench", holder);
        if (model != null) FitInside(model, holder, height: 0.95f);

        var b = LocalBounds(holder);
        var col = root.AddComponent<BoxCollider>();
        col.center = b.size == Vector3.zero ? new Vector3(0f, 0.45f, 0f) : b.center;
        col.size = b.size == Vector3.zero ? new Vector3(1.2f, 0.9f, 1.2f) : b.size;

        var point = new GameObject("Interact Point").transform;
        point.SetParent(root.transform, false);
        point.localPosition = new Vector3(0f, 0f, Mathf.Max(0.9f, b.extents.z + 0.5f));
        bench.interactPoint = point;
        return SavePrefab(root, path);
    }

    static GameObject BuildHudPrefab(TMP_FontAsset font, TMP_FontAsset italic, HashSet<string> rebuilt)
    {
        string path = PrefabDir + "/HUD.prefab";
        var current = CurrentPrefab(path, HudVersion, "HUD", rebuilt);
        if (current != null) return current;

        var root = new GameObject("HUD", typeof(RectTransform));
        AddMarker(root, HudVersion);
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

        // Messages, lower centre.
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

        BuildCardUi(root.transform, hud, font, italic);
        BuildLetterUi(root.transform, hud, font, italic);
        return SavePrefab(root, path);
    }

    static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.83f, 1f);

    // Full-screen overlay node with a CanvasGroup (hidden at start) and a dim backdrop.
    static CanvasGroup Overlay(string name, Transform parent, Color backdrop)
    {
        var node = UiNode(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var bg = node.gameObject.AddComponent<Image>();
        bg.color = backdrop;
        bg.raycastTarget = false;
        var group = node.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        return group;
    }

    static Image Paper(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var node = UiNode(name, parent, anchor, anchor, pos, size);
        var img = node.gameObject.AddComponent<Image>();
        img.color = PaperColor;
        img.raycastTarget = false;
        var shadow = node.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
        shadow.effectDistance = new Vector2(8f, -8f);
        return img;
    }

    static TMP_Text Ink(string name, Transform parent, TMP_FontAsset font, float size, TextAlignmentOptions align,
                        string text, Vector2 anchor, Vector2 pos, Vector2 box, float alpha = 1f)
    {
        var node = UiNode(name, parent, anchor, anchor, pos, box);
        var t = Text("Text", node, font, font.material, size, align, text, Vector2.zero, box, stretch: true);
        t.color = new Color(InkColor.r, InkColor.g, InkColor.b, alpha);
        return t;
    }

    // A letter: a sheet of paper over a dark backdrop; its lines fade in one by one.
    static void BuildLetterUi(Transform root, DigHud hud, TMP_FontAsset font, TMP_FontAsset italic)
    {
        var p = hud.letter;
        p.group = Overlay("Letter", root, new Color(0.04f, 0.03f, 0.02f, 0.82f));
        var paper = Paper("Paper", p.group.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(920f, 860f));

        var top = new Vector2(0.5f, 1f);
        p.title = Ink("Title", paper.transform, font, 26f, TextAlignmentOptions.Center, "<smallcaps>Childhood</smallcaps>",
            top, new Vector2(0f, -56f), new Vector2(760f, 40f), 0.6f);
        p.title.fontStyle = FontStyles.SmallCaps;
        p.title.characterSpacing = 6f;

        var body = UiNode("Lines", paper.transform, top, top, new Vector2(0f, -130f), new Vector2(760f, 620f));
        var layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 26f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        p.lines = new TMP_Text[6];
        for (int i = 0; i < p.lines.Length; i++)
        {
            var t = Text("Line " + (i + 1), body, italic, italic.material, 34f, TextAlignmentOptions.TopLeft,
                "A line of the letter.", Vector2.zero, new Vector2(760f, 50f));
            t.color = InkColor;
            t.lineSpacing = 8f;
        }
        p.lines = body.GetComponentsInChildren<TMP_Text>(true);

        p.hint = Ink("Hint", paper.transform, font, 20f, TextAlignmentOptions.Center, "Click to continue",
            new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(760f, 34f), 0.5f);
    }

    // Memory card: name, illustration area, the one-line memory.
    static void BuildCardUi(Transform root, DigHud hud, TMP_FontAsset font, TMP_FontAsset italic)
    {
        hud.cardGroup = Overlay("Memory Card", root, new Color(0.05f, 0.03f, 0.02f, 0.55f));
        var panel = Paper("Card", hud.cardGroup.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(580f, 740f));
        hud.cardPanel = (RectTransform)panel.transform;

        var top = new Vector2(0.5f, 1f);
        hud.cardTitle = Ink("Title", panel.transform, font, 42f, TextAlignmentOptions.Center, "Bicycle Bell", top, new Vector2(0f, -40f), new Vector2(500f, 64f));

        var art = UiNode("Illustration", panel.transform, top, top, new Vector2(0f, -122f), new Vector2(480f, 330f));
        hud.cardIllustration = art.gameObject.AddComponent<Image>();
        hud.cardIllustration.raycastTarget = false;
        var outline = art.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(InkColor.r, InkColor.g, InkColor.b, 0.35f);
        outline.effectDistance = new Vector2(2f, -2f);

        hud.cardLine = Ink("Line", panel.transform, italic, 30f, TextAlignmentOptions.Center,
            "“Someone was holding the back of the seat.”", top, new Vector2(0f, -472f), new Vector2(490f, 190f));
        Ink("Hint", panel.transform, font, 20f, TextAlignmentOptions.Center, "Click to close",
            new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(490f, 34f), 0.45f);
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
            if (t.font != TMP_Settings.defaultFontAsset) continue;   // keep fonts chosen on purpose
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
    // Golden hour. Each part is applied once, so later tweaks in the Lighting window are kept.
    static void BuildLighting(Mats m)
    {
        if (RenderSettings.skybox != m.sky)
        {
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
            report.AppendLine("  set golden-hour lighting, sky and fog");
        }

        // The photographic HDRI would clash with the low-poly look as a visible sky, so it is
        // only used for reflections. The visible sky stays procedural, and ambient stays
        // Trilight because DigAtmosphere darkens it with depth.
        if (RenderSettings.customReflectionTexture == null && File.Exists(HdriPath))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(HdriPath);
            if (importer.textureShape != TextureImporterShape.TextureCube)
            {
                importer.textureShape = TextureImporterShape.TextureCube;
                importer.SaveAndReimport();
            }
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(HdriPath);
            if (cube != null)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cube;
                RenderSettings.fogDensity = 0.013f;   // thinner, so the distant hills read
                report.AppendLine("  using the Evening Field HDRI for reflections; thinner fog");
            }
        }
        DynamicGI.UpdateEnvironment();
    }

    // ---- Scene: shaft and hub -------------------------------------------------
    static void BuildDigSite(Scene scene, Mats m, Mesh wallMesh)
    {
        float s = DigTerrain.Size;
        var digSite = Root(scene, "Dig Site", out _);

        Ensure(digSite, "Shaft Floor", () =>
        {
            var floor = new GameObject("Shaft Floor", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            floor.GetComponent<MeshRenderer>().sharedMaterial = m.strata;
            floor.AddComponent<DigTerrain>();
            return floor;
        });

        Ensure(digSite, "Shaft Walls", () =>
        {
            var walls = new GameObject("Shaft Walls", typeof(MeshFilter), typeof(MeshRenderer));
            walls.GetComponent<MeshFilter>().sharedMesh = wallMesh;
            var wr = walls.GetComponent<MeshRenderer>();
            wr.sharedMaterial = m.strata;
            wr.shadowCastingMode = ShadowCastingMode.Off;
            return walls;
        });

        // Colliders and shadow casters only: the visible walls are "Shaft Walls".
        Ensure(digSite, "Shaft Earth", () =>
        {
            var earth = new GameObject("Shaft Earth");
            const float t = 2f, h = DigGame.TotalDepth + 2f;
            EarthBox("West", earth.transform, new Vector3(-t / 2f, -h / 2f, s / 2f), new Vector3(t, h, s + 2f * t), m.earth);
            EarthBox("East", earth.transform, new Vector3(s + t / 2f, -h / 2f, s / 2f), new Vector3(t, h, s + 2f * t), m.earth);
            EarthBox("South", earth.transform, new Vector3(s / 2f, -h / 2f, -t / 2f), new Vector3(s, h, t), m.earth);
            EarthBox("North", earth.transform, new Vector3(s / 2f, -h / 2f, s + t / 2f), new Vector3(s, h, t), m.earth);
            return earth;
        });

        Ensure(digSite, "Safety Floor", () =>
        {
            var bottom = new GameObject("Safety Floor").AddComponent<BoxCollider>();
            bottom.transform.position = new Vector3(s / 2f, -DigGame.TotalDepth - 0.6f, s / 2f);
            bottom.size = new Vector3(s, 1f, s);
            return bottom.gameObject;
        });

        Ensure(digSite, "Debris", () =>
        {
            var debris = new GameObject("Debris").AddComponent<DigDebris>();
            debris.terrain = digSite.Find("Shaft Floor").GetComponent<DigTerrain>();
            debris.clodMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            debris.materials = m.clods;
            return debris.gameObject;
        });
    }

    // The yard in front of the house's back door: spawn, memory shelf, workbench lean-to.
    static void BuildHub(Scene scene, GameObject shelfPrefab, GameObject benchPrefab)
    {
        var hub = Root(scene, "Surface Hub", out bool created);
        if (!created) return;

        var spawn = new GameObject("Spawn Point").transform;
        spawn.SetParent(hub, false);
        spawn.position = new Vector3(3f, 1f, -8f);

        // Where the ending leaves you: looking at the shelf by the back door.
        var endView = new GameObject("Ending View").transform;
        endView.SetParent(hub, false);
        endView.position = new Vector3(ShelfPos.x, 1f, ShelfPos.z + 3.6f);
        endView.rotation = Quaternion.Euler(0f, 180f, 0f);

        Instance(shelfPrefab, hub, ShelfPos, 0f);

        var leanTo = new GameObject("Lean-to").transform;
        leanTo.SetParent(hub, false);
        Instance(benchPrefab, leanTo, BenchPos, -90f);
        Place(leanTo, "SurvivalKit/structure-roof", BenchPos, -90f, width: 3.4f);
        Place(leanTo, "SurvivalKit/resource-planks", BenchPos + new Vector3(0.6f, 0f, 1.3f), 10f, width: 1.2f);
        Place(leanTo, "SurvivalKit/tool-pickaxe", BenchPos + new Vector3(0.9f, 0f, -1.2f), 30f, height: 0.6f);
        Lantern(leanTo, BenchPos + new Vector3(1.4f, 0f, 1.6f), 2.1f);
    }

    static void WireGame(Scene scene, MemoryData[] memories, LetterData[] letters, GameObject findPrefab)
    {
        var gameRoot = Root(scene, "Game", out _);
        // Not "??": Unity's fake-null objects defeat it.
        var game = gameRoot.GetComponent<DigGame>();
        if (game == null) game = gameRoot.gameObject.AddComponent<DigGame>();
        var atmosphere = gameRoot.GetComponent<DigAtmosphere>();
        if (atmosphere == null) atmosphere = gameRoot.gameObject.AddComponent<DigAtmosphere>();
        var story = gameRoot.GetComponent<DigStory>();
        if (story == null) story = gameRoot.gameObject.AddComponent<DigStory>();

        var player = Find<DigPlayer>(scene);
        if (game.terrain == null) game.terrain = Find<DigTerrain>(scene);
        if (game.debris == null) game.debris = Find<DigDebris>(scene);
        if (game.player == null) game.player = player;
        if (game.shovel == null && player != null) game.shovel = player.shovel;
        if (game.hud == null) game.hud = Find<DigHud>(scene);
        if (game.workbench == null) game.workbench = Find<Workbench>(scene);
        if (game.shelf == null) game.shelf = Find<MemoryShelf>(scene);
        var hub = FindRoot(scene, "Surface Hub");
        if (game.spawnPoint == null && hub != null) game.spawnPoint = hub.Find("Spawn Point");
        if (game.memories == null || game.memories.Length == 0) game.memories = memories;
        if (game.findPrefab == null) game.findPrefab = findPrefab.GetComponent<MemoryFind>();
        if (game.story == null) game.story = story;
        EditorUtility.SetDirty(game);

        if (story.game == null) story.game = game;
        if (story.hud == null) story.hud = game.hud;
        if (story.letters == null || story.letters.Length == 0) story.letters = letters.Take(3).ToArray();
        EditorUtility.SetDirty(story);

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

    // The material a Kenney model was imported with (they share one colormap material per kit).
    static Material KitMaterial(string kitModel)
    {
        string path = KenneyDir + "/" + kitModel + ".fbx";
        var mat = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().FirstOrDefault();
        if (mat == null)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var r = go != null ? go.GetComponentInChildren<Renderer>() : null;
            if (r != null) mat = r.sharedMaterial;
        }
        return mat;
    }

    static Material TintedMaterial(string name, Material source, Color tint, float smoothness, float metallic)
    {
        if (source == null) return null;
        return LoadOrCreate(MatDir + "/" + name + ".mat", () =>
        {
            var mat = new Material(source);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.GetColor("_BaseColor") * tint);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
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
        if (material != null) t.fontSharedMaterial = material;
        t.fontSize = size;
        t.color = TextColor;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.raycastTarget = false;
        t.text = text;
        return t;
    }

    static Transform FindRoot(Scene scene, string name)
    {
        var go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
        return go != null ? go.transform : null;
    }

    static void DestroyRoot(Scene scene, string name)
    {
        var t = FindRoot(scene, name);
        if (t == null) return;
        Object.DestroyImmediate(t.gameObject);
        report.AppendLine("  removed scene/" + name + " (rebuilt below)");
    }

    // Finds a root object by name, or creates it (empty, or from a prefab).
    static Transform Root(Scene scene, string name, out bool created, GameObject prefab = null)
    {
        var existing = FindRoot(scene, name);
        if (existing != null) { created = false; Skipped("scene/" + name); return existing; }

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

    // Finds a child by name, or creates it with `create` and parents it (keeping world pose).
    static Transform Ensure(Transform parent, string name, System.Func<GameObject> create)
    {
        var existing = parent.Find(name);
        if (existing != null) return existing;
        var go = create();
        go.name = name;
        go.transform.SetParent(parent, true);
        Created("scene/" + parent.name + "/" + name);
        return go.transform;
    }

    static GameObject Instance(GameObject prefab, Transform parent, Vector3 position, float yaw)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        return go;
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
