using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace BlueSky.Rendering.GI;

/// <summary>
/// Irradiance volume using Spherical Harmonics for dynamic object lighting.
/// Provides photorealistic GI for moving objects with near-zero runtime cost.
/// This is how games like Spider-Man and The Last of Us achieve amazing lighting on PS4.
/// </summary>
public class IrradianceVolume
{
    private readonly Vector3 _minBounds;
    private readonly Vector3 _maxBounds;
    private readonly int _resolutionX;
    private readonly int _resolutionY;
    private readonly int _resolutionZ;
    
    // Spherical Harmonics coefficients (9 per probe, RGB)
    private readonly SHCoefficients[] _probes;
    
    public IrradianceVolume(Vector3 minBounds, Vector3 maxBounds, int resolution = 32)
    {
        _minBounds = minBounds;
        _maxBounds = maxBounds;
        _resolutionX = resolution;
        _resolutionY = resolution;
        _resolutionZ = resolution;
        
        int probeCount = _resolutionX * _resolutionY * _resolutionZ;
        _probes = new SHCoefficients[probeCount];
        
        Console.WriteLine($"[IrradianceVolume] Created {resolution}³ grid = {probeCount} probes");
        Console.WriteLine($"[IrradianceVolume] Memory: {probeCount * 9 * 3 * 4 / 1024}KB");
    }
    
    /// <summary>
    /// Bake irradiance volume using path tracing.
    /// This runs offline and can take several minutes for high quality.
    /// </summary>
    public async Task BakeAsync(BVHNode bvh, List<HorizonLight> lights, int samplesPerProbe = 512)
    {
        Console.WriteLine($"[IrradianceVolume] Baking with {samplesPerProbe} samples per probe...");
        
        await Task.Run(() =>
        {
            Parallel.For(0, _probes.Length, probeIndex =>
            {
                var probePos = GetProbePosition(probeIndex);
                _probes[probeIndex] = BakeProbe(probePos, bvh, lights, samplesPerProbe);
                
                if (probeIndex % 1000 == 0)
                {
                    float progress = (float)probeIndex / _probes.Length * 100f;
                    Console.WriteLine($"[IrradianceVolume] Progress: {progress:F1}%");
                }
            });
        });
        
        Console.WriteLine("[IrradianceVolume] Baking complete!");
    }
    
    private SHCoefficients BakeProbe(Vector3 position, BVHNode bvh, List<HorizonLight> lights, int samples)
    {
        var sh = new SHCoefficients();
        var random = new Random(position.GetHashCode());
        
        // Sample hemisphere around probe
        for (int i = 0; i < samples; i++)
        {
            // Generate random direction
            var direction = SampleHemisphere(random);
            var ray = new Ray(position, direction);
            
            // Trace ray and get incoming light
            var color = TraceRay(ray, bvh, lights, random);
            
            // Project onto spherical harmonics
            sh.AddSample(direction, color);
        }
        
        sh.Normalize(samples);
        return sh;
    }
    
    private Vector3 TraceRay(Ray ray, BVHNode bvh, List<HorizonLight> lights, Random random)
    {
        // Simple path tracing (1 bounce for speed)
        var hit = bvh?.Intersect(ray);
        
        if (hit == null)
        {
            // Hit sky
            return GetSkyColor(ray.Direction);
        }
        
        // Direct lighting
        var directLight = Vector3.Zero;
        foreach (var light in lights)
        {
            directLight += SampleLight(light, hit.Position, hit.Normal, bvh);
        }
        
        // Indirect lighting (1 bounce)
        var indirectRay = GenerateCosineWeightedRay(hit.Position, hit.Normal, random);
        var indirectHit = bvh?.Intersect(indirectRay);
        
        var indirectLight = Vector3.Zero;
        if (indirectHit != null)
        {
            foreach (var light in lights)
            {
                indirectLight += SampleLight(light, indirectHit.Position, indirectHit.Normal, bvh);
            }
        }
        
        var albedo = hit.Material?.Albedo ?? Vector3.One;
        return (directLight + indirectLight * 0.5f) * albedo;
    }
    
    private Vector3 SampleLight(HorizonLight light, Vector3 position, Vector3 normal, BVHNode bvh)
    {
        Vector3 lightDir;
        float distance;
        
        if (light.Type == LightType.Directional)
        {
            lightDir = -light.Direction;
            distance = float.MaxValue;
        }
        else
        {
            lightDir = Vector3.Normalize(light.Position - position);
            distance = Vector3.Distance(light.Position, position);
        }
        
        float NdotL = Math.Max(0, Vector3.Dot(normal, lightDir));
        if (NdotL <= 0) return Vector3.Zero;
        
        // Shadow test
        var shadowRay = new Ray(position + normal * 0.001f, lightDir);
        bool inShadow = bvh?.IntersectAny(shadowRay, distance) ?? false;
        
        if (inShadow) return Vector3.Zero;
        
        float attenuation = light.Type == LightType.Directional ? 1f : 1f / (distance * distance);
        return light.Color * light.Intensity * NdotL * attenuation;
    }
    
    /// <summary>
    /// Sample irradiance at a world position using trilinear interpolation.
    /// This is called every frame for dynamic objects - must be fast!
    /// </summary>
    public Vector3 SampleIrradiance(Vector3 worldPosition, Vector3 normal)
    {
        // Convert world position to grid coordinates
        var localPos = (worldPosition - _minBounds) / (_maxBounds - _minBounds);
        localPos = Vector3.Clamp(localPos, Vector3.Zero, Vector3.One);
        
        var gridPos = localPos * new Vector3(_resolutionX - 1, _resolutionY - 1, _resolutionZ - 1);
        
        int x0 = (int)gridPos.X;
        int y0 = (int)gridPos.Y;
        int z0 = (int)gridPos.Z;
        int x1 = Math.Min(x0 + 1, _resolutionX - 1);
        int y1 = Math.Min(y0 + 1, _resolutionY - 1);
        int z1 = Math.Min(z0 + 1, _resolutionZ - 1);
        
        float fx = gridPos.X - x0;
        float fy = gridPos.Y - y0;
        float fz = gridPos.Z - z0;
        
        // Trilinear interpolation of 8 nearest probes
        var c000 = _probes[GetProbeIndex(x0, y0, z0)].Evaluate(normal);
        var c001 = _probes[GetProbeIndex(x0, y0, z1)].Evaluate(normal);
        var c010 = _probes[GetProbeIndex(x0, y1, z0)].Evaluate(normal);
        var c011 = _probes[GetProbeIndex(x0, y1, z1)].Evaluate(normal);
        var c100 = _probes[GetProbeIndex(x1, y0, z0)].Evaluate(normal);
        var c101 = _probes[GetProbeIndex(x1, y0, z1)].Evaluate(normal);
        var c110 = _probes[GetProbeIndex(x1, y1, z0)].Evaluate(normal);
        var c111 = _probes[GetProbeIndex(x1, y1, z1)].Evaluate(normal);
        
        var c00 = Vector3.Lerp(c000, c001, fz);
        var c01 = Vector3.Lerp(c010, c011, fz);
        var c10 = Vector3.Lerp(c100, c101, fz);
        var c11 = Vector3.Lerp(c110, c111, fz);
        
        var c0 = Vector3.Lerp(c00, c01, fy);
        var c1 = Vector3.Lerp(c10, c11, fy);
        
        return Vector3.Lerp(c0, c1, fx);
    }
    
    private Vector3 GetProbePosition(int index)
    {
        int z = index / (_resolutionX * _resolutionY);
        int y = (index / _resolutionX) % _resolutionY;
        int x = index % _resolutionX;
        
        var t = new Vector3(
            (float)x / (_resolutionX - 1),
            (float)y / (_resolutionY - 1),
            (float)z / (_resolutionZ - 1)
        );
        
        return _minBounds + t * (_maxBounds - _minBounds);
    }
    
    private int GetProbeIndex(int x, int y, int z)
    {
        return z * (_resolutionX * _resolutionY) + y * _resolutionX + x;
    }
    
    private Vector3 SampleHemisphere(Random random)
    {
        float u1 = (float)random.NextDouble();
        float u2 = (float)random.NextDouble();
        
        float r = MathF.Sqrt(u1);
        float theta = 2f * MathF.PI * u2;
        
        float x = r * MathF.Cos(theta);
        float y = r * MathF.Sin(theta);
        float z = MathF.Sqrt(Math.Max(0, 1f - u1));
        
        return new Vector3(x, y, z);
    }
    
    private Ray GenerateCosineWeightedRay(Vector3 origin, Vector3 normal, Random random)
    {
        var direction = SampleHemisphere(random);
        
        // Transform to world space
        var tangent = Vector3.Normalize(Vector3.Cross(normal, Math.Abs(normal.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
        var bitangent = Vector3.Cross(normal, tangent);
        
        var worldDir = direction.X * tangent + direction.Y * bitangent + direction.Z * normal;
        return new Ray(origin + normal * 0.001f, Vector3.Normalize(worldDir));
    }
    
    private Vector3 GetSkyColor(Vector3 direction)
    {
        float t = 0.5f * (direction.Y + 1f);
        return Vector3.Lerp(new Vector3(1f, 1f, 1f), new Vector3(0.5f, 0.7f, 1f), t) * 0.5f;
    }
}

/// <summary>
/// Spherical Harmonics coefficients for storing directional lighting.
/// Uses L2 (9 coefficients) which is the sweet spot for quality vs memory.
/// </summary>
public struct SHCoefficients
{
    // RGB coefficients for 9 SH basis functions
    public Vector3 L00; // DC term
    public Vector3 L1m1, L10, L1p1; // Linear terms
    public Vector3 L2m2, L2m1, L20, L2p1, L2p2; // Quadratic terms
    
    /// <summary>
    /// Add a directional light sample to the SH coefficients.
    /// </summary>
    public void AddSample(Vector3 direction, Vector3 color)
    {
        // Evaluate SH basis functions
        float y00 = 0.282095f; // sqrt(1/(4*pi))
        float y1m1 = 0.488603f * direction.Y;
        float y10 = 0.488603f * direction.Z;
        float y1p1 = 0.488603f * direction.X;
        float y2m2 = 1.092548f * direction.X * direction.Y;
        float y2m1 = 1.092548f * direction.Y * direction.Z;
        float y20 = 0.315392f * (3f * direction.Z * direction.Z - 1f);
        float y2p1 = 1.092548f * direction.X * direction.Z;
        float y2p2 = 0.546274f * (direction.X * direction.X - direction.Y * direction.Y);
        
        // Accumulate
        L00 += color * y00;
        L1m1 += color * y1m1;
        L10 += color * y10;
        L1p1 += color * y1p1;
        L2m2 += color * y2m2;
        L2m1 += color * y2m1;
        L20 += color * y20;
        L2p1 += color * y2p1;
        L2p2 += color * y2p2;
    }
    
    /// <summary>
    /// Normalize coefficients after accumulating samples.
    /// </summary>
    public void Normalize(int sampleCount)
    {
        float scale = 4f * MathF.PI / sampleCount;
        L00 *= scale;
        L1m1 *= scale;
        L10 *= scale;
        L1p1 *= scale;
        L2m2 *= scale;
        L2m1 *= scale;
        L20 *= scale;
        L2p1 *= scale;
        L2p2 *= scale;
    }
    
    /// <summary>
    /// Evaluate irradiance for a given normal direction.
    /// This is the runtime function - must be fast!
    /// </summary>
    public Vector3 Evaluate(Vector3 normal)
    {
        // Evaluate SH basis functions for normal
        float y00 = 0.282095f;
        float y1m1 = 0.488603f * normal.Y;
        float y10 = 0.488603f * normal.Z;
        float y1p1 = 0.488603f * normal.X;
        float y2m2 = 1.092548f * normal.X * normal.Y;
        float y2m1 = 1.092548f * normal.Y * normal.Z;
        float y20 = 0.315392f * (3f * normal.Z * normal.Z - 1f);
        float y2p1 = 1.092548f * normal.X * normal.Z;
        float y2p2 = 0.546274f * (normal.X * normal.X - normal.Y * normal.Y);
        
        // Dot product with coefficients
        var result = L00 * y00 +
                     L1m1 * y1m1 + L10 * y10 + L1p1 * y1p1 +
                     L2m2 * y2m2 + L2m1 * y2m1 + L20 * y20 + L2p1 * y2p1 + L2p2 * y2p2;
        
        return Vector3.Max(result, Vector3.Zero);
    }
}

public enum LightType
{
    Directional,
    Point,
    Spot
}

public class HorizonLight
{
    public LightType Type { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Direction { get; set; }
    public Vector3 Color { get; set; } = Vector3.One;
    public float Intensity { get; set; } = 1f;
}
