using System.Numerics;

namespace BlueSky.Audio;

/// <summary>
/// Audio backend interface for platform-specific audio implementations
/// </summary>
public interface IAudioBackend : IDisposable
{
    void Initialize();
    AudioClip? LoadAudioClip(string filePath);
    void PlaySource(AudioSource source);
    void StopSource(AudioSource source);
    void UpdateSource(AudioSource source);
    void SetListenerPosition(Vector3 position, Vector3 forward, Vector3 up);
}

/// <summary>
/// Stub audio backend for platforms without audio support
/// </summary>
public class StubAudioBackend : IAudioBackend
{
    public void Initialize()
    {
        Console.WriteLine("[Audio] Using stub audio backend (no sound)");
    }
    
    public AudioClip? LoadAudioClip(string filePath)
    {
        return new AudioClip { Name = System.IO.Path.GetFileNameWithoutExtension(filePath) };
    }
    
    public void PlaySource(AudioSource source) { }
    public void StopSource(AudioSource source) { }
    public void UpdateSource(AudioSource source) { }
    public void SetListenerPosition(Vector3 position, Vector3 forward, Vector3 up) { }
    public void Dispose() { }
}
