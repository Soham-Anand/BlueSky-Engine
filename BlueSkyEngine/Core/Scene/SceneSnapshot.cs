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
        // Add more component types here as needed
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
