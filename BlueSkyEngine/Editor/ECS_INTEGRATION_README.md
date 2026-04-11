# ECS Integration with Unreal-Style Editor UI

## Overview

The BlueSky Engine Editor now has full ECS (Entity Component System) integration with the Unreal-style UI. The World Outliner (hierarchy panel) displays all entities from the ECS World, and the Details panel (inspector) shows their components and properties.

## Features Implemented

### 1. World Outliner (Hierarchy Panel)
- **Entity Display**: Shows all entities from `World.GetAllEntities()`
- **Entity Names**: Displays entity names from `NameComponent`, falls back to "Entity {ID}"
- **Selection**: Click any entity to select it
- **Visual Feedback**: Selected entities are highlighted in blue
- **Alternating Rows**: Even rows have slightly darker background for readability

### 2. Details Panel (Inspector)
- **Component Display**: Shows all components attached to the selected entity
- **Supported Components**:
  - NameComponent (entity name)
  - TransformComponent (position, rotation, scale)
  - MeshComponent (vertex/index counts, material ID)
  - CameraComponent (FOV, near/far planes, orthographic mode)
  - LightComponent (type, color, intensity, range)
  - RigidbodyComponent (mass, gravity, kinematic mode)
  - StaticMeshComponent (mesh and material asset IDs)
- **Real-time Updates**: Inspector refreshes when entity selection changes

### 3. Entity Management
- **Create Entity**: Press `Ctrl+N` to create a new entity with default Transform
- **Delete Entity**: Press `Delete` to remove the selected entity
- **Test Entities**: 4 test entities are created on startup:
  - Main Camera (with CameraComponent)
  - Directional Light (with LightComponent)
  - Cube (with StaticMeshComponent)
  - Sphere (with StaticMeshComponent and RigidbodyComponent)

## Architecture

```
Program.cs (Main Editor)
    ├── World (ECS)
    │   ├── Entities
    │   └── Components
    ├── HierarchyPanelController
    │   ├── Displays entities from World
    │   ├── Handles entity selection
    │   └── Fires OnEntitySelected event
    └── InspectorPanelController
        ├── Listens to OnEntitySelected
        └── Displays entity components
```

## Usage

### Viewing Entities
1. Launch the editor
2. Look at the "World Outliner" panel on the left
3. You'll see all entities in the world listed

### Selecting Entities
1. Click on any entity in the World Outliner
2. The Details panel on the right will update to show that entity's components

### Creating Entities
1. Press `Ctrl+N` anywhere in the editor
2. A new entity will be created with a default Transform component
3. The hierarchy will refresh and the new entity will be selected

### Deleting Entities
1. Select an entity in the World Outliner
2. Press `Delete` key
3. The entity will be removed from the world
4. The hierarchy will refresh

## Code Structure

### HierarchyPanelController.cs
- `Refresh()`: Rebuilds the entity list from World
- `SelectEntity(Entity)`: Selects an entity and fires event
- `CreateEntity(string)`: Creates a new entity with name
- `DeleteSelectedEntity()`: Removes the selected entity
- `ProcessInput(Vector2, bool)`: Handles mouse clicks for selection

### InspectorPanelController.cs
- `SetSelectedEntity(Entity?)`: Updates the inspected entity
- `Refresh()`: Rebuilds the component display
- `AddComponentSection(string, float)`: Adds a component header
- `AddPropertyLabel(string, string, float)`: Adds a property row

### EntityListItem.cs (in HierarchyPanelController.cs)
- Custom UILabel that represents an entity in the list
- Draws selection highlight and alternating row colors
- Contains Entity reference for click handling

## Next Steps

To fully complete the ECS integration, consider:

1. **Component Editing**: Add editable fields in the inspector (currently read-only)
2. **Add/Remove Components**: Add buttons to add/remove components from entities
3. **Viewport Selection**: Wire up viewport clicks to select entities via raycasting
4. **Drag-and-Drop**: Support dragging entities to reparent them
5. **Context Menus**: Right-click menus for entity operations
6. **Entity Filtering**: Search/filter entities by name
7. **Multi-Selection**: Ctrl+click to select multiple entities
8. **Undo/Redo**: Track entity and component changes for undo

## Testing

To test the integration:
1. Build and run the editor
2. Verify 4 test entities appear in World Outliner
3. Click each entity and verify Details panel updates
4. Press Ctrl+N to create a new entity
5. Select the new entity and press Delete to remove it
6. Verify the hierarchy updates correctly

## Technical Notes

- The hierarchy refreshes every second to catch any entities created by systems
- Entity selection is synchronized between hierarchy and inspector via events
- All component data is read-only in the current implementation
- The ECS World is the single source of truth for all entity data
