using UnityEngine;
using UnityEngine.InputSystem;

// Demo mode for judging. F12 toggles it; while it is on:
//   F1  surface, Toy Shovel (start of Childhood)
//   F2  start of School Days on the life path, Garden Shovel
//   F3  start of Growing Up on the life path, Steel Shovel
//   F4  the end of the path, next to the blank page (the ending)
//   F5  the cat's shop, with a bag of stones to sell
// Sits on "Game" next to DigGame.
public class DigDemo : MonoBehaviour
{
    public DigGame game;
    public DigHud hud;
    [Tooltip("Start with demo mode already on.")]
    public bool startInDemoMode;

    bool demoMode;

    void Start()
    {
        demoMode = startInDemoMode;
        hud.SetDemo(demoMode);

        // Built player, "-benchmark <log file>": visits the yard and each stratum, logs the
        // frame rate at each, and quits. Used to check a build launches and keeps 60 fps.
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-benchmark");
        if (i >= 0) StartCoroutine(Benchmark(i + 1 < args.Length ? args[i + 1] : "benchmark.txt"));
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

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.f12Key.wasPressedThisFrame)
        {
            demoMode = !demoMode;
            hud.SetDemo(demoMode);
        }
        if (!demoMode || game.InputLocked) return;

        if (kb.f1Key.wasPressedThisFrame) game.DemoJump(0);
        else if (kb.f2Key.wasPressedThisFrame) game.DemoJump(1);
        else if (kb.f3Key.wasPressedThisFrame) game.DemoJump(2);
        else if (kb.f4Key.wasPressedThisFrame) game.DemoEnding();
        else if (kb.f5Key.wasPressedThisFrame) game.DemoShop();
    }
}
