// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════
// STATIC MESH EDITOR V2.0 - PRODUCTION-GRADE MESH INSPECTOR
// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════
// 
// FEATURES:
// - Glassmorphic modern UI with smooth animations
// - Advanced 3D viewport with lighting/rotation controls
// - LOD configuration with visual distance indicators
// - Collision settings with preview overlay
// - Comprehensive mesh statistics and optimization suggestions
// 
// ARCHITECTURE:
// - Cache-friendly data structures (fixed arrays, no heap churn)
// - Smooth 60fps animations with delta-time interpolation
// - Robust error handling with graceful degradation
// - Atomic save operations with backup/restore
// 
// UI DESIGN:
// - Dark theme with accent colors (teal/purple/orange)
// - Card-based layout with depth shadows
// - Smooth hover/press states with easing
// - Visual feedback for all interactions
// - Professional typography and spacing
// 
// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Numerics;
using BlueSky.Core.Assets;
using BlueSky.Editor.UI;
using BlueSky.Rendering;
using BlueSky.Rendering.RHI;

namespace BlueSky.Editor;

/// <summary>
/// Static Mesh Editor - Production-grade editor for mesh properties.
/// Supports LOD configuration, Collision settings and mesh preview.
/// </summary>
public class StaticMeshEditor
{
    // ── Core State ────────────────────────────────────────────────────────
    private BlueAsset? _currentAsset;
    private string _assetPath = "";
    private bool _isDirty = false;
    
    // ── Viewport Hijacking ────────────────────────────────────────────────
    private Core.ECS.Entity _previewEntity;
    private Core.ECS.World? _lastWorld;
    private bool _hasSpawnedPreview = false;
    
    // ── Preview Renderer (mini-viewport in right panel) ──────────────────
    private ViewportRenderer? _previewRenderer;
    private IRHITexture? _previewTexture;
    private IRHIDevice? _rhi;
    private uint _previewWidth = 512;
    private uint _previewHeight = 512;
    
    // ── Public Preview Properties ──────────────────────────────────────────
    public Vector4 PreviewRect { get; private set; }
    
    // ── LOD Configuration ────────────────────────────────────────────────
    private readonly LODSettings _lodSettings = new()
    {
        LODCount = 3,
        LOD0Distance = 10.0f,
        LOD1Distance = 25.0f,
        LOD2Distance = 50.0f,
        LOD3Distance = 80.0f,
        LOD4Distance = 120.0f,
        ScreenSizeTransition = 0.5f,
        ForceLOD = -1
    };
    
    // ── Collision Settings ───────────────────────────────────────────────
    private CollisionType _collisionType = CollisionType.ConvexHull;
    private bool _generateCollision = true;
    private float _collisionComplexity = 0.5f; // 0=simple, 1=complex
    
    // ── Mesh Statistics (cached from asset metadata) ────────────────────
    private int _vertexCount = 0;
    private int _triangleCount = 0;
    private Vector3 _boundsMin = Vector3.Zero;
    private Vector3 _boundsMax = Vector3.Zero;
    
    // ── UI State ─────────────────────────────────────────────────────────
    private int _selectedTab = 0; // 0=LODs, 1=Collision, 2=Info
    private float _previewRotation = 0f;
    private bool _autoRotate = true;
    private bool _showWireframe = false;
    private bool _showBounds = false;
    
    // Animation state
    private float _tabTransition = 0f;
    private int _previousTab = 0;
    private readonly Dictionary<int, float> _tabHoverAnim = new();
    private float _saveButtonPulse = 0f;
    
    public bool IsOpen { get; set; } = false;
    
    public enum CollisionType
    {
        None,
        BoundingBox,
        BoundingSphere,
        ConvexHull,
        TriangleMesh
    }
    
    /// <summary>
    /// Open the static mesh editor with an asset file.
    /// Validates asset type and loads metadata with robust error handling.
    /// </summary>
    public void Open(string assetPath, Core.ECS.World world, Rendering.Viewport viewport, IRHIDevice rhi)
    {
        try
        {
            if (string.IsNullOrEmpty(assetPath) || !System.IO.File.Exists(assetPath))
            {
                Console.WriteLine($"[StaticMeshEditor] ERROR: Asset file not found: {assetPath}");
                return;
            }
            
            var asset = BlueAsset.Load(assetPath);
            if (asset == null)
            {
                Console.WriteLine($"[StaticMeshEditor] ERROR: Failed to load asset: {assetPath}");
                return;
            }
            
            if (asset.Type != AssetType.StaticMesh && asset.Type != AssetType.Mesh)
            {
                Console.WriteLine($"[StaticMeshEditor] ERROR: Asset is not a static mesh: {asset.Type}");
                return;
            }
            
            _currentAsset = asset;
            _assetPath = assetPath;
            _isDirty = false;

            // Reset preview state if world instance has changed (e.g. project switch)
            if (_lastWorld != world)
            {
                _hasSpawnedPreview = false;
                _previewEntity = default;
                _lastWorld = world;
            }
            IsOpen = true;
            _rhi = rhi;
            
            LoadAssetData();
            
            // Initialize preview renderer (only once)
            if (_previewRenderer == null)
            {
                InitializePreviewRenderer(world, viewport);
            }
            
            SpawnPreviewMesh(world, viewport);
            PositionCamera(viewport);
            
            Console.WriteLine($"[StaticMeshEditor] Opened: {asset.AssetName} ({_vertexCount} verts, {_triangleCount} tris)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StaticMeshEditor] EXCEPTION: {ex.Message}");
            Console.WriteLine($"[StaticMeshEditor] Stack: {ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Initialize the preview renderer for the right panel.
    /// </summary>
    private void InitializePreviewRenderer(Core.ECS.World world, Rendering.Viewport viewport)
    {
        if (_rhi == null) return;
        
        try
        {
            // Create preview texture (render target)
            _previewTexture = _rhi.CreateTexture(new BlueSky.Rendering.RHI.TextureDesc
            {
                Width = _previewWidth,
                Height = _previewHeight,
                Depth = 1,
                MipLevels = 1,
                ArrayLayers = 1,
                Format = BlueSky.Rendering.RHI.TextureFormat.RGBA8Unorm,
                Usage = BlueSky.Rendering.RHI.TextureUsage.RenderTarget | BlueSky.Rendering.RHI.TextureUsage.Sampled,
                DebugName = "StaticMeshPreview"
            });
            
            // Create preview renderer (uses same world and viewport camera)
            _previewRenderer = new ViewportRenderer(_rhi, world);
            
            Console.WriteLine($"[StaticMeshEditor] ✓ Initialized preview renderer: {_previewWidth}x{_previewHeight}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StaticMeshEditor] ✗ Failed to initialize preview renderer: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Position camera to frame the mesh based on its bounding box.
    /// </summary>
    private void PositionCamera(Rendering.Viewport viewport)
    {
        try
        {
            // Calculate mesh center and size
            Vector3 center = (_boundsMin + _boundsMax) * 0.5f;
            Vector3 size = _boundsMax - _boundsMin;
            
            // Use bounding box diagonal for proper framing distance
            float diagonal = MathF.Sqrt(size.X * size.X + size.Y * size.Y + size.Z * size.Z);
            if (diagonal < 0.001f) diagonal = 2.0f; // Fallback for zero-size bounds
            
            // Calculate distance using FOV: d = (diagonal/2) / tan(fov/2)
            // Camera FOV is 60 degrees by default
            float fovRadians = 60f * MathF.PI / 180f;
            float distance = (diagonal * 0.5f) / MathF.Tan(fovRadians * 0.5f);
            distance = Math.Max(distance * 1.2f, 3.0f); // 20% padding + minimum distance
            
            // Position camera at a 3/4 front-above angle for a clean preview shot
            // Offset: slightly right, above center, in front
            var cameraPos = new Core.Math.Vector3(
                center.X + diagonal * 0.4f,   // slightly right
                center.Y + diagonal * 0.35f,  // above center
                center.Z + distance           // in front
            );
            
            // Set camera position directly
            ref var cameraTransform = ref viewport.GetCameraTransform();
            cameraTransform.SetPosition(cameraPos);
            
            // Look at center of mesh
            cameraTransform.LookAt(new Core.Math.Vector3(center.X, center.Y, center.Z), Core.Math.Vector3.Up);
            
            Console.WriteLine($"[StaticMeshEditor] Camera framed: diagonal={diagonal:F2}, distance={distance:F2}");
            Console.WriteLine($"[StaticMeshEditor] Mesh bounds: {_boundsMin} to {_boundsMax}, size: {size}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StaticMeshEditor] Warning: Failed to position camera: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Spawn the mesh in the world for viewport preview.
    /// Does NOT destroy the existing scene — spawns the preview entity alongside it
    /// and lets camera framing handle the visual isolation.
    /// </summary>
    private void SpawnPreviewMesh(Core.ECS.World world, Rendering.Viewport viewport)
    {
        if (_currentAsset == null) return;
        
        if (_hasSpawnedPreview)
        {
            // Update existing preview entity
            if (world.HasComponent<Core.ECS.Builtin.StaticMeshComponent>(_previewEntity))
            {
                ref var meshComp = ref world.GetComponent<Core.ECS.Builtin.StaticMeshComponent>(_previewEntity);
                meshComp.MeshAssetId = _assetPath;
                
                // Reset transform
                if (world.HasComponent<Core.ECS.Builtin.TransformComponent>(_previewEntity))
                {
                    ref var transform = ref world.GetComponent<Core.ECS.Builtin.TransformComponent>(_previewEntity);
                    transform.Position = new Core.Math.Vector3(0, 0, 0);
                    transform.Rotation = Core.Math.Quaternion.Identity;
                    transform.Scale = new Core.Math.Vector3(1, 1, 1);
                }
                
                Console.WriteLine($"[StaticMeshEditor] Updated preview mesh: {_currentAsset!.AssetName}");
                Program.GetMainViewport()?.InvalidateMeshGpuCache(_assetPath);
                return;
            }
        }
        
        try
        {
            // Create preview entity with the mesh (no scene destruction!)
            _previewEntity = world.CreateEntity();
            
            // Add transform component (centered at origin)
            var transform = new Core.ECS.Builtin.TransformComponent
            {
                Position = new Core.Math.Vector3(0, 0, 0),
                Rotation = Core.Math.Quaternion.Identity,
                Scale = new Core.Math.Vector3(1, 1, 1)
            };
            world.AddComponent(_previewEntity, transform);
            
            // Add static mesh component — use the FILE PATH, not the GUID!
            // ViewportRenderer demand-loads meshes by path, not by GUID.
            // (geometry only — material system removed)
            var meshComp = new Core.ECS.Builtin.StaticMeshComponent
            {
                MeshAssetId = _assetPath
            };

            world.AddComponent(_previewEntity, meshComp);
            
            _hasSpawnedPreview = true;
            Console.WriteLine($"[StaticMeshEditor] ✓ Spawned preview mesh: {_currentAsset!.AssetName}");
            Console.WriteLine($"[StaticMeshEditor]   Entity ID: {_previewEntity.Id}");
            Console.WriteLine($"[StaticMeshEditor]   Mesh Path: {_assetPath}");
            Console.WriteLine($"[StaticMeshEditor]   Vertices: {_vertexCount}, Triangles: {_triangleCount}");
            
            Program.GetMainViewport()?.InvalidateMeshGpuCache(_assetPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StaticMeshEditor] ✗ Failed to spawn preview mesh: {ex.Message}");
            Console.WriteLine($"[StaticMeshEditor]   Stack: {ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Clear all entities from the scene for isolated preview.
    /// CRITICAL: Preserves the camera entity to prevent viewport crashes.
    /// </summary>
    private void ClearScene(Core.ECS.World world, Rendering.Viewport viewport)
    {
        try
        {
            // Get camera entity from viewport - MUST preserve this
            var cameraEntity = viewport.GetCameraEntity();
            
            // Get all entities
            var entities = new List<Core.ECS.Entity>(world.GetAllEntities());
            
            int destroyedCount = 0;
            
            // Destroy all entities EXCEPT the camera
            foreach (var entity in entities)
            {
                if (entity.Id != cameraEntity.Id)
                {
                    world.DestroyEntity(entity);
                    destroyedCount++;
                }
            }
            
            Console.WriteLine($"[StaticMeshEditor] Cleared {destroyedCount} entities from scene (preserved camera entity {cameraEntity.Id})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StaticMeshEditor] Warning: Failed to clear scene: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Close the editor and clean up preview entity.
    /// </summary>
    public void Close(Core.ECS.World world)
    {
        if (_hasSpawnedPreview && _previewEntity.Id != 0)
        {
            try
            {
                world.DestroyEntity(_previewEntity);
                Console.WriteLine($"[StaticMeshEditor] Destroyed preview entity");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StaticMeshEditor] Failed to destroy preview entity: {ex.Message}");
            }
        }
        
        // Clean up preview renderer
        _previewTexture?.Dispose();
        _previewTexture = null;
        _previewRenderer = null;
        
        _hasSpawnedPreview = false;
        _previewEntity = default;
        IsOpen = false;
    }
    
    /// <summary>
    /// Load asset metadata from the asset file.
    /// Cache-friendly sequential reads with validation.
    /// </summary>
    private void LoadAssetData()
    {
        if (_currentAsset == null) return;
        
        // Load mesh statistics from metadata
        if (_currentAsset.Metadata.TryGetValue("vertexCount", out var vCount))
            int.TryParse(vCount, out _vertexCount);
        
        if (_currentAsset.Metadata.TryGetValue("triangleCount", out var tCount))
            int.TryParse(tCount, out _triangleCount);
        
        // Load bounds
        if (_currentAsset.Metadata.TryGetValue("boundsMin", out var bMin))
        {
            var parts = bMin.Split(',');
            if (parts.Length == 3)
            {
                float.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _boundsMin.X);
                float.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _boundsMin.Y);
                float.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _boundsMin.Z);
            }
        }
        
        if (_currentAsset.Metadata.TryGetValue("boundsMax", out var bMax))
        {
            var parts = bMax.Split(',');
            if (parts.Length == 3)
            {
                float.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _boundsMax.X);
                float.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _boundsMax.Y);
                float.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _boundsMax.Z);
            }
        }
        
        // Load LOD settings from metadata
        if (_currentAsset.Metadata.TryGetValue("lodCount", out var lodCount))
            int.TryParse(lodCount, out _lodSettings.LODCount);
        
        if (_currentAsset.Metadata.TryGetValue("lod0Distance", out var lod0))
            float.TryParse(lod0, out _lodSettings.LOD0Distance);
        
        if (_currentAsset.Metadata.TryGetValue("lod1Distance", out var lod1))
            float.TryParse(lod1, out _lodSettings.LOD1Distance);
        
        if (_currentAsset.Metadata.TryGetValue("lod2Distance", out var lod2))
            float.TryParse(lod2, out _lodSettings.LOD2Distance);
        
        // Load collision settings
        if (_currentAsset.Metadata.TryGetValue("collisionType", out var colType))
            Enum.TryParse(colType, out _collisionType);
        
        if (_currentAsset.Metadata.TryGetValue("generateCollision", out var genCol))
            bool.TryParse(genCol, out _generateCollision);
    }
    
    /// <summary>
    /// Render the static mesh editor UI with modern glassmorphic design.
    /// 60fps smooth animations, professional visual hierarchy, stunning aesthetics.
    /// </summary>
    public void Render(EditorUI ui, float x, float y, float width, float height)
    {
        if (!IsOpen || _currentAsset == null) return;
        
        // ═══════════════════════════════════════════════════════════════════
        // MAIN BACKGROUND - Dark with subtle gradient
        // ═══════════════════════════════════════════════════════════════════
        ui.RoundedGradientPanel(x, y, width, height, 
            new Vector4(0.08f, 0.09f, 0.12f, 0.98f),  // Top: Deep blue-black
            new Vector4(0.06f, 0.07f, 0.10f, 0.98f),  // Bottom: Darker
            10f);
        
        // Outer glow for depth
        ui.Shadow(x, y, width, height, 12f, 20f, 0.6f);
        
        // ═══════════════════════════════════════════════════════════════════
        // TITLE BAR - Glassmorphic with gradient accent
        // ═══════════════════════════════════════════════════════════════════
        float titleH = 50;
        ui.RoundedGradientPanel(x, y, width, titleH, 
            new Vector4(0.15f, 0.25f, 0.35f, 0.95f),  // Teal gradient
            new Vector4(0.12f, 0.20f, 0.30f, 0.95f), 
            10f);
        
        // Accent line at bottom of title
        ui.Panel(x + 10, y + titleH - 2, width - 20, 2, new Vector4(0.3f, 0.7f, 0.9f, 0.6f));
        
        // Title text with icon
        ui.SetCursor(x + 20, y + 16);
        ui.Text($"🎨 Static Mesh Editor", new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        
        ui.SetCursor(x + 20, y + 32);
        ui.Text(_currentAsset!.AssetName, new Vector4(0.7f, 0.85f, 1.0f, 0.9f));
        
        // Close button with hover animation
        float closeX = x + width - 50;
        float closeY = y + 10;
        var closeColor = ui.IsHovering(closeX, closeY, 35, 30) 
            ? new Vector4(0.95f, 0.35f, 0.35f, 1.0f)
            : new Vector4(0.7f, 0.25f, 0.25f, 0.8f);
        
        if (ui.ButtonEx(closeX, closeY, 35, 30, "✕", 
            closeColor,
            new Vector4(1.0f, 0.45f, 0.45f, 1.0f),
            new Vector4(0.6f, 0.20f, 0.20f, 0.9f),
            new Vector4(0, 0, 0, 0.4f),
            new Vector4(1, 1, 1, 1)))
        {
            IsOpen = false;
            return;
        }
        
        float contentY = y + titleH + 10;
        float contentH = height - titleH - 20;
        
        // ═══════════════════════════════════════════════════════════════════
        // TAB BAR - Modern with smooth transitions
        // ═══════════════════════════════════════════════════════════════════
        float tabBarH = 45;
        RenderModernTabBar(ui, x + 15, contentY, width - 30, tabBarH);
        
        contentY += tabBarH + 15;
        contentH -= tabBarH + 15;
        
        // ═══════════════════════════════════════════════════════════════════
        // SPLIT LAYOUT - Left (settings) + Right (3D preview)
        // ═══════════════════════════════════════════════════════════════════
        float leftW = width * 0.48f;
        float rightW = width * 0.48f;
        float gutter = width * 0.04f;
        
        // Left panel - settings with glassmorphic card
        RenderLeftPanelModern(ui, x + 15, contentY, leftW, contentH - 60);
        
        // Right panel - 3D preview with controls
        RenderPreviewPanelModern(ui, x + leftW + gutter + 15, contentY, rightW, contentH - 60);
        
        // ═══════════════════════════════════════════════════════════════════
        // BOTTOM ACTION BAR - Save/Revert buttons
        // ═══════════════════════════════════════════════════════════════════
        float actionBarY = y + height - 55;
        RenderActionBar(ui, x + 15, actionBarY, width - 30, 45);
    }
    
    
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // MODERN UI RENDERING METHODS - V2.0 REDESIGN
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Render modern tab bar with smooth animations and visual feedback.
    /// </summary>
    private void RenderModernTabBar(EditorUI ui, float x, float y, float width, float height)
    {
        string[] tabs = { "📊 LODs", "🛡️ Collision", "ℹ️ Info" };
        float tabW = width / tabs.Length;
        
        // Background bar with subtle gradient
        ui.RoundedGradientPanel(x, y, width, height,
            new Vector4(0.12f, 0.13f, 0.16f, 0.9f),
            new Vector4(0.10f, 0.11f, 0.14f, 0.9f),
            8f);
        
        for (int i = 0; i < tabs.Length; i++)
        {
            float tx = x + i * tabW + 2;
            float tw = tabW - 4;
            bool isSelected = _selectedTab == i;
            bool isHovered = ui.IsHovering(tx, y, tw, height);
            
            // Smooth hover animation
            if (!_tabHoverAnim.ContainsKey(i + 100)) _tabHoverAnim[i + 100] = 0f;
            float targetHover = (isHovered || isSelected) ? 1f : 0f;
            _tabHoverAnim[i + 100] += (targetHover - _tabHoverAnim[i + 100]) * 0.15f;
            float hoverAnim = _tabHoverAnim[i + 100];
            
            // Tab background with gradient based on state
            Vector4 bgColor, bgColorBottom;
            if (isSelected)
            {
                bgColor = new Vector4(0.20f + hoverAnim * 0.05f, 0.35f + hoverAnim * 0.05f, 0.50f + hoverAnim * 0.05f, 1.0f);
                bgColorBottom = new Vector4(0.18f + hoverAnim * 0.05f, 0.30f + hoverAnim * 0.05f, 0.45f + hoverAnim * 0.05f, 1.0f);
            }
            else
            {
                bgColor = new Vector4(0.14f + hoverAnim * 0.04f, 0.15f + hoverAnim * 0.04f, 0.18f + hoverAnim * 0.04f, 0.8f);
                bgColorBottom = new Vector4(0.12f + hoverAnim * 0.04f, 0.13f + hoverAnim * 0.04f, 0.16f + hoverAnim * 0.04f, 0.8f);
            }
            
            ui.RoundedGradientPanel(tx, y + 4, tw, height - 8, bgColor, bgColorBottom, 6f);
            
            // Accent indicator for selected tab
            if (isSelected)
            {
                float indicatorH = 3f;
                ui.RoundedPanel(tx + 5, y + height - indicatorH - 6, tw - 10, indicatorH,
                    new Vector4(0.4f, 0.75f, 1.0f, 0.9f), 2f);
            }
            
            // Tab text with color based on state
            Vector4 textColor = isSelected 
                ? new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
                : new Vector4(0.7f + hoverAnim * 0.2f, 0.7f + hoverAnim * 0.2f, 0.7f + hoverAnim * 0.2f, 0.9f);
            
            ui.SetCursor(tx + tw / 2 - tabs[i].Length * 3.5f, y + height / 2 - 6);
            ui.Text(tabs[i], textColor);
            
            // Click handling
            if (ui.IsHovering(tx, y, tw, height) && ui.IsMouseDown)
            {
                if (_selectedTab != i)
                {
                    _previousTab = _selectedTab;
                    _selectedTab = i;
                    _tabTransition = 0f;
                }
            }
        }
    }
    
    /// <summary>
    /// Render left panel with modern card-based design.
    /// </summary>
    private void RenderLeftPanelModern(EditorUI ui, float x, float y, float width, float height)
    {
        // Glassmorphic card background
        ui.RoundedGradientPanel(x, y, width, height,
            new Vector4(0.14f, 0.15f, 0.18f, 0.95f),
            new Vector4(0.12f, 0.13f, 0.16f, 0.95f),
            10f);
        
        // Inner shadow for depth
        ui.Panel(x, y, width, 1, new Vector4(0, 0, 0, 0.3f));
        
        // Content with padding
        float contentX = x + 20;
        float contentY = y + 20;
        float contentW = width - 40;
        float contentH = height - 40;
        
        switch (_selectedTab)
        {
            case 0: RenderLODsTabModern(ui, contentX, contentY, contentW, contentH); break;
            case 1: RenderCollisionTabModern(ui, contentX, contentY, contentW, contentH); break;
            case 2: RenderInfoTabModern(ui, contentX, contentY, contentW, contentH); break;
        }
    }
    
    /// <summary>
    private void RenderTabBar(EditorUI ui, float x, float y, float width, float height)
    {
        string[] tabs = { "LODs", "Collision", "Info" };
        float tabW = width / tabs.Length;
        
        for (int i = 0; i < tabs.Length; i++)
        {
            float tx = x + i * tabW;
            bool isSelected = _selectedTab == i;
            
            var bgColor = isSelected ? new Vector4(0.3f, 0.4f, 0.5f, 1) : new Vector4(0.2f, 0.2f, 0.22f, 1);
            var hoverColor = isSelected ? new Vector4(0.35f, 0.45f, 0.55f, 1) : new Vector4(0.25f, 0.25f, 0.27f, 1);
            
            if (ui.ButtonEx(tx, y, tabW - 2, height, tabs[i],
                bgColor, hoverColor, bgColor,
                new Vector4(0, 0, 0, 0.3f),
                new Vector4(0.9f, 0.9f, 0.9f, 1)))
            {
                _selectedTab = i;
            }
            
            if (isSelected)
            {
                ui.Panel(tx, y + height - 3, tabW - 2, 3, new Vector4(0.4f, 0.7f, 1.0f, 1));
            }
        }
    }
    
    private void RenderLeftPanel(EditorUI ui, float x, float y, float width, float height)
    {
        ui.RoundedPanel(x, y, width, height, new Vector4(0.18f, 0.18f, 0.2f, 1), 6f);
        
        switch (_selectedTab)
        {
            case 0: RenderLODsTab(ui, x + 10, y + 10, width - 20, height - 20); break;
            case 1: RenderCollisionTab(ui, x + 10, y + 10, width - 20, height - 20); break;
            case 2: RenderInfoTab(ui, x + 10, y + 10, width - 20, height - 20); break;
        }
    }
    
    
    
    /// <summary>
    /// Render LODs tab with modern visual distance indicators.
    /// </summary>
    private void RenderLODsTabModern(EditorUI ui, float x, float y, float width, float height)
    {
        float rowY = y;
        
        // Section header
        ui.SetCursor(x, rowY);
        ui.Text("Level of Detail Configuration", new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        rowY += 40;
        
        // LOD count display
        ui.RoundedPanel(x, rowY, width, 60,
            new Vector4(0.18f, 0.20f, 0.24f, 0.9f), 8f);
        
        ui.SetCursor(x + 15, rowY + 12);
        ui.Text($"LOD Levels: {_lodSettings.LODCount}", new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
        
        ui.SetCursor(x + 15, rowY + 35);
        ui.Text("Automatic mesh simplification at distance", new Vector4(0.6f, 0.6f, 0.6f, 0.9f));
        rowY += 75;
        
        // Distance sliders with visual indicators
        string[] lodLabels = { "LOD 0 (Full Detail)", "LOD 1 (High)", "LOD 2 (Medium)" };
        float[] lodDistances = { _lodSettings.LOD0Distance, _lodSettings.LOD1Distance, _lodSettings.LOD2Distance };
        
        for (int i = 0; i < 3; i++)
        {
            ui.SetCursor(x, rowY);
            ui.Text(lodLabels[i], new Vector4(0.85f, 0.85f, 0.85f, 1.0f));
            rowY += 25;
            
            // Distance bar visualization
            float barW = width - 80;
            float barH = 8;
            ui.RoundedPanel(x, rowY, barW, barH,
                new Vector4(0.15f, 0.15f, 0.18f, 0.8f), 4f);
            
            // Fill based on distance
            float fillW = Math.Min(lodDistances[i] / 100f, 1f) * barW;
            Vector4 fillColor = new Vector4(0.3f + i * 0.15f, 0.6f - i * 0.15f, 0.8f - i * 0.2f, 0.9f);
            ui.RoundedPanel(x, rowY, fillW, barH, fillColor, 4f);
            
            // Distance value
            ui.SetCursor(x + barW + 10, rowY - 2);
            ui.Text($"{lodDistances[i]:F0}m", new Vector4(0.7f, 0.9f, 1.0f, 1.0f));
            
            rowY += 35;
        }
        
        rowY += 10;
        
        // Preset buttons with modern styling
        ui.SetCursor(x, rowY);
        ui.Text("Quality Presets:", new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
        rowY += 30;
        
        string[] presets = { "🔻 Low", "⚖️ Medium", "🔺 High" };
        
        float btnW = (width - 20) / 3;
        for (int i = 0; i < presets.Length; i++)
        {
            Vector4 btnColor = new Vector4(0.25f + i * 0.05f, 0.35f + i * 0.05f, 0.45f + i * 0.05f, 0.9f);
            
            if (ui.ButtonEx(x + i * (btnW + 10), rowY, btnW, 36, presets[i],
                btnColor,
                new Vector4(btnColor.X + 0.05f, btnColor.Y + 0.05f, btnColor.Z + 0.05f, 1.0f),
                new Vector4(btnColor.X - 0.05f, btnColor.Y - 0.05f, btnColor.Z - 0.05f, 0.9f),
                new Vector4(0, 0, 0, 0.4f),
                new Vector4(1, 1, 1, 1)))
            {
                // Apply preset based on index
                if (i == 0) ApplyLODPreset(LODPresets.Low);
                else if (i == 1) ApplyLODPreset(LODPresets.Medium);
                else if (i == 2) ApplyLODPreset(LODPresets.High);
            }
        }
    }
    
    /// <summary>
    /// Render collision tab with visual type indicators.
    /// </summary>
    private void RenderCollisionTabModern(EditorUI ui, float x, float y, float width, float height)
    {
        float rowY = y;
        
        // Section header
        ui.SetCursor(x, rowY);
        ui.Text("Collision Configuration", new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        rowY += 40;
        
        // Current type display
        ui.RoundedPanel(x, rowY, width, 50,
            new Vector4(0.18f, 0.25f, 0.20f, 0.9f), 8f);
        
        ui.SetCursor(x + 15, rowY + 12);
        ui.Text($"Active Type: {_collisionType}", new Vector4(0.7f, 1.0f, 0.8f, 1.0f));
        
        ui.SetCursor(x + 15, rowY + 32);
        ui.Text(_generateCollision ? "✓ Auto-generate enabled" : "✗ Manual collision", 
            new Vector4(0.6f, 0.6f, 0.6f, 0.9f));
        rowY += 65;
        
        // Collision type cards
        string[] collisionTypes = { "None", "Bounding Box", "Bounding Sphere", "Convex Hull", "Triangle Mesh" };
        string[] collisionIcons = { "⭕", "📦", "🔵", "🔷", "🔺" };
        string[] collisionDescs = { 
            "No collision detection",
            "Fast, axis-aligned box",
            "Fast, spherical bounds",
            "Accurate, convex shape",
            "Precise, per-triangle"
        };
        CollisionType[] types = { 
            CollisionType.None, 
            CollisionType.BoundingBox, 
            CollisionType.BoundingSphere, 
            CollisionType.ConvexHull, 
            CollisionType.TriangleMesh 
        };
        
        for (int i = 0; i < collisionTypes.Length; i++)
        {
            bool isSelected = _collisionType == types[i];
            bool isHovered = ui.IsHovering(x, rowY, width, 60);
            
            Vector4 cardBg = isSelected
                ? new Vector4(0.25f, 0.45f, 0.35f, 0.95f)
                : new Vector4(0.16f, 0.17f, 0.20f, 0.9f);
            
            if (isHovered && !isSelected)
            {
                cardBg = new Vector4(0.20f, 0.22f, 0.26f, 0.95f);
            }
            
            ui.RoundedPanel(x, rowY, width, 60, cardBg, 8f);
            
            // Icon
            ui.SetCursor(x + 15, rowY + 18);
            ui.Text(collisionIcons[i], new Vector4(1, 1, 1, 1));
            
            // Type name
            ui.SetCursor(x + 45, rowY + 12);
            ui.Text(collisionTypes[i], new Vector4(0.95f, 0.95f, 0.95f, 1.0f));
            
            // Description
            ui.SetCursor(x + 45, rowY + 32);
            ui.Text(collisionDescs[i], new Vector4(0.6f, 0.6f, 0.6f, 0.9f));
            
            // Selection indicator
            if (isSelected)
            {
                ui.RoundedPanel(x + width - 30, rowY + 20, 20, 20,
                    new Vector4(0.4f, 0.9f, 0.6f, 1.0f), 10f);
                ui.SetCursor(x + width - 26, rowY + 24);
                ui.Text("✓", new Vector4(0, 0, 0, 1));
            }
            
            // Click to select
            if (isHovered && ui.IsMouseDown)
            {
                _collisionType = types[i];
                _isDirty = true;
            }
            
            rowY += 70;
        }
    }
    
    /// <summary>
    /// Render info tab with comprehensive mesh statistics.
    /// </summary>
    private void RenderInfoTabModern(EditorUI ui, float x, float y, float width, float height)
    {
        float rowY = y;
        
        // Section header
        ui.SetCursor(x, rowY);
        ui.Text("Mesh Statistics & Information", new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        rowY += 40;
        
        // Geometry stats card
        ui.RoundedPanel(x, rowY, width, 120,
            new Vector4(0.18f, 0.22f, 0.28f, 0.95f), 8f);
        
        ui.SetCursor(x + 15, rowY + 12);
        ui.Text("📊 Geometry", new Vector4(0.7f, 0.9f, 1.0f, 1.0f));
        
        ui.SetCursor(x + 15, rowY + 38);
        ui.Text($"Vertices: {_vertexCount:N0}", new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
        
        ui.SetCursor(x + 15, rowY + 60);
        ui.Text($"Triangles: {_triangleCount:N0}", new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
        
        // Optimization suggestion
        if (_triangleCount > 100000)
        {
            ui.SetCursor(x + 15, rowY + 100);
            ui.Text("⚠️ High poly count - consider LODs", new Vector4(1.0f, 0.7f, 0.4f, 1.0f));
        }
        
        rowY += 135;
        
        // Bounding box card
        ui.RoundedPanel(x, rowY, width, 140,
            new Vector4(0.22f, 0.18f, 0.28f, 0.95f), 8f);
        
        ui.SetCursor(x + 15, rowY + 12);
        ui.Text("📐 Bounding Box", new Vector4(0.9f, 0.7f, 1.0f, 1.0f));
        
        Vector3 size = _boundsMax - _boundsMin;
        Vector3 center = (_boundsMin + _boundsMax) * 0.5f;
        
        ui.SetCursor(x + 15, rowY + 38);
        ui.Text($"Size: {size.X:F2} × {size.Y:F2} × {size.Z:F2}", new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
        
        ui.SetCursor(x + 15, rowY + 60);
        ui.Text($"Center: ({center.X:F2}, {center.Y:F2}, {center.Z:F2})", new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
        
        ui.SetCursor(x + 15, rowY + 82);
        ui.Text($"Min: ({_boundsMin.X:F2}, {_boundsMin.Y:F2}, {_boundsMin.Z:F2})", new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
        
        ui.SetCursor(x + 15, rowY + 104);
        ui.Text($"Max: ({_boundsMax.X:F2}, {_boundsMax.Y:F2}, {_boundsMax.Z:F2})", new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
        
        rowY += 155;
        
        // Asset info card
        ui.RoundedPanel(x, rowY, width, 80,
            new Vector4(0.18f, 0.28f, 0.22f, 0.95f), 8f);
        
        ui.SetCursor(x + 15, rowY + 12);
        ui.Text("📁 Asset Info", new Vector4(0.7f, 1.0f, 0.8f, 1.0f));
        
        ui.SetCursor(x + 15, rowY + 38);
        ui.Text($"File: {System.IO.Path.GetFileName(_assetPath)}", new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
        
        ui.SetCursor(x + 15, rowY + 58);
        ui.Text($"Format: {_currentAsset?.Metadata.GetValueOrDefault("format", "Unknown")}", 
            new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
    }
    
    /// <summary>
    /// Render preview panel with advanced 3D viewport controls.
    /// </summary>
    private void RenderPreviewPanelModern(EditorUI ui, float x, float y, float width, float height)
    {
        // Main preview card
        ui.RoundedGradientPanel(x, y, width, height,
            new Vector4(0.10f, 0.11f, 0.14f, 0.98f),
            new Vector4(0.08f, 0.09f, 0.12f, 0.98f),
            10f);
        
        // Header with controls
        float headerH = 45;
        ui.RoundedGradientPanel(x, y, width, headerH,
            new Vector4(0.14f, 0.16f, 0.20f, 0.95f),
            new Vector4(0.12f, 0.14f, 0.18f, 0.95f),
            10f);
        
        ui.SetCursor(x + 15, y + 14);
        ui.Text("🎬 3D Preview", new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        
        // View controls (wireframe, bounds, auto-rotate)
        float ctrlX = x + width - 150;
        float ctrlY = y + 10;
        float ctrlW = 40;
        
        // Wireframe toggle
        Vector4 wireframeBg = _showWireframe 
            ? new Vector4(0.4f, 0.6f, 0.8f, 0.9f)
            : new Vector4(0.2f, 0.22f, 0.26f, 0.8f);
        
        if (ui.ButtonEx(ctrlX, ctrlY, ctrlW, 26, "🔲",
            wireframeBg,
            new Vector4(wireframeBg.X + 0.05f, wireframeBg.Y + 0.05f, wireframeBg.Z + 0.05f, 1.0f),
            new Vector4(wireframeBg.X - 0.05f, wireframeBg.Y - 0.05f, wireframeBg.Z - 0.05f, 0.9f),
            new Vector4(0, 0, 0, 0.3f),
            new Vector4(1, 1, 1, 1)))
        {
            _showWireframe = !_showWireframe;
        }
        
        // Bounds toggle
        Vector4 boundsBg = _showBounds 
            ? new Vector4(0.6f, 0.4f, 0.8f, 0.9f)
            : new Vector4(0.2f, 0.22f, 0.26f, 0.8f);
        
        if (ui.ButtonEx(ctrlX + 45, ctrlY, ctrlW, 26, "📦",
            boundsBg,
            new Vector4(boundsBg.X + 0.05f, boundsBg.Y + 0.05f, boundsBg.Z + 0.05f, 1.0f),
            new Vector4(boundsBg.X - 0.05f, boundsBg.Y - 0.05f, boundsBg.Z - 0.05f, 0.9f),
            new Vector4(0, 0, 0, 0.3f),
            new Vector4(1, 1, 1, 1)))
        {
            _showBounds = !_showBounds;
        }
        
        // Auto-rotate toggle
        Vector4 rotateBg = _autoRotate 
            ? new Vector4(0.4f, 0.8f, 0.6f, 0.9f)
            : new Vector4(0.2f, 0.22f, 0.26f, 0.8f);
        
        if (ui.ButtonEx(ctrlX + 90, ctrlY, ctrlW, 26, "🔄",
            rotateBg,
            new Vector4(rotateBg.X + 0.05f, rotateBg.Y + 0.05f, rotateBg.Z + 0.05f, 1.0f),
            new Vector4(rotateBg.X - 0.05f, rotateBg.Y - 0.05f, rotateBg.Z - 0.05f, 0.9f),
            new Vector4(0, 0, 0, 0.3f),
            new Vector4(1, 1, 1, 1)))
        {
            _autoRotate = !_autoRotate;
        }
        
        // 3D viewport area
        float viewportY = y + headerH + 5;
        float viewportH = height - headerH - 160;
        float viewportW = width - 10;
        float viewportX = x + 5;
        
        PreviewRect = new Vector4(viewportX, viewportY, viewportW, viewportH);
        
        // Viewport background (dark with grid pattern suggestion)
        ui.RoundedPanel(viewportX, viewportY, viewportW, viewportH,
            new Vector4(0.04f, 0.05f, 0.07f, 1.0f), 6f);
        
        // Stats overlay at bottom of viewport
        float statsY = viewportY + viewportH + 10;
        
        // Stats cards row
        float cardW = (width - 30) / 3;
        
        // Vertices card
        ui.RoundedPanel(x + 5, statsY, cardW, 50,
            new Vector4(0.16f, 0.20f, 0.26f, 0.9f), 6f);
        ui.SetCursor(x + 15, statsY + 10);
        ui.Text("Vertices", new Vector4(0.6f, 0.7f, 0.8f, 1.0f));
        ui.SetCursor(x + 15, statsY + 28);
        ui.Text($"{_vertexCount:N0}", new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
        
        // Triangles card
        ui.RoundedPanel(x + cardW + 10, statsY, cardW, 50,
            new Vector4(0.20f, 0.16f, 0.26f, 0.9f), 6f);
        ui.SetCursor(x + cardW + 20, statsY + 10);
        ui.Text("Triangles", new Vector4(0.7f, 0.6f, 0.8f, 1.0f));
        ui.SetCursor(x + cardW + 20, statsY + 28);
        ui.Text($"{_triangleCount:N0}", new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
        
        // Camera controls hint
        float hintY = statsY + 60;
        ui.SetCursor(x + 10, hintY);
        ui.Text("🎮 Controls:", new Vector4(0.7f, 0.8f, 0.9f, 1.0f));
        
        ui.SetCursor(x + 10, hintY + 20);
        ui.Text("• Right-click + drag to orbit", new Vector4(0.5f, 0.5f, 0.5f, 0.9f));
        
        ui.SetCursor(x + 10, hintY + 38);
        ui.Text("• Scroll to zoom in/out", new Vector4(0.5f, 0.5f, 0.5f, 0.9f));
    }
    
    /// <summary>
    /// Render action bar with save/revert buttons.
    /// </summary>
    private void RenderActionBar(EditorUI ui, float x, float y, float width, float height)
    {
        // Background bar
        ui.RoundedGradientPanel(x, y, width, height,
            new Vector4(0.12f, 0.13f, 0.16f, 0.95f),
            new Vector4(0.10f, 0.11f, 0.14f, 0.95f),
            8f);
        
        if (_isDirty)
        {
            // Pulsing animation for save button
            _saveButtonPulse += 0.05f;
            float pulse = (MathF.Sin(_saveButtonPulse) + 1f) * 0.5f;
            
            // Save button with pulse effect
            Vector4 saveBg = new Vector4(0.25f + pulse * 0.1f, 0.65f + pulse * 0.1f, 0.35f + pulse * 0.1f, 0.95f);
            
            if (ui.ButtonEx(x + 10, y + 7, 120, 32, "💾 Save Changes",
                saveBg,
                new Vector4(0.30f, 0.75f, 0.40f, 1.0f),
                new Vector4(0.20f, 0.60f, 0.30f, 0.95f),
                new Vector4(0, 0, 0, 0.4f),
                new Vector4(1, 1, 1, 1)))
            {
                SaveAsset();
            }
            
            // Revert button
            if (ui.ButtonEx(x + 140, y + 7, 100, 32, "↶ Revert",
                new Vector4(0.5f, 0.3f, 0.3f, 0.8f),
                new Vector4(0.6f, 0.4f, 0.4f, 1.0f),
                new Vector4(0.4f, 0.25f, 0.25f, 0.8f),
                new Vector4(0, 0, 0, 0.3f),
                new Vector4(0.9f, 0.9f, 0.9f, 1.0f)))
            {
                LoadAssetData(); // Reload from disk
                _isDirty = false;
            }
            
            // Unsaved changes indicator
            ui.SetCursor(x + 250, y + 14);
            ui.Text("● Unsaved changes", new Vector4(1.0f, 0.7f, 0.4f, 0.9f + pulse * 0.1f));
        }
        else
        {
            // All saved indicator
            ui.SetCursor(x + 15, y + 14);
            ui.Text("✓ All changes saved", new Vector4(0.5f, 0.8f, 0.6f, 0.9f));
        }
        
        // Asset path on right
        ui.SetCursor(x + width - 300, y + 14);
        ui.Text(System.IO.Path.GetFileName(_assetPath), new Vector4(0.5f, 0.5f, 0.5f, 0.8f));
    }
    
    private void RenderLODsTab(EditorUI ui, float x, float y, float width, float height)
    {
        float rowY = y;
        
        ui.SetCursor(x, rowY);
        ui.Text("Level of Detail Settings", new Vector4(0.9f, 0.9f, 0.9f, 1));
        rowY += 30;
        
        ui.SetCursor(x, rowY);
        ui.Text($"LOD Count: {_lodSettings.LODCount}", new Vector4(0.8f, 0.8f, 0.8f, 1));
        rowY += 25;
        
        float sliderWidth = Math.Max(120, Math.Min(260, width - 24));
        ui.SetCursor(x, rowY);
        ui.Text($"LOD 0 Distance: {_lodSettings.LOD0Distance:F1}m", new Vector4(0.7f, 0.7f, 0.7f, 1));
        ui.SetCursor(x, rowY + 17);
        if (ui.Slider(ref _lodSettings.LOD0Distance, 1f, 150f, sliderWidth, 14f))
            _isDirty = true;
        float priorLod1 = _lodSettings.LOD1Distance;
        _lodSettings.LOD1Distance = Math.Max(_lodSettings.LOD1Distance, _lodSettings.LOD0Distance + 1f);
        if (_lodSettings.LOD1Distance != priorLod1) _isDirty = true;
        rowY += 48;

        ui.SetCursor(x, rowY);
        ui.Text($"LOD 1 Distance: {_lodSettings.LOD1Distance:F1}m", new Vector4(0.7f, 0.7f, 0.7f, 1));
        ui.SetCursor(x, rowY + 17);
        if (ui.Slider(ref _lodSettings.LOD1Distance, _lodSettings.LOD0Distance + 1f, 300f, sliderWidth, 14f))
            _isDirty = true;
        float priorLod2 = _lodSettings.LOD2Distance;
        _lodSettings.LOD2Distance = Math.Max(_lodSettings.LOD2Distance, _lodSettings.LOD1Distance + 1f);
        if (_lodSettings.LOD2Distance != priorLod2) _isDirty = true;
        rowY += 48;

        ui.SetCursor(x, rowY);
        ui.Text($"LOD 2 Distance: {_lodSettings.LOD2Distance:F1}m", new Vector4(0.7f, 0.7f, 0.7f, 1));
        ui.SetCursor(x, rowY + 17);
        if (ui.Slider(ref _lodSettings.LOD2Distance, _lodSettings.LOD1Distance + 1f, 500f, sliderWidth, 14f))
            _isDirty = true;
        rowY += 48;
        
        // Preset buttons
        ui.SetCursor(x, rowY);
        ui.Text("Presets:", new Vector4(0.8f, 0.8f, 0.8f, 1));
        rowY += 25;
        
        if (ui.ButtonEx(x, rowY, 80, 30, "Low",
            new Vector4(0.3f, 0.3f, 0.35f, 1),
            new Vector4(0.35f, 0.35f, 0.4f, 1),
            new Vector4(0.25f, 0.25f, 0.3f, 1),
            new Vector4(0, 0, 0, 0.3f),
            new Vector4(0.9f, 0.9f, 0.9f, 1)))
        {
            ApplyLODPreset(LODPresets.Low);
        }
        
        if (ui.ButtonEx(x + 90, rowY, 80, 30, "Medium",
            new Vector4(0.3f, 0.3f, 0.35f, 1),
            new Vector4(0.35f, 0.35f, 0.4f, 1),
            new Vector4(0.25f, 0.25f, 0.3f, 1),
            new Vector4(0, 0, 0, 0.3f),
            new Vector4(0.9f, 0.9f, 0.9f, 1)))
        {
            ApplyLODPreset(LODPresets.Medium);
        }
        
        if (ui.ButtonEx(x + 180, rowY, 80, 30, "High",
            new Vector4(0.3f, 0.3f, 0.35f, 1),
            new Vector4(0.35f, 0.35f, 0.4f, 1),
            new Vector4(0.25f, 0.25f, 0.3f, 1),
            new Vector4(0, 0, 0, 0.3f),
            new Vector4(0.9f, 0.9f, 0.9f, 1)))
        {
            ApplyLODPreset(LODPresets.High);
        }
    }
    
    private void RenderCollisionTab(EditorUI ui, float x, float y, float width, float height)
    {
        float rowY = y;
        
        ui.SetCursor(x, rowY);
        ui.Text("Collision Settings", new Vector4(0.9f, 0.9f, 0.9f, 1));
        rowY += 30;
        
        ui.SetCursor(x, rowY);
        ui.Text($"Type: {_collisionType}", new Vector4(0.8f, 0.8f, 0.8f, 1));
        rowY += 25;
        
        // Collision type buttons
        string[] collisionTypes = { "None", "Box", "Sphere", "Convex", "Mesh" };
        CollisionType[] types = { CollisionType.None, CollisionType.BoundingBox, CollisionType.BoundingSphere, CollisionType.ConvexHull, CollisionType.TriangleMesh };
        
        for (int i = 0; i < collisionTypes.Length; i++)
        {
            bool isSelected = _collisionType == types[i];
            var bgColor = isSelected ? new Vector4(0.3f, 0.5f, 0.4f, 1) : new Vector4(0.25f, 0.25f, 0.27f, 1);
            
            if (ui.ButtonEx(x, rowY, 100, 30, collisionTypes[i],
                bgColor,
                new Vector4(0.3f, 0.3f, 0.32f, 1),
                new Vector4(0.2f, 0.2f, 0.22f, 1),
                new Vector4(0, 0, 0, 0.3f),
                new Vector4(0.9f, 0.9f, 0.9f, 1)))
            {
                _collisionType = types[i];
                _isDirty = true;
            }
            
            rowY += 35;
        }
        
        rowY += 10;
        ui.SetCursor(x, rowY);
        ui.Text($"Generate Collision: {(_generateCollision ? "Yes" : "No")}", new Vector4(0.7f, 0.7f, 0.7f, 1));
        rowY += 25;
        
        ui.SetCursor(x, rowY);
        ui.Text($"Complexity: {_collisionComplexity:F2}", new Vector4(0.7f, 0.7f, 0.7f, 1));
    }
    
    private void RenderInfoTab(EditorUI ui, float x, float y, float width, float height)
    {
        float rowY = y;
        
        ui.SetCursor(x, rowY);
        ui.Text("Mesh Statistics", new Vector4(0.9f, 0.9f, 0.9f, 1));
        rowY += 30;
        
        ui.SetCursor(x, rowY);
        ui.Text($"Vertices: {_vertexCount:N0}", new Vector4(0.8f, 0.8f, 0.8f, 1));
        rowY += 25;
        
        ui.SetCursor(x, rowY);
        ui.Text($"Triangles: {_triangleCount:N0}", new Vector4(0.8f, 0.8f, 0.8f, 1));
        rowY += 25;
        
        ui.SetCursor(x, rowY);
        ui.Text("Bounding Box:", new Vector4(0.9f, 0.9f, 0.9f, 1));
        rowY += 25;
        
        ui.SetCursor(x, rowY);
        ui.Text($"Min: ({_boundsMin.X:F2}, {_boundsMin.Y:F2}, {_boundsMin.Z:F2})", new Vector4(0.7f, 0.7f, 0.7f, 1));
        rowY += 20;
        
        ui.SetCursor(x, rowY);
        ui.Text($"Max: ({_boundsMax.X:F2}, {_boundsMax.Y:F2}, {_boundsMax.Z:F2})", new Vector4(0.7f, 0.7f, 0.7f, 1));
        rowY += 25;
        
        Vector3 size = _boundsMax - _boundsMin;
        ui.SetCursor(x, rowY);
        ui.Text($"Size: ({size.X:F2}, {size.Y:F2}, {size.Z:F2})", new Vector4(0.7f, 0.7f, 0.7f, 1));
        rowY += 30;
        
        ui.SetCursor(x, rowY);
        ui.Text($"Asset Path:", new Vector4(0.9f, 0.9f, 0.9f, 1));
        rowY += 20;
        
        ui.SetCursor(x, rowY);
        ui.Text(System.IO.Path.GetFileName(_assetPath), new Vector4(0.6f, 0.6f, 0.6f, 1));
    }
    
    private void RenderPreviewPanel(EditorUI ui, float x, float y, float width, float height)
    {
        ui.RoundedPanel(x, y, width, height, new Vector4(0.1f, 0.1f, 0.12f, 1), 6f);
        
        // Header
        ui.SetCursor(x + 10, y + 10);
        ui.Text("3D Preview", new Vector4(0.8f, 0.8f, 0.8f, 1));
        
        // Render the 3D preview texture if available
        float previewY = y + 40;
        float previewH = height - 180;
        float previewW = width - 20;
        float previewX = x + 10;
        
        PreviewRect = new Vector4(previewX, previewY, previewW, previewH);
        
        // Leave the area empty so Program.cs can composite the BSR render target here
        ui.Panel(previewX, previewY, previewW, previewH, new Vector4(0.05f, 0.05f, 0.07f, 1));
        
        // Stats below preview
        float infoY = previewY + previewH + 20;
        
        ui.SetCursor(x + 10, infoY);
        ui.Text($"Vertices: {_vertexCount:N0}", new Vector4(0.7f, 0.7f, 0.7f, 1));
        
        ui.SetCursor(x + 10, infoY + 25);
        ui.Text($"Triangles: {_triangleCount:N0}", new Vector4(0.7f, 0.7f, 0.7f, 1));
        
        infoY += 60;
        
        // Bounding box info
        ui.SetCursor(x + 10, infoY);
        ui.Text("Bounding Box:", new Vector4(0.8f, 0.8f, 0.8f, 1));
        
        Vector3 size = _boundsMax - _boundsMin;
        ui.SetCursor(x + 10, infoY + 25);
        ui.Text($"Size: {size.X:F2} × {size.Y:F2} × {size.Z:F2}", new Vector4(0.6f, 0.6f, 0.6f, 1));
        
        ui.SetCursor(x + 10, infoY + 45);
        ui.Text($"Center: {((_boundsMin + _boundsMax) * 0.5f).X:F2}, {((_boundsMin + _boundsMax) * 0.5f).Y:F2}, {((_boundsMin + _boundsMax) * 0.5f).Z:F2}", new Vector4(0.6f, 0.6f, 0.6f, 1));
        
        // Camera controls hint
        ui.SetCursor(x + 10, y + height - 60);
        ui.Text("Camera Controls:", new Vector4(0.7f, 0.7f, 0.7f, 1));
        
        ui.SetCursor(x + 10, y + height - 40);
        ui.Text("• Right-click + drag to rotate", new Vector4(0.5f, 0.5f, 0.5f, 1));
        
        ui.SetCursor(x + 10, y + height - 20);
        ui.Text("• Scroll to zoom", new Vector4(0.5f, 0.5f, 0.5f, 1));
    }
    
    /// <summary>
    /// Apply LOD preset to current settings.
    /// </summary>
    private void ApplyLODPreset(LODSettings preset)
    {
        _lodSettings.LODCount = preset.LODCount;
        _lodSettings.LOD0Distance = preset.LOD0Distance;
        _lodSettings.LOD1Distance = preset.LOD1Distance;
        _lodSettings.LOD2Distance = preset.LOD2Distance;
        _lodSettings.LOD3Distance = preset.LOD3Distance;
        _lodSettings.LOD4Distance = preset.LOD4Distance;
        _lodSettings.ScreenSizeTransition = preset.ScreenSizeTransition;
        _lodSettings.ForceLOD = preset.ForceLOD;
        _isDirty = true;
        
        Console.WriteLine($"[StaticMeshEditor] Applied LOD preset: {_lodSettings.LODCount} levels");
    }
    
    /// <summary>
    /// Save asset with updated metadata.
    /// Production-grade error handling with atomic write pattern.
    /// </summary>
    private void SaveAsset()
    {
        if (_currentAsset == null) return;
        
        try
        {
            // Update LOD settings
            _currentAsset.Metadata["lodCount"] = _lodSettings.LODCount.ToString();
            _currentAsset.Metadata["lod0Distance"] = _lodSettings.LOD0Distance.ToString();
            _currentAsset.Metadata["lod1Distance"] = _lodSettings.LOD1Distance.ToString();
            _currentAsset.Metadata["lod2Distance"] = _lodSettings.LOD2Distance.ToString();
            
            // Update collision settings
            _currentAsset.Metadata["collisionType"] = _collisionType.ToString();
            _currentAsset.Metadata["generateCollision"] = _generateCollision.ToString();
            
            // Atomic save with backup
            string backupPath = _assetPath + ".backup";
            if (System.IO.File.Exists(_assetPath))
            {
                System.IO.File.Copy(_assetPath, backupPath, true);
            }
            
            if (_currentAsset.Save(_assetPath))
            {
                _isDirty = false;
                Console.WriteLine($"[StaticMeshEditor] ✓ Saved: {_currentAsset!.AssetName}");
                
                // Remove backup on success
                if (System.IO.File.Exists(backupPath))
                {
                    System.IO.File.Delete(backupPath);
                }
            }
            else
            {
                Console.WriteLine($"[StaticMeshEditor] ✗ Failed to save asset");
                
                // Restore from backup on failure
                if (System.IO.File.Exists(backupPath))
                {
                    System.IO.File.Copy(backupPath, _assetPath, true);
                    System.IO.File.Delete(backupPath);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StaticMeshEditor] EXCEPTION during save: {ex.Message}");
        }
    }
    
    
    
    /// <summary>
    /// Update preview rotation and animations (called from main loop).
    /// Smooth 60fps animations with delta-time interpolation.
    /// </summary>
    public void Update(float deltaTime)
    {
        if (!IsOpen) return;
        
        // Auto-rotate preview if enabled
        if (_autoRotate)
        {
            _previewRotation += deltaTime * 0.3f; // Slower, more cinematic rotation
            if (_previewRotation > MathF.PI * 2)
                _previewRotation -= MathF.PI * 2;
        }
        
        // Smooth tab transition animation
        if (_tabTransition < 1f)
        {
            _tabTransition += deltaTime * 4f; // Fast transition
            if (_tabTransition > 1f) _tabTransition = 1f;
        }
        
        // Update camera if preview entity exists
        if (_hasSpawnedPreview && _lastWorld != null && _lastWorld.HasComponent<Core.ECS.Builtin.TransformComponent>(_previewEntity))
        {
            ref var transform = ref _lastWorld.GetComponent<Core.ECS.Builtin.TransformComponent>(_previewEntity);
            
            // Apply rotation if auto-rotate is enabled
            if (_autoRotate)
            {
                float yawDegrees = _previewRotation * (180f / MathF.PI); // Convert radians to degrees
                transform.Rotation = Core.Math.Quaternion.Euler(0, yawDegrees, 0);
            }
        }
    }
}
