using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Airborne.Collision;

/// <summary>
/// Optimized collision detection system for game physics.
/// Uses broad phase (spatial hashing) + narrow phase (AABB/GJK) for efficiency.
/// </summary>
public class CollisionSystem
{
    private readonly SpatialHash _spatialHash;
    private readonly List<Collider> _colliders = new();
    private readonly List<CollisionPair> _collisionPairs = new();
    
    private const float CellSize = 10.0f; // Spatial hash cell size
    private const int MaxCollisionsPerFrame = 1000;
    
    public CollisionSystem()
    {
        _spatialHash = new SpatialHash(CellSize);
    }
    
    /// <summary>
    /// Register a collider with the system.
    /// </summary>
    public void AddCollider(Collider collider)
    {
        if (collider == null) return;
        if (collider.Id == Guid.Empty) collider.Id = Guid.NewGuid();
        
        if (_colliders.Contains(collider))
        {
            Console.WriteLine($"[CollisionSystem] Collider {collider.Id} already registered");
            return;
        }
        
        collider.UpdateBounds();
        _colliders.Add(collider);
        _spatialHash.Insert(collider);
    }
    
    /// <summary>
    /// Remove a collider from the system.
    /// </summary>
    public void RemoveCollider(Collider collider)
    {
        if (collider == null) return;
        
        if (!_colliders.Contains(collider))
        {
            Console.WriteLine($"[CollisionSystem] Collider {collider.Id} not found");
            return;
        }
        
        _colliders.Remove(collider);
        _spatialHash.Remove(collider);
    }
    
    /// <summary>
    /// Update collider positions (call after physics simulation).
    /// </summary>
    public void UpdateColliders()
    {
        _spatialHash.Clear();
        foreach (var collider in _colliders)
        {
            collider.UpdateBounds();
            _spatialHash.Insert(collider);
        }
    }
    
    /// <summary>
    /// Detect all collisions in the scene.
    /// </summary>
    public List<CollisionPair> DetectCollisions()
    {
        _collisionPairs.Clear();
        
        // Broad phase: spatial hash query
        foreach (var collider in _colliders)
        {
            if (!collider.IsEnabled) continue;
            
            var nearby = _spatialHash.Query(collider.Bounds);
            
            foreach (var other in nearby)
            {
                if (collider == other || !other.IsEnabled) continue;
                
                // Check collision channels
                if (!ShouldCollide(collider, other)) continue;
                
                // Avoid duplicate pairs
                if (collider.Id > other.Id) continue;
                
                // Narrow phase: detailed collision test
                if (TestCollision(collider, other, out var manifold))
                {
                    _collisionPairs.Add(new CollisionPair(collider, other, manifold));
                }
            }
        }
        
        return _collisionPairs;
    }
    
    /// <summary>
    /// Check if two colliders should collide based on their channels/masks.
    /// </summary>
    private bool ShouldCollide(Collider a, Collider b)
    {
        return (a.CollisionChannels & b.CollisionMask) != 0 &&
               (b.CollisionChannels & a.CollisionMask) != 0;
    }
    
    /// <summary>
    /// Narrow phase collision test between two colliders.
    /// </summary>
    private bool TestCollision(Collider a, Collider b, out CollisionManifold manifold)
    {
        manifold = default;
        
        // Quick AABB test first
        if (!AABB.Overlap(a.Bounds, b.Bounds))
            return false;
        
        // Detailed test based on collider types
        switch (a.ShapeType, b.ShapeType)
        {
            case (ShapeType.Box, ShapeType.Box):
                return BoxBoxCollision((BoxCollider)a, (BoxCollider)b, out manifold);
            case (ShapeType.Sphere, ShapeType.Sphere):
                return SphereSphereCollision((SphereCollider)a, (SphereCollider)b, out manifold);
            case (ShapeType.Box, ShapeType.Sphere):
                bool boxSphereHit = BoxSphereCollision((BoxCollider)a, (SphereCollider)b, out manifold);
                manifold.Normal = -manifold.Normal;
                return boxSphereHit;
            case (ShapeType.Sphere, ShapeType.Box):
                // BoxSphereCollision points from the box toward the sphere,
                // which is already the separation direction when the sphere is A.
                return BoxSphereCollision((BoxCollider)b, (SphereCollider)a, out manifold);
            case (ShapeType.Capsule, ShapeType.Capsule):
                return CapsuleCapsuleCollision((CapsuleCollider)a, (CapsuleCollider)b, out manifold);
            default:
                // Fallback to AABB
                manifold.Normal = Vector3.Normalize(a.Position - b.Position);
                manifold.Penetration = 0.01f;
                return true;
        }
    }
    
    // Collision detection algorithms
    private bool BoxBoxCollision(BoxCollider a, BoxCollider b, out CollisionManifold manifold)
    {
        // AABB test is sufficient for most cases
        if (!AABB.Overlap(a.Bounds, b.Bounds))
        {
            manifold = default;
            return false;
        }
        
        // Calculate penetration and normal
        var centerDiff = a.Position - b.Position;
        var overlap = a.Bounds.HalfExtents + b.Bounds.HalfExtents - Vector3.Abs(centerDiff);
        
        float minOverlap = Math.Min(overlap.X, Math.Min(overlap.Y, overlap.Z));
        
        manifold = new CollisionManifold
        {
            Normal = minOverlap == overlap.X ? SignedAxis(centerDiff.X, Vector3.UnitX) :
                     minOverlap == overlap.Y ? SignedAxis(centerDiff.Y, Vector3.UnitY) :
                     SignedAxis(centerDiff.Z, Vector3.UnitZ),
            Penetration = minOverlap
        };
        
        return true;
    }

    private static Vector3 SignedAxis(float value, Vector3 axis)
    {
        // Coincident centers are valid overlaps.  Returning a zero normal
        // would make impulse resolution silently do nothing.
        return value >= 0.0f ? axis : -axis;
    }
    
    private bool SphereSphereCollision(SphereCollider a, SphereCollider b, out CollisionManifold manifold)
    {
        var diff = a.Position - b.Position;
        float distance = diff.Length();
        float radiusSum = a.Radius + b.Radius;
        
        if (distance >= radiusSum)
        {
            manifold = default;
            return false;
        }
        
        manifold = new CollisionManifold
        {
            Normal = distance > 0.001f ? Vector3.Normalize(diff) : Vector3.UnitY,
            Penetration = radiusSum - distance
        };
        
        return true;
    }
    
    private bool BoxSphereCollision(BoxCollider box, SphereCollider sphere, out CollisionManifold manifold)
    {
        // Find closest point on box to sphere center
        var closest = Vector3.Clamp(sphere.Position, box.Bounds.Min, box.Bounds.Max);
        var diff = sphere.Position - closest;
        float distance = diff.Length();
        
        if (distance >= sphere.Radius)
        {
            manifold = default;
            return false;
        }
        
        manifold = new CollisionManifold
        {
            Normal = distance > 0.001f ? Vector3.Normalize(diff) : Vector3.UnitY,
            Penetration = sphere.Radius - distance
        };
        
        return true;
    }
    
    private bool CapsuleCapsuleCollision(CapsuleCollider a, CapsuleCollider b, out CollisionManifold manifold)
    {
        Vector3 axisA = a.SegmentAxis * MathF.Max(0.0f, a.Height * 0.5f);
        Vector3 axisB = b.SegmentAxis * MathF.Max(0.0f, b.Height * 0.5f);
        Vector3 p1 = a.Position - axisA, q1 = a.Position + axisA;
        Vector3 p2 = b.Position - axisB, q2 = b.Position + axisB;
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float aa = Vector3.Dot(d1, d1);
        float ee = Vector3.Dot(d2, d2);
        float ff = Vector3.Dot(d2, r);
        float s, t;

        if (aa <= 1e-8f && ee <= 1e-8f)
        {
            s = t = 0.0f;
        }
        else if (aa <= 1e-8f)
        {
            s = 0.0f;
            t = Math.Clamp(ff / ee, 0.0f, 1.0f);
        }
        else
        {
            float cc = Vector3.Dot(d1, r);
            if (ee <= 1e-8f)
            {
                t = 0.0f;
                s = Math.Clamp(-cc / aa, 0.0f, 1.0f);
            }
            else
            {
                float bb = Vector3.Dot(d1, d2);
                float denominator = aa * ee - bb * bb;
                s = denominator > 1e-8f
                    ? Math.Clamp((bb * ff - cc * ee) / denominator, 0.0f, 1.0f)
                    : 0.0f;
                t = (bb * s + ff) / ee;
                if (t < 0.0f)
                {
                    t = 0.0f;
                    s = Math.Clamp(-cc / aa, 0.0f, 1.0f);
                }
                else if (t > 1.0f)
                {
                    t = 1.0f;
                    s = Math.Clamp((bb - cc) / aa, 0.0f, 1.0f);
                }
            }
        }

        Vector3 pointA = p1 + d1 * s;
        Vector3 pointB = p2 + d2 * t;
        Vector3 separation = pointA - pointB;
        float distanceSquared = separation.LengthSquared();
        float radiusSum = MathF.Max(0.0f, a.Radius) + MathF.Max(0.0f, b.Radius);
        if (distanceSquared >= radiusSum * radiusSum)
        {
            manifold = default;
            return false;
        }

        float distance = MathF.Sqrt(distanceSquared);
        manifold = new CollisionManifold
        {
            Normal = distance > 1e-5f ? separation / distance : Vector3.UnitY,
            Penetration = radiusSum - distance
        };
        return true;
    }
}

/// <summary>
/// Spatial hash for broad phase collision detection.
/// </summary>
public class SpatialHash
{
    private readonly float _cellSize;
    private readonly Dictionary<(int, int, int), List<Collider>> _cells = new();
    
    public SpatialHash(float cellSize)
    {
        _cellSize = cellSize;
    }
    
    public void Insert(Collider collider)
    {
        var bounds = collider.Bounds;
        var minCell = WorldToCell(bounds.Min);
        var maxCell = WorldToCell(bounds.Max);
        
        for (int x = minCell.Item1; x <= maxCell.Item1; x++)
        {
            for (int y = minCell.Item2; y <= maxCell.Item2; y++)
            {
                for (int z = minCell.Item3; z <= maxCell.Item3; z++)
                {
                    var key = (x, y, z);
                    if (!_cells.ContainsKey(key))
                        _cells[key] = new List<Collider>();
                    _cells[key].Add(collider);
                }
            }
        }
    }
    
    public void Remove(Collider collider)
    {
        // Remove from all cells
        foreach (var cell in _cells.Values)
        {
            cell.Remove(collider);
        }
    }
    
    public void Clear()
    {
        _cells.Clear();
    }
    
    public List<Collider> Query(AABB bounds)
    {
        var seen = new HashSet<Guid>();
        var result = new List<Collider>();
        var minCell = WorldToCell(bounds.Min);
        var maxCell = WorldToCell(bounds.Max);
        
        for (int x = minCell.Item1; x <= maxCell.Item1; x++)
        {
            for (int y = minCell.Item2; y <= maxCell.Item2; y++)
            {
                for (int z = minCell.Item3; z <= maxCell.Item3; z++)
                {
                    var key = (x, y, z);
                    if (_cells.TryGetValue(key, out var cell))
                    {
                        foreach (var collider in cell)
                        {
                            if (seen.Add(collider.Id))
                                result.Add(collider);
                        }
                    }
                }
            }
        }
        
        return result;
    }
    
    private (int, int, int) WorldToCell(Vector3 position)
    {
        return (
            (int)Math.Floor(position.X / _cellSize),
            (int)Math.Floor(position.Y / _cellSize),
            (int)Math.Floor(position.Z / _cellSize)
        );
    }
}

/// <summary>
/// Collision pair data.
/// </summary>
public struct CollisionPair
{
    public Collider A;
    public Collider B;
    public CollisionManifold Manifold;
    
    public CollisionPair(Collider a, Collider b, CollisionManifold manifold)
    {
        A = a;
        B = b;
        Manifold = manifold;
    }
}

/// <summary>
/// Collision manifold data.
/// </summary>
public struct CollisionManifold
{
    public Vector3 Normal;
    public float Penetration;
}
