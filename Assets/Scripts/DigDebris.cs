using UnityEngine;

// Flying dirt: a pool of small tumbling clods, simulated by hand so they need no
// colliders and no particle material. Created by DigGame.
public class DigDebris : MonoBehaviour
{
    const int PoolSize = 140;
    const float Gravity = 14f;

    Transform[] clods;
    Renderer[] renderers;
    Vector3[] velocity;
    Vector3[] spin;
    Vector3[] shape;
    float[] life;
    bool[] landed;
    int next;

    DigTerrain terrain;
    Material[] materials;   // one per stratum

    public void Build(DigTerrain terrain, Material[] materials, Mesh cubeMesh)
    {
        this.terrain = terrain;
        this.materials = materials;

        clods = new Transform[PoolSize];
        renderers = new Renderer[PoolSize];
        velocity = new Vector3[PoolSize];
        spin = new Vector3[PoolSize];
        shape = new Vector3[PoolSize];
        life = new float[PoolSize];
        landed = new bool[PoolSize];

        for (int i = 0; i < PoolSize; i++)
        {
            var go = new GameObject("Clod");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
            renderers[i] = go.AddComponent<MeshRenderer>();
            renderers[i].sharedMaterial = materials[0];
            go.SetActive(false);
            clods[i] = go.transform;
        }
    }

    // Throws `count` clods out of the ground at `point`. `toward` is a horizontal
    // direction (toward the player) that the spray leans into slightly.
    public void Burst(Vector3 point, Vector3 normal, Vector3 toward, int stratum, int count)
    {
        var mat = materials[Mathf.Clamp(stratum, 0, materials.Length - 1)];
        for (int n = 0; n < count; n++)
        {
            int i = next;
            next = (next + 1) % PoolSize;

            bool big = n % 4 == 0;   // a few larger clumps among the crumbs
            float size = big ? Random.Range(0.07f, 0.13f) : Random.Range(0.025f, 0.055f);
            shape[i] = new Vector3(size * Random.Range(0.7f, 1.3f), size * Random.Range(0.6f, 1f), size * Random.Range(0.7f, 1.3f));

            Vector3 dir = normal * 1.2f + Vector3.up * 1.6f + Random.insideUnitSphere * 1.3f + toward * 0.5f;
            velocity[i] = dir * (big ? Random.Range(0.9f, 1.5f) : Random.Range(1.2f, 2.3f));
            spin[i] = Random.insideUnitSphere * 600f;
            life[i] = Random.Range(0.9f, 1.5f);
            landed[i] = false;

            renderers[i].sharedMaterial = mat;
            clods[i].position = point + Random.insideUnitSphere * 0.15f + Vector3.up * 0.05f;
            clods[i].rotation = Random.rotation;
            clods[i].localScale = shape[i];
            clods[i].gameObject.SetActive(true);
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < PoolSize; i++)
        {
            if (life[i] <= 0f) continue;

            life[i] -= dt;
            if (life[i] <= 0f)
            {
                clods[i].gameObject.SetActive(false);
                continue;
            }

            if (!landed[i])
            {
                velocity[i].y -= Gravity * dt;
                Vector3 p = clods[i].position + velocity[i] * dt;

                // Inside the shaft the clods land on the dug surface; outside, on the grass.
                bool inShaft = p.x > 0f && p.x < DigTerrain.Size && p.z > 0f && p.z < DigTerrain.Size;
                if (!inShaft && p.y < 0f)
                {
                    p.x = Mathf.Clamp(p.x, 0.02f, DigTerrain.Size - 0.02f);
                    p.z = Mathf.Clamp(p.z, 0.02f, DigTerrain.Size - 0.02f);
                    inShaft = true;
                }
                float ground = (inShaft ? terrain.HeightAt(p.x, p.z) : 0.1f) + shape[i].y * 0.5f;

                if (p.y < ground)
                {
                    p.y = ground;
                    if (velocity[i].y < -2f)
                    {
                        velocity[i] = new Vector3(velocity[i].x * 0.4f, -velocity[i].y * 0.25f, velocity[i].z * 0.4f);
                        spin[i] *= 0.5f;
                    }
                    else
                    {
                        landed[i] = true;
                        life[i] = Mathf.Min(life[i], 0.7f);
                    }
                }
                clods[i].position = p;
                clods[i].Rotate(spin[i] * dt, Space.World);
            }

            // Shrink away at the end instead of popping out.
            clods[i].localScale = shape[i] * Mathf.Clamp01(life[i] / 0.25f);
        }
    }
}
