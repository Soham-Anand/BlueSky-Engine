using System;
using BlueSky.Airborne;
using System.Collections.Generic;
using BlueSky.SkyCore;
using BlueSky.SkyCore.Builtin;

namespace BlueSky.Meridian;

/// <summary>
/// Captures and restores the complete state of all entities in a scene across Play/Stop cycles.
/// Preserves .bsphy physics asset paths, restores mutated components in-place, and purges any
/// transient entities spawned dynamically during Play Mode.
/// </summary>
public class SceneSnapshot
{
    private readonly Dictionary<Entity, EntitySnapshot> _entityStates = new();
    private SceneData? _fullSceneBackup;
    
    private class EntitySnapshot
    {
        public TransformComponent Transform;
        public bool HasParent;
        public ParentComponent Parent;
        public bool HasLocalTransform;
        public LocalTransformComponent LocalTransform;
        public bool HasTeaScript;
        public TeaScriptComponent? TeaScript;
        public bool HasPhysics;
        public PhysicsComponent? Physics;
        public string PhysicsAssetPath = "";
        public bool HasCarController;
        public CarControllerComponent? CarController;
        public bool HasAudioSource;
        public AudioSourceComponent? AudioSource;
        public bool HasAIAgent;
        public AIAgentComponent? AIAgent;
    }
    
    /// <summary>
    /// Capture the current state of all entities in the world before Play Mode begins.
    /// </summary>
    public void Capture(World world)
    {
        _entityStates.Clear();
        _fullSceneBackup = SceneConverter.WorldToSceneData(world, "PlayModeSnapshot");
        
        foreach (var entity in world.GetAllEntities())
        {
            var snapshot = new EntitySnapshot();
            
            if (world.TryGetComponent<TransformComponent>(entity, out var transform))
                snapshot.Transform = transform;

            if (world.TryGetComponent<ParentComponent>(entity, out var parent))
            {
                snapshot.HasParent = true;
                snapshot.Parent = parent;
            }

            if (world.TryGetComponent<LocalTransformComponent>(entity, out var localTransform))
            {
                snapshot.HasLocalTransform = true;
                snapshot.LocalTransform = localTransform;
            }
            
            if (world.TryGetComponent<TeaScriptComponent>(entity, out var teaScript))
            {
                snapshot.HasTeaScript = true;
                snapshot.TeaScript = teaScript;
            }
            
            if (world.TryGetComponent<PhysicsComponent>(entity, out var physics))
            {
                snapshot.HasPhysics = true;
                snapshot.Physics = physics;
                snapshot.PhysicsAssetPath = PhysicsAssetCache.GetPath((uint)entity.Id);
            }
            
            if (world.TryGetComponent<CarControllerComponent>(entity, out var carController))
            {
                snapshot.HasCarController = true;
                snapshot.CarController = carController;
            }

            if (world.TryGetComponent<AudioSourceComponent>(entity, out var audio))
            {
                snapshot.HasAudioSource = true;
                snapshot.AudioSource = audio;
            }

            if (world.TryGetComponent<AIAgentComponent>(entity, out var ai))
            {
                snapshot.HasAIAgent = true;
                snapshot.AIAgent = ai;
            }
            
            _entityStates[entity] = snapshot;
        }
        
        Console.WriteLine($"[SceneSnapshot] Captured state of {_entityStates.Count} entities");
    }
    
    /// <summary>
    /// Restore all entities to their captured state and destroy any entities spawned during Play Mode.
    /// </summary>
    public void Restore(World world)
    {
        // 1. Destroy any entities that were spawned dynamically during Play Mode
        var playSpawnedEntities = new List<Entity>();
        foreach (var liveEntity in world.GetAllEntities())
        {
            if (!_entityStates.ContainsKey(liveEntity) &&
                !world.HasComponent<CameraComponent>(liveEntity) &&
                !world.HasComponent<EditorOnlyTagComponent>(liveEntity))
            {
                playSpawnedEntities.Add(liveEntity);
            }
        }

        foreach (var extra in playSpawnedEntities)
        {
            PhysicsAssetCache.Clear((uint)extra.Id);
            world.DestroyEntity(extra);
        }

        // 2. Check if any original entity was destroyed during Play Mode; if so, rebuild from full scene backup
        bool anyOriginalDestroyed = false;
        foreach (var kvp in _entityStates)
        {
            if (!world.IsEntityValid(kvp.Key))
            {
                anyOriginalDestroyed = true;
                break;
            }
        }

        if (anyOriginalDestroyed && _fullSceneBackup != null)
        {
            SceneConverter.SceneDataToWorld(_fullSceneBackup, world, clearWorld: true);
            Console.WriteLine($"[SceneSnapshot] Reconstructed scene after Play Mode entity destruction ({_fullSceneBackup.Entities.Count} entities)");
            return;
        }

        int restoredCount = 0;
        
        foreach (var kvp in _entityStates)
        {
            var entity = kvp.Key;
            var snapshot = kvp.Value;
            
            if (!world.IsEntityValid(entity))
                continue;
            
            if (world.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref world.GetComponent<TransformComponent>(entity);
                transform = snapshot.Transform;
            }

            if (snapshot.HasLocalTransform && world.HasComponent<LocalTransformComponent>(entity))
            {
                ref var local = ref world.GetComponent<LocalTransformComponent>(entity);
                local = snapshot.LocalTransform;
            }
            
            if (snapshot.HasTeaScript && world.HasComponent<TeaScriptComponent>(entity))
            {
                ref var teaScript = ref world.GetComponent<TeaScriptComponent>(entity);
                teaScript.IsInitialized = false;
                teaScript.RuntimeInstance = 0;
                if (snapshot.TeaScript.HasValue)
                {
                    teaScript.ScriptAssetId = snapshot.TeaScript.Value.ScriptAssetId;
                    teaScript.IsEnabled = snapshot.TeaScript.Value.IsEnabled;
                    teaScript.AllowRuntimeUI = snapshot.TeaScript.Value.AllowRuntimeUI;
                    teaScript.BlockRuntimeInput = snapshot.TeaScript.Value.BlockRuntimeInput;
                }
            }
            
            // Restore Physics AND preserve .bsphy asset path!
            if (snapshot.HasPhysics && world.HasComponent<PhysicsComponent>(entity))
            {
                ref var phys = ref world.GetComponent<PhysicsComponent>(entity);
                if (snapshot.Physics.HasValue)
                {
                    phys = snapshot.Physics.Value;
                    if (!string.IsNullOrEmpty(snapshot.PhysicsAssetPath))
                        PhysicsAssetCache.SetPath((uint)entity.Id, snapshot.PhysicsAssetPath);
                    else
                        PhysicsAssetCache.Clear((uint)entity.Id);
                }
            }
            
            if (snapshot.HasCarController && world.HasComponent<CarControllerComponent>(entity))
            {
                ref var cc = ref world.GetComponent<CarControllerComponent>(entity);
                if (snapshot.CarController.HasValue)
                {
                    cc = snapshot.CarController.Value;
                    cc.IsInitialized = false;
                    cc.IsPossessed = false;
                    cc.EntityId = 0;
                }
            }

            if (snapshot.HasAudioSource && snapshot.AudioSource.HasValue && world.HasComponent<AudioSourceComponent>(entity))
            {
                ref var audio = ref world.GetComponent<AudioSourceComponent>(entity);
                audio = snapshot.AudioSource.Value;
                audio.IsPlaying = false;
            }

            if (snapshot.HasAIAgent && snapshot.AIAgent.HasValue && world.HasComponent<AIAgentComponent>(entity))
            {
                ref var ai = ref world.GetComponent<AIAgentComponent>(entity);
                ai = snapshot.AIAgent.Value;
                ai.DecisionTimer = 0f;
            }
            
            restoredCount++;
        }
        
        Console.WriteLine($"[SceneSnapshot] Restored state of {restoredCount} entities (purged {playSpawnedEntities.Count} Play-Mode spawned entities)");
    }
    
    public void Clear()
    {
        _entityStates.Clear();
        _fullSceneBackup = null;
    }
}
