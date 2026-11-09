using System.Numerics;

namespace BlueSky.Echo;

/// <summary>
/// Audio backend interface for platform-specific audio implementations
/// </summary>
public interface IAudioBackend : IDisposable
{
    void Initialize();
    AudioClip? LoadAudioClip(string filePath);
    void PlaySource(AudioSource source);
    void StopSource(AudioSource source);
    /// <summary>Halts playback but keeps the position, so <see cref="ResumeSource"/> continues from the same point.</summary>
    void PauseSource(AudioSource source);
    void ResumeSource(AudioSource source);
    void UpdateSource(AudioSource source);
    void SetListenerPosition(Vector3 position, Vector3 forward, Vector3 up);
}
