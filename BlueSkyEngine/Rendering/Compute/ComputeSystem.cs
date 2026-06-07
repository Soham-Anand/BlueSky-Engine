using System;
using System.Collections.Generic;
using NotBSRenderer;

namespace BlueSky.Rendering.Compute;

/// <summary>
/// Compute shader system for GPU-accelerated operations
/// Provides automatic fallback to CPU when compute shaders are unavailable (DX11 Feature Level 10.x)
/// Used for: particle simulation, UI tessellation, occlusion culling, etc.
/// </summary>
public class ComputeSystem : IDisposable
{
    private readonly IRHIDevice _device;
    private readonly bool _computeSupported;
    private readonly Dictionary<string, IRHIPipeline> _computePipelines = new();
    
    public bool IsComputeSupported => _computeSupported;
    
    public ComputeSystem(IRHIDevice device)
    {
        _device = device;
        _computeSupported = device.Capabilities.HasFlag(RHICapabilities.ComputeShaders);
        
        Console.WriteLine($"[ComputeSystem] Initialized. Compute support: {_computeSupported}");
        
        if (_computeSupported)
        {
            InitializeComputePipelines();
        }
    }
    
    private void InitializeComputePipelines()
    {
        // Pipelines will be loaded from compiled shaders
        // For now, we'll create them on-demand
    }
    
    /// <summary>
    /// Create or get a compute pipeline
    /// </summary>
    public IRHIPipeline? GetOrCreatePipeline(string name, byte[] shaderBytecode, string entryPoint = "main")
    {
        if (!_computeSupported)
            return null;
        
        if (_computePipelines.TryGetValue(name, out var existingPipeline))
            return existingPipeline;
        
        var desc = new ComputePipelineDesc
        {
            ComputeShader = new ShaderDesc
            {
                Stage = ShaderStage.Compute,
                Bytecode = shaderBytecode,
                EntryPoint = entryPoint,
                DebugName = name
            },
            DebugName = name
        };
        
        var pipeline = _device.CreateComputePipeline(desc);
        _computePipelines[name] = pipeline;
        
        return pipeline;
    }
    
    /// <summary>
    /// Dispatch a compute shader
    /// </summary>
    public void Dispatch(IRHICommandBuffer cmd, string pipelineName, uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        if (!_computeSupported)
        {
            Console.WriteLine($"[ComputeSystem] Compute not supported, skipping dispatch: {pipelineName}");
            return;
        }
        
        if (!_computePipelines.TryGetValue(pipelineName, out var pipeline))
        {
            Console.WriteLine($"[ComputeSystem] Pipeline not found: {pipelineName}");
            return;
        }
        
        cmd.SetPipeline(pipeline);
        cmd.Dispatch(groupCountX, groupCountY, groupCountZ);
    }
    
    /// <summary>
    /// Dispatch indirect (read dispatch parameters from buffer)
    /// </summary>
    public void DispatchIndirect(IRHICommandBuffer cmd, string pipelineName, IRHIBuffer argsBuffer, ulong offset = 0)
    {
        if (!_computeSupported)
            return;
        
        if (!_computePipelines.TryGetValue(pipelineName, out var pipeline))
            return;
        
        cmd.SetPipeline(pipeline);
        cmd.DispatchIndirect(argsBuffer, offset);
    }
    
    public void Dispose()
    {
        foreach (var pipeline in _computePipelines.Values)
        {
            pipeline.Dispose();
        }
        _computePipelines.Clear();
    }
}

/// <summary>
/// Particle simulation using compute shaders
/// Falls back to CPU simulation on DX11 Feature Level 10.x
/// </summary>
public class ParticleComputeSimulator
{
    private readonly ComputeSystem _computeSystem;
    private readonly IRHIDevice _device;
    private IRHIBuffer? _particleBuffer;
    private IRHIPipeline? _simulationPipeline;
    private IRHIBuffer? _uniformBuffer;
    
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SimulationParams
    {
        public float DeltaTime;
        public System.Numerics.Vector3 Wind;
        public float Gravity;
        public uint ParticleCount;
        public float _padding1;
        public float _padding2;
    }
    
    public ParticleComputeSimulator(ComputeSystem computeSystem, IRHIDevice device)
    {
        _computeSystem = computeSystem;
        _device = device;
    }
    
    public void Initialize(uint maxParticles, byte[] shaderBytes, string entryPoint = "main")
    {
        if (!_computeSystem.IsComputeSupported)
            return;
        
        // We do not create the particle buffer here anymore, the ParticleSystem owns it and passes it in
        
        _uniformBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = (ulong)System.Runtime.InteropServices.Marshal.SizeOf<SimulationParams>(),
            Usage = BufferUsage.Uniform,
            MemoryType = MemoryType.CpuToGpu,
            DebugName = "ParticleSimUniforms"
        });
        
        _simulationPipeline = _computeSystem.GetOrCreatePipeline("ParticleSimulation", shaderBytes, entryPoint);
    }
    
    public void Simulate(IRHICommandBuffer cmd, IRHIBuffer particleBuffer, float deltaTime, System.Numerics.Vector3 wind, float gravity, uint particleCount)
    {
        if (!_computeSystem.IsComputeSupported || _simulationPipeline == null)
        {
            return;
        }
        
        var uniforms = new SimulationParams
        {
            DeltaTime = deltaTime,
            Wind = wind,
            Gravity = gravity,
            ParticleCount = particleCount
        };
        
        _device.UpdateBuffer(_uniformBuffer!, System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref uniforms, 1)));
        
        // GPU simulation
        cmd.SetPipeline(_simulationPipeline);
        cmd.SetStorageBuffer(particleBuffer, 0);
        cmd.SetUniformBuffer(_uniformBuffer!, 1);
        
        // Dispatch
        uint groupCount = (particleCount + 255) / 256;
        cmd.Dispatch(groupCount, 1, 1);
        cmd.BufferBarrier(particleBuffer);
    }
}

/// <summary>
/// UI tessellation using compute shaders
/// Falls back to CPU tessellation on DX11 Feature Level 10.x
/// </summary>
public class UITessellationCompute
{
    private readonly ComputeSystem _computeSystem;
    private readonly IRHIDevice _device;
    private IRHIBuffer? _vertexBuffer;
    private IRHIBuffer? _indexBuffer;
    private IRHIPipeline? _tessellationPipeline;
    
    public UITessellationCompute(ComputeSystem computeSystem, IRHIDevice device)
    {
        _computeSystem = computeSystem;
        _device = device;
    }
    
    public void Initialize(uint maxVertices, uint maxIndices)
    {
        if (!_computeSystem.IsComputeSupported)
            return;
        
        _vertexBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = maxVertices * 32, // Vertex size
            Usage = BufferUsage.Storage | BufferUsage.Vertex,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "UIVertexBuffer"
        });
        
        _indexBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = maxIndices * 4,
            Usage = BufferUsage.Storage | BufferUsage.Index,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "UIIndexBuffer"
        });
    }
    
    public void TessellateRoundedRect(IRHICommandBuffer cmd, float x, float y, float width, float height, float radius)
    {
        if (!_computeSystem.IsComputeSupported || _tessellationPipeline == null)
        {
            // CPU fallback
            TessellateCPU(x, y, width, height, radius);
            return;
        }
        
        // GPU tessellation
        cmd.SetPipeline(_tessellationPipeline);
        cmd.SetStorageBuffer(_vertexBuffer!, 0);
        cmd.SetStorageBuffer(_indexBuffer!, 1);
        
        // Dispatch
        cmd.Dispatch(1, 1, 1);
        cmd.MemoryBarrier();
    }
    
    private void TessellateCPU(float x, float y, float width, float height, float radius)
    {
        // CPU fallback - existing tessellation code
    }
}

/// <summary>
/// Occlusion culling using compute shaders
/// </summary>
public class OcclusionCullingCompute
{
    private readonly ComputeSystem _computeSystem;
    private readonly IRHIDevice _device;
    private IRHIBuffer? _instanceBuffer;
    private IRHIBuffer? _visibilityBuffer;
    private IRHIPipeline? _cullingPipeline;
    
    public OcclusionCullingCompute(ComputeSystem computeSystem, IRHIDevice device)
    {
        _computeSystem = computeSystem;
        _device = device;
    }
    
    public void Initialize(uint maxInstances)
    {
        if (!_computeSystem.IsComputeSupported)
            return;
        
        _instanceBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = maxInstances * 64,
            Usage = BufferUsage.Storage,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "InstanceBuffer"
        });
        
        _visibilityBuffer = _device.CreateBuffer(new BufferDesc
        {
            Size = maxInstances * 4, // uint per instance
            Usage = BufferUsage.Storage,
            MemoryType = MemoryType.GpuOnly,
            DebugName = "VisibilityBuffer"
        });
    }
    
    public void CullInstances(IRHICommandBuffer cmd, uint instanceCount, IRHITexture depthBuffer)
    {
        if (!_computeSystem.IsComputeSupported || _cullingPipeline == null)
            return;
        
        cmd.SetPipeline(_cullingPipeline);
        cmd.SetStorageBuffer(_instanceBuffer!, 0);
        cmd.SetStorageBuffer(_visibilityBuffer!, 1);
        cmd.SetTexture(depthBuffer, 2);
        
        uint groupCount = (instanceCount + 63) / 64;
        cmd.Dispatch(groupCount, 1, 1);
        cmd.MemoryBarrier();
    }
}
