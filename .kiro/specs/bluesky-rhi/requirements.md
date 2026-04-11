# BlueSky RHI - Requirements

## Overview
Build a custom Render Hardware Interface (RHI) abstraction layer that provides a unified API for multiple graphics backends while maintaining high performance and low overhead.

## Goals
1. Support multiple graphics APIs: Metal, DirectX 11, DirectX 12, Vulkan, OpenGL
2. Provide a clean, modern C# API
3. Zero-cost abstractions where possible
4. Platform-specific optimizations
5. Easy to extend and maintain

## Target Platforms & APIs
- **macOS**: Metal (primary), OpenGL (fallback)
- **Windows**: DirectX 11, DirectX 12, Vulkan, OpenGL
- **Linux**: Vulkan (primary), OpenGL (fallback)

## Core Features

### Device Management
- Device enumeration and selection
- Capability querying
- Adapter information

### Resource Management
- Buffers (vertex, index, uniform/constant)
- Textures (1D, 2D, 3D, Cube, Array)
- Samplers
- Resource binding and layouts

### Pipeline State
- Graphics pipelines (vertex/fragment shaders)
- Compute pipelines
- Render states (blend, depth, stencil, raster)
- Shader compilation and reflection

### Command Recording
- Command buffers/lists
- Draw calls (indexed, instanced, indirect)
- Compute dispatches
- Resource barriers and transitions

### Synchronization
- Fences
- Semaphores
- Frame pacing

### Swapchain Management
- Present modes (immediate, vsync, mailbox)
- Format selection
- Resize handling

## Non-Goals (Phase 1)
- Ray tracing
- Mesh shaders
- Variable rate shading
- Multi-GPU support
- Advanced compute features

## API Design Principles
1. **Explicit over implicit**: Clear resource lifetimes and state transitions
2. **Type-safe**: Use strong typing to prevent errors at compile time
3. **Minimal allocations**: Struct-based API where possible
4. **Native interop**: Direct P/Invoke to native APIs, no middleware
5. **Modern patterns**: Use spans, stackalloc, and unsafe code for performance

## Success Criteria
- Render a textured 3D model on all supported platforms
- Achieve 60 FPS with 10,000 draw calls
- Support hot-reloading of shaders
- Memory usage under 100MB for basic scenes
- Clean shutdown with no leaks

## Dependencies
- BlueSky.Platform (for windowing)
- Platform SDKs (Metal, DirectX, Vulkan)
- Shader compiler (SPIRV-Cross or custom)

## Timeline
- Phase 1: Metal backend (1-2 weeks)
- Phase 2: DirectX 11 backend (1 week)
- Phase 3: Vulkan backend (2 weeks)
- Phase 4: DirectX 12 backend (2 weeks)
- Phase 5: OpenGL backend (1 week)
