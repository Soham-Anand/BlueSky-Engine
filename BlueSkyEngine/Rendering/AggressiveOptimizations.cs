using System;
using System.Numerics;
using System.Collections.Generic;
using NotBSRenderer;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;

namespace BlueSky.Rendering;

/// <summary>
/// Aggressive Optimizations - Every trick to squeeze performance
/// Goal: Ultra graphics on integrated graphics at 120fps
/// 
/// "If the player can't see it, don't render it"
/// "If the player won't notice, fake it"
/// "If it's expensive, do it less often"
/// </summary>
public static class AggressiveOptimizations
{
    /// <summary>
    /// Frustum culling - don't render what's outside the camera view
    /// </summary>
    public static bool IsInFrustum(Vector3 position, float radius, Matrix4x4 viewProj)
    {
        Span<Vector4> planes = stackalloc Vector4[6];
        
        planes[0] = new Vector4(viewProj.M14 + viewProj.M11, viewProj.M24 + viewProj.M21, viewProj.M34 + viewProj.M31, viewProj.M44 + viewProj.M41);
        planes[1] = new Vector4(viewProj.M14 - viewProj.M11, viewProj.M24 - viewProj.M21, viewProj.M34 - viewProj.M31, viewProj.M44 - viewProj.M41);
        planes[2] = new Vector4(viewProj.M14 + viewProj.M12, viewProj.M24 + viewProj.M22, viewProj.M34 + viewProj.M32, viewProj.M44 + viewProj.M42);
        planes[3] = new Vector4(viewProj.M14 - viewProj.M12, viewProj.M24 - viewProj.M22, viewProj.M34 - viewProj.M32, viewProj.M44 - viewProj.M42);
        planes[4] = new Vector4(viewProj.M13, viewProj.M23, viewProj.M33, viewProj.M43);
        planes[5] = new Vector4(viewProj.M14 - viewProj.M13, viewProj.M24 - viewProj.M23, viewProj.M34 - viewProj.M33, viewProj.M44 - viewProj.M43);

        for (int i = 0; i < 6; i++)
        {
            float length = MathF.Sqrt(planes[i].X * planes[i].X + planes[i].Y * planes[i].Y + planes[i].Z * planes[i].Z);
            if (length > 0.0001f)
            {
                planes[i] /= length;
                float distance = planes[i].X * position.X + planes[i].Y * position.Y + planes[i].Z * position.Z + planes[i].W;
                if (distance < -radius)
                    return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Occlusion culling - don't render what's behind other objects
    /// Uses hierarchical Z-buffer (Hi-Z) for fast rejection
    /// </summary>
    public static bool IsOccluded(Vector3 position, float radius, IRHITexture depthBuffer)
    {
        // TODO: Sample hierarchical depth buffer
        // If object is fully behind existing geometry, cull it
        return false;
    }
    
    /// <summary>
    /// Small object culling - don't render tiny objects far away
    /// </summary>
    public static bool IsTooSmall(Vector3 position, float radius, Vector3 cameraPos, 
                                  float screenHeight, float fov, float threshold)
    {
        float distance = Vector3.Distance(position, cameraPos);
        
        // Calculate screen-space size
        float screenSize = (radius * screenHeight) / (distance * MathF.Tan(fov * 0.5f));
        
        // Cull if smaller than threshold (in pixels)
        return screenSize < threshold;
    }
    
    /// <summary>
    /// LOD selection - use lower detail models for distant objects
    /// </summary>
    public static int SelectLOD(Vector3 position, Vector3 cameraPos, float lodBias, int maxLOD)
    {
        float distance = Vector3.Distance(position, cameraPos);
        
        // LOD thresholds (in meters)
        float[] thresholds = { 10.0f, 25.0f, 50.0f, 100.0f };
        
        for (int i = 0; i < Math.Min(thresholds.Length, maxLOD); i++)
        {
            if (distance < thresholds[i] / lodBias)
                return i;
        }
        
        return maxLOD;
    }
    
    /// <summary>
    /// Batch similar draw calls to reduce CPU overhead
    /// </summary>
    public static List<DrawBatch> BatchDrawCalls(List<RenderObject> objects)
    {
        var batches = new Dictionary<BatchKey, DrawBatch>();
        
        foreach (var obj in objects)
        {
            var key = new BatchKey
            {
                MaterialId = obj.MaterialId,
                MeshId = obj.MeshId,
                LOD = obj.LOD
            };
            
            if (!batches.ContainsKey(key))
            {
                batches[key] = new DrawBatch
                {
                    MaterialId = obj.MaterialId,
                    MeshId = obj.MeshId,
                    LOD = obj.LOD,
                    Instances = new List<Matrix4x4>()
                };
            }
            
            batches[key].Instances.Add(obj.Transform);
        }
        
        return new List<DrawBatch>(batches.Values);
    }
    
    /// <summary>
    /// Checkerboard rendering - render expensive effects at half resolution in checkerboard pattern
    /// Reconstruct full image using temporal data
    /// </summary>
    public static bool ShouldRenderPixel(int x, int y, int frameIndex)
    {
        // Alternate checkerboard pattern each frame
        return ((x + y + frameIndex) & 1) == 0;
    }
    
    /// <summary>
    /// Temporal amortization - spread expensive work across multiple frames
    /// </summary>
    public static bool ShouldUpdateThisFrame(int objectId, int frameIndex, int updateFrequency)
    {
        return (objectId + frameIndex) % updateFrequency == 0;
    }
    
    /// <summary>
    /// Distance-based update frequency - update distant objects less often
    /// </summary>
    public static int GetUpdateFrequency(float distance)
    {
        if (distance < 20.0f) return 1;  // Every frame
        if (distance < 50.0f) return 2;  // Every 2 frames
        if (distance < 100.0f) return 4; // Every 4 frames
        return 8; // Every 8 frames
    }
}

/// <summary>
/// Render object for culling and batching
/// </summary>
public struct RenderObject
{
    public Guid MaterialId;
    public Guid MeshId;
    public int LOD;
    public Matrix4x4 Transform;
    public Vector3 Position;
    public float BoundingRadius;
}

/// <summary>
/// Batch key for grouping similar draw calls
/// </summary>
public struct BatchKey : IEquatable<BatchKey>
{
    public Guid MaterialId;
    public Guid MeshId;
    public int LOD;
    
    public bool Equals(BatchKey other)
    {
        return MaterialId == other.MaterialId &&
               MeshId == other.MeshId &&
               LOD == other.LOD;
    }
    
    public override bool Equals(object? obj) => obj is BatchKey key && Equals(key);
    public override int GetHashCode() => HashCode.Combine(MaterialId, MeshId, LOD);
}

/// <summary>
/// Draw batch for instanced rendering
/// </summary>
public class DrawBatch
{
    public Guid MaterialId;
    public Guid MeshId;
    public int LOD;
    public List<Matrix4x4> Instances = new();
}

/// <summary>
/// Smart culling system that combines multiple culling techniques
/// 
/// LEGACY CPU PATH: This is the fallback for DX11 Feature Level 10.x
/// For modern hardware (DX11 FL 11.0+), use GPUDrivenCullingSystem instead!
/// 
/// GPU-Driven Culling Performance:
/// - CPU Path (this): ~5-10ms for 100K objects
/// - GPU Path (GPUDrivenCullingSystem): <1ms for 1M objects
/// 
/// The GPU path moves ALL culling to compute shaders with zero CPU overhead.
/// </summary>
public class SmartCullingSystem
{
    private readonly AdaptiveQualitySystem _qualitySystem;
    private IRHITexture? _hiZBuffer;
    
    public SmartCullingSystem(AdaptiveQualitySystem qualitySystem)
    {
        _qualitySystem = qualitySystem;
    }
    
    /// <summary>
    /// Cull objects and return visible list
    /// </summary>
    public List<RenderObject> CullObjects(List<RenderObject> allObjects, 
                                         Vector3 cameraPos, 
                                         Matrix4x4 viewProj,
                                         float screenHeight,
                                         float fov)
    {
        var visible = new List<RenderObject>();
        
        float drawDistance = _qualitySystem.GetDrawDistance();
        float cullThreshold = _qualitySystem.GetSmallObjectCullThreshold();
        float lodBias = _qualitySystem.GetLODBias();
        
        foreach (var obj in allObjects)
        {
            // Distance culling
            float distance = Vector3.Distance(obj.Position, cameraPos);
            if (distance > drawDistance)
                continue;
            
            // Frustum culling
            if (!AggressiveOptimizations.IsInFrustum(obj.Position, obj.BoundingRadius, viewProj))
                continue;
            
            // Small object culling
            if (AggressiveOptimizations.IsTooSmall(obj.Position, obj.BoundingRadius, 
                                                   cameraPos, screenHeight, fov, cullThreshold))
                continue;
            
            // Occlusion culling (if Hi-Z buffer available)
            if (_hiZBuffer != null && 
                AggressiveOptimizations.IsOccluded(obj.Position, obj.BoundingRadius, _hiZBuffer))
                continue;
            
            visible.Add(obj);
        }
        
        return visible;
    }
    
    /// <summary>
    /// Generate Hi-Z buffer for occlusion culling
    /// </summary>
    public void GenerateHiZBuffer(IRHICommandBuffer cmd, IRHITexture depthBuffer)
    {
        // TODO: Generate mip chain of depth buffer
        // Each mip level stores max depth of 2x2 region from previous level
        // Allows fast conservative occlusion tests
    }
}

/// <summary>
/// Fake detail system - use shaders to fake expensive geometry
/// </summary>
public static class FakeDetailSystem
{
    /// <summary>
    /// Parallax occlusion mapping - fake depth using height maps
    /// Looks like real geometry but it's just a shader trick
    /// </summary>
    public static Vector2 ParallaxMapping(Vector2 uv, Vector3 viewDir, float heightScale, 
                                         Func<Vector2, float> heightSampler)
    {
        // Ray march through height field
        const int numSteps = 8;
        float stepSize = 1.0f / numSteps;
        
        Vector2 uvOffset = viewDir.XY() * heightScale / numSteps;
        Vector2 currentUV = uv;
        float currentHeight = 1.0f;
        
        for (int i = 0; i < numSteps; i++)
        {
            float sampledHeight = heightSampler(currentUV);
            
            if (currentHeight < sampledHeight)
                break;
            
            currentUV -= uvOffset;
            currentHeight -= stepSize;
        }
        
        return currentUV;
    }
    
    /// <summary>
    /// Tessellation LOD - use tessellation on close objects, simple mesh on far objects
    /// </summary>
    public static float GetTessellationFactor(float distance)
    {
        if (distance < 10.0f) return 4.0f;  // High tessellation
        if (distance < 25.0f) return 2.0f;  // Medium tessellation
        if (distance < 50.0f) return 1.0f;  // Low tessellation
        return 0.0f; // No tessellation
    }
}

static class Vector3Extensions
{
    public static Vector2 XY(this Vector3 v) => new Vector2(v.X, v.Y);
}
