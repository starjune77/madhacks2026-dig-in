using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

// Everything that makes digging feel like digging, as assets:
//   - the shovel's swing as animation clips and an Animator Controller (Assets/Animations),
//   - lumpy clod meshes and their physics material,
//   - the crumb and dust particle systems and their materials,
//   - the material of the ring that previews the next scoop.
// All of it can be opened and tuned in the editor afterwards.
public static partial class DigInSceneBuilder
{
    const string AnimDir = "Assets/Animations";
    const int SwingVersion = 5;     // bump to rewrite the clips (hand edits are lost)

    class Feel
    {
        public RuntimeAnimatorController shovelController;
        public Mesh[] clodMeshes;
        public PhysicsMaterial clodPhysics;
        public Material crumb, dust, ring;
    }

    // Where the shovel rests in camera space: low on the right, blade tipped up.
    static readonly Vector3 ShovelRestPos = new Vector3(0.40f, -0.46f, 0.40f);
    static readonly Vector3 ShovelRestRot = new Vector3(-30f, -20f, 8f);

    static Feel BuildFeelAssets()
    {
        var f = new Feel();
        f.shovelController = BuildShovelAnimations();

        // Lumps of earth: noisy, squashed, with flat broken faces where they split off.
        f.clodMeshes = new Mesh[8];
        for (int i = 0; i < f.clodMeshes.Length; i++)
        {
            int seed = 300 + i * 23;
            // The version is in the file name: a new shape gets new files, the old ones are removed.
            f.clodMeshes[i] = LoadOrCreate(MeshDir + "/Earth Clod v2 " + (char)('A' + i) + ".asset", () => BuildClodMesh(seed));
            if (AssetDatabase.DeleteAsset(MeshDir + "/Earth Clod " + (char)('A' + i) + ".asset")) report.AppendLine("  deleted an old Earth Clod mesh");
        }
        foreach (var old in new[] { "A", "B", "C", "D" })
            if (AssetDatabase.DeleteAsset(MeshDir + "/Clod " + old + ".asset")) report.AppendLine("  deleted the old Clod " + old + " mesh");

        f.clodPhysics = LoadOrCreate(MatDir + "/Clod.physicMaterial", () => new PhysicsMaterial("Clod")
        {
            dynamicFriction = 0.85f,
            staticFriction = 0.95f,
            bounciness = 0.12f,
            frictionCombine = PhysicsMaterialCombine.Maximum,
            bounceCombine = PhysicsMaterialCombine.Minimum,
        });

        // Crumbs are tinted per particle (the stratum's colour), so they use the particle shader.
        f.crumb = LoadOrCreate(MatDir + "/Crumb.mat", () =>
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0f);
            mat.enableInstancing = true;
            return mat;
        });

        var puff = BuildPuffTexture();
        f.dust = LoadOrCreate(MatDir + "/Dust.mat", () => TransparentParticleMaterial(puff));
        f.ring = LoadOrCreate(MatDir + "/Aim Ring.mat", () => TransparentParticleMaterial(null));
        return f;
    }

    static Material TransparentParticleMaterial(Texture texture)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        if (texture != null) mat.SetTexture("_BaseMap", texture);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Surface", 1f);          // transparent
        mat.SetFloat("_Blend", 0f);            // alpha
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }

    // A soft, slightly uneven puff for dust.
    static Texture2D BuildPuffTexture()
    {
        string path = TexDir + "/Soft Puff.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) { Skipped(path); return existing; }

        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
            float r = Mathf.Sqrt(u * u + v * v);
            float cloud = 0.65f + 0.35f * Mathf.PerlinNoise(u * 2.3f + 5f, v * 2.3f + 9f) + 0.15f * (Mathf.PerlinNoise(u * 6f + 2f, v * 6f + 4f) - 0.5f);
            float a = Mathf.Clamp01(1f - r);
            a = a * a * (3f - 2f * a) * Mathf.Clamp01(cloud);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        Created(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---- Shovel animation ------------------------------------------------------------
    // Key poses of the "Pivot" under the shovel, in camera space. Each stroke winds up,
    // drives the blade in (the fastest part, ending dead at the impact), levers the earth
    // out, flicks it away to the right, and settles.
    struct Pose
    {
        public float time;
        public Vector3 pos, rot;
        public bool hit;       // arrive fast and stop dead (the impact)
        public Pose(float time, Vector3 pos, Vector3 rot, bool hit = false) { this.time = time; this.pos = pos; this.rot = rot; this.hit = hit; }
    }

    static RuntimeAnimatorController BuildShovelAnimations()
    {
        string controllerPath = AnimDir + "/Shovel.controller";
        string marker = AnimDir + "/Shovel swing v" + SwingVersion + ".txt";
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (existing != null && File.Exists(marker)) { Skipped(controllerPath); return existing; }

        foreach (var old in Directory.GetFiles(AnimDir, "Shovel swing v*.txt")) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
        // The clips are rewritten in place, so the controller and the Player prefab keep their references.

        var rest = new Pose(0f, ShovelRestPos, ShovelRestRot);
        // At the impact the blade is out in front, just under the crosshair.
        var impactPos = new Vector3(0.16f, -0.22f, 0.70f);
        var impactRot = new Vector3(26f, -6f, 0f);

        var idle = Clip("Shovel Idle", true, new[] { rest, new Pose(1f, rest.pos, rest.rot) }, null);

        var swing = Clip("Shovel Swing", false, new[]
        {
            rest,
            new Pose(0.10f, new Vector3(0.42f, -0.38f, 0.31f), new Vector3(-50f, -24f, 10f)),    // drawing back
            new Pose(0.21f, new Vector3(0.40f, -0.27f, 0.25f), new Vector3(-70f, -18f, 6f)),     // wound up
            new Pose(0.30f, impactPos, impactRot, hit: true),                                    // the blade lands
            new Pose(0.37f, new Vector3(0.15f, -0.25f, 0.77f), new Vector3(33f, -6f, -2f)),      // pushed in
            new Pose(0.47f, new Vector3(0.27f, -0.27f, 0.60f), new Vector3(-6f, -14f, 10f)),     // levered out, loaded
            new Pose(0.56f, new Vector3(0.47f, -0.25f, 0.46f), new Vector3(-40f, -36f, 28f)),    // flicked away
            new Pose(0.66f, new Vector3(0.44f, -0.40f, 0.42f), new Vector3(-36f, -26f, 14f)),    // follow-through
            new Pose(0.80f, rest.pos, rest.rot),
        },
        new[] { Event(0.30f, "OnImpact"), Event(0.50f, "OnToss"), Event(0.62f, "OnReady"), Event(0.79f, "OnDone") });

        var bounce = Clip("Shovel Bounce", false, new[]
        {
            new Pose(0f, impactPos, impactRot),
            new Pose(0.07f, new Vector3(0.33f, -0.29f, 0.42f), new Vector3(-16f, -16f, 12f)),     // thrown back
            new Pose(0.16f, new Vector3(0.37f, -0.40f, 0.44f), new Vector3(-22f, -20f, 4f)),
            new Pose(0.32f, rest.pos, rest.rot),
        },
        new[] { Event(0.24f, "OnReady"), Event(0.31f, "OnDone") });

        File.WriteAllText(marker, "Marks the version of the shovel clips made by DigIn > Build Scene.\n");
        AssetDatabase.ImportAsset(marker);

        // States are added to an existing controller in place, so the Player prefab keeps its reference.
        var controller = existing != null ? existing : AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        var sm = controller.layers[0].stateMachine;
        var idleState = State(sm, "Idle", idle);
        sm.defaultState = idleState;
        // "Swing" and "Swing 2" are the same clip: holding the button cross-fades from one
        // into the other, because an Animator will not cross-fade a state into itself.
        foreach (var (name, clip) in new[] { ("Swing", swing), ("Swing 2", swing), ("Bounce", bounce) })
        {
            var state = State(sm, name, clip);
            if (state.transitions.Length > 0) continue;
            var back = state.AddTransition(idleState);
            back.hasExitTime = true;
            back.exitTime = 0.98f;
            back.hasFixedDuration = true;
            back.duration = 0.05f;
        }
        EditorUtility.SetDirty(controller);
        if (existing == null) Created(controllerPath);
        return controller;
    }

    static AnimatorState State(AnimatorStateMachine sm, string name, Motion motion)
    {
        AnimatorState state = null;
        foreach (var child in sm.states)
            if (child.state.name == name) state = child.state;
        if (state == null) state = sm.AddState(name);
        state.motion = motion;
        return state;
    }

    static AnimationEvent Event(float time, string function)
    {
        return new AnimationEvent { time = time, functionName = function };
    }

    static AnimationClip Clip(string name, bool loop, Pose[] poses, AnimationEvent[] events)
    {
        var clip = new AnimationClip { name = name, frameRate = 60f };
        string[] props = { "localPosition.x", "localPosition.y", "localPosition.z", "localEulerAngles.x", "localEulerAngles.y", "localEulerAngles.z" };
        for (int c = 0; c < 6; c++)
        {
            var keys = new Keyframe[poses.Length];
            for (int i = 0; i < poses.Length; i++)
            {
                float v = c < 3 ? poses[i].pos[c] : poses[i].rot[c - 3];
                keys[i] = new Keyframe(poses[i].time, v);
            }
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0f);

            // The strike: leave the wind-up slowly, arrive at full speed, stop dead.
            for (int i = 1; i < poses.Length; i++)
            {
                if (!poses[i].hit) continue;
                float slope = (keys[i].value - keys[i - 1].value) / (keys[i].time - keys[i - 1].time);
                var k = curve[i];
                k.inTangent = slope * 2.2f;
                k.outTangent = 0f;
                curve.MoveKey(i, k);
                var before = curve[i - 1];
                before.outTangent = 0f;
                curve.MoveKey(i - 1, before);
            }
            clip.SetCurve("Pivot", typeof(Transform), props[c], curve);
        }

        if (loop)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }
        if (events != null) AnimationUtility.SetAnimationEvents(clip, events);

        string path = AnimDir + "/" + name + ".anim";
        var asset = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (asset != null)
        {
            EditorUtility.CopySerialized(clip, asset);
            EditorUtility.SetDirty(asset);
            report.AppendLine("  rewrote " + path);
            return asset;
        }
        AssetDatabase.CreateAsset(clip, path);
        Created(path);
        return clip;
    }

    // ---- Clods -------------------------------------------------------------------------
    // A lumpy clod about one unit across: a subdivided icosahedron pushed around by noise.
    static Mesh BuildClodMesh(int seed)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var verts = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
        var tris = new List<int>
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };

        for (int pass = 0; pass < 2; pass++)
        {
            var cache = new Dictionary<long, int>();
            var next = new List<int>(tris.Count * 4);
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int m)) return m;
                verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                cache[key] = verts.Count - 1;
                return verts.Count - 1;
            }
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            tris = next;
        }

        var rng = new System.Random(seed);
        float R() => (float)rng.NextDouble();
        var squash = new Vector3(Mathf.Lerp(0.75f, 1.2f, R()), Mathf.Lerp(0.55f, 0.9f, R()), Mathf.Lerp(0.75f, 1.2f, R()));
        float ox = R() * 50f, oy = R() * 50f, oz = R() * 50f;
        float Noise(Vector3 v, float f) =>
            (Mathf.PerlinNoise(v.x * f + ox, v.y * f + oy) + Mathf.PerlinNoise(v.y * f + oz, v.z * f + ox) + Mathf.PerlinNoise(v.z * f + oy, v.x * f + oz)) / 3f - 0.5f;
        for (int i = 0; i < verts.Count; i++)
        {
            Vector3 v = verts[i];
            float r = 1f + Noise(v, 1.3f) * 0.9f + Noise(v, 3.7f) * 0.6f + Noise(v, 9f) * 0.35f;
            verts[i] = Vector3.Scale(v, squash) * (0.5f * r);
        }

        // Broken faces: one to three planes the clod split along, pressed nearly flat.
        int cuts = 2 + rng.Next(3);
        for (int k = 0; k < cuts; k++)
        {
            Vector3 n = new Vector3(R() * 2f - 1f, R() * 2f - 1f, R() * 2f - 1f).normalized;
            float d = Mathf.Lerp(0.12f, 0.3f, R());
            for (int i = 0; i < verts.Count; i++)
            {
                float over = Vector3.Dot(verts[i], n) - d;
                if (over > 0f) verts[i] -= n * over * 0.92f;
            }
        }

        // Front faces are clockwise in Unity: make sure the first triangle faces outward.
        Vector3 a0 = verts[tris[0]], b0 = verts[tris[1]], c0 = verts[tris[2]];
        if (Vector3.Dot(Vector3.Cross(b0 - a0, c0 - a0), a0 + b0 + c0) < 0f)
            for (int i = 0; i < tris.Count; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);

        var mesh = new Mesh { name = "Clod" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---- Debris objects in the scene ---------------------------------------------------
    static GameObject BuildDebris(Mats m, Feel f)
    {
        var go = new GameObject("Debris");
        var debris = go.AddComponent<DigDebris>();
        debris.clodMeshes = f.clodMeshes;
        debris.clodMaterials = m.clods;
        debris.clodPhysics = f.clodPhysics;

        // Crumbs: tiny grains with weight, bouncing off the ground and settling.
        var crumbsGo = new GameObject("Crumbs");
        crumbsGo.transform.SetParent(go.transform, false);
        var crumbs = crumbsGo.AddComponent<ParticleSystem>();
        var main = crumbs.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 2f;
        main.startSpeed = 0f;
        main.startSize = 0.03f;
        main.startRotation3D = true;
        main.gravityModifier = 1.3f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 700;
        var emission = crumbs.emission;
        emission.enabled = false;
        var shape = crumbs.shape;
        shape.enabled = false;
        var collision = crumbs.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.quality = ParticleSystemCollisionQuality.High;
        collision.bounce = 0.22f;
        collision.dampen = 0.45f;
        collision.lifetimeLoss = 0f;
        collision.radiusScale = 0.6f;
        collision.collidesWith = 1;   // Default: ground and props, not the player or clods
        var rotation = crumbs.rotationOverLifetime;
        rotation.enabled = true;
        rotation.separateAxes = true;
        rotation.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
        rotation.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
        rotation.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        var size = crumbs.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0f)));
        var crumbRenderer = crumbsGo.GetComponent<ParticleSystemRenderer>();
        crumbRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        crumbRenderer.mesh = f.clodMeshes[0];
        crumbRenderer.sharedMaterial = f.crumb;
        crumbRenderer.alignment = ParticleSystemRenderSpace.Local;
        crumbRenderer.enableGPUInstancing = true;
        debris.crumbs = crumbs;

        // Dust: a soft puff that swells and thins out.
        var dustGo = new GameObject("Dust");
        dustGo.transform.SetParent(go.transform, false);
        var dust = dustGo.AddComponent<ParticleSystem>();
        main = dust.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 1.2f;
        main.startSpeed = 0f;
        main.startSize = 0.5f;
        main.gravityModifier = -0.015f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 160;
        emission = dust.emission;
        emission.enabled = false;
        shape = dust.shape;
        shape.enabled = false;
        var limit = dust.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.12f;
        limit.limit = 0.05f;
        var fade = dust.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.55f, 0.5f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        size = dust.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.45f, 0f, 3f), new Keyframe(0.3f, 1.1f, 0.9f, 0.9f), new Keyframe(1f, 1.7f, 0.5f, 0f)));
        var dustRotation = dust.rotationOverLifetime;
        dustRotation.enabled = true;
        dustRotation.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
        var dustRenderer = dustGo.GetComponent<ParticleSystemRenderer>();
        dustRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        dustRenderer.sharedMaterial = f.dust;
        dustRenderer.shadowCastingMode = ShadowCastingMode.Off;
        debris.dust = dust;
        return go;
    }

    static GameObject BuildAimRing(Feel f)
    {
        var go = new GameObject("Aim Ring");
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = f.ring;
        line.widthMultiplier = 0.009f;
        line.numCornerVertices = 2;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.alignment = LineAlignment.View;
        go.AddComponent<DigAim>();
        return go;
    }

    // ---- Layers ------------------------------------------------------------------------
    // "Player" and "Debris": loose earth never pushes the player or blocks aiming.
    static void EnsureLayers()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return;
        var tags = new SerializedObject(assets[0]);
        var layers = tags.FindProperty("layers");
        bool changed = false;
        foreach (var (index, name) in new[] { (6, "Player"), (7, "Debris") })
        {
            var p = layers.GetArrayElementAtIndex(index);
            if (p.stringValue == name) continue;
            if (!string.IsNullOrEmpty(p.stringValue)) report.AppendLine("  layer " + index + " was '" + p.stringValue + "', now '" + name + "'");
            p.stringValue = name;
            changed = true;
        }
        if (changed)
        {
            tags.ApplyModifiedPropertiesWithoutUndo();
            report.AppendLine("  added the Player and Debris layers");
        }
        Physics.IgnoreLayerCollision(6, 7, true);
    }
}
