using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using BlueSky.Editor.UI;
using BlueSky.Platform;
using BlueSky.Platform.Input;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Math;
using BlueSky.Rendering;
using BlueSky.Core.Scripting;
using BlueSky.Core.Scene;
using BlueSky.Rendering.RHI;

namespace BlueSky.Editor;

partial class Program
{
    private static void SyncPhysicsToTransforms()
    {
        if (_world == null || _physicsWorld == null) return;

        // Query all entities with rigidbody + transform
        var physicsQuery = _world.CreateQuery()
            .All<BlueSky.Core.ECS.Builtin.PhysicsComponent>()
            .All<BlueSky.Core.ECS.Builtin.TransformComponent>()
            .Build();

        var chunks = _world.GetQueryChunks(physicsQuery);
        foreach (var chunk in chunks)
        {
            var entities = chunk.GetEntities();
            int transIdx = chunk.GetComponentIndex(typeof(BlueSky.Core.ECS.Builtin.TransformComponent));

            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                if (!_physicsWorld.HasBody(entity))
                    continue;
                
                // Interpolate between fixed ticks for a stable render pose.
                if (!_currentEditorPhysicsState.TryGetValue(entity.Id, out var current))
                {
                    var initialPos = _physicsWorld.GetPosition(entity);
                    var initialRot = _physicsWorld.GetRotation(entity);
                    current = (initialPos, initialRot);
                    _prevEditorPhysicsState[entity.Id] = current;
                    _currentEditorPhysicsState[entity.Id] = current;
                }

                var previous = _prevEditorPhysicsState.TryGetValue(entity.Id, out var previousState)
                    ? previousState
                    : current;
                float alpha = Math.Clamp((float)(_physicsAccumulator / FixedTimeStep), 0.0f, 1.0f);
                var physPos = System.Numerics.Vector3.Lerp(previous.pos, current.pos, alpha);
                var physRot = System.Numerics.Quaternion.Slerp(previous.rot, current.rot, alpha);

                // Update transform
                ref var transform = ref chunk.GetComponent<BlueSky.Core.ECS.Builtin.TransformComponent>(i, transIdx);
                transform.Position = new BlueSky.Core.Math.Vector3(physPos.X, physPos.Y, physPos.Z);
                transform.Rotation = new BlueSky.Core.Math.Quaternion(physRot.X, physRot.Y, physRot.Z, physRot.W);
            }
        }
    }

    private static void CaptureEditorPhysicsState()
    {
        if (_world == null || _physicsWorld == null) return;

        var query = _world.CreateQuery()
            .All<PhysicsComponent>()
            .All<TransformComponent>()
            .Build();

        foreach (var chunk in _world.GetQueryChunks(query))
        {
            var entities = chunk.GetEntities();
            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                if (!_physicsWorld.HasBody(entity)) continue;

                var state = (_physicsWorld.GetPosition(entity), _physicsWorld.GetRotation(entity));
                if (_currentEditorPhysicsState.TryGetValue(entity.Id, out var previous))
                    _prevEditorPhysicsState[entity.Id] = previous;
                else
                    _prevEditorPhysicsState[entity.Id] = state;
                _currentEditorPhysicsState[entity.Id] = state;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TERRAIN CREATION
    // ═══════════════════════════════════════════════════════════════════════

    private static void HandleTerrainSculpting()
    {
        if (!_terrainEditMode || _terrainSystem == null || _viewport == null || _world == null || _input == null)
        {
            _editorViewportRenderer?.SetTerrainBrushPreview(false, default, default, _terrainBrushRadius, _terrainBrushMode);
            return;
        }
        if (_selectedEntityId == 0 || _isDraggingGizmo)
        {
            _editorViewportRenderer?.SetTerrainBrushPreview(false, default, default, _terrainBrushRadius, _terrainBrushMode);
            return;
        }

        var mouse = _input.MousePosition;
        bool inViewport = mouse.X >= _lastViewportRect.X && mouse.X <= _lastViewportRect.X + _lastViewportRect.W &&
                          mouse.Y >= _lastViewportRect.Y && mouse.Y <= _lastViewportRect.Y + _lastViewportRect.H;
        if (!inViewport || _input.IsMouseButtonDown(MouseButton.Right))
        {
            _editorViewportRenderer?.SetTerrainBrushPreview(false, default, default, _terrainBrushRadius, _terrainBrushMode);
            return;
        }

        if (!_world.TryResolveEntity(_selectedEntityId, out var entity) || !_world.TryGetComponent<TerrainComponent>(entity, out var terrain))
        {
            _editorViewportRenderer?.SetTerrainBrushPreview(false, default, default, _terrainBrushRadius, _terrainBrushMode);
            return;
        }

        var ray = _viewport.GetRayFromMouse(mouse);
        if (!_terrainSystem.Raycast(_selectedEntityId, ray.Origin, ray.Direction, out var hit))
        {
            _editorViewportRenderer?.SetTerrainBrushPreview(false, default, default, _terrainBrushRadius, _terrainBrushMode);
            return;
        }

        _editorViewportRenderer?.SetTerrainBrushPreview(
            true,
            new System.Numerics.Vector3(hit.Position.X, hit.Position.Y, hit.Position.Z),
            new System.Numerics.Vector3(hit.Normal.X, hit.Normal.Y, hit.Normal.Z),
            _terrainBrushRadius,
            _terrainBrushMode);

        if (!_input.IsMouseButtonDown(MouseButton.Left))
            return;

        _terrainSystem.ApplyBrush(_selectedEntityId, new TerrainBrushStroke
        {
            LocalX = hit.LocalX,
            LocalZ = hit.LocalZ,
            Radius = _terrainBrushRadius,
            Strength = _terrainBrushStrength,
            Mode = _terrainBrushMode,
            TargetHeight = _terrainFlattenHeight,
            Layer = _terrainPaintLayer
        });

        _sceneDirty = true;
    }

    private static void CreateTerrain()
    {
        if (_world == null || _terrainSystem == null)
        {
            Log("Cannot create terrain: World or TerrainSystem not initialized");
            return;
        }

        // Create entity
        var entity = _world.CreateEntity();

        // Add transform at origin
        var transform = new TransformComponent
        {
            Position = new BlueSky.Core.Math.Vector3(0, 0, 0),
            Rotation = BlueSky.Core.Math.Quaternion.Identity,
            Scale = new BlueSky.Core.Math.Vector3(1, 1, 1)
        };
        _world.AddComponent(entity, transform);

        string terrainAssetPath = "";
        if (!string.IsNullOrEmpty(ProjectManager.AssetsDir))
        {
            string terrainDir = Path.Combine(ProjectManager.AssetsDir, "Terrains");
            Directory.CreateDirectory(terrainDir);
            terrainAssetPath = Path.Combine(terrainDir, $"Terrain_{entity.Id}.blueskyasset");
        }

        // Add terrain component with HD 3000-safe default settings.
        var terrain = new TerrainComponent
        {
            Width = 256,
            Height = 256,
            WorldWidth = 100.0f,
            WorldHeight = 100.0f,
            MaxElevation = 20.0f,
            ChunkSize = 32,
            LodCount = 3,
            SurfaceMode = (int)TerrainSurfaceMode.SimpleTwoLayer,
            CollisionEnabled = true,
            NeedsRebuild = false, // Changed: Don't force rebuild on creation - let TerrainSystem handle it
            MeshHandle = 0
        };
        terrain.TerrainAssetPath = terrainAssetPath;
        _world.AddComponent(entity, terrain);

        // Initialize heightmap in terrain system FIRST before any rendering
        _terrainSystem.InitializeTerrain((uint)entity.Id, terrain);
        if (!string.IsNullOrEmpty(terrainAssetPath))
            _terrainSystem.SaveTerrainAsset((uint)entity.Id, terrainAssetPath);
        
        // Now set NeedsRebuild to trigger mesh generation
        terrain.NeedsRebuild = true;
        _world.AddComponent(entity, terrain);

        // Add name
        var name = new NameComponent();
        name.SetName($"Terrain_{entity.Id}");
        _world.AddComponent(entity, name);

        // Select the new terrain
        _selectedEntityId = (uint)entity.Id;

        Log($"✓ Created terrain entity {entity.Id}");
    }


}
