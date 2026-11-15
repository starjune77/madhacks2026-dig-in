using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// The pause menu and the How to Play note. On "Game".
//
// Esc (when no shop, card or letter is open: those close first) pauses: time stops, the
// view softly blurs and dims behind a notebook page with Resume, How to Play, Settings
// (volume, mouse sensitivity) and Quit (asks once). Esc again, or Resume, goes back to the
// game; the cursor is locked again and the click never starts a swing.
//
// The very first time the game starts, the How to Play note comes up before you can move;
// one click (or a key) puts it away. The same note is in the pause menu.
public class DigPause : MonoBehaviour
{
    public DigGame game;
    public DigHud hud;
    [Tooltip("A global volume that blurs and dims the view; its weight follows the menu.")]
    public Volume blur;

    [Header("Settings")]
    public Vector2 sensitivityRange = new Vector2(0.03f, 0.35f);

    const string SeenKey = "DigIn.HowToSeen";
    const string VolumeKey = "DigIn.Volume";
    const string SensitivityKey = "DigIn.Sensitivity";
    const string MusicKey = "DigIn.Music", MusicOnKey = "DigIn.MusicOn";
    const string EffectsKey = "DigIn.Effects", EffectsOnKey = "DigIn.EffectsOn";

    enum Page { None, Menu, Settings, Confirm, HowTo, Intro }
    Page page = Page.None;
    int openedFrame = -10;
    float blurWeight;

    public bool IsOpen => page != Page.None;
    // The frame the menu (or the note) closed: its click belongs to the menu, not the spade.
    public static int LastClosedFrame { get; private set; } = -10;

    void Start()
    {
        // Saved settings (per player, on this machine).
        float volume = Get(VolumeKey, 1f);
        SetVolume(volume);
        if (game.player != null) game.player.lookSensitivity = Get(SensitivityKey, game.player.lookSensitivity);

        hud.pauseResume.onClick.AddListener(Resume);
        hud.pauseHowTo.onClick.AddListener(() => Show(Page.HowTo));
        hud.pauseSettingsButton.onClick.AddListener(() => Show(Page.Settings));
        hud.pauseQuit.onClick.AddListener(() => Show(Page.Confirm));
        hud.pauseSettingsBack.onClick.AddListener(() => Show(Page.Menu));
        hud.pauseQuitNo.onClick.AddListener(() => Show(Page.Menu));
        hud.pauseQuitYes.onClick.AddListener(Quit);
        hud.pauseVolume.SetValueWithoutNotify(volume);
        hud.pauseVolume.onValueChanged.AddListener(v => { SetVolume(v); Set(VolumeKey, v); });
        if (game.player != null)
            hud.pauseSensitivity.SetValueWithoutNotify(Mathf.InverseLerp(sensitivityRange.x, sensitivityRange.y, game.player.lookSensitivity));
        hud.pauseSensitivity.onValueChanged.AddListener(v =>
        {
            float s = Mathf.Lerp(sensitivityRange.x, sensitivityRange.y, v);
            if (game.player != null) game.player.lookSensitivity = s;
            Set(SensitivityKey, s);
        });

        // Music and sound effects: on/off and a volume each, through the mixer.
        hud.pauseMusicOn.SetIsOnWithoutNotify(Get(MusicOnKey, 1f) > 0.5f);
        hud.pauseMusic.SetValueWithoutNotify(Get(MusicKey, 1f));
        hud.pauseEffectsOn.SetIsOnWithoutNotify(Get(EffectsOnKey, 1f) > 0.5f);
        hud.pauseEffects.SetValueWithoutNotify(Get(EffectsKey, 1f));
        ApplyChannels();
        hud.pauseMusicOn.onValueChanged.AddListener(on => { Set(MusicOnKey, on ? 1f : 0f); ApplyChannels(); });
        hud.pauseMusic.onValueChanged.AddListener(v => { Set(MusicKey, v); ApplyChannels(); });
        hud.pauseEffectsOn.onValueChanged.AddListener(on => { Set(EffectsOnKey, on ? 1f : 0f); ApplyChannels(); });
        hud.pauseEffects.onValueChanged.AddListener(v => { Set(EffectsKey, v); ApplyChannels(); });

        // The first time ever: the note, before anything moves. Never in a benchmark run.
        bool benchmark = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-benchmark") >= 0;
        if (!benchmark && Get(SeenKey, 0f) < 0.5f) Open(Page.Intro);
    }

    void Update()
    {
        // The blur follows the menu, on unscaled time (time itself is stopped).
        float target = IsOpen ? 1f : 0f;
        blurWeight = Mathf.MoveTowards(blurWeight, target, Time.unscaledDeltaTime * 4f);
        if (blur != null) blur.weight = blurWeight;

        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || Time.frameCount == openedFrame) return;
        bool esc = kb.escapeKey.wasPressedThisFrame;

        if (!IsOpen)
        {
            // A shop, card, letter or the ending gets the Esc first.
            bool shopOpen = game.shop != null && (game.shop.IsOpen || Time.frameCount - DigShop.LastClosedFrame <= 1);
            bool storyOpen = game.story != null && game.story.Locked;
            if (esc && !shopOpen && !storyOpen) Open(Page.Menu);
            return;
        }

        bool anyKey = esc || kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame;
        bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;
        switch (page)
        {
            case Page.Intro:
                if (anyKey || click) { Set(SeenKey, 1f); Resume(); }
                break;
            case Page.HowTo:
                if (anyKey || click) Show(Page.Menu);
                break;
            case Page.Menu:
                if (esc) Resume();
                break;
            default:   // settings, confirm: Esc steps back
                if (esc) Show(Page.Menu);
                break;
        }
    }

    void Open(Page p)
    {
        openedFrame = Time.frameCount;
        Time.timeScale = 0f;
        if (p != Page.Intro)   // the first note is put away with any click: no cursor needed
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        Show(p);
    }

    void Show(Page p)
    {
        page = p;
        openedFrame = Time.frameCount;   // the click or key that brought us here is spent
        bool note = p == Page.HowTo || p == Page.Intro;
        hud.ShowPause(!note && p != Page.None, p == Page.Menu, p == Page.Settings, p == Page.Confirm);
        hud.ShowHowTo(note, p == Page.Intro);
    }

    public void Resume()
    {
        if (!IsOpen) return;
        Show(Page.None);
        Time.timeScale = 1f;
        LastClosedFrame = Time.frameCount;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Pauses from outside (tests, the demo). Does nothing if something else is open.
    public void OpenMenu()
    {
        if (!IsOpen) Open(Page.Menu);
    }

    void ApplyChannels()
    {
        if (DigAudio.I == null) return;
        DigAudio.I.SetMusic(hud.pauseMusicOn.isOn, hud.pauseMusic.value);
        DigAudio.I.SetEffects(hud.pauseEffectsOn.isOn, hud.pauseEffects.value);
        // A switched-off channel's slider fades a little, so it reads as off.
        hud.pauseMusic.interactable = hud.pauseMusicOn.isOn;
        hud.pauseEffects.interactable = hud.pauseEffectsOn.isOn;
    }

    // Through the mixer's master volume (DigAudio), or the listener if there is none.
    static void SetVolume(float v)
    {
        if (DigAudio.I != null) DigAudio.I.SetMasterVolume(v);
        else AudioListener.volume = v;
    }

    void Quit()
    {
#if UNITY_EDITOR
        Time.timeScale = 1f;
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnDisable()
    {
        if (IsOpen) Time.timeScale = 1f;
    }

    static float Get(string key, float fallback)
    {
        try { return PlayerPrefs.GetFloat(key, fallback); } catch { return fallback; }
    }

    static void Set(string key, float value)
    {
        try { PlayerPrefs.SetFloat(key, value); PlayerPrefs.Save(); } catch { }
    }
}
