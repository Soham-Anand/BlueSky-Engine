# BlueSky Runtime

Standalone game runtime for BlueSky Engine. It runs scenes, fixed-step physics, car controls, TeaScript, and viewport rendering outside the editor UI.

## Features

✅ **Native Window Creation** - Cocoa and Win32; Linux requires X11 or XWayland  
✅ **Hardware-Accelerated Rendering** - Auto-detects best RHI backend (Metal/Vulkan/DX11)  
⚠️ **Renderer Reuse** - Currently reuses `ViewportRenderer` and editor UI renderer code.  
✅ **High Performance** - Runs at native refresh rate with VSync control  
✅ **Multi-Instance** - Test multiplayer scenarios locally  
✅ **EOS-Ready Launch Flow** - Standalone hosts/clients can be started with EOS session flags  

## Architecture

```
Program.cs          → Command-line parsing, entry point, and runtime configuration
GameRuntime.cs      → Core game loop and system initialization
```

### System Initialization Order

1. **Core** - ECS world and terrain system
2. **Platform** - Window creation, event handling
3. **Rendering** - RHI device, swapchain, viewport renderer, and HUD
4. **Physics** - Jolt when available, Airborne built-in fallback otherwise
5. **Input** - Window input context and vehicle controls
6. **Multiplayer** - Optional EOS session and replication path
7. **Scene Loading** - Scene deserialization, terrain assets, and physics registration
8. **Scripting** - Per-entity TeaScript start/update/fixedUpdate

## Usage

### From Editor
Click **"Play Standalone"** button in the toolbar.

### From Command Line
```bash
# Quick launch (uses launch-runtime.sh)
./launch-runtime.sh --width 1920 --height 1080 --fullscreen

# Direct launch with dotnet
dotnet run --project BlueSkyRuntime/BlueSkyRuntime.csproj -- [args]

# Build standalone executable
dotnet publish BlueSkyRuntime/BlueSkyRuntime.csproj -c Release -r osx-arm64
```

### Command-Line Arguments

```
--scene <path>      Path to .scene file (required for actual gameplay)
--mode <mode>       Runtime mode: standalone (default), server, headless
--width <int>       Window width (default: 1280)
--height <int>      Window height (default: 720)
--fullscreen        Enable fullscreen mode
--vsync             Enable VSync (default: true)
--multiplayer <m>   offline, host, or join
--session-name <id> EOS lobby/session name
--join-session <id> Join an existing session id
--player-name <n>   Local player display name
--eos-product-id <id>
--eos-sandbox-id <id>
--eos-deployment-id <id>
--eos-client-id <id>
--eos-client-secret <secret>
```

## Current Status

### Implemented in this checkout
- Native window creation (Cocoa on macOS, Win32 on Windows, X11/XWayland on Linux)
- RHI initialization with auto-detection (Metal/Vulkan/DX11)
- Game loop with proper frame timing
- Input handling and vehicle controls
- Viewport scene rendering and TeaScript HUD
- Fixed-step physics, scene deserialization, and terrain registration
- Optional EOS host/join coordination

### Not included
- A platform audio playback backend
- A runtime renderer separate from the editor renderer assembly

## Technical Details

### Rendering Flow
```
1. AcquireNextImage()          → Get swapchain image
2. CreateCommandBuffer()       → Get command buffer
3. BeginRenderPass()           → Start rendering
4. ViewportRenderer.Render()   → Render the loaded scene
5. EndRenderPass()             → Finish rendering
6. Submit()                    → Submit to GPU
7. Present()                   → Display on screen
```

### Performance
- Currently runs at ~120 FPS (vsync disabled) on M-series Macs
- Metal backend on macOS
- Vulkan backend on Linux
- DirectX 11 on Windows

## Integration with Editor

The `StandalonePlayController` in the editor manages runtime processes:
- Builds runtime if needed
- Launches with current scene
- Passes EOS multiplayer flags when host/join mode is requested
- Captures stdout/stderr
- Tracks process lifetime
- Supports multiple instances

## Building Executables

For distribution, publish platform-specific executables:

```bash
# macOS (Apple Silicon)
dotnet publish -c Release -r osx-arm64 --self-contained

# macOS (Intel)
dotnet publish -c Release -r osx-x64 --self-contained

# Windows
dotnet publish -c Release -r win-x64 --self-contained

# Linux
dotnet publish -c Release -r linux-x64 --self-contained
```

Output will be in `bin/Release/net8.0/<rid>/publish/`

---

**Status**: Scene, physics, scripting, and rendering paths are present. Audio playback and renderer separation are not included.
