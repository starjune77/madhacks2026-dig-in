using UnityEngine;

// The first-person shovel. Lives in the Player prefab under the camera, with one child
// model per shovel level (assigned in `models`). DigPlayer drives the swing via Pose().
public class DigShovel : MonoBehaviour
{
    [Tooltip("One model per shovel level: Toy, Garden, Steel. Origin = grip, handle along +Z.")]
    public GameObject[] models;

    [Header("Swing (camera space)")]
    [Tooltip("Point in the swing (0..1) where the blade hits the ground.")]
    [Range(0f, 1f)] public float impactT = 0.42f;

    // Key poses: rest -> raised -> strike -> dig in -> scoop -> rest.
    // Rotation x = pitch (positive points the blade down).
    public float[] keyTime = { 0f, 0.30f, 0.42f, 0.50f, 0.75f, 1f };
    public Vector3[] keyPos =
    {
        new Vector3(0.30f, -0.30f, 0.40f),
        new Vector3(0.28f, -0.26f, 0.36f),
        new Vector3(0.20f, -0.28f, 0.55f),
        new Vector3(0.19f, -0.30f, 0.59f),
        new Vector3(0.26f, -0.30f, 0.48f),
        new Vector3(0.30f, -0.30f, 0.40f),
    };
    public Vector3[] keyRot =
    {
        new Vector3(-5f, -12f, 0f),
        new Vector3(-35f, -8f, 0f),
        new Vector3(22f, -10f, 0f),
        new Vector3(26f, -10f, 0f),
        new Vector3(-12f, -6f, 12f),
        new Vector3(-5f, -12f, 0f),
    };

    void Start()
    {
        Pose(-1f, 0f, 0f);
    }

    public void SetLevel(int level)
    {
        for (int i = 0; i < models.Length; i++)
            if (models[i] != null) models[i].SetActive(i == level);
    }

    // t: swing progress 0..1, or negative when idle. bob: walking sway.
    public void Pose(float t, float bobPhase, float bobAmount)
    {
        Vector3 pos = keyPos[0];
        Quaternion rot = Quaternion.Euler(keyRot[0]);

        if (t >= 0f)
        {
            t = Mathf.Clamp01(t);
            int seg = 0;
            while (seg < keyTime.Length - 2 && t > keyTime[seg + 1]) seg++;

            float u = Mathf.InverseLerp(keyTime[seg], keyTime[seg + 1], t);
            // The strike accelerates into the ground; everything else eases in and out.
            u = seg == 1 ? u * u : Mathf.SmoothStep(0f, 1f, u);

            pos = Vector3.Lerp(keyPos[seg], keyPos[seg + 1], u);
            rot = Quaternion.Slerp(Quaternion.Euler(keyRot[seg]), Quaternion.Euler(keyRot[seg + 1]), u);
        }

        pos += new Vector3(Mathf.Cos(bobPhase) * 0.012f, -Mathf.Abs(Mathf.Sin(bobPhase)) * 0.014f, 0f) * bobAmount;
        transform.localPosition = pos;
        transform.localRotation = rot;
    }
}
