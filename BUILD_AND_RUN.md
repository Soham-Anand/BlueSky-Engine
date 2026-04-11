# Build and Run BlueSky Editor with ECS Integration

## Executables Generated

✅ **macOS**: `Build/macOS_Editor/BlueSkyEngine` (123 KB)
✅ **Windows**: `Build/Windows_Editor/BlueSkyEngine.exe` (148 KB)

## Running the Editor

### macOS
```bash
cd Build/macOS_Editor
./BlueSkyEngine
```

### Windows
```bash
cd Build\Windows_Editor
BlueSkyEngine.exe
```

## What You'll See

When you launch the editor, you'll see the Unreal-style UI with:

1. **Top Menu Bar** - Shows "BlueSky Engine Editor" and FPS counter
2. **World Outliner** (left) - Lists 4 test entities:
   - Main Camera
   - Directional Light
   - Cube
   - Sphere
3. **Viewport** (center) - 3D scene view with "[Perspective]" label
4. **Details** (right) - Shows components when you select an entity
5. **Content Browser** (bottom left) - Asset management (placeholder)

## Try It Out

1. **Select an entity**: Click "Main Camera" in the World Outliner
   - The Details panel will show its components (Name, Transform, Camera)

2. **Select another entity**: Click "Sphere" in the World Outliner
   - Details panel updates to show Sphere's components (Name, Transform, StaticMesh, Rigidbody)

3. **Create a new entity**: Press `Ctrl+N`
   - A new entity appears in the World Outliner
   - It's automatically selected in the Details panel

4. **Delete an entity**: Select any entity and press `Delete`
   - The entity is removed from the World Outliner
   - Details panel clears

## Rebuilding

If you make changes to the code:

```bash
# Build only
dotnet build BlueSkyEngine/BlueSkyEngine.csproj -c Release

# Publish for macOS
dotnet publish BlueSkyEngine/BlueSkyEngine.csproj -c Release -r osx-arm64 --self-contained -o Build/macOS_Editor

# Publish for Windows
dotnet publish BlueSkyEngine/BlueSkyEngine.csproj -c Release -r win-x64 --self-contained -o Build/Windows_Editor
```

## ECS Integration Features

- ✅ World Outliner displays entities from ECS World
- ✅ Details panel shows all entity components
- ✅ Entity selection synchronization
- ✅ Create entities (Ctrl+N)
- ✅ Delete entities (Delete key)
- ✅ Real-time hierarchy refresh
- ✅ Component property display

## Documentation

See these files for more details:
- `BlueSkyEngine/Editor/ECS_INTEGRATION_README.md` - Technical details
- `BlueSkyEngine/Editor/USAGE_GUIDE.md` - How to use the editor
- `BlueSkyEngine/Editor/ARCHITECTURE.md` - System architecture
- `BlueSkyEngine/Editor/IMPLEMENTATION_SUMMARY.md` - What was built
