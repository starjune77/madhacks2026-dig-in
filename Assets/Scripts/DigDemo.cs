using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Demo and review modes. F12 cycles: off -> Demo -> Review -> off (a badge shows which).
//
// Demo (for judging):
//   F1  surface, Old Spade (start of Childhood)
//   F2  start of School Days on the life path, Garden Spade
//   F3  start of Growing Up on the life path, Steel Spade
//   F4  the end of the path, next to the blank page (the ending)
//   F5  the cat's shop, with a bag of stones to sell
//
// Review (to check every story moment): each jump reloads the scene, so it starts clean
// and the same key replays the moment.
//   F1-F9  memory 1-9, about one scoop away, with the right spade and every earlier memory
//          found (so memories 3, 6 and 9 bring their letter after the card)
//   F10    the shop counter, a full bag of mixed stones, coins for the next upgrade
//   F11    the bottom of the path, facing the blank page (the whole ending)
// Sits on "Game" next to DigGame.
public class DigDemo : MonoBehaviour
{
    public DigGame game;
    public DigHud hud;
    [Tooltip("Start with demo mode already on.")]
    public bool startInDemoMode;

    enum Mode { Off, Demo, Review }
    static Mode mode = Mode.Off;
    static bool modeSet;
    static int pendingReview = -1;   // survives the scene reload

    void Start()
    {
        if (!modeSet) { mode = startInDemoMode ? Mode.Demo : Mode.Off; modeSet = true; }
        ShowBadge();

        // Built player, "-benchmark <log file>": visits the yard and each stratum, logs the
        // frame rate at each, and quits. Used to check a build launches and keeps 60 fps.
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-benchmark");
        if (i >= 0) StartCoroutine(Benchmark(i + 1 < args.Length ? args[i + 1] : "benchmark.txt"));

        if (pendingReview >= 0)
        {
            int jump = pendingReview;
            pendingReview = -1;
            StartCoroutine(ReviewAfterLoad(jump));
        }
    }

    void ShowBadge()
    {
        hud.SetDemo(mode == Mode.Demo);
        hud.SetReview(mode == Mode.Review);
    }

    System.Collections.IEnumerator ReviewAfterLoad(int jump)
    {
        // Let the ground, minerals and finds set themselves up first.
        yield return null;
        yield return new WaitForSecondsRealtime(0.3f);
        game.GiveSpade();
        if (jump < 9) game.ReviewMemory(jump);
        else if (jump == 9) game.ReviewShop();
        else game.DemoEnding();
    }

    System.Collections.IEnumerator Benchmark(string logPath)
    {
        var log = new System.Text.StringBuilder();
        log.AppendLine("DigIn benchmark " + System.DateTime.Now + "  " + Screen.width + "x" + Screen.height + "  " + SystemInfo.graphicsDeviceName);
        string[] names = { "yard vista", "School Days tunnel", "Growing Up tunnel", "the shop", "the end of the path" };
        for (int step = 0; step < names.Length; step++)
        {
            if (step == 1) game.DemoJump(1);
            else if (step == 2) game.DemoJump(2);
            else if (step == 3) game.DemoShop();
            else if (step == 4) game.DemoEnding();
            yield return new WaitForSeconds(2f);   // settle: shaders, streaming
            int frames = 0; float time = 0f, worst = 0f;
            while (time < 5f) { yield return null; frames++; time += Time.unscaledDeltaTime; worst = Mathf.Max(worst, Time.unscaledDeltaTime); }
            log.AppendLine(names[step] + ": " + (frames / time).ToString("0") + " fps average, worst frame " + (worst * 1000f).ToString("0.0") + " ms");
        }
        System.IO.File.WriteAllText(logPath, log.ToString());
        Application.Quit();
    }

    // Review: reload the scene and jump once it is up (0-8 memories, 9 shop, 10 ending).
    public static void Review(int jump)
    {
        mode = Mode.Review;
        modeSet = true;
        pendingReview = jump;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.f12Key.wasPressedThisFrame)
        {
            mode = mode == Mode.Off ? Mode.Demo : mode == Mode.Demo ? Mode.Review : Mode.Off;
            ShowBadge();
        }
        // The review panel steps aside while the shop board is open (it covers Close).
        hud.SetReview(mode == Mode.Review && !(game.shop != null && game.shop.IsOpen));
        if (mode == Mode.Off) return;

        if (mode == Mode.Demo)
        {
            if (game.InputLocked) return;
            if (kb.f1Key.wasPressedThisFrame) game.DemoJump(0);
            else if (kb.f2Key.wasPressedThisFrame) game.DemoJump(1);
            else if (kb.f3Key.wasPressedThisFrame) game.DemoJump(2);
            else if (kb.f4Key.wasPressedThisFrame) game.DemoEnding();
            else if (kb.f5Key.wasPressedThisFrame) game.DemoShop();
            return;
        }

        // Review jumps reload the scene, so they work even over a card, a letter or the ending.
        Key[] keys = { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11 };
        for (int k = 0; k < keys.Length; k++)
            if (kb[keys[k]].wasPressedThisFrame) { Review(k); return; }
    }
}
