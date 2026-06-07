using System;
using System.Collections.Generic;

namespace BlueSky.Audio;

public enum AudioChannel
{
    Master,
    Music,
    SFX,
    Ambience,
    UI
}

/// <summary>
/// Handles audio routing, volume control, ducking, and priority management.
/// </summary>
public class AudioMixer
{
    private readonly Dictionary<AudioChannel, float> _channelVolumes = new();
    private readonly Dictionary<AudioChannel, float> _duckingMultipliers = new();
    private readonly Dictionary<AudioChannel, bool> _channelMutes = new();
    
    // Priority system
    private const int MaxSimultaneousVoices = 32;
    private readonly List<AudioSource> _activeSources = new();

    public AudioMixer()
    {
        foreach (AudioChannel channel in Enum.GetValues(typeof(AudioChannel)))
        {
            _channelVolumes[channel] = 1.0f;
            _duckingMultipliers[channel] = 1.0f;
            _channelMutes[channel] = false;
        }
    }

    public void SetChannelVolume(AudioChannel channel, float volume)
    {
        _channelVolumes[channel] = Math.Clamp(volume, 0.0f, 1.0f);
    }

    public float GetChannelVolume(AudioChannel channel)
    {
        return _channelVolumes[channel];
    }

    public void SetChannelMute(AudioChannel channel, bool mute)
    {
        _channelMutes[channel] = mute;
    }

    /// <summary>
    /// Gets the final computed volume multiplier for a specific channel,
    /// considering the master volume and any active ducking.
    /// </summary>
    public float GetFinalVolumeMultiplier(AudioChannel channel)
    {
        if (_channelMutes[channel] || _channelMutes[AudioChannel.Master])
            return 0.0f;

        float volume = _channelVolumes[channel] * _duckingMultipliers[channel];
        if (channel != AudioChannel.Master)
        {
            volume *= _channelVolumes[AudioChannel.Master] * _duckingMultipliers[AudioChannel.Master];
        }
        
        return volume;
    }

    public void ApplyDucking(AudioChannel targetChannel, float ratio, float fadeTime)
    {
        // Simple immediate ducking for now.
        // A full implementation would use fadeTime to interpolate the multiplier in Update()
        _duckingMultipliers[targetChannel] = ratio;
    }

    public void RemoveDucking(AudioChannel targetChannel)
    {
        _duckingMultipliers[targetChannel] = 1.0f;
    }

    public void RegisterSource(AudioSource source)
    {
        if (!_activeSources.Contains(source))
        {
            _activeSources.Add(source);
            CullLowPrioritySources();
        }
    }

    public void UnregisterSource(AudioSource source)
    {
        _activeSources.Remove(source);
    }

    public void Update(float deltaTime)
    {
        // Here we would interpolate ducking values if we implemented smooth ducking
    }

    private void CullLowPrioritySources()
    {
        if (_activeSources.Count <= MaxSimultaneousVoices)
            return;

        // Sort by priority. Lower volume (or further distance, which affects calculated volume) = lower priority
        _activeSources.Sort((a, b) => a.CalculatedVolume.CompareTo(b.CalculatedVolume));

        // Stop the lowest priority sources
        while (_activeSources.Count > MaxSimultaneousVoices)
        {
            var lowest = _activeSources[0];
            lowest.IsPlaying = false; // This will cause Orchestra to stop and remove it
            _activeSources.RemoveAt(0);
        }
    }
}
