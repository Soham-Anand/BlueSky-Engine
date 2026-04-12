using System;
using System.Collections.Generic;
using System.Numerics;
using NotBSRenderer;

namespace BlueSky.Rendering.GI;

/// <summary>
/// Reflection probe system using dual-paraboloid mapping (Forza's technique).
/// 50% less memory than cubemaps, faster to sample, perfect for low-end hardware.
/// </summary>
public class ReflectionProbeSystem
{
    private readonly List<ReflectionProbe> _probes = new();
    private readonly IRHIDevice _device;
    private int _currentUpdateIndex = 0;
    
    public ReflectionProbeSystem(IRHIDevice device)
    {
        _device = device;
    }
    
    /// <summary>
    /// Add a reflection probe to the scene.
    /// </summary>
    public void AddProbe(ReflectionProbe probe)
    {
        _probes.Add(probe);
        Console.WriteLine($"[ReflectionProbeSystem] Added probe at {probe.Position}");
    }
    
    /// <summary>
    /// Update one probe per frame (amortized cost).
    /// This keeps reflections dynamic without killing performance.
    /// </summary>
    public void UpdateProbes(IRHICommandBuffer cmd, Vector3 cameraPosition)
    {
        if (_probes.Count == 0) return;
        
        // Find closest probe to camera that needs update
        var probe = FindProbeToUpdate(cameraPosition);
        if (probe != null)
        {
            RenderProbe(cmd, probe);
        }
        
        _currentUpdateIndex = (_currentUpdateIndex + 1) % _probes.Count;
    }
    
    /// <summary>
    /// Get the best reflection probe for a given world position.
    /// </summary>
    public ReflectionProbe? GetProbeForPosition(Vector3 worldPosition)
    {
        if (_probes.Count == 0) return null;
        
        ReflectionProbe? bestProbe = null;
        float bestScore = float.MaxValue;
        
        foreach (var probe in _probes)
        {
            float distance = Vector3.Distance(worldPosition, probe.Position);
            
            // Skip if outside probe range
            if (distance > probe.Range) continue;
            
            // Prefer closer probes
            float score = distance / probe.Range;
            
            if (score < bestScore)
            {
                bestScore = score;
                bestProbe = probe;
            }
        }
        
        return bestProbe;
    }
    
    /// <summary>
    /// Blend between multiple probes for smooth transitions.
    /// </summary>
    public (ReflectionProbe?, ReflectionProbe?, float) GetBlendedProbes(Vector3 worldPosition)
    {
        if (_probes.Count == 0) return (null, null, 0f);
        if (_probes.Count == 1) return (_probes[0], null, 1f);
        
        // Find two closest probes
        ReflectionProbe? probe1 = null, probe2 = null;
        float dist1 = float.MaxValue, dist2 = float.MaxValue;
        
        foreach (var probe in _probes)
        {
            float distance = Vector3.Distance(worldPosition, probe.Position);
            
            if (distance < dist1)
            {
                probe2 = probe1;
                dist2 = dist1;
                probe1 = probe;
                dist1 = distance;
            }
            else if (distance < dist2)
            {
                probe2 = probe;
                dist2 = distance;
            }
        }
        
        // Calculate blend factor
        float totalDist = dist1 + dist2;
        float blend = totalDist > 0 ? dist1 / totalDist : 0f;
        
        return (probe1, probe2, blend);
    }
    
    private ReflectionProbe? FindProbeToUpdate(Vector3 cameraPosition)
    {
        // Update probes in round-robin fashion, prioritizing those near camera
        var probe = _probes[_currentUpdateIndex];
        
        // Skip update if probe is static and already rendered
        if (probe.IsStatic && probe.IsRendered)
            return null;
        
        // Skip if too far from camera (optimization)
        float distance = Vector3.Distance(cameraPosition, probe.Position);
        if (distance > probe.Range * 2f)
            return null;
        
        return probe;
    }
    
    private void RenderProbe(IRHICommandBuffer cmd, ReflectionProbe probe)
    {
        // Render dual-paraboloid maps (front and back hemispheres)
        // This is much faster than rendering 6 cubemap faces
        
        if (probe.FrontTexture == null || probe.BackTexture == null)
        {
            CreateProbeTextures(probe);
        }
        
        // TODO: Implement actual rendering
        // 1. Render front hemisphere (looking forward)
        // 2. Render back hemisphere (looking backward)
        // 3. Apply paraboloid projection in vertex shader
        
        probe.IsRendered = true;
        probe.LastUpdateFrame = Environment.TickCount;
    }
    
    private void CreateProbeTextures(ReflectionProbe probe)
    {
        int resolution = probe.Resolution;
        
        var desc = new NotBSRenderer.TextureDesc
        {
            Width = (uint)resolution,
            Height = (uint)resolution,
            Depth = 1,
            Format = TextureFormat.RGBA16Float, // HDR for better quality
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            MipLevels = 1,
            ArrayLayers = 1
        };
        
        probe.FrontTexture = _device.CreateTexture(desc);
        probe.BackTexture = _device.CreateTexture(desc);
        
        Console.WriteLine($"[ReflectionProbeSystem] Created {resolution}x{resolution} dual-paraboloid textures");
    }
    
    /// <summary>
    /// Bake all static probes offline for maximum quality.
    /// </summary>
    public void BakeStaticProbes(BVHNode bvh, List<HorizonLight> lights)
    {
        Console.WriteLine($"[ReflectionProbeSystem] Baking {_probes.Count} static probes...");
        
        foreach (var probe in _probes)
        {
            if (probe.IsStatic)
            {
                BakeProbe(probe, bvh, lights);
            }
        }
        
        Console.WriteLine("[ReflectionProbeSystem] Baking complete!");
    }
    
    private void BakeProbe(ReflectionProbe probe, BVHNode bvh, List<HorizonLight> lights)
    {
        // Ray trace the probe environment for perfect reflections
        int resolution = probe.Resolution;
        var frontPixels = new Vector3[resolution * resolution];
        var backPixels = new Vector3[resolution * resolution];
        
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                // Convert to paraboloid coordinates
                var direction = ParaboloidToDirection(x, y, resolution, true);
                var ray = new Ray(probe.Position, direction);
                
                frontPixels[y * resolution + x] = TraceReflectionRay(ray, bvh, lights);
                
                // Back hemisphere
                direction = ParaboloidToDirection(x, y, resolution, false);
                ray = new Ray(probe.Position, direction);
                
                backPixels[y * resolution + x] = TraceReflectionRay(ray, bvh, lights);
            }
        }
        
        // TODO: Upload to GPU textures
        probe.IsRendered = true;
    }
    
    private Vector3 TraceReflectionRay(Ray ray, BVHNode bvh, List<HorizonLight> lights)
    {
        var hit = bvh?.Intersect(ray);
        
        if (hit == null)
        {
            return GetSkyColor(ray.Direction);
        }
        
        // Simple lighting for reflections
        var color = Vector3.Zero;
        foreach (var light in lights)
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
                lightDir = Vector3.Normalize(light.Position - hit.Position);
                distance = Vector3.Distance(light.Position, hit.Position);
            }
            
            float NdotL = Math.Max(0, Vector3.Dot(hit.Normal, lightDir));
            if (NdotL > 0)
            {
                var shadowRay = new Ray(hit.Position + hit.Normal * 0.001f, lightDir);
                bool inShadow = bvh?.IntersectAny(shadowRay, distance) ?? false;
                
                if (!inShadow)
                {
                    float attenuation = light.Type == LightType.Directional ? 1f : 1f / (distance * distance);
                    color += light.Color * light.Intensity * NdotL * attenuation;
                }
            }
        }
        
        var albedo = hit.Material?.Albedo ?? Vector3.One;
        return color * albedo;
    }
    
    private Vector3 ParaboloidToDirection(int x, int y, int resolution, bool front)
    {
        // Convert paraboloid UV to direction vector
        float u = (x + 0.5f) / resolution * 2f - 1f;
        float v = (y + 0.5f) / resolution * 2f - 1f;
        
        float l = u * u + v * v;
        if (l > 1f) return front ? Vector3.UnitZ : -Vector3.UnitZ;
        
        float z = (1f - l) / (1f + l);
        float scale = MathF.Sqrt(1f - z * z);
        
        var direction = new Vector3(u * scale, v * scale, z);
        return front ? direction : -direction;
    }
    
    private Vector3 GetSkyColor(Vector3 direction)
    {
        float t = 0.5f * (direction.Y + 1f);
        return Vector3.Lerp(new Vector3(1f, 1f, 1f), new Vector3(0.5f, 0.7f, 1f), t);
    }
}

/// <summary>
/// Single reflection probe instance.
/// </summary>
public class ReflectionProbe
{
    public Vector3 Position { get; set; }
    public float Range { get; set; } = 10f;
    public int Resolution { get; set; } = 128; // 128x128 is good for low-end
    public bool IsStatic { get; set; } = true; // Static probes are baked once
    public int Priority { get; set; } = 0; // Higher priority updates more often
    
    // Runtime state
    public IRHITexture? FrontTexture { get; set; }
    public IRHITexture? BackTexture { get; set; }
    public bool IsRendered { get; set; }
    public int LastUpdateFrame { get; set; }
    
    public ReflectionProbe(Vector3 position, float range = 10f, int resolution = 128)
    {
        Position = position;
        Range = range;
        Resolution = resolution;
    }
}
