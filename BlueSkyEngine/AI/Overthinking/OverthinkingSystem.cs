using System;
using System.Collections.Generic;
using BlueSky.Core.ECS;

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
