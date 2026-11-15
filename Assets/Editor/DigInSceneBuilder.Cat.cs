using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

// The family cat (Meshy). Meshy can only rig humanoids, so the cat is a still model that
// lives through animation clips on a "Body" pivot: slow breathing, a little settle now
// and then, and a faster, softer rhythm when it purrs. DigCat turns it to watch you.
public static partial class DigInSceneBuilder
{
    // Which way each cat model faces (degrees) before it is turned to face the yard: the
    // Munchkin kitten (Meshy, from one original reference image) sitting, licking a paw,
    // and asleep.
    static float CatModelYaw(string meshyName) =>
        meshyName == "Munchkin" ? CatYawSit : meshyName == "MunchkinLick" ? CatYawLick : meshyName == "MunchkinSleeping" ? CatYawSleep : 0f;
    const float CatYawSit = 0f, CatYawLick = 0f, CatYawSleep = 40f;   // asleep: its face toward your eyes

    static RuntimeAnimatorController BuildCatAnimations()
    {
        string path = AnimDir + "/Cat.controller";
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (existing != null) { Skipped(path); return existing; }

        // Breathing: the body swells a touch, mostly sideways (a cat breathes with its sides),
        // and every few breaths it settles: a tiny lean and back.
        var idle = new AnimationClip { name = "Cat Breathe", frameRate = 30f };
        SetScaleCurves(idle, new[] { 0f, 1.6f, 3.2f, 4.8f, 6.4f, 8.0f, 9.6f },
            new[] { 1f, 1.022f, 1f, 1.024f, 1f, 1.02f, 1f },
            new[] { 1f, 1.010f, 1f, 1.012f, 1f, 1.01f, 1f });
        idle.SetCurve("Body", typeof(Transform), "localEulerAngles.z", Curve(new[] { 0f, 5.6f, 6.4f, 7.4f, 9.6f }, new[] { 0f, 0f, 1.6f, 0f, 0f }));
        idle.SetCurve("Body", typeof(Transform), "localEulerAngles.x", Curve(new[] { 0f, 2.2f, 3.0f, 4.0f, 9.6f }, new[] { 0f, 0f, -1.2f, 0f, 0f }));
        Loop(idle);
        AssetDatabase.CreateAsset(idle, AnimDir + "/Cat Breathe.anim");

        // Purring: quick, shallow breaths with the faintest tremble.
        var purr = new AnimationClip { name = "Cat Purr", frameRate = 30f };
        float[] t = new float[13];
        float[] w = new float[13];
        float[] h = new float[13];
        for (int i = 0; i < 13; i++) { t[i] = i * 0.25f; w[i] = i % 2 == 0 ? 1f : 1.014f; h[i] = i % 2 == 0 ? 1f : 1.007f; }
        SetScaleCurves(purr, t, w, h);
        Loop(purr);
        AssetDatabase.CreateAsset(purr, AnimDir + "/Cat Purr.anim");

        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Purr", AnimatorControllerParameterType.Bool);
        var sm = controller.layers[0].stateMachine;
        var a = sm.AddState("Breathe"); a.motion = idle;
        var b = sm.AddState("Purr"); b.motion = purr;
        sm.defaultState = a;
        var toPurr = a.AddTransition(b); toPurr.hasExitTime = false; toPurr.duration = 0.4f; toPurr.AddCondition(AnimatorConditionMode.If, 0f, "Purr");
        var toIdle = b.AddTransition(a); toIdle.hasExitTime = false; toIdle.duration = 0.8f; toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Purr");
        Created(path);
        return controller;
    }

    static AnimationCurve Curve(float[] times, float[] values)
    {
        var keys = new Keyframe[times.Length];
        for (int i = 0; i < times.Length; i++) keys[i] = new Keyframe(times[i], values[i]);
        var c = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++) c.SmoothTangents(i, 0f);
        return c;
    }

    static void SetScaleCurves(AnimationClip clip, float[] times, float[] wide, float[] tall)
    {
        clip.SetCurve("Body", typeof(Transform), "localScale.x", Curve(times, wide));
        clip.SetCurve("Body", typeof(Transform), "localScale.y", Curve(times, tall));
        clip.SetCurve("Body", typeof(Transform), "localScale.z", Curve(times, wide));
    }

    static void Loop(AnimationClip clip)
    {
        var s = AnimationUtility.GetAnimationClipSettings(clip);
        s.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, s);
    }

    // Cat (DigCat, Animator) / Body (animated) / one holder per pose, each with its model
    // standing on the origin. `lickName`: a second pose of the same cat, licking a paw.
    static GameObject MakeCat(string meshyName, float length, bool watches, string lickName = null)
    {
        var model = MeshyAsset(meshyName, 0.15f);
        if (model == null) return null;
        var root = new GameObject(watches ? "Cat" : "Sleeping Cat");
        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        var sit = CatPose(model, meshyName, body, "Sit", length);
        GameObject lick = null;
        var lickModel = lickName != null ? MeshyAsset(lickName, 0.15f) : null;
        if (lickModel != null)
        {
            // Scaled to the same height as the sitting cat, so it is the same size.
            lick = CatPose(lickModel, lickName, body, "Lick", length);
            float hs = LocalBounds(sit.transform).size.y, hl = LocalBounds(lick.transform).size.y;
            var m = lick.transform.GetChild(0);
            m.localScale *= hs / Mathf.Max(0.001f, hl);
            var lb = LocalBounds(lick.transform);
            m.localPosition -= new Vector3(lb.center.x, lb.min.y, lb.center.z);
        }
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.On;

        var animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = BuildCatAnimations();
        animator.cullingMode = AnimatorCullingMode.CullCompletely;
        var cat = root.AddComponent<DigCat>();
        cat.watchDistance = watches ? 6f : 0f;
        if (lick != null)
        {
            cat.sitPose = sit;
            cat.lickPose = lick;
            lick.SetActive(false);
        }
        return root;
    }

    static GameObject CatPose(GameObject model, string meshyName, Transform body, string name, float length)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(body, false);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model, holder.transform);
        go.transform.localRotation = Quaternion.Euler(0f, CatModelYaw(meshyName), 0f);
        var b = LocalBounds(holder.transform);
        go.transform.localScale *= length / Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.z));
        b = LocalBounds(holder.transform);
        go.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
        return holder;
    }

    // The cat sits on the counter, at the end nearest the yard, looking out.
    static void BuildShopCat(Transform shop, GameObject counter)
    {
        var cat = MakeCat("Munchkin", 0.32f, true, "MunchkinLick");
        if (cat == null) return;
        cat.transform.SetParent(shop, false);
        var b = WorldBounds(counter);
        // The counter faces -X (yaw -90): the cat sits on top, toward the front, facing the yard.
        // The tabletop, found by casting down through the counter's mesh (its box collider
        // includes the tall chalkboard).
        Vector3 spot = new Vector3(b.center.x - b.extents.x * 0.2f, 0f, b.center.z + b.extents.z * 0.55f);
        float top = b.min.y + b.size.y * 0.7f;
        var probe = new GameObject("probe");
        foreach (var mf in counter.GetComponentsInChildren<MeshFilter>())
        {
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            if (mc.Raycast(new Ray(spot + Vector3.up * (b.max.y + 1f), Vector3.down), out RaycastHit hit, 10f) && hit.point.y < b.min.y + b.size.y * 0.85f)
                top = Mathf.Max(top < b.min.y + b.size.y * 0.7f + 0.001f ? hit.point.y : top, hit.point.y);
            Object.DestroyImmediate(mc);
        }
        Object.DestroyImmediate(probe);
        cat.transform.position = new Vector3(spot.x, top, spot.z);
        cat.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
    }
}
