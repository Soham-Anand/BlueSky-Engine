// BlueSkyEngine - GPU-Driven Culling System
// 
// PHASE 2: THE NANITE COMPETITOR
// ================================
// This system moves ALL culling to the GPU using compute shaders and indirect draw commands.
// The CPU does ZERO work for culling - everything happens on the GPU in parallel.
//
// Architecture:
// 1. Upload all instance data (transforms, bounds) to GPU buffer
// 2. Compute shader performs frustum + distance + occlusion culling
// 3. Compute shader writes surviving instances to IndirectDrawArguments buffer
// 4. Single DrawIndexedIndirect() call renders ALL visible objects
//
// Performance Target:
// - 1 million objects culled in <1ms on GPU
// - Zero CPU overhead for culling
// - Scales to Nanite-level triangle counts
//
// Hardware Requirements:
// - DX11 Feature Level 11.0+ (Compute Shaders + Indirect Drawing)
// - DX12/Vulkan/Metal (Optimal with Bindless Resources)
//
// Comparison with UE5 Nanite:
// ┌─────────────────────────┬──────────────────┬──────────────────┐
// │ Feature                 │ BlueSky GPU      │ UE5 Nanite       │
// ├─────────────────────────┼──────────────────┼──────────────────┤
// │ GPU-Driven Culling      │ ✓ Yes            │ ✓ Yes            │
// │ Indirect Draw Commands  │ ✓ Yes            │ ✓ Yes            │
// │ Compute-Based Culling   │ ✓ Yes            │ ✓ Yes            │
// │ Occlusion Culling       │ ✓ Hi-Z Buffer    │ ✓ Two-Pass       │
// │ LOD Selection           │ ✓ Distance-Based │ ✓ Cluster-Based  │
// │ Virtual Geometry        │ ✗ Not Yet        │ ✓ Yes            │
// │ Mesh Shaders            │ ✗ Not Yet        │ ✓ Yes (Optional) │
// │ Min Hardware            │ DX11 FL 11.0     │ DX12 / SM 6.0    │
// └─────────────────────────┴──────────────────┴──────────────────┘
//
// Next Steps (Phase 3+):
// - Virtual Geometry (streaming, paging)
// - Mesh Shaders (DX12/Vulkan only)
// - Cluster-Based LOD (like Nanite)
// - Software Rasterization for micro-triangles

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using NotBSRenderer;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;

namespace BlueSky.Rendering;

/// <summary>
/// GPU-Driven Culling System - Zero CPU overhead culling
/// Inspired by Nanite's GPU-driven pipeline
/// </summary>
public class GPUDrivenCullingSystem : IDisposable
{
    private readonly IRHIDevice _device;
    private readonly bool _gpuCullingSupported;
    
    // CPU fallback culler for when GPU compute isn't available
    private readonly CPUFrustumCuller _cpuCuller = new();
    
    // Actual screen dimensions
    private float _screenWidth = 1920;
    private float _screenHeight = 1080;
    private readonly CompatibleShaderLoader _shaderLoader;
    
    // GPU Buffers
    private IRHIBuffer? _instanceBuffer;           // All instance transforms + bounds
    private IRHIBuffer? _visibilityBuffer;         // Per-instance visibility flags (0 or 1)
    private IRHIBuffer? _indirectArgsBuffer;       // DrawIndexedIndirectCommand structs
    private IRHIBuffer? _drawCountBuffer;          // Atomic counter for visible instances
    private IRHIBuffer? _cullingParamsBuffer;      // Camera frustum, culling thresholds
    
    // Compute Pipelines
    private IRHIPipeline? _frustumCullingPipeline;
    private IRHIPipeline? _occlusionCullingPipeline;
    private IRHIPipeline? _compactArgsPipeline;
    
    // Bindless handles (if supported)
    private BindlessResourceHandle _instanceHandle;
    private BindlessResourceHandle _visibilityHandle;
    private BindlessResourceHandle _indirectArgsHandle;
    
    // Configuration
    private uint _maxInstances = 1_000_000;  // Support up to 1 million objects
    private uint _currentInstanceCount = 0;
    
    // Statistics
    public uint TotalInstances => _currentInstanceCount;
    public uint VisibleInstances { get; private set; }
    public float CullingTimeMs { get; private set; }
    
    public bool IsGPUCullingSupported => _gpuCullingSupported;
    
    public GPUDrivenCullingSystem(IRHIDevice device, uint maxInstances = 1_000_000)
    {
        _device = device;
        _maxInstances = maxInstances;
        _shaderLoader = new CompatibleShaderLoader(device);
        
        // GPU culling requires Compute Shaders + Indirect Drawing
        _gpuCullingSupported = device.Capabilities.HasFlag(RHICapabilities.ComputeShaders) &&
                               device.Capabilities.HasFlag(RHICapabilities.IndirectDrawing);
        
        if (!_gpuCullingSupported)
        {
            Console.WriteLine("[GPUDrivenCulling] GPU culling not supported. Falling back to CPU culling.");
            Console.WriteLine($"  Compute Shaders: {device.Capabilities.HasFlag(RHICapabilities.ComputeShaders)}");
            Console.WriteLine($"  Indirect Drawing: {device.Capabilities.HasFlag(RHICapabilities.IndirectDrawing)}");
            Console.WriteLine($"  Shader Model: {_shaderLoader.ShaderModel}");
            Console.WriteLine();
            Console.WriteLine("  Hardware Compatibility:");
            Console.WriteLine("    GPU Culling: Requires SM 5.0 (Intel HD 4000+, GTX 400+, Radeon HD 5000+)");
            Console.WriteLine("    Your Hardware: SM 4.0 or lower (i5-2410M, GeForce 8/9, Radeon HD 2000/3000)");
            Console.WriteLine("    Fallback: Optimized CPU culling will be used");
            return;
        }
        
        Console.WriteLine($"[GPUDrivenCulling] Initialized for {maxInstances:N0} instances");
        Console.WriteLine($"  Backend: {device.Backend}");
        Console.WriteLine($"  Shader Model: {_shaderLoader.ShaderModel}");
        Console.WriteLine($"  Bindless: {device.Capabilities.HasFlag(RHICapabilities.BindlessResources)}");
        
        InitializeBuffers();
        InitializeComputePipelines();
    }
    
    private void InitializeBuffers()
    {
        // Instance buffer: Transform + Bounding Sphere for each object
        _instanceBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = _maxInstances * (uint)Marshal.SizeOf<GPUInstance>(),
            Usage = BufferUsage.Storage | BufferUsage.TransferDst,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "GPUCulling_InstanceBuffer"
        });
        
        // Visibility buffer: 1 uint per instance (0 = culled, 1 = visible)
        _visibilityBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = _maxInstances * sizeof(uint),
            Usage = BufferUsage.Storage | BufferUsage.TransferDst,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "GPUCulling_VisibilityBuffer"
        });
        
        // Indirect args buffer: DrawIndexedIndirectCommand for each unique mesh
        // For now, assume max 10,000 unique meshes
        uint maxDrawCalls = 10_000;
        _indirectArgsBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = maxDrawCalls * (uint)Marshal.SizeOf<DrawIndexedIndirectCommand>(),
            Usage = BufferUsage.Indirect | BufferUsage.Storage | BufferUsage.TransferDst,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "GPUCulling_IndirectArgsBuffer"
        });
        
        // Draw count buffer: Atomic counter for visible instances
        _drawCountBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = sizeof(uint),
            Usage = BufferUsage.Storage | BufferUsage.TransferDst | BufferUsage.TransferSrc,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "GPUCulling_DrawCountBuffer"
        });
        
        // Culling params buffer: Camera frustum planes, culling thresholds
        _cullingParamsBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = (uint)Marshal.SizeOf<CullingParams>(),
            Usage = BufferUsage.Uniform | BufferUsage.TransferDst,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "GPUCulling_ParamsBuffer"
        });
        
        // Register bindless resources if supported
        if (_device.Capabilities.HasFlag(RHICapabilities.BindlessResources))
        {
            _instanceHandle = _device.RegisterBindlessBuffer(_instanceBuffer);
            _visibilityHandle = _device.RegisterBindlessBuffer(_visibilityBuffer);
            _indirectArgsHandle = _device.RegisterBindlessBuffer(_indirectArgsBuffer);
        }
    }
    
    private void InitializeComputePipelines()
    {
        // Load compute shaders using compatible shader loader
        // This automatically handles SM 5.0 requirement and fallback
        
        Console.WriteLine("[GPUDrivenCulling] Loading compute shaders...");
        
        // Frustum culling shader
        _frustumCullingPipeline = _shaderLoader.CreateComputePipeline("FrustumCulling", new ComputePipelineDesc
        {
            ComputeShader = new ShaderDesc
            {
                Stage = ShaderStage.Compute,
                EntryPoint = "main",
                DebugName = "FrustumCulling"
            },
            DebugName = "FrustumCullingPipeline"
        });
        
        if (_frustumCullingPipeline == null)
        {
            Console.WriteLine("[GPUDrivenCulling] WARNING: Failed to load FrustumCulling shader");
            Console.WriteLine("  This is expected on SM 4.0 hardware (i5-2410M, etc.)");
            Console.WriteLine("  CPU culling will be used as fallback");
        }
        
        // Occlusion culling shader
        _occlusionCullingPipeline = _shaderLoader.CreateComputePipeline("OcclusionCulling", new ComputePipelineDesc
        {
            ComputeShader = new ShaderDesc
            {
                Stage = ShaderStage.Compute,
                EntryPoint = "main",
                DebugName = "OcclusionCulling"
            },
            DebugName = "OcclusionCullingPipeline"
        });
        
        // Compact args shader
        _compactArgsPipeline = _shaderLoader.CreateComputePipeline("CompactIndirectArgs", new ComputePipelineDesc
        {
            ComputeShader = new ShaderDesc
            {
                Stage = ShaderStage.Compute,
                EntryPoint = "main",
                DebugName = "CompactIndirectArgs"
            },
            DebugName = "CompactArgsPipeline"
        });
        
        if (_frustumCullingPipeline != null)
        {
            Console.WriteLine("[GPUDrivenCulling] Compute shaders loaded successfully");
            Console.WriteLine($"  Shader Model: {_shaderLoader.ShaderModel}");
        }
    }
    
    /// <summary>
    /// Upload instance data to GPU
    /// Call once per frame with all renderable objects
    /// </summary>
    public void UploadInstances(ReadOnlySpan<GPUInstance> instances)
    {
        if (!_gpuCullingSupported || _instanceBuffer == null)
            return;
        
        _currentInstanceCount = (uint)Math.Min(instances.Length, (int)_maxInstances);
        
        if (_currentInstanceCount == 0)
            return;
        
        // Upload to GPU
        _device.UpdateBuffer(_instanceBuffer, MemoryMarshal.AsBytes(instances.Slice(0, (int)_currentInstanceCount)));
    }
    
    /// <summary>
    /// Perform GPU culling and generate indirect draw commands
    /// This is where the magic happens - zero CPU work!
    /// </summary>
    public void PerformGPUCulling(IRHICommandBuffer cmd, 
                                  Matrix4x4 viewMatrix, 
                                  Matrix4x4 projMatrix,
                                  Vector3 cameraPos,
                                  float nearPlane,
                                  float farPlane,
                                  float drawDistance,
                                  float smallObjectThreshold,
                                  IRHITexture? hiZBuffer = null)
    {
        if (!_gpuCullingSupported || _currentInstanceCount == 0)
            return;
        
        if (_frustumCullingPipeline == null)
        {
            Console.WriteLine("[GPUDrivenCulling] Compute pipelines not initialized. Skipping GPU culling.");
            return;
        }
        
        var startTime = DateTime.UtcNow;
        
        // Step 1: Clear visibility buffer and draw count
        ClearBuffers(cmd);
        
        // Step 2: Build culling parameters
        var cullingParams = BuildCullingParams(viewMatrix, projMatrix, cameraPos, 
                                              nearPlane, farPlane, drawDistance, smallObjectThreshold);
        _device.UpdateBuffer(_cullingParamsBuffer!, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref cullingParams, 1)));
        
        // Step 3: Frustum + Distance + Small Object Culling
        cmd.SetPipeline(_frustumCullingPipeline);
        cmd.SetUniformBuffer(_cullingParamsBuffer!, 0);
        cmd.SetStorageBuffer(_instanceBuffer!, 0);
        cmd.SetStorageBuffer(_visibilityBuffer!, 1);
        
        uint groupCount = (_currentInstanceCount + 63) / 64; // 64 threads per group
        cmd.Dispatch(groupCount, 1, 1);
        cmd.MemoryBarrier();
        
        // Step 4: Occlusion Culling (if Hi-Z buffer available)
        if (hiZBuffer != null && _occlusionCullingPipeline != null)
        {
            cmd.SetPipeline(_occlusionCullingPipeline);
            cmd.SetUniformBuffer(_cullingParamsBuffer!, 0);
            cmd.SetStorageBuffer(_instanceBuffer!, 0);
            cmd.SetStorageBuffer(_visibilityBuffer!, 1);
            cmd.SetTexture(hiZBuffer, 2);
            
            cmd.Dispatch(groupCount, 1, 1);
            cmd.MemoryBarrier();
        }
        
        // Step 5: Compact visible instances into indirect draw commands
        if (_compactArgsPipeline != null)
        {
            cmd.SetPipeline(_compactArgsPipeline);
            cmd.SetStorageBuffer(_instanceBuffer!, 0);
            cmd.SetStorageBuffer(_visibilityBuffer!, 1);
            cmd.SetStorageBuffer(_indirectArgsBuffer!, 2);
            cmd.SetStorageBuffer(_drawCountBuffer!, 3);
            
            cmd.Dispatch(groupCount, 1, 1);
            cmd.MemoryBarrier();
        }
        
        CullingTimeMs = (float)(DateTime.UtcNow - startTime).TotalMilliseconds;
        
        // Read back draw count for statistics via CPU fallback
        // GPU readback is async and may not be available on all platforms
        try
        {
            // Use CPU culler as fallback for accurate stats
            _cpuCuller.SetScreenDimensions(_screenWidth, _screenHeight, 60.0f * MathF.PI / 180.0f);
            _cpuCuller.MaxDrawDistance = drawDistance;
            _cpuCuller.UpdateFrustum(viewMatrix * projMatrix);
            
            var spheres = new CPUFrustumCuller.BoundingSphere[_currentInstanceCount];
            // TODO: populate from instance buffer when readback is available
            VisibleInstances = _currentInstanceCount; // Fallback until readback works
        }
        catch
        {
            VisibleInstances = _currentInstanceCount;
        }
    }
    
    /// <summary>
    /// Issue indirect draw call for all visible instances
    /// Single draw call renders everything!
    /// </summary>
    public void DrawIndirect(IRHICommandBuffer cmd, IRHIBuffer meshIndexBuffer, uint indexCount)
    {
        if (!_gpuCullingSupported || _indirectArgsBuffer == null)
            return;
        
        if (_currentInstanceCount == 0)
            return;
        
        // Bind mesh index buffer
        cmd.SetIndexBuffer(meshIndexBuffer, IndexType.UInt32, 0);
        
        // Issue indirect draw
        // This reads draw commands from _indirectArgsBuffer generated by GPU
        cmd.DrawIndexedIndirect(_indirectArgsBuffer, 0, 1, (uint)Marshal.SizeOf<DrawIndexedIndirectCommand>());
    }
    
    /// <summary>
    /// Get instance buffer for shader binding
    /// </summary>
    public IRHIBuffer? GetInstanceBuffer() => _instanceBuffer;
    
    /// <summary>
    /// Get visibility buffer for shader binding
    /// </summary>
    public IRHIBuffer? GetVisibilityBuffer() => _visibilityBuffer;
    
    private void ClearBuffers(IRHICommandBuffer cmd)
    {
        // Clear visibility buffer to 0 (all culled)
        if (_visibilityBuffer != null)
        {
            var clearData = new byte[_currentInstanceCount * 4]; // All zeros = all culled
            _device.UpdateBuffer(_visibilityBuffer, clearData);
        }
        
        // Clear draw count to 0
        if (_drawCountBuffer != null)
        {
            var zeroCount = new byte[4]; // uint32 = 0
            _device.UpdateBuffer(_drawCountBuffer, zeroCount);
        }
        
        // Clear indirect args buffer
        if (_indirectArgsBuffer != null)
        {
            var clearArgs = new byte[Marshal.SizeOf<DrawIndexedIndirectCommand>()];
            _device.UpdateBuffer(_indirectArgsBuffer, clearArgs);
        }
    }
    
    private CullingParams BuildCullingParams(Matrix4x4 viewMatrix, Matrix4x4 projMatrix, Vector3 cameraPos,
                                            float nearPlane, float farPlane, float drawDistance, float smallObjectThreshold)
    {
        // Extract frustum planes from view-projection matrix
        Matrix4x4 viewProj = viewMatrix * projMatrix;
        
        return new CullingParams
        {
            ViewMatrix = viewMatrix,
            ProjMatrix = projMatrix,
            ViewProjMatrix = viewProj,
            CameraPosition = new Vector4(cameraPos, 1.0f),
            FrustumPlanes = ExtractFrustumPlanes(viewProj),
            NearPlane = nearPlane,
            FarPlane = farPlane,
            DrawDistance = drawDistance,
            SmallObjectThreshold = smallObjectThreshold,
            ScreenWidth = (uint)_screenWidth,
            ScreenHeight = (uint)_screenHeight
        };
    }
    
    /// <summary>
    /// Update screen dimensions for culling calculations.
    /// Call this when the window/viewport resizes.
    /// </summary>
    public void SetScreenDimensions(float width, float height)
    {
        _screenWidth = Math.Max(1, width);
        _screenHeight = Math.Max(1, height);
    }
    
    /// <summary>
    /// Get CPU fallback culler for use when GPU culling isn't available.
    /// </summary>
    public CPUFrustumCuller GetCPUFallbackCuller() => _cpuCuller;
    
    private FrustumPlanes ExtractFrustumPlanes(Matrix4x4 viewProj)
    {
        // Extract 6 frustum planes from view-projection matrix
        // Plane equation: Ax + By + Cz + D = 0
        
        return new FrustumPlanes
        {
            Left   = NormalizePlane(new Vector4(viewProj.M14 + viewProj.M11, viewProj.M24 + viewProj.M21, viewProj.M34 + viewProj.M31, viewProj.M44 + viewProj.M41)),
            Right  = NormalizePlane(new Vector4(viewProj.M14 - viewProj.M11, viewProj.M24 - viewProj.M21, viewProj.M34 - viewProj.M31, viewProj.M44 - viewProj.M41)),
            Bottom = NormalizePlane(new Vector4(viewProj.M14 + viewProj.M12, viewProj.M24 + viewProj.M22, viewProj.M34 + viewProj.M32, viewProj.M44 + viewProj.M42)),
            Top    = NormalizePlane(new Vector4(viewProj.M14 - viewProj.M12, viewProj.M24 - viewProj.M22, viewProj.M34 - viewProj.M32, viewProj.M44 - viewProj.M42)),
            Near   = NormalizePlane(new Vector4(viewProj.M14 + viewProj.M13, viewProj.M24 + viewProj.M23, viewProj.M34 + viewProj.M33, viewProj.M44 + viewProj.M43)),
            Far    = NormalizePlane(new Vector4(viewProj.M14 - viewProj.M13, viewProj.M24 - viewProj.M23, viewProj.M34 - viewProj.M33, viewProj.M44 - viewProj.M43))
        };
    }
    
    private Vector4 NormalizePlane(Vector4 plane)
    {
        float length = MathF.Sqrt(plane.X * plane.X + plane.Y * plane.Y + plane.Z * plane.Z);
        return plane / length;
    }
    
    public void Dispose()
    {
        if (_device.Capabilities.HasFlag(RHICapabilities.BindlessResources))
        {
            if (_instanceHandle.Index != 0) _device.UnregisterBindlessResource(_instanceHandle);
            if (_visibilityHandle.Index != 0) _device.UnregisterBindlessResource(_visibilityHandle);
            if (_indirectArgsHandle.Index != 0) _device.UnregisterBindlessResource(_indirectArgsHandle);
        }
        
        _instanceBuffer?.Dispose();
        _visibilityBuffer?.Dispose();
        _indirectArgsBuffer?.Dispose();
        _drawCountBuffer?.Dispose();
        _cullingParamsBuffer?.Dispose();
        
        _frustumCullingPipeline?.Dispose();
        _occlusionCullingPipeline?.Dispose();
        _compactArgsPipeline?.Dispose();
    }
}

/// <summary>
/// GPU instance data - uploaded to GPU once per frame
/// Tightly packed for cache efficiency
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct GPUInstance
{
    public Matrix4x4 Transform;        // 64 bytes - World transform matrix
    public Vector4 BoundingSphere;     // 16 bytes - xyz = center, w = radius
    public uint MeshId;                // 4 bytes - Index into mesh table
    public uint MaterialId;            // 4 bytes - Index into material table
    public uint LODLevel;              // 4 bytes - Current LOD level
    public uint Flags;                 // 4 bytes - Instance flags (e.g., cast shadows)
    
    // Total: 96 bytes per instance
    // 1 million instances = 96 MB GPU memory
}

/// <summary>
/// Culling parameters - uploaded to GPU each frame
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct CullingParams
{
    public Matrix4x4 ViewMatrix;
    public Matrix4x4 ProjMatrix;
    public Matrix4x4 ViewProjMatrix;
    public Vector4 CameraPosition;
    public FrustumPlanes FrustumPlanes;
    public float NearPlane;
    public float FarPlane;
    public float DrawDistance;
    public float SmallObjectThreshold;
    public uint ScreenWidth;
    public uint ScreenHeight;
    public uint _padding1;
    public uint _padding2;
}

/// <summary>
/// Frustum planes for GPU culling
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct FrustumPlanes
{
    public Vector4 Left;
    public Vector4 Right;
    public Vector4 Bottom;
    public Vector4 Top;
    public Vector4 Near;
    public Vector4 Far;
}

/// <summary>
/// Indirect draw command structure
/// Matches D3D11_DRAW_INDEXED_INSTANCED_INDIRECT / VkDrawIndexedIndirectCommand
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DrawIndexedIndirectCommand
{
    public uint IndexCount;
    public uint InstanceCount;
    public uint FirstIndex;
    public int VertexOffset;
    public uint FirstInstance;
}
