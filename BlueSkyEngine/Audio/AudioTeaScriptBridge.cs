using System;
using System.Numerics;
using BlueSky.Core.ECS;
using TeaScript.Runtime;

namespace BlueSky.Audio;

/// <summary>
/// Exposes audio functions to TeaScript
/// </summary>
public static class AudioTeaScriptBridge
{
    private static Orchestra? _orchestra;
    private static Entity _currentEntity;
    private static World? _world;
    
    public static void Initialize(Orchestra orchestra, World world)
    {
        _orchestra = orchestra;
        _world = world;
    }
    
    public static void SetCurrentEntity(Entity entity)
    {
        _currentEntity = entity;
    }
    
    public static void RegisterFunctions(Interpreter interpreter)
    {
        // Play sound at entity position
        interpreter.RegisterNativeFunction("playSound", args =>
        {
            if (args.Count >= 1 && _orchestra != null && _world != null)
            {
                var clipName = args[0]?.ToString() ?? "";
                var volume = args.Count >= 2 ? Convert.ToSingle(args[1]) : 1.0f;
                var loop = args.Count >= 3 && Convert.ToBoolean(args[2]);
                
                // Get entity position
                if (_world.HasComponent<Core.ECS.Builtin.TransformComponent>(_currentEntity))
                {
                    var transform = _world.GetComponent<Core.ECS.Builtin.TransformComponent>(_currentEntity);
                    var pos = new Vector3(transform.Position.X, transform.Position.Y, transform.Position.Z);
                    _orchestra.PlaySound(clipName, pos, volume, loop);
                }
            }
            return null;
        });
        
        // Play sound at specific position
        interpreter.RegisterNativeFunction("playSoundAt", args =>
        {
            if (args.Count >= 4 && _orchestra != null)
            {
                var clipName = args[0]?.ToString() ?? "";
                var x = Convert.ToSingle(args[1]);
                var y = Convert.ToSingle(args[2]);
                var z = Convert.ToSingle(args[3]);
                var volume = args.Count >= 5 ? Convert.ToSingle(args[4]) : 1.0f;
                var loop = args.Count >= 6 && Convert.ToBoolean(args[5]);
                
                _orchestra.PlaySound(clipName, new Vector3(x, y, z), volume, loop);
            }
            return null;
        });
        
        // Play music (2D, no position)
        interpreter.RegisterNativeFunction("playMusic", args =>
        {
            if (args.Count >= 1 && _orchestra != null)
            {
                var clipName = args[0]?.ToString() ?? "";
                var volume = args.Count >= 2 ? Convert.ToSingle(args[1]) : 1.0f;
                var loop = args.Count >= 3 ? Convert.ToBoolean(args[2]) : true;
                
                _orchestra.PlayMusic(clipName, volume, loop);
            }
            return null;
        });
        
        // Stop all sounds
        interpreter.RegisterNativeFunction("stopAllSounds", args =>
        {
            _orchestra?.StopAllSounds();
            return null;
        });
        
        // Volume control
        interpreter.RegisterNativeFunction("setMasterVolume", args =>
        {
            if (args.Count >= 1 && _orchestra != null)
            {
                _orchestra.MasterVolume = Convert.ToSingle(args[0]);
            }
            return null;
        });
        
        interpreter.RegisterNativeFunction("setMusicVolume", args =>
        {
            if (args.Count >= 1 && _orchestra != null)
            {
                _orchestra.MusicVolume = Convert.ToSingle(args[0]);
            }
            return null;
        });
        
        interpreter.RegisterNativeFunction("setSFXVolume", args =>
        {
            if (args.Count >= 1 && _orchestra != null)
            {
                _orchestra.SFXVolume = Convert.ToSingle(args[0]);
            }
            return null;
        });
    }
}
