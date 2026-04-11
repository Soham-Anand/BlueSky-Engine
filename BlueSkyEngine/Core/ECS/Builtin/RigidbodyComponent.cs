using System.Numerics;

namespace BlueSky.Core.ECS.Builtin;

public struct RigidbodyComponent
{
    public float Mass;
    public float Drag;
    public float AngularDrag;
    public bool UseGravity;
    public bool IsKinematic;
    public bool FreezePositionX;
    public bool FreezePositionY;
    public bool FreezePositionZ;
    public bool FreezeRotationX;
    public bool FreezeRotationY;
    public bool FreezeRotationZ;

    public RigidbodyComponent()
    {
        Mass = 1.0f;
        Drag = 0.0f;
        AngularDrag = 0.05f;
        UseGravity = true;
        IsKinematic = false;
        FreezePositionX = false;
        FreezePositionY = false;
        FreezePositionZ = false;
        FreezeRotationX = false;
        FreezeRotationY = false;
        FreezeRotationZ = false;
    }
}

/// <summary>
/// Collider component for physics collision detection.
/// </summary>
public struct ColliderComponent
{
    public ColliderType Type;
    public Vector3 Center;
    public Vector3 Size;      // For box collider
    public float Radius;      // For sphere/capsule collider
    public float Height;      // For capsule collider
    public bool IsTrigger;
    public float Friction;
    public float Restitution; // Bounciness

    public ColliderComponent()
    {
        Type = ColliderType.Box;
        Center = Vector3.Zero;
        Size = Vector3.One;
        Radius = 0.5f;
        Height = 2.0f;
        IsTrigger = false;
        Friction = 0.5f;
        Restitution = 0.0f;
    }
}

public enum ColliderType
{
    Box,
    Sphere,
    Capsule,
    Mesh,
    Convex
}
