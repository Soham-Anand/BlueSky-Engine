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
