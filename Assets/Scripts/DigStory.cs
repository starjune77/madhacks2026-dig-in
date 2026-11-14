using UnityEngine;
using UnityEngine.InputSystem;

// The story layer on top of digging: memory cards, a letter after the last memory of
// each stratum, and the ending. Sits on "Game" next to DigGame. While anything is open,
// the player cannot move or dig.
public class DigStory : MonoBehaviour
{
    public DigGame game;
    public DigHud hud;

    [Tooltip("One per stratum: Childhood, School Days, Growing Up.")]
    public LetterData[] letters;

    [Tooltip("The words under the blank page.")]
    public LetterData ending;

    [Header("Ending pacing (seconds)")]
    public float endingInterval = 2.8f;
    public float endingHold = 4f;
    public float fadeOut = 1.8f;
    public float blackHold = 1.2f;
    public float fadeIn = 2.5f;

    [Tooltip("Seconds an overlay ignores input after opening, so the click that opened it does not close it.")]
    public float inputGrace = 0.35f;

    [Header("Letter pacing (seconds)")]
    public float lineDelay = 0.8f;
    public float lineInterval = 2.2f;
    public float lineFade = 1.4f;

    enum Mode { None, Card, Letter, Ending }
    enum EndingPhase { Words, FadeOut, Black, FadeIn }
    EndingPhase phase;
    float phaseTime;
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

    public void PlayEnding()
    {
        hud.HideCard();
        hud.CloseLines(hud.letter);
        hud.OpenLines(hud.ending, "", ending != null ? ending.lines : new string[0]);
        revealTime = 0f;
        revealDone = false;
        phase = EndingPhase.Words;
        phaseTime = 0f;
        Open(Mode.Ending);
    }

    void UpdateEnding()
    {
        phaseTime += Time.deltaTime;
        switch (phase)
        {
            case EndingPhase.Words:
                revealTime += Time.deltaTime;
                bool wasDone = revealDone;
                revealDone = hud.RevealLines(hud.ending, revealTime, lineDelay + 1.5f, endingInterval, lineFade + 0.6f);
                if (revealDone && !wasDone) phaseTime = 0f;
                if (revealDone && phaseTime >= endingHold) Next(EndingPhase.FadeOut);
                break;
            case EndingPhase.FadeOut:
                hud.SetFade(Mathf.Clamp01(phaseTime / fadeOut));
                if (phaseTime >= fadeOut)
                {
                    hud.CloseLines(hud.ending);
                    game.FinishEnding();
                    Next(EndingPhase.Black);
                }
                break;
            case EndingPhase.Black:
                hud.SetFade(1f);
                if (phaseTime >= blackHold) Next(EndingPhase.FadeIn);
                break;
            case EndingPhase.FadeIn:
                hud.SetFade(1f - Mathf.Clamp01(phaseTime / fadeIn));
                if (phaseTime >= fadeIn) Close();
                break;
        }
    }

    void Next(EndingPhase p)
    {
        phase = p;
        phaseTime = 0f;
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

        if (mode == Mode.Ending)
        {
            UpdateEnding();
            // A click only hurries the words; the fade and return always play out.
            if (phase == EndingPhase.Words && !revealDone && Time.unscaledTime - openedAt > 1.5f && ContinuePressed())
                revealTime = 1000f;
            return;
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
