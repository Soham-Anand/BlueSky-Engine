using System;
using System.Collections.Generic;
using System.Numerics;
using JoltPhysicsSharp;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Diagnostics;

namespace BlueSky.Physics;

/// <summary>
/// Physics world using Jolt Physics for high-performance simulation.
/// Supports 1000+ rigidbodies with minimal CPU overhead.
/// Rewritten for JoltPhysicsSharp v2.11.2 API.
/// </summary>
public class JoltPhysicsWorld : IPhysicsWorld
{

    private JoltPhysicsSharp.PhysicsSystem? _physicsSystem;
    private BodyInterface _bodyInterface;
    private JobSystemThreadPool? _jobSystem;
    private readonly Dictionary<Entity, BodyID> _entityToBody = new();
    private readonly Dictionary<BodyID, Entity> _bodyToEntity = new();
    private readonly Dictionary<Entity, TerrainBody> _terrainBodies = new();

    // Real terrain colliders built from PhysicsTerrainData. Each one
    // is a STATIC Jolt body with a HeightFieldShape so dynamic bodies
    // can physically land on the terrain. This replaces the old
    // post-step ResolveTerrainCollisions hack which guessed at the
    // collider half-height and caused bodies to levitate.
    private readonly Dictionary<Entity, BodyID> _terrainColliderBodies = new();
    private readonly Dictionary<Entity, PhysicsTerrainData> _terrainColliderData = new();
    private readonly Dictionary<Entity, TerrainHeightSampler> _terrainSamplers = new();
    // Per-body collider extents. Required for terrain penetration
    // resolution to compute the actual collider bottom on every step.
    private readonly Dictionary<BodyID, Vector3> _bodyColliderHalfExtents = new();
    private readonly Dictionary<BodyID, float> _bodyColliderHalfHeight = new();

    // Keep filter references alive for GC
    private ObjectLayerPairFilterTable? _pairFilter;
    private BroadPhaseLayerInterfaceTable? _bpLayerInterface;
    private ObjectVsBroadPhaseLayerFilterTable? _objVsBpFilter;

    private bool _initialized;
    private bool _disposed;

    public Vector3 Gravity { get; set; } = new Vector3(0, -9.81f, 0);
    public int MaxBodies { get; set; } = 10240;
    public int MaxBodyPairs { get; set; } = 65536;
    public int MaxContactConstraints { get; set; } = 10240;
    public bool IsInitialized => _initialized;

    private class TerrainBody
    {
        public Entity Entity;
        public TerrainHeightSampler Sampler = null!;
    }

    public JoltPhysicsWorld() { }

    public void Initialize()
    {
        if (_initialized) return;

        try
        {
            Foundation.Init();

            // ── Layer filters ──────────────────────────────────────────
            _pairFilter = new ObjectLayerPairFilterTable(2);
            _pairFilter.EnableCollision(new ObjectLayer(ObjectLayers.NonMoving), new ObjectLayer(ObjectLayers.Moving));
            _pairFilter.EnableCollision(new ObjectLayer(ObjectLayers.Moving),    new ObjectLayer(ObjectLayers.Moving));

            _bpLayerInterface = new BroadPhaseLayerInterfaceTable(2, 2);
            _bpLayerInterface.MapObjectToBroadPhaseLayer(new ObjectLayer(ObjectLayers.NonMoving), new BroadPhaseLayer(0));
            _bpLayerInterface.MapObjectToBroadPhaseLayer(new ObjectLayer(ObjectLayers.Moving),    new BroadPhaseLayer(1));

            _objVsBpFilter = new ObjectVsBroadPhaseLayerFilterTable(
                _bpLayerInterface, 2, _pairFilter, 2);

            // ── Physics system ─────────────────────────────────────────
            var settings = new PhysicsSystemSettings
            {
                MaxBodies = MaxBodies,
                MaxBodyPairs = MaxBodyPairs,
                MaxContactConstraints = MaxContactConstraints,
                NumBodyMutexes = 0,
                ObjectLayerPairFilter = _pairFilter,
                BroadPhaseLayerInterface = _bpLayerInterface,
                ObjectVsBroadPhaseLayerFilter = _objVsBpFilter
            };

            _physicsSystem = new JoltPhysicsSharp.PhysicsSystem(settings);
            _physicsSystem.Gravity = Gravity;

            _bodyInterface = _physicsSystem.BodyInterface;
            _jobSystem = new JobSystemThreadPool();

            _physicsSystem.OptimizeBroadPhase();

            _initialized = true;
            ErrorHandler.LogInfo("Jolt Physics initialized successfully", "JoltPhysicsWorld");
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError($"Failed to initialize Jolt Physics: {ex.Message}", ex, "JoltPhysicsWorld");
            throw;
        }
    }

    public void Step(float deltaTime)
    {
        if (!_initialized || _physicsSystem == null || _jobSystem == null) return;

        _physicsSystem.Update(deltaTime, 1, _jobSystem);
        ResolveTerrainPenetration();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  BODY MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════

    public void AddBody(Entity entity, RigidbodyComponent rb, ColliderComponent col, Vector3 pos, Quaternion rot)
    {
        if (!_initialized || _bodyInterface.IsNull)
            throw new InvalidOperationException("JoltPhysicsWorld not initialized");

        // Create shape
        Shape shape = col.Type switch
        {
            ColliderType.Box     => new BoxShape(col.Size * 0.5f, 0.05f),
            ColliderType.Sphere  => new SphereShape(col.Radius),
            ColliderType.Capsule => new CapsuleShape(col.Height * 0.5f, col.Radius),
            _                   => new BoxShape(new Vector3(0.5f), 0.05f)
        };

        var motionType = rb.IsKinematic ? MotionType.Kinematic :
                         rb.Mass == 0   ? MotionType.Static : MotionType.Dynamic;
        var objLayer   = (rb.IsKinematic || rb.Mass == 0)
            ? new ObjectLayer(ObjectLayers.NonMoving)
            : new ObjectLayer(ObjectLayers.Moving);

        using var bodySettings = new BodyCreationSettings(shape, pos, rot, motionType, objLayer);

        var bodyId = _bodyInterface.CreateAndAddBody(bodySettings,
            (rb.IsKinematic || rb.Mass == 0) ? Activation.DontActivate : Activation.Activate);

        // Material properties
        _bodyInterface.SetFriction(bodyId, col.Friction);
        _bodyInterface.SetRestitution(bodyId, col.Restitution);

        Vector3 halfExtents = ComputeColliderHalfExtents(col);
        _bodyColliderHalfExtents[bodyId] = halfExtents;
        _bodyColliderHalfHeight[bodyId] = halfExtents.Y;

        _entityToBody[entity] = bodyId;
        _bodyToEntity[bodyId] = entity;
    }

    private static float ComputeColliderHalfHeight(ColliderComponent col)
    {
        return col.Type switch
        {
            ColliderType.Box     => MathF.Max(0.01f, col.Size.Y * 0.5f),
            ColliderType.Sphere  => MathF.Max(0.01f, col.Radius),
            ColliderType.Capsule => MathF.Max(0.01f, col.Height * 0.5f + col.Radius),
            _                    => 0.5f
        };
    }

    public void RemoveBody(Entity entity)
    {
        if (!_initialized || _bodyInterface.IsNull) return;

        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            _bodyInterface.RemoveAndDestroyBody(bodyId);
            _entityToBody.Remove(entity);
            _bodyToEntity.Remove(bodyId);
            _bodyColliderHalfExtents.Remove(bodyId);
            _bodyColliderHalfHeight.Remove(bodyId);
        }
    }

    private static Vector3 ComputeColliderHalfExtents(ColliderComponent col)
    {
        return col.Type switch
        {
            ColliderType.Box     => Vector3.Max(col.Size * 0.5f, new Vector3(0.01f)),
            ColliderType.Sphere  => new Vector3(MathF.Max(0.01f, col.Radius)),
            ColliderType.Capsule => new Vector3(MathF.Max(0.01f, col.Radius), MathF.Max(0.01f, col.Height * 0.5f + col.Radius), MathF.Max(0.01f, col.Radius)),
            _                    => new Vector3(0.5f)
        };
    }

    public bool HasBody(Entity entity) => _entityToBody.ContainsKey(entity);

    // ═══════════════════════════════════════════════════════════════════
    //  TERRAIN
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Build a static box collider for the terrain. The car's box collider
    /// will physically land on this through Jolt's narrow phase. This is
    /// simpler than HeightFieldShape and avoids native crashes.
    /// </summary>
    public void AddTerrain(Entity entity, in PhysicsTerrainData terrain)
    {
        if (!_initialized || _bodyInterface.IsNull)
            return;
        if (terrain.Samples == null || terrain.Width < 2 || terrain.Height < 2)
            return;

        // Remove any previous body for this entity
        RemoveTerrain(entity);

        PhysicsTerrainData terrainCopy = terrain;
        TerrainHeightSampler sampler = (Vector3 worldPosition, out float height, out Vector3 normal) =>
            SampleTerrain(terrainCopy, worldPosition, out height, out normal);
        _terrainSamplers[entity] = sampler;
        _terrainBodies[entity] = new TerrainBody { Entity = entity, Sampler = sampler };

        // Create a simple box collider for the terrain surface.
        // The terrain mesh goes from (0,0,0) to (WorldWidth,0,WorldDepth)
        // in local space, offset by OriginOffset (entity world transform).
        // The box center must be at the midpoint of that rectangle.
        var halfExtents = new Vector3(
            terrain.WorldWidth * 0.5f,
            0.5f,  // 1 meter thick
            terrain.WorldDepth * 0.5f);

        var shape = new BoxShape(halfExtents, 0.05f);

        // Center the box over the terrain mesh
        var position = new Vector3(
            terrain.OriginOffset.X + halfExtents.X,
            terrain.OriginOffset.Y - 0.5f,
            terrain.OriginOffset.Z + halfExtents.Z);

        using var bodySettings = new BodyCreationSettings(
            shape,
            position,
            Quaternion.Identity,
            MotionType.Static,
            new ObjectLayer(ObjectLayers.NonMoving));

        var bodyId = _bodyInterface.CreateAndAddBody(bodySettings, Activation.DontActivate);
        _terrainColliderBodies[entity] = bodyId;
        _terrainColliderData[entity]   = terrain;
        _bodyToEntity[bodyId] = entity;

        ErrorHandler.LogInfo(
            $"Terrain collider and height sampler built for entity {entity.Id}: " +
            $"{terrain.WorldWidth}x{terrain.WorldDepth} world units",
            "JoltPhysicsWorld");
    }

    public void AddTerrain(Entity entity, TerrainHeightSampler sampler)
    {
        // Keep the sampler so wheel raycasts can hit procedural terrain.
        _terrainBodies[entity] = new TerrainBody { Entity = entity, Sampler = sampler };
        _terrainSamplers[entity] = sampler;
    }

    public void RemoveTerrain(Entity entity)
    {
        _terrainBodies.Remove(entity);
        _terrainSamplers.Remove(entity);
        if (_terrainColliderBodies.TryGetValue(entity, out var bodyId))
        {
            if (_initialized && !_bodyInterface.IsNull)
            {
                _bodyInterface.RemoveAndDestroyBody(bodyId);
            }
            _bodyToEntity.Remove(bodyId);
            _terrainColliderBodies.Remove(entity);
        }
        _terrainColliderData.Remove(entity);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  POSITION / ROTATION
    // ═══════════════════════════════════════════════════════════════════

    public void SetPosition(Entity entity, Vector3 position)
    {
        if (!_initialized || _bodyInterface.IsNull) return;
        if (_entityToBody.TryGetValue(entity, out var bodyId))
            _bodyInterface.SetPosition(bodyId, position, Activation.Activate);
    }

    public void SetRotation(Entity entity, Quaternion rotation)
    {
        if (!_initialized || _bodyInterface.IsNull) return;
        if (_entityToBody.TryGetValue(entity, out var bodyId))
            _bodyInterface.SetRotation(bodyId, rotation, Activation.Activate);
    }

    public Vector3 GetPosition(Entity entity)
    {
        if (!_initialized || _bodyInterface.IsNull) return Vector3.Zero;
        return _entityToBody.TryGetValue(entity, out var bodyId)
            ? _bodyInterface.GetPosition(bodyId)
            : Vector3.Zero;
    }

    public Quaternion GetRotation(Entity entity)
    {
        if (!_initialized || _bodyInterface.IsNull) return Quaternion.Identity;
        return _entityToBody.TryGetValue(entity, out var bodyId)
            ? _bodyInterface.GetRotation(bodyId)
            : Quaternion.Identity;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  VELOCITY / FORCE
    // ═══════════════════════════════════════════════════════════════════

    public void SetVelocity(Entity entity, Vector3 velocity)
    {
        if (!_initialized || _bodyInterface.IsNull) return;
        if (_entityToBody.TryGetValue(entity, out var bodyId))
            _bodyInterface.SetLinearVelocity(bodyId, velocity);
    }

    public Vector3 GetVelocity(Entity entity)
    {
        if (!_initialized || _bodyInterface.IsNull) return Vector3.Zero;
        return _entityToBody.TryGetValue(entity, out var bodyId)
            ? _bodyInterface.GetLinearVelocity(bodyId)
            : Vector3.Zero;
    }

    public void AddForce(Entity entity, Vector3 force)
    {
        if (!_initialized || _bodyInterface.IsNull) return;
        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            // CRITICAL: Wake up sleeping bodies before applying forces!
            // Without this, forces accumulate but the body remains asleep and doesn't move.
            _bodyInterface.ActivateBody(bodyId);
            _bodyInterface.AddForce(bodyId, force);
        }
    }

    public void AddImpulse(Entity entity, Vector3 impulse)
    {
        if (!_initialized || _bodyInterface.IsNull) return;
        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            // CRITICAL: Wake up sleeping bodies before applying impulses!
            _bodyInterface.ActivateBody(bodyId);
            _bodyInterface.AddImpulse(bodyId, impulse);
        }
    }

    public void SetMass(Entity entity, float mass)
    {
        // Jolt doesn't expose a direct SetMass after creation;
        // would need to recreate the body. Ignored for now.
    }

    public void AddForceAtPosition(Entity entity, Vector3 force, Vector3 worldPosition)
    {
        if (!_initialized || _bodyInterface.IsNull) return;
        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            // CRITICAL: Wake up sleeping bodies before applying forces!
            _bodyInterface.ActivateBody(bodyId);
            _bodyInterface.AddForce(bodyId, force, worldPosition);
        }
    }

    public void SetAngularVelocity(Entity entity, Vector3 angularVelocity)
    {
        if (!_initialized || _bodyInterface.IsNull) return;
        if (_entityToBody.TryGetValue(entity, out var bodyId))
            _bodyInterface.SetAngularVelocity(bodyId, angularVelocity);
    }

    public Vector3 GetAngularVelocity(Entity entity)
    {
        if (!_initialized || _bodyInterface.IsNull) return Vector3.Zero;
        return _entityToBody.TryGetValue(entity, out var bodyId)
            ? _bodyInterface.GetAngularVelocity(bodyId)
            : Vector3.Zero;
    }

    public void SetUseGravity(Entity entity, bool useGravity)
    {
        if (!_initialized || _bodyInterface.IsNull) return;
        if (_entityToBody.TryGetValue(entity, out var bodyId))
            _bodyInterface.SetGravityFactor(bodyId, useGravity ? 1.0f : 0.0f);
    }

    public void SetKinematic(Entity entity, bool isKinematic)
    {
        if (!_initialized || _bodyInterface.IsNull) return;
        if (_entityToBody.TryGetValue(entity, out var bodyId))
        {
            _bodyInterface.SetMotionType(bodyId,
                isKinematic ? MotionType.Kinematic : MotionType.Dynamic,
                Activation.Activate);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RAYCAST
    // ═══════════════════════════════════════════════════════════════════

    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit, Entity ignoreEntity = default)
    {
        hit = default;
        if (!_initialized || _physicsSystem == null) return false;
        if (direction.LengthSquared() < 0.000001f || maxDistance <= 0.0f) return false;

        direction = Vector3.Normalize(direction);
        bool hasTerrainHit = TryRaycastTerrain(origin, direction, maxDistance, out RaycastHit terrainHit);
        bool hasPhysicsHit = false;
        RaycastHit physicsHit = default;
        Vector3 currentOrigin = origin;
        float remainingDistance = maxDistance;
        const int MaxRaycastSteps = 5; // prevent infinite loops in edge cases

        for (int step = 0; step < MaxRaycastSteps; step++)
        {
            var ray = new Ray(currentOrigin, direction * remainingDistance);
            var result = RayCastResult.Default;

            if (_physicsSystem.NarrowPhaseQuery.CastRay(ray, out result, null, null, null))
            {
                float hitDist = result.Fraction * remainingDistance;
                Vector3 hitPoint = currentOrigin + direction * hitDist;
                Entity hitEntity = _bodyToEntity.TryGetValue(result.BodyID, out var entity) ? entity : default;
                bool hitTerrainProxy = IsTerrainColliderEntity(hitEntity) && _terrainSamplers.ContainsKey(hitEntity);

                if ((ignoreEntity != default && hitEntity == ignoreEntity) || hitTerrainProxy)
                {
                    // Ignore self-collision and flat terrain proxy hits. Terrain
                    // height is sampled manually below so suspension follows the
                    // real height field instead of the proxy box.
                    float stepSize = hitDist + 0.01f;
                    if (stepSize >= remainingDistance)
                    {
                        break;
                    }
                    remainingDistance -= stepSize;
                    currentOrigin = hitPoint + direction * 0.01f;
                    continue;
                }

                physicsHit = new RaycastHit
                {
                    Hit = true,
                    Distance = (hitPoint - origin).Length(), // absolute distance from the original origin
                    Point    = hitPoint,
                    Normal   = Vector3.UnitY,
                    Entity   = hitEntity
                };
                hasPhysicsHit = true;
                break;
            }
            else
            {
                break;
            }
        }

        if (hasTerrainHit && (!hasPhysicsHit || terrainHit.Distance <= physicsHit.Distance))
        {
            hit = terrainHit;
            return true;
        }

        if (hasPhysicsHit)
        {
            hit = physicsHit;
            return true;
        }

        return false;
    }

    private bool TryRaycastTerrain(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit)
    {
        hit = default;
        if (_terrainBodies.Count > 0)
        {
            float bestDist = float.MaxValue;
            RaycastHit bestHit = default;

            foreach (var terrain in _terrainBodies.Values)
            {
                // Sample terrain height at the ray's XZ position
                if (!terrain.Sampler(origin, out float terrainHeight, out var terrainNormal))
                    continue;

                float heightDiff = origin.Y - terrainHeight;

                // For downward-facing rays (suspension checks) compute parametric distance
                if (direction.Y < -0.01f && heightDiff >= 0)
                {
                    float rayDist = heightDiff / MathF.Abs(direction.Y);
                    if (rayDist <= maxDistance && rayDist < bestDist)
                    {
                        Vector3 hitPoint = origin + direction * rayDist;
                        bestDist = rayDist;
                        bestHit = new RaycastHit
                        {
                            Hit      = true,
                            Distance = rayDist,
                            Point    = new Vector3(hitPoint.X, terrainHeight, hitPoint.Z),
                            Normal   = terrainNormal.LengthSquared() > 0.0001f
                                        ? Vector3.Normalize(terrainNormal)
                                        : Vector3.UnitY,
                            Entity   = terrain.Entity
                        };
                    }
                }
                else if (direction.Y >= -0.01f)
                {
                    // General case – sample along the ray
                    const int steps = 10;
                    for (int s = 1; s <= steps; s++)
                    {
                        float t = maxDistance * s / steps;
                        Vector3 p = origin + direction * t;
                        if (terrain.Sampler(p, out float h, out var n) && p.Y <= h)
                        {
                            if (t < bestDist)
                            {
                                bestDist = t;
                                bestHit = new RaycastHit
                                {
                                    Hit      = true,
                                    Distance = t,
                                    Point    = new Vector3(p.X, h, p.Z),
                                    Normal   = n.LengthSquared() > 0.0001f
                                                ? Vector3.Normalize(n)
                                                : Vector3.UnitY,
                                    Entity   = terrain.Entity
                                };
                            }
                            break;
                        }
                    }
                }
            }

            if (bestHit.Hit)
            {
                hit = bestHit;
                return true;
            }
        }

        return false;
    }

    private bool IsTerrainColliderEntity(Entity entity)
    {
        if (entity == default)
            return false;
        return _terrainColliderBodies.ContainsKey(entity);
    }

    private static bool SampleTerrain(in PhysicsTerrainData terrain, Vector3 worldPosition, out float height, out Vector3 normal)
    {
        height = 0.0f;
        normal = Vector3.UnitY;

        if (terrain.Samples == null || terrain.Width < 2 || terrain.Height < 2 ||
            terrain.WorldWidth <= 0.0f || terrain.WorldDepth <= 0.0f)
            return false;

        float localX = worldPosition.X - terrain.OriginOffset.X;
        float localZ = worldPosition.Z - terrain.OriginOffset.Z;
        if (localX < 0.0f || localZ < 0.0f || localX > terrain.WorldWidth || localZ > terrain.WorldDepth)
            return false;

        float gx = localX / terrain.WorldWidth * (terrain.Width - 1);
        float gz = localZ / terrain.WorldDepth * (terrain.Height - 1);
        height = terrain.OriginOffset.Y + BilinearHeight(terrain, gx, gz);

        float stepX = MathF.Max(0.001f, terrain.WorldWidth / (terrain.Width - 1));
        float stepZ = MathF.Max(0.001f, terrain.WorldDepth / (terrain.Height - 1));
        float hL = BilinearHeight(terrain, MathF.Max(0.0f, gx - 1.0f), gz);
        float hR = BilinearHeight(terrain, MathF.Min(terrain.Width - 1, gx + 1.0f), gz);
        float hD = BilinearHeight(terrain, gx, MathF.Max(0.0f, gz - 1.0f));
        float hU = BilinearHeight(terrain, gx, MathF.Min(terrain.Height - 1, gz + 1.0f));
        Vector3 n = new(-(hR - hL) / (2.0f * stepX), 1.0f, -(hU - hD) / (2.0f * stepZ));
        normal = n.LengthSquared() > 0.0001f ? Vector3.Normalize(n) : Vector3.UnitY;
        return true;
    }

    private static float BilinearHeight(in PhysicsTerrainData terrain, float gx, float gz)
    {
        int x0 = Math.Clamp((int)MathF.Floor(gx), 0, terrain.Width - 1);
        int z0 = Math.Clamp((int)MathF.Floor(gz), 0, terrain.Height - 1);
        int x1 = Math.Min(x0 + 1, terrain.Width - 1);
        int z1 = Math.Min(z0 + 1, terrain.Height - 1);
        float tx = Math.Clamp(gx - x0, 0.0f, 1.0f);
        float tz = Math.Clamp(gz - z0, 0.0f, 1.0f);

        float h00 = terrain.Samples[z0 * terrain.Width + x0];
        float h10 = terrain.Samples[z0 * terrain.Width + x1];
        float h01 = terrain.Samples[z1 * terrain.Width + x0];
        float h11 = terrain.Samples[z1 * terrain.Width + x1];
        float h0 = h00 + (h10 - h00) * tx;
        float h1 = h01 + (h11 - h01) * tx;
        return h0 + (h1 - h0) * tz;
    }

    private void ResolveTerrainPenetration()
    {
        if (_terrainBodies.Count == 0 || _bodyInterface.IsNull)
            return;

        foreach (var kvp in _entityToBody)
        {
            BodyID bodyId = kvp.Value;
            if (!_bodyColliderHalfExtents.TryGetValue(bodyId, out Vector3 halfExtents))
                continue;

            Vector3 position = _bodyInterface.GetPosition(bodyId);
            float bottomY = position.Y - halfExtents.Y;
            float bestPenetration = 0.0f;
            Vector3 bestNormal = Vector3.UnitY;

            foreach (Vector3 samplePoint in GetBodyTerrainSamplePoints(position, halfExtents))
            {
                foreach (var terrain in _terrainBodies.Values)
                {
                    if (!terrain.Sampler(samplePoint, out float terrainHeight, out Vector3 terrainNormal))
                        continue;

                    float penetration = terrainHeight - bottomY;
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

            position.Y += bestPenetration + 0.002f;
            _bodyInterface.SetPosition(bodyId, position, Activation.Activate);

            Vector3 velocity = _bodyInterface.GetLinearVelocity(bodyId);
            float velocityIntoTerrain = Vector3.Dot(velocity, bestNormal);
            if (velocityIntoTerrain < 0.0f)
            {
                velocity -= velocityIntoTerrain * bestNormal;
                _bodyInterface.SetLinearVelocity(bodyId, velocity);
            }
        }
    }

    private static IEnumerable<Vector3> GetBodyTerrainSamplePoints(Vector3 position, Vector3 halfExtents)
    {
        float x = MathF.Max(0.1f, halfExtents.X * 0.85f);
        float z = MathF.Max(0.1f, halfExtents.Z * 0.85f);
        yield return position;
        yield return new Vector3(position.X - x, position.Y, position.Z - z);
        yield return new Vector3(position.X - x, position.Y, position.Z + z);
        yield return new Vector3(position.X + x, position.Y, position.Z - z);
        yield return new Vector3(position.X + x, position.Y, position.Z + z);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DISPOSE
    // ═══════════════════════════════════════════════════════════════════

    public void Dispose()
    {
        if (_disposed) return;

        if (_initialized && !_bodyInterface.IsNull)
        {
            foreach (var bodyId in _entityToBody.Values.ToList())
            {
                _bodyInterface.RemoveAndDestroyBody(bodyId);
            }
            _entityToBody.Clear();
            _bodyToEntity.Clear();
        }

        _bodyColliderHalfExtents.Clear();
        _bodyColliderHalfHeight.Clear();
        _terrainBodies.Clear();
        _terrainColliderBodies.Clear();
        _terrainColliderData.Clear();
        _terrainSamplers.Clear();
        _jobSystem?.Dispose();
        _physicsSystem?.Dispose();
        _physicsSystem = null;
        _objVsBpFilter?.Dispose();
        _bpLayerInterface?.Dispose();
        _pairFilter?.Dispose();

        try { Foundation.Shutdown(); } catch { /* May already be shut down */ }

        _initialized = false;
        _disposed = true;
        ErrorHandler.LogInfo("Jolt Physics shut down", "JoltPhysicsWorld");
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
