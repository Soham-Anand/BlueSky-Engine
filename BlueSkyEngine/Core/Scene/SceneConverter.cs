using System;
using System.Collections.Generic;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using SysVec3 = System.Numerics.Vector3;
using SysVec4 = System.Numerics.Vector4;
using SysQuat = System.Numerics.Quaternion;
using EngVec3 = BlueSky.Core.Math.Vector3;
using EngQuat = BlueSky.Core.Math.Quaternion;

namespace BlueSky.Core.Scene;

/// <summary>
/// Converts between ECS World and serializable SceneData.
/// Simplified version - expand as needed.
/// </summary>
public static class SceneConverter
{
    public static SceneData WorldToSceneData(World world, string sceneName = "Untitled Scene")
    {
        var sceneData = new SceneData
        {
            Name = sceneName,
            Version = "1.0"
        };

        // Query all entities with transforms
        var query = world.CreateQuery()
            .All<TransformComponent>()
            .Build();

        var chunks = world.GetQueryChunks(query);

        foreach (var chunk in chunks)
        {
            var entities = chunk.GetEntities();
            int transformIndex = chunk.GetComponentIndex(typeof(TransformComponent));
            
            // Check which components this archetype has
            bool hasMesh = chunk.Archetype.HasComponent(typeof(StaticMeshComponent));
            bool hasSkeletalMesh = chunk.Archetype.HasComponent(typeof(SkeletalMeshComponent));
            bool hasName = chunk.Archetype.HasComponent(typeof(NameComponent));
            bool hasTerrain = chunk.Archetype.HasComponent(typeof(TerrainComponent));
            
            int meshIndex = hasMesh ? chunk.GetComponentIndex(typeof(StaticMeshComponent)) : -1;
            int skeletalMeshIndex = hasSkeletalMesh ? chunk.GetComponentIndex(typeof(SkeletalMeshComponent)) : -1;
            int nameIndex = hasName ? chunk.GetComponentIndex(typeof(NameComponent)) : -1;
            int terrainIndex = hasTerrain ? chunk.GetComponentIndex(typeof(TerrainComponent)) : -1;

            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                var entityData = new EntityData { Id = entity.Id };

                // Name
                if (nameIndex >= 0)
                {
                    var name = chunk.GetComponent<NameComponent>(i, nameIndex);
                    entityData.Name = name.Name;
                }
                else
                {
                    entityData.Name = $"Entity_{entity.Id}";
                }

                // Transform
                var transform = chunk.GetComponent<TransformComponent>(i, transformIndex);
                entityData.Components.Add(new TransformComponentData
                {
                    Position = ToSysVec3(transform.Position),
                    Rotation = new SysVec4(transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W),
                    Scale = ToSysVec3(transform.Scale)
                });

                // StaticMesh
                if (meshIndex >= 0)
                {
                    var mesh = chunk.GetComponent<StaticMeshComponent>(i, meshIndex);
                    var meshData = new StaticMeshComponentData
                    {
                        MeshAssetId = mesh.MeshAssetId ?? "",
                        MaterialSlots = new List<string>()
                    };

                    for (int slot = 0; slot < 8; slot++)
                    {
                        meshData.MaterialSlots.Add(mesh.GetMaterialSlot(slot) ?? "");
                    }

                    entityData.Components.Add(meshData);
                }

                // SkeletalMesh
                if (skeletalMeshIndex >= 0)
                {
                    var skeletalMesh = chunk.GetComponent<SkeletalMeshComponent>(i, skeletalMeshIndex);
                    var skeletalMeshData = new SkeletalMeshComponentData
                    {
                        MeshAssetPath = skeletalMesh.MeshAssetPath ?? "",
                        IsLoaded = skeletalMesh.IsLoaded,
                        MaterialSlots = new List<string>()
                    };

                    for (int slot = 0; slot < 8; slot++)
                    {
                        skeletalMeshData.MaterialSlots.Add(skeletalMesh.GetMaterialSlot(slot) ?? "");
                    }

                    entityData.Components.Add(skeletalMeshData);
                }

                if (terrainIndex >= 0)
                {
                    var terrain = chunk.GetComponent<TerrainComponent>(i, terrainIndex);
                    entityData.Components.Add(new TerrainComponentData
                    {
                        TerrainAssetPath = terrain.TerrainAssetPath,
                        Width = terrain.Width,
                        Height = terrain.Height,
                        WorldWidth = terrain.WorldWidth,
                        WorldHeight = terrain.WorldHeight,
                        MaxElevation = terrain.MaxElevation,
                        ChunkSize = terrain.ChunkSize,
                        LodCount = terrain.LodCount,
                        MaterialMode = terrain.MaterialMode,
                        CollisionEnabled = terrain.CollisionEnabled
                    });
                }

                sceneData.Entities.Add(entityData);
            }
        }

        Console.WriteLine($"[SceneConverter] Exported {sceneData.Entities.Count} entities");
        return sceneData;
    }

    public static void SceneDataToWorld(SceneData sceneData, World world, bool clearWorld = true)
    {
        if (clearWorld)
        {
            // Simple clear - get all entities and destroy them
            var allQuery = world.CreateQuery().Build();
            var allChunks = world.GetQueryChunks(allQuery);
            var entitiesToDestroy = new List<Entity>();

            foreach (var chunk in allChunks)
            {
                var entities = chunk.GetEntities();
                for (int i = 0; i < entities.Length; i++)
                {
                    entitiesToDestroy.Add(entities[i]);
                }
            }

            foreach (var entity in entitiesToDestroy)
            {
                world.DestroyEntity(entity);
            }
        }

        // Create entities from scene data
        foreach (var entityData in sceneData.Entities)
        {
            var entity = world.CreateEntity();

            foreach (var compData in entityData.Components)
            {
                switch (compData)
                {
                    case TransformComponentData t:
                        world.AddComponent(entity, new TransformComponent
                        {
                            Position = ToEngVec3(t.Position),
                            Rotation = new EngQuat(t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W),
                            Scale = ToEngVec3(t.Scale)
                        });
                        break;

                    case StaticMeshComponentData m:
                        var meshComp = new StaticMeshComponent
                        {
                            MeshAssetId = m.MeshAssetId
                        };

                        for (int slot = 0; slot < m.MaterialSlots.Count && slot < 8; slot++)
                        {
                            if (!string.IsNullOrEmpty(m.MaterialSlots[slot]))
                            {
                                meshComp.SetMaterialSlot(slot, m.MaterialSlots[slot]);
                            }
                        }

                        world.AddComponent(entity, meshComp);
                        break;

                    case SkeletalMeshComponentData s:
                        var skeletalMeshComp = new SkeletalMeshComponent(s.MeshAssetPath)
                        {
                            IsLoaded = s.IsLoaded
                        };

                        for (int slot = 0; slot < s.MaterialSlots.Count && slot < 8; slot++)
                        {
                            if (!string.IsNullOrEmpty(s.MaterialSlots[slot]))
                            {
                                skeletalMeshComp.SetMaterialSlot(slot, s.MaterialSlots[slot]);
                            }
                        }

                        world.AddComponent(entity, skeletalMeshComp);
                        break;

                    case TerrainComponentData t:
                        var terrainComp = new TerrainComponent
                        {
                            Width = t.Width,
                            Height = t.Height,
                            WorldWidth = t.WorldWidth,
                            WorldHeight = t.WorldHeight,
                            MaxElevation = t.MaxElevation,
                            ChunkSize = t.ChunkSize,
                            LodCount = t.LodCount,
                            MaterialMode = t.MaterialMode,
                            CollisionEnabled = t.CollisionEnabled,
                            NeedsRebuild = true
                        };
                        terrainComp.TerrainAssetPath = t.TerrainAssetPath;
                        world.AddComponent(entity, terrainComp);
                        break;
                }
            }

            // Add name
            world.AddComponent(entity, new NameComponent(entityData.Name));
        }

        Console.WriteLine($"[SceneConverter] Imported {sceneData.Entities.Count} entities");
    }

    // Helper conversions
    private static SysVec3 ToSysVec3(EngVec3 v) => new SysVec3(v.X, v.Y, v.Z);
    private static EngVec3 ToEngVec3(SysVec3 v) => new EngVec3(v.X, v.Y, v.Z);
}
