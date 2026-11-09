using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using BlueSky.Core.Assets;

namespace BlueSky.Echo;

/// <summary>
/// Echo - BlueSky's audio system
/// Handles 3D spatial audio, music, and sound effects
/// </summary>
public class Echo : IDisposable
{
    private readonly IAudioBackend _backend;
    private readonly Dictionary<string, AudioClip> _clips = new();
    private readonly Dictionary<string, AudioClip> _clipsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AudioSource> _sources = new();
    // Sounds that SetGamePaused(true) paused, so resuming the game resumes exactly those and nothing else.
    private readonly HashSet<AudioSource> _pausedByGame = new();

    /// <summary>True while the game is paused (see <see cref="SetGamePaused"/>).</summary>
    public bool IsGamePaused { get; private set; }
    
    public AudioMixer Mixer { get; } = new();
    public SpatialAudio Spatial { get; } = new();
    
    private Vector3 _listenerPosition = Vector3.Zero;
    private Vector3 _listenerVelocity = Vector3.Zero;
    private Vector3 _listenerForward = new Vector3(0, 0, -1);
    private Vector3 _listenerUp = Vector3.UnitY;
    
    private bool _disposed;
    
    public Echo(IAudioBackend backend)
    {
        _backend = backend;
        _backend.Initialize();
        Console.WriteLine("[Echo] Audio system initialized");
    }
    
    /// <summary>
    /// Loads an audio clip, Echo Source, or Echo Super. Echo Source/Super paths
    /// resolve to converted assets; raw WAV/OGG/MP3/FLAC still use the backend.
    /// Returns the cached/loaded clip so callers can attach live editor state.
    /// </summary>
    public AudioClip? LoadClip(string name, string filePath, EchoSuperAsset? superOverride = null, EchoSourceAsset? sourceOverride = null)
    {
        string resolved = AssetPathResolver.Resolve(filePath);
        if (string.IsNullOrWhiteSpace(resolved))
            resolved = filePath;

        if (_clipsByPath.TryGetValue(resolved, out var cached))
        {
            if (sourceOverride != null)
                cached.SourceAsset = sourceOverride;
            if (superOverride != null)
                cached.SuperAsset = superOverride;
            cached.Name = name;
            _clips[name] = cached;
            return cached;
        }

        AudioClip? clip = null;
        string ext = Path.GetExtension(resolved);

        if (ext.Equals(EchoSourceAsset.Extension, StringComparison.OrdinalIgnoreCase))
        {
            var source = sourceOverride ?? EchoSourceAsset.Load(resolved);
            if (source != null)
            {
                clip = new AudioClip
                {
                    Name = name,
                    FilePath = resolved,
                    SourceAsset = source,
                    SampleRate = source.SampleRate,
                    Channels = 2,
                    Duration = source.Duration
                };
            }
            else
            {
                Console.WriteLine($"[Echo] Could not load Echo Source: {resolved}");
            }
        }
        else if (ext.Equals(EchoSuperAsset.Extension, StringComparison.OrdinalIgnoreCase))
        {
            var super = superOverride ?? EchoSuperAsset.Load(resolved);
            var source = sourceOverride ?? super?.ResolveSource();
            if (super != null && source != null)
            {
                clip = new AudioClip
                {
                    Name = name,
                    FilePath = resolved,
                    SuperAsset = super,
                    SourceAsset = source,
                    SampleRate = source.SampleRate,
                    Channels = 2,
                    Duration = source.Duration
                };
            }
            else
            {
                Console.WriteLine($"[Echo] Could not load Echo Super or its Source: {resolved}");
            }
        }
        else
        {
            clip = _backend.LoadAudioClip(resolved);
        }

        if (clip != null)
        {
            clip.Name = name;
            _clips[name] = clip;
            _clipsByPath[resolved] = clip;
            Console.WriteLine($"[Echo] Loaded audio clip: {name}");
        }

        return clip;
    }
    
    public AudioSource? PlaySound(string clipName, Vector3 position, float volume = 1.0f, bool loop = false, AudioChannel channel = AudioChannel.SFX, bool is3D = true)
    {
        if (!_clips.TryGetValue(clipName, out var clip))
        {
            Console.WriteLine($"[Echo] Audio clip not found: {clipName}");
            return null;
        }
        
        var source = new AudioSource
        {
            Clip = clip,
            Super = clip.SuperAsset,
            Position = position,
            Volume = volume,
            Channel = channel,
            Loop = loop,
            Is3D = is3D,
            IsPlaying = true
        };

        ApplySuperDefaults(source, clip.SuperAsset);
        
        _sources.Add(source);
        Mixer.RegisterSource(source);
        
        // Initial process
        Spatial.ProcessSource(source, 0.0f);
        source.CalculatedVolume *= Mixer.GetFinalVolumeMultiplier(source.Channel);
        
        _backend.PlaySource(source);
        
        return source;
    }

    /// <summary>Applies a Super's output defaults to a voice without overriding
    /// the instance's explicit 3D/position choice.</summary>
    private static void ApplySuperDefaults(AudioSource source, EchoSuperAsset? super)
    {
        if (super?.Output == null)
            return;

        var output = super.Output;
        source.Volume *= Math.Clamp(output.Gain, 0f, 8f);
        source.Channel = output.Channel;
        source.Loop |= output.Loop;
        source.MinDistance = Math.Max(0.01f, output.MinDistance);
        source.MaxDistance = Math.Max(source.MinDistance, output.MaxDistance);
        source.AttenuationCurve = output.AttenuationCurve;
    }
    
    public AudioSource? PlayMusic(string clipName, float volume = 1.0f, bool loop = true)
    {
        if (!_clips.TryGetValue(clipName, out var clip))
        {
            Console.WriteLine($"[Echo] Audio clip not found: {clipName}");
            return null;
        }
        
        var source = new AudioSource
        {
            Clip = clip,
            Super = clip.SuperAsset,
            Volume = volume,
            Channel = AudioChannel.Music,
            Loop = loop,
            Is3D = false,
            IsPlaying = true
        };
        
        _sources.Add(source);
        Mixer.RegisterSource(source);
        
        source.CalculatedVolume = source.Volume * Mixer.GetFinalVolumeMultiplier(source.Channel);
        _backend.PlaySource(source);
        
        return source;
    }
    
    public void StopSound(AudioSource source)
    {
        source.IsPlaying = false;
        source.IsPaused = false;
        _pausedByGame.Remove(source);
        Mixer.UnregisterSource(source);
        _backend.StopSource(source);
        _sources.Remove(source);
    }
    
    public void StopAllSounds()
    {
        foreach (var source in _sources.ToArray())
        {
            StopSound(source);
        }
    }

    /// <summary>Pauses one sound, keeping its position.</summary>
    public void PauseSound(AudioSource source)
    {
        if (!source.IsPlaying || source.IsPaused)
            return;

        _backend.PauseSource(source);
        source.IsPaused = true;
    }

    /// <summary>Continues a paused sound from where it stopped.</summary>
    public void ResumeSound(AudioSource source)
    {
        if (!source.IsPaused)
            return;

        _backend.ResumeSource(source);
        source.IsPaused = false;
        _pausedByGame.Remove(source);
    }

    /// <summary>
    /// Pauses or resumes every game sound at once. Sounds with <see cref="AudioSource.IgnoresGamePause"/> set
    /// (editor previews, pause-menu music) keep playing. Resuming only resumes what this pause stopped, so a sound
    /// that was already paused on its own stays paused.
    /// </summary>
    public void SetGamePaused(bool paused)
    {
        if (paused == IsGamePaused)
            return;

        IsGamePaused = paused;
        if (paused)
        {
            foreach (var source in _sources)
            {
                if (source.IgnoresGamePause || !source.IsPlaying || source.IsPaused)
                    continue;

                PauseSound(source);
                _pausedByGame.Add(source);
            }
        }
        else
        {
            foreach (var source in _pausedByGame.ToArray())
                ResumeSound(source);

            _pausedByGame.Clear();
        }
    }

    /// <summary>
    /// Stops every game sound (anything without <see cref="AudioSource.IgnoresGamePause"/>), including sounds that
    /// scripts started. Used when the game session ends, such as leaving Play mode.
    /// </summary>
    public void StopGameSounds()
    {
        foreach (var source in _sources.ToArray())
        {
            if (!source.IgnoresGamePause)
                StopSound(source);
        }

        _pausedByGame.Clear();
        IsGamePaused = false;
    }
    
    public void SetListenerPosition(Vector3 position, Vector3 forward, Vector3 up, Vector3 velocity = default)
    {
        _listenerPosition = position;
        _listenerForward = forward;
        _listenerUp = up;
        _listenerVelocity = velocity;
        
        Spatial.UpdateListener(position, forward, up, velocity);
        _backend.SetListenerPosition(position, forward, up);
    }
    
    public void Update(float deltaTime)
    {
        Mixer.Update(deltaTime);
        
        foreach (var source in _sources.ToArray())
        {
            if (!source.IsPlaying)
            {
                StopSound(source);
                continue;
            }
            
            if (source.Is3D)
            {
                Spatial.ProcessSource(source, deltaTime);
            }
            else
            {
                source.CalculatedVolume = source.Volume;
                source.CalculatedPitch = source.Pitch;
            }
            
            // Apply mixer volume
            source.CalculatedVolume *= Mixer.GetFinalVolumeMultiplier(source.Channel);
            
            // Update backend
            _backend.UpdateSource(source);
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        StopAllSounds();
        _backend.Dispose();
        _disposed = true;
        
        Console.WriteLine("[Echo] Audio system shut down");
    }
}

/// <summary>
/// Audio source representing a playing sound
/// </summary>
public class AudioSource
{
    public AudioClip? Clip { get; set; }
    /// <summary>Optional live Super override used by the editor for auditioning.</summary>
    public EchoSuperAsset? Super { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
    public float Volume { get; set; } = 1.0f;
    public float CalculatedVolume { get; set; } = 1.0f;
    public float Pitch { get; set; } = 1.0f;
    public float CalculatedPitch { get; set; } = 1.0f;
    public float Pan { get; set; } = 0.0f;
    public AudioChannel Channel { get; set; } = AudioChannel.SFX;
    public bool Loop { get; set; }
    public bool Is3D { get; set; } = true;
    public bool IsPlaying { get; set; }
    /// <summary>Paused with its position kept. A paused sound still counts as playing.</summary>
    public bool IsPaused { get; internal set; }
    /// <summary>Keeps playing while the game is paused or stopped: editor previews, pause-menu music.</summary>
    public bool IgnoresGamePause { get; set; }
    public float MinDistance { get; set; } = 1.0f;
    public float MaxDistance { get; set; } = 100.0f;
    public DistanceAttenuationCurve AttenuationCurve { get; set; } = DistanceAttenuationCurve.Linear;
    public int BackendHandle { get; set; } = -1;
}

/// <summary>
/// Audio clip containing sound data
/// </summary>
public class AudioClip
{
    public string Name { get; set; } = "";
    /// <summary>Source file on disk. Backends may stream/decode from here.</summary>
    public string FilePath { get; set; } = "";
    /// <summary>Converted Echo Source payload, when this clip came from a .echosource.</summary>
    public EchoSourceAsset? SourceAsset { get; set; }
    /// <summary>Live effect preset, when this clip came from a .echosuper.</summary>
    public EchoSuperAsset? SuperAsset { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public float Duration { get; set; }
    public int BackendHandle { get; set; } = -1;
}
