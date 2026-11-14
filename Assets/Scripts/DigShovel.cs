using UnityEngine;

// The shovel you see in first person, with a code-driven swing. Created by DigGame
// as a child of the camera. DigPlayer drives it through Pose().
public class DigShovel : MonoBehaviour
{
    // Point in the swing (0..1) where the blade hits the ground.
    public const float ImpactT = 0.42f;

    // Key poses in camera space: rest -> raised -> strike -> dig in -> scoop -> rest.
    static readonly float[] KeyTime = { 0f, 0.30f, ImpactT, 0.50f, 0.75f, 1f };
    static readonly Vector3[] KeyPos =
    {
        new Vector3(0.30f, -0.30f, 0.40f),
        new Vector3(0.28f, -0.26f, 0.36f),
        new Vector3(0.20f, -0.28f, 0.55f),
        new Vector3(0.19f, -0.30f, 0.59f),
        new Vector3(0.26f, -0.30f, 0.48f),
        new Vector3(0.30f, -0.30f, 0.40f),
    };
    static readonly Vector3[] KeyRot =   // x = pitch (positive points the blade down)
    {
        new Vector3(-5f, -12f, 0f),
        new Vector3(-35f, -8f, 0f),
        new Vector3(22f, -10f, 0f),
        new Vector3(26f, -10f, 0f),
        new Vector3(-12f, -6f, 12f),
        new Vector3(-5f, -12f, 0f),
    };

    GameObject[] models;

    public void Build(DigGame game)
    {
        models = new GameObject[3];
        //                       handle length, thickness, blade width, blade length, handle colour, blade colour
        models[0] = BuildModel(game, "Toy Shovel", 0.42f, 0.030f, 0.13f, 0.15f,
            new Color(0.95f, 0.78f, 0.20f), new Color(0.86f, 0.22f, 0.20f), 0.5f);
        models[1] = BuildModel(game, "Garden Shovel", 0.60f, 0.032f, 0.16f, 0.20f,
            new Color(0.58f, 0.40f, 0.22f), new Color(0.22f, 0.36f, 0.27f), 0.4f);
        models[2] = BuildModel(game, "Steel Shovel", 0.70f, 0.034f, 0.20f, 0.26f,
            new Color(0.42f, 0.28f, 0.16f), new Color(0.74f, 0.76f, 0.80f), 0.8f);
        SetLevel(0);
        Pose(-1f, 0f, 0f);
    }

    // The model's origin is the grip; the handle runs along +Z to the blade.
    GameObject BuildModel(DigGame game, string name, float length, float thickness,
                          float bladeWidth, float bladeLength, Color handleColor, Color bladeColor, float bladeSmoothness)
    {
        var root = new GameObject(name);
        root.transform.SetParent(transform, false);

        var handleMat = game.MakeMaterial(handleColor, 0.3f);
        var bladeMat = game.MakeMaterial(bladeColor, bladeSmoothness);

        game.MakePart(PrimitiveType.Cylinder, "Handle", root.transform,
            new Vector3(0f, 0f, length * 0.5f), new Vector3(90f, 0f, 0f),
            new Vector3(thickness, length * 0.5f, thickness), handleMat);
        game.MakePart(PrimitiveType.Cube, "Grip", root.transform,
            new Vector3(0f, 0f, -0.01f), Vector3.zero,
            new Vector3(0.09f, thickness, thickness), bladeMat);
        game.MakePart(PrimitiveType.Cube, "Blade", root.transform,
            new Vector3(0f, -0.012f, length + bladeLength * 0.5f - 0.02f), new Vector3(8f, 0f, 0f),
            new Vector3(bladeWidth, 0.012f, bladeLength), bladeMat);

        foreach (var r in root.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return root;
    }

    public void SetLevel(int level)
    {
        for (int i = 0; i < models.Length; i++) models[i].SetActive(i == level);
    }

    // t: swing progress 0..1, or negative when idle. bob: walking sway.
    public void Pose(float t, float bobPhase, float bobAmount)
    {
        Vector3 pos = KeyPos[0];
        Quaternion rot = Quaternion.Euler(KeyRot[0]);

        if (t >= 0f)
        {
            t = Mathf.Clamp01(t);
            int seg = 0;
            while (seg < KeyTime.Length - 2 && t > KeyTime[seg + 1]) seg++;

            float u = Mathf.InverseLerp(KeyTime[seg], KeyTime[seg + 1], t);
            // The strike accelerates into the ground; everything else eases in and out.
            u = seg == 1 ? u * u : Mathf.SmoothStep(0f, 1f, u);

            pos = Vector3.Lerp(KeyPos[seg], KeyPos[seg + 1], u);
            rot = Quaternion.Slerp(Quaternion.Euler(KeyRot[seg]), Quaternion.Euler(KeyRot[seg + 1]), u);
        }

        pos += new Vector3(Mathf.Cos(bobPhase) * 0.012f, -Mathf.Abs(Mathf.Sin(bobPhase)) * 0.014f, 0f) * bobAmount;
        transform.localPosition = pos;
        transform.localRotation = rot;
    }
}
