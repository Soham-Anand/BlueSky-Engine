# BlueSky RHI - Design Document

## Architecture

### Layer Structure
```
┌─────────────────────────────────────┐
│   BlueSky.Rendering (High-level)   │
├─────────────────────────────────────┤
│   BlueSky.RHI (Abstraction Layer)  │
├─────────────────────────────────────┤
│  Backend Implementations            │
│  - Metal    - DX11    - DX12        │
│  - Vulkan   - OpenGL                │
├─────────────────────────────────────┤
│   BlueSky.Platform (Windowing)     │
└─────────────────────────────────────┘
```

## Core Interfaces

### IRHIDevice
Main entry point for the RHI. Responsible for:
- Resource creation (buffers, textures, pipelines)
- Command buffer allocation
- Swapchain management
- Capability queries

### IRHICommandBuffer
Records rendering commands:
- Begin/End render pass
- Bind pipeline, resources
- Draw calls
- Compute dispatches
- Resource transitions

### IRHISwapchain
Manages presentation:
- Acquire next image
- Present
- Resize
- Format/mode selection

### IRHIBuffer
GPU buffer abstraction:
- Vertex buffers
- Index buffers
- Uniform/Constant buffers
- Storage buffers

### IRHITexture
Texture abstraction:
- 2D/3D/Cube textures
- Render targets
- Depth/stencil buffers
- Mipmaps

### IRHIPipeline
Pipeline state object:
- Shader stages
- Vertex layout
- Render states
- Resource bindings

## Resource Management

### Lifetime Management
- Explicit creation/destruction
- Reference counting for shared resources
- Deferred deletion (wait for GPU idle)

### Memory Allocation
- Per-backend allocators
- Pooling for small allocations
- Staging buffers for uploads

## Shader System

### Shader Compilation
- Source format: HLSL (cross-compile to others)
- Compile-time: Use DXC for HLSL → DXIL/SPIRV
- Runtime: Load pre-compiled binaries

### Shader Reflection
- Extract vertex inputs
- Extract resource bindings
- Generate binding layouts automatically

## Backend-Specific Details

### Metal Backend
- Use Metal-cpp for C++ interop
- P/Invoke from C# to C++ wrapper
- CAMetalLayer for swapchain
- MTLDevice, MTLCommandQueue, MTLRenderCommandEncoder

### DirectX 11 Backend
- Direct P/Invoke to d3d11.dll
- ID3D11Device, ID3D11DeviceContext
- DXGI for swapchain
- Simpler than DX12, good baseline

### DirectX 12 Backend
- Direct P/Invoke to d3d12.dll
- Explicit resource barriers
- Descriptor heaps
- Command lists and allocators

### Vulkan Backend
- P/Invoke to vulkan-1.dll/libvulkan.so
- VkInstance, VkDevice, VkQueue
- Explicit synchronization
- Most verbose but most control

### OpenGL Backend
- P/Invoke to OpenGL32.dll/libGL.so
- Context management via platform
- VAOs, VBOs, FBOs
- Compatibility profile for older hardware

## API Example

```csharp
// Device creation
var device = RHIDevice.Create(RHIBackend.Metal, window);

// Buffer creation
var vertexBuffer = device.CreateBuffer(new BufferDesc
{
    Size = vertices.Length * sizeof(float),
    Usage = BufferUsage.Vertex,
    MemoryType = MemoryType.GpuOnly
});

// Upload data
device.UploadBuffer(vertexBuffer, vertices);

// Pipeline creation
var pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDesc
{
    VertexShader = vertexShader,
    FragmentShader = fragmentShader,
    VertexLayout = vertexLayout,
    BlendState = BlendState.Opaque,
    DepthState = DepthState.Default,
    RasterizerState = RasterizerState.Default
});

// Rendering
var cmd = device.BeginCommandBuffer();
cmd.BeginRenderPass(swapchain.CurrentRenderTarget);
cmd.SetPipeline(pipeline);
cmd.SetVertexBuffer(vertexBuffer);
cmd.Draw(vertexCount, 0);
cmd.EndRenderPass();
device.Submit(cmd);
swapchain.Present();
```

## Performance Considerations

### Minimize State Changes
- Sort draw calls by pipeline
- Batch similar objects
- Use instancing where possible

### Reduce CPU Overhead
- Multi-threaded command recording
- Persistent mapped buffers
- Minimize validation in release builds

### GPU Optimization
- Use appropriate buffer types
- Minimize resource transitions
- Leverage hardware features (tile-based rendering on Metal)

## Testing Strategy

### Unit Tests
- Resource creation/destruction
- Command buffer recording
- State validation

### Integration Tests
- Full render pipeline
- Multi-frame rendering
- Resource updates

### Platform Tests
- Run on all target platforms
- Verify visual output matches
- Performance benchmarks

## Migration Path

### Phase 1: Metal Only
- Get Metal backend working
- Validate API design
- Test with existing renderer

### Phase 2: Add DX11
- Implement DX11 backend
- Refine abstraction based on differences
- Cross-platform testing

### Phase 3: Add Vulkan
- Most explicit API, will expose design issues
- Refine synchronization model
- Performance baseline

### Phase 4: Add DX12
- Similar to Vulkan
- Windows-specific optimizations
- DirectX Raytracing prep

### Phase 5: Add OpenGL
- Compatibility fallback
- Simpler implementation
- Legacy hardware support
