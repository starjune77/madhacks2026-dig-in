using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// The realistic look: imported model materials, the HDRI sky and sun, post-processing.
//   Meshy models  Assets/ThirdParty/Meshy/<Name>/<Name>.fbx + <Name>_textures/*.png
//   Poly Haven    Assets/ThirdParty/PolyHaven/Models/<id>/<id>.fbx + textures/*
//   Unity Terrain demo pack (trees, grass, terrain layers)  Assets/ThirdParty/UnityTerrainDemo
// Materials made here live in Assets/Materials/Models and can be tuned by hand; they are
// only created when missing.
public static partial class DigInSceneBuilder
{
    const string MeshyRoot = "Assets/ThirdParty/Meshy";
    const string PolyHavenModels = "Assets/ThirdParty/PolyHaven/Models";
    const string DemoPack = "Assets/ThirdParty/UnityTerrainDemo";
    const string ModelMatDir = "Assets/Materials/Models";

    static Shader LitShader => Shader.Find("Universal Render Pipeline/Lit");

    // Orientation of the Meshy models (checked in screenshots).
    const float ShelfYaw = 0f;       // turns the bookshelf's open side toward the yard (+Z)
    const float CounterYaw = 0f;     // turns the shop counter's front toward the player
    static readonly float[] ShelfRows = { 0.70f, 0.42f, 0.13f };   // shelf boards, as a fraction of the shelf's height

    // Real grass on the diggable yard: the demo pack's clumps, drawn by DigGrass.
    static GameObject BuildYardGrass(DigVolume volume)
    {
        var go = new GameObject("Grass");
        var grass = go.AddComponent<DigGrass>();
        grass.volume = volume;
        grass.bareCentre = DigStart;
        grass.area = new Rect(YardMinX - 1f, YardMinZ, YardMaxX - YardMinX + 2f, YardMaxZ - YardMinZ + 1f);
        var kinds = new List<DigGrass.Kind>();
        foreach (var (n, w, lo, hi) in new[] { ("Grass_B", 3f, 0.8f, 1.2f), ("Grass_C", 2f, 0.7f, 1.1f), ("Grass_D", 1f, 0.6f, 1.0f), ("GrassDry_A", 0.6f, 0.7f, 1.1f), ("Grass_A", 2f, 1.0f, 1.6f) })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPack + "/Prefabs/Details/" + n + ".prefab");
            if (prefab == null) continue;
            var mf = prefab.GetComponentInChildren<MeshFilter>();
            var mr = prefab.GetComponentInChildren<MeshRenderer>();
            mr.sharedMaterial.enableInstancing = true;
            kinds.Add(new DigGrass.Kind { mesh = mf.sharedMesh, material = mr.sharedMaterial, weight = w, scale = new Vector2(lo, hi) });
        }
        grass.kinds = kinds.ToArray();
        return go;
    }

    // ---- Models ------------------------------------------------------------------------
    // A Meshy model: one material from its base colour and normal maps, nearly matte.
    static GameObject MeshyAsset(string name, float smoothness = 0.2f)
    {
        string fbx = MeshyRoot + "/" + name + "/" + name + ".fbx";
        if (!File.Exists(fbx)) { report.AppendLine("  MISSING " + fbx); return null; }
        string texDir = MeshyRoot + "/" + name + "/" + name + "_textures/";
        EnsureFolder(ModelMatDir);
        var mat = LoadOrCreate(ModelMatDir + "/Meshy " + name + ".mat", () =>
        {
            var m = new Material(LitShader);
            m.SetTexture("_BaseMap", ImportScan(texDir + "base_color.png", TextureImporterType.Default, true, 2048));
            var nrm = ImportScan(texDir + "normal.png", TextureImporterType.NormalMap, false, 2048);
            if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            return m;
        });
        return RemapModel(fbx, _ => mat);
    }

    // A Poly Haven model: one material per texture set in its textures/ folder (colour,
    // OpenGL normal, and alpha for foliage, which becomes alpha-clipped and double-sided).
    static GameObject PolyHavenAsset(string id)
    {
        string dir = PolyHavenModels + "/" + id;
        string fbx = dir + "/" + id + ".fbx";
        if (!File.Exists(fbx)) { report.AppendLine("  MISSING " + fbx); return null; }
        EnsureFolder(ModelMatDir);
        var diffs = Directory.GetFiles(dir + "/textures", "*_diff_*").Select(p => p.Replace('\\', '/'))
            .Where(p => !p.EndsWith(".meta") && !p.EndsWith("_rgba.png")).ToArray();
        if (diffs.Length == 0) return AssetDatabase.LoadAssetAtPath<GameObject>(fbx);

        Material MaterialFor(string diffPath)
        {
            string prefix = Path.GetFileName(diffPath);
            prefix = prefix.Substring(0, prefix.IndexOf("_diff_"));
            return LoadOrCreate(ModelMatDir + "/" + prefix + ".mat", () =>
            {
                var m = new Material(LitShader);
                string alphaPath = Directory.GetFiles(dir + "/textures", prefix + "_alpha_*").FirstOrDefault(f => !f.EndsWith(".meta"));
                Texture2D baseMap;
                if (alphaPath != null)
                {
                    baseMap = CombineAlpha(diffPath, alphaPath.Replace('\\', '/'));
                    m.SetFloat("_AlphaClip", 1f);
                    m.EnableKeyword("_ALPHATEST_ON");
                    m.SetFloat("_Cutoff", 0.45f);
                    m.SetFloat("_Cull", 0f);       // leaves are seen from both sides
                    m.doubleSidedGI = true;
                }
                else baseMap = ImportScan(diffPath, TextureImporterType.Default, true, 1024);
                m.SetTexture("_BaseMap", baseMap);
                string nrmPath = Directory.GetFiles(dir + "/textures", prefix + "_nor_gl_*").FirstOrDefault(f => !f.EndsWith(".meta"));
                if (nrmPath != null)
                {
                    m.SetTexture("_BumpMap", ImportScan(nrmPath.Replace('\\', '/'), TextureImporterType.NormalMap, false, 1024));
                    m.EnableKeyword("_NORMALMAP");
                }
                m.SetFloat("_Smoothness", 0.12f);
                m.SetFloat("_Metallic", 0f);
                m.enableInstancing = true;
                return m;
            });
        }

        return RemapModel(fbx, slot =>
        {
            // Match the FBX's material to a texture set by name; one set fits all.
            string best = diffs[0];
            foreach (var d in diffs)
            {
                string prefix = Path.GetFileName(d);
                prefix = prefix.Substring(0, prefix.IndexOf("_diff_"));
                if (slot.ToLowerInvariant().Contains(prefix.ToLowerInvariant()) || prefix.ToLowerInvariant().Contains(slot.ToLowerInvariant())) best = d;
            }
            return MaterialFor(best);
        });
    }

    // Points every material the FBX brings with it at our own, so the importer never
    // extracts duplicate textures or materials.
    static GameObject RemapModel(string fbx, System.Func<string, Material> materialFor)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
        bool changed = false;
        if (importer.importCameras || importer.importLights || importer.importAnimation)
        {
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            changed = true;
        }
        if (importer.materialImportMode == ModelImporterMaterialImportMode.None)
        {
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            changed = true;
        }
        if (changed) importer.SaveAndReimport();

        // Material slots: the ones still embedded, plus the ones already remapped (a remapped
        // material is no longer listed in the FBX, even if what it points at was deleted).
        var remaps = importer.GetExternalObjectMap();
        var slots = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Material>().Select(m => m.name)
            .Concat(remaps.Keys.Where(k => k.type == typeof(Material)).Select(k => k.name)).Distinct().ToArray();
        foreach (var slot in slots)
        {
            var ours = materialFor(slot);
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), slot);
            if (remaps.TryGetValue(id, out var current) && current == ours) continue;
            importer.AddRemap(id, ours);
            changed = true;
        }
        if (changed) importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
    }

    // Base colour with the separate alpha map in its alpha channel, written next to it.
    static Texture2D CombineAlpha(string diffPath, string alphaPath)
    {
        string outPath = diffPath.Substring(0, diffPath.LastIndexOf('.')) + "_rgba.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        if (existing != null) return existing;
        var c = ReadableCopy(diffPath, true);
        var a = ReadableCopy(alphaPath, false);
        var cp = c.GetPixels32();
        var ap = a.GetPixels32();
        for (int i = 0; i < cp.Length; i++) cp[i].a = ap[Mathf.Min(i, ap.Length - 1)].r;
        c.SetPixels32(cp);
        File.WriteAllBytes(outPath, c.EncodeToPNG());
        Object.DestroyImmediate(c);
        Object.DestroyImmediate(a);
        AssetDatabase.ImportAsset(outPath);
        var imp = (TextureImporter)AssetImporter.GetAtPath(outPath);
        imp.alphaIsTransparency = true;
        imp.mipMapsPreserveCoverage = true;     // leaves keep their shape in the distance
        imp.alphaTestReferenceValue = 0.45f;
        imp.maxTextureSize = 1024;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }

    static Texture2D ReadableCopy(string path, bool sRGB)
    {
        var bytes = File.ReadAllBytes(path);
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, !sRGB);
        t.LoadImage(bytes);
        return t;
    }

    // ---- Sky, sun and post-processing ---------------------------------------------------
    // The Evening Field HDRI is the visible sky and the ambient light. The sun is pointed
    // to where the HDRI's own sun is, so shadows agree with the sky.
    static void BuildRealisticLighting()
    {
        var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(HdriPath);
        if (cube == null) { report.AppendLine("  MISSING " + HdriPath); return; }

        var sky = LoadOrCreate(MatDir + "/Sky Evening Field.mat", () =>
        {
            var m = new Material(Shader.Find("Skybox/Cubemap"));
            m.SetTexture("_Tex", cube);
            m.SetFloat("_Exposure", 1.0f);
            m.SetColor("_Tint", new Color(0.52f, 0.5f, 0.48f));   // gamma-space grey = neutral
            return m;
        });

        var sun = Object.FindObjectsByType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
        if (sun == null) sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.name = "Sun";
        if (RenderSettings.skybox != sky)
        {
            Vector3 dir = HdriSunDirection(cube);
            sun.transform.rotation = Quaternion.LookRotation(-dir);
            sun.color = new Color(1f, 0.80f, 0.58f);    // a little too golden
            sun.intensity = 2.1f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;
            report.AppendLine("  sun set from the HDRI: " + sun.transform.eulerAngles.ToString("0"));
        }
        RenderSettings.sun = sun;
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.3f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 0.8f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.86f, 0.74f, 0.60f);
        RenderSettings.fogDensity = 0.0085f;
        DynamicGI.UpdateEnvironment();
        BuildPostVolume();
    }

    static Vector3 HdriSunDirection(Cubemap cube)
    {
        string path = AssetDatabase.GetAssetPath(cube);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        bool readable = imp.isReadable;
        if (!readable) { imp.isReadable = true; imp.SaveAndReimport(); cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path); }

        float best = -1f;
        Vector3 dir = new Vector3(0.6f, 0.25f, 0.75f).normalized;
        int n = cube.width;
        var faces = new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ };
        foreach (var face in faces)
        {
            var px = cube.GetPixels(face);
            for (int i = 0; i < px.Length; i += 3)
            {
                float lum = px[i].r * 0.3f + px[i].g * 0.59f + px[i].b * 0.11f;
                if (lum <= best) continue;
                best = lum;
                float u = ((i % n) + 0.5f) / n * 2f - 1f, v = ((i / n) + 0.5f) / n * 2f - 1f;
                switch (face)
                {
                    case CubemapFace.PositiveX: dir = new Vector3(1f, -v, -u); break;
                    case CubemapFace.NegativeX: dir = new Vector3(-1f, -v, u); break;
                    case CubemapFace.PositiveY: dir = new Vector3(u, 1f, v); break;
                    case CubemapFace.NegativeY: dir = new Vector3(u, -1f, -v); break;
                    case CubemapFace.PositiveZ: dir = new Vector3(u, -v, 1f); break;
                    default: dir = new Vector3(-u, -v, -1f); break;
                }
            }
        }
        if (!readable) { imp.isReadable = false; imp.SaveAndReimport(); }
        dir.Normalize();
        // Keep the sun low but above the horizon, whatever the brightest texel is.
        if (dir.y < 0.12f) dir = new Vector3(dir.x, 0.12f, dir.z).normalized;
        return dir;
    }

    // Warm, calm, filmic: ACES, soft bloom, a warm grade, faint grain and vignette, a little
    // depth of field far away. SSAO is on the PC renderer already.
    static void BuildPostVolume()
    {
        string path = "Assets/Settings/DigIn Post.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.7f);
            color.contrast.Override(8f);
            color.saturation.Override(6f);
            color.colorFilter.Override(new Color(1f, 0.95f, 0.88f));

            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(14f);
            wb.tint.Override(4f);

            var lgg = profile.Add<LiftGammaGain>(true);
            lgg.lift.Override(new Vector4(1f, 0.97f, 0.92f, 0.02f));
            lgg.gain.Override(new Vector4(1f, 0.98f, 0.94f, 0f));

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.75f);
            bloom.tint.Override(new Color(1f, 0.9f, 0.78f));

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.24f);
            vignette.smoothness.Override(0.5f);
            vignette.color.Override(new Color(0.18f, 0.1f, 0.05f));

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.16f);

            var dof = profile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(28f);
            dof.gaussianEnd.Override(110f);
            dof.gaussianMaxRadius.Override(0.8f);

            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            Created(path);
        }

        var volume = Object.FindObjectsByType<Volume>().FirstOrDefault(v => v.isGlobal);
        if (volume == null) volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;
        volume.priority = 1f;
        EditorUtility.SetDirty(volume);

        // Soft shadows that reach across the yard.
        var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (rp != null && rp.shadowDistance < 80f)
        {
            rp.shadowDistance = 80f;
            rp.shadowCascadeCount = 3;
            EditorUtility.SetDirty(rp);
        }
    }
}
