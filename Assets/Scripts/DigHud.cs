using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Drives the HUD canvas (the HUD prefab). Layout and style live in the prefab;
// this only updates text and fades things in and out.
public class DigHud : MonoBehaviour
{
    [Header("Status (bottom left)")]
    public TMP_Text depthText;
    public TMP_Text shovelText;
    public TMP_Text scrapText;
    public Color scrapFlashColor = new Color(1f, 0.82f, 0.45f, 1f);

    [Header("Contextual prompt, e.g. at the workbench")]
    public TMP_Text promptText;
    public CanvasGroup promptGroup;

    [Header("Messages and memory lines")]
    public TMP_Text messageText;
    public CanvasGroup messageGroup;

    [Header("Controls, shown once at the start")]
    public CanvasGroup controlsGroup;
    public float controlsSeconds = 9f;

    [Header("Memory card")]
    public CanvasGroup cardGroup;
    public RectTransform cardPanel;
    public TMP_Text cardTitle;
    public TMP_Text cardLine;
    public Image cardIllustration;
    [Tooltip("Illustration background per stratum while a memory has no card art.")]
    public Color[] stratumTints =
    {
        new Color(0.93f, 0.80f, 0.62f),
        new Color(0.88f, 0.72f, 0.62f),
        new Color(0.78f, 0.78f, 0.80f),
    };

    // A full-screen panel whose lines fade in one by one (letters, the ending).
    [System.Serializable]
    public class LinePanel
    {
        public CanvasGroup group;
        public TMP_Text title;
        public TMP_Text[] lines;
        public TMP_Text hint;
        [System.NonSerialized] public bool visible;
        [System.NonSerialized] public int count;
    }

    [Header("Letters")]
    public LinePanel letter = new LinePanel();

    [Header("Ending")]
    public LinePanel ending = new LinePanel();
    [Tooltip("Full-screen black, for fading out and back in.")]
    public CanvasGroup fadeGroup;

    [Header("The cat's shop")]
    public CanvasGroup shopGroup;
    public TMP_Text shopCat;
    public TMP_Text shopCoins;
    public TMP_Text shopEmpty;
    [Tooltip("One row per kind of stone in the bag (unused rows are hidden).")]
    public ShopRow[] shopRows;
    public ShopRow shopSellAll;
    [Tooltip("Spade, bag, lantern.")]
    public ShopRow[] shopUpgrades;
    public Button shopClose;

    [System.Serializable]
    public class ShopRow
    {
        public GameObject root;
        public TMP_Text label;
        public Button button;
        public TMP_Text buttonLabel;
    }

    [Header("Demo mode badge")]
    public CanvasGroup demoGroup;

    public float fadeSpeed = 3f;

    Color scrapBaseColor;
    float scrapFlash;
    float messageTimer;
    bool promptVisible;
    bool cardVisible;
    bool shopVisible;
    float time;

    void Awake()
    {
        if (scrapText != null) scrapBaseColor = scrapText.color;
        if (promptGroup != null) promptGroup.alpha = 0f;
        if (messageGroup != null) messageGroup.alpha = 0f;
        if (controlsGroup != null) controlsGroup.alpha = 1f;
        if (cardGroup != null) cardGroup.alpha = 0f;
        if (letter.group != null) letter.group.alpha = 0f;
        if (ending.group != null) ending.group.alpha = 0f;
        if (fadeGroup != null) fadeGroup.alpha = 0f;
        if (shopGroup != null) shopGroup.alpha = 0f;
    }

    // Hides the status corner, the crosshair and the badge (the bedroom at the end).
    public void SetHidden(bool hidden)
    {
        foreach (var name in new[] { "Status", "Crosshair", "Prompt", "Demo Badge", "Controls" })
        {
            var t = transform.Find(name);
            if (t != null) t.gameObject.SetActive(!hidden);
        }
    }

    public void SetDemo(bool on)
    {
        if (demoGroup != null) demoGroup.alpha = on ? 1f : 0f;
    }

    // 0 = clear, 1 = black.
    public void SetFade(float alpha)
    {
        if (fadeGroup != null) fadeGroup.alpha = alpha;
    }

    public void SetStatus(float depth, string shovelName, int coins, int bagCount, int bagSize)
    {
        if (depthText != null) depthText.text = depth.ToString("0.0") + " m";
        if (shovelText != null) shovelText.text = shovelName;
        if (scrapText != null) scrapText.text = coins + " coins    <alpha=#AA>bag " + bagCount + " / " + bagSize;
    }

    // The cat's price board (DigShop fills it).
    public void ShowShop(bool visible)
    {
        shopVisible = visible;
        if (visible) messageTimer = 0f;
        if (shopGroup != null)
        {
            shopGroup.blocksRaycasts = visible;
            shopGroup.interactable = visible;
        }
    }

    public void FlashScrap()
    {
        scrapFlash = 1f;
    }

    // Call every frame; pass null to hide.
    public void SetPrompt(string text)
    {
        promptVisible = !string.IsNullOrEmpty(text);
        if (promptVisible && promptText != null && promptText.text != text) promptText.text = text;
    }

    public void ShowMessage(string text, float seconds = 3f)
    {
        if (messageText != null) messageText.text = text;
        messageTimer = seconds;
    }

    public void ShowCard(MemoryData data)
    {
        cardVisible = true;
        if (cardTitle != null) cardTitle.text = data.title;
        if (cardLine != null) cardLine.text = "“" + data.line + "”";
        if (cardIllustration != null)
        {
            cardIllustration.sprite = data.card;
            cardIllustration.preserveAspect = true;
            cardIllustration.color = data.card != null ? Color.white
                : stratumTints[Mathf.Clamp(data.stratum, 0, stratumTints.Length - 1)];
        }
        if (cardPanel != null) cardPanel.localScale = Vector3.one * 0.94f;
        messageTimer = 0f;   // the card replaces any message
    }

    public void HideCard()
    {
        cardVisible = false;
    }

    public void OpenLines(LinePanel panel, string title, string[] text)
    {
        panel.visible = true;
        panel.count = Mathf.Min(text.Length, panel.lines.Length);
        if (panel.title != null)
        {
            panel.title.text = title ?? "";
            panel.title.gameObject.SetActive(!string.IsNullOrEmpty(title));
        }
        for (int i = 0; i < panel.lines.Length; i++)
        {
            bool used = i < panel.count;
            panel.lines[i].gameObject.SetActive(used);
            panel.lines[i].text = used ? text[i] : "";
            panel.lines[i].alpha = 0f;
        }
        if (panel.hint != null) panel.hint.alpha = 0f;
        messageTimer = 0f;
    }

    // Shows the lines as of `t` seconds after opening. Returns true once all are fully in.
    public bool RevealLines(LinePanel panel, float t, float delay, float interval, float fade)
    {
        for (int i = 0; i < panel.count; i++)
            panel.lines[i].alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - delay - i * interval) / fade));
        float end = delay + (panel.count - 1) * interval + fade;
        if (panel.hint != null) panel.hint.alpha = 0.5f * Mathf.Clamp01((t - end - 0.5f) / 0.8f);
        return t >= end;
    }

    public void CloseLines(LinePanel panel)
    {
        panel.visible = false;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        time += dt;

        if (messageTimer > 0f) messageTimer -= dt;
        Fade(messageGroup, messageTimer > 0f);
        Fade(promptGroup, promptVisible);
        Fade(controlsGroup, time < controlsSeconds, 0.5f);
        Fade(cardGroup, cardVisible, 1.5f);
        Fade(shopGroup, shopVisible, 2f);
        Fade(letter.group, letter.visible, 0.6f);
        Fade(ending.group, ending.visible, 0.35f);
        if (cardPanel != null && cardVisible)
            cardPanel.localScale = Vector3.one * Mathf.MoveTowards(cardPanel.localScale.x, 1f, dt * 0.5f);

        if (scrapText != null && scrapFlash > 0f)
        {
            scrapFlash = Mathf.Max(0f, scrapFlash - dt * 2f);
            scrapText.color = Color.Lerp(scrapBaseColor, scrapFlashColor, scrapFlash);
        }
    }

    void Fade(CanvasGroup group, bool visible, float speedScale = 1f)
    {
        if (group == null) return;
        group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, fadeSpeed * speedScale * Time.deltaTime);
    }
}
