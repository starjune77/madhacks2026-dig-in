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
