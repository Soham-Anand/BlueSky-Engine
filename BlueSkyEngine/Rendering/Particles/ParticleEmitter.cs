using System;
using System.Numerics;
using BlueSky.Core.ECS;

namespace BlueSky.Rendering.Particles;

public enum EmitterShape
{
    Point,
    Sphere,
    Cone,
    Box
}

/// <summary>
/// ECS component that configures a particle emitter
/// </summary>
public struct ParticleEmitterComponent
{
    public bool IsActive;
    
    // Emission config
    public float EmissionRate; // Particles per second
    public float EmitAccumulator; // Internal use
    
    // Lifetime config
    public float MinLifetime;
    public float MaxLifetime;
    
    // Velocity config
    public Vector3 MinStartVelocity;
    public Vector3 MaxStartVelocity;
    
    // Shape config
    public EmitterShape Shape;
    public float ShapeRadius; // Used for Sphere and Cone
    public float ShapeAngle;  // Used for Cone
    public Vector3 ShapeExtents; // Used for Box
    
    // Physics config
    public float GravityMultiplier;
    
    // Visual config
    public float StartSize;
    public float EndSize;
    public Vector4 StartColor;
    public Vector4 EndColor;
    
    // Texture/Material
    public int AtlasIndex;

    public ParticleEmitterComponent()
    {
        IsActive = true;
        EmissionRate = 10.0f;
        EmitAccumulator = 0.0f;
        MinLifetime = 1.0f;
        MaxLifetime = 2.0f;
        MinStartVelocity = new Vector3(-1, 1, -1);
        MaxStartVelocity = new Vector3(1, 3, 1);
        Shape = EmitterShape.Point;
        ShapeRadius = 1.0f;
        ShapeAngle = 30.0f;
        ShapeExtents = Vector3.One;
        GravityMultiplier = 1.0f;
        StartSize = 1.0f;
        EndSize = 0.0f;
        StartColor = Vector4.One;
        EndColor = new Vector4(1, 1, 1, 0);
        AtlasIndex = 0;
    }
}
