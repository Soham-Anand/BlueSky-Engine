using System;
using System.Numerics;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;

namespace BlueSky.Physics;

public delegate bool TerrainHeightSampler(Vector3 worldPosition, out float height, out Vector3 normal);
public struct RaycastHit
{
    public bool Hit;
    public float Distance;
    public Vector3 Point;
    public Vector3 Normal;
    public Entity Entity;
}

/// <summary>
/// Height-field terrain data for physics. The physics world will create a
/// real collider out of this (Jolt's HeightFieldShape) so dynamic bodies
/// actually land on the surface instead of falling through. Builtin
/// physics uses it only for raycasts (no collider is built).
/// </summary>
public struct PhysicsTerrainData
{
    /// <summary>Grid width (X axis) in samples.</summary>
    public int Width;
    /// <summary>Grid depth (Z axis) in samples.</summary>
    public int Height;
    /// <summary>World-space size along X.</summary>
    public float WorldWidth;
    /// <summary>World-space size along Z.</summary>
    public float WorldDepth;
    /// <summary>Row-major height samples (Width * Height floats).</summary>
    public float[] Samples;
    /// <summary>World-space offset added to the local terrain origin.</summary>
    public Vector3 OriginOffset;
}

public interface IPhysicsWorld : IDisposable
{
    void Initialize();
    void Step(float deltaTime);
    void AddBody(Entity entity, RigidbodyComponent rb, ColliderComponent col, Vector3 pos, Quaternion rot);
    /// <summary>
    /// Register a height-field terrain. Implementations that support it
    /// (Jolt) build a real collider from the samples so dynamic bodies
    /// can physically rest on the terrain. Implementations that don't
    /// (Builtin fallback) use the height sampler below for raycasts only.
    /// </summary>
    void AddTerrain(Entity entity, in PhysicsTerrainData terrain);
    /// <summary>Kept for the Builtin fallback: raycast-only height sampling.</summary>
    void AddTerrain(Entity entity, TerrainHeightSampler sampler);
    void RemoveTerrain(Entity entity);
    void RemoveBody(Entity entity);
    void SetPosition(Entity entity, Vector3 position);
    void SetRotation(Entity entity, Quaternion rotation);
    Vector3 GetPosition(Entity entity);
    Quaternion GetRotation(Entity entity);
    void SetVelocity(Entity entity, Vector3 velocity);
    Vector3 GetVelocity(Entity entity);
    void AddForce(Entity entity, Vector3 force);
    void AddImpulse(Entity entity, Vector3 impulse);
    void SetMass(Entity entity, float mass);
    void SetUseGravity(Entity entity, bool useGravity);
    void SetKinematic(Entity entity, bool isKinematic);
    bool HasBody(Entity entity);
    
    // Vehicle physics extensions
    bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit, Entity ignoreEntity = default);
    void AddForceAtPosition(Entity entity, Vector3 force, Vector3 worldPosition);
    void SetAngularVelocity(Entity entity, Vector3 angularVelocity);
    Vector3 GetAngularVelocity(Entity entity);
}
