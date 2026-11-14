using UnityEngine;
using UnityEngine.InputSystem;

// The story layer on top of digging: memory cards, and a letter after the last memory
// of each stratum. Sits on "Game" next to DigGame. While anything is open, the player
// cannot move or dig.
public class DigStory : MonoBehaviour
{
    public DigGame game;
    public DigHud hud;

    [Tooltip("One per stratum: Childhood, School Days, Growing Up.")]
    public LetterData[] letters;

    [Tooltip("Seconds an overlay ignores input after opening, so the click that opened it does not close it.")]
    public float inputGrace = 0.35f;

    [Header("Letter pacing (seconds)")]
    public float lineDelay = 0.8f;
    public float lineInterval = 2.2f;
    public float lineFade = 1.4f;

    enum Mode { None, Card, Letter }
    Mode mode;
    float openedAt;
    int unlockFrame;
    int cardStratum = -1;
    bool[] letterShown = new bool[3];
    float revealTime;
    bool revealDone;

    // True while an overlay is open, and for one frame after it closes (so the closing
    // click or E press is not also read as a dig or a workbench use).
    public bool Locked => mode != Mode.None || Time.frameCount <= unlockFrame;

    public void ShowCard(MemoryData data)
    {
        hud.ShowCard(data);
        cardStratum = data.stratum;
        Open(Mode.Card);
    }

    void ShowLetter(int stratum)
    {
        letterShown[stratum] = true;
        var l = letters[stratum];
        hud.OpenLines(hud.letter, l.title, l.lines);
        revealTime = 0f;
        revealDone = false;
        Open(Mode.Letter);
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
        if (mode == Mode.Letter)
        {
            revealTime += Time.deltaTime;
            revealDone = hud.RevealLines(hud.letter, revealTime, lineDelay, lineInterval, lineFade);
        }

        if (mode == Mode.None) return;
        if (Time.unscaledTime - openedAt < inputGrace || !ContinuePressed()) return;

        if (mode == Mode.Card)
        {
            hud.HideCard();
            Close();

            // The last memory of a stratum brings its letter.
            int s = cardStratum;
            if (letters != null && s >= 0 && s < letters.Length && !letterShown[s] && letters[s] != null && game.StratumComplete(s))
                ShowLetter(s);
        }
        else if (mode == Mode.Letter)
        {
            if (!revealDone)
            {
                revealTime = 1000f;   // first click shows the rest at once
                return;
            }
            hud.CloseLines(hud.letter);
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
