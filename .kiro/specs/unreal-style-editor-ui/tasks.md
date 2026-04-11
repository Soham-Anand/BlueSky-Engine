# Implementation Plan: Unreal-Style Editor UI

## Overview

This implementation plan creates a professional Unreal Engine-style editor UI for the BlueSky Engine with dockable panels, 3D viewport, hierarchy, inspector, content browser, and console. The plan begins by backing up the working DirectX 9 rendering logic as a safety measure, then builds the UI framework and editor panels incrementally.

## Tasks

- [ ] 1. Backup DirectX 9 rendering logic to BlueSky.RHI.Test
  - [ ] 1.1 Create backup directory structure in BlueSky.RHI.Test
    - Create `BlueSky.RHI.Test/Backup/` directory
    - Create backup manifest data structure
    - _Requirements: 10.1_
  
  - [ ] 1.2 Implement BackupSystem class with file copying and hash verification
    - Create `BackupSystem.cs` with methods for identifying, copying, and verifying files
    - Implement SHA256 hash calculation for file integrity
    - Implement backup manifest generation (JSON format)
    - _Requirements: 10.2, 10.3, 10.4_
  
  - [ ]* 1.3 Write property test for backup file integrity
    - **Property 9: Backup File Integrity**
    - **Validates: Requirements 10.3**
    - Generate random file content, backup, verify hash matches
  
  - [ ]* 1.4 Write property test for backup manifest completeness
    - **Property 10: Backup Manifest Completeness**
    - **Validates: Requirements 10.4**
    - Verify manifest contains all expected files with metadata
  
  - [ ] 1.5 Execute backup operation for DirectX 9 rendering files
    - Identify all D3D9*.cs files in BlueSkyEngine project
    - Run backup operation and verify success
    - Save backup manifest to `BlueSky.RHI.Test/Backup/backup_manifest.json`
    - _Requirements: 10.1, 10.2, 10.6_

- [ ] 2. Checkpoint - Verify backup completed successfully
  - Ensure backup manifest exists and all files are verified, ask the user if questions arise.

- [ ] 3. Create core docking system infrastructure
  - [ ] 3.1 Define IEditorPanel interface and PanelFlags enum
    - Create `IEditorPanel.cs` with all required properties and methods
    - Define PanelFlags enum with Dockable, Resizable, Closable, HasTitleBar flags
    - _Requirements: 2.1_
  
  - [ ] 3.2 Implement DockNode data model
    - Create `DockNode.cs` with Type, Position, Size, Parent, Children, Panel, SplitDirection, SplitRatio
    - Implement validation rules for split ratios (0.1 to 0.9)
    - _Requirements: 2.5_
  
  - [ ] 3.3 Implement DockingSystem class with panel management
    - Create `DockingSystem.cs` implementing IDockingSystem interface
    - Implement Initialize, AddPanel, RemovePanel methods
    - Implement docking tree construction and validation
    - _Requirements: 2.1, 2.6_
  
  - [ ]* 3.4 Write property test for docking tree validity
    - **Property 5: Docking Tree Validity**
    - **Validates: Requirements 2.5**
    - Generate random docking operations, verify split nodes have two children and valid ratios
  
  - [ ]* 3.5 Write property test for panel addition correctness
    - **Property 6: Panel Addition Correctness**
    - **Validates: Requirements 2.1**
    - Add panels at various positions, verify they appear in correct location in tree

- [ ] 4. Implement docking system resize and layout logic
  - [ ] 4.1 Implement ResizePanels algorithm
    - Add ResizePanels method to DockingSystem
    - Implement recursive resize with split ratio calculations
    - Enforce minimum size constraints
    - _Requirements: 2.4, 3.1, 3.3_
  
  - [ ] 4.2 Implement window resize handling
    - Add Resize method to DockingSystem
    - Call ResizePanels on root node when window resizes
    - _Requirements: 2.4, 3.2_
  
  - [ ]* 4.3 Write property test for panel layout consistency
    - **Property 1: Panel Layout Consistency**
    - **Validates: Requirements 3.4**
    - Generate random window sizes and panel configurations, verify all panels fit within bounds
  
  - [ ]* 4.4 Write property test for minimum size enforcement
    - **Property 2: Minimum Size Enforcement**
    - **Validates: Requirements 3.1**
    - Resize panels to various sizes, verify minimum size is always respected
  
  - [ ]* 4.5 Write property test for proportional resize
    - **Property 8: Proportional Resize**
    - **Validates: Requirements 2.4**
    - Resize window, verify panels resize proportionally according to split ratios

- [ ] 5. Implement drag-and-drop docking operations
  - [ ] 5.1 Implement BeginDrag, UpdateDrag, EndDrag methods
    - Add drag state tracking to DockingSystem
    - Implement CalculateDockZone helper method
    - Implement visual feedback for dock zones
    - _Requirements: 2.2, 2.3_
  
  - [ ] 5.2 Implement ProcessDocking algorithm
    - Add ProcessDocking method with target panel detection
    - Implement dock zone calculation (left, right, top, bottom, center)
    - Update docking tree when drag completes
    - _Requirements: 2.2, 2.3_
  
  - [ ]* 5.3 Write unit tests for docking operations
    - Test drag-and-drop to each dock zone
    - Test docking tree updates after drop
    - Verify visual feedback during drag

- [ ] 6. Implement layout persistence
  - [ ] 6.1 Create EditorLayout and PanelState data models
    - Create `EditorLayout.cs` with Name, RootNode, PanelStates
    - Create `PanelState.cs` with PanelType, Position, Size, IsVisible, CustomData
    - _Requirements: 9.5_
  
  - [ ] 6.2 Implement SaveLayout and LoadLayout methods
    - Add SaveLayout method to serialize docking tree to JSON
    - Add LoadLayout method to deserialize and reconstruct layout
    - Implement layout validation before loading
    - _Requirements: 9.1, 9.2, 9.4_
  
  - [ ]* 6.3 Write property test for layout serialization round-trip
    - **Property 34: Layout Serialization Round-Trip**
    - **Validates: Requirements 9.1, 9.2, 9.5**
    - Generate random layouts, save, load, verify equivalence
  
  - [ ]* 6.4 Write property test for layout validation
    - **Property 35: Layout Validation**
    - **Validates: Requirements 9.4**
    - Generate malformed layout files, verify they are rejected

- [ ] 7. Checkpoint - Verify docking system functionality
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 8. Create UIRenderer for panel rendering
  - [ ] 8.1 Implement UIRenderer class with RHI integration
    - Create `UIRenderer.cs` with DrawPanel, DrawRect, DrawText methods
    - Integrate with NotBSUI framework
    - Create UI rendering pipeline (vertex/fragment shaders)
    - _Requirements: 1.3_
  
  - [ ] 8.2 Implement FontAtlas for text rendering
    - Create `FontAtlas.cs` using StbTrueTypeSharp
    - Generate font texture atlas
    - Implement text measurement and rendering
    - _Requirements: 1.3_
  
  - [ ] 8.3 Implement DrawPanel method with title bar and content area
    - Draw panel background rectangle
    - Draw title bar if HasTitleBar flag is set
    - Call panel.OnDraw() for content rendering
    - _Requirements: 2.1_

- [ ] 9. Implement ViewportPanel for 3D scene rendering
  - [ ] 9.1 Create ViewportPanel class implementing IEditorPanel
    - Define Camera, Scene, GizmoMode, RenderTarget properties
    - Implement OnUpdate, OnDraw, OnResize methods
    - _Requirements: 4.1_
  
  - [ ] 9.2 Implement offscreen render target for viewport
    - Create render target texture matching panel size
    - Resize render target when panel resizes
    - Render 3D scene to render target
    - Display render target as texture in panel
    - _Requirements: 4.1, 4.2_
  
  - [ ]* 9.3 Write property test for render target dimension matching
    - **Property 11: Render Target Dimension Matching**
    - **Validates: Requirements 4.2**
    - Resize viewport panel, verify render target dimensions match
  
  - [ ] 9.4 Implement camera controls (WASD movement, mouse orbit)
    - Handle keyboard input for WASD camera movement
    - Handle mouse input for camera rotation (right-click drag)
    - Implement SetCameraMode for perspective/orthographic switching
    - _Requirements: 4.3, 4.4_
  
  - [ ] 9.5 Implement object picking via raycasting
    - Implement ScreenPointToRay method
    - Perform raycast against scene entities on mouse click
    - Notify HierarchyPanel of selected entity
    - _Requirements: 4.5, 4.6_
  
  - [ ]* 9.6 Write property test for viewport selection notification
    - **Property 12: Viewport Selection Notification**
    - **Validates: Requirements 4.6**
    - Simulate viewport clicks, verify hierarchy panel receives selection notifications
  
  - [ ] 9.7 Add viewport statistics display (FPS, draw calls)
    - Calculate and display frame time and FPS
    - Display draw call count and triangle count
    - Render statistics in viewport corner
    - _Requirements: 4.7_

- [ ] 10. Implement HierarchyPanel for scene entity tree
  - [ ] 10.1 Create HierarchyPanel class implementing IEditorPanel
    - Define Scene, SelectedEntities properties
    - Implement OnUpdate, OnDraw methods
    - _Requirements: 5.1_
  
  - [ ] 10.2 Implement entity tree rendering
    - Render entities in tree structure with indentation
    - Display entity names and icons
    - Implement tree expand/collapse functionality
    - _Requirements: 5.1_
  
  - [ ] 10.3 Implement entity selection (single and multi-select)
    - Implement SelectEntity method with addToSelection parameter
    - Handle click for single selection
    - Handle Ctrl+click for multi-selection
    - Notify InspectorPanel of selection changes
    - _Requirements: 5.2, 5.3, 5.4_
  
  - [ ]* 10.4 Write property test for selection synchronization
    - **Property 4: Selection Synchronization**
    - **Validates: Requirements 5.4**
    - Select entities in hierarchy, verify inspector shows same selection
  
  - [ ]* 10.5 Write property test for single selection exclusivity
    - **Property 13: Single Selection Exclusivity**
    - **Validates: Requirements 5.2**
    - Click entity without Ctrl, verify only that entity is selected
  
  - [ ]* 10.6 Write property test for multi-selection addition
    - **Property 14: Multi-Selection Addition**
    - **Validates: Requirements 5.3**
    - Ctrl+click entities, verify they are added to selection without clearing
  
  - [ ] 10.7 Implement drag-and-drop reparenting
    - Handle drag start on entity
    - Display drop target feedback
    - Update entity parent on drop
    - _Requirements: 5.5_
  
  - [ ]* 10.8 Write property test for entity reparenting
    - **Property 15: Entity Reparenting**
    - **Validates: Requirements 5.5**
    - Drag entity onto another, verify parent is updated
  
  - [ ] 10.9 Implement context menu for entity operations
    - Display context menu on right-click
    - Add menu items: Rename, Delete, Duplicate, Create Child
    - Implement RenameEntity, DeleteSelected, DuplicateSelected, CreateEntity methods
    - _Requirements: 5.6_
  
  - [ ] 10.10 Implement entity filtering by name
    - Add filter text input field
    - Filter entity display based on name contains filter text
    - _Requirements: 5.7_
  
  - [ ]* 10.11 Write property test for hierarchy filtering
    - **Property 16: Hierarchy Filtering**
    - **Validates: Requirements 5.7**
    - Apply filter, verify only matching entities are displayed

- [ ] 11. Checkpoint - Verify viewport and hierarchy functionality
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 12. Implement InspectorPanel for property editing
  - [ ] 12.1 Create InspectorPanel class implementing IEditorPanel
    - Define SelectedEntities property
    - Implement OnUpdate, OnDraw methods
    - _Requirements: 6.1_
  
  - [ ] 12.2 Implement property display for selected entities
    - Iterate through entity components and properties
    - Render appropriate editor widgets for each property type (int, float, string, Vector3, etc.)
    - Display component headers with expand/collapse
    - _Requirements: 6.1_
  
  - [ ]* 12.3 Write property test for property display completeness
    - **Property 17: Property Display Completeness**
    - **Validates: Requirements 6.1**
    - Select entity, verify all properties are displayed
  
  - [ ] 12.4 Implement immediate property updates
    - Handle property value changes from editor widgets
    - Update entity properties immediately on change
    - _Requirements: 6.2_
  
  - [ ]* 12.5 Write property test for immediate property updates
    - **Property 18: Immediate Property Updates**
    - **Validates: Requirements 6.2**
    - Change property value, verify entity property is updated immediately
  
  - [ ] 12.6 Implement multi-object editing
    - Display common properties when multiple entities selected
    - Show mixed values indicator when properties differ
    - Apply changes to all selected entities
    - _Requirements: 6.3_
  
  - [ ] 12.7 Implement Add Component functionality
    - Add "Add Component" button
    - Display component type selection menu
    - Create component on selected entity
    - Refresh property display
    - _Requirements: 6.4, 6.5_
  
  - [ ]* 12.8 Write property test for component addition
    - **Property 19: Component Addition**
    - **Validates: Requirements 6.5**
    - Add component, verify it exists on entity
  
  - [ ] 12.9 Implement Remove Component functionality
    - Add remove button to component headers
    - Delete component from entity
    - Refresh property display
    - _Requirements: 6.6_
  
  - [ ]* 12.10 Write property test for component removal
    - **Property 20: Component Removal**
    - **Validates: Requirements 6.6**
    - Remove component, verify it no longer exists on entity

- [ ] 13. Implement ContentBrowserPanel for asset management
  - [ ] 13.1 Create ContentBrowserPanel class implementing IEditorPanel
    - Define CurrentDirectory, SelectedAssets properties
    - Create AssetEntry data model with Name, Path, Type, Thumbnail
    - Implement OnUpdate, OnDraw methods
    - _Requirements: 7.1_
  
  - [ ] 13.2 Implement asset directory display
    - Scan current directory for assets
    - Display assets in grid layout with thumbnails
    - Display folders and files with appropriate icons
    - _Requirements: 7.1_
  
  - [ ] 13.3 Implement directory navigation
    - Handle double-click on folder to navigate into it
    - Implement NavigateToDirectory and NavigateUp methods
    - Display current directory path
    - _Requirements: 7.2, 7.3_
  
  - [ ]* 13.4 Write property test for content browser navigation
    - **Property 21: Content Browser Navigation**
    - **Validates: Requirements 7.2**
    - Double-click folder, verify current directory updates
  
  - [ ]* 13.5 Write property test for parent directory navigation
    - **Property 22: Parent Directory Navigation**
    - **Validates: Requirements 7.3**
    - Click up button, verify current directory moves to parent
  
  - [ ] 13.6 Implement asset import functionality
    - Add "Import" button to open file dialog
    - Implement ImportAsset method to copy file to project
    - Generate asset metadata
    - Refresh asset display
    - _Requirements: 7.4_
  
  - [ ]* 13.7 Write property test for asset import integrity
    - **Property 23: Asset Import Integrity**
    - **Validates: Requirements 7.4**
    - Import file, verify it's copied and metadata is generated
  
  - [ ] 13.8 Implement asset deletion
    - Handle delete key or context menu delete
    - Implement DeleteAsset method to remove file from disk
    - Refresh asset display
    - _Requirements: 7.5_
  
  - [ ]* 13.9 Write property test for asset deletion
    - **Property 24: Asset Deletion**
    - **Validates: Requirements 7.5**
    - Delete asset, verify file is removed from disk
  
  - [ ] 13.10 Implement asset renaming
    - Handle F2 key or context menu rename
    - Implement RenameAsset method to update file name and metadata
    - Refresh asset display
    - _Requirements: 7.6_
  
  - [ ]* 13.11 Write property test for asset renaming
    - **Property 25: Asset Renaming**
    - **Validates: Requirements 7.6**
    - Rename asset, verify file name and metadata are updated
  
  - [ ] 13.12 Implement thumbnail generation
    - Generate thumbnails for textures using StbImageSharp
    - Generate thumbnails for meshes by rendering preview
    - Cache thumbnails to avoid regeneration
    - Implement LRU eviction for thumbnail cache
    - _Requirements: 7.7, 13.6_
  
  - [ ]* 13.13 Write property test for thumbnail generation
    - **Property 26: Thumbnail Generation**
    - **Validates: Requirements 7.7**
    - Add visual asset, verify thumbnail is generated
  
  - [ ] 13.14 Implement asset filtering
    - Add filter text input field
    - Filter assets by name and type
    - _Requirements: 7.8_
  
  - [ ]* 13.15 Write property test for asset filtering
    - **Property 27: Asset Filtering**
    - **Validates: Requirements 7.8**
    - Apply filter, verify only matching assets are displayed

- [ ] 14. Implement ConsolePanel for logging and commands
  - [ ] 14.1 Create ConsolePanel class implementing IEditorPanel
    - Define Logs, FilterLevel properties
    - Create LogEntry data model with Message, Level, Timestamp
    - Implement OnUpdate, OnDraw methods
    - _Requirements: 8.1_
  
  - [ ] 14.2 Implement log message display with color coding
    - Render log messages in scrollable list
    - Apply color coding based on log level (trace=gray, debug=white, info=cyan, warning=yellow, error=red, fatal=magenta)
    - Implement auto-scroll to latest message
    - _Requirements: 8.1, 8.6_
  
  - [ ]* 14.3 Write property test for log color coding
    - **Property 28: Log Color Coding**
    - **Validates: Requirements 8.1**
    - Add log messages at each level, verify colors match
  
  - [ ]* 14.4 Write property test for console auto-scroll
    - **Property 33: Console Auto-Scroll**
    - **Validates: Requirements 8.6**
    - Add new log message, verify scroll position updates
  
  - [ ] 14.5 Implement command execution
    - Add command input text field
    - Implement ExecuteCommand method
    - Display command results in log
    - _Requirements: 8.2_
  
  - [ ]* 14.6 Write property test for command execution
    - **Property 29: Command Execution**
    - **Validates: Requirements 8.2**
    - Execute command, verify result is displayed
  
  - [ ] 14.7 Implement command history
    - Store executed commands in history list
    - Handle up/down arrow keys to cycle through history
    - _Requirements: 8.3_
  
  - [ ]* 14.8 Write property test for command history navigation
    - **Property 30: Command History Navigation**
    - **Validates: Requirements 8.3**
    - Execute commands, press up arrow, verify previous command is displayed
  
  - [ ] 14.9 Implement log filtering
    - Add log level filter dropdown
    - Add search text filter input
    - Filter displayed logs based on level and search text
    - _Requirements: 8.4, 8.5_
  
  - [ ]* 14.10 Write property test for log level filtering
    - **Property 31: Log Level Filtering**
    - **Validates: Requirements 8.4**
    - Apply level filter, verify only matching logs are displayed
  
  - [ ]* 14.11 Write property test for log text filtering
    - **Property 32: Log Text Filtering**
    - **Validates: Requirements 8.5**
    - Apply search filter, verify only matching logs are displayed
  
  - [ ] 14.12 Implement log history limit
    - Limit log history to 10,000 entries
    - Remove oldest entries when limit exceeded
    - _Requirements: 8.7_

- [ ] 15. Checkpoint - Verify all panels are functional
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 16. Implement input event routing
  - [ ] 16.1 Implement focus management in DockingSystem
    - Track currently focused panel
    - Implement SetFocus method
    - Ensure at most one panel is focused
    - _Requirements: 11.1, 11.5_
  
  - [ ]* 16.2 Write property test for focus exclusivity
    - **Property 36: Focus Exclusivity**
    - **Validates: Requirements 11.5**
    - Set focus on multiple panels, verify only one is focused
  
  - [ ]* 16.3 Write property test for focus assignment
    - **Property 37: Focus Assignment**
    - **Validates: Requirements 11.1**
    - Click panel, verify it becomes focused
  
  - [ ] 16.4 Implement keyboard input routing
    - Route keyboard events to focused panel first
    - Fall back to global handlers if panel doesn't consume input
    - _Requirements: 11.2, 11.3_
  
  - [ ]* 16.5 Write property test for keyboard input routing
    - **Property 38: Keyboard Input Routing**
    - **Validates: Requirements 11.2**
    - Send keyboard input, verify it goes to focused panel
  
  - [ ]* 16.6 Write property test for input routing priority
    - **Property 39: Input Routing Priority**
    - **Validates: Requirements 11.3**
    - Send key press, verify panel gets it first, then global handlers
  
  - [ ] 16.7 Implement mouse event routing
    - Route mouse events to panel under cursor
    - Handle panel click to set focus
    - _Requirements: 11.4_
  
  - [ ]* 16.8 Write property test for mouse event routing
    - **Property 40: Mouse Event Routing**
    - **Validates: Requirements 11.4**
    - Move mouse, verify events go to panel under cursor

- [ ] 17. Implement security validations
  - [ ] 17.1 Implement path traversal prevention for asset import
    - Validate file paths to reject ".." and absolute paths outside project
    - Implement path sanitization
    - _Requirements: 14.1_
  
  - [ ]* 17.2 Write property test for path traversal prevention
    - **Property 41: Path Traversal Prevention**
    - **Validates: Requirements 14.1**
    - Attempt to import files with traversal paths, verify they are rejected
  
  - [ ] 17.3 Implement layout file validation
    - Validate layout file structure before deserialization
    - Reject malformed or malicious files
    - _Requirements: 14.2_
  
  - [ ]* 17.4 Write property test for layout file validation
    - **Property 42: Layout File Validation**
    - **Validates: Requirements 14.2**
    - Load malformed layout files, verify they are rejected
  
  - [ ] 17.5 Implement console command sanitization
    - Validate and sanitize command input
    - Whitelist allowed commands
    - _Requirements: 14.3, 14.6_
  
  - [ ]* 17.6 Write property test for command input sanitization
    - **Property 43: Command Input Sanitization**
    - **Validates: Requirements 14.3**
    - Execute malicious commands, verify they are sanitized
  
  - [ ]* 17.7 Write property test for command whitelisting
    - **Property 46: Command Whitelisting**
    - **Validates: Requirements 14.6**
    - Execute non-whitelisted commands, verify they are rejected
  
  - [ ] 17.8 Implement asset import directory restriction
    - Restrict imports to designated project directories
    - Reject files outside project
    - _Requirements: 14.4_
  
  - [ ]* 17.9 Write property test for asset import directory restriction
    - **Property 44: Asset Import Directory Restriction**
    - **Validates: Requirements 14.4**
    - Attempt to import files outside project, verify they are rejected
  
  - [ ] 17.10 Implement asset size limits
    - Check file size before import
    - Reject files exceeding maximum size
    - _Requirements: 14.5_
  
  - [ ]* 17.11 Write property test for asset size limits
    - **Property 45: Asset Size Limits**
    - **Validates: Requirements 14.5**
    - Attempt to import oversized files, verify they are rejected

- [ ] 18. Implement error handling
  - [ ] 18.1 Implement RHI initialization error handling
    - Catch DirectX 9 initialization failures
    - Display user-friendly error dialog
    - Log detailed error information
    - Exit gracefully
    - _Requirements: 1.7, 12.1_
  
  - [ ] 18.2 Implement asset import error handling
    - Catch import failures (corrupted files, unsupported formats)
    - Display error message with specific reason
    - Rollback partial imports
    - _Requirements: 12.2_
  
  - [ ] 18.3 Implement layout load error handling
    - Catch layout load failures
    - Fall back to default layout
    - Notify user
    - _Requirements: 9.3, 12.3_
  
  - [ ] 18.4 Implement backup operation error handling
    - Catch backup failures (disk space, permissions)
    - Display detailed error message
    - Abort cleanly without partial backups
    - _Requirements: 10.5, 12.4_

- [x] 19. Create main editor application and initialization
  - [x] 19.1 Create BlueSkyEngineEditor.exe entry point
    - Create `Program.cs` with Main method
    - Parse command line arguments
    - Initialize logging system
    - _Requirements: 1.1_
    - _Status: COMPLETED - Program.cs exists with full initialization_
  
  - [x] 19.2 Implement InitializeEditor algorithm
    - Create window with WindowOptions
    - Initialize RHI device (DirectX 9 on Windows 7)
    - Create UIRenderer and FontAtlas
    - Initialize DockingSystem
    - Create all default panels
    - Setup default layout
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6_
    - _Status: COMPLETED - OnLoad() implements full initialization_
  
  - [x] 19.3 Implement main editor loop
    - Process window events
    - Update docking system and panels
    - Render all panels via UIRenderer
    - Present frame via swapchain
    - _Requirements: 1.1_
    - _Status: COMPLETED - Main loop with OnUpdate/OnRender exists_
  
  - [x] 19.4 Wire ECS to Editor UI
    - Create HierarchyPanelController to display entities from World
    - Create InspectorPanelController to display/edit entity components
    - Wire viewport entity selection to hierarchy panel
    - Populate hierarchy with entities from World.GetAllEntities()
    - Display entity names from NameComponent
    - Show entity transforms in inspector
    - Enable component editing in inspector
    - _Requirements: 5.4, 4.6_
    - _Status: COMPLETED - Full ECS integration with hierarchy and inspector_

- [ ] 20. Final checkpoint - Ensure all tests pass and editor is functional
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties
- Unit tests validate specific examples and edge cases
- The backup operation (Task 1) is completed first as a safety measure before UI development
- All code examples and implementations use C# language
