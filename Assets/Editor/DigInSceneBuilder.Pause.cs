using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The pause menu and the How to Play note (DigPause), as pages from an old notebook: warm
// paper with faint ruled lines and a margin, Lora, ink-coloured words you can click. Behind
// them the game softly blurs and dims ("Pause Blur", a global volume DigPause fades in).
public static partial class DigInSceneBuilder
{
    static readonly Color RuleColor = new Color(0.45f, 0.55f, 0.68f, 0.16f);
    static readonly Color MarginColor = new Color(0.78f, 0.36f, 0.30f, 0.22f);

    static void BuildPauseUi(Transform root, DigHud hud, TMP_FontAsset font, TMP_FontAsset italic)
    {
        hud.pauseGroup = Overlay("Pause", root, new Color(0.06f, 0.04f, 0.02f, 0.32f));
        var page = NotebookPage("Page", hud.pauseGroup.transform, new Vector2(700f, 760f), -0.6f);
        var top = new Vector2(0.5f, 1f);

        // Main: the menu.
        var main = UiNode("Main", page, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 760f));
        hud.pauseMain = main.gameObject;
        var title = Ink("Title", main, font, 28f, TextAlignmentOptions.Center, "Paused", top, new Vector2(0f, -70f), new Vector2(500f, 40f), 0.6f);
        title.characterSpacing = 8f;
        title.fontStyle = FontStyles.SmallCaps;
        Ink("Subtitle", main, italic, 28f, TextAlignmentOptions.Center, "The yard will wait.", top, new Vector2(0f, -116f), new Vector2(560f, 40f), 0.7f);
        hud.pauseResume = WordButton("Resume", main, new Vector2(0f, -232f), font, "Resume");
        hud.pauseHowTo = WordButton("How to Play", main, new Vector2(0f, -320f), font, "How to Play");
        hud.pauseSettingsButton = WordButton("Settings", main, new Vector2(0f, -408f), font, "Settings");
        hud.pauseQuit = WordButton("Quit", main, new Vector2(0f, -496f), font, "Quit Game");
        Ink("Hint", main, italic, 22f, TextAlignmentOptions.Center, "Esc to go back to the yard", new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(560f, 32f), 0.45f);

        // Settings: volume and mouse sensitivity.
        var settings = UiNode("Settings", page, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 760f));
        hud.pauseSettings = settings.gameObject;
        var st = Ink("Title", settings, font, 28f, TextAlignmentOptions.Center, "Settings", top, new Vector2(0f, -70f), new Vector2(500f, 40f), 0.6f);
        st.characterSpacing = 8f;
        st.fontStyle = FontStyles.SmallCaps;
        Ink("Volume Label", settings, italic, 30f, TextAlignmentOptions.Left, "Volume", top, new Vector2(0f, -136f), new Vector2(460f, 40f), 0.85f);
        hud.pauseVolume = NotebookSlider("Volume", settings, new Vector2(0f, -180f));
        hud.pauseMusicOn = NotebookCheck("Music On", settings, new Vector2(-230f, -238f));
        Ink("Music Label", settings, italic, 30f, TextAlignmentOptions.Left, "Music", top, new Vector2(24f, -236f), new Vector2(412f, 40f), 0.85f);
        hud.pauseMusic = NotebookSlider("Music", settings, new Vector2(0f, -282f));
        hud.pauseEffectsOn = NotebookCheck("Effects On", settings, new Vector2(-230f, -340f));
        Ink("Effects Label", settings, italic, 30f, TextAlignmentOptions.Left, "Sound effects", top, new Vector2(24f, -338f), new Vector2(412f, 40f), 0.85f);
        hud.pauseEffects = NotebookSlider("Effects", settings, new Vector2(0f, -384f));
        Ink("Sensitivity Label", settings, italic, 30f, TextAlignmentOptions.Left, "Mouse sensitivity", top, new Vector2(0f, -442f), new Vector2(460f, 40f), 0.85f);
        hud.pauseSensitivity = NotebookSlider("Sensitivity", settings, new Vector2(0f, -486f));
        hud.pauseSettingsBack = WordButton("Back", settings, new Vector2(0f, -580f), font, "Back");
        settings.gameObject.SetActive(false);

        // Quit: asks once.
        var confirm = UiNode("Confirm", page, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 760f));
        hud.pauseConfirm = confirm.gameObject;
        Ink("Question", confirm, italic, 40f, TextAlignmentOptions.Center, "Leave the yard?", top, new Vector2(0f, -250f), new Vector2(560f, 56f), 0.9f);
        Ink("Note", confirm, italic, 26f, TextAlignmentOptions.Center, "The things you found won't be kept.", top, new Vector2(0f, -312f), new Vector2(560f, 40f), 0.6f);
        hud.pauseQuitNo = WordButton("Stay", confirm, new Vector2(-110f, -420f), font, "Stay");
        hud.pauseQuitYes = WordButton("Quit", confirm, new Vector2(110f, -420f), font, "Quit");
        confirm.gameObject.SetActive(false);
    }

    // How to Play: the controls with little key caps, and two soft lines.
    static void BuildHowToUi(Transform root, DigHud hud, TMP_FontAsset font, TMP_FontAsset italic)
    {
        hud.howToGroup = Overlay("How To Play", root, new Color(0.06f, 0.04f, 0.02f, 0.4f));
        var page = NotebookPage("Page", hud.howToGroup.transform, new Vector2(900f, 880f), 0.5f);
        var top = new Vector2(0.5f, 1f);
        var topLeft = new Vector2(0f, 1f);
        Ink("Title", page, font, 40f, TextAlignmentOptions.Center, "How to Play", top, new Vector2(0f, -58f), new Vector2(600f, 56f), 0.85f);

        var rows = new (string[] keys, string what)[]
        {
            (new[] { "W", "A", "S", "D" }, "walk"),
            (new[] { "Mouse" }, "look around"),
            (new[] { "Hold left click" }, "dig"),
            (new[] { "E" }, "pick things up, pet the cat"),
            (new[] { "R" }, "to the surface and back"),
            (new[] { "Esc" }, "menu"),
        };
        for (int r = 0; r < rows.Length; r++)
        {
            float y = -158f - r * 66f;
            float x = 130f;
            foreach (var k in rows[r].keys) x += KeyCap(page, k, new Vector2(x, y), font) + 8f;
            Ink("What " + (r + 1), page, italic, 30f, TextAlignmentOptions.Left, rows[r].what, topLeft, new Vector2(420f, y - 3f), new Vector2(440f, 46f), 0.85f);
        }

        Ink("Line 1", page, italic, 28f, TextAlignmentOptions.Center,
            "Dig where the ground feels soft. Things are buried here, and some are worth keeping.",
            top, new Vector2(0f, -578f), new Vector2(700f, 80f), 0.8f);
        Ink("Line 2", page, italic, 28f, TextAlignmentOptions.Center,
            "The cat by the house will trade for the stones you find.",
            top, new Vector2(0f, -672f), new Vector2(700f, 44f), 0.8f);
        hud.howToHint = Ink("Hint", page, font, 22f, TextAlignmentOptions.Center, "Click to begin", new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(600f, 32f), 0.5f);
        hud.howToGroup.blocksRaycasts = false;
    }

    // A sheet of notebook paper: warm, faintly ruled, with a margin line, a little askew.
    static RectTransform NotebookPage(string name, Transform parent, Vector2 size, float tilt)
    {
        var paper = Paper(name, parent, new Vector2(0.5f, 0.5f), Vector2.zero, size);
        var rt = (RectTransform)paper.transform;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.localRotation = Quaternion.Euler(0f, 0f, tilt);
        for (float y = -110f; y > -size.y + 30f; y -= 46f)
        {
            var line = UiNode("Rule", rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(size.x - 40f, 2f));
            var img = line.gameObject.AddComponent<Image>();
            img.color = RuleColor;
            img.raycastTarget = false;
        }
        var margin = UiNode("Margin", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(76f, 0f), new Vector2(2f, size.y));
        var m = margin.gameObject.AddComponent<Image>();
        m.color = MarginColor;
        m.raycastTarget = false;
        return rt;
    }

    // Words you can click: ink at rest, warmer under the mouse, darker when pressed.
    static Button WordButton(string name, Transform parent, Vector2 pos, TMP_FontAsset font, string text)
    {
        var top = new Vector2(0.5f, 1f);
        var node = UiNode(name, parent, top, top, pos, new Vector2(360f, 64f));
        var hit = node.gameObject.AddComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0f);
        var label = Text("Label", node, font, font.material, 40f, TextAlignmentOptions.Center, text, Vector2.zero, node.sizeDelta, stretch: true);
        label.color = Color.white;
        var button = node.gameObject.AddComponent<Button>();
        button.targetGraphic = label;
        var colors = button.colors;
        colors.normalColor = InkColor;
        colors.highlightedColor = new Color(0.70f, 0.38f, 0.16f);
        colors.pressedColor = new Color(0.40f, 0.22f, 0.10f);
        colors.selectedColor = InkColor;
        colors.disabledColor = new Color(InkColor.r, InkColor.g, InkColor.b, 0.4f);
        colors.fadeDuration = 0.1f;
        button.colors = colors;
        var nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
        return button;
    }

    // A thin ink line with a round handle, 460 px wide.
    static Slider NotebookSlider(string name, Transform parent, Vector2 pos)
    {
        var top = new Vector2(0.5f, 1f);
        var node = UiNode(name, parent, top, top, pos, new Vector2(460f, 36f));
        var bg = Stretch("Track", node, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -3f), new Vector2(0f, 3f));
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.color = new Color(InkColor.r, InkColor.g, InkColor.b, 0.25f);
        var fillArea = Stretch("Fill Area", node, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -3f), new Vector2(0f, 3f));
        var fill = Stretch("Fill", fillArea, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.color = new Color(0.55f, 0.36f, 0.20f, 0.9f);
        var handleArea = Stretch("Handle Area", node, Vector2.zero, Vector2.one, new Vector2(14f, 0f), new Vector2(-14f, 0f));
        var handle = Stretch("Handle", handleArea, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(-14f, 4f), new Vector2(14f, -4f));
        var handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        handleImg.color = Color.white;

        var slider = node.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImg;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0.5f;
        var colors = slider.colors;
        colors.normalColor = InkColor;
        colors.highlightedColor = new Color(0.70f, 0.38f, 0.16f);
        colors.pressedColor = new Color(0.40f, 0.22f, 0.10f);
        colors.selectedColor = InkColor;
        slider.colors = colors;
        var nav = slider.navigation;
        nav.mode = Navigation.Mode.None;
        slider.navigation = nav;
        return slider;
    }

    // A little inked box with a dot in it when on, 34 px; `pos` is its top left, from the top centre.
    static Toggle NotebookCheck(string name, Transform parent, Vector2 pos)
    {
        var top = new Vector2(0.5f, 1f);
        var node = UiNode(name, parent, top, top, pos, new Vector2(34f, 34f));
        node.pivot = new Vector2(0f, 1f);
        node.anchoredPosition = pos;
        var box = node.gameObject.AddComponent<Image>();
        box.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        box.type = Image.Type.Sliced;
        box.color = Color.white;
        var edge = node.gameObject.AddComponent<Outline>();
        edge.effectColor = new Color(InkColor.r, InkColor.g, InkColor.b, 0.7f);
        edge.effectDistance = new Vector2(1.5f, -1.5f);
        var mark = Stretch("Mark", node, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -8f));
        var dot = mark.gameObject.AddComponent<Image>();
        dot.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        dot.color = new Color(0.55f, 0.36f, 0.20f);
        dot.raycastTarget = false;
        var toggle = node.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = box;
        toggle.graphic = dot;
        toggle.isOn = true;
        var colors = toggle.colors;
        colors.normalColor = new Color(0.99f, 0.96f, 0.9f);
        colors.highlightedColor = new Color(1f, 0.88f, 0.72f);
        colors.pressedColor = new Color(0.9f, 0.78f, 0.62f);
        colors.selectedColor = colors.normalColor;
        toggle.colors = colors;
        var nav = toggle.navigation;
        nav.mode = Navigation.Mode.None;
        toggle.navigation = nav;
        return toggle;
    }

    static RectTransform Stretch(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return rt;
    }

    // A small key cap with its label, its top left at `pos`. Returns its width.
    static float KeyCap(Transform parent, string key, Vector2 pos, TMP_FontAsset font)
    {
        float w = key.Length == 1 ? 46f : 30f + key.Length * 12.5f;
        var topLeft = new Vector2(0f, 1f);
        var node = UiNode("Key " + key, parent, topLeft, topLeft, pos, new Vector2(w, 46f));
        var img = node.gameObject.AddComponent<Image>();
        img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        img.type = Image.Type.Sliced;
        img.color = new Color(0.99f, 0.96f, 0.9f);
        img.raycastTarget = false;
        var edge = node.gameObject.AddComponent<Outline>();
        edge.effectColor = new Color(InkColor.r, InkColor.g, InkColor.b, 0.55f);
        edge.effectDistance = new Vector2(1.5f, -1.5f);
        var shadow = node.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(InkColor.r, InkColor.g, InkColor.b, 0.35f);
        shadow.effectDistance = new Vector2(0f, -3f);
        var t = Text("Label", node, font, font.material, 22f, TextAlignmentOptions.Center, key, Vector2.zero, node.sizeDelta, stretch: true);
        t.color = InkColor;
        return w;
    }

    // "Pause Blur": a global volume, off (weight 0) until DigPause fades it in. Everything
    // goes soft and a little dimmer and greyer.
    static void BuildPauseVolume(Scene scene)
    {
        string path = "Assets/Settings/DigIn Pause.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var dof = profile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(0f);
            dof.gaussianEnd.Override(2.5f);
            dof.gaussianMaxRadius.Override(1.5f);
            dof.highQualitySampling.Override(true);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.15f);
            color.saturation.Override(-12f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            Created(path);
        }
        var go = Root(scene, "Pause Blur", out bool created);
        var volume = go.GetComponent<Volume>();
        if (volume == null) volume = go.gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        volume.weight = 0f;
        volume.sharedProfile = profile;
        EditorUtility.SetDirty(volume);
    }
}
