using System.Collections.Generic;
using UnityEngine;

// Game rules and state: coins, the bag, spade level, what a stroke does, memory objects. Sits on
// the "Game" object in SampleScene. Everything it talks to is real scene content wired up
// in the inspector (DigIn/Build Scene does the first wiring); nothing is built here.
public class DigGame : MonoBehaviour
{
    public static DigGame I { get; private set; }

    // Layers a look, dig or ground ray can hit: everything but the player and loose debris.
    public static int AimMask { get; private set; } = Physics.DefaultRaycastLayers;

    // True while a card, letter or the ending is on screen.
    public bool InputLocked => (story != null && story.Locked) || (shop != null && shop.IsOpen);

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
    public DigShop shop;
    public DigMinerals minerals;
    public DigAtmosphere atmosphere;
    [Tooltip("The shop counter: walk up to it and press E.")]
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
    [Tooltip("Small memory fragments between the major ones (lines only, never on the shelf).")]
    public MemoryData[] fragments = new MemoryData[0];
    [Tooltip("A memory turns up when a scoop comes this close to its spot (m, beyond the scoop itself).")]
    public float findReach = 0.8f;

    [Header("Ending")]
    public MemoryData blankPage;
    public MemoryFind blankPagePrefab;

    [Header("Spades (names and prices are on the shop)")]
    [Tooltip("Scoop radius in the soft soil of the life path, per spade level (m). A spade digs strata up to its level; the last one is just faster.")]
    public float[] shovelRadius = { 0.5f, 0.56f, 0.62f, 0.74f };

    [Header("Soil")]
    [Tooltip("Soil is at its softest within the first distance of the life path and at its densest beyond the second (m).")]
    public Vector2 softRange = new Vector2(1.5f, 5f);
    [Tooltip("Scoop size in dense soil, as a fraction of the soft-soil size.")]
    public float denseScale = 0.62f;

    [Header("State")]
    public int coins;
    public int shovelLevel;
    public int bagLevel;
    public int lanternLevel;
    public readonly System.Collections.Generic.List<MineralData> bag = new System.Collections.Generic.List<MineralData>();

    public int BagCount => bag.Count;
    public int BagValue { get { int v = 0; foreach (var m in bag) v += m.value; return v; } }
    public float DeepestDepth { get; private set; }
    public string ShovelName => shop != null ? shop.SpadeName : "Spade";

    bool[] spawned;
    bool[] found;
    bool[] fragmentSpawned;
    bool pageSpawned;
    MemoryFind[] liveFinds;
    int lastStratum;
    float lastAmount = 1f;
    bool tossPending;
    Vector3 returnPoint;
    float returnYaw;
    bool hasReturnPoint;

    void Awake()
    {
        I = this;
        // Never let a slow frame turn into many physics steps (and a slower frame).
        Time.maximumDeltaTime = 0.1f;
        int mask = Physics.DefaultRaycastLayers;
        int playerLayer = LayerMask.NameToLayer("Player"), debrisLayer = LayerMask.NameToLayer("Debris");
        if (playerLayer >= 0) mask &= ~(1 << playerLayer);
        if (debrisLayer >= 0) mask &= ~(1 << debrisLayer);
        AimMask = mask;

        spawned = new bool[memories.Length];
        fragmentSpawned = new bool[fragments.Length];
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
            DigAudio.I?.Bounce(hit.point);
            debris.Chips(hit.point, hit.normal, stratum);
            return DigOutcome.Protected;
        }
        if (stratum > shovelLevel)
        {
            DigAudio.I?.Bounce(hit.point);
            debris.Chips(hit.point, hit.normal, stratum);
            hud.ShowMessage("This ground is too hard for the " + ShovelName + ".");
            return DigOutcome.TooHard;
        }

        float radius = DigRadius(hit.point);
        Vector3 center = hit.point + viewDirection * (radius * 0.35f);   // bite into the surface, not on it
        scoops.Begin(center, radius, viewDirection, shovelLevel, stratum, ScoopDone);
        debris.Impact(hit.point, hit.normal, -viewDirection, stratum);
        DigAudio.I?.Dig(stratum, hit.point);

        lastStratum = stratum;
        lastAmount = radius / shovelRadius[shovelLevel];
        tossPending = true;
        shovel.ShowLoad(debris.clodMaterials[Mathf.Clamp(stratum, 0, debris.clodMaterials.Length - 1)], lastAmount);
        hasDug = true;
        return DigOutcome.Dug;
    }

    // Called by the player when the swing flicks the earth off the blade.
    public void Toss(Vector3 from, Vector3 direction)
    {
        shovel.HideLoad();
        if (!tossPending) return;
        tossPending = false;
        debris.Toss(from, direction, lastStratum, lastAmount);
        DigAudio.I?.Toss(from);
    }

    void ScoopDone(Vector3 center, float radius, float volumeRemoved)
    {
        if (minerals != null) minerals.Freed(center, radius, player.transform.position - center);
        CheckFinds(center, radius);
    }

    // ---- The bag and the shop ---------------------------------------------------
    // A loose mineral the player walked over. False (and a hint) when the bag is full.
    public bool TryBag(MineralData kind)
    {
        int size = shop != null ? shop.BagSize : 6;
        if (bag.Count >= size)
        {
            if (Time.time > bagFullHint) { hud.ShowMessage("Your bag is full. Press <b>R</b> to go up and sell.", 3f); bagFullHint = Time.time + 6f; }
            return false;
        }
        bag.Add(kind);
        hud.FlashScrap();
        DigAudio.I?.Mineral();
        hud.ShowMessage("<b>" + kind.title + "</b>  <alpha=#BB><i>" + kind.line + "</i>", 3.5f);
        return true;
    }
    float bagFullHint;
    bool doorTried, catHinted, hasDug;
    [Header("Where to dig")]
    [Tooltip("The bare patch where the dig begins (stones, flowers and a trowel around it).")]
    public Vector3 digSpot = new Vector3(2.5f, 0f, 2.5f);
    [Tooltip("Shown under the crosshair while standing on the patch, until the first dig.")]
    public string digHint = "Hold <b>left click</b> to dig.";
    public float digHintRadius = 2.6f;
    [Tooltip("The cat says this the first time you pass the shop, if you have not started digging.")]
    public string catWonder = "The cat watches the bare patch by the stones for a moment.  <i>“Something's under the soft ground, I think.”</i>";
    [Tooltip("What draws the eye to the patch (the glint, the pool of light). It fades away once you dig.")]
    public GameObject digSpotLure;
    float lureFade = 1f;
    [Header("Dream")]
    [Tooltip("Standing here, the player tries the back door.")]
    public Vector3 backDoor = new Vector3(3.2f, 0.8f, -15.2f);

    public int SellBag(out int count)
    {
        count = bag.Count;
        int value = BagValue;
        coins += value;
        bag.Clear();
        return value;
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

        // Fragments: smaller things, between the memories.
        for (int i = 0; i < fragments.Length; i++)
        {
            var f = fragments[i];
            if (fragmentSpawned[i] || f == null || f.findPrefab == null) continue;
            Vector3 spot = path.PointAt(f.pathDistance);
            if ((spot - center).sqrMagnitude > (radius + findReach) * (radius + findReach)) continue;
            fragmentSpawned[i] = true;
            Unearth(f.findPrefab, f, spot);
            return;
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
        volume.CollapseLoose(spot, 0.45f, null);
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
            // A few steps back, looking along the tunnel: its walls lead the eye to the face.
            StandOnPath(distance - 3f, 2f);
        }
        hud.ShowMessage(StratumNames[stratum], 2f);
    }

    // In front of the cat's counter with a bag of stones to sell.
    public void DemoShop()
    {
        ReturnToSurface();
        bag.Clear();
        if (minerals != null && minerals.kinds.Length > 0)
            for (int i = 0; i < (shop != null ? shop.BagSize : 6); i++) bag.Add(minerals.kinds[(i * 3 + 1) % minerals.kinds.Length]);
        if (workbench != null && workbench.interactPoint != null)
        {
            Vector3 p = workbench.interactPoint.position;
            Vector3 toCounter = workbench.transform.position - p;
            player.Teleport(new Vector3(p.x, spawnPoint.position.y, p.z));
            player.SetLook(Mathf.Atan2(toCounter.x, toCounter.z) * Mathf.Rad2Deg, 12f);
        }
        hud.ShowMessage("The shop. Press <b>E</b>.", 2f);
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
        if (minerals != null) foreach (var p in points) minerals.ClearInside(p, 1.3f);
    }

    void StandOnPath(float distance, float lookDown = 12f)
    {
        Vector3 p = path.PointAt(distance);
        Vector3 forward = path.TangentAt(distance);
        player.Teleport(p + Vector3.up * 0.6f);
        float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        float pitch = Mathf.Clamp(Mathf.Asin(-forward.y) * Mathf.Rad2Deg, -10f, 45f);
        player.SetLook(yaw, pitch + lookDown);
    }

    void SetShovel(int level)
    {
        shovelLevel = Mathf.Clamp(level, 0, shovelRadius.Length - 1);
        shovel.SetLevel(shovelLevel);
    }

    void SkipMemoriesBefore(float distance)
    {
        for (int i = 0; i < fragments.Length; i++)
            if (fragments[i] != null && fragments[i].pathDistance < distance) fragmentSpawned[i] = true;
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

        if (find.Data.fragment)
        {
            Destroy(find.gameObject);
            DigAudio.I?.Page();
            hud.ShowMessage("<i>" + find.Data.line + "</i>", 5f);
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
    // E key. At the shop counter, opens the shop.
    public void Interact()
    {
        if (shop == null || workbench == null || !workbench.InRange(player.transform.position)) return;
        catHinted = true;   // you found the cat; it can tell you itself
        shop.Open();
    }

    string ShopPrompt()
    {
        if (shop == null || shop.IsOpen || workbench == null || !workbench.InRange(player.transform.position)) return null;
        return bag.Count > 0
            ? "Press <b>E</b> to see the cat  <alpha=#99>(" + bag.Count + " stones to sell)"
            : "Press <b>E</b> to see the cat";
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

    // The glint and the light on the patch ease away once the first earth is turned.
    void FadeLure()
    {
        lureFade = Mathf.MoveTowards(lureFade, 0f, Time.deltaTime / 3f);
        foreach (var l in digSpotLure.GetComponentsInChildren<Light>())
        {
            if (!lureIntensity.ContainsKey(l)) lureIntensity[l] = l.intensity;
            l.intensity = lureIntensity[l] * lureFade;
        }
        foreach (var ps in digSpotLure.GetComponentsInChildren<ParticleSystem>())
            if (ps.isEmitting) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (lureFade <= 0f) digSpotLure.SetActive(false);
    }
    readonly Dictionary<Light, float> lureIntensity = new Dictionary<Light, float>();

    void Update()
    {
        // Whatever happens, nobody is left under the world.
        if (player.transform.position.y < DigVolume.Min.y - 3f) ReturnToSurface();

        float depth = Mathf.Max(0f, -player.FeetY);
        DeepestDepth = Mathf.Max(DeepestDepth, depth);

        // Where to dig: nothing says so until you stand on the patch (then, until the first
        // dig, a quiet prompt), and the cat wonders about it the first time you pass the shop.
        Vector3 here = player.transform.position;
        bool onSpot = !hasDug && depth < 0.5f && new Vector2(here.x - digSpot.x, here.z - digSpot.z).magnitude < digHintRadius;
        if (hasDug && digSpotLure != null && digSpotLure.activeSelf) FadeLure();
        if (!catHinted && DeepestDepth < 0.6f && !InputLocked && workbench != null && (here - workbench.transform.position).sqrMagnitude < 4.5f * 4.5f)
        {
            catHinted = true;
            hud.ShowMessage(catWonder, 5f);
        }

        // The back door does not open. Nobody is home; nobody ever is, here.
        if (!doorTried && (player.transform.position - backDoor).sqrMagnitude < 1.6f * 1.6f)
        {
            doorTried = true;
            hud.ShowMessage("The back door won't open. It's very quiet inside.", 4f);
        }
        hud.SetStatus(depth, ShovelName, coins, bag.Count, shop != null ? shop.BagSize : 6);
        string prompt = ShopPrompt();
        hud.SetPrompt(string.IsNullOrEmpty(prompt) && onSpot && !InputLocked ? digHint : prompt);
        if (atmosphere != null && shop != null) atmosphere.lanternPower = shop.LanternPower;
    }
}
