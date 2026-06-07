# 🌌 BlueSky Engine

A high-performance, cross-platform game engine built in C# with native rendering backends and advanced vehicle physics.

![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)
![License](https://img.shields.io/badge/license-Custom%20(Attribution%20Required)-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)

## ✨ Features

### � Core Engine
- **Entity Component System (ECS)** - High-performance archetype-based architecture
- **Multi-threaded Job System** - Work-stealing scheduler with microsecond wake-up latency
- **Asset Pipeline** - Import GLTF, FBX, OBJ with skeletal animation support
- **Scene Management** - Hierarchical transforms, prefabs, serialization

### 🎨 Rendering (Triple Backend Support)
- **Metal** - Native macOS/iOS rendering with tile-based deferred rendering
- **Vulkan** - High-performance cross-platform with X11/Wayland/MoltenVK support
- **DirectX 11** - Full Windows compatibility with feature level detection

**Rendering Features:**
- Forward+ clustered lighting
- Ease+ Ultimate renderer with advanced PBR
- Skeletal animation with bone-driven transforms
- Horizon-based global illumination
- Polaris upscaling for performance
- Terrain system with real-time sculpting
- Hardware ray tracing support (experimental)

### ⚙️ Physics
- **Jolt Physics** integration for high-fidelity simulation
- **Advanced Vehicle Physics** - 4-wheel independent suspension, tire simulation, drift mechanics
- **Terrain Collision** - Heightmap-based collision with normal extraction
- **Raycast System** - Fast spatial queries

### 🚗 Vehicle System
- Realistic suspension with independent wheel contact
- Tire force model with slip ratio/angle calculation
- Aerodynamic downforce and stability control
- Automatic transmission with gear shifting
- Chase camera with dynamic smoothing
- Bone-driven wheel animation for skeletal meshes

### 🎬 Animation
- Skeletal mesh import from GLTF/FBX
- Animation clips with blend trees
- Procedural animation system
- Runtime bone manipulation
- LOD support for skeletal meshes

### 🎵 Audio
- Spatial 3D audio with Orchestra integration
- Audio mixer with dynamic routing
- Streaming support for large audio files

### 🤖 AI & Scripting
- **TeaScript** - Custom scripting language for gameplay
- **Overthinking AI** - Decision tree system for NPCs
- Hot-reload support for rapid iteration

### 🛠️ Editor
- **UE5-Inspired Docking System** - Flexible panel layout
- **Viewport Renderer** - Real-time 3D preview with gizmos
- **Material Editor** - Visual shader graph
- **Animation Editor** - Timeline-based animation editing
- **Terrain Sculptor** - Brush-based heightmap editing
- **Content Browser** - Asset management and organization
- **Play Mode** - In-editor testing with pause/resume

## 🚀 Quick Start

### Prerequisites
- **.NET 8.0 SDK** or higher
- **Platform-specific requirements:**
  - **macOS**: Xcode command line tools
  - **Windows**: Visual Studio 2022 or Build Tools
  - **Linux**: Vulkan drivers, X11/Wayland development libraries

### Build & Run

```bash
# Clone the repository
git clone https://github.com/yourusername/bluesky-engine.git
cd bluesky-engine

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
3. Choose a template (Blank, 3D Scene, First Person, etc.) {ALL TEMPLATES ARE SAME LOL}
4. Set project name and location
5. Click **"Create Project"**

### Add a Car Controller

**Using the Editor UI:**

1. **Import Car Model**: Drag your rigged car model (GLTF/FBX) into the Content Browser to import it
2. **Add to Scene**: Drag the imported car entity from Content Browser into the viewport/scene
3. **Setup Physics** (in Details panel after selecting the car entity):
   - Click **Add Rigidbody** button
     - Mass: `1400`
     - Use Gravity: ✓ (checked)
   - Click **Add Collider** button
     - Type: `Box`
     - Size: `(2, 1.2, 4.5)`
   - Click **Add Car Controller** button
     - Default settings will be applied automatically
4. **Add TeaScript Control** (optional):
   - Drag the `car_system.tea` file from Content Browser onto the car entity
   - The TeaScript Component will be added automatically

[PRO TIP: Use the Unreal Engine 5 Car Rigger Addon for Blender to have the best results and use the Bone Names: FR, FL, RR, RL for the respective Front and Rear tires]

**Using TeaScript:**

You can also control your car programmatically using TeaScript. Create a `car_system.tea` file:

```tea
// car_system.tea - Advanced car control script
entity car;
float throttle = 0.0;
float steerAngle = 0.0;
float maxSpeed = 50.0;

function OnStart() {
    print("Car system initialized!");
    car = GetEntity("Car");
}

function OnUpdate(deltaTime) {
    // Get input
    if (IsKeyDown("W")) {
        throttle = 1.0;
    } else if (IsKeyDown("S")) {
        throttle = -0.5;
    } else {
        throttle = 0.0;
    }
    
    if (IsKeyDown("A")) {
        steerAngle = -30.0;
    } else if (IsKeyDown("D")) {
        steerAngle = 30.0;
    } else {
        steerAngle = 0.0;
    }
    
    // Apply forces
    Vector3 velocity = Physics.GetVelocity(car);
    float currentSpeed = velocity.Length();
    
    if (currentSpeed < maxSpeed) {
        Vector3 forward = GetForward(car);
        Physics.AddForce(car, forward * throttle * 5000.0);
    }
    
    // Apply steering (simplified)
    if (currentSpeed > 0.5) {
        Vector3 angularVel = Physics.GetAngularVelocity(car);
        angularVel.Y = steerAngle * 0.02;
        Physics.SetAngularVelocity(car, angularVel);
    }
}

function OnCollision(other) {
    print("Car collided with: " + other.name);
}
```

**To use this script:**
1. Create a new file in Content Browser → **TeaScript** → name it `car_system.tea`
2. Add **TeaScript Component** to your Car entity
3. Drag `car_system.tea` into the Script Asset field
4. Press Play to test!

## 📐 Architecture

```
BlueSkyEngine/
├── Core/
│   ├── ECS/              # Entity Component System
│   ├── Memory/           # Custom allocators, pooling
│   ├── Threading/        # Job system, work-stealing
│   ├── Scene/            # Scene graph, serialization
│   └── Gameplay/         # CarController, PlayerController
├── Rendering/
│   ├── EasePlus/         # Advanced PBR renderer
│   ├── Lighting/         # Forward+, Horizon GI
│   ├── PostProcessing/   # Bloom, DOF, tonemapping
│   └── TerrainSystem.cs
├── RHI/                  # Rendering Hardware Interface
│   ├── Metal/            # macOS/iOS backend
│   ├── Vulkan/           # Cross-platform backend
│   └── DirectX11/        # Windows backend
├── Physics/
│   ├── PhysicsWorld.cs   # Jolt integration
│   └── VehiclePhysics.cs # Car simulation
├── Animation/
│   ├── SkeletalMesh.cs
│   ├── AnimationClip.cs
│   └── GLTF/             # GLTF importer
├── Audio/
│   └── Orchestra.cs      # 3D spatial audio
├── Editor/
│   ├── EditorApp.cs      # Main editor loop
│   ├── DockingSystem.cs  # UE5-style panels
│   └── ViewportRenderer.cs
└── Platform/
    ├── macOS/            # Cocoa window
    ├── Windows/          # Win32 window
    └── Linux/            # X11/Wayland windows
```

## 🎯 Performance

- **ECS Archetype Queries**: <0.1ms for 100k entities
- **Physics Simulation**: 60fps with 1000+ dynamic bodies
- **Skeletal Animation**: 60fps with 50+ animated characters
- **Rendering**: 144fps @ 1080p (Metal on M1 MacBook)
- **Job System**: Sub-microsecond task scheduling

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run specific test
dotnet test --filter "Category=Physics"
```

## 📦 Dependencies

- **JoltPhysicsSharp** (2.11.2) - Physics simulation
- **StbImageSharp** (2.30.15) - Image loading
- **StbTrueTypeSharp** (1.26.12) - Font rendering

## 🎓 Documentation

- [Getting Started Guide](docs/getting-started.md)
- [API Reference](docs/api-reference.md)
- [Vehicle Physics Guide](docs/vehicle-physics.md)
- [Rendering Pipeline](docs/rendering-pipeline.md)
- [TeaScript Scripting](docs/teascript.md)

## 🤝 Contributing

We welcome contributions! See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

### Development Setup

```bash
# Install dependencies
dotnet restore

# Build in debug mode
dotnet build --configuration Debug

# Format code
dotnet format

# Run tests
dotnet test
```

## 📜 License

BlueSky Engine is free to use for any purpose, including commercial projects. 

**Attribution Requirement**: All games and applications created with BlueSky Engine must include visible credit/advertisement (e.g., "Made with BlueSky Engine" in splash screen, credits, or about section).

See [LICENSE](LICENSE) file for full details.

## 🙏 Acknowledgments

- **Jolt Physics** - High-performance physics engine
- **StbImage** - Image loading library
- **Vulkan** - Cross-platform graphics API
- **Metal** - Apple's graphics framework

## � Contact

- **Issues**: [GitHub Issues](https://github.com/yourusername/bluesky-engine/issues)
- **Discussions**: [GitHub Discussions](https://github.com/yourusername/bluesky-engine/discussions)

---

**Built with ❤️ for game developers who demand performance and flexibility.**
