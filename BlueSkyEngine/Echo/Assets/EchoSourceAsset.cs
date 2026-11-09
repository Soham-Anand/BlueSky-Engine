using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlueSky.Core.Assets;
using Miniaudio;
using NVorbis;

namespace BlueSky.Echo;

/// <summary>
/// The canonical, converted audio asset behind every imported audio file.
///
/// An Echo Source owns the audio payload. It is written once at import time and
/// can be referenced by any number of Echo Supers. The payload is stored as
/// interleaved 16-bit stereo PCM so the file stays reasonably small while the
/// runtime can convert it to float PCM for the effect chain.
/// </summary>
public sealed class EchoSourceAsset
{
    public const string Extension = ".echosource";
    private const uint Magic = 0x43525345; // "ESRC" little-endian
    private const int FormatVersion = 1;
    private const int PayloadPcm16Stereo = 1;
    private const int WaveformBuckets = 2048;

    [JsonPropertyName("assetId")]
    public Guid AssetId { get; set; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Unnamed Source";

    [JsonPropertyName("originalFileName")]
    public string OriginalFileName { get; set; } = "";

    [JsonPropertyName("originalPath")]
    public string OriginalPath { get; set; } = "";

    [JsonPropertyName("sampleRate")]
    public int SampleRate { get; set; }

    [JsonPropertyName("channels")]
    public int Channels { get; set; } = 2;

    [JsonPropertyName("frameCount")]
    public long FrameCount { get; set; }

    [JsonPropertyName("durationSeconds")]
    public float DurationSeconds { get; set; }

    [JsonPropertyName("trimStartFrame")]
    public long TrimStartFrame { get; set; }

    [JsonPropertyName("trimEndFrame")]
    public long TrimEndFrame { get; set; }

    [JsonPropertyName("loopStartFrame")]
    public long LoopStartFrame { get; set; }

    [JsonPropertyName("loopEndFrame")]
    public long LoopEndFrame { get; set; }

    [JsonPropertyName("gain")]
    public float Gain { get; set; } = 1.0f;

    [JsonPropertyName("channel")]
    public AudioChannel Channel { get; set; } = AudioChannel.SFX;

    [JsonPropertyName("is3D")]
    public bool Is3D { get; set; } = true;

    [JsonPropertyName("minDistance")]
    public float MinDistance { get; set; } = 1.0f;

    [JsonPropertyName("maxDistance")]
    public float MaxDistance { get; set; } = 100.0f;

    [JsonPropertyName("attenuationCurve")]
    public DistanceAttenuationCurve AttenuationCurve { get; set; } = DistanceAttenuationCurve.Linear;

    /// <summary>Normalized 0..1 peak per waveform bucket, for the editor display.</summary>
    [JsonPropertyName("waveform")]
    public float[] Waveform { get; set; } = Array.Empty<float>();

    /// <summary>Interleaved stereo float PCM. Not serialized as JSON; stored in the binary payload.</summary>
    [JsonIgnore]
    public float[] Pcm { get; set; } = Array.Empty<float>();

    [JsonIgnore]
    public int EffectiveChannels => 2;

    [JsonIgnore]
    public long EffectiveFrameCount => Pcm.Length / 2;

    public float Duration => SampleRate > 0 ? (float)FrameCount / SampleRate : DurationSeconds;

    public long EffectiveTrimStart => Math.Clamp(TrimStartFrame, 0, FrameCount);
    public long EffectiveTrimEnd => TrimEndFrame <= 0 ? FrameCount : Math.Clamp(TrimEndFrame, EffectiveTrimStart, FrameCount);
    public long EffectiveLoopStart => Math.Clamp(LoopStartFrame, EffectiveTrimStart, EffectiveTrimEnd);
    public long EffectiveLoopEnd => LoopEndFrame <= 0 ? EffectiveTrimEnd : Math.Clamp(LoopEndFrame, EffectiveLoopStart, EffectiveTrimEnd);

    public void NormalizeRanges()
    {
        FrameCount = Math.Max(0, FrameCount);
        DurationSeconds = SampleRate > 0 ? (float)FrameCount / SampleRate : 0f;

        TrimStartFrame = Math.Clamp(TrimStartFrame, 0, FrameCount);
        if (TrimEndFrame <= 0 || TrimEndFrame > FrameCount) TrimEndFrame = FrameCount;
        if (TrimEndFrame < TrimStartFrame) TrimEndFrame = TrimStartFrame;

        LoopStartFrame = Math.Clamp(LoopStartFrame, TrimStartFrame, TrimEndFrame);
        if (LoopEndFrame <= 0 || LoopEndFrame > TrimEndFrame) LoopEndFrame = TrimEndFrame;
        if (LoopEndFrame < LoopStartFrame) LoopEndFrame = LoopStartFrame;
    }

    public bool Save(string path)
    {
        try
        {
            NormalizeRanges();

            string fullPath = EnsureExtension(path);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // If the caller only edited metadata, keep the existing payload.
            float[] pcm = Pcm ?? Array.Empty<float>();
            if (pcm.Length == 0 && File.Exists(fullPath))
                pcm = LoadPayloadOnly(fullPath) ?? Array.Empty<float>();

            if (pcm.Length > 0)
            {
                FrameCount = pcm.Length / 2;
                DurationSeconds = SampleRate > 0 ? (float)FrameCount / SampleRate : 0f;
                NormalizeRanges();
            }

            var json = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
            var payload = FloatToPcm16(pcm);

            using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(fs);
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(json.Length);
            writer.Write(json);
            writer.Write(PayloadPcm16Stereo);
            writer.Write((long)payload.Length);
            writer.Write(payload);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EchoSource] Save failed for '{path}': {ex.Message}");
            return false;
        }
    }

    public static EchoSourceAsset? Load(string path)
    {
        try
        {
            string fullPath = AssetPathResolver.Resolve(path);
            if (!File.Exists(fullPath))
                return null;

            using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);
            if (fs.Length < 16)
                return null;

            uint magic = reader.ReadUInt32();
            if (magic != Magic)
                return null;

            int version = reader.ReadInt32();
            if (version != FormatVersion)
                return null;

            int jsonLength = reader.ReadInt32();
            if (jsonLength <= 0 || jsonLength > 32 * 1024 * 1024 || jsonLength > fs.Length - fs.Position)
                return null;

            var json = reader.ReadBytes(jsonLength);
            var asset = JsonSerializer.Deserialize<EchoSourceAsset>(json, JsonOptions);
            if (asset == null)
                return null;

            int payloadFormat = reader.ReadInt32();
            if (fs.Position + sizeof(long) > fs.Length)
                return null;
            long payloadLength = reader.ReadInt64();
            if (payloadLength < 0 || payloadLength > fs.Length - fs.Position)
                return null;

            if (payloadFormat == PayloadPcm16Stereo)
            {
                var payload = reader.ReadBytes((int)payloadLength);
                asset.Pcm = Pcm16ToFloat(payload);
            }

            asset.NormalizeRanges();
            return asset;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EchoSource] Load failed for '{path}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Reads only the JSON header; used by the Asset Browser without loading audio.</summary>
    public static EchoSourceAsset? LoadHeader(string path)
    {
        try
        {
            string fullPath = AssetPathResolver.Resolve(path);
            if (!File.Exists(fullPath))
                return null;

            using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);
            if (fs.Length < 16)
                return null;

            if (reader.ReadUInt32() != Magic)
                return null;
            if (reader.ReadInt32() != FormatVersion)
                return null;

            int jsonLength = reader.ReadInt32();
            if (jsonLength <= 0 || jsonLength > 32 * 1024 * 1024 || jsonLength > fs.Length - fs.Position)
                return null;

            var json = reader.ReadBytes(jsonLength);
            var asset = JsonSerializer.Deserialize<EchoSourceAsset>(json, JsonOptions);
            asset?.NormalizeRanges();
            return asset;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Converts an audio file into an Echo Source next to the original.</summary>
    public static EchoSourceAsset? Import(string sourcePath, string? outputPath = null)
    {
        try
        {
            string resolved = AssetPathResolver.Resolve(sourcePath);
            if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
                return null;

            if (resolved.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                return Load(resolved);

            var pcm = EchoAudioDecoder.DecodeToStereo(resolved, out int sampleRate, out int channels, out string error);
            if (pcm == null || pcm.Length < 2 || sampleRate <= 0)
            {
                Console.WriteLine($"[EchoSource] Could not convert '{Path.GetFileName(resolved)}': {error}");
                return null;
            }

            var asset = new EchoSourceAsset
            {
                Name = Path.GetFileNameWithoutExtension(resolved),
                OriginalFileName = Path.GetFileName(resolved),
                OriginalPath = resolved,
                SampleRate = sampleRate,
                Channels = channels,
                Pcm = pcm,
                FrameCount = pcm.Length / 2,
                DurationSeconds = sampleRate > 0 ? (float)(pcm.Length / 2) / sampleRate : 0f,
                TrimStartFrame = 0,
                TrimEndFrame = pcm.Length / 2,
                LoopStartFrame = 0,
                LoopEndFrame = pcm.Length / 2,
                Waveform = BuildWaveform(pcm)
            };
            asset.NormalizeRanges();

            string target = outputPath ?? Path.ChangeExtension(resolved, Extension);
            if (!asset.Save(target))
                return null;

            Console.WriteLine($"[EchoSource] Converted '{Path.GetFileName(resolved)}' -> '{Path.GetFileName(target)}' ({asset.Duration:F2}s, {sampleRate}Hz)");
            return asset;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EchoSource] Import failed for '{sourcePath}': {ex.Message}");
            return null;
        }
    }

    public static float[] BuildWaveform(float[] pcm, int buckets = WaveformBuckets)
    {
        var result = new float[Math.Max(1, buckets)];
        if (pcm == null || pcm.Length < 2)
            return result;

        int frames = pcm.Length / 2;
        int bucketCount = result.Length;
        for (int b = 0; b < bucketCount; b++)
        {
            long start = (long)b * frames / bucketCount;
            long end = (long)(b + 1) * frames / bucketCount;
            if (end <= start) end = start + 1;

            float peak = 0f;
            for (long i = start; i < end && i < frames; i++)
            {
                float l = MathF.Abs(pcm[i * 2]);
                float r = MathF.Abs(pcm[i * 2 + 1]);
                if (l > peak) peak = l;
                if (r > peak) peak = r;
            }
            result[b] = Math.Clamp(peak, 0f, 1f);
        }
        return result;
    }

    private static float[]? LoadPayloadOnly(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs);
            if (fs.Length < 16) return null;
            if (reader.ReadUInt32() != Magic) return null;
            if (reader.ReadInt32() != FormatVersion) return null;
            int jsonLength = reader.ReadInt32();
            if (jsonLength <= 0 || jsonLength > 32 * 1024 * 1024 || jsonLength > fs.Length - fs.Position) return null;
            reader.BaseStream.Position += jsonLength;
            int payloadFormat = reader.ReadInt32();
            long payloadLength = reader.ReadInt64();
            if (payloadFormat != PayloadPcm16Stereo || payloadLength < 0 || payloadLength > fs.Length - fs.Position) return null;
            return Pcm16ToFloat(reader.ReadBytes((int)payloadLength));
        }
        catch
        {
            return null;
        }
    }

    private static byte[] FloatToPcm16(float[] pcm)
    {
        var bytes = new byte[pcm.Length * sizeof(short)];
        for (int i = 0; i < pcm.Length; i++)
        {
            float sample = Math.Clamp(pcm[i], -1f, 1f);
            short value = (short)MathF.Round(sample * 32767f);
            bytes[i * 2] = (byte)(value & 0xFF);
            bytes[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }
        return bytes;
    }

    private static float[] Pcm16ToFloat(byte[] bytes)
    {
        int sampleCount = bytes.Length / sizeof(short);
        var pcm = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short value = (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
            pcm[i] = value / 32768f;
        }
        return pcm;
    }

    private static string EnsureExtension(string path)
    {
        return path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? path : path + Extension;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// Decodes WAV/MP3/FLAC through miniaudio and OGG/Vorbis through NVorbis,
/// normalizing everything to interleaved stereo float PCM.
/// </summary>
public static unsafe class EchoAudioDecoder
{
    public static float[]? DecodeToStereo(string path, out int sampleRate, out int channels, out string error)
    {
        sampleRate = 0;
        channels = 0;
        error = "";

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            error = "file not found";
            return null;
        }

        if (path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".oga", StringComparison.OrdinalIgnoreCase))
            return DecodeOggToStereo(path, out sampleRate, out channels, out error);

        return DecodeWithMiniaudio(path, out sampleRate, out channels, out error);
    }

    private static float[]? DecodeWithMiniaudio(string path, out int sampleRate, out int channels, out string error)
    {
        sampleRate = 0;
        channels = 2;
        error = "";

        ma_decoder* decoder = (ma_decoder*)Marshal.AllocHGlobal(Marshal.SizeOf<ma_decoder>());
        IntPtr pathPtr = IntPtr.Zero;
        try
        {
            pathPtr = Marshal.StringToCoTaskMemUTF8(path);
            var config = ma.decoder_config_init(ma_format.ma_format_f32, 2, 0);
            var result = ma.decoder_init_file((sbyte*)pathPtr, &config, decoder);
            if (result != ma_result.MA_SUCCESS)
            {
                error = $"miniaudio decoder_init_file: {result}";
                return null;
            }

            ma_format format;
            uint nativeChannels = 0;
            uint nativeRate = 0;
            byte channelMap;
            ma.decoder_get_data_format(decoder, &format, &nativeChannels, &nativeRate, &channelMap, 0);
            sampleRate = (int)nativeRate;
            channels = (int)nativeChannels;

            ulong frames = 0;
            ma.decoder_get_length_in_pcm_frames(decoder, &frames);
            if (frames == 0 || frames > (ulong)(int.MaxValue / 2))
            {
                error = "empty or too-large audio stream";
                return null;
            }

            var pcm = new float[(int)frames * 2];
            ulong totalRead = 0;
            fixed (float* pcmPtr = pcm)
            {
                while (totalRead < frames)
                {
                    ulong read = 0;
                    var readResult = ma.decoder_read_pcm_frames(
                        decoder, pcmPtr + totalRead * 2, frames - totalRead, &read);
                    if (readResult != ma_result.MA_SUCCESS && readResult != ma_result.MA_AT_END)
                    {
                        error = $"miniaudio decoder_read_pcm_frames: {readResult}";
                        break;
                    }
                    if (read == 0)
                        break;
                    totalRead += read;
                }
            }

            if (totalRead == 0)
            {
                if (string.IsNullOrEmpty(error)) error = "no PCM frames decoded";
                return null;
            }

            if (totalRead < frames)
                Array.Resize(ref pcm, (int)totalRead * 2);

            return pcm;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
        finally
        {
            if (pathPtr != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pathPtr);
            ma.decoder_uninit(decoder);
            Marshal.FreeHGlobal((IntPtr)decoder);
        }
    }

    private static float[]? DecodeOggToStereo(string path, out int sampleRate, out int channels, out string error)
    {
        sampleRate = 0;
        channels = 0;
        error = "";

        try
        {
            using var reader = new VorbisReader(path);
            sampleRate = reader.SampleRate;
            int sourceChannels = reader.Channels;
            channels = 2;
            if (sourceChannels <= 0 || sampleRate <= 0)
            {
                error = "invalid OGG stream";
                return null;
            }

            long totalSamples = reader.TotalSamples;
            if (totalSamples > 0 && totalSamples * sourceChannels < int.MaxValue)
            {
                var interleaved = new float[(int)(totalSamples * sourceChannels)];
                int read = 0;
                while (read < interleaved.Length)
                {
                    int n = reader.ReadSamples(interleaved, read, interleaved.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < interleaved.Length)
                    Array.Resize(ref interleaved, read);
                return DownmixToStereo(interleaved, sourceChannels);
            }

            var all = new List<float>();
            var chunk = new float[4096 * sourceChannels];
            while (true)
            {
                int n = reader.ReadSamples(chunk, 0, chunk.Length);
                if (n <= 0) break;
                for (int i = 0; i < n; i++)
                    all.Add(chunk[i]);
            }
            if (all.Count == 0)
            {
                error = "no PCM samples decoded";
                return null;
            }
            return DownmixToStereo(all.ToArray(), sourceChannels);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static float[] DownmixToStereo(float[] interleaved, int sourceChannels)
    {
        if (sourceChannels == 2)
            return interleaved;

        int frames = interleaved.Length / Math.Max(1, sourceChannels);
        var stereo = new float[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            int baseIndex = i * sourceChannels;
            if (sourceChannels == 1)
            {
                float sample = interleaved[baseIndex];
                stereo[i * 2] = sample;
                stereo[i * 2 + 1] = sample;
            }
            else
            {
                float left = interleaved[baseIndex];
                float right = interleaved[baseIndex + 1];
                for (int c = 2; c < sourceChannels; c++)
                {
                    float v = interleaved[baseIndex + c];
                    left += v * 0.5f;
                    right += v * 0.5f;
                }
                float norm = 1f / MathF.Max(1f, 1f + (sourceChannels - 2) * 0.5f);
                stereo[i * 2] = left * norm;
                stereo[i * 2 + 1] = right * norm;
            }
        }
        return stereo;
    }
}
