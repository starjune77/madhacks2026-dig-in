using UnityEngine;

// Every sound in the game, played through a few pooled sources. Soft and natural:
// recorded foley (Assets/Audio, sources in CREDITS.md), a slight random pitch and volume
// on repeats, birds and wind on the surface that fade and muffle as you go underground,
// and a quiet hum that deepens with depth. Sits on "Game"; clips are inspector data.
public class DigAudio : MonoBehaviour
{
    public static DigAudio I { get; private set; }

    [Header("Clips")]
    public AudioClip[] digDirt;
    public AudioClip[] digStone;
    public AudioClip[] clods;
    public AudioClip[] steps;
    public AudioClip[] mineral;
    public AudioClip[] page;
    public AudioClip[] meow;
    public AudioClip purr;
    public AudioClip sleepyPurr;
    public AudioClip birds, wind, underground;

    [Header("Mix (keep it quiet)")]
    [Range(0f, 1f)] public float master = 0.8f;
    [Range(0f, 1f)] public float effects = 0.55f;
    [Range(0f, 1f)] public float ambience = 0.35f;
    [Tooltip("Random pitch spread on repeated sounds.")]
    public float pitchSpread = 0.08f;
    [Tooltip("Depth (m) by which the surface has faded away and the hum is full.")]
    public float fullDepth = 6f;

    // True in the bedroom: the yard's ambience and footsteps are silent.
    [System.NonSerialized] public bool Muted;

    AudioSource[] pool;
    int next;
    AudioSource birdsSource, windSource, humSource, purrSource;
    AudioLowPassFilter surfaceFilter;
    Transform listener;
    float stepDistance;
    Vector3 lastStep;

    void Awake()
    {
        I = this;
        pool = new AudioSource[12];
        for (int i = 0; i < pool.Length; i++) pool[i] = MakeSource("Sound " + i, false);

        // Surface ambience goes through a low-pass filter that closes as you go down.
        var surface = new GameObject("Surface Ambience");
        surface.transform.SetParent(transform, false);
        birdsSource = Loop(surface, birds);
        windSource = Loop(surface, wind);
        surfaceFilter = surface.AddComponent<AudioLowPassFilter>();   // needs the sources first
        var hum = new GameObject("Underground");
        hum.transform.SetParent(transform, false);
        humSource = Loop(hum, underground);
        purrSource = MakeSource("Purr", true);
        purrSource.clip = purr;
        purrSource.loop = true;
    }

    AudioSource MakeSource(string name, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = 0f;
        return s;
    }

    AudioSource Loop(GameObject host, AudioClip clip)
    {
        var s = host.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = true;
        s.playOnAwake = false;
        s.volume = 0f;
        s.spatialBlend = 0f;
        if (clip != null) { s.time = Random.Range(0f, clip.length); s.Play(); }
        return s;
    }

    // ---- One-shots -----------------------------------------------------------------
    // `at` puts the sound in the world (softly 3D); null plays it in the head.
    public void Play(AudioClip[] clips, float volume, Vector3? at = null, float pitch = 1f)
    {
        if (clips == null || clips.Length == 0) return;
        var clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;
        var s = pool[next];
        next = (next + 1) % pool.Length;
        s.Stop();
        s.clip = clip;
        s.volume = volume * effects * master * Random.Range(0.85f, 1f);
        s.pitch = pitch * Random.Range(1f - pitchSpread, 1f + pitchSpread);
        if (at.HasValue)
        {
            s.transform.position = at.Value;
            s.spatialBlend = 0.6f;
            s.minDistance = 2f;
            s.maxDistance = 25f;
        }
        else s.spatialBlend = 0f;
        s.Play();
    }

    // Stone is lower and duller; clay a little lower than dirt.
    public void Dig(int stratum, Vector3 at)
    {
        if (stratum >= 2) Play(digStone, 0.6f, at, 0.85f);
        else Play(digDirt, 0.75f, at, stratum == 1 ? 0.9f : 1f);
    }

    public void Bounce(Vector3 at) => Play(digStone, 0.5f, at, 1.1f);
    public void Toss(Vector3 at) => Play(digDirt, 0.35f, at, 1.15f);
    public void ClodLanded(Vector3 at, float strength) => Play(clods, Mathf.Clamp01(strength) * 0.25f, at, 1.25f);
    public void Mineral() => Play(mineral, 0.45f, null, 1.2f);
    public void Page() => Play(page, 0.5f);
    public void Meow() => Play(meow, 0.6f);

    // ---- Every frame ----------------------------------------------------------------
    void Update()
    {
        var game = DigGame.I;
        if (game == null) return;
        if (listener == null) listener = game.player.cam;

        float depth = Mathf.Max(0f, -game.player.FeetY);
        float under = Mathf.Clamp01(depth / fullDepth);
        float surface = 1f - Mathf.SmoothStep(0f, 1f, under);
        float target = Muted ? 0f : ambience * master;
        birdsSource.volume = Mathf.MoveTowards(birdsSource.volume, target * 0.8f * surface, Time.deltaTime);
        windSource.volume = Mathf.MoveTowards(windSource.volume, target * 0.55f * Mathf.Max(surface, 0.08f), Time.deltaTime);
        surfaceFilter.cutoffFrequency = Mathf.Lerp(22000f, 500f, Mathf.Sqrt(under));
        humSource.volume = Mathf.MoveTowards(humSource.volume, target * 0.55f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((depth - 1.5f) / fullDepth)), Time.deltaTime * 0.5f);
        humSource.pitch = Mathf.Lerp(1f, 0.82f, Mathf.Clamp01(depth / 30f));   // deeper down, a deeper hum

        // The cat purrs while the shop is open.
        if (Muted) return;   // the bedroom: only the sleepy purr
        bool purring = game.shop != null && game.shop.IsOpen;
        if (purring && !purrSource.isPlaying) { purrSource.volume = 0f; purrSource.Play(); }
        purrSource.volume = Mathf.MoveTowards(purrSource.volume, purring ? 0.35f * effects * master : 0f, Time.deltaTime * 0.6f);
        if (!purring && purrSource.isPlaying && purrSource.volume <= 0.001f) purrSource.Stop();

        // Footsteps: one every so often while walking on the ground.
        Vector3 p = game.player.transform.position;
        var cc = game.player.GetComponent<CharacterController>();
        if (cc.isGrounded && !game.InputLocked)
        {
            stepDistance += Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(lastStep.x, 0f, lastStep.z));
            if (stepDistance > 1.9f) { stepDistance = 0f; Play(steps, depth > 0.5f ? 0.3f : 0.22f, null, depth > 0.5f ? 0.9f : 1f); }
        }
        lastStep = p;
    }

    // The ending's purr, on its own (the shop is closed by then).
    public void SleepyPurr(bool on)
    {
        purrSource.clip = on ? sleepyPurr : purr;
        purrSource.loop = true;
        if (on) { purrSource.volume = 0.3f * effects * master; purrSource.Play(); }
        else purrSource.Stop();
    }
}
