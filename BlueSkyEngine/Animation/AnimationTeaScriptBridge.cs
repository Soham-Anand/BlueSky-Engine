using System;
using System.Numerics;
using BlueSky.Core.ECS;

namespace BlueSky.Animation;

/// <summary>
/// Bridge between TeaScript and NotBSAnimation system.
/// Exposes animation functions to scripts.
/// </summary>
public static class AnimationTeaScriptBridge
{
    private static AnimationSystem? _animationSystem;
    
    public static void Initialize(AnimationSystem animationSystem)
    {
        _animationSystem = animationSystem;
    }
    
    /// <summary>
    /// Play an animation clip on an entity
    /// Usage in TeaScript: PlayAnimation(entityId, "walk", 0.2)
    /// </summary>
    public static void PlayAnimation(uint entityId, string clipName, float blendTime = 0.2f)
    {
        if (_animationSystem == null)
        {
            Console.WriteLine("[NotBSAnimation] Animation system not initialized");
            return;
        }
        
        var entity = new Entity((int)entityId, 0); // Generation 0 for script access
        var controller = _animationSystem.GetController(entity);
        
        if (controller == null)
        {
            Console.WriteLine($"[NotBSAnimation] No controller found for entity {entityId}");
            return;
        }
        
        controller.Play(clipName, blendTime);
    }
    
    /// <summary>
    /// Stop animation on an entity
    /// </summary>
    public static void StopAnimation(uint entityId)
    {
        if (_animationSystem == null) return;
        
        var entity = new Entity((int)entityId, 0);
        var controller = _animationSystem.GetController(entity);
        controller?.Play("", 0); // Empty clip name stops animation
    }
    
    /// <summary>
    /// Check if an entity is currently playing an animation
    /// </summary>
    public static bool IsAnimationPlaying(uint entityId)
    {
        if (_animationSystem == null) return false;
        
        var entity = new Entity((int)entityId, 0);
        var controller = _animationSystem.GetController(entity);
        return controller?.CurrentState != null;
    }
    
    /// <summary>
    /// Get current animation time
    /// </summary>
    public static float GetAnimationTime(uint entityId)
    {
        if (_animationSystem == null) return 0;
        
        var entity = new Entity((int)entityId, 0);
        var controller = _animationSystem.GetController(entity);
        return controller?.CurrentState?.Time ?? 0;
    }
    
    /// <summary>
    /// Set animation playback speed
    /// </summary>
    public static void SetAnimationSpeed(uint entityId, float speed)
    {
        if (_animationSystem == null) return;
        
        var entity = new Entity((int)entityId, 0);
        var controller = _animationSystem.GetController(entity);
        
        if (controller?.CurrentState != null)
        {
            controller.CurrentState.Speed = speed;
        }
    }
}
