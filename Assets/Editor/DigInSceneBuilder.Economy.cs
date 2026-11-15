using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// The economy as assets: the eleven minerals (Meshy models, Assets/Data/Minerals), the
// glint that makes them easy to spot, and the cat's price board in the HUD.
public static partial class DigInSceneBuilder
{
    const string MineralDir = "Assets/Data/Minerals";

    // title, Meshy folder, coins, weight in Childhood / School Days / Growing Up, smoothness, metallic, glint, line
    static readonly (string title, string folder, int value, Vector3 weights, float smooth, float metal, Color glint, string line)[] MineralDefs =
    {
        ("Quartz", "Quartz", 3, new Vector3(3f, 1.5f, 0.6f), 0.75f, 0f, new Color(1f, 1f, 1f), "Clear, like the mornings before school."),
        ("Feldspar", "Feldspar", 4, new Vector3(2.5f, 1.5f, 0.8f), 0.45f, 0f, new Color(1f, 0.85f, 0.8f), "Pink as a sunburn after a long day outside."),
        ("Mica", "Mica", 5, new Vector3(2f, 1.5f, 0.6f), 0.7f, 0.3f, new Color(1f, 0.95f, 0.8f), "Thin layers, like the pages you meant to finish."),
        ("Jasper", "Jasper", 8, new Vector3(1.2f, 1.5f, 0.8f), 0.55f, 0f, new Color(1f, 0.75f, 0.6f), "Warm in the hand, like a pocket stone you never threw."),
        ("Garnet", "Garnet", 14, new Vector3(0.4f, 1.2f, 1f), 0.7f, 0f, new Color(1f, 0.55f, 0.55f), "Deep red, like a ribbon on a birthday you almost forgot."),
        ("Malachite", "Malachite", 18, new Vector3(0.2f, 1f, 1f), 0.55f, 0f, new Color(0.7f, 1f, 0.8f), "Green rings, one for every summer."),
        ("Rose Quartz", "RoseQuartz", 22, new Vector3(0.3f, 0.8f, 0.8f), 0.65f, 0f, new Color(1f, 0.8f, 0.88f), "Soft pink, like the first time someone held your hand."),
        ("Fluorite", "Fluorite", 26, new Vector3(0f, 0.6f, 1f), 0.75f, 0f, new Color(0.8f, 1f, 0.9f), "Two colours that never quite mix. Like home, after moving."),
        ("Amethyst", "Amethyst", 30, new Vector3(0f, 0.5f, 1f), 0.75f, 0f, new Color(0.9f, 0.8f, 1f), "Violet, the colour of the sky the night before you left."),
        ("Pyrite", "Pyrite", 35, new Vector3(0f, 0.3f, 0.9f), 0.6f, 0.9f, new Color(1f, 0.92f, 0.6f), "Fool's gold. It looked like everything, once."),
        ("Opal", "Opal", 70, new Vector3(0f, 0.05f, 0.4f), 0.8f, 0f, new Color(0.85f, 0.95f, 1f), "All the colours at once, if you hold it to the light."),
    };

    static MineralData[] BuildMinerals()
    {
        EnsureFolder(MineralDir);
        var list = new List<MineralData>();
        for (int i = 0; i < MineralDefs.Length; i++)
        {
            var d = MineralDefs[i];
            var model = MeshyAsset(d.folder, d.smooth, d.metal);
            if (model == null) continue;
            var data = LoadOrCreate(MineralDir + "/" + (i + 1).ToString("00") + " " + d.title + ".asset", () =>
            {
                var m = ScriptableObject.CreateInstance<MineralData>();
                m.title = d.title;
                m.line = d.line;
                m.value = d.value;
                m.weightByStratum = d.weights;
                m.glint = d.glint;
                return m;
            });
            if (data.model != model) { data.model = model; EditorUtility.SetDirty(data); }
            list.Add(data);
        }
        report.AppendLine("  " + list.Count + " minerals");
        return list.ToArray();
    }

    // A slow, occasional sparkle.
    static ParticleSystem BuildGlintPrefab(Material dust)
    {
        string path = PrefabDir + "/Mineral Glint.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) { Skipped(path); return existing.GetComponent<ParticleSystem>(); }

        var mat = LoadOrCreate(MatDir + "/Glint.mat", () =>
        {
            var m = new Material(dust);
            m.SetFloat("_Blend", 2f);   // additive
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetColor("_BaseColor", new Color(1f, 0.95f, 0.85f, 1f));
            return m;
        });

        var go = new GameObject("Mineral Glint");
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = 0.7f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.035f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 4;
        var emission = ps.emission;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.04f;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        Created(path);
        return prefab.GetComponent<ParticleSystem>();
    }

    static GameObject BuildMineralField(DigVolume volume, DigPath path, MineralData[] kinds, ParticleSystem glint)
    {
        var go = new GameObject("Minerals");
        var m = go.AddComponent<DigMinerals>();
        m.volume = volume;
        m.path = path;
        m.kinds = kinds;
        m.glintPrefab = glint;
        return go;
    }

    // The cat's price board: a large sheet of warm paper on the right of the screen (the cat
    // stays in view on the left). Coins at the top left, Close at the top right, two tabs:
    //   "Sell your finds": a tile per kind of stone in the bag (picture, name, count and
    //     value, Sell; its line shows under the mouse), and Sell All with the total.
    //   "For the next dig": a card per spade, bag and lantern tier (picture, name, a line,
    //     Buy and its price, or "Yours").
    // The cat's words run along the bottom. DigShop fills it; DigShopTile lifts the tiles.
    static void BuildShopUi(Transform root, DigHud hud, TMP_FontAsset font, TMP_FontAsset italic)
    {
        const float W = 1240f, H = 1030f;
        var node = UiNode("Shop", root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-36f, 0f), new Vector2(W, H));
        node.pivot = new Vector2(1f, 0.5f);
        hud.shopGroup = node.gameObject.AddComponent<CanvasGroup>();
        hud.shopGroup.alpha = 0f;
        hud.shopGroup.blocksRaycasts = false;
        hud.shopGroup.interactable = false;
        var paper = Paper("Board", node, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W - 20f, H - 20f));
        paper.color = new Color(0.93f, 0.86f, 0.72f, 0.98f);
        var p = paper.transform;
        var top = new Vector2(0.5f, 1f);
        var topLeft = new Vector2(0f, 1f);

        var title = Ink("Title", p, font, 36f, TextAlignmentOptions.Center, "The Shop", top, new Vector2(0f, -26f), new Vector2(500f, 52f), 0.8f);
        title.characterSpacing = 4f;
        hud.shopCoins = Ink("Coins", p, font, 34f, TextAlignmentOptions.Left, "0 coins", topLeft, new Vector2(40f, -24f), new Vector2(320f, 50f), 0.9f);
        hud.shopCoinsDelta = Ink("Coins Delta", p, italic, 24f, TextAlignmentOptions.Left, "", topLeft, new Vector2(44f, -70f), new Vector2(220f, 30f), 0.85f);
        hud.shopClose = ShopButton("Close", p, new Vector2(1f, 1f), new Vector2(-28f, -26f), new Vector2(130f, 48f), font, "Close", out _);
        ((RectTransform)hud.shopClose.transform).pivot = new Vector2(1f, 1f);

        hud.shopTabSell = ShopTab("Tab Sell", p, new Vector2(-160f, -92f), font, "Sell your finds");
        hud.shopTabBuy = ShopTab("Tab Buy", p, new Vector2(160f, -92f), font, "For the next dig");

        // Pages: the area under the tabs, 1140 x 760, from the top left.
        const float PageW = 1140f, PageH = 760f, Gap = 14f;
        var sell = UiNode("Sell Page", p, topLeft, topLeft, new Vector2(40f, -158f), new Vector2(PageW, PageH));
        hud.shopSellPage = sell.gameObject;
        const int Cols = 6;
        float tileW = Mathf.Floor((PageW - (Cols - 1) * Gap) / Cols), tileH = 292f;
        hud.shopSellTiles = new DigHud.ShopItem[12];
        for (int i = 0; i < hud.shopSellTiles.Length; i++)
        {
            var pos = new Vector2((i % Cols) * (tileW + Gap), -(i / Cols) * (tileH + 16f));
            hud.shopSellTiles[i] = ShopItemUi("Stone " + (i + 1), sell, pos, new Vector2(tileW, tileH), 150f, true, hud.shopGroup, font, italic);
        }
        hud.shopEmpty = Ink("Empty", sell, italic, 30f, TextAlignmentOptions.Center, "Your bag is empty. Dig a little, then come back.",
            new Vector2(0f, 1f), new Vector2(0f, -240f), new Vector2(PageW, 60f), 0.7f);
        hud.shopSellTotal = Ink("Total", sell, italic, 28f, TextAlignmentOptions.Left, "6 stones in your bag, worth 137 coins.",
            topLeft, new Vector2(4f, -2 * (tileH + 16f) - 18f), new Vector2(760f, 56f), 0.85f);
        hud.shopSellAll = ShopButton("Sell All", sell, topLeft, new Vector2(PageW - 300f, -2 * (tileH + 16f) - 18f), new Vector2(300f, 56f), font, "Sell all", out _);
        ((RectTransform)hud.shopSellAll.transform).pivot = topLeft;

        var buy = UiNode("Buy Page", p, topLeft, topLeft, new Vector2(40f, -158f), new Vector2(PageW, PageH));
        hud.shopBuyPage = buy.gameObject;
        const float LabelW = 150f, cardH = 244f;
        float cardW = Mathf.Floor((PageW - LabelW - 3 * Gap) / 4f);
        DigHud.ShopItem[] Row(string label, int row, int count)
        {
            float y = -row * (cardH + Gap);
            Ink(label + " Label", buy, italic, 30f, TextAlignmentOptions.Left, label, topLeft, new Vector2(4f, y - cardH / 2f + 26f), new Vector2(LabelW - 8f, 52f), 0.7f);
            var cards = new DigHud.ShopItem[count];
            for (int i = 0; i < count; i++)
                cards[i] = ShopItemUi(label + " " + (i + 1), buy, new Vector2(LabelW + i * (cardW + Gap), y), new Vector2(cardW, cardH), 108f, false, hud.shopGroup, font, italic);
            return cards;
        }
        hud.shopSpades = Row("Spades", 0, 4);
        hud.shopBags = Row("Bags", 1, 4);
        hud.shopLanterns = Row("Lanterns", 2, 3);
        hud.shopBuyPage.SetActive(false);

        // The cat's words along the bottom, beside a small portrait of it.
        hud.shopCat = Ink("Cat", p, italic, 28f, TextAlignmentOptions.Center, "“Mrrp.”", new Vector2(0.5f, 0f), new Vector2(50f, 20f), new Vector2(980f, 56f), 0.85f);
        var portrait = CatPortrait();
        if (portrait != null)
        {
            var frame = UiNode("Cat Portrait", p, Vector2.zero, Vector2.zero, new Vector2(36f, 10f), new Vector2(84f, 84f));
            var img = frame.gameObject.AddComponent<Image>();
            img.sprite = portrait;
            img.preserveAspect = true;
            img.raycastTarget = false;
            var edge = frame.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(InkColor.r, InkColor.g, InkColor.b, 0.35f);
            edge.effectDistance = new Vector2(1.5f, -1.5f);
        }
    }

    // A tab: a button whose colour DigShop sets (dark for the open page, pale otherwise).
    static Button ShopTab(string name, Transform parent, Vector2 pos, TMP_FontAsset font, string text)
    {
        var tab = ShopButton(name, parent, new Vector2(0.5f, 1f), pos, new Vector2(300f, 50f), font, text, out _);
        ((RectTransform)tab.transform).pivot = new Vector2(0.5f, 1f);
        var colors = tab.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.9f, 0.86f, 0.8f);
        colors.pressedColor = new Color(0.75f, 0.7f, 0.64f);
        colors.selectedColor = Color.white;
        tab.colors = colors;
        return tab;
    }

    static readonly Color ShopTileColor = new Color(0.97f, 0.93f, 0.84f);

    // An item tile (selling) or card (buying), `size` px, its top left at `pos` in `parent`.
    // The root catches the mouse; everything visible sits on "Lift", which DigShopTile
    // raises under the mouse. Selling tiles show the stone's line over the picture on hover;
    // cards always show theirs.
    static DigHud.ShopItem ShopItemUi(string name, Transform parent, Vector2 pos, Vector2 size, float imageH, bool sellTile,
                                      CanvasGroup board, TMP_FontAsset font, TMP_FontAsset italic)
    {
        var topLeft = new Vector2(0f, 1f);
        var top = new Vector2(0.5f, 1f);
        var bottom = new Vector2(0.5f, 0f);
        var root = UiNode(name, parent, topLeft, topLeft, pos, size);
        var hit = root.gameObject.AddComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0f);   // invisible, but the mouse finds it
        var item = new DigHud.ShopItem { root = root.gameObject, group = root.gameObject.AddComponent<CanvasGroup>() };

        var lift = UiNode("Lift", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
        var card = lift.gameObject.AddComponent<Image>();
        card.color = ShopTileColor;
        card.raycastTarget = false;
        var shadow = lift.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.2f, 0.12f, 0.05f, 0.28f);
        shadow.effectDistance = new Vector2(3f, -4f);
        var edge = lift.gameObject.AddComponent<Outline>();
        edge.effectColor = new Color(InkColor.r, InkColor.g, InkColor.b, 0.18f);
        edge.effectDistance = new Vector2(1f, -1f);

        var art = UiNode("Picture", lift, top, top, new Vector2(0f, -8f), new Vector2(size.x - 14f, imageH));
        item.image = art.gameObject.AddComponent<Image>();
        item.image.preserveAspect = true;
        item.image.raycastTarget = false;

        float y = -8f - imageH - 2f;
        item.title = Ink("Name", lift, font, sellTile ? 25f : 24f, TextAlignmentOptions.Center, "Quartz", top, new Vector2(0f, y), new Vector2(size.x - 10f, 32f), 0.95f);
        item.title.textWrappingMode = TextWrappingModes.NoWrap;
        item.title.overflowMode = TextOverflowModes.Ellipsis;
        y -= 32f;
        CanvasGroup reveal = null;
        if (sellTile)
        {
            item.detail = Ink("Count", lift, font, 21f, TextAlignmentOptions.Center, "×3   4 each", top, new Vector2(0f, y), new Vector2(size.x - 10f, 28f), 0.95f);
            // The stone's line, over the picture, only under the mouse.
            var note = UiNode("Line", lift, top, top, new Vector2(0f, -6f), new Vector2(size.x - 10f, imageH + 4f));
            var notePaper = note.gameObject.AddComponent<Image>();
            notePaper.color = new Color(ShopTileColor.r, ShopTileColor.g, ShopTileColor.b, 0.94f);
            notePaper.raycastTarget = false;
            reveal = note.gameObject.AddComponent<CanvasGroup>();
            reveal.alpha = 0f;
            reveal.blocksRaycasts = false;
            item.line = Text("Text", note, italic, italic.material, 19f, TextAlignmentOptions.Center, "Clear, like the mornings before school.", Vector2.zero, size, stretch: true);
            item.line.color = InkColor;
            ((RectTransform)item.line.transform).offsetMin = new Vector2(8f, 4f);
            ((RectTransform)item.line.transform).offsetMax = new Vector2(-8f, -4f);
        }
        else
        {
            item.line = Ink("Line", lift, italic, 18f, TextAlignmentOptions.Top, "Worn smooth where small hands held it.", top, new Vector2(0f, y + 2f), new Vector2(size.x - 18f, 52f), 0.8f);
            item.line.lineSpacing = -6f;
        }

        item.button = ShopButton("Button", lift, bottom, new Vector2(0f, 10f), new Vector2(size.x - 22f, 40f), font, sellTile ? "Sell" : "Buy", out var label);
        ((RectTransform)item.button.transform).pivot = bottom;
        item.buttonLabel = label;
        item.buttonLabel.fontSize = 22f;
        item.stamp = Ink("Stamp", lift, italic, 26f, TextAlignmentOptions.Center, "Yours", bottom, new Vector2(0f, 12f), new Vector2(size.x - 22f, 38f), 0.75f);
        item.stamp.gameObject.SetActive(false);

        var tile = root.gameObject.AddComponent<DigShopTile>();
        tile.lift = lift;
        tile.shadow = shadow;
        tile.reveal = reveal;
        tile.board = board;
        return item;
    }

    // A warm, ink-coloured button: lighter on hover, darker when pressed, faded when it
    // cannot be used (not enough coins).
    static Button ShopButton(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, TMP_FontAsset font, string text, out TMP_Text label)
    {
        var node = UiNode(name, parent, anchor, anchor, pos, size);
        var img = node.gameObject.AddComponent<Image>();
        img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        img.type = Image.Type.Sliced;
        img.color = Color.white;
        img.raycastTarget = true;
        var button = node.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        var colors = button.colors;
        colors.normalColor = new Color(0.42f, 0.29f, 0.19f);
        colors.highlightedColor = new Color(0.62f, 0.44f, 0.27f);
        colors.pressedColor = new Color(0.30f, 0.20f, 0.13f);
        colors.selectedColor = colors.normalColor;
        colors.disabledColor = new Color(0.55f, 0.5f, 0.45f, 0.45f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        var nav = button.navigation;
        nav.mode = Navigation.Mode.None;   // no keyboard focus ring; the mouse is the way
        button.navigation = nav;
        label = Text("Label", node, font, font.material, 24f, TextAlignmentOptions.Center, text, Vector2.zero, size, stretch: true);
        label.color = new Color(0.98f, 0.94f, 0.86f);
        return button;
    }
}
