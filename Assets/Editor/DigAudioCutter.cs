using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// How the game's sounds were cut from their sources (CC0 recordings, see CREDITS.md).
// Put the sources in Assets/AudioIn (not in the repo), then call these from a script:
// Analyse() prints what each source holds; Hits() cuts the strongest separate events into
// short one-shots; Loop() makes a seamless crossfaded loop. Output is 16-bit WAV in
// Assets/Audio. Nothing here runs in the game.
public static class DigAudioCutter
{
    public const string InDir = "Assets/AudioIn";

    public class Pcm
    {
        public float[] data;   // interleaved
        public int channels, rate;
        public int Frames => data.Length / channels;
        public float Seconds => (float)Frames / rate;
    }

    public static Pcm Load(string file)
    {
        string path = InDir + "/" + file;
        var importer = (AudioImporter)AssetImporter.GetAtPath(path);
        if (importer == null) throw new FileNotFoundException(path);
        var s = importer.defaultSampleSettings;
        if (s.loadType != AudioClipLoadType.DecompressOnLoad || importer.forceToMono || s.compressionFormat != AudioCompressionFormat.PCM)
        {
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.PCM;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = s;
            importer.forceToMono = false;
            importer.SaveAndReimport();
        }
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        clip.LoadAudioData();
        var pcm = new Pcm { channels = clip.channels, rate = clip.frequency, data = new float[clip.samples * clip.channels] };
        clip.GetData(pcm.data, 0);
        return pcm;
    }

    // RMS per window (seconds), mixed across channels.
    public static float[] Envelope(Pcm p, float window)
    {
        int w = Mathf.Max(1, (int)(window * p.rate));
        int n = p.Frames / w;
        var env = new float[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int f = i * w; f < (i + 1) * w; f++)
                for (int c = 0; c < p.channels; c++) { float v = p.data[f * p.channels + c]; sum += v * v; }
            env[i] = (float)System.Math.Sqrt(sum / (w * p.channels));
        }
        return env;
    }

    static float Db(float v) => 20f * Mathf.Log10(Mathf.Max(v, 1e-6f));

    // Duration, channels, peak, loudness, noise floor and a brightness hint (zero
    // crossings per second) for every source given.
    public static string Analyse(params string[] files)
    {
        var sb = new StringBuilder();
        foreach (var f in files)
        {
            try
            {
                var p = Load(f);
                var env = Envelope(p, 0.05f);
                var sorted = (float[])env.Clone();
                System.Array.Sort(sorted);
                float floor = sorted[Mathf.Clamp(sorted.Length / 10, 0, sorted.Length - 1)];
                float loud = sorted[Mathf.Clamp(sorted.Length * 9 / 10, 0, sorted.Length - 1)];
                float peak = 0f;
                int zc = 0;
                for (int i = 0; i < p.data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(p.data[i]));
                for (int fr = 1; fr < p.Frames; fr++) if ((p.data[fr * p.channels] >= 0f) != (p.data[(fr - 1) * p.channels] >= 0f)) zc++;
                sb.AppendFormat("{0}: {1:0.0}s {2}ch {3}Hz peak {4:0}dB loud {5:0}dB floor {6:0}dB range {7:0}dB zc/s {8:0} hits {9}\n",
                    f, p.Seconds, p.channels, p.rate, Db(peak), Db(loud), Db(floor), Db(loud) - Db(floor), zc / p.Seconds, Onsets(p, 0.25f, 9f).Count);
            }
            catch (System.Exception e) { sb.AppendLine(f + ": " + e.Message); }
        }
        return sb.ToString();
    }

    // Times (s) where the level jumps by `riseDb` above the recent average, at least `gap` apart.
    public static List<float> Onsets(Pcm p, float gap, float riseDb)
    {
        const float win = 0.01f;
        var env = Envelope(p, win);
        var result = new List<float>();
        float avg = env.Length > 0 ? env[0] : 0f, last = -999f;
        var sorted = (float[])env.Clone();
        System.Array.Sort(sorted);
        float floor = sorted.Length > 0 ? sorted[sorted.Length / 5] : 0f;
        for (int i = 1; i < env.Length; i++)
        {
            float t = i * win;
            if (Db(env[i]) - Db(avg) > riseDb && env[i] > floor * 3f && t - last > gap)
            {
                result.Add(Mathf.Max(0f, t - win));
                last = t;
            }
            avg = Mathf.Lerp(avg, env[i], 0.08f);
        }
        return result;
    }

    // Cuts up to `count` one-shots of `length` s at the loudest separate onsets, each with a
    // short pre-roll, a fade out over the last `fade` s, normalised to `peakDb`. The cuts
    // are spread across the source (not all from one spot). Returns the files written.
    public static List<string> Hits(string file, string outPrefix, int count, float length, float fade, float preRoll = 0.02f,
                                    float peakDb = -3f, float gap = 0.35f, float riseDb = 9f, float skipStart = 0f, float offset = 0f)
    {
        var p = Load(file);
        var onsets = Onsets(p, gap, riseDb);
        onsets.RemoveAll(t => t < skipStart || t + offset + length > p.Seconds);
        // Rank by strength, keep the strongest `count * 2`, then take every other one in time order for variety.
        var env = Envelope(p, 0.01f);
        onsets.Sort((a, b) => Strength(env, b).CompareTo(Strength(env, a)));
        var chosen = onsets.GetRange(0, Mathf.Min(onsets.Count, count));
        chosen.Sort();
        var written = new List<string>();
        for (int i = 0; i < chosen.Count; i++)
        {
            float start = Mathf.Max(0f, chosen[i] + offset - preRoll);
            var cut = Slice(p, start, length);
            Fade(cut, 0.004f, fade);
            Normalise(cut, peakDb);
            string path = outPrefix + "_" + (written.Count + 1).ToString("00") + ".wav";
            Write(path, cut);
            written.Add(path);
        }
        return written;
    }

    static float Strength(float[] env, float t)
    {
        int i = Mathf.Clamp((int)(t / 0.01f), 0, env.Length - 1);
        float m = 0f;
        for (int k = i; k < Mathf.Min(env.Length, i + 20); k++) m = Mathf.Max(m, env[k]);
        return m;
    }

    // `count` pieces of `length` s, evenly spread over [from, to] of the source (for
    // textures without clear onsets: trickles, pours, birdsong phrases).
    public static List<string> Pieces(string file, string outPrefix, int count, float length, float from, float to, float fadeIn, float fadeOut, float peakDb = -3f)
    {
        var p = Load(file);
        to = Mathf.Min(to, p.Seconds - length);
        var written = new List<string>();
        for (int i = 0; i < count; i++)
        {
            float start = count == 1 ? from : Mathf.Lerp(from, to, (float)i / (count - 1));
            var cut = Slice(p, start, length);
            Fade(cut, fadeIn, fadeOut);
            Normalise(cut, peakDb);
            string path = outPrefix + "_" + (i + 1).ToString("00") + ".wav";
            Write(path, cut);
            written.Add(path);
        }
        return written;
    }

    // A seamless loop of `length` s from `start`: its last `crossfade` s are blended
    // (equal power) into its beginning. Normalised so its loud parts sit at `loudDb`.
    public static string Loop(string file, string outPath, float start, float length, float crossfade, float loudDb, bool mono = false)
    {
        var p = Shrink(Load(file), mono);
        length = Mathf.Min(length, p.Seconds - start - crossfade);
        var seg = Slice(p, start, length + crossfade);
        int ch = seg.channels, n = (int)(length * seg.rate), x = (int)(crossfade * seg.rate);
        var outp = new Pcm { channels = ch, rate = seg.rate, data = new float[n * ch] };
        System.Array.Copy(seg.data, outp.data, n * ch);
        for (int f = 0; f < x; f++)
        {
            float t = (float)f / x;
            float a = Mathf.Sin(t * Mathf.PI * 0.5f), b = Mathf.Cos(t * Mathf.PI * 0.5f);
            for (int c = 0; c < ch; c++)
                outp.data[f * ch + c] = seg.data[f * ch + c] * a + seg.data[(n + f) * ch + c] * b;
        }
        var env = Envelope(outp, 0.05f);
        var sorted = (float[])env.Clone();
        System.Array.Sort(sorted);
        float loud = sorted[sorted.Length * 9 / 10];
        Gain(outp, Mathf.Pow(10f, loudDb / 20f) / Mathf.Max(loud, 1e-5f));
        Limit(outp, 0.9f);
        Write(outPath, outp);
        return outPath + " " + length.ToString("0.0") + "s";
    }

    // A piece of a source as is (music): optional fades, normalised peak.
    public static string Excerpt(string file, string outPath, float start, float length, float fadeIn, float fadeOut, float peakDb)
    {
        var p = Load(file);
        length = Mathf.Min(length, p.Seconds - start);
        var cut = Slice(p, start, length);
        Fade(cut, fadeIn, fadeOut);
        Normalise(cut, peakDb);
        Write(outPath, cut);
        return outPath + " " + length.ToString("0.0") + "s";
    }

    // Mono (average of the channels) and/or half the sample rate above 48 kHz, to keep loops small.
    public static Pcm Shrink(Pcm p, bool mono)
    {
        int step = p.rate > 48000 ? 2 : 1;
        int ch = mono ? 1 : p.channels, n = p.Frames / step;
        var o = new Pcm { channels = ch, rate = p.rate / step, data = new float[n * ch] };
        for (int f = 0; f < n; f++)
            for (int c = 0; c < ch; c++)
            {
                float sum = 0f; int k = 0;
                for (int s = 0; s < step; s++)
                    for (int ic = 0; ic < p.channels; ic++)
                        if (mono || ic == c) { sum += p.data[(f * step + s) * p.channels + ic]; k++; }
                o.data[f * ch + c] = sum / k;
            }
        return o;
    }

    public static Pcm Slice(Pcm p, float start, float length)
    {
        int s = Mathf.Clamp((int)(start * p.rate), 0, p.Frames);
        int n = Mathf.Clamp((int)(length * p.rate), 0, p.Frames - s);
        var o = new Pcm { channels = p.channels, rate = p.rate, data = new float[n * p.channels] };
        System.Array.Copy(p.data, s * p.channels, o.data, 0, n * p.channels);
        return o;
    }

    public static void Fade(Pcm p, float fadeIn, float fadeOut)
    {
        int n = p.Frames, a = Mathf.Min(n, (int)(fadeIn * p.rate)), b = Mathf.Min(n, (int)(fadeOut * p.rate));
        for (int f = 0; f < n; f++)
        {
            float g = 1f;
            if (f < a) g *= Mathf.Sin((float)f / a * Mathf.PI * 0.5f);
            if (f >= n - b) g *= Mathf.Cos((float)(f - (n - b)) / b * Mathf.PI * 0.5f);
            for (int c = 0; c < p.channels; c++) p.data[f * p.channels + c] *= g;
        }
    }

    public static void Normalise(Pcm p, float peakDb)
    {
        float peak = 1e-6f;
        foreach (var v in p.data) peak = Mathf.Max(peak, Mathf.Abs(v));
        Gain(p, Mathf.Pow(10f, peakDb / 20f) / peak);
    }

    static void Gain(Pcm p, float g)
    {
        for (int i = 0; i < p.data.Length; i++) p.data[i] *= g;
    }

    static void Limit(Pcm p, float ceiling)
    {
        for (int i = 0; i < p.data.Length; i++)
        {
            float v = p.data[i], a = Mathf.Abs(v);
            if (a > ceiling) p.data[i] = Mathf.Sign(v) * (ceiling + (1f - ceiling) * (float)System.Math.Tanh((a - ceiling) / (1f - ceiling)));
        }
    }

    public static void Write(string path, Pcm p)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var w = new BinaryWriter(File.Create(path)))
        {
            int bytes = p.data.Length * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)p.channels);
            w.Write(p.rate); w.Write(p.rate * p.channels * 2); w.Write((short)(p.channels * 2)); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (var v in p.data) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
        }
    }
}
