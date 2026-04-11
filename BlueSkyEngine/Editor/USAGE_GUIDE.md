# BlueSky Editor - ECS Integration Usage Guide

## Quick Start

The BlueSky Engine Editor now displays entities from the ECS World in real-time. When you launch the editor, you'll see:

- **World Outliner** (left panel): Lists all entities in the world
- **Details** (right panel): Shows components of the selected entity
- **Viewport** (center): 3D scene view
- **Content Browser** (bottom left): Asset management

## Keyboard Shortcuts

- **Ctrl+N**: Create a new entity
- **Delete**: Delete the selected entity

## Features

### World Outliner (Hierarchy)
The World Outliner displays all entities from the ECS World. Each entity shows:
- Entity name (from NameComponent) or "Entity {ID}" if no name
- Visual selection highlight (blue background)
- Alternating row colors for readability

**How to use:**
1. Click any entity to select it
2. The Details panel will update to show that entity's components
3. Press Ctrl+N to create a new entity
4. Press Delete to remove the selected entity

### Details Panel (Inspector)
The Details panel shows all components attached to the selected entity:

**Displayed Components:**
- **Name Component**: Entity name
- **Transform Component**: Position, rotation (quaternion), scale
- **Mesh Component**: Vertex count, index count, material ID
- **Camera Component**: Field of view, near/far planes, orthographic mode
- **Light Component**: Type (directional/point/spot), color, intensity, range
- **Rigidbody Component**: Mass, gravity, kinematic mode
- **Static Mesh Component**: Mesh asset ID, material asset ID

**How to use:**
1. Select an entity in the World Outliner
2. The Details panel automatically updates
3. View all component properties (currently read-only)

### Test Entities

Four test entities are created on startup:

1. **Main Camera**
   - Position: (0, 2, -5)
   - Has: NameComponent, TransformComponent, CameraComponent
   - FOV: 60°, Near: 0.1, Far: 1000

2. **Directional Light**
   - Position: (0, 5, 0)
   - Has: NameComponent, TransformComponent, LightComponent
   - Color: White (1, 1, 1), Intensity: 1.0

3. **Cube**
   - Position: (0, 0, 0)
   - Has: NameComponent, TransformComponent, StaticMeshComponent
   - Mesh: cube.obj, Material: default.mat

4. **Sphere**
   - Position: (3, 0, 0)
   - Has: NameComponent, TransformComponent, StaticMeshComponent, RigidbodyComponent
   - Mesh: sphere.obj, Material: default.mat
   - Mass: 1.0, Gravity: enabled

## Technical Details

### Data Flow
The ECS World is the single source of truth. The UI panels query the World to display entity data:

```
World.GetAllEntities() → HierarchyPanelController → EntityListItem widgets
                                    ↓
                            User clicks entity
                                    ↓
                         OnEntitySelected event
                                    ↓
                    InspectorPanelController.SetSelectedEntity()
                                    ↓
                World.HasComponent<T>() and World.GetComponent<T>()
                                    ↓
                    Component data displayed in Details panel
```

### Refresh Strategy
- **Hierarchy**: Refreshes every 1 second to catch new entities
- **Inspector**: Refreshes immediately when selection changes
- **Manual Refresh**: Triggered after create/delete operations

### Component Display Format
Each component is displayed as:
```
┌─────────────────────────────┐
│ Component Name              │ ← Blue header
├─────────────────────────────┤
│   Property: Value           │
│   Property: Value           │
│   Property: Value           │
└─────────────────────────────┘
```

## Limitations (Current Implementation)

- Component properties are read-only (no editing)
- No add/remove component buttons
- No viewport entity picking
- No drag-and-drop reparenting
- No context menus
- No entity search/filter
- No multi-selection
- No undo/redo

## Future Enhancements

See IMPLEMENTATION_SUMMARY.md for the full list of planned features.
