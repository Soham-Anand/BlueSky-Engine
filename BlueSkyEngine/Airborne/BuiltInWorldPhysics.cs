using System;
using System.Collections.Generic;
using System.Numerics;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Gameplay;

namespace BlueSky.Airborne;

/// <summary>
/// Modular physics facade used by the engine runtime.
///
/// The public physics contract is stable while the implementation can be
/// swapped between Jolt and the managed fallback.  The selected implementation
/// is exposed for diagnostics and backend-specific features through
/// <see cref="JoltPhysics"/> and <see cref="FallbackPhysics"/>.
/// </summary>
public class BuiltInWorldPhysics : IPhysicsWorld
{
    protected IPhysicsWorld Backend { get; }

    public JoltPhysicsWorld? JoltPhysics => Backend as JoltPhysicsWorld;
    public BuiltinPhysicsWorld? FallbackPhysics => Backend as BuiltinPhysicsWorld;
    public bool UsingJolt => JoltPhysics != null;
    public bool IsInitialized => Backend is JoltPhysicsWorld jolt ? jolt.IsInitialized :
                                 Backend is BuiltinPhysicsWorld builtin && builtin.IsInitialized;
    public Vector3 Gravity
    {
        get => Backend is JoltPhysicsWorld jolt ? jolt.Gravity :
               Backend is BuiltinPhysicsWorld builtin ? builtin.Gravity : new Vector3(0, -9.81f, 0);
        set
        {
            if (Backend is JoltPhysicsWorld jolt) jolt.Gravity = value;
            if (Backend is BuiltinPhysicsWorld builtin) builtin.Gravity = value;
        }
    }

    public BuiltInWorldPhysics(bool preferJolt = true)
    {
        if (preferJolt)
        {
            try
            {
                var jolt = new JoltPhysicsWorld();
                jolt.Initialize();
                Backend = jolt;
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Physics] Jolt unavailable; using managed fallback: {ex.Message}");
            }
        }

        var fallback = new BuiltinPhysicsWorld();
        fallback.Initialize();
        Backend = fallback;
    }

    protected BuiltInWorldPhysics(IPhysicsWorld backend)
    {
        Backend = backend ?? throw new ArgumentNullException(nameof(backend));
        if (!IsInitialized)
            Backend.Initialize();
    }

    public virtual void Initialize() => Backend.Initialize();
    public virtual void Step(float deltaTime) => Backend.Step(deltaTime);
    public virtual void AddBody(Entity entity, PhysicsComponent phys, Vector3 pos, Quaternion rot) => Backend.AddBody(entity, phys, pos, rot);
    public virtual void AddTerrain(Entity entity, in PhysicsTerrainData terrain) => Backend.AddTerrain(entity, in terrain);
    public virtual void AddTerrain(Entity entity, TerrainHeightSampler sampler) => Backend.AddTerrain(entity, sampler);
    public virtual void RemoveTerrain(Entity entity) => Backend.RemoveTerrain(entity);
    public virtual void RemoveBody(Entity entity) => Backend.RemoveBody(entity);
    public virtual void SetPosition(Entity entity, Vector3 position) => Backend.SetPosition(entity, position);
    public virtual void SetRotation(Entity entity, Quaternion rotation) => Backend.SetRotation(entity, rotation);
    public virtual Vector3 GetPosition(Entity entity) => Backend.GetPosition(entity);
    public virtual Quaternion GetRotation(Entity entity) => Backend.GetRotation(entity);
    public virtual void SetVelocity(Entity entity, Vector3 velocity) => Backend.SetVelocity(entity, velocity);
    public virtual Vector3 GetVelocity(Entity entity) => Backend.GetVelocity(entity);
    public virtual void AddForce(Entity entity, Vector3 force) => Backend.AddForce(entity, force);
    public virtual void AddImpulse(Entity entity, Vector3 impulse) => Backend.AddImpulse(entity, impulse);
    public virtual void SetMass(Entity entity, float mass) => Backend.SetMass(entity, mass);
    public virtual void SetUseGravity(Entity entity, bool useGravity) => Backend.SetUseGravity(entity, useGravity);
    public virtual void SetKinematic(Entity entity, bool isKinematic) => Backend.SetKinematic(entity, isKinematic);
    public virtual bool HasBody(Entity entity) => Backend.HasBody(entity);
    public virtual bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit, Entity ignoreEntity = default) => Backend.Raycast(origin, direction, maxDistance, out hit, ignoreEntity);
    public virtual void AddForceAtPosition(Entity entity, Vector3 force, Vector3 worldPosition) => Backend.AddForceAtPosition(entity, force, worldPosition);
    public virtual void SetAngularVelocity(Entity entity, Vector3 angularVelocity) => Backend.SetAngularVelocity(entity, angularVelocity);
    public virtual Vector3 GetAngularVelocity(Entity entity) => Backend.GetAngularVelocity(entity);
    public virtual IEnumerable<PhysicsDebugBody> GetDebugBodies() => Backend.GetDebugBodies();

    public virtual void Dispose() => Backend.Dispose();
}

/// <summary>
/// Vehicle-capable physics world. Vehicle-specific behavior is an extension
/// of the same modular world, rather than a second unrelated physics stack.
/// </summary>
public class VehicleWorldPhysics : BuiltInWorldPhysics
{
    public VehicleWorldPhysics(bool preferJolt = true) : base(preferJolt) { }

    public VehicleWorldPhysics(IPhysicsWorld backend) : base(backend) { }

    public VehiclePhysics CreateVehicle(WheelState[] wheelStates, float mass = 1500f,
                                         float motorForce = 9000f, float brakeForce = 14000f,
                                         float maxSteerAngle = 32f)
    {
        return new VehiclePhysics(this, wheelStates, mass, motorForce, brakeForce, maxSteerAngle);
    }
}
