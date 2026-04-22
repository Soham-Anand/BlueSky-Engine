using System;
using BlueSky.Core.ECS;
using TeaScript.Runtime;

namespace BlueSky.AI.Overthinking;

/// <summary>
/// Exposes AI functions to TeaScript
/// </summary>
public static class AITeaScriptBridge
{
    private static OverthinkingSystem? _aiSystem;
    private static Entity _currentEntity;

    public static void Initialize(OverthinkingSystem aiSystem)
    {
        _aiSystem = aiSystem;
    }

    public static void SetCurrentEntity(Entity entity)
    {
        _currentEntity = entity;
    }

    public static void RegisterFunctions(Interpreter interpreter)
    {
        // Blackboard functions
        interpreter.RegisterNativeFunction("setBB", args =>
        {
            if (args.Count >= 2 && _aiSystem != null)
            {
                var brain = _aiSystem.GetBrain(_currentEntity);
                if (brain != null)
                {
                    var key = args[0]?.ToString() ?? "";
                    brain.SetBlackboardValue(key, args[1]);
                }
            }
            return null;
        });

        interpreter.RegisterNativeFunction("getBB", args =>
        {
            if (args.Count >= 1 && _aiSystem != null)
            {
                var brain = _aiSystem.GetBrain(_currentEntity);
                if (brain != null)
                {
                    var key = args[0]?.ToString() ?? "";
                    return brain.GetBlackboardValue<object>(key);
                }
            }
            return null;
        });

        interpreter.RegisterNativeFunction("hasBB", args =>
        {
            if (args.Count >= 1 && _aiSystem != null)
            {
                var brain = _aiSystem.GetBrain(_currentEntity);
                if (brain != null)
                {
                    var key = args[0]?.ToString() ?? "";
                    return brain.HasBlackboardValue(key);
                }
            }
            return false;
        });

        // Behavior control
        interpreter.RegisterNativeFunction("addBehavior", args =>
        {
            if (args.Count >= 2 && _aiSystem != null)
            {
                var brain = _aiSystem.GetBrain(_currentEntity);
                if (brain != null)
                {
                    var scriptPath = args[0]?.ToString() ?? "";
                    var priority = Convert.ToInt32(args[1]);

                    // This would load and add a TeaScript behavior
                    // Implementation depends on how you want to handle dynamic script loading
                }
            }
            return null;
        });
    }
}
