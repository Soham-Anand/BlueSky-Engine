# BlueSky Engine

A high-performance, cross-platform 3D Game Engine built from the ground up using C# 8.0 (.NET) with custom Platform Application loops, native Windowing backends (Cocoa, Win32, Wayland), and highly-optimized Vulkan/Metal graphics renderer pipelines!

## Features
- **Custom Built**: Every layer is built scratch-first—window management, ECS paradigms, input layers, math modules.
- **RHI (Render Hardware Interface)**: Multi-backend rendering API (Metal for MacOS, Vulkan for Linux/Windows) allowing abstract shader compiling and low-level descriptor buffer allocations.
- **Docking UI**: Implemented an advanced internal docking and panel system entirely within our non-allocating rendering loops!
- **Dynamic Entities & Shadows**: Supports loading raw 3D assets (`.obj`) dynamically baked into binary formats (`.blueasset`), dynamically constructing flat shaders with mathematical Normal recalculators, and rendering high-resolution multidirectional Shadow Maps!

## Quick Start (MacOS + Metal)

```bash
# Build the Shaders
./build-shaders.sh

# Run the Engine Runtime
dotnet run --project BlueSkyEngine/BlueSkyEngine.csproj
```

## Architecture
- **Core/:** Entity Component System (ECS), asset importers (`OBJParser`), Memory Math logic.
- **Platform/:** Operating System specific integrations (`macOS/CocoaWindow`, `Windows/Win32Window`, `Linux/WaylandWindow`).
- **RHI/:** Abstracted graphics pipeline layers mappings rendering to Vulkan / Metal backends simultaneously.
- **Editor/:** Visual UI Docking implementation encompassing an integrated Content Browser, properties Inspector, Outliner, and the interactive 3D Viewport.

Welcome to the future of high-performance custom engines!
