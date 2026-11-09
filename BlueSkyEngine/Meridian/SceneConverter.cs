using System;
using System.Collections.Generic;
using BlueSky.SkyCore;
using BlueSky.SkyCore.Builtin;
using SysVec3 = System.Numerics.Vector3;
using SysVec4 = System.Numerics.Vector4;
using SysQuat = System.Numerics.Quaternion;
using EngVec3 = BlueSky.Ramanujan.Vector3;
using EngQuat = BlueSky.Ramanujan.Quaternion;

namespace BlueSky.Meridian;

/// <summary>
/// Converts between SkyCore ECS World and serializable SceneData.
/// </summary>
public static class SceneConverter
{
    public static SceneData WorldToSceneData(World world, string sceneName = "Untitled Scene")
    {
        var sceneData = new SceneData
        {
            Name = sceneName,
            Version = "2.0"
        };

        var query = world.CreateQuery()
            .All<TransformComponent>()
            .None<EditorOnlyTagComponent>()
            .Build();

        foreach (var chunk in world.EnumerateQueryChunks(query))
        {
            var entities = chunk.GetEntities();
            int transformIndex = chunk.GetComponentIndex(typeof(TransformComponent));
            
            bool hasParent = chunk.Archetype.HasComponent(typeof(ParentComponent));
            bool hasLocalTransform = chunk.Archetype.HasComponent(typeof(LocalTransformComponent));
            bool hasWorldCell = chunk.Archetype.HasComponent(typeof(WorldCellComponent));
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
            bool hasAudioSource = chunk.Archetype.HasComponent(typeof(AudioSourceComponent));
            bool hasAIAgent = chunk.Archetype.HasComponent(typeof(AIAgentComponent));
            
            int parentIndex = hasParent ? chunk.GetComponentIndex(typeof(ParentComponent)) : -1;
            int localTransformIndex = hasLocalTransform ? chunk.GetComponentIndex(typeof(LocalTransformComponent)) : -1;
            int worldCellIndex = hasWorldCell ? chunk.GetComponentIndex(typeof(WorldCellComponent)) : -1;
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
            int audioSourceIndex = hasAudioSource ? chunk.GetComponentIndex(typeof(AudioSourceComponent)) : -1;
            int aiAgentIndex = hasAIAgent ? chunk.GetComponentIndex(typeof(AIAgentComponent)) : -1;

            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                
                // Skip internal camera entities (preview entities are filtered out by .None<EditorOnlyTagComponent>())
                if (hasCamera)
                {
                    continue;
                }
                
                var entityData = new EntityData { Id = entity.Id };

                if (nameIndex >= 0)
                {
                    var name = chunk.GetComponent<NameComponent>(i, nameIndex);
                    entityData.Name = name.Name;
                }
                else
                {
                    entityData.Name = $"Entity_{entity.Id}";
                }

                var transform = chunk.GetComponent<TransformComponent>(i, transformIndex);
                entityData.Components.Add(new TransformComponentData
                {
                    Position = new float[] { transform.Position.X, transform.Position.Y, transform.Position.Z },
                    Rotation = new float[] { transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W },
                    Scale = new float[] { transform.Scale.X, transform.Scale.Y, transform.Scale.Z }
                });

                if (parentIndex >= 0)
                {
                    var parentComp = chunk.GetComponent<ParentComponent>(i, parentIndex);
                    var local = localTransformIndex >= 0
                        ? chunk.GetComponent<LocalTransformComponent>(i, localTransformIndex)
                        : LocalTransformComponent.Identity;
                    entityData.Components.Add(new ParentComponentData
                    {
                        ParentEntityId = parentComp.Parent.Id,
                        LocalPosition = new float[] { local.Position.X, local.Position.Y, local.Position.Z },
                        LocalRotation = new float[] { local.Rotation.X, local.Rotation.Y, local.Rotation.Z, local.Rotation.W },
                        LocalScale = new float[] { local.Scale.X, local.Scale.Y, local.Scale.Z }
                    });
                }

                if (worldCellIndex >= 0)
                {
                    var cell = chunk.GetComponent<WorldCellComponent>(i, worldCellIndex);
                    entityData.Components.Add(new WorldCellComponentData
                    {
                        CellX = cell.Cell.X,
                        CellZ = cell.Cell.Z,
                        IsPersistent = cell.IsPersistent
                    });
                }

                if (meshIndex >= 0)
                {
                    var mesh = chunk.GetComponent<StaticMeshComponent>(i, meshIndex);
                    if (!string.IsNullOrEmpty(mesh.MeshAssetId))
                    {
                        entityData.Components.Add(new StaticMeshComponentData
                        {
                            MeshAssetId = mesh.MeshAssetId ?? ""
                        });
                    }
                }

                if (skeletalMeshIndex >= 0)
                {
                    var skeletalMesh = chunk.GetComponent<SkeletalMeshComponent>(i, skeletalMeshIndex);
                    if (!string.IsNullOrEmpty(skeletalMesh.MeshAssetPath))
                    {
                        entityData.Components.Add(new SkeletalMeshComponentData
                        {
                            MeshAssetPath = skeletalMesh.MeshAssetPath ?? "",
                            IsLoaded = skeletalMesh.IsLoaded
                        });
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

                if (spawnPointIndex >= 0)
                {
                    var sp = chunk.GetComponent<SpawnPointComponent>(i, spawnPointIndex);
                    entityData.Components.Add(new SpawnPointComponentData
                    {
                        Index = sp.Index
                    });
                }

                if (audioSourceIndex >= 0)
                {
                    var aud = chunk.GetComponent<AudioSourceComponent>(i, audioSourceIndex);
                    entityData.Components.Add(new AudioSourceComponentData
                    {
                        ClipPath = aud.ClipPath,
                        Volume = aud.Volume,
                        Pitch = aud.Pitch,
                        MinDistance = aud.MinDistance,
                        MaxDistance = aud.MaxDistance,
                        Loop = aud.Loop,
                        Is3D = aud.Is3D,
                        PlayOnStart = aud.PlayOnStart,
                        AttenuationCurve = aud.AttenuationCurve
                    });
                }

                if (aiAgentIndex >= 0)
                {
                    var ai = chunk.GetComponent<AIAgentComponent>(i, aiAgentIndex);
                    entityData.Components.Add(new AIAgentComponentData
                    {
                        BehaviorProfile = ai.BehaviorProfile,
                        PerceptionRadius = ai.PerceptionRadius,
                        DecisionInterval = ai.DecisionInterval,
                        IsEnabled = ai.IsEnabled
                    });
                }

                if (entityData.Components.Count >= 1)
                {
                    sceneData.Entities.Add(entityData);
                }
            }
        }

        Console.WriteLine($"[SceneConverter] Exported {sceneData.Entities.Count} entities");
        return sceneData;
    }

    public static SceneData WorldToSceneDataForEntity(World world, Entity entity, string prefabName = "Prefab")
    {
        var sceneData = new SceneData { Name = prefabName, Version = "2.0" };

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

        if (world.TryGetComponent<ParentComponent>(entity, out var parentComp))
        {
            world.TryGetComponent<LocalTransformComponent>(entity, out var local);
            entityData.Components.Add(new ParentComponentData
            {
                ParentEntityId = parentComp.Parent.Id,
                LocalPosition = new float[] { local.Position.X, local.Position.Y, local.Position.Z },
                LocalRotation = new float[] { local.Rotation.X, local.Rotation.Y, local.Rotation.Z, local.Rotation.W },
                LocalScale = new float[] { local.Scale.X, local.Scale.Y, local.Scale.Z }
            });
        }

        if (world.TryGetComponent<WorldCellComponent>(entity, out var cell))
        {
            entityData.Components.Add(new WorldCellComponentData
            {
                CellX = cell.Cell.X,
                CellZ = cell.Cell.Z,
                IsPersistent = cell.IsPersistent
            });
        }

        if (world.TryGetComponent<StaticMeshComponent>(entity, out var mesh))
        {
            if (!string.IsNullOrEmpty(mesh.MeshAssetId))
            {
                entityData.Components.Add(new StaticMeshComponentData { MeshAssetId = mesh.MeshAssetId });
            }
        }

        if (world.TryGetComponent<SkeletalMeshComponent>(entity, out var skeletalMesh))
        {
            if (!string.IsNullOrEmpty(skeletalMesh.MeshAssetPath))
            {
                entityData.Components.Add(new SkeletalMeshComponentData
                {
                    MeshAssetPath = skeletalMesh.MeshAssetPath,
                    IsLoaded = skeletalMesh.IsLoaded
                });
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

        if (world.TryGetComponent<AudioSourceComponent>(entity, out var aud))
        {
            entityData.Components.Add(new AudioSourceComponentData
            {
                ClipPath = aud.ClipPath,
                Volume = aud.Volume,
                Pitch = aud.Pitch,
                MinDistance = aud.MinDistance,
                MaxDistance = aud.MaxDistance,
                Loop = aud.Loop,
                Is3D = aud.Is3D,
                PlayOnStart = aud.PlayOnStart
            });
        }

        if (world.TryGetComponent<AIAgentComponent>(entity, out var ai))
        {
            entityData.Components.Add(new AIAgentComponentData
            {
                BehaviorProfile = ai.BehaviorProfile,
                PerceptionRadius = ai.PerceptionRadius,
                DecisionInterval = ai.DecisionInterval,
                IsEnabled = ai.IsEnabled
            });
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
            var allQuery = world.CreateQuery().Build();
            var entitiesToDestroy = new List<Entity>();

            foreach (var chunk in world.EnumerateQueryChunks(allQuery))
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

        var idRemap = new Dictionary<int, Entity>();
        var pendingParents = new List<(Entity child, ParentComponentData data)>();

        foreach (var entityData in sceneData.Entities)
        {
            var entity = world.CreateEntity();
            idRemap[entityData.Id] = entity;

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

                    case ParentComponentData p:
                        pendingParents.Add((entity, p));
                        break;

                    case WorldCellComponentData wc:
                        world.AddComponent(entity, new WorldCellComponent(new WorldCellCoord(wc.CellX, wc.CellZ), wc.IsPersistent));
                        break;

                    case StaticMeshComponentData m:
                        world.AddComponent(entity, new StaticMeshComponent
                        {
                            MeshAssetId = m.MeshAssetId
                        });
                        break;

                    case SkeletalMeshComponentData s:
                        world.AddComponent(entity, new SkeletalMeshComponent(s.MeshAssetPath)
                        {
                            IsLoaded = s.IsLoaded
                        });
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
                            NeedsRebuild = false
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

                    case AudioSourceComponentData aud:
                        world.AddComponent(entity, new AudioSourceComponent(aud.ClipPath, SafeFloat(aud.Volume, 1.0f), aud.Loop, aud.Is3D)
                        {
                            Pitch = SafeFloat(aud.Pitch, 1.0f),
                            MinDistance = SafeFloat(aud.MinDistance, 1.0f),
                            MaxDistance = SafeFloat(aud.MaxDistance, 100.0f),
                            PlayOnStart = aud.PlayOnStart,
                            AttenuationCurve = aud.AttenuationCurve
                        });
                        break;

                    case AIAgentComponentData ai:
                        world.AddComponent(entity, new AIAgentComponent(ai.BehaviorProfile, SafeFloat(ai.PerceptionRadius, 25.0f), SafeFloat(ai.DecisionInterval, 0.2f))
                        {
                            IsEnabled = ai.IsEnabled
                        });
                        break;
                }
            }

            world.AddComponent(entity, new NameComponent(entityData.Name));
        }

        // Resolve parent entity links after all entities exist
        foreach (var (child, pData) in pendingParents)
        {
            Entity parentEntity = default;
            if (!idRemap.TryGetValue(pData.ParentEntityId, out parentEntity))
            {
                world.TryResolveEntity((uint)System.Math.Max(0, pData.ParentEntityId), out parentEntity);
            }

            if (world.IsEntityValid(parentEntity))
            {
                world.AddComponent(child, new ParentComponent(parentEntity));
                var lp = pData.LocalPosition is { Length: >= 3 } ? new EngVec3(pData.LocalPosition[0], pData.LocalPosition[1], pData.LocalPosition[2]) : EngVec3.Zero;
                var lr = pData.LocalRotation is { Length: >= 4 } ? new EngQuat(pData.LocalRotation[0], pData.LocalRotation[1], pData.LocalRotation[2], pData.LocalRotation[3]) : EngQuat.Identity;
                var ls = pData.LocalScale is { Length: >= 3 } ? new EngVec3(pData.LocalScale[0], pData.LocalScale[1], pData.LocalScale[2]) : EngVec3.One;
                world.AddComponent(child, new LocalTransformComponent(lp, lr, ls));
            }
        }

        Console.WriteLine($"[SceneConverter] Imported {sceneData.Entities.Count} entities");
    }

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
