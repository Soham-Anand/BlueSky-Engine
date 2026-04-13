# BlueSky Engine

A high-performance, cross-platform 3D game engine built from scratch in C# with custom windowing, ECS architecture, multi-backend rendering, and **TeaScript** - a custom scripting language for gameplay programming.

![BlueSky Engine](https://img.shields.io/badge/Platform-macOS%20%7C%20Windows%20%7C%20Linux-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/License-MIT-green)

## ✨ Features

### 🎮 Scripting with TeaScript
- **Custom Scripting Language** - Easy-to-learn syntax designed for game development
- **Hot Reload** - Update scripts without restarting the engine
- **ECS Integration** - Direct access to entities, components, and transforms
- **Play Mode Isolation** - Test scripts without modifying your scene
- **Built-in Functions** - Math, transforms, time, input, and debug logging
- **Visual Feedback** - Green indicator shows script-enabled entities

👉 **[Get Started with TeaScript](TeaScripting_GettingStarted.md)**

### 🏗️ Core Engine
- **Custom ECS (Entity Component System)** - High-performance archetype-based entity management
- **Cross-Platform Windowing** - Native backends for macOS (Cocoa), Windows (Win32), Linux (Wayland)
- **RHI (Render Hardware Interface)** - Abstracted rendering supporting Metal (macOS) and DirectX 9 (Windows)
- **Asset Pipeline** - Import OBJ models, textures, and materials with binary caching
- **Scene Snapshots** - Unity-style Play/Pause/Stop that preserves editor state

### 🎨 Editor
- **Docking System** - Flexible panel layout with drag-and-drop docking
- **3D Viewport** - Interactive scene view with camera controls and grid
- **Content Browser** - Asset management with type indicators and drag-drop
- **Outliner** - Hierarchical entity list with selection
- **Details Panel** - Component inspector and editor
- **Script Editor** - Built-in editor for TeaScript files with syntax highlighting

### 🌟 Rendering
- **PBR Lighting** - Physically-based rendering with metallic/roughness workflow
- **Shadow Mapping** - High-resolution directional shadows with PCF filtering
- **Procedural Sky** - Beautiful atmospheric sky with clouds
- **Grid Overlay** - Editor grid with axis coloring (X=red, Z=blue)
- **Metal Backend** - Native Metal rendering on macOS for optimal performance

## 🚀 Quick Start

### Prerequisites
- **.NET 8.0 SDK** - [Download here](https://dotnet.microsoft.com/download)
- **macOS** (Metal) or **Windows** (DirectX 9)
- **For macOS**: Xcode Command Line Tools (`xcode-select --install`)

### Build & Run

```bash
# Clone the repository
git clone https://github.com/yourusername/bluesky-engine.git
cd bluesky-engine

# macOS: One-command launch (compiles shaders, builds, and runs)
./launch-bluesky.sh

# Or for quick launches after first time:
./quick-launch.sh

# Windows: Manual steps
cd BlueSkyEngine/Editor/Shaders
compile_shaders.bat
cd ../../..
dotnet build
dotnet run --project BlueSkyEngine/BlueSkyEngine.csproj
```

### Your First Script

1. **Launch the engine** and create or open a project
2. **Right-click in Content Browser** → "New TeaScript"
3. **Name it** `MyFirstScript.tea`
4. **Double-click to edit** and paste this code:

```tea
// My first TeaScript!
let speed = 2.0
let time = 0

fn start() {
    log("Hello from TeaScript!")
}

fn update() {
    let dt = getDeltaTime()
    time = time + dt
    
    // Move in a circle
    let x = cos(time * speed) * 3
    let z = sin(time * speed) * 3
    setPosition(x, 1, z)
}
```

5. **Drag the script** onto the teapot entity in the viewport
6. **Press Play** ▶️ and watch your teapot move in a circle!
7. **Press Stop** ⏹️ to return to editor mode

## 📚 Documentation

- **[TeaScript Getting Started Guide](TeaScripting_GettingStarted.md)** - Complete tutorial for scripting
- **[TeaScript Language Reference](TeaScript/README.md)** - Full language documentation
- **[Example Scripts](TeaScript/Examples/)** - Learn from working examples

## 🎯 Example Scripts

### Simple Movement
```tea
let speed = 5.0

fn update() {
    let dt = getDeltaTime()
    let x = getPositionX()
    setPositionX(x + speed * dt)
}
```

### Circular Motion
```tea
let radius = 5.0
let speed = 2.0
let time = 0

fn update() {
    time = time + getDeltaTime()
    let x = cos(time * speed) * radius
    let z = sin(time * speed) * radius
    setPosition(x, 1, z)
}
```

### Bouncing Object
```tea
let velocity = 0
let gravity = -9.8

fn update() {
    let dt = getDeltaTime()
    let y = getPositionY()
    
    velocity = velocity + gravity * dt
    y = y + velocity * dt
    
    if (y <= 0) {
        y = 0
        velocity = abs(velocity) * 0.8
    }
    
    setPositionY(y)
}
```

More examples in `TeaScript/Examples/`!

## 🏗️ Project Structure

```
BlueSkyEngine/
├── Core/
│   ├── ECS/              # Entity Component System
│   ├── Assets/           # Asset importers and management
│   ├── Scripting/        # TeaScript integration
│   ├── Scene/            # Scene serialization and snapshots
│   └── Math/             # Vector, matrix, quaternion math
├── Platform/
│   ├── macOS/            # Cocoa windowing and input
│   ├── Windows/          # Win32 windowing and input
│   └── Linux/            # Wayland windowing (WIP)
├── RHI/
│   ├── Metal/            # Metal rendering backend (macOS)
│   ├── DirectX9/         # DirectX 9 backend (Windows)
│   └── Validation/       # Debug validation layer
├── Editor/
│   ├── UI/               # Docking system and UI renderer
│   ├── Shaders/          # Editor shaders (.metal, .hlsl)
│   └── Program.cs        # Main editor application
└── Physics/              # Physics simulation (WIP)

TeaScript/
├── Frontend/             # Lexer, parser, AST
├── Runtime/              # Interpreter and environment
├── Bridge/               # Engine integration
├── Examples/             # Example scripts
│   ├── simple.tea        # Basic syntax
│   ├── advanced.tea      # Arrays, loops, logic
│   ├── moving_teapot.tea # Circular motion
│   └── player.tea        # Player controller
└── README.md             # Language documentation
```

## 🎮 Editor Controls

### Viewport Camera
- **Right Mouse + Drag** - Rotate camera
- **WASD** - Move camera
- **Q/E** - Move up/down
- **Scroll** - Zoom in/out

### Editor Shortcuts
- **Cmd+I** (macOS) / **Ctrl+I** (Windows) - Import assets
- **Play ▶️** - Start simulation (scripts run)
- **Pause ⏸️** - Pause simulation
- **Stop ⏹️** - Stop and restore editor state
- **Double-click .tea** - Open script editor

## 🔧 Development Status

### ✅ Complete
- Core ECS architecture
- Cross-platform windowing (macOS, Windows)
- Metal rendering backend (macOS)
- DirectX 9 rendering backend (Windows)
- Asset import pipeline (OBJ, textures)
- Editor UI with docking system
- **TeaScript language and runtime**
- **Play mode with scene snapshots**
- Content browser with drag-drop
- 3D viewport with camera controls

### 🚧 In Progress
- Physics simulation
- Animation system
- Audio system

### 📋 Planned
- Visual scripting (Blueprints)
- Terrain system
- Particle effects
- Post-processing effects
- Profiler and debugging tools
- Linux support (Vulkan)

## 🤝 Contributing

This is a learning project, but contributions are welcome! Whether you want to:
- Add new TeaScript functions
- Improve the editor UI
- Fix bugs
- Write documentation
- Create example projects

Feel free to open issues or pull requests!

## 📖 Learning Resources

### For Engine Developers
- `BlueSkyEngine/Core/ECS/` - Study the ECS implementation
- `BlueSkyEngine/RHI/` - Learn about rendering abstraction
- `BlueSkyEngine/Platform/` - See cross-platform windowing

### For Game Developers
- `TeaScript/Examples/` - Learn scripting by example
- `TeaScripting_GettingStarted.md` - Step-by-step tutorial
- `TeaScript/README.md` - Complete language reference

## 📝 License

MIT License - See LICENSE file for details

## 🙏 Credits

Built from scratch with passion for:
- Game engine architecture
- Low-level systems programming
- Language design and implementation
- Cross-platform development

Special thanks to the .NET team for the amazing runtime!

---

**Welcome to BlueSky Engine - Where gameplay comes to life with TeaScript!** ☁️✨☕

*Start scripting today and bring your game ideas to reality!*
