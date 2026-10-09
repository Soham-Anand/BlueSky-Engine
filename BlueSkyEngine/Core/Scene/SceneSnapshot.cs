using System;
using System.Collections.Generic;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;
using BlueSky.Core.Math;

namespace BlueSky.Core.Scene;

/// <summary>
/// Captures and restores the state of all entities in a scene.
/// Used for Play mode isolation - changes during Play don't affect the editor state.
/// </summary>
public class SceneSnapshot
{
    private readonly Dictionary<Entity, EntitySnapshot> _entityStates = new();
    
    /// <summary>
    /// Snapshot of a single entity's state.
    /// </summary>
    private class EntitySnapshot
    {
        public TransformComponent Transform;
        public bool HasTeaScript;
        public TeaScriptComponent? TeaScript;
        public bool HasPhysics;
        public PhysicsComponent? Physics;
        public bool HasCarController;
        public CarControllerComponent? CarController;
    }
    
    /// <summary>
    /// Capture the current state of all entities in the world.
    /// </summary>
    public void Capture(World world)
    {
        _entityStates.Clear();
        
        foreach (var entity in world.GetAllEntities())
        {
            var snapshot = new EntitySnapshot();
            
            // Capture Transform
            if (world.TryGetComponent<TransformComponent>(entity, out var transform))
            {
                snapshot.Transform = transform;
            }
            
            // Capture TeaScript
            if (world.TryGetComponent<TeaScriptComponent>(entity, out var teaScript))
            {
                snapshot.HasTeaScript = true;
                snapshot.TeaScript = teaScript;
            }
            
            // Capture Physics
            if (world.TryGetComponent<PhysicsComponent>(entity, out var physics))
            {
                snapshot.HasPhysics = true;
                snapshot.Physics = physics;
            }
            
            // Capture CarController
            if (world.TryGetComponent<CarControllerComponent>(entity, out var carController))
            {
                snapshot.HasCarController = true;
                snapshot.CarController = carController;
            }
            
            _entityStates[entity] = snapshot;
        }
        
        Console.WriteLine($"[SceneSnapshot] Captured state of {_entityStates.Count} entities");
    }
    
    /// <summary>
    /// Restore all entities to their captured state.
    /// </summary>
    public void Restore(World world)
    {
        int restoredCount = 0;
        
        foreach (var kvp in _entityStates)
        {
            var entity = kvp.Key;
            var snapshot = kvp.Value;
            
            // Skip if entity no longer exists
            if (!world.IsEntityValid(entity))
                continue;
            
            // Restore Transform
            if (world.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref world.GetComponent<TransformComponent>(entity);
                transform = snapshot.Transform;
            }
            
            // Restore TeaScript
            if (snapshot.HasTeaScript && world.HasComponent<TeaScriptComponent>(entity))
            {
                ref var teaScript = ref world.GetComponent<TeaScriptComponent>(entity);
                // Reset the script state
                teaScript.IsInitialized = false;
                teaScript.RuntimeInstance = 0;
                // Keep the ScriptAssetId and IsEnabled from snapshot
                if (snapshot.TeaScript.HasValue)
                {
                    teaScript.ScriptAssetId = snapshot.TeaScript.Value.ScriptAssetId;
                    teaScript.IsEnabled = snapshot.TeaScript.Value.IsEnabled;
                    teaScript.AllowRuntimeUI = snapshot.TeaScript.Value.AllowRuntimeUI;
                    teaScript.BlockRuntimeInput = snapshot.TeaScript.Value.BlockRuntimeInput;
                }
            }
            
            // Restore Physics — reset velocity/angular state that accumulated during play
            if (snapshot.HasPhysics && world.HasComponent<PhysicsComponent>(entity))
            {
                ref var phys = ref world.GetComponent<PhysicsComponent>(entity);
                if (snapshot.Physics.HasValue)
                {
                    phys = snapshot.Physics.Value;
                    PhysicsAssetCache.Clear((uint)entity.Id);
                }
            }
            
            // Restore CarController — reset IsInitialized so it re-initializes on next play
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
            
            restoredCount++;
        }
        
        Console.WriteLine($"[SceneSnapshot] Restored state of {restoredCount} entities");
    }
    
    /// <summary>
    /// Clear the snapshot data.
    /// </summary>
    public void Clear()
    {
        _entityStates.Clear();
    }
}
