using System;
using System.Collections.Generic;
using System.Numerics;
using BlueSky.Core.ECS;

namespace BlueSky.Audio;

/// <summary>
/// Orchestra - BlueSky's audio system
/// Handles 3D spatial audio, music, and sound effects
/// </summary>
public class Orchestra : IDisposable
{
    private readonly IAudioBackend _backend;
    private readonly Dictionary<string, AudioClip> _clips = new();
    private readonly List<AudioSource> _sources = new();
    
    public AudioMixer Mixer { get; } = new();
    public SpatialAudio Spatial { get; } = new();
    
    private Vector3 _listenerPosition = Vector3.Zero;
    private Vector3 _listenerVelocity = Vector3.Zero;
    private Vector3 _listenerForward = new Vector3(0, 0, -1);
    private Vector3 _listenerUp = Vector3.UnitY;
    
    private bool _disposed;
    
    public Orchestra(IAudioBackend backend)
    {
        _backend = backend;
        _backend.Initialize();
        Console.WriteLine("[Orchestra] Audio system initialized");
    }
    
    public void LoadClip(string name, string filePath)
    {
        var clip = _backend.LoadAudioClip(filePath);
        if (clip != null)
        {
            clip.Name = name;
            _clips[name] = clip;
            Console.WriteLine($"[Orchestra] Loaded audio clip: {name}");
        }
    }
    
    public AudioSource? PlaySound(string clipName, Vector3 position, float volume = 1.0f, bool loop = false, AudioChannel channel = AudioChannel.SFX)
    {
        if (!_clips.TryGetValue(clipName, out var clip))
        {
            Console.WriteLine($"[Orchestra] Audio clip not found: {clipName}");
            return null;
        }
        
        var source = new AudioSource
        {
            Clip = clip,
            Position = position,
            Volume = volume,
            Channel = channel,
            Loop = loop,
            Is3D = true,
            IsPlaying = true
        };
        
        _sources.Add(source);
        Mixer.RegisterSource(source);
        
        // Initial process
        Spatial.ProcessSource(source, 0.0f);
        source.CalculatedVolume *= Mixer.GetFinalVolumeMultiplier(source.Channel);
        
        _backend.PlaySource(source);
        
        return source;
    }
    
    public AudioSource? PlayMusic(string clipName, float volume = 1.0f, bool loop = true)
    {
        if (!_clips.TryGetValue(clipName, out var clip))
        {
            Console.WriteLine($"[Orchestra] Audio clip not found: {clipName}");
            return null;
        }
        
        var source = new AudioSource
        {
            Clip = clip,
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
        
        Console.WriteLine("[Orchestra] Audio system shut down");
    }
}

/// <summary>
/// Audio source representing a playing sound
/// </summary>
public class AudioSource
{
    public AudioClip? Clip { get; set; }
    public Vector3 Position { get; set; }
    public float Volume { get; set; } = 1.0f;
    public float CalculatedVolume { get; set; } = 1.0f;
    public float Pitch { get; set; } = 1.0f;
    public float CalculatedPitch { get; set; } = 1.0f;
    public float Pan { get; set; } = 0.0f;
    public AudioChannel Channel { get; set; } = AudioChannel.SFX;
    public bool Loop { get; set; }
    public bool Is3D { get; set; } = true;
    public bool IsPlaying { get; set; }
    public float MinDistance { get; set; } = 1.0f;
    public float MaxDistance { get; set; } = 100.0f;
    public int BackendHandle { get; set; } = -1;
}

/// <summary>
/// Audio clip containing sound data
/// </summary>
public class AudioClip
{
    public string Name { get; set; } = "";
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public float Duration { get; set; }
    public int BackendHandle { get; set; } = -1;
}

/// <summary>
/// Component for entities that emit sound
/// </summary>
public struct AudioSourceComponent
{
    public string ClipName;
    public float Volume;
    public bool Loop;
    public bool PlayOnStart;
    public bool Is3D;
    
    public AudioSourceComponent()
    {
        ClipName = "";
        Volume = 1.0f;
        Loop = false;
        PlayOnStart = false;
        Is3D = true;
    }
}
