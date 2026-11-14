using UnityEngine;
using UnityEngine.InputSystem;

// The story layer on top of digging: memory cards (and, later, letters and the ending).
// Sits on "Game" next to DigGame. While anything is open, the player cannot move or dig.
public class DigStory : MonoBehaviour
{
    public DigGame game;
    public DigHud hud;

    [Tooltip("Seconds an overlay ignores input after opening, so the click that opened it does not close it.")]
    public float inputGrace = 0.35f;

    enum Mode { None, Card }
    Mode mode;
    float openedAt;
    int unlockFrame;

    // True while an overlay is open, and for one frame after it closes (so the closing
    // click or E press is not also read as a dig or a workbench use).
    public bool Locked => mode != Mode.None || Time.frameCount <= unlockFrame;

    public void ShowCard(MemoryData data)
    {
        hud.ShowCard(data);
        Open(Mode.Card);
    }

    void Open(Mode m)
    {
        mode = m;
        openedAt = Time.unscaledTime;
    }

    void Close()
    {
        mode = Mode.None;
        unlockFrame = Time.frameCount + 1;
    }

    void Update()
    {
        if (mode == Mode.None) return;
        if (Time.unscaledTime - openedAt < inputGrace || !ContinuePressed()) return;

        if (mode == Mode.Card)
        {
            hud.HideCard();
            Close();
        }
    }

    static bool ContinuePressed()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        return (mouse != null && mouse.leftButton.wasPressedThisFrame) ||
               (kb != null && (kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame));
    }
}
