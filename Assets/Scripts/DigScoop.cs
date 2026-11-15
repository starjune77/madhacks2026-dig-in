using System;
using System.Collections.Generic;
using UnityEngine;

// Runs each shovel stroke's bite out of the ground. The hole does not appear at once:
// it grows over a few frames, then small pieces of its rim give way and crumble.
// Sits on "Dig Site/Scoops".
public class DigScoop : MonoBehaviour
{
    public DigVolume volume;
    public DigDebris debris;

    [Header("The bite")]
    [Tooltip("Seconds for the hole to open.")]
    public float growTime = 0.13f;
    [Tooltip("How much longer the scoop is along the dig direction than across it.")]
    public float stretch = 1.25f;

    [Header("Crumbling rim")]
    public int crumbles = 4;
    public Vector2 crumbleRadius = new Vector2(0.10f, 0.18f);
    [Tooltip("Seconds after the bite over which the rim crumbles.")]
    public float crumbleWindow = 0.35f;

    class Scoop
    {
        public Vector3 center, axis;
        public float radius, time, removed;
        public int level, stratum;
        public Action<Vector3, float, float> done;
        public bool grown;
        public List<Vector4> rim;      // xyz = position, w = time it gives way
    }

    readonly List<Scoop> active = new List<Scoop>();

    // `done` is called with (centre, radius, cubic metres removed) once the hole is open.
    public void Begin(Vector3 center, float radius, Vector3 axis, int shovelLevel, int stratum, Action<Vector3, float, float> done)
    {
        active.Add(new Scoop { center = center, axis = axis.normalized, radius = radius, level = shovelLevel, stratum = stratum, done = done });
    }

    void Update()
    {
        if (active.Count == 0) return;

        bool settle = false;   // something finished moving this frame: colliders must catch up
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var s = active[i];
            s.time += Time.deltaTime;

            if (!s.grown)
            {
                float t = Mathf.Clamp01(s.time / growTime);
                float eased = 1f - (1f - t) * (1f - t) * (1f - t);
                s.removed += volume.Carve(s.center, s.radius * Mathf.Max(0.25f, eased), s.axis, stretch, s.level);
                if (t >= 1f)
                {
                    s.grown = true;
                    settle = true;
                    debris.Wake(s.center, s.radius * 1.6f);
                    s.done?.Invoke(s.center, s.radius, s.removed);
                    PlanCrumbles(s);
                }
                continue;
            }

            for (int k = s.rim.Count - 1; k >= 0; k--)
            {
                if (s.time < s.rim[k].w) continue;
                Vector3 p = s.rim[k];
                float r = UnityEngine.Random.Range(crumbleRadius.x, crumbleRadius.y);
                if (volume.Carve(p, r, Vector3.down, 1f, s.level) > 0f)
                {
                    debris.Crumble(p, s.stratum);
                    settle = true;
                }
                s.rim.RemoveAt(k);
            }
            if (s.rim.Count == 0) active.RemoveAt(i);
        }

        volume.Flush(settle);
    }

    // A few spots around the rim of the bite, mostly on the side facing the digger and
    // above, where loose earth would fall from.
    void PlanCrumbles(Scoop s)
    {
        s.rim = new List<Vector4>(crumbles);
        for (int k = 0; k < crumbles; k++)
        {
            Vector3 dir = Vector3.ProjectOnPlane(UnityEngine.Random.onUnitSphere, s.axis).normalized - s.axis * 0.25f + Vector3.up * 0.35f;
            Vector3 p = s.center + dir.normalized * (s.radius * UnityEngine.Random.Range(0.9f, 1.05f));
            s.rim.Add(new Vector4(p.x, p.y, p.z, growTime + UnityEngine.Random.Range(0.04f, crumbleWindow)));
        }
    }
}
