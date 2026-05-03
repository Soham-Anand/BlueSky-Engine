using System;
using System.Numerics;
using BlueSky.Core.ECS;

namespace BlueSky.Physics;

/// <summary>
/// Exposes physics functions to TeaScript.
/// This bridge connects the TeaScript runtime to the physics world.
/// </summary>
public static class PhysicsTeaScriptBridge
{
    private static BuiltinPhysicsWorld? _physicsWorld;

    public static void Initialize(BuiltinPhysicsWorld physicsWorld)
    {
        _physicsWorld = physicsWorld;
    }

    public static void Shutdown()
    {
        _physicsWorld = null;
    }

    // ═══════════════════════════════════════════════════════════════
    //  VELOCITY API
    // ═══════════════════════════════════════════════════════════════

    public static Vector3 GetVelocity(Entity entity)
    {
        if (_physicsWorld != null)
        {
            return _physicsWorld.GetVelocity(entity);
        }
        return Vector3.Zero;
    }

    public static void SetVelocity(Entity entity, Vector3 velocity)
    {
        _physicsWorld?.SetVelocity(entity, velocity);
    }

    // ═══════════════════════════════════════════════════════════════
    //  FORCE API
    // ═══════════════════════════════════════════════════════════════

    public static void AddForce(Entity entity, Vector3 force)
    {
        _physicsWorld?.AddForce(entity, force);
    }

    // ═══════════════════════════════════════════════════════════════
    //  POSITION/ROTATION API
    // ═══════════════════════════════════════════════════════════════

    public static Vector3 GetPosition(Entity entity)
    {
        if (_physicsWorld != null)
        {
            return _physicsWorld.GetPosition(entity);
        }
        return Vector3.Zero;
    }

    public static void SetPosition(Entity entity, Vector3 position)
    {
        _physicsWorld?.SetPosition(entity, position);
    }

    public static Quaternion GetRotation(Entity entity)
    {
        if (_physicsWorld != null)
        {
            return _physicsWorld.GetRotation(entity);
        }
        return Quaternion.Identity;
    }

    public static void SetRotation(Entity entity, Quaternion rotation)
    {
        _physicsWorld?.SetRotation(entity, rotation);
    }
}
