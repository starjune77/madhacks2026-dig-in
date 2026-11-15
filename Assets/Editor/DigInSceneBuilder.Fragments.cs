using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Sixteen small memory fragments between the nine major memories: ordinary things
// (Poly Haven, CC0), each with one quiet line. Picking one up shows only its line.
public static partial class DigInSceneBuilder
{
    const string FragmentDir = "Assets/Data/Fragments";
    const string FragmentVersion = "v1 fragment";

    // Poly Haven id, metres along the life path, longest side (m), line.
    static readonly (string id, float distance, float size, string line)[] FragmentDefs =
    {
        ("food_apple_01", 4f, 0.09f, "Packed in my lunch every day. I traded it every day."),
        ("lambis_shell", 10.5f, 0.14f, "We held it to our ears to hear the sea. It only ever sounded like the hallway."),
        ("vintage_flashlight", 17f, 0.16f, "Under the covers, reading past bedtime. The batteries always ran out first."),
        ("sungka_board", 24f, 0.3f, "Count the stones, sow them around. Grandma always won, and always pretended not to."),
        ("binder_notebook", 27.5f, 0.22f, "My name on the cover, in three different handwritings. One for each year."),
        ("stationery_supplies", 35f, 0.18f, "Someone always borrowed my good pen. I never asked for it back."),
        ("digital_wrist_watch", 44f, 0.08f, "The alarm went off at 7:15 for years after I stopped needing it."),
        ("portable_cassette_player", 52f, 0.15f, "Side B, again. The tape went thin where we rewound it most."),
        ("round_spectacles", 55f, 0.12f, "Grandpa let me try them on. The whole room went soft."),
        ("can_rusted", 58f, 0.12f, "We buried a time capsule in a tin like this. I never found it again."),
        ("office_notepads", 64.5f, 0.17f, "A list of things to do. All crossed out but one."),
        ("lightbulb_led", 71f, 0.1f, "The porch light was always on when I got home late."),
        ("vintage_lighter", 72.5f, 0.07f, "Birthday candles. I always wished for something different."),
        ("pocket_watch", 77f, 0.07f, "It stopped at twenty past four. Nobody wound it after that."),
        ("wall_clock", 79f, 0.22f, "It ticked loudest when the house was empty."),
        ("fishermans_hat", 80.5f, 0.24f, "Dad wore it to every lake, even the ones with no fish."),
    };

    static MemoryData[] BuildFragments(System.Func<float, int> stratumAt)
    {
        EnsureFolder(FragmentDir);
        var list = new List<MemoryData>();
        for (int i = 0; i < FragmentDefs.Length; i++)
        {
            var d = FragmentDefs[i];
            var asset = PolyHavenAsset(d.id);
            if (asset == null) continue;
            var data = LoadOrCreate(FragmentDir + "/" + (i + 1).ToString("00") + " " + d.id + ".asset", () =>
            {
                var m = ScriptableObject.CreateInstance<MemoryData>();
                m.title = d.id;
                m.line = d.line;
                m.pathDistance = d.distance;
                m.fragment = true;
                return m;
            });
            data.stratum = stratumAt(d.distance);
            var prefab = BuildFragmentPrefab(d.id, asset, d.size);
            if (data.findPrefab != prefab) data.findPrefab = prefab;
            EditorUtility.SetDirty(data);
            list.Add(data);
        }
        report.AppendLine("  " + list.Count + " fragments");
        return list.ToArray();
    }

    static MemoryFind BuildFragmentPrefab(string id, GameObject asset, float size)
    {
        string path = PrefabDir + "/Fragments/" + id + ".prefab";
        EnsureFolder(PrefabDir + "/Fragments");
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null && existing.transform.Find(FragmentVersion) != null) return existing.GetComponent<MemoryFind>();

        var root = new GameObject(id);
        AddMarker(root, FragmentVersion);
        var col = root.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.3f;
        col.center = new Vector3(0f, 0.1f, 0f);
        var find = root.AddComponent<MemoryFind>();
        find.size = 1f;
        find.hover = 0.02f;
        var holder = new GameObject("Model").transform;
        holder.SetParent(root.transform, false);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, holder);
        var b = LocalBounds(holder);
        model.transform.localScale *= size / Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.y, b.size.z));
        b = LocalBounds(holder);
        model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
        find.model = holder;
        var glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(root.transform, false);
        glow.transform.localPosition = new Vector3(0f, 0.35f, 0f);
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.88f, 0.62f);
        glow.range = 1.1f;
        glow.intensity = 0.45f;
        glow.shadows = LightShadows.None;
        return SavePrefab(root, path).GetComponent<MemoryFind>();
    }
}
