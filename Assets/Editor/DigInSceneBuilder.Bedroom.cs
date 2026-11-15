using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// The ending: you wake up. A small bedroom far from the yard, morning light through the
// window, and the family cat curled up asleep on your chest. Its camera stays off until
// DigStory switches to it after the blank page.
public static partial class DigInSceneBuilder
{
    const string BedroomVersion = "v2 morning";
    static readonly Vector3 BedroomPos = new Vector3(420f, 0f, 420f);
    // Which end of the Meshy bed has the pillow, along its long axis (+1 or -1).
    const float PillowEnd = 1f;

    static void BuildBedroom(Scene scene)
    {
        var room = Root(scene, "Bedroom", out bool created);
        if (!created && room.Find(BedroomVersion) != null) return;
        if (!created) { Object.DestroyImmediate(room.gameObject); room = Root(scene, "Bedroom", out _); }
        new GameObject(BedroomVersion).transform.SetParent(room, false);
        room.position = BedroomPos;

        // Turn the room so its window (local +Z wall) faces the sun: morning light pours in.
        var sun = RenderSettings.sun;
        Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.forward;
        room.rotation = Quaternion.Euler(0f, Mathf.Atan2(toSun.x, toSun.z) * Mathf.Rad2Deg, 0f);

        EnsureFolder(ModelMatDir);
        var plaster = LoadOrCreate(ModelMatDir + "/Bedroom Plaster.mat", () => ScanMaterial("painted_plaster_wall", new Vector2(1.5f, 1.5f), new Color(1f, 0.97f, 0.92f)));
        var floor = LoadOrCreate(ModelMatDir + "/Bedroom Floor.mat", () => ScanMaterial("plank_flooring", new Vector2(2f, 2f), Color.white));

        const float w = 3.6f, d = 4.2f, h = 2.6f, t = 0.1f;
        Box(room, "Floor", new Vector3(0f, -t / 2f, 0f), new Vector3(w, t, d), floor);
        Box(room, "Ceiling", new Vector3(0f, h + t / 2f, 0f), new Vector3(w, t, d), plaster);
        Box(room, "Wall Back", new Vector3(0f, h / 2f, -d / 2f), new Vector3(w, h, t), plaster);
        Box(room, "Wall Left", new Vector3(-w / 2f, h / 2f, 0f), new Vector3(t, h, d), plaster);
        Box(room, "Wall Right", new Vector3(w / 2f, h / 2f, 0f), new Vector3(t, h, d), plaster);
        // The window wall, built around a 1.3 x 1.2 m opening.
        float ww = 1.3f, wh = 1.2f, sill = 0.9f, z = d / 2f;
        Box(room, "Wall Window Left", new Vector3(-(w + ww) / 4f, h / 2f, z), new Vector3((w - ww) / 2f, h, t), plaster);
        Box(room, "Wall Window Right", new Vector3((w + ww) / 4f, h / 2f, z), new Vector3((w - ww) / 2f, h, t), plaster);
        Box(room, "Wall Under Window", new Vector3(0f, sill / 2f, z), new Vector3(ww, sill, t), plaster);
        Box(room, "Wall Over Window", new Vector3(0f, (sill + wh + h) / 2f, z), new Vector3(ww, h - sill - wh, t), plaster);

        // The bed against the left wall, its head at the back; the cat asleep on it.
        var bedAsset = MeshyAsset("Bed", 0.1f);
        Bounds bed = new Bounds(room.TransformPoint(new Vector3(-0.8f, 0.3f, -0.6f)), new Vector3(1f, 0.6f, 2f));
        GameObject bedGo = null;
        if (bedAsset != null)
        {
            bedGo = PlaceAsset(room, bedAsset, room.TransformPoint(new Vector3(-0.85f, 0f, -0.7f)), room.eulerAngles.y, width: 2.05f);
            var b = WorldBounds(bedGo);
            // Long side along the room's Z.
            Vector3 size = Quaternion.Inverse(room.rotation) * b.size;
            if (Mathf.Abs(size.x) > Mathf.Abs(size.z)) bedGo.transform.rotation = room.rotation * Quaternion.Euler(0f, 90f, 0f);
            // Push it into the back-left corner: head against the back wall, side near the left wall.
            var lb = LocalBoundsIn(room, bedGo);
            bedGo.transform.position += room.TransformVector(new Vector3(-w / 2f + 0.08f - lb.min.x, 0f, -d / 2f + 0.08f - lb.min.z));
            bed = WorldBounds(bedGo);
        }
        Prop(room, "ClassicNightstand_01", room.TransformPoint(new Vector3(0.25f, 0f, -d / 2f + 0.35f)), room.eulerAngles.y, height: 0.6f);

        // Where your chest is: a third of the way down the bed from the pillow, on top of the quilt.
        var bl = bedGo != null ? LocalBoundsIn(room, bedGo) : new Bounds(new Vector3(-0.8f, 0.3f, -1f), new Vector3(1f, 0.6f, 2f));
        Vector3 bedLocal = bl.center;
        // The pillow is at the back wall (PillowEnd = 1) or at the other end (-1).
        float headZ = PillowEnd > 0f ? bl.min.z + 0.1f : bl.max.z - 0.1f;
        float footZ = PillowEnd > 0f ? bl.max.z - 0.1f : bl.min.z + 0.1f;
        Vector3 chest = new Vector3(bedLocal.x, 0f, Mathf.Lerp(headZ, footZ, 0.4f));
        float top = TopOf(bedGo, room.TransformPoint(chest), bed);

        var cat = MakeCat("CatSleeping", 0.38f, false);
        if (cat != null)
        {
            cat.transform.SetParent(room, true);
            cat.transform.position = new Vector3(room.TransformPoint(chest).x, top - 0.02f, room.TransformPoint(chest).z);
            cat.transform.rotation = room.rotation * Quaternion.Euler(0f, 70f, 0f);
        }

        // Your eyes: on the pillow, looking down along yourself to the cat and the window.
        var camGo = new GameObject("Wake Camera");
        camGo.transform.SetParent(room, false);
        Vector3 eye = room.TransformPoint(new Vector3(bedLocal.x, 0f, Mathf.Lerp(headZ, footZ, 0.12f)));
        float eyeTop = TopOf(bedGo, eye, bed);
        camGo.transform.position = new Vector3(eye.x, eyeTop + 0.16f, eye.z);
        Vector3 look = (cat != null ? cat.transform.position + Vector3.up * 0.12f : room.TransformPoint(chest)) - camGo.transform.position;
        Vector3 window = room.TransformPoint(new Vector3(0f, sill + wh * 0.5f, z)) - camGo.transform.position;
        camGo.transform.rotation = Quaternion.LookRotation(Vector3.Slerp(look.normalized, window.normalized, 0.5f));
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 58f;
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 300f;
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        camGo.AddComponent<AudioListener>().enabled = false;
        camGo.SetActive(false);

        // Soft morning light: a warm spot through the window and a faint fill so the room is never dark.
        var spot = new GameObject("Morning Light").AddComponent<Light>();
        spot.transform.SetParent(room, false);
        spot.transform.localPosition = new Vector3(0f, sill + wh + 0.4f, z + 1.4f);
        spot.transform.localRotation = Quaternion.LookRotation(new Vector3(-0.35f, -0.55f, -1f));
        spot.type = LightType.Spot;
        spot.spotAngle = 70f;
        spot.innerSpotAngle = 30f;
        spot.range = 7f;
        spot.intensity = 6f;
        spot.color = new Color(1f, 0.86f, 0.66f);
        spot.shadows = LightShadows.Soft;
        var fill = new GameObject("Room Fill").AddComponent<Light>();
        fill.transform.SetParent(room, false);
        fill.transform.localPosition = new Vector3(0.6f, h - 0.4f, 0.4f);
        fill.type = LightType.Point;
        fill.range = 5f;
        fill.intensity = 0.7f;
        fill.color = new Color(1f, 0.9f, 0.78f);
        fill.shadows = LightShadows.None;
    }

    // Bounds of `go`'s meshes in `space`'s local coordinates.
    static Bounds LocalBoundsIn(Transform space, GameObject go)
    {
        bool any = false;
        var b = new Bounds();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = space.InverseTransformPoint(mf.transform.TransformPoint(c));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }
        return b;
    }

    // Height of the bed's top surface at `p`, found by casting onto its meshes.
    static float TopOf(GameObject go, Vector3 p, Bounds fallback)
    {
        float top = fallback.max.y * 0.8f + fallback.min.y * 0.2f;
        if (go == null) return top;
        float best = float.MinValue;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            if (mc.Raycast(new Ray(new Vector3(p.x, fallback.max.y + 1f, p.z), Vector3.down), out RaycastHit hit, 10f)) best = Mathf.Max(best, hit.point.y);
            Object.DestroyImmediate(mc);
        }
        return best > float.MinValue ? best : top;
    }

    static Material ScanMaterial(string id, Vector2 tiling, Color tint)
    {
        var m = new Material(LitShader);
        string dir = PolyHavenDir + "/" + id + "/" + id;
        m.SetTexture("_BaseMap", ImportScan(dir + "_diff_1k.jpg", TextureImporterType.Default, true, 1024));
        m.SetTextureScale("_BaseMap", tiling);
        var n = ImportScan(dir + "_nor_gl_1k.jpg", TextureImporterType.NormalMap, false, 1024);
        if (n != null) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
        m.SetColor("_BaseColor", tint);
        m.SetFloat("_Smoothness", 0.15f);
        return m;
    }

    static void Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        // Cube UVs are per face; stretch the tiling to the face size via a world-space-ish scale.
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.TwoSided;
    }
}
