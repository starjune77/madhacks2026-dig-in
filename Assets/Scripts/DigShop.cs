using UnityEngine;
using UnityEngine.InputSystem;

// The cat's shop by the house. Walk up to the counter and press E: the price board opens
// and the cursor is freed. Two pages: "Sell your finds" (a tile per kind of stone in the
// bag, each with Sell, and Sell All) and "For the next dig" (a card per spade, bag and
// lantern tier: yours, the next one to buy, and later ones greyed out). Close, Esc or E
// leaves. Number keys still work: 1 sell all, 2 spade, 3 bag, 4 lantern. The cat says a
// short line for each. Prices, names, lines and pictures are inspector data. On "Game".
public class DigShop : MonoBehaviour
{
    public DigGame game;
    public DigHud hud;

    [Header("Spades (index = level)")]
    public string[] spadeNames = { "Old Spade", "Garden Spade", "Steel Spade", "Fine Steel Spade" };
    public int[] spadePrices = { 0, 60, 220, 480 };
    public string[] spadeLines =
    {
        "Worn smooth where small hands held it.",
        "Sharp enough for clay. Smells of summer gardens.",
        "Heavy and sure. Stone gives way to it.",
        "It digs as if it already knows the way.",
    };
    public Sprite[] spadeIcons;

    [Header("Bag (index = level)")]
    public string[] bagNames = { "Small Basket", "Picnic Basket", "Wooden Bucket", "Old Suitcase" };
    public int[] bagSizes = { 6, 10, 16, 24 };
    public int[] bagPrices = { 0, 40, 110, 240 };
    public string[] bagLines =
    {
        "Just enough for small pockets.",
        "It still smells of sandwiches.",
        "Heavier than it looks.",
        "It has been further than you.",
    };
    public Sprite[] bagIcons;

    [Header("Lantern (index = level)")]
    public string[] lanternNames = { "Small Lantern", "Bright Lantern", "Warm Lamp" };
    public float[] lanternPower = { 1f, 1.5f, 2.1f };
    public int[] lanternPrices = { 0, 50, 150 };
    public string[] lanternLines =
    {
        "Enough light to see your hands.",
        "A wider circle, like a porch light left on.",
        "Warm as a kitchen window at night.",
    };
    public Sprite[] lanternIcons;

    [Header("What the cat says")]
    public string[] greetings =
    {
        "Mrrp. You're back.",
        "Show me what you found.",
        "Dirt on your knees again.",
        "I kept the counter warm.",
    };
    public string[] onSell = { "Shiny. I'll keep these.", "Good finds.", "Mm. These will do nicely.", "The bag feels lighter, doesn't it?" };
    public string[] onBuy = { "Use it well.", "That one suits you.", "Careful with it.", "Go on, then." };
    public string[] onPoor = { "Not yet. Dig a little more.", "Come back with more stones.", "A few more, and it's yours." };
    public string[] onNothing = { "Your bag is empty.", "Nothing to sell? Then dig." };
    [Tooltip("Things a cat could not know. Said now and then, more often the deeper you have been.")]
    public string[] knowing =
    {
        "You used to dig here when you were small. You never got very far.",
        "Someone always called you in when it got dark. You never wanted to go.",
        "You wrote your name in the wet cement by the back step. It's still there.",
        "You're the one who left that window open, every summer.",
        "You always say you'll call back. You will, this time.",
        "It's later than it looks out here.",
    };

    public bool IsOpen { get; private set; }
    int openedFrame;
    int visits;

    public string SpadeName => spadeNames[Mathf.Clamp(game.shovelLevel, 0, spadeNames.Length - 1)];
    public int BagSize => bagSizes[Mathf.Clamp(game.bagLevel, 0, bagSizes.Length - 1)];
    public float LanternPower => lanternPower[Mathf.Clamp(game.lanternLevel, 0, lanternPower.Length - 1)];

    // The frame the shop last closed: that frame's Esc or click belongs to the shop, not
    // to the player (DigPlayer checks it so the cursor stays locked and nothing digs).
    public static int LastClosedFrame { get; private set; } = -10;

    MineralData[] rowKinds;
    bool wired;

    public void Open()
    {
        IsOpen = true;
        openedFrame = Time.frameCount;
        DigAudio.I?.Meow();
        visits++;
        // The first visit is ordinary. Later, now and then, the cat knows too much.
        string line = Pick(greetings);
        if (visits > 1 && Random.value < Mathf.Lerp(0.2f, 0.6f, Mathf.Clamp01(game.DeepestDepth / 25f)))
            line = knowing[Mathf.Min(knowingIndex++, knowing.Length - 1)];
        Wire();
        // The mouse is the main way to trade: free the cursor while the shop is open.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        // Something to sell? Start there. Otherwise, what's for sale.
        SetPage(game.bag.Count == 0);
        hud.SetShopCoins(game.coins, true);
        Refresh(line);
    }
    int knowingIndex;

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        LastClosedFrame = Time.frameCount;
        hud.ShowShop(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Hooks the price board's buttons to the shop (once).
    void Wire()
    {
        if (wired) return;
        wired = true;
        for (int i = 0; i < hud.shopSellTiles.Length; i++)
        {
            int tile = i;
            hud.shopSellTiles[i].button.onClick.AddListener(() => { if (rowKinds != null && tile < rowKinds.Length && rowKinds[tile] != null) SellKind(rowKinds[tile]); });
        }
        hud.shopSellAll.onClick.AddListener(Sell);
        // Only the next tier's button is ever clickable, so every card can share one action.
        foreach (var c in hud.shopSpades) c.button.onClick.AddListener(BuySpade);
        foreach (var c in hud.shopBags) c.button.onClick.AddListener(BuyBag);
        foreach (var c in hud.shopLanterns) c.button.onClick.AddListener(BuyLantern);
        hud.shopTabSell.onClick.AddListener(() => SetPage(false));
        hud.shopTabBuy.onClick.AddListener(() => SetPage(true));
        hud.shopClose.onClick.AddListener(Close);
    }

    // false = "Sell your finds", true = "For the next dig".
    public void SetPage(bool buy)
    {
        hud.shopSellPage.SetActive(!buy);
        hud.shopBuyPage.SetActive(buy);
        Tab(hud.shopTabSell, !buy);
        Tab(hud.shopTabBuy, buy);
    }

    // The open page's tab is dark ink; the other is a pale tab you can click.
    static void Tab(UnityEngine.UI.Button tab, bool active)
    {
        var img = tab.targetGraphic as UnityEngine.UI.Image;
        if (img != null) img.color = active ? new Color(0.42f, 0.29f, 0.19f) : new Color(0.86f, 0.77f, 0.62f);
        var label = tab.GetComponentInChildren<TMPro.TMP_Text>();
        if (label != null) label.color = active ? new Color(0.98f, 0.94f, 0.86f) : new Color(0.32f, 0.22f, 0.14f);
    }

    void Update()
    {
        if (!IsOpen || Time.frameCount == openedFrame) return;
        var kb = Keyboard.current;
        if (kb == null) return;
        // Keys still work as a fallback.
        if (kb.eKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame) { Close(); return; }
        if (kb.digit1Key.wasPressedThisFrame) Sell();
        else if (kb.digit2Key.wasPressedThisFrame) BuySpade();
        else if (kb.digit3Key.wasPressedThisFrame) BuyBag();
        else if (kb.digit4Key.wasPressedThisFrame) BuyLantern();
    }

    // ---- Trading (public, so demo mode and tests can drive it) -------------------------
    [Tooltip("Said the first time you sell, about where the memories are.")]
    public string firstSale = "The warm soil remembers more.";
    bool soldOnce;

    string SoldLine(int coins)
    {
        string line = soldOnce ? Pick(onSell) : firstSale;
        soldOnce = true;
        return line + "  <alpha=#99>(+" + coins + " coins)";
    }

    public void Sell()
    {
        int coins = game.SellBag(out int count);
        Refresh(count == 0 ? Pick(onNothing) : SoldLine(coins));
        if (count > 0) DigAudio.I?.Mineral();
    }

    // Sells every stone of one kind.
    public void SellKind(MineralData kind)
    {
        int count = 0, coins = 0;
        for (int i = game.bag.Count - 1; i >= 0; i--)
        {
            if (game.bag[i] != kind) continue;
            coins += kind.value;
            count++;
            game.bag.RemoveAt(i);
        }
        game.coins += coins;
        if (count > 0) DigAudio.I?.Mineral();
        Refresh(count == 0 ? Pick(onNothing) : SoldLine(coins));
    }

    public void BuySpade() => Buy(ref game.shovelLevel, spadePrices, () => game.shovel.SetLevel(game.shovelLevel));
    public void BuyBag() => Buy(ref game.bagLevel, bagPrices, null);
    public void BuyLantern() => Buy(ref game.lanternLevel, lanternPrices, null);

    void Buy(ref int level, int[] prices, System.Action after)
    {
        if (level >= prices.Length - 1) { Refresh("That's the best there is."); return; }
        int price = prices[level + 1];
        if (game.coins < price) { Refresh(Pick(onPoor)); return; }
        game.coins -= price;
        level++;
        after?.Invoke();
        DigAudio.I?.Page();
        Refresh(Pick(onBuy));
    }

    // Fills the price board: a tile per kind of stone in the bag, Sell All, the tier cards.
    void Refresh(string catLine)
    {
        Wire();
        // The bag, grouped by kind, most valuable first.
        var counts = new System.Collections.Generic.Dictionary<MineralData, int>();
        foreach (var m in game.bag) { counts.TryGetValue(m, out int c); counts[m] = c + 1; }
        var kinds = new System.Collections.Generic.List<MineralData>(counts.Keys);
        kinds.Sort((a, b) => b.value.CompareTo(a.value));
        rowKinds = new MineralData[hud.shopSellTiles.Length];
        for (int i = 0; i < hud.shopSellTiles.Length; i++)
        {
            var tile = hud.shopSellTiles[i];
            bool used = i < kinds.Count;
            tile.root.SetActive(used);
            if (!used) continue;
            var k = kinds[i];
            rowKinds[i] = k;
            Picture(tile.image, k.icon);
            tile.title.text = k.title;
            tile.detail.text = "\u00d7" + counts[k] + "   <alpha=#99>" + k.value + " each";
            tile.line.text = k.line;
            tile.buttonLabel.text = "Sell  " + (k.value * counts[k]);
            tile.button.interactable = true;
        }
        if (hud.shopEmpty != null) hud.shopEmpty.gameObject.SetActive(kinds.Count == 0);
        hud.shopSellAll.interactable = game.bag.Count > 0;
        hud.shopSellAll.gameObject.SetActive(game.bag.Count > 0);
        hud.shopSellTotal.text = game.bag.Count > 0
            ? game.bag.Count + (game.bag.Count == 1 ? " stone" : " stones") + " in your bag, worth " + game.BagValue + " coins."
            : "";

        Cards(hud.shopSpades, game.shovelLevel, spadePrices, spadeNames, spadeLines, spadeIcons);
        var bagLabels = new string[bagLines.Length];
        for (int i = 0; i < bagLabels.Length; i++) bagLabels[i] = "Holds " + bagSizes[Mathf.Min(i, bagSizes.Length - 1)] + ". " + bagLines[i];
        Cards(hud.shopBags, game.bagLevel, bagPrices, bagNames, bagLabels, bagIcons);
        Cards(hud.shopLanterns, game.lanternLevel, lanternPrices, lanternNames, lanternLines, lanternIcons);

        hud.SetShopCoins(game.coins, false);
        hud.ShowShop(true);
        if (hud.shopCat != null) hud.shopCat.text = "\u201c" + catLine + "\u201d";
    }

    // One card per tier: the ones you have are stamped, the next has Buy (if you can
    // afford it), the ones after it are greyed out until then.
    void Cards(DigHud.ShopItem[] cards, int level, int[] prices, string[] names, string[] lines, Sprite[] icons)
    {
        for (int i = 0; i < cards.Length; i++)
        {
            var c = cards[i];
            bool exists = i < prices.Length;
            c.root.SetActive(exists);
            if (!exists) continue;
            Picture(c.image, icons != null && i < icons.Length ? icons[i] : null);
            c.title.text = i < names.Length ? names[i] : "";
            c.line.text = i < lines.Length ? lines[i] : "";
            bool owned = i <= level;
            c.button.gameObject.SetActive(!owned);
            c.stamp.gameObject.SetActive(owned);
            if (owned)
            {
                c.stamp.text = i == level ? "Yours" : "Outgrown";
                c.group.alpha = i == level ? 1f : 0.62f;
                continue;
            }
            bool next = i == level + 1;
            c.buttonLabel.text = next ? "Buy  " + prices[i] + " coins" : prices[i] + " coins";
            c.button.interactable = next && game.coins >= prices[i];
            c.group.alpha = next ? 1f : 0.42f;
        }
    }

    static void Picture(UnityEngine.UI.Image image, Sprite sprite)
    {
        image.sprite = sprite;
        image.preserveAspect = true;
        image.color = sprite != null ? Color.white : new Color(0f, 0f, 0f, 0f);
    }

    static string Pick(string[] lines) => lines[Random.Range(0, lines.Length)];
}
