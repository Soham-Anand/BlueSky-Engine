using System;
using System.Collections.Generic;
using BlueSky.Core.ECS;
using BlueSky.Core.ECS.Builtin;

namespace BlueSky.AI.Overthinking;

/// <summary>
/// Overthinking AI System - Updates all AI brains in the world
/// </summary>
public class OverthinkingSystem : SystemBase
{
    private readonly Dictionary<Entity, AIBrain> _brains = new();
    
    public AIBrain CreateBrain(Entity entity)
    {
        if (_brains.ContainsKey(entity))
            return _brains[entity];
        
        var brain = new AIBrain { Owner = entity };
        _brains[entity] = brain;
        return brain;
    }
    
    public AIBrain? GetBrain(Entity entity)
    {
        return _brains.TryGetValue(entity, out var brain) ? brain : null;
    }
    
    public void RemoveBrain(Entity entity)
    {
        _brains.Remove(entity);
    }
    
    public override void Update(float deltaTime)
    {
        // Update all AI brains
        foreach (var brain in _brains.Values)
        {
            brain.Update(deltaTime);
        }
    }
    
    public IEnumerable<AIBrain> GetAllBrains() => _brains.Values;
}

/// <summary>
/// Component to mark entities as AI-controlled
/// </summary>
public struct AIComponent
{
    public bool IsEnabled;
    public float ThinkInterval; // How often to update (0 = every frame)
    public float TimeSinceLastThink;
    
    public AIComponent()
    {
        IsEnabled = true;
        ThinkInterval = 0f;
        TimeSinceLastThink = 0f;
    }
}
