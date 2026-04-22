# BlueSky Engine ☁️

A modern, cross-platform 3D game engine built from scratch in C# with custom ECS architecture, multi-backend rendering, and **TeaScript** - a custom scripting language designed for gameplay programming.

![BlueSky Engine](https://img.shields.io/badge/Platform-macOS%20%7C%20Windows%20%7C%20Linux-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/License-MIT-green)

---

## ✨ Features

### 🎮 TeaScript - Custom Scripting Language
- **Easy to Learn** - Simple syntax designed for game development
- **Hot Reload** - Update scripts without restarting the engine
- **ECS Integration** - Direct access to entities, components, and transforms
- **Play Mode Isolation** - Test scripts without modifying your scene
- **Built-in Functions** - Math, transforms, time, and debug logging

👉 **[Get Started with TeaScript](TEASCRIPT_GETTING_STARTED.md)**

### 🏗️ Core Engine
- **Custom ECS** - High-performance archetype-based entity management
- **Cross-Platform** - Native backends for macOS (Cocoa), Windows (Win32), Linux (Wayland)
- **Multi-Backend Rendering** - Metal (macOS), DirectX 9 (Windows), Vulkan (planned)
- **Asset Pipeline** - Import OBJ models, textures, and materials with `.blueskyasset` format
- **Scene Snapshots** - Unity-style Play/Pause/Stop that preserves editor state

### 🎨 Modern Editor
- **Docking System** - Flexible panel layout with drag-and-drop
- **3D Viewport** - Interactive scene view with camera controls
- **Content Browser** - Asset management with type indicators and drag-drop
- **Material Editor** - Visual PBR material editing with real-time preview
- **Script Editor** - Built-in editor for TeaScript files
- **Animated UI** - Smooth transitions and toast notifications
- **Performance Monitor** - Real-time FPS and frame time tracking (press F3)

### 🌟 Rendering
- **PBR Lighting** - Physically-based rendering with metallic/roughness workflow
- **Shadow Mapping** - High-resolution directional shadows
- **Procedural Sky** - Atmospheric sky with clouds
- **Material System** - Full PBR materials with texture support
- **Grid Overlay** - Editor grid with axis coloring

---

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

# Build and run
dotnet build BlueSkyEngine/BlueSkyEngine.csproj
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

5. **Drag the script** onto an entity in the viewport
6. **Press Play** ▶️ and watch it move!
7. **Press Stop** ⏹️ to return to editor mode

---

## 📚 Documentation

- **[TeaScript Getting Started](TEASCRIPT_GETTING_STARTED.md)** - Complete tutorial for scripting
- **[TeaScript Language Reference](TeaScript/README.md)** - Full language documentation

---

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

---

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
├── Rendering/
│   ├── Materials/        # PBR material system
│   ├── Viewport.cs       # 3D viewport rendering
│   └── GI/               # Global illumination (WIP)
├── Editor/
│   ├── UI/               # Modern UI system with animations
│   ├── Shaders/          # Editor shaders (.metal, .hlsl)
│   └── Program.cs        # Main editor application
└── Physics/              # Physics simulation (WIP)

TeaScript/
├── Frontend/             # Lexer, parser, AST
├── Runtime/              # Interpreter and environment
├── Bridge/               # Engine integration
└── Examples/             # Example scripts
```

---

## 🎮 Editor Controls

### Viewport Camera
- **Right Mouse + Drag** - Rotate camera
- **WASD** - Move camera
- **Q/E** - Move up/down
- **Scroll** - Zoom in/out

### Editor Shortcuts
- **Cmd+I** (macOS) / **Ctrl+I** (Windows) - Import assets
- **F3** - Toggle performance overlay
- **Play ▶️** - Start simulation (scripts run)
- **Pause ⏸️** - Pause simulation
- **Stop ⏹️** - Stop and restore editor state
- **Double-click .tea** - Open script editor
- **Double-click .blueskyasset (Material)** - Open material editor

---

## 🔧 Development Status

### ✅ Complete
- Core ECS architecture
- Cross-platform windowing (macOS, Windows)
- Metal rendering backend (macOS)
- DirectX 9 rendering backend (Windows)
- Asset import pipeline (OBJ, textures, materials)
- Modern editor UI with animations
- **TeaScript language and runtime**
- **Play mode with scene snapshots**
- **Material editor with PBR support**
- Content browser with drag-drop
- 3D viewport with camera controls
- Toast notifications and performance monitoring

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
- Networking

---

## 🎨 UI Features

### Modern Interface
- **Smooth Animations** - Eased transitions on all interactions
- **Toast Notifications** - Success, error, warning, and info messages
- **Performance Overlay** - Real-time FPS, frame time, and draw call tracking
- **Animated Buttons** - Hover effects, press feedback, and glow
- **Professional Theme** - 5-layer color system with semantic colors

### Asset Management
- **Content Browser** - Visual asset browser with icons and badges
- **Material Editor** - Real-time PBR material editing
- **Script Editor** - Built-in TeaScript editor with live editing
- **Drag & Drop** - Drag assets onto entities to attach them
- **Context Menus** - Right-click for quick actions

---

## 🤝 Contributing

This is a learning project, but contributions are welcome! Whether you want to:
- Add new TeaScript functions
- Improve the editor UI
- Fix bugs
- Write documentation
- Create example projects

Feel free to open issues or pull requests!

---

## 📖 Learning Resources

### For Engine Developers
- `BlueSkyEngine/Core/ECS/` - Study the ECS implementation
- `BlueSkyEngine/RHI/` - Learn about rendering abstraction
- `BlueSkyEngine/Platform/` - See cross-platform windowing

### For Game Developers
- `TeaScript/Examples/` - Learn scripting by example
- `TEASCRIPT_GETTING_STARTED.md` - Step-by-step tutorial
- `TeaScript/README.md` - Complete language reference

---

## 📝 License

MIT License - See LICENSE file for details

---

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
