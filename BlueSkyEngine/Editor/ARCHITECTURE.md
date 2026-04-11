# BlueSky Editor Architecture with ECS Integration

## System Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                      BlueSky Engine Editor                       │
│                         (Program.cs)                             │
└─────────────────────────────────────────────────────────────────┘
                                 │
                    ┌────────────┴────────────┐
                    │                         │
         ┌──────────▼──────────┐   ┌─────────▼──────────┐
         │    ECS World        │   │   UI System        │
         │  (Source of Truth)  │   │  (NotBSUI)         │
         └──────────┬──────────┘   └─────────┬──────────┘
                    │                        │
         ┌──────────┴──────────┐            │
         │                     │            │
    ┌────▼────┐          ┌────▼────┐       │
    │Entities │          │Systems  │       │
    └────┬────┘          └─────────┘       │
         │                                  │
    ┌────▼────────────────────┐            │
    │     Components          │            │
    │  - NameComponent        │            │
    │  - TransformComponent   │            │
    │  - MeshComponent        │            │
    │  - CameraComponent      │            │
    │  - LightComponent       │            │
    │  - RigidbodyComponent   │            │
    │  - StaticMeshComponent  │            │
    └─────────────────────────┘            │
                                           │
                    ┌──────────────────────┴──────────────────────┐
                    │                                              │
         ┌──────────▼──────────────┐              ┌───────────────▼──────────────┐
         │ HierarchyPanelController│              │ InspectorPanelController     │
         │  (World Outliner)       │              │  (Details Panel)             │
         └──────────┬──────────────┘              └───────────────┬──────────────┘
                    │                                              │
         ┌──────────▼──────────────┐              ┌───────────────▼──────────────┐
         │  UIPanel (Hierarchy)    │              │  UIPanel (Inspector)         │
         │  ┌──────────────────┐   │              │  ┌──────────────────────┐   │
         │  │ EntityListItem   │   │              │  │ Component Sections   │   │
         │  │ EntityListItem   │   │              │  │ - Name               │   │
         │  │ EntityListItem   │   │              │  │ - Transform          │   │
         │  │ EntityListItem   │   │              │  │ - Mesh               │   │
         │  │ ...              │   │              │  │ - Camera             │   │
         │  └──────────────────┘   │              │  │ - Light              │   │
         └─────────────────────────┘              │  │ - Rigidbody          │   │
                                                  │  │ - StaticMesh         │   │
                                                  │  └──────────────────────┘   │
                                                  └──────────────────────────────┘
```

## Component Interaction Flow

### Entity Selection Flow
```
User clicks entity in World Outliner
    ↓
EntityListItem.ContainsPoint() detects click
    ↓
HierarchyPanelController.ProcessInput() handles click
    ↓
HierarchyPanelController.SelectEntity(entity)
    ↓
EntityListItem.IsSelected = true (visual feedback)
    ↓
OnEntitySelected event fires
    ↓
InspectorPanelController.SetSelectedEntity(entity)
    ↓
InspectorPanelController.Refresh()
    ↓
World.HasComponent<T>() checks for each component type
    ↓
World.GetComponent<T>() retrieves component data
    ↓
AddComponentSection() creates UI sections
    ↓
AddPropertyLabel() creates property rows
    ↓
Details panel displays entity components
```

### Entity Creation Flow
```
User presses Ctrl+N
    ↓
Program.OnUpdate() detects keyboard input
    ↓
HierarchyPanelController.CreateEntity("Entity X")
    ↓
World.CreateEntity() creates new entity
    ↓
World.AddComponent<NameComponent>(entity, name)
    ↓
World.AddComponent<TransformComponent>(entity, default)
    ↓
HierarchyPanelController.Refresh()
    ↓
New EntityListItem created and added to panel
    ↓
HierarchyPanelController.SelectEntity(entity)
    ↓
Inspector updates to show new entity
```

### Entity Deletion Flow
```
User presses Delete key
    ↓
Program.OnUpdate() detects keyboard input
    ↓
HierarchyPanelController.DeleteSelectedEntity()
    ↓
World.DestroyEntity(selectedEntity)
    ↓
Entity removed from all archetypes
    ↓
HierarchyPanelController.Refresh()
    ↓
EntityListItem removed from panel
    ↓
OnEntitySelected event fires with null
    ↓
Inspector clears selection
```

## UI Panel Layout

```
┌─────────────────────────────────────────────────────────────────────┐
│  Top Menu Bar                                          FPS: 60      │
├──────────────┬──────────────────────────────────────┬───────────────┤
│              │                                      │               │
│  World       │                                      │   Details     │
│  Outliner    │                                      │   Panel       │
│              │                                      │               │
│ ┌──────────┐ │          Viewport                   │ Entity 1      │
│ │Entity 1  │ │      [Perspective]                  │ (Gen 1)       │
│ │Entity 2  │ │                                      │               │
│ │Entity 3  │ │                                      │ Name Comp     │
│ │Entity 4  │ │                                      │  Name: Cube   │
│ └──────────┘ │                                      │               │
│              │                                      │ Transform     │
├──────────────┤                                      │  Pos: (0,0,0) │
│              │                                      │  Rot: (0,0,0) │
│  Content     │                                      │  Scale: (1,1) │
│  Browser     │                                      │               │
│              │                                      │ Mesh Comp     │
│              │                                      │  Verts: 24    │
└──────────────┴──────────────────────────────────────┴───────────────┘
```

## Key Classes and Responsibilities

### Program.cs
- Main entry point and editor loop
- Creates and manages all UI panels
- Initializes ECS World and systems
- Handles keyboard shortcuts
- Coordinates between controllers

### HierarchyPanelController
- Queries World.GetAllEntities()
- Creates EntityListItem for each entity
- Manages entity selection state
- Handles entity creation/deletion
- Fires OnEntitySelected events

### InspectorPanelController
- Listens to OnEntitySelected events
- Queries World for entity components
- Displays component data in formatted sections
- Updates UI when selection changes

### EntityListItem
- Custom UILabel widget
- Represents one entity in the list
- Draws selection highlight
- Handles click detection

## Data Ownership

- **ECS World**: Owns all entity and component data
- **Controllers**: Own UI state (selection, scroll position, etc.)
- **UI Panels**: Own visual representation only
- **No Duplication**: Entity data is never duplicated, always queried from World

## Performance Considerations

- Hierarchy refreshes every 1 second to catch new entities
- Inspector only refreshes on selection change
- Entity list items are recreated on each refresh (could be optimized)
- Component queries use World.HasComponent() and World.GetComponent()
- No caching of component data (always fresh from World)
