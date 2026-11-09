using System;
using System.Collections.Generic;
using BlueSky.SkyCore;
using BlueSky.SkyCore.Builtin;

namespace BlueSky.Overthinking;

/// <summary>
/// Overthinking AI System - Synchronizes with SkyCore <see cref="AIAgentComponent"/> entities
/// and updates all active AI brains in the world.
/// </summary>
public class OverthinkingSystem : SystemBase
{
    private readonly Dictionary<Entity, AIBrain> _brains = new();
    private QueryDescription _agentQuery;

    protected override void OnInitialize()
    {
        if (World != null)
        {
            _agentQuery = World.CreateQuery()
                .All<AIAgentComponent>()
                .Build();
        }
    }
    
    public AIBrain CreateBrain(Entity entity)
    {
        if (_brains.TryGetValue(entity, out var existing))
            return existing;
        
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
        if (World != null)
        {
            foreach (var chunk in World.EnumerateQueryChunks(_agentQuery))
            {
                int agentIdx = chunk.GetComponentIndex(typeof(AIAgentComponent));
                var agents = chunk.GetComponentSpan<AIAgentComponent>(agentIdx);
                var entities = chunk.GetEntities();

                for (int i = 0; i < entities.Length; i++)
                {
                    ref var agent = ref agents[i];
                    if (!agent.IsEnabled)
                        continue;

                    var brain = CreateBrain(entities[i]);
                    agent.DecisionTimer += deltaTime;
                    if (agent.DecisionTimer >= MathF.Max(0.01f, agent.DecisionInterval))
                    {
                        agent.DecisionTimer = 0f;
                        brain.Blackboard["PerceptionRadius"] = agent.PerceptionRadius;
                        brain.Blackboard["BehaviorProfile"] = agent.BehaviorProfile;
                    }
                }
            }
        }

        foreach (var brain in _brains.Values)
        {
            brain.Update(deltaTime);
        }
    }
    
    public IEnumerable<AIBrain> GetAllBrains() => _brains.Values;
}
