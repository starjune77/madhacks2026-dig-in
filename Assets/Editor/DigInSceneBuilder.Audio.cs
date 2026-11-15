using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Sound: recorded CC0 foley and field recordings in Assets/Audio (cut with
// DigAudioCutter; sources in CREDITS.md). Sets import settings and fills DigAudio's clip
// lists and mixer groups. Short sounds keep their stereo and load decompressed at high
// quality (the digging is the detail the player hears most); loops and music stream.
public static partial class DigInSceneBuilder
{
    const string AudioDir = "Assets/Audio";

    static AudioClip[] Clips(string folder, string pattern = "*.wav")
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
        float quality = stream ? 0.7f : 0.9f;
        if (s.loadType != want || s.compressionFormat != AudioCompressionFormat.Vorbis || importer.forceToMono || !Mathf.Approximately(s.quality, quality))
        {
            s.loadType = want;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = quality;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = s;
            importer.forceToMono = false;   // keep the recordings' own stereo
            importer.loadInBackground = stream;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    static AudioClip Stream(string file) => ImportClip(AudioDir + "/" + file, true);

    static void WireAudio(DigAudio a)
    {
        a.mixer = BuildMixer();
        a.musicGroup = MixerGroup(a.mixer, "Music");
        a.sfxGroup = MixerGroup(a.mixer, "SFX");
        a.ambienceGroup = MixerGroup(a.mixer, "Ambience");
        a.uiGroup = MixerGroup(a.mixer, "UI");

        a.dirtBite = Clips("Dig/Dirt", "Bite_*.wav");
        a.dirtLift = Clips("Dig/Dirt", "Lift_*.wav");
        a.clayBite = Clips("Dig/Clay");
        a.stoneBite = Clips("Dig/Stone");
        a.trickle = Clips("Dig/Trickle");
        a.clods = Clips("Dig/Clods");
        a.stepsGrass = Clips("Steps/Grass");
        a.stepsDirt = Clips("Steps/Dirt");
        a.mineral = Clips("Minerals", "*.ogg");
        a.page = Clips("UI");
        a.meow = Clips("Cat", "Meow_*.wav");
        a.purr = ImportClip(AudioDir + "/Cat/Purr.wav", false);
        a.sleepyPurr = ImportClip(AudioDir + "/Cat/SleepyPurr.wav", false);
        a.birdCalls = Clips("Ambience/Birds");
        a.yard = Stream("Ambience/Yard.wav");
        a.garden = Stream("Ambience/Garden.wav");
        a.roomTone = Stream("Ambience/RoomTone.wav");
        a.musicYard = Stream("Music/Yard.ogg");
        a.musicUnderground = Stream("Music/Underground.ogg");
        a.musicEnding = Stream("Music/Ending.wav");
        EditorUtility.SetDirty(a);
        int shots = a.dirtBite.Length + a.dirtLift.Length + a.clayBite.Length + a.stoneBite.Length + a.trickle.Length + a.clods.Length
                  + a.stepsGrass.Length + a.stepsDirt.Length + a.mineral.Length + a.page.Length + a.meow.Length + a.birdCalls.Length;
        report.AppendLine("  audio: " + shots + " one-shots, 3 ambience loops, 3 music pieces, mixer " + (a.mixer != null ? "ok" : "MISSING"));
    }
}
