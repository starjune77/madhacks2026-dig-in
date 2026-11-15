using UnityEngine;
using UnityEngine.InputSystem;

// The cat's shop by the house. Walk up to the counter and press E: the price board opens
// and the cursor is freed. Click Sell on a kind of stone, Sell All, or Buy on an upgrade;
// Close (or Esc, or E) leaves. Number keys still work: 1 sell all, 2 spade, 3 bag, 4 lantern.
// The cat says a short line for each. Prices and lines are inspector data. Sits on "Game".
public class DigShop : MonoBehaviour
{
    public DigGame game;
    public DigHud hud;

    [Header("Spades (index = level)")]
    public string[] spadeNames = { "Old Spade", "Garden Spade", "Steel Spade", "Fine Steel Spade" };
    public int[] spadePrices = { 0, 60, 220, 480 };

    [Header("Bag (index = level)")]
    public int[] bagSizes = { 6, 10, 16, 24 };
    public int[] bagPrices = { 0, 40, 110, 240 };

    [Header("Lantern (index = level)")]
    public string[] lanternNames = { "Small Lantern", "Bright Lantern", "Warm Lamp" };
    public float[] lanternPower = { 1f, 1.5f, 2.1f };
    public int[] lanternPrices = { 0, 50, 150 };

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
        for (int i = 0; i < hud.shopRows.Length; i++)
        {
            int row = i;
            hud.shopRows[i].button.onClick.AddListener(() => { if (rowKinds != null && row < rowKinds.Length && rowKinds[row] != null) SellKind(rowKinds[row]); });
        }
        hud.shopSellAll.button.onClick.AddListener(Sell);
        hud.shopUpgrades[0].button.onClick.AddListener(BuySpade);
        hud.shopUpgrades[1].button.onClick.AddListener(BuyBag);
        hud.shopUpgrades[2].button.onClick.AddListener(BuyLantern);
        hud.shopClose.onClick.AddListener(Close);
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
    public void Sell()
    {
        int coins = game.SellBag(out int count);
        Refresh(count == 0 ? Pick(onNothing) : Pick(onSell) + "  <alpha=#99>(+" + coins + " coins)");
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
        Refresh(count == 0 ? Pick(onNothing) : Pick(onSell) + "  <alpha=#99>(+" + coins + " coins)");
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

    // Fills the price board: one row per kind of stone in the bag, Sell All, the upgrades.
    void Refresh(string catLine)
    {
        Wire();
        // The bag, grouped by kind, most valuable first.
        var counts = new System.Collections.Generic.Dictionary<MineralData, int>();
        foreach (var m in game.bag) { counts.TryGetValue(m, out int c); counts[m] = c + 1; }
        var kinds = new System.Collections.Generic.List<MineralData>(counts.Keys);
        kinds.Sort((a, b) => b.value.CompareTo(a.value));
        rowKinds = new MineralData[hud.shopRows.Length];
        for (int i = 0; i < hud.shopRows.Length; i++)
        {
            var row = hud.shopRows[i];
            bool used = i < kinds.Count;
            row.root.SetActive(used);
            if (!used) continue;
            var k = kinds[i];
            rowKinds[i] = k;
            row.label.text = k.title + "  <alpha=#99>×" + counts[k];
            row.buttonLabel.text = "Sell  " + (k.value * counts[k]) + " coins";
            row.button.interactable = true;
        }
        if (hud.shopEmpty != null) hud.shopEmpty.gameObject.SetActive(kinds.Count == 0);
        hud.shopSellAll.button.interactable = game.bag.Count > 0;
        hud.shopSellAll.buttonLabel.text = "Sell all  " + game.BagValue + " coins";

        var bagLabels = new string[bagSizes.Length];
        for (int i = 0; i < bagLabels.Length; i++) bagLabels[i] = "A bag for " + bagSizes[i] + " stones";
        Upgrade(hud.shopUpgrades[0], game.shovelLevel, spadePrices, spadeNames);
        Upgrade(hud.shopUpgrades[1], game.bagLevel, bagPrices, bagLabels);
        Upgrade(hud.shopUpgrades[2], game.lanternLevel, lanternPrices, lanternNames);

        hud.shopCoins.text = "You have " + game.coins + " coins";
        hud.ShowShop(true);
        if (hud.shopCat != null) hud.shopCat.text = "“" + catLine + "”";
    }

    void Upgrade(DigHud.ShopRow row, int level, int[] prices, string[] names)
    {
        if (level >= prices.Length - 1)
        {
            row.label.text = names[names.Length - 1] + "  <alpha=#88>(yours)";
            row.buttonLabel.text = "The best there is";
            row.button.interactable = false;
            return;
        }
        row.label.text = names[level + 1];
        row.buttonLabel.text = "Buy  " + prices[level + 1] + " coins";
        row.button.interactable = game.coins >= prices[level + 1];
    }

    static string Pick(string[] lines) => lines[Random.Range(0, lines.Length)];
}
