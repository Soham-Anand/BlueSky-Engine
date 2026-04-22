using System;
using BlueSky.Core.ECS;
using TeaScript.Runtime;

namespace BlueSky.Animation;

/// <summary>
/// Exposes animation functions to TeaScript
/// </summary>
public static class AnimationTeaScriptBridge
{
    private static AnimationSystem? _animationSystem;
    private static Entity _currentEntity;
    
    public static void Initialize(AnimationSystem animationSystem)
    {
        _animationSystem = animationSystem;
    }
    
    public static void SetCurrentEntity(Entity entity)
    {
        _currentEntity = entity;
    }
    
    public static void RegisterFunctions(Interpreter interpreter)
    {
        // Animation playback
        interpreter.RegisterNativeFunction("playAnimation", args =>
        {
            if (args.Count >= 1 && _animationSystem != null)
            {
                var controller = _animationSystem.GetController(_currentEntity);
                if (controller != null)
                {
                    var clipName = args[0]?.ToString() ?? "";
                    controller.Play(clipName);
                }
            }
            return null;
        });
        
        interpreter.RegisterNativeFunction("stopAnimation", args =>
        {
            if (_animationSystem != null)
            {
                var controller = _animationSystem.GetController(_currentEntity);
                controller?.Stop();
            }
            return null;
        });
        
        interpreter.RegisterNativeFunction("pauseAnimation", args =>
        {
            if (_animationSystem != null)
            {
                var controller = _animationSystem.GetController(_currentEntity);
                controller?.Pause();
            }
            return null;
        });
        
        interpreter.RegisterNativeFunction("resumeAnimation", args =>
        {
            if (_animationSystem != null)
            {
                var controller = _animationSystem.GetController(_currentEntity);
                controller?.Resume();
            }
            return null;
        });
        
        interpreter.RegisterNativeFunction("setAnimationSpeed", args =>
        {
            if (args.Count >= 1 && _animationSystem != null)
            {
                var controller = _animationSystem.GetController(_currentEntity);
                if (controller != null)
                {
                    controller.Speed = Convert.ToSingle(args[0]);
                }
            }
            return null;
        });
        
        interpreter.RegisterNativeFunction("setAnimationLoop", args =>
        {
            if (args.Count >= 1 && _animationSystem != null)
            {
                var controller = _animationSystem.GetController(_currentEntity);
                if (controller != null)
                {
                    controller.Loop = Convert.ToBoolean(args[0]);
                }
            }
            return null;
        });
        
        interpreter.RegisterNativeFunction("isAnimationPlaying", args =>
        {
            if (_animationSystem != null)
            {
                var controller = _animationSystem.GetController(_currentEntity);
                return controller?.IsPlaying ?? false;
            }
            return false;
        });
    }
}
