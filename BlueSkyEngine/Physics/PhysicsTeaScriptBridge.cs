using System;
using System.Numerics;
using BlueSky.Core.ECS;
using TeaScript.Runtime;

namespace BlueSky.Physics;

#if false

/// <summary>
/// Exposes physics functions to TeaScript
/// </summary>
public static class PhysicsTeaScriptBridge
{
    private static PhysicsWorld? _physicsWorld;
    private static Entity _currentEntity;

    public static void Initialize(PhysicsWorld physicsWorld)
    {
        _physicsWorld = physicsWorld;
    }

    public static void SetCurrentEntity(Entity entity)
    {
        _currentEntity = entity;
    }

    public static void RegisterFunctions(Interpreter interpreter)
    {
        // Force and impulse
        interpreter.RegisterNativeFunction("addForce", args =>
        {
            if (args.Count >= 3 && _physicsWorld != null)
            {
                var force = new Vector3(
                    Convert.ToSingle(args[0]),
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2])
                );
                _physicsWorld.AddForce(_currentEntity, force);
            }
            return null;
        });

        interpreter.RegisterNativeFunction("addImpulse", args =>
        {
            if (args.Count >= 3 && _physicsWorld != null)
            {
                var impulse = new Vector3(
                    Convert.ToSingle(args[0]),
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2])
                );
                _physicsWorld.AddImpulse(_currentEntity, impulse);
            }
            return null;
        });

        // Velocity
        interpreter.RegisterNativeFunction("setVelocity", args =>
        {
            if (args.Count >= 3 && _physicsWorld != null)
            {
                var velocity = new Vector3(
                    Convert.ToSingle(args[0]),
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2])
                );
                _physicsWorld.SetVelocity(_currentEntity, velocity);
            }
            return null;
        });

        interpreter.RegisterNativeFunction("getVelocity", args =>
        {
            if (_physicsWorld != null)
            {
                var velocity = _physicsWorld.GetVelocity(_currentEntity);
                return new object[] { velocity.X, velocity.Y, velocity.Z };
            }
            return new object[] { 0f, 0f, 0f };
        });

        interpreter.RegisterNativeFunction("getVelocityX", args =>
        {
            if (_physicsWorld != null)
            {
                var velocity = _physicsWorld.GetVelocity(_currentEntity);
                return velocity.X;
            }
            return 0f;
        });

        interpreter.RegisterNativeFunction("getVelocityY", args =>
        {
            if (_physicsWorld != null)
            {
                var velocity = _physicsWorld.GetVelocity(_currentEntity);
                return velocity.Y;
            }
            return 0f;
        });

        interpreter.RegisterNativeFunction("getVelocityZ", args =>
        {
            if (_physicsWorld != null)
            {
                var velocity = _physicsWorld.GetVelocity(_currentEntity);
                return velocity.Z;
            }
            return 0f;
        });

        // Raycast
        interpreter.RegisterNativeFunction("raycast", args =>
        {
            if (args.Count >= 7 && _physicsWorld != null)
            {
                var origin = new Vector3(
                    Convert.ToSingle(args[0]),
                    Convert.ToSingle(args[1]),
                    Convert.ToSingle(args[2])
                );
                var direction = new Vector3(
                    Convert.ToSingle(args[3]),
                    Convert.ToSingle(args[4]),
                    Convert.ToSingle(args[5])
                );
                var maxDistance = Convert.ToSingle(args[6]);

                if (_physicsWorld.Raycast(origin, direction, maxDistance, out var hit))
                {
                    return new object[]
                    {
                        true,
                        hit.Point.X, hit.Point.Y, hit.Point.Z,
                        hit.Normal.X, hit.Normal.Y, hit.Normal.Z,
                        hit.Distance
                    };
                }
            }
            return false;
        });
    }
}

#endif
