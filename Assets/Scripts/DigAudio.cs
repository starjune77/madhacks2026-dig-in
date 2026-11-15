using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

// Every sound in the game, quiet and natural (recorded CC0 foley and field recordings,
// sources in CREDITS.md), routed through the mixer's Music, SFX, Ambience and UI groups.
// Sits on "Game"; clips are inspector data.
//
// Digging is layered and close, right at the ear (2D, the recordings' own stereo): the
// blade biting into the ground (a set each for dirt, clay and stone) on the swing's
// impact frame, the soil being lifted a moment later, a trickle of grains falling off the
// blade when the earth is flicked away, and clods landing out in the world (3D). Every
// layer picks a variation that is not the last one, with pitch +-4% and volume +-2 dB.
//
// Around it: long seamless field-recording loops of the yard and the odd blackbird call
// from somewhere around it; underground the yard fades and muffles, a soft room tone and
// a small-room reverb close in. Music is a slow solo piano, lowest of all, softer and
// darker underground, ducked under cards, letters, the shop and the cat; the ending has
// its own few bars.
public class DigAudio : MonoBehaviour
{
    public static DigAudio I { get; private set; }

    [Header("Mixer")]
    public AudioMixer mixer;
    public AudioMixerGroup musicGroup, sfxGroup, ambienceGroup, uiGroup;

    [Header("Digging")]
    public AudioClip[] dirtBite;
    public AudioClip[] dirtLift;
    public AudioClip[] clayBite;
    public AudioClip[] stoneBite;
    public AudioClip[] trickle;
    public AudioClip[] clods;

    [Header("Footsteps")]
    public AudioClip[] stepsGrass;
    public AudioClip[] stepsDirt;

    [Header("Other effects")]
    public AudioClip[] mineral;
    public AudioClip[] page;
    public AudioClip[] meow;
    public AudioClip purr;
    public AudioClip sleepyPurr;

    [Header("Ambience")]
    public AudioClip yard;
    public AudioClip garden;
    public AudioClip roomTone;
    public AudioClip[] birdCalls;

    [Header("Music")]
    public AudioClip musicYard;
    public AudioClip musicUnderground;
    public AudioClip musicEnding;

    [Header("Mix (keep it quiet)")]
    [Range(0f, 1f)] public float effects = 0.8f;
    [Range(0f, 1f)] public float ambience = 0.32f;
    [Range(0f, 1f)] public float music = 0.16f;
    [Tooltip("Pitch spread on repeated sounds (0.04 = +-4%).")]
    public float pitchSpread = 0.04f;
    [Tooltip("Volume spread on repeated sounds, dB either way.")]
    public float volumeSpreadDb = 2f;
    [Tooltip("Depth (m) by which the surface has faded away and the tunnel sound is full.")]
    public float fullDepth = 6f;
    [Tooltip("Seconds of quiet between plays of a music piece.")]
    public Vector2 musicGap = new Vector2(18f, 40f);
    [Tooltip("Seconds between bird calls around the yard.")]
    public Vector2 birdGap = new Vector2(7f, 20f);

    // True in the bedroom: the yard, the tunnels and the steps are silent.
    [System.NonSerialized] public bool Muted;

    AudioSource[] near;    // 2D, close: the shovel, steps, the UI
    AudioSource[] world;   // 3D: clods, bird calls
    int nextNear, nextWorld;
    AudioSource yardSource, gardenSource, roomSource, purrSource, endingSource;
    AudioSource musicA, musicB;
    AudioLowPassFilter surfaceFilter, musicFilter;
    AudioReverbZone reverb;
    readonly Dictionary<AudioClip[], int> lastPick = new Dictionary<AudioClip[], int>();
    float stepDistance, birdTimer, musicTimerA, musicTimerB, duckTimer, duck = 1f;
    Vector3 lastStep;

    void Awake()
    {
        I = this;
        near = new AudioSource[10];
        for (int i = 0; i < near.Length; i++) near[i] = Source("Near " + i, sfxGroup, 0f);
        world = new AudioSource[8];
        for (int i = 0; i < world.Length; i++)
        {
            world[i] = Source("World " + i, sfxGroup, 1f);
            world[i].rolloffMode = AudioRolloffMode.Logarithmic;
            world[i].minDistance = 1.5f;
            world[i].maxDistance = 30f;
        }

        // The yard: two long loops through a low-pass that closes as you go down.
        var surface = new GameObject("Surface Ambience");
        surface.transform.SetParent(transform, false);
        yardSource = Loop(surface, yard, ambienceGroup);
        gardenSource = Loop(surface, garden, ambienceGroup);
        surfaceFilter = surface.AddComponent<AudioLowPassFilter>();   // needs the sources first
        var room = new GameObject("Room Tone");
        room.transform.SetParent(transform, false);
        roomSource = Loop(room, roomTone, ambienceGroup);

        // Music: the yard's piece and the underground one, crossfading with depth; the
        // underground one through a soft low-pass.
        var musicGo = new GameObject("Music");
        musicGo.transform.SetParent(transform, false);
        musicA = musicGo.AddComponent<AudioSource>();
        Setup(musicA, musicGroup, 0f);
        var musicUnder = new GameObject("Music Underground");
        musicUnder.transform.SetParent(transform, false);
        musicB = musicUnder.AddComponent<AudioSource>();
        Setup(musicB, musicGroup, 0f);
        musicFilter = musicUnder.AddComponent<AudioLowPassFilter>();
        musicFilter.cutoffFrequency = 2600f;
        musicTimerA = 2f;
        musicTimerB = 6f;
        endingSource = Source("Ending Music", musicGroup, 0f);

        purrSource = Source("Purr", sfxGroup, 0.85f);
        purrSource.loop = true;
        purrSource.minDistance = 1.2f;
        purrSource.maxDistance = 12f;
        purrSource.rolloffMode = AudioRolloffMode.Logarithmic;

        birdTimer = Random.Range(3f, 8f);
    }

    AudioSource Source(string name, AudioMixerGroup group, float spatial)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        Setup(s, group, spatial);
        return s;
    }

    static void Setup(AudioSource s, AudioMixerGroup group, float spatial)
    {
        s.playOnAwake = false;
        s.spatialBlend = spatial;
        s.outputAudioMixerGroup = group;
        s.dopplerLevel = 0f;
    }

    AudioSource Loop(GameObject host, AudioClip clip, AudioMixerGroup group)
    {
        var s = host.AddComponent<AudioSource>();
        Setup(s, group, 0f);
        s.clip = clip;
        s.loop = true;
        s.volume = 0f;
        if (clip != null) { s.time = Random.Range(0f, clip.length); s.Play(); }
        return s;
    }

    // A variation that is not the last one played from this set.
    AudioClip Pick(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        lastPick.TryGetValue(clips, out int last);
        int i = Random.Range(0, clips.Length);
        if (clips.Length > 1 && i == last) i = (i + 1 + Random.Range(0, clips.Length - 1)) % clips.Length;
        lastPick[clips] = i;
        return clips[i];
    }

    float Spread() => Mathf.Pow(10f, Random.Range(-volumeSpreadDb, volumeSpreadDb) / 20f);

    // ---- One-shots -----------------------------------------------------------------
    // Close (2D, at the ear), with `delay` seconds before it starts.
    public void PlayNear(AudioClip[] clips, float volume, float pitch = 1f, float delay = 0f, float reverbMix = 0.35f, AudioMixerGroup group = null)
    {
        var clip = Pick(clips);
        if (clip == null) return;
        var s = near[nextNear];
        nextNear = (nextNear + 1) % near.Length;
        s.Stop();
        s.clip = clip;
        s.outputAudioMixerGroup = group != null ? group : sfxGroup;
        s.volume = Mathf.Clamp01(volume * effects * Spread());
        s.pitch = pitch * Random.Range(1f - pitchSpread, 1f + pitchSpread);
        s.reverbZoneMix = reverbMix;   // the tunnel's room, but the detail stays dry and close
        if (delay > 0f) s.PlayDelayed(delay); else s.Play();
    }

    // Out in the world (3D, soft rolloff).
    public void PlayAt(AudioClip[] clips, float volume, Vector3 at, float pitch = 1f)
    {
        var clip = Pick(clips);
        if (clip == null) return;
        var s = world[nextWorld];
        nextWorld = (nextWorld + 1) % world.Length;
        s.Stop();
        s.clip = clip;
        s.transform.position = at;
        s.volume = Mathf.Clamp01(volume * effects * Spread());
        s.pitch = pitch * Random.Range(1f - pitchSpread, 1f + pitchSpread);
        s.Play();
    }

    // The blade lands (animation event): the bite, then the soil coming up with it.
    public void Dig(int stratum, Vector3 at)
    {
        if (stratum >= 2)
        {
            PlayNear(stoneBite, 0.75f);
            return;
        }
        PlayNear(stratum == 1 ? clayBite : dirtBite, 0.8f, stratum == 1 ? 0.97f : 1f);
        PlayNear(dirtLift, 0.5f, stratum == 1 ? 0.9f : 1f, Random.Range(0.1f, 0.16f));
    }

    // Too hard for this spade: a dry scrape and a bounce.
    public void Bounce(Vector3 at) => PlayNear(stoneBite, 0.45f, 1.08f);

    // The earth is flicked off the blade: grains trickle away, tailing off on their own.
    public void Toss(Vector3 at) => PlayNear(trickle, 0.42f, 1f, 0.05f);

    public void ClodLanded(Vector3 at, float strength) => PlayAt(clods, 0.25f + Mathf.Clamp01(strength) * 0.45f, at);
    public void Mineral() => PlayNear(mineral, 0.4f, 1.2f, 0f, 0.2f, uiGroup);
    public void Page() => PlayNear(page, 0.45f, 1f, 0f, 0f, uiGroup);

    public void Meow()
    {
        PlayNear(meow, 0.38f, 1f, 0f, 0.1f);
        Duck(2.5f);
    }

    // The music steps back for a while (the cat talks, a card opens).
    public void Duck(float seconds) => duckTimer = Mathf.Max(duckTimer, seconds);

    // The pause menu's volume (0..1), through the mixer's MasterVolume.
    public void SetMasterVolume(float v)
    {
        if (mixer != null) mixer.SetFloat("MasterVolume", v <= 0.0001f ? -80f : 20f * Mathf.Log10(v));
        else AudioListener.volume = v;
    }

    // The pause menu's Music and Sound Effects settings (ambience follows sound effects).
    public void SetMusic(bool on, float v) => SetParam("MusicVolume", on ? v : 0f);

    public void SetEffects(bool on, float v)
    {
        SetParam("SfxVolume", on ? v : 0f);
        SetParam("AmbienceVolume", on ? v : 0f);
    }

    void SetParam(string param, float v)
    {
        if (mixer != null) mixer.SetFloat(param, v <= 0.0001f ? -80f : 20f * Mathf.Log10(v));
    }

    // ---- Every frame ----------------------------------------------------------------
    void Update()
    {
        var game = DigGame.I;
        if (game == null) return;
        float dt = Time.unscaledDeltaTime;

        float depth = Mathf.Max(0f, -game.player.FeetY);
        float under = Mathf.Clamp01(depth / fullDepth);
        float surface = 1f - Mathf.SmoothStep(0f, 1f, under);
        float amb = Muted ? 0f : ambience;
        yardSource.volume = Mathf.MoveTowards(yardSource.volume, amb * 0.9f * surface, dt * 0.5f);
        gardenSource.volume = Mathf.MoveTowards(gardenSource.volume, amb * 0.45f * Mathf.Max(surface, 0.05f), dt * 0.5f);
        surfaceFilter.cutoffFrequency = Mathf.Lerp(22000f, 450f, Mathf.Sqrt(under));
        float tunnel = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((depth - 1f) / 3f));
        roomSource.volume = Mathf.MoveTowards(roomSource.volume, amb * 0.7f * tunnel, dt * 0.4f);

        UpdateReverb(game, tunnel);
        UpdateMusic(game, under, dt);
        if (Muted) return;   // the bedroom: only the sleepy purr and the ending's piano

        // Bird calls from somewhere around the yard, on the surface.
        birdTimer -= dt;
        if (birdTimer <= 0f)
        {
            birdTimer = Random.Range(birdGap.x, birdGap.y);
            if (surface > 0.5f)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                Vector3 p = game.player.transform.position + new Vector3(dir.x, 0f, dir.y) * Random.Range(12f, 26f);
                p.y = Random.Range(3f, 9f);   // in a tree
                PlayAt(birdCalls, 0.55f * ambience / 0.32f, p);
            }
        }

        // The cat purrs, softly, while you trade.
        bool purring = game.shop != null && game.shop.IsOpen;
        if (purring && !purrSource.isPlaying)
        {
            var cat = FindAnyObjectByType<DigCat>();
            purrSource.transform.position = cat != null ? cat.transform.position + Vector3.up * 0.2f : game.player.transform.position;
            purrSource.clip = purr;
            purrSource.volume = 0f;
            purrSource.Play();
        }
        purrSource.volume = Mathf.MoveTowards(purrSource.volume, purring ? 0.4f * effects : 0f, dt * 0.6f);
        if (!purring && purrSource.isPlaying && purrSource.volume <= 0.001f) purrSource.Stop();

        // Footsteps at the pace you walk: grass on the lawn, earth on the patch and below.
        Vector3 pos = game.player.transform.position;
        var cc = game.player.GetComponent<CharacterController>();
        if (cc.isGrounded && !game.InputLocked)
        {
            float moved = Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(lastStep.x, 0f, lastStep.z));
            stepDistance += moved;
            float stride = Mathf.Lerp(1.15f, 1.6f, Mathf.Clamp01(moved / Mathf.Max(Time.deltaTime, 1e-4f) / game.player.moveSpeed));
            if (stepDistance > stride)
            {
                stepDistance = 0f;
                bool earth = depth > 0.3f || new Vector2(pos.x - game.digSpot.x, pos.z - game.digSpot.z).magnitude < 2.6f;
                PlayNear(earth ? stepsDirt : stepsGrass, earth ? 0.32f : 0.26f, 1f, 0f, 0.4f);
            }
        }
        lastStep = pos;
    }

    // A small, close room around you in the tunnels; nothing at all on the surface.
    void UpdateReverb(DigGame game, float tunnel)
    {
        if (reverb == null)
        {
            var go = new GameObject("Tunnel Reverb");
            reverb = go.AddComponent<AudioReverbZone>();
            reverb.reverbPreset = AudioReverbPreset.User;
            reverb.minDistance = 50f;
            reverb.maxDistance = 60f;
            reverb.decayTime = 0.75f;
            reverb.decayHFRatio = 0.5f;
            reverb.reflections = -900;
            reverb.reflectionsDelay = 0.006f;
            reverb.reverb = -700;
            reverb.reverbDelay = 0.012f;
            reverb.roomHF = -900;
            reverb.diffusion = 70f;
            reverb.density = 85f;
        }
        reverb.transform.position = game.player.cam.position;
        reverb.room = Mathf.RoundToInt(Mathf.Lerp(-10000f, -1100f, tunnel));
        reverb.enabled = tunnel > 0.01f && !Muted;
    }

    // Two pieces, each played now and then with a quiet gap between, crossfaded by depth
    // and ducked under cards, letters, the shop and the cat.
    void UpdateMusic(DigGame game, float under, float dt)
    {
        bool hold = game.InputLocked && !(game.pause != null && game.pause.IsOpen);   // shop, card, letter
        duckTimer = Mathf.Max(0f, duckTimer - dt);
        float duckTarget = hold || duckTimer > 0f ? 0.45f : 1f;
        duck = Mathf.MoveTowards(duck, duckTarget, dt * 0.8f);
        float level = Muted ? 0f : music * duck;
        float mix = Mathf.SmoothStep(0f, 1f, under);
        musicA.volume = Mathf.MoveTowards(musicA.volume, level * (1f - mix), dt * 0.25f);
        musicB.volume = Mathf.MoveTowards(musicB.volume, level * 0.85f * mix, dt * 0.25f);
        Pace(musicA, musicYard, ref musicTimerA, dt);
        Pace(musicB, musicUnderground, ref musicTimerB, dt);
    }

    void Pace(AudioSource s, AudioClip clip, ref float timer, float dt)
    {
        if (clip == null || Muted || s.isPlaying) return;
        timer -= dt;
        if (timer > 0f) return;
        timer = Random.Range(musicGap.x, musicGap.y);
        s.clip = clip;
        s.Play();
    }

    // The ending: the sleepy purr on the bed and the piano's first few bars.
    public void SleepyPurr(bool on)
    {
        purrSource.Stop();
        purrSource.clip = on ? sleepyPurr : purr;
        purrSource.loop = true;
        purrSource.spatialBlend = on ? 0f : 0.85f;
        if (on) { purrSource.volume = 0.32f * effects; purrSource.Play(); }
        if (on && musicEnding != null)
        {
            musicA.Stop();
            musicB.Stop();
            endingSource.clip = musicEnding;
            endingSource.volume = music * 1.2f;
            endingSource.PlayDelayed(1.5f);
        }
        else endingSource.Stop();
    }
}
