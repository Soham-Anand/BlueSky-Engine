using System;
using System.Collections.Generic;
using System.Numerics;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Diagnostics;
using BlueSky.Airborne.Collision;

namespace BlueSky.Airborne;

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
        if (!float.IsFinite(deltaTime) || deltaTime <= 0.0f) return;
        deltaTime = MathF.Min(deltaTime, 0.1f);

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

            // Frozen rotation axes suppress angular velocity around that world axis.
            if (body.FreezeRotX) body.AngularVelocity.X = 0;
            if (body.FreezeRotY) body.AngularVelocity.Y = 0;
            if (body.FreezeRotZ) body.AngularVelocity.Z = 0;

            // Integrate position
            if (!body.FreezePosX) body.Position.X += body.Velocity.X * deltaTime;
            if (!body.FreezePosY) body.Position.Y += body.Velocity.Y * deltaTime;
            if (!body.FreezePosZ) body.Position.Z += body.Velocity.Z * deltaTime;

            // AngularVelocity is stored in world-space radians per second.
            float angularSpeed = body.AngularVelocity.Length();
            if (angularSpeed > 1e-6f)
            {
                Quaternion deltaRotation = Quaternion.CreateFromAxisAngle(
                    body.AngularVelocity / angularSpeed,
                    angularSpeed * deltaTime);
                body.Rotation = Quaternion.Normalize(deltaRotation * body.Rotation);
            }

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

    public void AddBody(Entity entity, PhysicsComponent phys, Vector3 pos, Quaternion rot)
    {
        // Replacing a body must also unregister its old collider.  Otherwise
        // the collision system keeps resolving contacts against a stale body.
        RemoveBody(entity);

        phys.Mass = MathF.Max(0.0f, phys.Mass);
        phys.Drag = MathF.Max(0.0f, phys.Drag);
        phys.AngularDrag = MathF.Max(0.0f, phys.AngularDrag);
        phys.Size = Vector3.Max(Vector3.Abs(phys.Size), new Vector3(0.01f));
        phys.Radius = MathF.Max(0.01f, phys.Radius);
        phys.Height = MathF.Max(0.01f, phys.Height);

        Collider collider = phys.Type switch
        {
            ColliderType.Box => new BoxCollider(phys.Size) { Position = pos, Rotation = rot },
            ColliderType.Sphere => new SphereCollider(phys.Radius) { Position = pos, Rotation = rot },
            ColliderType.Capsule => new CapsuleCollider(phys.Radius, phys.Height) { Position = pos, Rotation = rot },
            _ => new BoxCollider(Vector3.One) { Position = pos, Rotation = rot }
        };

        var body = new BuiltinBody
        {
            Entity = entity,
            Position = pos,
            Rotation = rot,
            Mass = phys.Mass,
            InverseMass = phys.Mass > 0 ? 1f / phys.Mass : 0,
            Drag = phys.Drag,
            AngularDrag = phys.AngularDrag,
            IsKinematic = phys.IsKinematic,
            UseGravity = phys.UseGravity,
            FreezePosX = phys.FreezePositionX, FreezePosY = phys.FreezePositionY, FreezePosZ = phys.FreezePositionZ,
            FreezeRotX = phys.FreezeRotationX, FreezeRotY = phys.FreezeRotationY, FreezeRotZ = phys.FreezeRotationZ,
            ColliderCenter = phys.Center,
            Restitution = MathF.Max(0.0f, MathF.Min(1.0f, phys.Restitution)),
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
    /// Builtin fallback physics cannot build a triangle-mesh collider, so it
    /// stores a sampled height field for deterministic raycasts and terrain
    /// penetration correction.
    /// </summary>
    public void AddTerrain(Entity entity, in PhysicsTerrainData terrain)
    {
        if (terrain.Samples == null || terrain.Width < 2 || terrain.Height < 2 ||
            terrain.WorldWidth <= 0.0f || terrain.WorldDepth <= 0.0f ||
            terrain.Samples.Length < terrain.Width * terrain.Height)
            return;

        // Keep the built-in backend behaviorally compatible with the Jolt
        // backend: terrain data must support both wheel raycasts and body
        // penetration correction. Capture a value copy so callers may reuse
        // their temporary terrain struct safely.
        PhysicsTerrainData copy = terrain;
        AddTerrain(entity, (Vector3 worldPosition, out float height, out Vector3 normal) =>
            SampleTerrain(copy, worldPosition, out height, out normal));
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
        if (!float.IsFinite(maxDistance) || maxDistance <= 0.0f || direction.LengthSquared() < 1e-8f)
            return false;

        direction = Vector3.Normalize(direction);
        float bestDistance = float.MaxValue;

        // First test registered rigidbody colliders.  This is intentionally a
        // conservative AABB raycast, but it is deterministic and supports the
        // vehicle solver even when no terrain exists.
        foreach (var body in _bodies.Values)
        {
            if (body.Entity == ignoreEntity || !body.Collider.IsEnabled)
                continue;

            if (RaycastAabb(origin, direction, maxDistance, body.Collider.Bounds, out float distance) &&
                distance < bestDistance)
            {
                bestDistance = distance;
                hit = new RaycastHit
                {
                    Hit = true,
                    Distance = distance,
                    Point = origin + direction * distance,
                    Normal = EstimateAabbNormal(origin + direction * distance, body.Collider.Bounds),
                    Entity = body.Entity
                };
            }
        }

        foreach (var terrain in _terrainBodies.Values)
        {
            if (TryRaycastTerrain(terrain, origin, direction, maxDistance, out var terrainHit) &&
                terrainHit.Distance < bestDistance)
            {
                bestDistance = terrainHit.Distance;
                hit = terrainHit;
            }
        }

        return hit.Hit;
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

    private static bool TryRaycastTerrain(TerrainBody terrain, Vector3 origin, Vector3 direction,
                                           float maxDistance, out RaycastHit hit)
    {
        hit = default;
        // March in small intervals and refine the first crossing.  This is a
        // fallback backend, so the important contract is stable wheel contact,
        // not a GPU-quality terrain intersection routine.
        const int steps = 64;
        float previousT = 0.0f;
        Vector3 previous = origin;
        if (!terrain.Sampler(previous, out float previousHeight, out _))
            previousHeight = float.NaN;

        for (int i = 1; i <= steps; i++)
        {
            float t = maxDistance * i / steps;
            Vector3 point = origin + direction * t;
            if (!terrain.Sampler(point, out float height, out Vector3 normal))
            {
                previousT = t;
                previous = point;
                previousHeight = float.NaN;
                continue;
            }

            float currentSigned = point.Y - height;
            float previousSigned = float.IsNaN(previousHeight) ? currentSigned : previous.Y - previousHeight;
            if (currentSigned <= 0.0f || previousSigned > 0.0f && currentSigned <= 0.0f)
            {
                float lo = previousT;
                float hi = t;
                for (int refinement = 0; refinement < 8; refinement++)
                {
                    float mid = (lo + hi) * 0.5f;
                    Vector3 midPoint = origin + direction * mid;
                    if (!terrain.Sampler(midPoint, out float midHeight, out _))
                    {
                        lo = mid;
                        continue;
                    }
                    if (midPoint.Y - midHeight > 0.0f) lo = mid;
                    else hi = mid;
                }

                float distance = hi;
                Vector3 hitPoint = origin + direction * distance;
                terrain.Sampler(hitPoint, out float hitHeight, out Vector3 hitNormal);
                hit = new RaycastHit
                {
                    Hit = true,
                    Distance = distance,
                    Point = new Vector3(hitPoint.X, hitHeight, hitPoint.Z),
                    Normal = hitNormal.LengthSquared() > 1e-8f ? Vector3.Normalize(hitNormal) : Vector3.UnitY,
                    Entity = terrain.Entity
                };
                return true;
            }

            previousT = t;
            previous = point;
            previousHeight = height;
        }

        return false;
    }

    private static bool RaycastAabb(Vector3 origin, Vector3 direction, float maxDistance, AABB bounds, out float distance)
    {
        float tMin = 0.0f;
        float tMax = maxDistance;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = origin[axis];
            float d = direction[axis];
            float min = bounds.Min[axis];
            float max = bounds.Max[axis];
            if (MathF.Abs(d) < 1e-8f)
            {
                if (o < min || o > max) { distance = 0.0f; return false; }
                continue;
            }
            float inv = 1.0f / d;
            float t1 = (min - o) * inv;
            float t2 = (max - o) * inv;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
            if (tMin > tMax) { distance = 0.0f; return false; }
        }
        distance = tMin;
        return distance <= maxDistance;
    }

    private static Vector3 EstimateAabbNormal(Vector3 point, AABB bounds)
    {
        Vector3 distances = new(
            MathF.Min(MathF.Abs(point.X - bounds.Min.X), MathF.Abs(point.X - bounds.Max.X)),
            MathF.Min(MathF.Abs(point.Y - bounds.Min.Y), MathF.Abs(point.Y - bounds.Max.Y)),
            MathF.Min(MathF.Abs(point.Z - bounds.Min.Z), MathF.Abs(point.Z - bounds.Max.Z)));
        if (distances.X <= distances.Y && distances.X <= distances.Z)
            return new Vector3(point.X - bounds.Center.X >= 0 ? 1 : -1, 0, 0);
        if (distances.Y <= distances.Z)
            return new Vector3(0, point.Y - bounds.Center.Y >= 0 ? 1 : -1, 0);
        return new Vector3(0, 0, point.Z - bounds.Center.Z >= 0 ? 1 : -1);
    }

    private static bool SampleTerrain(PhysicsTerrainData terrain, Vector3 worldPosition,
                                      out float height, out Vector3 normal)
    {
        height = 0.0f;
        normal = Vector3.UnitY;
        if (terrain.Samples == null || terrain.Samples.Length < terrain.Width * terrain.Height ||
            terrain.Width < 2 || terrain.Height < 2 || terrain.WorldWidth <= 0.0f || terrain.WorldDepth <= 0.0f)
            return false;
        float localX = worldPosition.X - terrain.OriginOffset.X;
        float localZ = worldPosition.Z - terrain.OriginOffset.Z;
        if (localX < 0 || localZ < 0 || localX > terrain.WorldWidth || localZ > terrain.WorldDepth)
            return false;
        float gx = localX / terrain.WorldWidth * (terrain.Width - 1);
        float gz = localZ / terrain.WorldDepth * (terrain.Height - 1);
        float HeightAt(float x, float z)
        {
            int x0 = Math.Clamp((int)MathF.Floor(x), 0, terrain.Width - 1);
            int z0 = Math.Clamp((int)MathF.Floor(z), 0, terrain.Height - 1);
            int x1 = Math.Min(x0 + 1, terrain.Width - 1);
            int z1 = Math.Min(z0 + 1, terrain.Height - 1);
            float tx = Math.Clamp(x - x0, 0, 1);
            float tz = Math.Clamp(z - z0, 0, 1);
            float h0 = terrain.Samples[z0 * terrain.Width + x0] +
                       (terrain.Samples[z0 * terrain.Width + x1] - terrain.Samples[z0 * terrain.Width + x0]) * tx;
            float h1 = terrain.Samples[z1 * terrain.Width + x0] +
                       (terrain.Samples[z1 * terrain.Width + x1] - terrain.Samples[z1 * terrain.Width + x0]) * tx;
            return h0 + (h1 - h0) * tz;
        }
        height = terrain.OriginOffset.Y + HeightAt(gx, gz);
        float dx = terrain.WorldWidth / (terrain.Width - 1);
        float dz = terrain.WorldDepth / (terrain.Height - 1);
        float left = HeightAt(MathF.Max(0, gx - 1), gz);
        float right = HeightAt(MathF.Min(terrain.Width - 1, gx + 1), gz);
        float down = HeightAt(gx, MathF.Max(0, gz - 1));
        float up = HeightAt(gx, MathF.Min(terrain.Height - 1, gz + 1));
        normal = Vector3.Normalize(new Vector3(-(right - left) / (2 * dx), 1, -(up - down) / (2 * dz)));
        return true;
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

    public IEnumerable<PhysicsDebugBody> GetDebugBodies()
    {
        foreach (var kvp in _bodies)
        {
            var body = kvp.Value;
            Vector3 colliderSize = Vector3.One;
            if (body.Collider is BoxCollider box)
                colliderSize = box.Size;
            else if (body.Collider is SphereCollider sphere)
                colliderSize = new Vector3(sphere.Radius * 2, sphere.Radius * 2, sphere.Radius * 2);

            yield return new PhysicsDebugBody
            {
                Entity = body.Entity,
                Position = body.Position,
                Rotation = body.Rotation,
                Velocity = body.Velocity,
                ColliderCenter = body.ColliderCenter,
                ColliderSize = colliderSize,
            };
        }
    }

    public void Dispose()
    {
        foreach (var body in _bodies.Values)
            _collisionSystem.RemoveCollider(body.Collider);
        _bodies.Clear();
        _terrainBodies.Clear();
        _initialized = false;
    }
}
