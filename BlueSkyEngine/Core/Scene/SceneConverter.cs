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
            bool hasPhysics = chunk.Archetype.HasComponent(typeof(PhysicsComponent));
            bool hasTeaScript = chunk.Archetype.HasComponent(typeof(TeaScriptComponent));
            bool hasCarController = chunk.Archetype.HasComponent(typeof(CarControllerComponent));
            bool hasCamera = chunk.Archetype.HasComponent(typeof(CameraComponent));
            bool hasLight = chunk.Archetype.HasComponent(typeof(LightComponent));
            bool hasNetworkManager = chunk.Archetype.HasComponent(typeof(NetworkManagerComponent));
            bool hasSpawnPoint = chunk.Archetype.HasComponent(typeof(SpawnPointComponent));
            
            int meshIndex = hasMesh ? chunk.GetComponentIndex(typeof(StaticMeshComponent)) : -1;
            int skeletalMeshIndex = hasSkeletalMesh ? chunk.GetComponentIndex(typeof(SkeletalMeshComponent)) : -1;
            int nameIndex = hasName ? chunk.GetComponentIndex(typeof(NameComponent)) : -1;
            int terrainIndex = hasTerrain ? chunk.GetComponentIndex(typeof(TerrainComponent)) : -1;
            int physicsIndex = hasPhysics ? chunk.GetComponentIndex(typeof(PhysicsComponent)) : -1;
            int teaScriptIndex = hasTeaScript ? chunk.GetComponentIndex(typeof(TeaScriptComponent)) : -1;
            int carControllerIndex = hasCarController ? chunk.GetComponentIndex(typeof(CarControllerComponent)) : -1;
            int cameraIndex = hasCamera ? chunk.GetComponentIndex(typeof(CameraComponent)) : -1;
            int lightIndex = hasLight ? chunk.GetComponentIndex(typeof(LightComponent)) : -1;
            int networkManagerIndex = hasNetworkManager ? chunk.GetComponentIndex(typeof(NetworkManagerComponent)) : -1;
            int spawnPointIndex = hasSpawnPoint ? chunk.GetComponentIndex(typeof(SpawnPointComponent)) : -1;

            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                
                // CRITICAL: Skip entities with ID >= 200 (reserved for preview/temporary entities)
                // The editor uses IDs >= 200 for preview meshes, gizmos, etc.
                if (entity.Id >= 200)
                {
                    continue;
                }

                // Skip the editor viewport camera entity to avoid duplicates when loading scenes
                if (hasCamera)
                {
                    continue;
                }
                
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
                    Position = new float[] { transform.Position.X, transform.Position.Y, transform.Position.Z },
                    Rotation = new float[] { transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W },
                    Scale = new float[] { transform.Scale.X, transform.Scale.Y, transform.Scale.Z }
                });

                // StaticMesh
                if (meshIndex >= 0)
                {
                    var mesh = chunk.GetComponent<StaticMeshComponent>(i, meshIndex);
                    
                    // Skip if mesh asset ID is empty or looks like a temporary preview
                    if (string.IsNullOrEmpty(mesh.MeshAssetId))
                    {
                        Console.WriteLine($"[SceneConverter] WARNING: Entity {entity.Id} has empty MeshAssetId - skipping StaticMesh component");
                    }
                    else
                    {
                        var meshData = new StaticMeshComponentData
                        {
                            MeshAssetId = mesh.MeshAssetId ?? ""
                        };

                        entityData.Components.Add(meshData);
                    }
                }

                // SkeletalMesh
                if (skeletalMeshIndex >= 0)
                {
                    var skeletalMesh = chunk.GetComponent<SkeletalMeshComponent>(i, skeletalMeshIndex);
                    
                    // Skip if mesh path is empty
                    if (string.IsNullOrEmpty(skeletalMesh.MeshAssetPath))
                    {
                        Console.WriteLine($"[SceneConverter] WARNING: Entity {entity.Id} has empty MeshAssetPath - skipping SkeletalMesh component");
                    }
                    else
                    {
                        var skeletalMeshData = new SkeletalMeshComponentData
                        {
                            MeshAssetPath = skeletalMesh.MeshAssetPath ?? "",
                            IsLoaded = skeletalMesh.IsLoaded
                        };

                        entityData.Components.Add(skeletalMeshData);
                    }
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
                        SurfaceMode = terrain.SurfaceMode,
                        CollisionEnabled = terrain.CollisionEnabled
                    });
                }

                // Physics (merged Rigidbody + Collider)
                if (physicsIndex >= 0)
                {
                    var phys = chunk.GetComponent<PhysicsComponent>(i, physicsIndex);
                    entityData.Components.Add(new PhysicsComponentData
                    {
                        Mass = phys.Mass,
                        Drag = phys.Drag,
                        AngularDrag = phys.AngularDrag,
                        UseGravity = phys.UseGravity,
                        IsKinematic = phys.IsKinematic,
                        FreezePositionX = phys.FreezePositionX,
                        FreezePositionY = phys.FreezePositionY,
                        FreezePositionZ = phys.FreezePositionZ,
                        FreezeRotationX = phys.FreezeRotationX,
                        FreezeRotationY = phys.FreezeRotationY,
                        FreezeRotationZ = phys.FreezeRotationZ,
                        ColliderType = phys.Type.ToString(),
                        Center = new float[] { phys.Center.X, phys.Center.Y, phys.Center.Z },
                        Size = new float[] { phys.Size.X, phys.Size.Y, phys.Size.Z },
                        Radius = phys.Radius,
                        Height = phys.Height,
                        IsTrigger = phys.IsTrigger,
                        Friction = phys.Friction,
                        Restitution = phys.Restitution,
                        PhysicsAssetPath = PhysicsAssetCache.GetPath((uint)entity.Id)
                    });
                }

                // TeaScript
                if (teaScriptIndex >= 0)
                {
                    var ts = chunk.GetComponent<TeaScriptComponent>(i, teaScriptIndex);
                    entityData.Components.Add(new TeaScriptComponentData
                    {
                        ScriptAssetId = ts.ScriptAssetId,
                        AllowRuntimeUI = ts.AllowRuntimeUI,
                        BlockRuntimeInput = ts.BlockRuntimeInput
                    });
                }

                // CarController
                if (carControllerIndex >= 0)
                {
                    var cc = chunk.GetComponent<CarControllerComponent>(i, carControllerIndex);
                    entityData.Components.Add(new CarControllerComponentData
                    {
                        MotorForce = cc.MotorForce,
                        BrakeForce = cc.BrakeForce,
                        MaxSteerAngle = cc.MaxSteerAngle,
                        DownForce = cc.DownForce,
                        CenterOfMassOffset = new float[] { cc.CenterOfMassOffset.X, cc.CenterOfMassOffset.Y, cc.CenterOfMassOffset.Z },
                        CameraOffset = new float[] { cc.CameraOffset.X, cc.CameraOffset.Y, cc.CameraOffset.Z },
                        CameraTargetOffset = new float[] { cc.CameraTargetOffset.X, cc.CameraTargetOffset.Y, cc.CameraTargetOffset.Z },
                        WheelPositionFL = new float[] { cc.WheelPositionFL.X, cc.WheelPositionFL.Y, cc.WheelPositionFL.Z },
                        WheelPositionFR = new float[] { cc.WheelPositionFR.X, cc.WheelPositionFR.Y, cc.WheelPositionFR.Z },
                        WheelPositionRL = new float[] { cc.WheelPositionRL.X, cc.WheelPositionRL.Y, cc.WheelPositionRL.Z },
                        WheelPositionRR = new float[] { cc.WheelPositionRR.X, cc.WheelPositionRR.Y, cc.WheelPositionRR.Z },
                        SuspensionRestLength = cc.SuspensionRestLength,
                        SuspensionStiffness = cc.SuspensionStiffness,
                        SuspensionDamping = cc.SuspensionDamping,
                        WheelRadius = cc.WheelRadius
                    });
                }

                // Camera
                if (cameraIndex >= 0)
                {
                    var cam = chunk.GetComponent<CameraComponent>(i, cameraIndex);
                    entityData.Components.Add(new CameraComponentData
                    {
                        Fov = cam.FieldOfView,
                        Near = cam.NearPlane,
                        Far = cam.FarPlane,
                        IsActive = true
                    });
                }

                // Light
                if (lightIndex >= 0)
                {
                    var light = chunk.GetComponent<LightComponent>(i, lightIndex);
                    entityData.Components.Add(new LightComponentData
                    {
                        LightType = light.Type.ToString(),
                        Color = new float[] { light.Color.X, light.Color.Y, light.Color.Z },
                        Intensity = light.Intensity
                    });
                }

                // NetworkManager
                if (networkManagerIndex >= 0)
                {
                    var nm = chunk.GetComponent<NetworkManagerComponent>(i, networkManagerIndex);
                    entityData.Components.Add(new NetworkManagerComponentData
                    {
                        PrefabPath = NetworkManagerStorage.GetPrefabPath(nm.StorageIndex),
                        MaxPlayers = nm.MaxPlayers,
                        ReplicationInterval = nm.ReplicationInterval
                    });
                }

                // SpawnPoint
                if (spawnPointIndex >= 0)
                {
                    var sp = chunk.GetComponent<SpawnPointComponent>(i, spawnPointIndex);
                    entityData.Components.Add(new SpawnPointComponentData
                    {
                        Index = sp.Index
                    });
                }

                // Only add entity if it has at least Transform component (count >= 1)
                if (entityData.Components.Count >= 1)
                {
                    sceneData.Entities.Add(entityData);
                }
                else
                {
                    Console.WriteLine($"[SceneConverter] WARNING: Entity {entity.Id} has no components - skipping");
                }
            }
        }

        Console.WriteLine($"[SceneConverter] Exported {sceneData.Entities.Count} entities");
        return sceneData;
    }

    /// <summary>
    /// Serialize a single entity (and all its components) into a SceneData
    /// suitable for saving as a prefab (.bseprefab).
    /// </summary>
    public static SceneData WorldToSceneDataForEntity(World world, Entity entity, string prefabName = "Prefab")
    {
        var sceneData = new SceneData { Name = prefabName, Version = "1.0" };

        if (!world.IsEntityValid(entity))
            return sceneData;

        var entityData = new EntityData { Id = entity.Id };

        if (world.TryGetComponent<TransformComponent>(entity, out var transform))
        {
            entityData.Components.Add(new TransformComponentData
            {
                Position = new float[] { transform.Position.X, transform.Position.Y, transform.Position.Z },
                Rotation = new float[] { transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W },
                Scale = new float[] { transform.Scale.X, transform.Scale.Y, transform.Scale.Z }
            });
        }

        if (world.TryGetComponent<StaticMeshComponent>(entity, out var mesh))
        {
            if (!string.IsNullOrEmpty(mesh.MeshAssetId))
            {
                var meshData = new StaticMeshComponentData { MeshAssetId = mesh.MeshAssetId };
                entityData.Components.Add(meshData);
            }
        }

        if (world.TryGetComponent<SkeletalMeshComponent>(entity, out var skeletalMesh))
        {
            if (!string.IsNullOrEmpty(skeletalMesh.MeshAssetPath))
            {
                var skeletalMeshData = new SkeletalMeshComponentData
                {
                    MeshAssetPath = skeletalMesh.MeshAssetPath,
                    IsLoaded = skeletalMesh.IsLoaded
                };
                entityData.Components.Add(skeletalMeshData);
            }
        }

        if (world.TryGetComponent<PhysicsComponent>(entity, out var phys))
        {
            entityData.Components.Add(new PhysicsComponentData
            {
                Mass = phys.Mass, Drag = phys.Drag, AngularDrag = phys.AngularDrag,
                UseGravity = phys.UseGravity, IsKinematic = phys.IsKinematic,
                FreezePositionX = phys.FreezePositionX, FreezePositionY = phys.FreezePositionY, FreezePositionZ = phys.FreezePositionZ,
                FreezeRotationX = phys.FreezeRotationX, FreezeRotationY = phys.FreezeRotationY, FreezeRotationZ = phys.FreezeRotationZ,
                ColliderType = phys.Type.ToString(),
                Center = new float[] { phys.Center.X, phys.Center.Y, phys.Center.Z },
                Size = new float[] { phys.Size.X, phys.Size.Y, phys.Size.Z },
                Radius = phys.Radius, Height = phys.Height, IsTrigger = phys.IsTrigger,
                Friction = phys.Friction, Restitution = phys.Restitution,
                PhysicsAssetPath = PhysicsAssetCache.GetPath((uint)entity.Id)
            });
        }

        if (world.TryGetComponent<CarControllerComponent>(entity, out var cc))
        {
            entityData.Components.Add(new CarControllerComponentData
            {
                MotorForce = cc.MotorForce, BrakeForce = cc.BrakeForce, MaxSteerAngle = cc.MaxSteerAngle,
                DownForce = cc.DownForce,
                CenterOfMassOffset = new float[] { cc.CenterOfMassOffset.X, cc.CenterOfMassOffset.Y, cc.CenterOfMassOffset.Z },
                CameraOffset = new float[] { cc.CameraOffset.X, cc.CameraOffset.Y, cc.CameraOffset.Z },
                CameraTargetOffset = new float[] { cc.CameraTargetOffset.X, cc.CameraTargetOffset.Y, cc.CameraTargetOffset.Z },
                WheelPositionFL = new float[] { cc.WheelPositionFL.X, cc.WheelPositionFL.Y, cc.WheelPositionFL.Z },
                WheelPositionFR = new float[] { cc.WheelPositionFR.X, cc.WheelPositionFR.Y, cc.WheelPositionFR.Z },
                WheelPositionRL = new float[] { cc.WheelPositionRL.X, cc.WheelPositionRL.Y, cc.WheelPositionRL.Z },
                WheelPositionRR = new float[] { cc.WheelPositionRR.X, cc.WheelPositionRR.Y, cc.WheelPositionRR.Z },
                SuspensionRestLength = cc.SuspensionRestLength, SuspensionStiffness = cc.SuspensionStiffness,
                SuspensionDamping = cc.SuspensionDamping, WheelRadius = cc.WheelRadius
            });
        }

        if (world.TryGetComponent<TeaScriptComponent>(entity, out var ts))
        {
            entityData.Components.Add(new TeaScriptComponentData
            {
                ScriptAssetId = ts.ScriptAssetId, AllowRuntimeUI = ts.AllowRuntimeUI, BlockRuntimeInput = ts.BlockRuntimeInput
            });
        }

        if (world.TryGetComponent<NetworkManagerComponent>(entity, out var nm))
        {
            entityData.Components.Add(new NetworkManagerComponentData
            {
                PrefabPath = NetworkManagerStorage.GetPrefabPath(nm.StorageIndex),
                MaxPlayers = nm.MaxPlayers, ReplicationInterval = nm.ReplicationInterval
            });
        }

        if (world.TryGetComponent<SpawnPointComponent>(entity, out var sp))
        {
            entityData.Components.Add(new SpawnPointComponentData { Index = sp.Index });
        }

        if (world.TryGetComponent<LightComponent>(entity, out var light))
        {
            entityData.Components.Add(new LightComponentData
            {
                LightType = light.Type.ToString(),
                Color = new float[] { light.Color.X, light.Color.Y, light.Color.Z },
                Intensity = light.Intensity
            });
        }

        if (world.TryGetComponent<NameComponent>(entity, out var name))
            entityData.Name = name.Name;
        else
            entityData.Name = $"Entity_{entity.Id}";

        sceneData.Entities.Add(entityData);
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
                            Position = new EngVec3(t.Position[0], t.Position[1], t.Position[2]),
                            Rotation = new EngQuat(t.Rotation[0], t.Rotation[1], t.Rotation[2], t.Rotation[3]),
                            Scale = new EngVec3(t.Scale[0], t.Scale[1], t.Scale[2])
                        });
                        break;

                    case StaticMeshComponentData m:
                        var meshComp = new StaticMeshComponent
                        {
                            MeshAssetId = m.MeshAssetId
                        };

                        world.AddComponent(entity, meshComp);
                        break;

                    case SkeletalMeshComponentData s:
                        var skeletalMeshComp = new SkeletalMeshComponent(s.MeshAssetPath)
                        {
                            IsLoaded = s.IsLoaded
                        };

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
                            SurfaceMode = t.SurfaceMode,
                            CollisionEnabled = t.CollisionEnabled,
                            NeedsRebuild = false  // Changed: Don't force rebuild on load - let LoadTerrainAssetsForWorld handle it
                        };
                        terrainComp.TerrainAssetPath = t.TerrainAssetPath;
                        world.AddComponent(entity, terrainComp);
                        break;

                    case PhysicsComponentData p:
                        Enum.TryParse<ColliderType>(p.ColliderType, true, out var pType);
                        world.AddComponent(entity, new PhysicsComponent
                        {
                            Mass = SafeFloat(p.Mass, 1.0f),
                            Drag = SafeFloat(p.Drag, 0.0f),
                            AngularDrag = SafeFloat(p.AngularDrag, 0.05f),
                            UseGravity = p.UseGravity,
                            IsKinematic = p.IsKinematic,
                            FreezePositionX = p.FreezePositionX,
                            FreezePositionY = p.FreezePositionY,
                            FreezePositionZ = p.FreezePositionZ,
                            FreezeRotationX = p.FreezeRotationX,
                            FreezeRotationY = p.FreezeRotationY,
                            FreezeRotationZ = p.FreezeRotationZ,
                            Type = pType,
                            Center = SafeVector3FromArray(p.Center),
                            Size = SafeVector3FromArray(p.Size),
                            Radius = SafeFloat(p.Radius, 0.5f),
                            Height = SafeFloat(p.Height, 2.0f),
                            IsTrigger = p.IsTrigger,
                            Friction = SafeFloat(p.Friction, 0.5f),
                            Restitution = SafeFloat(p.Restitution, 0.3f)
                        });
                        PhysicsAssetCache.SetPath((uint)entity.Id, p.PhysicsAssetPath ?? "");
                        break;

                    // ── Migration: old "Rigidbody" → PhysicsComponent with collider defaults ──
                    case RigidbodyComponentData_Migration r:
                        var mass = SafeFloat(r.Mass, 1.0f);
                        if (mass < 0.001f) mass = 1.0f;
                        world.AddComponent(entity, new PhysicsComponent
                        {
                            Mass = mass,
                            Drag = SafeFloat(r.Drag, 0.05f),
                            AngularDrag = SafeFloat(r.AngularDrag, 0.05f),
                            UseGravity = r.UseGravity,
                            IsKinematic = r.IsKinematic,
                            FreezePositionX = r.FreezePositionX,
                            FreezePositionY = r.FreezePositionY,
                            FreezePositionZ = r.FreezePositionZ,
                            FreezeRotationX = r.FreezeRotationX,
                            FreezeRotationY = r.FreezeRotationY,
                            FreezeRotationZ = r.FreezeRotationZ
                        });
                        break;

                    // ── Migration: old "Collider" → merges into existing PhysicsComponent or creates fresh ──
                    case ColliderComponentData_Migration c:
                        Enum.TryParse<ColliderType>(c.ColliderType, true, out var cType);
                        var colCenter = SafeVector3FromArray(c.Center);
                        var colSize = SafeVector3FromArray(c.Size);

                        if (world.HasComponent<PhysicsComponent>(entity))
                        {
                            // Merge collider fields into existing component
                            ref var existingPhys = ref world.GetComponent<PhysicsComponent>(entity);
                            existingPhys.Type = cType;
                            existingPhys.Center = colCenter;
                            existingPhys.Size = colSize;
                            existingPhys.Radius = SafeFloat(c.Radius, 0.5f);
                            existingPhys.Height = SafeFloat(c.Height, 2.0f);
                            existingPhys.IsTrigger = c.IsTrigger;
                            existingPhys.Friction = SafeFloat(c.Friction, 0.5f);
                            existingPhys.Restitution = SafeFloat(c.Restitution, 0.3f);
                        }
                        else
                        {
                            // No rigidbody — create fresh with rigidbody defaults
                            world.AddComponent(entity, new PhysicsComponent
                            {
                                Mass = 1.0f,
                                UseGravity = true,
                                Type = cType,
                                Center = colCenter,
                                Size = colSize,
                                Radius = SafeFloat(c.Radius, 0.5f),
                                Height = SafeFloat(c.Height, 2.0f),
                                IsTrigger = c.IsTrigger,
                                Friction = SafeFloat(c.Friction, 0.5f),
                                Restitution = SafeFloat(c.Restitution, 0.3f)
                            });
                        }
                        break;

                    case TeaScriptComponentData ts:
                        world.AddComponent(entity, new TeaScriptComponent
                        {
                            ScriptAssetId = ts.ScriptAssetId,
                            IsEnabled = true,
                            IsInitialized = false,
                            AllowRuntimeUI = ts.AllowRuntimeUI,
                            BlockRuntimeInput = ts.BlockRuntimeInput
                        });
                        break;

                    case CarControllerComponentData cc:
                        world.AddComponent(entity, new CarControllerComponent
                        {
                            MotorForce = SafeFloat(cc.MotorForce, 12000f),
                            BrakeForce = SafeFloat(cc.BrakeForce, 22000f),
                            MaxSteerAngle = SafeFloat(cc.MaxSteerAngle, 30f),
                            DownForce = SafeFloat(cc.DownForce, 100f),
                            CenterOfMassOffset = SafeVector3FromArray(cc.CenterOfMassOffset),
                            CameraOffset = SafeVector3FromArray(cc.CameraOffset),
                            CameraTargetOffset = SafeVector3FromArray(cc.CameraTargetOffset),
                            WheelPositionFL = SafeVector3FromArray(cc.WheelPositionFL),
                            WheelPositionFR = SafeVector3FromArray(cc.WheelPositionFR),
                            WheelPositionRL = SafeVector3FromArray(cc.WheelPositionRL),
                            WheelPositionRR = SafeVector3FromArray(cc.WheelPositionRR),
                            SuspensionRestLength = SafeFloat(cc.SuspensionRestLength, 0.24f),
                            SuspensionStiffness = SafeFloat(cc.SuspensionStiffness, 22000f),
                            SuspensionDamping = SafeFloat(cc.SuspensionDamping, 2800f),
                            WheelRadius = SafeFloat(cc.WheelRadius, 0.30f),
                            IsInitialized = false,
                            IsPossessed = false,
                            EntityId = 0
                        });
                        break;

                    case CameraComponentData cam:
                        world.AddComponent(entity, new CameraComponent(cam.Fov, cam.Near, cam.Far));
                        break;

                    case LightComponentData l:
                        Enum.TryParse<LightComponent.LightType>(l.LightType, true, out var lType);
                        var lightColor = SafeVector3FromArray(l.Color);
                        world.AddComponent(entity, new LightComponent(lType, ToEngVec3FromArray(l.Color), SafeFloat(l.Intensity, 1.0f)));
                        break;

                    case NetworkManagerComponentData nm:
                        int storIdx = NetworkManagerStorage.Allocate(nm.PrefabPath);
                        world.AddComponent(entity, new NetworkManagerComponent
                        {
                            StorageIndex = storIdx,
                            MaxPlayers = nm.MaxPlayers,
                            ReplicationInterval = SafeFloat(nm.ReplicationInterval, 0.1f),
                            ReplicationTimer = 0f,
                            PlayerCount = 0,
                            IsHost = false,
                            HasStartedSpawning = false
                        });
                        break;

                    case SpawnPointComponentData sp:
                        world.AddComponent(entity, new SpawnPointComponent
                        {
                            Index = sp.Index,
                            IsOccupied = false,
                            OccupantEntityId = -1
                        });
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
    
    // Convert Vector3 to float array with NaN protection
    private static float[] ToFloatArray(SysVec3 v)
    {
        return new float[] 
        { 
            SafeFloat(v.X, 0f),
            SafeFloat(v.Y, 0f),
            SafeFloat(v.Z, 0f)
        };
    }
    
    // Convert float array to System.Numerics.Vector3 with validation
    private static SysVec3 SafeVector3FromArray(float[] arr)
    {
        if (arr == null || arr.Length < 3)
            return SysVec3.Zero;
        
        return new SysVec3(
            SafeFloat(arr[0], 0f),
            SafeFloat(arr[1], 0f),
            SafeFloat(arr[2], 0f)
        );
    }
    
    // Convert float array to Engine Vector3 with validation
    private static EngVec3 ToEngVec3FromArray(float[] arr)
    {
        if (arr == null || arr.Length < 3)
            return EngVec3.Zero;
        
        return new EngVec3(
            SafeFloat(arr[0], 0f),
            SafeFloat(arr[1], 0f),
            SafeFloat(arr[2], 0f)
        );
    }
    
    // Validate float value - replace NaN/Infinity with default
    private static float SafeFloat(float value, float defaultValue)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            Console.WriteLine($"[SceneConverter] WARNING: Invalid float value {value} replaced with {defaultValue}");
            return defaultValue;
        }
        return value;
    }
}
