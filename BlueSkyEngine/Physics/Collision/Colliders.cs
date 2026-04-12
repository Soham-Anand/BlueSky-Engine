using System;
using System.Numerics;

namespace BlueSky.Physics.Collision;

/// <summary>
/// Base collider class.
/// </summary>
public abstract class Collider
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public abstract ShapeType ShapeType { get; }
    public AABB Bounds { get; protected set; }
    public bool IsEnabled { get; set; } = true;
    
    public CollisionChannels CollisionChannels { get; set; } = CollisionChannels.Default;
    public CollisionChannels CollisionMask { get; set; } = CollisionChannels.All;
    
    public abstract void UpdateBounds();
}

/// <summary>
/// Axis-aligned bounding box.
/// </summary>
public struct AABB
{
    public Vector3 Min;
    public Vector3 Max;
    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 HalfExtents => (Max - Min) * 0.5f;
    
    public AABB(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }
    
    public static bool Overlap(AABB a, AABB b)
    {
        return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
               a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
               a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
    }
    
    public static AABB Merge(AABB a, AABB b)
    {
        return new AABB(
            Vector3.Min(a.Min, b.Min),
            Vector3.Max(a.Max, b.Max)
        );
    }
}

/// <summary>
/// Box collider.
/// </summary>
public class BoxCollider : Collider
{
    public Vector3 Size { get; set; } = Vector3.One;
    public override ShapeType ShapeType => ShapeType.Box;
    
    public BoxCollider() { }
    
    public BoxCollider(Vector3 size)
    {
        Size = size;
    }
    
    public override void UpdateBounds()
    {
        var halfSize = Size * 0.5f;
        var min = Position - halfSize;
        var max = Position + halfSize;
        Bounds = new AABB(min, max);
    }
}

/// <summary>
/// Sphere collider.
/// </summary>
public class SphereCollider : Collider
{
    public float Radius { get; set; } = 0.5f;
    public override ShapeType ShapeType => ShapeType.Sphere;
    
    public SphereCollider() { }
    
    public SphereCollider(float radius)
    {
        Radius = radius;
    }
    
    public override void UpdateBounds()
    {
        var min = Position - new Vector3(Radius);
        var max = Position + new Vector3(Radius);
        Bounds = new AABB(min, max);
    }
}

/// <summary>
/// Capsule collider.
/// </summary>
public class CapsuleCollider : Collider
{
    public float Radius { get; set; } = 0.5f;
    public float Height { get; set; } = 1.0f;
    public override ShapeType ShapeType => ShapeType.Capsule;
    
    public CapsuleCollider() { }
    
    public CapsuleCollider(float radius, float height)
    {
        Radius = radius;
        Height = height;
    }
    
    public override void UpdateBounds()
    {
        var halfHeight = Height * 0.5f;
        var min = Position - new Vector3(Radius, halfHeight + Radius, Radius);
        var max = Position + new Vector3(Radius, halfHeight + Radius, Radius);
        Bounds = new AABB(min, max);
    }
}

/// <summary>
/// Convex hull collider (for arbitrary meshes).
/// </summary>
public class ConvexHullCollider : Collider
{
    public Vector3[] Vertices { get; set; } = Array.Empty<Vector3>();
    public override ShapeType ShapeType => ShapeType.ConvexHull;
    
    public ConvexHullCollider() { }
    
    public ConvexHullCollider(Vector3[] vertices)
    {
        Vertices = vertices;
    }
    
    public override void UpdateBounds()
    {
        if (Vertices.Length == 0)
        {
            Bounds = new AABB(Position, Position);
            return;
        }
        
        var min = Vertices[0];
        var max = Vertices[0];
        
        foreach (var v in Vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        
        Bounds = new AABB(min, max);
    }
}

/// <summary>
/// Collider shape types.
/// </summary>
public enum ShapeType
{
    Box,
    Sphere,
    Capsule,
    ConvexHull,
    Mesh,
    Terrain
}

/// <summary>
/// Collision channels for collision filtering.
/// </summary>
[Flags]
public enum CollisionChannels : uint
{
    None = 0,
    Default = 1 << 0,
    Static = 1 << 1,
    Dynamic = 1 << 2,
    Kinematic = 1 << 3,
    Trigger = 1 << 4,
    Character = 1 << 5,
    Projectile = 1 << 6,
    Vehicle = 1 << 7,
    Ragdoll = 1 << 8,
    All = uint.MaxValue
}
