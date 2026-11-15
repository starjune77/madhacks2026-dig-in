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
    const string BedroomVersion = "v3 curtains";
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
        BuildWindow(room, ww, wh, sill, z - t / 2f);

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

    // A painted wooden window in the opening (frame, sill, glazing bars that throw a soft
    // cross of shadow into the room) and two cream curtains drawn back to the sides on a
    // rod. `inner` is the room-side face of the window wall (local z). Poly Haven textures.
    static void BuildWindow(Transform room, float ww, float wh, float sill, float inner)
    {
        var frame = LoadOrCreate(ModelMatDir + "/Window Frame.mat", () => ScanMaterial("weathered_planks", new Vector2(0.3f, 0.3f), new Color(0.98f, 0.95f, 0.88f)));
        var cloth = LoadOrCreate(ModelMatDir + "/Curtain.mat", () => ScanMaterial("terlenka", new Vector2(1.5f, 3f), new Color(1f, 0.95f, 0.86f)));
        var rodMat = LoadOrCreate(ModelMatDir + "/Curtain Rod.mat", () =>
        {
            var m = new Material(LitShader);
            m.SetColor("_BaseColor", new Color(0.30f, 0.21f, 0.14f));
            m.SetFloat("_Smoothness", 0.45f);
            return m;
        });

        var win = new GameObject("Window").transform;
        win.SetParent(room, false);
        float top = sill + wh, mid = inner + 0.05f;   // the sash sits in the middle of the wall
        const float cw = 0.08f, cd = 0.025f;          // casing width and depth
        // Casing around the opening on the room side, and a deeper sill.
        Box(win, "Casing Left", new Vector3(-(ww + cw) / 2f, sill + wh / 2f, inner - cd / 2f), new Vector3(cw, wh + cw, cd), frame);
        Box(win, "Casing Right", new Vector3((ww + cw) / 2f, sill + wh / 2f, inner - cd / 2f), new Vector3(cw, wh + cw, cd), frame);
        Box(win, "Casing Top", new Vector3(0f, top + cw / 2f, inner - cd / 2f), new Vector3(ww + cw * 2f, cw, cd), frame);
        Box(win, "Sill", new Vector3(0f, sill - 0.02f, inner - 0.06f), new Vector3(ww + 0.22f, 0.04f, 0.2f), frame);
        // The sash: a frame and a cross of glazing bars.
        const float sw = 0.055f, sd = 0.045f;
        Box(win, "Sash Left", new Vector3(-(ww - sw) / 2f, sill + wh / 2f, mid), new Vector3(sw, wh, sd), frame);
        Box(win, "Sash Right", new Vector3((ww - sw) / 2f, sill + wh / 2f, mid), new Vector3(sw, wh, sd), frame);
        Box(win, "Sash Top", new Vector3(0f, top - sw / 2f, mid), new Vector3(ww, sw, sd), frame);
        Box(win, "Sash Bottom", new Vector3(0f, sill + sw / 2f, mid), new Vector3(ww, sw, sd), frame);
        Box(win, "Bar Upright", new Vector3(0f, sill + wh / 2f, mid), new Vector3(0.035f, wh, 0.03f), frame);
        Box(win, "Bar Across", new Vector3(0f, sill + wh * 0.55f, mid), new Vector3(ww, 0.035f, 0.03f), frame);

        // The rod, a little above and wider than the window.
        float rodY = top + 0.2f, rodZ = inner - 0.11f, rodLen = ww + 0.75f;
        var rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(rod.GetComponent<Collider>());
        rod.name = "Curtain Rod";
        rod.transform.SetParent(win, false);
        rod.transform.localPosition = new Vector3(0f, rodY, rodZ);
        rod.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        rod.transform.localScale = new Vector3(0.025f, rodLen / 2f, 0.025f);
        rod.GetComponent<MeshRenderer>().sharedMaterial = rodMat;
        foreach (float side in new[] { -1f, 1f })
            Box(win, side < 0f ? "Bracket Left" : "Bracket Right", new Vector3(side * (rodLen / 2f - 0.06f), rodY, (inner + rodZ) / 2f), new Vector3(0.02f, 0.04f, inner - rodZ), rodMat);

        // Two curtains drawn back, each an accordion of soft folds hanging from the rod to
        // just below the sill, slightly overlapping the window's edge.
        float drop = rodY - 0.03f - (sill - 0.18f);
        Curtain(win, "Curtain Left", -1f, ww, rodY - 0.03f, drop, rodZ, cloth);
        Curtain(win, "Curtain Right", 1f, ww, rodY - 0.03f, drop, rodZ, cloth);
    }

    static void Curtain(Transform parent, string name, float side, float ww, float hangY, float drop, float z, Material cloth)
    {
        var c = new GameObject(name).transform;
        c.SetParent(parent, false);
        const int folds = 9;
        const float pitch = 0.06f, depth = 0.045f;
        float inner = ww / 2f - 0.12f;                 // its inner edge, a little over the glass
        for (int i = 0; i < folds; i++)
        {
            // Zigzag between the front and back of the folds, outward from the inner edge.
            float x0 = side * (inner + i * pitch), x1 = side * (inner + (i + 1) * pitch);
            float z0 = z + (i % 2 == 0 ? 0f : depth) - depth / 2f, z1 = z + (i % 2 == 0 ? depth : 0f) - depth / 2f;
            Vector3 a = new Vector3(x0, 0f, z0), b = new Vector3(x1, 0f, z1);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = "Fold " + (i + 1);
            go.transform.SetParent(c, false);
            // Each fold hangs a touch longer toward the outside, so the hem is not a ruler line.
            float len = drop + (i % 3) * 0.012f;
            go.transform.localPosition = (a + b) / 2f + Vector3.up * (hangY - len / 2f);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.right, (b - a).normalized);
            go.transform.localScale = new Vector3((b - a).magnitude + 0.006f, len, 0.008f);
            go.GetComponent<MeshRenderer>().sharedMaterial = cloth;
        }
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
