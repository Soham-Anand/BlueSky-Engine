using System;
using System.Collections.Generic;

namespace BlueSky.Motif.AnimScript;

/// <summary>
/// Comparison operator for declarative Motif animation state transitions.
/// </summary>
public enum AnimConditionOp
{
    GreaterThan,
    LessThan,
    Equals,
    NotEquals,
    IsTrue,
    IsFalse,
    Triggered
}

/// <summary>
/// Single parameter condition on an <see cref="AnimTransition"/>.
/// </summary>
public readonly struct AnimCondition
{
    public readonly string Parameter;
    public readonly AnimConditionOp Operator;
    public readonly float Threshold;

    public AnimCondition(string parameter, AnimConditionOp op, float threshold = 0f)
    {
        Parameter = parameter;
        Operator = op;
        Threshold = threshold;
    }
}

/// <summary>
/// Directed transition between two states in an <see cref="AnimStateGraph"/>.
/// </summary>
public sealed class AnimTransition
{
    public string FromState { get; }
    public string ToState { get; }
    public float BlendDuration { get; }
    public List<AnimCondition> Conditions { get; } = new();

    public AnimTransition(string fromState, string toState, float blendDuration = 0.2f)
    {
        FromState = fromState;
        ToState = toState;
        BlendDuration = MathF.Max(0f, blendDuration);
    }

    public AnimTransition When(string parameter, AnimConditionOp op, float threshold = 0f)
    {
        Conditions.Add(new AnimCondition(parameter, op, threshold));
        return this;
    }
}

/// <summary>
/// Motif declarative animation state machine for skeletal meshes.
/// Evaluates float, bool, and trigger parameters to blend smoothly between animation states.
/// </summary>
public sealed class AnimStateGraph
{
    private readonly HashSet<string> _states = new(StringComparer.Ordinal);
    private readonly List<AnimTransition> _transitions = new();
    private readonly Dictionary<string, float> _floats = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _bools = new(StringComparer.Ordinal);
    private readonly HashSet<string> _triggers = new(StringComparer.Ordinal);

    public string CurrentState { get; private set; } = "Idle";
    public string? PreviousState { get; private set; }
    public float BlendProgress { get; private set; } = 1.0f;
    public float ActiveBlendDuration { get; private set; }
    public float StateTime { get; private set; }

    public AnimStateGraph(string initialState = "Idle")
    {
        CurrentState = initialState;
        _states.Add(initialState);
    }

    public AnimStateGraph AddState(string stateName)
    {
        if (!string.IsNullOrWhiteSpace(stateName))
            _states.Add(stateName);
        return this;
    }

    public AnimTransition AddTransition(string fromState, string toState, float blendDuration = 0.2f)
    {
        _states.Add(fromState);
        _states.Add(toState);
        var transition = new AnimTransition(fromState, toState, blendDuration);
        _transitions.Add(transition);
        return transition;
    }

    public void SetFloat(string name, float value) => _floats[name] = value;
    public float GetFloat(string name) => _floats.TryGetValue(name, out float v) ? v : 0f;

    public void SetBool(string name, bool value) => _bools[name] = value;
    public bool GetBool(string name) => _bools.TryGetValue(name, out bool v) && v;

    public void SetTrigger(string name) => _triggers.Add(name);

    /// <summary>
    /// Advances state timer, evaluates transitions out of <see cref="CurrentState"/>,
    /// and updates blend weights.
    /// </summary>
    public void Evaluate(float deltaTime)
    {
        float dt = MathF.Max(0f, deltaTime);
        StateTime += dt;

        if (BlendProgress < 1.0f)
        {
            BlendProgress = ActiveBlendDuration > 1e-4f
                ? MathF.Min(1.0f, BlendProgress + dt / ActiveBlendDuration)
                : 1.0f;
        }

        for (int i = 0; i < _transitions.Count; i++)
        {
            var tr = _transitions[i];
            if (!string.Equals(tr.FromState, CurrentState, StringComparison.Ordinal) &&
                !string.Equals(tr.FromState, "*", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(tr.ToState, CurrentState, StringComparison.Ordinal))
                continue;

            if (AreConditionsMet(tr.Conditions))
            {
                ConsumeTriggers(tr.Conditions);
                PreviousState = CurrentState;
                CurrentState = tr.ToState;
                StateTime = 0f;
                ActiveBlendDuration = tr.BlendDuration;
                BlendProgress = tr.BlendDuration > 1e-4f ? 0f : 1.0f;
                break;
            }
        }
    }

    private bool AreConditionsMet(List<AnimCondition> conditions)
    {
        if (conditions.Count == 0)
            return false;

        for (int i = 0; i < conditions.Count; i++)
        {
            var c = conditions[i];
            bool ok = c.Operator switch
            {
                AnimConditionOp.GreaterThan => GetFloat(c.Parameter) > c.Threshold,
                AnimConditionOp.LessThan    => GetFloat(c.Parameter) < c.Threshold,
                AnimConditionOp.Equals      => MathF.Abs(GetFloat(c.Parameter) - c.Threshold) < 0.001f,
                AnimConditionOp.NotEquals   => MathF.Abs(GetFloat(c.Parameter) - c.Threshold) >= 0.001f,
                AnimConditionOp.IsTrue      => GetBool(c.Parameter),
                AnimConditionOp.IsFalse     => !GetBool(c.Parameter),
                AnimConditionOp.Triggered   => _triggers.Contains(c.Parameter),
                _ => false
            };

            if (!ok)
                return false;
        }
        return true;
    }

    private void ConsumeTriggers(List<AnimCondition> conditions)
    {
        for (int i = 0; i < conditions.Count; i++)
        {
            if (conditions[i].Operator == AnimConditionOp.Triggered)
                _triggers.Remove(conditions[i].Parameter);
        }
    }
}
