using System;
using System.Collections.Generic;
using BlueSky.Core.ECS;
using BlueSky.Core.Math;

namespace BlueSky.Animation;

/// <summary>
/// Animation system for skeletal and procedural animations
/// </summary>
public class AnimationSystem : SystemBase
{
    private readonly Dictionary<Entity, AnimationController> _controllers = new();
    
    public AnimationController CreateController(Entity entity)
    {
        if (_controllers.ContainsKey(entity))
            return _controllers[entity];
        
        var controller = new AnimationController(entity);
        _controllers[entity] = controller;
        return controller;
    }
    
    public AnimationController? GetController(Entity entity)
    {
        return _controllers.TryGetValue(entity, out var controller) ? controller : null;
    }
    
    public void RemoveController(Entity entity)
    {
        _controllers.Remove(entity);
    }
    
    public override void Update(float deltaTime)
    {
        foreach (var controller in _controllers.Values)
        {
            controller.Update(deltaTime);
        }
    }
}

/// <summary>
/// Controls animation playback for an entity
/// </summary>
public class AnimationController
{
    public Entity Owner { get; }
    public List<AnimationClip> Clips { get; } = new();
    public AnimationClip? CurrentClip { get; private set; }
    public float CurrentTime { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool Loop { get; set; } = true;
    public float Speed { get; set; } = 1.0f;
    
    private readonly Dictionary<string, AnimationClip> _clipsByName = new();
    
    public AnimationController(Entity owner)
    {
        Owner = owner;
    }
    
    public void AddClip(AnimationClip clip)
    {
        Clips.Add(clip);
        _clipsByName[clip.Name] = clip;
    }
    
    public void Play(string clipName)
    {
        if (_clipsByName.TryGetValue(clipName, out var clip))
        {
            CurrentClip = clip;
            CurrentTime = 0f;
            IsPlaying = true;
        }
    }
    
    public void Stop()
    {
        IsPlaying = false;
        CurrentTime = 0f;
    }
    
    public void Pause()
    {
        IsPlaying = false;
    }
    
    public void Resume()
    {
        IsPlaying = true;
    }
    
    public void Update(float deltaTime)
    {
        if (!IsPlaying || CurrentClip == null) return;
        
        CurrentTime += deltaTime * Speed;
        
        if (CurrentTime >= CurrentClip.Duration)
        {
            if (Loop)
            {
                CurrentTime = CurrentTime % CurrentClip.Duration;
            }
            else
            {
                CurrentTime = CurrentClip.Duration;
                IsPlaying = false;
            }
        }
        
        // Sample animation at current time
        CurrentClip.Sample(CurrentTime);
    }
}

/// <summary>
/// Animation clip containing keyframes
/// </summary>
public class AnimationClip
{
    public string Name { get; set; } = "Unnamed";
    public float Duration { get; set; }
    public List<AnimationTrack> Tracks { get; } = new();
    
    public void Sample(float time)
    {
        foreach (var track in Tracks)
        {
            track.Sample(time);
        }
    }
}

/// <summary>
/// Animation track for a specific property (position, rotation, scale)
/// </summary>
public class AnimationTrack
{
    public string TargetProperty { get; set; } = "";
    public List<Keyframe> Keyframes { get; } = new();
    
    public void Sample(float time)
    {
        if (Keyframes.Count == 0) return;
        
        // Find surrounding keyframes
        Keyframe? prev = null;
        Keyframe? next = null;
        
        for (int i = 0; i < Keyframes.Count; i++)
        {
            if (Keyframes[i].Time <= time)
                prev = Keyframes[i];
            if (Keyframes[i].Time >= time)
            {
                next = Keyframes[i];
                break;
            }
        }
        
        if (prev == null && next != null)
        {
            // Before first keyframe
            ApplyValue(next.Value);
        }
        else if (prev != null && next == null)
        {
            // After last keyframe
            ApplyValue(prev.Value);
        }
        else if (prev != null && next != null)
        {
            // Interpolate between keyframes
            float t = (time - prev.Value.Time) / (next.Value.Time - prev.Value.Time);
            var interpolated = Interpolate(prev.Value.Value, next.Value.Value, t);
            ApplyValue(interpolated);
        }
    }
    
    private object Interpolate(object a, object b, float t)
    {
        if (a is Vector3 v3a && b is Vector3 v3b)
            return Vector3.Lerp(v3a, v3b, t);
        if (a is Quaternion qa && b is Quaternion qb)
            return Quaternion.Slerp(qa, qb, t);
        if (a is float fa && b is float fb)
            return fa + (fb - fa) * t;
        
        return t < 0.5f ? a : b;
    }
    
    private void ApplyValue(object value)
    {
        // This would apply the value to the target property
        // Implementation depends on how you want to handle property binding
    }
}

/// <summary>
/// Keyframe in an animation track
/// </summary>
public struct Keyframe
{
    public float Time;
    public object Value;
    public InterpolationType Interpolation;
}

public enum InterpolationType
{
    Linear,
    Step,
    Cubic
}

/// <summary>
/// Component to mark entities as animated
/// </summary>
public struct AnimationComponent
{
    public bool IsPlaying;
    public string CurrentAnimation;
    public float PlaybackSpeed;
    
    public AnimationComponent()
    {
        IsPlaying = false;
        CurrentAnimation = "";
        PlaybackSpeed = 1.0f;
    }
}

/// <summary>
/// Skeleton for skeletal animation
/// </summary>
public class Skeleton
{
    public List<Bone> Bones { get; } = new();
    public Dictionary<string, int> BoneNameToIndex { get; } = new();
    
    public void AddBone(Bone bone)
    {
        BoneNameToIndex[bone.Name] = Bones.Count;
        Bones.Add(bone);
    }
    
    public Bone? GetBone(string name)
    {
        if (BoneNameToIndex.TryGetValue(name, out var index))
            return Bones[index];
        return null;
    }
}

public class Bone
{
    public string Name { get; set; } = "";
    public int ParentIndex { get; set; } = -1;
    public Matrix4x4 LocalTransform { get; set; } = Matrix4x4.Identity;
    public Matrix4x4 InverseBindPose { get; set; } = Matrix4x4.Identity;
}
