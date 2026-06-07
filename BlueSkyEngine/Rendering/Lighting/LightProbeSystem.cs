using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Rendering.Lighting;

/// <summary>
/// Light Probe System - Dynamic global illumination using spherical harmonics
/// Provides fast ambient lighting that responds to scene changes
/// </summary>
public class LightProbeSystem
{
    private readonly List<LightProbe> _probes = new();
    private readonly Dictionary<Vector3, LightProbe> _probeGrid = new();
    private float _probeSpacing = 2.0f;
    
    /// <summary>
    /// Add a light probe at a position
    /// </summary>
    public void AddProbe(Vector3 position)
    {
        var probe = new LightProbe
        {
            Position = position,
            SH = new SphericalHarmonics()
        };
        
        _probes.Add(probe);
        _probeGrid[position] = probe;
    }
    
    /// <summary>
    /// Generate a grid of light probes in a volume
    /// </summary>
    public void GenerateProbeGrid(Vector3 min, Vector3 max, float spacing)
    {
        _probeSpacing = spacing;
        _probes.Clear();
        _probeGrid.Clear();
        
        for (float x = min.X; x <= max.X; x += spacing)
        {
            for (float y = min.Y; y <= max.Y; y += spacing)
            {
                for (float z = min.Z; z <= max.Z; z += spacing)
                {
                    AddProbe(new Vector3(x, y, z));
                }
            }
        }
        
        Console.WriteLine($"[Horizon] Generated {_probes.Count} light probes");
    }
    
    /// <summary>
    /// Update all light probes by sampling the scene
    /// </summary>
    public void UpdateProbes(HorizonLighting lighting)
    {
        foreach (var probe in _probes)
        {
            UpdateProbe(probe, lighting);
        }
    }
    
    /// <summary>
    /// Sample lighting at a world position using probe interpolation
    /// </summary>
    public Vector3 SampleLighting(Vector3 worldPos, Vector3 normal)
    {
        // Find nearest probes for trilinear interpolation
        var nearbyProbes = FindNearbyProbes(worldPos, 8);
        
        if (nearbyProbes.Count == 0)
            return Vector3.Zero;
        
        // Weighted average based on distance
        Vector3 result = Vector3.Zero;
        float totalWeight = 0;
        
        foreach (var probe in nearbyProbes)
        {
            float distance = Vector3.Distance(worldPos, probe.Position);
            float weight = 1.0f / (distance + 0.001f);
            
            result += probe.SH.Evaluate(normal) * weight;
            totalWeight += weight;
        }
        
        return result / totalWeight;
    }
    
    private void UpdateProbe(LightProbe probe, HorizonLighting lighting)
    {
        // Sample lighting in multiple directions and encode to SH
        var directions = GetSampleDirections();
        var samples = new Vector3[directions.Length];
        
        for (int i = 0; i < directions.Length; i++)
        {
            // Sample lighting in this direction
            var input = new LightingInput
            {
                WorldPos = probe.Position,
                Normal = directions[i],
                ViewDir = -directions[i],
                ScreenUV = Vector2.Zero,
                Depth = 0,
                Albedo = Vector3.One,
                Metallic = 0,
                Roughness = 1.0f,
                AO = 1.0f
            };
            
            samples[i] = lighting.CalculateLighting(input);
        }
        
        // Project samples to spherical harmonics
        probe.SH.ProjectFromSamples(directions, samples);
    }
    
    private List<LightProbe> FindNearbyProbes(Vector3 position, int maxCount)
    {
        var nearby = new List<(LightProbe probe, float distance)>();
        
        foreach (var probe in _probes)
        {
            float distance = Vector3.Distance(position, probe.Position);
            if (distance < _probeSpacing * 2)
            {
                nearby.Add((probe, distance));
            }
        }
        
        // Sort by distance and take closest
        nearby.Sort((a, b) => a.distance.CompareTo(b.distance));
        
        var result = new List<LightProbe>();
        for (int i = 0; i < Math.Min(maxCount, nearby.Count); i++)
        {
            result.Add(nearby[i].probe);
        }
        
        return result;
    }
    
    private Vector3[] GetSampleDirections()
    {
        // Fibonacci sphere sampling for even distribution
        const int sampleCount = 64;
        var directions = new Vector3[sampleCount];
        
        float phi = MathF.PI * (3.0f - MathF.Sqrt(5.0f)); // Golden angle
        
        for (int i = 0; i < sampleCount; i++)
        {
            float y = 1.0f - (i / (float)(sampleCount - 1)) * 2.0f;
            float radius = MathF.Sqrt(1.0f - y * y);
            float theta = phi * i;
            
            float x = MathF.Cos(theta) * radius;
            float z = MathF.Sin(theta) * radius;
            
            directions[i] = Vector3.Normalize(new Vector3(x, y, z));
        }
        
        return directions;
    }
}

/// <summary>
/// Light probe storing spherical harmonics coefficients
/// </summary>
public class LightProbe
{
    public Vector3 Position;
    public SphericalHarmonics SH;
}

/// <summary>
/// Spherical Harmonics (L2) for efficient lighting storage
/// Uses 9 coefficients (3 bands) for good quality/performance balance
/// </summary>
public class SphericalHarmonics
{
    // RGB coefficients for 9 SH basis functions (L0, L1, L2)
    public Vector3[] Coefficients = new Vector3[9];
    
    public SphericalHarmonics()
    {
        for (int i = 0; i < 9; i++)
            Coefficients[i] = Vector3.Zero;
    }
    
    /// <summary>
    /// Evaluate SH lighting for a given normal direction
    /// </summary>
    public Vector3 Evaluate(Vector3 normal)
    {
        Vector3 result = Vector3.Zero;
        
        // L0 band (constant)
        result += Coefficients[0] * 0.282095f;
        
        // L1 band (linear)
        result += Coefficients[1] * (0.488603f * normal.Y);
        result += Coefficients[2] * (0.488603f * normal.Z);
        result += Coefficients[3] * (0.488603f * normal.X);
        
        // L2 band (quadratic)
        result += Coefficients[4] * (1.092548f * normal.X * normal.Y);
        result += Coefficients[5] * (1.092548f * normal.Y * normal.Z);
        result += Coefficients[6] * (0.315392f * (3.0f * normal.Z * normal.Z - 1.0f));
        result += Coefficients[7] * (1.092548f * normal.X * normal.Z);
        result += Coefficients[8] * (0.546274f * (normal.X * normal.X - normal.Y * normal.Y));
        
        return Vector3.Max(result, Vector3.Zero);
    }
    
    /// <summary>
    /// Project lighting samples onto SH basis
    /// </summary>
    public void ProjectFromSamples(Vector3[] directions, Vector3[] samples)
    {
        if (directions.Length != samples.Length)
            throw new ArgumentException("Directions and samples must have same length");
        
        // Clear coefficients
        for (int i = 0; i < 9; i++)
            Coefficients[i] = Vector3.Zero;
        
        float weight = 4.0f * MathF.PI / directions.Length;
        
        for (int i = 0; i < directions.Length; i++)
        {
            Vector3 dir = directions[i];
            Vector3 sample = samples[i];
            
            // Project onto each SH basis function
            Coefficients[0] += sample * (0.282095f * weight);
            Coefficients[1] += sample * (0.488603f * dir.Y * weight);
            Coefficients[2] += sample * (0.488603f * dir.Z * weight);
            Coefficients[3] += sample * (0.488603f * dir.X * weight);
            Coefficients[4] += sample * (1.092548f * dir.X * dir.Y * weight);
            Coefficients[5] += sample * (1.092548f * dir.Y * dir.Z * weight);
            Coefficients[6] += sample * (0.315392f * (3.0f * dir.Z * dir.Z - 1.0f) * weight);
            Coefficients[7] += sample * (1.092548f * dir.X * dir.Z * weight);
            Coefficients[8] += sample * (0.546274f * (dir.X * dir.X - dir.Y * dir.Y) * weight);
        }
    }
    
    /// <summary>
    /// Add another SH to this one (for blending)
    /// </summary>
    public void Add(SphericalHarmonics other, float weight = 1.0f)
    {
        for (int i = 0; i < 9; i++)
        {
            Coefficients[i] += other.Coefficients[i] * weight;
        }
    }
}
