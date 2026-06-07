using System;
using System.Collections.Generic;

namespace BlueSky.Animation.AnimScript;

/// <summary>
/// Runtime executor for AnimScript state machines.
/// Manages state transitions, animation playback, and variable updates.
/// </summary>
public class AnimScriptRuntime
{
    private readonly AnimStateMachine _stateMachine;
    private readonly AnimationController _controller;
    private readonly Dictionary<string, AnimationClip> _animations = new();
    
    public string CurrentState { get; private set; } = "";
    public float StateTime { get; private set; } = 0;
    
    public AnimScriptRuntime(AnimStateMachine stateMachine, AnimationController controller)
    {
        _stateMachine = stateMachine;
        _controller = controller;
        
        // Start with initial state
        if (!string.IsNullOrEmpty(_stateMachine.InitialState))
        {
            TransitionTo(_stateMachine.InitialState);
        }
    }
    
    /// <summary>
    /// Register an animation clip
    /// </summary>
    public void RegisterAnimation(string name, AnimationClip clip)
    {
        _animations[name] = clip;
        _controller.AddClip(name, clip);
    }
    
    /// <summary>
    /// Set a variable value (for conditions)
    /// </summary>
    public void SetVariable(string name, float value)
    {
        _stateMachine.Variables[name] = value;
    }
    
    /// <summary>
    /// Get a variable value
    /// </summary>
    public float GetVariable(string name)
    {
        return _stateMachine.Variables.TryGetValue(name, out float value) ? value : 0;
    }
    
    /// <summary>
    /// Update the state machine (call every frame)
    /// </summary>
    public void Update(float deltaTime)
    {
        StateTime += deltaTime;
        
        // Update animation controller
        _controller.Update(deltaTime);
        
        // Check transitions
        var currentState = _stateMachine.GetState(CurrentState);
        if (currentState == null) return;
        
        // Sort transitions by priority (higher first)
        var sortedTransitions = new List<AnimTransition>(currentState.Transitions);
        sortedTransitions.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        
        foreach (var transition in sortedTransitions)
        {
            if (transition.Condition.Evaluate(_stateMachine))
            {
                TransitionTo(transition.TargetState, transition.BlendTime);
                break;
            }
        }
    }
    
    /// <summary>
    /// Force transition to a specific state
    /// </summary>
    public void TransitionTo(string stateName, float blendTime = 0.2f)
    {
        var state = _stateMachine.GetState(stateName);
        if (state == null)
        {
            Console.WriteLine($"[AnimScript] State not found: {stateName}");
            return;
        }
        
        // Exit current state
        if (!string.IsNullOrEmpty(CurrentState))
        {
            var oldState = _stateMachine.GetState(CurrentState);
            if (oldState != null)
            {
                foreach (var evt in oldState.OnExit)
                {
                    TriggerEvent(evt);
                }
            }
        }
        
        // Enter new state
        CurrentState = stateName;
        StateTime = 0;
        
        Console.WriteLine($"[AnimScript] Transition to: {stateName}");
        
        // Play animation
        if (!string.IsNullOrEmpty(state.AnimationName))
        {
            _controller.Play(state.AnimationName, blendTime);
            
            // Set animation properties
            if (_controller.CurrentState != null)
            {
                _controller.CurrentState.Speed = state.Speed;
            }
        }
        
        // Trigger enter events
        foreach (var evt in state.OnEnter)
        {
            TriggerEvent(evt);
        }
    }
    
    private void TriggerEvent(AnimEvent evt)
    {
        Console.WriteLine($"[AnimScript] Event: {evt.EventName}");
        // TODO: Hook up to game event system
    }
}

/// <summary>
/// Bridge between TeaScript and AnimScript.
/// Allows TeaScript to control AnimScript state machines.
/// </summary>
public static class AnimScriptTeaScriptBridge
{
    private static readonly Dictionary<uint, AnimScriptRuntime> _runtimes = new();
    
    /// <summary>
    /// Create an AnimScript runtime for an entity
    /// </summary>
    public static void CreateRuntime(uint entityId, string scriptPath, AnimationController controller)
    {
        var language = new AnimScriptLanguage();
        if (!language.ParseFile(scriptPath))
        {
            Console.WriteLine($"[AnimScript] Failed to load: {scriptPath}");
            return;
        }
        
        var runtime = new AnimScriptRuntime(language.StateMachine, controller);
        _runtimes[entityId] = runtime;
        
        Console.WriteLine($"[AnimScript] Runtime created for entity {entityId}");
    }
    
    /// <summary>
    /// Update all runtimes (call from AnimationSystem)
    /// </summary>
    public static void UpdateAll(float deltaTime)
    {
        foreach (var runtime in _runtimes.Values)
        {
            runtime.Update(deltaTime);
        }
    }
    
    /// <summary>
    /// Set a variable in an entity's AnimScript
    /// Usage in TeaScript: SetAnimVariable(entityId, "speed", 5.0)
    /// </summary>
    public static void SetAnimVariable(uint entityId, string name, float value)
    {
        if (_runtimes.TryGetValue(entityId, out var runtime))
        {
            runtime.SetVariable(name, value);
        }
    }
    
    /// <summary>
    /// Get a variable from an entity's AnimScript
    /// </summary>
    public static float GetAnimVariable(uint entityId, string name)
    {
        if (_runtimes.TryGetValue(entityId, out var runtime))
        {
            return runtime.GetVariable(name);
        }
        return 0;
    }
    
    /// <summary>
    /// Force a state transition
    /// Usage in TeaScript: TransitionToState(entityId, "Jump")
    /// </summary>
    public static void TransitionToState(uint entityId, string stateName)
    {
        if (_runtimes.TryGetValue(entityId, out var runtime))
        {
            runtime.TransitionTo(stateName);
        }
    }
    
    /// <summary>
    /// Get current state name
    /// </summary>
    public static string GetCurrentState(uint entityId)
    {
        if (_runtimes.TryGetValue(entityId, out var runtime))
        {
            return runtime.CurrentState;
        }
        return "";
    }
    
    /// <summary>
    /// Register an animation clip for an entity
    /// </summary>
    public static void RegisterAnimation(uint entityId, string name, AnimationClip clip)
    {
        if (_runtimes.TryGetValue(entityId, out var runtime))
        {
            runtime.RegisterAnimation(name, clip);
        }
    }
    
    /// <summary>
    /// Remove runtime for an entity
    /// </summary>
    public static void RemoveRuntime(uint entityId)
    {
        _runtimes.Remove(entityId);
    }
}
