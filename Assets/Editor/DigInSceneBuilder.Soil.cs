using UnityEditor;
using UnityEngine;

// The photoscanned ground: Poly Haven textures (CC0) in Assets/ThirdParty/PolyHaven/<id>/,
// each as <id>_diff_2k.jpg, <id>_nor_gl_2k.jpg and <id>_ao_1k.jpg. This sets their import
// settings and puts them on the soil and clod materials. Tints and scales are only set
// when the material does not have the maps yet, so hand tuning in the inspector is kept.
public static partial class DigInSceneBuilder
{
    const string PolyHavenDir = "Assets/ThirdParty/PolyHaven";

    const string DirtWall = "excavated_soil_wall";   // compact earth on walls (Childhood)
    const string DirtFloor = "forest_ground_06";     // loose earth on floors
    const string Clay = "red_mud_stones";            // School Days
    const string Stone = "rock_face_03";             // Growing Up
    const string Grass = "leafy_grass";              // the untouched top

    // Per stratum: dirt, clay, stone. The scans are pushed toward the strata colours.
    static readonly Color[] SoilTints =
    {
        new Color(0.98f, 0.92f, 0.86f),
        new Color(1.10f, 0.86f, 0.74f),
        new Color(0.86f, 0.86f, 0.88f),
    };

    struct SoilMap { public Texture2D diff, nrm, ao; }

    static SoilMap LoadSoilMap(string id)
    {
        string dir = PolyHavenDir + "/" + id + "/" + id;
        var m = new SoilMap
        {
            diff = ImportScan(dir + "_diff_2k.jpg", TextureImporterType.Default, true, 2048),
            nrm = ImportScan(dir + "_nor_gl_2k.jpg", TextureImporterType.NormalMap, false, 2048),
            ao = ImportScan(dir + "_ao_1k.jpg", TextureImporterType.Default, false, 1024),
        };
        if (m.diff == null) report.AppendLine("  MISSING texture " + dir + "_diff_2k.jpg");
        return m;
    }

    static Texture2D ImportScan(string path, TextureImporterType type, bool sRGB, int maxSize)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return null;
        if (importer.textureType != type || importer.sRGBTexture != sRGB || importer.maxTextureSize != maxSize || importer.anisoLevel != 8)
        {
            importer.textureType = type;
            importer.sRGBTexture = sRGB;
            importer.maxTextureSize = maxSize;
            importer.anisoLevel = 8;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void SetUpSoil(Material mat)
    {
        if (mat.shader != Shader.Find("DigIn/Soil")) mat.shader = Shader.Find("DigIn/Soil");
        bool fresh = mat.GetTexture("_DirtWallDiff") == null;
        Put(mat, "_DirtWall", LoadSoilMap(DirtWall));
        Put(mat, "_DirtFloor", LoadSoilMap(DirtFloor));
        Put(mat, "_Clay", LoadSoilMap(Clay));
        Put(mat, "_Stone", LoadSoilMap(Stone));
        Put(mat, "_Grass", LoadSoilMap(Grass));
        if (fresh)
        {
            mat.SetColor("_DirtTint", SoilTints[0]);
            mat.SetColor("_ClayTint", SoilTints[1]);
            mat.SetColor("_StoneTint", SoilTints[2]);
            mat.SetColor("_GrassTint", new Color(0.95f, 1f, 0.82f));
            report.AppendLine("  put the photoscanned maps on " + mat.name);
        }
        EditorUtility.SetDirty(mat);
    }

    static void Put(Material mat, string prefix, SoilMap m)
    {
        mat.SetTexture(prefix + "Diff", m.diff);
        mat.SetTexture(prefix + "Nrm", m.nrm);
        mat.SetTexture(prefix + "AO", m.ao);
    }

    static Material ClodMaterial(string name, string scan, Color tint)
    {
        var shader = Shader.Find("DigIn/Clod");
        var mat = LoadOrCreate(MatDir + "/" + name + ".mat", () => new Material(shader));
        bool fresh = mat.shader != shader;
        if (fresh)
        {
            mat.shader = shader;
            mat.SetColor("_BaseColor", tint);
        }
        var m = LoadSoilMap(scan);
        mat.SetTexture("_BaseMap", m.diff);
        mat.SetTexture("_NormalMap", m.nrm);
        mat.SetTexture("_AOMap", m.ao);
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
