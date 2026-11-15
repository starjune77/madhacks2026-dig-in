using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

// The audio mixer (Assets/Audio/DigIn.mixer): Master with Music, SFX, Ambience and UI
// groups, and "MasterVolume" exposed for the pause menu. Unity has no public API to make a
// mixer from a script, so this uses the editor's own mixer controller (by reflection),
// once; after that the asset is ordinary and editable in the Audio Mixer window.
public static partial class DigInSceneBuilder
{
    const string MixerPath = "Assets/Audio/DigIn.mixer";
    public static readonly string[] MixerGroups = { "Music", "SFX", "Ambience", "UI" };

    static AudioMixer BuildMixer()
    {
        var existing = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        if (existing != null)
        {
            // The pause menu's Music and Sound Effects settings (Ambience follows Sound Effects).
            ExposeVolume(existing, "Music", "MusicVolume");
            ExposeVolume(existing, "SFX", "SfxVolume");
            ExposeVolume(existing, "Ambience", "AmbienceVolume");
            Skipped(MixerPath);
            return existing;
        }

        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var asm = typeof(Editor).Assembly;
        var controllerType = asm.GetType("UnityEditor.Audio.AudioMixerController");
        var groupType = asm.GetType("UnityEditor.Audio.AudioMixerGroupController");
        var mixer = controllerType.GetMethod("CreateMixerControllerAtPath", Any).Invoke(null, new object[] { MixerPath });
        var master = controllerType.GetProperty("masterGroup", Any).GetValue(mixer);
        var create = controllerType.GetMethod("CreateNewGroup", Any);
        var addChild = controllerType.GetMethod("AddChildToParent", Any);
        foreach (var name in MixerGroups)
        {
            var group = create.Invoke(mixer, new object[] { name, false });
            addChild.Invoke(mixer, new object[] { group, master });
        }

        // Expose the master volume as "MasterVolume".
        var guid = (GUID)groupType.GetMethod("GetGUIDForVolume", Any).Invoke(master, null);
        var pathType = asm.GetType("UnityEditor.Audio.AudioGroupParameterPath");
        var path = System.Activator.CreateInstance(pathType, master, guid);
        controllerType.GetMethod("AddExposedParameter", Any).Invoke(mixer, new[] { path });
        var exposedProp = controllerType.GetProperty("exposedParameters", Any);
        var exposed = (System.Array)exposedProp.GetValue(mixer);
        for (int i = 0; i < exposed.Length; i++)
        {
            var p = exposed.GetValue(i);
            var guidField = p.GetType().GetField("guid", Any);
            if ((GUID)guidField.GetValue(p) != guid) continue;
            p.GetType().GetField("name", Any).SetValue(p, "MasterVolume");
            exposed.SetValue(p, i);
        }
        exposedProp.SetValue(mixer, exposed);
        EditorUtility.SetDirty((Object)mixer);
        AssetDatabase.SaveAssets();
        Created(MixerPath);
        return AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
    }

    // Exposes a group's volume under `param` (once).
    static void ExposeVolume(AudioMixer mixerAsset, string groupName, string param)
    {
        if (mixerAsset.GetFloat(param, out _)) return;
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var asm = typeof(Editor).Assembly;
        var controllerType = asm.GetType("UnityEditor.Audio.AudioMixerController");
        var groupType = asm.GetType("UnityEditor.Audio.AudioMixerGroupController");
        var group = MixerGroup(mixerAsset, groupName);
        if (group == null) return;
        var guid = (GUID)groupType.GetMethod("GetGUIDForVolume", Any).Invoke(group, null);
        var pathType = asm.GetType("UnityEditor.Audio.AudioGroupParameterPath");
        var path = System.Activator.CreateInstance(pathType, group, guid);
        controllerType.GetMethod("AddExposedParameter", Any).Invoke(mixerAsset, new[] { path });
        var exposedProp = controllerType.GetProperty("exposedParameters", Any);
        var exposed = (System.Array)exposedProp.GetValue(mixerAsset);
        for (int i = 0; i < exposed.Length; i++)
        {
            var p = exposed.GetValue(i);
            if ((GUID)p.GetType().GetField("guid", Any).GetValue(p) != guid) continue;
            p.GetType().GetField("name", Any).SetValue(p, param);
            exposed.SetValue(p, i);
        }
        exposedProp.SetValue(mixerAsset, exposed);
        EditorUtility.SetDirty(mixerAsset);
        AssetDatabase.SaveAssets();
        report.AppendLine("  exposed " + param);
    }

    static AudioMixerGroup MixerGroup(AudioMixer mixer, string name) =>
        mixer == null ? null : mixer.FindMatchingGroups("Master/" + name).FirstOrDefault();
}
