using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Rendering.Lighting;

namespace BlueSky.Rendering.GI;

/// <summary>
/// Offline lightmap baker using path tracing for photorealistic GI.
/// Bakes direct + indirect lighting into textures for zero runtime cost.
/// This is the secret to Ultra graphics on low-end hardware.
/// </summary>
public class LightmapBaker
{
    private readonly World _world;
    private readonly List<HorizonLight> _lights;
    private readonly BakingSettings _settings;
    
    // Baking progress
    public float Progress { get; private set; }
    public string CurrentTask { get; private set; } = "";
    public bool IsCompleted { get; private set; }
    
    public LightmapBaker(World world, List<HorizonLight> lights, BakingSettings settings)
    {
        _world = world;
        _lights = lights;
        _settings = settings;
    }
    
    /// <summary>
    /// Bake lightmaps for all static geometry in the scene.
    /// This runs offline (in editor) and can take minutes/hours for high quality.
    /// </summary>
    public async Task<LightmapAtlas> BakeAsync()
    {
        Progress = 0f;
        IsCompleted = false;
        
        // Step 1: Collect all static meshes
        CurrentTask = "Collecting static geometry...";
        var staticMeshes = CollectStaticMeshes();
        Progress = 0.1f;
        
        // Step 2: Generate UV2 (lightmap UVs) if needed
        CurrentTask = "Generating lightmap UVs...";
        GenerateLightmapUVs(staticMeshes);
        Progress = 0.2f;
        
        // Step 3: Pack lightmaps into atlas
        CurrentTask = "Packing lightmap atlas...";
        var atlas = PackLightmapAtlas(staticMeshes);
        Progress = 0.3f;
        
        // Step 4: Build acceleration structure for ray tracing
        CurrentTask = "Building BVH acceleration structure...";
        var bvh = BuildBVH(staticMeshes);
        Progress = 0.4f;
        
        // Step 5: Bake direct lighting
        CurrentTask = "Baking direct lighting...";
        await BakeDirectLightingAsync(atlas, bvh, staticMeshes);
        Progress = 0.6f;
        
        // Step 6: Bake indirect lighting (multiple bounces)
        CurrentTask = "Baking indirect lighting (this takes time)...";
        await BakeIndirectLightingAsync(atlas, bvh, staticMeshes);
        Progress = 0.9f;
        
        // Step 7: Post-process (denoise, dilate edges)
        CurrentTask = "Post-processing lightmaps...";
        PostProcessLightmaps(atlas);
        Progress = 1.0f;
        
        CurrentTask = "Complete!";
        IsCompleted = true;
        
        return atlas;
    }
    
    private List<StaticMeshInstance> CollectStaticMeshes()
    {
        var meshes = new List<StaticMeshInstance>();
        
        // Use ForEach to iterate entities with Transform and StaticMesh components
        _world.ForEach<TransformComponent, StaticMeshComponent>((entity, transform, meshComp) =>
        {
            // For now, consider all meshes as static (we'll add IsStatic flag later)
            meshes.Add(new StaticMeshInstance
            {
                Entity = entity,
                Transform = transform,
                MeshComponent = meshComp
            });
        });
        
        Console.WriteLine($"[LightmapBaker] Found {meshes.Count} static meshes to bake");
        return meshes;
    }
    
    private void GenerateLightmapUVs(List<StaticMeshInstance> meshes)
    {
        // Generate secondary UV channel for lightmaps
        // This ensures no overlapping UVs and proper texel density
        foreach (var mesh in meshes)
        {
            // TODO: Implement xatlas or similar UV unwrapping
            // For now, assume meshes have proper UV2
            Console.WriteLine($"[LightmapBaker] Note: Mesh {mesh.Entity.Id} will use default UVs for lightmapping");
        }
    }
    
    private LightmapAtlas PackLightmapAtlas(List<StaticMeshInstance> meshes)
    {
        // Pack all lightmaps into a single large texture atlas
        // This minimizes draw calls and texture switches
        
        int atlasSize = _settings.AtlasResolution;
        var atlas = new LightmapAtlas(atlasSize, atlasSize);
        
        // Simple bin packing (can be improved with better algorithm)
        int currentX = 0, currentY = 0, rowHeight = 0;
        
        foreach (var mesh in meshes)
        {
            int texelSize = CalculateTexelSize(mesh);
            
            // Check if we need to move to next row
            if (currentX + texelSize > atlasSize)
            {
                currentX = 0;
                currentY += rowHeight;
                rowHeight = 0;
            }
            
            // Check if atlas is full
            if (currentY + texelSize > atlasSize)
            {
                Console.WriteLine($"[LightmapBaker] Warning: Atlas full, some meshes won't have lightmaps");
                break;
            }
            
            // Allocate space in atlas
            atlas.AllocateRegion(mesh.Entity, currentX, currentY, texelSize, texelSize);
            
            currentX += texelSize;
            rowHeight = Math.Max(rowHeight, texelSize);
        }
        
        Console.WriteLine($"[LightmapBaker] Packed {atlas.RegionCount} meshes into {atlasSize}x{atlasSize} atlas");
        return atlas;
    }
    
    private int CalculateTexelSize(StaticMeshInstance mesh)
    {
        // Calculate appropriate lightmap resolution based on mesh size
        // For now, use a default size (we'll add Bounds field to StaticMeshComponent later)
        
        // Target: 10 texels per world unit (configurable)
        float texelsPerUnit = _settings.TexelsPerUnit;
        int texelSize = 128; // Default size
        
        // TODO: Calculate based on actual mesh bounds when available
        // var bounds = mesh.MeshComponent.Bounds;
        // float surfaceArea = CalculateSurfaceArea(bounds);
        // int texelSize = (int)MathF.Sqrt(surfaceArea * texelsPerUnit);
        
        // Clamp to power of 2 for better GPU performance
        texelSize = NextPowerOfTwo(Math.Clamp(texelSize, 32, 512));
        
        return texelSize;
    }
    
    private float CalculateSurfaceArea(BoundingBox bounds)
    {
        var size = bounds.Max - bounds.Min;
        return 2f * (size.X * size.Y + size.Y * size.Z + size.Z * size.X);
    }
    
    private int NextPowerOfTwo(int value)
    {
        int power = 1;
        while (power < value) power *= 2;
        return power;
    }
    
    private BVHNode BuildBVH(List<StaticMeshInstance> meshes)
    {
        // Build Bounding Volume Hierarchy for fast ray-triangle intersection
        // This makes path tracing 100-1000x faster
        
        Console.WriteLine($"[LightmapBaker] Building BVH for {meshes.Count} meshes...");
        
        var triangles = new List<Triangle>();
        foreach (var mesh in meshes)
        {
            // Extract triangles from mesh (TODO: implement mesh data access)
            // triangles.AddRange(ExtractTriangles(mesh));
        }
        
        var root = BVHNode.Build(triangles, 0, triangles.Count);
        Console.WriteLine($"[LightmapBaker] BVH built with {triangles.Count} triangles");
        
        return root;
    }
    
    private async Task BakeDirectLightingAsync(LightmapAtlas atlas, BVHNode bvh, List<StaticMeshInstance> meshes)
    {
        // Bake direct lighting from all light sources
        // This is the "easy" part - just shadow rays
        
        int samplesPerTexel = _settings.DirectSamples;
        
        await Task.Run(() =>
        {
            Parallel.ForEach(atlas.Regions, region =>
            {
                for (int y = 0; y < region.Height; y++)
                {
                    for (int x = 0; x < region.Width; x++)
                    {
                        var worldPos = GetWorldPosition(region, x, y);
                        var normal = GetWorldNormal(region, x, y);
                        
                        var color = Vector3.Zero;
                        
                        // Sample each light with multiple shadow rays
                        foreach (var light in _lights)
                        {
                            color += SampleDirectLight(light, worldPos, normal, bvh, samplesPerTexel);
                        }
                        
                        atlas.SetPixel(region.X + x, region.Y + y, color);
                    }
                }
            });
        });
    }
    
    private async Task BakeIndirectLightingAsync(LightmapAtlas atlas, BVHNode bvh, List<StaticMeshInstance> meshes)
    {
        // Bake indirect lighting using path tracing
        // This is where the magic happens - multiple light bounces for photorealism
        
        int bounces = _settings.IndirectBounces;
        int samplesPerTexel = _settings.IndirectSamples;
        
        Console.WriteLine($"[LightmapBaker] Path tracing with {bounces} bounces, {samplesPerTexel} samples per texel");
        
        await Task.Run(() =>
        {
            Parallel.ForEach(atlas.Regions, region =>
            {
                var random = new Random(region.GetHashCode());
                
                for (int y = 0; y < region.Height; y++)
                {
                    for (int x = 0; x < region.Width; x++)
                    {
                        var worldPos = GetWorldPosition(region, x, y);
                        var normal = GetWorldNormal(region, x, y);
                        
                        var indirectColor = Vector3.Zero;
                        
                        // Monte Carlo integration - shoot many rays
                        for (int sample = 0; sample < samplesPerTexel; sample++)
                        {
                            var ray = GenerateCosineWeightedRay(worldPos, normal, random);
                            indirectColor += PathTrace(ray, bvh, bounces, random);
                        }
                        
                        indirectColor /= samplesPerTexel;
                        
                        // Add to existing direct lighting
                        var existingColor = atlas.GetPixel(region.X + x, region.Y + y);
                        atlas.SetPixel(region.X + x, region.Y + y, existingColor + indirectColor);
                    }
                }
            });
        });
    }
    
    private Vector3 SampleDirectLight(HorizonLight light, Vector3 position, Vector3 normal, BVHNode bvh, int samples)
    {
        // Sample direct lighting from a single light source
        var color = Vector3.Zero;
        
        for (int i = 0; i < samples; i++)
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
            if (NdotL > 0)
            {
                // Cast shadow ray
                var shadowRay = new Ray(position + normal * 0.001f, lightDir);
                bool inShadow = bvh?.IntersectAny(shadowRay, distance) ?? false;
                
                if (!inShadow)
                {
                    float attenuation = light.Type == LightType.Directional ? 1f : 1f / (distance * distance);
                    color += light.Color * light.Intensity * NdotL * attenuation;
                }
            }
        }
        
        return color / samples;
    }
    
    private Vector3 PathTrace(Ray ray, BVHNode bvh, int bouncesLeft, Random random)
    {
        // Recursive path tracing for global illumination
        if (bouncesLeft <= 0) return Vector3.Zero;
        
        var hit = bvh?.Intersect(ray);
        if (hit == null) return GetSkyColor(ray.Direction);
        
        // Russian roulette for early termination
        float survivalProbability = 0.8f;
        if (random.NextDouble() > survivalProbability) return Vector3.Zero;
        
        // Sample next bounce direction
        var nextRay = GenerateCosineWeightedRay(hit.Position, hit.Normal, random);
        var incomingLight = PathTrace(nextRay, bvh, bouncesLeft - 1, random);
        
        // Apply BRDF (simplified Lambertian for now)
        var albedo = hit.Material?.Albedo ?? Vector3.One;
        var brdf = albedo / MathF.PI;
        
        return incomingLight * brdf / survivalProbability;
    }
    
    private Ray GenerateCosineWeightedRay(Vector3 origin, Vector3 normal, Random random)
    {
        // Generate random direction in hemisphere weighted by cosine
        float u1 = (float)random.NextDouble();
        float u2 = (float)random.NextDouble();
        
        float r = MathF.Sqrt(u1);
        float theta = 2f * MathF.PI * u2;
        
        float x = r * MathF.Cos(theta);
        float y = r * MathF.Sin(theta);
        float z = MathF.Sqrt(Math.Max(0, 1f - u1));
        
        // Transform to world space
        var tangent = Vector3.Normalize(Vector3.Cross(normal, Math.Abs(normal.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
        var bitangent = Vector3.Cross(normal, tangent);
        
        var direction = x * tangent + y * bitangent + z * normal;
        return new Ray(origin + normal * 0.001f, Vector3.Normalize(direction));
    }
    
    private Vector3 GetSkyColor(Vector3 direction)
    {
        // Simple sky color for environment lighting
        float t = 0.5f * (direction.Y + 1f);
        return Vector3.Lerp(new Vector3(1f, 1f, 1f), new Vector3(0.5f, 0.7f, 1f), t) * 0.5f;
    }
    
    private void PostProcessLightmaps(LightmapAtlas atlas)
    {
        // Post-process lightmaps for better quality
        
        // 1. Denoise (optional, for low sample counts)
        if (_settings.Denoise)
        {
            atlas.Denoise();
        }
        
        // 2. Dilate edges to prevent seams
        atlas.DilateEdges();
        
        // 3. Apply exposure and gamma correction
        atlas.ApplyExposure(_settings.Exposure);
    }
    
    private Vector3 GetWorldPosition(LightmapRegion region, int x, int y)
    {
        // TODO: Implement UV to world space conversion
        return Vector3.Zero;
    }
    
    private Vector3 GetWorldNormal(LightmapRegion region, int x, int y)
    {
        // TODO: Implement UV to world space normal conversion
        return Vector3.UnitY;
    }
}

/// <summary>
/// Settings for lightmap baking quality vs speed tradeoff.
/// </summary>
public class BakingSettings
{
    public int AtlasResolution { get; set; } = 4096; // 4K atlas
    public float TexelsPerUnit { get; set; } = 10f; // Texel density
    
    public int DirectSamples { get; set; } = 16; // Samples for direct lighting
    public int IndirectSamples { get; set; } = 256; // Samples for GI (higher = better quality)
    public int IndirectBounces { get; set; } = 5; // Light bounces (higher = more realistic)
    
    public bool Denoise { get; set; } = true;
    public float Exposure { get; set; } = 1.0f;
    
    public static BakingSettings Fast => new()
    {
        AtlasResolution = 2048,
        DirectSamples = 8,
        IndirectSamples = 64,
        IndirectBounces = 3
    };
    
    public static BakingSettings Balanced => new()
    {
        AtlasResolution = 4096,
        DirectSamples = 16,
        IndirectSamples = 256,
        IndirectBounces = 5
    };
    
    public static BakingSettings HighQuality => new()
    {
        AtlasResolution = 8192,
        DirectSamples = 32,
        IndirectSamples = 1024,
        IndirectBounces = 10
    };
}

public class StaticMeshInstance
{
    public Entity Entity { get; set; }
    public TransformComponent Transform { get; set; }
    public StaticMeshComponent MeshComponent { get; set; }
}

// BoundingBox moved to BVHNode.cs to avoid duplication

public struct Ray
{
    public Vector3 Origin;
    public Vector3 Direction;
    
    public Ray(Vector3 origin, Vector3 direction)
    {
        Origin = origin;
        Direction = direction;
    }
}

public class RayHit
{
    public Vector3 Position { get; set; }
    public Vector3 Normal { get; set; }
    public float Distance { get; set; }
    public MaterialData? Material { get; set; }
}

public class MaterialData
{
    public Vector3 Albedo { get; set; } = Vector3.One;
}
