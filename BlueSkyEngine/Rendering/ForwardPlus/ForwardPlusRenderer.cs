using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NotBSRenderer;
using BlueSky.Rendering.Lighting;

namespace BlueSky.Rendering.ForwardPlus;

/// <summary>
/// Forward+ (Clustered Forward) Renderer
/// Scales from DX11 Feature Level 10.0 (reduced clusters) to modern APIs (full clustered lighting)
/// Inspired by Frostbite 3's clustered shading and UE5's forward renderer
/// </summary>
public class ForwardPlusRenderer : IDisposable
{
    private readonly IRHIDevice _device;
    private readonly ClusterConfig _config;
    
    // Cluster buffers
    private IRHIBuffer? _clusterAABBBuffer;
    private IRHIBuffer? _lightIndexListBuffer;
    private IRHIBuffer? _lightGridBuffer;
    private IRHIBuffer? _lightDataBuffer;
    
    // Compute pipelines (only on modern APIs)
    private IRHIPipeline? _clusterBuildPipeline;
    private IRHIPipeline? _lightCullingPipeline;
    
    // Bindless handles (only on modern APIs)
    private BindlessResourceHandle _clusterAABBHandle;
    private BindlessResourceHandle _lightGridHandle;
    private BindlessResourceHandle _lightIndexHandle;
    
    private bool _useComputePath;
    private bool _useBindless;
    
    public ForwardPlusRenderer(IRHIDevice device, ClusterConfig? config = null)
    {
        _device = device;
        _config = config ?? ClusterConfig.Default;
        
        // Feature detection
        _useComputePath = _device.Capabilities.HasFlag(RHICapabilities.ComputeShaders);
        _useBindless = _device.Capabilities.HasFlag(RHICapabilities.BindlessResources);
        
        // Adjust cluster count for older hardware (DX11 Feature Level 10.x)
        if (!_useComputePath)
        {
            _config = ClusterConfig.LowEnd;
        }
        
        InitializeResources();
    }
    
    private void InitializeResources()
    {
        // Calculate cluster grid size
        uint totalClusters = _config.ClusterCountX * _config.ClusterCountY * _config.ClusterCountZ;
        
        // Cluster AABB buffer (min/max bounds for each cluster)
        _clusterAABBBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = totalClusters * (uint)Marshal.SizeOf<ClusterAABB>(),
            Usage = BufferUsage.Storage | BufferUsage.TransferDst,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "ClusterAABBBuffer"
        });
        
        // Light grid buffer (offset + count per cluster)
        _lightGridBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = totalClusters * (uint)Marshal.SizeOf<LightGrid>(),
            Usage = BufferUsage.Storage | BufferUsage.TransferDst,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "LightGridBuffer"
        });
        
        // Light index list (global list of light indices)
        uint maxLightIndices = totalClusters * _config.MaxLightsPerCluster;
        _lightIndexListBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = maxLightIndices * sizeof(uint),
            Usage = BufferUsage.Storage | BufferUsage.TransferDst,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "LightIndexListBuffer"
        });
        
        // Light data buffer (all light parameters)
        _lightDataBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = _config.MaxLights * (uint)Marshal.SizeOf<GPULight>(),
            Usage = BufferUsage.Storage | BufferUsage.TransferDst,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "LightDataBuffer"
        });
        
        // Register bindless resources if supported
        if (_useBindless)
        {
            _clusterAABBHandle = _device.RegisterBindlessBuffer(_clusterAABBBuffer);
            _lightGridHandle = _device.RegisterBindlessBuffer(_lightGridBuffer);
            _lightIndexHandle = _device.RegisterBindlessBuffer(_lightIndexListBuffer);
        }
        
        // Create compute pipelines if supported
        if (_useComputePath)
        {
            InitializeComputePipelines();
        }
    }
    
    private void InitializeComputePipelines()
    {
        // TODO: Load compiled compute shaders
        // For now, pipelines will be null and we'll use CPU fallback
    }
    
    /// <summary>
    /// Update light data and perform light culling
    /// </summary>
    public void UpdateLights(IRHICommandBuffer cmd, ReadOnlySpan<HorizonLight> lights, 
                            Matrix4x4 viewMatrix, Matrix4x4 projMatrix,
                            float nearPlane, float farPlane,
                            uint screenWidth, uint screenHeight)
    {
        // Upload light data to GPU
        UploadLightData(lights);
        
        if (_useComputePath)
        {
            // GPU-based light culling (modern path)
            PerformGPULightCulling(cmd, viewMatrix, projMatrix, nearPlane, farPlane, 
                                  screenWidth, screenHeight, lights.Length);
        }
        else
        {
            // CPU-based light culling (DX11 Feature Level 10.x fallback)
            PerformCPULightCulling(lights, viewMatrix, projMatrix, nearPlane, farPlane,
                                  screenWidth, screenHeight);
        }
    }
    
    private void UploadLightData(ReadOnlySpan<HorizonLight> lights)
    {
        int lightCount = Math.Min(lights.Length, (int)_config.MaxLights);
        
        // Convert to GPU format
        Span<GPULight> gpuLights = stackalloc GPULight[lightCount];
        for (int i = 0; i < lightCount; i++)
        {
            gpuLights[i] = ConvertToGPULight(lights[i]);
        }
        
        // Upload to GPU
        _device.UpdateBuffer(_lightDataBuffer!, MemoryMarshal.AsBytes(gpuLights));
    }
    
    private void PerformGPULightCulling(IRHICommandBuffer cmd, Matrix4x4 viewMatrix, Matrix4x4 projMatrix,
                                       float nearPlane, float farPlane, uint screenWidth, uint screenHeight,
                                       int lightCount)
    {
        // Step 1: Build cluster AABBs (only needs to be done when camera changes)
        if (_clusterBuildPipeline != null)
        {
            cmd.SetPipeline(_clusterBuildPipeline);
            
            // Set cluster build parameters
            Matrix4x4.Invert(projMatrix, out var invProjMatrix);
            var clusterParams = new ClusterBuildParams
            {
                ViewMatrix = viewMatrix,
                InvProjMatrix = invProjMatrix,
                ScreenWidth = screenWidth,
                ScreenHeight = screenHeight,
                NearPlane = nearPlane,
                FarPlane = farPlane,
                ClusterCountX = _config.ClusterCountX,
                ClusterCountY = _config.ClusterCountY,
                ClusterCountZ = _config.ClusterCountZ
            };
            
            cmd.SetComputeUniforms(0, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref clusterParams, 1)));
            cmd.SetStorageBuffer(_clusterAABBBuffer!, 0);
            
            // Dispatch cluster build
            uint groupsX = (_config.ClusterCountX + 7) / 8;
            uint groupsY = (_config.ClusterCountY + 7) / 8;
            uint groupsZ = (_config.ClusterCountZ + 7) / 8;
            cmd.Dispatch(groupsX, groupsY, groupsZ);
            
            cmd.BufferBarrier(_clusterAABBBuffer!);
        }
        
        // Step 2: Cull lights against clusters
        if (_lightCullingPipeline != null)
        {
            cmd.SetPipeline(_lightCullingPipeline);
            
            var cullParams = new LightCullParams
            {
                ViewMatrix = viewMatrix,
                LightCount = (uint)lightCount,
                MaxLightsPerCluster = _config.MaxLightsPerCluster
            };
            
            cmd.SetComputeUniforms(0, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref cullParams, 1)));
            cmd.SetStorageBuffer(_clusterAABBBuffer!, 0);
            cmd.SetStorageBuffer(_lightDataBuffer!, 1);
            cmd.SetStorageBuffer(_lightGridBuffer!, 2);
            cmd.SetStorageBuffer(_lightIndexListBuffer!, 3);
            
            // Dispatch light culling
            uint totalClusters = _config.ClusterCountX * _config.ClusterCountY * _config.ClusterCountZ;
            uint groups = (totalClusters + 63) / 64;
            cmd.Dispatch(groups, 1, 1);
            
            cmd.MemoryBarrier();
        }
    }
    
    private void PerformCPULightCulling(ReadOnlySpan<HorizonLight> lights, Matrix4x4 viewMatrix, Matrix4x4 projMatrix,
                                       float nearPlane, float farPlane, uint screenWidth, uint screenHeight)
    {
        // CPU fallback for DX11 Feature Level 10.x - simplified clustering
        // Build cluster AABBs
        var clusters = BuildClusterAABBs(viewMatrix, projMatrix, nearPlane, farPlane, screenWidth, screenHeight);
        
        // Cull lights against clusters
        var lightGrid = new LightGrid[_config.ClusterCountX * _config.ClusterCountY * _config.ClusterCountZ];
        var lightIndices = new System.Collections.Generic.List<uint>();
        
        for (int z = 0; z < _config.ClusterCountZ; z++)
        {
            for (int y = 0; y < _config.ClusterCountY; y++)
            {
                for (int x = 0; x < _config.ClusterCountX; x++)
                {
                    int clusterIdx = x + y * (int)_config.ClusterCountX + z * (int)_config.ClusterCountX * (int)_config.ClusterCountY;
                    var clusterAABB = clusters[clusterIdx];
                    
                    uint offset = (uint)lightIndices.Count;
                    uint count = 0;
                    
                    // Test each light against cluster AABB
                    for (int i = 0; i < lights.Length && count < _config.MaxLightsPerCluster; i++)
                    {
                        if (TestLightClusterIntersection(lights[i], clusterAABB, viewMatrix))
                        {
                            lightIndices.Add((uint)i);
                            count++;
                        }
                    }
                    
                    lightGrid[clusterIdx] = new LightGrid { Offset = offset, Count = count };
                }
            }
        }
        
        // Upload to GPU
        _device.UpdateBuffer(_lightGridBuffer!, MemoryMarshal.AsBytes<LightGrid>(lightGrid));
        _device.UpdateBuffer(_lightIndexListBuffer!, MemoryMarshal.AsBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(lightIndices)));
    }
    
    private ClusterAABB[] BuildClusterAABBs(Matrix4x4 viewMatrix, Matrix4x4 projMatrix,
                                           float nearPlane, float farPlane,
                                           uint screenWidth, uint screenHeight)
    {
        uint totalClusters = _config.ClusterCountX * _config.ClusterCountY * _config.ClusterCountZ;
        var clusters = new ClusterAABB[totalClusters];
        
        Matrix4x4.Invert(projMatrix, out var invProj);
        
        for (uint z = 0; z < _config.ClusterCountZ; z++)
        {
            for (uint y = 0; y < _config.ClusterCountY; y++)
            {
                for (uint x = 0; x < _config.ClusterCountX; x++)
                {
                    uint idx = x + y * _config.ClusterCountX + z * _config.ClusterCountX * _config.ClusterCountY;
                    
                    // Calculate cluster bounds in NDC space
                    float minX = (float)x / _config.ClusterCountX * 2.0f - 1.0f;
                    float maxX = (float)(x + 1) / _config.ClusterCountX * 2.0f - 1.0f;
                    float minY = (float)y / _config.ClusterCountY * 2.0f - 1.0f;
                    float maxY = (float)(y + 1) / _config.ClusterCountY * 2.0f - 1.0f;
                    
                    // Exponential depth slicing
                    float minZ = nearPlane * MathF.Pow(farPlane / nearPlane, (float)z / _config.ClusterCountZ);
                    float maxZ = nearPlane * MathF.Pow(farPlane / nearPlane, (float)(z + 1) / _config.ClusterCountZ);
                    
                    // Transform to view space
                    Vector3 minPoint = TransformNDCToView(new Vector3(minX, minY, minZ), invProj);
                    Vector3 maxPoint = TransformNDCToView(new Vector3(maxX, maxY, maxZ), invProj);
                    
                    clusters[idx] = new ClusterAABB
                    {
                        Min = Vector3.Min(minPoint, maxPoint),
                        Max = Vector3.Max(minPoint, maxPoint)
                    };
                }
            }
        }
        
        return clusters;
    }
    
    private Vector3 TransformNDCToView(Vector3 ndc, Matrix4x4 invProj)
    {
        Vector4 viewPos = Vector4.Transform(new Vector4(ndc, 1.0f), invProj);
        return new Vector3(viewPos.X, viewPos.Y, viewPos.Z) / viewPos.W;
    }
    
    private bool TestLightClusterIntersection(HorizonLight light, ClusterAABB cluster, Matrix4x4 viewMatrix)
    {
        // Transform light to view space
        Vector3 lightPosView = Vector3.Transform(light.Position, viewMatrix);
        
        switch (light.Type)
        {
            case LightType.Point:
                return TestSphereAABB(lightPosView, light.Range, cluster.Min, cluster.Max);
            
            case LightType.Spot:
                // Simplified: treat as sphere for now
                return TestSphereAABB(lightPosView, light.Range, cluster.Min, cluster.Max);
            
            case LightType.Directional:
                // Directional lights affect all clusters
                return true;
            
            default:
                return false;
        }
    }
    
    private bool TestSphereAABB(Vector3 center, float radius, Vector3 aabbMin, Vector3 aabbMax)
    {
        Vector3 closest = Vector3.Clamp(center, aabbMin, aabbMax);
        float distSq = Vector3.DistanceSquared(center, closest);
        return distSq <= radius * radius;
    }
    
    private GPULight ConvertToGPULight(HorizonLight light)
    {
        return new GPULight
        {
            PositionAndType = new Vector4(light.Position, (float)light.Type),
            DirectionAndRange = new Vector4(light.Direction, light.Range),
            ColorAndIntensity = new Vector4(light.Color, light.Intensity),
            SpotAngles = new Vector4(
                MathF.Cos(light.InnerAngle),
                MathF.Cos(light.OuterAngle),
                light.Attenuation,
                light.CastShadows ? 1.0f : 0.0f
            )
        };
    }
    
    /// <summary>
    /// Bind cluster data for rendering
    /// </summary>
    public void BindClusterData(IRHICommandBuffer cmd)
    {
        if (_useBindless)
        {
            // Bindless path - pass handles to shader
            Span<BindlessResourceHandle> handles = stackalloc BindlessResourceHandle[]
            {
                _clusterAABBHandle,
                _lightGridHandle,
                _lightIndexHandle
            };
            cmd.SetBindlessResourceTable(1, handles);
        }
        else
        {
            // Traditional binding
            cmd.SetStorageBuffer(_lightGridBuffer!, 0, 1);
            cmd.SetStorageBuffer(_lightIndexListBuffer!, 1, 1);
            cmd.SetStorageBuffer(_lightDataBuffer!, 2, 1);
        }
    }
    
    public void Dispose()
    {
        if (_useBindless)
        {
            _device.UnregisterBindlessResource(_clusterAABBHandle);
            _device.UnregisterBindlessResource(_lightGridHandle);
            _device.UnregisterBindlessResource(_lightIndexHandle);
        }
        
        _clusterAABBBuffer?.Dispose();
        _lightIndexListBuffer?.Dispose();
        _lightGridBuffer?.Dispose();
        _lightDataBuffer?.Dispose();
        _clusterBuildPipeline?.Dispose();
        _lightCullingPipeline?.Dispose();
    }
}

/// <summary>
/// Cluster configuration
/// </summary>
public struct ClusterConfig
{
    public uint ClusterCountX;
    public uint ClusterCountY;
    public uint ClusterCountZ;
    public uint MaxLightsPerCluster;
    public uint MaxLights;
    
    public static ClusterConfig Default => new()
    {
        ClusterCountX = 16,
        ClusterCountY = 9,
        ClusterCountZ = 24,
        MaxLightsPerCluster = 128,
        MaxLights = 1024
    };
    
    public static ClusterConfig LowEnd => new()
    {
        ClusterCountX = 8,
        ClusterCountY = 5,
        ClusterCountZ = 16,
        MaxLightsPerCluster = 32,
        MaxLights = 256
    };
    
    public static ClusterConfig HighEnd => new()
    {
        ClusterCountX = 32,
        ClusterCountY = 18,
        ClusterCountZ = 32,
        MaxLightsPerCluster = 256,
        MaxLights = 4096
    };
}

/// <summary>
/// GPU structures
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ClusterAABB
{
    public Vector3 Min;
    public float _padding1;
    public Vector3 Max;
    public float _padding2;
}

[StructLayout(LayoutKind.Sequential)]
public struct LightGrid
{
    public uint Offset;
    public uint Count;
}

[StructLayout(LayoutKind.Sequential)]
public struct GPULight
{
    public Vector4 PositionAndType;
    public Vector4 DirectionAndRange;
    public Vector4 ColorAndIntensity;
    public Vector4 SpotAngles;
}

[StructLayout(LayoutKind.Sequential)]
struct ClusterBuildParams
{
    public Matrix4x4 ViewMatrix;
    public Matrix4x4 InvProjMatrix;
    public uint ScreenWidth;
    public uint ScreenHeight;
    public float NearPlane;
    public float FarPlane;
    public uint ClusterCountX;
    public uint ClusterCountY;
    public uint ClusterCountZ;
    public uint _padding;
}

[StructLayout(LayoutKind.Sequential)]
struct LightCullParams
{
    public Matrix4x4 ViewMatrix;
    public uint LightCount;
    public uint MaxLightsPerCluster;
    public uint _padding1;
    public uint _padding2;
}
