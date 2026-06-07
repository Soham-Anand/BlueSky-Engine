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
    private static IPhysicsWorld? _physicsWorld;

    public static IPhysicsWorld? PhysicsWorld => _physicsWorld;

    public static void Initialize(IPhysicsWorld physicsWorld)
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

    public static void AddImpulse(Entity entity, Vector3 impulse)
    {
        _physicsWorld?.AddImpulse(entity, impulse);
    }

    public static void SetMass(Entity entity, float mass)
    {
        _physicsWorld?.SetMass(entity, mass);
    }

    public static void SetUseGravity(Entity entity, bool useGravity)
    {
        _physicsWorld?.SetUseGravity(entity, useGravity);
    }

    public static void SetKinematic(Entity entity, bool isKinematic)
    {
        _physicsWorld?.SetKinematic(entity, isKinematic);
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

    // ═══════════════════════════════════════════════════════════════
    //  VEHICLE PHYSICS API
    // ═══════════════════════════════════════════════════════════════

    public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit)
    {
        if (_physicsWorld != null)
        {
            return _physicsWorld.Raycast(origin, direction, maxDistance, out hit);
        }
        hit = default;
        return false;
    }

    public static void AddForceAtPosition(Entity entity, Vector3 force, Vector3 worldPosition)
    {
        _physicsWorld?.AddForceAtPosition(entity, force, worldPosition);
    }

    public static void SetAngularVelocity(Entity entity, Vector3 angularVelocity)
    {
        _physicsWorld?.SetAngularVelocity(entity, angularVelocity);
    }

    public static Vector3 GetAngularVelocity(Entity entity)
    {
        if (_physicsWorld != null)
        {
            return _physicsWorld.GetAngularVelocity(entity);
        }
        return Vector3.Zero;
    }
}
