using System;
using System.Collections.Generic;
using BlueSky.Core.ECS;

namespace BlueSky.AI.Overthinking;

/// <summary>
/// Overthinking AI System - TeaScript-driven AI behaviors
/// Each entity can have multiple AI behaviors that run in priority order
/// </summary>
public class AIBrain
{
    public Entity Owner { get; set; }
    public List<AIBehavior> Behaviors { get; } = new();
    public Dictionary<string, object> Blackboard { get; } = new();
    
    private AIBehavior? _currentBehavior;
    
    public void AddBehavior(AIBehavior behavior)
    {
        behavior.Brain = this;
        Behaviors.Add(behavior);
        Behaviors.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }
    
    public void RemoveBehavior(AIBehavior behavior)
    {
        Behaviors.Remove(behavior);
    }
    
    public void Update(float deltaTime)
    {
        // Find highest priority behavior that can run
        AIBehavior? nextBehavior = null;
        foreach (var behavior in Behaviors)
        {
            if (behavior.CanExecute())
            {
                nextBehavior = behavior;
                break;
            }
        }
        
        // Handle behavior transitions
        if (nextBehavior != _currentBehavior)
        {
            _currentBehavior?.OnExit();
            _currentBehavior = nextBehavior;
            _currentBehavior?.OnEnter();
        }
        
        // Execute current behavior
        _currentBehavior?.Execute(deltaTime);
    }
    
    public void SetBlackboardValue(string key, object value)
    {
        Blackboard[key] = value;
    }
    
    public T? GetBlackboardValue<T>(string key)
    {
        if (Blackboard.TryGetValue(key, out var value) && value is T typedValue)
            return typedValue;
        return default;
    }
    
    public bool HasBlackboardValue(string key)
    {
        return Blackboard.ContainsKey(key);
    }
}

/// <summary>
/// Base class for AI behaviors - can be implemented in C# or driven by TeaScript
/// </summary>
public abstract class AIBehavior
{
    public string Name { get; set; } = "Unnamed";
    public int Priority { get; set; } = 0;
    public AIBrain? Brain { get; set; }
    
    public abstract bool CanExecute();
    public abstract void OnEnter();
    public abstract void Execute(float deltaTime);
    public abstract void OnExit();
}
