using TMPro;
using UnityEngine;

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

    public float fadeSpeed = 3f;

    Color scrapBaseColor;
    float scrapFlash;
    float messageTimer;
    bool promptVisible;
    float time;

    void Awake()
    {
        if (scrapText != null) scrapBaseColor = scrapText.color;
        if (promptGroup != null) promptGroup.alpha = 0f;
        if (messageGroup != null) messageGroup.alpha = 0f;
        if (controlsGroup != null) controlsGroup.alpha = 1f;
    }

    // upgradeCost < 0 means there is nothing left to upgrade to.
    public void SetStatus(float depth, string shovelName, int scrap, int upgradeCost)
    {
        if (depthText != null) depthText.text = depth.ToString("0.0") + " m";
        if (shovelText != null) shovelText.text = shovelName;
        if (scrapText != null)
            scrapText.text = upgradeCost >= 0 ? scrap + " / " + upgradeCost + " scrap" : scrap + " scrap";
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

    public void ShowMemory(string title, string line)
    {
        // Placeholder until the illustrated card exists.
        ShowMessage("<size=70%><smallcaps>" + title + "</smallcaps></size>\n<i>“" + line + "”</i>", 8f);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        time += dt;

        if (messageTimer > 0f) messageTimer -= dt;
        Fade(messageGroup, messageTimer > 0f);
        Fade(promptGroup, promptVisible);
        Fade(controlsGroup, time < controlsSeconds, 0.5f);

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
