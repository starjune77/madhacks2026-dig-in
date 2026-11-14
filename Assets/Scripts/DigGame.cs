using UnityEngine;

// Put this on one empty GameObject in the scene. It builds the yard, the shaft
// and the player at runtime, and holds the game state (scrap, shovel level).
public class DigGame : MonoBehaviour
{
    public static DigGame I { get; private set; }

    // ---- Tuning -------------------------------------------------------------
    public const int StratumCount = 3;
    public const float StratumDepth = 8f;                       // metres per stratum
    public const float TotalDepth = StratumCount * StratumDepth;

    const float VolumePerScrap = 0.6f;   // cubic metres of earth dug per scrap found

    static readonly string[] StratumNames = { "Childhood", "School Days", "Growing Up" };
    static readonly Color[] StrataColors =
    {
        new Color(0.55f, 0.38f, 0.24f),   // dirt
        new Color(0.70f, 0.42f, 0.28f),   // clay
        new Color(0.45f, 0.45f, 0.48f),   // stone
        new Color(0.92f, 0.90f, 0.84f),   // today (the very bottom)
    };

    static readonly string[] ShovelNames = { "Toy Shovel", "Garden Shovel", "Steel Shovel" };
    static readonly float[] ShovelPower = { 1f, 2f, 4f };       // compared against stratum hardness
    static readonly float[] ShovelRadius = { 0.8f, 0.9f, 1.0f };
    static readonly int[] UpgradeCost = { 8, 20 };   // cost to reach level 1, level 2

    static readonly Vector3 SpawnPoint = new Vector3(2.5f, 1.1f, -3f);

    // Memory objects. Each one turns up wherever you are digging when you first
    // reach its depth, so the demo is reproducible but nothing can be missed.
    class Find
    {
        public string name, line;
        public float depth;
        public Transform obj;      // placeholder visual while it lies in the dirt
        public float age;
        public bool spawned, taken;
    }

    readonly Find[] finds =
    {
        new Find { depth = 2.5f, name = "Bicycle Bell", line = "Someone was holding the back of the seat. I didn't notice when they let go." },
        new Find { depth = 5.0f, name = "Crayon",       line = "I drew a house with four windows. The real house only had two." },
        new Find { depth = 7.2f, name = "Toy Car",      line = "One wheel was missing, but I still raced it across the kitchen floor." },
    };

    // ---- State --------------------------------------------------------------
    public int scrap;
    public int shovelLevel;

    DigPlayer player;
    DigTerrain terrain;
    DigDebris debris;
    DigShovel shovel;
    Material baseMaterial;
    Mesh cubeMesh;
    Material findMaterial;
    float scrapProgress;
    float scrapFlash;
    string message = "";
    float messageTimer;

    void Awake()
    {
        I = this;
        BuildYard();
        BuildTerrain();
        BuildPlayer();
    }

    // ---- World --------------------------------------------------------------
    public Material MakeMaterial(Color color, float smoothness = 0.1f)
    {
        if (baseMaterial == null)
        {
            // Borrow the default lit material from a primitive so this also works in builds.
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseMaterial = temp.GetComponent<Renderer>().sharedMaterial;
            cubeMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(temp);
        }
        var mat = new Material(baseMaterial);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        mat.color = color;
        return mat;
    }

    // A primitive used only for looks (no collider), e.g. shovel parts.
    public GameObject MakePart(PrimitiveType type, string name, Transform parent,
                               Vector3 localPos, Vector3 localEuler, Vector3 localScale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        DestroyImmediate(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localEulerAngles = localEuler;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
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
        float s = DigTerrain.Size;
        var earth = MakeMaterial(new Color(0.36f, 0.26f, 0.18f));
        var grass = MakeMaterial(new Color(0.35f, 0.55f, 0.30f));

        // Four big earth boxes around the shaft: they are the pit walls. They only
        // collide and cast shadows; the visible, layered walls are drawn by DigTerrain.
        const float w = 40f;      // how far the yard extends
        const float h = 60f;      // how deep the walls go
        var boxes = new[]
        {
            MakeBox("Earth West",  new Vector3(-w / 2f,     -h / 2f, s / 2f),      new Vector3(w, h, s + 2f * w), earth),
            MakeBox("Earth East",  new Vector3(s + w / 2f,  -h / 2f, s / 2f),      new Vector3(w, h, s + 2f * w), earth),
            MakeBox("Earth South", new Vector3(s / 2f,      -h / 2f, -w / 2f),     new Vector3(s, h, w),          earth),
            MakeBox("Earth North", new Vector3(s / 2f,      -h / 2f, s + w / 2f),  new Vector3(s, h, w),          earth),
        };
        foreach (var box in boxes)
            box.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

        // Thin grass slabs on top so the surface reads as a yard.
        const float g = 0.1f;
        MakeBox("Grass West",  new Vector3(-w / 2f,     g / 2f, s / 2f),      new Vector3(w, g, s + 2f * w), grass);
        MakeBox("Grass East",  new Vector3(s + w / 2f,  g / 2f, s / 2f),      new Vector3(w, g, s + 2f * w), grass);
        MakeBox("Grass South", new Vector3(s / 2f,      g / 2f, -w / 2f),     new Vector3(s, g, w),          grass);
        MakeBox("Grass North", new Vector3(s / 2f,      g / 2f, s + w / 2f),  new Vector3(s, g, w),          grass);

        // Safety floor just under the deepest the terrain can go.
        MakeBox("Safety Floor", new Vector3(s / 2f, -TotalDepth - 0.6f, s / 2f), new Vector3(s, 1f, s), earth);
    }

    void BuildTerrain()
    {
        var strata = MakeMaterial(Color.white, 0.05f);
        var tex = DigTerrain.MakeStrataTexture(StrataColors);
        if (strata.HasProperty("_BaseMap")) strata.SetTexture("_BaseMap", tex);
        strata.mainTexture = tex;

        var go = new GameObject("Terrain");
        go.transform.SetParent(transform, false);
        terrain = go.AddComponent<DigTerrain>();
        terrain.Build(strata);

        var clodMaterials = new Material[StratumCount];
        for (int i = 0; i < StratumCount; i++) clodMaterials[i] = MakeMaterial(StrataColors[i] * 0.9f, 0.05f);

        var debrisGo = new GameObject("Debris");
        debrisGo.transform.SetParent(transform, false);
        debris = debrisGo.AddComponent<DigDebris>();
        debris.Build(terrain, clodMaterials, cubeMesh);

        findMaterial = MakeMaterial(new Color(0.98f, 0.80f, 0.35f), 0.6f);
    }

    void BuildPlayer()
    {
        var go = new GameObject("Player");
        go.transform.position = SpawnPoint;

        var cc = go.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.stepOffset = 0.3f;
        cc.slopeLimit = 50f;

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
        cam.transform.localPosition = new Vector3(0f, DigPlayer.EyeHeight, 0f);
        cam.transform.localRotation = Quaternion.identity;
        cam.nearClipPlane = 0.05f;


        var shovelGo = new GameObject("Shovel");
        shovelGo.transform.SetParent(cam.transform, false);
        shovel = shovelGo.AddComponent<DigShovel>();
        shovel.Build(this);

        player = go.AddComponent<DigPlayer>();
        player.cam = cam.transform;
        player.shovel = shovel;
    }

    // ---- Rules --------------------------------------------------------------
    // Called by the player when the shovel lands. Returns true if the shovel bounced
    // off ground that is too hard.
    public bool Strike(RaycastHit hit, Vector3 playerPosition)
    {
        if (hit.collider != terrain.Collider)
        {
            // Grass or a shaft wall: not diggable.
            return false;
        }

        Vector3 toward = playerPosition - hit.point;
        toward.y = 0f;
        toward.Normalize();

        float floor = -(shovelLevel + 1) * StratumDepth;
        var result = terrain.Dig(hit.point, ShovelRadius[shovelLevel], ShovelPower[shovelLevel], floor);

        AddScrap(result.volume);

        if (result.hitFloor)
        {
            if (shovelLevel < StratumCount - 1)
            {
                debris.Burst(hit.point, hit.normal, toward, shovelLevel + 1, 4);
                Say("This ground is too hard for the " + ShovelNames[shovelLevel] + ".");
                return true;
            }
            // The very bottom ("today"). The ending will hook in here.
            return false;
        }

        debris.Burst(hit.point, hit.normal, toward, result.stratum, 16);
        CheckFinds(hit.point);
        return false;
    }

    void AddScrap(float volume)
    {
        scrapProgress += volume;
        bool found = false;
        while (scrapProgress >= VolumePerScrap)
        {
            scrapProgress -= VolumePerScrap;
            scrap++;
            found = true;
        }
        if (found)
        {
            scrapFlash = 0.5f;
        }
    }

    // ---- Memory objects ---------------------------------------------------------
    void CheckFinds(Vector3 digPoint)
    {
        float depth = -terrain.HeightAt(digPoint.x, digPoint.z);
        foreach (var find in finds)
        {
            if (find.spawned || depth < find.depth) continue;

            find.spawned = true;
            float margin = 0.6f;
            var pos = new Vector3(
                Mathf.Clamp(digPoint.x, margin, DigTerrain.Size - margin), 0f,
                Mathf.Clamp(digPoint.z, margin, DigTerrain.Size - margin));
            find.obj = MakePart(PrimitiveType.Sphere, find.name, transform, pos, Vector3.zero, Vector3.zero, findMaterial).transform;

            debris.Burst(pos + Vector3.up * terrain.HeightAt(pos.x, pos.z), Vector3.up, Vector3.zero, TerrainStratum(pos), 12);
            Say("Something is buried here. Click it to pick it up.", 5f);
            break;   // one discovery per stroke
        }
    }

    int TerrainStratum(Vector3 pos)
    {
        return DigTerrain.StratumAt(-terrain.HeightAt(pos.x, pos.z));
    }

    void UpdateFinds()
    {
        foreach (var find in finds)
        {
            if (find.obj == null) continue;
            find.age += Time.deltaTime;

            // Pops out of the dirt with a little overshoot, then rests on the dug surface.
            float t = Mathf.Clamp01(find.age / 0.4f);
            float pop = 1f + 0.35f * Mathf.Sin(t * Mathf.PI) * (1f - t);
            float size = 0.24f * Mathf.SmoothStep(0f, 1f, t) * pop;
            Vector3 p = find.obj.position;
            p.y = terrain.HeightAt(p.x, p.z) + 0.10f + Mathf.Sin(find.age * 2.5f) * 0.02f;
            find.obj.position = p;
            find.obj.localScale = Vector3.one * size;
        }
    }

    // Picks up a found object if the ray points at one. Returns true if it did.
    public bool TryPickUp(Ray ray, float reach)
    {
        foreach (var find in finds)
        {
            if (find.obj == null) continue;

            Vector3 to = find.obj.position - ray.origin;
            float along = Vector3.Dot(to, ray.direction);
            if (along < 0f || along > reach + 0.3f) continue;
            if ((to - ray.direction * along).sqrMagnitude > 0.35f * 0.35f) continue;

            Destroy(find.obj.gameObject);
            find.obj = null;
            find.taken = true;
            // Placeholder until the illustrated memory card exists.
            Say(find.name + "\n\"" + find.line + "\"", 8f);
            return true;
        }
        return false;
    }

    // ---- Shovel, travel, messages -----------------------------------------------
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
        shovel.SetLevel(shovelLevel);
        Say("You now have the " + ShovelNames[shovelLevel] + ".");
    }

    public void ReturnToSurface()
    {
        player.Teleport(SpawnPoint);
    }

    public void Say(string text, float seconds = 3f)
    {
        message = text;
        messageTimer = seconds;
    }

    void Update()
    {
        if (messageTimer > 0f) messageTimer -= Time.deltaTime;
        if (scrapFlash > 0f) scrapFlash -= Time.deltaTime;
        UpdateFinds();
    }

    // ---- Temporary HUD (replace with real UI later) --------------------------
    void OnGUI()
    {
        var label = new GUIStyle(GUI.skin.label) { fontSize = 22 };
        label.normal.textColor = Color.white;

        float depth = Mathf.Max(0f, -(player.transform.position.y - 0.98f));
        int stratum = Mathf.Clamp((int)(depth / StratumDepth), 0, StratumCount - 1);

        GUI.Label(new Rect(20, 15, 600, 30), "Depth: " + depth.ToString("0.0") + " m   (" + StratumNames[stratum] + ")", label);
        GUI.Label(new Rect(20, 45, 600, 30), "Shovel: " + ShovelNames[shovelLevel], label);

        string scrapLine = "Scrap: " + scrap;
        if (shovelLevel < UpgradeCost.Length) scrapLine += " / " + UpgradeCost[shovelLevel] + "   [E] upgrade";
        var scrapStyle = new GUIStyle(label);
        if (scrapFlash > 0f) scrapStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
        GUI.Label(new Rect(20, 75, 600, 30), scrapLine, scrapStyle);

        var hint = new GUIStyle(label) { fontSize = 16 };
        GUI.Label(new Rect(20, Screen.height - 35, 900, 30),
            "WASD move   Space jump   Hold left mouse to dig   R back to surface   Esc free cursor", hint);

        // Crosshair
        var center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(Screen.width / 2f - 20, Screen.height / 2f - 20, 40, 40), "+", center);

        if (messageTimer > 0f)
        {
            var text = new GUIStyle(label) { alignment = TextAnchor.UpperCenter, wordWrap = true };
            var rect = new Rect(Screen.width * 0.15f, Screen.height * 0.68f, Screen.width * 0.7f, 140f);
            text.normal.textColor = Color.black;
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), message, text);
            text.normal.textColor = Color.white;
            GUI.Label(rect, message, text);
        }
    }
}
