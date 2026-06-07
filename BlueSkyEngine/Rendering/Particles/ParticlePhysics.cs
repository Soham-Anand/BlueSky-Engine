using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace BlueSky.Rendering.Particles;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ParticleData
{
    public Vector3 Position;
    public float Size;
    
    public Vector3 Velocity;
    public float Rotation;
    
    public Vector4 Color;
    
    public float Life;
    public float MaxLife;
    public Vector2 _Padding; // Align to 64 bytes
    
    // Position (12) + Size (4) = 16
    // Velocity (12) + Rotation (4) = 16
    // Color (16) = 16
    // Life (4) + MaxLife (4) + Padding (8) = 16
    // Total = 64 bytes
}

/// <summary>
/// Handles CPU-based particle simulation as a fallback for devices without compute shaders
/// </summary>
public class ParticlePhysics
{
    public static void SimulateCPU(Span<ParticleData> particles, float deltaTime, Vector3 wind, ref uint activeParticleCount)
    {
        uint currentActiveCount = 0;
        
        for (int i = 0; i < activeParticleCount; i++)
        {
            ref var particle = ref particles[i];
            
            if (particle.Life <= 0)
                continue;
            
            particle.Life -= deltaTime;
            
            if (particle.Life <= 0)
            {
                // Particle died this frame
                continue;
            }
            
            // Apply physics
            particle.Velocity += new Vector3(0, -9.81f, 0) * deltaTime; // Gravity
            particle.Velocity += wind * deltaTime;
            
            // Simple drag
            particle.Velocity *= (1.0f - (1.0f * deltaTime));
            
            particle.Position += particle.Velocity * deltaTime;
            
            // If it survived, we move it to the front of the array (compaction)
            if (currentActiveCount != i)
            {
                particles[(int)currentActiveCount] = particle;
            }
            
            currentActiveCount++;
        }
        
        activeParticleCount = currentActiveCount;
    }
}
