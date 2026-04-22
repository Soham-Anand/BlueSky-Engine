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
/// Physics system using Jolt Physics for high-performance simulation
/// </summary>
public class PhysicsSystem : SystemBase, IDisposable
{
    private PhysicsSystemSettings? _settings;
    private JoltPhysicsSharp.PhysicsSystem? _joltSystem;
    private BodyInterface? _bodyInterface;
    
    private readonly Dictionary<Entity, BodyID> _entityToBody = new();
    private readonly Dictionary<BodyID, Entity> _bodyToEntity = new();
    
    private bool _initialized;
    private bool _disposed;
    
    public Vector3 Gravity { get; set; } = new Vector3(0, -9.81f, 0);
    public bool IsInitialized => _initialized;
    
    public void Initialize()
    {
        if (_initialized) return;
        
        try
        {
            Foundation.Init();
            
            // Create physics system with reasonable defaults
            _settings = new PhysicsSystemSettings
            {
                MaxBodies = 10240,
                MaxBodyPairs = 65536,
                MaxContactConstraints = 10240
            };
            
            _joltSystem = new JoltPhysicsSharp.PhysicsSystem();
            _joltSystem.Init(_settings.Value);
            _joltSystem.Gravity = Gravity;
            
            _bodyInterface = _joltSystem.BodyInterface;
            _initialized = true;
            
            Console.WriteLine("[Physics] Jolt Physics initialized successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Physics] Failed to initialize: {ex.Message}");
            throw;
        }
    }
    
    public override void Update(float deltaTime)
    {
        if (!_initialized || _joltSystem == null) return;
        
        // Step physics simulation
        _joltSystem.Update(deltaTime, 1);
        
        // Sync physics transforms back to ECS
        SyncPhysicsToECS();
    }
    
    private void SyncPhysicsToECS()
    {
        if (World == null || _bodyInterface == null) return;
        
        foreach (var (entity, bodyId) in _entityToBody)
        {
            if (!World.IsEntityValid(entity)) continue;
            if (!World.HasComponent<TransformComponent>(entity)) continue;
            
            var transform = World.GetComponent<TransformComponent>(entity);
            var position = _bodyInterface.GetPosition(bodyId);
            var rotation = _bodyInterface.GetRotation(bodyId);
            
            transform.Position = new Core.Math.Vector3(position.X, position.Y, position.Z);
            transform.Rotation = new Core.Math.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
            
            World.AddComponent(entity, transform);
        }
    }
    
    public BodyID CreateRigidbody(Entity entity, RigidbodyComponent rigidbody, Vector3 position, Quaternion rotation)
    {
        if (!_initialized || _bodyInterface == null)
            throw new InvalidOperationException("Physics system not initialized");
        
        // Create shape based on rigidbody type
        Shape shape = rigidbody.ColliderType switch
        {
            ColliderType.Box => new BoxShape(rigidbody.Size * 0.5f),
            ColliderType.Sphere => new SphereShape(rigidbody.Radius),
            ColliderType.Capsule => new CapsuleShape(rigidbody.Height * 0.5f, rigidbody.Radius),
            _ => new BoxShape(new Vector3(0.5f))
        };
        
        var bodySettings = new BodyCreationSettings
        {
            Shape = shape,
            Position = position,
            Rotation = rotation,
            MotionType = rigidbody.IsKinematic ? MotionType.Kinematic :
                        rigidbody.Mass == 0 ? MotionType.Static : MotionType.Dynamic,
            ObjectLayer = rigidbody.IsKinematic || rigidbody.Mass == 0 ? 
                         ObjectLayers.NonMoving : ObjectLayers.Moving,
            AllowSleeping = true,
            Friction = rigidbody.Friction,
            Restitution = rigidbody.Restitution
        };
        
        var body = _bodyInterface.CreateBody(bodySettings);
        var bodyId = body.ID;
        
        // Set mass for dynamic bodies
        if (rigidbody.Mass > 0 && !rigidbody.IsKinematic)
        {
            _bodyInterface.SetMotionType(bodyId, MotionType.Dynamic, Activation.Activate);
        }
        
        _bodyInterface.AddBody(bodyId, Activation.Activate);
        
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
    
    public void AddForce(Entity entity, Vector3 force)
    {
        if (!_initialized || _bodyInterface == null) return;
        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            _bodyInterface.AddForce(bodyId, force);
        }
    }
    
    public void AddImpulse(Entity entity, Vector3 impulse)
    {
        if (!_initialized || _bodyInterface == null) return;
        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            _bodyInterface.AddImpulse(bodyId, impulse);
        }
    }
    
    public void SetVelocity(Entity entity, Vector3 velocity)
    {
        if (!_initialized || _bodyInterface == null) return;
        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            _bodyInterface.SetLinearVelocity(bodyId, velocity);
        }
    }
    
    public Vector3 GetVelocity(Entity entity)
    {
        if (!_initialized || _bodyInterface == null) return Vector3.Zero;
        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            return _bodyInterface.GetLinearVelocity(bodyId);
        }
        return Vector3.Zero;
    }
    
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit)
    {
        hit = default;
        if (!_initialized || _joltSystem == null) return false;
        
        var ray = new RRayCast
        {
            Origin = origin,
            Direction = direction * maxDistance
        };
        
        var collector = new CastRayClosestHitCollisionCollector();
        _joltSystem.NarrowPhaseQuery.CastRay(ray, collector);
        
        if (collector.HadHit())
        {
            var result = collector.Hit;
            hit = new RaycastHit
            {
                Point = result.ContactPoint,
                Normal = result.ContactNormal,
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
            foreach (var bodyId in _entityToBody.Values)
            {
                _bodyInterface.RemoveBody(bodyId);
                _bodyInterface.DestroyBody(bodyId);
            }
            
            _entityToBody.Clear();
            _bodyToEntity.Clear();
        }
        
        _joltSystem?.Dispose();
        Foundation.Shutdown();
        
        _disposed = true;
        Console.WriteLine("[Physics] Jolt Physics shut down");
    }
}

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

public enum ColliderType
{
    Box,
    Sphere,
    Capsule
}

public struct RigidbodyComponent
{
    public float Mass;
    public bool IsKinematic;
    public float Friction;
    public float Restitution;
    public ColliderType ColliderType;
    public Vector3 Size;      // For box
    public float Radius;      // For sphere/capsule
    public float Height;      // For capsule
    
    public RigidbodyComponent()
    {
        Mass = 1.0f;
        IsKinematic = false;
        Friction = 0.5f;
        Restitution = 0.3f;
        ColliderType = ColliderType.Box;
        Size = new Vector3(1, 1, 1);
        Radius = 0.5f;
        Height = 2.0f;
    }
}

#endif
