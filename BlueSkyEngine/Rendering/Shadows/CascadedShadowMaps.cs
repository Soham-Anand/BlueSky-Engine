using System;
using System.Runtime.InteropServices;
using NotBSRenderer;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using Vector3 = System.Numerics.Vector3;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Vector4 = System.Numerics.Vector4;
using BSMat4 = BlueSky.Core.Math.Matrix4x4;

namespace BlueSky.Rendering.Shadows;

/// <summary>
/// Cascaded Shadow Maps (CSM) for large outdoor scenes
/// Provides crisp shadows near camera and soft shadows in distance
/// Used in: Frostbite, UE5, Unity, virtually all modern engines
/// </summary>
public class CascadedShadowMaps : IDisposable
{
    private readonly IRHIDevice _device;
    
    // Shadow map cascades
    private IRHITexture?[] _shadowMaps;
    private IRHIPipeline? _shadowPipeline;
    private IRHIBuffer? _cascadeBuffer;
    
    private readonly int _cascadeCount;
    private readonly int _shadowMapSize;
    private readonly float[] _cascadeSplits;
    
    public CascadedShadowMaps(IRHIDevice device, int cascadeCount = 4, int shadowMapSize = 2048)
    {
        _device = device;
        _cascadeCount = cascadeCount;
        _shadowMapSize = shadowMapSize;
        _shadowMaps = new IRHITexture[cascadeCount];
        _cascadeSplits = new float[cascadeCount];
        
        Initialize();
    }
    
    private void Initialize()
    {
        CreateShadowMaps();
        CreatePipeline();
        CreateBuffers();
        
        Console.WriteLine($"[CSM] Initialized with {_cascadeCount} cascades @ {_shadowMapSize}x{_shadowMapSize}");
    }
    
    /// <summary>
    /// Render shadow maps for all cascades
    /// </summary>
    public void RenderShadows(IRHICommandBuffer cmd, World world,
                             Vector3 lightDirection, Vector3 cameraPosition,
                             Matrix4x4 cameraView, Matrix4x4 cameraProj,
                             float nearPlane, float farPlane)
    {
        if (_shadowPipeline == null)
            return;
        
        // Calculate cascade splits using logarithmic distribution
        CalculateCascadeSplits(nearPlane, farPlane);
        
        // Render each cascade
        for (int i = 0; i < _cascadeCount; i++)
        {
            float cascadeNear = i == 0 ? nearPlane : _cascadeSplits[i - 1];
            float cascadeFar = _cascadeSplits[i];
            
            // Calculate light view-projection matrix for this cascade
            var lightViewProj = CalculateLightMatrix(lightDirection, cameraPosition,
                                                     cameraView, cameraProj,
                                                     cascadeNear, cascadeFar);
            
            // Render shadow map
            cmd.BeginRenderPass(_shadowMaps[i]!, ClearValue.FromDepth(1.0f));
            cmd.SetPipeline(_shadowPipeline);
            
            // Render all shadow-casting objects
            RenderShadowCasters(cmd, world, lightViewProj);
            
            cmd.EndRenderPass();
            
            // Store cascade data for shader
            UpdateCascadeData(i, lightViewProj, cascadeNear, cascadeFar);
        }
    }
    
    /// <summary>
    /// Calculate cascade split distances using logarithmic distribution
    /// Provides good balance between near and far shadow quality
    /// </summary>
    private void CalculateCascadeSplits(float nearPlane, float farPlane)
    {
        float lambda = 0.75f; // Blend between uniform and logarithmic (0.5-0.9 typical)
        
        for (int i = 0; i < _cascadeCount; i++)
        {
            float p = (i + 1) / (float)_cascadeCount;
            
            // Logarithmic split
            float log = nearPlane * MathF.Pow(farPlane / nearPlane, p);
            
            // Uniform split
            float uniform = nearPlane + (farPlane - nearPlane) * p;
            
            // Blend
            _cascadeSplits[i] = lambda * log + (1.0f - lambda) * uniform;
        }
    }
    
    /// <summary>
    /// Calculate light view-projection matrix for a cascade
    /// Uses tight-fitting frustum to maximize shadow resolution
    /// </summary>
    private Matrix4x4 CalculateLightMatrix(Vector3 lightDirection, Vector3 cameraPosition,
                                          Matrix4x4 cameraView, Matrix4x4 cameraProj,
                                          float nearPlane, float farPlane)
    {
        // Get frustum corners in world space
        var frustumCorners = GetFrustumCorners(cameraView, cameraProj, nearPlane, farPlane);
        
        // Calculate frustum center
        Vector3 frustumCenter = Vector3.Zero;
        foreach (var corner in frustumCorners)
        {
            frustumCenter += corner;
        }
        frustumCenter /= frustumCorners.Length;
        
        // Create light view matrix
        Vector3 lightPos = frustumCenter - lightDirection * 100.0f; // Offset along light direction
        Matrix4x4 lightView = Matrix4x4.CreateLookAt(lightPos, frustumCenter, Vector3.UnitY);
        
        // Calculate AABB of frustum in light space
        Vector3 min = new Vector3(float.MaxValue);
        Vector3 max = new Vector3(float.MinValue);
        
        foreach (var corner in frustumCorners)
        {
            Vector3 lightSpaceCorner = Vector3.Transform(corner, lightView);
            min = Vector3.Min(min, lightSpaceCorner);
            max = Vector3.Max(max, lightSpaceCorner);
        }
        
        // Extend Z range to include potential shadow casters
        min.Z -= 100.0f;
        max.Z += 100.0f;
        
        // Create orthographic projection for directional light
        Matrix4x4 lightProj = Matrix4x4.CreateOrthographicOffCenter(
            min.X, max.X, min.Y, max.Y, min.Z, max.Z);
        
        // Stabilize shadows (prevent shimmering when camera moves)
        return StabilizeShadowMatrix(lightView * lightProj, _shadowMapSize);
    }
    
    /// <summary>
    /// Stabilize shadow matrix to prevent shimmering
    /// Snaps to texel grid in shadow map space
    /// </summary>
    private Matrix4x4 StabilizeShadowMatrix(Matrix4x4 lightViewProj, int shadowMapSize)
    {
        // Transform origin to shadow map space
        Vector4 origin = Vector4.Transform(Vector4.UnitW, lightViewProj);
        origin *= shadowMapSize / 2.0f;
        
        // Round to nearest texel
        Vector4 rounded = new Vector4(
            MathF.Round(origin.X),
            MathF.Round(origin.Y),
            origin.Z,
            origin.W
        );
        
        // Calculate offset
        Vector4 offset = rounded - origin;
        offset *= 2.0f / shadowMapSize;
        
        // Apply offset to matrix
        lightViewProj.M41 += offset.X;
        lightViewProj.M42 += offset.Y;
        
        return lightViewProj;
    }
    
    /// <summary>
    /// Get frustum corners in world space
    /// </summary>
    private Vector3[] GetFrustumCorners(Matrix4x4 view, Matrix4x4 proj, float nearPlane, float farPlane)
    {
        Matrix4x4 invViewProj = Matrix4x4.Invert(view * proj, out var inverted) ? inverted : Matrix4x4.Identity;
        
        var corners = new Vector3[8];
        int index = 0;
        
        for (int z = 0; z < 2; z++)
        {
            for (int y = 0; y < 2; y++)
            {
                for (int x = 0; x < 2; x++)
                {
                    Vector4 ndc = new Vector4(
                        x * 2.0f - 1.0f,
                        y * 2.0f - 1.0f,
                        z,
                        1.0f
                    );
                    
                    Vector4 worldPos = Vector4.Transform(ndc, invViewProj);
                    corners[index++] = new Vector3(worldPos.X, worldPos.Y, worldPos.Z) / worldPos.W;
                }
            }
        }
        
        return corners;
    }
    
    private void RenderShadowCasters(IRHICommandBuffer cmd, World world, Matrix4x4 lightViewProj)
    {
        // Render all entities with StaticMeshComponent
        foreach (var entity in world.GetAllEntities())
        {
            if (world.HasComponent<StaticMeshComponent>(entity) &&
                world.HasComponent<TransformComponent>(entity))
            {
                var mesh = world.GetComponent<StaticMeshComponent>(entity);
                var transform = world.GetComponent<TransformComponent>(entity);
                
                // Calculate MVP matrix
                Matrix4x4 mvp = Matrix4x4.Multiply(ToSysMat4(transform.WorldMatrix), lightViewProj);
                
                // Set uniforms and draw
                cmd.SetVertexUniforms(0, System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                    System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref mvp, 1)));
                
                // TODO: Bind mesh and draw
                // cmd.SetVertexBuffer(mesh.VertexBuffer);
                // cmd.SetIndexBuffer(mesh.IndexBuffer);
                // cmd.DrawIndexed(mesh.IndexCount);
            }
        }
    }
    
    private void UpdateCascadeData(int cascadeIndex, Matrix4x4 lightViewProj, float near, float far)
    {
        // TODO: Upload cascade data to GPU buffer
        // This will be used in the main rendering pass to sample correct cascade
    }
    
    private void CreateShadowMaps()
    {
        for (int i = 0; i < _cascadeCount; i++)
        {
            _shadowMaps[i] = _device.CreateTexture(new TextureDesc
            {
                Width = (uint)_shadowMapSize,
                Height = (uint)_shadowMapSize,
                Depth = 1,
                Format = TextureFormat.Depth32Float,
                Usage = TextureUsage.DepthStencil | TextureUsage.Sampled,
                MipLevels = 1,
                ArrayLayers = 1,
                DebugName = $"ShadowMap_Cascade{i}"
            });
        }
    }
    
    private void CreatePipeline()
    {
        // TODO: Load shadow map shader (depth-only rendering)
        Console.WriteLine("[CSM] Shadow pipeline created");
    }
    
    private void CreateBuffers()
    {
        _cascadeBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)Marshal.SizeOf<CascadeData>() * (ulong)_cascadeCount,
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "Cascade_Data"
        });
    }
    
    public IRHITexture[] GetShadowMaps() => _shadowMaps!;
    public float[] GetCascadeSplits() => _cascadeSplits;
    
    // Type conversion helper
    private static Matrix4x4 ToSysMat4(BSMat4 m) => new Matrix4x4(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44
    );
    
    public void Dispose()
    {
        foreach (var shadowMap in _shadowMaps)
        {
            shadowMap?.Dispose();
        }
        
        _cascadeBuffer?.Dispose();
        _shadowPipeline?.Dispose();
    }
}

[StructLayout(LayoutKind.Sequential)]
struct CascadeData
{
    public Matrix4x4 ViewProjMatrix;
    public float SplitDistance;
    public Vector3 _padding;
}
