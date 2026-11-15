using System.Collections.Generic;
using UnityEngine;

// Game rules and state: scrap, shovel level, what a stroke does, memory objects. Sits on
// the "Game" object in SampleScene. Everything it talks to is real scene content wired up
// in the inspector (DigIn/Build Scene does the first wiring); nothing is built here.
public class DigGame : MonoBehaviour
{
    public static DigGame I { get; private set; }

    // Layers a look, dig or ground ray can hit: everything but the player and loose debris.
    public static int AimMask { get; private set; } = Physics.DefaultRaycastLayers;

    // True while a card, letter or the ending is on screen.
    public bool InputLocked => story != null && story.Locked;

    public const int StratumCount = 3;

    public enum DigOutcome { Nothing, Dug, TooHard, Protected }

    [Header("Scene")]
    public DigVolume volume;
    public DigPath path;
    public DigScoop scoops;
    public DigDebris debris;
    public DigAim aim;
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
    [Tooltip("A memory turns up when a scoop comes this close to its spot (m, beyond the scoop itself).")]
    public float findReach = 0.8f;

    [Header("Ending")]
    public MemoryData blankPage;
    public MemoryFind blankPagePrefab;

    [Header("Shovels")]
    public string[] shovelNames = { "Toy Shovel", "Garden Shovel", "Steel Shovel" };
    [Tooltip("Scoop radius in the soft soil of the life path (m).")]
    public float[] shovelRadius = { 0.5f, 0.6f, 0.7f };
    public int[] upgradeCost = { 8, 20 };               // cost to reach level 1, level 2

    [Header("Soil")]
    [Tooltip("Soil is at its softest within the first distance of the life path and at its densest beyond the second (m).")]
    public Vector2 softRange = new Vector2(1.5f, 5f);
    [Tooltip("Scoop size in dense soil, as a fraction of the soft-soil size.")]
    public float denseScale = 0.62f;

    [Header("Scrap")]
    [Tooltip("Cubic metres of earth dug per scrap found.")]
    public float volumePerScrap = 1.2f;

    [Header("State")]
    public int scrap;
    public int shovelLevel;

    bool[] spawned;
    bool[] found;
    bool pageSpawned;
    MemoryFind[] liveFinds;
    float scrapProgress;
    int lastStratum;
    float lastAmount = 1f;
    bool tossPending;
    Vector3 returnPoint;
    float returnYaw;
    bool hasReturnPoint;

    void Awake()
    {
        I = this;
        int mask = Physics.DefaultRaycastLayers;
        int playerLayer = LayerMask.NameToLayer("Player"), debrisLayer = LayerMask.NameToLayer("Debris");
        if (playerLayer >= 0) mask &= ~(1 << playerLayer);
        if (debrisLayer >= 0) mask &= ~(1 << debrisLayer);
        AimMask = mask;

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
    // How big a scoop is here: full size in the soft soil of the life path, smaller in
    // the dense earth away from it. This is the only "wall" there is.
    public float DigRadius(Vector3 point)
    {
        float soft = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(softRange.x, softRange.y, path.Distance(point)));
        return shovelRadius[shovelLevel] * Mathf.Lerp(denseScale, 1f, soft);
    }

    // Called by the player when the blade lands on `hit`.
    public DigOutcome Dig(RaycastHit hit, Vector3 viewDirection)
    {
        if (!volume.Owns(hit.collider)) return DigOutcome.Nothing;   // grass beyond the yard, walls, props

        Vector3 inside = hit.point - hit.normal * 0.12f;
        int stratum = volume.StratumAt(inside);

        if (volume.IsProtected(inside))
        {
            debris.Chips(hit.point, hit.normal, stratum);
            return DigOutcome.Protected;
        }
        if (stratum > shovelLevel)
        {
            debris.Chips(hit.point, hit.normal, stratum);
            hud.ShowMessage("This ground is too hard for the " + shovelNames[shovelLevel] + ".");
            return DigOutcome.TooHard;
        }

        float radius = DigRadius(hit.point);
        Vector3 center = hit.point + viewDirection * (radius * 0.35f);   // bite into the surface, not on it
        scoops.Begin(center, radius, viewDirection, shovelLevel, stratum, ScoopDone);
        debris.Impact(hit.point, hit.normal, -viewDirection, stratum);

        lastStratum = stratum;
        lastAmount = radius / shovelRadius[shovelLevel];
        tossPending = true;
        return DigOutcome.Dug;
    }

    // Called by the player when the swing flicks the earth off the blade.
    public void Toss(Vector3 from, Vector3 direction)
    {
        if (!tossPending) return;
        tossPending = false;
        debris.Toss(from, direction, lastStratum, lastAmount);
    }

    void ScoopDone(Vector3 center, float radius, float volumeRemoved)
    {
        scrapProgress += volumeRemoved;
        bool gained = false;
        while (scrapProgress >= volumePerScrap)
        {
            scrapProgress -= volumePerScrap;
            scrap++;
            gained = true;
        }
        if (gained) hud.FlashScrap();

        CheckFinds(center, radius);
    }

    // The ring that shows where the next scoop will land.
    public void UpdateAim(Ray ray, float reach)
    {
        if (aim == null) return;
        // Between strokes only: while the shovel is busy the ring would just be clutter.
        if (shovel.Ready && Physics.Raycast(ray, out RaycastHit hit, reach, AimMask, QueryTriggerInteraction.Ignore) && volume.Owns(hit.collider))
        {
            Vector3 inside = hit.point - hit.normal * 0.12f;
            if (!volume.IsProtected(inside))
            {
                aim.Show(hit, DigRadius(hit.point), volume.StratumAt(inside) > shovelLevel, AimMask);
                return;
            }
        }
        aim.Hide();
    }

    public void HideAim()
    {
        if (aim != null) aim.Hide();
    }

    // ---- Memory objects -----------------------------------------------------
    // Each memory lies at a fixed spot on the life path and turns up when a scoop comes
    // close to it, so the demo is reproducible.
    void CheckFinds(Vector3 center, float radius)
    {
        for (int i = 0; i < memories.Length; i++)
        {
            if (spawned[i] || memories[i] == null) continue;
            Vector3 spot = path.PointAt(memories[i].pathDistance);
            if ((spot - center).sqrMagnitude > (radius + findReach) * (radius + findReach)) continue;

            spawned[i] = true;
            int s = memories[i].stratum;
            var prefab = memories[i].findPrefab != null ? memories[i].findPrefab
                : findPrefabsByStratum != null && s < findPrefabsByStratum.Length && findPrefabsByStratum[s] != null
                ? findPrefabsByStratum[s] : findPrefab;
            liveFinds[i] = Unearth(prefab, memories[i], spot);
            hud.ShowMessage("Something is buried here.", 4f);
            return;   // one discovery per stroke
        }

        // At the very end of the path, a blank page.
        if (!pageSpawned && blankPagePrefab != null)
        {
            Vector3 end = path.PointAt(path.Length - 0.6f);
            if ((end - center).sqrMagnitude <= (radius + findReach) * (radius + findReach)) SpawnBlankPage();
        }
    }

    // Opens a little room around the spot so the object is not stuck in solid earth.
    MemoryFind Unearth(MemoryFind prefab, MemoryData data, Vector3 spot)
    {
        volume.Carve(spot + Vector3.up * 0.15f, 0.45f, Vector3.down, 1f, 99);
        volume.Flush(true);
        debris.Crumble(spot + Vector3.up * 0.2f, volume.StratumAt(spot));
        var find = Instantiate(prefab, spot, Quaternion.identity);
        find.Init(data);
        return find;
    }

    void SpawnBlankPage()
    {
        pageSpawned = true;
        Unearth(blankPagePrefab, blankPage, path.PointAt(path.Length - 0.6f));
    }

    // ---- Demo mode (DigDemo) --------------------------------------------------
    static readonly string[] StratumNames = { "Childhood", "School Days", "Growing Up" };

    // Start of a stratum with the shovel for it. Memories before it on the path are skipped.
    public void DemoJump(int stratum)
    {
        SetShovel(stratum);
        if (stratum == 0)
        {
            SkipMemoriesBefore(0f);
            ReturnToSurface();
        }
        else
        {
            float boundary = stratum == 1 ? volume.stratumDepth.x : volume.stratumDepth.y;
            float distance = path.DistanceAtDepth(boundary + volume.wobble + 0.5f);
            SkipMemoriesBefore(distance);
            TunnelTo(distance);
            StandOnPath(distance - 1.2f);
        }
        hud.ShowMessage(StratumNames[stratum], 2f);
    }

    // The end of the path with the blank page in front of you.
    public void DemoEnding()
    {
        SetShovel(StratumCount - 1);
        SkipMemoriesBefore(path.Length + 1f);
        TunnelTo(path.Length - 0.3f);
        StandOnPath(path.Length - 2.6f);
        if (!pageSpawned) SpawnBlankPage();
    }

    // Digs a walkable tunnel along the life path from the surface to `distance`.
    void TunnelTo(float distance)
    {
        var points = new List<Vector3>();
        for (float d = 0f; d <= distance; d += 0.4f) points.Add(path.PointAt(d) + Vector3.up * 0.45f);
        volume.CarveTunnel(points, 1.05f);
    }

    void StandOnPath(float distance)
    {
        Vector3 p = path.PointAt(distance);
        Vector3 forward = path.TangentAt(distance);
        player.Teleport(p + Vector3.up * 0.6f);
        float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        float pitch = Mathf.Clamp(Mathf.Asin(-forward.y) * Mathf.Rad2Deg, -10f, 45f);
        player.SetLook(yaw, pitch + 12f);
    }

    void SetShovel(int level)
    {
        shovelLevel = Mathf.Clamp(level, 0, shovelNames.Length - 1);
        shovel.SetLevel(shovelLevel);
    }

    void SkipMemoriesBefore(float distance)
    {
        for (int i = 0; i < memories.Length; i++)
        {
            if (memories[i] == null || memories[i].pathDistance >= distance) continue;
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
        hasReturnPoint = false;
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
        if (!Physics.Raycast(ray, out RaycastHit hit, reach, AimMask, QueryTriggerInteraction.Collide)) return false;
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

    // R key: up to the surface from below, and back down to where you were from above.
    public void ToggleSurface()
    {
        if (player.FeetY < -1.2f)
        {
            returnPoint = player.transform.position;
            returnYaw = player.transform.eulerAngles.y;
            hasReturnPoint = true;
            ReturnToSurface();
            hud.ShowMessage("Press <b>R</b> to go back down.", 3f);
        }
        else if (hasReturnPoint)
        {
            player.Teleport(returnPoint + Vector3.up * 0.05f);
            player.SetLook(returnYaw, 10f);
        }
    }

    public void ReturnToSurface()
    {
        player.Teleport(spawnPoint.position);
        player.SetLook(spawnPoint.eulerAngles.y, 0f);
    }

    void Update()
    {
        // Whatever happens, nobody is left under the world.
        if (player.transform.position.y < DigVolume.Min.y - 3f) ReturnToSurface();

        float depth = Mathf.Max(0f, -player.FeetY);
        int cost = shovelLevel < upgradeCost.Length ? upgradeCost[shovelLevel] : -1;
        hud.SetStatus(depth, shovelNames[shovelLevel], scrap, cost);
        hud.SetPrompt(WorkbenchPrompt());
    }
}
