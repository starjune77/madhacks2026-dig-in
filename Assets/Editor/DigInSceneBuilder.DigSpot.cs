using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Where to dig: a small hand-painted wooden sign ("Dig here" and an arrow) on firm ground
// facing the spawn, a loose ring of stones around the bare patch, and a garden trowel
// stuck in the dirt. Stones and trowel come loose and fall if the ground under them is
// dug (DigLoosen), so they never float. Poly Haven models and textures (CC0).
public static partial class DigInSceneBuilder
{
    // On firm ground just outside the diggable circle, on the way from the spawn.
    static readonly Vector3 SignPos = new Vector3(1.3f, 0f, -2.3f);

    static GameObject BuildDigSpot()
    {
        var spot = new GameObject("Dig Spot");
        var t = spot.transform;
        int debris = LayerMask.NameToLayer("Debris");

        // ---- The sign: a post, a weathered board, painted words and an arrow -------------
        var wood = LoadOrCreate(ModelMatDir + "/Sign Wood.mat", () => ScanMaterial("weathered_planks", new Vector2(0.6f, 0.35f), new Color(0.95f, 0.88f, 0.78f)));
        var paint = LoadOrCreate(ModelMatDir + "/Sign Paint.mat", () =>
        {
            var m = new Material(LitShader);
            m.SetColor("_BaseColor", new Color(0.96f, 0.92f, 0.82f));
            m.SetFloat("_Smoothness", 0.25f);
            return m;
        });

        var sign = new GameObject("Sign").transform;
        sign.SetParent(t, false);
        Vector3 toSpawn = SpawnPos - SignPos;
        toSpawn.y = 0f;
        sign.position = OnGround(SignPos.x, SignPos.z);
        sign.rotation = Quaternion.LookRotation(toSpawn.normalized);   // the board faces the spawn (+Z)
        SignBox(sign, "Post", new Vector3(0f, 0.55f, -0.04f), new Vector3(0.07f, 1.1f, 0.07f), Quaternion.Euler(0f, 0f, 2f), wood);
        var board = SignBox(sign, "Board", new Vector3(0f, 0.92f, 0f), new Vector3(0.72f, 0.34f, 0.035f), Quaternion.Euler(0f, 0f, -3f), wood);
        var col = sign.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.6f, 0f);
        col.size = new Vector3(0.75f, 1.2f, 0.12f);

        // Painted words, a little uneven, facing the spawn.
        var italic = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontDir + "/Lora-Italic SDF.asset");
        var textGo = new GameObject("Words", typeof(RectTransform));
        textGo.transform.SetParent(board, false);
        var rt = (RectTransform)textGo.transform;
        rt.localPosition = new Vector3(0.17f, 0.02f, 0.53f);             // board space: z is its thin side; +x is the reader's left
        rt.localRotation = Quaternion.Euler(0f, 180f, 1.5f);
        rt.localScale = new Vector3(1f / board.localScale.x, 1f / board.localScale.y, 1f) * 0.1f;
        rt.sizeDelta = new Vector2(6f, 2.6f);
        var tmp = textGo.AddComponent<TextMeshPro>();
        if (italic != null) tmp.font = italic;
        tmp.text = "Dig here";
        tmp.fontSize = 13f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.97f, 0.93f, 0.84f);
        tmp.fontStyle = FontStyles.Bold;

        // A painted arrow beside the words, pointing on toward the patch.
        var arrow = new GameObject("Arrow").transform;
        arrow.SetParent(sign, false);
        // Seen from the front, local -x is the reader's right: the arrow sits right of the
        // words and leans right, toward the patch behind the sign.
        arrow.localPosition = new Vector3(-0.27f, 0.92f, 0.022f);
        arrow.localRotation = Quaternion.Euler(0f, 0f, 22f);
        SignQuad(arrow, "Shaft", new Vector3(0f, -0.02f, 0f), new Vector3(0.022f, 0.16f, 1f), 0f, paint);
        SignQuad(arrow, "Head A", new Vector3(-0.024f, 0.04f, 0f), new Vector3(0.02f, 0.08f, 1f), 42f, paint);
        SignQuad(arrow, "Head B", new Vector3(0.024f, 0.04f, 0f), new Vector3(0.02f, 0.08f, 1f), -42f, paint);

        // ---- A loose ring of stones around the bare patch ------------------------------------
        var rng = new System.Random(31);
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

        // ---- A garden trowel stuck in the dirt -------------------------------------------------
        var trowel = PolyHavenAsset("trowel_01");
        if (trowel != null)
        {
            var go = PlaceAsset(t, trowel, OnGround(DigStart.x - 0.9f, DigStart.y - 1.1f), 0f, width: 0.3f);
            // Lay it along Z, then stand it on its tip, leaning back, blade in the soil.
            go.transform.rotation = Quaternion.Euler(-62f, 200f, 8f) * Quaternion.FromToRotation(LongAxis(go), Vector3.forward);
            var b = WorldBounds(go);
            go.transform.position += new Vector3(0f, OnGround(DigStart.x - 0.9f, DigStart.y - 1.1f).y - b.min.y - b.size.y * 0.28f, 0f);
            Loosen(go, debris);
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

    static Transform SignBox(Transform parent, string name, Vector3 pos, Vector3 size, Quaternion rot, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go.transform;
    }

    static void SignQuad(Transform parent, string name, Vector3 pos, Vector3 size, float roll, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, 180f, roll);   // faces +Z, like the words
        go.transform.localScale = size;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }
}
