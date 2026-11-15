using UnityEngine;
using UnityEngine.InputSystem;

// The cat's shop by the house. Walk up to the counter and press E: the price board opens.
//   1  sell everything in the bag
//   2  the next spade      3  a bigger bag      4  a brighter lantern
//   E or Esc  close
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
        Refresh(line);
    }
    int knowingIndex;

    public void Close()
    {
        IsOpen = false;
        hud.ShowShop(false, null, null);
    }

    void Update()
    {
        if (!IsOpen || Time.frameCount == openedFrame) return;
        var kb = Keyboard.current;
        if (kb == null) return;
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

    void Refresh(string catLine)
    {
        string Row(string key, string what, int level, int[] prices, string[] names)
        {
            if (level >= prices.Length - 1) return "<alpha=#66>" + key + "   " + what + " - the best there is<alpha=#FF>";
            string next = names != null ? names[level + 1] : what;
            string price = prices[level + 1] + " coins";
            bool can = game.coins >= prices[level + 1];
            return key + "   " + next + "  " + (can ? "" : "<alpha=#77>") + price + (can ? "" : "<alpha=#FF>");
        }
        string bagNames(int i) => bagSizes[i] + " stones";
        var bagLabels = new string[bagSizes.Length];
        for (int i = 0; i < bagLabels.Length; i++) bagLabels[i] = "A bag for " + bagNames(i);

        string board =
            "1   Sell what's in the bag  <alpha=#99>(" + game.BagCount + " stones, " + game.BagValue + " coins)<alpha=#FF>\n" +
            Row("2", "Spade", game.shovelLevel, spadePrices, spadeNames) + "\n" +
            Row("3", "Bag", game.bagLevel, bagPrices, bagLabels) + "\n" +
            Row("4", "Lantern", game.lanternLevel, lanternPrices, lanternNames) + "\n\n" +
            "<alpha=#99>You have " + game.coins + " coins.     E  leave<alpha=#FF>";
        hud.ShowShop(true, board, catLine);
    }

    static string Pick(string[] lines) => lines[Random.Range(0, lines.Length)];
}
