using UnityEngine;

// Game rules and state: scrap, shovel level, memory objects. Sits on the "Game" object
// in SampleScene. Everything it talks to is real scene content wired up in the
// inspector (DigIn/Build Scene does the first wiring); nothing is built here.
public class DigGame : MonoBehaviour
{
    public static DigGame I { get; private set; }

    // True while a card, letter or the ending is on screen.
    public bool InputLocked => story != null && story.Locked;

    public const int StratumCount = 3;
    public const float StratumDepth = 8f;                       // metres per stratum
    public const float TotalDepth = StratumCount * StratumDepth;

    [Header("Scene")]
    public DigTerrain terrain;
    public DigDebris debris;
    public DigPlayer player;
    public DigShovel shovel;
    public DigHud hud;
    public DigStory story;
    public Workbench workbench;
    public MemoryShelf shelf;
    public Transform spawnPoint;

    [Header("Memories (in shelf order)")]
    public MemoryData[] memories;
    public MemoryFind findPrefab;

    [Header("Shovels")]
    public string[] shovelNames = { "Toy Shovel", "Garden Shovel", "Steel Shovel" };
    public float[] shovelPower = { 1f, 2f, 4f };        // compared against stratum hardness
    public float[] shovelRadius = { 0.8f, 0.9f, 1.0f };
    public int[] upgradeCost = { 8, 20 };               // cost to reach level 1, level 2

    [Header("Scrap")]
    [Tooltip("Cubic metres of earth dug per scrap found.")]
    public float volumePerScrap = 0.6f;

    [Header("State")]
    public int scrap;
    public int shovelLevel;

    bool[] spawned;
    bool[] found;
    MemoryFind[] liveFinds;
    float scrapProgress;

    void Awake()
    {
        I = this;
        spawned = new bool[memories.Length];
        found = new bool[memories.Length];
        liveFinds = new MemoryFind[memories.Length];
    }

    void Start()
    {
        shovel.SetLevel(shovelLevel);
        ReturnToSurface();
    }

    // ---- Digging ------------------------------------------------------------
    // Called by the player when the shovel lands. Returns true if the shovel bounced
    // off ground that is too hard.
    public bool Strike(RaycastHit hit, Vector3 playerPosition)
    {
        if (hit.collider != terrain.Collider) return false;   // grass, walls, props

        Vector3 toward = playerPosition - hit.point;
        toward.y = 0f;
        toward.Normalize();

        float floor = -(shovelLevel + 1) * StratumDepth;
        var result = terrain.Dig(hit.point, shovelRadius[shovelLevel], shovelPower[shovelLevel], floor);

        AddScrap(result.volume);

        if (result.hitFloor)
        {
            if (shovelLevel < StratumCount - 1)
            {
                debris.Burst(hit.point, hit.normal, toward, shovelLevel + 1, 4);
                hud.ShowMessage("This ground is too hard for the " + shovelNames[shovelLevel] + ".");
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
        while (scrapProgress >= volumePerScrap)
        {
            scrapProgress -= volumePerScrap;
            scrap++;
            found = true;
        }
        if (found) hud.FlashScrap();
    }

    // ---- Memory objects -----------------------------------------------------
    // Each memory turns up wherever you are digging when you first reach its depth,
    // so the demo is reproducible but nothing can be missed.
    void CheckFinds(Vector3 digPoint)
    {
        float depth = -terrain.HeightAt(digPoint.x, digPoint.z);
        for (int i = 0; i < memories.Length; i++)
        {
            if (spawned[i] || memories[i] == null || depth < memories[i].depth) continue;

            spawned[i] = true;
            const float margin = 0.6f;
            var pos = new Vector3(
                Mathf.Clamp(digPoint.x, margin, DigTerrain.Size - margin), 0f,
                Mathf.Clamp(digPoint.z, margin, DigTerrain.Size - margin));
            pos.y = terrain.HeightAt(pos.x, pos.z);

            liveFinds[i] = Instantiate(findPrefab, pos, Quaternion.identity);
            liveFinds[i].Init(memories[i], terrain);

            debris.Burst(pos, Vector3.up, Vector3.zero, DigTerrain.StratumAt(-pos.y), 12);
            hud.ShowMessage("Something is buried here.", 4f);
            break;   // one discovery per stroke
        }
    }

    // True when all memories of a stratum have been picked up.
    public bool StratumComplete(int stratum)
    {
        bool any = false;
        for (int i = 0; i < memories.Length; i++)
        {
            if (memories[i] == null || memories[i].stratum != stratum) continue;
            if (!found[i]) return false;
            any = true;
        }
        return any;
    }

    // Picks up a found object if the ray points at one. Returns true if it did.
    public bool TryPickUp(Ray ray, float reach)
    {
        if (!Physics.Raycast(ray, out RaycastHit hit, reach + 0.3f, ~0, QueryTriggerInteraction.Collide)) return false;
        var find = hit.collider.GetComponentInParent<MemoryFind>();
        if (find == null) return false;

        int index = System.Array.IndexOf(memories, find.Data);
        Destroy(find.gameObject);
        if (index < 0) return true;

        liveFinds[index] = null;
        found[index] = true;
        shelf.Fill(index, find.Data);
        story.ShowCard(find.Data);
        return true;
    }

    // ---- Surface ------------------------------------------------------------
    // E key. Only does something at the workbench.
    public void Interact()
    {
        if (workbench == null || !workbench.InRange(player.transform.position)) return;
        if (shovelLevel >= shovelNames.Length - 1) return;

        int cost = upgradeCost[shovelLevel];
        if (scrap < cost)
        {
            hud.ShowMessage("Not enough scrap yet.", 2f);
            return;
        }

        scrap -= cost;
        shovelLevel++;
        shovel.SetLevel(shovelLevel);
        hud.ShowMessage("You now have the " + shovelNames[shovelLevel] + ".");
    }

    string WorkbenchPrompt()
    {
        if (workbench == null || !workbench.InRange(player.transform.position)) return null;
        if (shovelLevel >= shovelNames.Length - 1) return "This is the best shovel you have.";

        int cost = upgradeCost[shovelLevel];
        string next = shovelNames[shovelLevel + 1];
        return scrap >= cost
            ? "Press <b>E</b> to make a " + next + "  <alpha=#99>(" + cost + " scrap)"
            : "A " + next + " needs " + cost + " scrap  <alpha=#99>(you have " + scrap + ")";
    }

    public void ReturnToSurface()
    {
        player.Teleport(spawnPoint.position);
        player.transform.rotation = Quaternion.Euler(0f, spawnPoint.eulerAngles.y, 0f);
    }

    void Update()
    {
        float depth = Mathf.Max(0f, -(player.transform.position.y - 0.98f));
        int cost = shovelLevel < upgradeCost.Length ? upgradeCost[shovelLevel] : -1;
        hud.SetStatus(depth, shovelNames[shovelLevel], scrap, cost);
        hud.SetPrompt(WorkbenchPrompt());
    }
}
