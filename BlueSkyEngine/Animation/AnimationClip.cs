using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Animation;

/// <summary>
/// Animation clip containing keyframe data for skeletal animation.
/// Supports position, rotation, and scale tracks per bone.
/// </summary>
public class AnimationClip
{
    public string Name { get; set; } = string.Empty;
    public float Duration { get; set; } = 1.0f; // Duration in seconds
    public float FrameRate { get; set; } = 30.0f; // Frames per second
    public bool Looping { get; set; } = true;
    
    /// <summary>
    /// Animation tracks per bone (key = bone name)
    /// </summary>
    public Dictionary<string, BoneTrack> BoneTracks { get; set; } = new();
    
    /// <summary>
    /// Get total frame count
    /// </summary>
    public int FrameCount => (int)(Duration * FrameRate);
}

/// <summary>
/// Animation track for a single bone.
/// Contains keyframes for position, rotation, and scale.
/// </summary>
public class BoneTrack
{
    public string BoneName { get; set; } = string.Empty;
    
    public List<PositionKeyframe> PositionKeys { get; set; } = new();
    public List<RotationKeyframe> RotationKeys { get; set; } = new();
    public List<ScaleKeyframe> ScaleKeys { get; set; } = new();
    
    /// <summary>
    /// Sample the track at a specific time
    /// </summary>
    public (Vector3 position, Quaternion rotation, Vector3 scale) Sample(float time)
    {
        var position = SamplePosition(time);
        var rotation = SampleRotation(time);
        var scale = SampleScale(time);
        return (position, rotation, scale);
    }
    
    private Vector3 SamplePosition(float time)
    {
        if (PositionKeys.Count == 0) return Vector3.Zero;
        if (PositionKeys.Count == 1) return PositionKeys[0].Value;
        
        // Find surrounding keyframes
        int index = FindKeyframeIndex(PositionKeys, time);
        if (index < 0) return PositionKeys[0].Value;
        if (index >= PositionKeys.Count - 1) return PositionKeys[^1].Value;
        
        var key0 = PositionKeys[index];
        var key1 = PositionKeys[index + 1];
        
        // Linear interpolation
        float t = (time - key0.Time) / (key1.Time - key0.Time);
        return Vector3.Lerp(key0.Value, key1.Value, t);
    }
    
    private Quaternion SampleRotation(float time)
    {
        if (RotationKeys.Count == 0) return Quaternion.Identity;
        if (RotationKeys.Count == 1) return RotationKeys[0].Value;
        
        int index = FindKeyframeIndex(RotationKeys, time);
        if (index < 0) return RotationKeys[0].Value;
        if (index >= RotationKeys.Count - 1) return RotationKeys[^1].Value;
        
        var key0 = RotationKeys[index];
        var key1 = RotationKeys[index + 1];
        
        // Spherical linear interpolation (slerp)
        float t = (time - key0.Time) / (key1.Time - key0.Time);
        return Quaternion.Slerp(key0.Value, key1.Value, t);
    }
    
    private Vector3 SampleScale(float time)
    {
        if (ScaleKeys.Count == 0) return Vector3.One;
        if (ScaleKeys.Count == 1) return ScaleKeys[0].Value;
        
        int index = FindKeyframeIndex(ScaleKeys, time);
        if (index < 0) return ScaleKeys[0].Value;
        if (index >= ScaleKeys.Count - 1) return ScaleKeys[^1].Value;
        
        var key0 = ScaleKeys[index];
        var key1 = ScaleKeys[index + 1];
        
        float t = (time - key0.Time) / (key1.Time - key0.Time);
        return Vector3.Lerp(key0.Value, key1.Value, t);
    }
    
    private int FindKeyframeIndex<T>(List<T> keys, float time) where T : IKeyframe
    {
        // Binary search for efficiency
        int left = 0;
        int right = keys.Count - 1;
        
        while (left <= right)
        {
            int mid = (left + right) / 2;
            if (keys[mid].Time <= time)
                left = mid + 1;
            else
                right = mid - 1;
        }
        
        return right;
    }
}

/// <summary>
/// Base interface for keyframes
/// </summary>
public interface IKeyframe
{
    float Time { get; set; }
}

/// <summary>
/// Position keyframe
/// </summary>
public struct PositionKeyframe : IKeyframe
{
    public float Time { get; set; }
    public Vector3 Value { get; set; }
}

/// <summary>
/// Rotation keyframe (using quaternions for smooth interpolation)
/// </summary>
public struct RotationKeyframe : IKeyframe
{
    public float Time { get; set; }
    public Quaternion Value { get; set; }
}

/// <summary>
/// Scale keyframe
/// </summary>
public struct ScaleKeyframe : IKeyframe
{
    public float Time { get; set; }
    public Vector3 Value { get; set; }
}
