using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Sound: recorded CC0 foley in Assets/Audio (sources in CREDITS.md). Sets import settings
// (ambience streams, short sounds load decompressed) and fills DigAudio's clip lists.
public static partial class DigInSceneBuilder
{
    const string AudioDir = "Assets/Audio";

    static AudioClip[] Clips(string folder, string pattern)
    {
        string dir = AudioDir + "/" + folder;
        if (!Directory.Exists(dir)) return new AudioClip[0];
        return Directory.GetFiles(dir, pattern).Where(f => !f.EndsWith(".meta")).OrderBy(f => f)
            .Select(f => ImportClip(f.Replace('\\', '/'), false)).Where(c => c != null).ToArray();
    }

    static AudioClip ImportClip(string path, bool stream)
    {
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) return null;
        var s = importer.defaultSampleSettings;
        var want = stream ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
        if (s.loadType != want || s.compressionFormat != AudioCompressionFormat.Vorbis || importer.forceToMono != !stream)
        {
            s.loadType = want;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = stream ? 0.6f : 0.7f;
            importer.defaultSampleSettings = s;
            importer.forceToMono = !stream;
            importer.loadInBackground = stream;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    static void WireAudio(DigAudio a)
    {
        a.digDirt = Clips("Dig", "Dig_*");
        a.digStone = Clips("Dig", "Stone_*");
        a.clods = Clips("Dig", "Dig_*");
        a.steps = Clips("Steps", "*.ogg");
        a.mineral = Clips("Minerals", "*.ogg");
        a.page = Clips("UI", "*.wav");
        a.meow = new[] { "cat_mewfood.wav", "cat_mewpurr.wav", "cat_mewpurr2.wav" }
            .Select(n => ImportClip(AudioDir + "/Cat/" + n, false)).Where(c => c != null).ToArray();
        a.purr = ImportClip(AudioDir + "/Cat/cat_purractive_loop.wav", false);
        a.sleepyPurr = ImportClip(AudioDir + "/Cat/cat_purrsleepy_loop.wav", false);
        a.birds = ImportClip(AudioDir + "/Ambience/Birds.wav", true);
        a.wind = ImportClip(AudioDir + "/Ambience/Wind.wav", true);
        a.underground = ImportClip(AudioDir + "/Ambience/Underground.ogg", true);
        EditorUtility.SetDirty(a);
        report.AppendLine("  audio: " + (a.digDirt.Length + a.digStone.Length + a.steps.Length + a.mineral.Length + a.page.Length + a.meow.Length) + " one-shots, 3 ambience loops");
    }
}
