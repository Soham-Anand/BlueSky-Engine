using System;
using System.IO;
using System.Numerics;

namespace BlueSky.Echo;

/// <summary>
/// Static façade used by TeaScript (and other scripting layers) to trigger
/// audio without holding a direct reference to the Echo instance.
/// </summary>
public static class EchoTeaScriptBridge
{
    /// <summary>The active Echo instance, set by the editor/runtime at startup.</summary>
    public static Echo? Active { get; set; }

    public static bool PlaySound(string clipPath, float volume = 1f, bool loop = false,
                                 bool is3D = true, float x = 0f, float y = 0f, float z = 0f)
    {
        if (Active == null)
            return false;

        string? clipName = LoadClip(clipPath);
        if (clipName == null)
            return false;

        var source = Active.PlaySound(clipName, new Vector3(x, y, z),
                                      Math.Clamp(volume, 0f, 8f), loop, AudioChannel.SFX, is3D);
        return source != null;
    }

    public static bool PlayMusic(string clipPath, float volume = 1f, bool loop = true)
    {
        if (Active == null)
            return false;

        string? clipName = LoadClip(clipPath);
        if (clipName == null)
            return false;

        return Active.PlayMusic(clipName, Math.Clamp(volume, 0f, 8f), loop) != null;
    }

    public static void StopAllSounds() => Active?.StopAllSounds();

    public static void SetChannelVolume(string channelName, float volume)
    {
        if (Active == null)
            return;

        if (Enum.TryParse<AudioChannel>(channelName, ignoreCase: true, out var channel))
            Active.Mixer.SetChannelVolume(channel, Math.Clamp(volume, 0f, 1f));
    }

    private static string? LoadClip(string clipPath)
    {
        if (string.IsNullOrWhiteSpace(clipPath))
            return null;

        string resolved = BlueSky.Core.Assets.AssetPathResolver.Resolve(clipPath);
        if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
        {
            Console.WriteLine($"[Echo] TeaScript audio clip not found: {clipPath}");
            return null;
        }

        string clipName = Path.GetFileNameWithoutExtension(resolved);
        Active!.LoadClip(clipName, resolved);
        return clipName;
    }
}
