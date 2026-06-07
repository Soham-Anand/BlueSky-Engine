using System;
using System.Collections.Generic;
using System.Numerics;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Diagnostics;
using BlueSky.Physics.Collision;

namespace BlueSky.Physics;

/// <summary>
/// A lightweight, built-in fallback physics system that runs if Jolt is unavailable.
/// Uses the existing CollisionSystem for spatial hashing and narrow phase.
/// </summary>
public class BuiltinPhysicsWorld : IPhysicsWorld
{

    private class BuiltinBody
    {
        public Entity Entity;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
        public Vector3 AccumulatedForce; // Forces accumulated during the frame
        public float Mass;
        public float InverseMass;
        public float Drag;
        public float AngularDrag;
        public bool IsKinematic;
        public bool UseGravity;
        public Collider Collider = null!;
        public Vector3 ColliderCenter;
        public float Restitution;
        
        // Freezes
        public bool FreezePosX, FreezePosY, FreezePosZ;
        public bool FreezeRotX, FreezeRotY, FreezeRotZ;
    }

    private class TerrainBody
    {
        public Entity Entity;
        public TerrainHeightSampler Sampler = null!;
    }

    private readonly CollisionSystem _collisionSystem;
    private readonly Dictionary<Entity, BuiltinBody> _bodies = new();
    private readonly Dictionary<Entity, TerrainBody> _terrainBodies = new();
    private bool _initialized;
    
    public Vector3 Gravity { get; set; } = new Vector3(0, -9.81f, 0);
    public bool IsInitialized => _initialized;

    public BuiltinPhysicsWorld()
    {
        _collisionSystem = new CollisionSystem();
    }

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        ErrorHandler.LogInfo("Builtin Fallback Physics initialized", "Physics");
    }

    public void Step(float deltaTime)
    {
        if (!_initialized) return;

        // 1. Integrate velocities (Euler)
        foreach (var body in _bodies.Values)
        {
            if (body.IsKinematic) continue;

            // Apply gravity
            if (body.UseGravity && body.InverseMass > 0)
            {
                body.Velocity += Gravity * deltaTime;
            }

            // Apply accumulated forces (F = ma, so a = F * inverseMass)
            if (body.AccumulatedForce != Vector3.Zero && body.InverseMass > 0)
            {
                body.Velocity += body.AccumulatedForce * body.InverseMass * deltaTime;
                body.AccumulatedForce = Vector3.Zero; // Clear after applying
            }

            // Apply drag
            body.Velocity *= (1.0f - Math.Min(body.Drag * deltaTime, 1.0f));
            body.AngularVelocity *= (1.0f - Math.Min(body.AngularDrag * deltaTime, 1.0f));

            // Integrate position
            if (!body.FreezePosX) body.Position.X += body.Velocity.X * deltaTime;
            if (!body.FreezePosY) body.Position.Y += body.Velocity.Y * deltaTime;
            if (!body.FreezePosZ) body.Position.Z += body.Velocity.Z * deltaTime;

            // Simple angular integration (placeholder)
            // if (!body.FreezeRotX) ... etc

            // Update collider bounds
            SyncCollider(body);
        }

        // 2. Broad & Narrow phase collision
        _collisionSystem.UpdateColliders();
        var collisions = _collisionSystem.DetectCollisions();

        // 3. Resolve collisions (simple impulse)
        foreach (var pair in collisions)
        {
            var bodyA = GetBodyFromCollider(pair.A);
            var bodyB = GetBodyFromCollider(pair.B);

            if (bodyA == null || bodyB == null) continue;
            if (bodyA.IsKinematic && bodyB.IsKinematic) continue;

            // Positional correction (Projection method to prevent sinking)
            float totalInvMass = bodyA.InverseMass + bodyB.InverseMass;
            if (totalInvMass <= 0) continue;

            var correction = pair.Manifold.Normal * (pair.Manifold.Penetration / totalInvMass) * 0.8f; // 0.8 is correction percentage

            if (!bodyA.IsKinematic) bodyA.Position += correction * bodyA.InverseMass;
            if (!bodyB.IsKinematic) bodyB.Position -= correction * bodyB.InverseMass;

            // Velocity resolution
            var relVel = bodyA.Velocity - bodyB.Velocity;
            float velAlongNormal = Vector3.Dot(relVel, pair.Manifold.Normal);

            // Do not resolve if velocities are separating
            if (velAlongNormal > 0) continue;

            // Calculate restitution (bounciness)
            float restitution = 0.2f; // Could get from colliders later
            float j = -(1 + restitution) * velAlongNormal;
            j /= totalInvMass;

            var impulse = pair.Manifold.Normal * j;

            if (!bodyA.IsKinematic)
            {
                var newVel = bodyA.Velocity + impulse * bodyA.InverseMass;
                if (!bodyA.FreezePosX) bodyA.Velocity.X = newVel.X;
                if (!bodyA.FreezePosY) bodyA.Velocity.Y = newVel.Y;
                if (!bodyA.FreezePosZ) bodyA.Velocity.Z = newVel.Z;
            }

            if (!bodyB.IsKinematic)
            {
                var newVel = bodyB.Velocity - impulse * bodyB.InverseMass;
                if (!bodyB.FreezePosX) bodyB.Velocity.X = newVel.X;
                if (!bodyB.FreezePosY) bodyB.Velocity.Y = newVel.Y;
                if (!bodyB.FreezePosZ) bodyB.Velocity.Z = newVel.Z;
            }
        }

        ResolveTerrainCollisions();
    }

    public void AddBody(Entity entity, RigidbodyComponent rb, ColliderComponent col, Vector3 pos, Quaternion rot)
    {
        Collider collider = col.Type switch
        {
            ColliderType.Box => new BoxCollider(col.Size) { Position = pos, Rotation = rot },
            ColliderType.Sphere => new SphereCollider(col.Radius) { Position = pos, Rotation = rot },
            ColliderType.Capsule => new CapsuleCollider(col.Radius, col.Height) { Position = pos, Rotation = rot },
            _ => new BoxCollider(Vector3.One) { Position = pos, Rotation = rot }
        };

        var body = new BuiltinBody
        {
            Entity = entity,
            Position = pos,
            Rotation = rot,
            Mass = rb.Mass,
            InverseMass = rb.Mass > 0 ? 1f / rb.Mass : 0,
            Drag = rb.Drag,
            AngularDrag = rb.AngularDrag,
            IsKinematic = rb.IsKinematic,
            UseGravity = rb.UseGravity,
            FreezePosX = rb.FreezePositionX, FreezePosY = rb.FreezePositionY, FreezePosZ = rb.FreezePositionZ,
            FreezeRotX = rb.FreezeRotationX, FreezeRotY = rb.FreezeRotationY, FreezeRotZ = rb.FreezeRotationZ,
            ColliderCenter = col.Center,
            Restitution = MathF.Max(0.0f, MathF.Min(1.0f, col.Restitution)),
            Collider = collider
        };

        SyncCollider(body);
        _bodies[entity] = body;
        _collisionSystem.AddCollider(collider);
    }

    public void AddTerrain(Entity entity, TerrainHeightSampler sampler)
    {
        _terrainBodies[entity] = new TerrainBody
        {
            Entity = entity,
            Sampler = sampler
        };
    }

    /// <summary>
    /// Builtin fallback physics can't build a real triangle-mesh terrain
    /// collider, so we just store the height sampler for raycasts. The
    /// car body will fall through in this path, but it WON'T levitate
    /// (the old penetration-push is gone, see ResolveTerrainCollisions).
    /// </summary>
    public void AddTerrain(Entity entity, in PhysicsTerrainData terrain)
    {
        // No-op. The height-sampler overload is the only path that
        // contributes anything in Builtin physics.
    }

    public void RemoveTerrain(Entity entity)
    {
        _terrainBodies.Remove(entity);
    }

    public void RemoveBody(Entity entity)
    {
        if (_bodies.TryGetValue(entity, out var body))
        {
            _collisionSystem.RemoveCollider(body.Collider);
            _bodies.Remove(entity);
        }
    }

    public void SetPosition(Entity entity, Vector3 position)
    {
        if (_bodies.TryGetValue(entity, out var body))
        {
            body.Position = position;
            SyncCollider(body);
        }
    }

    public void SetRotation(Entity entity, Quaternion rotation)
    {
        if (_bodies.TryGetValue(entity, out var body))
        {
            body.Rotation = rotation;
            SyncCollider(body);
        }
    }

    public Vector3 GetPosition(Entity entity)
    {
        return _bodies.TryGetValue(entity, out var body) ? body.Position : Vector3.Zero;
    }

    public Vector3 GetRawBodyPosition(Entity entity)
    {
        return _bodies.TryGetValue(entity, out var body) ? body.Position : Vector3.Zero;
    }

    public Quaternion GetRotation(Entity entity)
    {
        return _bodies.TryGetValue(entity, out var body) ? body.Rotation : Quaternion.Identity;
    }

    public void SetVelocity(Entity entity, Vector3 velocity)
    {
        if (_bodies.TryGetValue(entity, out var body) && !body.IsKinematic)
        {
            body.Velocity = velocity;
        }
    }

    public Vector3 GetVelocity(Entity entity)
    {
        return _bodies.TryGetValue(entity, out var body) ? body.Velocity : Vector3.Zero;
    }

    public void AddForce(Entity entity, Vector3 force)
    {
        if (_bodies.TryGetValue(entity, out var body) && !body.IsKinematic && body.InverseMass > 0)
        {
            body.AccumulatedForce += force; // Accumulated and applied during Step() with deltaTime
        }
    }

    public void AddImpulse(Entity entity, Vector3 impulse)
    {
        if (_bodies.TryGetValue(entity, out var body) && !body.IsKinematic && body.InverseMass > 0)
        {
            body.Velocity += impulse * body.InverseMass;
        }
    }

    public void SetMass(Entity entity, float mass)
    {
        if (_bodies.TryGetValue(entity, out var body))
        {
            body.Mass = Math.Max(0.0f, mass);
            body.InverseMass = body.Mass > 0.0f && !body.IsKinematic ? 1.0f / body.Mass : 0.0f;
        }
    }

    public void SetUseGravity(Entity entity, bool useGravity)
    {
        if (_bodies.TryGetValue(entity, out var body))
        {
            body.UseGravity = useGravity;
        }
    }

    public void SetKinematic(Entity entity, bool isKinematic)
    {
        if (_bodies.TryGetValue(entity, out var body))
        {
            body.IsKinematic = isKinematic;
            body.InverseMass = body.Mass > 0.0f && !body.IsKinematic ? 1.0f / body.Mass : 0.0f;
        }
    }

    public bool HasBody(Entity entity) => _bodies.ContainsKey(entity);

    // Vehicle physics extensions (simplified implementations for builtin physics)
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit, Entity ignoreEntity = default)
    {
        hit = default;
        // Simplified raycast - in a real implementation we'd check against colliders
        // For now, we'll use terrain sampling as a basic ground check
        if (_terrainBodies.Count > 0)
        {
            foreach (var terrain in _terrainBodies.Values)
            {
                // Sample terrain along ray path
                for (int i = 1; i <= 10; i++)
                {
                    float t = i * 0.1f;
                    Vector3 samplePos = origin + direction * (maxDistance * t);
                    if (terrain.Sampler(samplePos, out float terrainHeight, out var terrainNormal))
                    {
                        if (samplePos.Y >= terrainHeight && samplePos.Y <= terrainHeight + 0.1f)
                        {
                            hit = new RaycastHit
                            {
                                Hit = true,
                                Distance = maxDistance * t,
                                Point = new Vector3(samplePos.X, terrainHeight, samplePos.Z),
                                Normal = terrainNormal,
                                Entity = terrain.Entity
                            };
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    public void AddForceAtPosition(Entity entity, Vector3 force, Vector3 worldPosition)
    {
        // Builtin physics doesn't distinguish force position - apply at center of mass
        AddForce(entity, force);
    }

    public void SetAngularVelocity(Entity entity, Vector3 angularVelocity)
    {
        if (_bodies.TryGetValue(entity, out var body) && !body.IsKinematic)
        {
            body.AngularVelocity = angularVelocity;
        }
    }

    public Vector3 GetAngularVelocity(Entity entity)
    {
        return _bodies.TryGetValue(entity, out var body) ? body.AngularVelocity : Vector3.Zero;
    }

    private void ResolveTerrainCollisions()
    {
        if (_terrainBodies.Count == 0) return;

        foreach (var body in _bodies.Values)
        {
            if (body.IsKinematic || body.InverseMass <= 0.0f || body.Collider == null)
                continue;

            SyncCollider(body);

            float bottom = body.Collider.Bounds.Min.Y;
            float bestPenetration = 0.0f;
            Vector3 bestNormal = Vector3.UnitY;

            foreach (var samplePoint in GetTerrainSamplePoints(body))
            {
                foreach (var terrain in _terrainBodies.Values)
                {
                    if (!terrain.Sampler(samplePoint, out float terrainHeight, out var terrainNormal))
                        continue;

                    float penetration = terrainHeight - bottom;
                    if (penetration > bestPenetration)
                    {
                        bestPenetration = penetration;
                        bestNormal = terrainNormal.LengthSquared() > 0.0001f
                            ? Vector3.Normalize(terrainNormal)
                            : Vector3.UnitY;
                    }
                }
            }

            if (bestPenetration <= 0.0f)
                continue;

            if (!body.FreezePosY)
                body.Position.Y += bestPenetration;

            float velocityIntoTerrain = Vector3.Dot(body.Velocity, bestNormal);
            if (velocityIntoTerrain < 0.0f)
            {
                body.Velocity -= (1.0f + body.Restitution) * velocityIntoTerrain * bestNormal;
                body.Velocity.X *= 1.0f - Math.Min(body.Drag * 0.05f, 0.5f);
                body.Velocity.Z *= 1.0f - Math.Min(body.Drag * 0.05f, 0.5f);
            }

            SyncCollider(body);
        }
    }

    private static IEnumerable<Vector3> GetTerrainSamplePoints(BuiltinBody body)
    {
        var bounds = body.Collider.Bounds;
        float y = bounds.Min.Y;
        yield return new Vector3(body.Position.X + body.ColliderCenter.X, y, body.Position.Z + body.ColliderCenter.Z);

        if (body.Collider is BoxCollider)
        {
            yield return new Vector3(bounds.Min.X, y, bounds.Min.Z);
            yield return new Vector3(bounds.Min.X, y, bounds.Max.Z);
            yield return new Vector3(bounds.Max.X, y, bounds.Min.Z);
            yield return new Vector3(bounds.Max.X, y, bounds.Max.Z);
        }
    }

    private static void SyncCollider(BuiltinBody body)
    {
        if (body.Collider == null) return;

        body.Collider.Position = body.Position + body.ColliderCenter;
        body.Collider.Rotation = body.Rotation;
        body.Collider.UpdateBounds();
    }

    private BuiltinBody? GetBodyFromCollider(Collider collider)
    {
        foreach (var body in _bodies.Values)
        {
            if (body.Collider == collider) return body;
        }
        return null;
    }

    public void Dispose()
    {
        _bodies.Clear();
        _terrainBodies.Clear();
    }
}
