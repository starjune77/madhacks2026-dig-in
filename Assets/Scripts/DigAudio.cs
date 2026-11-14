using UnityEngine;

// All sound effects, synthesised at startup so the project needs no audio files.
// Created by DigGame on the camera.
public class DigAudio : MonoBehaviour
{
    public enum Sound { Whoosh, Thud, Clank, Clink, Chime, Pickup }

    const int Rate = 44100;
    const int Variants = 3;

    readonly System.Random rng = new System.Random(7);
    AudioSource[] sources;
    int nextSource;
    AudioClip[][] hits;        // [stratum][variant]
    AudioClip[] clips;         // indexed by Sound

    void Awake()
    {
        // A few sources so overlapping sounds can each have their own pitch.
        sources = new AudioSource[6];
        for (int i = 0; i < sources.Length; i++)
        {
            sources[i] = gameObject.AddComponent<AudioSource>();
            sources[i].playOnAwake = false;
            sources[i].spatialBlend = 0f;
        }

        hits = new AudioClip[3][];
        for (int s = 0; s < 3; s++)
        {
            hits[s] = new AudioClip[Variants];
            for (int v = 0; v < Variants; v++)
            {
                float k = 1f + (v - 1) * 0.12f;   // each variant is tuned a little differently
                float[] data;
                if (s == 0)      data = Impact(0.30f, 2400f * k, 20f, 0.8f, 120f * k, 55f, 24f, 0.9f, null, 0f, 0f);
                else if (s == 1) data = Impact(0.32f, 1100f * k, 16f, 0.7f, 95f * k, 45f, 18f, 1.0f, null, 0f, 0f);
                else             data = Impact(0.30f, 6000f, 45f, 0.7f, 160f, 80f, 40f, 0.5f,
                                               new[] { 1320f * k, 2210f * k, 3475f * k }, 38f, 0.5f);
                hits[s][v] = ToClip("Hit " + s + "-" + v, data);
            }
        }

        clips = new AudioClip[6];
        clips[(int)Sound.Whoosh] = ToClip("Whoosh", Whoosh());
        clips[(int)Sound.Thud] = ToClip("Thud", Impact(0.25f, 700f, 22f, 0.5f, 90f, 50f, 26f, 1.0f, null, 0f, 0f));
        clips[(int)Sound.Clank] = ToClip("Clank", Impact(0.50f, 7000f, 70f, 0.5f, 200f, 110f, 45f, 0.3f,
                                                         new[] { 830f, 1385f, 2290f, 3170f }, 13f, 0.8f));
        clips[(int)Sound.Clink] = ToClip("Clink", Impact(0.30f, 8000f, 90f, 0.1f, 0f, 0f, 1f, 0f,
                                                         new[] { 2637f, 3951f, 5274f }, 26f, 0.8f));
        clips[(int)Sound.Chime] = ToClip("Chime", Bells(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.09f, 1.8f));
        clips[(int)Sound.Pickup] = ToClip("Pickup", Bells(new[] { 783.99f, 1046.5f }, 0.11f, 1.2f));
    }

    public void Play(Sound sound, float volume = 1f, float pitchJitter = 0.06f)
    {
        PlayClip(clips[(int)sound], volume, pitchJitter);
    }

    public void PlayHit(int stratum, float volume = 0.9f)
    {
        var set = hits[Mathf.Clamp(stratum, 0, hits.Length - 1)];
        PlayClip(set[Random.Range(0, set.Length)], volume, 0.1f);
    }

    void PlayClip(AudioClip clip, float volume, float pitchJitter)
    {
        var src = sources[nextSource];
        nextSource = (nextSource + 1) % sources.Length;
        src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        src.PlayOneShot(clip, volume);
    }

    // ---- Synthesis -------------------------------------------------------------
    float[] Noise(int n)
    {
        var s = new float[n];
        for (int i = 0; i < n; i++) s[i] = (float)rng.NextDouble() * 2f - 1f;
        return s;
    }

    static void LowPass(float[] s, float cutoff)
    {
        float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
        float y = 0f;
        for (int i = 0; i < s.Length; i++)
        {
            y += a * (s[i] - y);
            s[i] = y;
        }
    }

    static void Normalize(float[] s, float peak)
    {
        float max = 0.0001f;
        for (int i = 0; i < s.Length; i++) max = Mathf.Max(max, Mathf.Abs(s[i]));
        float gain = peak / max;
        for (int i = 0; i < s.Length; i++) s[i] *= gain;
    }

    static void FadeOut(float[] s, int samples)
    {
        samples = Mathf.Min(samples, s.Length);
        for (int i = 0; i < samples; i++) s[s.Length - 1 - i] *= (float)i / samples;
    }

    // A burst of filtered noise (grit) + a pitch-dropping sine (weight) + optional
    // ringing partials (stone / metal).
    float[] Impact(float seconds, float noiseCutoff, float noiseDecay, float noiseAmp,
                   float thumpStart, float thumpEnd, float thumpDecay, float thumpAmp,
                   float[] pings, float pingDecay, float pingAmp)
    {
        int n = (int)(seconds * Rate);
        var noise = Noise(n);
        LowPass(noise, noiseCutoff);
        Normalize(noise, 1f);

        var s = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float v = noise[i] * Mathf.Exp(-t * noiseDecay) * noiseAmp;

            float f = Mathf.Lerp(thumpEnd, thumpStart, Mathf.Exp(-t * 30f));
            phase += 2f * Mathf.PI * f / Rate;
            v += Mathf.Sin(phase) * Mathf.Exp(-t * thumpDecay) * thumpAmp;

            if (pings != null)
            {
                float ring = 0f;
                for (int p = 0; p < pings.Length; p++)
                    ring += Mathf.Sin(2f * Mathf.PI * pings[p] * t + p * 1.3f);
                v += ring / pings.Length * Mathf.Exp(-t * pingDecay) * pingAmp;
            }
            s[i] = v;
        }
        FadeOut(s, 300);
        Normalize(s, 0.9f);
        return s;
    }

    float[] Whoosh()
    {
        int n = (int)(0.22f * Rate);
        var s = Noise(n);
        LowPass(s, 1500f);
        Normalize(s, 1f);
        for (int i = 0; i < n; i++)
        {
            float e = Mathf.Sin(Mathf.PI * i / n);
            s[i] *= e * e;
        }
        Normalize(s, 0.6f);
        return s;
    }

    // Soft bell notes played one after another.
    static float[] Bells(float[] notes, float spacing, float seconds)
    {
        int n = (int)(seconds * Rate);
        var s = new float[n];
        for (int k = 0; k < notes.Length; k++)
        {
            int start = (int)(k * spacing * Rate);
            for (int i = start; i < n; i++)
            {
                float t = (float)(i - start) / Rate;
                float attack = Mathf.Min(1f, t / 0.004f);
                float w = 2f * Mathf.PI * notes[k] * t;
                float v = Mathf.Sin(w) + 0.35f * Mathf.Sin(w * 2f) * Mathf.Exp(-t * 6f) + 0.15f * Mathf.Sin(w * 3.01f) * Mathf.Exp(-t * 9f);
                s[i] += v * attack * Mathf.Exp(-t * 3.5f);
            }
        }
        FadeOut(s, 2000);
        Normalize(s, 0.8f);
        return s;
    }

    static AudioClip ToClip(string name, float[] data)
    {
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
