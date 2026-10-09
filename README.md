# 🌌 BlueSky Engine

**A high-performance, cross-platform game engine built from scratch in C# (.NET 8.0)**

CPU ray tracing research • TeaScript • BSR rendering • Horizon lighting • LHO networking rewrite planned

> **Note on honesty:** the tables below describe what this checkout *actually does*,
> verified against the tree. Status meanings are defined in Project Status.
> Numbers marked "reported" are historic claims without a reproducible in-tree benchmark.

![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)
![License](https://img.shields.io/badge/license-Custom%20(Attribution%20Required)-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)

**[Read the Full Story on Medium](https://medium.com/@sohamanand409/building-bluesky-engine-60-fps-ray-tracing-on-a-2011-laptop-9a08e95d1a48)** • **[Join Discussions](https://github.com/Soham-Anand/BlueSky-Engine/discussions)**

---

## 📊 Project Status

**Legend:**
- ✅ **Implemented** - A working path exists in this checkout
- 🚧 **Partial** - Some functionality is present; limitations are listed
- 🧪 **Experimental** - Research code without a supported runtime route
- ❌ **Planned** - Not yet implemented

---

## ✨ Feature Status

### 🏗️ Core Engine

| Feature | Status | Notes |
|---------|--------|-------|
| **Archetype-Based ECS** | ✅ Complete (single-threaded) | Generational IDs, chunk swap-remove, query cache. Update loop is synchronous; no job system, no lock-free path (the old "<0.1ms" badge is a reported claim, not a reproduced benchmark) |
| **Work-Stealing Job System** | ❌ Planned | The engine update loop is synchronous; Jolt uses its own worker pool. |
| **Asset Database** | ❌ Planned | The editor content browser scans project files directly; there is no central GUID registry or dependency database. Mesh import uses Blender Ease and `.stratapack`. |
| **Scene Serialization** | ✅ Complete | Save/load with prefab support |
| **Memory Allocators** | ❌ Planned | The runtime currently uses managed .NET memory; no custom allocator subsystem is connected. |

### 🎨 BSR — BlueSky Rendering

| Feature | Status | Notes |
|---------|--------|-------|
| **Metal Backend** | 🚧 Partial | Most complete backend (render + compute). No iOS support, no tile-based deferred path. |
| **Vulkan Backend** | 🚧 Partial | Hand-rolled loader (X11/Win32/MoltenVK surfaces). Requires SPIR-V bytecode; no `.spv` binaries ship in-tree, so Vulkan UI/mesh pipelines warn and fall back. |
| **DirectX 11 Backend** | 🚧 Partial | Windows raster backend (VS+PS, input layout, blend/depth, DXGI swapchain). Compute pipelines and storage-resource binding throw `NotSupportedException` by design. |
| **Vectra particles** | ❌ Absent | No particle code exists in-tree (name reserved for a future system). |
| **Horizon lighting** | 🚧 In Progress | The viewport consumes ECS lights through the Horizon shader path. Forward+ culling, contact-shadow rendering, and volumetric passes remain incomplete. |
| **Skeletal Animation** | 🚧 Partial | Skeletal rig/skin/clip data import; `AnimationClip.Sample()` interpolates. No runtime playback controller, no IK, no runtime LOD selection. |
| **Terrain System** | ✅ Mostly complete | Heightmap rendering, brush sculpting, raycast, Jolt heightfield collision. `ChunkSize`/`LodCount` are stored per terrain but the renderer draws the full mesh (no runtime LOD selection yet). |
| **BSR renderer** | 🚧 Partial | Viewport adapter and raster scene path; optional effects and some backends are not complete. |
| **Afterglow post-processing** | ❌ Absent | Name reserved; no post-process pass classes exist in-tree. |
| **Strata materials** | 🚧 In Progress | `.stratamat`/`.stratapack` codec + importer + per-draw upload are real and tested. Tier-2 lobes (clearcoat/sheen/aniso/iridescence) exist as mask bits with partial shader wiring. A live **Strata configurator panel** exists in-editor (basic sliders/toggles, not a full material editor). |

### 🔥 Project Polaris (CPU Ray Tracing)

| Feature | Status | Notes |
|---------|--------|-------|
| **AVX SIMD BVH Traversal** | ✅ Complete | 8-wide ray packets; in-tree SIMD/BVH unit tests pass (77 FPS figure is a reported claim, not reproduced here) |
| **SAH BVH Construction** | ✅ Complete | Tested in-tree |
| **Checkerboard Rendering** | ✅ Complete | 320×180 → 720p upscale path exists |
| **Temporal Accumulation** | ✅ Complete | Reduces noise over frames |
| **GPU Upscaler** | 🚧 Partial | Metal/DX11 shader paths exist; Vulkan binaries not supplied. |
| **RT Backend Selector** | 🧪 Experimental | No automatic selection; editor/runtime rendering does not route through Polaris (zero non-test callers in-tree). |
| **Multi-Bounce GI** | ❌ Planned | No supported multi-bounce GI path is included. |

### ⚙️ Airborne — Physics (Jolt Integration)

| Feature | Status | Notes |
|---------|--------|-------|
| **Rigidbody Simulation** | ✅ Complete | 1000+ bodies at 60 FPS |
| **Terrain Collision** | ✅ Complete | Heightfield with normal extraction |
| **Raycast System** | ✅ Complete | Fast spatial queries |
| **Vehicle Physics** | ✅ Complete | 4-wheel suspension, tire force model |
| **Wheel Bone Binding** | ✅ Complete | Auto-detects wheel bones (FR/FL/RR/RL) |
| **Tire Force Model** | ✅ Complete | Slip ratio/angle, realistic handling |
| **Soft Body Physics** | ❌ Planned | Not yet implemented |

### 🎬 Motif — Animation

| Feature | Status | Notes |
|---------|--------|-------|
| **Direct FBX/GLTF/OBJ import** | ❌ Unsupported by current policy | Use Blender Ease to export `.stratapack`; skeletal export is not yet part of the addon workflow. |
| **Skeletal Mesh Rendering** | 🚧 In Progress | Skinning code exists; the full authoring-to-runtime workflow needs completion. |
| **Animation Clips** | 🚧 Partial | Clip data, keyframes, and serialization exist; runtime playback and an animation controller are not implemented. |
| **IK System** | ❌ Planned | Not yet implemented |
| **Mesh LOD** | 🚧 Partial | The static mesh editor saves LOD metadata and presets; mesh generation and runtime LOD selection are not implemented. |

### 🎵 Echo — Audio

| Feature | Status | Notes |
|---------|--------|-------|
| **Playback backend** | ❌ Planned | Zero `IAudioBackend` implementations in-tree; `Echo` orchestrates with nothing to play through. |
| **Spatial audio** | 🚧 Partial (silent) | Attenuation, panning, Doppler approximation, and Airborne raycast occlusion are computed; no audible output without a backend. |
| **DSP Effects** | ❌ Planned | Reverb, filters planned |

### 🤖 Overthinking AI & TeaScript

| Feature | Status | Notes |
|---------|--------|-------|
| **TeaScript Language** | ✅ Complete (small) | Lexer → Parser → tree-walk runtime with hot-reload, step/depth guards. No `for` loops, classes, or modules; calls are `Ident(args)` only. |
| **TeaScript C# Bindings** | ✅ Complete | Native engine API access for UI and physics; audio playback bindings are not wired. |
| **Overthinking AI System** | 🧪 Orphaned | Brain/behavior classes exist but nothing in editor/runtime registers or ticks them (only referenced by tests). |
| **NavMesh** | ❌ Planned | No pathfinding code in-tree |

### 🛠️ Editor

| Feature | Status | Notes |
|---------|--------|-------|
| **Docking System** | ✅ Complete | Drag-and-drop panels; tiny-splitter and narrow-slider crashes fixed + tested |
| **3D Viewport** | ✅ Complete | Real-time preview, transform gizmos, Lit/Wireframe/Unlit toolbar (mode buttons) |
| **Viewport Dbg views** | ✅ Complete | Toolbar `Dbg:` cycles Lit → Nrm → Unl → Shd → AO → Dir → Env → Sun (normals/albedo/shadow/AO/sun-direct/ambient/sun-NdotL isolation for diagnosing shading issues) |
| **Content Browser** | ✅ Complete | Asset management, thumbnails |
| **Terrain Sculptor** | ✅ Complete | Brush-based heightmap editing (raise/lower/smooth/flatten/noise/erode) |
| **Play-in-Editor** | ✅ Complete | Pause/resume, state reset, hot-reload |
| **Material Editor** | 🚧 Basic | The live Strata configurator panel edits materials in-session; no full node-based/graph editor. |
| **Animation Timeline** | ❌ Planned | An animation timeline editor is not included. |
| **Visual Scripting** | ❌ Planned | Blueprint-style nodes planned (no code in-tree) |
| **Profiler** | ❌ Planned | No dedicated CPU/GPU profiler is included (no profiler classes in-tree). |

### 🌐 LHO — Let's Hop On (Networking)

| Feature | Status | Notes |
|---------|--------|-------|
| **LHO rewrite** | 📝 Planned | Name selected for a rewrite; current EOS/replication code is the legacy implementation. |
| **Client-Side Prediction** | ❌ Planned | No client prediction or server reconciliation path is implemented. |

---

## 🎯 Performance Benchmarks

**Reported measurements (not reproduced by this source/build audit):**

The numbers below are retained from the project's earlier notes. This checkout does not include a reproducible benchmark report for each result, and this maintenance pass did not measure runtime performance.

**MacBook Pro M1, 1080p:**
- **ECS Queries**: <0.1ms for 100,000 entities
- **Physics**: 1000+ rigidbodies at locked 60 FPS
- **Rendering**: 144 FPS (Metal backend)
- **Job System**: <1μs task scheduling latency
- **GPU Culling**: 1,000,000 instance capacity

**i5-2410M (2011) + Intel HD 3000:**
- **Polaris Ray Tracing**: 77 FPS @ 320×180 → 720p
- **Forward Rendering**: 60 FPS @ 720p
- **Physics**: 500 rigidbodies at 60 FPS

---

## 🚀 Highlights

### Project Polaris: Ray Tracing on 2011 Hardware

Everyone said ray tracing needs RTX cards. **Project Polaris proves them wrong.**

```
Performance Budget (i5-2410M + Intel HD 3000):
  Ray Generation:     ~0.5ms
  BVH Traversal:      ~8.0ms  (AVX 8-wide SIMD)
  Shading:            ~3.0ms
  Upload + Upscale:   ~1.5ms
  ────────────────────────────
  Total:              ~13ms   (77 FPS)
```

**How it works:**
- **AVX SIMD**: Trace 8 rays simultaneously per instruction
- **BVH + SAH**: ~20,000x speedup over naive intersection
- **Checkerboard**: Render 320×180, upscale to 720p
- **Temporal Accumulation**: Reduce noise across frames
- **Automatic fallback**: No runtime selector is included in this checkout; Polaris requires explicit construction and supported shader binaries.

### TeaScript: Custom Language for Game Dev

```javascript
// Complete vehicle control in TeaScript
fn update() {
    let dt = getDeltaTime()

    // Real-time physics queries
    let velX = getVelocityX()
    let velY = getVelocityY()
    let velZ = getVelocityZ()
    let speedKMH = sqrt(velX*velX + velY*velY + velZ*velZ) * 3.6

    // Direct UI rendering
    uiPanel(10.0, 10.0, 280.0, 120.0, "TopLeft", 0.05, 0.05, 0.08, 0.8)
    uiText("Speed: " + speedKMH + " km/h", 20.0, 45.0, "TopLeft", 1.0, 1.0, 1.0, 1.0)
}
```

**Features:**
- C#-like syntax
- Hot-reload without recompilation
- Native C# bindings for performance
- Built from scratch: Lexer → Parser → Runtime

### Ease + StrataPack: Blender-Only Ingestion

**Ease is the Blender addon** (`Blender/`, install `BlueSkyEngineEase.zip`, Blender 4.0+),
not a renderer. It is the only mesh authoring path: evaluated depsgraph mesh,
Principled scalars + image links, optional AO/bent bakes → single `.stratapack`
(JSON header + binary blobs, 12-check decoder validation). The old handwritten
FBX/GLTF/OBJ/MTL parsers were deleted; the engine no longer reads those formats
directly. See `StrataPackPlan.md` for the container spec and `Blender/README.md`
for the workflow (Preview → Check → Export).

---

## 🚀 Quick Start

### Prerequisites
- **.NET 8.0 SDK** or higher
- **Platform-specific requirements:**
  - **macOS**: Xcode command line tools
  - **Windows**: Visual Studio 2022 or Build Tools
  - **Linux**: Vulkan drivers and an X11 display (Wayland sessions need XWayland)

### Build & Run

```bash
# Clone the repository
git clone https://github.com/Soham-Anand/BlueSky-Engine.git
cd BlueSky-Engine

# Build the engine
dotnet build ./BlueSkyEngine/BlueSkyEngine.csproj

# Launch the editor
./launch-bluesky.sh
# Or on Windows:
dotnet run --project ./BlueSkyEngine/BlueSkyEngine.csproj
```

### Create Your First Project

1. Launch the editor
2. Click **"New Project"**
3. Choose a template (Blank, 3D Scene, First Person, etc.)
4. Set project name and location
5. Click **"Create Project"**

---

## 🚗 Vehicle Setup Guide

### Using the Editor UI:

1. **Import Car Model**: Export a supported static mesh from Blender using BlueSky Engine Ease, then import its `.stratapack`. Skeletal vehicle export is not yet a complete path.
2. **Add to Scene**: Drag imported car entity into viewport
3. **Setup Physics** (in Details panel):
   - Click **Add Rigidbody**
     - Mass: `1400`
     - Use Gravity: ✓
   - Click **Add Collider**
     - Type: `Box`
     - Size: `(2, 1.2, 4.5)`
   - Click **Add Car Controller**
     - Default settings auto-applied
4. **Add TeaScript** (optional):
   - Attach a `.tea` script asset to the car entity

**Pro Tip**: Use the [UE5 Car Rigger Addon for Blender](https://blendermarket.com/products/ue5-car-rigger) with bone names: **FR, FL, RR, RL** for wheels

---

## 📐 Architecture

```
BlueSkyEngine/
├── Core/
│   ├── ECS/              # Keystone: Entity Component System
│   ├── Scene/            # Keystone: scene serialization
│   └── Gameplay/         # Controllers and vehicle systems
├── Rendering/
│   ├── Strata/           # Material format, importer, and surface data
│   ├── RayTracing/
│   │   └── Polaris/      # CPU ray tracer
│   └── TerrainSystem.cs  # Terra
├── RHI/                  # BSR graphics API layer
│   ├── Metal/            # macOS/iOS backend
│   ├── Vulkan/           # Cross-platform backend
│   └── DirectX11/        # Windows backend
├── Airborne/
│   ├── PhysicsWorld.cs   # Jolt integration
│   └── VehiclePhysics.cs # Car simulation
├── Motif/
│   ├── SkeletalMesh.cs
│   └── AnimationClip.cs  # Imported clip data; no runtime playback controller
├── TeaScript/            # Custom language implementation
├── Audio/
│   └── Echo.cs      # Audio orchestration; platform playback backend not included
├── AI/
│   └── Overthinking/     # AI behavior system
├── Networking/           # LHO rewrite target
├── Editor/
│   ├── EditorApp.cs      # Main editor loop
│   ├── DockingSystem.cs  # UE5-style panels
│   └── ViewportRenderer.cs
└── Platform/
    ├── macOS/            # Cocoa window
    ├── Windows/          # Win32 window
    └── Linux/            # X11 window and input backend
```

---

## 🧪 Tests

17 in-tree suites (ECS, TeaScript, physics/vehicle, Polaris SIMD, Strata
codec/pack/importer/probe/sky/surface, networking, audio/AI, editor stability)
run through a custom console runner — no xUnit/NUnit:

```bash
dotnet run --project ./BlueSkyEngine/BlueSkyEngine.csproj -- --test
```

Coverage is Strata-heavy; ECS/audio/networking suites are smoke-level.
A green run prints `ALL ENGINE SUBSYSTEM TESTS PASSED`.

---

## 📦 Dependencies

- **JoltPhysicsSharp** (2.11.2) - Physics simulation
- **StbImageSharp** (2.30.15) - Image loading
- **StbTrueTypeSharp** (1.26.12) - Font rendering

All dependencies are managed via NuGet.

---

## ⚠️ Known Issues (open, under investigation)

- **View-dependent dark artifact on meshes.** A dark spot with radial streaks appears
  on car meshes and glides as the camera orbits. Eliminated so far: albedo data
  (Unlit clean), shadow factor (reads 1.0), mesh normals (smooth in Normals view),
  baked AO (reads 1.0), below-horizon reflections (ground blend lifted, horizon AO
  floored). The uniform sun path currently contributes ~nothing scene-wide
  (Direct-only debug view reads black with a healthy sun vector), so scenes run on
  loop-light + ambient only. Prime suspects remaining: sun uniform delivery on GPU
  vs per-mesh normal orientation from the pack pipeline. Viewport `Dbg:` views
  (Sun included) are the instruments; use same-camera comparisons.

---

## 🤝 Contributing

Contributions welcome! This project is still growing and there's plenty to do.

**Areas needing help:**
- Multi-bounce GI in Polaris ray tracer
- Visual scripting/Blueprint system
- NavMesh pathfinding
- Soft body physics
- Mobile platform optimization

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

---

## 📜 License

BlueSky Engine is free to use for any purpose, including commercial projects. 

**Attribution Requirement**: All games/applications must include visible credit (e.g., "Made with BlueSky Engine" in splash screen or credits).

See [LICENSE](LICENSE) for full details.

---

## 🙏 Acknowledgments

- **Jolt Physics** - High-performance physics engine
- **StbImage** - Image loading library
- **Vulkan/Metal/DirectX** - Graphics APIs

---

## 📬 Contact

- **Author**: Soham Anand (13 years old)
- **Issues**: [GitHub Issues](https://github.com/Soham-Anand/BlueSky-Engine/issues)
- **Discussions**: [GitHub Discussions](https://github.com/Soham-Anand/BlueSky-Engine/discussions)

---

**Built with ❤️ by a 13-year-old passionate about low-level optimization and making advanced graphics accessible on any hardware.**
