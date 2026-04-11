using System;
using System.Collections.Generic;
using System.Numerics;
using JoltPhysicsSharp;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Diagnostics;

namespace BlueSky.Physics;

#if false

/// <summary>
/// Physics world using Jolt Physics for high-performance simulation.
/// Supports 1000+ rigidbodies with minimal CPU overhead.
/// </summary>
public class PhysicsWorld : IDisposable
{
    private PhysicsSystem? _physicsSystem;
    private BodyInterface? _bodyInterface;
    private readonly Dictionary<Entity, BodyID> _entityToBody = new();
    private readonly Dictionary<BodyID, Entity> _bodyToEntity = new();
    
    private bool _initialized;
    private bool _disposed;

    public Vector3 Gravity { get; set; } = new Vector3(0, -9.81f, 0);
    public int MaxBodies { get; set; } = 10240;
    public int MaxBodyPairs { get; set; } = 65536;
    public int MaxContactConstraints { get; set; } = 10240;

    public PhysicsWorld()
    {
    }

    public void Initialize()
    {
        if (_initialized) return;

        try
        {
            // Register default allocator
            Foundation.Init();

            // Create physics system
            var settings = new PhysicsSystemSettings
            {
                MaxBodies = (uint)MaxBodies,
                MaxBodyPairs = (uint)MaxBodyPairs,
                MaxContactConstraints = (uint)MaxContactConstraints,
                NumBodyMutexes = 0, // Auto-detect
                ObjectLayerPairFilter = new ObjectLayerPairFilterTable(2),
                BroadPhaseLayerInterface = new BroadPhaseLayerInterfaceTable(2, 2),
                ObjectVsBroadPhaseLayerFilter = new ObjectVsBroadPhaseLayerFilterTable(
                    new BroadPhaseLayerInterfaceTable(2, 2), 2, 
                    new ObjectLayerPairFilterTable(2), 2)
            };

            _physicsSystem = new PhysicsSystem();
            _physicsSystem.Init(settings);
            _physicsSystem.Gravity = new System.Numerics.Vector3(Gravity.X, Gravity.Y, Gravity.Z);

            _bodyInterface = _physicsSystem.BodyInterface;

            _initialized = true;
            ErrorHandler.LogInfo("Jolt Physics initialized successfully", "PhysicsWorld");
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError($"Failed to initialize Jolt Physics: {ex.Message}", "PhysicsWorld");
            throw;
        }
    }

    public void Step(float deltaTime)
    {
        if (!_initialized || _physicsSystem == null) return;

        // Update physics (1 step, 1/60 collision steps)
        _physicsSystem.Update(deltaTime, 1, 1);
    }

    public BodyID CreateRigidbody(Entity entity, RigidbodyComponent rigidbody, ColliderComponent collider, Vector3 position, Quaternion rotation)
    {
        if (!_initialized || _bodyInterface == null)
        {
            throw new InvalidOperationException("PhysicsWorld not initialized");
        }

        // Create shape based on collider type
        Shape? shape = collider.Type switch
        {
            ColliderType.Box => new BoxShape(new System.Numerics.Vector3(
                collider.Size.X * 0.5f, 
                collider.Size.Y * 0.5f, 
                collider.Size.Z * 0.5f)),
            ColliderType.Sphere => new SphereShape(collider.Radius),
            ColliderType.Capsule => new CapsuleShape(collider.Height * 0.5f, collider.Radius),
            _ => throw new NotSupportedException($"Collider type {collider.Type} not supported")
        };

        // Create body settings
        var bodySettings = new BodyCreationSettings
        {
            Shape = shape,
            Position = new System.Numerics.Vector3(position.X, position.Y, position.Z),
            Rotation = new System.Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W),
            MotionType = rigidbody.IsKinematic ? MotionType.Kinematic : 
                        rigidbody.Mass == 0 ? MotionType.Static : MotionType.Dynamic,
            ObjectLayer = rigidbody.IsKinematic || rigidbody.Mass == 0 ? 
                         ObjectLayers.NonMoving : ObjectLayers.Moving,
            AllowSleeping = true,
            Friction = collider.Friction,
            Restitution = collider.Restitution
        };

        // Create body
        var body = _bodyInterface.CreateBody(bodySettings);
        var bodyId = body.ID;

        // Set mass properties for dynamic bodies
        if (rigidbody.Mass > 0 && !rigidbody.IsKinematic)
        {
            var massProperties = new MassProperties();
            shape.GetMassProperties(ref massProperties);
            massProperties.ScaleToMass(rigidbody.Mass);
            _bodyInterface.SetMassProperties(bodyId, massProperties);
        }

        // Add to world
        _bodyInterface.AddBody(bodyId, rigidbody.IsKinematic || rigidbody.Mass == 0 ? 
                              Activation.DontActivate : Activation.Activate);

        // Track entity-body mapping
        _entityToBody[entity] = bodyId;
        _bodyToEntity[bodyId] = entity;

        return bodyId;
    }

    public void RemoveRigidbody(Entity entity)
    {
        if (!_initialized || _bodyInterface == null) return;

        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            _bodyInterface.RemoveBody(bodyId);
            _bodyInterface.DestroyBody(bodyId);
            _entityToBody.Remove(entity);
            _bodyToEntity.Remove(bodyId);
        }
    }

    public void SetPosition(Entity entity, Vector3 position)
    {
        if (!_initialized || _bodyInterface == null) return;

        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            _bodyInterface.SetPosition(bodyId, 
                new System.Numerics.Vector3(position.X, position.Y, position.Z), 
                Activation.Activate);
        }
    }

    public void SetRotation(Entity entity, Quaternion rotation)
    {
        if (!_initialized || _bodyInterface == null) return;

        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            _bodyInterface.SetRotation(bodyId, 
                new System.Numerics.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), 
                Activation.Activate);
        }
    }

    public Vector3 GetPosition(Entity entity)
    {
        if (!_initialized || _bodyInterface == null) return Vector3.Zero;

        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            var pos = _bodyInterface.GetPosition(bodyId);
            return new Vector3(pos.X, pos.Y, pos.Z);
        }

        return Vector3.Zero;
    }

    public Quaternion GetRotation(Entity entity)
    {
        if (!_initialized || _bodyInterface == null) return Quaternion.Identity;

        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            var rot = _bodyInterface.GetRotation(bodyId);
            return new Quaternion(rot.X, rot.Y, rot.Z, rot.W);
        }

        return Quaternion.Identity;
    }

    public void AddForce(Entity entity, Vector3 force)
    {
        if (!_initialized || _bodyInterface == null) return;

        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            _bodyInterface.AddForce(bodyId, new System.Numerics.Vector3(force.X, force.Y, force.Z));
        }
    }

    public void AddImpulse(Entity entity, Vector3 impulse)
    {
        if (!_initialized || _bodyInterface == null) return;

        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            _bodyInterface.AddImpulse(bodyId, new System.Numerics.Vector3(impulse.X, impulse.Y, impulse.Z));
        }
    }

    public void SetVelocity(Entity entity, Vector3 velocity)
    {
        if (!_initialized || _bodyInterface == null) return;

        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            _bodyInterface.SetLinearVelocity(bodyId, new System.Numerics.Vector3(velocity.X, velocity.Y, velocity.Z));
        }
    }

    public Vector3 GetVelocity(Entity entity)
    {
        if (!_initialized || _bodyInterface == null) return Vector3.Zero;

        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            var vel = _bodyInterface.GetLinearVelocity(bodyId);
            return new Vector3(vel.X, vel.Y, vel.Z);
        }

        return Vector3.Zero;
    }

    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit)
    {
        hit = default;
        
        if (!_initialized || _physicsSystem == null) return false;

        var ray = new RRayCast
        {
            Origin = new System.Numerics.Vector3(origin.X, origin.Y, origin.Z),
            Direction = new System.Numerics.Vector3(direction.X, direction.Y, direction.Z) * maxDistance
        };

        var collector = new CastRayClosestHitCollisionCollector();
        _physicsSystem.NarrowPhaseQuery.CastRay(ray, collector);

        if (collector.HadHit())
        {
            var result = collector.Hit;
            hit = new RaycastHit
            {
                Point = new Vector3(result.ContactPoint.X, result.ContactPoint.Y, result.ContactPoint.Z),
                Normal = new Vector3(result.ContactNormal.X, result.ContactNormal.Y, result.ContactNormal.Z),
                Distance = result.Fraction * maxDistance,
                Entity = _bodyToEntity.TryGetValue(result.BodyID, out var entity) ? entity : default
            };
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_initialized && _bodyInterface != null)
        {
            // Remove all bodies
            foreach (var bodyId in _entityToBody.Values.ToList())
            {
                _bodyInterface.RemoveBody(bodyId);
                _bodyInterface.DestroyBody(bodyId);
            }

            _entityToBody.Clear();
            _bodyToEntity.Clear();
        }

        _physicsSystem?.Dispose();
        Foundation.Shutdown();

        _disposed = true;
        ErrorHandler.LogInfo("Jolt Physics shut down", "PhysicsWorld");
    }
}

/// <summary>
/// Object layers for broad-phase collision filtering.
/// </summary>
public static class ObjectLayers
{
    public const byte NonMoving = 0;
    public const byte Moving = 1;
}

public struct RaycastHit
{
    public Vector3 Point;
    public Vector3 Normal;
    public float Distance;
    public Entity Entity;
}

#endif
