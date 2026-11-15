using UnityEngine;

// A memory object lying in the dirt, waiting to be picked up. Spawned from the
// MemoryFind prefab by DigGame when the player first digs down to its depth.
public class MemoryFind : MonoBehaviour
{
    [Tooltip("The visible model. It pops in and bobs; the root stays put for the collider.")]
    public Transform model;
    public float size = 0.24f;
    public float hover = 0.10f;

    public MemoryData Data { get; private set; }

    Vector3 modelScale;
    float age;
    float restY;
    float nextCheck;

    public void Init(MemoryData data)
    {
        Data = data;
        name = "Find: " + data.title;
        restY = transform.position.y;
    }

    void Awake()
    {
        modelScale = model.localScale;
        model.localScale = Vector3.zero;
    }

    void Update()
    {
        age += Time.deltaTime;

        // Pops out of the dirt with a little overshoot, then rests on the dug surface.
        float t = Mathf.Clamp01(age / 0.4f);
        float pop = 1f + 0.35f * Mathf.Sin(t * Mathf.PI) * (1f - t);
        model.localScale = modelScale * (size * Mathf.SmoothStep(0f, 1f, t) * pop);
        model.localRotation = Quaternion.Euler(0f, age * 25f, 0f);

        // Rests on whatever ground is under it, and settles if that is dug away.
        Vector3 p = transform.position;
        if (age >= nextCheck)
        {
            nextCheck = age + 0.25f;
            if (Physics.Raycast(new Vector3(p.x, restY + 0.4f, p.z), Vector3.down, out RaycastHit hit, 3f, DigGame.AimMask, QueryTriggerInteraction.Ignore))
                restY = hit.point.y;
        }
        p.y = Mathf.MoveTowards(p.y, restY + hover + Mathf.Sin(age * 2.5f) * 0.02f, 3f * Time.deltaTime);
        transform.position = p;
    }
}
