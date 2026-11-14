using UnityEngine;

// Put this on one empty GameObject in the scene. It builds the yard, the shaft
// and the player at runtime, and holds the game state (scrap, shovel level).
public class DigGame : MonoBehaviour
{
    public static DigGame I { get; private set; }

    // ---- Tuning -------------------------------------------------------------
    const int ShaftSize = 5;            // 5 x 5 cells
    const int LayersPerStratum = 8;     // 3 strata -> 24 layers deep
    const int StratumCount = 3;
    const float ScrapChance = 0.35f;

    static readonly string[] StratumNames = { "Childhood", "School Days", "Growing Up" };
    static readonly float[] BlockHp = { 3f, 9f, 24f };
    static readonly Color[] BlockColors =
    {
        new Color(0.55f, 0.38f, 0.24f),   // dirt
        new Color(0.70f, 0.42f, 0.28f),   // clay
        new Color(0.45f, 0.45f, 0.48f),   // stone
    };

    static readonly string[] ShovelNames = { "Toy Shovel", "Garden Shovel", "Steel Shovel" };
    static readonly float[] ShovelDamage = { 1f, 3f, 8f };
    static readonly int[] UpgradeCost = { 8, 20 };   // cost to reach level 1, level 2

    static readonly Vector3 SpawnPoint = new Vector3(2.5f, 1.1f, -3f);

    // ---- State --------------------------------------------------------------
    public int scrap;
    public int shovelLevel;

    DigPlayer player;
    Material baseMaterial;
    string message = "";
    float messageTimer;

    void Awake()
    {
        I = this;
        BuildYard();
        BuildShaft();
        BuildPlayer();
    }

    // ---- World --------------------------------------------------------------
    Material MakeMaterial(Color color)
    {
        if (baseMaterial == null)
        {
            // Borrow the default lit material from a primitive so this also works in builds.
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseMaterial = temp.GetComponent<Renderer>().sharedMaterial;
            Destroy(temp);
        }
        var mat = new Material(baseMaterial);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        mat.color = color;
        return mat;
    }

    GameObject MakeBox(string name, Vector3 center, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(transform);
        go.transform.position = center;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    void BuildYard()
    {
        float s = ShaftSize;
        float totalDepth = LayersPerStratum * StratumCount;
        var earth = MakeMaterial(new Color(0.36f, 0.26f, 0.18f));
        var grass = MakeMaterial(new Color(0.35f, 0.55f, 0.30f));

        // Four big earth boxes around the shaft: they are the pit walls.
        const float w = 40f;      // how far the yard extends
        const float h = 60f;      // how deep the walls go
        MakeBox("Earth West",  new Vector3(-w / 2f,     -h / 2f, s / 2f),      new Vector3(w, h, s + 2f * w), earth);
        MakeBox("Earth East",  new Vector3(s + w / 2f,  -h / 2f, s / 2f),      new Vector3(w, h, s + 2f * w), earth);
        MakeBox("Earth South", new Vector3(s / 2f,      -h / 2f, -w / 2f),     new Vector3(s, h, w),          earth);
        MakeBox("Earth North", new Vector3(s / 2f,      -h / 2f, s + w / 2f),  new Vector3(s, h, w),          earth);

        // Thin grass slabs on top so the surface reads as a yard.
        const float g = 0.1f;
        MakeBox("Grass West",  new Vector3(-w / 2f,     g / 2f, s / 2f),      new Vector3(w, g, s + 2f * w), grass);
        MakeBox("Grass East",  new Vector3(s + w / 2f,  g / 2f, s / 2f),      new Vector3(w, g, s + 2f * w), grass);
        MakeBox("Grass South", new Vector3(s / 2f,      g / 2f, -w / 2f),     new Vector3(s, g, w),          grass);
        MakeBox("Grass North", new Vector3(s / 2f,      g / 2f, s + w / 2f),  new Vector3(s, g, w),          grass);

        // The floor under the last layer: "today". Not diggable yet.
        var floor = MakeMaterial(new Color(0.92f, 0.90f, 0.84f));
        MakeBox("Today Floor", new Vector3(s / 2f, -totalDepth - 0.5f, s / 2f), new Vector3(s, 1f, s), floor);
    }

    void BuildShaft()
    {
        var mats = new Material[StratumCount];
        for (int i = 0; i < StratumCount; i++) mats[i] = MakeMaterial(BlockColors[i]);

        var parent = new GameObject("Shaft").transform;
        parent.SetParent(transform);

        int totalLayers = LayersPerStratum * StratumCount;
        for (int layer = 0; layer < totalLayers; layer++)
        {
            int stratum = layer / LayersPerStratum;
            for (int x = 0; x < ShaftSize; x++)
            {
                for (int z = 0; z < ShaftSize; z++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "Block " + layer + " (" + x + "," + z + ")";
                    go.transform.SetParent(parent);
                    go.transform.position = new Vector3(x + 0.5f, -layer - 0.5f, z + 0.5f);
                    go.GetComponent<Renderer>().sharedMaterial = mats[stratum];
                    go.AddComponent<DigBlock>().Init(layer, stratum, BlockHp[stratum]);
                }
            }
        }
    }

    void BuildPlayer()
    {
        var go = new GameObject("Player");
        go.transform.position = SpawnPoint;

        var cc = go.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.stepOffset = 0.3f;

        // Reuse the scene's Main Camera so its URP settings are kept.
        Camera cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }
        cam.transform.SetParent(go.transform);
        cam.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        cam.transform.localRotation = Quaternion.identity;
        cam.nearClipPlane = 0.05f;

        player = go.AddComponent<DigPlayer>();
        player.cam = cam.transform;
    }

    // ---- Rules --------------------------------------------------------------
    public void HitBlock(DigBlock block)
    {
        if (block.stratum > shovelLevel)
        {
            Say("This ground is too hard for the " + ShovelNames[shovelLevel] + ".");
            return;
        }

        if (block.Hit(ShovelDamage[shovelLevel]))
        {
            Destroy(block.gameObject);
            if (Random.value < ScrapChance)
            {
                scrap++;
            }
        }
    }

    public void TryUpgrade()
    {
        if (shovelLevel >= ShovelNames.Length - 1)
        {
            Say("This is the best shovel you have.");
            return;
        }

        int cost = UpgradeCost[shovelLevel];
        if (scrap < cost)
        {
            Say("Need " + cost + " scrap to upgrade (you have " + scrap + ").");
            return;
        }

        scrap -= cost;
        shovelLevel++;
        Say("You now have the " + ShovelNames[shovelLevel] + ".");
    }

    public void ReturnToSurface()
    {
        player.Teleport(SpawnPoint);
    }

    public void Say(string text)
    {
        message = text;
        messageTimer = 3f;
    }

    void Update()
    {
        if (messageTimer > 0f) messageTimer -= Time.deltaTime;
    }

    // ---- Temporary HUD (replace with real UI later) --------------------------
    void OnGUI()
    {
        var label = new GUIStyle(GUI.skin.label) { fontSize = 22 };
        label.normal.textColor = Color.white;

        float depth = Mathf.Max(0f, -(player.transform.position.y - 0.9f));
        int stratum = Mathf.Clamp((int)(depth / LayersPerStratum), 0, StratumCount - 1);

        GUI.Label(new Rect(20, 15, 600, 30), "Depth: " + depth.ToString("0.0") + " m   (" + StratumNames[stratum] + ")", label);
        GUI.Label(new Rect(20, 45, 600, 30), "Shovel: " + ShovelNames[shovelLevel], label);

        string scrapLine = "Scrap: " + scrap;
        if (shovelLevel < UpgradeCost.Length) scrapLine += " / " + UpgradeCost[shovelLevel] + "   [E] upgrade";
        GUI.Label(new Rect(20, 75, 600, 30), scrapLine, label);

        var hint = new GUIStyle(label) { fontSize = 16 };
        GUI.Label(new Rect(20, Screen.height - 35, 900, 30),
            "WASD move   Space jump   Hold left mouse to dig   R back to surface   Esc free cursor", hint);

        // Crosshair
        var center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(Screen.width / 2f - 20, Screen.height / 2f - 20, 40, 40), "+", center);

        if (messageTimer > 0f)
        {
            GUI.Label(new Rect(0, Screen.height * 0.7f, Screen.width, 40), message, center);
        }
    }
}
