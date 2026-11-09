using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Miniaudio;

namespace BlueSky.Echo;

/// <summary>
/// Cross-platform Echo backend built on miniaudio (via Miniaudio-CS).
///
/// One ma_engine owns the device, mixer and listener. Each playing
/// AudioSource gets its own ma_sound addressed by a stable integer handle
/// stored in AudioSource.BackendHandle.
///
/// WAV/MP3/FLAC are decoded natively by miniaudio. OGG/Vorbis is decoded
/// with NVorbis (the prebuilt miniaudio natives do not include stb_vorbis)
/// and handed to miniaudio as an in-memory audio buffer.
///
/// Initialization is intentionally forgiving: if no audio device is
/// available (CI, headless servers) the backend logs once and turns every
/// call into a no-op so the rest of the engine keeps running.
/// </summary>
public sealed unsafe class MiniAudioBackend : IAudioBackend
{
    private sealed class SoundEntry
    {
        public IntPtr Sound;
        public IntPtr AudioBuffer; // 0 for file-backed sounds
        public EchoEffectDataSource? EffectSource;
        public EchoSuperAsset? Super;
        public int SuperRevision = -1;
    }

    private ma_engine* _engine;
    private bool _initialized;
    private bool _failed;
    private int _nextHandle = 1;
    // Pointer types cannot be generic arguments, so native pointers live in IntPtr.
    private readonly Dictionary<int, SoundEntry> _sounds = new();

    public bool IsAvailable => _initialized;

    public void Initialize()
    {
        if (_initialized || _failed)
            return;

        try
        {
            _engine = (ma_engine*)Marshal.AllocHGlobal(Marshal.SizeOf<ma_engine>());
            var config = ma.engine_config_init();
            var result = ma.engine_init(&config, _engine);
            if (result != ma_result.MA_SUCCESS)
                throw new InvalidOperationException($"ma_engine_init failed: {result}");

            result = ma.engine_start(_engine);
            if (result != ma_result.MA_SUCCESS)
                throw new InvalidOperationException($"ma_engine_start failed: {result}");

            _initialized = true;
            Console.WriteLine($"[Echo] miniaudio backend online ({ma.engine_get_channels(_engine)}ch @ {ma.engine_get_sample_rate(_engine)} Hz)");
        }
        catch (Exception ex)
        {
            _failed = true;
            if (_engine != null)
            {
                try { ma.engine_uninit(_engine); } catch { /* best effort */ }
                Marshal.FreeHGlobal((IntPtr)_engine);
                _engine = null;
            }
            Console.WriteLine($"[Echo] Audio disabled (no usable device): {ex.Message}");
        }
    }

    public AudioClip? LoadAudioClip(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            Console.WriteLine($"[Echo] Audio file not found: {filePath}");
            return null;
        }

        var clip = new AudioClip
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath
        };

        // OGG/Vorbis metadata comes from NVorbis; miniaudio's prebuilt natives
        // do not ship stb_vorbis.
        if (IsOggFile(filePath))
        {
            try
            {
                using var reader = new NVorbis.VorbisReader(filePath);
                clip.SampleRate = reader.SampleRate;
                clip.Channels = reader.Channels;
                clip.Duration = (float)reader.TotalTime.TotalSeconds;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Echo] OGG probe failed for '{filePath}': {ex.Message}");
            }
            return clip;
        }

        if (!_initialized)
            return clip;

        // Probe metadata with a decoder so callers get duration/rate even
        // though playback decodes through ma_sound.
        ma_decoder* decoder = (ma_decoder*)Marshal.AllocHGlobal(Marshal.SizeOf<ma_decoder>());
        IntPtr pathPtr = IntPtr.Zero;
        try
        {
            pathPtr = Marshal.StringToCoTaskMemUTF8(filePath);
            var config = ma.decoder_config_init(ma_format.ma_format_f32, 0, 0);
            var result = ma.decoder_init_file((sbyte*)pathPtr, &config, decoder);
            if (result == ma_result.MA_SUCCESS)
            {
                ma_format format;
                uint channels = 0;
                uint sampleRate = 0;
                byte channelMap;
                ma.decoder_get_data_format(decoder, &format, &channels, &sampleRate, &channelMap, 0);
                ulong frames = 0;
                ma.decoder_get_length_in_pcm_frames(decoder, &frames);
                clip.Channels = (int)channels;
                clip.SampleRate = (int)sampleRate;
                clip.Duration = sampleRate > 0 ? (float)frames / sampleRate : 0f;
                ma.decoder_uninit(decoder);
            }
            else
            {
                Console.WriteLine($"[Echo] Could not decode '{filePath}': {result}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Echo] Clip probe failed for '{filePath}': {ex.Message}");
        }
        finally
        {
            if (pathPtr != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pathPtr);
            Marshal.FreeHGlobal((IntPtr)decoder);
        }

        return clip;
    }

    public void PlaySource(AudioSource source)
    {
        if (!_initialized || source.Clip == null)
            return;

        // Echo Source / Echo Super: canonical PCM + a live effect chain.
        if (source.Clip.SourceAsset != null || source.Clip.SuperAsset != null || source.Super != null)
        {
            PlayEchoSource(source);
            return;
        }

        string path = source.Clip.FilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        // If this source is already playing, restart it cleanly.
        StopSource(source);

        if (IsOggFile(path))
        {
            PlayOggSource(source, path);
            return;
        }

        ma_sound* sound = (ma_sound*)Marshal.AllocHGlobal(Marshal.SizeOf<ma_sound>());
        IntPtr pathPtr = IntPtr.Zero;
        try
        {
            pathPtr = Marshal.StringToCoTaskMemUTF8(path);
            uint flags = (uint)ma_sound_flags.MA_SOUND_FLAG_DECODE;
            if (source.Loop)
                flags |= (uint)ma_sound_flags.MA_SOUND_FLAG_LOOPING;

            var result = ma.sound_init_from_file(_engine, (sbyte*)pathPtr, flags, null, null, sound);
            if (result != ma_result.MA_SUCCESS)
            {
                Console.WriteLine($"[Echo] sound_init_from_file failed for '{path}': {result}");
                Marshal.FreeHGlobal((IntPtr)sound);
                return;
            }

            ApplySourceSettings(sound, source);
            ma.sound_start(sound);

            int handle = _nextHandle++;
            _sounds[handle] = new SoundEntry { Sound = (IntPtr)sound };
            source.BackendHandle = handle;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Echo] PlaySource failed: {ex.Message}");
            Marshal.FreeHGlobal((IntPtr)sound);
        }
        finally
        {
            if (pathPtr != IntPtr.Zero)
                Marshal.FreeCoTaskMem(pathPtr);
        }
    }

    /// <summary>
    /// Play an Echo Source with an optional Super effect chain. The custom data
    /// source reads the Source's converted PCM and processes it live.
    /// </summary>
    private void PlayEchoSource(AudioSource source)
    {
        var sourceAsset = source.Clip?.SourceAsset ?? source.Super?.ResolveSource();
        if (sourceAsset == null)
        {
            Console.WriteLine("[Echo] Echo Source has no audio payload.");
            return;
        }

        var super = source.Super ?? source.Clip?.SuperAsset;
        StopSource(source);

        var dataSource = new EchoEffectDataSource(sourceAsset, super, source.Loop);
        ma_sound* sound = (ma_sound*)Marshal.AllocHGlobal(Marshal.SizeOf<ma_sound>());

        try
        {
            uint flags = source.Loop ? (uint)ma_sound_flags.MA_SOUND_FLAG_LOOPING : 0u;
            var result = ma.sound_init_from_data_source(
                _engine, (void*)dataSource.NativePointer, flags, null, sound);
            if (result != ma_result.MA_SUCCESS)
            {
                Console.WriteLine($"[Echo] sound_init_from_data_source failed for Echo Source: {result}");
                dataSource.Dispose();
                Marshal.FreeHGlobal((IntPtr)sound);
                return;
            }

            ApplySourceSettings(sound, source);
            ma.sound_start(sound);

            int handle = _nextHandle++;
            _sounds[handle] = new SoundEntry
            {
                Sound = (IntPtr)sound,
                EffectSource = dataSource,
                Super = super,
                SuperRevision = super?.Revision ?? -1
            };
            source.BackendHandle = handle;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Echo] PlayEchoSource failed: {ex.Message}");
            dataSource.Dispose();
            Marshal.FreeHGlobal((IntPtr)sound);
        }
    }

    /// <summary>
    /// Decode an OGG/Vorbis file to interleaved float PCM, then feed it to
    /// miniaudio through an in-memory ma_audio_buffer.
    /// </summary>
    private void PlayOggSource(AudioSource source, string path)
    {
        float[]? pcm = DecodeOgg(path, out int channels);
        if (pcm == null || pcm.Length == 0 || channels <= 0)
            return;

        ma_sound* sound = (ma_sound*)Marshal.AllocHGlobal(Marshal.SizeOf<ma_sound>());
        ma_audio_buffer* buffer = (ma_audio_buffer*)Marshal.AllocHGlobal(Marshal.SizeOf<ma_audio_buffer>());

        try
        {
            fixed (float* pcmPtr = pcm)
            {
                var config = ma.audio_buffer_config_init(
                    ma_format.ma_format_f32,
                    (uint)channels,
                    (ulong)(pcm.Length / channels),
                    pcmPtr,
                    null);

                var bufferResult = ma.audio_buffer_init_copy(&config, buffer);
                if (bufferResult != ma_result.MA_SUCCESS)
                {
                    Console.WriteLine($"[Echo] OGG audio_buffer_init_copy failed for '{path}': {bufferResult}");
                    Marshal.FreeHGlobal((IntPtr)sound);
                    Marshal.FreeHGlobal((IntPtr)buffer);
                    return;
                }
            }

            uint flags = source.Loop ? (uint)ma_sound_flags.MA_SOUND_FLAG_LOOPING : 0u;
            var soundResult = ma.sound_init_from_data_source(_engine, buffer, flags, null, sound);
            if (soundResult != ma_result.MA_SUCCESS)
            {
                Console.WriteLine($"[Echo] OGG sound_init_from_data_source failed for '{path}': {soundResult}");
                ma.audio_buffer_uninit(buffer);
                Marshal.FreeHGlobal((IntPtr)sound);
                Marshal.FreeHGlobal((IntPtr)buffer);
                return;
            }

            ApplySourceSettings(sound, source);
            ma.sound_start(sound);

            int handle = _nextHandle++;
            _sounds[handle] = new SoundEntry
            {
                Sound = (IntPtr)sound,
                AudioBuffer = (IntPtr)buffer
            };
            source.BackendHandle = handle;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Echo] PlayOggSource failed: {ex.Message}");
            Marshal.FreeHGlobal((IntPtr)sound);
            Marshal.FreeHGlobal((IntPtr)buffer);
        }
    }

    private static float[]? DecodeOgg(string path, out int channels)
    {
        channels = 0;
        try
        {
            using var reader = new NVorbis.VorbisReader(path);
            channels = reader.Channels;
            long totalSamples = reader.TotalSamples;
            if (channels <= 0 || totalSamples <= 0)
                return null;

            long interleaved = totalSamples * channels;
            if (interleaved > int.MaxValue)
            {
                Console.WriteLine($"[Echo] OGG file too large to decode: {path}");
                return null;
            }

            var pcm = new float[(int)interleaved];
            int read = 0;
            while (read < pcm.Length)
            {
                int n = reader.ReadSamples(pcm, read, pcm.Length - read);
                if (n <= 0)
                    break;
                read += n;
            }

            if (read == 0)
                return null;

            if (read < pcm.Length)
                Array.Resize(ref pcm, read);

            return pcm;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Echo] OGG decode failed for '{path}': {ex.Message}");
            return null;
        }
    }

    private static void ApplySourceSettings(ma_sound* sound, AudioSource source)
    {
        // Echo's SpatialAudio is the single spatializer. Leaving miniaudio's
        // spatialization on applies attenuation twice.
        ma.sound_set_spatialization_enabled(sound, 0);
        ma.sound_set_volume(sound, source.CalculatedVolume);
        ma.sound_set_pitch(sound, source.CalculatedPitch);
        ma.sound_set_pan(sound, source.Pan);
        ma.sound_set_looping(sound, source.Loop ? 1u : 0u);
    }

    private static bool IsOggFile(string path) =>
        path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".oga", StringComparison.OrdinalIgnoreCase);

    public void StopSource(AudioSource source)
    {
        if (!_sounds.TryGetValue(source.BackendHandle, out var entry))
            return;

        var sound = (ma_sound*)entry.Sound;
        try
        {
            ma.sound_stop(sound);
            ma.sound_uninit(sound);
        }
        catch
        {
            // best effort during shutdown
        }

        if (entry.AudioBuffer != IntPtr.Zero)
        {
            try { ma.audio_buffer_uninit((ma_audio_buffer*)entry.AudioBuffer); } catch { /* best effort */ }
            Marshal.FreeHGlobal(entry.AudioBuffer);
        }

        entry.EffectSource?.Dispose();

        Marshal.FreeHGlobal(entry.Sound);
        _sounds.Remove(source.BackendHandle);
        source.BackendHandle = -1;
    }

    public void UpdateSource(AudioSource source)
    {
        if (!_sounds.TryGetValue(source.BackendHandle, out var entry))
            return;

        var sound = (ma_sound*)entry.Sound;
        ma.sound_set_volume(sound, source.CalculatedVolume);
        ma.sound_set_pitch(sound, Math.Clamp(source.CalculatedPitch, 0.01f, 8.0f));
        ma.sound_set_pan(sound, Math.Clamp(source.Pan, -1.0f, 1.0f));
        ma.sound_set_looping(sound, source.Loop ? 1u : 0u);

        // Push live Super edits into the audio-thread chain.
        var super = source.Super ?? source.Clip?.SuperAsset;
        if (entry.EffectSource != null && super != null && super.Revision != entry.SuperRevision)
        {
            entry.EffectSource.PostSuper(super);
            entry.Super = super;
            entry.SuperRevision = super.Revision;
        }

        // If the sound reached its end, let Echo reap it.
        if (ma.sound_at_end(sound) != 0 && !source.Loop)
            source.IsPlaying = false;
    }

    public void PauseSource(AudioSource source)
    {
        if (!_sounds.TryGetValue(source.BackendHandle, out var entry))
            return;

        // ma_sound_stop does not rewind: the cursor stays where it is, so a later start continues from there.
        ma.sound_stop((ma_sound*)entry.Sound);
    }

    public void ResumeSource(AudioSource source)
    {
        if (!_sounds.TryGetValue(source.BackendHandle, out var entry))
            return;

        ma.sound_start((ma_sound*)entry.Sound);
    }

    public void SetListenerPosition(Vector3 position, Vector3 forward, Vector3 up)
    {
        if (!_initialized)
            return;

        ma.engine_listener_set_position(_engine, 0, position.X, position.Y, position.Z);
        ma.engine_listener_set_direction(_engine, 0, forward.X, forward.Y, forward.Z);
        ma.engine_listener_set_world_up(_engine, 0, up.X, up.Y, up.Z);
    }

    public void Dispose()
    {
        foreach (var kv in _sounds)
        {
            var entry = kv.Value;
            var sound = (ma_sound*)entry.Sound;
            try
            {
                ma.sound_stop(sound);
                ma.sound_uninit(sound);
            }
            catch { /* best effort */ }
            Marshal.FreeHGlobal(entry.Sound);

            if (entry.AudioBuffer != IntPtr.Zero)
            {
                try { ma.audio_buffer_uninit((ma_audio_buffer*)entry.AudioBuffer); } catch { /* best effort */ }
                Marshal.FreeHGlobal(entry.AudioBuffer);
            }

            entry.EffectSource?.Dispose();
        }
        _sounds.Clear();

        if (_engine != null)
        {
            try { ma.engine_uninit(_engine); } catch { /* best effort */ }
            Marshal.FreeHGlobal((IntPtr)_engine);
            _engine = null;
        }

        _initialized = false;
    }
}
