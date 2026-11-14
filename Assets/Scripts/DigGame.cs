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
    [Tooltip("Where the ending leaves the player: in front of the shelf.")]
    public Transform endingView;

    [Header("Memories (in shelf order)")]
    public MemoryData[] memories;
    public MemoryFind findPrefab;
    [Tooltip("Optional find prefab per stratum; empty entries use findPrefab.")]
    public MemoryFind[] findPrefabsByStratum;

    [Header("Ending")]
    public MemoryData blankPage;
    public MemoryFind blankPagePrefab;
    [Tooltip("Depth at which the blank page turns up, once every memory has.")]
    public float blankPageDepth = 23.6f;

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
    bool pageSpawned;
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
            // The very bottom ("today"): nothing more to dig, but the blank page may still
            // be waiting to turn up.
            CheckFinds(hit.point);
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

            int s = memories[i].stratum;
            var prefab = memories[i].findPrefab != null ? memories[i].findPrefab
                : findPrefabsByStratum != null && s < findPrefabsByStratum.Length && findPrefabsByStratum[s] != null
                ? findPrefabsByStratum[s] : findPrefab;
            liveFinds[i] = Instantiate(prefab, pos, Quaternion.identity);
            liveFinds[i].Init(memories[i], terrain);

            debris.Burst(pos, Vector3.up, Vector3.zero, DigTerrain.StratumAt(-pos.y), 12);
            hud.ShowMessage("Something is buried here.", 4f);
            return;   // one discovery per stroke
        }

        // At the very bottom, after everything else, a blank page.
        if (!pageSpawned && blankPagePrefab != null && depth >= blankPageDepth && System.Array.TrueForAll(spawned, x => x))
            SpawnBlankPage(digPoint);
    }

    void SpawnBlankPage(Vector3 near)
    {
        pageSpawned = true;
        const float margin = 0.6f;
        var pos = new Vector3(
            Mathf.Clamp(near.x, margin, DigTerrain.Size - margin), 0f,
            Mathf.Clamp(near.z, margin, DigTerrain.Size - margin));
        pos.y = terrain.HeightAt(pos.x, pos.z);
        var page = Instantiate(blankPagePrefab, pos, Quaternion.identity);
        page.Init(blankPage, terrain);
        debris.Burst(pos, Vector3.up, Vector3.zero, 2, 10);
    }

    // ---- Demo mode (DigDemo) --------------------------------------------------
    static readonly string[] StratumNames = { "Childhood", "School Days", "Growing Up" };

    // Start of a stratum with the shovel for it. Memories above that depth are skipped.
    public void DemoJump(int stratum)
    {
        SetShovel(stratum);
        float depth = stratum * StratumDepth;
        SkipMemoriesAbove(depth);
        if (stratum == 0)
        {
            ReturnToSurface();
        }
        else
        {
            terrain.LowerTo(-depth);
            player.Teleport(new Vector3(DigTerrain.Size * 0.5f, -depth + 1f, 1.2f));
            player.SetLook(0f, 30f);
        }
        hud.ShowMessage(StratumNames[stratum], 2f);
    }

    // The bottom of the shaft with the blank page in front of you.
    public void DemoEnding()
    {
        SetShovel(StratumCount - 1);
        SkipMemoriesAbove(TotalDepth + 1f);
        float floor = -(blankPageDepth + 0.1f);
        terrain.LowerTo(floor);
        player.Teleport(new Vector3(DigTerrain.Size * 0.5f, floor + 1f, 1.2f));
        player.SetLook(0f, 40f);
        pageSpawned = false;
        SpawnBlankPage(new Vector3(DigTerrain.Size * 0.5f, 0f, 2.9f));
    }

    void SetShovel(int level)
    {
        shovelLevel = Mathf.Clamp(level, 0, shovelNames.Length - 1);
        shovel.SetLevel(shovelLevel);
    }

    void SkipMemoriesAbove(float depth)
    {
        for (int i = 0; i < memories.Length; i++)
        {
            if (memories[i] == null || memories[i].depth >= depth) continue;
            spawned[i] = true;
            if (liveFinds[i] != null) { Destroy(liveFinds[i].gameObject); liveFinds[i] = null; }
        }
    }

    // Called by DigStory while the screen is black: everything ends up on the shelf and
    // the player is back at the surface, looking at it.
    public void FinishEnding()
    {
        for (int i = 0; i < memories.Length; i++)
        {
            if (memories[i] == null) continue;
            found[i] = true;
            spawned[i] = true;
            shelf.Fill(i, memories[i]);
            if (liveFinds[i] != null) { Destroy(liveFinds[i].gameObject); liveFinds[i] = null; }
        }
        var view = endingView != null ? endingView : spawnPoint;
        player.Teleport(view.position);
        player.SetLook(view.eulerAngles.y, 6f);
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

        if (find.Data == blankPage)
        {
            Destroy(find.gameObject);
            story.PlayEnding();
            return true;
        }

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
