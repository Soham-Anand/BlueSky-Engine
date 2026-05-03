using System;
using System.Collections.Generic;
using System.Numerics;
using BlueSky.Core.ECS;

namespace BlueSky.Animation;

/// <summary>
/// NotBSAnimation - ECS system for skeletal and procedural animations.
/// Manages animation controllers and updates them each frame.
/// </summary>
public class AnimationSystem : SystemBase
{
    private readonly Dictionary<Entity, AnimationController> _controllers = new();
    
    /// <summary>
    /// Create an animation controller for an entity with a skeletal mesh
    /// </summary>
    public AnimationController CreateController(Entity entity, SkeletalMesh mesh)
    {
        if (_controllers.ContainsKey(entity))
            return _controllers[entity];
        
        var controller = new AnimationController(mesh);
        _controllers[entity] = controller;
        return controller;
    }
    
    /// <summary>
    /// Get the animation controller for an entity
    /// </summary>
    public AnimationController? GetController(Entity entity)
    {
        return _controllers.TryGetValue(entity, out var controller) ? controller : null;
    }
    
    /// <summary>
    /// Remove animation controller from an entity
    /// </summary>
    public void RemoveController(Entity entity)
    {
        _controllers.Remove(entity);
    }
    
    /// <summary>
    /// Update all animation controllers
    /// </summary>
    public override void Update(float deltaTime)
    {
        foreach (var controller in _controllers.Values)
        {
            controller.Update(deltaTime);
        }
    }
}

/// <summary>
/// Component to mark entities as animated.
/// Stores animation state for ECS queries.
/// </summary>
public struct AnimationComponent
{
    public bool IsPlaying;
    private unsafe fixed char _currentAnimation[64];
    public float PlaybackSpeed;
    
    public unsafe string CurrentAnimation
    {
        get
        {
            fixed (char* ptr = _currentAnimation)
            {
                return new string(ptr).TrimEnd('\0');
            }
        }
        set
        {
            value ??= string.Empty;
            int length = Math.Min(63, value.Length);
            for (int i = 0; i < length; i++)
            {
                _currentAnimation[i] = value[i];
            }
            _currentAnimation[length] = '\0';
        }
    }
    
    public AnimationComponent()
    {
        IsPlaying = false;
        PlaybackSpeed = 1.0f;
    }
}
