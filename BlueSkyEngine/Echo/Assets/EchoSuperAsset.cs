using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlueSky.Core.Assets;

namespace BlueSky.Echo;

/// <summary>The effect kinds supported by an Echo Super's live chain.</summary>
public enum EchoEffectType
{
    Gain,
    Equalizer,
    LowPass,
    HighPass,
    Delay,
    Reverb,
    Compressor,
    Distortion
}

/// <summary>
/// One slot in an Echo Super's effect rack.
///
/// This is a flat parameter bag so the JSON stays readable and the editor can
/// add/remove/reorder slots without a polymorphic serializer. Only the fields
/// relevant to <see cref="Type"/> are used by the DSP.
/// </summary>
public sealed class EchoEffectSettings
{
    [JsonPropertyName("type")]
    public EchoEffectType Type { get; set; } = EchoEffectType.Gain;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Wet/dry blend for effects that have a wet path. 1 = fully wet.</summary>
    [JsonPropertyName("mix")]
    public float Mix { get; set; } = 1.0f;

    // Gain
    [JsonPropertyName("gainDb")]
    public float GainDb { get; set; }

    // Equalizer
    [JsonPropertyName("lowGainDb")]
    public float LowGainDb { get; set; }

    [JsonPropertyName("midGainDb")]
    public float MidGainDb { get; set; }

    [JsonPropertyName("highGainDb")]
    public float HighGainDb { get; set; }

    [JsonPropertyName("midFrequency")]
    public float MidFrequency { get; set; } = 1000f;

    // Filter
    [JsonPropertyName("cutoff")]
    public float Cutoff { get; set; } = 1000f;

    [JsonPropertyName("resonance")]
    public float Resonance { get; set; } = 0.707f;

    // Delay
    [JsonPropertyName("delayTime")]
    public float DelayTime { get; set; } = 0.25f;

    [JsonPropertyName("feedback")]
    public float Feedback { get; set; } = 0.3f;

    [JsonPropertyName("pingPong")]
    public bool PingPong { get; set; }

    // Reverb
    [JsonPropertyName("roomSize")]
    public float RoomSize { get; set; } = 0.5f;

    [JsonPropertyName("damping")]
    public float Damping { get; set; } = 0.5f;

    [JsonPropertyName("width")]
    public float Width { get; set; } = 1.0f;

    // Compressor
    [JsonPropertyName("thresholdDb")]
    public float ThresholdDb { get; set; } = -18f;

    [JsonPropertyName("ratio")]
    public float Ratio { get; set; } = 4.0f;

    [JsonPropertyName("attackMs")]
    public float AttackMs { get; set; } = 10f;

    [JsonPropertyName("releaseMs")]
    public float ReleaseMs { get; set; } = 120f;

    [JsonPropertyName("makeupDb")]
    public float MakeupDb { get; set; }

    // Distortion
    [JsonPropertyName("drive")]
    public float Drive { get; set; } = 0.4f;

    [JsonPropertyName("tone")]
    public float Tone { get; set; } = 0.5f;

    [JsonIgnore]
    public string DisplayName => Type switch
    {
        EchoEffectType.Gain => "Gain",
        EchoEffectType.Equalizer => "3-Band EQ",
        EchoEffectType.LowPass => "Low-Pass Filter",
        EchoEffectType.HighPass => "High-Pass Filter",
        EchoEffectType.Delay => "Delay / Echo",
        EchoEffectType.Reverb => "Reverb",
        EchoEffectType.Compressor => "Compressor",
        EchoEffectType.Distortion => "Distortion",
        _ => Type.ToString()
    };

    public static EchoEffectSettings CreateDefault(EchoEffectType type)
    {
        var effect = new EchoEffectSettings { Type = type };

        switch (type)
        {
            case EchoEffectType.Gain:
                effect.GainDb = 0f;
                effect.Mix = 1f;
                break;
            case EchoEffectType.Equalizer:
                effect.Mix = 1f;
                break;
            case EchoEffectType.LowPass:
                effect.Cutoff = 8000f;
                effect.Resonance = 0.707f;
                effect.Mix = 1f;
                break;
            case EchoEffectType.HighPass:
                effect.Cutoff = 80f;
                effect.Resonance = 0.707f;
                effect.Mix = 1f;
                break;
            case EchoEffectType.Delay:
                effect.DelayTime = 0.25f;
                effect.Feedback = 0.3f;
                effect.Mix = 0.25f;
                break;
            case EchoEffectType.Reverb:
                effect.RoomSize = 0.5f;
                effect.Damping = 0.5f;
                effect.Width = 1.0f;
                effect.Mix = 0.25f;
                break;
            case EchoEffectType.Compressor:
                effect.ThresholdDb = -18f;
                effect.Ratio = 4f;
                effect.AttackMs = 10f;
                effect.ReleaseMs = 120f;
                effect.MakeupDb = 0f;
                effect.Mix = 1f;
                break;
            case EchoEffectType.Distortion:
                effect.Drive = 0.4f;
                effect.Tone = 0.5f;
                effect.Mix = 0.5f;
                break;
        }

        return effect;
    }

    public EchoEffectSettings Clone() => (EchoEffectSettings)MemberwiseClone();
}

/// <summary>Output/spatial defaults stored on a Super.</summary>
public sealed class EchoOutputSettings
{
    [JsonPropertyName("gain")]
    public float Gain { get; set; } = 1.0f;

    [JsonPropertyName("channel")]
    public AudioChannel Channel { get; set; } = AudioChannel.SFX;

    [JsonPropertyName("loop")]
    public bool Loop { get; set; }

    [JsonPropertyName("is3D")]
    public bool Is3D { get; set; } = true;

    [JsonPropertyName("minDistance")]
    public float MinDistance { get; set; } = 1.0f;

    [JsonPropertyName("maxDistance")]
    public float MaxDistance { get; set; } = 100.0f;

    [JsonPropertyName("attenuationCurve")]
    public DistanceAttenuationCurve AttenuationCurve { get; set; } = DistanceAttenuationCurve.Linear;

    public EchoOutputSettings Clone() => (EchoOutputSettings)MemberwiseClone();
}

/// <summary>
/// An Echo Super: a live composite that references one <see cref="EchoSourceAsset"/>
/// and applies an ordered effect chain to it at playback time.
///
/// Supers never copy audio. They are pure JSON presets; the source stays the
/// single canonical payload, so many Supers can reuse it cheaply.
/// </summary>
public sealed class EchoSuperAsset
{
    public const string Extension = ".echosuper";

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("assetId")]
    public Guid AssetId { get; set; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Unnamed Super";

    [JsonPropertyName("sourceId")]
    public Guid SourceId { get; set; }

    /// <summary>Project-relative path to the referenced .echosource.</summary>
    [JsonPropertyName("sourcePath")]
    public string SourcePath { get; set; } = "";

    [JsonPropertyName("effects")]
    public List<EchoEffectSettings> Effects { get; set; } = new();

    [JsonPropertyName("output")]
    public EchoOutputSettings Output { get; set; } = new();

    /// <summary>Bumped by the editor whenever a live parameter changes.</summary>
    [JsonIgnore]
    public int Revision;

    [JsonIgnore]
    public string FilePath { get; set; } = "";

    public void Touch() => Revision++;

    public EchoSourceAsset? ResolveSource()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
            return null;

        return EchoSourceAsset.Load(SourcePath);
    }

    public EchoSuperAsset Clone()
    {
        var clone = new EchoSuperAsset
        {
            Version = Version,
            AssetId = AssetId,
            Name = Name,
            SourceId = SourceId,
            SourcePath = SourcePath,
            Output = Output.Clone(),
            FilePath = FilePath
        };

        foreach (var effect in Effects)
            clone.Effects.Add(effect.Clone());

        return clone;
    }

    public bool Save(string path)
    {
        try
        {
            string fullPath = EnsureExtension(path);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            FilePath = fullPath;
            var json = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
            File.WriteAllBytes(fullPath, json);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EchoSuper] Save failed for '{path}': {ex.Message}");
            return false;
        }
    }

    public static EchoSuperAsset? Load(string path)
    {
        try
        {
            string fullPath = AssetPathResolver.Resolve(path);
            if (!File.Exists(fullPath))
                return null;

            var json = File.ReadAllBytes(fullPath);
            var super = JsonSerializer.Deserialize<EchoSuperAsset>(json, JsonOptions);
            if (super == null)
                return null;

            super.FilePath = fullPath;
            super.Effects ??= new List<EchoEffectSettings>();
            super.Output ??= new EchoOutputSettings();
            return super;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EchoSuper] Load failed for '{path}': {ex.Message}");
            return null;
        }
    }

    public static EchoSuperAsset CreateForSource(EchoSourceAsset source, string sourcePath, string? projectDir = null)
    {
        return new EchoSuperAsset
        {
            Name = $"{source.Name} Super",
            SourceId = source.AssetId,
            SourcePath = ToProjectRelative(sourcePath, projectDir),
            Effects = new List<EchoEffectSettings>(),
            Output = new EchoOutputSettings
            {
                Gain = source.Gain,
                Channel = source.Channel,
                Is3D = source.Is3D,
                MinDistance = source.MinDistance,
                MaxDistance = source.MaxDistance,
                AttenuationCurve = source.AttenuationCurve
            }
        };
    }

    /// <summary>Default sibling path for a new Super: "Name.echosuper".</summary>
    public static string DefaultPathFor(string sourcePath)
    {
        string dir = Path.GetDirectoryName(sourcePath) ?? "";
        string name = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(dir, name + Extension);
    }

    private static string ToProjectRelative(string path, string? projectDir)
    {
        if (string.IsNullOrEmpty(projectDir))
            return path;

        try
        {
            string relative = Path.GetRelativePath(projectDir, path).Replace('\\', '/');
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                return relative;
        }
        catch
        {
            // Fall through.
        }

        return path;
    }

    private static string EnsureExtension(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? path : path + Extension;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}
