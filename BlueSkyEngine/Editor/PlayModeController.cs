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
using NotBSRenderer;

namespace BlueSky.Editor;

partial class Program
{
    private static void SyncPhysicsToTransforms()
    {
        if (_world == null || _physicsWorld == null) return;

        // Query all entities with rigidbody + transform
        var physicsQuery = _world.CreateQuery()
            .All<BlueSky.Core.ECS.Builtin.RigidbodyComponent>()
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
                
                // Get physics position/rotation
                var physPos = _physicsWorld.GetPosition(entity);
                var physRot = _physicsWorld.GetRotation(entity);

                // Update transform
                ref var transform = ref chunk.GetComponent<BlueSky.Core.ECS.Builtin.TransformComponent>(i, transIdx);
                transform.Position = new BlueSky.Core.Math.Vector3(physPos.X, physPos.Y, physPos.Z);
                transform.Rotation = new BlueSky.Core.Math.Quaternion(physRot.X, physRot.Y, physRot.Z, physRot.W);
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

        var entity = _world.GetAllEntities().FirstOrDefault(e => e.Id == _selectedEntityId);
        if (entity.Id == 0 || !_world.TryGetComponent<TerrainComponent>(entity, out var terrain))
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
            MaterialMode = (int)TerrainMaterialMode.SimpleTwoLayer,
            CollisionEnabled = true,
            NeedsRebuild = true,
            MeshHandle = 0
        };
        terrain.TerrainAssetPath = terrainAssetPath;
        _world.AddComponent(entity, terrain);

        // Initialize heightmap in terrain system
        _terrainSystem.InitializeTerrain((uint)entity.Id, terrain);
        if (!string.IsNullOrEmpty(terrainAssetPath))
            _terrainSystem.SaveTerrainAsset((uint)entity.Id, terrainAssetPath);

        // Add name
        var name = new NameComponent();
        name.SetName($"Terrain_{entity.Id}");
        _world.AddComponent(entity, name);

        // Select the new terrain
        _selectedEntityId = (uint)entity.Id;

        Log($"✓ Created terrain entity {entity.Id}");
    }


}
